namespace SourceSharp.Abi;

/// <summary>
/// The client's reads and calls on Steam through libsteam_api.so's flat C API
/// (plan_client_novalve C6, Steam to managed; lane c6-input): what
/// CClientSteamContext and the name filter asked of steamapicontext.
/// </summary>
public interface ISteamClientApi
{
    /// <summary>Whether libsteam_api.so is in this process (the engine's copy) and exports what this needs.</summary>
    bool Available { get; }

    /// <summary><c>SteamAPI_InitSafe()</c>.</summary>
    bool InitSafe();

    /// <summary><c>SteamAPI_SetTryCatchCallbacks( b )</c>.</summary>
    void SetTryCatchCallbacks(bool tryCatch);

    /// <summary>Whether the context's SteamUser() and SteamUtils() both answer.</summary>
    bool HasUserAndUtils { get; }

    /// <summary><c>SteamUtils()-&gt;InitFilterText()</c>; false when there are no utils.</summary>
    bool InitFilterText();

    /// <summary><c>SteamUser()-&gt;BLoggedOn()</c>.</summary>
    bool LoggedOn { get; }

    /// <summary><c>SteamUser()-&gt;GetSteamID()</c>, as its 64-bit value.</summary>
    ulong SteamId { get; }

    /// <summary><c>SteamUtils()-&gt;GetConnectedUniverse()</c>.</summary>
    int Universe { get; }

    /// <summary><c>SteamUtils()-&gt;GetAppID()</c>.</summary>
    uint AppId { get; }

    /// <summary>
    /// <c>SteamUtils()-&gt;FilterText( context, steamID, in, out, size )</c> into a
    /// buffer of <paramref name="maxBytes"/>; null when there are no utils.
    /// </summary>
    string? FilterText(int context, ulong sourceSteamId, string text, int maxBytes);

    /// <summary><c>SteamAPI_RegisterCallback( pCallback, iCallback )</c>: Steam writes the block's flags and id.</summary>
    void RegisterCallback(nint callback, int callbackId);

    /// <summary><c>SteamAPI_UnregisterCallback( pCallback )</c>: Steam clears the block's registered flag.</summary>
    void UnregisterCallback(nint callback);

    /// <summary>
    /// <c>SteamApps()-&gt;GetLaunchCommandLine</c>, asked twice as stock asks it
    /// (the size, then the text): the text and the byte count the second call
    /// answered. Null when there are no apps.
    /// </summary>
    string? LaunchCommandLine(out int bytes);
}

/// <summary>
/// <see cref="ISteamClientApi"/> through libsteam_api.so's flat C API, the way
/// SteamUserTickets binds it: the engine's copy of the library or none
/// (<see cref="LoadedLibrary"/>), so it can never start a second Steam context. The
/// ISteamUser and ISteamUtils pointers are asked for on every call, since Steam
/// hands out new ones after a reconnect (the context's accessors did the same).
/// </summary>
public sealed unsafe class SteamClientFlat : ISteamClientApi
{
    public static string Library => LoadedLibrary.SteamApi;

    /// <summary>The accessors this platform's Steam API exports (<see cref="SteamApiExports"/>: ISteamUser's version differs).</summary>
    public static string UserAccessor => SteamApiExports.UserAccessor;
    public const string UtilsAccessor = "SteamAPI_SteamUtils_v010";
    public const string AppsAccessor = "SteamAPI_SteamApps_v008";


    private bool _asked;
    private bool _bound;
    private delegate* unmanaged<byte> _initSafe;
    private delegate* unmanaged<byte, void> _setTryCatch;
    private delegate* unmanaged<IntPtr> _user;
    private delegate* unmanaged<IntPtr> _utils;
    private delegate* unmanaged<IntPtr, uint, byte> _initFilterText;
    private delegate* unmanaged<IntPtr, int, ulong, byte*, byte*, uint, int> _filterText;
    private delegate* unmanaged<IntPtr, byte> _loggedOn;
    private delegate* unmanaged<IntPtr, ulong> _steamId;
    private delegate* unmanaged<IntPtr, int> _universe;
    private delegate* unmanaged<IntPtr, uint> _appId;
    private delegate* unmanaged<IntPtr, int, void> _register;
    private delegate* unmanaged<IntPtr, void> _unregister;
    private delegate* unmanaged<IntPtr> _apps;
    private delegate* unmanaged<IntPtr, byte*, int, int> _launchCommandLine;

