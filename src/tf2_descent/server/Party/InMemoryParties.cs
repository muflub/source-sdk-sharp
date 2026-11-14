using System.Collections.Immutable;
using System.Globalization;

using SourceSharp.Abi;

using TF2.Descent.Rules;

namespace TF2.Descent.Server.Party;

/// <summary>
/// The parties of an unhosted server (plan_descent_logic E8 for parties): held in this process,
/// so they last across every level change, and gone with it. A member is named by his ledger
/// name. The rules:
/// <list type="bullet">
/// <item>Only the leader invites, kicks and disbands; a hero in no party who invites makes a party
/// of himself first and leads it.</item>
/// <item>A hero in a party cannot be invited, and cannot accept another party's invitation
/// before he leaves his own. Accepting joins the party and drops every other invitation he had.</item>
/// <item>At most <see cref="MaxSize"/> members; an invitation to a full party is refused, and so is
/// accepting one into a party that filled since.</item>
/// <item>A leader who leaves passes the lead to the member who joined next.</item>
/// <item>A party lasts while it has two members, or one with an invitation out: the last member
/// of one that falls below is solo again, and its invitations are gone.</item>
/// <item>A hero who disconnects leaves his party and his invitations (<see cref="Gone"/>).</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class InMemoryParties(TF2DescentRules rules, IHooks<IPartyHook> hooks) : IParties
{
    private readonly PartyBook _book = new(hooks);
    private int _next;

    public int MaxSize => rules.Content.RobotRules.Party.MaxMembers;

    public IReadOnlyList<PartyInfo> All => _book.All;

    public string? MemberId(string hero) => hero;

    public string? HeroOf(string member) => member;

    public PartyInfo? Of(string hero) => _book.WithMember(hero);

    public IReadOnlyList<PartyInfo> InvitesOf(string hero) => _book.Inviting(hero);

    /// <summary>Unhosted every member is a hero on this server: no card is needed.</summary>
    public HostMemberCard? Card(string member) => null;

    public Task<PartyAnswer> Create(string hero) => Task.FromResult(CreateNow(hero));

    private PartyAnswer CreateNow(string hero)
    {
        if (Of(hero) is not null)
            return PartyAnswer.Refused(PartyRefusals.InParty);
        string id = "p" + (++_next).ToString(CultureInfo.InvariantCulture);
        var party = new PartyInfo(id, hero, [hero], []);
        _book.Commit(id, party);
        return PartyAnswer.Done(party);
    }

    public Task<PartyAnswer> Invite(string from, string target)
    {
        if (from == target)
            return PartyAnswer.RefusedTask(PartyRefusals.Self);
        if (Of(target) is not null)
            return PartyAnswer.RefusedTask(PartyRefusals.TargetInParty);
        var party = Of(from);
        if (party is not null && party.Leader != from)
            return PartyAnswer.RefusedTask(PartyRefusals.NotLeader);
        if (party is { } p && p.Size >= MaxSize)
            return PartyAnswer.RefusedTask(PartyRefusals.Full);
        if (party is { } q && q.Invites(target))
            return PartyAnswer.RefusedTask(PartyRefusals.AlreadyInvited);
        party ??= CreateNow(from).Party!;
        var now = party with { Invited = party.Invited.Add(target) };
        _book.Commit(party.Id, now);
        return Task.FromResult(PartyAnswer.Done(now));
    }

    public Task<PartyAnswer> Accept(string hero, string? partyId = null)
    {
        if (Invitation(hero, partyId) is not { } party)
            return PartyAnswer.RefusedTask(PartyRefusals.NoInvite);
        if (Of(hero) is not null)
            return PartyAnswer.RefusedTask(PartyRefusals.InParty);
        if (party.Size >= MaxSize)
        {
            Settle(party with { Invited = party.Invited.Remove(hero) });
            return PartyAnswer.RefusedTask(PartyRefusals.Full);
        }

        var now = party with { Members = party.Members.Add(hero), Invited = party.Invited.Remove(hero) };
        _book.Commit(party.Id, now);
        foreach (var other in InvitesOf(hero).ToList())
            Settle(other with { Invited = other.Invited.Remove(hero) });
        return Task.FromResult(PartyAnswer.Done(now));
    }

    public Task<PartyAnswer> Decline(string hero, string? partyId = null)
    {
        if (Invitation(hero, partyId) is not { } party)
            return PartyAnswer.RefusedTask(PartyRefusals.NoInvite);
        return Task.FromResult(PartyAnswer.Done(Settle(party with { Invited = party.Invited.Remove(hero) })));
    }

    public Task<PartyAnswer> Leave(string hero)
    {
        if (Of(hero) is not { } party)
            return PartyAnswer.RefusedTask(PartyRefusals.NotInParty);
        return Task.FromResult(PartyAnswer.Done(Without(party, hero, kicked: false)));
    }

    public Task<PartyAnswer> Kick(string leader, string target)
    {
        if (Of(leader) is not { } party)
            return PartyAnswer.RefusedTask(PartyRefusals.NotInParty);
        if (party.Leader != leader)
            return PartyAnswer.RefusedTask(PartyRefusals.NotLeader);
        if (target == leader)
            return PartyAnswer.RefusedTask(PartyRefusals.Self);
        if (!party.Has(target))
            return PartyAnswer.RefusedTask(PartyRefusals.NotAMember);
        return Task.FromResult(PartyAnswer.Done(Without(party, target, kicked: true)));
    }

    public Task<PartyAnswer> Disband(string leader)
    {
        if (Of(leader) is not { } party)
            return PartyAnswer.RefusedTask(PartyRefusals.NotInParty);
        if (party.Leader != leader)
            return PartyAnswer.RefusedTask(PartyRefusals.NotLeader);
        _book.Commit(party.Id, null);
        return Task.FromResult(PartyAnswer.Done(null));
    }

    public Task<PartyAnswer> Make(IReadOnlyList<string> heroes)
    {
        var distinct = heroes.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count == 0)
            return PartyAnswer.RefusedTask(PartyRefusals.NoSuchPlayer);
        if (distinct.Count > MaxSize)
            return PartyAnswer.RefusedTask(PartyRefusals.Full);
        foreach (string hero in distinct)
        {
            if (Of(hero) is { } party)
                Without(party, hero, kicked: false);
            foreach (var inviting in InvitesOf(hero).ToList())
                Settle(inviting with { Invited = inviting.Invited.Remove(hero) });
        }

        var made = CreateNow(distinct[0]).Party!;
        var now = made with { Members = [.. distinct] };
        _book.Commit(made.Id, now);
        return Task.FromResult(PartyAnswer.Done(now));
    }

    public void Gone(string hero)
    {
        if (Of(hero) is { } party)
            Without(party, hero, kicked: false);
        foreach (var inviting in InvitesOf(hero).ToList())
            Settle(inviting with { Invited = inviting.Invited.Remove(hero) });
    }

    /// <summary>The invitation the hero answers: the named party's, or the newest.</summary>
    private PartyInfo? Invitation(string hero, string? partyId) =>
        partyId is null ? InvitesOf(hero).LastOrDefault() : _book.ById(partyId) is { } p && p.Invites(hero) ? p : null;

    /// <summary>The party without <paramref name="member"/>, the lead passed on when he led it.</summary>
    private PartyInfo? Without(PartyInfo party, string member, bool kicked)
    {
        var members = party.Members.Remove(member);
        string leader = party.Leader == member && members.Length > 0 ? members[0] : party.Leader;
        var now = party with { Leader = leader, Members = members };
        return Settle(now, kicked ? member : null);
    }

    /// <summary>Holds <paramref name="now"/>, or ends it when it is too small to last; the party as it is after.</summary>
    private PartyInfo? Settle(PartyInfo now, string? kicked = null)
    {
        bool lasts = now.Size >= 2 || (now.Size == 1 && now.Invited.Length > 0);
        if (!lasts)
        {
            // The member's own change is told before the party ends: he left, then it is gone.
            var held = _book.ById(now.Id);
            if (held is not null && now.Size < held.Size)
                _book.Commit(now.Id, now with { Invited = ImmutableArray<string>.Empty }, kicked);
            _book.Commit(now.Id, null);
            return null;
        }

        _book.Commit(now.Id, now, kicked);
        return now;
    }
}
