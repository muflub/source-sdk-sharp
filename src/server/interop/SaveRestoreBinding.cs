using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's save/restore entries, managed-built (plan_engineapi_vtables
/// E4): hot reload's save and restore, and the save game's entity block. The
/// Entry.cs thunks' bodies, moved; each failure still answers -1 (the generated
/// thunk's catch would answer 0, "nothing saved"), so each keeps its catch.
/// </summary>
public sealed unsafe class SaveRestoreBinding(Crossings crossings, ILog log, ISideModule module) : IManagedSaveRestore
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    private static Span<byte> AsBytes(byte* buffer, int bytes)
        => buffer == null || bytes <= 0 ? Span<byte>.Empty : new Span<byte>(buffer, bytes);

    public int ReloadSave()
    {
        Enter(CrossingOp.ServerReloadSave);
        try { return module.ReloadSave(); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestore.ReloadSave", e); return -1; }
    }

    public int ReloadRestore()
    {
        Enter(CrossingOp.ServerReloadRestore);
        try { return module.ReloadRestore(); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestore.ReloadRestore", e); return -1; }
    }

    public int SaveCollectEntities(byte* a0, int a1)
    {
        Enter(CrossingOp.ServerSaveCollect);
        try { return module.SaveCollect(AsBytes(a0, a1)); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestore.SaveCollectEntities", e); return -1; }
    }

    public int SaveRestoreEntities(byte* a0, int a1)
    {
        Enter(CrossingOp.ServerSaveRestore);
        try { return module.SaveRestore(AsBytes(a0, a1)); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestore.SaveRestoreEntities", e); return -1; }
    }
}
