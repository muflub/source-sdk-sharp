using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>One server of Steam's list, the parts of gameserveritem_t the browser shows.</summary>
public readonly record struct SteamServerItem(
    uint Ip, ushort QueryPort, ushort ConnectionPort, int Ping, bool Responded, string GameDir, string Map,
    string Description, int Players, int MaxPlayers, int Bots, bool Password, bool Secure, string Name);

/// <summary>One of Steam's favourite or history servers for this game (ISteamMatchmaking::GetFavoriteGame).</summary>
public readonly record struct SteamFavourite(uint Ip, ushort ConnectionPort, ushort QueryPort, uint Flags);

/// <summary>
/// The server browser's Steam (stock's ServerBrowser.so asked the same):
/// ISteamMatchmakingServers' internet list, polled, and ISteamMatchmaking's
/// favourite and history servers. Unavailable without a running Steam (a
/// -hushsteam launch), where every list is empty and every change does nothing.
/// </summary>
public interface ISteamServerBrowser
{
    bool Available { get; }

    /// <summary>Starts the internet list for servers of <paramref name="gameDir"/>, dropping any earlier one.</summary>
    bool RequestInternet(string gameDir);

    bool Refreshing { get; }

    int ServerCount { get; }

    /// <summary>Whether list entry <paramref name="i"/> has answered (read without its strings).</summary>
    bool Responded(int i);

    bool TryGetServer(int i, out SteamServerItem item);

    void ReleaseRequest();

    int FavouriteCount { get; }

    bool TryGetFavourite(int i, out SteamFavourite favourite);

    void AddFavourite(uint ip, ushort connectionPort, ushort queryPort, uint flags);

    void RemoveFavourite(uint ip, ushort connectionPort, ushort queryPort, uint flags);
}

/// <summary>
/// The offsets of gameserveritem_t (matchmakingtypes.h) that <see cref="SteamServerBrowser"/>
/// reads: steam_server_item_layout_test holds them to the C++ declaration, and
/// SteamServerBrowserTests pins the same numbers here.
/// </summary>
public static class SteamServerItemLayout
{
    public const int ConnectionPort = 0, QueryPort = 2, Ip = 4, Ping = 8, HadSuccessfulResponse = 12, GameDir = 14, Map = 46,
                     Description = 78, Players = 148, MaxPlayers = 152, Bots = 156, Password = 160, Secure = 161, Name = 172;

    public const int GameDirBytes = 32, MapBytes = 32, DescriptionBytes = 64, NameBytes = 64;

    /// <summary>MatchMakingKeyValuePair_t: a 256-byte key, then a 256-byte value.</summary>
    public const int FilterKeyBytes = 256, FilterBytes = 512;

    /// <summary>A gameserveritem_t's fields from its bytes.</summary>
    public static unsafe SteamServerItem Read(byte* p)
        => new(*(uint*)(p + Ip), *(ushort*)(p + QueryPort), *(ushort*)(p + ConnectionPort), *(int*)(p + Ping),
               p[HadSuccessfulResponse] != 0, Vtable.TextOf(p + GameDir, GameDirBytes) ?? "", Vtable.TextOf(p + Map, MapBytes) ?? "",
               Vtable.TextOf(p + Description, DescriptionBytes) ?? "", *(int*)(p + Players), *(int*)(p + MaxPlayers),
               *(int*)(p + Bots), p[Password] != 0, p[Secure] != 0, Vtable.TextOf(p + Name, NameBytes) ?? "");
}

