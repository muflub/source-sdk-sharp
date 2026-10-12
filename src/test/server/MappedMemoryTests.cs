using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The blob as mapped memory — plan_nettable_generic.md Part 9, Step 5.
///
/// <para>
/// A state object stopped copying values into managed fields and started holding
/// a pointer into the memory the SendProps read. That trade buys a store where
/// there was an interop call, and it costs the one guarantee the old shape had
/// for free: a pointer cannot check that its entity is still there. THE ENTIRE
/// STEP RESTS ON THE COMPARE THAT REPLACES THAT CHECK, so this file is about the
/// compare rather than about the speed.
/// </para>
///
/// <para>
/// These tests play native. They allocate a blob and a control-block slot of
/// their own and bind a state to them, because what has to be provable is what
/// happens when the slot's serial moves — which in a running game means an
/// entity died, and which no test can arrange by killing an entity.
/// </para>
///
/// <para>
/// WHY THE FAILURES THROW rather than fail closed, which is what they used to
/// do: a read that answers a fabricated zero is a lie the game will act on. It
/// is the pane-of-glass failure one layer down — a value correct on one side,
/// wrong on the other, and nothing anywhere reporting it. Every one of the
/// throws below is checked for its MESSAGE as well as its type, because a throw
/// nobody can act on is barely better than silence.
/// </para>
///
/// <para>
/// THIS WAS ONE <c>[Fact]</c> WITH 47 ASSERTIONS IN IT, which is the shape
/// CLAUDE.md forbids and for the reason the split proved: xUnit stops a fact at
/// its first failure, so the 46 behind it reported nothing. It is 47 facts now,
/// one per assertion, each naming one behaviour, and the state each needs is
/// built by <see cref="Bound"/> rather than inherited from the assertion above
/// it — the bulk fact ran over ONE state object, so half its assertions were
/// reading whatever the assertion before them had left.
/// </para>
/// </summary>
public unsafe class MappedMemoryTests
{
    //-------------------------------------------------------------------------
    // NATIVE'S HALF, PLAYED BY HAND: a blob at least as long as the state, and
    // a slot with a serial nothing else holds. Pinned, because the pointer
    // handed to the state has to stay put.
    //
    // One per fact rather than one shared: the bulk fact this replaces ran its
    // assertions in a fixed order over ONE state, so half of them were reading
    // whatever the assertion above had left. That is exactly the dependency a
    // split has to remove, not preserve.
    //-------------------------------------------------------------------------

    private sealed class Bound
    {
        public const uint Serial = 4242u;

        public readonly StandaloneNetState Layout = StandaloneNetStates.Of(typeof(BarrelNetState));
        public readonly byte[] Blob;
        public readonly EntitySlot[] Slot = GC.AllocateArray<EntitySlot>(1, pinned: true);
        public readonly BarrelNetState State = new();

        public Bound()
        {
            Blob = GC.AllocateArray<byte>(Layout.Size + 8, pinned: true);

            Slot[0].Serial = Serial;
            Slot[0].Dirty = 0;

            State.BindTo(new EntityHandle(0x00010007u),
                         (byte*)Unsafe.AsPointer(ref Blob[0]), Blob.Length,
                         (EntitySlot*)Unsafe.AsPointer(ref Slot[0]), Serial);
        }

        public byte* PBlob => (byte*)Unsafe.AsPointer(ref Blob[0]);

        public EntitySlot* PSlot => (EntitySlot*)Unsafe.AsPointer(ref Slot[0]);

        public uint Dirty => Slot[0].Dirty;

        public void Clean() => Slot[0].Dirty = 0;

        /// <summary>The entity goes. Native closes the slot, which is a serial of zero.</summary>
        public void Close() => Slot[0].Serial = 0;

        public int Offset(string field) => Layout[field].Offset;
    }

    //-------------------------------------------------------------------------
    // THE WRITE IS THE UPDATE: there is no encode step and no copy, so the bytes
    // the SendProps read are the bytes the property set produced.
    //-------------------------------------------------------------------------

    [Fact]
    public void AWriteThroughACachedPointerLandsInTheBlob()
    {
        var b = new Bound();

        b.State.Thinks = 55;
        b.State.LastDamage = 1.5f;

        Assert.True(BitConverter.ToInt16(b.Blob, b.Offset("Thinks")) == 55
              && BitConverter.ToSingle(b.Blob, b.Offset("LastDamage")) == 1.5f, "a write through a cached pointer lands in the blob");
    }

