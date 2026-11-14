//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// THE CLIENT MODE'S KEY DECISION (stock ClientModeShared::KeyInput and
/// HandleSpectatorKeyInput, clientmode_shared.cpp), which the shell asks for
/// every key the engine offers:
/// <list type="bullet">
/// <item>with the console up, the engine runs the binding;</item>
/// <item>messagemode / say opens the chat to everyone, messagemode2 / say_team
/// to the team (stock StartMessageMode: not with one player); messagemode3 /
/// say_party is used and does nothing (BCanSendPartyChatMessages is false);
/// all of them only on the press, and the key is used either way;</item>
/// <item>an observer past the death cam: +attack spec_next, +attack2
/// spec_prev, +jump spec_mode; +duck and +strafe are used (the spectator menu
/// and HLTV's director, neither built); each on the press;</item>
/// <item>the cancelselect key with a weapon selection open closes it and is
/// used (CBaseHudWeaponSelection::KeyInput);</item>
/// <item>anything else: no decision, and the engine runs it.</item>
/// </list>
/// The chat key opened nothing before this: the fork's StartMessageMode lost
/// its CHudChat, and KeyInput still used the key.
/// </summary>
public sealed class ClientKeyInput(IChatOpener chat, IObserverSource observer, IFwdEngineClient engine,
                                   WeaponSelectionHud? selection = null, IHooks<IHudMenuKeys>? hudMenus = null)
    : IClientKeyEvents
{
    public const int EngineRuns = 1, Used = 0, NoDecision = UiClientBinding.KeyNoDecision;

    public int KeyInput(bool down, ClientKeyBinding binding, bool consoleVisible, int maxClients)
    {
        if (consoleVisible)
            return EngineRuns;

        switch (binding)
        {
            case ClientKeyBinding.MessageMode or ClientKeyBinding.Say:
                if (down && maxClients != 1)
                    chat.Open(teamOnly: false);
                return Used;
            case ClientKeyBinding.MessageMode2 or ClientKeyBinding.SayTeam:
                if (down && maxClients != 1)
                    chat.Open(teamOnly: true);
                return Used;
            case ClientKeyBinding.MessageMode3 or ClientKeyBinding.SayParty:
                return Used;
        }

        if (observer.Mode > SpectatorMode.DeathCam && down && Spectator(binding))
            return Used;

        // The HUD elements: CBaseHudWeaponSelection::KeyInput closes an open
        // selection on the cancelselect key and uses it.
        if (binding == ClientKeyBinding.CancelSelect && selection is not null && selection.CancelKey())
            return Used;

        // The hud menus' own keys, the ask stock's client mode makes of each
        // element (ClientModeTFNormal::KeyInput's HudElementKeyInput walk,
        // clientmode_tf.cpp:1543-1553): the first that takes the key uses it.
        if (down && hudMenus is not null)
        {
            foreach (IHudMenuKeys menu in hudMenus.All)
                if (menu.Key(binding))
                    return Used;
        }

        return NoDecision;
    }

    // HandleSpectatorKeyInput: true when the key was the spectator's.
    private bool Spectator(ClientKeyBinding binding)
    {
        switch (binding)
        {
            case ClientKeyBinding.Attack: Run(SpectatorKeyCommand.Next); return true;
            case ClientKeyBinding.Attack2: Run(SpectatorKeyCommand.Prev); return true;
            case ClientKeyBinding.Jump: Run(SpectatorKeyCommand.Mode); return true;
            case ClientKeyBinding.Duck or ClientKeyBinding.Strafe: return true;
            default: return false;
        }
    }

    private void Run(SpectatorKeyCommand command) => engine.SpectatorCommand(command);
}

/// <summary>
/// One hud menu's own keys, the element half of stock's
/// <c>ClientModeTFNormal::KeyInput</c> walk (clientmode_tf.cpp:1543-1553, each
/// hud element's <c>HudElementKeyInput</c>): asked on every press the client
/// mode has no other answer for; taking the key uses it, so the engine never
/// runs the binding, and refusing lets it through. Only presses are asked —
/// stock's walk begins with the down check — and the console-visible and
/// spectator decisions stand ahead of it, stock's order.
/// </summary>
public interface IHudMenuKeys
{
    /// <summary>Whether this menu takes <paramref name="binding"/> now
    /// (stock's <c>return 0</c> arms); false leaves the key to the engine.</summary>
    bool Key(ClientKeyBinding binding);
}
