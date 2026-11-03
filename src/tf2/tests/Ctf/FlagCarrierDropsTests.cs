using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities.Ctf;
using TF2.Server.Entities.Rooms;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// The carrier's drops (stage 2.5): <c>CTFPlayer::Event_Killed</c>'s defend
/// event and <c>RemoveAllItems</c>, the disconnect's <c>RemoveAllItems</c>,
/// the <c>dropitem</c> command, func_respawnflag and the respawn room.
/// </summary>
public sealed class FlagCarrierDropsTests
{
    private static (CtfRig Rig, ItemTeamFlag Red, EntityHandle Carrier, FlagCarrierDrops Drops) Carried()
    {
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red);
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[carrier].Center = new Vector(300f, 400f, 50f);
        rig.Registry.Touch(red.Handle, carrier);
        Assert.True(red.IsStolen);
        return (rig, red, carrier, new FlagCarrierDrops(rig.Rules, rig.Players, rig.Events));
    }

    private static PlayerDeath Death(EntityHandle player, EntityHandle attacker)
        => new(player, player.EntryIndex, attacker, new Vector(300f, 400f, 0f), EntityHandle.None);

    [Fact]
    public void A_carrier_killed_by_an_enemy_drops_the_flag_where_they_died_and_the_killer_defends()
    {
        var (rig, red, carrier, drops) = Carried();
        var killer = rig.Players.Add(2, TFTeam.Red);
        rig.Clock.Now = 30f;

        drops.OnPlayerDied(Death(carrier, killer));

        Assert.True(red.IsDropped);
        Assert.Equal(90f, red.ResetTime); // the stock 60 s return time
        Assert.Equal(new Vector(300f, 400f, 50f), (Vector)rig.Engine.Last("WorldQuery.EntityTraceHull")!.Value.Args[1]!);
        Assert.Equal(CtfRig.Ground, rig.OriginOf(red));
        int defend = rig.Events.Fired.IndexOf("flag Defend player=2 team=Red priority=8 carrier=3");
        int dropped = rig.Events.Fired.IndexOf("flag Dropped player=3 team=Red priority=8");
        Assert.True(defend >= 0 && dropped > defend, string.Join("\n", rig.Events.Fired));
    }

    [Fact]
    public void A_death_is_not_a_throw_and_a_suicide_or_world_death_defends_nothing()
    {
        var (rig, red, carrier, drops) = Carried();

        drops.OnPlayerDied(Death(carrier, carrier));

        Assert.True(red.IsDropped);
        Assert.True(red.AllowOwnerPickup);
        Assert.DoesNotContain(rig.Events.Fired, e => e.StartsWith("flag Defend", StringComparison.Ordinal));

        red.Reset();
        rig.Registry.Touch(red.Handle, carrier);
        drops.OnPlayerDied(Death(carrier, EntityHandle.None));
        Assert.DoesNotContain(rig.Events.Fired, e => e.StartsWith("flag Defend", StringComparison.Ordinal));
    }

    [Fact]
    public void A_death_of_a_player_carrying_nothing_touches_no_flag()
    {
        var (rig, red, _, drops) = Carried();
        var other = rig.Players.Add(4, TFTeam.Blue);
        int events = rig.Events.Fired.Count;

        drops.OnPlayerDied(Death(other, EntityHandle.None));

        Assert.True(red.IsStolen);
        Assert.Equal(events, rig.Events.Fired.Count);
    }

    [Fact]
    public void A_carrier_who_disconnects_drops_the_flag()
    {
        var (rig, red, carrier, drops) = Carried();

        ((IClientLifecycle)drops).OnClientDisconnected(carrier.EntryIndex);

        Assert.True(red.IsDropped);
        Assert.Contains("flag Dropped player=3 team=Red priority=8", rig.Events.Fired);
    }

    [Fact]
    public void Another_players_disconnect_leaves_the_flag_carried()
    {
        var (_, red, carrier, drops) = Carried();

        ((IClientLifecycle)drops).OnClientDisconnected(carrier.EntryIndex + 1);

        Assert.True(red.IsStolen);
    }

    [Fact]
    public void dropitem_throws_the_flag_and_is_claimed_even_with_nothing_to_drop()
    {
        var (rig, red, carrier, _) = Carried();
        var command = new TFDropItemCommand(rig.Rules);
        var other = rig.Players.Add(4, TFTeam.Blue);

        Assert.True(command.Handle(other, "dropitem", []));
        Assert.True(red.IsStolen);

        Assert.True(command.Handle(carrier, "DropItem", []));
        Assert.True(red.IsDropped);
        Assert.False(red.AllowOwnerPickup);
        Assert.False(command.Handle(carrier, "dropit", []));
    }

    [Fact]
    public void A_carrier_who_walks_into_a_respawn_flag_zone_drops_it_and_it_goes_home()
    {
        var (rig, red, carrier, _) = Carried();
        var zone = rig.Spawn<FuncRespawnFlag>(FuncRespawnFlag.MapClass, new Vector(1000f, 1000f, 0f));

        rig.Registry.Touch(zone.Handle, carrier);

        Assert.True(red.IsHome);
        Assert.Equal(CtfRig.RedHome, rig.OriginOf(red));
        Assert.Equal(1, red.OnReturn.FireCount);
    }

    [Fact]
    public void A_flag_dropped_inside_a_respawn_flag_zone_goes_home_and_one_outside_stays()
    {
        var (rig, red, carrier, _) = Carried();
        var zone = rig.Spawn<FuncRespawnFlag>(FuncRespawnFlag.MapClass, CtfRig.Ground); // bounds ±50 about it
        Assert.True(zone.Contains(CtfRig.Ground));
        Assert.False(zone.Contains(CtfRig.Ground + new Vector(51f, 0f, 0f)));

        red.RemoveFromCarrier(carrier);

        Assert.True(red.IsHome);

        rig.Registry.Input(zone.Handle, "Disable", string.Empty, 0f, EntityHandle.None);
        rig.Registry.Touch(red.Handle, carrier);
        red.RemoveFromCarrier(carrier);
        Assert.True(red.IsDropped);
    }

    // ---- the respawn room (CFuncRespawnRoom::RespawnRoomTouch, func_respawnroom.cpp:111-151) ----

    private static FuncRespawnRoom Room(CtfRig rig, TFTeam team)
        => rig.Spawn<FuncRespawnRoom>(FuncRespawnRoom.MapClass, new Vector(1000f, 1000f, 0f),
                                      ("TeamNum", ((int)team).ToString()));

    [Fact]
    public void A_carrier_who_runs_into_their_own_respawn_room_drops_the_flag()
    {
        var (rig, red, carrier, _) = Carried();
        var room = Room(rig, TFTeam.Blue);

        rig.Registry.Touch(room.Handle, carrier);

        Assert.True(red.IsDropped);
        Assert.False(red.AllowOwnerPickup);
        Assert.Contains("flag Dropped player=3 team=Red priority=8", rig.Events.Fired);
    }

    [Fact]
    public void An_enemy_carrier_running_into_the_room_keeps_the_flag()
    {
        var rig = new CtfRig();
        var blue = rig.Flag(TFTeam.Blue);
        var carrier = rig.Players.Add(3, TFTeam.Red);
        rig.Registry.Touch(blue.Handle, carrier);
        Assert.True(blue.IsStolen);
        var room = Room(rig, TFTeam.Blue);

        rig.Registry.Touch(room.Handle, carrier);

        Assert.True(blue.IsStolen);
        Assert.DoesNotContain(rig.Events.Fired, e => e.StartsWith("flag Dropped", StringComparison.Ordinal));
    }

    [Fact]
    public void A_teammate_without_the_flag_who_enters_the_room_changes_nothing()
    {
        var (rig, red, _, _) = Carried();
        var mate = rig.Players.Add(4, TFTeam.Blue);
        var room = Room(rig, TFTeam.Blue);
        int events = rig.Events.Fired.Count;

        rig.Registry.Touch(room.Handle, mate);

        Assert.True(red.IsStolen);
        Assert.Equal(events, rig.Events.Fired.Count);
    }

    [Fact]
    public void A_carrier_made_while_already_inside_the_room_drops_on_the_next_touch()
    {
        // RespawnRoomTouch is SetTouch (func_respawnroom.cpp:94): the room
        // hears every touch, so a player standing in their own room who comes
        // to carry the flag there is dropped without leaving and re-entering.
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red);
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        var room = Room(rig, TFTeam.Blue);

        rig.Registry.Touch(room.Handle, carrier);
        Assert.True(red.IsHome);

        rig.Registry.Touch(red.Handle, carrier);
        Assert.True(red.IsStolen);

        rig.Registry.Touch(room.Handle, carrier);

        Assert.True(red.IsDropped);
        Assert.Contains("flag Dropped player=3 team=Red priority=8", rig.Events.Fired);
    }

    // ---- the team change and the forced respawn (ForceChangeTeam, ForceRespawn) ----

    private static (CtfRig Rig, ItemTeamFlag Red, EntityHandle Carrier) CarriedOnly()
    {
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red);
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Registry.Touch(red.Handle, carrier);
        Assert.True(red.IsStolen);
        return (rig, red, carrier);
    }

    [Fact]
    public void A_team_change_makes_the_carrier_let_go()
    {
        // CTFPlayer::ForceChangeTeam's DropFlag (tf_player.cpp:6571): a thrown
        // drop, and the old carrier may not take it back for three seconds.
        var (rig, red, carrier) = CarriedOnly();
        var watcher = new FlagTeamChangeDrops(rig.Rules);

        watcher.TeamChanged(carrier, (int)TFTeam.Blue, (int)TFTeam.Red);

        Assert.True(red.IsDropped);
        Assert.False(red.AllowOwnerPickup);
        Assert.Contains("flag Dropped player=3 team=Red priority=8", rig.Events.Fired);
    }

    [Fact]
    public void A_team_change_of_a_player_carrying_nothing_drops_nothing()
    {
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red);
        var watcher = new FlagTeamChangeDrops(rig.Rules);
        var blue = rig.Players.Add(3, TFTeam.Blue);

        watcher.TeamChanged(blue, (int)TFTeam.Unassigned, (int)TFTeam.Blue);

        Assert.True(red.IsHome);
        Assert.Empty(rig.Events.Fired);
    }

    [Fact]
    public void A_forced_respawn_makes_the_carrier_let_go_before_the_kit()
    {
        // CTFPlayer::ForceRespawn drops the flag (tf_player.cpp:14625-14627)
        // before it strips the items; the class change's redeploy and
        // game_forcerespawn both walk the spawn hook.
        var (rig, red, carrier) = CarriedOnly();
        var hook = new FlagRespawnDrops(rig.Rules);

        hook.OnSpawning(carrier, rig.Clock.Now);

        Assert.True(red.IsDropped);
        Assert.False(red.AllowOwnerPickup);

        // The kit hand-back does nothing further.
        hook.OnSpawned(carrier, rig.Clock.Now);
        Assert.True(red.IsDropped);
    }

    [Fact]
    public void A_spawn_of_a_player_carrying_nothing_drops_nothing()
    {
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red);
        var hook = new FlagRespawnDrops(rig.Rules);
        var blue = rig.Players.Add(3, TFTeam.Blue);

        hook.OnSpawning(blue, rig.Clock.Now);
        hook.OnSpawned(blue, rig.Clock.Now);

        Assert.True(red.IsHome);
        Assert.Empty(rig.Events.Fired);
    }
}
