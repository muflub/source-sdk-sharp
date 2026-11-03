namespace SourceSharp.Ai;

/// <summary>What a node answered this decide.</summary>
public enum BtStatus : byte
{
    Failure = 0,
    Success = 1,
    Running = 2,
}

/// <summary>
/// One decide's run of a tree (plan_hl2mp_ai §9 Phase 5 step 1): the tick,
/// the tick length, the brain's seeded random stream and the trace the run
/// writes. Made once per brain and reused: a run allocates nothing.
/// </summary>
public sealed class BtRun(AiRandom random, BtTrace trace, float tickSeconds)
{
    public Tick Now { get; private set; }

    public AiRandom Random { get; } = random;

    public BtTrace Trace { get; } = trace;

    public float TickSeconds { get; } = tickSeconds;

    /// <summary>The decide's serial: a node runs at most once per serial.</summary>
    public int Serial { get; private set; }

    internal void Begin(Tick now, int serial)
    {
        Now = now;
        Serial = serial;
    }
}

/// <summary>
/// A behaviour-tree node over a decision state <typeparamref name="T"/>. A
/// node runs at most once per decide: a second visit in the same decide
/// answers the first visit's status without running it again (§8.2: "a leaf
/// runs once per decide").
/// </summary>
public abstract class BtNode<T>(string name) where T : class
{
    private int _ranSerial = -1;
    private BtStatus _ranStatus;

    public string Name { get; } = name;

    /// <summary>The node's place in its tree, depth first from the root's 0; -1 before the tree numbers it.</summary>
    public short Id { get; internal set; } = -1;

    /// <summary>How many times the node's own work has run (not counting a repeated visit).</summary>
    public int Runs { get; private set; }

    /// <summary>The node's children, for the tree's numbering and the inspector.</summary>
    public virtual ReadOnlySpan<BtNode<T>> Children => [];

    /// <summary>Runs the node once this decide, or answers what it answered already.</summary>
    public BtStatus Run(T state, BtRun run)
    {
        if (_ranSerial == run.Serial)
            return _ranStatus;
        _ranSerial = run.Serial;
        Runs++;
        int entry = run.Trace.Add(Id, BtStatus.Running, float.NaN);
        BtStatus s = Execute(state, run);
        run.Trace.Set(entry, s, Value);
        _ranStatus = s;
        return s;
    }

    /// <summary>A value the trace keeps beside the status: a condition's 1 or 0, a utility, else NaN.</summary>
    protected float Value { get; set; } = float.NaN;

    protected abstract BtStatus Execute(T state, BtRun run);

    /// <summary>Forgets what the node remembers between decides (a cooldown, a once): a new life.</summary>
    public virtual void Reset()
    {
        foreach (BtNode<T> c in Children)
            c.Reset();
    }
}

/// <summary>Runs its children in order while they succeed; a failed or running child ends it with that status.</summary>
public sealed class BtSequence<T>(string name, params BtNode<T>[] children) : BtNode<T>(name) where T : class
{
    public override ReadOnlySpan<BtNode<T>> Children => children;

    protected override BtStatus Execute(T state, BtRun run)
    {
        foreach (BtNode<T> c in children)
        {
            BtStatus s = c.Run(state, run);
            if (s != BtStatus.Success)
                return s;
        }

        return BtStatus.Success;
    }
}

/// <summary>Runs its children in order until one succeeds or runs; fails when all fail.</summary>
public sealed class BtSelector<T>(string name, params BtNode<T>[] children) : BtNode<T>(name) where T : class
{
    public override ReadOnlySpan<BtNode<T>> Children => children;

    protected override BtStatus Execute(T state, BtRun run)
    {
        foreach (BtNode<T> c in children)
        {
            BtStatus s = c.Run(state, run);
            if (s != BtStatus.Failure)
                return s;
        }

        return BtStatus.Failure;
    }
}

/// <summary>A child with the utility a <see cref="BtUtilitySelector{T}"/> ranks it by.</summary>
public sealed class BtScored<T>(string name, Func<T, float> utility, BtNode<T> child, BtCommit commit = default) : BtNode<T>(name) where T : class
{
    private readonly BtNode<T>[] _child = [child];

    /// <summary>How the selector holds to this child once chosen (none by default).</summary>
    public BtCommit Commit { get; } = commit;

    public override ReadOnlySpan<BtNode<T>> Children => _child;

    /// <summary>The utility, recorded on the node for the trace.</summary>
    public float Score(T state)
    {
        float u = utility(state);
        Value = u;
        LastScore = u;
        return u;
    }

    /// <summary>The utility the last <see cref="Score"/> gave.</summary>
    public float LastScore { get; private set; }

