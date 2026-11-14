using TF2.Descent.Server.Party;

using Xunit;

namespace TF2.Descent.Tests.Party;

/// <summary>
/// The party's client verbs (WO9): each acts for the player who sent it, is validated by the
/// party service, and a refusal is told to him alone; the invitation reaches the invited player
/// as a notice and a view; each hero's client is told his own party, never another's.
/// </summary>
public sealed class PartyVerbsTests
{
    private readonly PartyKit _k = new(1, 2, 3);

    private void Pair()
    {
        Assert.True(_k.Act(1, PartyVerbs.InviteVerb, "2").Ok);
        Assert.True(_k.Act(2, PartyVerbs.AcceptVerb).Ok);
    }

    [Fact]
    public void Invite_by_slot_invites_that_hero()
    {
        Assert.True(_k.Act(1, PartyVerbs.InviteVerb, "2").Ok);

        Assert.Equal("hero1", _k.Parties.InvitesOf("hero2").Single().Leader);
    }

    [Fact]
    public void Invite_by_name_or_a_part_only_one_name_has()
    {
        Assert.True(_k.Act(1, PartyVerbs.InviteVerb, "name2").Ok);
        Assert.True(_k.Act(1, PartyVerbs.InviteVerb, "e3").Ok);

        Assert.Equal(["hero2", "hero3"], _k.Parties.Of("hero1")!.Invited.ToArray());
    }

    [Fact]
    public void An_argument_that_names_no_hero_is_refused_to_the_asker_with_it()
    {
        var refused = _k.Act(1, PartyVerbs.InviteVerb, "Nobody");

        Assert.Equal(PartyRefusals.NoSuchPlayer, refused.Refusal);
        Assert.Equal(["#Descent_Party_NoSuchPlayer [Nobody]"], _k.NoticesTo(1));
        Assert.Empty(_k.NoticesTo(2));
        Assert.Empty(_k.Parties.All);
    }

    [Fact]
    public void A_part_of_a_name_two_heroes_share_names_nobody()
    {
        Assert.Equal(PartyRefusals.NoSuchPlayer, _k.Act(1, PartyVerbs.InviteVerb, "Name").Refusal);
    }

    [Fact]
    public void The_invited_hero_is_told_who_invites_him_and_his_view_carries_the_invitation()
    {
        _k.Act(1, PartyVerbs.InviteVerb, "2");

        Assert.Contains("#Descent_Party_Invited [Name1]", _k.NoticesTo(2));
        Assert.Contains("#Descent_Party_InviteSent [Name2]", _k.NoticesTo(1));
        var view = _k.ViewOf(2)!;
        Assert.False(view.InParty);
        Assert.Equal((1, "Name1"), (view.InviterSlot, view.InviterName));
    }

    [Fact]
    public void Accept_joins_and_both_views_list_both_with_the_leader()
    {
        Pair();

        foreach (int slot in new[] { 1, 2 })
        {
            var view = _k.ViewOf(slot)!;
            Assert.Equal([1, 2], view.Members.Select(m => m.Slot));
            Assert.Equal(["Name1", "Name2"], view.Members.Select(m => m.Name));
            Assert.Equal([11, 12], view.Members.Select(m => m.Level));
            Assert.Equal(0, view.Leader);
            Assert.False(view.Invited);
        }
        Assert.Contains("#Descent_Party_Joined [Name2]", _k.NoticesTo(1));
    }

    [Fact]
    public void A_stranger_is_told_nothing_of_another_party()
    {
        Pair();

        Assert.Equal(0, _k.ViewsTo(3));
        Assert.Empty(_k.NoticesTo(3));
    }

    [Fact]
    public void Accept_with_no_invitation_is_refused_to_the_asker()
    {
        Assert.Equal(PartyRefusals.NoInvite, _k.Act(2, PartyVerbs.AcceptVerb).Refusal);
        Assert.Equal(["#Descent_Party_NoInvite"], _k.NoticesTo(2));
    }

    [Fact]
    public void Decline_tells_the_leader_and_clears_the_invitation_from_the_view()
    {
        _k.Act(1, PartyVerbs.InviteVerb, "2");

        Assert.True(_k.Act(2, PartyVerbs.DeclineVerb).Ok);

        Assert.Contains("#Descent_Party_Declined [Name2]", _k.NoticesTo(1));
        Assert.False(_k.ViewOf(2)!.Invited);
    }

    [Fact]
    public void Leave_tells_the_others_and_the_leaver_and_his_view_is_empty()
    {
        Pair();
        _k.Act(1, PartyVerbs.InviteVerb, "3");
        _k.Act(3, PartyVerbs.AcceptVerb);

        Assert.True(_k.Act(3, PartyVerbs.LeaveVerb).Ok);

        Assert.Contains("#Descent_Party_Left [Name3]", _k.NoticesTo(1));
        Assert.Contains("#Descent_Party_YouLeft", _k.NoticesTo(3));
        Assert.False(_k.ViewOf(3)!.InParty);
        Assert.Equal([1, 2], _k.ViewOf(1)!.Members.Select(m => m.Slot));
    }

