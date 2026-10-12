using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's hot-reload save and restore, managed-built (plan_client_vtables
/// CE7): the bodies of the Entry.cs thunks ClientReloadSave and
/// ClientReloadRestore, moved, as the server's SaveRestoreBinding took its own
/// (plan_engineapi_vtables E4). The save game's entity block is the server's
/// alone: the client's SaveCollectEntities/SaveRestoreEntities had no native
/// caller (their call site sat under #ifndef CLIENT_DLL) and were dropped. The generated
/// thunk has already made the client the active side. Each failure still
/// answers -1 (the generated thunk's catch would answer 0, "nothing saved",
/// and 0 is a legitimate answer to "how much state is there"), so each keeps
/// its catch.
/// </summary>
public sealed unsafe class SaveRestoreClientBinding(Crossings crossings, ILog log, ISideModule module) : IManagedSaveRestoreClient
{
    public int ReloadSave()
    {
        crossings.Count(CrossingOp.ClientReloadSave);
        try { return module.ReloadSave(); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestoreClient.ReloadSave", e); return -1; }
    }

    public int ReloadRestore()
    {
        crossings.Count(CrossingOp.ClientReloadRestore);
        try { return module.ReloadRestore(); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestoreClient.ReloadRestore", e); return -1; }
    }
}