    private bool Bind()
    {
        if (_asked)
            return _bound;

        _asked = true;
        IntPtr library = LoadedLibrary.Open(Library);
        if (library == IntPtr.Zero)
            return false;

        IntPtr Get(string name) => System.Runtime.InteropServices.NativeLibrary.TryGetExport(library, name, out IntPtr p) ? p : IntPtr.Zero;

        IntPtr initSafe = Get("SteamAPI_InitSafe"), setTryCatch = Get("SteamAPI_SetTryCatchCallbacks");
        IntPtr user = Get(UserAccessor), utils = Get(UtilsAccessor);
        IntPtr initFilter = Get("SteamAPI_ISteamUtils_InitFilterText"), filter = Get("SteamAPI_ISteamUtils_FilterText");
        IntPtr loggedOn = Get("SteamAPI_ISteamUser_BLoggedOn"), steamId = Get("SteamAPI_ISteamUser_GetSteamID");
        IntPtr universe = Get("SteamAPI_ISteamUtils_GetConnectedUniverse"), appId = Get("SteamAPI_ISteamUtils_GetAppID");
        IntPtr register = Get("SteamAPI_RegisterCallback"), unregister = Get("SteamAPI_UnregisterCallback");
        IntPtr apps = Get(AppsAccessor), launch = Get("SteamAPI_ISteamApps_GetLaunchCommandLine");
        if (initSafe == 0 || setTryCatch == 0 || user == 0 || utils == 0 || initFilter == 0 || filter == 0 ||
            loggedOn == 0 || steamId == 0 || universe == 0 || appId == 0 ||
            register == 0 || unregister == 0 || apps == 0 || launch == 0)
            return false;

        _initSafe = (delegate* unmanaged<byte>)initSafe;
        _setTryCatch = (delegate* unmanaged<byte, void>)setTryCatch;
        _user = (delegate* unmanaged<IntPtr>)user;
        _utils = (delegate* unmanaged<IntPtr>)utils;
        _initFilterText = (delegate* unmanaged<IntPtr, uint, byte>)initFilter;
        _filterText = (delegate* unmanaged<IntPtr, int, ulong, byte*, byte*, uint, int>)filter;
        _loggedOn = (delegate* unmanaged<IntPtr, byte>)loggedOn;
        _steamId = (delegate* unmanaged<IntPtr, ulong>)steamId;
        _universe = (delegate* unmanaged<IntPtr, int>)universe;
        _appId = (delegate* unmanaged<IntPtr, uint>)appId;
        _register = (delegate* unmanaged<IntPtr, int, void>)register;
        _unregister = (delegate* unmanaged<IntPtr, void>)unregister;
        _apps = (delegate* unmanaged<IntPtr>)apps;
        _launchCommandLine = (delegate* unmanaged<IntPtr, byte*, int, int>)launch;
        _bound = true;
        return true;
    }

    public bool Available => Bind();

    public bool InitSafe() => Bind() && _initSafe() != 0;

    public void SetTryCatchCallbacks(bool tryCatch)
    {
        if (Bind())
            _setTryCatch(tryCatch ? (byte)1 : (byte)0);
    }

    private IntPtr User => Bind() ? _user() : 0;

    private IntPtr Utils => Bind() ? _utils() : 0;

    public bool HasUserAndUtils => User != 0 && Utils != 0;

    public bool InitFilterText() => Utils is var u && u != 0 && _initFilterText(u, 0) != 0;

    public bool LoggedOn => User is var u && u != 0 && _loggedOn(u) != 0;

    public ulong SteamId => User is var u && u != 0 ? _steamId(u) : 0;

    public int Universe => Utils is var u && u != 0 ? _universe(u) : 0;

    public uint AppId => Utils is var u && u != 0 ? _appId(u) : 0;

    /// <summary>
    /// <c>SteamUtils()-&gt;FilterText( context, steamID, in, out, size )</c> into a
    /// buffer of <paramref name="maxBytes"/>; null when there are no utils (the
    /// caller then shows the name as it came, as stock does).
    /// </summary>
    public string? FilterText(int context, ulong sourceSteamId, string text, int maxBytes)
    {
        IntPtr u = Utils;
        if (u == 0)
            return null;

        byte[] input = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        byte* output = stackalloc byte[maxBytes];
        fixed (byte* p = input)
            _filterText(u, context, sourceSteamId, p, output, (uint)maxBytes);

        int length = 0;
        while (length < maxBytes && output[length] != 0)
            length++;
        return System.Text.Encoding.UTF8.GetString(output, length);
    }

    public void RegisterCallback(nint callback, int callbackId)
    {
        if (Bind() && callback != 0)
            _register(callback, callbackId);
    }

    public void UnregisterCallback(nint callback)
    {
        if (Bind() && callback != 0)
            _unregister(callback);
    }

    public string? LaunchCommandLine(out int bytes)
    {
        bytes = 0;
        IntPtr apps = Bind() ? _apps() : 0;
        if (apps == 0)
            return null;

        int size = _launchCommandLine(apps, null, 0);
        if (size <= 0)
            return string.Empty;

        var buffer = new byte[size];
        fixed (byte* p = buffer)
            bytes = _launchCommandLine(apps, p, size);

        int length = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length < 0 ? size : length);
    }
}
