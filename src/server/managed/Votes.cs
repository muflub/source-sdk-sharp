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
}

/// <summary>
/// Calling and answering a vote: <c>ss_callvote kick &lt;slot&gt;</c> and <c>ss_vote
/// &lt;1|2&gt;</c> over the level's vote_controller (made when a map brings none),
/// whose rules are stock's: quorum, its timer, one vote each and the caller's
/// cooldown. Every change goes to every player, so the count shows live; the host
/// and the server's admins cannot be voted off.
/// </summary>
public sealed class ServerVotes : IClientCommandHook, IOrderedHook
{
    private readonly VoteOffice _office;
    private readonly IEngineEntities _entities;
    private readonly IEnginePlayers _players;
    private readonly IEngineHost _host;
    private readonly IGameClock _clock;
    private readonly IUserMessageSender _messages;
    private readonly IAdminStatus? _admins;
    private readonly ILog? _log;

    public ServerVotes(VoteOffice office, IEngineEntities entities, IEnginePlayers players, IEngineHost host, IGameClock clock,
                       IUserMessageSender messages, IAdminStatus? admins = null, ILog? log = null)
    {
        _log = log;
        _office = office;
        _entities = entities;
        _players = players;
        _host = host;
        _clock = clock;
        _messages = messages;
        _admins = admins;
        office.Changed = Broadcast;
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
        if (args.Length != 2 || args[0] != "kick" || !int.TryParse(args[1], out int target))
            return;
        var targetPlayer = _players.GetHandle(target);
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
        if (target == slot || !_entities.IsValid(targetPlayer))
        {
            Refuse(slot, VoteRefusal.BadTarget, 0f);
            return;
        }
        if (IsImmune(target))
        {
            Refuse(slot, VoteRefusal.Immune, 0f);
            return;
        }

        _office.Caller = slot;
        _office.Target = target;
        var issue = new VoteIssue("kick", target.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                  $"kickid {_players.GetUserId(targetPlayer)} Voted off");
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
        bool sent = _messages.Send(State(vote, _office.Caller, _office.Target, _clock.Now), 0, reliable: true);
        _log?.Msg($"[SourceSharp/Vote] ballot {(sent ? "sent" : "NOT SENT")}: {vote.VotesCast} of {vote.PotentialVoters}\n");
    }

    /// <summary>What goes on the wire for a ballot as it stands.</summary>
    public static VoteState State(VoteController vote, int caller, int target, float now)
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
