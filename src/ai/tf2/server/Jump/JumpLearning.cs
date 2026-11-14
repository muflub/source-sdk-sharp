namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// What a learn pass answered (lane G6b): the level's links grown by the learned ones and their plans, or no set when
/// the learned links are the ones published already; every shown jump's verdict; the thread CPU it took and the flights.
/// </summary>
public sealed record JumpLearnOutcome(JumpLinkSet? Set, IReadOnlyList<JumpPlan> Plans, ShownFitResult Fits, double CpuMs, int Flights)
{
    /// <summary>The learned links in the set to publish (or published already when <see cref="Set"/> is null).</summary>
    public int Learned => Fits.Links.Count;

    /// <summary>What the practice rows came to (lane G8): each row's verdict and the links taken, which join before the learned ones.</summary>
    public PracticeResult Practice { get; init; } = PracticeResult.None;

    /// <summary>The practice links in the set to publish (lane G8).</summary>
    public int Practiced => Practice.Links.Count;
}

/// <summary>
/// A level's learned blast links (plan_bots_teams_tf2_part_g G.6, lane G6b): at each new snapshot of the experience,
/// the jumps players showed fitted (<see cref="ShownJumpFit"/>) against the level's shape links, and the set those make
/// with them: the shape links first, index for index as the shape pass published them, then the learned ones. Made on
/// the game's thread with the simulators it may only make there; <see cref="Learn"/> on a worker, one at a time, never
/// in a decide. Nothing of it is cached on disk: the store changes every round, so the learned links are fitted again
/// at each level's load from the snapshot (each jump once a level).
///
/// <para>
/// The snapshot's practice rows (lane G8, <see cref="JumpPractice"/>) are read first: each recipe flown in the level's
/// simulator and, verified, a link that joins after the shape links and before the shown jumps' (a shown jump a practice
/// link already makes is <see cref="ShownFitVerdict.Practiced"/>, not searched). With no practice rows the pass is the one
/// of before, link for link.
/// </para>
/// </summary>
public sealed class JumpLearning
{
    private readonly NavGraph _graph;
    private readonly BlastJumpSim _sim;
    private readonly BlastJumpSim _openAir;
    private readonly JumpPilotLimits _limits;
    private readonly JumpSearchSettings _search;
    private readonly ShownJumpFitSettings _settings;
    private readonly NavLinkCosts _costs;
    private readonly Func<double>? _cpu;
    private ShownJumpFit? _fit;
    private JumpPractice? _practice;
    private IReadOnlyList<JumpLink> _published = [];

    /// <param name="sim">A simulator of the level's free space, this learning's own.</param>
    /// <param name="openAir">A simulator of <see cref="JumpEnvelope.OpenAir"/>, for the jump's reach.</param>
    /// <param name="cpu">The thread's CPU clock in microseconds, or null for none.</param>
    public JumpLearning(NavGraph graph, BlastJumpSim sim, BlastJumpSim openAir, JumpPilotLimits limits, JumpSearchSettings search, ShownJumpFitSettings settings,
                        NavLinkCosts costs, Func<double>? cpu)
    {
        _graph = graph;
        _sim = sim;
        _openAir = openAir;
        _limits = limits;
        _search = search;
        _settings = settings;
        _costs = costs;
        _cpu = cpu;
    }

    public NavGraph Graph => _graph;

    /// <summary>
    /// The pass over <paramref name="snapshot"/>: its shown jumps fitted against <paramref name="shape"/> (the level's own
    /// links, whose plans are <paramref name="shapePlans"/>), and the set they make. When the learned links are those of the
    /// last set it answered, it answers none (nothing to publish); with none learned, the set is <paramref name="shape"/>
    /// itself. <paramref name="cancel"/> stops it between searches, throwing.
    /// </summary>
    public JumpLearnOutcome Learn(MapExperience snapshot, JumpLinkSet shape, IReadOnlyList<JumpPlan> shapePlans, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(shapePlans);
        double start = _cpu?.Invoke() ?? 0d;
        if (_fit is null)
        {
            // One simulator and one walk for both readers: they run one after the other on this pass's thread.
            GraphJumpWalk walk = GraphJumpWalk.Of(_graph, _limits.RunSpeed);
            var search = new JumpSearch(new JumpFlier(_sim, _limits), walk, _search);
            _fit = new ShownJumpFit(_graph, search, () => JumpEnvelope.Measure(new JumpFlier(_openAir, _limits), _search), _settings);
            var practising = new JumpPracticeSettings();
            _practice = new JumpPractice(_graph, new JumpSearch(new JumpFlier(_sim, _limits), walk, practising.Verifying(_search)), walk, practising);
        }

        PracticeResult practice = _practice!.Check(snapshot, shape, cancel);
        ShownFitResult fits = _fit.Fit(snapshot, TFLinkExperience.KeysOf(shape), cancel, practice.Links.Count == 0 ? null : [.. practice.Links.Select(l => TFLinkExperience.KeyOf(l.Recipe))]);
        cancel.ThrowIfCancellationRequested();
        JumpLink[] learned = [.. practice.Links, .. fits.Links];
        if (learned.SequenceEqual(_published))
            return new JumpLearnOutcome(null, [], fits, Ms(start), practice.Flights + fits.Flights) { Practice = practice };

        JumpLinkSet set = shape;
        JumpPlan[] plans = [.. shapePlans];
        int flights = practice.Flights + fits.Flights;
        if (learned.Length > 0)
        {
            set = JumpLinkSet.Of(_graph, [.. shape.Jumps, .. learned], _costs);
            plans = new JumpPlan[set.Links.Count];
            var flier = new JumpFlier(_sim, _limits);
            for (int i = 0; i < plans.Length; i++)
            {
                if (i < shapePlans.Count)
                {
                    plans[i] = shapePlans[i];
                    continue;
                }

                JumpLink j = set.JumpOf(i);
                plans[i] = set.PlanOf(i, flier.Fly(j.Recipe, j.Recipe.Start(), _search.MaxCommands));
                flights++;
            }
        }

        _published = learned;
        return new JumpLearnOutcome(set, plans, fits, Ms(start), flights) { Practice = practice };
    }

    private double Ms(double start) => _cpu is null ? 0d : (_cpu() - start) / 1000d;
}
