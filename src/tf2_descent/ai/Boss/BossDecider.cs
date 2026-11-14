using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Families;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules.Bosses;

namespace TF2.Descent.Ai.Boss;

/// <summary>
/// A boss's brain (plan_descent_logic HO13; plan_tf2_rpg 5c: "bosses use the same runtime with
/// phase-scripted trees"): its fight is its weapon family's decider (Sir Nukesalot lobs as a
/// demoman, Major Crits fires as a soldier, Captain Punch rushes as a melee heavy), and over it
/// a small tree its script's phase drives, in this order:
///
/// <list type="number">
/// <item><b>Phase.</b> The phase is read from its own life in the view (<see cref="SelfState.Health"/>
/// of <see cref="SelfState.Armour"/>, <see cref="BossRules.PhaseAt"/>) and held once entered.</item>
/// <item><b>Summon</b> (<see cref="HordeAbilities.BossSummon"/>): on entering a phase that has
/// adds, with a hero in sight, it calls them, once a phase.</item>
/// <item><b>Telegraph</b> (<see cref="HordeAbilities.BossSpecial"/>): its phase's special, every
/// <c>specialSeconds</c> of the phase from when it first saw a hero, with a hero in sight (and,
/// for a slam, inside nine tenths of its radius). The windup the heroes see and hear is the
/// server's.</item>
/// <item><b>Arena.</b> It never leaves its arena (<see cref="BossArena"/>, the room holding its
/// home): a follow whose next corner lies outside goes as far as the arena's edge and holds there,
/// a strafe or retreat whose next step lies outside is a hold; a boss standing outside goes home.</item>
/// <item>Otherwise its family's intent, as that decider gave it.</item>
/// </list>
///
/// A boss whole again with no hero in sight after it has fought has been reset by the server
/// (every hero left its arena): it forgets its phase, its adds and its specials' clock.
///
/// Replay-safe: it reads the view's recorded fields and the level, and keeps only what it decided.
/// </summary>
public sealed class BossDecider : IDecider, IHordeRouted
{
    private readonly BrainSetup _setup;
    private readonly BossScript _script;
    private readonly BossRules _rules;
    private readonly IDecider _inner;
    private readonly HordeRoute _route;
    private readonly float _margin;
    private readonly BtTrace _trace = new();
    private BossArena? _arena;
    private DoingRecord _doing;
    private bool _own;
    private int _summoned = -1;
    private Tick _specialAt;
    private bool _engaged;

