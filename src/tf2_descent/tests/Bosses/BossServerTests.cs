using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Bosses;
using TF2.Descent.Server.Horde.Boss;
using TF2.Descent.Shared;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The boss fight on the server (plan_descent_logic HO13): the intro when a hero first stands in
/// the arena, the phase at its threshold, the reset when the arena has been empty, the boss bar to
/// the heroes in the arena only, the telegraphed specials, the adds within the roster's and the
/// pool's limits, the Tank's stations and its roof, and the way down closed while the boss stands.
/// </summary>
public sealed class BossServerTests
{
    private static AiVector InArena => Centre(BossRig.ArenaRoom) + At(-150, 0);

    private static AiVector Outside => Centre(0);

    // ------------------------------------------------------------------ the intro

    [Fact]
    public void The_fight_does_not_begin_until_a_hero_stands_in_the_arena()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(Outside);

        rig.Tick(BossRig.Seconds(2));

        Assert.False(rig.Encounter.Engaged);
        Assert.DoesNotContain(rig.World.Notices, n => n.Token == BossEncounter.IntroToken);
        Assert.Equal(rig.Row, rig.Encounter.Row);   // the stimulus: it found its boss
    }

    [Fact]
    public void A_hero_stepping_into_the_arena_hears_the_boss_s_name_once()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(Outside);
        rig.Tick(BossRig.Seconds(1));

        rig.MoveHero(1, InArena);
        rig.Tick(BossRig.Seconds(3));

        Assert.True(rig.Encounter.Engaged);
        var intro = Assert.Single(rig.World.Notices, n => n.Token == BossEncounter.IntroToken);
        Assert.Equal((1, "Sir Nukesalot"), (intro.Player, intro.Arg));
        Assert.Contains(rig.World.Sounds, s => s.Sound == TheRules.Content.Bosses.Sounds.Intro);
        Assert.Contains("[TF2Descent/Boss] intro: Sir Nukesalot", rig.Horde.Log.Text);
    }

    [Fact]
    public void Major_crits_intro_brings_the_tank_into_his_arena()
    {
        var rig = new BossRig("major_crits");
        rig.Lend();
        rig.Hero(InArena);
        int before = rig.Roster.Count;

        rig.Tick(BossRig.Seconds(1));

        Assert.Equal(before + 1, rig.Roster.Count);
        int tank = rig.Encounter.Companion;
        Assert.Equal(TheRules.Content.Bosses.Tank.Template, rig.Roster.Template(tank));
        Assert.Equal(BossRig.ArenaRoom, rig.Roster.HomeRoom(tank));
        Assert.Equal(RobotState.Embodied, rig.Roster.State(tank));
        Assert.False(rig.Roster.IsBoss(tank));
    }

    // ------------------------------------------------------------------ the phase

    [Fact]
    public void A_phase_change_is_logged_with_its_life_and_tick_and_told_to_the_arena()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(InArena);
        rig.Hero(Outside);
        rig.Tick(BossRig.Seconds(1));
        Assert.Equal(0, rig.Encounter.Phase);

        rig.LifeTo(0.6);
        rig.Tick(BossRig.Seconds(1));

        Assert.Equal(1, rig.Encounter.Phase);
        var (phase, life, tick) = Assert.Single(rig.Encounter.Phases);
        Assert.Equal(1, phase);
        Assert.Equal(rig.Roster.Stats(rig.Row).Life * 0.6, life, 3);
        Assert.InRange(tick, rig.Now - BossRig.Seconds(1), rig.Now);
        Assert.Contains("[TF2Descent/Boss] phase 1 of 3: sir_nukesalot at life", rig.Horde.Log.Text);
        var told = rig.World.Notices.Where(n => n.Token == BossEncounter.PhaseToken).Select(n => n.Player).ToList();
        Assert.Equal([1], told);   // the hero outside the arena is not told
    }

    [Fact]
    public void A_boss_hurt_past_two_thresholds_at_once_is_in_the_deeper_phase()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(InArena);
        rig.Tick(BossRig.Seconds(1));

        rig.LifeTo(0.2);
        rig.Tick(BossRig.Seconds(1));

        Assert.Equal(2, rig.Encounter.Phase);
    }

    // ------------------------------------------------------------------ the reset

    [Fact]
    public void A_boss_whose_arena_has_been_empty_for_its_reset_time_is_whole_again_at_its_post()
    {
        var rig = new BossRig();
        var body = rig.Lend();
        rig.Hero(InArena);
        rig.Tick(BossRig.Seconds(1));
        rig.LifeTo(0.5);
        body.MoveTo(Centre(BossRig.ArenaRoom) + At(100, 100), 0f);
        rig.Tick(BossRig.Seconds(1));
        Assert.Equal(1, rig.Encounter.Phase);

        rig.MoveHero(1, Outside);
        rig.Tick(BossRig.Seconds(TheRules.Content.Bosses.ResetSeconds - 1));
        Assert.Equal(0, rig.Encounter.Resets);   // not yet

        rig.Tick(BossRig.Seconds(2));

        Assert.Equal(1, rig.Encounter.Resets);
        Assert.Equal(rig.Roster.Stats(rig.Row).Life, rig.Roster.Life(rig.Row));
        Assert.Equal(rig.Roster.Post(rig.Row), body.Position);
        Assert.Equal((0, false), (rig.Encounter.Phase, rig.Encounter.Engaged));
    }

    [Fact]
    public void A_hero_coming_back_in_time_keeps_the_fight()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(InArena);
        rig.Tick(BossRig.Seconds(1));
        rig.LifeTo(0.5);

        rig.MoveHero(1, Outside);
        rig.Tick(BossRig.Seconds(TheRules.Content.Bosses.ResetSeconds / 2));
        rig.MoveHero(1, InArena);
        rig.Tick(BossRig.Seconds(TheRules.Content.Bosses.ResetSeconds));

        Assert.Equal(0, rig.Encounter.Resets);
        Assert.Equal(rig.Roster.Stats(rig.Row).Life * 0.5, rig.Roster.Life(rig.Row), 3);
    }

    // ------------------------------------------------------------------ the bar

    [Fact]
    public void The_bar_goes_to_the_heroes_in_the_arena_with_the_boss_s_name_life_and_phase()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(InArena);
        rig.Hero(Outside);

        rig.Tick(BossRig.Seconds(1));

        var (player, bar) = Assert.Single(rig.World.Bars);
        Assert.Equal(1, player);
        double most = rig.Roster.Stats(rig.Row).Life;
        Assert.Equal(new BossBarView(true, TemplateNumber("sir_nukesalot"), "Sir Nukesalot", (int)Math.Ceiling(most), (int)Math.Ceiling(most), 0, 3), bar);
    }

    [Fact]
    public void The_bar_follows_the_boss_s_life_and_phase_and_is_not_sent_again_unchanged()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(InArena);
        rig.Tick(BossRig.Seconds(1));

        rig.Tick(BossRig.Seconds(1));
        Assert.Single(rig.World.Bars);

        rig.LifeTo(0.5);
        rig.Tick(BossRig.Seconds(1));

        var last = rig.World.Bars[^1].Bar;
        Assert.Equal(2, rig.World.Bars.Count);
        Assert.Equal((int)Math.Ceiling(rig.Roster.Life(rig.Row)), last.Life);
        Assert.Equal(1, last.Phase);
    }

    [Fact]
    public void A_hero_who_leaves_the_arena_is_sent_the_bar_absent_once()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(InArena);
        rig.Tick(BossRig.Seconds(1));

        rig.MoveHero(1, Outside);
        rig.Tick(BossRig.Seconds(2));

        Assert.Equal([true, false], rig.World.Bars.Select(b => b.Bar.Present));
    }

    [Fact]
    public void The_boss_s_death_withdraws_its_bar()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(InArena);
        rig.Tick(BossRig.Seconds(1));

        rig.Roster.Hurt(rig.Row, rig.Roster.Life(rig.Row));
        rig.Tick(BossRig.Seconds(1));

        Assert.False(rig.World.Bars[^1].Bar.Present);
        Assert.Equal(1, rig.Encounter.BarsWithdrawn);
    }

    // ------------------------------------------------------------------ the specials

    [Fact]
    public void A_special_shows_its_telegraph_at_the_ask_and_lands_after_its_windup()
    {
        var rig = new BossRig();
        var body = rig.Lend();
        rig.Hero(InArena);
        var volley = TheRules.Content.Bosses.Special("nuke_volley")!;

        Assert.True(rig.Ask(body, HordeAbilities.BossSpecial, 0));

        Assert.Contains(rig.World.Sounds, s => s.Entity == body.Handle && s.Sound == volley.Sound);
        Assert.Contains(rig.World.Particles, p => p.Name == volley.Particle);
        Assert.Empty(rig.Hands.Launches);
        long due = rig.Abilities.LandsAt(body.Index);
        Assert.Equal(BossRules.Ticks(volley.WindupSeconds, Horde.ServerHordeRig.Tps), due - rig.Now);

        rig.Tick((int)(due - rig.Now) - 1);
        Assert.Empty(rig.Hands.Launches);

        rig.Tick();
        Assert.Equal(volley.Shots, rig.Hands.Launches.Count);
    }

    [Fact]
    public void A_volley_fans_its_shots_round_the_boss_s_aim_with_its_attack_s_projectile_and_damage()
    {
        var rig = new BossRig();
        var body = rig.Lend();
        var volley = TheRules.Content.Bosses.Special("nuke_volley")!;
        rig.Ask(body, HordeAbilities.BossSpecial, 0);

        rig.Tick(BossRig.Seconds(volley.WindupSeconds) + 1);

        var attack = TheRules.Attacks.Of(volley.Attack);
        Assert.All(rig.Hands.Launches, l => Assert.Equal(attack.Projectile, l.Projectile));
        float damage = (float)(rig.Roster.Stats(rig.Row).Damage * volley.DamageShare);
        Assert.All(rig.Hands.Launches, l => Assert.Equal(damage, l.Damage, 3));
        // Each shot's bearing off the boss's facing (it faces 180: a bearing near the wrap is taken as its nearest turn).
        var offs = rig.Hands.Launches.Select(l => (MathF.Atan2(l.Velocity.Y, l.Velocity.X) * 180f / MathF.PI - body.Yaw + 540f) % 360f - 180f).ToList();
        Assert.Equal((float)(volley.FanDegrees * (volley.Shots - 1)), offs.Max() - offs.Min(), 2);
        Assert.Equal(0f, offs.Average(), 2);   // fanned round the aim
        Assert.All(rig.Hands.Launches, l => Assert.False(l.Crit));
    }

    [Fact]
    public void Major_crits_salvo_is_all_crits()
    {
        var rig = new BossRig("major_crits");
        var body = rig.Lend();
        rig.Ask(body, HordeAbilities.BossSpecial, 0);

        rig.Tick(BossRig.Seconds(2));

        Assert.NotEmpty(rig.Hands.Launches);
        Assert.All(rig.Hands.Launches, l => Assert.True(l.Crit));
    }

    [Fact]
    public void A_volley_at_the_projectile_cap_fires_only_what_the_cap_leaves()
    {
        var rig = new BossRig();
        var body = rig.Lend();
        rig.Hands.Cap = 2;
        rig.Ask(body, HordeAbilities.BossSpecial, 0);

        rig.Tick(BossRig.Seconds(2));

        Assert.Equal(2, rig.Hands.Launches.Count);
        Assert.Equal(2, rig.Abilities.Shots);
    }

    [Fact]
    public void A_slam_hits_the_heroes_inside_its_radius_and_no_other()
    {
        var rig = new BossRig("captain_punch");
        var body = rig.Lend();
        var slam = TheRules.Content.Bosses.Special("ground_slam")!;
        rig.Hero(body.Position + At((float)slam.Radius * 0.5f, 0));
        rig.Hero(body.Position + At((float)slam.Radius * 1.5f, 0));

        Assert.True(rig.Ask(body, HordeAbilities.BossSpecial, 1));
        rig.Tick(BossRig.Seconds(slam.WindupSeconds) + 1);

        var hurt = Assert.Single(rig.World.Hurts);
        Assert.Equal(new EntityHandle(1), hurt.Victim);
        Assert.Equal((float)(rig.Roster.Stats(rig.Row).Damage * slam.DamageShare), hurt.Damage, 3);
        Assert.Equal(DamageType.Blast, hurt.Bits);
    }

    [Fact]
    public void A_special_asked_in_a_phase_with_none_is_taken_and_refused()
    {
        var rig = new BossRig("captain_punch");
        var body = rig.Lend();

        Assert.True(rig.Ask(body, HordeAbilities.BossSpecial, 0));

        Assert.Equal(1, rig.Abilities.Refused(BossRefusal.NoSpecial));
        Assert.Equal(0, rig.Abilities.Telegraphs);
    }

    [Fact]
    public void A_special_whose_boss_is_stunned_during_the_windup_does_not_land()
    {
        var rig = new BossRig();
        var body = rig.Lend();
        rig.Ask(body, HordeAbilities.BossSpecial, 0);

        rig.Roster.ApplyStatus(rig.Row, "stun", rig.Now);
        rig.Tick(BossRig.Seconds(2));

        Assert.Empty(rig.Hands.Launches);
        Assert.Equal(1, rig.Abilities.Refused(BossRefusal.Lost));
    }

    [Fact]
    public void An_ability_that_is_not_a_boss_s_is_left_to_the_next_member()
    {
        var rig = new BossRig();
        var body = rig.Lend();

        Assert.False(rig.Ask(body, HordeAbilities.Heal, 0));
    }

    // ------------------------------------------------------------------ the adds

    [Fact]
    public void A_phase_s_adds_go_on_the_roster_in_the_arena_once_and_take_bodies()
    {
        var rig = new BossRig();
        var body = rig.Lend();
        int before = rig.Roster.Count;

        Assert.True(rig.Ask(body, HordeAbilities.BossSummon, 1));
        Assert.True(rig.Ask(body, HordeAbilities.BossSummon, 1));

        var adds = Enumerable.Range(before, rig.Roster.Count - before).ToList();
        var asked = Assert.Single(BossRules.AddsOf(TheRules.Content.Bosses.Of("sir_nukesalot")!, 1));   // phase 1: demomen (RU14 tuned the count), once
        Assert.Equal("demoman", asked.Template);
        Assert.Equal(asked.Count, adds.Count);
        Assert.All(adds, a => Assert.Equal(("demoman", BossRig.ArenaRoom, RobotState.Embodied), (rig.Roster.Template(a), rig.Roster.HomeRoom(a), rig.Roster.State(a))));
        Assert.All(adds, a => Assert.True(rig.Level.Rooms[BossRig.ArenaRoom].Contains(rig.Roster.Post(a) with { Z = 1 })));
        Assert.Equal(1, rig.Abilities.Refused(BossRefusal.Summoned));
        Assert.Equal(asked.Count, rig.World.Particles.Count(p => p.Name == TF2.Descent.Server.Horde.Champions.ChampionTeleports.In));
    }

    [Fact]
    public void Adds_stand_on_the_arena_s_markers_before_the_boss_s_own()
    {
        var rig = new BossRig();
        var room = rig.Level.Rooms[BossRig.ArenaRoom];

        var spots = BossAdds.Spots(rig.Level, BossRig.ArenaRoom, room.Markers[1].Position, 96f);

        Assert.Equal(room.Markers[1].Position, spots[^1]);
        Assert.Equal([room.Markers[0].Position, room.Markers[2].Position], spots.Take(2));
    }

    [Fact]
    public void A_boss_calls_no_more_adds_than_its_share_of_the_pool()
    {
        var rig = new BossRig();
        rig.Lend();

        int put = rig.Adds.Summon(rig.Controller, rig.Row, "demoman", BossTable.MaxAdds + 5, 9, rig.Now);

        Assert.Equal(BossTable.MaxAdds, put);
        Assert.Equal(5, rig.Adds.Refused);
        Assert.True(rig.Controller.Pool!.FreePoolBodies >= 0);
    }

    [Fact]
    public void Adds_stop_at_the_roster_s_last_row()
    {
        var rig = new BossRig();
        rig.Lend();
        while (rig.Roster.Count < rig.Roster.Capacity - 1)
            rig.Roster.Add("scout", 1.0, 0, new TF2.Descent.Ai.Level.PackMarker(Centre(0), 0), (ulong)rig.Roster.Count);

        int put = rig.Adds.Summon(rig.Controller, rig.Row, "demoman", 3, 9, rig.Now);

        Assert.Equal(1, put);
        Assert.Equal(rig.Roster.Capacity, rig.Roster.Count);
    }

    // ------------------------------------------------------------------ the Tank's verbs

    [Fact]
    public void The_tank_s_station_tells_the_heroes_in_its_arena_it_is_drilling()
    {
        var rig = new BossRig("major_crits");
        rig.Lend();
        rig.Hero(InArena);
        rig.Hero(Outside);
        rig.Tick(BossRig.Seconds(1));
        var tank = rig.Horde.Bodies.Body(rig.Roster.Body(rig.Encounter.Companion));

        Assert.True(rig.Ask(tank, HordeAbilities.TankStation, 0));

        Assert.Equal([1], rig.World.Notices.Where(n => n.Token == BossAbilities.DrillToken).Select(n => n.Player));
        Assert.Contains(rig.World.Sounds, s => s.Entity == tank.Handle && s.Sound == TheRules.Content.Bosses.Sounds.TankDrill);
    }

    [Fact]
    public void The_roof_hurts_every_hero_in_the_arena_and_no_other()
    {
        var rig = new BossRig("major_crits");
        rig.Lend();
        rig.Hero(InArena);
        rig.Hero(Outside);
        rig.Tick(BossRig.Seconds(1));
        var tank = rig.Horde.Bodies.Body(rig.Roster.Body(rig.Encounter.Companion));

        Assert.True(rig.Ask(tank, HordeAbilities.TankRoof, 0));

        var hurt = Assert.Single(rig.World.Hurts);
        Assert.Equal((new EntityHandle(1), (float)TheRules.Content.Bosses.Tank.RoofDamage), (hurt.Victim, hurt.Damage));
        Assert.Equal(1, rig.Abilities.Roofs);
    }

    // ------------------------------------------------------------------ the way down

    [Fact]
    public void The_way_down_is_closed_while_the_boss_stands_and_says_why()
    {
        var rig = new BossRig();

        Assert.True(rig.Exit.Closed);
        Assert.False(rig.Exit.MayLeave(3));

        var told = Assert.Single(rig.World.Notices);
        Assert.Equal((3, BossExit.ClosedToken, "Sir Nukesalot"), told);
    }

    [Fact]
    public void The_way_down_is_open_once_the_boss_is_dead()
    {
        var rig = new BossRig();
        rig.Roster.Hurt(rig.Row, rig.Roster.Life(rig.Row));

        Assert.False(rig.Exit.Closed);
        Assert.True(rig.Exit.MayLeave(3));
        Assert.Empty(rig.World.Notices);
    }

    [Fact]
    public void A_new_load_of_the_level_starts_with_its_way_down_closed()
    {
        var first = new BossRig();
        first.Roster.Hurt(first.Row, first.Roster.Life(first.Row));
        Assert.False(first.Exit.Closed);

        var second = new BossRig();   // the level loaded again: a new controller, a new roster

        Assert.True(second.Exit.Closed);
    }

    [Fact]
    public void A_level_with_no_boss_has_its_way_down_open()
    {
        var rig = new Horde.ServerHordeRig();
        var exit = new BossExit(new Horde.RigHordeCurrent(rig), new Horde.RigRules(), new FakeBossWorld(), rig.Log);

        Assert.False(exit.Closed);
        Assert.True(exit.MayLeave(1));
    }

    private static int TemplateNumber(string id) =>
        TheRules.Content.Robots.Rows.Select((r, k) => (r.Id, k)).Single(x => x.Id == id).k + 1;
}
