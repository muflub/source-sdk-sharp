using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Hot reload — plan_nettable_generic.md Step 10, plan.md Part 5 tier 1.
///
/// <para>
/// The claim the whole step rests on is that a managed assembly can be swapped
/// under a running game with clients connected, because nothing the two sides
/// negotiate is generated any more: the ServerClasses are the seven size tiers,
/// fixed at DLL init, and reshaping a C# type inside its tier moves no prop.
/// </para>
///
/// <para>
/// TWO FACTS HAVE TO HOLD FOR THAT TO BE TRUE, and they pull against each other.
/// A blob is native memory that outlives the assembly, so state survives a
/// reload for free — that is the win. And a state object bound to a blob is
/// managed memory that does NOT survive it, so every pointer the outgoing
/// assembly cached has to stop being writable, or a leaked object from the old
/// build writes through the new build's layout and nothing reports it.
/// </para>
///
/// <para>
/// THE SECOND IS NOT DONE BY UNBINDING THE STATES. It cannot be: unbinding them
/// needs a list of live states, and a list of live states in the ABI — which
/// lives in the default context and is shared by both sides — is precisely what
/// stops a collectible context ever unloading. It is done from native instead:
/// the reload gives every open control-block slot a fresh serial, and every
/// pointer from before the reload fails its next compare. These tests play
/// native's half by hand, the way <see cref="MappedMemoryTests"/> does, because
/// what has to be provable is what happens when the serial moves and no test can
/// arrange that by reloading an assembly.
/// </para>
/// </summary>
[Collection(ProcessWide.Name)]
public unsafe class ReloadTests
{
    // This class's own side state (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's.
    private readonly ReloadStore _reload = new(Side.Server);

    /// <summary>
    /// A reload retires a binding without closing the slot, and the difference
    /// between those two is visible from managed code.
    ///
    /// <para>
    /// Closing would have been the easy implementation and it is wrong: a closed
    /// slot reads serial 0, which is the answer "this entity is gone", and the
    /// entity is not gone — it is sitting in the world with its blob intact,
    /// waiting for the new assembly to bind to it. A message that said the
    /// entity had died would send whoever read it looking for a spawn bug.
    /// </para>
    /// </summary>
    [Fact]
    public void ARetiredBindingThrowsAndDoesNotClaimTheEntityDied()
    {
        var layout = StandaloneNetStates.Of(typeof(BarrelNetState));

        byte[] blob = GC.AllocateArray<byte>(layout.Size, pinned: true);
        EntitySlot[] slot = GC.AllocateArray<EntitySlot>(1, pinned: true);

        byte* pBlob = (byte*)Unsafe.AsPointer(ref blob[0]);
        EntitySlot* pSlot = (EntitySlot*)Unsafe.AsPointer(ref slot[0]);

        const uint before = 900u;
        slot[0].Serial = before;

        var old = new BarrelNetState();
        old.BindTo(new EntityHandle(0x00020009u), pBlob, blob.Length, pSlot, before);

        old.Thinks = 17;
        Assert.True(old.Thinks == 17, "a state bound before the reload works before the reload");

        // Native's Retire: a fresh serial, the slot left open. Never zero —
        // zero is the closed sentinel and means something else.
        slot[0].Serial = before + 1;

        Assert.True(!old.IsLive, "the state the outgoing assembly held is no longer live");

        string message = Threw<StaleEntityException>(() => old.Thinks = 18);

        Assert.True(message.Length > 0, "and writing through it throws rather than landing in the blob");
        Assert.True(message.Contains("expected serial 900") && message.Contains("says 901"),
                    "the message carries both serials, so a reload and a death can be told apart");
        Assert.True(message.Contains("managed reload"),
                    "and it names the reload as one of the two things that produce it");
    }

