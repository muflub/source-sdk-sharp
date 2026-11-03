using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// trigger_timer_door: the capture area that paces a door with the capture
/// (stock's CTriggerTimerDoor, tf_triggers.cpp L238-332). On the first touch
/// it finds the door its <c>door_name</c> names and sets the door's speed to
/// the closed-to-open travel over <c>area_time_to_cap</c> (L288-301), so the
/// gate is fully open when the capture completes; a touch that names no door
/// never starts a capture, and the cap never scales with mp_capstyle (L251).
/// </summary>
public sealed class TriggerTimerDoorTests
{
    // A brush whose collision bounds report 130 x 66 x 130: 128 of travel along
    // +x, the default movedir, once the engine's grown-by-one unit is off.
    private static readonly Vector Bounds = new(130f, 66f, 130f);

    private static (ObjectiveRig Rig, FuncDoor Door, TriggerTimerDoor Area) Rig(float capTime = 8f, int capStyle = 0,
                                                                                string doorName = "gate1",
                                                                                float doorSpeed = 4f,
                                                                                bool startDisabled = false,
                                                                                bool missingDoor = false)
    {
        var rig = new ObjectiveRig();
        rig.Settings.CapStyle = capStyle;
        rig.Engine.Returns("Entities.GetSpawnOrigin", new Vector(100f, 200f, 8f))
                  .Returns("Entities.GetBoundsSize", Bounds)
                  .Returns("Entities.IsValid", true);
        rig.Registry.RegisterAll(typeof(FuncDoor).Assembly);

        var doorKeys = new List<(string, string)> { ("targetname", "gate1"), ("speed", Num(doorSpeed)) };
        if (startDisabled)
            doorKeys.Add(("StartDisabled", "1"));
        var door = rig.Spawn<FuncDoor>(EntityNames.FuncDoor, doorKeys.ToArray());

        var areaKeys = new List<(string, string)>
        {
            ("area_cap_point", "cp_a"), ("area_time_to_cap", Num(capTime)),
            ("team_cancap_2", "1"), ("team_cancap_3", "1"), ("door_name", missingDoor ? "nowhere" : doorName)
        };
        if (startDisabled)
            areaKeys.Add(("StartDisabled", "1"));
        var area = rig.Spawn<TriggerTimerDoor>(TriggerTimerDoor.MapClass, areaKeys.ToArray());

        _ = rig.Point("cp_a", 0);
        rig.StartRound();
        return (rig, door, area);
    }

    private static string Num(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void TheFirstTouchTimesTheDoorToTheCap()
    {
        // 128 of travel over an 8 s cap: 16 units a second, from the map's 4.
        var (rig, door, area) = Rig(capTime: 8f, doorSpeed: 4f);
        var red = rig.Players.Add(1, TFTeam.Red);

        Assert.Equal(4f, door.Vars.Speed);
        rig.Registry.Touch(area.Handle, red);

        Assert.Equal(16f, door.Vars.Speed);
    }

    [Fact]
    public void TheDoorIsTunedOnceAndLaterTouchesLeaveThePace()
    {
        var (rig, door, area) = Rig(capTime: 8f);
        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));
        Assert.Equal(16f, door.Vars.Speed);

        rig.Registry.Touch(area.Handle, rig.Players.Add(2, TFTeam.Red));

        Assert.Equal(16f, door.Vars.Speed);
    }

    [Fact]
    public void ATouchThatNamesNoDoorStartsNoCapture()
    {
        var (rig, _, area) = Rig(missingDoor: true);
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red);

        Assert.False(area.IsCapturing);
    }

    [Fact]
    public void TheCaptureRunsAtTheFlatCapTimeHoweverMpCapstyleIsSet()
    {
        // mp_capstyle 1 would open on 2 T here; the timer door pins
        // CaptureModeScalesWithPlayers off, so the cap is T (L251).
        var (rig, _, area) = Rig(capTime: 8f, capStyle: 1);

        rig.Registry.Touch(area.Handle, rig.Players.Add(1, TFTeam.Red));

        Assert.True(area.IsCapturing);
        Assert.Equal(8f, area.TimeRemaining);
    }

    [Fact]
    public void ADissabledTriggerTunesNothing()
    {
        var (rig, door, area) = Rig(doorSpeed: 4f, startDisabled: true);
        var red = rig.Players.Add(1, TFTeam.Red);

        rig.Registry.Touch(area.Handle, red);

        Assert.Equal(4f, door.Vars.Speed);
        Assert.False(area.IsCapturing);
    }
}
