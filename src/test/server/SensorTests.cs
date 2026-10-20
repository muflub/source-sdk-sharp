using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The angular velocity sensor's debounce.
///
/// Worth testing on its own because it is the whole entity: reading a physics
/// object is one call, and everything else is deciding when a change is real.
/// dm_lockdown's fan drives sixteen output connections through this, so a
/// sensor that fired on every wobble would fire all of them.
/// </summary>
public class SensorTests
{
    private static readonly EntityHandle Ent = new(1);

    private static PointAngularVelocitySensor Spawn(float threshold, float interval)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PointAngularVelocitySensor).Assembly);
        registry.Services = Services();
        registry.Spawn(Ent, EntityNames.PointAngularvelocitysensor);

        var sensor = (PointAngularVelocitySensor)registry.Get(Ent)!;
        sensor.Configure(threshold, interval);
        return sensor;
    }

    [Fact]
    public void Thresholds()
    {
        var sensor = Spawn(threshold: 100f, interval: 0f);

        Assert.True(sensor.Classify(150f) == 1, "above the threshold classifies as greater");
        Assert.True(sensor.Classify(50f) == -1, "below the threshold classifies as less");

        // Exactly on it is its own answer, not "not greater": the map has a
        // separate output for it.
        Assert.True(sensor.Classify(100f) == 0, "exactly the threshold classifies as equal");
    }

    [Fact]
    public void Debounce()
    {
        var sensor = Spawn(threshold: 100f, interval: 1f);

        // The first reading establishes a baseline. Firing on it would announce
        // a change that never happened — the fan was always stopped.
        sensor.Observe(0f, now: 0f);
        Assert.True(sensor.OnLessThan.FireCount == 0, "the first reading fires nothing");

        sensor.Observe(500f, now: 0f);
        Assert.True(sensor.OnGreaterThan.FireCount == 0, "a change alone does not fire");

        sensor.Observe(500f, now: 0.5f);
        Assert.True(sensor.OnGreaterThan.FireCount == 0, "a change that has not held long enough does not fire");

        sensor.Observe(500f, now: 1.0f);
        Assert.True(sensor.OnGreaterThan.FireCount == 1, "a change that holds out the interval fires");

        // Once fired it must not keep firing: the outputs are events, and this
        // runs every tick.
        sensor.Observe(500f, now: 2.0f);
        Assert.True(sensor.OnGreaterThan.FireCount == 1, "holding steady after firing does not fire again");

        // The inclusive outputs go with it, which is what lets a map wire
        // "at or above" without a second entity.
        Assert.True(sensor.OnGreaterThanOrEqualTo.FireCount == 1, "greater also reports greater-or-equal");
    }

    [Fact]
    public void Oscillation()
    {
        var sensor = Spawn(threshold: 100f, interval: 1f);

        sensor.Observe(0f, now: 0f);
        sensor.Observe(500f, now: 0f);
        sensor.Observe(500f, now: 1f);
        Assert.True(sensor.OnGreaterThan.FireCount == 1, "the sensor has fired once");

        // Dipping below and coming back before the interval expires: the world
        // never saw it leave, so it must not be announced as arriving again.
        sensor.Observe(50f, now: 1.1f);
        sensor.Observe(500f, now: 1.2f);
        sensor.Observe(500f, now: 3.0f);

        Assert.True(sensor.OnLessThan.FireCount == 0 && sensor.OnGreaterThan.FireCount == 1, "a brief dip fires neither output");

        // A dip that lasts, however, is real.
        sensor.Observe(50f, now: 4.0f);
        sensor.Observe(50f, now: 5.5f);
        Assert.True(sensor.OnLessThan.FireCount == 1, "a dip that holds out the interval fires");
    }
}

/// <summary>
/// The angle sensor's hold, which is the only part of it that is a decision.
///
/// Reading a facing is two engine calls and a dot product; deciding that a
/// stare has lasted long enough, and that looking away resets it, is the entity.
/// </summary>
public class PointSensorTests
{
    private static readonly EntityHandle Ent = new(2);

