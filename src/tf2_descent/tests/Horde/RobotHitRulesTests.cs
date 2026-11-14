using SourceSharp.Abi;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// What a hit on a robot body is, by the rules (plan_descent_logic HO8, 3.8): a skill's hit is
/// its declared damage type, an immune hit takes nothing and is still handed on, a mark raises
/// what a hit takes, and a status's damage tick is its own amount and type.
/// </summary>
public sealed class RobotHitRulesTests
{
    private static readonly EntityHandle Hero = new(0x0001_0000u | 1);

    private static int Hit(RobotBody body, float damage, DamageType type) =>
        body.OnDamagedAt(Hero, damage, type, default, default, HitGroup.Chest);

    [Fact]
    public void A_skills_hit_is_its_declared_type_not_its_projectiles_bits()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        double before = rig.Roster.Life(id.Index);
        Assert.Equal("shock", TheRules.Skills.DamageTypeOf("emp_charge"));
        rig.SkillHits.Skill = "emp_charge";

        Hit(body, 60f, DamageType.Blast);

        var hit = Assert.Single(rig.Hits.Hits);
        Assert.Equal("shock", hit.DamageType);
        Assert.Equal(RobotRules.DamageTaken(rig.Roster.Stats(id.Index), "shock", 60), hit.Taken, 9);
        Assert.Equal(before - hit.Taken, rig.Roster.Life(id.Index), 9);
    }

    [Fact]
    public void Another_robots_blast_takes_nothing_and_is_told_to_no_one()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        var (_, other) = rig.Lent("soldier");
        double before = rig.Roster.Life(id.Index);

        int answer = body.OnDamagedAt(other.Handle, 90f, DamageType.Blast, default, new Vector(0, 0, 400), HitGroup.Generic);

        Assert.Equal(0, answer);
        Assert.Equal(before, rig.Roster.Life(id.Index));
        Assert.Empty(rig.Hits.Hits);
    }

    [Fact]
    public void A_robots_own_blast_hurts_it()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        double before = rig.Roster.Life(id.Index);

        body.OnDamagedAt(body.Handle, 40f, DamageType.Blast, default, new Vector(0, 0, 400), HitGroup.Generic);

        var hit = Assert.Single(rig.Hits.Hits);
        Assert.True(hit.Taken > 0);
        Assert.Equal(before - hit.Taken, rig.Roster.Life(id.Index), 9);
    }

    [Fact]
    public void A_rocket_with_no_skill_in_flight_is_its_bits_type()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");

        Hit(body, 60f, DamageType.Blast);

        Assert.Equal("blast", Assert.Single(rig.Hits.Hits).DamageType);
    }

    [Fact]
    public void A_skill_that_declares_no_type_leaves_the_bits_type()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");
        Assert.Null(TheRules.Skills.DamageTypeOf("rocket_specialist"));
        rig.SkillHits.Skill = "rocket_specialist";

        Hit(body, 60f, DamageType.Bullet);

        Assert.Equal("bullet", Assert.Single(rig.Hits.Hits).DamageType);
    }

    [Fact]
    public void An_immune_hit_takes_nothing_answers_no_engine_damage_and_is_handed_on_as_immune()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier", 1.0, "fireproof");
        double life = rig.Roster.Life(id.Index);
        int health = body.EngineHealth;

        int answered = Hit(body, 80f, DamageType.Burn);

        Assert.Equal(0, answered);
        Assert.Equal((life, health), (rig.Roster.Life(id.Index), body.EngineHealth));
        var hit = Assert.Single(rig.Hits.Hits);
        Assert.True(hit.Immune);
        Assert.Equal((0.0, "fire", false), (hit.Taken, hit.DamageType, hit.Killed));
        Assert.Equal(Hero, hit.Attacker);
    }

    [Fact]
    public void An_immune_robot_dies_to_another_type()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier", 1.0, "fireproof");

        Hit(body, 1_000_000f, DamageType.Blast);

        var hit = Assert.Single(rig.Hits.Hits);
        Assert.False(hit.Immune);
        Assert.True(hit.Killed);
        Assert.Equal(TF2.Descent.Ai.Roster.RobotState.Dead, rig.Roster.State(id.Index));
    }

    [Fact]
    public void A_resisted_hit_is_not_immune()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("demoman");

        Hit(body, 100f, DamageType.Blast);

        var hit = Assert.Single(rig.Hits.Hits);
        Assert.False(hit.Immune);
        Assert.Equal(50, hit.Taken, 9);
    }

    [Fact]
    public void A_marked_robot_takes_the_marks_share_more()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        rig.Roster.ApplyStatus(id.Index, "marked", rig.Now);

        Hit(body, 40f, DamageType.Bullet);

        double plain = RobotRules.DamageTaken(rig.Roster.Stats(id.Index), "bullet", 40);
        Assert.Equal(plain * 1.35, Assert.Single(rig.Hits.Hits).Taken, 5);
    }

    [Fact]
    public void A_status_tick_is_its_own_amount_and_type_whatever_the_engine_hands_on()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        rig.SkillHits.Skill = "emp_charge";

        rig.Bodies.BeginStatusTick(body.Index, 4, "fire");
        Hit(body, 9.5f, DamageType.Generic);
        rig.Bodies.EndStatusTick();

        var hit = Assert.Single(rig.Hits.Hits);
        Assert.Equal(("fire", 4f), (hit.DamageType, hit.Dealt));
        Assert.Equal(RobotRules.DamageTaken(rig.Roster.Stats(id.Index), "fire", 4), hit.Taken, 9);
    }

    [Fact]
    public void A_status_tick_for_another_body_leaves_this_hit_plain()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");

        rig.Bodies.BeginStatusTick(body.Index + 1, 4, "fire");
        Hit(body, 30f, DamageType.Bullet);

        Assert.Equal(("bullet", 30f), (rig.Hits.Hits[0].DamageType, rig.Hits.Hits[0].Dealt));
    }

    [Fact]
    public void A_spawn_with_an_affix_that_is_none_is_refused()
    {
        var rig = new ServerHordeRig();

        var id = rig.Controller.SpawnByHand("soldier", 1.0, Centre(0), 0, 1, rig.Now, out string why, ["frostbitten"]);

        Assert.False(id.IsValid);
        Assert.Contains("frostbitten", why);
    }
}