/// <summary>
/// <see cref="ISteamServerBrowser"/> through libsteam_api's flat API, the
/// engine's copy or none (<see cref="LoadedLibrary"/>), as
/// <see cref="SteamUserTickets"/> reaches Steam, so it never starts a Steam
/// context of its own. The list request's response object is built by
/// libss_fwd_client (SteamServerListFwd) and answers nothing: the list is
/// polled. The accessors are asked on every call, since Steam hands out new
/// ones after a reconnect; a null one is no Steam.
/// </summary>
public sealed unsafe class SteamServerBrowser : ISteamServerBrowser
{
    private readonly SteamServerListFwd* _fwd;
    private bool _bound;
    private delegate* unmanaged<IntPtr> _servers, _matchmaking, _utils;
    private delegate* unmanaged<IntPtr, uint> _appId;
    private delegate* unmanaged<IntPtr, uint, void**, uint, void*, IntPtr> _requestInternet;
    private delegate* unmanaged<IntPtr, IntPtr, byte> _isRefreshing;
    private delegate* unmanaged<IntPtr, IntPtr, int> _serverCount;
    private delegate* unmanaged<IntPtr, IntPtr, int, byte*> _serverDetails;
    private delegate* unmanaged<IntPtr, IntPtr, void> _release;
    private delegate* unmanaged<IntPtr, int> _favouriteCount;
    private delegate* unmanaged<IntPtr, int, uint*, uint*, ushort*, ushort*, uint*, uint*, byte> _favourite;
    private delegate* unmanaged<IntPtr, uint, uint, ushort, ushort, uint, uint, int> _addFavourite;
    private delegate* unmanaged<IntPtr, uint, uint, ushort, ushort, uint, byte> _removeFavourite;

    private void* _response;
    private void* _filter;
    private IntPtr _request;

    private SteamServerBrowser(SteamServerListFwd* fwd) => _fwd = fwd;

