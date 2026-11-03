using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Ctf;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Ctf;

public sealed class FuncFlagDetectionZoneTests
{
    private static (CtfRig Rig, FuncFlagDetectionZone Zone, ItemTeamFlag Red, ItemTeamFlag Blue) Map()
    {
        var rig = new CtfRig();
        // The carrier entry fires a game event; the stub answers none. The
        // end-of-touch sweep asks whether a remembered carrier still exists.
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        rig.Engine.Returns("Entities.IsValid", true);
        var zone = rig.Spawn<FuncFlagDetectionZone>(FuncFlagDetectionZone.MapClass, CtfRig.Ground, ("alarm", "1"));
        return (rig, zone, rig.Flag(TFTeam.Red), rig.Flag(TFTeam.Blue));
    }

    /// <summary>Two carriers: blue holding the red flag, red the blue one.</summary>
    private static (EntityHandle A, EntityHandle B) Carriers(CtfRig rig, ItemTeamFlag red, ItemTeamFlag blue)
    {
        EntityHandle a = rig.Players.Add(3, TFTeam.Blue);
        EntityHandle b = rig.Players.Add(4, TFTeam.Red);
        rig.Registry.Touch(red.Handle, a);
        rig.Registry.Touch(blue.Handle, b);
        return (a, b);
    }

    // ---- the volume outputs -------------------------------------------------

    [Fact]
    public void A_carriers_entry_fires_OnStartTouch_and_the_first_also_the_flag_pair()
    {
        var (rig, zone, red, blue) = Map();
        var (a, _) = Carriers(rig, red, blue);

        rig.Registry.Touch(zone.Handle, a);

        Assert.Equal(1, zone.OnStartTouch.FireCount);
        Assert.Equal(1, zone.OnStartTouchFlag.FireCount);
        Assert.Equal(1, zone.OnStartTouchAll.FireCount);
        Assert.Equal(0, zone.OnEndTouchFlag.FireCount);
    }

    [Fact]
    public void A_second_carrier_fires_OnStartTouch_again_but_not_the_first_carriers_pair()
    {
        var (rig, zone, red, blue) = Map();
        (EntityHandle a, EntityHandle b) = Carriers(rig, red, blue);
        rig.Registry.Touch(zone.Handle, a);

        rig.Registry.Touch(zone.Handle, b);

        Assert.Equal(2, zone.OnStartTouch.FireCount);
        Assert.Equal(1, zone.OnStartTouchFlag.FireCount);
        Assert.Equal(1, zone.OnStartTouchAll.FireCount);
    }

