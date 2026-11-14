using TF2.Descent.Rules;
using TF2.Descent.Server.Party;

using Xunit;

namespace TF2.Descent.Tests.Party;

/// <summary>The unhosted parties' rules (WO9): one fact a rule, each over the in-memory service alone.</summary>
public sealed class InMemoryPartiesTests
{
    private readonly PartyChanges _told = new();
    private readonly InMemoryParties _parties;

    public InMemoryPartiesTests() => _parties = new InMemoryParties(new TF2DescentRules(), _told.Hooks);

    private PartyAnswer Do(Task<PartyAnswer> verb)
    {
        Assert.True(verb.IsCompleted);
        return verb.Result;
    }

    private PartyInfo Pair(string a = "a", string b = "b")
    {
        Assert.True(Do(_parties.Invite(a, b)).Ok);
        return Do(_parties.Accept(b)).Party!;
    }

    [Fact]
    public void The_size_is_the_rules_party_size()
    {
        Assert.Equal(new TF2DescentRules().Content.RobotRules.Party.MaxMembers, _parties.MaxSize);
        Assert.Equal(4, _parties.MaxSize);
    }

    [Fact]
    public void Create_makes_a_party_the_hero_leads_alone()
    {
        var made = Do(_parties.Create("a"));

        Assert.True(made.Ok);
        Assert.Equal("a", made.Party!.Leader);
        Assert.Equal(["a"], made.Party.Members.ToArray());
        Assert.Same(made.Party, _parties.Of("a"));
    }

    [Fact]
    public void A_hero_in_a_party_cannot_create_another()
    {
        Do(_parties.Create("a"));

        Assert.Equal(PartyRefusals.InParty, Do(_parties.Create("a")).Refusal);
        Assert.Single(_parties.All);
    }

    [Fact]
    public void An_invite_from_a_hero_in_no_party_makes_his_party_with_the_invitation_out()
    {
        var asked = Do(_parties.Invite("a", "b"));

        Assert.True(asked.Ok);
        Assert.Equal("a", asked.Party!.Leader);
        Assert.Equal(["a"], asked.Party.Members.ToArray());
        Assert.Equal(["b"], asked.Party.Invited.ToArray());
        Assert.Equal([asked.Party.Id], _parties.InvitesOf("b").Select(p => p.Id));
        Assert.Null(_parties.Of("b"));
    }

    [Fact]
    public void Accepting_joins_the_party_and_clears_the_invitation()
    {
        var party = Pair();

        Assert.Equal(["a", "b"], party.Members.ToArray());
        Assert.Empty(party.Invited.ToArray());
        Assert.Empty(_parties.InvitesOf("b"));
        Assert.Same(_parties.Of("a"), _parties.Of("b"));
    }

    [Fact]
    public void Only_the_leader_invites()
    {
        Pair();

        var refused = Do(_parties.Invite("b", "c"));

        Assert.False(refused.Ok);
        Assert.Equal(PartyRefusals.NotLeader, refused.Refusal);
        Assert.Empty(_parties.InvitesOf("c"));
    }

    [Fact]
    public void A_hero_cannot_invite_himself()
    {
        Assert.Equal(PartyRefusals.Self, Do(_parties.Invite("a", "a")).Refusal);
        Assert.Empty(_parties.All);
    }

    [Fact]
    public void A_hero_in_a_party_cannot_be_invited()
    {
        Pair("a", "b");

        Assert.Equal(PartyRefusals.TargetInParty, Do(_parties.Invite("c", "b")).Refusal);
        Assert.Empty(_parties.InvitesOf("b"));
    }

    [Fact]
    public void A_second_invitation_of_the_same_hero_is_refused()
    {
        Do(_parties.Invite("a", "b"));

        Assert.Equal(PartyRefusals.AlreadyInvited, Do(_parties.Invite("a", "b")).Refusal);
        Assert.Single(_parties.Of("a")!.Invited.ToArray());
    }

