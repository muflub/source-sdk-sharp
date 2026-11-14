using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde.Attacks;
using TF2.Descent.Tests.Hero;
using TF2.Shared;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde's hands (plan_descent_logic 3.8, HO7; <see cref="RobotAttacks"/>): each kind of
/// attack deals the rules' number in the body's name along where the body looks, a hero takes
/// it after his own armour and resistance exactly as the rules say, the cadence, clip, windup
/// and refusals hold, the 33rd projectile is not fired, and a medic's beam heals at the rules'
/// rate.
/// </summary>
public sealed class RobotAttacksTests
{
    private static double Tps => ServerHordeRig.Tps;

    private static RobotAttack AttackOf(string family) => TheRules.Attacks.Of(family);

    private static double Damage(AttackRig rig, Ai.Roster.RobotId id, string family) =>
        RobotAttackRules.AttackDamage(rig.Roster.Stats(id.Index), AttackOf(family));

    /// <summary>What a hero of the stats rig takes from a hit of the rules' number: the hero's rules' armour against a level-1 attacker, then the resistance of the attack's family's type.</summary>
    private static float HeroTakes(HeroStatsRig hero, double damage, string family)
    {
        var stats = hero.Hero.Stats!;
        double armour = hero.Rig.Rules.Heroes.ArmourReduction(stats.Armour, 1);
        double resist = stats.Resist(TheRules.Attacks.DamageTypeOf(AttackOf(family)));
        return (float)(damage * (1 - armour) * (1 - resist));
    }

    //-------------------------------------------------------------------------
    // Each kind's damage
    //-------------------------------------------------------------------------

