using System.Text.Json;
using Debug.Api.Server.Interfaces;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// The AI's dev-API routes (plan_hl2mp_ai §7.3) over the course: /ai/nav
/// carries every cell and every non-walk link with the graph's own values,
/// the obstacles with their state; /ai/flow's next and cost are the field's;
/// with no navigation both refuse with the level's refusal.
/// </summary>
public sealed class AiDebugRoutesTests
{
    private sealed class Navigation(LoadedNav? current, string? refusal, NavObstacleState? obstacles) : IHl2mpNavigation
    {
        public LoadedNav? Current => current;
        public string? Refusal => refusal;
        public NavObstacleState? Obstacles => obstacles;
        public NavObstacleState? ObstaclesFor(SourceSharp.Abi.EntityHandle player) => Obstacles;

        public NavSight? Sight => null;


        public ChokeSlot? Chokes => null;
    }

    private static readonly Lazy<LoadedNav> Course = new(() =>
    {
        byte[] bsp = TinyBsp.Make(TinyBsp.Stock);
        NavLoadResult r = NavLoader.Load("course", p => p.EndsWith(".bsp", StringComparison.Ordinal) ? bsp
                                                        : NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write(), null);
        return r.Nav!;
    });

    private static (AiDebugRoutes Routes, NavObstacleState Obstacles) Make()
    {
        var obstacles = new NavObstacleState(Course.Value.Nav.Graph.ObstacleCount);
        obstacles.Set(0, true);
        return (new AiDebugRoutes(new Navigation(Course.Value, null, obstacles)), obstacles);
    }

    [Fact]
    public void NavCarriesEveryCellAndNonWalkLinkAndTheObstacleState()
    {
        DebugRouteReply reply = Make().Routes.Handle("/ai/nav", new Dictionary<string, string>());
        using JsonDocument doc = JsonDocument.Parse(reply.Json);
        NavGraph g = Course.Value.Nav.Graph;
        int nonWalk = Enumerable.Range(0, g.CellCount).Sum(c => g.EdgesFrom(c).ToArray().Count(e => e.Type != NavEdgeType.Walk));

        Assert.Equal(200, reply.Status);
        Assert.Equal(g.CellCount * 7, doc.RootElement.GetProperty("cell").GetArrayLength());
        Assert.Equal(nonWalk * 3, doc.RootElement.GetProperty("link").GetArrayLength());
        JsonElement cell = doc.RootElement.GetProperty("cell");
        NavCell c7 = g.Cell(7);
        Assert.Equal((c7.MinX, c7.FloorZ, (int)c7.Classes), (cell[7 * 7].GetSingle(), cell[7 * 7 + 4].GetSingle(), cell[7 * 7 + 5].GetInt32()));
        JsonElement door = doc.RootElement.GetProperty("obstacles")[0];
        Assert.Equal(("door_1", "Door", true), (door.GetProperty("name").GetString(), door.GetProperty("kind").GetString(), door.GetProperty("blocking").GetBoolean()));
    }

    [Fact]
    public void FlowIsTheFieldsNextAndCost()
    {
        NavGraph g = Course.Value.Nav.Graph;
        int target = g.FindCell(new AiVector(40, 40, 16)).Index, from = g.FindCell(new AiVector(560, 300, 16)).Index;
        DebugRouteReply reply = Make().Routes.Handle("/ai/flow", new Dictionary<string, string> { ["cell"] = $"{target}", ["class"] = "stand" });
        using JsonDocument doc = JsonDocument.Parse(reply.Json);
        var field = new FlowField();
        field.Build(g, new CellId(target), ClearanceClass.Stand, Make().Obstacles);

        Assert.Equal(200, reply.Status);
        Assert.Equal(field.NextFrom(from).Index, doc.RootElement.GetProperty("next")[from].GetInt32());
        Assert.Equal((int)MathF.Round(field.CostAt(from)), doc.RootElement.GetProperty("cost")[from].GetInt32());
        Assert.Equal(-1, doc.RootElement.GetProperty("next")[target].GetInt32());
        Assert.Equal(field.Reachable, doc.RootElement.GetProperty("reachable").GetInt32());
    }

    [Fact]
    public void FlowByAPointFindsItsCell()
    {
        DebugRouteReply reply = Make().Routes.Handle("/ai/flow", new Dictionary<string, string> { ["x"] = "40", ["y"] = "40", ["z"] = "16" });
        using JsonDocument doc = JsonDocument.Parse(reply.Json);
        Assert.Equal(Course.Value.Nav.Graph.FindCell(new AiVector(40, 40, 16)).Index, doc.RootElement.GetProperty("objective").GetInt32());
    }

    [Fact]
    public void WithNoNavigationTheRoutesRefuseWithTheLevelsRefusal()
    {
        var routes = new AiDebugRoutes(new Navigation(null, "no navigation for dm_x: expected maps/dm_x.nav3d", null));
        DebugRouteReply reply = routes.Handle("/ai/nav", new Dictionary<string, string>());
        Assert.Equal((409, "{\"error\":\"no navigation for dm_x: expected maps/dm_x.nav3d\"}"), (reply.Status, reply.Json));
    }
}
