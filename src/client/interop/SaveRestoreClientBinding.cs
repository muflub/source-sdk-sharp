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
/// <para>
/// And the save game's client cluster (the client save codec):
/// IBaseClientDLL's SaveInit, SaveWriteFields and SaveReadFields each forward
/// here, to the shared <see cref="SaveBlockSession"/> -- the client state
/// file's buffer, and its DECALLIST and MUSICLIST blocks.
/// </para>
/// </summary>
public sealed unsafe class SaveRestoreClientBinding(Crossings crossings, ILog log, ISideModule module, SaveBlockSession session)
    : IManagedSaveRestoreClient
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

    public void* SaveInit(int a0, float a1)
    {
        crossings.Count(CrossingOp.ClientSaveInit);
        try { return session.Init(a0, a1); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestoreClient.SaveInit", e); return null; }
    }

    public int SaveWriteFields(void* a0, byte* a1, void* a2, void* a3, int a4, int a5)
    {
        crossings.Count(CrossingOp.ClientSaveWriteFields);
        try { return session.WriteFields(a0, a1, (byte*)a2, (byte*)a3, a4, a5) ? 1 : 0; }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestoreClient.SaveWriteFields", e); return 0; }
    }

    public int SaveReadFields(void* a0, byte* a1, void* a2, void* a3, int a4, int a5)
    {
        crossings.Count(CrossingOp.ClientSaveReadFields);
        try { return session.ReadFields(a0, a1, (byte*)a2, (byte*)a3, a4, a5) ? 1 : 0; }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestoreClient.SaveReadFields", e); return 0; }
    }
}
