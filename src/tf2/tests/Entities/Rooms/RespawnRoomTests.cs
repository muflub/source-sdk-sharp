using SourceSharp.Abi;
using SourceSharp.Shared;
using TF2.Server.Entities;
using TF2.Server.Entities.Rooms;
using TF2.Shared;
using TF2.Tests.Entities.Items;

using Xunit;

namespace TF2.Tests.Entities.Rooms;

/// <summary>
/// func_respawnroom and func_respawnroomvisualizer (stage 2.4):
/// <c>PointInRespawnRoom</c> (func_respawnroom.cpp), which makes a class
/// change instant (<c>CTFPlayer::HandleCommand_JoinClass</c>, tf_player.cpp:6981,
/// asks it with <c>bTouching_SameTeamOnly</c>), the room's team derived at
/// RoundActivate, and the visualizer following its room.
/// </summary>
public sealed class RespawnRoomTests
{
    private sealed class Rig
    {
        public readonly MapItemRig Map = new();
        public readonly TFRespawnRooms Rooms;
        public readonly Dictionary<EntityHandle, Vector> Origins = new();
        public readonly Dictionary<EntityHandle, (Vector, Vector)> Bounds = new();

        public Rig()
        {
            var engine = Map.Engine;
            engine.ReturnsEachCall("Entities.GetOrigin", () => Origins.GetValueOrDefault((EntityHandle)engine.Last("Entities.GetOrigin")!.Value.Arg(0)!))
                  .ReturnsEachCall("Entities.GetBounds", () => Bounds.GetValueOrDefault((EntityHandle)engine.Last("Entities.GetBounds")!.Value.Arg(0)!))
                  // Every player's centre is far from every room: a player is in a room by touch.
                  .Returns("Entities.GetWorldSpaceCenter", new Vector(9999f, 9999f, 9999f));
            Rooms = new TFRespawnRooms(Map.Registry, engine, Map.Players);
        }

        public FuncRespawnRoom Room(TFTeam team, Vector mins = default, Vector maxs = default)
        {
            var room = Map.Spawn<FuncRespawnRoom>(FuncRespawnRoom.MapClass, ("targetname", "room"), ("TeamNum", ((int)team).ToString()));
            Bounds[room.Handle] = (mins, maxs);
            room.OnServerActivate();
            return room;
        }
    }

    [Fact]
    public void Touching_your_teams_room_allows_the_instant_class_change_and_leaving_ends_it()
    {
        var rig = new Rig();
        var room = rig.Room(TFTeam.Red);
        var (player, _) = rig.Map.Players.Add(TFTeam.Red);

        Assert.False(rig.Rooms.InRespawnRoom(player, sameTeamOnly: true));

        rig.Map.Touch(room, player);
        Assert.True(rig.Rooms.InRespawnRoom(player, sameTeamOnly: true));
        Assert.Equal(1, rig.Rooms.TouchCount(player));

        rig.Map.Registry.EndTouch(room.Handle, player);
        Assert.False(rig.Rooms.InRespawnRoom(player, sameTeamOnly: true));
        Assert.Equal(0, rig.Rooms.TouchCount(player));
    }

    [Fact]
    public void The_enemy_teams_room_counts_only_when_any_team_will_do()
    {
        var rig = new Rig();
        var room = rig.Room(TFTeam.Blue);
        var (red, _) = rig.Map.Players.Add(TFTeam.Red);

        rig.Map.Touch(room, red);

        Assert.False(rig.Rooms.InRespawnRoom(red, sameTeamOnly: true));
        Assert.True(rig.Rooms.InRespawnRoom(red, sameTeamOnly: false));
    }

    [Fact]
    public void An_inactive_room_holds_nobody()
    {
        var rig = new Rig();
        var room = rig.Room(TFTeam.Red);
        var (player, _) = rig.Map.Players.Add(TFTeam.Red);
        rig.Map.Touch(room, player);

        rig.Map.Input(room, "SetInactive");
        Assert.False(room.Active);
        Assert.False(rig.Rooms.InRespawnRoom(player, sameTeamOnly: true));

        rig.Map.Input(room, "ToggleActive");
        Assert.True(rig.Rooms.InRespawnRoom(player, sameTeamOnly: true));
    }

