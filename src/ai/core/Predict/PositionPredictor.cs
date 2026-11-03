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
    private readonly PredictorScratch _scratch;

    // The cells with mass, in the order they gained it this hop (the order
    // every sum runs in), their mass, and the mass-weighted direction each
    // cell's mass arrived from: a player keeps going the way it goes, so mass
    // rarely turns back.
    private int[] _cells = new int[MaxCells];
    private float[] _mass = new float[MaxCells];
    private AiVector[] _dir = new AiVector[MaxCells];
    private int _n;
    private float _carry;

    /// <summary>A predictor with scratch of its own (a fact's).</summary>
    public PositionPredictor(NavGraph graph) : this(graph, new PredictorScratch(graph?.CellCount ?? 0))
    {
    }

    /// <summary>
    /// A predictor over <paramref name="graph"/> whose hops accumulate in
    /// <paramref name="scratch"/>, which the predictors of one bot share: they
    /// step one after another on the bot's own thread, never at once.
    /// </summary>
    public PositionPredictor(NavGraph graph, PredictorScratch scratch)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(scratch);
        if (scratch.CellCount < graph.CellCount)
            throw new ArgumentException($"the scratch holds {scratch.CellCount} cells, the graph {graph.CellCount}", nameof(scratch));
        _graph = graph;
        _scratch = scratch;
    }

    public bool Active => _n > 0;

    public int CellsWithMass => _n;

    /// <summary>The cells holding mass, in their order.</summary>
    public ReadOnlySpan<int> MassCells => _cells.AsSpan(0, _n);

    /// <summary>Each of <see cref="MassCells"/>' mass.</summary>
    public ReadOnlySpan<float> Masses => _mass.AsSpan(0, _n);

    /// <summary>The probability on <paramref name="cell"/>.</summary>
    public float P(int cell)
    {
        int i = Array.IndexOf(_cells, cell, 0, _n);
        return i < 0 ? 0f : _mass[i];
    }

    /// <summary>The mass on the cells <paramref name="cells"/> picks.</summary>
    public float MassOn(Func<int, bool> cells)
    {
        float m = 0f;
        for (int i = 0; i < _n; i++)
        {
            if (cells(_cells[i]))
                m += _mass[i];
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
            for (int i = 0; i < _n; i++)
            {
                int c = _cells[i];
                float p = _mass[i];
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
            for (int i = 0; i < _n; i++)
            {
                float p = _mass[i];
                if (p > 0f)
                    h -= p * MathF.Log2(p);
            }

            return h;
        }
    }

    public void Clear()
    {
        _n = 0;
        _carry = 0f;
        _spread = 0f;
    }

    /// <summary>Sight lost at <paramref name="cell"/>, moving at <paramref name="velocity"/> (zero: no idea which way): all the mass there.</summary>
    public void LostAt(CellId cell, AiVector velocity)
    {
        Clear();
        if (!cell.IsValid)
            return;
        Put(cell.Index, 1f, new AiVector(velocity.X, velocity.Y, 0f).Normalised());
    }

    /// <summary>Heard at <paramref name="cell"/>: the mass collapses onto the cells within <paramref name="radius"/> of it.</summary>
    public void Heard(CellId cell, float radius)
    {
        if (!cell.IsValid)
            return;
        Clear();
        AiVector at = _graph.Cell(cell).Centre;
        PredictorScratch x = _scratch;
        int stamp = x.NextStamp();
        int count = 0;
        x.Touch(ref count, cell.Index, stamp);
        for (int i = 0; i < count; i++)
        {
            foreach (NavEdge e in _graph.EdgesFrom(x.Touched[i]))
            {
                if (x.Stamp[e.To] != stamp && _graph.Cell(e.To).Centre.DistanceTo(at) <= radius)
                    x.Touch(ref count, e.To, stamp);
                else
                    x.Stamp[e.To] = stamp;
            }
        }

        for (int i = 0; i < count; i++)
        {
            int c = x.Touched[i];
            Put(c, (c == cell.Index ? 2f : 1f) / (count + 1), default);
        }
    }

    /// <summary>The bot looked at <paramref name="cell"/> and nobody was there: it holds no mass.</summary>
    public void SeenEmpty(int cell)
    {
        int i = Array.IndexOf(_cells, cell, 0, _n);
        if (i < 0)
            return;
        Array.Copy(_cells, i + 1, _cells, i, _n - i - 1);
        Array.Copy(_mass, i + 1, _mass, i, _n - i - 1);
        Array.Copy(_dir, i + 1, _dir, i, _n - i - 1);
        _n--;
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
        if (_n == 0 || seconds <= 0f || _spread >= MaxSpreadSeconds)
            return;
        _spread += seconds;
        float cell = MathF.Max(1f, _graph.Cell(_cells[0]).MaxX - _graph.Cell(_cells[0]).MinX);
        _carry += speed * seconds / cell;
        int hops = (int)_carry;
        _carry -= hops;
        for (int h = 0; h < hops; h++)
            Hop(routePrior, cls);
    }

    private void Hop(ReadOnlySpan<byte> prior, ClearanceClass cls)
    {
        PredictorScratch x = _scratch;
        int stamp = x.NextStamp();
        int count = 0;
        Span<float> w = stackalloc float[64];
        Span<AiVector> unit = stackalloc AiVector[64];
        for (int k = 0; k < _n; k++)
        {
            int c = _cells[k];
            float p = _mass[k];
            AiVector v = _dir[k];
            bool momentum = v.LengthSquared > 0f;
            AiVector here = _graph.CellRef(c).Centre;
            ReadOnlySpan<NavEdge> edges = _graph.EdgesFrom(c);
            int n = Math.Min(edges.Length, w.Length);
            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                int to = edges[i].To;
                ref readonly NavCell next = ref _graph.CellRef(to);
                if (!next.Admits(cls))
                {
                    w[i] = 0f;
                    continue;
                }

                // The step's direction in the ground plane, once: the weight's and the arrival's.
                AiVector d = next.Centre - here;
                unit[i] = new AiVector(d.X, d.Y, 0f).Normalised();
                float weight = 1f + (to < prior.Length ? PriorWeight * prior[to] : 0f);
                if (momentum)
                {
                    // Forward favoured, back nearly ruled out.
                    float along = unit[i].Dot(v);
                    weight *= along >= 0f ? 1f + along : 1f - Backtrack * -along;
                }

                w[i] = weight;
                sum += weight;
            }

            if (sum <= 0f)
            {
                x.Add(ref count, c, p, default, stamp);
                continue;
            }

            x.Add(ref count, c, p * Stay, v, stamp);
            float moving = p * (1f - Stay);
            for (int i = 0; i < n; i++)
            {
                if (w[i] <= 0f)
                    continue;
                x.Add(ref count, edges[i].To, moving * w[i] / sum, unit[i], stamp);
            }
        }

        float keep = Floor;
        if (count > MaxCells)
        {
            // The MaxCells-th largest mass is the floor this hop.
            Span<float> masses = x.Order(count);
            for (int i = 0; i < count; i++)
                masses[i] = x.Mass[x.Touched[i]];
            keep = MathF.Max(keep, PredictorScratch.Select(masses, count - MaxCells));
        }

        _n = 0;
        for (int i = 0; i < count; i++)
        {
            int c = x.Touched[i];
            float p = x.Mass[c];
            if (p >= keep && _n < MaxCells)
                Put(c, p, x.Dir[c].Normalised());
        }

        Normalise();
    }

    private void Put(int c, float p, AiVector dir)
    {
        if (_n == _cells.Length)
        {
            Array.Resize(ref _cells, _n * 2);
            Array.Resize(ref _mass, _n * 2);
            Array.Resize(ref _dir, _n * 2);
        }

        _cells[_n] = c;
        _mass[_n] = p;
        _dir[_n] = dir;
        _n++;
    }

    private void Normalise()
    {
        float s = 0f;
        for (int i = 0; i < _n; i++)
            s += _mass[i];
        if (s <= 0f)
        {
            _n = 0;
            return;
        }

        for (int i = 0; i < _n; i++)
            _mass[i] /= s;
    }
}

