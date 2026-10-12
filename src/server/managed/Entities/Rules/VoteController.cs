//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>What is being voted on, and what a pass means.</summary>
/// <param name="Name">The issue's short name, as a caller names it.</param>
/// <param name="Details">The argument, if the issue takes one — the map to change to.</param>
/// <param name="PassCommand">The server command a pass runs, or empty for a vote that only reports.</param>
/// <param name="TeamRestricted">Whether only the caller's team may vote and see it.</param>
/// <param name="Options">The choices, or null for the usual Yes/No.</param>
public readonly record struct VoteIssue(
    string Name,
    string Details,
    string PassCommand,
    bool TeamRestricted = false,
    string[]? Options = null);

/// <summary>Whether a ballot was accepted, and if not, why not.</summary>
public enum VoteCast
{
    Ok,

    /// <summary>Voting is switched off on this server.</summary>
    ServerDisabled,

    /// <summary>Nothing is being voted on.</summary>
    NoActiveIssue,

    /// <summary>The ballot is closed and the result already decided.</summary>
    Closed,

    /// <summary>An ally-only vote, and this voter is not on that team.</summary>
    TeamRestricted,

    /// <summary>This voter has already voted, and a vote cannot be changed.</summary>
    AlreadyVoted,

    /// <summary>No such choice on this ballot.</summary>
    BadOption,

    /// <summary>Nobody to record the vote against.</summary>
    NoSuchVoter,
}

/// <summary>How a finished vote came out.</summary>
public enum VoteOutcome
{
    /// <summary>Not finished, or nothing has been voted on yet.</summary>
    Undecided,
    Passed,
    Failed,
}

/// <summary>
/// Holds one vote at a time: who may vote, what the choices are, when the ballot
/// closes and whether it carried.
///
/// The decisions it owns are the ballot's, and they are all here: quorum against
/// the number of people who could have voted, the yes/no rule that a tie fails,
/// closing early once everybody has answered, the delay between a pass and the
/// command running, and the per-caller cooldown that stops one player calling
/// votes all round. Stock spreads those between `CVoteController` and
/// `CBaseIssue`; the split kept here is different and simpler — the controller
/// owns the ballot, and whoever starts a vote says what passing it means.
///
/// Nothing in this build starts one. `g_voteControllerGlobal` is assigned only by
/// TF's rules, no `CBaseIssue` subclass exists outside TF either, and the five
/// user messages the stock controller sends are registered only in
/// `tf_usermessages.cpp` — so the native entity has been an inert state machine
/// here since the mod began, and this is the same machine with its rules written
/// down where they can be read and tested. See docs/divergences.md.
/// </summary>
/// <summary>
/// Where vote indices come from: one sequence per server module, shared by every
/// vote_controller in it.
/// </summary>
public interface IVoteIndices
{
    /// <summary>The next index. Rises for the life of the module.</summary>
    int Next();
}

/// <inheritdoc cref="IVoteIndices"/>
public sealed class VoteIndices : IVoteIndices
{
    private int _next;

    /// <inheritdoc/>
    public int Next() => _next++;
}