    [Fact]
    public void A_player_without_the_flag_fires_nothing_and_fires_no_event()
    {
        var (rig, zone, _, _) = Map();
        var plain = rig.Players.Add(3, TFTeam.Red);

        rig.Registry.Touch(zone.Handle, plain);

        Assert.Equal(0, zone.OnStartTouch.FireCount);
        Assert.Equal(0, zone.OnStartTouchFlag.FireCount);
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    [Fact]
    public void A_carriers_entry_tells_the_game_event_bus_who_is_carrier()
    {
        var (rig, zone, red, blue) = Map();
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(42));
        rig.Engine.Returns("GameEvents.EventFire", true);
        var (a, _) = Carriers(rig, red, blue);

        rig.Registry.Touch(zone.Handle, a);

        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventCreate"));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    [Fact]
    public void A_carriers_exit_fires_OnEndTouch_and_only_the_last_leaving_fires_the_flag_pair()
    {
        var (rig, zone, red, blue) = Map();
        (EntityHandle a, EntityHandle b) = Carriers(rig, red, blue);
        rig.Registry.Touch(zone.Handle, a);
        rig.Registry.Touch(zone.Handle, b);

        rig.Registry.EndTouch(zone.Handle, a);

        Assert.Equal(1, zone.OnLeft.FireCount);
        Assert.Equal(0, zone.OnEndTouchFlag.FireCount);

        rig.Registry.EndTouch(zone.Handle, b);

        Assert.Equal(2, zone.OnLeft.FireCount);
        Assert.Equal(1, zone.OnEndTouchFlag.FireCount);
        Assert.Equal(1, zone.OnEndTouchAll.FireCount);
    }

    [Fact]
    public void A_player_without_the_flag_leaving_fires_nothing()
    {
        var (rig, zone, _, _) = Map();
        var plain = rig.Players.Add(3, TFTeam.Red);
        rig.Registry.Touch(zone.Handle, plain);

        rig.Registry.EndTouch(zone.Handle, plain);

        Assert.Equal(0, zone.OnLeft.FireCount);
        Assert.Equal(0, zone.OnEndTouchFlag.FireCount);
        Assert.Equal(0, zone.OnEndTouchAll.FireCount);
    }

    [Fact]
    public void A_carrier_who_died_inside_does_not_hold_the_zone_when_the_last_other_leaves()
    {
        var (rig, zone, red, blue) = Map();
        (EntityHandle a, EntityHandle b) = Carriers(rig, red, blue);
        rig.Registry.Touch(zone.Handle, a);
        rig.Registry.Touch(zone.Handle, b);
        rig.Players[b].Alive = false;

        rig.Registry.EndTouch(zone.Handle, a);

        // The sweep on a's exit finds b dead: the zone is empty for the outputs.
        Assert.Equal(1, zone.OnEndTouchFlag.FireCount);
        Assert.Equal(1, zone.OnEndTouchAll.FireCount);
    }

    // ---- the inputs ---------------------------------------------------------

    [Fact]
    public void The_Test_input_speaks_from_the_carriers_it_still_has()
    {
        var (rig, zone, red, blue) = Map();
        (EntityHandle a, _) = Carriers(rig, red, blue);
        rig.Registry.Touch(zone.Handle, a);

        Assert.True(rig.Registry.Input(zone.Handle, "Test", string.Empty, 0f, EntityHandle.None));

        // The survivor is named as the start touch, and the flag output fires
        // again over it; the zone is not empty, so no end speaks.
        Assert.Equal(2, zone.OnStartTouch.FireCount);
        Assert.Equal(2, zone.OnStartTouchFlag.FireCount);
        Assert.Equal(0, zone.OnEndTouchFlag.FireCount);
    }

    [Fact]
    public void The_Test_input_over_an_empty_zone_speaks_the_end()
    {
        var (rig, zone, _, _) = Map();

        Assert.True(rig.Registry.Input(zone.Handle, "Test", string.Empty, 0f, EntityHandle.None));

        Assert.Equal(1, zone.OnEndTouchFlag.FireCount);
        Assert.Equal(1, zone.OnEndTouchAll.FireCount);
        Assert.Equal(0, zone.OnStartTouch.FireCount);
    }

    [Fact]
    public void The_Test_input_sweeps_a_carrier_who_died_inside()
    {
        var (rig, zone, red, blue) = Map();
        (EntityHandle a, _) = Carriers(rig, red, blue);
        rig.Registry.Touch(zone.Handle, a);
        rig.Players[a].Alive = false;

        Assert.True(rig.Registry.Input(zone.Handle, "Test", string.Empty, 0f, EntityHandle.None));

        Assert.Equal(1, zone.OnEndTouchFlag.FireCount);
    }

    [Fact]
    public void A_zone_keyed_StartDisabled_hears_nothing()
    {
        var rig = new CtfRig();
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        var zone = rig.Spawn<FuncFlagDetectionZone>(FuncFlagDetectionZone.MapClass, CtfRig.Ground, ("StartDisabled", "1"));
        (EntityHandle a, _) = Carriers(rig, rig.Flag(TFTeam.Red), rig.Flag(TFTeam.Blue));

        rig.Registry.Touch(zone.Handle, a);

        Assert.Equal(0, zone.OnStartTouch.FireCount);
        Assert.Equal(0, zone.OnStartTouchFlag.FireCount);
    }

    [Fact]
    public void The_Enable_input_arms_a_disabled_zone()
    {
        var rig = new CtfRig();
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        var zone = rig.Spawn<FuncFlagDetectionZone>(FuncFlagDetectionZone.MapClass, CtfRig.Ground, ("StartDisabled", "1"));
        (EntityHandle a, _) = Carriers(rig, rig.Flag(TFTeam.Red), rig.Flag(TFTeam.Blue));
        Assert.True(rig.Registry.Input(zone.Handle, "Enable", string.Empty, 0f, EntityHandle.None));

        rig.Registry.Touch(zone.Handle, a);

        Assert.Equal(1, zone.OnStartTouchFlag.FireCount);
    }

    [Fact]
    public void The_Disable_input_silences_a_live_zone()
    {
        var (rig, zone, red, blue) = Map();
        (EntityHandle a, EntityHandle b) = Carriers(rig, red, blue);
        rig.Registry.Touch(zone.Handle, a);
        Assert.True(rig.Registry.Input(zone.Handle, "Disable", string.Empty, 0f, EntityHandle.None));

        rig.Registry.Touch(zone.Handle, b);

        Assert.Equal(1, zone.OnStartTouch.FireCount);
    }

    [Fact]
    public void The_alarm_key_reads_IsAlarmZone_and_the_default_is_off()
    {
        var (rig, zone, _, _) = Map();
        var plain = rig.Spawn<FuncFlagDetectionZone>(FuncFlagDetectionZone.MapClass, CtfRig.Ground);

        Assert.True(zone.IsAlarmZone);
        Assert.False(plain.IsAlarmZone);
    }

    // ---- what the flag tells the zones --------------------------------------

    [Fact]
    public void A_pickup_inside_the_zone_fires_OnPickedUpFlag_and_enters_the_carrier()
    {
        var (rig, zone, red, _) = Map();
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(zone.Handle, carrier);

        rig.Registry.Touch(red.Handle, carrier);

        Assert.Equal(1, zone.OnPickedUpFlag.FireCount);
        Assert.Equal(1, zone.OnStartTouch.FireCount);
        Assert.Equal(1, zone.OnStartTouchFlag.FireCount);
    }

    [Fact]
    public void A_carrier_dropping_inside_fires_OnDroppedFlag_and_the_zones_end()
    {
        var (rig, zone, red, _) = Map();
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, carrier);
        rig.Registry.Touch(zone.Handle, carrier);

        red.Drop(carrier, visible: true);

        Assert.Equal(1, zone.OnDroppedFlag.FireCount);
        Assert.Equal(1, zone.OnEndTouchFlag.FireCount);
        Assert.Equal(1, zone.OnEndTouchAll.FireCount);
    }