    protected override BtStatus Execute(T state, BtRun run) => _child[0].Run(state, run);
}

/// <summary>
/// How a <see cref="BtUtilitySelector{T}"/> holds to a child once chosen
/// (plan_hl2mp_ai §5.4): for <see cref="MinSeconds"/> nothing replaces it, and
/// after that a rival must beat its score by more than <see cref="Margin"/>.
/// </summary>
public readonly record struct BtCommit(float MinSeconds, float Margin);

/// <summary>
/// Scores its children and runs the best first, falling to the next on a
/// failure. Equal utilities are ordered by the brain's seed (a permutation
/// drawn once from the run's random stream at the first decide), so a tie
/// goes the same way every decide for one seed (§8.2: "ties are seed-stable").
/// A child scored 0 or less is not run.
/// <para>The child last chosen is held by its <see cref="BtCommit"/>. What ends
/// that at once: <see cref="Interrupt"/> saying so, its own score falling to 0
/// or less (what it was for is gone), or its failing.</para>
/// </summary>
public sealed class BtUtilitySelector<T> : BtNode<T> where T : class
{
    private readonly BtScored<T>[] _children;
    private readonly float[] _scores;
    private readonly int[] _order;
    private readonly int[] _tie;
    private readonly float[] _ranks;
    private bool _tieDrawn;
    private int _held = -1;
    private Tick _heldSince;

    public BtUtilitySelector(string name, params BtScored<T>[] children) : base(name)
    {
        _children = children;
        _scores = new float[children.Length];
        _ranks = new float[children.Length];
        _order = new int[children.Length];
        _tie = new int[children.Length];
    }

    public override ReadOnlySpan<BtNode<T>> Children => _children;

    /// <summary>The child that ran last and did not fail, or null: the chosen branch.</summary>
    public BtScored<T>? Chosen { get; private set; }

    /// <summary>What breaks a commitment at once this decide, whatever its timer says. Null: nothing does.</summary>
    public Func<T, bool>? Interrupt { get; init; }

    /// <summary>A new life: nothing is held.</summary>
    public override void Reset()
    {
        _held = -1;
        base.Reset();
    }

    protected override BtStatus Execute(T state, BtRun run)
    {
        if (!_tieDrawn)
        {
            for (int i = 0; i < _tie.Length; i++)
                _tie[i] = i;
            for (int i = _tie.Length - 1; i > 0; i--)
            {
                int j = run.Random.NextInt(i + 1);
                (_tie[i], _tie[j]) = (_tie[j], _tie[i]);
            }

            _tieDrawn = true;
        }

        for (int i = 0; i < _children.Length; i++)
        {
            _scores[i] = _children[i].Score(state);
            _ranks[i] = _scores[i];
            _order[i] = i;
        }

        if (_held >= 0 && _scores[_held] > 0f && Interrupt?.Invoke(state) != true)
        {
            BtCommit commit = _children[_held].Commit;
            _ranks[_held] = (run.Now - _heldSince) * run.TickSeconds < commit.MinSeconds ? float.MaxValue : _scores[_held] + commit.Margin;
        }

        // Insertion sort, best first, ties by the seeded rank: no allocation.
        for (int i = 1; i < _order.Length; i++)
        {
            int k = _order[i];
            int j = i - 1;
            while (j >= 0 && Before(k, _order[j]))
            {
                _order[j + 1] = _order[j];
                j--;
            }

            _order[j + 1] = k;
        }

        Chosen = null;
        foreach (int i in _order)
        {
            if (!(_scores[i] > 0f))
                break;
            BtStatus s = _children[i].Run(state, run);
            if (s != BtStatus.Failure)
            {
                Chosen = _children[i];
                Value = _scores[i];
                if (_held != i)
                {
                    _held = i;
                    _heldSince = run.Now;
                }

                return s;
            }
        }

        _held = -1;
        return BtStatus.Failure;
    }

    private bool Before(int a, int b)
        => _ranks[a] > _ranks[b] || (_ranks[a] == _ranks[b] && _tie[a] < _tie[b]);
}

/// <summary>A test of the state: success when it holds.</summary>
public sealed class BtCondition<T>(string name, Func<T, bool> test) : BtNode<T>(name) where T : class
{
    protected override BtStatus Execute(T state, BtRun run)
    {
        bool holds = test(state);
        Value = holds ? 1f : 0f;
        return holds ? BtStatus.Success : BtStatus.Failure;
    }
}

/// <summary>Work on the state: what it answers is the node's status.</summary>
public sealed class BtAction<T>(string name, Func<T, BtRun, BtStatus> act) : BtNode<T>(name) where T : class
{
    protected override BtStatus Execute(T state, BtRun run) => act(state, run);
}

