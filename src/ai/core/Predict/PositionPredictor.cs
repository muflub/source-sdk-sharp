namespace SourceSharp.Ai;

/// <summary>
/// Where an unseen opponent is now (plan_hl2mp_ai §5.3.1): a probability per
/// nav cell. At the loss of sight the mass sits on the last known cell;
/// each step it spreads along the graph's edges at the opponent's speed,
/// weighted by the route prior (where it has been seen to go) and, for the
/// first second, by its last velocity; hearing it collapses the mass round
/// the heard cell; seeing a cell empty zeroes it. Sparse: only cells with
/// mass are kept, so a step costs the frontier, not the map.
/// </summary>
public sealed class PositionPredictor
{
    /// <summary>Mass below this is dropped.</summary>
    public const float Floor = 1e-5f;

    /// <summary>How strongly a visit count pulls the mass (per count, saturating at 255).</summary>
    public const float PriorWeight = 1f;

    /// <summary>
    /// The most cells the mass is kept on: past it the least likely go (the
    /// main thread's budget, §9; a bot searching that widely is guessing).
    /// </summary>
    public const int MaxCells = 2048;

    /// <summary>How long after the loss of sight the mass keeps spreading; after it the belief holds still until heard or seen.</summary>
    public const float MaxSpreadSeconds = 8f;

    private float _spread;

    /// <summary>How much of the weight back the way the mass came is removed.</summary>
    public const float Backtrack = 0.9f;

    /// <summary>How much of a cell's mass stays put each hop (the opponent may stop).</summary>
    public const float Stay = 0.1f;

    private readonly NavGraph _graph;
    private readonly Dictionary<int, float> _mass = new();
    private readonly Dictionary<int, float> _next = new();

    // The mass-weighted direction each cell's mass arrived from: a player
    // keeps going the way it goes, so mass rarely turns back.
    private readonly Dictionary<int, AiVector> _dir = new();
    private readonly Dictionary<int, AiVector> _nextDir = new();
    private float _carry;