    /// <summary>
    /// THE HEADLINE, and the reason a reload is worth having at all: the state
    /// is still there afterwards.
    ///
    /// <para>
    /// plan.md Part 5 says entity state survives a reload with zero migration
    /// code because it lives in engine-owned native memory rather than in
    /// managed objects. That is a claim about the blob, and retiring the serial
    /// is a change to the thing that guards the blob — so the two are checked
    /// together here. A retire that zeroed the blob, or a rebind that allocated
    /// a fresh one, would break the claim while leaving every other test green.
    /// </para>
    /// </summary>
    [Fact]
    public void StateSurvivesTheReloadAndTheNewAssemblyReadsItBack()
    {
        var layout = StandaloneNetStates.Of(typeof(BarrelNetState));

        byte[] blob = GC.AllocateArray<byte>(layout.Size, pinned: true);
        EntitySlot[] slot = GC.AllocateArray<EntitySlot>(1, pinned: true);

        byte* pBlob = (byte*)Unsafe.AsPointer(ref blob[0]);
        EntitySlot* pSlot = (EntitySlot*)Unsafe.AsPointer(ref slot[0]);

        var handle = new EntityHandle(0x00020009u);

        slot[0].Serial = 1000u;

        var old = new BarrelNetState();
        old.BindTo(handle, pBlob, blob.Length, pSlot, 1000u);
        old.Thinks = 42;
        old.Broken = true;

        byte[] image = blob.AsSpan().ToArray();

        // The reload: serial moves, blob does not.
        slot[0].Serial = 1001u;

        Assert.True(blob.AsSpan().SequenceEqual(image), "retiring a binding does not touch the bytes");

        // What the new assembly does: a brand new state object, bound to the
        // same entity, which is handed the same pointer and the current serial.
        var fresh = new BarrelNetState();
        fresh.BindTo(handle, pBlob, blob.Length, pSlot, 1001u);

        Assert.True(fresh.Thinks == 42 && fresh.Broken, "the new assembly reads the state the old one wrote");
    }

    /// <summary>
    /// A retire does not clear the dirty word, and the reason is a change that
    /// would otherwise be lost.
    ///
    /// <para>
    /// The dirty word is C#'s, a one in it means a property really was written
    /// this frame, and the frame drain has not run at the moment a reload
    /// command executes. Clearing it would drop the last write the outgoing
    /// assembly made — correct in the blob, never announced, with nothing
    /// anywhere saying so. The write was real whoever made it.
    /// </para>
    /// </summary>
    [Fact]
    public void AWriteMadeBeforeTheReloadIsStillAnnounced()
    {
        var layout = StandaloneNetStates.Of(typeof(BarrelNetState));

        byte[] blob = GC.AllocateArray<byte>(layout.Size, pinned: true);
        EntitySlot[] slot = GC.AllocateArray<EntitySlot>(1, pinned: true);

        byte* pBlob = (byte*)Unsafe.AsPointer(ref blob[0]);
        EntitySlot* pSlot = (EntitySlot*)Unsafe.AsPointer(ref slot[0]);

        slot[0].Serial = 500u;
        slot[0].Dirty  = 0;

        var old = new BarrelNetState();
        old.BindTo(new EntityHandle(0x00020009u), pBlob, blob.Length, pSlot, 500u);
        old.Thinks = 7;

        Assert.True(slot[0].Dirty == 1, "the write before the reload marked the entity");

        slot[0].Serial = 501u;

        Assert.True(slot[0].Dirty == 1, "and the mark is still standing for the drain to find");
    }

