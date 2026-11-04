//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>The level's vote_controller, and what it tells the players: the bridge between it and <see cref="ServerVotes"/>.</summary>
public sealed class VoteOffice
{
    public VoteController? Current { get; set; }

    /// <summary>The ballot changed; <see cref="ServerVotes"/> tells the players.</summary>
    public Action<VoteController>? Changed { get; set; }

    /// <summary>The kick target of the ballot on now, a player slot; 0 for none.</summary>
    public int Target { get; set; }

    public int Caller { get; set; }

    public VoteKind Kind { get; set; }

    /// <summary>The map or mode the ballot on now would change to.</summary>
    public string Detail { get; set; } = "";
}

/// <summary>One thing a server lets its players vote on, and the server command it runs if they do.</summary>
public sealed record VoteOption(VoteKind Kind, string Id, string Label, string Command);

/// <summary>What this server lets its players vote on now, besides a kick.</summary>
public interface IVoteOptions
{
    IReadOnlyList<VoteOption> Build();
}

/// <summary>A game setting players may vote on: its convar and the values on offer, each with what the menu calls it.</summary>
public sealed record VoteSetting(string ConVar, params (string Value, string Label)[] Values);

/// <summary>A mod's settings to vote on, in place of the core's: a mod knows which of them mean anything in its game.</summary>
public interface IVoteSettingSource
{
    IReadOnlyList<VoteSetting> VoteSettings { get; }
}

/// <summary>The maps of the map cycle, the modes registered but the one on, a restart, the game's settings; each by its convar.</summary>
public sealed class GameVoteOptions(IEngineFiles files, IEngineConsole console, ServiceContainer services,
                                    MapCycleFileConVar mapCycle, VoteIssueChangeLevelAllowedConVar changeLevel,
                                    VoteIssueModeAllowedConVar mode, VoteIssueRestartGameAllowedConVar restart,
                                    VoteIssueSettingsAllowedConVar settings) : IVoteOptions
{
    public IReadOnlyList<VoteOption> Build()
    {
        var modes = services.Resolve<GameModes>();
        return Of(Intermission.ReadCycle(files, mapCycle.Value),
                  modes is null ? [] : [.. modes.Registered.Select(m => m.Id)], modes?.ActiveId,
                  changeLevel.Value, mode.Value, restart.Value,
                  settings.Value ? SettingsOf(services) : [], console.FindConVarValue);
    }

    /// <summary>This game's settings: its mods' if any list them, else the core's.</summary>
    public static IEnumerable<VoteSetting> SettingsOf(ServiceContainer services)
    {
        var mods = services.ResolveAll<IVoteSettingSource>();
        return mods.Count > 0 ? mods.SelectMany(m => m.VoteSettings) : CoreSettings();
    }

    /// <summary>What every deathmatch has.</summary>
    public static VoteSetting[] CoreSettings() =>
    [
        FriendlyFire(),
        TimeLimit(),
        new("mp_fraglimit", ("0", "#SS_UI_Vote_NoLimit"), ("10", "10"), ("20", "20"), ("30", "30"), ("50", "50")),
    ];

    public static VoteSetting FriendlyFire() => new("mp_friendlyfire", ("0", "#SS_UI_Vote_Off"), ("1", "#SS_UI_Vote_On"));

    public static VoteSetting TimeLimit()
        => new("mp_timelimit", ("0", "#SS_UI_Vote_NoLimit"), ("10", "#SS_UI_Vote_Min10"), ("20", "#SS_UI_Vote_Min20"), ("30", "#SS_UI_Vote_Min30"), ("60", "#SS_UI_Vote_Min60"));

    public static List<VoteOption> Of(IEnumerable<string> maps, IEnumerable<string> modes, string? activeMode, bool allowMaps, bool allowModes, bool allowRestart,
                                      IEnumerable<VoteSetting>? settings = null, Func<string, string?>? current = null)
    {
        var options = new List<VoteOption>();
        if (allowMaps)
            options.AddRange(maps.Distinct(StringComparer.OrdinalIgnoreCase).Select(m => new VoteOption(VoteKind.Map, m, m, $"changelevel {m}")));
        if (allowModes)
            options.AddRange(modes.Where(m => !string.Equals(m, activeMode, StringComparison.OrdinalIgnoreCase)).Select(m => new VoteOption(VoteKind.Mode, m, m, $"sv_mode {m}")));
        if (allowRestart)
            options.Add(new VoteOption(VoteKind.Restart, VoteOptionWords.Restart, "", "mp_restartgame 1"));
        foreach (var setting in settings ?? [])
        {
            // A convar this game does not have is no choice, nor the value it holds now.
            if (current?.Invoke(setting.ConVar) is not { } now)
                continue;
            foreach (var (value, label) in setting.Values)
                if (!SameValue(now, value))
                    options.Add(new VoteOption(VoteKind.Setting, $"{setting.ConVar}={value}", label, $"{setting.ConVar} {value}"));
        }
        return options;
    }

    // Convars read back as they were written: "20" and "20.0" are one time limit.
    private static bool SameValue(string a, string b)
        => float.TryParse(a, System.Globalization.CultureInfo.InvariantCulture, out float x)
           && float.TryParse(b, System.Globalization.CultureInfo.InvariantCulture, out float y) ? x == y : a == b;
}