    [Fact]
    public void AReadComesBackOutOfTheBlob()
    {
        var b = new Bound();

        b.State.Thinks = 55;
        b.State.LastDamage = 1.5f;

        Assert.True(b.State.Thinks == 55 && b.State.LastDamage == 1.5f, "a read comes back out of the blob rather than a managed copy");
    }

    [Fact]
    public void AChangeMadeInTheBlobIsWhatTheNextReadSees()
    {
        // The blob is what the props read, so a change made behind the state's
        // back — which is exactly what a poke command or a decode is — must be
        // what the next get returns. A managed copy would have shadowed it.
        var b = new Bound();

        b.State.Thinks = 55;
        BitConverter.GetBytes((short)77).CopyTo(b.Blob, b.Offset("Thinks"));

        Assert.True(b.State.Thinks == 77, "a change made in the blob is what the next read sees");
    }

    //-------------------------------------------------------------------------
    // The dirty word: a store, not a call, and set by writes only.
    //-------------------------------------------------------------------------

    [Fact]
    public void AWriteMarksTheEntityDirty()
    {
        var b = new Bound();

        b.State.Thinks = 55;

        Assert.True(b.Dirty == 1, "a write marks the entity dirty");
    }

    [Fact]
    public void AReadMarksNothing()
    {
        var b = new Bound();

        b.State.Thinks = 55;
        b.Clean();

        _ = b.State.Thinks;
        _ = b.State.NetModel;
        _ = b.State.SpawnOrigin;

        Assert.True(b.Dirty == 0, "a READ marks nothing — the behaviour change that makes this worth doing");
    }

    [Fact]
    public void AndTheNextWriteMarksItAgain()
    {
        var b = new Bound();

        b.State.Thinks = 55;
        b.Clean();
        _ = b.State.Thinks;

        b.State.Broken = true;

        Assert.True(b.Dirty == 1, "and the next write marks it again");
    }

    //-------------------------------------------------------------------------
    // A WRITE THAT CHANGES NOTHING MARKS NOTHING.
    //
    // A dirty edict is the only thing that makes SV_PackEntity encode at all,
    // and the hierarchy carries a 512-word payload — so a store of the value
    // already there costs a full encode and a full delta compare over two
    // kilobytes to send nothing. Recomputing an unchanged value is the ordinary
    // shape of a think function, so this is the common case.
    //
    // THE COMPARE IS BITWISE, and the tests that matter are the ones where
    // bitwise and numeric disagree. An epsilon is not an option: it suppresses
    // genuine small changes and lets error accumulate with nothing reporting it,
    // which is a far worse trade than the encode it saves.
    //-------------------------------------------------------------------------

    [Fact]
    public void TenWritesOfTheValueAlreadyThereMarkNothing()
    {
        var b = new Bound();

        b.State.Thinks = 1234;
        b.Clean();

        for (int i = 0; i < 10; i++)
            b.State.Thinks = 1234;

        Assert.True(b.Dirty == 0, "ten writes of the value already there mark nothing");
    }

    [Fact]
    public void AndTheValueIsStillWhatItWas()
    {
        var b = new Bound();

        b.State.Thinks = 1234;
        b.Clean();

        for (int i = 0; i < 10; i++)
            b.State.Thinks = 1234;

        Assert.True(b.State.Thinks == 1234, "and the value is still what it was");
    }

    [Fact]
    public void AWriteOfADifferentValueMarks()
    {
        var b = new Bound();

        b.State.Thinks = 1234;
        b.Clean();

        b.State.Thinks = 1235;

        Assert.True(b.Dirty == 1, "a write of a different value marks");
    }

    [Fact]
    public void ChangedAndChangedBackIsTwoChangesNotNone()
    {
        // A value that moves and moves back within a tick has genuinely changed
        // as far as any client that saw the intermediate state is concerned.
        // Collapsing that to nothing would be a correctness bug wearing the
        // costume of an optimisation. Each leg is counted on its own.
        var b = new Bound();

        b.State.Thinks = 1235;

        b.Clean();
        b.State.Thinks = 7;
        bool firstLeg = b.Dirty == 1;

        b.Clean();
        b.State.Thinks = 1235;
        bool secondLeg = b.Dirty == 1;

        Assert.True(firstLeg && secondLeg, "changed and changed back is two changes, not none");
    }

