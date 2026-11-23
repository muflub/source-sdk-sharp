using SourceSharp.Ai;

namespace TF2.Descent.Server.Horde.Nav;

/// <summary>
/// The components of one clearance class's cells under one obstacle state: cells joined by
/// passable edges taken either way (<see cref="NavGraph.Passable"/>), so two cells in different
/// components have no path (one component can still lack one, a drop taken one way, which the
/// search finds). Immutable once made; any number of AI workers read it.
/// </summary>
public sealed class NavComponents
{
    private readonly int[] _comp;

    private NavComponents(int[] comp, int largest)
    {
        _comp = comp;
        Largest = largest;
    }

    /// <summary>The component holding the most cells (the lower on a tie); -1 for none.</summary>
    public int Largest { get; }

    /// <summary>The component of <paramref name="cell"/>, -1 for a cell the class does not stand on or that is blocked.</summary>
    public int Of(CellId cell) => cell.IsValid && (uint)cell.Index < (uint)_comp.Length ? _comp[cell.Index] : -1;

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are in one component.</summary>
    public bool Joined(CellId a, CellId b) => Of(a) is var k and >= 0 && k == Of(b);

    /// <summary>
    /// Whether a search from <paramref name="start"/> can reach <paramref name="goal"/>'s component
    /// under <paramref name="state"/> (the state these components were made with): its own
    /// component's, or, for a start in no component (a body pressed into the cells a class keeps off
    /// a box, blocked for its hull), that of a cell it steps out to, as the search steps out of its
    /// start (a step is refused only into a blocked cell).
    /// </summary>
    public bool JoinedFrom(NavGraph graph, CellId start, CellId goal, ClearanceClass cls, NavObstacleState state)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(state);
        int k = Of(goal);
        if (k < 0 || !start.IsValid)
            return false;
        if (Of(start) is var own and >= 0)
            return own == k;
        foreach (NavEdge e in graph.EdgesFrom(start.Index))
        {
            if (graph.Passable(e, cls, state) && Of(new CellId(e.To)) == k)
                return true;
        }

