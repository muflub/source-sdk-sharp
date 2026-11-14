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

/// <summary>
/// One thing a server lets its players vote on, and the server command it runs if they do.
/// With a <paramref name="Ballot"/> of two to five choices it is a multiple-choice vote,
/// each choice running its own command when it wins.
/// </summary>
public sealed record VoteOption(VoteKind Kind, string Id, string Label, string Command, IReadOnlyList<VoteChoice>? Ballot = null)
{
    /// <summary>The share of the voters that must cast for it to pass; null for <c>sv_vote_quorum_ratio</c>.</summary>
    public float? Quorum { get; init; }
}

/// <summary>One choice of a multiple-choice vote: what the ballot calls it, and what it runs when it wins.</summary>
public sealed record VoteChoice(string Label, string Command);

/// <summary>What this server lets its players vote on now, besides a kick.</summary>
public interface IVoteOptions
{
    IReadOnlyList<VoteOption> Build();

    /// <summary>Why the listed <paramref name="choice"/> cannot be called now; null when it can.</summary>
    string? Refusal(VoteOption choice) => null;
}

/// <summary>The pieces the vote issues build their lists from (<see cref="IVoteIssue"/>).</summary>
public static class GameVoteOptions
{
    /// <summary>
    /// The settings players may vote on: the playing mode's rows marked votable
    /// and not the admin's alone. Another mode's rows are no choice: deathmatch
    /// offers no team count (a switch sends the list again).
    /// </summary>
    public static IEnumerable<SettingSpec> Votable(ModeSpec? mode)
        => mode?.Settings.Where(s => s.Votable && !s.AdminOnly) ?? [];

    /// <summary>
    /// What the menus call a mode: the core's two by name (the core's
    /// <c>"null"</c> is "Deathmatch", plan Q13; <c>tdm</c> is "Team
    /// Deathmatch"); a mod's mode by its id.
    /// </summary>
    public static string ModeLabel(string id) => id switch
    {
        GameModes.NullModeId => "#SS_UI_Mode_Deathmatch",
        TeamDeathmatchMode.ModeId => "#SS_UI_Mode_TeamDeathmatch",
        _ => id,
    };

    public static List<VoteOption> Of(IEnumerable<string> maps, IEnumerable<string> modes, string? activeMode, bool allowMaps, bool allowModes, bool allowRestart,
                                      IEnumerable<SettingSpec>? settings = null, Func<string, string?>? current = null,
                                      bool allowScramble = false)
    {
        var options = new List<VoteOption>();
        if (allowMaps)
            options.AddRange(maps.Distinct(StringComparer.OrdinalIgnoreCase).Select(m => new VoteOption(VoteKind.Map, m, m, $"changelevel {m}")));
        if (allowModes)
            options.AddRange(modes.Where(m => !string.Equals(m, activeMode, StringComparison.OrdinalIgnoreCase)).Select(m => new VoteOption(VoteKind.Mode, m, ModeLabel(m), $"sv_mode {m}")));
        if (allowRestart)
            options.Add(new VoteOption(VoteKind.Restart, VoteOptionWords.Restart, "", "mp_restartgame 1"));
        if (allowScramble)
            options.Add(new VoteOption(VoteKind.Scramble, VoteOptionWords.Scramble, "", TeamScrambleCommand.Name));
        foreach (var setting in settings ?? [])
        {
            // A convar this game does not have is no choice, nor the value it holds now.
            if (current?.Invoke(setting.Key) is not { } now)
                continue;
            foreach (var choice in setting.Choices)
                if (!ModeSettingWrites.SameValue(now, choice.Value))
                    options.Add(new VoteOption(VoteKind.Setting, $"{setting.Key}={choice.Value}", choice.Label,
                                               $"{ModeSettingCommand.Name} {setting.Key} {choice.Value}"));
        }
        return options;
    }
}

/// <summary>A call's words to the option it names: only what the server's own list holds, so a command is never the client's.</summary>
public static class VoteOptionWords
{
    public const string Map = "map", Mode = "mode", Restart = "restart", Setting = "setting", Scramble = "scramble",
                        NextMap = "nextmap", Extend = "extend";

    /// <summary>The word a call names <paramref name="kind"/> by; a choice with no id of its own carries the word as its id.</summary>
    public static string WordOf(VoteKind kind) => kind switch
    {
        VoteKind.Map => Map,
        VoteKind.Mode => Mode,
        VoteKind.Restart => Restart,
        VoteKind.Setting => Setting,
        VoteKind.Scramble => Scramble,
        VoteKind.NextMap => NextMap,
        VoteKind.Extend => Extend,
        _ => "",
    };

