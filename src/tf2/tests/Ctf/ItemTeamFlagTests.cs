using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities.Ctf;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Ctf;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// item_teamflag (stage 2.5): <c>CCaptureFlag</c>'s touch, pick-up, drop,
/// return and reset (shared/tf/entity_capture_flag.cpp), on a fake clock.
/// Every fact starts with ctf_2fort's two flags at home, the round running.
/// </summary>
public sealed class ItemTeamFlagTests
{
    private static (CtfRig Rig, ItemTeamFlag Red, ItemTeamFlag Blue) Map()
    {
        var rig = new CtfRig();
        return (rig, rig.Flag(TFTeam.Red), rig.Flag(TFTeam.Blue));
    }

    /// <summary>The two flags as an attack/defend map keys them (GameType 1).</summary>
    private static (CtfRig Rig, ItemTeamFlag Red, ItemTeamFlag Blue) AttackDefendMap()
    {
        var rig = new CtfRig();
        return (rig, rig.Flag(TFTeam.Red, ("GameType", "1")), rig.Flag(TFTeam.Blue, ("GameType", "1")));
    }

    // ---- spawn ------------------------------------------------------------------

    [Fact]
    public void A_spawned_flag_is_home_enabled_touchable_and_thinking_on_its_team()
    {
        var (_, red, blue) = Map();

        Assert.True(red.IsHome);
        Assert.Equal(TFTeam.Red, red.Team);
        Assert.Equal(TFTeam.Blue, blue.Team);
        Assert.False(red.IsDisabled);
        Assert.True(red.TouchEnabled);
        Assert.True(red.Thinking);
        Assert.Equal(60, red.ReturnTime);
        Assert.Equal(0, red.Skin);
        Assert.Equal(1, blue.Skin);
    }

    [Fact]
    public void The_trigger_is_the_forced_bounds_bloated_24_sideways_and_12_up_and_always_sent()
    {
        var (rig, red, _) = Map();

        var pickup = rig.Engine.AllOf("Players.MakePickup").Single(c => (EntityHandle)c.Args[0]! == red.Handle);
        Assert.Equal((43.5f, 46.5f, 18.5f), ((float)pickup.Args[1]!, (float)pickup.Args[2]!, (float)pickup.Args[3]!));
        Assert.Contains(rig.Engine.AllOf("EntityRender.SetTransmitAlways"),
                        c => (EntityHandle)c.Args[0]! == red.Handle && (bool)c.Args[1]!);
        Assert.Contains(rig.Engine.AllOf("EntityRender.SetModel"),
                        c => (EntityHandle)c.Args[0]! == red.Handle && (string)c.Args[1]! == ItemTeamFlag.DefaultModel);
    }

    [Fact]
    public void A_flag_builds_and_plays_without_the_optional_seams()
    {
        var rig = new CtfRig(optionalSeams: false);
        var red = rig.Flag(TFTeam.Red);
        var spy = rig.Players.Add(3, TFTeam.Blue, TFClass.Spy);
        rig.Players[spy].Conditions = TFCondMask.Of(TFCond.Disguised);
        rig.RespawnRooms.Inside.Add(spy); // not registered: nobody asks it

        rig.Registry.Touch(red.Handle, spy);

        Assert.True(red.IsStolen);
        Assert.Empty(rig.Effects.Done);
    }

    [Fact]
    public void A_flag_keyed_StartDisabled_spawns_disabled_untouchable_and_still()
    {
        var rig = new CtfRig();
        var flag = rig.Flag(TFTeam.Red, ("StartDisabled", "1"));
        var blue = rig.Players.Add(3, TFTeam.Blue);

        Assert.True(flag.IsDisabled);
        Assert.False(flag.TouchEnabled);
        Assert.False(flag.Thinking);

        rig.Registry.Touch(flag.Handle, blue);
        Assert.True(flag.IsHome);
    }

    [Fact]
    public void A_spawned_flag_publishes_its_team_and_glow_to_the_wire()
    {
        var (_, red, blue) = Map();

        Assert.Equal(2, red.State.Team);           // TF_TEAM_RED
        Assert.Equal(3, blue.State.Team);           // TF_TEAM_BLUE
        Assert.Equal(1, red.State.GlowEnabled);     // m_bGlowEnabled defaults on
    }

    [Fact]
    public void The_glow_input_takes_the_wire_with_it()
    {
        var (rig, red, _) = Map();

        rig.Registry.Input(red.Handle, "ForceGlowDisabled", "", 1f, EntityHandle.None);
        Assert.False(red.GlowEnabled);
        Assert.Equal(0, red.State.GlowEnabled);

        rig.Registry.Input(red.Handle, "ForceGlowDisabled", "", 0f, EntityHandle.None);
        Assert.True(red.GlowEnabled);
        Assert.Equal(1, red.State.GlowEnabled);
    }

    [Fact]
    public void An_invade_flag_following_its_carrier_publishes_the_new_team()
    {
        var rig = new CtfRig();
        var flag = rig.Flag(TFTeam.Unassigned, ("GameType", "3"));   // TF_FLAGTYPE_INVADE
        var blue = rig.Players.Add(3, TFTeam.Blue);

        Assert.Equal(0, flag.State.Team);   // TF_TEAM_UNASSIGNED at spawn
        rig.Registry.Touch(flag.Handle, blue);

        Assert.True(flag.IsStolen);
        Assert.Equal(3, flag.State.Team);   // TF_TEAM_BLUE, the carrier's (entity_capture_flag.cpp's invade arm)
    }

    // ---- who may take it --------------------------------------------------------