    private static PointAngleSensor Spawn(float duration)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PointAngleSensor).Assembly);
        registry.Services = Services();
        registry.Spawn(Ent, EntityNames.PointAnglesensor);

        var sensor = (PointAngleSensor)registry.Get(Ent)!;
        sensor.Configure(duration);
        return sensor;
    }

    //-------------------------------------------------------------------------
    // THESE WERE NOT FACTS. All four were `internal static void`, called only
    // from SensorTests.Covers -- so this class ran nothing of its own, and
    // everything it checks was reported under another class's name. They are
    // ordinary facts now, split one behaviour each.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheMomentItStartsFacingFiresNothing()
    {
        var sensor = Spawn(duration: 2f);

        sensor.Judge(facing: true, now: 10f);

        Assert.True(sensor.OnFacingLookat.FireCount == 0, "the moment it starts facing fires nothing");
    }

    [Fact]
    public void AStareShortOfTheDurationFiresNothing()
    {
        var sensor = Spawn(duration: 2f);

        sensor.Judge(facing: true, now: 10f);
        sensor.Judge(facing: true, now: 11.9f);

        Assert.True(sensor.OnFacingLookat.FireCount == 0, "a stare short of the duration fires nothing");
    }

    [Fact]
    public void AStareThatLastsTheDurationFires()
    {
        var sensor = Spawn(duration: 2f);

        sensor.Judge(facing: true, now: 10f);
        sensor.Judge(facing: true, now: 11.9f);
        sensor.Judge(facing: true, now: 12f);

        Assert.True(sensor.OnFacingLookat.FireCount == 1, "a stare that lasts the duration fires");
    }

    [Fact]
    public void HoldingTheStareDoesNotFireAgain()
    {
        // The output is an event and this runs every tick, so holding must not
        // keep firing it.
        var sensor = Spawn(duration: 2f);

        sensor.Judge(facing: true, now: 10f);
        sensor.Judge(facing: true, now: 12f);
        sensor.Judge(facing: true, now: 20f);

        Assert.True(sensor.OnFacingLookat.FireCount == 1, "holding the stare does not fire again");
    }

    [Fact]
    public void LookingAwayRestartsTheHold()
    {
        // Looking away RESTARTS the count rather than pausing it, which is what
        // "pointed at it for two seconds" means. A sensor that resumed would
        // fire for somebody who glanced twice.
        var sensor = Spawn(duration: 2f);

        sensor.Judge(facing: true, now: 0f);
        sensor.Judge(facing: true, now: 1.5f);
        sensor.Judge(facing: false, now: 1.6f);
        sensor.Judge(facing: true, now: 1.7f);
        sensor.Judge(facing: true, now: 2.0f);

        Assert.True(sensor.OnFacingLookat.FireCount == 0, "looking away restarts the hold");
    }

    [Fact]
    public void AndTheRestartedHoldFiresOnItsOwnSchedule()
    {
        var sensor = Spawn(duration: 2f);

        sensor.Judge(facing: true, now: 0f);
        sensor.Judge(facing: true, now: 1.5f);
        sensor.Judge(facing: false, now: 1.6f);
        sensor.Judge(facing: true, now: 1.7f);
        sensor.Judge(facing: true, now: 2.0f);
        sensor.Judge(facing: true, now: 3.7f);

        Assert.True(sensor.OnFacingLookat.FireCount == 1, "and the restarted hold fires on its own schedule");
    }

    // A tolerance of 60 degrees is a dot of 0.5.

    [Fact]
    public void DeadOnReportsOne()
        => Assert.True(PointAngleSensor.Percentage(1f, 0.5f) == 1f, "dead on reports 1");

    [Fact]
    public void AtTheEdgeOfToleranceReportsZero()
        => Assert.True(PointAngleSensor.Percentage(0.5f, 0.5f) == 0f, "at the edge of tolerance reports 0");

    [Fact]
    public void HalfwayAcrossTheConeReportsAHalf()
        => Assert.True(MathF.Abs(PointAngleSensor.Percentage(0.75f, 0.5f) - 0.5f) < 0.0001f, "halfway across the cone reports a half");

    [Fact]
    public void OutsideTheConeClampsToZero()
    {
        // Outside the cone would be negative without the clamp, and the number
        // is networked to whatever the map wired it to.
        Assert.True(PointAngleSensor.Percentage(-1f, 0.5f) == 0f, "outside the cone clamps to 0");
    }

    [Fact]
    public void AZeroWidthConeDoesNotDivideByZero()
    {
        // A zero-degree tolerance leaves no span to interpolate across; the
        // division would be a NaN rather than a small number.
        Assert.True(PointAngleSensor.Percentage(0.9f, 1f) == 0f && PointAngleSensor.Percentage(1f, 1f) == 1f, "a zero-width cone does not divide by zero");
    }

    private static readonly Vector Axis = new(1f, 0f, 0f);

    [Fact]
    public void DistanceAlongTheAxisIsTheComponentOnIt()
        => Assert.True(PointProximitySensor.AlongAxis(new Vector(30f, 40f, 0f), Axis) == 30f, "distance along the axis is the component on it");

    [Fact]
    public void TheSameDistanceBehindReadsTheSame()
    {
        // Unsigned, because the output is a DISTANCE. The same distance behind
        // would compare as smaller than everything if it came back negative.
        Assert.True(PointProximitySensor.AlongAxis(new Vector(-30f, 40f, 0f), Axis) == 30f, "the same distance behind reads the same");
    }

    [Fact]
    public void NothingAlongTheAxisIsZeroAwayAlongIt()
        => Assert.True(PointProximitySensor.AlongAxis(new Vector(0f, 40f, 0f), Axis) == 0f, "nothing along the axis is zero away along it");

    [Fact]
    public void AFiredVectorReadsBackAsItself()
    {
        // The display form Vector.ToString gives has brackets and commas in it,
        // and a map reading that back gets a zero. The check is that what this
        // writes is what the map's own parser reads.
        var v = new Vector(1.5f, -2.25f, 0f);
        var parsed = TriggerPush.ParseVector(PointAngleSensor.FormatVector(v));

        Assert.True(parsed.X == v.X && parsed.Y == v.Y && parsed.Z == v.Z, "a fired vector reads back as itself");
    }
}