/// <summary>
/// A bot's predictors' working space for one hop (dense by cell, so a hop
/// costs its frontier and no hashing): the mass and direction each touched
/// cell gathers, which cells were touched in which order, and a stamp per
/// cell so nothing is cleared between hops. One per bot: its predictors run
/// one after another on the thread its brain runs on, never two at once.
/// </summary>
public sealed class PredictorScratch
{
    private int _stamp;
    private float[] _order = new float[PositionPredictor.MaxCells * 4];

    public PredictorScratch(int cellCount)
    {
        CellCount = cellCount;
        Mass = new float[cellCount];
        Dir = new AiVector[cellCount];
        Stamp = new int[cellCount];
        Touched = new int[Math.Min(cellCount, PositionPredictor.MaxCells * 4) + 1];
    }

    public int CellCount { get; }

    internal float[] Mass { get; }

    internal AiVector[] Dir { get; }

    internal int[] Stamp { get; }

    internal int[] Touched { get; private set; }

    internal int NextStamp()
    {
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(Stamp);
            _stamp = 1;
        }

        return _stamp;
    }

    /// <summary>Marks <paramref name="cell"/> touched this stamp, in order, with nothing gathered yet.</summary>
    internal void Touch(ref int count, int cell, int stamp)
    {
        Stamp[cell] = stamp;
        Mass[cell] = 0f;
        Dir[cell] = default;
        if (count == Touched.Length)
        {
            int[] grown = Touched;
            Array.Resize(ref grown, count * 2);
            Touched = grown;
        }

        Touched[count++] = cell;
    }

    /// <summary>Gathers <paramref name="p"/> of mass arriving from <paramref name="from"/> on <paramref name="cell"/>.</summary>
    internal void Add(ref int count, int cell, float p, AiVector from, int stamp)
    {
        if (Stamp[cell] != stamp)
            Touch(ref count, cell, stamp);
        Mass[cell] += p;
        Dir[cell] += from * p;
    }

    /// <summary>Room for <paramref name="count"/> masses to order.</summary>
    internal Span<float> Order(int count)
    {
        if (_order.Length < count)
            _order = new float[Math.Max(count, _order.Length * 2)];
        return _order.AsSpan(0, count);
    }

    /// <summary>The <paramref name="k"/>-th smallest of <paramref name="v"/> (0-based): what sorting it ascending puts at <paramref name="k"/>. Reorders <paramref name="v"/>.</summary>
    internal static float Select(Span<float> v, int k)
    {
        int lo = 0, hi = v.Length - 1;
        while (lo < hi)
        {
            float pivot = v[lo + ((hi - lo) >> 1)];
            int i = lo, j = hi;
            while (i <= j)
            {
                while (v[i] < pivot)
                    i++;
                while (v[j] > pivot)
                    j--;
                if (i <= j)
                {
                    (v[i], v[j]) = (v[j], v[i]);
                    i++;
                    j--;
                }
            }

            if (k <= j)
                hi = j;
            else if (k >= i)
                lo = i;
            else
                return v[k];
        }

        return v[k];
    }
}
