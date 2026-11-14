using SourceSharp.Ai.Server.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// Per-team obstacle solidity (plan_bots_teams_tf2 P6 step 2, C.7): a volume solid to every
/// team but one closes its cells to the others' players and to nobody else, and TF's respawn
/// room visualizers are such volumes, read from the map's entities.
/// </summary>
public class NavTeamBlockersTests
{
    private const ClearanceClasses Both = ClearanceClasses.Stand | ClearanceClasses.Crouch;

    /// <summary>A corridor of five 32 u cells along x, a doorway between the second and the third.</summary>
    private static NavGraph Corridor()
    {
        var b = new NavGraphBuilder();
        var cells = new CellId[5];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = b.AddCell(new NavCell(i * 32f, 0f, (i + 1) * 32f, 32f, 0f, Both, NavCellFlags.None, 1f));
        for (int i = 0; i + 1 < cells.Length; i++)
            b.AddBoth(cells[i], cells[i + 1], NavEdgeType.Walk);
        return b.Build();
    }

    /// <summary>Red's (team 2) visualizer: a thin brush across the corridor at x 64.</summary>
    private static readonly TeamSolid RedDoor = new(new AiVector(63f, 0f, 0f), new AiVector(65f, 32f, 128f), 2);

    private static readonly NavNumbers Body = TF2NavNumbers.Numbers;

    [Fact]
    public void AVolumeSolidToTheEnemyClosesItsCellsToTheEnemy()
    {
        NavTeamBlockers blockers = NavTeamBlockers.From(Corridor(), [RedDoor], Body);

        // The brush at 63..65, and half TF's 48 u hull either side: 39..89, the second and third cells.
        NavCellBlocks blue = blockers.For(3)!;
        Assert.Equal([NavCellBlocks.Free, NavCellBlocks.Blocked, NavCellBlocks.Blocked, NavCellBlocks.Free, NavCellBlocks.Free],
                     Enumerable.Range(0, 5).Select(blue.State).ToArray());
    }

    [Fact]
    public void ItClosesNothingToItsOwnTeam() => Assert.Null(NavTeamBlockers.From(Corridor(), [RedDoor], Body).For(2));

    [Fact]
    public void AVolumeAboveAStandingBodyClosesNothing()
    {
        var high = RedDoor with { Mins = RedDoor.Mins with { Z = 200f }, Maxs = RedDoor.Maxs with { Z = 260f } };
        Assert.Null(NavTeamBlockers.From(Corridor(), [high], Body).For(3));
    }

    [Fact]
    public void TheEnemyHasNoRouteThroughAndTheTeamDoes()
    {
        NavGraph g = Corridor();
        NavTeamBlockers blockers = NavTeamBlockers.From(g, [RedDoor], Body);
        var field = new FlowField();

        field.Build(g, new CellId(4), ClearanceClass.Crouch, new NavObstacleState(0) { TeamCells = blockers.For(3) });
        Assert.False(float.IsFinite(field.CostAt(0)));

        field.Build(g, new CellId(4), ClearanceClass.Crouch, new NavObstacleState(0) { TeamCells = blockers.For(2) });
        Assert.True(float.IsFinite(field.CostAt(0)));
    }

    [Fact]
    public void TheTeamsCellsRideEveryCopyOfTheState()
    {
        NavCellBlocks? cells = NavTeamBlockers.From(Corridor(), [RedDoor], Body).For(3);
        var state = new NavObstacleState(0) { TeamCells = cells };
        var lane = new NavObstacleState(0);
        lane.CopyFrom(state);
        Assert.All(new[] { state.Clone(), state.Shoved(), lane }, s => Assert.Same(cells, s.TeamCells));
    }

    private static MapEntity Entity(string cls, string? name, AiVector origin, AiVector? mins, AiVector? maxs, params (string, string)[] keys)
        => new(cls, name, origin, mins, maxs, Keys: [.. keys.Select(k => new KeyValuePair<string, string>(k.Item1, k.Item2))]);

    private static readonly AiVector Lo = new(0f, 0f, 0f), Hi = new(256f, 256f, 128f);

    private static MapEntity Visualizer(params (string, string)[] keys)
        => Entity(TF2TeamSolids.Visualizer, null, default, new AiVector(256f, 96f, 0f), new AiVector(264f, 160f, 128f),
                  [("respawnroomname", "spawn_red"), .. keys]);

    [Fact]
    public void AVisualizerIsSolidToTheEnemyOfItsRoomsTeam()
    {
        var entities = new MapEntities([Entity(TF2TeamSolids.Room, "spawn_red", default, Lo, Hi, ("TeamNum", "2")), Visualizer()], null);

        TeamSolid solid = Assert.Single(TF2TeamSolids.Read(entities));
        Assert.Equal((2, 256f, 264f), (solid.Team, solid.Mins.X, solid.Maxs.X));
    }

    [Fact]
    public void ARoomWithNoTeamTakesTheTeamOfASpawnInsideIt()
    {
        var entities = new MapEntities([
            Entity(TF2TeamSolids.Room, "spawn_red", default, Lo, Hi),
            Entity(TF2TeamSolids.TeamSpawn, null, new AiVector(500f, 500f, 0f), null, null, ("TeamNum", "2")),
            Entity(TF2TeamSolids.TeamSpawn, null, new AiVector(64f, 64f, 0f), null, null, ("TeamNum", "2"), ("StartDisabled", "1")),
            Entity(TF2TeamSolids.TeamSpawn, null, new AiVector(128f, 128f, 0f), null, null, ("TeamNum", "3")),
            Visualizer(),
        ], null);

        Assert.Equal(3, Assert.Single(TF2TeamSolids.Read(entities)).Team);
    }

    [Fact]
    public void AVisualizerNotSolidToEnemiesIsNoSolid()
    {
        var entities = new MapEntities([Entity(TF2TeamSolids.Room, "spawn_red", default, Lo, Hi, ("TeamNum", "2")),
                                        Visualizer(("solid_to_enemies", "0"))], null);
        Assert.Empty(TF2TeamSolids.Read(entities));
    }

    [Fact]
    public void AVisualizerWithNoRoomIsNoSolid()
        => Assert.Empty(TF2TeamSolids.Read(new MapEntities([Visualizer()], null)));

    [Fact]
    public void ARoomWithNoTeamAndNoSpawnGivesNoSolid()
        => Assert.Empty(TF2TeamSolids.Read(new MapEntities([Entity(TF2TeamSolids.Room, "spawn_red", default, Lo, Hi), Visualizer()], null)));

    [Fact]
    public void AnEntitysKeysAreReadAsTheLumpHoldsThem()
    {
        MapEntity e = Entity("x", null, default, null, null, ("TeamNum", "3"), ("teamnum", "9"), ("bad", "q"));
        Assert.Equal((3, 0, 7, (string?)null), (e.Int("TeamNum"), e.Int("bad"), e.Int("missing", 7), e.Get("TEAMNUM")));
    }
}