    [Fact]
    public void A_full_party_invites_nobody_more()
    {
        Assert.True(Do(_parties.Make(["a", "b", "c", "d"])).Ok);

        Assert.Equal(PartyRefusals.Full, Do(_parties.Invite("a", "e")).Refusal);
        Assert.Empty(_parties.InvitesOf("e"));
    }

    [Fact]
    public void Accepting_into_a_party_that_filled_since_is_refused_and_drops_the_invitation()
    {
        Do(_parties.Invite("a", "e"));
        Do(_parties.Invite("a", "b"));
        Do(_parties.Accept("b"));
        Do(_parties.Invite("a", "c"));
        Do(_parties.Accept("c"));
        Do(_parties.Invite("a", "d"));
        Do(_parties.Accept("d"));

        var late = Do(_parties.Accept("e"));

        Assert.Equal(PartyRefusals.Full, late.Refusal);
        Assert.Null(_parties.Of("e"));
        Assert.Empty(_parties.InvitesOf("e"));
        Assert.Equal(4, _parties.Of("a")!.Size);
    }

    [Fact]
    public void Accepting_with_no_invitation_is_refused()
    {
        Assert.Equal(PartyRefusals.NoInvite, Do(_parties.Accept("b")).Refusal);
    }

    [Fact]
    public void Accepting_drops_the_heros_other_invitations()
    {
        Do(_parties.Invite("a", "x"));
        Do(_parties.Invite("c", "x"));
        Assert.Equal(2, _parties.InvitesOf("x").Count);

        Do(_parties.Accept("x"));   // the newest: c's

        Assert.Equal("c", _parties.Of("x")!.Leader);
        Assert.Empty(_parties.InvitesOf("x"));
    }

    [Fact]
    public void Accept_names_the_party_it_answers()
    {
        var first = Do(_parties.Invite("a", "x")).Party!;
        Do(_parties.Invite("c", "x"));

        Do(_parties.Accept("x", first.Id));

        Assert.Equal("a", _parties.Of("x")!.Leader);
    }

    [Fact]
    public void Declining_drops_the_invitation_and_a_party_of_one_with_none_out_ends()
    {
        var party = Do(_parties.Invite("a", "b")).Party!;

        var declined = Do(_parties.Decline("b"));

        Assert.True(declined.Ok);
        Assert.Empty(_parties.InvitesOf("b"));
        Assert.Null(_parties.Of("a"));
        Assert.Contains((PartyChangeKind.Declined, "b"), _told.Kinds(party.Id));
        Assert.Equal(PartyChangeKind.Disbanded, _told.Kinds(party.Id)[^1].Kind);
    }

    [Fact]
    public void Declining_with_no_invitation_is_refused()
    {
        Assert.Equal(PartyRefusals.NoInvite, Do(_parties.Decline("b")).Refusal);
    }

    [Fact]
    public void A_member_leaving_a_party_of_three_leaves_two()
    {
        Do(_parties.Make(["a", "b", "c"]));

        var left = Do(_parties.Leave("c"));

        Assert.True(left.Ok);
        Assert.Equal(["a", "b"], left.Party!.Members.ToArray());
        Assert.Null(_parties.Of("c"));
    }

    [Fact]
    public void A_leader_leaving_passes_the_lead_to_the_member_who_joined_next()
    {
        Do(_parties.Make(["a", "b", "c"]));

        var left = Do(_parties.Leave("a"));

        Assert.Equal("b", left.Party!.Leader);
        Assert.Equal(["b", "c"], left.Party.Members.ToArray());
        Assert.Contains((PartyChangeKind.LeaderChanged, "b"), _told.Kinds(left.Party.Id));
    }

    [Fact]
    public void The_last_member_of_a_pair_is_solo_when_the_other_leaves()
    {
        var party = Pair();

        Do(_parties.Leave("b"));

        Assert.Null(_parties.Of("a"));
        Assert.Empty(_parties.All);
        var kinds = _told.Kinds(party.Id);
        Assert.Equal((PartyChangeKind.Left, "b"), kinds[^2]);
        Assert.Equal(PartyChangeKind.Disbanded, kinds[^1].Kind);
    }

