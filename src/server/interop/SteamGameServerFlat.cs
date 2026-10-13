using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// How <see cref="SteamGameServerFlat"/> finds the engine's libsteam_api.so and
/// its exports: the loaded copy or none (<see cref="LoadedLibrary"/>) in the game, a fake in a test.
/// </summary>
public interface ISteamLibrary
{
    /// <summary>The handle of the copy this process already loaded, or zero (never loads one).</summary>
    IntPtr OpenLoaded(string file);

    /// <summary>An export's address in that library.</summary>
    bool TryGetExport(IntPtr library, string name, out IntPtr address);
}

/// <summary>The process's own loader: the loaded copy or none (<see cref="LoadedLibrary"/>), and its exports.</summary>
internal sealed class LoadedSteamLibrary : ISteamLibrary
{
    public IntPtr OpenLoaded(string file) => LoadedLibrary.Open(file);

    public bool TryGetExport(IntPtr library, string name, out IntPtr address)
        => NativeLibrary.TryGetExport(library, name, out address);
}

/// <summary>
/// The Steam game server, through libsteam_api.so's flat C API —
/// plan_server_novalve.md P2.5. server.so neither links libsteam_api.so nor
/// includes a steam/ header any more; this is the server's whole use of Steam,
/// which was CSteamGameServerAPIContext's Init/Clear and one GetGameplayStats.
///
/// <para>
/// <b>THE ENGINE'S COPY, OR NONE.</b> The library is opened with
/// <see cref="LoadedLibrary"/>: it answers the copy the engine already loaded, and
/// nothing when there is none. It can never load a second copy, which would be
/// a second Steam context talking to nobody. The launcher loads the same file
/// by path before the engine does, so on this build there is exactly one.
/// </para>
///
/// <para>
/// <b>RE-FETCHED ON EVERY ACTIVATION</b>, as CSteamGameServerAPIContext::Init
/// was: Steam hands out a new interface after a reconnect and the old pointer
/// is stale by then. <see cref="Clear"/> drops it, as Clear() did, and nothing
/// fetches it again until the next activation.
/// </para>
///
/// <para>
/// <b>AN INSTANCE, ONE PER SERVER LOAD</b> (P1 run 3, the user's ruling: it
/// was seven statics in Abi so that it outlived a reload). The server
/// composition owns it and hands it to the engine host binding. Every value it
/// holds can be derived again, so an instance that was never activated or
/// cleared — the one a <c>cs_sv_reload</c> builds while the server is up —
/// binds on its first use: dlopen, dlsym, then the accessor.
/// </para>
/// </summary>
public sealed unsafe class SteamGameServerFlat : ISteamGameServer
{
    public static string Library => LoadedLibrary.SteamApi;

    // SteamGameServer014: the version THIS libsteam_api.so's flat API is built
    // for -- it exports SteamAPI_SteamGameServer_v014 and no other. The SDK's
    // isteamgameserver.h names 015, which the native context asked the Steam
    // client for; the client serves both, and the flat functions below take
    // the 014 pointer their own accessor returns, so the pair is consistent.
    public const string AccessorExport = "SteamAPI_SteamGameServer_v014";

    private readonly ISteamLibrary _loader;

    private bool _bound;
    private IntPtr _library;
    private delegate* unmanaged<IntPtr> _accessor;
    private delegate* unmanaged<IntPtr, void> _getGameplayStats;
    private delegate* unmanaged<IntPtr, byte> _loggedOn;

    private IntPtr _gameServer;

    // Fresh: never activated or cleared in this load, so the first use fetches
    // the live pointer (the reload case). Activated or Cleared: only an
    // activation (Reacquire) fetches it again, as stock's context did.
    private bool _told;

    /// <summary>Over the process's own loaded copy of libsteam_api.so.</summary>
    public SteamGameServerFlat() : this(new LoadedSteamLibrary()) { }

    /// <summary>Over the given loader: a test's fake.</summary>
    public SteamGameServerFlat(ISteamLibrary loader)
        => _loader = loader ?? throw new ArgumentNullException(nameof(loader));

    /// <inheritdoc/>
    public string WhyUnbound { get; private set; } = "not asked yet";

    /// <summary>The engine's libsteam_api.so handle, or zero. The instance the engine uses (<see cref="LoadedLibrary"/>).</summary>
    public IntPtr LibraryHandle => Bind() ? _library : IntPtr.Zero;

    private bool Bind()
    {
        if (_bound)
            return true;

        _library = _loader.OpenLoaded(Library);
        if (_library == IntPtr.Zero)
        {
            WhyUnbound = Library + " is not loaded in this process (" + LoadedLibrary.NoLoad + "); no Steam game server";
            return false;
        }

        if (!_loader.TryGetExport(_library, AccessorExport, out IntPtr accessor) ||
            !_loader.TryGetExport(_library, "SteamAPI_ISteamGameServer_GetGameplayStats", out IntPtr stats) ||
            !_loader.TryGetExport(_library, "SteamAPI_ISteamGameServer_BLoggedOn", out IntPtr loggedOn))
        {
            WhyUnbound = Library + " does not export " + AccessorExport +
                         " / SteamAPI_ISteamGameServer_GetGameplayStats / SteamAPI_ISteamGameServer_BLoggedOn";
            return false;
        }

        _accessor = (delegate* unmanaged<IntPtr>)accessor;
        _getGameplayStats = (delegate* unmanaged<IntPtr, void>)stats;
        _loggedOn = (delegate* unmanaged<IntPtr, byte>)loggedOn;
        _bound = true;
        WhyUnbound = string.Empty;
        return true;
    }

    /// <summary>
    /// Clear, then fetch the game server interface afresh. Answers whether
    /// there is a game server to talk to — stock's own next test.
    /// </summary>
    public bool Reacquire()
    {
        Clear();

        if (!Bind())
            return false;

        _gameServer = _accessor();
        return _gameServer != IntPtr.Zero;
    }

    /// <summary>Drop the interface; the next activation fetches it again.</summary>
    public void Clear()
    {
        _gameServer = IntPtr.Zero;
        _told = true;
    }

    // The pointer the two calls use: the held one, or, in a load that was
    // never told (a reloaded module's), the live one, bound on first use.
    private IntPtr GameServer()
    {
        if (!_told && _gameServer == IntPtr.Zero && Bind())
            _gameServer = _accessor();
        return _gameServer;
    }

    /// <inheritdoc/>
    public bool HasGameServer => GameServer() != IntPtr.Zero;

    /// <summary>ISteamGameServer::GetGameplayStats, when there is a game server.</summary>
    public void RequestGameplayStats()
    {
        IntPtr server = GameServer();
        if (server != IntPtr.Zero)
            _getGameplayStats(server);
    }

    /// <summary>ISteamGameServer::BLoggedOn; false when there is no game server.</summary>
    public bool LoggedOn()
    {
        IntPtr server = GameServer();
        return server != IntPtr.Zero && _loggedOn(server) != 0;
    }
}
