using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Motion;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Motion;
using TF2.Descent.Shared;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A body's motion over the horde's own bodies, roster and rules (HO3): the
/// controller ticks it, the roster's status sets its speed, the writes reach
/// the entity and its net state, and a blast on the body pushes it.
/// </summary>
public sealed class RobotMotionRigTests
{
    private const float Tick = 0.015f;

    private static MoveIntent FarAlongX(AiVector from) => MotionRig.Follow(from + At(3000, 0));

    private static float Speed(ServerHordeRig rig, RobotBody body, int ticks)
    {
        var a = body.Position;
        rig.Tick(ticks);
        return (body.Position - a).Length2D / (ticks * Tick);
    }

    [Fact]
    public void The_controller_moves_a_lent_body_along_its_order()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");
        var start = body.Position;

        rig.Motion.Order(body.Index, FarAlongX(start), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(60);

        Assert.True(body.Position.X > start.X + 100f, $"at {body.Position}");
        Assert.Equal(HordeVectors.ToEngine(body.Position), rig.LastFor<Vector>("Entities.SetOrigin", body.Handle, 1));
    }

    [Fact]
    public void A_slowed_robot_moves_at_exactly_the_slows_share_of_its_speed()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        rig.Tick(12);
        float full = (float)rig.Roster.Stats(id.Index).Speed;
        rig.Roster.ApplyStatus(id.Index, "slow", rig.Now);
        float share = rig.Roster.SpeedFactor(id.Index);
        Assert.Equal(0.6f, share, 3);

        rig.Motion.Order(body.Index, FarAlongX(body.Position), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(40);

        Assert.Equal(full * share, Speed(rig, body, 40), 0);
    }

    [Fact]
    public void A_stunned_robot_does_not_move_on_its_order()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        rig.Tick(12);
        rig.Roster.ApplyStatus(id.Index, "stun", rig.Now);
        var start = body.Position;

        rig.Motion.Order(body.Index, FarAlongX(start), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(40);

        Assert.Equal(start, body.Position);
    }

    [Fact]
    public void A_still_body_is_not_written_again()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");
        rig.Tick(20);
        int before = rig.Engine.AllOf("Entities.SetOrigin").Count(c => c.ArgAs<EntityHandle>(0) == body.Handle);

        rig.Tick(30);

        Assert.Equal(before, rig.Engine.AllOf("Entities.SetOrigin").Count(c => c.ArgAs<EntityHandle>(0) == body.Handle));
    }

    [Fact]
    public void A_running_body_plays_its_run_sequence_once_and_runs_forward_and_stands_when_it_stops()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");
        rig.Tick(20);
        byte serial = body.State.SequenceSerial;

        rig.Motion.Order(body.Index, FarAlongX(body.Position), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(60);

        Assert.True(body.Running);
        Assert.Equal((byte)(serial + 1), body.State.SequenceSerial);
        Assert.Contains(rig.Engine.AllOf("EntityRender.LookupSequence"), c => c.ArgAs<string>(1) == "run_PRIMARY");
        Assert.Equal(1f, body.State.MoveX, 2);
        Assert.Equal(0f, body.State.MoveY, 2);

        rig.Motion.Hold(body.Index);
        rig.Tick(60);
        Assert.False(body.Running);
        Assert.Equal((byte)(serial + 2), body.State.SequenceSerial);
        Assert.Equal(0f, body.State.MoveX);
    }

    [Fact]
    public void A_dead_bodys_order_is_dropped()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");
        body.OnDamagedAt(new EntityHandle(1u), 100_000f, DamageType.Blast, default, default, HitGroup.Chest);

        rig.Motion.Order(body.Index, FarAlongX(body.Position), LookIntent.AlongPath, long.MaxValue);

        Assert.Equal(1, rig.Motion.OrdersDropped);
    }

    [Fact]
    public void A_rockets_blast_pushes_the_body_by_the_rules_push_and_it_lands()
    {
        var rig = new ServerHordeRig();
        var push = new RobotBlastPush(new RigRules(), rig.Motion);
        rig.HitMembers.Add(push);
        var (id, body) = rig.Lent("soldier");
        rig.Tick(20);
        float floor = body.Position.Z;
        var force = new Vector(0f, 0f, 540f);

        body.OnDamagedAt(new EntityHandle(1u), 60f, DamageType.Blast, default, force, HitGroup.Chest);

        var expected = TheRules.BlastPush.VelocityChange(new BlastForce(new Vec3(0, 0, 540), 60, BlastOwner.Other),
                                                         rig.Roster.Scale(id.Index), rig.Roster.Stats(id.Index).KnockbackResistPct);
        Assert.Equal(1, push.Pushed);
        Assert.Equal((float)expected.Z, push.LastPush.Z, 3);
        Assert.True(expected.Z > 140, $"the push {expected.Z} would not lift a body off the ground");

        float peak = floor;
        for (int t = 0; t < 150; t++)
        {
            rig.Tick();
            peak = MathF.Max(peak, body.Position.Z);
        }

        float v = (float)expected.Z;
        Assert.InRange(peak - floor, v * v / 1600f - 3f, v * v / 1600f + 3f);
        Assert.True(rig.Motion.Landed(body.Index).Ever);
        Assert.Equal(floor, body.Position.Z, 1);
    }

    [Fact]
    public void A_bullet_does_not_push()
    {
        var rig = new ServerHordeRig();
        var push = new RobotBlastPush(new RigRules(), rig.Motion);
        rig.HitMembers.Add(push);
        var (_, body) = rig.Lent("soldier");

        body.OnDamagedAt(new EntityHandle(1u), 10f, DamageType.Bullet, default, new Vector(0f, 0f, 540f), HitGroup.Chest);

        Assert.Equal(0, push.Pushed);
    }

    [Fact]
    public void A_giant_robot_moves_by_its_scaled_hull()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("giant_heavy", 1.75);
        rig.World.Put(new Vector(body.Position.X + 100, -1000, -1), new Vector(body.Position.X + 140, 1000, 300));
        rig.Motion.Order(body.Index, FarAlongX(body.Position), LookIntent.AlongPath, long.MaxValue);
        float wall = body.Position.X + 100;

        rig.Tick(120);

        Assert.InRange(body.Position.X, wall - 42f - 6f, wall - 42f);
        Assert.Equal(BlockerKind.World, rig.Motion.Blocked(body.Index).Kind);
    }
}