    [Fact]
    public void TheSameFloatMarksNothing()
    {
        var b = new Bound();

        b.State.LastDamage = 1.0f;
        b.Clean();
        b.State.LastDamage = 1.0f;

        Assert.True(b.Dirty == 0, "the same float marks nothing");
    }

    [Fact]
    public void AFloatThatDiffersByOneUlpIsAChange()
    {
        var b = new Bound();

        b.State.LastDamage = 1.0f;
        b.Clean();
        b.State.LastDamage = 1.0f + float.Epsilon;

        Assert.True(b.Dirty == 1 || b.State.LastDamage == 1.0f, "a float that differs by one ulp is a change, not noise");
    }

    [Fact]
    public void NegativeZeroOverPositiveZeroIsAChange()
    {
        // The one an epsilon compare gets wrong in the other direction.
        var b = new Bound();

        b.State.LastDamage = 0.0f;
        b.Clean();
        b.State.LastDamage = -0.0f;

        Assert.True(b.Dirty == 1, "-0.0 over +0.0 is a change: equal numerically, different on the wire");
    }

    [Fact]
    public void RewritingTheSameNaNMarksNothing()
    {
        // NaN != NaN numerically, so a numeric guard would mark every NaN write
        // dirty forever and this entity would re-encode every tick for the life
        // of the map. Bitwise gives the stable answer.
        var b = new Bound();

        b.State.LastDamage = float.NaN;
        b.Clean();
        b.State.LastDamage = float.NaN;

        Assert.True(b.Dirty == 0, "rewriting the same NaN marks nothing");
    }

    [Fact]
    public void NaNToARealNumberStillMarks()
    {
        var b = new Bound();

        b.State.LastDamage = float.NaN;
        b.Clean();
        b.State.LastDamage = 0.0f;

        Assert.True(b.Dirty == 1, "and NaN to a real number still marks");
    }

    [Fact]
    public void TheSameStringMarksNothing()
    {
        var b = new Bound();

        b.State.NetModel = "models/props/glass.mdl";
        b.Clean();
        b.State.NetModel = "models/props/glass.mdl";

        Assert.True(b.Dirty == 0, "the same string marks nothing");
    }

    [Fact]
    public void AShorterStringWithTheSamePrefixIsStillAChange()
    {
        // The writer clears the buffer before encoding, so a shorter string
        // leaves different trailing bytes and those bytes go on the wire.
        // Comparing only the prefix would call this pair unchanged.
        var b = new Bound();

        b.State.NetModel = "models/props/glass.mdl";
        b.Clean();
        b.State.NetModel = "models/props";

        Assert.True(b.Dirty == 1, "a shorter string with the same prefix is still a change");
    }

    [Fact]
    public void AndItReadsBackAsTheShorterString()
    {
        var b = new Bound();

        b.State.NetModel = "models/props/glass.mdl";
        b.State.NetModel = "models/props";

        Assert.True(b.State.NetModel == "models/props", "and it reads back as the shorter string");
    }

    [Fact]
    public void AStringTooLongForItsBufferMarksNothingAndWritesNothing()
    {
        // The one case where a set does neither: too long for its buffer, so it
        // throws before the compare. It must leave the bytes and the flag
        // exactly as it found them — a half-written path that also marked the
        // entity would put a truncated lookup on the wire and say it was
        // deliberate. Step 13; the buffer is 64 here.
        var b = new Bound();

        b.State.NetModel = "models/props";
        b.Clean();

        try   { b.State.NetModel = new string('x', 64); }
        catch (FieldRangeException) { }

        Assert.True(b.Dirty == 0 && b.State.NetModel == "models/props", "a string too long for its buffer marks nothing and writes nothing");
    }

    [Fact]
    public void AWriteWhoseBytesDifferRepairsThemAndMarks()
    {
        // Not just "the flag stayed clear": a no-op that rewrote the same bytes
        // would still be a store, and on this design the store IS the update.
        var b = new Bound();

        b.State.Thinks = 999;
        int offset = b.Offset("Thinks");
        b.Blob[offset] ^= 0xFF;                       // corrupt it behind the state
        byte corrupted = b.Blob[offset];

        b.Clean();
        b.State.Thinks = 999;                          // the value it "should" hold

        Assert.True(b.Blob[offset] != corrupted && b.Dirty == 1, "a write whose bytes differ repairs them and marks");
    }