    [Fact]
    public void A_point_inside_the_rooms_box_is_in_it()
    {
        var rig = new Rig();
        var room = rig.Room(TFTeam.Red, new Vector(-64f, -64f, 0f), new Vector(64f, 64f, 128f));
        rig.Origins[room.Handle] = new Vector(1000f, 0f, 0f);

        Assert.True(rig.Rooms.PointInRespawnRoom(new Vector(1000f, 10f, 64f)));
        Assert.False(rig.Rooms.PointInRespawnRoom(new Vector(0f, 10f, 64f)));
        Assert.False(rig.Rooms.PointInRespawnRoom(new Vector(1000f, 10f, 500f))); // above it
    }

    [Fact]
    public void A_room_with_no_team_takes_the_first_enabled_spawn_inside_it_at_round_activate()
    {
        var rig = new Rig();
        var room = rig.Room(TFTeam.Unassigned, new Vector(-64f, -64f, 0f), new Vector(64f, 64f, 128f));
        var disabledBlue = rig.Map.Spawn<InfoPlayerTeamSpawn>(InfoPlayerTeamSpawn.MapClass, ("TeamNum", "3"), ("StartDisabled", "1"));
        var red = rig.Map.Spawn<InfoPlayerTeamSpawn>(InfoPlayerTeamSpawn.MapClass, ("TeamNum", "2"));
        var outside = rig.Map.Spawn<InfoPlayerTeamSpawn>(InfoPlayerTeamSpawn.MapClass, ("TeamNum", "3"));
        rig.Origins[disabledBlue.Handle] = new Vector(0f, 0f, 16f);
        rig.Origins[red.Handle] = new Vector(10f, 0f, 16f);
        rig.Origins[outside.Handle] = new Vector(500f, 0f, 16f);

        Assert.Equal(0, room.Team);
        rig.Map.Input(room, "RoundActivate");

        Assert.Equal((int)TFTeam.Red, room.Team);
    }

    [Fact]
    public void A_room_the_map_gave_a_team_keeps_it_at_round_activate()
    {
        var rig = new Rig();
        var room = rig.Room(TFTeam.Blue, new Vector(-64f, -64f, 0f), new Vector(64f, 64f, 128f));
        var red = rig.Map.Spawn<InfoPlayerTeamSpawn>(InfoPlayerTeamSpawn.MapClass, ("TeamNum", "2"));
        rig.Origins[red.Handle] = new Vector(0f, 0f, 16f);

        rig.Map.Input(room, "RoundActivate");

        Assert.Equal((int)TFTeam.Blue, room.Team);
    }

    [Fact]
    public void A_visualizer_takes_its_rooms_team_and_follows_its_on_off_state()
    {
        var rig = new Rig();
        var room = rig.Room(TFTeam.Blue);
        var viz = rig.Map.Spawn<FuncRespawnRoomVisualizer>(FuncRespawnRoomVisualizer.MapClass, ("respawnroomname", "room"));

        Assert.False(viz.Shown); // no team yet
        rig.Map.Input(viz, "RoundActivate");

        Assert.Same(room, viz.Room);
        Assert.Equal((int)TFTeam.Blue, viz.Team);
        Assert.True(viz.Shown);
        Assert.Contains(viz, room.Visualizers);

        rig.Map.Input(room, "SetInactive");
        Assert.False(viz.Shown);
        Assert.False(viz.Active);
    }

    /// <summary>
    /// A visualizer's 32×32×32 box centred on <paramref name="at"/>.
    /// </summary>
    private static FuncRespawnRoomVisualizer Visualizer(Rig rig, int team, Vector at)
    {
        var viz = rig.Map.Spawn<FuncRespawnRoomVisualizer>(FuncRespawnRoomVisualizer.MapClass,
                                                           ("TeamNum", team.ToString()));
        rig.Origins[viz.Handle] = at;
        rig.Bounds[viz.Handle] = (new Vector(-16f, -16f, -16f), new Vector(16f, 16f, 16f));
        return viz;
    }