    [Fact]
    public void An_enemy_touch_picks_the_flag_up_and_makes_the_toucher_its_carrier()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);

        rig.Registry.Touch(red.Handle, blue);

        Assert.True(red.IsStolen);
        Assert.Equal(blue, red.Carrier);
        Assert.Same(red, rig.Rules.CarriedBy(blue));
        Assert.Contains("flag Pickup player=3 team=Red priority=8 home=1", rig.Events.Fired);
        Assert.Equal(1, red.OnPickUp.FireCount);
        Assert.Equal(1, red.OnPickupTeam2.FireCount);
        Assert.Equal(0, red.OnPickupTeam1.FireCount);
    }

    [Fact]
    public void A_team_mate_touch_leaves_the_flag_home_and_fires_OnTouchSameTeam()
    {
        var (rig, red, _) = Map();
        var redPlayer = rig.Players.Add(2, TFTeam.Red);

        rig.Registry.Touch(red.Handle, redPlayer);

        Assert.True(red.IsHome);
        Assert.Equal(EntityHandle.None, red.Carrier);
        Assert.Equal(1, red.OnTouchSameTeam.FireCount);
        Assert.Equal(0, red.OnPickUp.FireCount);
    }

    [Theory]
    [InlineData(TFTeam.Unassigned)]
    [InlineData(TFTeam.Spectator)]
    public void A_live_player_on_no_game_team_does_not_pick_it_up(TFTeam team)
    {
        // Seen live on ctf_2fort: a player who had not joined a team took red's
        // flag and capped it at red's zone (which holds the flag) five times.
        var (rig, red, _) = Map();
        var nobody = rig.Players.Add(3, team);

        rig.Registry.Touch(red.Handle, nobody);

        Assert.True(red.IsHome);
    }

    [Fact]
    public void A_dead_player_or_a_non_TF_player_does_not_pick_it_up()
    {
        var (rig, red, _) = Map();
        var dead = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[dead].Alive = false;
        var bare = rig.Players.Add(4, TFTeam.Blue);
        rig.Players[bare].IsTF = false;

        rig.Registry.Touch(red.Handle, dead);
        rig.Registry.Touch(red.Handle, bare);

        Assert.True(red.IsHome);
    }

    [Theory]
    [InlineData(TFCond.Stealthed)]
    [InlineData(TFCond.StealthedBlink)]
    [InlineData(TFCond.StealthedUserBuffFading)]
    [InlineData(TFCond.Invulnerable)]
    [InlineData(TFCond.InvulnerableUserBuff)]
    [InlineData(TFCond.Phase)]
    [InlineData(TFCond.SelectedToTeleport)]
    public void A_cloaked_ubered_phased_or_teleporting_enemy_does_not_pick_it_up(TFCond cond)
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[blue].Conditions = TFCondMask.Of(cond);

        rig.Registry.Touch(red.Handle, blue);

        Assert.True(red.IsHome);
    }

    [Fact]
    public void An_enemy_with_an_unrelated_condition_still_picks_it_up()
    {
        // The positive arm of the refusals above: a condition the touch does not read changes nothing.
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[blue].Conditions = TFCondMask.Of(TFCond.Critboosted);

        rig.Registry.Touch(red.Handle, blue);

        Assert.True(red.IsStolen);
    }

    [Fact]
    public void The_cannot_pick_up_intelligence_attribute_refuses_the_pick_up_and_without_it_it_is_taken()
    {
        // CTFPlayer::IsAllowedToPickUpFlag (tf_player_shared.cpp:11260-11270,
        // called at entity_capture_flag.cpp:1092): the attribute hook over the
        // player's own item manager.
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[blue].AllowedToPickUp = false;

        rig.Registry.Touch(red.Handle, blue);

        Assert.True(red.IsHome);
        Assert.Empty(rig.Events.Fired);

        rig.Players[blue].AllowedToPickUp = true;
        rig.Registry.Touch(red.Handle, blue);
        Assert.True(red.IsStolen);
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(1f)]
    public void A_player_more_than_a_quarter_invisible_does_not_pick_it_up(float percent)
    {
        // The fade's arm of entity_capture_flag.cpp:1115: a spy still fading
        // out of the cloak is more invisible than the limit.
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[blue].PercentInvisible = percent;

        rig.Registry.Touch(red.Handle, blue);

        Assert.True(red.IsHome);
    }

    [Fact]
    public void A_player_exactly_a_quarter_invisible_still_picks_it_up()
    {
        // Stock compares strictly: GetPercentInvisible() > 0.25f (:1115).
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[blue].PercentInvisible = 0.25f;

        rig.Registry.Touch(red.Handle, blue);

        Assert.True(red.IsStolen);
    }

    [Fact]
    public void A_disguised_spy_picks_it_up_and_loses_the_disguise()
    {
        var (rig, red, _) = Map();
        var spy = rig.Players.Add(3, TFTeam.Blue, TFClass.Spy);
        rig.Players[spy].Conditions = TFCondMask.Of(TFCond.Disguised);

        rig.Registry.Touch(red.Handle, spy);

        Assert.True(red.IsStolen);
        Assert.Equal(new[] { "undisguise 3" }, rig.Effects.Done);
    }

    [Fact]
    public void An_undisguised_spy_or_a_disguised_non_spy_keeps_nothing_removed()
    {
        var (rig, red, blueFlag) = Map();
        var spy = rig.Players.Add(3, TFTeam.Blue, TFClass.Spy);
        var scout = rig.Players.Add(4, TFTeam.Red);
        rig.Players[scout].Conditions = TFCondMask.Of(TFCond.Disguised);

        rig.Registry.Touch(red.Handle, spy);
        rig.Registry.Touch(blueFlag.Handle, scout);

        Assert.True(red.IsStolen);
        Assert.True(blueFlag.IsStolen);
        Assert.Empty(rig.Effects.Done);
    }

    [Fact]
    public void A_carrier_does_not_take_a_second_flag_without_return_on_touch()
    {
        var rig = new CtfRig();
        var red1 = rig.Flag(TFTeam.Red);
        var red2 = rig.Flag(TFTeam.Red);
        var blue = rig.Players.Add(3, TFTeam.Blue);

        rig.Registry.Touch(red1.Handle, blue);
        rig.Registry.Touch(red2.Handle, blue);

        Assert.True(red1.IsStolen);
        Assert.True(red2.IsHome);
    }

    [Fact]
    public void Nobody_picks_a_flag_up_in_a_respawn_room()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.RespawnRooms.Inside.Add(blue);

        rig.Registry.Touch(red.Handle, blue);

        Assert.True(red.IsHome);
    }

    [Theory]
    [InlineData(RoundState.Preround)]
    [InlineData(RoundState.TeamWin)]
    public void Flags_may_not_be_picked_up_before_the_round_or_after_a_win(RoundState state)
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Round.State = state;

        rig.Registry.Touch(red.Handle, blue);

        Assert.True(red.IsHome);
    }

    [Fact]
    public void Flags_may_be_picked_up_in_a_stalemate_but_not_while_waiting_for_players()
    {
        var (rig, red, blueFlag) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var redPlayer = rig.Players.Add(2, TFTeam.Red);

        rig.Round.InWaitingForPlayers = true;
        rig.Registry.Touch(red.Handle, blue);
        Assert.True(red.IsHome);

        rig.Round.InWaitingForPlayers = false;
        rig.Round.State = RoundState.Stalemate;
        rig.Registry.Touch(blueFlag.Handle, redPlayer);
        Assert.True(blueFlag.IsStolen);
    }

    // ---- carrying ---------------------------------------------------------------

    [Fact]
    public void The_carried_flag_rides_on_its_carrier_at_the_origin_with_no_flag_attachment()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);

        rig.Registry.Touch(red.Handle, blue);

        var parent = rig.Engine.AllOf("Entities.SetParent").Last(c => (EntityHandle)c.Args[0]! == red.Handle);
        Assert.Equal(blue, (EntityHandle)parent.Args[1]!);
        Assert.Equal(0, (int)parent.Args[2]!);
        Assert.Equal(3, red.Skin); // red's skin 0, brighter while carried
    }

    [Fact]
    public void The_carried_flag_hangs_from_the_flag_attachment_when_the_model_has_one()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[blue].Attachment = 5;

        rig.Registry.Touch(red.Handle, blue);

        var parent = rig.Engine.AllOf("Entities.SetParent").Last(c => (EntityHandle)c.Args[0]! == red.Handle);
        Assert.Equal(blue, (EntityHandle)parent.Args[1]!);
        Assert.Equal(5, (int)parent.Args[2]!);
    }

    [Fact]
    public void A_carried_flag_ignores_every_touch()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var blue2 = rig.Players.Add(4, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);

        rig.Registry.Touch(red.Handle, blue2);
        red.FlagTouch(blue2);

        Assert.Equal(blue, red.Carrier);
    }

    // ---- the objective table ------------------------------------------------------

    [Fact]
    public void The_objective_table_follows_the_flag_home_stolen_dropped_home()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        Assert.Equal((0, 0, 0f), Row(rig, TFTeam.Red));

        rig.Registry.Touch(red.Handle, blue);
        Assert.Equal((1, 3, 0f), Row(rig, TFTeam.Red));

        rig.Clock.Now = 20f;
        red.RemoveFromCarrier(blue);
        Assert.Equal((2, 0, 80f), Row(rig, TFTeam.Red));

        rig.ThinkAt(red, 80.25f);
        Assert.Equal((0, 0, 0f), Row(rig, TFTeam.Red));
    }

    private static (int Status, int Carrier, float ResetTime) Row(CtfRig rig, TFTeam team)
        => (rig.ObjectiveState.FlagStatus[(int)team], rig.ObjectiveState.FlagCarrier[(int)team],
            rig.ObjectiveState.FlagResetTime[(int)team]);

    [Fact]
    public void The_table_says_whether_a_team_has_a_flag_at_all()
    {
        var (rig, red, _) = Map();

        // a spawned, enabled flag is present; disabled, absent or removed is not —
        // a home status alone cannot be told from no flag
        Assert.True(rig.ObjectiveState.HasFlag[(int)TFTeam.Red]);

        red.SetDisabled(true);
        Assert.False(rig.ObjectiveState.HasFlag[(int)TFTeam.Red]);

        red.SetDisabled(false);
        Assert.True(rig.ObjectiveState.HasFlag[(int)TFTeam.Red]);

        red.OnRemoved();
        Assert.False(rig.ObjectiveState.HasFlag[(int)TFTeam.Red]);
    }

    // ---- the drop -----------------------------------------------------------------

    [Fact]
    public void A_drop_lands_the_flag_where_the_trace_below_the_carriers_centre_stops()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[blue].Center = new Vector(100f, 200f, 50f);
        rig.Registry.Touch(red.Handle, blue);

        red.RemoveFromCarrier(blue);

        var trace = rig.Engine.Last("WorldQuery.EntityTraceHull")!.Value;
        Assert.Equal(blue, (EntityHandle)trace.Args[0]!); // the trace passes through the carrier
        Assert.Equal(new Vector(100f, 200f, 50f), (Vector)trace.Args[1]!);
        Assert.Equal(new Vector(0f, 120f, 0f), (Vector)rig.Engine.Last("Entities.SetAngles")!.Value.Args[1]!);
        Assert.Equal(new Vector(100f, 200f, -7950f), (Vector)trace.Args[2]!);
        Assert.Equal(CtfRig.Ground, rig.OriginOf(red));
        Assert.True(red.IsDropped);
        Assert.Equal(EntityHandle.None, rig.LastParent(red));
        Assert.Equal(0, red.Skin);
        Assert.True(red.TouchEnabled);
        Assert.Equal(1, red.OnDrop.FireCount);
        Assert.Contains("flag Dropped player=3 team=Red priority=8", rig.Events.Fired);
    }

    [Fact]
    public void A_drop_that_starts_in_solid_wiggles_and_one_that_never_fits_stays_at_the_centre()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[blue].Center = new Vector(100f, 200f, 50f);
        rig.Registry.Touch(red.Handle, blue);
        rig.Engine.Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(CtfRig.Ground, true, true));
        rig.Engine.Clear();

        red.RemoveFromCarrier(blue);

        // The first trace, then every wiggle of 27 (the first wiggle is the centre again).
        Assert.Equal(1 + 27, rig.Engine.CountOf("WorldQuery.EntityTraceHull"));
        Assert.Equal(new Vector(100f, 200f, 50f), rig.OriginOf(red));
    }

    [Fact]
    public void A_wiggle_that_fits_lands_where_its_trace_stops()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        int traces = 0;
        var landed = new Vector(1f, 2f, 3f);
        rig.Engine.ReturnsEachCall("WorldQuery.EntityTraceHull",
                                   () => ++traces < 3 ? new EntityHullTrace(default, true, true) : new EntityHullTrace(landed, false, false));

        red.RemoveFromCarrier(blue);

        Assert.Equal(3, traces);
        Assert.Equal(landed, rig.OriginOf(red));
    }

    // ---- the return timer ---------------------------------------------------------

    [Fact]
    public void A_dropped_flag_goes_home_on_the_first_think_after_its_return_time()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        rig.Clock.Now = 20f;
        red.RemoveFromCarrier(blue);
        Assert.Equal(80f, red.ResetTime);

        rig.ThinkAt(red, 80f);
        Assert.True(red.IsDropped);

        rig.ThinkAt(red, 80.25f);
        Assert.True(red.IsHome);
        Assert.Equal(CtfRig.RedHome, rig.OriginOf(red));
        Assert.Equal(1, red.OnReturn.FireCount);
        Assert.Contains("flag Returned player=-1 team=Red priority=8", rig.Events.Fired);
        Assert.Contains(rig.Engine.AllOf("Sound.EntityEmitSound"), c => (string)c.Args[1]! == ItemTeamFlag.FlagSpawnSound);
    }

    [Fact]
    public void Without_the_shot_clock_every_drop_waits_the_full_return_time_whatever_the_credit_factor()
    {
        var (rig, red, _) = Map();
        rig.Settings.ReturnTimeCreditFactor = 0f;
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        rig.Clock.Now = 20f;
        red.RemoveFromCarrier(blue);
        rig.Clock.Now = 70f; // 10 s left
        rig.Registry.Touch(red.Handle, blue);
        rig.Clock.Now = 90f;

        red.RemoveFromCarrier(blue);

        Assert.Equal(90f + 60f, red.ResetTime);
    }

    [Theory]
    [InlineData(1f, 30f)] // 10 s left at the pick-up, 20 s carried at 1 s a second
    [InlineData(0f, 10f)] // no credit: the 10 s left at the pick-up
    [InlineData(4f, 60f)] // 10 + 80, capped at the full time
    public void The_shot_clock_credits_the_return_time_for_the_time_carried(float factor, float expected)
    {
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red, ("ShotClockMode", "1"));
        rig.Settings.ReturnTimeCreditFactor = factor;
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        rig.Clock.Now = 20f;
        red.RemoveFromCarrier(blue); // the full 60: picked up from home
        Assert.Equal(80f, red.ResetTime);

        rig.Clock.Now = 70f;
        rig.Registry.Touch(red.Handle, blue); // 10 s left
        rig.Clock.Now = 90f;
        red.RemoveFromCarrier(blue);

        Assert.Equal(90f + expected, red.ResetTime);
        Assert.Equal(60f, red.MaxResetTime);
    }

    // ---- return on touch ----------------------------------------------------------

    [Fact]
    public void A_team_mate_returns_a_dropped_flag_by_touch_under_tf_flag_return_on_touch()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var redPlayer = rig.Players.Add(2, TFTeam.Red);
        rig.Registry.Touch(red.Handle, blue);
        red.RemoveFromCarrier(blue);
        rig.Settings.ReturnOnTouch = true;

        rig.Registry.Touch(red.Handle, redPlayer);

        Assert.True(red.IsHome);
        Assert.Equal(1, red.OnReturn.FireCount);
    }

    [Fact]
    public void A_team_mate_in_a_respawn_room_does_not_return_a_dropped_flag_either()
    {
        // Stock's one PointInRespawnRoom call (entity_capture_flag.cpp:1127)
        // stands before the Reset branch (:1131): a team mate standing in the
        // room returns nothing.
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var redPlayer = rig.Players.Add(2, TFTeam.Red);
        rig.Registry.Touch(red.Handle, blue);
        red.RemoveFromCarrier(blue);
        rig.Settings.ReturnOnTouch = true;
        rig.RespawnRooms.Inside.Add(redPlayer);

        rig.Registry.Touch(red.Handle, redPlayer);

        Assert.True(red.IsDropped);
        Assert.Equal(0, red.OnReturn.FireCount);
    }

    [Fact]
    public void Without_tf_flag_return_on_touch_a_team_mate_leaves_a_dropped_flag_where_it_lies()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var redPlayer = rig.Players.Add(2, TFTeam.Red);
        rig.Registry.Touch(red.Handle, blue);
        red.RemoveFromCarrier(blue);

        rig.Registry.Touch(red.Handle, redPlayer);

        Assert.True(red.IsDropped);
        Assert.Equal(0, red.OnReturn.FireCount);
    }

    [Fact]
    public void An_enemy_picks_a_dropped_flag_back_up_and_the_event_says_not_from_home()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var blue2 = rig.Players.Add(4, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        red.RemoveFromCarrier(blue);

        rig.Registry.Touch(red.Handle, blue2);

        Assert.Equal(blue2, red.Carrier);
        Assert.Contains("flag Pickup player=4 team=Red priority=8 home=0", rig.Events.Fired);
    }

    // ---- the thrown drop ----------------------------------------------------------

    [Fact]
    public void A_thrower_may_not_take_the_flag_back_for_three_seconds()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        rig.Clock.Now = 20f;

        red.DropFlag(blue);
        rig.Registry.Touch(red.Handle, blue);
        Assert.True(red.IsDropped);
        Assert.False(red.AllowOwnerPickup);

        rig.ThinkAt(red, 23f);
        rig.Registry.Touch(red.Handle, blue);
        Assert.True(red.IsDropped);

        rig.ThinkAt(red, 23.25f);
        rig.Registry.Touch(red.Handle, blue);
        Assert.Equal(blue, red.Carrier);
    }

    [Fact]
    public void A_drop_that_is_not_a_throw_lets_the_last_carrier_take_it_straight_back()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);

        red.RemoveFromCarrier(blue);
        rig.Registry.Touch(red.Handle, blue);

        Assert.Equal(blue, red.Carrier);
    }

    // ---- inputs and the round ------------------------------------------------------

    [Fact]
    public void RoundActivate_drops_a_carried_flag_and_sends_it_home()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);

        rig.Registry.Input(red.Handle, "RoundActivate", string.Empty, 0f, EntityHandle.None);

        Assert.True(red.IsHome);
        Assert.Equal(CtfRig.RedHome, rig.OriginOf(red));
    }

    [Fact]
    public void ForceReset_drops_and_returns_a_carried_flag_with_the_returned_event()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);

        rig.Registry.Input(red.Handle, "ForceReset", string.Empty, 0f, EntityHandle.None);

        Assert.True(red.IsHome);
        Assert.Contains("flag Dropped player=3 team=Red priority=8", rig.Events.Fired);
        Assert.Contains("flag Returned player=-1 team=Red priority=8", rig.Events.Fired);
    }

    [Fact]
    public void ForceResetAndDisableSilent_returns_without_the_message_and_disables()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);

        rig.Registry.Input(red.Handle, "ForceResetAndDisableSilent", string.Empty, 0f, EntityHandle.None);

        Assert.True(red.IsHome);
        Assert.True(red.IsDisabled);
        Assert.DoesNotContain(rig.Events.Fired, e => e.StartsWith("flag Returned", StringComparison.Ordinal));
        Assert.Equal(0, red.OnReturn.FireCount);
    }

    [Fact]
    public void SetReturnTime_restarts_a_dropped_flags_timer_on_the_new_time()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        rig.Clock.Now = 20f;
        red.RemoveFromCarrier(blue);
        rig.Clock.Now = 30f;

        rig.Registry.Input(red.Handle, "SetReturnTime", "5", 5f, EntityHandle.None);

        Assert.Equal(5, red.ReturnTime);
        Assert.Equal(35f, red.ResetTime);
    }

    [Fact]
    public void Disable_stops_the_return_timer_and_Enable_starts_it_again()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        rig.Clock.Now = 20f;
        red.RemoveFromCarrier(blue);

        rig.Registry.Input(red.Handle, "Disable", string.Empty, 0f, EntityHandle.None);
        rig.Clock.Now = 100f;
        red.OnThink();
        Assert.True(red.IsDropped);

        rig.Registry.Input(red.Handle, "Enable", string.Empty, 0f, EntityHandle.None);
        red.OnThink();
        Assert.True(red.IsHome);
    }

    [Fact]
    public void The_think_holds_the_return_timer_while_flags_may_not_be_capped()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        rig.Clock.Now = 20f;
        red.RemoveFromCarrier(blue);
        rig.Round.State = RoundState.TeamWin;

        rig.ThinkAt(red, 100f);
        Assert.True(red.IsDropped);

        rig.Round.State = RoundState.RoundRunning;
        rig.ThinkAt(red, 100.25f);
        Assert.True(red.IsHome);
    }

    // ---- the announcer (stage 2.5) -------------------------------------------------

    [Fact]
    public void A_pick_up_tells_the_announcer_who_took_which_flag()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);

        rig.Registry.Touch(red.Handle, blue);

        Assert.Equal([$"stolen flag={red.Handle.EntryIndex} pickerTeam=Blue picker=3"], rig.Announcer.Lines);
    }

    [Fact]
    public void A_drop_tells_the_announcer_who_dropped_which_flag()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);

        red.RemoveFromCarrier(blue);

        Assert.Equal([$"stolen flag={red.Handle.EntryIndex} pickerTeam=Blue picker=3",
                      $"dropped flag={red.Handle.EntryIndex} dropperTeam=Blue dropper=3"], rig.Announcer.Lines);
    }

    [Fact]
    public void A_return_tells_the_announcer_which_flag_went_home()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);
        red.RemoveFromCarrier(blue);

        rig.ThinkAt(red, rig.Clock.Now + red.ReturnTime + 0.25f);

        Assert.Contains($"returned flag={red.Handle.EntryIndex} flagTeam=Red", rig.Announcer.Lines);
    }

    [Fact]
    public void A_silent_force_reset_tells_the_announcer_nothing()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, blue);

        rig.Registry.Input(red.Handle, "ForceResetAndDisableSilent", string.Empty, 0f, EntityHandle.None);

        // The pick-up line was the touch's; the silent reset adds neither the drop nor the return.
        Assert.DoesNotContain(rig.Announcer.Lines, l => l.StartsWith("dropped", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Announcer.Lines, l => l.StartsWith("returned", StringComparison.Ordinal));
    }

    // ---- the attack/defend announcer (entity_capture_flag.cpp's AD arms) ----
    // The AD touch refuses the other team (:1067-1071), so the carrier is
    // always the flag's own team.

    [Fact]
    public void An_attack_defend_pick_up_tells_the_announcer_who_took_which_flag()
    {
        var (rig, red, _) = AttackDefendMap();
        var mate = rig.Players.Add(3, TFTeam.Red);

        rig.Registry.Touch(red.Handle, mate);

        Assert.Equal([$"ad stolen flag={red.Handle.EntryIndex} pickerTeam=Red picker=3"], rig.Announcer.Lines);
    }

    [Fact]
    public void An_attack_defend_drop_tells_the_announcer_who_dropped_which_flag()
    {
        var (rig, red, _) = AttackDefendMap();
        var mate = rig.Players.Add(3, TFTeam.Red);
        rig.Registry.Touch(red.Handle, mate);

        red.RemoveFromCarrier(mate);

        Assert.Equal([$"ad stolen flag={red.Handle.EntryIndex} pickerTeam=Red picker=3",
                      $"ad dropped flag={red.Handle.EntryIndex} dropperTeam=Red dropper=3"], rig.Announcer.Lines);
    }

    [Fact]
    public void An_attack_defend_return_tells_the_announcer_which_flag_went_home()
    {
        var (rig, red, _) = AttackDefendMap();
        var mate = rig.Players.Add(3, TFTeam.Red);
        rig.Registry.Touch(red.Handle, mate);
        red.RemoveFromCarrier(mate);

        rig.ThinkAt(red, rig.Clock.Now + red.ReturnTime + 0.25f);

        Assert.Contains($"ad returned flag={red.Handle.EntryIndex} flagTeam=Red", rig.Announcer.Lines);
    }

    [Fact]
    public void An_attack_defend_capture_tells_the_announcer_who_capped_which_flag()
    {
        var (rig, red, _) = AttackDefendMap();
        var mate = rig.Players.Add(3, TFTeam.Red);

        rig.Registry.Touch(red.Handle, mate);

        red.Capture(mate, capturePoint: 1);

        Assert.Contains($"ad captured flag={red.Handle.EntryIndex} capperTeam=Red capper=3", rig.Announcer.Lines);
    }

    // ---- the carried effects (entity_capture_flag.cpp:2350-2746, 2408-2428) -------------------

    private static readonly EntityHandle Trail = new(0x0050_0001u);
    private static readonly EntityHandle Icon = new(0x0050_0002u);

    /// <summary>What the trail was keyed with, as (key, value) pairs on its handle.</summary>
    private static IEnumerable<(string Key, string Value)> KeysFor(CtfRig rig, EntityHandle handle)
        => rig.Engine.AllOf("Entities.SetKeyValue")
                      .Where(c => (EntityHandle)c.Args[0]! == handle)
                      .Select(c => ((string)c.Args[1]!, (string)c.Args[2]!));

    /// <summary>A map whose next created entity is the trail, which the registry has an instance for.</summary>
    private static CtfRig TrailMap()
    {
        var (rig, _, _) = Map();
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.Returns("Precache.ModelFrameCount", 1);   // the sprite's frame count, Spawn asks it
        rig.Engine.Returns("Entities.Create", Trail);
        rig.Registry.Spawn(Trail, EntityNames.EnvSpritetrail);
        return rig;
    }

    [Fact]
    public void A_pickup_lays_the_colour_trail_from_the_flag_in_the_carrier_s_colour()
    {
        var rig = TrailMap();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var red = rig.Flag(TFTeam.Red);

        rig.Registry.Touch(red.Handle, blue);

        Assert.Contains(rig.Engine.AllOf("Entities.Create"), c => (string)c.Args[0]! == EntityNames.EnvSpritetrail);
        // The carrier is BLU, so the ribbon is the blu material (GetTrailEffect, :328-333).
        var keys = KeysFor(rig, Trail).ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("effects/flagtrail_blu.vmt", keys["spritename"]);
        Assert.Equal("0.7", keys["lifetime"]);          // SetLifeTime( 0.70 ), :2707
        Assert.Equal("32", keys["startwidth"]);         // SetStartWidth( 32 ), :2705
        Assert.Equal("4", keys["rendermode"]);          // kRenderTransAlpha, :2704
        Assert.Equal("255 255 255", keys["rendercolor"]);
        Assert.Equal("96", keys["renderamt"]);          // TF_FLAG_TRAIL_ALPHA, :2704
        // The one factory value no key names: the texture resolution.
        var trail = (EnvSpriteTrail)rig.Registry.Get(Trail)!;
        Assert.Equal(TFFlagTrail.TextureRes, trail.Trail.TextureRes, 5);
        // FollowEntity( this ) + SetAttachment( this, 0 ): the ribbon runs from the carrier.
        Assert.Equal(red.Handle,
                     rig.Engine.AllOf("Entities.SetParent")
                               .Where(c => (EntityHandle)c.Args[0]! == Trail)
                               .Select(c => (EntityHandle)c.Args[1]!)
                               .Single());
    }

    [Fact]
    public void A_red_carrier_s_trail_is_the_red_material()
    {
        var rig = TrailMap();
        var redPlayer = rig.Players.Add(4, TFTeam.Red);

        rig.Registry.Touch(rig.Flag(TFTeam.Blue).Handle, redPlayer);

        var keys = KeysFor(rig, Trail).ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("effects/flagtrail_red.vmt", keys["spritename"]);
    }

    [Theory]
    [InlineData("0")]   // FLAG_EFFECTS_NONE
    [InlineData("2")]   // FLAG_EFFECTS_PAPERONLY
    public void A_trail_effect_without_the_colour_lays_no_ribbon(string trailEffect)
    {
        var (rig, _, _) = Map();
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.Returns("Entities.Create", Trail);
        var flag = rig.Flag(TFTeam.Red, ("trail_effect", trailEffect));
        var blue = rig.Players.Add(3, TFTeam.Blue);

        rig.Registry.Touch(flag.Handle, blue);

        Assert.DoesNotContain(rig.Engine.AllOf("Entities.Create"), c => (string)c.Args[0]! == EntityNames.EnvSpritetrail);
    }

    [Fact]
    public void The_trail_starts_at_the_flag_s_origin()
    {
        var rig = TrailMap();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var flag = rig.Flag(TFTeam.Red);

        rig.Registry.Touch(flag.Handle, blue);

        Assert.Equal(CtfRig.RedHome, rig.OriginOf((ScriptedEntity)rig.Registry.Get(Trail)!));
    }

    [Fact]
    public void A_drop_fades_the_trail_from_full_brightness_a_step_at_a_time()
    {
        var rig = TrailMap();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var red = rig.Flag(TFTeam.Red);
        rig.Registry.Touch(red.Handle, blue);

        red.Drop(blue, visible: true);

        // The fade is a context think a twentieth of a second out, and the
        // first step paints the full alpha (RemoveFlagTrail, :2718-2746).
        Assert.Equal(ItemTeamFlag.FadeStepTime, red.NextThinkDelay(ItemTeamFlag.FadeContext), 3);
        var trail = (EnvSpriteTrail)rig.Registry.Get(Trail)!;
        Assert.Equal(TFFlagTrail.Alpha, trail.Trail.Brightness);

        // Each step paints a tenth of the alpha less; the last one deletes the
        // trail. The fade think is driven by hand, so the heap still holds the
        // schedule the drop made — the removal is the subject, not the heap.
        float life = ItemTeamFlag.FadeLife - ItemTeamFlag.FadeLifeStep;
        for (int step = 0; step < 9; step++)
        {
            rig.Clock.Now += ItemTeamFlag.FadeStepTime;
            red.OnThink(ItemTeamFlag.FadeContext);
            Assert.Equal((int)(TFFlagTrail.Alpha * life), trail.Trail.Brightness);
            life -= ItemTeamFlag.FadeLifeStep;
        }

        rig.Clock.Now += ItemTeamFlag.FadeStepTime;
        red.OnThink(ItemTeamFlag.FadeContext);
        Assert.Contains(rig.Engine.AllOf("Entities.Remove"), c => (EntityHandle)c.Args[0]! == Trail);
    }

    [Fact]
    public void A_capture_and_a_reset_kill_the_trail_at_once()
    {
        var rig = TrailMap();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var red = rig.Flag(TFTeam.Red);
        rig.Registry.Touch(red.Handle, blue);

        red.Capture(blue, capturePoint: 1);

        Assert.Contains(rig.Engine.AllOf("Entities.Remove"), c => (EntityHandle)c.Args[0]! == Trail);
        Assert.Equal(-1f, red.NextThinkDelay(ItemTeamFlag.FadeContext), 3);

        // A reset is the other instant arm (entity_capture_flag.cpp:834-835).
        var rig2 = TrailMap();
        var blue2 = rig2.Players.Add(3, TFTeam.Blue);
        var red2 = rig2.Flag(TFTeam.Red);
        rig2.Registry.Touch(red2.Handle, blue2);

        red2.Reset();

        Assert.Contains(rig2.Engine.AllOf("Entities.Remove"), c => (EntityHandle)c.Args[0]! == Trail);
    }

    [Fact]
    public void A_stopped_carrier_s_think_takes_the_trail_into_its_fade_and_a_running_one_keeps_it()
    {
        var rig = TrailMap();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var red = rig.Flag(TFTeam.Red);
        rig.Registry.Touch(red.Handle, blue);
        Assert.Equal(-1f, red.NextThinkDelay(ItemTeamFlag.FadeContext), 3);

        // Standing still: the think fades the trail (entity_capture_flag.cpp:2355-2378).
        rig.Players[blue].Velocity = default;
        rig.ThinkAt(red, 10.25f);
        Assert.Equal(ItemTeamFlag.FadeStepTime, red.NextThinkDelay(ItemTeamFlag.FadeContext), 3);

        // A running carrier's think neither re-creates while the fade runs.
        rig.Players[blue].Velocity = new Vector(200f, 0f, 0f);
        rig.ThinkAt(red, 10.5f);
        Assert.Equal(1, rig.Engine.CountOf("Entities.Create"));
    }

    [Fact]
    public void A_faded_trail_comes_back_when_the_carrier_runs_again()
    {
        var rig = TrailMap();
        var blue = rig.Players.Add(3, TFTeam.Blue);
        var red = rig.Flag(TFTeam.Red);
        rig.Registry.Touch(red.Handle, blue);
        rig.Players[blue].Velocity = default;
        rig.ThinkAt(red, 10.25f);

        for (int step = 0; step < 11; step++)
        {
            rig.Clock.Now += ItemTeamFlag.FadeStepTime;
            red.OnThink(ItemTeamFlag.FadeContext);
        }

        rig.Players[blue].Velocity = new Vector(200f, 0f, 0f);
        rig.ThinkAt(red, 11f);

        // The ribbon is laid again from the flag.
        Assert.Equal(2, rig.Engine.CountOf("Entities.Create"));
        Assert.Contains(rig.Engine.AllOf("Entities.SetParent"),
                        c => (EntityHandle)c.Args[0]! == Trail && (EntityHandle)c.Args[1]! == red.Handle);
    }

    [Fact]
    public void A_drop_raises_the_return_icon_over_the_flag_and_it_transmits_always()
    {
        var (rig, red, _) = Map();
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.Returns("Entities.Create", Icon);
        rig.Registry.Spawn(Icon, ItemTeamFlagReturnIcon.MapClass);
        var blue = rig.Players.Add(3, TFTeam.Blue);

        red.Drop(blue, visible: true);

        Assert.Contains(rig.Engine.AllOf("Entities.Create"),
                        c => (string)c.Args[0]! == ItemTeamFlagReturnIcon.MapClass);
        // cl_flag_return_height over the flag's dropped position, parented to it.
        var icon = (ItemTeamFlagReturnIcon)rig.Registry.Get(Icon)!;
        Assert.Equal(rig.OriginOf(red) + new Vector(0f, 0f, ItemTeamFlagReturnIcon.Height), rig.OriginOf(icon));
        Assert.Contains(rig.Engine.AllOf("Entities.SetParent"),
                        c => (EntityHandle)c.Args[0]! == Icon && (EntityHandle)c.Args[1]! == red.Handle);
        // Spawn's ±50 bounds and FL_EDICT_ALWAYS (entity_capture_flag.cpp:2665-2677).
        var bounds = rig.Engine.AllOf("Entities.SetBounds")
                                .Where(c => (EntityHandle)c.Args[0]! == Icon)
                                .ToList();
        Assert.NotEmpty(bounds);
        Assert.Equal(new Vector(-50f, -50f, -50f), (Vector)bounds[0].Args[1]!);
        Assert.Equal(new Vector(50f, 50f, 50f), (Vector)bounds[0].Args[2]!);
        Assert.Contains(rig.Engine.AllOf("EntityRender.SetTransmitAlways"),
                        c => (EntityHandle)c.Args[0]! == Icon && (bool)c.Args[1]!);
    }

    [Fact]
    public void The_icon_comes_down_on_a_pickup_and_on_a_return()
    {
        var (rig, red, _) = Map();
        rig.Engine.Returns("Entities.IsValid", true);
        var creates = new Queue<EntityHandle>([Icon, Trail]);
        rig.Engine.ReturnsEachCall("Entities.Create", () => creates.TryDequeue(out var h) ? h : default);
        rig.Engine.Returns("Precache.ModelFrameCount", 1);   // the trail sprite's Spawn asks it
        rig.Registry.Spawn(Icon, ItemTeamFlagReturnIcon.MapClass);
        rig.Registry.Spawn(Trail, EntityNames.EnvSpritetrail);
        var blue = rig.Players.Add(3, TFTeam.Blue);

        red.Drop(blue, visible: true);
        rig.Registry.Touch(red.Handle, blue);   // the enemy takes it again

        Assert.Contains(rig.Engine.AllOf("Entities.Remove"), c => (EntityHandle)c.Args[0]! == Icon);

        // A return message is the other destroy (entity_capture_flag.cpp:1020).
        var rig2 = new CtfRig();
        rig2.Engine.Returns("Entities.IsValid", true);
        rig2.Engine.Returns("Entities.Create", Icon);
        rig2.Registry.Spawn(Icon, ItemTeamFlagReturnIcon.MapClass);
        var blue2 = rig2.Players.Add(3, TFTeam.Blue);
        var red2 = rig2.Flag(TFTeam.Red);
        red2.Drop(blue2, visible: true);

        red2.ResetMessage();

        Assert.Contains(rig2.Engine.AllOf("Entities.Remove"), c => (EntityHandle)c.Args[0]! == Icon);
    }

    [Fact]
    public void The_think_takes_the_icon_down_when_its_return_time_is_up()
    {
        var (rig, red, _) = Map();
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.Returns("Entities.Create", Icon);
        rig.Registry.Spawn(Icon, ItemTeamFlagReturnIcon.MapClass);
        var blue = rig.Players.Add(3, TFTeam.Blue);
        red.Drop(blue, visible: true);

        // The backstop at the top of the think (entity_capture_flag.cpp:2350-2353):
        // the return timer's end, whether or not the reset arm has run yet.
        rig.ThinkAt(red, red.ResetTime + 0.1f);

        Assert.Contains(rig.Engine.AllOf("Entities.Remove"), c => (EntityHandle)c.Args[0]! == Icon);
    }

    [Fact]
    public void The_show_timer_input_raises_the_icon()
    {
        var (rig, red, _) = Map();
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.Returns("Entities.Create", Icon);
        rig.Registry.Spawn(Icon, ItemTeamFlagReturnIcon.MapClass);

        rig.Registry.Input(red.Handle, "ShowTimer", string.Empty, 30f, EntityHandle.None);

        Assert.Contains(rig.Engine.AllOf("Entities.Create"),
                        c => (string)c.Args[0]! == ItemTeamFlagReturnIcon.MapClass);
    }

    [Fact]
    public void The_wire_carries_the_status_the_paper_effect_and_the_switch()
    {
        var (rig, red, _) = Map();
        var blue = rig.Players.Add(3, TFTeam.Blue);

        var flag = rig.Flag(TFTeam.Red, ("flag_paper", "my_own_paper"), ("trail_effect", "3"));
        Assert.Equal((byte)TFFlagEffects.ColorOnly, flag.State.UseTrailEffect);
        Assert.Equal("my_own_paper", flag.State.PaperEffect);
        Assert.Equal((byte)TFFlagStatus.Home, flag.State.FlagStatus);

        rig.Registry.Touch(flag.Handle, blue);

        Assert.Equal((byte)TFFlagStatus.Stolen, flag.State.FlagStatus);
    }

    [Fact]
    public void Spawn_precaches_the_paper_effect_and_both_trail_materials()
    {
        var (rig, red, _) = Map();

        // Stock's Precache (entity_capture_flag.cpp:403-410): the paper system
        // and BOTH team materials, whichever team the flag is.
        Assert.Contains("player_intel_papertrail",
                        rig.Engine.AllOf("Precache.PrecacheParticleSystem").Select(c => (string)c.Args[0]!));
        var materials = rig.Engine.AllOf("Precache.PrecacheModel").Select(c => (string)c.Args[0]!).ToList();
        Assert.Contains("effects/flagtrail_red.vmt", materials);
        Assert.Contains("effects/flagtrail_blu.vmt", materials);
    }
}