    //-------------------------------------------------------------------------
    // The entity goes. Native closes the slot, which is a serial of zero, and
    // every access through the state that was bound to it must say so.
    //-------------------------------------------------------------------------

    [Fact]
    public void AStaleStateIsNotLiveAndAskingDoesNotThrow()
    {
        var b = new Bound();
        b.Close();

        Assert.True(!b.State.IsLive, "a stale state is not live, and asking does not throw");
    }

    [Fact]
    public void ASetThroughAStaleStateThrows()
    {
        var b = new Bound();
        b.Close();

        Assert.True(Threw<StaleEntityException>(() => b.State.Thinks = 1).Length > 0, "a set through a stale state throws");
    }

    [Fact]
    public void AGetThroughAStaleStateThrowsToo()
    {
        var b = new Bound();
        b.Close();

        Assert.True(Threw<StaleEntityException>(() => _ = b.State.Thinks).Length > 0, "a get through a stale state throws too");
    }

    // A throw nobody can act on is barely better than silence, so the message
    // has to name the thing that moved as well as the thing that broke.

    [Fact]
    public void TheStaleMessageNamesTheStateTypeAndTheField()
    {
        var b = new Bound();
        b.Close();

        Assert.True(Threw<StaleEntityException>(() => b.State.Thinks = 1).Contains("BarrelNetState.Thinks"), "the message names the state type and the field");
    }

    [Fact]
    public void TheStaleMessageNamesTheEntityClassAndIndex()
    {
        var b = new Bound();
        b.Close();

        string message = Threw<StaleEntityException>(() => b.State.Thinks = 1);

        Assert.True(message.Contains("ss_barrel") && message.Contains("entity 7"), "the message names the entity class and index");
    }

    [Fact]
    public void TheStaleMessageNamesBothSerials()
    {
        var b = new Bound();
        b.Close();

        string message = Threw<StaleEntityException>(() => b.State.Thinks = 1);

        Assert.True(message.Contains("expected serial 4242") && message.Contains("says 0"), "the message names both serials");
    }

    [Fact]
    public void TheThrowDoesNotHalfWrite()
    {
        // The one that pins the ORDER of the accessor's steps. A future version
        // that stored before it checked would pass every test above and fail
        // this one, and what it would be doing in a running game is writing into
        // memory the allocator has already handed to something else.
        var b = new Bound();

        b.State.Thinks = 55;
        byte[] before = b.Blob.ToArray();
        b.Clean();
        b.Close();

        _ = Threw<StaleEntityException>(() => b.State.Thinks = 1);

        Assert.True(b.Blob.AsSpan().SequenceEqual(before), "the throw does not half-write: the blob is untouched");
    }

    [Fact]
    public void AndTheDirtyWordIsStillClearAfterAThrow()
    {
        var b = new Bound();

        b.State.Thinks = 55;
        b.Clean();
        b.Close();

        _ = Threw<StaleEntityException>(() => b.State.Thinks = 1);

        Assert.True(b.Dirty == 0, "and the dirty word is still clear");
    }

    //-------------------------------------------------------------------------
    // Rebinding: a fresh serial from native is what a state gets when its entity
    // is created again, and it must start working again.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARebindAtTheNewSerialWorksAgain()
    {
        var b = new Bound();
        b.Close();

        b.Slot[0].Serial = Bound.Serial + 1;
        b.State.BindTo(new EntityHandle(0x00010007u), b.PBlob, b.Blob.Length, b.PSlot, Bound.Serial + 1);
        b.State.Thinks = 3;

        Assert.True(b.State.Thinks == 3, "a rebind at the new serial works again");
    }

    //-------------------------------------------------------------------------
    // No storage at all.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnUnboundStateThrowsOnSet()
    {
        var unbound = new BarrelNetState();

        Assert.True(Threw<UnboundStateException>(() => unbound.Thinks = 1).Length > 0, "an unbound state throws on set");
    }

    [Fact]
    public void AnUnboundStateThrowsOnGet()
    {
        var unbound = new BarrelNetState();

        Assert.True(Threw<UnboundStateException>(() => _ = unbound.Thinks).Length > 0, "an unbound state throws on get");
    }

    [Fact]
    public void AnUnboundStateIsNotLive()
    {
        var unbound = new BarrelNetState();

        Assert.True(!unbound.IsLive, "an unbound state is not live, and asking does not throw");
    }

