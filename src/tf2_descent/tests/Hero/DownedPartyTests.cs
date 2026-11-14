using TF2.Descent.Server.Hero;

using Xunit;

using static TF2.Descent.Tests.Hero.DownedRig;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The downed state is per party (WO9; plan_tf2_rpg R26: "a downed hero can be revived by a
/// party member"): only a member of the downed hero's party standing keeps him down, and only a
/// member revives him; two heroes in no party are each solo, and a solo hero dies outright.
/// </summary>
public sealed class DownedPartyTests
{
    private const int C = 5;

    [Fact]
    public void A_lethal_hit_with_only_a_stranger_up_kills_the_hero_outright()
    {
        var rig = DownedRig.Strangers(A, B);

        Assert.Equal(500f, rig.Hit(A, 500));

        Assert.False(rig.Hero(A).Downed);
        Assert.DoesNotContain(PlayerOf(A), rig.Clock.Alive);
    }

    [Fact]
    public void In_a_party_of_two_a_goes_down_and_b_revives_him()
    {
        var rig = DownedRig.Strangers(A, B);
        rig.MakeParty(A, B);

        rig.Hit(A, 500);
        Assert.True(rig.Hero(A).Downed);
        Assert.StartsWith($"player {B} holds", rig.Downed.DevRevive(A, B));
        rig.RunTo(rig.Clock.Tick + 31);

        Assert.False(rig.Hero(A).Downed);
        Assert.Equal(60, rig.Clock.Health(PlayerOf(A)));   // 30 % of 200
    }

    [Fact]
    public void A_stranger_on_the_level_cannot_revive_a_downed_hero()
    {
        var rig = DownedRig.Strangers(A, B, C);
        rig.MakeParty(A, B);
        rig.Hit(A, 500);

        string answer = rig.Downed.DevRevive(A, C);

        Assert.StartsWith("refused", answer);
        Assert.True(rig.Hero(A).Downed);
        Assert.Null(rig.Downed.MemberOf(C)!.Reviving);
    }

    [Fact]
    public void A_strangers_held_use_does_not_revive_a_downed_hero_beside_him()
    {
        var rig = DownedRig.Strangers(A, B, C);
        rig.MakeParty(A, B);
        rig.Hit(A, 500);
        rig.Clock.Holding.Add(PlayerOf(C));    // the stranger stands on him (every origin is the same), holding use

        rig.RunTo(rig.Clock.Tick + 40);

        Assert.True(rig.Hero(A).Downed);
        Assert.Null(rig.Downed.MemberOf(C)!.Reviving);
    }

    [Fact]
    public void The_last_member_up_going_down_fells_his_party_though_a_stranger_stands()
    {
        var rig = DownedRig.Strangers(A, B, C);
        rig.MakeParty(A, B);
        rig.Hit(A, 500);
        Assert.True(rig.Hero(A).Downed);

        rig.Hit(B, 500);   // the last one of the party up: both die, the stranger standing changes nothing

        Assert.DoesNotContain(PlayerOf(A), rig.Clock.Alive);
        Assert.DoesNotContain(PlayerOf(B), rig.Clock.Alive);
        Assert.Contains(PlayerOf(C), rig.Clock.Alive);
    }

    [Fact]
    public void Two_parties_on_one_level_each_keep_their_own_downed()
    {
        const int D = 6;
        var rig = DownedRig.Strangers(A, B, C, D);
        rig.MakeParty(A, B);
        rig.MakeParty(C, D);

        rig.Hit(A, 500);
        rig.Hit(C, 500);

        Assert.True(rig.Hero(A).Downed);
        Assert.True(rig.Hero(C).Downed);
        Assert.NotEqual(rig.Downed.MachineOf(A), rig.Downed.MachineOf(C));
        Assert.Equal(rig.Downed.MachineOf(A), rig.Downed.MachineOf(B));
    }

    [Fact]
    public async Task A_hero_who_leaves_the_party_while_down_dies()
    {
        var rig = DownedRig.Strangers(A, B);
        rig.MakeParty(A, B);
        rig.Hit(A, 500);

        Assert.True((await rig.Parties.Leave(rig.Key(A))).Ok);
        rig.Think();

        Assert.DoesNotContain(PlayerOf(A), rig.Clock.Alive);
        Assert.Contains(PlayerOf(A), rig.Clock.Kills);
    }

    [Fact]
    public async Task A_hero_out_of_the_party_is_solo_after_he_leaves()
    {
        var rig = DownedRig.Strangers(A, B);
        rig.MakeParty(A, B);
        rig.Think();
        Assert.True((await rig.Parties.Leave(rig.Key(B))).Ok);
        rig.Think();

        Assert.Equal(500f, rig.Hit(A, 500));   // B stands, but he is no member: A dies outright

        Assert.DoesNotContain(PlayerOf(A), rig.Clock.Alive);
    }

    [Fact]
    public void A_hero_who_changes_party_keeps_his_downs_on_the_level()
    {
        var rig = DownedRig.Strangers(A, B, C);
        rig.MakeParty(A, B);
        rig.Hit(A, 500);
        rig.Downed.DevRevive(A, B);
        rig.RunTo(rig.Clock.Tick + 31);
        Assert.Equal(1, rig.Downed.MemberOf(A)!.DownsThisLevel);

        rig.MakeParty(A, C);
        rig.Think();

        Assert.Equal(rig.Downed.MachineOf(C), rig.Downed.MachineOf(A));
        Assert.Equal(1, rig.Downed.MemberOf(A)!.DownsThisLevel);
    }
}
