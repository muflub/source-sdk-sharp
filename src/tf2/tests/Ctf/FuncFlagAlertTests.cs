using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Ctf;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Ctf;

public sealed class FuncFlagAlertTests
{
    private static (CtfRig Rig, FuncFlagAlert Zone, ItemTeamFlag Red, ItemTeamFlag Blue) Map(
        params (string Key, string Value)[] keys)
    {
        var rig = new CtfRig();
        rig.Engine.Returns("Entities.IsValid", true);
        var zone = rig.Spawn<FuncFlagAlert>(FuncFlagAlert.MapClass, CtfRig.Ground, keys);
        return (rig, zone, rig.Flag(TFTeam.Red), rig.Flag(TFTeam.Blue));
    }

    /// <summary>A blue zone and a red carrier holding the blue flag, past any gate.</summary>
    private static (CtfRig Rig, FuncFlagAlert Zone, ItemTeamFlag Red, EntityHandle Carrier) BlueZoneRedCarrier()
    {
        var (rig, zone, red, blue) = Map(("TeamNum", "3"));
        var carrier = rig.Players.Add(3, TFTeam.Red);
        rig.Registry.Touch(blue.Handle, carrier);
        return (rig, zone, red, carrier);
    }

    private int Broadcasts(CtfRig rig, string line)
        => rig.RoundAnnouncer.Said.Count(s => s == $"broadcast {line} Announcer.SecurityAlert");

    // ---- the outputs ---------------------------------------------------------

    [Fact]
    public void A_red_carriers_entry_fires_OnTriggeredByTeam1_and_tells_blue()
    {
        var (rig, zone, _, carrier) = BlueZoneRedCarrier();

        rig.Registry.Touch(zone.Handle, carrier);

        Assert.Equal(1, zone.OnTriggeredByTeam1.FireCount);
        Assert.Equal(0, zone.OnTriggeredByTeam2.FireCount);
        Assert.Equal(1, Broadcasts(rig, "Blue"));
        // The base's own outputs speak too (BaseClass::StartTouch).
        Assert.Equal(1, zone.OnStartTouch.FireCount);
    }

    [Fact]
    public void A_blue_carriers_entry_fires_OnTriggeredByTeam2_and_tells_red()
    {
        var (rig, zone, red, _) = Map(("TeamNum", "2"));
        var carrier = rig.Players.Add(4, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, carrier);

        rig.Registry.Touch(zone.Handle, carrier);

        Assert.Equal(1, zone.OnTriggeredByTeam2.FireCount);
        Assert.Equal(0, zone.OnTriggeredByTeam1.FireCount);
        Assert.Equal(1, Broadcasts(rig, "Red"));
    }

    [Fact]
    public void A_player_without_the_flag_alerts_nothing_but_the_base_still_hears_them()
    {
        var (rig, zone, _, carrier) = BlueZoneRedCarrier();
        var plain = rig.Players.Add(4, TFTeam.Red);

        rig.Registry.Touch(zone.Handle, plain);

        Assert.Equal(0, zone.OnTriggeredByTeam1.FireCount);
        Assert.Equal(0, Broadcasts(rig, "Blue"));
        Assert.Equal(1, zone.OnStartTouch.FireCount);
    }

    [Fact]
    public void A_carrier_of_the_zones_own_team_alerts_nothing()
    {
        var (rig, zone, _, blue) = Map(("TeamNum", "3"));
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(blue.Handle, carrier);

        rig.Registry.Touch(zone.Handle, carrier);

        Assert.Equal(0, zone.OnTriggeredByTeam1.FireCount);
        Assert.Equal(0, zone.OnTriggeredByTeam2.FireCount);
        Assert.Empty(rig.RoundAnnouncer.Said);
    }

    // ---- the per-team gate ---------------------------------------------------

    [Fact]
    public void The_same_team_does_not_alert_again_within_the_delay()
    {
        var (rig, zone, _, carrier) = BlueZoneRedCarrier();
        rig.Registry.Touch(zone.Handle, carrier);
        rig.Registry.EndTouch(zone.Handle, carrier);
        rig.Clock.Now = 15f; // 10 (entry) + 10 (default delay): not yet past it

        rig.Registry.Touch(zone.Handle, carrier);

        Assert.Equal(1, zone.OnTriggeredByTeam1.FireCount);
    }

    [Fact]
    public void A_team_alerts_again_once_its_delay_has_passed()
    {
        var (rig, zone, _, carrier) = BlueZoneRedCarrier();
        rig.Registry.Touch(zone.Handle, carrier);
        rig.Registry.EndTouch(zone.Handle, carrier);
        rig.Clock.Now = 20.5f;

        rig.Registry.Touch(zone.Handle, carrier);

        Assert.Equal(2, zone.OnTriggeredByTeam1.FireCount);
        Assert.Equal(2, Broadcasts(rig, "Blue"));
    }

