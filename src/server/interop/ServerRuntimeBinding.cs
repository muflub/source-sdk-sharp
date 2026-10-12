using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// <c>IManagedRuntimeServer</c> as managed writes it (plan_engineapi_vtables D5):
/// the shell game DLL's calls into the mod, slot for slot, in the types the
/// native table carries. <c>bool</c> is <c>byte</c>; a map load is the ABI's
/// flat <c>SourceSharpMapLoad</c> by pointer.
/// </summary>
[ManagedVtable(typeof(ManagedRuntimeServerVtbl))]
public unsafe interface IServerRuntime
{
    byte OnSystemStart();
    byte OnGameStart();
    void OnGamePostStart();
    void OnGameStop();
    byte OnMapLoad_Transition(void* pLoad);
    byte OnMapLoad_LoadGame(void* pLoad);
    byte OnMapLoad_Background(void* pLoad);
    byte OnMapLoad_NewGame(void* pLoad);
    void OnMapUnload();
    void OnServerActivate();
    void OnGameFrame(byte simulating);
    void OnPreClientUpdate();
    void OnThink();
    void CreateNetworkStringTables();
    float OnTickInterval();
    byte OnShouldHideServer();
    int OnBrowserMapOverride(byte* pOut, int nOutSize);
    int OnBrowserGameData(byte* pOut, int nOutSize);
    byte OnIsManualMapChangeOkay(byte* pReasonOut, int nOutSize);
    void OnQueryCvarValueFinished(int nCookie, int nEntIndex, int eStatus, byte* pCvarName, byte* pCvarValue);
    void OnHibernation(byte bHibernating);
    int OnStatusLines(byte* pOut, int nOutSize);
    void OnInvalidateMdlCache();
    void OnSteamGameServerActivated();
    void OnSteamGameServerShutdown();
    void OnMapLoadPreEntity(byte* pszMapName);
    void OnMapUnloadPreEntity();
    int OnWorldKeyValue(uint hWorld, byte* pKey, byte* pValue);
    void OnGetGameDescription(byte* pOut, int nOutSize);
}

/// <summary>
/// Forwards each runtime slot to the server module's own contracts. The bodies
/// are the Entry.cs thunks they replace, moved here so they load and unload
/// with the server's context: the same crossing counts, the same side marking,
/// and the same answers when there is no game. Exceptions are the generated
/// thunk's to catch.
/// </summary>
public sealed unsafe class ServerRuntimeBinding(Crossings crossings, ILog log, IServerModule game) : IServerRuntime
{
    private static byte B(bool value) => value ? (byte)1 : (byte)0;

    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    /// <summary>The engine's four pointers, borrowed for exactly this call (Entry.Borrow's reasons).</summary>
    private static MapLoad Borrow(MapLoadRaw* pLoad)
        => new(Utf8(pLoad->MapName),
               pLoad->MapEntities == null
                   ? ReadOnlySpan<byte>.Empty
                   : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(pLoad->MapEntities),
               Utf8(pLoad->OldLevel),
               Utf8(pLoad->LandmarkName));

    public byte OnSystemStart() { Enter(CrossingOp.ServerSystemStart); return B(game.Lifetime.SystemStart()); }

    public byte OnGameStart() { Enter(CrossingOp.ServerGameStart); return B(game.Lifetime.GameStart()); }

    public void OnGamePostStart() { Enter(CrossingOp.ServerGamePostStart); game.Lifetime.GamePostStart(); }

    public void OnGameStop() { Enter(CrossingOp.ServerGameStop); game.Lifetime.GameStop(); }

    public byte OnMapLoad_Transition(void* pLoad)
    {
        Enter(CrossingOp.ServerMapLoadTransition);
        return pLoad == null ? (byte)0 : B(game.Level.MapLoadTransition(Borrow((MapLoadRaw*)pLoad)));
    }

    public byte OnMapLoad_LoadGame(void* pLoad)
    {
        Enter(CrossingOp.ServerMapLoadSaveGame);
        return pLoad == null ? (byte)0 : B(game.Level.MapLoadSaveGame(Borrow((MapLoadRaw*)pLoad)));
    }

    public byte OnMapLoad_Background(void* pLoad)
    {
        Enter(CrossingOp.ServerMapLoadBackground);
        return pLoad == null ? (byte)0 : B(game.Level.MapLoadBackground(Borrow((MapLoadRaw*)pLoad)));
    }

    public byte OnMapLoad_NewGame(void* pLoad)
    {
        Enter(CrossingOp.ServerMapLoadNewGame);
        return pLoad == null ? (byte)0 : B(game.Level.MapLoadNewGame(Borrow((MapLoadRaw*)pLoad)));
    }

    public void OnMapUnload() { Enter(CrossingOp.ServerMapUnload); game.Level.MapUnload(); }

    public void OnServerActivate() { Enter(CrossingOp.ServerActivate); game.Level.ServerActivate(); }

    public void OnGameFrame(byte simulating) { Enter(CrossingOp.ServerGameFrame); game.Frames.GameFrame(simulating != 0); game.DrainConsole(); }

    public void OnPreClientUpdate() { Enter(CrossingOp.ServerPreClientUpdate); game.Frames.PreClientUpdate(); }

    public void OnThink() { Enter(CrossingOp.ServerThink); game.EngineThink(); }