    public BossDecider(BrainSetup setup, HordeRoutes routes, BossScript script, BossRules rules, IDecider inner, float margin)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _setup = setup;
        _script = script;
        _rules = rules;
        _inner = inner;
        _margin = margin;
        _route = (inner as IHordeRouted)?.Route ?? routes.NewRoute(setup.Bot.Slot);
        Level = routes.Level;
    }

    private HordeLevel Level { get; }

    public HordeRoute? Route => _route;

    public BtTrace Trace => _own ? _trace : _inner.Trace;

    public DoingRecord Doing => _own ? _doing : _inner.Doing;

    public IReadOnlyList<string> NodeNames => FamilyDecider.Names;

    public IReadOnlyList<int> NodeBranches => FamilyDecider.Branches;

    /// <summary>The phase the last decide was in.</summary>
    public int Phase { get; private set; }

    /// <summary>The node the last decide ended in: its own, or its family's.</summary>
    public FamilyNode Last { get; private set; }

    /// <summary>The script it fights by.</summary>
    public BossScript Script => _script;

    /// <summary>Fights it has forgotten: each time it was whole again with no hero in sight after a fight.</summary>
    public int Resets { get; private set; }

    /// <summary>The tick its next special is due (0 before it has seen a hero).</summary>
    public Tick SpecialAt => _specialAt;

    public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
    {
        ArgumentNullException.ThrowIfNull(view);
        AiVector self = view.Self.Origin;
        _arena ??= BossArena.Of(Level, _setup.Home ?? self, _margin);
        var arena = _arena.Value;
        bool seen = NearestHero(view, self, out ActorFacts hero);
        if (_engaged && !seen && view.Self.Armour > 0 && view.Self.Health >= view.Self.Armour)
        {
            // Whole again with nobody in sight: the server reset the fight (every hero left its arena), so it is forgotten.
            _engaged = false;
            _summoned = -1;
            Phase = 0;
            Resets++;
        }

        Phase = BossRules.Phase(_script, view.Self.Health, view.Self.Armour, Phase);

        IntentCommand intent = _inner.Decide(tick, view, seq);
        _own = false;
        Last = (FamilyNode)intent.Reason.NodeId;
        if (seen && !_engaged)
        {
            _engaged = true;
            _specialAt = tick + SpecialTicks(Phase);
        }

        // Summon: once a phase, as the phase begins (a phase with no adds is passed over).
        if (Phase > _summoned)
        {
            if (BossRules.AddsOf(_script, Phase).Count == 0)
                _summoned = Phase;
            else if (seen)
            {
                _summoned = Phase;
                return Own(tick, seq, FamilyNode.Summon, Bounded(tick, view, intent.Move, self, arena), intent.Look,
                           new ActionIntent(ActionKind.Ability, FireStyle.Tap, HordeAbilities.BossSummon, Phase), hero, "summon");
            }
        }

        // Telegraph: the phase's special when it is due.
        if (seen && _rules.SpecialOf(_script, Phase) is { } special && tick >= _specialAt
            && (special.Kind != BossSpecial.Slam || hero.Origin.DistanceTo(self) <= special.Radius * 0.9))
        {
            _specialAt = tick + SpecialTicks(Phase);
            return Own(tick, seq, FamilyNode.Telegraph, Bounded(tick, view, intent.Move, self, arena), intent.Look,
                       new ActionIntent(ActionKind.Ability, FireStyle.Tap, HordeAbilities.BossSpecial, Phase), hero, special.Id);
        }

        // Arena: the family's move, unless it leaves the arena.
        var move = Bounded(tick, view, intent.Move, self, arena);
        if (move != intent.Move)
            return Own(tick, seq, FamilyNode.Arena, move, intent.Look, intent.Action, hero, "held in the arena");
        return intent;
    }

    /// <summary>Ticks between two of phase <paramref name="phase"/>'s specials.</summary>
    private int SpecialTicks(int phase)
    {
        double seconds = (uint)phase < (uint)_script.Phases.Count ? _script.Phases[phase].SpecialSeconds : 0;
        return Math.Max(1, (int)MathF.Ceiling((float)seconds / _setup.TickSeconds));
    }

    /// <summary>
    /// <paramref name="move"/> kept inside the arena: a boss standing outside goes home along its
    /// route; a follow whose next corner, or a strafe or retreat whose next step, lies outside holds.
    /// </summary>
    private MoveIntent Bounded(Tick tick, PerceivedView view, in MoveIntent move, AiVector self, BossArena arena)
    {
        if (!arena.Holds(self))
            return _route.Follow(tick, view.Self, self, _setup.Home ?? arena.Centre(self.Z), MoveSpeed.Run);
        if (move.Kind == MoveKind.Follow && move.CornerCount > 0 && !arena.Holds(move.Corner0))
        {
            // Towards the corner as far as the arena's edge, and no farther.
            AiVector edge = arena.Clamp(move.Corner0) with { Z = self.Z };
            return self.DistanceTo(edge) <= EdgeUnits ? MoveIntent.Hold : _route.Follow(tick, view.Self, self, edge, move.Speed);
        }

        bool leaves = move.Kind is MoveKind.Strafe or MoveKind.Retreat && !arena.Holds(self + move.Direction * StepUnits);
        return leaves ? MoveIntent.Hold : move;
    }

    /// <summary>How far ahead a strafe or a retreat is tested against the arena's bounds.</summary>
    public const float StepUnits = 96f;

    /// <summary>How near the arena's edge a boss going after a hero beyond it stops.</summary>
    public const float EdgeUnits = 16f;

    private IntentCommand Own(Tick tick, int seq, FamilyNode node, in MoveIntent move, in LookIntent look, in ActionIntent action, in ActorFacts hero,
                              string verdict)
    {
        _own = true;
        Last = node;
        _trace.Begin(tick);
        _trace.Add((short)node, BtStatus.Success, 0f);
        _trace.End(new DecisionReason((short)node, 1f, 0f, 0f));
        _doing = new DoingRecord(nameof(BossDecider), FamilyDecider.NameOf(node), true, hero.Origin, hero.Id, verdict, 0f, 0f, 0f, 0f, 0f, 0f,
                                 tick + IntentCommand.LifetimeTicks);
        return new IntentCommand(_setup.Bot, tick, seq, tick + IntentCommand.LifetimeTicks, move, look, action,
                                 new DecisionReason((short)node, 1f, 0f, 0f));
    }

    private static bool NearestHero(PerceivedView view, AiVector self, out ActorFacts hero)
    {
        hero = default;
        float best = float.MaxValue;
        foreach (ref readonly ActorFacts f in view.Sighted)
        {
            if (!HordeView.IsHero(f))
                continue;
            float d = self.DistanceTo(f.Origin);
            if (d < best)
                (hero, best) = (f, d);
        }

        return best < float.MaxValue;
    }
}
