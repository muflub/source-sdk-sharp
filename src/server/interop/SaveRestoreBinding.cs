using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's save/restore entries, managed-built (plan_engineapi_vtables
/// E4): hot reload's save and restore. The Entry.cs thunks' bodies, moved; each
/// failure still answers -1 (the generated thunk's catch would answer 0,
/// "nothing saved"), so each keeps its catch.
/// <para>
/// And the save game's whole server cluster (plan_server_native_v2 T4c):
/// IServerGameDLL's SaveInit, SaveWriteFields, SaveReadFields, Save and
/// Restore each forward here, to <see cref="SaveGameSession"/>.
/// </para>
/// </summary>
public sealed unsafe class SaveRestoreBinding(Crossings crossings, ILog log, ISideModule module, SaveGameSession session)
    : IManagedSaveRestore, ISavedEntities
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

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

    /// <summary>The module's entities, as the session's body block asks for them.</summary>
    int ISavedEntities.Collect(Span<byte> destination) => module.SaveCollect(destination);

    /// <inheritdoc/>
    int ISavedEntities.Restore(ReadOnlySpan<byte> source) => module.SaveRestore(source);

    public void* SaveInit(int a0, float a1)
    {
        Enter(CrossingOp.ServerSaveInit);
        try { return session.Init(a0, a1); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestore.SaveInit", e); return null; }
    }

    public int SaveWriteFields(void* a0, byte* a1, void* a2, void* a3, int a4, int a5)
    {
        Enter(CrossingOp.ServerSaveWriteFields);
        try { return session.WriteFields(a0, a1, (byte*)a2, (byte*)a3, a4, a5) ? 1 : 0; }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestore.SaveWriteFields", e); return 0; }
    }

    public int SaveReadFields(void* a0, byte* a1, void* a2, void* a3, int a4, int a5)
    {
        Enter(CrossingOp.ServerSaveReadFields);
        try { return session.ReadFields(a0, a1, (byte*)a2, (byte*)a3, a4, a5) ? 1 : 0; }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestore.SaveReadFields", e); return 0; }
    }

    public int SaveWriteBody(void* a0)
    {
        Enter(CrossingOp.ServerSaveWriteBody);
        try { return session.WriteEntities(a0, this) ? 1 : 0; }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestore.SaveWriteBody", e); return 0; }
    }

    public int SaveReadBody(void* a0)
    {
        Enter(CrossingOp.ServerSaveReadBody);
        try { return session.ReadEntities(a0, this); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedSaveRestore.SaveReadBody", e); return -1; }
    }
}
