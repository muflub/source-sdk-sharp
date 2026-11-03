using System.Globalization;
using System.Text;
using Debug.Api.Server.Interfaces;
using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>
/// The AI's dev-API routes (plan_hl2mp_ai §7.1, §7.3): <c>/ai/nav</c>, the
/// level's derived graph (cells, the links that are not walks, obstacles and
/// their state), and <c>/ai/flow?cell=&amp;class=</c>, a flow field to a cell.
/// What the web UI's map tab draws its nav and flow layers from.
/// </summary>
[SideService(Side.Server, As = [typeof(IDebugRouteProvider)])]
public sealed class AiDebugRoutes(IHl2mpNavigation nav, Senses.AiBotRoutes? bots = null) : IDebugRouteProvider
{
    public IReadOnlyList<DebugRoute> Routes =>
    [
        new("GET", "/ai/nav", "the level's derived AI graph: cells [minX,minY,maxX,maxY,floorZ,classes,flags], non-walk links [from,to,type], obstacles"),
        new("GET", "/ai/flow", "a flow field: ?cell=N (or x=&y=&z=) &class=stand|crouch; each cell's next cell (-1 none) and cost (-1 unreachable)"),
        .. bots?.Routes ?? [],
    ];

    public DebugRouteReply Handle(string path, IReadOnlyDictionary<string, string> query)
    {
        if (bots is not null && Senses.AiBotRoutes.Serves(path))
            return bots.Handle(path, query);

        if (nav.Current is not { } loaded)
            return new DebugRouteReply(409, $"{{\"error\":{NavJson.Quote(nav.Refusal ?? "no navigation")}}}");
        NavObstacleState obstacles = nav.Obstacles ?? new NavObstacleState(loaded.Nav.Graph.ObstacleCount);
        if (path == "/ai/nav")
            return new DebugRouteReply(200, NavJson.Nav(loaded.Map, loaded.Nav, obstacles));

        NavGraph g = loaded.Nav.Graph;
        ClearanceClass cls = query.GetValueOrDefault("class") == "crouch" ? ClearanceClass.Crouch : ClearanceClass.Stand;
        CellId cell = CellId.None;
        if (query.TryGetValue("cell", out string? c) && int.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            && index >= 0 && index < g.CellCount)
            cell = new CellId(index);
        else if (TryFloat(query, "x", out float x) && TryFloat(query, "y", out float y) && TryFloat(query, "z", out float z))
            cell = NavPoints.CellNear(g, new AiVector(x, y, z), cls);
        if (!cell.IsValid)
            return new DebugRouteReply(400, "{\"error\":\"name a cell: ?cell=N, or ?x=&y=&z= on the graph\"}");

        var field = new FlowField();
        field.Build(g, cell, cls, obstacles);
        return new DebugRouteReply(200, NavJson.Flow(g, field));
    }

    private static bool TryFloat(IReadOnlyDictionary<string, string> q, string key, out float v)
    {
        v = 0f;
        return q.TryGetValue(key, out string? s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}

/// <summary>The graph and a field as the routes write them.</summary>
public static class NavJson
{
    public static string Nav(string map, DerivedNav nav, NavObstacleState obstacles)
    {
        NavGraph g = nav.Graph;
        var json = new StringBuilder(g.CellCount * 40);
        json.Append("{\"map\":").Append(Quote(map)).Append(",\"cells\":").Append(g.CellCount)
            .Append(",\"edges\":").Append(g.EdgeCount).Append(",\"cell\":[");
        for (int c = 0; c < g.CellCount; c++)
        {
            NavCell cell = g.Cell(c);
            json.Append(c == 0 ? "" : ",").Append(F(cell.MinX)).Append(',').Append(F(cell.MinY)).Append(',').Append(F(cell.MaxX))
                .Append(',').Append(F(cell.MaxY)).Append(',').Append(F(cell.FloorZ)).Append(',').Append((int)cell.Classes)
                .Append(',').Append((int)cell.Flags);
        }

        json.Append("],\"link\":[");
        bool first = true;
        for (int c = 0; c < g.CellCount; c++)
        {
            foreach (NavEdge e in g.EdgesFrom(c))
            {
                if (e.Type == NavEdgeType.Walk)
                    continue;
                json.Append(first ? "" : ",").Append(e.From).Append(',').Append(e.To).Append(',').Append((int)e.Type);
                first = false;
            }
        }

        json.Append("],\"obstacles\":[");
        for (int o = 0; o < nav.Obstacles.Length; o++)
        {
            NavObstacleInfo info = nav.Obstacles[o];
            json.Append(o == 0 ? "" : ",").Append("{\"name\":").Append(Quote(info.Name)).Append(",\"kind\":").Append(Quote(info.Kind.ToString()))
                .Append(",\"blocking\":").Append(o < obstacles.Count && obstacles.Blocking(o) ? "true" : "false").Append('}');
        }

        return json.Append("]}").ToString();
    }

    public static string Flow(NavGraph g, FlowField field)
    {
        var json = new StringBuilder(g.CellCount * 12);
        json.Append("{\"objective\":").Append(field.Objective.Index).Append(",\"class\":").Append(Quote(field.Class == ClearanceClass.Stand ? "stand" : "crouch"))
            .Append(",\"reachable\":").Append(field.Reachable).Append(",\"next\":[");
        for (int c = 0; c < g.CellCount; c++)
            json.Append(c == 0 ? "" : ",").Append(field.NextFrom(c).IsValid ? field.NextFrom(c).Index : -1);
        json.Append("],\"cost\":[");
        for (int c = 0; c < g.CellCount; c++)
        {
            float cost = field.CostAt(c);
            json.Append(c == 0 ? "" : ",").Append(float.IsFinite(cost) ? ((int)MathF.Round(cost)).ToString(CultureInfo.InvariantCulture) : "-1");
        }

        return json.Append("]}").ToString();
    }

    private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Quote(string s) => System.Text.Json.JsonSerializer.Serialize(s);
}
