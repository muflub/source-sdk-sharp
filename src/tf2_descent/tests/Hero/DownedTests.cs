using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Death;
using TF2.Descent.Server.Hero;

using Xunit;

using static TF2.Descent.Tests.Hero.DownedRig;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The downed state in the game (plan_descent_logic WO6; plan_tf2_rpg 2g, R26), over
/// <see cref="DownedRig"/>: two heroes at 200 life, 10 ticks a second, the shipped death.json
/// (a 20 s window shortened 5 s a down, a 3 s revive within 96 units at 30 % life).
/// </summary>
public sealed class DownedTests
{
    private static readonly EntityHandle Robot = new(0x0002_0100u);

    [Fact]
    public void A_lethal_hit_on_a_hero_with_a_partner_up_leaves_him_down_at_1_life()
    {
        var rig = new DownedRig();

        float lands = rig.Hit(A, 500);

        Assert.Equal(199f, lands);
        Assert.Equal(1, rig.Clock.Health(PlayerOf(A)));
        Assert.True(rig.Hero(A).Downed);
        Assert.Contains(PlayerOf(A), rig.Clock.Alive);
    }

    [Fact]
    public void A_hit_that_does_not_kill_lands_as_dealt()
    {
        var rig = new DownedRig();

        Assert.Equal(150f, rig.Hit(A, 150));
        Assert.False(rig.Hero(A).Downed);
    }

    [Fact]
    public void A_solo_hero_dies_of_a_lethal_hit_with_no_downed_state()
    {
        var rig = new DownedRig(A);

        Assert.Equal(500f, rig.Hit(A, 500));
        Assert.False(rig.Hero(A).Downed);
        Assert.DoesNotContain(PlayerOf(A), rig.Clock.Alive);
    }

    [Fact]
    public void Going_down_is_on_the_heros_networked_state_with_the_window_in_ticks()
    {
        var rig = new DownedRig();

        rig.Hit(A, 500);

        var s = rig.State(A);
        Assert.Equal((true, 1000, 1200), (s.Downed, s.DownedStartTick, s.DownedEndTick));
    }

    [Fact]
    public void A_downed_hero_is_frozen_for_his_window()
    {
        var rig = new DownedRig();

        rig.Hit(A, 500);

        Assert.Equal(21f, rig.Clock.Frozen[PlayerOf(A)]);   // the window, and a second's margin the revive lifts
    }

    [Fact]
    public void A_downed_hero_takes_no_damage()
    {
        var rig = new DownedRig();
        rig.Hit(A, 500);

        var hit = new DamageContext(PlayerOf(A), Robot, 40, (int)DamageType.Bullet);
        Assert.Equal(Decision.No, rig.Damage.Decide(in hit));
        Assert.Equal(0f, rig.Damage.Modify(in hit, 40));
    }

    [Fact]
    public void A_standing_heros_hits_are_not_refused()
    {
        var rig = new DownedRig();

        var hit = new DamageContext(PlayerOf(A), Robot, 40, (int)DamageType.Bullet);
        Assert.Equal(Decision.UseStockRule, rig.Damage.Decide(in hit));
    }

    [Fact]
    public void The_self_kill_command_still_kills_a_downed_hero()
    {
        var rig = new DownedRig();
        rig.Hit(A, 500);

        var hit = new DamageContext(PlayerOf(A), EntityHandle.None, PlayerSelfCommand.SuicideDamage, (int)DamageType.Generic);
        Assert.Equal(Decision.UseStockRule, rig.Damage.Decide(in hit));
        Assert.Equal(PlayerSelfCommand.SuicideDamage, rig.Damage.Modify(in hit, PlayerSelfCommand.SuicideDamage));
    }

    [Fact]
    public void A_downed_hero_bleeds_out_when_his_window_ends()
    {
        var rig = new DownedRig();
        rig.Hit(A, 500);

        rig.RunTo(1199);
        Assert.True(rig.Hero(A).Downed);
        rig.RunTo(1200);

        Assert.Equal([PlayerOf(A)], rig.Clock.Kills);
        Assert.False(rig.Hero(A).Downed);
        Assert.Equal(Standing.Dead, rig.Downed.MemberOf(A)!.Standing);
    }

    private static DownedRig Reviving(Vector at)
    {
        var rig = new DownedRig();
        rig.Clock.Origins[PlayerOf(B)] = at;
        rig.Hit(A, 500);
        rig.Clock.Holding.Add(PlayerOf(B));
        rig.RunTo(1001);
        return rig;
    }

    [Fact]
    public void Three_seconds_of_held_use_within_96_units_revives_at_30_percent_life()
    {
        var rig = Reviving(new Vector(90, 0, 0));

        rig.RunTo(1031);

        Assert.False(rig.Hero(A).Downed);
        Assert.Equal(60, rig.Clock.Health(PlayerOf(A)));     // 30 % of 200
        Assert.DoesNotContain(PlayerOf(A), rig.Clock.Frozen.Keys);
    }

    [Fact]
    public void A_revive_is_not_done_before_its_three_seconds()
    {
        var rig = Reviving(new Vector(90, 0, 0));

        rig.RunTo(1030);

        Assert.True(rig.Hero(A).Downed);
    }