/// <summary>A call's words to the option it names: only what the server's own list holds, so a command is never the client's.</summary>
public static class VoteOptionWords
{
    public const string Map = "map", Mode = "mode", Restart = "restart", Setting = "setting";

    public static VoteOption? Find(IReadOnlyList<VoteOption> options, ReadOnlySpan<string> args)
    {
        if (args.Length == 1 && args[0] == Restart)
            return options.FirstOrDefault(o => o.Kind == VoteKind.Restart);
        if (args.Length != 2)
            return null;
        VoteKind? kind = args[0] switch { Map => VoteKind.Map, Mode => VoteKind.Mode, Setting => VoteKind.Setting, _ => null };
        string id = args[1];
        return kind is null ? null : options.FirstOrDefault(o => o.Kind == kind && o.Id == id);
    }
}

/// <summary>
/// Calling and answering a vote: <c>ss_callvote kick &lt;slot&gt;</c> and <c>ss_vote
/// &lt;1|2&gt;</c> over the level's vote_controller (made when a map brings none),
/// whose rules are stock's: quorum, its timer, one vote each and the caller's
/// cooldown. Every change goes to every player, so the count shows live; the host
/// and the server's admins cannot be voted off.
/// </summary>
public sealed class ServerVotes : IClientCommandHook, IOrderedHook, IClientLifecycle
{
    private readonly VoteOffice _office;
    private readonly IEngineEntities _entities;
    private readonly IEnginePlayers _players;
    private readonly IEngineHost _host;
    private readonly IGameClock _clock;
    private readonly IUserMessageSender _messages;
    private readonly IAdminStatus? _admins;
    private readonly IVoteOptions? _options;
    private readonly ILog? _log;

    public ServerVotes(VoteOffice office, IEngineEntities entities, IEnginePlayers players, IEngineHost host, IGameClock clock,
                       IUserMessageSender messages, IAdminStatus? admins = null, ILog? log = null, IVoteOptions? options = null)
    {
        _log = log;
        _options = options;
        _office = office;
        _entities = entities;
        _players = players;
        _host = host;
        _clock = clock;
        _messages = messages;
        _admins = admins;
        office.Changed = Broadcast;
    }

    public int ClientLifecycleOrder => (int)LifecycleStage.Votes;

    /// <summary>A player in the game (each level, so a new map's list goes too) hears what can be voted on.</summary>
    public void OnClientActive(int entIndex)
    {
        if (_options is null)
            return;
        var lines = _options.Build().Select(o => new AdminMenuLine((int)o.Kind, o.Id, o.Label));
        var pieces = AdminMenuText.Pieces(AdminMenuText.Write(lines));
        for (int i = 0; i < pieces.Count; i++)
            _messages.Send(new VoteOptionsPiece { Index = (byte)i, Count = (byte)pieces.Count, Text = pieces[i] }, entIndex, reliable: true);
    }

