//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Steam;

/// <summary>
/// Stock's CClientSteamContext (game/client/clientsteamcontext.cpp) in C#
/// (plan_client_novalve C6, Steam to managed; lane c6-input): activation --
/// SteamAPI_InitSafe, no try/catch around callbacks, the context, the name
/// filter's text -- and the logged-on state, refreshed by the three server
/// connection callbacks, with the local SteamID, universe and AppID cached the
/// first time it logs on.
/// </summary>
public sealed class ClientSteamContext(ISteamClientApi steam, ILog log)
{
    public bool Active { get; private set; }

    public bool LoggedOn { get; private set; }

    /// <summary>The local SteamID, cached when it first logged on (m_SteamIDLocalPlayer).</summary>
    public ulong SteamId { get; private set; }

    public int Universe { get; private set; }

    public uint AppId { get; private set; }

    public void Activate()
    {
        if (Active)
            return;

        Active = true;

        // ignore failure, that will fall out later when they don't get a valid logon cookie
        steam.InitSafe();
        // We don't use exceptions, so tell steam not to use try/catch in callback handlers
        steam.SetTryCatchCallbacks(false);

        // Init(): the context's accessors are the flat API's, asked per call.
        steam.InitFilterText();

        UpdateLoggedOnState();
        log.Msg($"CClientSteamContext logged on = {(LoggedOn ? 1 : 0)}\n");
    }

    public void Shutdown()
    {
        if (!Active)
            return;

        Active = false;
        LoggedOn = false;
    }

    private void UpdateLoggedOnState()
    {
        bool previous = LoggedOn;
        LoggedOn = steam.HasUserAndUtils && steam.LoggedOn;

        if (!previous && LoggedOn)
        {
            SteamId = steam.SteamId;
            Universe = steam.Universe;
            AppId = steam.AppId;
        }

        // Stock's InvokeCallbacks( loggedOnChange ): InstallCallback has no
        // caller in this build, so there is nobody to tell.
    }

    public void OnSteamServersDisconnected()
    {
        UpdateLoggedOnState();
        log.Msg($"CClientSteamContext OnSteamServersDisconnected logged on = {(LoggedOn ? 1 : 0)}\n");
    }

    public void OnSteamServerConnectFailure()
    {
        UpdateLoggedOnState();
        log.Msg($"CClientSteamContext OnSteamServerConnectFailure logged on = {(LoggedOn ? 1 : 0)}\n");
    }

    public void OnSteamServersConnected()
    {
        UpdateLoggedOnState();
        log.Msg($"CClientSteamContext OnSteamServersConnected logged on = {(LoggedOn ? 1 : 0)}\n");
    }
}