    [Fact]
    public void TheUnboundMessageNamesTheStateAndTheField()
    {
        var unbound = new BarrelNetState();

        Assert.True(Threw<UnboundStateException>(() => _ = unbound.Broken)
                  .Contains("BarrelNetState.Broken"), "the unbound message names the state and the field");
    }

    [Fact]
    public void AnUnboundAgainStateThrowsRatherThanWritingThrough()
    {
        // Unbind is the deliberate drop: the entity is still there, and this
        // state object is not allowed to keep writing to it.
        //
        // It is NOT the managed-reload path, which is what this comment used to
        // say. A reload cannot reach the states to unbind them without the ABI
        // holding every one of them, and holding them is what stops a
        // collectible context unloading. The reload invalidates from native
        // instead, by renumbering the slot — see ReloadTests.
        var b = new Bound();

        var dropped = new BarrelNetState();
        dropped.BindTo(new EntityHandle(0x00010007u), b.PBlob, b.Blob.Length, b.PSlot, Bound.Serial);
        dropped.Unbind();

        Assert.True(Threw<UnboundStateException>(() => dropped.Thinks = 1).Length > 0, "an unbound-again state throws rather than writing through");
    }

    //-------------------------------------------------------------------------
    // A blob shorter than the state. Refused AT BIND, because the check cannot
    // be made again: a bound state holds the pointer.
    //-------------------------------------------------------------------------

    [Fact]
    public void BindingABlobShorterThanTheStateThrows()
    {
        var b = new Bound();

        byte[] tiny = GC.AllocateArray<byte>(4, pinned: true);
        byte* pTiny = (byte*)Unsafe.AsPointer(ref tiny[0]);

        Assert.True(Threw<BlobTooShortException>(
            () => new BarrelNetState().BindTo(new EntityHandle(3u), pTiny, 4, b.PSlot, Bound.Serial)).Length > 0,
            "binding a blob shorter than the state throws");
    }

    [Fact]
    public void AndTheShortBindMessageCarriesBothSizes()
    {
        var b = new Bound();

        byte[] tiny = GC.AllocateArray<byte>(4, pinned: true);
        byte* pTiny = (byte*)Unsafe.AsPointer(ref tiny[0]);

        string message = Threw<BlobTooShortException>(
            () => new BarrelNetState().BindTo(new EntityHandle(3u), pTiny, 4, b.PSlot, Bound.Serial));

        Assert.True(message.Contains(b.Layout.Size.ToString()) && message.Contains("offers 4"), "and the message carries both sizes");
    }

    [Fact]
    public void ABlobExactlyAsLongAsTheStateIsAccepted()
    {
        // Exactly the right length is not short. The boundary is worth pinning
        // because an off-by-one here refuses every legitimate bind, and the
        // hierarchy's blob is sized to the greediest class exactly.
        var b = new Bound();

        byte[] exact = GC.AllocateArray<byte>(b.Layout.Size, pinned: true);
        var snug = new BarrelNetState();

        snug.BindTo(new EntityHandle(3u), (byte*)Unsafe.AsPointer(ref exact[0]),
                    exact.Length, b.PSlot, Bound.Serial);

        Assert.True(snug.IsLive, "a blob exactly as long as the state is accepted");
    }

    //-------------------------------------------------------------------------
    // The view cannot escape, which is the one rule here enforced by the
    // compiler rather than by a check.
    //
    // Part 15 asked for a negative compilation test asserting CS8345. This
    // project's test assembly has no Roslyn reference to compile against, so
    // what is pinned instead is the property the compiler USES to produce that
    // error: a by-ref-like type may not be a field, be boxed, be captured or
    // outlive its stack frame. If a refactor ever made the view an ordinary
    // struct, this is what would notice.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheViewIsARefStructSoItCannotBeStored()
    {
        Assert.True(typeof(NetBlobView).IsByRefLike, "the view is a ref struct, so it cannot be stored");
    }

    [Fact]
    public void TheSlotItPointsAtIsAPlainUnmanagedPair()
    {
        Assert.True(sizeof(EntitySlot) == 8, "and the slot it points at is a plain unmanaged pair");
    }

    //-------------------------------------------------------------------------
    // A run of elements goes through the same gate as a single value, which is
    // what stops the array being a second write path with its own rules.
    //-------------------------------------------------------------------------