    /// <summary>Among the mods' own words, which never name these.</summary>
    public int HookOrder => 999_100;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (command == VoteVerbs.Call)
        {
            _log?.Msg($"[SourceSharp/Vote] player {player.EntryIndex}: {command} {string.Join(' ', args.ToArray())}\n");
            Call(player, args);
            return true;
        }
        if (command == VoteVerbs.Cast)
        {
            if (args.Length == 1 && int.TryParse(args[0], out int option) && _office.Current is { } vote)
                vote.Cast(player, option, _clock.Now);
            return true;
        }
        return false;
    }

    private void Call(EntityHandle caller, ReadOnlySpan<string> args)
    {
        int slot = caller.EntryIndex;
        bool kick = args.Length == 2 && args[0] == "kick";
        int target = 0;
        if (kick && !int.TryParse(args[1], out target))
            return;
        VoteOption? option = kick || _options is null ? null : VoteOptionWords.Find(_options.Build(), args);
        if (!kick && option is null && args.Length == 0)
            return;

        var targetPlayer = kick ? _players.GetHandle(target) : EntityHandle.None;
        var vote = Controller();
        float now = _clock.Now;

        if (vote is null || !vote.VotesAllowed)
        {
            Refuse(slot, VoteRefusal.Disabled, 0f);
            return;
        }
        if (vote.IsVoteActive)
        {
            Refuse(slot, VoteRefusal.InProgress, 0f);
            return;
        }
        if (!vote.CanCallVote(caller, now, out float wait))
        {
            Refuse(slot, VoteRefusal.Cooldown, wait);
            return;
        }
        if (kick ? target == slot || !_entities.IsValid(targetPlayer) : option is null)
        {
            Refuse(slot, VoteRefusal.BadTarget, 0f);
            return;
        }
        if (kick && IsImmune(target))
        {
            Refuse(slot, VoteRefusal.Immune, 0f);
            return;
        }

        _office.Caller = slot;
        _office.Target = kick ? target : 0;
        _office.Kind = kick ? VoteKind.Kick : option!.Kind;
        _office.Detail = kick || option!.Kind == VoteKind.Restart ? "" : option.Id;
        var issue = kick
            ? new VoteIssue("kick", target.ToString(System.Globalization.CultureInfo.InvariantCulture), $"kickid {_players.GetUserId(targetPlayer)} Voted off")
            : new VoteIssue(option!.Kind.ToString(), option.Id, option.Command);
        if (vote.StartVote(caller, issue, now))
        {
            vote.CanCallVote(caller, now, out wait);
            _messages.Send(new VoteCooldown { Refusal = (byte)VoteRefusal.None, Seconds = wait }, slot, reliable: true);
        }
    }

    /// <summary>A listen server's host (player 1 on one), or an admin.</summary>
    public bool IsImmune(int slot)
        => (!_host.IsDedicatedServer && slot == 1) || (_admins?.IsAdmin(slot) ?? false);

    private void Refuse(int slot, VoteRefusal why, float seconds)
    {
        _log?.Msg($"[SourceSharp/Vote] player {slot}'s call refused: {why}\n");
        _messages.Send(new VoteCooldown { Refusal = (byte)why, Seconds = seconds }, slot, reliable: true);
    }

    private VoteController? Controller()
    {
        if (_office.Current is null)
            _entities.Spawn(_entities.Create(EntityNames.VoteController));
        return _office.Current;
    }

    private void Broadcast(VoteController vote)
    {
        bool sent = _messages.Send(State(vote, _office.Caller, _office.Target, _clock.Now, _office.Kind, _office.Detail), 0, reliable: true);
        _log?.Msg($"[SourceSharp/Vote] ballot {(sent ? "sent" : "NOT SENT")}: {vote.VotesCast} of {vote.PotentialVoters}\n");
    }

    /// <summary>What goes on the wire for a ballot as it stands.</summary>
    public static VoteState State(VoteController vote, int caller, int target, float now, VoteKind kind = VoteKind.Kick, string detail = "")
    {
        var phase = !vote.IsVoteActive ? VotePhase.Idle
            : vote.Outcome == VoteOutcome.Passed ? VotePhase.Passed
            : vote.Outcome == VoteOutcome.Failed ? VotePhase.Failed
            : VotePhase.Voting;
        return new VoteState
        {
            Phase = (byte)phase,
            Caller = (byte)caller,
            Target = (byte)target,
            Yes = (byte)vote.VotesFor(1),
            No = (byte)vote.VotesFor(2),
            Voters = (byte)vote.PotentialVoters,
            SecondsLeft = vote.SecondsLeft(now),
            Kind = (byte)kind,
            Detail = detail,
        };
    }
}

