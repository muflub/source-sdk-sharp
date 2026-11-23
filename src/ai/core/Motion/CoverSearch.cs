namespace SourceSharp.Ai;

/// <summary>
/// §10 Cover's place out of sight, lifted from the HL2MP decider for every game's: the nearest cell
/// along the graph, within a reach of the bot, that the threat's cell does not see in the map's sight
/// table, by a breadth-first walk out from the bot's cell. Per bot: its marks and queue are its own
/// and kept between calls, so a search allocates nothing.
/// </summary>
public sealed class CoverSearch(int cellCount)
{
    /// <summary>The most cells one search queues.</summary>
    public const int MaxCells = 4096;

    private readonly int[] _seen = new int[Math.Max(0, cellCount)];
    private readonly int[] _queue = new int[MaxCells];
    private int _stamp;

    /// <summary>Places found, for the facts.</summary>
    public int Found { get; private set; }

    /// <summary>
    /// The nearest cell's centre within <paramref name="reach"/> of <paramref name="self"/> (by the
    /// walk's order) that the cell of <paramref name="threat"/> does not see. False when either has no
    /// cell, when the threat does not see the bot where it stands (it holds there), or when no such
    /// place is in reach.
    /// </summary>
    public bool Find(NavGraph graph, ICellSight sight, AiVector self, AiVector threat, float reach, out AiVector at)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sight);
        at = default;
        CellId from = graph.FindCell(self), seen = graph.FindCell(threat);
        if (!from.IsValid || !seen.IsValid || from.Index >= _seen.Length || !sight.Visible(seen.Index, from.Index))
            return false;

        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen);
            _stamp = 1;
        }

        int head = 0, tail = 0;
        _queue[tail++] = from.Index;
        _seen[from.Index] = _stamp;
        while (head < tail)
        {
            int c = _queue[head++];
            AiVector centre = graph.Cell(c).Centre;
            if (centre.DistanceTo(self) > reach)
                continue;
            if (!sight.Visible(seen.Index, c))
            {
                at = centre;
                Found++;
                return true;
            }

            foreach (NavEdge e in graph.EdgesFrom(c))
            {
                if (_seen[e.To] == _stamp || tail >= _queue.Length)
                    continue;
                _seen[e.To] = _stamp;
                _queue[tail++] = e.To;
            }
        }

        return false;
    }
}