    /// <summary>
    /// PointsCrossRespawnRoomVisualizer (func_respawnroom.cpp:346-368): the
    /// segment clipped against every visualizer at MASK_ALL, fraction &lt; 1
    /// crossing. Deferred row 67; its callers: the fan pusher and the builder,
    /// the latter living in the buildings lane's <c>TFObjectBuilder</c>.
    /// </summary>
    [Fact]
    public void A_ray_through_a_visualizer_crosses_it_and_one_past_it_does_not()
    {
        var rig = new Rig();
        Visualizer(rig, (int)TFTeam.Blue, new Vector(1000f, 0f, 0f));

        Assert.True(rig.Rooms.PointsCrossRespawnRoomVisualizer(new Vector(900f, 0f, 0f), new Vector(1100f, 0f, 0f)));
        Assert.False(rig.Rooms.PointsCrossRespawnRoomVisualizer(new Vector(900f, 0f, 1000f), new Vector(1100f, 0f, 1000f)));
        // Fraction 1 is not a crossing: the near face is the far end exactly.
        Assert.False(rig.Rooms.PointsCrossRespawnRoomVisualizer(new Vector(900f, 0f, 0f), new Vector(984f, 0f, 0f)));
        // The box behind the ray meets none of it.
        Assert.False(rig.Rooms.PointsCrossRespawnRoomVisualizer(new Vector(1100f, 0f, 0f), new Vector(1200f, 0f, 0f)));
    }

    [Fact]
    public void The_visualizers_of_the_ignored_team_do_not_block_the_ray()
    {
        var rig = new Rig();
        Visualizer(rig, (int)TFTeam.Red, new Vector(1000f, 0f, 0f));

        var through = new Vector(900f, 0f, 0f);
        var beyond = new Vector(1100f, 0f, 0f);
        Assert.True(rig.Rooms.PointsCrossRespawnRoomVisualizer(through, beyond));
        Assert.False(rig.Rooms.PointsCrossRespawnRoomVisualizer(through, beyond, teamToIgnore: (int)TFTeam.Red));
        // Stock skips only when the ignore is a real team (nTeamToIgnore !=
        // TEAM_UNASSIGNED): ignoring the unassigned ignores nobody.
        Assert.True(rig.Rooms.PointsCrossRespawnRoomVisualizer(through, beyond, teamToIgnore: 0));
    }

    [Fact]
    public void An_unassigned_visualizer_crosses_even_when_the_caller_ignores_the_unassigned()
    {
        var rig = new Rig();
        Visualizer(rig, 0, new Vector(1000f, 0f, 0f));

        Assert.True(rig.Rooms.PointsCrossRespawnRoomVisualizer(new Vector(900f, 0f, 0f), new Vector(1100f, 0f, 0f),
                                                               teamToIgnore: 0));
    }

    [Fact]
    public void A_ray_starting_inside_a_visualizer_meets_it()
    {
        // Stock's start-solid clip gives fraction 0, and fraction 0 < 1 is a
        // crossing: a caller whose own centre is inside the wall counts.
        var rig = new Rig();
        Visualizer(rig, (int)TFTeam.Blue, new Vector(1000f, 0f, 0f));

        Assert.True(rig.Rooms.PointsCrossRespawnRoomVisualizer(new Vector(1000f, 0f, 0f), new Vector(1008f, 0f, 0f)));
    }

    [Fact]
    public void A_visualizer_is_never_made_solid()
    {
        var rig = new Rig();
        var viz = rig.Map.Spawn<FuncRespawnRoomVisualizer>(FuncRespawnRoomVisualizer.MapClass, ("solid_to_enemies", "1"));

        Assert.True(viz.SolidToEnemies);
        var solidity = rig.Map.Engine.AllOf("EntityPhysics.SetBrushSolidity").Select(c => c.Arg(1)).ToList();
        Assert.NotEmpty(solidity);
        Assert.All(solidity, s => Assert.Equal(BrushSolidity.None, s));
    }
}
