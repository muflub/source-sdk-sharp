using SourceSharp.Abi;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Spy;

/// <summary>
/// The disguise's run (<c>CTFPlayerShared::Disguise</c>,
/// tf_player_shared.cpp:8157-8269), its completion
/// (<c>CompleteDisguise</c> :8300-8344) and the <c>lastdisguise</c> command
/// (<c>cc_tf_player_lastdisguise</c>, c_tf_player.cpp:9594-9633).
/// </summary>
public sealed class TFDisguiseTests
{
    /// <summary>A disguise asked at 100 and completed by the think.</summary>
    private static SpyRig Disguised(TFClass disguiseClass = TFClass.HeavyWeapons, TFTeam disguiseTeam = TFTeam.Blue)
    {
        var rig = new SpyRig();
        Assert.True(rig.Slice.Disguise(100f, disguiseClass, disguiseTeam));
        rig.Slice.Tick(102.1f);
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));
        return rig;
    }

    [Fact]
    public void A_disguise_takes_two_seconds_then_names_the_carrier()
    {
        var rig = new SpyRig();

        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));

        // TF_TIME_TO_DISGUISE is 2.0 (tf_shareddefs.h:674).
        rig.Slice.Tick(101.9f);
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));

        rig.Slice.Tick(102.1f);
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
        Assert.Equal((int)TFClass.HeavyWeapons, rig.State.DisguiseClass);
        Assert.Equal((int)TFTeam.Blue, rig.State.DisguiseTeam);
    }

    [Fact]
    public void A_disguise_while_disguised_is_quick()
    {
        var rig = Disguised();

        Assert.True(rig.Slice.Disguise(103f, TFClass.Soldier, TFTeam.Blue));

        // TF_TIME_TO_QUICK_DISGUISE is 0.5 (tf_shareddefs.h:675).
        rig.Slice.Tick(103.4f);
        Assert.Equal((int)TFClass.HeavyWeapons, rig.State.DisguiseClass);

        rig.Slice.Tick(103.6f);
        Assert.Equal((int)TFClass.Soldier, rig.State.DisguiseClass);
    }

    [Fact]
    public void Disguising_as_oneself_ends_the_disguise()
    {
        var rig = Disguised();

        // The spy's own class and team (red): stock resets everything instead.
        Assert.True(rig.Slice.Disguise(104f, TFClass.Spy, TFTeam.Red));

        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguised));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.DisguiseWearingoff));
    }

    [Fact]
    public void The_same_disguise_twice_is_refused()
    {
        var rig = Disguised();

        // Already disguised as this class and team (tf_player_shared.cpp:8184):
        // the ask is refused, not restarted.
        Assert.False(rig.Slice.Disguise(103f, TFClass.HeavyWeapons, TFTeam.Blue));
        Assert.Equal((int)TFClass.HeavyWeapons, rig.State.DisguiseClass);
    }

    [Fact]
    public void A_repeat_of_a_disguise_in_flight_is_refused()
    {
        var rig = new SpyRig();

        // The same ask while the first is still running
        // (tf_player_shared.cpp:8206-8211).
        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        Assert.False(rig.Slice.Disguise(100.1f, TFClass.HeavyWeapons, TFTeam.Blue));
    }

    // ---- the cloak-consuming disguise (tf_player_shared.cpp:8214-8234) ----

    /// <summary>The spy's own manager answering stock's
    /// <c>mod_disguise_consumes_cloak</c> (the additive format: the hook rides
    /// an int seed of 0, so an additive 1 reads as "the item consumes").</summary>
    private static AttributeContainer CloakConsumer()
    {
        var def = new AttributeDefinition(30043, "disguise consumes cloak", "mod_disguise_consumes_cloak",
                                        AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                        true, false, AttributeValueType.Default, MergedKeys.Block("cloak"));
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(def, 1f));
        return manager;
    }

    [Fact]
    public void The_cloak_consuming_item_drains_the_meter_outside_a_respawn_room()
    {
        var rig = new SpyRig();
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = CloakConsumer();
        rig.State.CloakMeter = 100f;

        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));

        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
        Assert.Equal(0f, rig.State.CloakMeter);                       // :8230
        Assert.Collection(rig.Rooms.Asks,                             // :8228, the spy's own centre
            ask => { Assert.Equal(SpyRig.Spy.EntryIndex, ask.Target);
                     Assert.Equal(new Vector(0f, 0f, 64f), ask.Origin); });
    }

    [Fact]
    public void The_cloak_consuming_item_keeps_the_meter_inside_a_respawn_room()
    {
        var rig = new SpyRig();
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = CloakConsumer();
        rig.State.CloakMeter = 100f;
        rig.Rooms.InRoom.Add(SpyRig.Spy.EntryIndex);

        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));

        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
        Assert.Equal(100f, rig.State.CloakMeter);                     // :8228 keeps it
    }

    [Fact]
    public void The_cloak_consuming_item_refuses_a_disguise_under_a_full_meter()
    {
        var rig = new SpyRig();
        rig.Owners.Managers[SpyRig.Spy.EntryIndex] = CloakConsumer();
        rig.State.CloakMeter = 99f;

        Assert.False(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));   // :8220-8225

        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
        Assert.Equal(99f, rig.State.CloakMeter);
        Assert.Equal(1, rig.Engine.CountOf("Sound.EntityEmitSound"));
        Assert.Equal("Player.UseDeny", rig.Engine.Last("Sound.EntityEmitSound")!.Value.ArgAs<string>(1));
        Assert.Equal(SpyRig.Spy, rig.Engine.Last("Sound.EntityEmitSound")!.Value.ArgAs<EntityHandle>(0));
        Assert.Empty(rig.Rooms.Asks);       // the refusal returns before the room ask
    }

    [Fact]
    public void A_spy_without_the_item_disguises_free_on_a_low_meter()
    {
        var rig = new SpyRig();
        rig.State.CloakMeter = 50f;

        Assert.True(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));

        Assert.True(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
        Assert.Equal(50f, rig.State.CloakMeter);
        Assert.Empty(rig.Rooms.Asks);       // no hook, no room ask
    }

    [Fact]
    public void A_disguise_out_of_the_teams_and_classes_is_refused()
    {
        var rig = new SpyRig();

        Assert.False(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Spectator));
        Assert.False(rig.Slice.Disguise(100f, TFClass.Undefined, TFTeam.Blue));
        Assert.False(rig.Slice.Disguise(100f, (TFClass)TFClass.CountAll, TFTeam.Blue));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
    }

    [Fact]
    public void Lastdisguise_redoes_the_last_disguise()
    {
        var rig = Disguised();

        // The attack's end: the disguise goes, and lastdisguise brings it back.
        rig.Slice.RemoveDisguise();
        Assert.True(rig.Spies.LastDisguise(SpyRig.Spy.EntryIndex));
        rig.Slice.Tick(102.1f);

        Assert.Equal((int)TFClass.HeavyWeapons, rig.State.DisguiseClass);
        Assert.Equal((int)TFTeam.Blue, rig.State.DisguiseTeam);
    }

    [Fact]
    public void Death_clears_the_disguise_fields_so_the_same_disguise_is_fresh_again()
    {
        var rig = Disguised();

        // Death and respawn: the spawn's RemoveAllCond removes condition by
        // condition (tf_player_shared.cpp:1545-1556), running OnConditionRemoved
        // with each (:1182), which leaves no
        // disguise on the carrier — so the same disguise is askable again, not
        // refused by the already-wearing-it guard (the fields, :8184).
        rig.Conditions.RemoveAll(SpyRig.Spy);

        Assert.Equal((int)TFClass.Undefined, rig.State.DisguiseClass);
        Assert.Equal((int)TFTeam.Unassigned, rig.State.DisguiseTeam);
        Assert.True(rig.Slice.Disguise(103f, TFClass.HeavyWeapons, TFTeam.Blue));
    }

    [Fact]
    public void Lastdisguise_with_none_draws_a_class_on_the_enemy_team()
    {
        var rig = new SpyRig();
        rig.Random.Int = (int)TFClass.Soldier;

        Assert.True(rig.Spies.LastDisguise(SpyRig.Spy.EntryIndex));
        rig.Slice.Tick(102.1f);

        Assert.Equal((int)TFClass.Soldier, rig.State.DisguiseClass);
        Assert.Equal((int)TFTeam.Blue, rig.State.DisguiseTeam);
    }

    [Fact]
    public void The_disguise_command_maps_stocks_team_numbers()
    {
        var rig = new SpyRig();
        var command = new TF2.Server.Player.TFSpyDisguiseCommand(rig.Spies, rig.Engine);

        // Stock's client intercept: -1 is the enemy team (1 is BLUE, anything
        // else RED, -2 the player's own). A red spy asking class 1 on -1.
        Assert.True(command.Handle(SpyRig.Spy, "disguise", new[] { "1", "-1" }));
        rig.Slice.Tick(102.1f);
        Assert.Equal((int)TFTeam.Blue, rig.State.DisguiseTeam);
        Assert.Equal((int)TFClass.Scout, rig.State.DisguiseClass);

        // 3 is RED by the same mapping, and the class rides as its number.
        Assert.True(command.Handle(SpyRig.Spy, "disguise", new[] { "3", "3" }));
        rig.Slice.Tick(103.1f);
        Assert.Equal((int)TFTeam.Red, rig.State.DisguiseTeam);
        Assert.Equal((int)TFClass.Soldier, rig.State.DisguiseClass);
    }

    [Fact]
    public void A_dead_spy_cannot_disguise()
    {
        var rig = new SpyRig();
        rig.SpyAlive = false;    // CanDisguise (tf_player_shared.cpp:12944-12967)

        Assert.False(rig.Slice.Disguise(100f, TFClass.HeavyWeapons, TFTeam.Blue));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));

        // The commands answer consumed, but nothing starts (CanDisguise's gate).
        var command = new TF2.Server.Player.TFSpyDisguiseCommand(rig.Spies, rig.Engine);
        Assert.True(command.Handle(SpyRig.Spy, "disguise", new[] { "6", "3" }));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
    }

    [Fact]
    public void Ending_an_enemy_disguise_drops_the_teleporters_condition()
    {
        var rig = Disguised();
        rig.Conditions.Add(SpyRig.Spy, TFCond.Teleported, provider: SpyRig.Spy);

        rig.Slice.RemoveDisguise();

        // tf_player_shared.cpp:8374-8382: the disguise team is gone, and the
        // teleporter's condition with it.
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Teleported));

        // An own-team disguise leaves it: the teleporter's team is still right.
        var own = Disguised(TFClass.Soldier, TFTeam.Red);
        own.Conditions.Add(SpyRig.Spy, TFCond.Teleported, provider: SpyRig.Spy);
        own.Slice.RemoveDisguise();
        Assert.True(own.Conditions.InCond(SpyRig.Spy, TFCond.Teleported));
    }

    [Fact]
    public void Lastdisguise_names_the_class_its_argument_names()
    {
        var rig = new SpyRig();
        var command = new TF2.Server.Player.TFSpyLastDisguiseCommand(rig.Spies);

        // The optional class argument (c_tf_player.cpp:9611-9615).
        Assert.True(command.Handle(SpyRig.Spy, "lastdisguise", new[] { "2" }));
        rig.Slice.Tick(102.1f);

        Assert.Equal((int)TFClass.Sniper, rig.State.DisguiseClass);
        Assert.Equal((int)TFTeam.Blue, rig.State.DisguiseTeam);
    }

    [Fact]
    public void Lastdisguises_random_draw_runs_to_the_civilian()
    {
        var rig = new SpyRig();
        rig.Random.Int = (int)TFClass.Civilian;    // TF_LAST_NORMAL_CLASS (tf_shareddefs.h:199)

        Assert.True(rig.Spies.LastDisguise(SpyRig.Spy.EntryIndex));
        rig.Slice.Tick(102.1f);

        Assert.Equal((int)TFClass.Civilian, rig.State.DisguiseClass);
        Assert.Equal((int)TFTeam.Blue, rig.State.DisguiseTeam);
    }

    [Fact]
    public void A_disguise_command_that_parses_nothing_is_consumed()
    {
        var rig = new SpyRig();
        var command = new TF2.Server.Player.TFSpyDisguiseCommand(rig.Spies, rig.Engine);

        // Two arguments, so the parse arm is reached and not the arity check.
        Assert.True(command.Handle(SpyRig.Spy, "disguise", new[] { "banana", "-1" }));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
    }

    [Fact]
    public void A_command_that_is_not_the_spy_ones_is_left_alone()
    {
        var rig = new SpyRig();
        var command = new TF2.Server.Player.TFSpyDisguiseCommand(rig.Spies, rig.Engine);

        Assert.False(command.Handle(SpyRig.Spy, "jointeam", new[] { "red" }));
        Assert.False(command.Handle(SpyRig.Spy, "disguise", new[] { "notanumber" }));
        Assert.False(rig.Conditions.InCond(SpyRig.Spy, TFCond.Disguising));
    }
}