    private sealed class Glass
    {
        public readonly StandaloneNetState Layout = StandaloneNetStates.Of(typeof(BreakableSurfaceNetState));
        public readonly byte[] Pane;
        public readonly EntitySlot[] Slot = GC.AllocateArray<EntitySlot>(1, pinned: true);
        public readonly BreakableSurfaceNetState State = new();

        public Glass()
        {
            Pane = GC.AllocateArray<byte>(Layout.Size, pinned: true);
            Slot[0].Serial = 9u;

            State.BindTo(new EntityHandle(11u), (byte*)Unsafe.AsPointer(ref Pane[0]), Pane.Length,
                         (EntitySlot*)Unsafe.AsPointer(ref Slot[0]), 9u);
        }

        public uint Dirty => Slot[0].Dirty;

        public void Clean() => Slot[0].Dirty = 0;

        public void Close() => Slot[0].Serial = 0;
    }

    [Fact]
    public void FillingTheRunWritesTheBlob()
    {
        var g = new Glass();

        g.State.FillPanes();

        Assert.True(BitConverter.ToUInt32(g.Pane, g.Layout["PaneBits.0"].Offset) == uint.MaxValue, "filling the run writes the blob");
    }

    [Fact]
    public void ReadingAPaneMarksNothing()
    {
        var g = new Glass();

        g.State.FillPanes();
        g.Clean();

        Assert.True(g.State.IsPaneSolid(0, 0) && g.Dirty == 0, "reading a pane marks nothing");
    }

    [Fact]
    public void WritingAPaneMarksTheEntity()
    {
        var g = new Glass();

        g.State.FillPanes();
        g.Clean();

        g.State.SetPaneSolid(0, 0, false);

        Assert.True(g.Dirty == 1, "writing a pane marks the entity");
    }

    [Fact]
    public void ARunThroughAStaleStateThrowsLikeAnyOtherField()
    {
        var g = new Glass();
        g.Close();

        Assert.True(Threw<StaleEntityException>(() => g.State.SetPaneSolid(1, 1, false)).Length > 0, "a run through a stale state throws like any other field");
    }

    [Fact]
    public void AndSoDoesReadingOneThroughAStaleState()
    {
        var g = new Glass();
        g.Close();

        Assert.True(Threw<StaleEntityException>(() => _ = g.State.IsPaneSolid(1, 1)).Length > 0, "and so does reading one");
    }

    //-------------------------------------------------------------------------
    // Decode, which is now a memcpy rather than a generated field walk.
    //-------------------------------------------------------------------------

    private static (BarrelNetState State, StandaloneNetState Layout) Local()
    {
        var client = new BarrelNetState();
        client.BindLocal();

        return (client, StandaloneNetStates.Of(typeof(BarrelNetState)));
    }

    [Fact]
    public void DecodeIsAMemcpyAndTheAccessorsReadWhatItLeft()
    {
        var (client, layout) = Local();

        byte[] update = new byte[layout.Size];
        BitConverter.GetBytes((short)909).CopyTo(update, layout["Thinks"].Offset);
        client.Decode(update);

        Assert.True(client.Thinks == 909, "decode is a memcpy and the accessors read what it left");
    }

    [Fact]
    public void ALocalStatesSnapshotIsItsWholeImage()
    {
        var (client, layout) = Local();

        Assert.True(client.Snapshot().Length == layout.Size, "a local state's snapshot is its whole image");
    }

    [Fact]
    public void EachLocalStateHasABufferOfItsOwn()
    {
        // The client's storage must not be the entity's: two client states of
        // the same class decoding different updates must not overwrite one
        // another, which a single shared buffer would.
        var (client, layout) = Local();
        var (other, _) = Local();

        byte[] update = new byte[layout.Size];

        BitConverter.GetBytes((short)909).CopyTo(update, layout["Thinks"].Offset);
        client.Decode(update);

        BitConverter.GetBytes((short)111).CopyTo(update, layout["Thinks"].Offset);
        other.Decode(update);

        Assert.True(client.Thinks == 909 && other.Thinks == 111, "each local state has a buffer of its own");
    }

    /// <summary>
    /// Runs an action that should throw and returns the message, or "" if it did
    /// not throw the expected type.
    ///
    /// The MESSAGE rather than a bool, because half of what these tests check is
    /// that the exception carries enough to act on — the state type, the field,
    /// the entity and both serials.
    /// </summary>
    private static string Threw<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return "";
        }
        catch (T e)
        {
            return e.Message;
        }
        catch
        {
            return "";
        }
    }
}