    [Fact]
    public void Leaving_with_no_party_is_refused()
    {
        Assert.Equal(PartyRefusals.NotInParty, Do(_parties.Leave("a")).Refusal);
    }

    [Fact]
    public void The_leader_kicks_a_member()
    {
        var party = Do(_parties.Make(["a", "b", "c"])).Party!;

        var kicked = Do(_parties.Kick("a", "c"));

        Assert.True(kicked.Ok);
        Assert.Null(_parties.Of("c"));
        Assert.Contains((PartyChangeKind.Kicked, "c"), _told.Kinds(party.Id));
    }

    [Fact]
    public void Only_the_leader_kicks()
    {
        Do(_parties.Make(["a", "b", "c"]));

        Assert.Equal(PartyRefusals.NotLeader, Do(_parties.Kick("b", "c")).Refusal);
        Assert.Equal(3, _parties.Of("a")!.Size);
    }

    [Fact]
    public void The_leader_cannot_kick_himself_or_a_stranger()
    {
        Pair();

        Assert.Equal(PartyRefusals.Self, Do(_parties.Kick("a", "a")).Refusal);
        Assert.Equal(PartyRefusals.NotAMember, Do(_parties.Kick("a", "z")).Refusal);
    }

    [Fact]
    public void The_leader_disbands_and_every_member_is_solo()
    {
        var party = Do(_parties.Make(["a", "b", "c"])).Party!;

        Assert.True(Do(_parties.Disband("a")).Ok);

        Assert.All(["a", "b", "c"], h => Assert.Null(_parties.Of(h)));
        Assert.Equal(PartyChangeKind.Disbanded, _told.Kinds(party.Id)[^1].Kind);
    }

    [Fact]
    public void Only_the_leader_disbands()
    {
        Pair();

        Assert.Equal(PartyRefusals.NotLeader, Do(_parties.Disband("b")).Refusal);
        Assert.NotNull(_parties.Of("a"));
    }

    [Fact]
    public void Make_forms_one_party_with_each_hero_out_of_his_own_first()
    {
        Pair("a", "b");
        Pair("c", "d");

        var made = Do(_parties.Make(["b", "d"]));

        Assert.Equal("b", made.Party!.Leader);
        Assert.Equal(["b", "d"], made.Party.Members.ToArray());
        Assert.Null(_parties.Of("a"));
        Assert.Null(_parties.Of("c"));
    }

    [Fact]
    public void Make_refuses_more_than_the_size()
    {
        Assert.Equal(PartyRefusals.Full, Do(_parties.Make(["a", "b", "c", "d", "e"])).Refusal);
        Assert.Empty(_parties.All);
    }

    [Fact]
    public void A_disconnect_leaves_the_party_and_the_invitations()
    {
        Do(_parties.Make(["a", "b", "c"]));
        Do(_parties.Invite("x", "b2"));

        _parties.Gone("c");
        _parties.Gone("b2");

        Assert.Null(_parties.Of("c"));
        Assert.Equal(["a", "b"], _parties.Of("a")!.Members.ToArray());
        Assert.Empty(_parties.InvitesOf("b2"));
        Assert.Null(_parties.Of("x"));
    }

    [Fact]
    public void Each_change_is_told_once_in_order()
    {
        var party = Do(_parties.Invite("a", "b")).Party!;
        Do(_parties.Accept("b"));
        Do(_parties.Invite("a", "c"));
        Do(_parties.Accept("c"));
        Do(_parties.Kick("a", "b"));
        Do(_parties.Leave("a"));

        Assert.Equal(
        [
            (PartyChangeKind.Formed, "a"), (PartyChangeKind.Invited, "b"), (PartyChangeKind.Joined, "b"),
            (PartyChangeKind.Invited, "c"), (PartyChangeKind.Joined, "c"), (PartyChangeKind.Kicked, "b"),
            (PartyChangeKind.Left, "a"), (PartyChangeKind.LeaderChanged, "c"), (PartyChangeKind.Disbanded, "c"),
        ], _told.Kinds(party.Id));
    }
}