    public void CreateNetworkStringTables()
    {
        Enter(CrossingOp.ServerCreateNetworkStringTables);
        game.Level.CreateNetworkStringTables();
    }

    // THE TWO SLOTS WHOSE FAILURE ANSWER IS NOT ZERO keep their own catch: the
    // generated thunk answers default, and a tick interval of 0 or a map change
    // refused because the check threw are both worse than the thunks' fallbacks.
    public float OnTickInterval()
    {
        Enter(CrossingOp.ServerTickInterval);
        try { return game.Lifetime.TickInterval; }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IServerRuntime.OnTickInterval", e);
            return IServerLifetime.DefaultTickInterval;
        }
    }

    public byte OnShouldHideServer() { Enter(CrossingOp.ServerShouldHideServer); return B(game.Listing.ShouldHideServer); }

    public int OnBrowserMapOverride(byte* pOut, int nOutSize)
    {
        Enter(CrossingOp.ServerBrowserMapOverride);
        if (pOut == null || nOutSize <= 0) return ListingBuffer.NoAnswer;
        return ListingBuffer.Write(game.Listing.BrowserMapOverride, new Span<byte>(pOut, nOutSize));
    }

    public int OnBrowserGameData(byte* pOut, int nOutSize)
    {
        Enter(CrossingOp.ServerBrowserGameData);
        if (pOut == null || nOutSize <= 0) return ListingBuffer.NoAnswer;
        return ListingBuffer.Write(game.Listing.BrowserGameData, new Span<byte>(pOut, nOutSize));
    }

    public byte OnIsManualMapChangeOkay(byte* pReasonOut, int nOutSize)
    {
        Enter(CrossingOp.ServerIsManualMapChangeOkay);
        try
        {
            bool okay = game.Listing.IsManualMapChangeOkay(out string? reason);

            if (pReasonOut != null && nOutSize > 0)
                ListingBuffer.Write(okay ? null : reason, new Span<byte>(pReasonOut, nOutSize));

            return B(okay);
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IServerRuntime.OnIsManualMapChangeOkay", e);
            return 1;
        }
    }

    public void OnQueryCvarValueFinished(int nCookie, int nEntIndex, int eStatus, byte* pCvarName, byte* pCvarValue)
    {
        Enter(CrossingOp.ServerQueryCvarValueFinished);
        game.Hosting.QueryCvarValueFinished(
            nCookie, nEntIndex, (QueryCvarStatus)eStatus,
            Utf8(pCvarName),
            pCvarValue == null ? null : Utf8(pCvarValue));
    }

    public void OnHibernation(byte bHibernating) { Enter(CrossingOp.ServerHibernation); game.Hosting.SetHibernating(bHibernating != 0); }

    public int OnStatusLines(byte* pOut, int nOutSize)
    {
        Enter(CrossingOp.ServerStatusLines);
        if (pOut == null || nOutSize <= 0) return ListingBuffer.NoAnswer;

        IEnumerable<string>? lines = game.Hosting.StatusLines;
        if (lines is null) return ListingBuffer.NoAnswer;

        return ListingBuffer.Write(string.Join('\n', lines), new Span<byte>(pOut, nOutSize));
    }

    public void OnInvalidateMdlCache() { Enter(CrossingOp.ServerInvalidateMdlCache); game.Hosting.InvalidateMdlCache(); }

    public void OnSteamGameServerActivated() { Enter(CrossingOp.ServerSteamGameServerActivated); game.Hosting.SteamGameServerActivated(); }

    public void OnSteamGameServerShutdown() { Enter(CrossingOp.ServerSteamGameServerShutdown); game.Hosting.SteamGameServerShutdown(); }

    public void OnMapLoadPreEntity(byte* pszMapName)
    {
        Enter(CrossingOp.ServerMapLoadPreEntity);
        game.Level.MapLoadPreEntity(Utf8(pszMapName));
    }

    public void OnMapUnloadPreEntity() { Enter(CrossingOp.ServerMapUnloadPreEntity); game.Level.MapUnloadPreEntity(); }

    public void OnGetGameDescription(byte* pOut, int nOutSize)
    {
        Enter(CrossingOp.ServerGetGameDescription);
        if (pOut == null || nOutSize <= 0) return;
        ListingBuffer.Write(game.Listing.GameDescription, new Span<byte>(pOut, nOutSize));
    }

    public int OnWorldKeyValue(uint hWorld, byte* pKey, byte* pValue)
    {
        Enter(CrossingOp.ServerWorldKeyValue);
        if (pKey == null || pValue == null) return 0;

        string key = Utf8(pKey);
        if (key.Length == 0) return 0;

        return game.Level.WorldKeyValue(new EntityHandle(hWorld), key, Utf8(pValue)) ? 1 : 0;
    }
}

/// <summary>
/// plan_client_vtables SI: the runtime's block, built through this assembly
/// (its generated Create is internal here), as ClientInteropAreas does for
/// the client. Stateless.
/// </summary>
public static class ServerInteropAreas
{
    public static ManagedVtable Runtime(ManagedVtables vtables, Crossings crossings, ILog log, IServerModule game)
        => IServerRuntime__ManagedVtable.Create(vtables, new ServerRuntimeBinding(crossings, log, game));
}
