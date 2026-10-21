//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The local player's two observer fields as networked: stock's
/// <c>m_iObserverMode</c> (OBS_MODE_*) and <c>m_hObserverTarget</c>, a raw
/// entity handle whose "none" is <c>0xFFFFFFFF</c>, never 0.
/// </summary>
public interface IObserverFields
{
    int Mode { get; }

    uint Target { get; }
}

/// <summary>Not observing: what the client reads until the fields are networked.</summary>
public sealed class NoObserverFields : IObserverFields
{
    public int Mode => 0;

    public uint Target => EntityHandle.None.Value;
}

/// <summary>
/// The local player's observer fields as the server networks them
/// (PlayerNetState.ObserverMode / ObserverTarget, written by the server's
/// Observers): the local player found by its entity index, read when asked.
/// No local player (the menu, between levels) is "not observing".
/// </summary>
public sealed class LocalObserverFields(IClientStandalones standalones, Func<int> localIndex) : IObserverFields
{
    private Standalone.ClientSsPlayer? Local => standalones.GetByIndex(localIndex()) as Standalone.ClientSsPlayer;

    public int Mode => Local?.State.ObserverMode ?? 0;

    public uint Target => Local is { } p ? p.State.ObserverTarget : EntityHandle.None.Value;
}

/// <summary>A client index's name, team and health (MAX(0, health), as the player resource sends it).</summary>
public interface ISpectatedPlayers
{
    bool TryFind(int client, out string name, out int team, out int health);
}

/// <summary>
/// WHAT THE SPECTATOR SEES (stock CSpectatorGUI::Update, SpectatorGUI.cpp):
/// the mode and the map for the top bar; and, in the two modes that follow a
/// player (in-eye and chase, as stock's label showed only there), who that
/// player is, their team and their health. The map name is read when the
/// player starts observing, never per frame.
/// </summary>
public sealed class ObserverState(IObserverFields fields, ISpectatedPlayers players, Func<string?> levelName)
    : IObserverSource, ISpectatorTargetSource
{
    /// <summary>HL2MP's player maximum health.</summary>
    public const int PlayerMaxHealth = 100;

    private SpectatorMode _last;

    public SpectatorMode Mode
    {
        get
        {
            int raw = fields.Mode;
            var mode = raw is >= 0 and <= (int)SpectatorMode.Roaming ? (SpectatorMode)raw : SpectatorMode.None;
            if (mode != SpectatorMode.None && _last == SpectatorMode.None)
                MapName = Path.GetFileNameWithoutExtension(levelName() ?? string.Empty);
            _last = mode;
            return mode;
        }
    }

    public string MapName { get; private set; } = string.Empty;

    public bool HasTarget => Mode is SpectatorMode.InEye or SpectatorMode.Chase && Find(out _, out _, out _);

    public string Name => Find(out string name, out _, out _) ? name : string.Empty;

    public int Team => Find(out _, out int team, out _) ? team : 0;

    public int Health => Find(out _, out _, out int health) ? health : 0;

    public int MaxHealth => PlayerMaxHealth;

    /// <summary>Stock's spectator bar named no weapon, and the player resource carries none.</summary>
    public string WeaponName => string.Empty;

    private bool Find(out string name, out int team, out int health)
    {
        (name, team, health) = (string.Empty, 0, 0);
        uint raw = fields.Target;
        if (raw == EntityHandle.None.Value)
            return false;
        return players.TryFind(new EntityHandle(raw).EntryIndex, out name, out team, out health);
    }
}

/// <summary>A client index's name, team and health over the player resource and the engine's player info.</summary>
public sealed class SpectatedPlayers(IClientStandalones standalones, IPlayerNames names) : ISpectatedPlayers
{    private Standalone.ClientPlayerResource? _resource;
    private readonly string[] _names = new string[PlayerResourceNetState.Slots];
    private readonly int[] _nameUserIds = new int[PlayerResourceNetState.Slots];

    // A name is asked of the engine once per user in the slot, not each frame.
    public bool TryFind(int client, out string name, out int team, out int health)
    {
        (name, team, health) = (string.Empty, 0, 0);
        if (_resource is null || !standalones.Live().Contains(_resource))
            _resource = standalones.OfClass(EntityNames.PlayerManager).OfType<Standalone.ClientPlayerResource>().FirstOrDefault();
        if (_resource is not { } resource || client <= 0 || client >= PlayerResourceNetState.Slots
            || !resource.State.HasFlag(client, PlayerResourceNetState.FlagConnected))
            return false;
        team = resource.State.Team[client];
        health = resource.State.HasFlag(client, PlayerResourceNetState.FlagAlive) ? Math.Max(0, (int)resource.State.Health[client]) : 0;
        int userId = resource.State.UserId[client];
        if (_names[client] is null || _nameUserIds[client] != userId)
        {
            _names[client] = names.TryGet(client, out var info) ? info.Name : string.Empty;
            _nameUserIds[client] = userId;
        }
        name = _names[client];
        return true;
    }
}

