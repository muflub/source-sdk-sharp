using SourceSharp.Abi;
using TF2.Descent.Server.Horde.Motion;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>Robots are one team: a trace passing a robot body passes the other bodies, and nothing else changes (HO3).</summary>
public sealed class RobotsPassRobotsTests
{
    private static (RobotsPassRobots Rule, MotionRig Rig) Make()
    {
        var rig = new MotionRig(bodies: 4);
        return (new RobotsPassRobots(rig.Motion), rig);
    }

    private static readonly EntityHandle Hero = new((uint)((1 << 16) | 1));

    [Fact]
    public void A_robots_trace_passes_another_robot()
    {
        var (rule, rig) = Make();

        Assert.False(rule.ShouldCollide(rig.Bodies.Handle(1), rig.Bodies.Handle(2), CollisionGroup.None, 0));
    }

    [Fact]
    public void A_robots_trace_still_hits_a_hero()
    {
        var (rule, rig) = Make();

        Assert.True(rule.ShouldCollide(rig.Bodies.Handle(1), Hero, CollisionGroup.None, 0));
    }

    [Fact]
    public void A_heros_trace_still_hits_a_robot()
    {
        var (rule, rig) = Make();

        Assert.True(rule.ShouldCollide(Hero, rig.Bodies.Handle(2), CollisionGroup.None, 0));
    }

    [Fact]
    public void A_trace_with_no_mover_still_hits_a_robot()
    {
        var (rule, rig) = Make();

        Assert.True(rule.ShouldCollide(EntityHandle.None, rig.Bodies.Handle(2), CollisionGroup.None, 0));
    }

    [Fact]
    public void An_entity_that_reuses_a_bodys_slot_is_not_a_body()
    {
        var (rule, rig) = Make();
        var body = rig.Bodies.Handle(2);
        var other = new EntityHandle((uint)((2 << 16) | body.EntryIndex));

        Assert.True(rule.ShouldCollide(rig.Bodies.Handle(1), other, CollisionGroup.None, 0));
    }
}
