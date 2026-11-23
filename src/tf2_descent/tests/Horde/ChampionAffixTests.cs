using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Attacks;
using TF2.Descent.Server.Horde.Champions;
using TF2.Descent.Shared;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// Every champion affix in champions.json does in a fight what the design says (plan_tf2_rpg 2f;
/// HO12): the stat-only ones' stats reach the fight (one fact each), and each behavioural one is
/// carried out by its service under server/Horde/Champions.
/// </summary>
public sealed class ChampionAffixTests
{
    private static double Tps => ServerHordeRig.Tps;

    private static ChampionAffix Affix(string id) => TheRules.Content.Champions[id];

    /// <summary>A champion of <paramref name="template"/> with <paramref name="affix"/> lent a body at the rig's centre, facing +x.</summary>
    private static (Ai.Roster.RobotId Id, RobotBody Body) Champion(AttackRig rig, string template, string affix)
    {
        var (id, body) = rig.Horde.Lent(template, 1.0, affix);
        body.MoveTo(Centre(0), 0f);
        return (id, body);
    }

    //-------------------------------------------------------------------------
    // Stat-only: the stat reaches the fight
    //-------------------------------------------------------------------------

    [Fact]
    public void Extra_fast_reaches_the_bodys_motion()
    {
        var rig = new AttackRig();
        var (_, plain) = rig.Robot("scout");
        var (_, fast) = Champion(rig, "scout", "extra_fast");
        rig.Horde.Tick();

        Assert.True(rig.Horde.Motion.TryBody(plain.Index, out var p));
        Assert.True(rig.Horde.Motion.TryBody(fast.Index, out var f));
        Assert.Equal(p.Speed * (1 + Affix("extra_fast").SpeedPct / 100), f.Speed, 2);
    }

    [Fact]
    public void Giant_reaches_the_bodys_health_and_its_swing()
    {
        var rig = new AttackRig();
        var (pid, plain) = rig.Robot("spy");
        var (gid, giant) = Champion(rig, "spy", "giant");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);

