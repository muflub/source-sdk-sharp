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
/// <item>anything else: no decision, and the shell asks the HUD elements
/// (weapon selection) before letting the engine run it.</item>
/// </list>
/// The chat key opened nothing before this: the fork's StartMessageMode lost
/// its CHudChat, and KeyInput still used the key.
/// </summary>
public sealed class ClientKeyInput(IChatOpener chat, IObserverSource observer, Func<IVtableEngineClient?> engine)
    : IClientKeyEvents
{
    public const int EngineRuns = 1, Used = 0, NoDecision = UiClientBinding.KeyNoDecision;

    private IVtableEngineClient? _engine;

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

    private void Run(SpectatorKeyCommand command)
    {
        _engine ??= engine();
        _engine?.SpectatorCommand(command);
    }
}