        return false;
    }

    /// <summary>How far past a changed box (<see cref="Near"/>) its window reaches: two of the built levels' 16-unit cells.</summary>
    public const float WindowMargin = 32f;

    /// <summary>
    /// The components under <paramref name="state"/> of a graph whose components under another
    /// state are <paramref name="open"/>, where the two states differ only inside
    /// <paramref name="changed"/> (boxes on the floor plane holding every cell whose block changed
    /// and both ends of every edge whose obstacle did): the same answer as
    /// <see cref="Of(NavGraph, ClearanceClass, NavObstacleState)"/>, made by flooding only a window
    /// round each box (<see cref="WindowMargin"/> past it; boxes whose windows meet are one window).
    /// In a window, every cell that links out of it must still be joined, inside it, to every other
    /// such cell of its component: then a way that crossed the window still does, round what
    /// changed, and only what the window holds can have split off (a cell no longer reached from
    /// its edge is a component of its own). Otherwise, or when a link out of a window changed, the
    /// whole graph is flooded. <paramref name="windows"/> says how many windows were flooded, -1 for
    /// the whole graph.
    /// </summary>
    public static NavComponents Near(NavComponents open, NavGraph graph, ClearanceClass cls, NavObstacleState state,
                                     IReadOnlyList<(float MinX, float MinY, float MaxX, float MaxY)> changed, out int windows)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(changed);
        windows = 0;
        var boxes = new List<(float MinX, float MinY, float MaxX, float MaxY)>(changed.Count);
        foreach (var b in changed)
            boxes.Add((b.MinX - WindowMargin, b.MinY - WindowMargin, b.MaxX + WindowMargin, b.MaxY + WindowMargin));
        Merge(boxes);

        int[] comp = (int[])open._comp.Clone();
        int next = 0;
        foreach (int k in comp)
            next = Math.Max(next, k + 1);

        // A cell is in the window being flooded while its stamp is the window's; its local component then (-1 dead, -2 not yet reached).
        int n = graph.CellCount;
        var stamp = new int[n];
        var local = new int[n];
        var cells = new List<int>();
        var inside = new List<int>();
        var stack = new Stack<int>();
        var edgeOf = new List<int>();   // local component -> the open component its links out of the window reach (-1 none, -3 two)
        var seen = new HashSet<int>();
        foreach (var w in boxes)
        {
            int mark = ++windows;
            graph.CellsNear(w.MinX, w.MinY, w.MaxX, w.MaxY, cells);
            inside.Clear();
            foreach (int c in cells)
            {
                AiVector m = graph.Cell(c).Centre;
                if (m.X <= w.MinX || m.X >= w.MaxX || m.Y <= w.MinY || m.Y >= w.MaxY)
                    continue;
                stamp[c] = mark;
                local[c] = Alive(graph, c, cls, state) ? -2 : -1;
                inside.Add(c);
            }

            edgeOf.Clear();
            foreach (int seed in inside)
            {
                if (local[seed] != -2)
                    continue;
                int at0 = edgeOf.Count;
                edgeOf.Add(-1);
                local[seed] = at0;
                stack.Push(seed);
                while (stack.Count > 0)
                {
                    int at = stack.Pop();
                    foreach (NavEdge e in graph.EdgesFrom(at))
                    {
                        if (!Step(graph, at, e.To, e, cls, state, open, stamp, mark, local, edgeOf, at0, stack))
                            return Whole(graph, cls, state, out windows);
                    }

                    foreach (NavEdge e in graph.EdgesTo(at))
                    {
                        if (!Step(graph, at, e.From, new NavEdge(e.To, e.From, e.Type, e.Cost, e.Obstacle), cls, state, open, stamp, mark, local, edgeOf, at0, stack))
                            return Whole(graph, cls, state, out windows);
                    }
                }
            }

            // Every open component the window's edge touches must be one local component.
            seen.Clear();
            foreach (int k in edgeOf)
            {
                if (k == -3 || (k >= 0 && !seen.Add(k)))
                    return Whole(graph, cls, state, out windows);
            }

            foreach (int c in inside)
                comp[c] = local[c] < 0 ? -1 : edgeOf[local[c]] >= 0 ? edgeOf[local[c]] : next + local[c];
            next += edgeOf.Count;
        }

        return new NavComponents(comp, LargestOf(comp, next));
    }

    /// <summary>
    /// One link of the window's flood from <paramref name="at"/> to <paramref name="to"/> by
    /// <paramref name="e"/> (taken from <paramref name="at"/>): into the window, a cell not yet
    /// reached joins the local component; out of it, the link must be as it was under the open
    /// state (false when it changed) and its far end's open component is the local component's.
    /// </summary>
    private static bool Step(NavGraph graph, int at, int to, in NavEdge e, ClearanceClass cls, NavObstacleState state, NavComponents open,
                             int[] stamp, int mark, int[] local, List<int> edgeOf, int component, Stack<int> stack)
    {
        bool linked = graph.Passable(e, cls, state);
        if (stamp[to] == mark)
        {
            if (linked && local[to] == -2)
            {
                local[to] = component;
                stack.Push(to);
            }

            return true;
        }

        int outside = open._comp[to];
        if (!linked)
            return outside < 0 || open._comp[at] != outside;   // a link out of the window the change cut (it was one in the open state)
        if (edgeOf[component] == -1)
            edgeOf[component] = outside;
        else if (edgeOf[component] != outside)
            edgeOf[component] = -3;
        return true;
    }

    private static NavComponents Whole(NavGraph graph, ClearanceClass cls, NavObstacleState state, out int windows)
    {
        windows = -1;
        return Of(graph, cls, state);
    }

    private static bool Alive(NavGraph graph, int cell, ClearanceClass cls, NavObstacleState state) =>
        graph.Cell(cell).Admits(cls) && !state.CellBlocked(cell);

    /// <summary>Joins boxes that overlap until none do.</summary>
    private static void Merge(List<(float MinX, float MinY, float MaxX, float MaxY)> boxes)
    {
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = 0; i < boxes.Count && !merged; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var (a, b) = (boxes[i], boxes[j]);
                    if (a.MinX > b.MaxX || b.MinX > a.MaxX || a.MinY > b.MaxY || b.MinY > a.MaxY)
                        continue;
                    boxes[i] = (MathF.Min(a.MinX, b.MinX), MathF.Min(a.MinY, b.MinY), MathF.Max(a.MaxX, b.MaxX), MathF.Max(a.MaxY, b.MaxY));
                    boxes.RemoveAt(j);
                    merged = true;
                    break;
                }
            }
        }
    }

    private static int LargestOf(int[] comp, int count)
    {
        var sizes = new int[Math.Max(count, 1)];
        foreach (int k in comp)
        {
            if (k >= 0)
                sizes[k]++;
        }

        int largest = -1, most = 0;
        for (int k = 0; k < sizes.Length; k++)
        {
            if (sizes[k] > most)
                (largest, most) = (k, sizes[k]);
        }

        return largest;
    }

    /// <summary>The components of <paramref name="cls"/>'s cells of <paramref name="graph"/> under <paramref name="state"/>: O(cells + edges).</summary>
    public static NavComponents Of(NavGraph graph, ClearanceClass cls, NavObstacleState state)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(state);
        int n = graph.CellCount;
        var comp = new int[n];
        Array.Fill(comp, -1);
        var stack = new Stack<int>();
        var sizes = new List<int>();
        int next = 0;
        for (int seed = 0; seed < n; seed++)
        {
            if (comp[seed] >= 0 || !graph.Cell(seed).Admits(cls) || state.CellBlocked(seed))
                continue;
            comp[seed] = next;
            stack.Push(seed);
            int size = 1;
            while (stack.Count > 0)
            {
                int at = stack.Pop();
                foreach (NavEdge e in graph.EdgesFrom(at))
                {
                    if (comp[e.To] < 0 && graph.Passable(e, cls, state))
                    {
                        comp[e.To] = next;
                        stack.Push(e.To);
                        size++;
                    }
                }

                foreach (NavEdge e in graph.EdgesTo(at))
                {
                    if (comp[e.From] < 0 && graph.Passable(new NavEdge(e.To, e.From, e.Type, e.Cost, e.Obstacle), cls, state))
                    {
                        comp[e.From] = next;
                        stack.Push(e.From);
                        size++;
                    }
                }
            }

            sizes.Add(size);
            next++;
        }

        int largest = -1, most = 0;
        for (int k = 0; k < sizes.Count; k++)
        {
            if (sizes[k] > most)
                (largest, most) = (k, sizes[k]);
        }

        return new NavComponents(comp, largest);
    }
}
