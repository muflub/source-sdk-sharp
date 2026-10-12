using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// <see cref="ISteamAuthTickets"/> through libsteam_api.so's flat C API, the
/// way SteamGameServerFlat reaches the game server: the engine's copy of the
/// library or none (<c>RTLD_NOLOAD</c>), so it can never start a second Steam
/// context. Bound on first use; the ISteamUser pointer is asked for on every
/// call, since Steam hands out a new one after a reconnect.
/// </summary>
public sealed unsafe class SteamUserTickets(ILog log) : ISteamAuthTickets
{
    public const string Library = "libsteam_api.so";

    /// <summary>The ISteamUser accessor THIS libsteam_api.so exports (it has no other).</summary>
    public const string AccessorExport = "SteamAPI_SteamUser_v021";

    /// <summary>Steam's advice for the buffer: a ticket is well under it.</summary>
    public const int MaxTicket = 1024;

    // sizeof(SteamNetworkingIdentity): m_eType, m_cbSize, then a 128-byte union.
    private const int IdentitySize = 136;

    private const int RTLD_NOW = 2;
    private const int RTLD_NOLOAD = 4;

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr DlOpen(string file, int mode);

    private bool _bound;
    private delegate* unmanaged<IntPtr> _user;
    private delegate* unmanaged<IntPtr, void*, int, uint*, void*, uint> _getTicket;
    private delegate* unmanaged<IntPtr, uint, void> _cancel;
    private delegate* unmanaged<void*, ulong, void> _setSteamId;
    private string _whyUnbound = "not asked yet";

    private bool Bind()
    {
        if (_bound)
            return true;

        IntPtr library = DlOpen(Library, RTLD_NOW | RTLD_NOLOAD);
        if (library == IntPtr.Zero)
        {
            _whyUnbound = Library + " is not loaded in this process (RTLD_NOLOAD): no Steam";
            return false;
        }

        if (!NativeLibrary.TryGetExport(library, AccessorExport, out IntPtr user) ||
            !NativeLibrary.TryGetExport(library, "SteamAPI_ISteamUser_GetAuthSessionTicket", out IntPtr getTicket) ||
            !NativeLibrary.TryGetExport(library, "SteamAPI_ISteamUser_CancelAuthTicket", out IntPtr cancel) ||
            !NativeLibrary.TryGetExport(library, "SteamAPI_SteamNetworkingIdentity_SetSteamID64", out IntPtr setSteamId))
        {
            _whyUnbound = Library + " does not export " + AccessorExport + " and the ISteamUser ticket functions";
            return false;
        }

        _user = (delegate* unmanaged<IntPtr>)user;
        _getTicket = (delegate* unmanaged<IntPtr, void*, int, uint*, void*, uint>)getTicket;
        _cancel = (delegate* unmanaged<IntPtr, uint, void>)cancel;
        _setSteamId = (delegate* unmanaged<void*, ulong, void>)setSteamId;
        _bound = true;
        return true;
    }

    public SteamAuthTicket? Create(ulong gameServerSteamId, out string why)
    {
        if (!Bind())
        {
            why = _whyUnbound;
            return null;
        }

        IntPtr user = _user();
        if (user == IntPtr.Zero)
        {
            why = "Steam has no user (not running, or not logged on)";
            return null;
        }

        byte* identity = stackalloc byte[IdentitySize];
        new Span<byte>(identity, IdentitySize).Clear();
        _setSteamId(identity, gameServerSteamId);

        var buffer = new byte[MaxTicket];
        uint length = 0;
        uint handle;
        fixed (byte* p = buffer)
            handle = _getTicket(user, p, buffer.Length, &length, identity);

        // k_HAuthTicketInvalid is 0.
        if (handle == 0 || length == 0 || length > MaxTicket)
        {
            why = $"GetAuthSessionTicket gave no ticket (handle {handle}, {length} bytes)";
            return null;
        }

        why = "";
        return new SteamAuthTicket(handle, buffer[..(int)length]);
    }

    public void Cancel(uint handle)
    {
        if (handle == 0 || !Bind())
            return;

        IntPtr user = _user();
        if (user == IntPtr.Zero)
        {
            log.Warning($"[SourceSharp/Client] no Steam user to cancel auth ticket {handle} with\n");
            return;
        }

        _cancel(user, handle);
    }
}
