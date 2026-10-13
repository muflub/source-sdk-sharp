namespace SourceSharp.Abi;

/// <summary>
/// The interface accessors each platform's shipped Steam API exports, for the
/// flat-API bindings (SteamGameServerFlat, SteamUserTickets, SteamClientFlat).
///
/// <para>
/// WHY PER PLATFORM: the SDK ships two different Steamworks builds. Linux's
/// libsteam_api.so exports SteamAPI_SteamUser_v021 and
/// SteamAPI_SteamGameServer_v014 and no other version; Windows'
/// steam_api64.dll is newer and exports v023 and v015 instead. A flat
/// accessor exists only at the version its library was built with, so a
/// binding that asked Windows for v021 found nothing and Steam stayed unbound.
/// The functions the bindings call on those interfaces
/// (GetAuthSessionTicket with its identity, CancelAuthTicket, BLoggedOn,
/// GetGameplayStats, ...) take the same arguments in both builds, and each
/// takes the pointer its own library's accessor returned, so the pair is
/// consistent on either. ISteamUtils (v010) and ISteamApps (v008) are the same
/// on both.
/// </para>
/// </summary>
public static class SteamApiExports
{
    /// <summary>This platform's ISteamUser accessor.</summary>
    public static string UserAccessor => UserAccessorFor(OperatingSystem.IsWindows());

    /// <summary>Either platform's ISteamUser accessor, so both are testable.</summary>
    public static string UserAccessorFor(bool windows) => windows ? "SteamAPI_SteamUser_v023" : "SteamAPI_SteamUser_v021";

    /// <summary>This platform's ISteamGameServer accessor.</summary>
    public static string GameServerAccessor => GameServerAccessorFor(OperatingSystem.IsWindows());

    /// <summary>Either platform's ISteamGameServer accessor, so both are testable.</summary>
    public static string GameServerAccessorFor(bool windows) => windows ? "SteamAPI_SteamGameServer_v015" : "SteamAPI_SteamGameServer_v014";
}
