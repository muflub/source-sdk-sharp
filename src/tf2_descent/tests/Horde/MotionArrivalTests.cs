using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The motion's arrival (the brain's <c>Arrived</c>, plan_descent_logic 3.6): a body that reaches
/// its order's last corner is reported once, a re-sent order to the same place reports nothing
/// more, and the session tells the body's brain the next tick.
/// </summary>
public sealed class MotionArrivalTests
{
    [Fact]
    public void A_body_that_reaches_its_orders_last_corner_is_reported_arrived_once()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        Assert.False(rig.Motion.Arrived(1).Ever);

        rig.Motion.Order(1, MotionRig.Follow(At(200, 0), At(200, 200)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(200);

        var arrived = rig.Motion.Arrived(1);
        Assert.True(arrived.Ever, $"never arrived: at {rig.At(1)}");
        Assert.Equal(1, arrived.Count);
        Assert.True((arrived.At - At(200, 200)).Length2D < 40f, $"arrived at {arrived.At}");

        // The brain re-sends its order each decide: no second arrival at the same place.
        rig.Motion.Order(1, MotionRig.Follow(At(200, 200)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(30);
        Assert.Equal(1, rig.Motion.Arrived(1).Count);

        rig.Motion.Order(1, MotionRig.Follow(At(0, 200)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(200);
        Assert.Equal(2, rig.Motion.Arrived(1).Count);
        Assert.Equal(2, rig.Motion.Arrivals);
    }

    [Fact]
    public void A_body_lent_again_starts_with_no_arrival()
    {
        var rig = new MotionRig();
        rig.Stand(1, At(0, 0));
        rig.Motion.Order(1, MotionRig.Follow(At(100, 0)), LookIntent.AlongPath, long.MaxValue);
        rig.Tick(120);
        Assert.True(rig.Motion.Arrived(1).Ever);

        rig.Stand(1, At(500, 0));

        Assert.False(rig.Motion.Arrived(1).Ever);
    }

    [Fact]
    public void An_arrival_is_an_arrived_event_for_the_bodys_brain_with_where_it_stood()
    {
        var log = new EventLog();
        var rig = new BrainRig(sink: log);
        int body = rig.Lend(rig.Robot(Centre(0)));
        rig.Slide = false;
        rig.Tick();

        rig.Brains.Arrived(rig.Now, body, Centre(0) + At(10, 0));

        var e = Assert.Single(log.Events, x => x.Kind == AiEventKind.Arrived);
        Assert.Equal(rig.Brains.BrainAt(body), e.Bot);
        Assert.Equal(Centre(0) + At(10, 0), e.Position);
        Assert.Equal(1, rig.Brains.ArrivalsTold);
    }

    [Fact]
    public void An_arrival_of_a_body_without_a_brain_is_told_to_nobody()
    {
        var log = new EventLog();
        var rig = new BrainRig(sink: log);

        rig.Brains.Arrived(rig.Now, 3, Centre(0));

        Assert.DoesNotContain(log.Events, x => x.Kind == AiEventKind.Arrived);
        Assert.Equal(0, rig.Brains.ArrivalsTold);
    }
}