    /// <summary>The browser's Steam over the response table <paramref name="engine"/>'s factory serves; without the table only the internet list is lost.</summary>
    public static SteamServerBrowser Acquire(ModuleEngine engine, out string why)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new(Forwarders.TryAcquire(engine.Interfaces, out SteamServerListFwd* fwd, out why) ? fwd : null);
    }

    private bool Bind()
    {
        if (_bound)
            return true;
        IntPtr library = LoadedLibrary.Open(LoadedLibrary.SteamApi);
        if (library == IntPtr.Zero)
            return false;
        IntPtr Export(string name) => NativeLibrary.TryGetExport(library, name, out IntPtr f) ? f : IntPtr.Zero;
        IntPtr[] f =
        [
            Export(SteamApiExports.MatchmakingServersAccessor), Export(SteamApiExports.MatchmakingAccessor), Export(SteamApiExports.UtilsAccessor), Export("SteamAPI_ISteamUtils_GetAppID"),
            Export("SteamAPI_ISteamMatchmakingServers_RequestInternetServerList"), Export("SteamAPI_ISteamMatchmakingServers_IsRefreshing"),
            Export("SteamAPI_ISteamMatchmakingServers_GetServerCount"), Export("SteamAPI_ISteamMatchmakingServers_GetServerDetails"),
            Export("SteamAPI_ISteamMatchmakingServers_ReleaseRequest"), Export("SteamAPI_ISteamMatchmaking_GetFavoriteGameCount"),
            Export("SteamAPI_ISteamMatchmaking_GetFavoriteGame"), Export("SteamAPI_ISteamMatchmaking_AddFavoriteGame"),
            Export("SteamAPI_ISteamMatchmaking_RemoveFavoriteGame"),
        ];
        if (Array.IndexOf(f, IntPtr.Zero) >= 0)
            return false;
        _servers = (delegate* unmanaged<IntPtr>)f[0];
        _matchmaking = (delegate* unmanaged<IntPtr>)f[1];
        _utils = (delegate* unmanaged<IntPtr>)f[2];
        _appId = (delegate* unmanaged<IntPtr, uint>)f[3];
        _requestInternet = (delegate* unmanaged<IntPtr, uint, void**, uint, void*, IntPtr>)f[4];
        _isRefreshing = (delegate* unmanaged<IntPtr, IntPtr, byte>)f[5];
        _serverCount = (delegate* unmanaged<IntPtr, IntPtr, int>)f[6];
        _serverDetails = (delegate* unmanaged<IntPtr, IntPtr, int, byte*>)f[7];
        _release = (delegate* unmanaged<IntPtr, IntPtr, void>)f[8];
        _favouriteCount = (delegate* unmanaged<IntPtr, int>)f[9];
        _favourite = (delegate* unmanaged<IntPtr, int, uint*, uint*, ushort*, ushort*, uint*, uint*, byte>)f[10];
        _addFavourite = (delegate* unmanaged<IntPtr, uint, uint, ushort, ushort, uint, uint, int>)f[11];
        _removeFavourite = (delegate* unmanaged<IntPtr, uint, uint, ushort, ushort, uint, byte>)f[12];
        _bound = true;
        return true;
    }

    private IntPtr Servers => Bind() ? _servers() : IntPtr.Zero;

    private IntPtr Matchmaking => Bind() ? _matchmaking() : IntPtr.Zero;

    private uint AppId
    {
        get
        {
            IntPtr utils = Bind() ? _utils() : IntPtr.Zero;
            return utils == IntPtr.Zero ? 0 : _appId(utils);
        }
    }

    public bool Available => Servers != IntPtr.Zero && Matchmaking != IntPtr.Zero;

    public bool RequestInternet(string gameDir)
    {
        ReleaseRequest();
        IntPtr servers = Servers;
        if (servers == IntPtr.Zero || _fwd == null)
            return false;
        if (_response == null)
        {
            void* memory = NativeMemory.AllocZeroed((nuint)_fwd->SizeOf());
            _response = _fwd->Construct(memory);
            if (_response == null)
            {
                NativeMemory.Free(memory);
                return false;
            }
        }
        // One filter, as stock's internet tab sent: the game's directory.
        if (_filter == null)
            _filter = NativeMemory.AllocZeroed(SteamServerItemLayout.FilterBytes);
        var key = new Span<byte>(_filter, SteamServerItemLayout.FilterKeyBytes);
        var value = new Span<byte>((byte*)_filter + SteamServerItemLayout.FilterKeyBytes, SteamServerItemLayout.FilterKeyBytes);
        key.Clear();
        value.Clear();
        "gamedir"u8.CopyTo(key);
        System.Text.Encoding.UTF8.GetBytes(gameDir.AsSpan(0, Math.Min(gameDir.Length, 200)), value);
        void* filters = _filter;
        _request = _requestInternet(servers, AppId, &filters, 1, _response);
        return _request != IntPtr.Zero;
    }

    public bool Refreshing
    {
        get
        {
            IntPtr servers = Servers;
            return _request != IntPtr.Zero && servers != IntPtr.Zero && _isRefreshing(servers, _request) != 0;
        }
    }

    public int ServerCount
    {
        get
        {
            IntPtr servers = Servers;
            return _request == IntPtr.Zero || servers == IntPtr.Zero ? 0 : _serverCount(servers, _request);
        }
    }

    public bool Responded(int i)
    {
        IntPtr servers = Servers;
        if (_request == IntPtr.Zero || servers == IntPtr.Zero)
            return false;
        byte* p = _serverDetails(servers, _request, i);
        return p != null && p[SteamServerItemLayout.HadSuccessfulResponse] != 0;
    }

    public bool TryGetServer(int i, out SteamServerItem item)
    {
        item = default;
        IntPtr servers = Servers;
        if (_request == IntPtr.Zero || servers == IntPtr.Zero)
            return false;
        byte* p = _serverDetails(servers, _request, i);
        if (p == null)
            return false;
        item = SteamServerItemLayout.Read(p);
        return true;
    }

    public void ReleaseRequest()
    {
        if (_request == IntPtr.Zero)
            return;
        IntPtr servers = Servers;
        if (servers != IntPtr.Zero)
            _release(servers, _request);
        _request = IntPtr.Zero;
    }

    public int FavouriteCount
    {
        get
        {
            IntPtr mm = Matchmaking;
            return mm == IntPtr.Zero ? 0 : _favouriteCount(mm);
        }
    }

    public bool TryGetFavourite(int i, out SteamFavourite favourite)
    {
        favourite = default;
        IntPtr mm = Matchmaking;
        if (mm == IntPtr.Zero)
            return false;
        uint app, ip, flags, played;
        ushort conn, query;
        if (_favourite(mm, i, &app, &ip, &conn, &query, &flags, &played) == 0 || app != AppId)
            return false;
        favourite = new SteamFavourite(ip, conn, query, flags);
        return true;
    }

    public void AddFavourite(uint ip, ushort connectionPort, ushort queryPort, uint flags)
    {
        IntPtr mm = Matchmaking;
        if (mm != IntPtr.Zero)
            _addFavourite(mm, AppId, ip, connectionPort, queryPort, flags, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public void RemoveFavourite(uint ip, ushort connectionPort, ushort queryPort, uint flags)
    {
        IntPtr mm = Matchmaking;
        if (mm != IntPtr.Zero)
            _removeFavourite(mm, AppId, ip, connectionPort, queryPort, flags);
    }
}
