using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's standalone entity hierarchy and its net state layout asks,
/// managed-built (plan_client_vtables CE5): the bodies of the Entry.cs thunks
/// ClientStandaloneCreate/Spawn/Removed/DataChanged/Draw/DormancyChanged and
/// ClientNetStateFieldOffset/Bits, moved. The generated thunk has already made
/// the client the active side and catches what a body throws.
///
/// <para>
/// Every slot answers harmlessly for a shell with no managed class, which is
/// ordinary rather than exceptional. A faulting create or draw answers the
/// generated catch's 0 ("no managed half", "nothing drawn"), as the thunks did.
/// The two layout asks answer -1 ("not published") on a fault and the generated
/// catch would answer 0, a real offset, so each keeps its own catch, as the
/// server's NetStateLayoutBinding does.
/// </para>
///
/// <para>
/// Create, spawn and draw run per standalone entity: the class name goes
/// through this binding's own name cache, so a repeated name allocates nothing.
/// </para>
/// </summary>
public sealed unsafe class StandaloneClientBinding(Crossings crossings, ILog log, IClientModule module, StandaloneMirror mirror,
                                                   NetStateCatalog catalog) : IManagedStandaloneClient
{
    private readonly Utf8NameCache _names = new();

    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    public int OnStandaloneCreate(uint a0, byte* a1)
    {
        crossings.Count(CrossingOp.ClientStandaloneCreate);
        return module.StandaloneCreate(new EntityHandle(a0), _names.Get(a1)) ? 1 : 0;
    }

    public void OnStandaloneSpawn(uint a0)
    {
        crossings.Count(CrossingOp.ClientStandaloneSpawn);
        module.StandaloneSpawn(new EntityHandle(a0));
    }

    public void OnStandaloneRemoved(uint a0)
    {
        crossings.Count(CrossingOp.ClientStandaloneRemoved);
        module.StandaloneRemoved(new EntityHandle(a0));

        // AFTER the module, because a class's own teardown may still ask what
        // it was playing; and unconditionally, because this is the only thing
        // that keeps the mirror's size the live entity count. HANDLES ARE
        // RECYCLED: an entry left here is not stale, it is inherited, and a
        // dormancy of true would make the next entity to wear this number
        // invisible to every managed reader while the engine went on drawing
        // it. See StandaloneMirror.
        mirror.Forget(new EntityHandle(a0));
    }

    public void OnStandaloneDataChanged(uint a0, byte* a1, int a2, int a3)
    {
        crossings.Count(CrossingOp.ClientStandaloneDataChanged);
        if (a1 == null || a2 <= 0)
            return;

        module.StandaloneDataChanged(new EntityHandle(a0), new ReadOnlySpan<byte>(a1, a2), a3);
    }

    /// <summary>Draw this entity. Once per frame, for an entity that asked to.</summary>
    public int OnStandaloneDraw(uint a0)
    {
        crossings.Count(CrossingOp.ClientStandaloneDraw);
        return module.StandaloneDraw(new EntityHandle(a0));
    }

    /// <summary>A view's custom draws, prepared in one crossing (C7 item 7); see IClientModule.StandaloneDrawBatch.</summary>
    public void OnStandaloneDrawBatch(void* a0, int a1, void* a2)
    {
        crossings.Count(CrossingOp.ClientStandaloneDrawBatch);
        if (a0 == null || a2 == null || a1 <= 0)
            return;
        var results = new Span<int>(a2, a1);
        results.Fill(-1);
        module.StandaloneDrawBatch(new ReadOnlySpan<uint>(a0, a1), results);
    }

    /// <summary>
    /// The engine pushed a PVS transition for a client shell entity
    /// (plan_crossings.md Phase X3). Fed straight into the mirror: nothing in
    /// the client module decides anything here.
    /// </summary>
    public void OnStandaloneDormancyChanged(uint a0, int a1)
    {
        crossings.Count(CrossingOp.ClientStandaloneDormancyChanged);
        mirror.NoteDormancy(new EntityHandle(a0), a1 != 0);
    }

    /// <summary>
    /// Where a declared field of a managed net state sits inside the blob, or
    /// -1. The layout is the shared assembly's, the same number on both sides;
    /// the caller adds where its own blob starts. Resolved once and kept, never
    /// per frame.
    /// </summary>
    public int NetStateFieldOffset(byte* a0, byte* a1)
    {
        crossings.Count(CrossingOp.ClientNetStateFieldOffset);
        try { return catalog.FieldOffset(Utf8(a0), Utf8(a1)); }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedStandaloneClient.NetStateFieldOffset", e);
            return -1;
        }
    }

    /// <summary>
    /// The shift and width of that field inside its dword, or -1. Resolved once
    /// beside the offset: since packing, an offset names the word a field
    /// shares and is not on its own a placement.
    /// </summary>
    public int NetStateFieldBits(byte* a0, byte* a1)
    {
        crossings.Count(CrossingOp.ClientNetStateFieldBits);
        try { return catalog.FieldBits(Utf8(a0), Utf8(a1)); }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedStandaloneClient.NetStateFieldBits", e);
            return -1;
        }
    }
}
