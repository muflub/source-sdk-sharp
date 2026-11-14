using SourceSharp.Abi;

using System.Collections.Immutable;

namespace TF2.Descent.Server.Party;

//=============================================================================
// THE PARTY (plan_descent_logic WO9; plan_tf2_rpg R8, D8, D10, R26). Up to the
// rules' size of heroes who share a level, its loot and XP, its revives, its
// map and its portals. A hero is in at most one party. A party names its
// members by member id: unhosted the hero's ledger name (HeroKeys), hosted the
// host's character id, so a member who is not on this server is still named.
// The verbs take the acting hero's ledger name: he is on this server.
//=============================================================================

/// <summary>
/// One party: its id, its leader and members (member ids, in the order they joined, the leader
/// first when he formed it), the heroes invited and not yet answered (member ids too), and,
/// hosted, the instance the party is playing now (the host's <c>LiveInstance</c>; empty when none, and
/// always unhosted).
/// </summary>
public sealed record PartyInfo(string Id, string Leader, ImmutableArray<string> Members, ImmutableArray<string> Invited, string LiveInstance = "")
{
    public int Size => Members.Length;

    public bool Has(string member) => Members.Contains(member);

    public bool Invites(string member) => Invited.Contains(member);
}

/// <summary>Why a party verb was refused: a stable code, the notice token's last word (<c>#Descent_Party_&lt;code&gt;</c>).</summary>
public static class PartyRefusals
{
    /// <summary>The acting hero is not a hero here (no ledger name yet).</summary>
    public const string NotAHero = "NotAHero";

    /// <summary>The hero is in a party already (create, accept).</summary>
    public const string InParty = "InParty";

    /// <summary>The hero is in no party (leave, kick, disband).</summary>
    public const string NotInParty = "NotInParty";

    /// <summary>Only the leader may do that (invite, kick, disband).</summary>
    public const string NotLeader = "NotLeader";

    /// <summary>The party has the rules' most members.</summary>
    public const string Full = "Full";

    /// <summary>The invited hero is in a party (he must leave it first).</summary>
    public const string TargetInParty = "TargetInParty";

    /// <summary>The invited hero is invited already.</summary>
    public const string AlreadyInvited = "AlreadyInvited";

    /// <summary>The hero has no invitation to answer.</summary>
    public const string NoInvite = "NoInvite";

    /// <summary>The kicked hero is not a member.</summary>
    public const string NotAMember = "NotAMember";

    /// <summary>A hero cannot invite or kick himself.</summary>
    public const string Self = "Self";

    /// <summary>The named player is not a hero on this server.</summary>
    public const string NoSuchPlayer = "NoSuchPlayer";

    /// <summary>A downed member cannot be kicked (it would kill him).</summary>
    public const string TargetDown = "TargetDown";

    /// <summary>The hosting service has no call for the verb (SDK 2.6.0: decline, disband); the reason says which.</summary>
    public const string HostUnsupported = "HostUnsupported";

    /// <summary>The hosting service refused; the reason carries its error and reason.</summary>
    public const string Host = "Host";
}

/// <summary>What a verb came to: done (with the party as it now is, null when it is gone), or refused with a code and a reason.</summary>
public sealed record PartyAnswer(bool Ok, string Refusal, string Reason, PartyInfo? Party)
{
    public static PartyAnswer Done(PartyInfo? party) => new(true, "", "", party);

    public static PartyAnswer Refused(string refusal, string reason = "") => new(false, refusal, reason, null);

    public static Task<PartyAnswer> RefusedTask(string refusal, string reason = "") => Task.FromResult(Refused(refusal, reason));
}

/// <summary>What changed in a party: told to the <see cref="IPartyHook"/> chain once per change.</summary>
public enum PartyChangeKind
{
    /// <summary>A party was made: <see cref="PartyChange.Member"/> leads it.</summary>
    Formed,