/// <summary>
/// THE CLIENT'S spec_next, spec_prev, spec_mode AND spec_player (stock
/// SpectatorGUI.cpp's CON_COMMANDs and ForwardSpecCmdToServer): an observer's
/// request, forwarded to the server as stock sent it -- the bare name, or the
/// name and its one argument quoted; with more arguments, nothing. spec_player
/// wants exactly one argument and prints its usage otherwise. Nothing is sent
/// by a player who is not observing, nor during demo playback.
/// <see cref="Line"/> is the decision; <see cref="SpectatorCommandRunner"/>
/// carries it out for the four commands.
/// </summary>
public sealed class SpectatorCommands
{
    public const string PlayerUsage = "Usage: spec_player { steamid | #userid | partial name match }\n";

    /// <summary>
    /// The line to send the server for <paramref name="args"/> (the command's
    /// name first), or null for none; <paramref name="usage"/> when
    /// spec_player's usage is to be printed instead.
    /// </summary>
    public string? Line(ReadOnlySpan<string> args, SpectatorMode mode, bool playingDemo, out bool usage)
    {
        usage = false;
        if (args.Length == 0 || mode == SpectatorMode.None)
            return null;
        if (args[0] == "spec_player" && args.Length != 2)
        {
            usage = true;
            return null;
        }
        if (playingDemo)
            return null;
        return args.Length switch
        {
            1 => args[0],
            2 => $"{args[0]} \"{args[1]}\"",
            _ => null,
        };
    }
}

/// <summary>
/// The four spec_* commands carried out: the observer mode read now, the line
/// sent through the engine's ServerCmd (reliable, as stock's), spec_player's
/// usage printed. Demo playback is not asked: IsPlayingDemo is not bound, and
/// the engine has no server to send to while one plays.
/// </summary>
public sealed class SpectatorCommandRunner(SpectatorCommands decide, IObserverSource observer, ILog log,
                                           IFwdEngineClient engine)
{
    public void Run(string name, string args)
    {
        var parts = ShowInfo.Split(args);
        var all = new string[parts.Count + 1];
        all[0] = name;
        parts.CopyTo(all, 1);
        string? line = decide.Line(all, observer.Mode, playingDemo: false, out bool usage);
        if (usage)
            log.Msg(SpectatorCommands.PlayerUsage);
        if (line is not null)
            engine.SpectatorServerCommand(line);
    }
}

[ConCommand("spec_next", Help = "Spectate next player", Flags = ConVarFlags.ClientCmdCanExecute)]
public sealed class SpecNextCommand(SpectatorCommandRunner spec) : IConCommand
{
    public void Execute(string args) => spec.Run("spec_next", args);
}

[ConCommand("spec_prev", Help = "Spectate previous player", Flags = ConVarFlags.ClientCmdCanExecute)]
public sealed class SpecPrevCommand(SpectatorCommandRunner spec) : IConCommand
{
    public void Execute(string args) => spec.Run("spec_prev", args);
}

[ConCommand("spec_mode", Help = "Set spectator mode", Flags = ConVarFlags.ClientCmdCanExecute)]
public sealed class SpecModeCommand(SpectatorCommandRunner spec) : IConCommand
{
    public void Execute(string args) => spec.Run("spec_mode", args);
}

[ConCommand("spec_player", Help = "Spectate player by partial name, steamid, or userid", Flags = ConVarFlags.ClientCmdCanExecute)]
public sealed class SpecPlayerCommand(SpectatorCommandRunner spec) : IConCommand
{
    public void Execute(string args) => spec.Run("spec_player", args);
}

/// <summary>Stock's cl_spec_mode (SpectatorGUI.cpp): the observer mode the player asks the server for, sent as user info.</summary>
[ConVar("cl_spec_mode", "1", Flags = ConVarFlags.Archive | ConVarFlags.UserInfo | ConVarFlags.ServerCanExecute,
        Help = "spectator mode")]
public sealed class ClSpecModeConVar : IntConVar
{
}

/// <summary>Stock's spec_scoreboard (SpectatorGUI.cpp), kept for the configs that set it: the spectator bar's scoreboard toggle, which this UI has not.</summary>
[ConVar("spec_scoreboard", "0", Flags = ConVarFlags.Archive)]
public sealed class SpecScoreboardConVar : BoolConVar
{
}