    /// <summary>
    /// The listed option <paramref name="args"/> name: a word and an id (<c>map dm_x</c>), or a
    /// word alone for the choice whose id is the word (<c>restart</c>, <c>nextmap</c> with no map).
    /// </summary>
    public static VoteOption? Find(IReadOnlyList<VoteOption> options, ReadOnlySpan<string> args)
    {
        if (args.Length is < 1 or > 2)
            return null;
        string word = args[0];
        string id = args.Length == 2 ? args[1] : word;
        return options.FirstOrDefault(o => o.Kind != VoteKind.Kick && WordOf(o.Kind) == word && o.Id == id);
    }
}

/// <summary>
/// Calling and answering a vote: <c>ss_callvote kick &lt;slot&gt;</c> and <c>ss_vote
/// &lt;1|2&gt;</c> over the level's vote_controller (made when a map brings none),
/// whose rules are stock's: quorum, its timer, one vote each and the caller's
/// cooldown. Every change goes to every player, so the count shows live; the host
/// and the server's admins cannot be voted off.
/// </summary>
public sealed class ServerVotes : IClientCommandHook, IOrderedHook, IClientLifecycle, IModeSwitchHook
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
    public void OnClientActive(int entIndex) => SendOptions(entIndex);

    /// <summary>A switch changes what can be voted on (the modes but the one on, its settings): everyone hears the new list.</summary>
    public void OnModeSwitched(IGameMode? previous, IGameMode next) => SendOptions(0);

    /// <summary>The vote list to one player, or to everyone at 0.</summary>
    private void SendOptions(int entIndex)
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
        if (!kick && _options!.Refusal(option!) is { } notNow)
        {
            Refuse(slot, VoteRefusal.NotNow, 0f, notNow);
            return;
        }
        string name = kick ? KickIssue : option!.Kind.ToString();
        if (vote.FailedLately(name, kick ? args[1] : option!.Id, now, out float failedWait))
        {
            int left = (int)MathF.Ceiling(failedWait);
            Refuse(slot, VoteRefusal.FailedLately, failedWait, $"wait {left / 60}:{left % 60:00}");
            return;
        }
        if (vote.TooFewVoters(out int eligible, out int needed))
        {
            Refuse(slot, VoteRefusal.TooFewVoters, 0f, $"{eligible} can vote here; this server needs {needed}");
            return;
        }

        _office.Caller = slot;
        _office.Target = kick ? target : 0;
        _office.Kind = kick ? VoteKind.Kick : option!.Kind;
        _office.Detail = kick ? "" : DetailOf(option!);
        var issue = kick
            ? new VoteIssue(KickIssue, target.ToString(System.Globalization.CultureInfo.InvariantCulture), $"kickid {_players.GetUserId(targetPlayer)} Voted off")
            : IssueOf(option!);
        if (vote.StartVote(caller, issue, now))
        {
            vote.CanCallVote(caller, now, out wait);
            _messages.Send(new VoteCooldown { Refusal = (byte)VoteRefusal.None, Seconds = wait, Reason = "" }, slot, reliable: true);
        }
    }

    /// <summary>Whether a vote is running now, or its result is still shown.</summary>
    public bool Running => _office.Current is { IsVoteActive: true };

    /// <summary>The admin's cancel: the ballot on now ends with no result. Answers null, or why not.</summary>
    public string? Cancel()
    {
        if (_office.Current is not { IsVoteActive: true } vote)
            return "no vote is running";
        vote.Cancel();
        _log?.Msg("[SourceSharp/Vote] the vote was cancelled by an admin\n");
        return null;
    }

    /// <summary>A kick's issue name on the controller (the other kinds go by their <see cref="VoteKind"/>).</summary>
    public const string KickIssue = "kick";

    /// <summary>The controller's issue for a listed choice: Yes/No, or its ballot's choices and their commands.</summary>
    public static VoteIssue IssueOf(VoteOption option)
        => option.Ballot is { Count: >= 2 } ballot
            ? new VoteIssue(option.Kind.ToString(), option.Id, option.Command,
                            Options: [.. ballot.Take(VoteWire.MaxOptions).Select(c => c.Label)],
                            OptionCommands: [.. ballot.Take(VoteWire.MaxOptions).Select(c => c.Command)],
                            QuorumRatio: option.Quorum)
            : new VoteIssue(option.Kind.ToString(), option.Id, option.Command, QuorumRatio: option.Quorum);

    /// <summary>What the ballot names as its subject: the map or mode it changes to; nothing for a choice or a kind with no subject.</summary>
    public static string DetailOf(VoteOption option)
        => option.Kind is VoteKind.Restart or VoteKind.Scramble or VoteKind.Extend || option.Ballot is not null ? "" : option.Id;

    /// <summary>
    /// A vote the server calls itself (stock's <c>CreateVote( DEDICATED_SERVER, ... )</c>):
    /// no caller, no cooldown, nobody's yes cast for it. Answers null when the ballot
    /// opened, else why not (votes off, another vote running).
    /// </summary>
    public string? CallAsServer(VoteOption option)
    {
        var vote = Controller();
        if (vote is null || !vote.VotesAllowed)
            return "votes are off on this server";
        if (vote.IsVoteActive)
            return "a vote is in progress";
        _office.Caller = 0;
        _office.Target = 0;
        _office.Kind = option.Kind;
        _office.Detail = DetailOf(option);
        if (!vote.StartVote(EntityHandle.None, IssueOf(option), _clock.Now))
            return "the vote could not start";
        _log?.Msg($"[SourceSharp/Vote] the server called {option.Kind} {option.Id}\n");
        return null;
    }

    /// <summary>A listen server's host (player 1 on one), or an admin.</summary>
    public bool IsImmune(int slot)
        => (!_host.IsDedicatedServer && slot == 1) || (_admins?.IsAdmin(slot) ?? false);

    private void Refuse(int slot, VoteRefusal why, float seconds, string reason = "")
    {
        _log?.Msg($"[SourceSharp/Vote] player {slot}'s call refused: {why}{(reason.Length > 0 ? ": " + reason : "")}\n");
        _messages.Send(new VoteCooldown { Refusal = (byte)why, Seconds = seconds, Reason = reason }, slot, reliable: true);
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
            Options = string.Join('\n', vote.Options),
            Counts = [.. Enumerable.Range(1, vote.IsYesNoVote ? 2 : vote.Options.Count).Select(o => (byte)vote.VotesFor(o))],
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
                                VoteAllowSpectatorsConVar allowSpectators, VoteMinVotersConVar minVoters,
                                VoteFailureTimerConVar failureTimer)
{
    public readonly AllowVotesConVar Allow = allow;
    public readonly VoteTimerDurationConVar TimerDuration = timerDuration;
    public readonly VoteCreationTimerConVar CreationTimer = creationTimer;
    public readonly VoteHolderMayVoteNoConVar HolderMayVoteNo = holderMayVoteNo;
    public readonly VoteEarlyFinishConVar EarlyFinish = earlyFinish;
    public readonly VoteQuorumRatioConVar QuorumRatio = quorumRatio;
    public readonly VoteCommandDelayConVar CommandDelay = commandDelay;
    public readonly VoteAllowSpectatorsConVar AllowSpectators = allowSpectators;
    public readonly VoteMinVotersConVar MinVoters = minVoters;
    public readonly VoteFailureTimerConVar FailureTimer = failureTimer;
}

