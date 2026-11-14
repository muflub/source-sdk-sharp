using SourceSharp.Abi;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Session;
using TF2.Server.Combat;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The status dev commands (HO8): <c>tf2_descent_robot_status</c> applies through the real sink,
/// <c>tf2_descent_robot_statuses</c> prints a robot's statuses, end ticks, immunity, resistances
/// and attacker level, <c>tf2_descent_robot_hurt</c> lands a typed hit through the hit path.
/// </summary>
public sealed class RobotStatusCommandTests
{
    private static readonly EntityHandle Hero = new(0x0001_0000u | 3);

    private sealed class RigCurrent(ServerHordeRig rig) : IHordeCurrent
    {
        public HordeController? Current => rig.Controller;
    }

    private sealed class Clock(ServerHordeRig rig) : IHordeClock
    {
        public long Tick => rig.Now;
        public double TicksPerSecond => ServerHordeRig.Tps;
    }

    private sealed class NoTaunts : IRobotTaunts
    {
        public void Taunted(RobotId robot, int body, int seat) { }
    }

    private sealed record Kit(ServerHordeRig Rig, RobotStatusEffects Sink, IRobotStatusDamage Damage, RobotHitTally Tally, RigCurrent Current);

    private static Kit Make()
    {
        var rig = new ServerHordeRig();
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () => rig.Engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex is > 0 and < 33);
        rig.Engine.ReturnsEachCall("Players.GetHandle", () => new EntityHandle(0x0001_0000u | (uint)rig.Engine.Calls[^1].ArgAs<int>(0)));
        var damage = new EngineRobotStatusDamage(rig.Engine, rig.Engine, rig.Engine, new TFDamageScopes());
        var current = new RigCurrent(rig);
        var sink = new RobotStatusEffects(current, rig.Registry, rig.Engine, new Clock(rig), new RigRules(), rig.Motion, damage, new NoTaunts());
        var tally = new RobotHitTally();
        rig.HitMembers.Add(tally);
        return new Kit(rig, sink, damage, tally, current);
    }

    [Fact]
    public void Robot_status_applies_through_the_sink_and_prints_the_answer_and_the_end_tick()
    {
        var k = Make();
        var (id, _) = k.Rig.Lent("soldier");

        new RobotStatusCommand(k.Current, k.Sink, k.Rig.Engine, k.Rig.Log).Execute(id.Index, "emp_stun", 1.5f, 3);

        long end = k.Rig.Roster.StatusSlot(id.Index, TF2.Descent.Tests.Horde.HordeFixtures.TheRules.StatusBook.RowOf("stun")).EndTick;
        Assert.True(k.Rig.Roster.Stunned(id.Index));
        Assert.Contains($"emp_stun on robot {id.Index} for 1.5s by player 3: Applied", k.Rig.Log.Text);
        Assert.Contains($"stunx1 until tick {end}", k.Rig.Log.Text);
        Assert.Contains("by player 3", k.Rig.Log.Text);
    }

    [Fact]
    public void Robot_status_on_a_robot_with_no_body_says_so()
    {
        var k = Make();

        new RobotStatusCommand(k.Current, k.Sink, k.Rig.Engine, k.Rig.Log).Execute(0, "stun", 2, 3);

        Assert.Contains("robot 0 is Asleep with no body", k.Rig.Log.Text);
    }

    [Fact]
    public void Robot_statuses_prints_the_immunity_resistances_and_attacker_level()
    {
        var k = Make();
        var (id, _) = k.Rig.Lent("soldier", 1.0, "fireproof");
        var levels = new RobotAttackerLevel(k.Current, k.Rig.Registry);

        new RobotStatusesCommand(k.Current, k.Sink, levels, k.Rig.Log).Execute(id.Index);

        Assert.Contains("no statuses", k.Rig.Log.Text);
        Assert.Contains("immune fire; resist blast", k.Rig.Log.Text);
        Assert.Contains("fire 100%", k.Rig.Log.Text);
        Assert.Contains($"attacker level {k.Rig.Roster.MonsterLevel(id.Index)}", k.Rig.Log.Text);
    }

    [Fact]
    public void Robot_hurt_lands_a_typed_hit_and_prints_an_immune_one()
    {
        var k = Make();
        var (id, _) = k.Rig.Lent("soldier", 1.0, "fireproof");
        double life = k.Rig.Roster.Life(id.Index);

        new RobotHurtCommand(k.Current, k.Damage, new RigRules(), k.Tally, k.Rig.Log).Execute(id.Index, "fire", 50, 3);

        Assert.Equal(life, k.Rig.Roster.Life(id.Index));
        Assert.True(k.Tally.Last!.Value.Immune);
        Assert.Equal(Hero, k.Tally.Last.Value.Attacker);
        Assert.Contains($"robot {id.Index} took 0 of 50 fire from player 3; immune True", k.Rig.Log.Text);
    }

    [Fact]
    public void Robot_hurt_of_another_type_takes_life()
    {
        var k = Make();
        var (id, _) = k.Rig.Lent("soldier", 1.0, "fireproof");
        double life = k.Rig.Roster.Life(id.Index);

        new RobotHurtCommand(k.Current, k.Damage, new RigRules(), k.Tally, k.Rig.Log).Execute(id.Index, "blast", 50, 3);

        Assert.True(k.Rig.Roster.Life(id.Index) < life);
        Assert.False(k.Tally.Last!.Value.Immune);
    }

    [Fact]
    public void Robot_hurt_refuses_a_word_that_is_no_damage_type()
    {
        var k = Make();
        var (id, _) = k.Rig.Lent("soldier");

        new RobotHurtCommand(k.Current, k.Damage, new RigRules(), k.Tally, k.Rig.Log).Execute(id.Index, "poison", 50, 3);

        Assert.Contains("'poison' is not a damage type", k.Rig.Log.Text);
        Assert.Equal(0, k.Tally.Hits);
    }
}
