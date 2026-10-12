//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Steam;

/// <summary>
/// The client's Steam callbacks (plan_client_novalve C6, Steam to managed;
/// lane c6-input): what stock's STEAM_CALLBACK members registered.
/// CClientSteamContext (game/client/clientsteamcontext.cpp) took the three
/// server-connection callbacks; CSecureLaunchSystem
/// (game/shared/secure_command_line.cpp) the URL launch and the rich-presence
/// join. Ids and payload sizes are the SDK headers' (isteamuser.h,
/// isteamapps.h, isteamfriends.h; callback structs packed to 4 on Linux), and
/// vtable_index_agreement_test holds them there.
/// </summary>
public sealed class ClientSteamCallbacks(ISteamCallbacks callbacks, ClientSteamContext context, SecureLaunch launch)
{
    public const string ServersConnected = "SteamServersConnected_t";
    public const string ServerConnectFailure = "SteamServerConnectFailure_t";
    public const string ServersDisconnected = "SteamServersDisconnected_t";
    public const string NewUrlLaunchParameters = "NewUrlLaunchParameters_t";
    public const string RichPresenceJoinRequested = "GameRichPresenceJoinRequested_t";

    // k_iSteamUserCallbacks = 100, k_iSteamAppsCallbacks = 1000,
    // k_iSteamFriendsCallbacks = 300.
    public const int ServersConnectedId = 100 + 1;
    public const int ServerConnectFailureId = 100 + 2;
    public const int ServersDisconnectedId = 100 + 3;
    public const int NewUrlLaunchParametersId = 1000 + 14;
    public const int RichPresenceJoinRequestedId = 300 + 37;

    /// <summary>
    /// Registers the five. Stock built the context's three with the context
    /// (a static, before Activate) and the launch system's two at the end of
    /// CHLClient::Init; here all five go when the module builds its root, after
    /// the context is active.
    /// </summary>
    public void RegisterAll()
    {
        // SteamServersConnected_t: no members (an empty struct is one byte).
        callbacks.Register(ServersConnected, ServersConnectedId, 1, _ => context.OnSteamServersConnected());
        // SteamServerConnectFailure_t: EResult, bool -- 8 bytes.
        callbacks.Register(ServerConnectFailure, ServerConnectFailureId, 8, _ => context.OnSteamServerConnectFailure());
        // SteamServersDisconnected_t: EResult -- 4 bytes.
        callbacks.Register(ServersDisconnected, ServersDisconnectedId, 4, _ => context.OnSteamServersDisconnected());
        // NewUrlLaunchParameters_t: no members.
        callbacks.Register(NewUrlLaunchParameters, NewUrlLaunchParametersId, 1, _ => launch.OnNewUrlLaunchParameters());
        // GameRichPresenceJoinRequested_t: CSteamID, char[256] -- 264 bytes.
        callbacks.Register(RichPresenceJoinRequested, RichPresenceJoinRequestedId, SecureLaunch.JoinRequestedBytes,
                           payload => launch.OnGameRichPresenceJoinRequested(payload));
    }
}