    /// <summary>
    /// A BOUND STATE DOES NOT PIN ITS SIDE'S LOAD CONTEXT, which is the design
    /// decision behind everything above rather than an incidental property.
    ///
    /// <para>
    /// Step 10 of the plan asks <c>NetStateBinding.Forget</c> to unbind every
    /// state of the unloading side. Doing that needs the ABI to hold every live
    /// state — and the ABI is in the default context, so one strong reference to
    /// one object from a side keeps that whole side alive forever. The symptom
    /// is not an error: it is a reload that leaks the old code and keeps running
    /// it, which is the failure <see cref="UnloadTests"/> exists to catch.
    /// </para>
    ///
    /// <para>
    /// So this is the guard on the road not taken. Add a registry of live states
    /// to the ABI and this test goes red, whatever else stays green.
    /// </para>
    /// </summary>
    [Fact]
    public void BindingAStateDoesNotKeepItsSideAlive()
    {
        var weak = BindFromASideAndUnloadIt(_reload);

        if (weak is null)
            return;

        // Collected outside the frame that held the context and the state, for
        // the reason UnloadTests spells out: the locals would still be live in
        // there and the check would report a leak that is only a stack frame.
        for (int i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.True(!weak.IsAlive, "a side whose states were bound to blobs still unloads");

        Assert.True(ReloadCarrier.Count(_reload) == 1, "and its saved state is still there to be restored");

        _reload.Clear();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference? BindFromASideAndUnloadIt(ReloadStore store)
    {
        var side   = new SideContext(collectible: true);
        var shared = side.FromBin("SourceSharp.Server");

        if (shared is null)
        {
            Assert.Fail("a collectible side context can load the shared assembly");
            return null;
        }

        // P1 run 3: the state's base, its caches and the carrier's codec are
        // the side's OWN copies now (Shared.Interop and Shared, loaded into its
        // context); only Abi, which holds the reload store's bytes, is shared.
        // The premise restated: a side whose own states were bound to blobs
        // and saved into the shared store still unloads.
        var interop = SideCopy.Interop(side);
        Type netState = interop.GetType("SourceSharp.Abi.NetState", throwOnError: true)!;
        var stateType = shared.GetTypes().FirstOrDefault(
            t => t.GetCustomAttributes(interop.GetType("SourceSharp.Abi.StandaloneNetStateAttribute", true)!, false).Length > 0
                 && netState.IsAssignableFrom(t));

        if (stateType is null)
        {
            Assert.Fail("the shared assembly declares a networked state that is a NetState");
            return null;
        }

        // The two caches a running side fills, and then the thing this test is
        // about: a live instance from that side, bound to a blob the way the
        // registry binds one at spawn.
        var described = SideCopy.Static(interop, "StandaloneNetStates", "Of", stateType)!;
        SideCopy.Static(interop, "NetStateBinding", "FieldsOn", stateType);
        int size = (int)described.GetType().GetProperty("Size")!.GetValue(described)!;

        byte[] blob = GC.AllocateArray<byte>(Math.Max(size, 1), pinned: true);
        EntitySlot[] slot = GC.AllocateArray<EntitySlot>(1, pinned: true);

        slot[0].Serial = 77u;

        object state = Activator.CreateInstance(stateType)!;
        netState.GetMethod("BindTo", BindingFlags.Instance | BindingFlags.NonPublic, null,
                           new[] { typeof(EntityHandle), typeof(byte*), typeof(int), typeof(EntitySlot*), typeof(uint) }, null)!
                .Invoke(state, new object[]
                {
                    new EntityHandle(0x00030004u),
                    System.Reflection.Pointer.Box(Unsafe.AsPointer(ref blob[0]), typeof(byte*)), blob.Length,
                    System.Reflection.Pointer.Box(Unsafe.AsPointer(ref slot[0]), typeof(EntitySlot*)), 77u,
                });

        Assert.True((bool)netState.GetProperty("IsLive")!.GetValue(state)!, "the state from the side's context really is bound");

        // AND THE CARRIER, which is the newer half of the same hazard. A hot
        // reload saves every entity's state into the ABI so it can outlive this
        // context -- and the ABI lives in the DEFAULT one, so a carrier holding
        // a Type, an entity or a delegate over one would keep the whole side
        // alive forever. It holds strings, and this is what says so.
        Type saved = shared.GetType("SourceSharp.Abi.SavedEntity", throwOnError: true)!;
        object carried = Activator.CreateInstance(saved, 0x00030004u, "a_side_class", "named", -1)!;
        SideCopy.Static(shared, "NetStateTransfer", "Save", state, carried);

        int netFields = ((System.Collections.ICollection)saved.GetProperty("Net")!.GetValue(carried)!).Count;
        Assert.True(netFields > 0, "the carrier really did take something off a side's state");

        SideCopy.Static(shared, "ReloadCarrier", "Put", store, carried);

        var weak = new WeakReference(side, trackResurrection: true);

        side.Unload();

        // Deliberately still in the store when the context is collected: the
        // check is that a carrier LEFT BEHIND does not pin the side, since that
        // is exactly where one sits during a reload. The store is the test's own
        // (its side state's, statics to zero), as the Bootstrap's outlives a
        // side's context.
        return weak;
    }

    /// <summary>The message, or empty when nothing of that type was thrown.</summary>
    private static string Threw<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return string.Empty;
        }
        catch (T e)
        {
            return e.Message;
        }
    }
}