    /// <summary>The member joined (an accepted invitation, or a dev make).</summary>
    Joined,

    /// <summary>The member left (his own leave, a disconnect unhosted).</summary>
    Left,

    /// <summary>The leader put the member out.</summary>
    Kicked,

    /// <summary>The member leads now.</summary>
    LeaderChanged,

    /// <summary>The member was invited.</summary>
    Invited,

    /// <summary>The member's invitation is gone without his joining (declined, or the party ended).</summary>
    Declined,

    /// <summary>The party is gone; <see cref="PartyChange.Party"/> is as it was last, and each member in it is in none now.</summary>
    Disbanded,
}

/// <summary>One change: what, to which party, which member it names, and the party after it (before it, for a disband).</summary>
public readonly record struct PartyChange(PartyChangeKind Kind, string PartyId, string Member, PartyInfo Party);

/// <summary>Told each party change, in order (a chain, <see cref="PartyRegistrar"/>): the client views, the automap, any later listener.</summary>
public interface IPartyHook
{
    void OnPartyChanged(in PartyChange change);
}

/// <summary>
/// THE PARTIES (WO9): create, invite, accept or decline, leave, kick, disband, at most the
/// rules' size, one party a hero, a leader. The in-memory parties serve an unhosted server
/// (<see cref="InMemoryParties"/>), the host's a hosted one (<see cref="HostedParties"/>), chosen
/// by <see cref="Parties"/> as the item ledger's two are. Every change is told to the
/// <see cref="IPartyHook"/> chain.
/// </summary>
public interface IParties
{
    /// <summary>The most members a party may have (robot_rules' <c>party.maxMembers</c>).</summary>
    int MaxSize { get; }

    /// <summary>Every party known here.</summary>
    IReadOnlyList<PartyInfo> All { get; }

    /// <summary>The member id of the hero (his ledger name unhosted; his character id hosted, null before his join answered).</summary>
    string? MemberId(string hero);

    /// <summary>The ledger name of the member when he is a hero on this server; null when he is not here.</summary>
    string? HeroOf(string member);

    /// <summary>The party of the hero (by ledger name); null when he is in none.</summary>
    PartyInfo? Of(string hero);

    /// <summary>The parties that have invited the hero, the newest last.</summary>
    IReadOnlyList<PartyInfo> InvitesOf(string hero);

    /// <summary>The name, class and level of a member who is not on this server, when known (hosted, from the host); null otherwise.</summary>
    HostMemberCard? Card(string member);

    /// <summary>A party led by the hero, of him alone.</summary>
    Task<PartyAnswer> Create(string hero);

    /// <summary>The leader invites <paramref name="target"/> (both ledger names); a hero in no party makes one first.</summary>
    Task<PartyAnswer> Invite(string from, string target);

    /// <summary>The hero joins the party that invited him (<paramref name="partyId"/>, or the newest invitation when null).</summary>
    Task<PartyAnswer> Accept(string hero, string? partyId = null);

    /// <summary>The hero turns the invitation down (<paramref name="partyId"/>, or the newest when null).</summary>
    Task<PartyAnswer> Decline(string hero, string? partyId = null);

    /// <summary>The hero leaves his party; a leader's lead passes to the member who joined next.</summary>
    Task<PartyAnswer> Leave(string hero);

    /// <summary>The leader puts <paramref name="target"/> (a member id) out.</summary>
    Task<PartyAnswer> Kick(string leader, string target);

    /// <summary>The leader ends the party.</summary>
    Task<PartyAnswer> Disband(string leader);

    /// <summary>Forms one party of these heroes (ledger names), the first leading, each leaving his own first: the dev command's.</summary>
    Task<PartyAnswer> Make(IReadOnlyList<string> heroes);

    /// <summary>The hero left this server (a disconnect): unhosted he leaves his party and his invitations; hosted the host's party keeps him.</summary>
    void Gone(string hero);
}
