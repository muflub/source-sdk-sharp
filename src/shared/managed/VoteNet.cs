using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>Where a ballot is: nothing on, open, or its result still showing.</summary>
public enum VotePhase : byte
{
    Idle,
    Voting,
    Passed,
    Failed,
}

/// <summary>What a ballot would do.</summary>
public enum VoteKind : byte
{
    Kick,
    Map,
    Mode,
    Restart,
    Setting,

    /// <summary>Reshuffle the players across the teams in play and restart the round (team deathmatch).</summary>
    Scramble,

    /// <summary>The map after this one (stock's <c>CNextLevelIssue</c>): sets <c>nextlevel</c>; with no map named, a choice among the candidates.</summary>
    NextMap,

    /// <summary>More time on this map (stock's <c>CExtendLevelIssue</c>).</summary>
    Extend,
}

/// <summary>Why a call was refused; <see cref="None"/> is a call accepted.</summary>
public enum VoteRefusal : byte
{
    None,

    /// <summary>A ballot is already open.</summary>
    InProgress,

    /// <summary>This caller called one lately: <see cref="VoteCooldown.Seconds"/> says how long is left.</summary>
    Cooldown,

    /// <summary>No such player, or the caller named themself.</summary>
    BadTarget,

    /// <summary>The server's host and its admins cannot be voted off.</summary>
    Immune,

    /// <summary>Voting is off on this server.</summary>
    Disabled,

    /// <summary>The issue cannot be called now; <see cref="VoteCooldown.Reason"/> says why (a mode this map cannot play).</summary>
    NotNow,

    /// <summary>A vote of this kind failed lately (<c>sv_vote_failure_timer</c>): <see cref="VoteCooldown.Seconds"/> until it may be called again.</summary>
    FailedLately,

    /// <summary>Fewer can vote than the server's <c>sv_vote_min_voters</c>; <see cref="VoteCooldown.Reason"/> gives the numbers.</summary>
    TooFewVoters,
}

/// <summary>The ballot as it stands, sent whenever it changes: who called it, whom it would kick, the count so far.</summary>
public partial struct VoteState
{
    public partial byte Phase { get; set; }

    /// <summary>The caller's and the target's player slots.</summary>
    public partial byte Caller { get; set; }

    public partial byte Target { get; set; }

    public partial byte Yes { get; set; }

    public partial byte No { get; set; }

    /// <summary>How many can vote on it.</summary>
    public partial byte Voters { get; set; }

    /// <summary>Seconds until the ballot closes, as of the send.</summary>
    public partial float SecondsLeft { get; set; }

    public partial byte Kind { get; set; }

    /// <summary>The map or mode it would change to; empty for a kick, whose target is a slot, and a restart.</summary>
    [UserMessageString(MaxLength = 64)]
    public partial string Detail { get; set; }

    /// <summary>A multiple-choice ballot's choices, one a line (at most <see cref="VoteWire.MaxOptions"/>); empty for Yes/No.</summary>
    [UserMessageString(MaxLength = 150)]
    public partial string Options { get; set; }

    /// <summary>The count for each choice, in order (<see cref="Yes"/> and <see cref="No"/> are the first two).</summary>
    [UserMessageArray(MaxCount = VoteWire.MaxOptions)]
    public partial byte[] Counts { get; set; }
}

/// <summary>The wire's limits for a ballot.</summary>
public static class VoteWire
{
    /// <summary>Stock's MAX_VOTE_OPTIONS.</summary>
    public const int MaxOptions = 5;
}

/// <summary>
/// A piece of what this server lets its players vote on, in the admin menu's text form
/// (<see cref="AdminMenuText"/>): each line's depth its <see cref="VoteKind"/>. Sent as a
/// player comes in, since the maps and modes are the server's to know.
/// </summary>
public partial struct VoteOptionsPiece
{
    public partial byte Index { get; set; }

    public partial byte Count { get; set; }

    [UserMessageArray(MaxCount = AdminMenuText.PieceBytes)]
    public partial byte[] Text { get; set; }
}

[UserMessage("VoteOptionsPiece")]
public sealed class VoteOptionsPieceMessage : IUserMessage<VoteOptionsPiece>
{
    private readonly IHooks<IVoteStateHook>? _hooks;

    public VoteOptionsPieceMessage()
    {
    }

    [Inject]
    public VoteOptionsPieceMessage(IHooks<IVoteStateHook>? hooks) => _hooks = hooks;

    public void OnReceived(VoteOptionsPiece message)
        => _hooks?.Each(nameof(IVoteStateHook.OnVoteOptionsPiece), hook => hook.OnVoteOptionsPiece(message));
}

/// <summary>What a vote's arrival tells the client.</summary>
public interface IVoteStateHook
{
    void OnVoteState(VoteState state);

    void OnVoteCooldown(VoteCooldown cooldown);

    void OnVoteOptionsPiece(VoteOptionsPiece piece);
}

[UserMessage("VoteState")]
public sealed class VoteStateMessage : IUserMessage<VoteState>
{
    private readonly IHooks<IVoteStateHook>? _hooks;

    /// <summary>The no-container fallback's, and the server's: a state goes nowhere.</summary>
    public VoteStateMessage()
    {
    }

    [Inject]
    public VoteStateMessage(IHooks<IVoteStateHook>? hooks) => _hooks = hooks;

    public void OnReceived(VoteState message)
        => _hooks?.Each(nameof(IVoteStateHook.OnVoteState), hook => hook.OnVoteState(message));
}

/// <summary>To a caller: why the call was refused, if it was, and how long until they may call again.</summary>
public partial struct VoteCooldown
{
    public partial byte Refusal { get; set; }

    public partial float Seconds { get; set; }

    /// <summary>Why, in words, where the refusal has more to say than its kind ("dm_lockdown has no item_teamflag").</summary>
    [UserMessageString(MaxLength = 120)]
    public partial string Reason { get; set; }
}

[UserMessage("VoteCooldown")]
public sealed class VoteCooldownMessage : IUserMessage<VoteCooldown>
{
    private readonly IHooks<IVoteStateHook>? _hooks;

    public VoteCooldownMessage()
    {
    }

    [Inject]
    public VoteCooldownMessage(IHooks<IVoteStateHook>? hooks) => _hooks = hooks;

    public void OnReceived(VoteCooldown message)
        => _hooks?.Each(nameof(IVoteStateHook.OnVoteCooldown), hook => hook.OnVoteCooldown(message));
}

/// <summary>The client's words for a vote, and the server's verbs for them.</summary>
public static class VoteVerbs
{
    /// <summary><c>ss_callvote kick &lt;slot&gt;</c>, or an issue's word and the id of its choice (<c>map dm_x</c>, <c>mode tdm</c>, <c>setting mp_fraglimit=20</c>), or the word alone (<c>restart</c>, <c>nextmap</c>).</summary>
    public const string Call = "ss_callvote";

    /// <summary><c>ss_vote 1</c> for yes, <c>ss_vote 2</c> for no; on a multiple-choice ballot the choice's number, 1 to 5.</summary>
    public const string Cast = "ss_vote";
}