    [Fact]
    public void Kick_by_name_puts_the_member_out_and_tells_him()
    {
        Pair();

        Assert.True(_k.Act(1, PartyVerbs.KickVerb, "Name2").Ok);

        Assert.Null(_k.Parties.Of("hero2"));
        Assert.Contains("#Descent_Party_YouWereKicked", _k.NoticesTo(2));
    }

    [Fact]
    public void Only_the_leader_kicks_and_the_refusal_is_told()
    {
        Pair();

        Assert.Equal(PartyRefusals.NotLeader, _k.Act(2, PartyVerbs.KickVerb, "1").Refusal);
        Assert.Contains("#Descent_Party_NotLeader [1]", _k.NoticesTo(2));
    }

    [Fact]
    public void A_downed_member_cannot_be_kicked()
    {
        Pair();
        _k.Heroes.Down.Add(2);

        Assert.Equal(PartyRefusals.TargetDown, _k.Act(1, PartyVerbs.KickVerb, "2").Refusal);
        Assert.NotNull(_k.Parties.Of("hero2"));
    }

    [Fact]
    public void Disband_by_the_leader_tells_every_member()
    {
        Pair();

        Assert.True(_k.Act(1, PartyVerbs.DisbandVerb).Ok);

        Assert.Contains("#Descent_Party_Disbanded", _k.NoticesTo(1));
        Assert.Contains("#Descent_Party_Disbanded", _k.NoticesTo(2));
        Assert.False(_k.ViewOf(2)!.InParty);
    }

    [Fact]
    public void A_verb_from_a_player_who_is_no_hero_passes_on()
    {
        Assert.False(_k.Verbs.Handle(TF2.Descent.Tests.Hero.HeroRig.PlayerOf(9), PartyVerbs.InviteVerb, ["1"]));
        Assert.True(_k.Verbs.Handle(TF2.Descent.Tests.Hero.HeroRig.PlayerOf(1), PartyVerbs.InviteVerb, ["2"]));
        Assert.False(_k.Verbs.Handle(TF2.Descent.Tests.Hero.HeroRig.PlayerOf(1), "tf2_descent_spend", ["brawn", "1"]));
    }

    [Fact]
    public void A_spawn_sends_the_hero_his_party_again()
    {
        Pair();
        int before = _k.ViewsTo(2);

        _k.Sync.OnSpawned(TF2.Descent.Tests.Hero.HeroRig.PlayerOf(2), 0f);

        Assert.Equal(before + 1, _k.ViewsTo(2));
        Assert.Equal([1, 2], _k.ViewOf(2)!.Members.Select(m => m.Slot));
    }

    [Fact]
    public void A_member_who_arrives_after_the_other_is_shown_on_this_server_to_him_when_he_spawns()
    {
        // Found live after a level change: A spawned first and was told B was not here (slot 0, his id).
        Pair();
        _k.Heroes.Remove(2);
        _k.Sync.OnSpawned(TF2.Descent.Tests.Hero.HeroRig.PlayerOf(1), 0f);
        Assert.Equal([1, 0], _k.ViewOf(1)!.Members.Select(m => m.Slot));

        _k.Heroes.Add(2);
        _k.Sync.OnSpawned(TF2.Descent.Tests.Hero.HeroRig.PlayerOf(2), 0f);

        Assert.Equal([1, 2], _k.ViewOf(1)!.Members.Select(m => m.Slot));
        Assert.Equal("Name2", _k.ViewOf(1)!.Members[1].Name);
    }

    [Fact]
    public void The_fullest_view_fits_one_user_message()
    {
        var view = new TF2.Descent.Shared.PartyView
        {
            Leader = 3,
            Slot = new byte[TF2.Descent.Shared.PartyView.MaxMembers],
            ClassId = new byte[TF2.Descent.Shared.PartyView.MaxMembers],
            Level = new byte[TF2.Descent.Shared.PartyView.MaxMembers],
            Names = string.Join('\n', Enumerable.Repeat(new string('x', TF2.Descent.Shared.PartyView.MaxName), TF2.Descent.Shared.PartyView.MaxMembers)),
            InviterSlot = 33,
            InviterName = new string('y', TF2.Descent.Shared.PartyView.MaxName),
        };

        Span<byte> buffer = stackalloc byte[SourceSharp.Abi.UserMessageSender.MaxPayloadBytes];
        Assert.True(view.Write(buffer) > 0);
    }

    [Fact]
    public void The_roster_names_mates_by_slot_and_a_solo_hero_alone()
    {
        Pair();

        Assert.Equal([1, 2], _k.Roster.MatesOf(2));
        Assert.Equal([3], _k.Roster.MatesOf(3));
        Assert.True(_k.Roster.SameParty(1, 2));
        Assert.False(_k.Roster.SameParty(1, 3));
        Assert.True(_k.Roster.SameParty(3, 3));
        Assert.Equal(_k.Roster.MachineKey(1), _k.Roster.MachineKey(2));
        Assert.NotEqual(_k.Roster.MachineKey(1), _k.Roster.MachineKey(3));
    }
}
