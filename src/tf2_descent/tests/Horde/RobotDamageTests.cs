using SourceSharp.Abi;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Horde;
using TF2.Descent.Shared;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// Hits on robot bodies (plan_descent_logic HO2, 3.8): a lent body's hit
/// reaches the roster through the rules' resistance and is handed on once; a
/// parked or dead body takes nothing; a kill marks the row dead and the next
/// decision gives the body back.
/// </summary>
public sealed class RobotDamageTests
{
    private static int Hit(RobotBody body, float damage, DamageType type = DamageType.Blast) =>
        body.OnDamagedAt(new EntityHandle(1u), damage, type, default, default, HitGroup.Chest);

    [Fact]
    public void A_robot_half_resistant_to_blast_takes_half_a_rockets_damage()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("demoman", 1.0);
        Assert.Equal(50, rig.Roster.Stats(id.Index).Resist["blast"]);
        double before = rig.Roster.Life(id.Index);

        Hit(body, 100f, DamageType.Blast);

        Assert.Equal(before - 50, rig.Roster.Life(id.Index), 6);
    }

    [Fact]
    public void A_bullet_takes_the_robots_bullet_resistance_not_its_blast_one()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("demoman", 1.0);
        double before = rig.Roster.Life(id.Index);

        Hit(body, 40f, DamageType.Bullet);

        Assert.Equal(before - 40 * (1 - rig.Roster.Stats(id.Index).Resist["bullet"] / 100), rig.Roster.Life(id.Index), 6);
    }

    [Fact]
    public void A_hit_answers_the_health_that_keeps_the_body_at_its_robots_life_rounded_up()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier", 1.0);
        int health = body.EngineHealth;

        int answered = Hit(body, 37f, DamageType.Blast);

        int after = (int)Math.Ceiling(rig.Roster.Life(id.Index));
        Assert.Equal(health - after, answered);
        Assert.Equal(after, body.EngineHealth);
        Assert.InRange(body.State.Life, 1, 254);
    }

    [Fact]
    public void Each_hit_is_handed_on_once_with_its_facts()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier", 1.0);

        Hit(body, 30f, DamageType.Blast);
        Hit(body, 20f, DamageType.Bullet);

        Assert.Equal(2, rig.Hits.Hits.Count);
        var first = rig.Hits.Hits[0];
        Assert.Equal(id, first.Robot);
        Assert.Equal(body.Index, first.Body);
        Assert.Equal(new EntityHandle(1u), first.Attacker);
        Assert.Equal(30f, first.Dealt);
        Assert.Equal("blast", first.DamageType);
        Assert.Equal(HitGroup.Chest, first.HitGroup);
        Assert.False(first.Killed);
        Assert.Equal("bullet", rig.Hits.Hits[1].DamageType);
    }

    [Fact]
    public void A_parked_body_takes_nothing_and_hands_nothing_on()
    {
        var rig = new ServerHordeRig();
        var body = rig.Bodies.Body(5);

        Assert.Equal(0, Hit(body, 500f));
        Assert.Empty(rig.Hits.Hits);
        Assert.False(body.TakesDamage);
    }

    [Fact]
    public void A_kill_marks_the_row_dead_and_answers_all_the_health()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("scout", 1.0);
        int health = body.EngineHealth;

        int answered = Hit(body, 10_000f);

        Assert.Equal(health, answered);
        Assert.Equal(RobotState.Dead, rig.Roster.State(id.Index));
        Assert.True(rig.Hits.Hits.Single().Killed);
        Assert.True(((RobotFlags)body.State.Flags).HasFlag(RobotFlags.Dead));
        Assert.Equal(SolidType.None, rig.LastFor<SolidType>("Entities.SetSolidity", body.Handle, 1));
    }

    [Fact]
    public void A_dead_robots_body_takes_nothing_more()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("scout", 1.0);
        Hit(body, 10_000f);

        Assert.Equal(0, Hit(body, 50f));
        Assert.Single(rig.Hits.Hits);
    }

    [Fact]
    public void The_next_decision_gives_a_dead_robots_body_back_even_in_view()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("scout", 1.0);
        rig.Heroes.Put(rig.Level, HordeFixtures.Centre(0));   // in the same room, in plain sight
        int free = rig.Controller.Pool!.FreePoolBodies;
        Hit(body, 10_000f);

        rig.Tick((int)rig.Controller.DecideInterval + 1);

        Assert.True(body.Parked);
        Assert.Equal(0, rig.Roster.Body(id.Index));
        Assert.Equal(RobotState.Dead, rig.Roster.State(id.Index));
        Assert.Equal(free + 1, rig.Controller.Pool.FreePoolBodies);
    }

    [Theory]
    [InlineData(DamageType.Blast, "blast")]
    [InlineData(DamageType.Bullet, "bullet")]
    [InlineData(DamageType.Club, "bullet")]
    [InlineData(DamageType.Burn, "fire")]
    [InlineData(DamageType.Plasma, "fire")]
    [InlineData(DamageType.Blast | DamageType.Plasma, "fire")]
    [InlineData(DamageType.Shock, "shock")]
    // A TF rocket, as it reached a body live: TF reuses slow burn as "use the distance mod"
    // and radiation as "half falloff" (TFDamageDefs), so neither is fire.
    [InlineData(DamageType.Blast | DamageType.Radiation | DamageType.SlowBurn, "blast")]
    [InlineData(DamageType.Bullet | DamageType.SlowBurn, "bullet")]
    public void Engine_damage_bits_map_to_a_descent_damage_type(DamageType bits, string type)
    {
        Assert.Equal(type, RobotDamageKinds.Of(bits));
        Assert.True(HordeFixtures.TheRules.Content.DamageTypes.Contains(type));
    }
}