    [Fact]
    public void The_player_the_zone_saw_drop_is_there_to_pick_up_again()
    {
        var (rig, zone, red, _) = Map();
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, carrier);
        rig.Registry.Touch(zone.Handle, carrier);
        red.Drop(carrier, visible: true);

        rig.Registry.Touch(red.Handle, carrier);

        Assert.Equal(1, zone.OnPickedUpFlag.FireCount);
        Assert.Equal(2, zone.OnStartTouch.FireCount);
    }

    [Fact]
    public void A_capture_tells_the_zones_the_drop_too()
    {
        var (rig, zone, red, _) = Map();
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, carrier);
        rig.Registry.Touch(zone.Handle, carrier);

        red.Capture(carrier, capturePoint: 1);

        Assert.Equal(1, zone.OnDroppedFlag.FireCount);
        Assert.Equal(1, zone.OnEndTouchFlag.FireCount);
    }

    [Fact]
    public void A_disabled_zone_is_skipped_by_the_dispatch()
    {
        var rig = new CtfRig();
        rig.Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        var zone = rig.Spawn<FuncFlagDetectionZone>(FuncFlagDetectionZone.MapClass, CtfRig.Ground, ("StartDisabled", "1"));
        var red = rig.Flag(TFTeam.Red);
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(zone.Handle, carrier);

        rig.Registry.Touch(red.Handle, carrier);

        Assert.Equal(0, zone.OnPickedUpFlag.FireCount);
    }
}