[ConVar("sv_vote_issue_settings_allowed", "1", Help = "Can players call votes to change game settings (friendly fire, time limit and so on)?")]
public sealed class VoteIssueSettingsAllowedConVar : BoolConVar;

/// <summary>Stock TF2's <c>sv_vote_issue_scramble_teams_allowed</c> (tf_voteissues.cpp), for team deathmatch's scramble.</summary>
[ConVar("sv_vote_issue_scramble_teams_allowed", "1", Help = "Can players call votes to scramble the teams?")]
public sealed class VoteIssueScrambleTeamsAllowedConVar : BoolConVar;

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

/// <summary>
/// The fewest who must be able to vote for a call to be taken (plan_bots_teams_tf2
/// D.3, §8.2 Q2): 1 lets a listen server's lone human with bots pass a vote on
/// their own yes; a dedicated server's operator raises it so one stranger cannot.
/// </summary>
[ConVar("sv_vote_min_voters", "1", Min = 1, Help = "The fewest players who must be able to vote for a vote to be called")]
public sealed class VoteMinVotersConVar : IntConVar;

/// <summary>Stock TF2's: how long a failed issue waits before anyone may call it again.</summary>
[ConVar("sv_vote_failure_timer", "300", Help = "A vote that fails cannot be re-submitted for this long")]
public sealed class VoteFailureTimerConVar : FloatConVar;

[ConVar("sv_vote_issue_nextlevel_allowed", "1", Help = "Can players call votes to set the next level?")]
public sealed class VoteIssueNextLevelAllowedConVar : BoolConVar;

/// <summary>
/// Stock TF2's (tf_voteissues.cpp), default 1 here where stock's is 0 (plan
/// §8.2 Q12): a listen server with bots has no other way to pick the next map
/// without the console. On, the server calls a choice of the next map when a
/// level's round ends, and the issue's quorum is a tenth.
/// </summary>
[ConVar("sv_vote_issue_nextlevel_choicesmode", "1", Help = "Present players with a list of maps to choose the next one from at the end of a level?")]
public sealed class VoteIssueNextLevelChoicesModeConVar : BoolConVar;

[ConVar("sv_vote_issue_extendlevel_allowed", "1", Help = "Can players call votes to set the extend the current level?")]
public sealed class VoteIssueExtendLevelAllowedConVar : BoolConVar;
