using SourceSharp.Ai;
using SourceSharp.Ai.Server.Nav;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Server.Horde.Boss;
using TF2.Shared.Movement;

namespace TF2.Descent.Server.Horde.Nav;

/// <summary>What part a named arena brush plays for the horde's paths (<see cref="HordeArenaObstacles"/>).</summary>
public enum ArenaBrushRole : byte
{
    /// <summary>Not an arena brush: its obstacle stays open, as every toggled brush was before.</summary>
    None = 0,

    /// <summary><c>descent_boss_exit</c>, the cover over the way down: blocks while enabled.</summary>
    Exit,

    /// <summary><c>descent_boss_pillar_&lt;n&gt;</c>: blocks while it stands (enabled).</summary>
    Pillar,

    /// <summary><c>descent_boss_rubble_&lt;n&gt;</c> (StartDisabled): blocks once enabled.</summary>
    Rubble,

    /// <summary><c>descent_boss_platform_&lt;p&gt;</c>: never blocks, in either state.</summary>
    Platform,
}

/// <summary>
/// The boss arena's toggled brushes (the ARNA contract, <see cref="ArenaStage"/>) as the horde's
/// paths plan round them. The level's <c>.nav3d</c> holds each named brush as a Toggle obstacle
/// whose whole box is open in the grid, its leaves naming the obstacle, with the floor under it
/// (the map session's probe of the built seed-1 levels, 2026-11-16): so a path is planned through
/// it unless the obstacle state blocks it. The rule agreed with the map session that day:
/// <list type="bullet">
/// <item>the exit cover blocks while enabled (robots route round its pit, which is solid while covered);</item>
/// <item>a pillar blocks while enabled (standing);</item>
/// <item>rubble blocks once enabled (it ships StartDisabled);</item>
/// <item>a platform section never blocks, in either state: the grid's floor under it (16) is one
/// step below its real top (32), so paths hold over it and bodies stand on the top; after its
/// drop the bed under it is the true floor, and the phase's staged slime is the hazard.</item>
/// </list>
/// Every other obstacle stays as the horde planned it before (all open: a robot's doors are open
/// doorways; a generated room's non-solid strips and plugs are toggled brushes too). Pure.
/// </summary>
public static class HordeArenaObstacles
{
    private const string PillarPrefix = "descent_boss_pillar_", RubblePrefix = "descent_boss_rubble_", PlatformPrefix = "descent_boss_platform_";

