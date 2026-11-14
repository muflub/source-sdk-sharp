using SourceSharp.Ai;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Brains;

/// <summary>
/// Which decider a robot's brain runs, per weapon family (plan_descent_logic
/// 3.6: one decider per robot family; HO6 adds the rusher, the rocket soldier
/// and the medic by <see cref="Register"/>). A brain's decider is made when its
/// body is lent, before its first view, and the session's recording names only
/// the brain's actor, seed and skills then: so the family is read from the
/// first view instead (<see cref="SelfState.ActiveWeapon"/>, which the recording
/// writes), and a replay picks the same decider. A family nothing is registered
/// for runs <see cref="HuntDecider"/>.
/// </summary>
public sealed class HordeDeciders
{
    private readonly HordeRoutes _routes;
    private readonly Dictionary<int, Func<BrainSetup, HordeRoutes, IDecider>> _families;

    public HordeDeciders(HordeRoutes routes, Dictionary<int, Func<BrainSetup, HordeRoutes, IDecider>> families)
    {
        _routes = routes;
        _families = families;
    }

    /// <summary>
    /// The level's deciders: <see cref="HuntDecider"/> for every family until one is registered,
    /// each walking by <paramref name="paths"/> once the level's graph is ready (none: the room
    /// route always).
    /// </summary>
    public static HordeDeciders For(HordeLevel level, IHordePaths? paths = null) =>
        new(new HordeRoutes(level, paths), new Dictionary<int, Func<BrainSetup, HordeRoutes, IDecider>>());

    public HordeLevel Level => _routes.Level;

    public HordeRoutes Routes => _routes;

    /// <summary>Makes <paramref name="family"/>'s robots decide with <paramref name="make"/> (a later package's family decider).</summary>
    public void Register(int family, Func<BrainSetup, HordeRoutes, IDecider> make)
    {
        ArgumentNullException.ThrowIfNull(make);
        _families[family] = make;
    }

    /// <summary>A brain's decider: it becomes its family's at the first decide (the session's <see cref="AiSession.Deciders"/>).</summary>
    public IDecider Make(BrainSetup setup) => new FamilyDecider(setup, this);

    /// <summary>The family's decider a brain's decider runs once its family is known (HO13: a boss's state is read through it); <paramref name="decider"/> itself when it is not the horde's wrapper.</summary>
    public static IDecider Unwrap(IDecider decider) => decider is FamilyDecider { Inner: { } inner } ? inner : decider;

    /// <summary>The decider for <paramref name="family"/>.</summary>
    public IDecider ForFamily(int family, BrainSetup setup) =>
        _families.TryGetValue(family, out var make) ? make(setup, _routes) : new HuntDecider(setup, _routes.NewRoute(setup.Bot.Slot));

    /// <summary>
    /// A brain's decider before its family is known: at its first decide it
    /// makes its family's and answers through it from then on. The tree a
    /// recording names (made before the first decide) is <see cref="HuntDecider"/>'s.
    /// </summary>
    private sealed class FamilyDecider(BrainSetup setup, HordeDeciders deciders) : IDecider, IHordeRouted
    {
        private readonly BtTrace _empty = new();
        private IDecider? _inner;

        public IDecider? Inner => _inner;

        public HordeRoute? Route => (_inner as IHordeRouted)?.Route;

        public BtTrace Trace => _inner?.Trace ?? _empty;

        public DoingRecord Doing => _inner?.Doing ?? default;

        public IReadOnlyList<string> NodeNames => _inner?.NodeNames ?? HuntDecider.Names;

        public IReadOnlyList<int> NodeBranches => _inner?.NodeBranches ?? HuntDecider.Branches;

        public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
        {
            ArgumentNullException.ThrowIfNull(view);
            _inner ??= deciders.ForFamily(view.Self.ActiveWeapon, setup);
            return _inner.Decide(tick, view, seq);
        }
    }
}