    [Fact]
    public void One_teams_alert_never_holds_the_other_teams_back()
    {
        // No TeamNum: the zone is nobody's, so both teams' entries count, as
        // stock's team-number compare does.
        var (rig, zone, red, blue) = Map();
        var redCarrier = rig.Players.Add(3, TFTeam.Red);
        var blueCarrier = rig.Players.Add(4, TFTeam.Blue);
        rig.Registry.Touch(blue.Handle, redCarrier);
        rig.Registry.Touch(red.Handle, blueCarrier);

        rig.Registry.Touch(zone.Handle, redCarrier);
        rig.Registry.Touch(zone.Handle, blueCarrier);

        Assert.Equal(1, zone.OnTriggeredByTeam1.FireCount);
        Assert.Equal(1, zone.OnTriggeredByTeam2.FireCount);
    }

    [Fact]
    public void The_alert_delay_key_is_the_seconds_a_team_waits()
    {
        var (rig, zone, _, carrier) = BlueZoneRedCarrier();
        var keyed = rig.Spawn<FuncFlagAlert>(FuncFlagAlert.MapClass, CtfRig.Ground, ("TeamNum", "3"), ("alert_delay", "30"));
        rig.Registry.Touch(keyed.Handle, carrier);
        rig.Registry.EndTouch(keyed.Handle, carrier);
        rig.Clock.Now = 25f;

        rig.Registry.Touch(keyed.Handle, carrier);

        Assert.Equal(1, keyed.OnTriggeredByTeam1.FireCount);

        rig.Clock.Now = 41f;
        rig.Registry.Touch(keyed.Handle, carrier);

        Assert.Equal(2, keyed.OnTriggeredByTeam1.FireCount);
        Assert.Equal(30, keyed.AlertDelay);
    }

    // ---- the keys ------------------------------------------------------------

    [Fact]
    public void The_sound_is_on_and_the_delay_ten_without_keys()
    {
        var (_, zone, _, _) = Map();

        Assert.True(zone.PlaysSound);
        Assert.Equal(10, zone.AlertDelay);
    }

    [Fact]
    public void Playsound_no_silences_the_broadcast_but_not_the_outputs()
    {
        var (rig, _, _, carrier) = BlueZoneRedCarrier();
        var quiet = rig.Spawn<FuncFlagAlert>(FuncFlagAlert.MapClass, CtfRig.Ground, ("TeamNum", "3"), ("playsound", "0"));

        rig.Registry.Touch(quiet.Handle, carrier);

        Assert.Empty(rig.RoundAnnouncer.Said);
        Assert.Equal(1, quiet.OnTriggeredByTeam1.FireCount);
    }

    [Fact]
    public void The_TeamNum_key_is_the_zones_team()
    {
        var (_, zone, _, _) = Map(("TeamNum", "2"));

        Assert.Equal(TFTeam.Red, zone.Team);
    }

    // ---- the base's gates ----------------------------------------------------

    [Fact]
    public void A_disabled_zone_hears_nobody()
    {
        var (rig, zone, _, carrier) = BlueZoneRedCarrier();
        Assert.True(rig.Registry.Input(zone.Handle, "Disable", string.Empty, 0f, EntityHandle.None));

        rig.Registry.Touch(zone.Handle, carrier);

        Assert.Equal(0, zone.OnTriggeredByTeam1.FireCount);
        Assert.Equal(0, zone.OnStartTouch.FireCount);
        Assert.Empty(rig.RoundAnnouncer.Said);
    }

    // ---- what the outputs name as their activator ----------------------------

    [Fact]
    public void The_team_outputs_name_the_zone_as_their_activator()
    {
        var (rig, zone, _, carrier) = BlueZoneRedCarrier();

        // A wired connection is the only way to see who an output named: the
        // stock line is FireOutput( this, this ) — the zone, twice
        // (func_flag_alert.cpp:77-82).
        rig.Registry.RegisterAll(typeof(FuncFlagAlertTests).Assembly);
        var probeHandle = rig.NewHandle();
        rig.Registry.KeyValue(probeHandle, AlertActivatorProbe.MapClass, "targetname", "alert_probe");
        rig.Registry.Spawn(probeHandle, AlertActivatorProbe.MapClass);
        rig.Registry.Directory.Connect(zone.Handle, "OnTriggeredByTeam1", "alert_probe,Land,,0,-1");

        rig.Registry.Touch(zone.Handle, carrier);
        rig.Registry.Directory.ServiceIo(rig.Clock.Now);

        var heard = ((AlertActivatorProbe)rig.Registry.Get(probeHandle)!).Activators;
        Assert.Single(heard);
        Assert.Equal(zone.Handle, heard[0]);
    }
}

/// <summary>Records every input's activator, for the output-activator fact.</summary>
[ScriptedEntity(AlertActivatorProbe.MapClass, ScriptedEntityKind.Logical)]
public sealed class AlertActivatorProbe : ScriptedEntity
{
    public const string MapClass = "test_alert_activator_probe";

    public readonly List<EntityHandle> Activators = new();

    public override bool OnInput(string name, string value, float number, EntityHandle activator)
    {
        Activators.Add(activator);
        return true;
    }
}