/// <summary>After its child succeeds, fails for <paramref name="seconds"/> without running it.</summary>
public sealed class BtCooldown<T>(string name, float seconds, BtNode<T> child) : BtNode<T>(name) where T : class
{
    private readonly BtNode<T>[] _child = [child];
    private Tick _until = new(int.MinValue);

    public override ReadOnlySpan<BtNode<T>> Children => _child;

    protected override BtStatus Execute(T state, BtRun run)
    {
        if (run.Now < _until)
            return BtStatus.Failure;
        BtStatus s = _child[0].Run(state, run);
        if (s == BtStatus.Success)
            _until = run.Now + Math.Max(1, (int)MathF.Ceiling(seconds / run.TickSeconds - 1e-3f));
        return s;
    }

    public override void Reset()
    {
        _until = new Tick(int.MinValue);
        base.Reset();
    }
}

/// <summary>Runs its child until it has succeeded once, then fails until <see cref="Reset"/>.</summary>
public sealed class BtOnce<T>(string name, BtNode<T> child) : BtNode<T>(name) where T : class
{
    private readonly BtNode<T>[] _child = [child];
    private bool _done;

    public override ReadOnlySpan<BtNode<T>> Children => _child;

    protected override BtStatus Execute(T state, BtRun run)
    {
        if (_done)
            return BtStatus.Failure;
        BtStatus s = _child[0].Run(state, run);
        _done = s == BtStatus.Success;
        return s;
    }

    public override void Reset()
    {
        _done = false;
        base.Reset();
    }
}

/// <summary>Fails its child once it has been running for longer than <paramref name="seconds"/> in a row.</summary>
public sealed class BtTimeout<T>(string name, float seconds, BtNode<T> child) : BtNode<T>(name) where T : class
{
    private readonly BtNode<T>[] _child = [child];
    private Tick _since = new(int.MinValue);
    private bool _running;

    public override ReadOnlySpan<BtNode<T>> Children => _child;

    protected override BtStatus Execute(T state, BtRun run)
    {
        if (_running && (run.Now - _since) * run.TickSeconds > seconds)
        {
            _running = false;
            return BtStatus.Failure;
        }

        BtStatus s = _child[0].Run(state, run);
        if (s == BtStatus.Running && !_running)
        {
            _running = true;
            _since = run.Now;
        }
        else if (s != BtStatus.Running)
        {
            _running = false;
        }

        return s;
    }

    public override void Reset()
    {
        _running = false;
        base.Reset();
    }
}

/// <summary>
/// A tree: its root, its nodes numbered depth first (the trace's ids), and
/// one decide.
/// </summary>
public sealed class BehaviorTree<T> where T : class
{
    private readonly BtNode<T>[] _nodes;
    private int _serial;

    public BehaviorTree(BtNode<T> root)
    {
        Root = root;
        var all = new List<BtNode<T>>();
        Number(root, all);
        _nodes = [.. all];
    }

    public BtNode<T> Root { get; }

    /// <summary>The nodes by id.</summary>
    public ReadOnlySpan<BtNode<T>> Nodes => _nodes;

    /// <summary>One decide: the root runs once; the trace records every node visited, in order.</summary>
    public BtStatus Decide(T state, BtRun run, Tick now)
    {
        run.Begin(now, ++_serial);
        run.Trace.Begin(now);
        BtStatus s = Root.Run(state, run);
        return s;
    }

    public void Reset() => Root.Reset();

    /// <summary>
    /// Each node's branch by id: the id of the root's child it lies under. The
    /// root's own is its id, and a node shared by two branches is the first's.
    /// </summary>
    public int[] Branches()
    {
        int[] of = new int[_nodes.Length];
        Array.Fill(of, Root.Id);
        ReadOnlySpan<BtNode<T>> branches = Root.Children;
        for (int i = branches.Length - 1; i >= 0; i--)
            Mark(branches[i], branches[i].Id, of);
        return of;
    }

    private static void Mark(BtNode<T> node, int branch, int[] of)
    {
        of[node.Id] = branch;
        foreach (BtNode<T> c in node.Children)
            Mark(c, branch, of);
    }

    private static void Number(BtNode<T> node, List<BtNode<T>> all)
    {
        if (node.Id >= 0)
        {
            // A node shared by two branches is numbered once; one of another tree is refused.
            if (node.Id < all.Count && ReferenceEquals(all[node.Id], node))
                return;
            throw new ArgumentException($"node '{node.Name}' is in another tree already", nameof(node));
        }

        node.Id = checked((short)all.Count);
        all.Add(node);
        foreach (BtNode<T> c in node.Children)
            Number(c, all);
    }
}