    /// <summary>The part the brush named <paramref name="name"/> plays (names are exact, lower case, decimal, unpadded).</summary>
    public static ArenaBrushRole RoleOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == ArenaStage.ExitName)
            return ArenaBrushRole.Exit;
        if (Numbered(name, PillarPrefix))
            return ArenaBrushRole.Pillar;
        if (Numbered(name, RubblePrefix))
            return ArenaBrushRole.Rubble;
        if (Numbered(name, PlatformPrefix))
            return ArenaBrushRole.Platform;
        return ArenaBrushRole.None;
    }

    /// <summary>Whether the brush stands enabled as the map ships it: rubble starts disabled, the rest enabled.</summary>
    public static bool EnabledAtStart(ArenaBrushRole role) => role != ArenaBrushRole.Rubble;

    /// <summary>Whether a brush of <paramref name="role"/> blocks a path when <paramref name="enabled"/>.</summary>
    public static bool Blocks(ArenaBrushRole role, bool enabled) =>
        enabled && role is ArenaBrushRole.Exit or ArenaBrushRole.Pillar or ArenaBrushRole.Rubble;

    /// <summary>
    /// The obstacles of <paramref name="nav"/> as the arena stands: <paramref name="open"/> (the
    /// level's all-open state, its prop cells kept) with each Toggle obstacle of an arena brush set
    /// by <see cref="Blocks"/> of its state in <paramref name="enabled"/> (a name not in it as the
    /// map ships it). A new state each call, so one a worker plans with never changes under it.
    /// </summary>
    public static NavObstacleState State(DerivedNav nav, NavObstacleState open, IReadOnlyDictionary<string, bool> enabled, out int blocking)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(enabled);
        NavObstacleState state = open.Clone();
        blocking = 0;
        for (int o = 0; o < nav.Obstacles.Length && o < state.Count; o++)
        {
            NavObstacleInfo info = nav.Obstacles[o];
            if (info.Kind != NavObstacleKind.Toggle)
                continue;
            ArenaBrushRole role = RoleOf(info.Name);
            if (role == ArenaBrushRole.None)
                continue;
            bool on = enabled.TryGetValue(info.Name, out bool e) ? e : EnabledAtStart(role);
            bool blocks = Blocks(role, on);
            state.Set(o, blocks);
            blocking += blocks ? 1 : 0;
        }

        return state;
    }

    /// <summary>A box whose top is no higher than this over a cell's floor is stepped onto, not gone round (TF's step).</summary>
    public static float StepUp => TFViewVectors.Params.StepSize;

    /// <summary>
    /// How much farther than a class's half-width a cell's centre is kept from a blocking box: half
    /// the level's cell (16 units square, every cell of the built levels). A path's corners lie on
    /// its cells' sides, so with the centres kept a half-width and this off, no point of a free cell
    /// puts the class's hull into the box.
    /// </summary>
    public const float CellHalf = 8f;

    /// <summary>
    /// <paramref name="state"/> for each class a robot plans by (indexed by
    /// <see cref="ClearanceClass"/>; Crouch, which no robot plans by, is <paramref name="state"/>):
    /// with every cell whose centre is within the class's half-width and <see cref="CellHalf"/> of a
    /// blocking Toggle obstacle's box on the floor plane, and whose hull's height meets the box above
    /// a step, blocked. The derivation gives a leaf one obstacle, the first that reaches it, and
    /// reaches with the game's own body's width, while the grid holds the box open: so the ring a
    /// box takes from a bigger hull, and the part of its own ring another obstacle holds, are open
    /// in the graph, and a path ran a hull into the box (live on the built level 10 seed 1,
    /// 2026-11-16: every class's path round landed rubble 1 ran 4 units off its box, the ring there
    /// fallen pillar 1's; the boss stood 52 s at its corner). Nothing blocking: <paramref name="state"/>
    /// for every class, nothing made. Pure; a new state for each class that differs.
    /// </summary>
    public static NavObstacleState[] ByClass(DerivedNav nav, NavObstacleState state)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(state);
        var classes = new NavObstacleState[2 + HordeHulls.Scales.Length];
        Array.Fill(classes, state);
        List<int>? boxes = null;
        for (int o = 0; o < nav.Obstacles.Length && o < state.Count; o++)
        {
            if (nav.Obstacles[o].Kind == NavObstacleKind.Toggle && state.Blocking(o))
                (boxes ??= []).Add(o);
        }

        if (boxes is null)
            return classes;
        NavGraph graph = nav.Graph;
        var near = new List<int>();
        for (int k = 0; k < classes.Length; k++)
        {
            var cls = (ClearanceClass)k;
            if (cls == ClearanceClass.Crouch)
                continue;
            float r = HordeHulls.HalfWidthOf(cls) + CellHalf, height = HordeHulls.HeightOf(cls);
            byte[]? cells = null;
            foreach (int o in boxes)
            {
                NavObstacleInfo box = nav.Obstacles[o];
                graph.CellsNear(box.Mins.X - r, box.Mins.Y - r, box.Maxs.X + r, box.Maxs.Y + r, near);
                foreach (int c in near)
                {
                    NavCell cell = graph.Cell(c);
                    AiVector m = cell.Centre;
                    if (!cell.Admits(cls) || m.X <= box.Mins.X - r || m.X >= box.Maxs.X + r || m.Y <= box.Mins.Y - r || m.Y >= box.Maxs.Y + r
                        || box.Maxs.Z <= cell.FloorZ + StepUp || box.Mins.Z >= cell.FloorZ + height)
                        continue;
                    if (cells is null)
                    {
                        cells = new byte[graph.CellCount];
                        NavCellBlocks? had = state.Cells;
                        for (int i = 0; had is not null && i < cells.Length; i++)
                            cells[i] = had.State(i);
                    }

                    cells[c] = NavCellBlocks.Blocked;
                }
            }

            if (cells is not null)
            {
                NavObstacleState grown = state.Clone();
                grown.Cells = new NavCellBlocks(cells);
                classes[k] = grown;
            }
        }

        return classes;
    }

    /// <summary>
    /// The obstacle every walk into <paramref name="cell"/> carries, or -1: the one whose leaf the
    /// cell is (the derivation gives a step the obstacle of the leaf it enters, when that has one).
    /// </summary>
    public static int Holder(NavGraph graph, int cell)
    {
        int holder = -1;
        foreach (NavEdge e in graph.EdgesTo(cell))
        {
            if (e.Type != NavEdgeType.Walk)
                continue;
            if (e.Obstacle < 0 || (holder >= 0 && e.Obstacle != holder))
                return -1;
            holder = e.Obstacle;
        }

        return holder;
    }

    /// <summary>The Toggle obstacles of <paramref name="nav"/> named <paramref name="name"/>.</summary>
    public static int Count(DerivedNav nav, string name)
    {
        ArgumentNullException.ThrowIfNull(nav);
        int n = 0;
        foreach (NavObstacleInfo info in nav.Obstacles)
            n += info.Kind == NavObstacleKind.Toggle && info.Name == name ? 1 : 0;
        return n;
    }

    private static bool Numbered(string name, string prefix)
    {
        if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        for (int i = prefix.Length; i < name.Length; i++)
        {
            if (name[i] is < '0' or > '9')
                return false;
        }

        return true;
    }
}
