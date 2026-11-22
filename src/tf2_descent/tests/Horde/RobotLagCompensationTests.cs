using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The robot bodies opt into the lag compensation (plan_descent_logic CR7,
/// <see cref="ScriptedEntity.LagCompensated"/>): a lent, living body is moved
/// back for a lagged hero's shot; a parked or dead one is not; and a body that
/// is parked or lent starts a fresh history, so no shot is rewound into
/// another robot's past.
/// </summary>
public sealed class RobotLagCompensationTests
{
    [Fact]
    public void A_lent_body_is_lag_compensated()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");

        Assert.True(body.LagCompensated);
    }

    [Fact]
    public void A_parked_body_is_not_lag_compensated()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");

        Assert.True(rig.Controller.ReturnByHand(id.Index, rig.Now, out _));

        Assert.False(body.LagCompensated);
    }

    [Fact]
    public void A_dead_robots_body_is_not_lag_compensated()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("scout");

        body.OnDamagedAt(EntityHandle.None, 1_000_000f, DamageType.Bullet, default, default);

        Assert.Equal(0, body.EngineHealth);
        Assert.False(body.LagCompensated);
    }

    [Fact]
    public void A_lent_body_declares_its_robots_top_speed()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("scout");

        Assert.True(body.LagCompensationMaxSpeed > 0f);
        Assert.Equal((float)rig.Roster.Stats(id.Index).Speed, body.LagCompensationMaxSpeed);
    }

    [Fact]
    public void A_lend_starts_a_fresh_lag_history()
    {
        var rig = new ServerHordeRig();
        var parked = new Dictionary<EntityHandle, int>();
        for (int b = 1; b <= rig.Bodies.LastBody; b++)
            parked[rig.Bodies.Body(b).Handle] = rig.Bodies.Body(b).LagHistorySerial;

        var (_, body) = rig.Lent("soldier");

        Assert.NotEqual(parked[body.Handle], body.LagHistorySerial);
    }

    [Fact]
    public void A_park_starts_a_fresh_lag_history()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        int lent = body.LagHistorySerial;

        Assert.True(rig.Controller.ReturnByHand(id.Index, rig.Now, out _));

        Assert.NotEqual(lent, body.LagHistorySerial);
    }

    [Fact]
    public void The_core_finds_exactly_the_lent_bodies()
    {
        var rig = new ServerHordeRig();
        var (_, first) = rig.Lent("soldier");
        var (_, second) = rig.Lent("heavy");
        var found = new List<LagCompensatedEntity>();

        new ScriptedLagCompensatedEntities(rig.Registry.Directory).Collect(found);

        Assert.Equal(new[] { first.Handle, second.Handle }.OrderBy(h => h.Value),
                     found.Select(e => e.Handle).OrderBy(h => h.Value));
    }
}