        // The body's health is whole: the giant's is the plain robot's life by the affix, within the rounding.
        Assert.InRange(giant.EngineHealth - rig.Roster.Life(pid.Index) * (1 + Affix("giant").LifePct / 100), -1.0, 1.0);
        Assert.InRange(plain.EngineHealth - rig.Roster.Life(pid.Index), -1.0, 1.0);
        rig.Fire(giant);
        rig.Tick(30);
        double plainDamage = RobotAttackRules.AttackDamage(rig.Roster.Stats(pid.Index), TheRules.Attacks.Of("knife"));
        Assert.Equal(plainDamage * (1 + Affix("giant").DamagePct / 100), Assert.Single(rig.World.Hurts).Damage, 2);
        Assert.Equal(rig.Roster.Stats(pid.Index).KnockbackResistPct + Affix("giant").KnockbackResistPct, rig.Roster.Stats(gid.Index).KnockbackResistPct, 3);
    }

    [Theory]
    [InlineData("fireproof", DamageType.Burn)]
    [InlineData("blast_plated", DamageType.Blast)]
    [InlineData("bulletproof", DamageType.Bullet)]
    [InlineData("shock_hardened", DamageType.Shock)]
    public void An_immunity_takes_nothing_of_its_type_and_full_of_another(string affix, DamageType bits)
    {
        var rig = new AttackRig();
        var (id, body) = Champion(rig, "soldier", affix);
        double life = rig.Roster.Life(id.Index);

        rig.Horde.Bodies.Hit(body, AttackRig.HeroEntity, 40f, bits, default, default, HitGroup.Generic);
        Assert.Equal(life, rig.Roster.Life(id.Index));
        Assert.True(rig.Horde.Hits.Hits[^1].Immune);

        rig.Horde.Bodies.Hit(body, AttackRig.HeroEntity, 40f, bits == DamageType.Bullet ? DamageType.Burn : DamageType.Bullet, default, default, HitGroup.Generic);
        Assert.True(rig.Roster.Life(id.Index) < life, $"{affix}: a hit of another type takes life");
    }

    [Fact]
    public void Multishots_damage_cut_and_fire_enchanteds_rise_reach_the_attack()
    {
        var rig = new AttackRig();
        var (pid, _) = rig.Robot("soldier");
        var (mid, _) = Champion(rig, "soldier", "multishot");
        var (fid, _) = Champion(rig, "soldier", "fire_enchanted");
        var rocket = TheRules.Attacks.Of("rocket_launcher");
        double plain = RobotAttackRules.AttackDamage(rig.Roster.Stats(pid.Index), rocket);

        Assert.Equal(plain * 0.8, RobotAttackRules.AttackDamage(rig.Roster.Stats(mid.Index), rocket), 3);
        Assert.Equal(plain * 1.15, RobotAttackRules.AttackDamage(rig.Roster.Stats(fid.Index), rocket), 3);
        Assert.Equal(rig.Roster.Stats(pid.Index).Resist["fire"] + 50, rig.Roster.Stats(fid.Index).Resist["fire"], 3);
    }

    //-------------------------------------------------------------------------
    // Behaviours: the attack's shape
    //-------------------------------------------------------------------------

    [Fact]
    public void A_multishot_champion_fires_the_rules_shots_fanned_the_rules_degrees_apart()
    {
        var rig = new AttackRig();
        var (_, body) = Champion(rig, "soldier", "multishot");

        rig.Fire(body);

        var a = Affix("multishot");
        Assert.Equal((int)a.Param("shots"), rig.World.Launches.Count);
        var yaws = rig.World.Launches.Select(l => MathF.Atan2(l.Velocity.Y, l.Velocity.X) * 180f / MathF.PI).Order().ToList();
        for (int k = 1; k < yaws.Count; k++)
            Assert.Equal((float)a.Param("spreadDegrees"), yaws[k] - yaws[k - 1], 1);
        Assert.Equal(0f, yaws.Average(), 1);
    }

    [Fact]
    public void A_crit_aura_crits_its_own_and_a_near_pack_mates_attacks_and_not_a_far_ones()
    {
        var rig = new AttackRig();
        var (_, aura) = Champion(rig, "spy", "crit_aura");
        float radius = (float)Affix("crit_aura").Param("radius");
        var (_, near) = rig.Mate("spy", At(0, radius - 50f));
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);

        rig.Fire(aura);
        rig.Fire(near);
        rig.Tick(30);

        Assert.Equal(2, rig.World.Hurts.Count);
        Assert.All(rig.World.Hurts, h => Assert.True((h.Bits & TF2.Shared.Combat.TFDamageBits.Critical) != 0, $"a crit: {h.Bits}"));

        near.MoveTo(Centre(0) + At(0, radius + 60f), 0f);
        rig.Tick(60);
        rig.Fire(near);
        rig.Tick(30);
        Assert.Equal(0, (int)(rig.World.Hurts[^1].Bits & TF2.Shared.Combat.TFDamageBits.Critical));
    }

    [Fact]
    public void A_fire_enchanted_champions_hit_sets_the_hero_alight_for_the_rules_seconds()
    {
        var rig = new AttackRig();
        var (_, body) = Champion(rig, "scout", "fire_enchanted");
        rig.World.Players.Add(AttackRig.HeroEntity);
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);

        rig.Fire(body);

        var ignite = Assert.Single(rig.World.Ignites);
        Assert.Equal((AttackRig.HeroEntity, body.Handle), (ignite.Victim, ignite.Attacker));
        Assert.Equal((float)Affix("fire_enchanted").Param("burnSeconds"), ignite.Seconds, 3);
    }

    [Fact]
    public void A_plain_robot_is_shaped_by_nothing()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("scout");
        rig.World.Players.Add(AttackRig.HeroEntity);
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);

        rig.Fire(body);

        Assert.Empty(rig.World.Ignites);
        Assert.Equal(0, (int)(Assert.Single(rig.World.Hurts).Bits & TF2.Shared.Combat.TFDamageBits.Critical));
        Assert.Equal(1, rig.Shaping.Shots(body.Robot.Index, out _));
    }

    //-------------------------------------------------------------------------
    // Behaviours: guards
    //-------------------------------------------------------------------------

    private static ChampionGuards Guards(AttackRig rig)
    {
        var guards = new ChampionGuards(new RigRules(), new RigHordeCurrent(rig.Horde), new RigClock(rig.Horde), rig.World, rig.Horde.Engine);
        rig.Horde.GuardMembers.Add(guards);
        rig.Tickers.Add(guards);
        return guards;
    }

    [Fact]
    public void Uber_at_low_health_stops_the_hit_at_its_share_turns_on_and_takes_nothing_till_it_ends()
    {
        var rig = new AttackRig();
        var guards = Guards(rig);
        var (id, body) = Champion(rig, "heavy", "uber_low_health");
        var a = Affix("uber_low_health");
        double most = rig.Roster.Stats(id.Index).Life, floor = most * a.Param("belowLifePct") / 100;

        rig.Horde.Bodies.Hit(body, AttackRig.HeroEntity, (float)(most * 1.5), DamageType.Bullet, default, default, HitGroup.Generic);
        Assert.Equal(floor, rig.Roster.Life(id.Index), 1);
        Assert.True(body.Has(RobotFlags.Uber));
        Assert.Contains(rig.World.Sounds, s => s.Sound == ChampionGuards.UberOn);

        rig.Horde.Bodies.Hit(body, AttackRig.HeroEntity, 50f, DamageType.Bullet, default, default, HitGroup.Generic);
        Assert.Equal(floor, rig.Roster.Life(id.Index), 1);
        Assert.Equal(1, guards.UberBlocked);

        rig.Tick((int)(a.Param("seconds") * Tps) + 2);
        Assert.False(body.Has(RobotFlags.Uber));
        rig.Horde.Bodies.Hit(body, AttackRig.HeroEntity, 50f, DamageType.Bullet, default, default, HitGroup.Generic);
        Assert.True(rig.Roster.Life(id.Index) < floor, "once a life: after it, hits take life again");
    }

    private sealed class SwitchedCurrent : IHordeCurrent
    {
        public HordeController? Current { get; set; }
    }

    // Found in review (2026-11-11): a roster's serials start again each level, so a row's über spent
    // on the last level read as spent on the next.
    [Fact]
    public void A_champions_uber_on_the_next_level_is_its_own_not_the_last_levels()
    {
        var first = new AttackRig();
        var current = new SwitchedCurrent { Current = first.Horde.Controller };
        var guards = new ChampionGuards(new RigRules(), current, new RigClock(first.Horde), first.World, first.Horde.Engine);
        first.Horde.GuardMembers.Add(guards);
        var (id1, body1) = Champion(first, "heavy", "uber_low_health");
        first.Horde.Bodies.Hit(body1, AttackRig.HeroEntity, (float)(first.Roster.Stats(id1.Index).Life * 1.5), DamageType.Bullet, default, default, HitGroup.Generic);
        Assert.Equal(1, guards.Ubers);

        var next = new AttackRig();
        current.Current = next.Horde.Controller;
        next.Horde.GuardMembers.Add(guards);
        var (id2, body2) = Champion(next, "heavy", "uber_low_health");
        Assert.Equal((id1.Index, id1.Serial), (id2.Index, id2.Serial));   // the stimulus: the same row and serial on a new roster
        next.Horde.Bodies.Hit(body2, AttackRig.HeroEntity, (float)(next.Roster.Stats(id2.Index).Life * 1.5), DamageType.Bullet, default, default, HitGroup.Generic);

        Assert.Equal(2, guards.Ubers);
        Assert.True(next.Roster.Life(id2.Index) > 0, "the new level's champion ubered rather than died");
    }

    [Fact]
    public void A_medic_shield_cuts_a_hit_from_the_front_and_not_one_from_behind()
    {
        var rig = new AttackRig();
        var guards = Guards(rig);
        var (id, body) = Champion(rig, "soldier", "medic_shielded");
        double life = rig.Roster.Life(id.Index);
        var front = HordeVectors.ToEngine(Centre(0) + At(100, 0, 40));
        var back = HordeVectors.ToEngine(Centre(0) + At(-100, 0, 40));

        rig.Horde.Bodies.Hit(body, EntityHandle.None, 100f, DamageType.Bullet, front, default, HitGroup.Generic);
        double cut = life - rig.Roster.Life(id.Index);
        rig.Horde.Bodies.Hit(body, EntityHandle.None, 100f, DamageType.Bullet, back, default, HitGroup.Generic);
        double whole = life - cut - rig.Roster.Life(id.Index);

        Assert.Equal(whole * (1 - Affix("medic_shielded").Param("absorbPct") / 100), cut, 2);
        Assert.Equal(1, guards.Shielded);
    }

    //-------------------------------------------------------------------------
    // Behaviours: death and the blink
    //-------------------------------------------------------------------------

    private static ChampionDeaths Deaths(AttackRig rig)
    {
        var deaths = new ChampionDeaths(new RigRules(), new RigHordeCurrent(rig.Horde), rig.World, rig.Horde.Heroes);
        rig.Horde.HitMembers.Add(deaths);
        return deaths;
    }

    [Fact]
    public void A_sentry_buster_death_champion_blasts_the_heroes_in_reach_when_it_dies()
    {
        var rig = new AttackRig();
        var deaths = Deaths(rig);
        var (id, body) = Champion(rig, "soldier", "sentry_buster_death");
        var a = Affix("sentry_buster_death");
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At((float)a.Param("radius") * 0.5f, 0));
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At(0, (float)a.Param("radius") + 80f));
        var near = rig.Horde.Heroes.Heroes[0].Eye.Player;
        rig.World.Shot = (_, to) => new TraceResult(true, 1f, to, default, to.X > Centre(0).X + 10 ? near : rig.Horde.Heroes.Heroes[1].Eye.Player);

        rig.Horde.Bodies.Hit(body, AttackRig.HeroEntity, (float)(rig.Roster.Life(id.Index) * 0.5), DamageType.Bullet, default, default, HitGroup.Generic);
        Assert.Empty(rig.World.Hurts);   // the control: a hit that does not kill blows nothing
        rig.Horde.Bodies.Hit(body, AttackRig.HeroEntity, (float)rig.Roster.Life(id.Index) + 10f, DamageType.Bullet, default, default, HitGroup.Generic);

        var hurt = Assert.Single(rig.World.Hurts);
        Assert.Equal((near, DamageType.Blast), (hurt.Victim, hurt.Bits));
        Assert.Equal(rig.Roster.Stats(id.Index).Damage * a.Param("damagePct") / 100, hurt.Damage, 2);
        Assert.Equal(1, deaths.Blew);
    }

    [Fact]
    public void A_sentry_buster_killed_before_its_blast_still_blows()
    {
        var rig = new AttackRig();
        Deaths(rig);
        var (id, body) = rig.Robot("sentry_buster");
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At(100, 0));
        var hero = rig.Horde.Heroes.Heroes[0].Eye.Player;
        rig.World.Shot = RigAttackWorld.Meets(hero);

        rig.Horde.Bodies.Hit(body, AttackRig.HeroEntity, (float)rig.Roster.Life(id.Index) + 10f, DamageType.Bullet, default, default, HitGroup.Generic);

        var hurt = Assert.Single(rig.World.Hurts);
        Assert.Equal(RobotAttackRules.AttackDamage(rig.Roster.Stats(id.Index), TheRules.Attacks.Of("sentry_buster")), hurt.Damage, 2);
    }

    [Fact]
    public void A_teleporting_champion_hit_from_afar_blinks_beside_the_hero_once_a_cooldown()
    {
        var rig = new AttackRig();
        var a = Affix("teleporting");
        var teleports = new ChampionTeleports(new RigRules(), new RigHordeCurrent(rig.Horde), rig.World, rig.Horde.Engine, new RigClock(rig.Horde), rig.Horde.Motion);
        rig.Horde.HitMembers.Add(teleports);
        var (_, body) = Champion(rig, "soldier", "teleporting");
        rig.Horde.Tick();   // the motion takes the body where it was lent: a blink must start it again
        var hero = Centre(0) + At((float)a.Param("fromUnits") + 300f, 0);
        rig.Horde.Engine.Returns("Entities.IsValid", true);
        rig.Horde.Engine.Returns("Entities.IsPlayer", true);
        rig.Horde.Engine.Returns("Entities.GetOrigin", HordeVectors.ToEngine(hero));
        // The floor under any spot; nothing else in the way.
        rig.World.Shot = (from, to) => to.Z < from.Z - 100f ? new TraceResult(true, 0.25f, new Vector(to.X, to.Y, 0f), new Vector(0, 0, 1), EntityHandle.None)
                                                           : new TraceResult(false, 1f, to, default, EntityHandle.None);
        rig.World.Swept = (_, to) => new TraceResult(false, 1f, to, default, EntityHandle.None);

        rig.Horde.Bodies.Hit(body, new EntityHandle(1u), 5f, DamageType.Bullet, default, default, HitGroup.Generic);

        Assert.Equal(1, teleports.Blinks);
        Assert.Equal((float)a.Param("toUnits"), new AiVector(body.Position.X - hero.X, body.Position.Y - hero.Y, 0f).Length, 1);
        Assert.Contains(rig.World.Particles, p => p.Name == ChampionTeleports.Out);
        Assert.Contains(rig.World.Particles, p => p.Name == ChampionTeleports.In);
        var landed = body.Position;
        rig.Horde.Tick();
        Assert.True(rig.Horde.Motion.Written(body.Index).DistanceTo(landed) < 2f,
                    $"the motion starts again from the new spot: {rig.Horde.Motion.Written(body.Index)} against {landed}");

        body.MoveTo(Centre(0), 0f);
        rig.Horde.Bodies.Hit(body, new EntityHandle(1u), 5f, DamageType.Bullet, default, default, HitGroup.Generic);
        Assert.Equal(1, teleports.Blinks);   // inside its cooldown
    }

    [Fact]
    public void A_teleporting_champion_hit_from_near_stays()
    {
        var rig = new AttackRig();
        var teleports = new ChampionTeleports(new RigRules(), new RigHordeCurrent(rig.Horde), rig.World, rig.Horde.Engine, new RigClock(rig.Horde), rig.Horde.Motion);
        rig.Horde.HitMembers.Add(teleports);
        var (_, body) = Champion(rig, "soldier", "teleporting");
        rig.Horde.Engine.Returns("Entities.IsValid", true);
        rig.Horde.Engine.Returns("Entities.IsPlayer", true);
        rig.Horde.Engine.Returns("Entities.GetOrigin", HordeVectors.ToEngine(Centre(0) + At(200, 0)));

        rig.Horde.Bodies.Hit(body, new EntityHandle(1u), 5f, DamageType.Bullet, default, default, HitGroup.Generic);

        Assert.Equal(0, teleports.Blinks);
        Assert.Equal(Centre(0), body.Position);
    }
}