/// <summary>Whether a player holds the server's admin rights (the host, or a key the server knows).</summary>
public interface IAdminStatus
{
    bool IsAdmin(int slot);
}

// Stock's sv_vote_* (vote_controller.cpp), the ones a ballot here reads: that file is
// no longer built, so they are declared here for a server's cfg to set.

[ConVar("sv_vote_issue_changelevel_allowed", "1", Help = "Can players call votes to change the map?")]
public sealed class VoteIssueChangeLevelAllowedConVar : BoolConVar;

[ConVar("sv_vote_issue_mode_allowed", "1", Help = "Can players call votes to change the game mode?")]
public sealed class VoteIssueModeAllowedConVar : BoolConVar;

/// <summary>The vote convars a vote controller reads.</summary>
public sealed class VoteConVars(AllowVotesConVar allow, VoteTimerDurationConVar timerDuration, VoteCreationTimerConVar creationTimer,
                                VoteHolderMayVoteNoConVar holderMayVoteNo, VoteEarlyFinishConVar earlyFinish,
                                VoteQuorumRatioConVar quorumRatio, VoteCommandDelayConVar commandDelay,
                                VoteAllowSpectatorsConVar allowSpectators, VoteBotsAllowedConVar botsAllowed)
{
    public readonly AllowVotesConVar Allow = allow;
    public readonly VoteTimerDurationConVar TimerDuration = timerDuration;
    public readonly VoteCreationTimerConVar CreationTimer = creationTimer;
    public readonly VoteHolderMayVoteNoConVar HolderMayVoteNo = holderMayVoteNo;
    public readonly VoteEarlyFinishConVar EarlyFinish = earlyFinish;
    public readonly VoteQuorumRatioConVar QuorumRatio = quorumRatio;
    public readonly VoteCommandDelayConVar CommandDelay = commandDelay;
    public readonly VoteAllowSpectatorsConVar AllowSpectators = allowSpectators;
    public readonly VoteBotsAllowedConVar BotsAllowed = botsAllowed;
}

[ConVar("sv_vote_issue_settings_allowed", "1", Help = "Can players call votes to change game settings (friendly fire, time limit and so on)?")]
public sealed class VoteIssueSettingsAllowedConVar : BoolConVar;

[ConVar("sv_vote_issue_restart_game_allowed", "1", Help = "Can players call votes to restart the game?")]
public sealed class VoteIssueRestartGameAllowedConVar : BoolConVar;

[ConVar("sv_allow_votes", "1", Help = "Allow voting?")]
public sealed class AllowVotesConVar : BoolConVar;

[ConVar("sv_vote_timer_duration", "15", Help = "How long to allow voting on an issue")]
public sealed class VoteTimerDurationConVar : FloatConVar;

[ConVar("sv_vote_timer_allow_early_finish", "1", Help = "If all votes are in, whether to end the vote (for debugging)")]
public sealed class VoteEarlyFinishConVar : BoolConVar;

[ConVar("sv_vote_command_delay", "2", Flags = ConVarFlags.DevelopmentOnly, Help = "How long after a vote passes until the action happens")]
public sealed class VoteCommandDelayConVar : FloatConVar;

[ConVar("sv_vote_creation_timer", "150", Help = "How long before a player can attempt to call another vote (in seconds).")]
public sealed class VoteCreationTimerConVar : FloatConVar;

[ConVar("sv_vote_quorum_ratio", "0.6", Flags = ConVarFlags.Notify, Help = "The minimum ratio of eligible players needed to pass a vote.  Min 0.1, Max 1.0.")]
public sealed class VoteQuorumRatioConVar : FloatConVar;

[ConVar("sv_vote_allow_spectators", "0", Help = "Allow spectators to vote?")]
public sealed class VoteAllowSpectatorsConVar : BoolConVar;

[ConVar("sv_vote_holder_may_vote_no", "0", Help = "1 = Vote caller is not forced to vote yes on yes/no votes.")]
public sealed class VoteHolderMayVoteNoConVar : BoolConVar;

[ConVar("sv_vote_bots_allowed", "0", Help = "Allow bots to vote or not.")]
public sealed class VoteBotsAllowedConVar : BoolConVar;