    public PositionPredictor(NavGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    public bool Active => _mass.Count > 0;

    public int CellsWithMass => _mass.Count;

    /// <summary>The probability on <paramref name="cell"/>.</summary>
    public float P(int cell) => _mass.GetValueOrDefault(cell);

    /// <summary>The mass on the cells <paramref name="cells"/> picks.</summary>
    public float MassOn(Func<int, bool> cells)
    {
        float m = 0f;
        foreach ((int c, float p) in _mass)
        {
            if (cells(c))
                m += p;
        }

        return m;
    }

    /// <summary>The most likely cell, or none.</summary>
    public CellId ArgMax
    {
        get
        {
            int best = -1;
            float top = -1f;
            foreach ((int c, float p) in _mass)
            {
                if (p > top || (p == top && c < best))
                    (best, top) = (c, p);
            }

            return best < 0 ? CellId.None : new CellId(best);
        }
    }

    /// <summary>The entropy of the mass in bits: 0 when certain.</summary>
    public float Entropy
    {
        get
        {
            float h = 0f;
            foreach (float p in _mass.Values)
            {
                if (p > 0f)
                    h -= p * MathF.Log2(p);
            }

            return h;
        }
    }

    public void Clear()
    {
        _mass.Clear();
        _dir.Clear();
        _carry = 0f;
        _spread = 0f;
    }

    /// <summary>Sight lost at <paramref name="cell"/>, moving at <paramref name="velocity"/> (zero: no idea which way): all the mass there.</summary>
    public void LostAt(CellId cell, AiVector velocity)
    {
        Clear();
        if (!cell.IsValid)
            return;
        _mass[cell.Index] = 1f;
        _dir[cell.Index] = new AiVector(velocity.X, velocity.Y, 0f).Normalised();
    }

    /// <summary>Heard at <paramref name="cell"/>: the mass collapses onto the cells within <paramref name="radius"/> of it.</summary>
    public void Heard(CellId cell, float radius)
    {
        if (!cell.IsValid)
            return;
        Clear();
        AiVector at = _graph.Cell(cell).Centre;
        var near = new List<int> { cell.Index };
        var seen = new HashSet<int> { cell.Index };
        for (int i = 0; i < near.Count; i++)
        {
            foreach (NavEdge e in _graph.EdgesFrom(near[i]))
            {
                if (seen.Add(e.To) && _graph.Cell(e.To).Centre.DistanceTo(at) <= radius)
                    near.Add(e.To);
            }
        }

        foreach (int c in near)
            _mass[c] = (c == cell.Index ? 2f : 1f) / (near.Count + 1);
    }

    /// <summary>The bot looked at <paramref name="cell"/> and nobody was there: it holds no mass.</summary>
    public void SeenEmpty(int cell)
    {
        if (_mass.Remove(cell))
            Normalise();
    }

    /// <summary>
    /// <paramref name="seconds"/> pass at <paramref name="speed"/>: the mass
    /// spreads one hop per cell width travelled, out of each cell along its
    /// edges by the route prior (<paramref name="routePrior"/>, visit counts
    /// by cell, or empty for a flat prior) and the fading momentum.
    /// </summary>
    public void Step(float seconds, float speed, ReadOnlySpan<byte> routePrior, ClearanceClass cls = ClearanceClass.Crouch)
    {
        if (_mass.Count == 0 || seconds <= 0f || _spread >= MaxSpreadSeconds)
            return;
        _spread += seconds;
        float cell = MathF.Max(1f, _graph.Cell(_mass.Keys.First()).MaxX - _graph.Cell(_mass.Keys.First()).MinX);
        _carry += speed * seconds / cell;
        int hops = (int)_carry;
        _carry -= hops;
        for (int h = 0; h < hops; h++)
            Hop(routePrior, cls);
    }

    private void Hop(ReadOnlySpan<byte> prior, ClearanceClass cls)
    {
        _next.Clear();
        _nextDir.Clear();
        Span<float> w = stackalloc float[64];
        foreach ((int c, float p) in _mass)
        {
            AiVector here = _graph.Cell(c).Centre;
            ReadOnlySpan<NavEdge> edges = _graph.EdgesFrom(c);
            int n = Math.Min(edges.Length, w.Length);
            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                NavEdge e = edges[i];
                if (!_graph.Cell(e.To).Admits(cls))
                {
                    w[i] = 0f;
                    continue;
                }

                float weight = 1f + (e.To < prior.Length ? PriorWeight * prior[e.To] : 0f);
                AiVector v = _dir.GetValueOrDefault(c);
                if (v.LengthSquared > 0f)
                {
                    // Forward favoured, back nearly ruled out.
                    AiVector d = _graph.Cell(e.To).Centre - here;
                    float along = new AiVector(d.X, d.Y, 0f).Normalised().Dot(v);
                    weight *= along >= 0f ? 1f + along : 1f - Backtrack * -along;
                }

                w[i] = weight;
                sum += weight;
            }

            if (sum <= 0f)
            {
                Add(c, p, default);
                continue;
            }

            Add(c, p * Stay, _dir.GetValueOrDefault(c));
            float moving = p * (1f - Stay);
            for (int i = 0; i < n; i++)
            {
                if (w[i] <= 0f)
                    continue;
                AiVector d = _graph.Cell(edges[i].To).Centre - here;
                Add(edges[i].To, moving * w[i] / sum, new AiVector(d.X, d.Y, 0f).Normalised());
            }
        }

        _mass.Clear();
        _dir.Clear();
        float keep = Floor;
        if (_next.Count > MaxCells)
        {
            // The MaxCells-th largest mass is the floor this hop.
            float[] masses = [.. _next.Values];
            Array.Sort(masses);
            keep = MathF.Max(keep, masses[masses.Length - MaxCells]);
        }

        foreach ((int c, float p) in _next)
        {
            if (p >= keep && _mass.Count < MaxCells)
            {
                _mass[c] = p;
                _dir[c] = _nextDir.GetValueOrDefault(c).Normalised();
            }
        }

        Normalise();
    }

    private void Add(int c, float p, AiVector from)
    {
        _next[c] = _next.GetValueOrDefault(c) + p;
        _nextDir[c] = _nextDir.GetValueOrDefault(c) + from * p;
    }

    private void Normalise()
    {
        float s = 0f;
        foreach (float p in _mass.Values)
            s += p;
        if (s <= 0f)
        {
            _mass.Clear();
            return;
        }

        foreach (int c in _mass.Keys.ToArray())
            _mass[c] /= s;
    }
}