[ScriptedEntity(EntityNames.VoteController)]
public sealed class VoteController : ScriptedEntity
{
    public VoteController(IGameClock clock, IEngineConsole console, IEngineHost host, IEnginePlayers players,
                          IVoteIndices indices)
    {
        _indices = indices;
        Clock = clock;
        Console = console;
        Host = host;
        Players = players;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    /// <summary>Stock's MAX_VOTE_OPTIONS. The wire format used to depend on it.</summary>
    private const int MaxOptions = 5;

    /// <summary>Seconds the result stays up before the controller goes idle again.</summary>
    private const float ResetDelay = 5f;

    /// <summary>How often the ballot is looked at. Stock's think interval.</summary>
    private const float ThinkInterval = 0.5f;

    /// <summary>A timer that is not running. Server time is never negative.</summary>
    private const float Never = -1f;

    private static readonly ImmutableArray<string> YesNo = [ "Yes", "No" ];

    /// <summary>
    /// Rises for the life of the server, so a client can tell two votes apart.
    /// The module's sequence: it was a static counter.
    /// </summary>
    private readonly IVoteIndices _indices;

    private VoteIssue? _issue;
    private IReadOnlyList<string> _options = YesNo;
    private readonly int[] _counts = new int[MaxOptions];

    // By entity handle rather than by SteamID, which is what stock keys on. See
    // docs/divergences.md: there is no SteamID on the managed side, so a voter
    // who reconnects mid-vote is a new voter here.
    private readonly Dictionary<uint, int> _castBy = new();
    private readonly Dictionary<uint, float> _callerCooldowns = new();

    private EntityHandle _holder = EntityHandle.None;
    private int _onlyTeam;
    private int _potentialVoters;
    private int _voteIndex = -1;

    private float _ballotClosesAt = Never;
    private float _executeAt = Never;
    private float _resetAt = Never;

    public override void OnSpawn()
    {
        Reset();
        SetNextThink(ThinkInterval);
    }

    public override void OnThink()
    {
        Tick(Clock.Now);
        SetNextThink(ThinkInterval);
    }

    //-------------------------------------------------------------------------
    // What the outside can see.
    //-------------------------------------------------------------------------

    /// <summary>Whether a vote is running or its result is still being shown.</summary>
    public bool IsVoteActive => _issue is not null;

    /// <summary>The issue being voted on, if there is one.</summary>
    public VoteIssue? ActiveIssue => _issue;

    /// <summary>This vote's serial number. -1 when nothing is running.</summary>
    public int VoteIndex => _voteIndex;

    /// <summary>
    /// Who called the vote. Kept because the caller is who a failure has to be
    /// explained to, and because a team-restricted vote is restricted to theirs.
    /// </summary>
    public EntityHandle Holder => _holder;

    /// <summary>How the last finished vote came out.</summary>
    public VoteOutcome Outcome { get; private set; } = VoteOutcome.Undecided;

    /// <summary>How many people could have voted when the ballot opened.</summary>
    public int PotentialVoters => _potentialVoters;

    /// <summary>Votes for one choice, numbered from 1 as a client sends them.</summary>
    public int VotesFor(int option)
        => (option >= 1 && option <= MaxOptions) ? _counts[option - 1] : 0;

    /// <summary>Votes cast on any choice.</summary>
    public int VotesCast
    {
        get
        {
            int total = 0;
            foreach (int count in _counts)
                total += count;

            return total;
        }
    }

    /// <summary>Whether this ballot is the two-choice kind, where a tie fails.</summary>
    public bool IsYesNoVote => _options.Count == 2;

    //-------------------------------------------------------------------------
    // Running a vote.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether a player may call a vote yet, and how long they must wait if not.
    ///
    /// The cooldown is per caller and survives the vote it was earned by, which
    /// is the point: the abuse it exists to stop is one player calling a fresh
    /// vote the moment the last one fails.
    /// </summary>
    public bool CanCallVote(EntityHandle caller, float now, out float wait)
    {
        wait = 0f;

        if (!_callerCooldowns.TryGetValue(caller.Value, out float until))
            return true;

        wait = until - now;
        if (wait > 0f)
            return false;

        _callerCooldowns.Remove(caller.Value);
        return true;
    }

    /// <summary>
    /// Opens a ballot, if voting is on, nothing else is running and this caller
    /// is not on cooldown.
    /// </summary>
    public bool StartVote(EntityHandle caller, VoteIssue issue, float now)
    {
        if (!VotesAllowed || IsVoteActive)
            return false;

        if (!CanCallVote(caller, now, out _))
            return false;

        _issue = issue;
        _options = (issue.Options is { Length: >= 2 }) ? issue.Options : YesNo;
        Array.Clear(_counts);
        _castBy.Clear();

        _holder = caller;

        // A vote called by something that is not a player -- the server itself
        // -- restricts nobody however the issue is marked, because there is no
        // team to restrict it to. Stock reaches the same place through its
        // dedicated-server special case.
        int callerTeam = Players.GetTeam(caller);
        _onlyTeam = (issue.TeamRestricted && callerTeam > TeamUnassigned) ? callerTeam : TeamUnassigned;
        _voteIndex = _indices.Next();
        _potentialVoters = CountPotentialVoters();
        Outcome = VoteOutcome.Undecided;

        // The jitter is stock's, and it is deliberate: every server in a cluster
        // closing its ballots on the same tick makes one very uneven frame.
        _ballotClosesAt = now + Number("sv_vote_timer_duration", 15f)
                              + Host.RandomFloat(-1f, 1f);
        _executeAt = Never;
        _resetAt = Never;

        _callerCooldowns[caller.Value] = now + Number("sv_vote_creation_timer", 150f);

        // Calling a yes/no vote is agreeing with it, unless the server lets the
        // caller vote against their own motion.
        if (IsYesNoVote && !Flag("sv_vote_holder_may_vote_no", false))
            Cast(caller, 1, now);

        return true;
    }

    /// <summary>
    /// Records one player's choice, numbered from 1.
    ///
    /// A choice past the end of a yes/no ballot is a No rather than an error,
    /// which is stock's rule and the safe way round: a client sending a stale
    /// option number must not be able to pass a vote by accident.
    /// </summary>
    public VoteCast Cast(EntityHandle voter, int option, float now)
    {
        if (!VotesAllowed)
            return VoteCast.ServerDisabled;

        if (_issue is null)
            return VoteCast.NoActiveIssue;

        // Once the result is in, the ballot is shut. Stock checks the same thing
        // by asking whether the execute timer has started.
        if (_ballotClosesAt < 0f)
            return VoteCast.Closed;

        if (voter == EntityHandle.None)
            return VoteCast.NoSuchVoter;

        if (_onlyTeam != TeamUnassigned && Players.GetTeam(voter) != _onlyTeam)
            return VoteCast.TeamRestricted;

        if (_castBy.ContainsKey(voter.Value))
            return VoteCast.AlreadyVoted;

        if (option < 1 || option > MaxOptions)
            return VoteCast.BadOption;

        if (IsYesNoVote && option > 2)
            option = 2;

        _counts[option - 1]++;
        _castBy[voter.Value] = option;

        if (Flag("sv_vote_timer_allow_early_finish", true) && VotesCast >= _potentialVoters)
        {
            // Run the timer out rather than deciding here. Stock does the same,
            // and the second of grace is what stops the result appearing before
            // the last vote has been drawn.
            _ballotClosesAt = now + 1f;
        }

        return VoteCast.Ok;
    }

    /// <summary>
    /// Moves the ballot on: closes it, decides it, runs what it decided, and
    /// eventually clears it. Takes the clock so the sequence can be tested
    /// without one.
    /// </summary>
    public void Tick(float now)
    {
        if (_issue is null)
            return;

        if (_ballotClosesAt >= 0f && now >= _ballotClosesAt)
        {
            _ballotClosesAt = Never;
            Decide(now);
        }

        if (_executeAt >= 0f && now >= _executeAt)
        {
            _executeAt = Never;

            string command = _issue.Value.PassCommand;
            if (command.Length > 0)
                Console.ExecuteServerCommand(command);
        }

        if (_resetAt >= 0f && now >= _resetAt)
            Reset();
    }

    private void Decide(float now)
    {
        int leading = LeadingOption(_counts, IsYesNoVote);
        bool passed = Passes(VotesCast, _potentialVoters, Number("sv_vote_quorum_ratio", 0.6f),
                             leading, IsYesNoVote);

        Outcome = passed ? VoteOutcome.Passed : VoteOutcome.Failed;

        if (passed)
        {
            // A vote with more than two choices adopts the winner as its detail,
            // so a caller that built its command from the detail gets the choice
            // the server actually made rather than the one it proposed.
            if (!IsYesNoVote && leading >= 0 && leading < _options.Count)
                _issue = _issue!.Value with { Details = _options[leading] };

            _executeAt = now + Number("sv_vote_command_delay", 2f);
        }

        _resetAt = now + ResetDelay;
    }

    private void Reset()
    {
        _issue = null;
        _options = YesNo;
        Array.Clear(_counts);
        _castBy.Clear();

        _holder = EntityHandle.None;
        _onlyTeam = TeamUnassigned;
        _potentialVoters = 0;
        _voteIndex = -1;

        _ballotClosesAt = Never;
        _executeAt = Never;
        _resetAt = Never;
    }

    //-------------------------------------------------------------------------
    // The rules, as arithmetic. Separate from the state so they can be read and
    // tested on their own -- a quorum that is one off looks like a vote system
    // that works, right up to the vote that matters.
    //-------------------------------------------------------------------------

    private const int TeamUnassigned = 0;
    private const int TeamSpectator = 1;

    /// <summary>
    /// The choice with the most votes, or -1 when there is nothing to lead.
    ///
    /// A yes/no ballot answers No on a tie, because it is asked as "shall we",
    /// and half the server not agreeing is not agreement.
    /// </summary>
    internal static int LeadingOption(int[] counts, bool yesNo)
    {
        if (yesNo)
            return counts[0] > counts[1] ? 0 : 1;

        int best = -1;
        for (int option = 0; option < counts.Length; option++)
        {
            if (counts[option] > 0 && (best < 0 || counts[option] > counts[best]))
                best = option;
        }

        return best;
    }

    /// <summary>
    /// Whether a ballot carried: enough of the room turned out, and the choice
    /// that won is one that means yes.
    ///
    /// The quorum is a count against a truncated fraction of the people who
    /// could have voted, which is stock's arithmetic including the truncation —
    /// on nine potential voters at the default ratio it takes five, not 5.4.
    /// </summary>
    internal static bool Passes(int votesCast, int potentialVoters, float quorumRatio,
                               int leadingOption, bool yesNo)
    {
        if (votesCast < (int)(potentialVoters * quorumRatio))
            return false;

        if (leadingOption < 0 || leadingOption >= MaxOptions)
            return false;

        // Yes is the first choice and nothing else will do. A ballot with more
        // choices has no "yes", so a quorum plus a winner is the whole test.
        return !yesNo || leadingOption == 0;
    }

    /// <summary>
    /// Whether somebody on this team gets a vote. Spectators are a server
    /// setting; nobody who has not picked a side gets one either way.
    /// </summary>
    internal static bool IsVoterTeam(int team, bool allowSpectators)
    {
        if (team <= TeamUnassigned)
            return false;

        return team != TeamSpectator || allowSpectators;
    }

    /// <summary>
    /// How many people could vote on the ballot about to open. Fixed slots for
    /// the same reason game_zone_player scans fixed slots — an empty one answers
    /// with an invalid handle, which costs a no-op rather than a crossing to ask
    /// how many there are.
    /// </summary>
    private int CountPotentialVoters()
    {
        bool allowSpectators = Flag("sv_vote_allow_spectators", false);
        int voters = 0;

        for (int slot = 1; slot <= MaxPlayerSlots; slot++)
        {
            var player = Players.GetHandle(slot);
            if (!Entities.IsValid(player))
                continue;

            int team = Players.GetTeam(player);
            if (IsVoterTeam(team, allowSpectators) && CanTeamCastVote(team))
                voters++;
        }

        return voters;
    }

    private const int MaxPlayerSlots = 64;

    /// <summary>Whether a team is allowed to vote on the current issue.</summary>
    private bool CanTeamCastVote(int team) => _onlyTeam == TeamUnassigned || team == _onlyTeam;

    //-------------------------------------------------------------------------
    // The sv_vote_* convars, read where they are used.
    //
    // Read from the engine rather than declared here, because vote_controller.cpp
    // registers all of them and stays in the build -- player.cpp needs its
    // symbols. Declaring them again in C# would be the same name twice.
    //-------------------------------------------------------------------------

    private bool VotesAllowed => Flag("sv_allow_votes", true);

    private bool Flag(string name, bool fallback)
        => ParseInt(Console.FindConVarValue(name) ?? string.Empty, fallback ? 1 : 0) != 0;

    private float Number(string name, float fallback)
        => ParseFloat(Console.FindConVarValue(name) ?? string.Empty, fallback);
}