    [Fact]
    public void A_swing_lands_after_its_windup_with_the_rules_damage_as_a_club()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("spy");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);

        rig.Fire(body);
        Assert.Empty(rig.World.Hurts);
        rig.Tick((int)RobotAttackRules.Ticks(AttackOf("melee").WindupSeconds, Tps));

        var hurt = Assert.Single(rig.World.Hurts);
        Assert.Equal((AttackRig.HeroEntity, body.Handle, DamageType.Club), (hurt.Victim, hurt.Attacker, hurt.Bits));
        Assert.Equal(Damage(rig, id, "melee"), hurt.Damage, 3);
    }

    [Fact]
    public void A_weakened_robots_swing_deals_that_much_less_until_the_debuff_ends()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("spy");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);
        int windup = (int)RobotAttackRules.Ticks(AttackOf("melee").WindupSeconds, Tps);
        int interval = (int)RobotAttackRules.Ticks(AttackOf("melee").IntervalSeconds, Tps);
        rig.Roster.Weaken(id.Index, -30, rig.Now + windup + 2, rig.Now);

        rig.Fire(body);
        rig.Tick(windup);
        Assert.Equal(Damage(rig, id, "melee") * 0.7, Assert.Single(rig.World.Hurts).Damage, 3);

        rig.Tick(interval);
        rig.Fire(body);
        rig.Tick(windup);
        Assert.Equal(2, rig.World.Hurts.Count);
        Assert.Equal(Damage(rig, id, "melee"), rig.World.Hurts[1].Damage, 3);
    }

    [Fact]
    public void A_heros_life_falls_by_a_swing_of_the_rules_after_his_armour_and_resistance()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("spy");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);
        rig.Fire(body);
        rig.Tick(40);
        var hurt = Assert.Single(rig.World.Hurts);
        var hero = new HeroStatsRig();

        float taken = hero.Hit(hero.Player, HeroStatsRig.Robot, hurt.Damage, hurt.Bits);

        Assert.Equal(HeroTakes(hero, Damage(rig, id, "melee"), "melee"), taken, 3);
        Assert.True(taken > 0f && taken < hurt.Damage, $"the control: armour and resistance take something ({taken} of {hurt.Damage})");
    }

    [Fact]
    public void A_volley_of_pellets_hits_its_victim_once_with_the_rules_damage_as_buckshot()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("scout");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);

        rig.Fire(body);

        var hurt = Assert.Single(rig.World.Hurts);
        Assert.Equal(DamageType.Bullet | DamageType.Buckshot, hurt.Bits);
        Assert.Equal(Damage(rig, id, "scattergun"), hurt.Damage, 3);
        Assert.Equal(AttackOf("scattergun").Pellets, rig.World.Lines.Count);
    }

    [Fact]
    public void A_heros_life_falls_by_a_volley_of_the_rules_after_his_armour_and_resistance()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("scout");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);
        rig.Fire(body);
        var hurt = Assert.Single(rig.World.Hurts);
        var hero = new HeroStatsRig();

        Assert.Equal(HeroTakes(hero, Damage(rig, id, "scattergun"), "scattergun"), hero.Hit(hero.Player, HeroStatsRig.Robot, hurt.Damage, hurt.Bits), 3);
    }

    [Fact]
    public void Pellets_that_meet_the_world_hurt_nobody()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("scout");
        rig.World.Shot = (_, to) => new TraceResult(true, 0.3f, to, default, EntityHandle.None);

        rig.Fire(body);

        Assert.Empty(rig.World.Hurts);
        Assert.Equal(1, rig.Attacks.Fired(HordeWeapons.Scattergun));
    }

    [Fact]
    public void A_rocket_leaves_along_the_look_with_the_rules_damage_and_speed_on_the_robots_team()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("soldier");

        rig.Fire(body);

        var l = Assert.Single(rig.World.Launches);
        Assert.Equal(("tf_projectile_rocket", body.Handle, (int)TFTeam.Blue), (l.Class, l.Owner, l.Team));
        Assert.Equal(Damage(rig, id, "rocket_launcher"), l.Damage, 3);
        Assert.Equal((float)AttackOf("rocket_launcher").Speed, l.Velocity.Length, 2);
        Assert.True(l.Velocity.X > 0.99f * l.Velocity.Length, "along the body's facing (+x)");
        Assert.True(l.Origin.X - body.Position.X > 24f * body.Scale, $"born outside the hull: {l.Origin.X - body.Position.X:0.0} units out");
    }

    [Fact]
    public void A_heros_life_falls_by_a_rocket_of_the_rules_after_his_armour_and_resistance()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("soldier");
        rig.Fire(body);
        var l = Assert.Single(rig.World.Launches);
        var hero = new HeroStatsRig();

        // A direct hit, before TF's own fall-off: the blast's bits.
        Assert.Equal(HeroTakes(hero, Damage(rig, id, "rocket_launcher"), "rocket_launcher"), hero.Hit(hero.Player, HeroStatsRig.Robot, l.Damage, DamageType.Blast), 3);
    }

    [Fact]
    public void A_flame_burns_a_hero_in_its_cone_and_reach_for_a_tick_of_the_rules()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("pyro");
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At(200, 0));
        var player = rig.Horde.Heroes.Heroes[0].Eye.Player;

        rig.Fire(body);

        var hurt = Assert.Single(rig.World.Hurts);
        Assert.Equal((player, DamageType.Burn), (hurt.Victim, hurt.Bits));
        Assert.Equal(Damage(rig, id, "flamethrower"), hurt.Damage, 3);
        var hero = new HeroStatsRig();
        Assert.Equal(HeroTakes(hero, Damage(rig, id, "flamethrower"), "flamethrower"), hero.Hit(hero.Player, HeroStatsRig.Robot, hurt.Damage, hurt.Bits), 3);
    }

    [Fact]
    public void A_flame_does_not_reach_a_hero_beside_its_cone_or_past_its_reach()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("pyro");
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At(150, 150));
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At((float)AttackOf("flamethrower").RangeUnits + 40f, 0));

        rig.Fire(body);

        Assert.Empty(rig.World.Hurts);
    }

    //-------------------------------------------------------------------------
    // Aim
    //-------------------------------------------------------------------------

    [Fact]
    public void A_shot_goes_where_the_body_looks_not_where_the_hero_is()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("sniper");
        body.MoveTo(Centre(0), 90f);
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At(300, 0));

        rig.Fire(body);
        rig.Tick((int)RobotAttackRules.Ticks(AttackOf("sniper_rifle").WindupSeconds, Tps));

        var (from, to) = Assert.Single(rig.World.Lines);
        var dir = (to - from).Normalized;
        Assert.True(dir.Y > 0.999f, $"along the body's look (+y), not at the hero (+x): {dir}");
    }

    [Fact]
    public void The_torso_dials_turn_the_shot()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("sniper");
        body.State.BodyYaw = -30f;    // the look is 30 degrees left of the feet
        body.State.BodyPitch = 20f;   // and 20 up

        rig.Fire(body);
        rig.Tick(40);

        var (from, to) = Assert.Single(rig.World.Lines);
        var dir = (to - from).Normalized;
        Assert.Equal(MathF.Sin(20f * MathF.PI / 180f), dir.Z, 3);
        Assert.Equal(30f, MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI, 2);
    }

    //-------------------------------------------------------------------------
    // Cadence, clip, refusals
    //-------------------------------------------------------------------------

    [Fact]
    public void An_ask_inside_the_interval_is_refused_and_one_after_it_fires()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("scout");
        long interval = RobotAttackRules.Ticks(AttackOf("scattergun").IntervalSeconds, Tps, 1);

        rig.Fire(body);
        rig.Tick((int)interval - 1);
        rig.Fire(body);
        Assert.Equal(1, rig.Attacks.Fired(HordeWeapons.Scattergun));
        Assert.Equal(1, rig.Attacks.Refused(HordeWeapons.Scattergun, AttackRefusal.Cooling));

        rig.Tick();
        rig.Fire(body);
        Assert.Equal(2, rig.Attacks.Fired(HordeWeapons.Scattergun));
    }

    [Fact]
    public void An_empty_clip_reloads_whole_before_the_next_shot()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("soldier");
        var a = AttackOf("rocket_launcher");
        long interval = RobotAttackRules.Ticks(a.IntervalSeconds, Tps, 1), reload = RobotAttackRules.Ticks(a.ReloadSeconds, Tps, 1);
        for (int s = 0; s < a.Clip; s++)
        {
            rig.Fire(body);
            rig.Tick((int)interval);
        }

        rig.Fire(body);
        Assert.Equal(a.Clip, rig.World.Launches.Count);
        Assert.Equal(1, rig.Attacks.Refused(HordeWeapons.RocketLauncher, AttackRefusal.Reloading));

        rig.Tick((int)(reload - interval));
        rig.Fire(body);
        Assert.Equal(a.Clip + 1, rig.World.Launches.Count);
    }

    [Fact]
    public void A_stunned_robot_does_not_attack()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("scout");
        rig.Roster.ApplyStatus(id.Index, "stun", rig.Now);
        Assert.True(rig.Roster.Stunned(id.Index), "the control: the robot is stunned");

        rig.Fire(body);

        Assert.Empty(rig.World.Lines);
        Assert.Equal(1, rig.Attacks.Refused(HordeWeapons.Scattergun, AttackRefusal.Stunned));
    }

    [Fact]
    public void A_dead_robot_does_not_attack()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("scout");
        rig.Roster.Hurt(id.Index, 1e6);

        rig.Fire(body);

        Assert.Empty(rig.World.Lines);
        Assert.Equal(1, rig.Attacks.Refused(HordeWeapons.Scattergun, AttackRefusal.Dead));
    }

    [Fact]
    public void A_swing_whose_body_went_to_another_robot_during_its_windup_does_not_land()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("spy");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);
        rig.Fire(body);
        Assert.True(rig.Horde.Controller.ReturnByHand(id.Index, rig.Now, out string why), why);
        var other = rig.Robot("spy");
        Assert.Equal(body.Index, other.Body.Index);

        rig.Tick(40);

        Assert.Empty(rig.World.Hurts);
        Assert.Equal(1, rig.Attacks.RefusedAll(AttackRefusal.Relent));
    }

    [Fact]
    public void A_parked_body_does_not_attack()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("scout");
        rig.Horde.Controller.ReturnByHand(id.Index, rig.Now, out _);

        rig.Fire(body);

        Assert.Empty(rig.World.Lines);
        Assert.Equal(1, rig.Attacks.RefusedAll(AttackRefusal.NoBody));
    }

    //-------------------------------------------------------------------------
    // The cap
    //-------------------------------------------------------------------------

    [Fact]
    public void The_thirty_third_projectile_is_not_fired()
    {
        var rig = new AttackRig();
        var soldiers = Enumerable.Range(0, 33).Select(_ => rig.Robot("soldier").Body).ToList();

        foreach (var s in soldiers)
            rig.Fire(s);

        Assert.Equal(32, rig.World.Launches.Count);
        Assert.Equal(1, rig.Attacks.Refused(HordeWeapons.RocketLauncher, AttackRefusal.Cap));
        Assert.Equal(32, rig.Attacks.Flying);
    }

    [Fact]
    public void A_projectile_gone_frees_its_place_under_the_cap()
    {
        var rig = new AttackRig();
        var soldiers = Enumerable.Range(0, 33).Select(_ => rig.Robot("soldier").Body).ToList();
        foreach (var s in soldiers.Take(32))
            rig.Fire(s);
        rig.World.Alive.Remove(rig.World.Launches[5].Made);
        rig.Tick();

        rig.Fire(soldiers[32]);

        Assert.Equal(33, rig.World.Launches.Count);
        Assert.Equal(0, rig.Attacks.RefusedAll(AttackRefusal.Cap));
        Assert.True(rig.Attacks.MostFlying <= 32);
    }

    //-------------------------------------------------------------------------
    // The medic's beam
    //-------------------------------------------------------------------------

    [Fact]
    public void A_beam_heals_the_pack_mate_the_medic_looks_at_by_the_rules_rate()
    {
        var rig = new AttackRig();
        var (mid, medic) = rig.Robot("medic");
        var (pid, patient) = rig.Mate("soldier", At(200, 0));
        rig.Roster.Hurt(pid.Index, 100);
        double before = rig.Roster.Life(pid.Index);
        byte bar = patient.State.Life;
        rig.Motion.Looks[medic.Index] = new LookIntent(LookKind.Actor, default, new ActorId(patient.Index, patient.Lease.Serial), AimPoint.Body, 0f, 0);

        rig.Heal(medic);
        rig.Tick(10);

        double rate = TheRules.Attacks.HealPerSecond(rig.Roster.MonsterLevel(mid.Index));
        Assert.Equal(before + 10 * rate / Tps, rig.Roster.Life(pid.Index), 6);
        double most = rig.Roster.Stats(pid.Index).Life;
        Assert.Equal((byte)Math.Ceiling(255 * rig.Roster.Life(pid.Index) / most), patient.State.Life);
        Assert.True(patient.State.Life < 255, $"the control: the bar shows the wound ({bar} before, {patient.State.Life} after)");
        Assert.Equal((int)Math.Ceiling(rig.Roster.Life(pid.Index)), patient.EngineHealth);
    }

    [Fact]
    public void A_beam_ends_its_hold_after_the_last_ask()
    {
        var rig = new AttackRig();
        var (_, medic) = rig.Robot("medic");
        var (pid, patient) = rig.Mate("soldier", At(200, 0));
        rig.Roster.Hurt(pid.Index, 150);
        rig.Motion.Looks[medic.Index] = new LookIntent(LookKind.Actor, default, new ActorId(patient.Index, patient.Lease.Serial), AimPoint.Body, 0f, 0);
        long hold = RobotAttackRules.Ticks(TheRules.Attacks.Settings.Heal.HoldSeconds, Tps, 1);

        rig.Heal(medic);
        rig.Tick((int)hold + 5);
        double after = rig.Roster.Life(pid.Index);
        rig.Tick(30);

        Assert.Equal(after, rig.Roster.Life(pid.Index), 9);
        Assert.Equal(hold, rig.Attacks.HealTicks);
    }

    [Fact]
    public void A_beam_on_a_robot_of_another_pack_is_refused()
    {
        var rig = new AttackRig();
        var (_, medic) = rig.Robot("medic");
        var (_, stranger) = rig.Robot("soldier");
        stranger.MoveTo(Centre(0) + At(200, 0), 0f);
        rig.Motion.Looks[medic.Index] = new LookIntent(LookKind.Actor, default, new ActorId(stranger.Index, stranger.Lease.Serial), AimPoint.Body, 0f, 0);

        rig.Heal(medic);

        Assert.Equal(1, rig.Attacks.Refused(HordeWeapons.SyringeGun, AttackRefusal.NoPatient));
        Assert.Equal(0, rig.Attacks.HealTicks);
    }

    [Fact]
    public void A_beam_on_a_pack_mate_out_of_reach_is_refused()
    {
        var rig = new AttackRig();
        var (_, medic) = rig.Robot("medic");
        var (_, patient) = rig.Mate("soldier", At((float)TheRules.Attacks.Settings.Heal.Range + 100f, 0));
        rig.Motion.Looks[medic.Index] = new LookIntent(LookKind.Actor, default, new ActorId(patient.Index, patient.Lease.Serial), AimPoint.Body, 0f, 0);

        rig.Heal(medic);

        Assert.Equal(1, rig.Attacks.Refused(HordeWeapons.SyringeGun, AttackRefusal.OutOfReach));
    }

    //-------------------------------------------------------------------------
    // The attack's look
    //-------------------------------------------------------------------------

    [Fact]
    public void An_attack_lays_the_models_gesture_over_the_gait_for_its_length()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("spy");
        byte serial = body.State.GestureSerial;
        Assert.Equal(-1, body.State.Gesture);

        rig.Fire(body);
        Assert.Equal((17, (byte)(serial + 1)), (body.State.Gesture, body.State.GestureSerial));
        rig.Tick((int)RobotAttackRules.Ticks(0.5, Tps) - 1);
        Assert.Equal(17, body.State.Gesture);
        rig.Tick();

        Assert.Equal(-1, body.State.Gesture);
        Assert.Equal(1, rig.Attacks.Gestures);
    }

    [Fact]
    public void A_model_with_no_attack_gesture_plays_none()
    {
        var rig = new AttackRig();
        rig.World.Gestured = (-1, 0f);
        var (_, body) = rig.Robot("spy");
        byte serial = body.State.GestureSerial;

        rig.Fire(body);

        Assert.Equal((-1, serial), (body.State.Gesture, body.State.GestureSerial));
        Assert.Equal(0, rig.Attacks.Gestures);
    }

    [Fact]
    public void An_attack_plays_its_weapons_sound_on_the_body()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("soldier");

        rig.Fire(body);

        Assert.Contains((body.Handle, "Weapon_RPG.Single"), rig.World.Sounds);
    }

    //-------------------------------------------------------------------------
    // Counting
    //-------------------------------------------------------------------------

    [Fact]
    public void A_hurt_on_a_hero_from_a_body_counts_as_its_familys()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("soldier");

        rig.Attacks.OnPlayerHurt(new PlayerHurt(AttackRig.HeroEntity, body.Handle, 57, 143));
        rig.Attacks.OnPlayerHurt(new PlayerHurt(AttackRig.HeroEntity, new EntityHandle(9), 20, 123));

        Assert.Equal(1, rig.Attacks.HeroHits(HordeWeapons.RocketLauncher));
        Assert.Equal(57.0, rig.Attacks.HeroDamage(HordeWeapons.RocketLauncher));
    }

    [Fact]
    public void The_cost_instrument_takes_a_sample_a_tick_while_on_and_none_while_off()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("scout");
        rig.Tick(5);
        Assert.Equal(0, rig.Attacks.Costs.Samples);

        rig.Attacks.Costs.Start();
        for (int t = 0; t < 10; t++)
        {
            rig.Tick();
            rig.Fire(body);
        }

        rig.Attacks.Costs.Stop();
        rig.Tick(5);

        Assert.Equal(9, rig.Attacks.Costs.Samples);   // a tick's sum is a sample when the next begins
        Assert.StartsWith("n 9 mean", rig.Attacks.Costs.Report());
    }

    [Fact]
    public void A_dev_ask_fires_through_the_sink_once_a_tick_it_is_ready()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("soldier");
        long interval = RobotAttackRules.Ticks(AttackOf("rocket_launcher").IntervalSeconds, Tps, 1);

        rig.Attacks.AskByHand(body.Index, 2);
        rig.Tick((int)interval + 2);

        Assert.Equal(2, rig.World.Launches.Count);
        Assert.Equal(0, rig.Attacks.AsksLeft(body.Index));
        Assert.Equal(0, rig.Attacks.RefusedAll(AttackRefusal.Cooling));
    }
}