    [Fact]
    public void Use_held_from_beyond_96_units_starts_no_revive()
    {
        var rig = Reviving(new Vector(120, 0, 0));

        rig.RunTo(1040);

        Assert.True(rig.Hero(A).Downed);
        Assert.Null(rig.Downed.MemberOf(B)!.Reviving);
    }

    [Fact]
    public void The_revive_and_its_progress_are_on_the_downed_heros_state()
    {
        var rig = Reviving(new Vector(10, 0, 0));

        var s = rig.State(A);
        Assert.Equal((B, 1001, 1031), (s.ReviverIndex, s.ReviveStartTick, s.ReviveEndTick));
    }

    [Fact]
    public void Letting_go_of_use_breaks_the_revive()
    {
        var rig = Reviving(new Vector(10, 0, 0));

        rig.Clock.Holding.Remove(PlayerOf(B));
        rig.RunTo(1010);

        Assert.Equal(0, rig.State(A).ReviverIndex);
        rig.RunTo(1040);
        Assert.True(rig.Hero(A).Downed);
    }

    [Fact]
    public void Moving_out_of_range_breaks_the_revive()
    {
        var rig = Reviving(new Vector(10, 0, 0));

        rig.Clock.Origins[PlayerOf(B)] = new Vector(200, 0, 0);
        rig.RunTo(1010);

        Assert.Null(rig.Downed.MemberOf(B)!.Reviving);
        Assert.Equal(0, rig.State(A).ReviverIndex);
    }

    [Fact]
    public void A_hit_on_the_reviver_breaks_the_revive()
    {
        var rig = Reviving(new Vector(10, 0, 0));

        rig.Hit(B, 10, Robot);

        Assert.Null(rig.Downed.MemberOf(B)!.Reviving);
    }

    [Fact]
    public void When_the_last_one_up_goes_down_everyone_down_dies()
    {
        var rig = new DownedRig();
        rig.Hit(A, 500);

        rig.Hit(B, 500);

        Assert.DoesNotContain(PlayerOf(A), rig.Clock.Alive);
        Assert.DoesNotContain(PlayerOf(B), rig.Clock.Alive);
        Assert.Equal([PlayerOf(A)], rig.Clock.Kills);     // B dies of his own hit
    }

    [Fact]
    public void The_listeners_hear_a_hero_go_down_and_get_up()
    {
        var rig = Reviving(new Vector(10, 0, 0));

        rig.RunTo(1031);

        Assert.Equal([(A, true), (A, false)], rig.Told.Heard);
    }

    [Fact]
    public void A_respawned_hero_goes_down_again_for_a_window_5_seconds_shorter()
    {
        var rig = new DownedRig();
        rig.Hit(A, 500);
        rig.RunTo(1200);                      // bled out
        rig.Clock.Respawn(PlayerOf(A));
        rig.RunTo(1300);

        rig.Hit(A, 500);

        Assert.Equal(1300 + 150, rig.State(A).DownedEndTick);
    }

    [Fact]
    public void A_new_level_starts_with_nobody_down_and_full_windows()
    {
        var rig = new DownedRig();
        rig.Hit(A, 500);

        rig.Downed.OnLevelInit("next");
        rig.Clock.Tick = 10;
        rig.Clock.Healths[PlayerOf(A)] = 200;
        rig.Hero(A).ShowDowned(false, 0, 0);    // the level change detaches the hero; his new state starts up

        Assert.False(rig.Downed.IsDown(A));
        rig.Hit(A, 500);
        Assert.Equal(10 + 200, rig.State(A).DownedEndTick);
    }

    [Fact]
    public void A_hit_during_the_revives_grace_leaves_the_hero_standing_at_1_life()
    {
        var rig = Reviving(new Vector(10, 0, 0));
        rig.RunTo(1031);

        float lands = rig.Hit(A, 500);

        Assert.Equal(59f, lands);
        Assert.False(rig.Hero(A).Downed);
    }

    //-------------------------------------------------------------------------
    // Dev commands
    //-------------------------------------------------------------------------

    [Fact]
    public void The_dev_down_puts_a_hero_down_at_1_life()
    {
        var rig = new DownedRig();

        Assert.Equal(LethalAnswer.Down, rig.Downed.DevDown(A));
        Assert.Equal(1, rig.Clock.Health(PlayerOf(A)));
    }

    [Fact]
    public void The_dev_revive_holds_a_revive_that_completes_after_three_seconds()
    {
        var rig = new DownedRig();
        rig.Downed.DevDown(A);
        rig.Clock.Origins[PlayerOf(B)] = new Vector(5000, 0, 0);    // the dev hold ignores the reach

        rig.Downed.DevRevive(A, B);
        rig.RunTo(1030);

        Assert.False(rig.Hero(A).Downed);
        Assert.Equal(60, rig.Clock.Health(PlayerOf(A)));
    }

    [Fact]
    public void The_dev_bleedout_lapses_the_window_now()
    {
        var rig = new DownedRig();
        rig.Downed.DevDown(A);

        Assert.True(rig.Downed.DevBleedOut(A));

        Assert.Equal([PlayerOf(A)], rig.Clock.Kills);
    }
}
