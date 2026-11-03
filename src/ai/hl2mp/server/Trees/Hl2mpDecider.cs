//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Ai.Hl2mp.Trees;

/// <summary>
/// How a bot's style weighs what it does with no one in sight (plan_hl2mp_ai §5.5,
/// §10). Pure over its parameters, which read how far each skill sits above the
/// bot's own mean, so these say the same of a favourite at any difficulty.
/// </summary>
public static class Hl2mpStyle
{
    /// <summary>What a level of Cover and a low Aggression are worth beside Cunning's <c>ambushWeight</c>, whose top is 0.8.</summary>
    public const float CampCover = 0.8f, CampPatience = 0.8f;

    /// <summary>From here a bot holds a place on no one's habit.</summary>
    public const float CampFrom = 0.48f;

    /// <summary>The top of chaseSeconds' curve.</summary>
    public const float ChaseCurveTop = 8f;

    /// <summary>What Hunt is worth to anyone, and Aggression's whole pull on it at that top.</summary>
    public const float HuntBase = 0.4f, AggressionPull = 0.3f;

    /// <summary>What a place already held, and one only planned, are worth (the Ambush branch).</summary>
    public const float HoldingBase = 0.35f, HoldingCamp = 0.5f, PlannedBase = 0.1f, PlannedCamp = 0.85f;

    /// <summary>How much a bot wants to hold a place, 0..0.8: Cunning's waiting, Cover's or, at a low Aggression, patience's ("Camps Alot", "Waits Forever").</summary>
    public static float Camp(in DecisionParams p) => Math.Max(p.AmbushWeight, Sitter(in p));

    /// <summary>The same without Cunning, who waits to catch someone, and hunts them where it knows they go.</summary>
    public static float Sitter(in DecisionParams p) => Math.Max(CampCover * p.CoverWeight, CampPatience * Math.Max(0f, 1f - 2f * Chase(in p)));

    /// <summary>Hunt's utility before the scoreboard and any grudge.</summary>
    public static float Hunt(in DecisionParams p) => HuntBase + AggressionPull * Chase(in p);

    public static float Holding(float camp) => HoldingBase + HoldingCamp * camp;

    public static float Planned(float camp) => PlannedBase + PlannedCamp * camp;

    private static float Chase(in DecisionParams p) => Math.Clamp(p.ChaseSeconds / ChaseCurveTop, 0f, 1f);
}

/// <summary>
/// The HL2MP bot's decisions (plan_hl2mp_ai §9 Phase 5 step 3, §5.4): one
/// utility selector over the branches stock's HL2MP bot behaviours name
/// (hl2mp/bot/behavior/: retreat_to_cover, get_health, attack,
/// melee_attack, seek_and_destroy, move_to_vantage_point, get_ammo, as tree
/// shapes, not code) — Survive, Fight (push / hold / retreat on the ammo
/// estimate, the bot's own reloads), Hunt (Headhunter-aware), Search, Ambush,
/// Deny, Arm and Roam — reading only the bot's view, its memory, the map it
/// knows and its skills' parameters. Its output is an intent a client could
/// carry out (§3): a goal to walk to, a point or an actor to look at, a
/// button to hold or press.
/// </summary>
public sealed class Hl2mpDecider : IDecider
{
    /// <summary>HL2MP's run speed (hl2_normspeed), the push test's.</summary>
    public const float RunSpeed = 190f;

    /// <summary>The nav distance to an opponent is taken as the straight one times this (§12.4).</summary>
    public const float NavDetour = 1.25f;

    /// <summary>An unsighted opponent is not hunted past this, times Headhunter's path cap.</summary>
    public const float HuntReach = 2048f;

    /// <summary>
    /// Within this someone in plain sight comes before anything else a bot is
    /// about (user ruling 2026-11-02: "bots should also fire if close to the player
    /// instead of trying to do other things"). The one distance for every gun: none
    /// reaches less (the shotgun's effective range is this, at the lowest gate).
    /// </summary>
    public const float CloseRange = AimStats.CloseRange;

    /// <summary>A one-shot's weapon slot asks for a weapon by catalog kind: this plus the kind (the applier's convention).</summary>
    public const int SelectByKind = 1000;

    /// <summary>§10 Cover: how far along the graph a bot at <c>coverWeight</c> 1 goes for a place its opponent cannot see.</summary>
    public const float CoverReach = 512f;

    /// <summary>The most cells one cover search visits.</summary>
    public const int CoverCells = 4096;

    /// <summary>§10 Cunning: a flanking approach's waypoint lies this far to the side of the straight line, at most.</summary>
    public const float FlankReach = 512f;

    /// <summary>A flank's waypoint is done with inside this.</summary>
    public const float FlankArrive = 128f;

    private readonly BotMemory _memory;
    private readonly DecisionParams _p;
    private readonly MapKnowledge _map;
    private readonly NavGraph? _graph;
    private readonly float _tick;
    private readonly AiRandom _rng;
    private readonly BtRun _run;
    private readonly BehaviorTree<Hl2mpDecider> _tree;
    private readonly BtUtilitySelector<Hl2mpDecider> _root;
    private readonly string[] _names;
    private readonly ActorId _bot;
    private readonly AiRandom _routeRng;
    private readonly int[] _coverSeen;
    private readonly int[] _coverQueue = new int[CoverCells];
    private int _coverStamp;

    // This decide's inputs.
    private PerceivedView _view = null!;
    private SelfState _self;
    private Tick _now;
    private KnownActor? _target;
    private bool _targetSighted;
    private AiVector _targetPos;
    private AiVector _targetVel;
    private bool _targetCrouched;

    /// <summary>A crouched body's chest, over its feet.</summary>
    public const float ChestCrouched = 24f;
    private float _targetDist;
    private float _scoreboardTerm;
    private WeaponUse _use;
    private WeaponFacts _ownFacts;
    private bool _ownHasFacts;

    // This decide's output.
    private MoveIntent _move;
    private LookIntent _look;
    private ActionIntent _action;
    private string _verdict = "";
    private string _leaf = "";
    private float _ammoTerm;
    private AmmoWindow.Verdict _window;
    private bool _hasGoal;
    private AiVector _goal;

    // What outlives a decide.
    private int _oneShot;
    private int _strafeSign = 1;
    private bool _strafing;
    private Tick _strafeUntil;
    private AiVector _roamGoal;
    private Tick _roamUntil;
    private bool _roaming;
    private Tick _ambushUntil;
    private AiVector _ambushSpot, _ambushWatch;
    private Tick _lastSelect;

    // A place to hold, as last planned (§5.5): planned once a second, not once a decide.
    private readonly AiVector[] _watched = new AiVector[CampWatchedKept];
    private int _watchedNext;
    private Tick _campPlanAt, _habitPlanAt;
    private bool _campPlanned, _campPlannedOnce, _habitPlanned, _habitPlannedOnce;
    private AiVector _campSpot, _campWatch, _habitPlanSpot, _habitPlanWatch;
    private float _ambushWalkSeconds = AmbushWalkSeconds;
    private readonly ulong _campSeed;

    // A prefire (§5.4).
    private readonly AiRandom _prefireRng;
    private int _lineAsk, _askAnswered;
    private AiVector _lineAt, _lineFrom, _prefireSpot;
    private bool _lineClear, _prefiring, _prefireAiming;
    private bool _triedFallback;
    private Tick _prefireUntil, _prefireNext;
    private string _prefireState = "";

    // The weapons the bot found empty in its hands (the HUD shows them so), by
    // catalog kind, and when; forgotten when the weapon is picked up again or
    // after DryForgetSeconds (an ammo pickup the bot did not see as one).
    private int _dryKinds;
    private int _lastHeld;
    private Tick _dryAt;

    /// <summary>How long a weapon found empty is not asked for again (an ammo pickup may have filled it since).</summary>
    public const float DryForgetSeconds = 30f;
    private ActorId _approachOf;
    private bool _flanking;
    private AiVector _flank;

    // What a commitment remembers between decides (§5.4).
    private bool _interrupted, _wasSighted, _goalHeld, _walkingToGoal, _ambushHolding, _preAim, _habitKnown, _preAiming;
    private AiVector _habitSpot;
    private float _grudgeTerm;
    private int _momentSeq;
    private string _momentKind = "";
    private ActorId _momentOther;
    private Tick _lastSpotMoment, _revengeSeen;
    private Tick _damageSeen, _goalHeldSince, _progressTick;
    private AiVector _heldGoal, _progressAt;
    private KnownActor? _heldFor, _lastTarget;
    private string _heldLeaf = "";

    /// <summary>How long a leaf's goal holds against a new one (§5.4).</summary>
    public const float GoalHoldSeconds = 3f;

    /// <summary>A new goal this near the held one is the same place: it is taken with no change of mind.</summary>
    public const float GoalNearUnits = 32f;

    /// <summary>Inside this the held goal is reached, as a search's and a roam's are.</summary>
    public const float GoalArriveUnits = 96f;

    /// <summary>A bot with a goal moving less than this for <see cref="NoProgressSeconds"/> is getting nowhere (§5.4).</summary>
    public const float NoProgressUnits = 32f;

    public const float NoProgressSeconds = 1.5f;

    /// <summary>The priority the opponent the bot is already about has over a rival (§5.4).</summary>
    public const float TargetStick = 1.25f;

    /// <summary>How long after losing sight a fight goes on as it was, before it becomes a chase (§5.4).</summary>
    public const float SightGraceSeconds = 0.75f;

    /// <summary>How long an ambusher may take to get to its spot, and how long it holds it from then (§5.5).</summary>
    public const float AmbushWalkSeconds = 8f, AmbushHoldSeconds = 12f;

    /// <summary>A real camper holds longer: this many seconds more at the top of <see cref="Hl2mpStyle.Camp"/>'s scale of 0..1.</summary>
    public const float AmbushHoldPerCamp = 45f;

    /// <summary>A place to hold is looked for this near.</summary>
    public const float CampReach = 1536f;

    /// <summary>A camper stands this many times its weapon's preferred range from what it watches, within these limits (§5.5).</summary>
    public const float CampRangeScale = 1.25f, CampRangeMin = 192f, CampRangeMax = 512f;

    /// <summary>It stands no nearer what it watches than this: not on the item.</summary>
    public const float CampNearest = 96f;

    /// <summary>How much a spot on people's routes counts against it, at the busiest.</summary>
    public const float CampOutOfTheWay = 1f;

    /// <summary>A plan stands this long before the bot looks again.</summary>
    public const float CampPlanSeconds = 1f;

    /// <summary>The places it last watched, which it does not go straight back to.</summary>
    public const int CampWatchedKept = 3;

    /// <summary>How long a share of the places lasts, and how many shares there are (<see cref="MyShare"/>).</summary>
    public const float CampEpochSeconds = 30f;

    public const int CampShares = 3;

    /// <summary>A place someone was seen this near, this lately, is taken.</summary>
    public const float CampTakenUnits = 192f, CampTakenSeconds = 15f;

    /// <summary>How many of the best places it tries for a spot before giving up this plan.</summary>
    public const int CampTries = 3;

    /// <summary>A pickup's worth as a place to hold (§5.5): what a person goes for, the best guns by their rank.</summary>
    public const float CampHealthValue = 0.8f, CampChargerValue = 0.7f, CampArmourValue = 0.6f, CampAmmoValue = 0.25f, CampWeaponRankFull = 8f;

    /// <summary>How much a hurt bot's want of health, and a weakly armed bot's of a weapon, adds to the worth (health), or multiplies it (weapon).</summary>
    public const float CampHurtWants = 0.8f, CampWeakWants = 1.5f;

    /// <summary>Under this rank a bot's best gun is a weak one.</summary>
    public const int CampWeakRank = 5;

    /// <summary>A choke's worth, its score times this plus Cunning's share (0..1 by ambushWeight's top).</summary>
    public const float CampChokeBase = 0.3f, CampChokeCunning = 1f, AmbushWeightTop = 0.8f;

    /// <summary>The distance at which a place is worth half.</summary>
    public const float CampCloseHalf = 256f;

    /// <summary>How much the bot's own whim multiplies a place's worth, at most (by its seed and the time).</summary>
    public const float CampWhim = 0.6f;

    // ---------------------------------------------------------------- prefire (§5.4)

    /// <summary>
    /// How long after losing sight of someone, after hearing them, and on an
    /// approach to their usual corner, a bot believes it knows where they are well
    /// enough to fire there; and how sure it must still be.
    /// </summary>
    public const float PrefireSeenSeconds = 2.5f, PrefireHeardSeconds = 1.5f, PrefireConfidence = 0.5f, PrefireHabitConfidence = 0.25f;

    /// <summary>The place must be within this of the way the view points: a human turn, about 0.2 s of a flick.</summary>
    public const float PrefireTurnDeg = 60f;

    /// <summary>How long between one chance of a prefire and the next: a person sprays, then checks.</summary>
    public const float PrefireCheckSeconds = 2f;

    /// <summary>The share of a clip it keeps: not a nearly empty gun.</summary>
    public const float PrefireClipShare = 0.25f;

    /// <summary>A line's answer stands while the bot has moved less than this, and it fires at nothing nearer.</summary>
    public const float PrefireLineMoved = 48f, PrefireNearest = 96f;

    /// <summary>How far along the graph from what it believes it looks for an opening it can see.</summary>
    public const int PrefireOpeningCells = 512;

    /// <summary>The height a place is fired at: a chest's.</summary>
    public const float PrefireChest = 48f;

    /// <summary>Kills by someone that make the grudge's whole weight (§5.5).</summary>
    public const float GrudgeFull = 2f;

    /// <summary>A spot an opponent has been found camping this many times is a habit (§5.5).</summary>
    public const float HabitVisits = 2f;

    /// <summary>A habit's counts fade (§5.2), so two visits a while ago still read as two: this share of them will do.</summary>
    public const float HabitFade = 0.9f;

    /// <summary>Camps this near each other are one place.</summary>
    public const float HabitSpotUnits = 128f;

    /// <summary>The usual corner is looked at from this near.</summary>
    public const float PreAimReach = 1024f;

    /// <summary>An opponent found this near its usual corner was caught at it.</summary>
    public const float SpotNear = 256f;

    /// <summary>How long after one "caught you at your spot" before another.</summary>
    public const float MomentCooldownSeconds = 20f;

    /// <summary>An ambusher this near its spot has arrived: it stands.</summary>
    public const float AmbushStandUnits = 48f;

    /// <summary>It walks the last of the way to its spot, and runs to get there.</summary>
    public const float AmbushWalkInUnits = 256f;

    /// <summary>What broke the commitment this decide ("" nothing), for the facts and the inspector.</summary>
    public string LastInterrupt { get; private set; } = "";

    /// <summary>Approaches begun, and those that took a flank (§10 Cunning's observable), for the facts and the inspector.</summary>
    public int Approaches { get; private set; }

    public int Flanks { get; private set; }

    /// <summary>Approaches begun knowing the opponent's habit, those that took another way in, and those that pre-aimed its corner (§5.5).</summary>
    public int HabitApproaches { get; private set; }

    public int HabitFlanks { get; private set; }

    public int PreAims { get; private set; }

    /// <summary>Decides on which the bot went for a place its opponent cannot see (§10 Cover), for the facts.</summary>
    public int CoverMoves { get; private set; }

    public Hl2mpDecider(BrainSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _bot = setup.Bot;
        _memory = setup.Memory;
        _p = setup.Params;
        _map = setup.Memory.World.Map;
        _graph = setup.Memory.World.Graph;
        _tick = setup.TickSeconds;
        _rng = new AiRandom(setup.Seed ^ 0x5452_4545UL);
        _routeRng = new AiRandom(setup.Seed ^ 0x524F_5554UL);
        _prefireRng = new AiRandom(setup.Seed ^ 0x5052_4546UL);
        _crouchRng = new AiRandom(setup.Seed ^ 0x4455_434BUL);
        _streakRng = new AiRandom(setup.Seed ^ 0x484F_5421UL);
        _campSeed = setup.Seed ^ 0x4341_4D50UL;
        Array.Fill(_watched, new AiVector(float.MaxValue, float.MaxValue, float.MaxValue));
        _coverSeen = new int[_graph?.CellCount ?? 0];
        Trace = new BtTrace();
        _run = new BtRun(_rng, Trace, _tick);
        _root = Build();
        _tree = new BehaviorTree<Hl2mpDecider>(_root);
        _names = new string[_tree.Nodes.Length];
        for (int i = 0; i < _names.Length; i++)
            _names[i] = _tree.Nodes[i].Name;
    }

    public BtTrace Trace { get; }

    public DoingRecord Doing { get; private set; }

    public IReadOnlyList<string> NodeNames => _names;

    public IReadOnlyList<int> NodeBranches => _tree.Branches();

    /// <summary>The parameters the skills made (§10).</summary>
    public DecisionParams Params => _p;

    public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
    {
        _view = view;
        _self = view.Self;
        _now = tick;
        _move = MoveIntent.Hold;
        _look = LookIntent.AlongPath;
        _action = ActionIntent.None;
        _verdict = "";
        _leaf = "";
        _ammoTerm = 0f;
        _window = default;
        _hasGoal = false;
        Streaks();

        if (!_self.Alive)
        {
            _tree.Reset();
            _roaming = false;
            _ambushUntil = default;
            _approachOf = ActorId.None;
            _goalHeld = _walkingToGoal = _wasSighted = false;
            _heldFor = _lastTarget = null;
            EndPrefire();
            _crouching = false;
            Trace.Begin(tick);
            Trace.End(default);
            Doing = new DoingRecord("dead", "", false, default, ActorId.None, "", 0f, 0f, 0f, 0f, 0f, 0f, tick,
                                    MomentSeq: _momentSeq, MomentKind: _momentKind, MomentOther: _momentOther);
            return IntentCommand.Hold(_bot, tick, seq);
        }

        _ownHasFacts = _map.Weapons.TryGet(_self.ActiveWeapon, out _ownFacts);
        NoteDry();
        _use = Hl2mpWeaponRanges.For(_ownHasFacts ? _ownFacts.Name : "");
        ChooseTarget();
        if (_target is null || _targetSighted)
            _approachOf = ActorId.None;   // an approach ends in sight, or with nobody to approach
        if (!_targetSighted)
            _aimSighted = false;          // back in sight, the aim starts over

        _interrupted = NoteInterrupts();
        _tree.Decide(this, _run, tick);
        HoldGoal();
        PreAim();
        Prefire();
        Crouch();
        if (!_targetSighted && _hasGoal && _look.Kind == LookKind.Path)
            Glance();
        Overlays();

        string branch = _root.Chosen?.Name ?? "none";
        var reason = new DecisionReason(LeafId(), _root.Chosen is null ? 0f : Utility(_root.Chosen), _scoreboardTerm, _ammoTerm);
        Trace.End(in reason);
        Tick expires = tick + IntentCommand.LifetimeTicks;
        Doing = new DoingRecord(branch, _leaf, _hasGoal, _goal, _target?.Id ?? ActorId.None, _verdict, _window.PEmpty, _window.PReloading,
                                _window.SecondsLeft, _window.ArriveSeconds, _scoreboardTerm, _ammoTerm, expires,
                                _grudgeTerm, _preAiming, _habitKnown ? _habitSpot : default, _momentSeq, _momentKind, _momentOther,
                                _lineAsk, _lineAt, _prefireState, _prefireAiming ? _prefireSpot : default, StreakName,
                                _streak == 0 ? 0f : StreakNow.AimErrorDeg, _streak == 0 ? 0f : StreakNow.ReactionMs, _streak == 0 ? 0f : StreakNow.RecognizeSeconds);
        return new IntentCommand(_bot, tick, seq, expires, _move, _look, _action, reason);
    }

    private static float Utility(BtScored<Hl2mpDecider> n) => n.LastScore;

    private int LeafId()
    {
        ReadOnlySpan<BtTraceEntry> e = Trace.Count > 0 ? Trace.EntriesAt(0) : default;
        for (int i = e.Length - 1; i >= 0; i--)
        {
            if (e[i].Status != BtStatus.Failure && _tree.Nodes[e[i].NodeId] is BtAction<Hl2mpDecider>)
                return e[i].NodeId;
        }

        return -1;
    }

    // ------------------------------------------------------------------ the tree

    private BtUtilitySelector<Hl2mpDecider> Build()
    {
        BtNode<Hl2mpDecider> Act(string name, Func<Hl2mpDecider, bool> act)
            => new BtAction<Hl2mpDecider>(name, (d, _) =>
            {
                if (!act(d))
                    return BtStatus.Failure;
                d._leaf = name;
                return BtStatus.Success;
            });
        BtNode<Hl2mpDecider> If(string name, Func<Hl2mpDecider, bool> test) => new BtCondition<Hl2mpDecider>(name, test);

        var fight = new BtSelector<Hl2mpDecider>("fight",
            new BtSequence<Hl2mpDecider>("own reload", If("reloading in sight", d => d.SelfReloading && d._targetSighted), Act("back off reloading", d => d.BackOffReloading())),
            new BtSequence<Hl2mpDecider>("empty", If("clip empty", d => d.ClipEmpty), Act("reload empty", d => d.ReloadEmpty())),
            new BtSequence<Hl2mpDecider>("push", If("push test", d => d.PushTest()), Act("push", d => d.Push())),
            new BtSequence<Hl2mpDecider>("retreat", If("outgunned", d => d.Outgunned()), Act("retreat", d => d.Retreat())),
            new BtSequence<Hl2mpDecider>("hold", If("in sight", d => d._targetSighted || d.SightJustLost), Act("hold range", d => d.HoldRange())),
            Act("chase", d => d.Chase()));

        return new BtUtilitySelector<Hl2mpDecider>("root",
            new BtScored<Hl2mpDecider>("Survive", d => d.SurviveUtility(), Act("go heal", d => d.GoHeal()), new BtCommit(3f, 0.1f)),
            new BtScored<Hl2mpDecider>("Fight", d => d.FightUtility(), fight, new BtCommit(1.5f, 0.1f)),
            new BtScored<Hl2mpDecider>("Hunt", d => d.HuntUtility(), Act("hunt", d => d.Hunt()), new BtCommit(2.5f, 0.1f)),
            new BtScored<Hl2mpDecider>("Search", d => d.SearchUtility(), Act("search", d => d.Search()), new BtCommit(2f, 0.1f)),
            new BtScored<Hl2mpDecider>("Ambush", d => d.AmbushUtility(), Act("ambush", d => d.Ambush()), new BtCommit(4f, 0.1f)),
            new BtScored<Hl2mpDecider>("Deny", d => d.DenyUtility(), Act("deny", d => d.Deny()), new BtCommit(2f, 0.1f)),
            new BtScored<Hl2mpDecider>("Arm", d => d.ArmUtility(), Act("arm", d => d.Arm()), new BtCommit(3f, 0.1f)),
            new BtScored<Hl2mpDecider>("Roam", _ => 0.1f, Act("roam", d => d.Roam()), new BtCommit(1f, 0.05f)))
        {
            Interrupt = d => d._interrupted,
        };
    }

    // ------------------------------------------------------------------ commitment (§5.4)

    /// <summary>
    /// What ends a commitment at once (§5.4): someone seen close (the ruling
    /// "close, firing comes first"), someone newly seen at any range, damage
    /// taken since the last decide, an empty clip, a goal gone, and getting
    /// nowhere. Reads the bot's own view and memory, and notes what it has seen
    /// for the next decide, so it runs once a decide, before the tree.
    /// </summary>
    private bool NoteInterrupts()
    {
        NoteMoments();
        Tick damaged = _memory.Board.LastDamageTick;
        bool hit = damaged != _damageSeen;
        _damageSeen = damaged;
        bool gained = _targetSighted && !_wasSighted;
        _wasSighted = _targetSighted;
        bool close = _targetSighted && _targetDist <= CloseRange;
        bool stuck = NoProgress();
        bool gone = HeldGoalGone();
        LastInterrupt = close ? "close" : gained ? "seen" : hit ? "hurt" : ClipEmpty ? "empty" : gone ? "goal gone" : stuck ? "stuck" : "";
        return LastInterrupt.Length > 0;
    }

    /// <summary>
    /// The moments a player would recognise (§5.5), for the log, the inspector and
    /// chat: a grudge paid back, and someone found at the corner it pre-aimed. Only
    /// what the bot itself knows and did.
    /// </summary>
    private void NoteMoments()
    {
        (ActorId who, Tick when) = _memory.LastRevenge;
        if (who.IsValid && when != _revengeSeen)
        {
            _revengeSeen = when;
            Moment("revenge", who);
        }

        bool gained = _targetSighted && !_wasSighted;
        if (gained && _preAim && _target is not null && HabitSpot(_target, out AiVector spot) && _targetPos.DistanceTo(spot) < SpotNear
            && (_lastSpotMoment.Value == 0 || (_now - _lastSpotMoment) * _tick >= MomentCooldownSeconds))
        {
            _lastSpotMoment = _now;
            Moment("spot", _target.Id);
        }
    }

    private void Moment(string kind, ActorId who)
    {
        _momentSeq++;
        _momentKind = kind;
        _momentOther = who;
    }

    /// <summary>The place an opponent has been found camping most, once it is a habit.</summary>
    private bool HabitSpot(KnownActor a, out AiVector spot)
    {
        spot = default;
        if (_graph is null || !_memory.TryHabits(a.Id, out OpponentHabits? h) || h is null)
            return false;
        // A place is more than a cell: a person camping shuffles between a few of them.
        ReadOnlySpan<(CellId Cell, float Count)> camps = h.Camps;
        float best = HabitVisits * HabitFade;
        bool found = false;
        foreach ((CellId cell, float count) in camps)
        {
            if (!cell.IsValid || count <= 0f)
                continue;
            AiVector centre = _graph.Cell(cell).Centre;
            float here = 0f;
            foreach ((CellId other, float others) in camps)
            {
                if (other.IsValid && _graph.Cell(other).Centre.DistanceTo(centre) <= HabitSpotUnits)
                    here += others;
            }

            if (here > best)
            {
                best = here;
                spot = centre;
                found = true;
            }
        }

        return found;
    }

    /// <summary>Hunting someone with a habit, on an approach Cunning picked: the view goes to their usual corner as soon as it is near.</summary>
    private void PreAim()
    {
        _preAiming = false;
        if (!_preAim || !_habitKnown || _targetSighted || _leaf is not ("hunt" or "chase"))
            return;
        if (_self.Origin.DistanceTo(_habitSpot) > PreAimReach)
            return;
        _preAiming = true;
        _look = new LookIntent(LookKind.Point, _habitSpot + new AiVector(0f, 0f, 48f), _target?.Id ?? ActorId.None, AimPoint.Body, 0f, 0);
    }

    /// <summary>
    /// A burst at the place the bot believes someone is, before seeing them (§5.4,
    /// user request 2026-11-02: "Make bots prefire spots it thinks players are at").
    /// Only from what it knows: its belief, what it heard, where it last saw them,
    /// their habit. And only down a line it has looked along: the game answers that
    /// from the bot's eye to the place through the world and props alone
    /// (<see cref="AiEventKind.LineAnswer"/>), so it shoots at the opening, never at
    /// a wall, and the answer says nothing of who is behind it. Seeing anyone at all
    /// ends it, whatever the burst had left.
    /// </summary>
    private void Prefire()
    {
        _prefireState = "";
        if (_view.SightedCount > 0 || _target is null || _targetSighted || !PrefireLeaf || !PrefireHands())
        {
            EndPrefire();
            return;
        }

        if (_prefiring && _now >= _prefireUntil)
        {
            EndPrefire();
            _prefireNext = _now + (int)(PrefireCheckSeconds / _tick);
            return;
        }

        if (!_prefireAiming)
        {
            if (_now < _prefireNext || !PrefireBelief(out AiVector believed, out AiVector fallback))
                return;
            AiVector spot = Opening(believed) + new AiVector(0f, 0f, PrefireChest);
            if (!PrefireInReach(spot))
                return;
            // One chance, then it waits to check again.
            _prefireNext = _now + (int)(PrefireCheckSeconds / _tick);
            if (_prefireRng.NextFloat() >= _p.PrefireChance * _p.PrefireThrift)
                return;
            _prefireSpot = spot;
            _prefireFallback = fallback + new AiVector(0f, 0f, PrefireChest);
            _triedFallback = false;
            _lineClear = false;
            _prefireAiming = true;
        }

        if (!PrefireInReach(_prefireSpot))
        {
            EndPrefire();
            return;
        }

        // The view goes there at once, as a person's does; the hands follow when the line is known.
        _look = new LookIntent(LookKind.CornerCheck, _prefireSpot, _target.Id, AimPoint.Body, 0f, 0);
        _prefireState = "aiming";
        (int ask, AiVector point, bool clear) = _memory.LastLine;
        if (ask == _lineAsk && ask != _askAnswered && _lineAsk > 0)
        {
            _askAnswered = ask;
            _lineClear = clear && point.DistanceTo(_prefireSpot) < 1f && _self.Origin.DistanceTo(_lineFrom) < PrefireLineMoved;
            if (!_lineClear && !_prefiring)
            {
                if (_triedFallback || _prefireFallback.DistanceTo(_prefireSpot) < 1f || !PrefireInReach(_prefireFallback))
                {
                    EndPrefire();
                    return;
                }

                // Behind what it can see: the place it last saw them is the opening.
                _triedFallback = true;
                _prefireSpot = _prefireFallback;
            }
            else if (!_lineClear)
            {
                EndPrefire();
                return;
            }
        }

        // Each decide it looks again: it moves, and a door may shut.
        if (_lineAsk == _askAnswered || _lineAt.DistanceTo(_prefireSpot) >= 1f)
        {
            _lineAsk++;
            _lineAt = _prefireSpot;
            _lineFrom = _self.Origin;
        }

        if (!_lineClear)
            return;
        if (!_prefiring)
        {
            _prefiring = true;
            _prefireUntil = _now + Math.Max(1, (int)(_p.PrefireBurstSeconds / _tick));
            Prefires++;
        }

        _prefireState = "firing";
        _action = new ActionIntent(ActionKind.Fire, _use.Style, 0, 0);
    }

    private AiVector _prefireFallback;

    // ---------------------------------------------------------------- streaks (§5.5)

    /// <summary>The chance a kill starts one, two kills close together, and how close.</summary>
    public const float StreakKillChance = 0.25f, StreakDoubleChance = 0.6f, StreakDoubleSeconds = 8f;

    /// <summary>How often one starts from nothing, a person's good minute, and how often that is looked at.</summary>
    public const float StreakRandomPerMinute = 0.1f, StreakCheckSeconds = 10f;

    /// <summary>A hot streak's length, and a rest after any streak before the next.</summary>
    public const float HotMinSeconds = 8f, HotMaxSeconds = 20f, StreakRestSeconds = 45f;

    /// <summary>The chance two deaths close together start a cold spell, how close, and its length.</summary>
    public const float ColdChance = 0.5f, ColdDeathSeconds = 20f, ColdMinSeconds = 6f, ColdMaxSeconds = 12f;

    private readonly AiRandom _streakRng;
    private int _streak, _streakAskSeen;
    private Tick _streakUntil, _streakNext, _streakCheck, _killSeen, _deathSeen;

    /// <summary>What the bot's streak makes of it now: only read with one on.</summary>
    private StreakParams StreakNow => _streak > 0 ? _p.Hot : _p.Cold;

    private string StreakName => _streak > 0 ? "hot" : _streak < 0 ? "cold" : "";

    /// <summary>The aim's error and lead now: a streak's, or its own.</summary>
    private float AimErrorNow => _streak == 0 ? _p.AimErrorDeg : StreakNow.AimErrorDeg;

    private float LeadNow => _streak == 0 ? _p.LeadQuality : StreakNow.LeadQuality;

    /// <summary>
    /// A run of playing above or below itself (§5.5, ruling 2026-11-02: "some times bad bots
    /// become great for a very short stint"). Mostly earned, by a kill or better two close
    /// together, and now and then from nothing; a death ends a hot one, and two close together
    /// can start a cold one. Own stream, every draw every time, so one bot's luck moves nothing
    /// else. A bot a streak would not change (one at the cap) has none to show.
    /// </summary>
    private void Streaks()
    {
        if (!_memory.StreaksOn)
        {
            _streak = 0;
            return;
        }

        if (_streak != 0 && _now >= _streakUntil)
            EndStreak();

        (Tick lastDeath, Tick deathBefore) = _memory.OwnDeaths;
        if (lastDeath != _deathSeen)
        {
            _deathSeen = lastDeath;
            float u = _streakRng.NextFloat();
            if (_streak > 0)
                EndStreak();
            if (u < ColdChance && deathBefore.Value != 0 && (lastDeath - deathBefore) * _tick <= ColdDeathSeconds && Rested && _p.Cold.AimErrorDeg != _p.AimErrorDeg)
                BeginStreak(-1);
        }

        (Tick lastKill, Tick killBefore) = _memory.OwnKills;
        if (lastKill != _killSeen)
        {
            _killSeen = lastKill;
            float u = _streakRng.NextFloat();
            bool twice = killBefore.Value != 0 && (lastKill - killBefore) * _tick <= StreakDoubleSeconds;
            if (u < (twice ? StreakDoubleChance : StreakKillChance) && Rested && CanRunHot)
                BeginStreak(1);
        }

        // The first look a period in: no bot opens its game on a run.
        if (_streakCheck.Value == 0)
            _streakCheck = _now + (int)(StreakCheckSeconds / _tick);
        if (_now >= _streakCheck)
        {
            _streakCheck = _now + (int)(StreakCheckSeconds / _tick);
            float u = _streakRng.NextFloat();
            if (u < StreakRandomPerMinute * StreakCheckSeconds / 60f && Rested && CanRunHot && _self.Alive)
                BeginStreak(1);
        }

        (int asked, int code) = _memory.StreakAsked;
        if (asked != _streakAskSeen)
        {
            _streakAskSeen = asked;
            BeginStreak(code == AiEvent.StreakHot ? 1 : -1);
        }
    }

    private bool Rested => _streak == 0 && _now >= _streakNext;

    private bool CanRunHot => _p.Hot.AimErrorDeg != _p.AimErrorDeg;

    private void BeginStreak(int kind)
    {
        float least = kind > 0 ? HotMinSeconds : ColdMinSeconds, most = kind > 0 ? HotMaxSeconds : ColdMaxSeconds;
        _streak = kind;
        _streakUntil = _now + (int)((least + (most - least) * _streakRng.NextFloat()) / _tick);
        Moment(kind > 0 ? "hot" : "cold", ActorId.None);
    }

    private void EndStreak()
    {
        _streak = 0;
        _streakNext = _now + (int)(StreakRestSeconds / _tick);
    }

    // ---------------------------------------------------------------- crouching (§5.4)

    /// <summary>The shortest a crouch is held, and the shortest gap after one: nobody's knee does less, and IN_DUCK flicking on and off looks wrong.</summary>
    public const float CrouchMinHoldSeconds = 0.35f, CrouchMinGapSeconds = 0.6f;

    /// <summary>A quick dip's upper end, a stretch's, and how often a crouch is a dip: holds a person's length (the user's: a median of about 0.9 s).</summary>
    public const float CrouchDipSeconds = 0.6f, CrouchStretchSeconds = 2.5f, CrouchDipShare = 0.3f;

    /// <summary>Up or down at more than this, it is in the air: no crouch mid-jump.</summary>
    public const float CrouchAirSpeed = 8f;

    private readonly AiRandom _crouchRng;
    private bool _crouching;
    private Tick _crouchUntil, _crouchNext;

    /// <summary>Whether the bot's last decide crouched for the fight, for the facts.</summary>
    public bool Crouching => _crouching;

    /// <summary>
    /// A bot in a fight sometimes crouches while it fires (§5.4, ruling 2026-11-02: "bots should
    /// crouch as well when shooting to make it hard to hit"): at range, in the fight's holding
    /// leaf, for a stretch or a quick dip, for <c>crouchShare</c> of the time. Not close, where a
    /// sidestep is worth more; not chasing or retreating, since a crouch is slow; not in the air. The
    /// holds and gaps are by the clock, not the decide, so the key does not flicker. What ends
    /// one early is what makes it wrong, and a new one waits its gap.
    /// </summary>
    private void Crouch()
    {
        bool fit = _move.Kind is MoveKind.Hold or MoveKind.Strafe or MoveKind.Follow && _targetSighted && _leaf == "hold range"
            && _targetDist > CloseRange && MathF.Abs(_self.Velocity.Z) <= CrouchAirSpeed;
        if (!fit)
        {
            if (_crouching)
                _crouchNext = _now + (int)(CrouchMinGapSeconds / _tick);
            _crouching = false;
            _crouchFit = false;
            return;
        }

        // Every draw every time, so the stream does not depend on the weights.
        if (!_crouchFit)
        {
            // A fight does not open with a crouch every time: the first waits part of a gap.
            _crouchFit = true;
            Tick first = _now + (int)(_crouchRng.NextFloat() * CrouchGap(1f) / _tick);
            if (first > _crouchNext)
                _crouchNext = first;
        }

        if (_crouching && _now >= _crouchUntil)
            _crouching = false;
        else if (!_crouching && _now >= _crouchNext)
        {
            float kind = _crouchRng.NextFloat(), length = _crouchRng.NextFloat(), jitter = _crouchRng.NextFloat();
            float hold = kind < CrouchDipShare
                ? CrouchMinHoldSeconds + (CrouchDipSeconds - CrouchMinHoldSeconds) * length
                : CrouchDipSeconds + (CrouchStretchSeconds - CrouchDipSeconds) * length;
            _crouching = _p.CrouchShare > 0f;
            _crouchUntil = _now + (int)(hold / _tick);
            _crouchNext = _crouchUntil + (int)(MathF.Max(CrouchMinGapSeconds, CrouchGap(hold) * (0.5f + jitter)) / _tick);
        }

        if (!_crouching)
            return;
        if (_move.Kind == MoveKind.Strafe)
        {
            _move = _move with { CrouchOn = true };
            return;
        }

        // Closing a long range waits: the crouch is the stop, and the walk goes on after it.
        _hasGoal = false;
        _move = MoveIntent.Hold with { Kind = MoveKind.Crouch, CrouchOn = true };
    }

    /// <summary>The gap that makes a hold this long <c>crouchShare</c> of the time.</summary>
    private float CrouchGap(float hold)
    {
        float share = MathF.Max(_p.CrouchShare, 0.002f);
        return hold * (1f - share) / share;
    }

    private bool _crouchFit;

    /// <summary>Bursts begun, for the facts.</summary>
    public int Prefires { get; private set; }

    private void EndPrefire()
    {
        _prefiring = _prefireAiming = _lineClear = false;
    }

    /// <summary>The fight-like leaves a prefire belongs to: hunting, chasing, holding a place, and a fight just out of sight.</summary>
    private bool PrefireLeaf => _leaf is "hunt" or "chase" or "ambush" or "hold range";

    /// <summary>A gun of a clip worth spraying and with enough in it: no crossbow, RPG or grenade, no melee, and not the last rounds.</summary>
    private bool PrefireHands()
    {
        if (!_ownHasFacts || !_ownFacts.HasClip || _use.Melee || SelfReloading)
            return false;
        return _use.Family is "pistol" or "smg" or "rifle" or "shotgun" && _self.Clip >= PrefireClipShare * _ownFacts.ClipSize;
    }

    /// <summary>
    /// Whether the bot believes well enough where its target is: just lost sight
    /// of, just heard, or at the corner its habit says, on an approach Cunning chose
    /// to pre-aim. <paramref name="at"/> is where it believes, <paramref name="last"/> the last place it had any evidence of.
    /// </summary>
    private bool PrefireBelief(out AiVector at, out AiVector last)
    {
        KnownActor a = _target!;
        at = last = default;
        float seen = a.EverSeen ? (_now - a.LastSeenTick) * _tick : float.MaxValue;
        float heard = a.EverHeard ? (_now - a.LastHeardTick) * _tick : float.MaxValue;
        if (a.Confidence >= PrefireConfidence && seen <= PrefireSeenSeconds)
        {
            at = Where(a);
            last = a.LastSeenPos;
        }
        else if (a.Confidence >= PrefireConfidence && heard <= PrefireHeardSeconds)
        {
            at = a.LastHeardPos;
            last = a.LastHeardPos;
        }
        else if (_preAiming && a.Confidence >= PrefireHabitConfidence)
        {
            at = last = _habitSpot;
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>In weapon range, not at its own feet, and within a human turn of where it looks.</summary>
    private bool PrefireInReach(AiVector spot)
    {
        AiVector eye = _self.Origin + new AiVector(0f, 0f, 64f);
        float distance = eye.DistanceTo(spot);
        if (distance < PrefireNearest || distance > _use.EffectiveRange * _p.FireRangeGate)
            return false;
        AiVector to = (spot - eye).Normalised();
        float cos = VisionMath.Forward(_self.Eye).Dot(to);
        return _prefiring || cos >= MathF.Cos(PrefireTurnDeg * (MathF.PI / 180f));
    }

    /// <summary>
    /// The nearest place to <paramref name="believed"/> along the graph that the
    /// map's sight table says the bot can see: what someone gone round a corner is
    /// behind. With no table, the place itself; the line's answer has the last word.
    /// </summary>
    private AiVector Opening(AiVector believed)
    {
        ICellSight? sight = _memory.World.Sight;
        if (_graph is null || sight is null)
            return believed;
        CellId from = _graph.FindCell(_self.Origin + new AiVector(0f, 0f, 1f)), start = _graph.FindCell(believed + new AiVector(0f, 0f, 1f));
        if (!from.IsValid || !start.IsValid)
            return believed;
        if (++_coverStamp == int.MaxValue)
        {
            Array.Clear(_coverSeen);
            _coverStamp = 1;
        }

        int head = 0, tail = 0;
        _coverQueue[tail++] = start.Index;
        _coverSeen[start.Index] = _coverStamp;
        while (head < tail && head < PrefireOpeningCells)
        {
            int c = _coverQueue[head++];
            if (sight.Visible(from.Index, c))
                return _graph.Cell(c).Centre;
            foreach (NavEdge e in _graph.EdgesFrom(c))
            {
                if (_coverSeen[e.To] == _coverStamp || tail >= _coverQueue.Length)
                    continue;
                _coverSeen[e.To] = _coverStamp;
                _coverQueue[tail++] = e.To;
            }
        }

        return believed;
    }

    /// <summary>
    /// A bot with a goal it has not reached that has moved less than
    /// <see cref="NoProgressUnits"/> for <see cref="NoProgressSeconds"/>; said
    /// once, then the time starts over. It is what the bot knows of itself: the
    /// driver's own detector, which routes round, is later and lives on the main
    /// thread.
    /// </summary>
    private bool NoProgress()
    {
        if (!_walkingToGoal || _self.Origin.DistanceTo(_progressAt) >= NoProgressUnits)
        {
            _progressAt = _self.Origin;
            _progressTick = _now;
            return false;
        }

        if ((_now - _progressTick) * _tick < NoProgressSeconds)
            return false;
        _progressAt = _self.Origin;
        _progressTick = _now;
        return true;
    }

    /// <summary>The goal held is gone: the pickup there is known taken, or the opponent it was about is dead.</summary>
    private bool HeldGoalGone()
    {
        if (!_goalHeld)
            return false;
        if (_heldFor is { Dead: true })
            return true;
        ReadOnlySpan<ItemBelief> beliefs = _memory.Board.Items;
        for (int i = 0; i < _map.Items.Length && i < beliefs.Length; i++)
        {
            if (beliefs[i].Present == false && _map.Items[i].Position.DistanceTo(_heldGoal) < 1f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// A leaf's goal holds for <see cref="GoalHoldSeconds"/> (§5.4): a belief's
    /// arg-max, the nearest pickup and the best investigation all move as the
    /// bot does, and a bot that turned for each of them walked away and back.
    /// A new goal is taken at once if it is near the held one, if the leaf
    /// changed, if something interrupted, or once the held one is reached. A
    /// goal on an opponent in sight is not held: it is where they are.
    /// </summary>
    private void HoldGoal()
    {
        bool leafChanged = _leaf != _heldLeaf;
        _heldLeaf = _leaf;
        if (!_hasGoal || (_targetSighted && _goal == _targetPos))
        {
            _goalHeld = false;
            _heldFor = null;
            _walkingToGoal = _hasGoal;
            return;
        }

        bool keep = _goalHeld && !leafChanged && !_interrupted
                    && (_now - _goalHeldSince) * _tick < GoalHoldSeconds
                    && _self.Origin.DistanceTo(_heldGoal) >= GoalArriveUnits
                    && _goal.DistanceTo(_heldGoal) >= GoalNearUnits;
        if (keep)
        {
            _goal = _heldGoal;
            _move = _move with { Corner0 = _heldGoal };
        }
        else
        {
            if (!_goalHeld || leafChanged || _interrupted || _goal.DistanceTo(_heldGoal) >= GoalNearUnits)
                _goalHeldSince = _now;
            _goalHeld = true;
            _heldGoal = _goal;
            _heldFor = _target;
        }

        _walkingToGoal = _self.Origin.DistanceTo(_goal) >= GoalArriveUnits;
    }

    // ------------------------------------------------------------------ targets

    /// <summary>
    /// The opponent the bot is about (§5.4): sighted ones first, then the
    /// remembered; priority by nearness (discounted by Headhunter's
    /// easy-target weight) plus Headhunter's rank bonus from the scoreboard;
    /// an unsighted one past the hunt reach (times the path cap) is no target.
    /// </summary>
    private void ChooseTarget()
    {
        _target = null;
        _targetSighted = false;
        _scoreboardTerm = 0f;
        _grudgeTerm = 0f;
        float best = 0f, closest = CloseRange;
        int rows = 0;
        bool close = false;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (a.Known && a.Rank > rows)
                rows = a.Rank;
        }

        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead)
                continue;
            bool sighted = _view.TryActor(a.Id, out ActorFacts f);
            AiVector at = sighted ? f.Origin : Where(a);
            float dist = _self.Origin.DistanceTo(at);
            if (!sighted)
            {
                if (a.Confidence < 0.05f || dist > HuntReach * _p.PathCapMultiplier)
                    continue;
            }

            float rankTerm = _p.RankBonus * RankBonus(a.Rank, rows);
            float grudgeTerm = _p.GrudgeWeight * Math.Min(1f, a.GrudgeAt(_now, _tick) / GrudgeFull);
            float proximity = 1f / (1f + dist / 768f);
            float priority = (sighted ? 1f : 0.5f * a.Confidence) * (proximity * _p.EasyTargetDiscount + rankTerm + grudgeTerm);
            if (ReferenceEquals(a, _lastTarget))
                priority *= TargetStick;
            // Someone close in plain sight outranks everyone else, the nearest first.
            bool closer = sighted && dist <= closest;
            if (closer)
            {
                closest = dist;
                close = true;
            }

            if (closer || (!close && priority > best))
            {
                best = priority;
                _target = a;
                _targetSighted = sighted;
                _targetPos = at;
                _targetVel = sighted ? f.Velocity : a.LastSeenVel;
                _targetCrouched = sighted && (f.Activity & ActorActivity.Crouched) != 0;
                _targetDist = dist;
                _scoreboardTerm = rankTerm;
                _grudgeTerm = grudgeTerm;
            }
        }

        _lastTarget = _target;
    }

    /// <summary>§5.4's rankBonus: rank 1 is 1, falling linearly to 0 at the bottom of the rows; no rank, nothing.</summary>
    public static float RankBonus(int rank, int rows)
    {
        if (rank <= 0 || rows <= 1)
            return rank == 1 ? 1f : 0f;
        return Math.Clamp((rows - rank) / (float)(rows - 1), 0f, 1f);
    }

    /// <summary>Where a remembered opponent is believed: the belief's arg-max, else where it was last seen or heard.</summary>
    private AiVector Where(KnownActor a)
    {
        if (a.Where is { Active: true } w && _graph is not null && w.ArgMax.IsValid)
            return _graph.Cell(w.ArgMax).Centre;
        return a.LastHeardTick > a.LastSeenTick ? a.LastHeardPos : a.LastSeenPos;
    }

    /// <summary>
    /// The opponent went out of sight a moment ago (§5.4): a corner flickers
    /// sight on and off, and a bot that stopped its fight for a chase each time
    /// flipped between the two leaves (70-85 % of all leaf flips, 9fa61c831).
    /// </summary>
    private bool SightJustLost => _target is not null && !_targetSighted && _target.EverSeen && (_now - _target.LastSeenTick) * _tick < SightGraceSeconds;

    private bool SelfReloading => (_self.Flags & SelfFlags.Reloading) != 0;

    private bool ClipEmpty => _ownHasFacts && _ownFacts.HasClip && _self.Clip == 0 && _self.Reserve > 0;

    private float HealthShare => Math.Clamp(_self.Health / 100f, 0f, 1f);

    // ------------------------------------------------------------------ utilities

    private float SurviveUtility()
    {
        bool threatened = _targetSighted || (_now - _memory.Board.LastDamageTick) * _tick < 3f;
        if (HealthShare < Math.Max(_p.RetreatHealthPct, _p.GiveUpHealth) && threatened)
            return 0.95f;
        if (_self.Health < 40 && !_targetSighted && NearestItem(ItemKind.Health, out _, out _))
            return 0.6f;
        return 0f;
    }

    private float FightUtility()
    {
        if (_target is null)
            return 0f;
        if (_targetSighted)
            return 0.8f;
        float since = (_now - _target.LastSeenTick) * _tick;
        return _target.EverSeen && since < _p.ChaseSeconds ? 0.55f : 0f;
    }

    private float HuntUtility()
    {
        if (_target is null || _targetSighted)
            return 0f;
        return Hl2mpStyle.Hunt(in _p) + 0.3f * (_scoreboardTerm + _grudgeTerm);
    }

    private float SearchUtility()
        => _memory.Board.TryBestInvestigation(_now, _tick, out Investigation i) && (_now - i.Tick) * _tick < _p.SearchSeconds ? 0.4f : 0f;

    private float AmbushUtility()
    {
        if (_targetSighted)
            return 0f;
        // Cunning waits where an opponent is known to be; Cunning, Cover or a low Aggression holds a place people come (§5.5).
        float camp = Hl2mpStyle.Camp(in _p);
        if (camp <= 0f)
            return 0f;
        if (_now < _ambushUntil)
            return Hl2mpStyle.Holding(camp);
        // Knowing someone's corner, Cunning goes at it another way (§5.5); a bot of Cover or patience is no less a camper for knowing it.
        float sitter = Hl2mpStyle.Sitter(in _p);
        if (_p.AmbushWeight > 0f && PlanAmbush())
            return Math.Max(0.2f + 0.4f * _p.AmbushWeight, sitter >= Hl2mpStyle.CampFrom ? Hl2mpStyle.Planned(sitter) : 0f);
        return camp >= Hl2mpStyle.CampFrom && PlanCamp() ? Hl2mpStyle.Planned(camp) : 0f;
    }

    private float DenyUtility() => !_targetSighted && DenyItem(out _) ? 0.3f : 0f;

    private float ArmUtility()
    {
        if (_targetSighted)
            return 0f;
        bool armed = BestHeld(out _) >= 3;
        if (!armed && NearestWeaponToTake(out _))
            return 0.65f;
        bool dry = _ownHasFacts && _ownFacts.HasClip && _self.Clip + _self.Reserve < _ownFacts.ClipSize;
        return dry && NearestItem(ItemKind.Ammo, out _, out _) ? 0.4f : 0f;
    }

    // ------------------------------------------------------------------ leaves

    private bool GoHeal()
    {
        if (NearestItem(ItemKind.Health, out AiVector at, out _) || NearestItem(ItemKind.Charger, out at, out _))
            GoTo(at, MoveSpeed.Run, MoveKind.Retreat);
        else if (_target is not null)
            GoTo(Away(_targetPos, 512f), MoveSpeed.Run, MoveKind.Retreat);
        else
            return false;
        _verdict = "retreat";
        LookAtTargetAndFire();
        return true;
    }

    private bool BackOffReloading()
    {
        _verdict = "retreat (own reload)";
        if (CoverSpot(out AiVector cover))
            GoTo(cover, MoveSpeed.Run, MoveKind.Retreat);
        else
            Strafe(-0.8f);
        // Rounds left in a reload a shot would break (a shotgun's): close, it fires;
        // a reload a shot cannot break just ignores the button.
        LookAtTarget(fire: _targetDist <= CloseRange && _self.Clip > 0);
        return true;
    }

    private bool ReloadEmpty()
    {
        _verdict = "reload";
        Strafe(-0.3f);
        LookAtTarget(fire: false);
        _action = new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, ++_oneShot);
        return true;
    }

    /// <summary>§5.4's push test on the target's ammo estimate: P(empty) + P(reloading) ≥ trust and the bot arrives in the window.</summary>
    private bool PushTest()
    {
        if (_target is null || !_target.Ammo.Known || !_map.Weapons.TryGet(_target.Ammo.Weapon, out WeaponFacts w))
            return false;
        float style = _memory.TryHabits(_target.Id, out OpponentHabits? h) && h is not null ? h.ReloadStyle : float.NaN;
        _window = AmmoWindow.Judge(_target.Ammo, w, _now, _tick, style, _targetDist * NavDetour, RunSpeed, _p.PushTrust, _p.PushMarginSeconds);
        _ammoTerm = _window.PEmpty + _window.PReloading;
        return _window.Push;
    }

    private bool Push()
    {
        _verdict = "push";
        GoTo(_targetPos, MoveSpeed.Run, MoveKind.Follow);
        LookAtTargetAndFire();
        return true;
    }

    /// <summary>
    /// §5.4's Retreat: the opponent probably has a full clip, the bot is low
    /// or worse armed, and the opponent is predicted to approach.
    /// </summary>
    private bool Outgunned()
    {
        if (_target is null || !_targetSighted)
            return false;
        bool full = _target.Ammo.Known ? _target.Ammo.PEmpty < 0.15f && !_target.Ammo.ReloadInProgress : true;
        bool low = HealthShare < 0.5f || Hl2mpWeaponRanges.For(WeaponName(_target.WeaponSeen)).Rank > _use.Rank + 2;
        if (!full || !low)
            return false;
        float retreat = _memory.TryHabits(_target.Id, out OpponentHabits? h) && h is not null ? h.RetreatTendency : 0.5f;
        float aggression = h?.Aggression ?? 0.5f;
        var x = new IntentInputs(retreat, aggression, 0f, _target.HealthEstimate, _targetDist, _target.Ammo.PEmpty, _target.Ammo.ReloadInProgress, false);
        return IntentClassifier.Top(in x).Intent == OpponentIntent.Approach;
    }

    private bool Retreat()
    {
        _verdict = "retreat";
        if (NearestItem(ItemKind.Health, out AiVector heal, out _))
            GoTo(heal, MoveSpeed.Run, MoveKind.Retreat);
        else if (CoverSpot(out AiVector cover))
            GoTo(cover, MoveSpeed.Run, MoveKind.Retreat);
        else
            GoTo(Away(_targetPos, 512f), MoveSpeed.Run, MoveKind.Retreat);
        LookAtTargetAndFire();
        return true;
    }

    /// <summary>§5.4's Hold: keep the weapon's preferred range (Aggression scales it), strafing by Agility, firing in range.</summary>
    private bool HoldRange()
    {
        _verdict = "hold";
        float preferred = _use.PreferredRange * _p.PreferredRangeScale;
        if (_targetDist > preferred * 1.4f)
            GoTo(_targetPos, MoveSpeed.Run, MoveKind.Follow);
        else if (_targetDist < preferred * 0.6f)
            Strafe(-0.7f);
        else
            Strafe(0f);
        LookAtTargetAndFire();
        // Just out of sight the view stays on where they were, as a chase's does.
        if (!_targetSighted && _target is not null)
            _look = new LookIntent(LookKind.Point, ChaseLook(_target.LastSeenPos), _target.Id, AimPoint.Body, 0f, 0);
        return true;
    }

    private bool Chase()
    {
        if (_target is null)
            return false;
        _verdict = "chase";
        Approach(_target.LastSeenPos);
        _look = new LookIntent(LookKind.Point, ChaseLook(_target.LastSeenPos), _target.Id, AimPoint.Body, 0f, 0);
        return true;
    }

    /// <summary>Near the place it chases to, a bot looks on past it at head height (<see cref="ChaseLook"/>).</summary>
    public const float ChaseLookNear = 128f;

    /// <summary>
    /// Where a chasing bot looks: at chest height of the place it last saw the
    /// target, until it is close; then level, past the place. Looking at that
    /// chest height from a pace away is a look at the floor (arriving, pitch
    /// 49° and more on flat ground, §12.3).
    /// </summary>
    private AiVector ChaseLook(AiVector place)
    {
        var to = new AiVector(place.X - _self.Origin.X, place.Y - _self.Origin.Y, 0f);
        float flat = to.Length;
        if (flat >= ChaseLookNear)
            return place + new AiVector(0f, 0f, 48f);
        AiVector ahead = flat > 8f ? to * (1f / flat) : VisionMath.Forward(_self.Eye);
        var level = new AiVector(ahead.X, ahead.Y, 0f).Normalised();
        return _self.Origin + new AiVector(0f, 0f, 64f) + level * ChaseLookNear;
    }

    private bool Hunt()
    {
        if (_target is null)
            return false;
        Approach(_targetPos);
        return true;
    }

    /// <summary>
    /// §10 Cunning's route alternatives: an approach to an unsighted opponent
    /// is begun once per opponent, and with chance <c>routeAltWeight</c> (its
    /// own seeded stream) it goes by a flank: a waypoint to one side of the
    /// straight line, half the distance out and at most <see cref="FlankReach"/>
    /// aside, on the graph, then the opponent. Otherwise the shortest way.
    /// </summary>
    private void Approach(AiVector at)
    {
        if (_target is null)
            return;
        if (_approachOf != _target.Id)
        {
            _approachOf = _target.Id;
            Approaches++;
            // Every draw every time, so the stream does not depend on the weights.
            float u = _routeRng.NextFloat(), side = _routeRng.NextFloat() < 0.5f ? -1f : 1f, aim = _routeRng.NextFloat();
            // A known habit doubles the chance of another way in, and Cunning decides the look at the corner (§5.5).
            _habitKnown = HabitSpot(_target, out _habitSpot);
            _preAim = _habitKnown && aim < _p.AmbushWeight * 1.25f;
            if (_habitKnown)
                HabitApproaches++;
            if (_preAim)
                PreAims++;
            float flankChance = _habitKnown ? Math.Min(1f, _p.RouteAltWeight * 2f) : _p.RouteAltWeight;
            _flanking = u < flankChance && Flank(at, side, out _flank);
            if (_flanking && _habitKnown)
                HabitFlanks++;
            if (_flanking)
                Flanks++;
        }

        if (_flanking && _self.Origin.DistanceTo(_flank) < FlankArrive)
            _flanking = false;
        GoTo(_flanking ? _flank : at, MoveSpeed.Run, MoveKind.Follow);
    }

    private bool Flank(AiVector at, float side, out AiVector waypoint)
    {
        waypoint = default;
        AiVector d = at - _self.Origin;
        float len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (_graph is null || len < 2f * FlankArrive)
            return false;
        float aside = Math.Min(0.5f * len, FlankReach) * side;
        AiVector mid = _self.Origin + d * 0.5f + new AiVector(-d.Y / len * aside, d.X / len * aside, 0f);
        CellId cell = _graph.FindCell(mid);
        if (!cell.IsValid)
            return false;
        waypoint = _graph.Cell(cell).Centre;
        return true;
    }

    /// <summary>
    /// §10 Cover: the nearest place within <c>coverWeight</c> × <see cref="CoverReach"/>
    /// along the graph that the opponent's cell does not see in the map's sight
    /// table. False with no table, no opponent within the table's range, a bot
    /// already unseen there (it holds), or no such place in reach.
    /// </summary>
    private bool CoverSpot(out AiVector at)
    {
        at = default;
        ICellSight? sight = _memory.World.Sight;
        float reach = CoverReach * _p.CoverWeight;
        if (_graph is null || sight is null || reach <= 0f || _target is null || _targetDist > sight.Range)
            return false;
        CellId from = _graph.FindCell(_self.Origin), threat = _graph.FindCell(_targetPos);
        if (!from.IsValid || !threat.IsValid || !sight.Visible(threat.Index, from.Index))
            return false;

        if (++_coverStamp == int.MaxValue)
        {
            Array.Clear(_coverSeen);
            _coverStamp = 1;
        }

        int head = 0, tail = 0;
        _coverQueue[tail++] = from.Index;
        _coverSeen[from.Index] = _coverStamp;
        while (head < tail)
        {
            int c = _coverQueue[head++];
            AiVector centre = _graph.Cell(c).Centre;
            if (centre.DistanceTo(_self.Origin) > reach)
                continue;
            if (!sight.Visible(threat.Index, c))
            {
                at = centre;
                CoverMoves++;
                return true;
            }

            foreach (NavEdge e in _graph.EdgesFrom(c))
            {
                if (_coverSeen[e.To] == _coverStamp || tail >= _coverQueue.Length)
                    continue;
                _coverSeen[e.To] = _coverStamp;
                _coverQueue[tail++] = e.To;
            }
        }

        return false;
    }

    private bool Search()
    {
        if (!_memory.Board.TryBestInvestigation(_now, _tick, out Investigation i))
            return false;
        // Near it, or getting nowhere towards it: a person gives up on a noise they cannot get to.
        if (_self.Origin.DistanceTo(i.Position) < 96f || LastInterrupt == "stuck")
            _memory.Board.Resolve(i.Position, 128f, i.Source);
        GoTo(i.Position, MoveSpeed.Run, MoveKind.Follow);
        return true;
    }

    private bool PlanAmbush()
    {
        if (_graph is null)
            return false;
        if (_habitPlannedOnce && (_now - _habitPlanAt) * _tick < CampPlanSeconds)
        {
            if (_habitPlanned)
            {
                _ambushSpot = _habitPlanSpot;
                _ambushWatch = _habitPlanWatch;
            }

            return _habitPlanned;
        }

        _habitPlanAt = _now;
        _habitPlannedOnce = true;
        Blackboard board = _memory.Board;
        float best = 0f;
        AiVector watch = default;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || !_memory.TryHabits(a.Id, out OpponentHabits? h) || h is null)
                continue;
            foreach ((CellId cell, float count) in h.Camps)
            {
                if (!cell.IsValid || count <= best)
                    continue;
                best = count;
                watch = _graph.Cell(cell).Centre;
            }
        }

        _habitPlanned = best > 0f && CampSpot(watch, out _habitPlanSpot);
        _habitPlanWatch = watch;
        if (_habitPlanned)
        {
            _ambushSpot = _habitPlanSpot;
            _ambushWatch = watch;
        }

        return _habitPlanned;
    }

    /// <summary>
    /// A place to hold with no one's habit to go on (§5.5, user request
    /// 2026-11-02: "camping should prefer item pickups like health good weapons or
    /// choke points"): the best of the map's pickups and chokes it knows, by what
    /// the place is worth, what the bot needs, how near it is and a whim of its own,
    /// that it has a spot to watch from. It looks again once a second.
    /// </summary>
    private bool PlanCamp()
    {
        if (_graph is null)
            return false;
        if (!_campPlannedOnce || (_now - _campPlanAt) * _tick >= CampPlanSeconds)
        {
            _campPlanAt = _now;
            _campPlannedOnce = true;
            _campPlanned = false;
            float below = float.PositiveInfinity;
            for (int attempt = 0; attempt < CampTries && !_campPlanned; attempt++)
            {
                if (!BestCampPlace(below, out AiVector watch, out below))
                    break;
                if (CampSpot(watch, out AiVector spot))
                {
                    _campPlanned = true;
                    _campSpot = spot;
                    _campWatch = watch;
                }
            }
        }

        if (_campPlanned)
        {
            _ambushSpot = _campSpot;
            _ambushWatch = _campWatch;
        }

        return _campPlanned;
    }

    /// <summary>The place worth most under <paramref name="below"/>, and its worth; none is false.</summary>
    private bool BestCampPlace(float below, out AiVector watch, out float worth)
    {
        watch = default;
        worth = 0f;
        ChokeMap? chokes = _memory.Chokes;
        int items = _map.Items.Length;
        int candidates = items + (chokes?.Top.Length ?? 0);
        bool share = candidates > CampShares;
        int epoch = _now.Value / Math.Max(1, (int)(CampEpochSeconds / _tick));
        CellId here = _graph!.FindCell(_self.Origin + new AiVector(0f, 0f, 1f));
        int part = here.IsValid ? _graph.ComponentOf(here.Index) : -1;
        int bestHeld = BestHeld(out _);
        for (int c = 0; c < candidates; c++)
        {
            if (share && !MyShare(c, epoch))
                continue;
            AiVector at;
            float value;
            if (c < items)
            {
                ItemSpawn item = _map.Items[c];
                at = item.Position;
                value = ItemWorth(item, bestHeld);
            }
            else
            {
                ChokePoint choke = chokes!.Value(c - items);
                at = choke.Position;
                value = choke.Score * (CampChokeBase + CampChokeCunning * Math.Clamp(_p.AmbushWeight / AmbushWeightTop, 0f, 1f));
            }

            float distance = _self.Origin.DistanceTo(at);
            if (value <= 0f || distance >= CampReach || Watched(at) || TakenBySomeone(at))
                continue;
            CellId place = _graph.FindCell(at + new AiVector(0f, 0f, 1f));
            if (!place.IsValid || (part >= 0 && _graph.ComponentOf(place.Index) != part))
                continue;
            float score = value / (1f + distance / CampCloseHalf) * (1f + CampWhim * Whim(c, epoch));
            if (score > worth && score < below)
            {
                worth = score;
                watch = at;
            }
        }

        return worth > 0f;
    }

    /// <summary>
    /// Whose turn a place is: each place falls to one slot of <see cref="CampShares"/>
    /// at a time, and the shares turn over every <see cref="CampEpochSeconds"/>. No
    /// bot knows what another means to do (they are opponents): a rule both follow is
    /// what keeps two that cannot see each other off one spot.
    /// </summary>
    private bool MyShare(int candidate, int epoch) => (candidate + epoch) % CampShares == _bot.Slot % CampShares;

    /// <summary>What a pickup is worth to hold, to this bot now: hurt it wants health, weakly armed a gun.</summary>
    private float ItemWorth(in ItemSpawn item, int bestHeld)
    {
        float hurt = 1f - HealthShare;
        switch (item.Kind)
        {
            case ItemKind.Health:
                return CampHealthValue + CampHurtWants * hurt;
            case ItemKind.Charger:
                return CampChargerValue + CampHurtWants * hurt;
            case ItemKind.Armour:
                return CampArmourValue;
            case ItemKind.Ammo:
                return CampAmmoValue;
            case ItemKind.Weapon:
                float rank = Hl2mpWeaponRanges.For(item.ClassName).Rank / CampWeaponRankFull;
                return bestHeld < CampWeakRank ? rank * CampWeakWants : rank;
            default:
                return 0f;
        }
    }

    /// <summary>A fixed number in 0..1 for this bot, this place and this stretch of time.</summary>
    private float Whim(int candidate, int epoch)
    {
        ulong x = _campSeed ^ ((ulong)(uint)candidate << 32) ^ (uint)(epoch / CampShares);
        x ^= x >> 33;
        x *= 0xFF51_AFD7_ED55_8CCDUL;
        x ^= x >> 33;
        x *= 0xC4CE_B9FE_1A85_EC53UL;
        x ^= x >> 33;
        return (x & 0xFFFF) / 65535f;
    }

    private bool Watched(AiVector at)
    {
        foreach (AiVector w in _watched)
        {
            if (w.DistanceTo(at) < 1f)
                return true;
        }

        return false;
    }

    /// <summary>Someone it saw there a moment ago (or sees): another place.</summary>
    private bool TakenBySomeone(AiVector at)
    {
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (a.Known && !a.Dead && a.EverSeen && (_now - a.LastSeenTick) * _tick < CampTakenSeconds && a.LastSeenPos.DistanceTo(at) < CampTakenUnits)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Where to stand to hold <paramref name="watch"/> (§5.5): a place on the graph
    /// that can walk to it, not on it, with a clear line to it by the map's sight
    /// table, at a range that suits the weapon, in cover if the bot cares for it (the
    /// least seen from elsewhere), and not far out of the bot's way. None such is
    /// false: a place it cannot watch from is no place to hold.
    /// </summary>
    private bool CampSpot(AiVector watch, out AiVector at)
    {
        at = default;
        CellId watched = _graph!.FindCell(watch + new AiVector(0f, 0f, 1f));
        if (!watched.IsValid)
            return false;
        ICellSight? sight = _memory.World.Sight;
        ChokeMap? chokes = _memory.Chokes;
        float range = Math.Clamp(_use.PreferredRange * CampRangeScale, CampRangeMin, CampRangeMax);
        float reach = range * 1.5f;

        if (++_coverStamp == int.MaxValue)
        {
            Array.Clear(_coverSeen);
            _coverStamp = 1;
        }

        int head = 0, tail = 0;
        _coverQueue[tail++] = watched.Index;
        _coverSeen[watched.Index] = _coverStamp;
        float best = float.NegativeInfinity;
        while (head < tail)
        {
            int c = _coverQueue[head++];
            NavCell cell = _graph.Cell(c);
            AiVector centre = cell.Centre;
            float d = centre.DistanceTo(watch);
            if (d > reach)
                continue;
            // One cell in four is enough to choose among: they are 16 units across.
            if ((c & 3) == 0 && d >= CampNearest && cell.Admits(ClearanceClass.Stand) && (cell.Flags & (NavCellFlags.Ladder | NavCellFlags.Water)) == 0
                && (sight is null || sight.Visible(watched.Index, c)))
            {
                float score = 1f - MathF.Abs(d - range) / range - 0.15f * Math.Min(1f, _self.Origin.DistanceTo(centre) / CampReach);
                if (sight is not null)
                    score += 0.5f * _p.CoverWeight * (1f - sight.Exposure(c));
                // Not in the doorway it is watching for, or anyone else's way.
                if (chokes is not null)
                    score -= CampOutOfTheWay * chokes.ScoreOf(c);
                if (score > best && !TakenBySomeone(centre))
                {
                    best = score;
                    at = centre;
                }
            }

            foreach (NavEdge e in _graph.EdgesTo(c))
            {
                if (_coverSeen[e.From] == _coverStamp || tail >= _coverQueue.Length)
                    continue;
                _coverSeen[e.From] = _coverStamp;
                _coverQueue[tail++] = e.From;
            }
        }

        return best > float.NegativeInfinity;
    }

    private bool Ambush()
    {
        if (_now >= _ambushUntil)
        {
            // Time enough to get there at a run, and a little over.
            _ambushWalkSeconds = Math.Max(AmbushWalkSeconds, _self.Origin.DistanceTo(_ambushSpot) * NavDetour / RunSpeed + 1f);
            _ambushUntil = _now + (int)(_ambushWalkSeconds / _tick);
            _ambushHolding = false;
            _watched[_watchedNext] = _ambushWatch;
            _watchedNext = (_watchedNext + 1) % _watched.Length;
        }

        // At the spot it stands and watches: a move to where it already is only jiggles.
        float toSpot = _self.Origin.DistanceTo(_ambushSpot);
        if (toSpot >= AmbushStandUnits)
        {
            GoTo(_ambushSpot, toSpot > AmbushWalkInUnits ? MoveSpeed.Run : MoveSpeed.Walk, MoveKind.Follow);
        }
        else if (!_ambushHolding)
        {
            _ambushHolding = true;
            _ambushUntil = _now + (int)((AmbushHoldSeconds + AmbushHoldPerCamp * Hl2mpStyle.Camp(in _p)) / _tick);
        }

        WatchAcross();
        return true;
    }

    /// <summary>
    /// A person holding an angle watches across it: the view sways slowly either
    /// side of the place, by <see cref="WatchSwayDeg"/> over
    /// <see cref="WatchSwaySeconds"/>. A bot frozen on one point looks switched off.
    /// </summary>
    private void WatchAcross()
    {
        AiVector to = _ambushWatch - _self.Origin;
        float flat = MathF.Sqrt(to.X * to.X + to.Y * to.Y);
        if (flat < 1f)
            return;
        float off = WatchSwayDeg * (MathF.PI / 180f) * MathF.Sin(_now.Value * _tick * 2f * MathF.PI / WatchSwaySeconds);
        float c = MathF.Cos(off), s = MathF.Sin(off);
        var swayed = new AiVector(to.X * c - to.Y * s, to.X * s + to.Y * c, to.Z + 48f);
        _look = new LookIntent(LookKind.Watch, _self.Origin + swayed, ActorId.None, AimPoint.Body, 0f, _watchedNext + 1);
    }

    /// <summary>The place it means to hold, or holds, for the facts and the inspector.</summary>
    public AiVector HoldingWatch => _ambushWatch;

    /// <summary>How far either side of the place held the view sways, and how long a sway takes.</summary>
    public const float WatchSwayDeg = 20f, WatchSwaySeconds = 5f;

    private bool DenyItem(out AiVector at)
    {
        at = default;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead || !_memory.TryHabits(a.Id, out OpponentHabits? h) || h is null)
                continue;
            foreach ((int item, float count) in h.FavouritePickups)
            {
                if (count < 2f || (uint)item >= (uint)_map.Items.Length || board.Items[item].Present == false)
                    continue;
                AiVector p = _map.Items[item].Position;
                if (_self.Origin.DistanceTo(p) < Where(a).DistanceTo(p))
                {
                    at = p;
                    return true;
                }
            }
        }

        return false;
    }

    private bool Deny()
    {
        if (!DenyItem(out AiVector at))
            return false;
        GoTo(at, MoveSpeed.Run, MoveKind.Follow);
        return true;
    }

    private bool Arm()
    {
        if (NearestWeaponToTake(out AiVector at) || NearestItem(ItemKind.Ammo, out at, out _))
        {
            GoTo(at, MoveSpeed.Run, MoveKind.Follow);
            return true;
        }

        return false;
    }

    private bool Roam()
    {
        if (!_roaming || _now >= _roamUntil || _self.Origin.DistanceTo(_roamGoal) < 96f)
        {
            int n = _map.Items.Length + _map.Spawns.Length;
            if (n == 0)
                return false;
            int pick = _rng.NextInt(n);
            _roamGoal = pick < _map.Items.Length ? _map.Items[pick].Position : _map.Spawns[pick - _map.Items.Length];
            _roamUntil = _now + (int)(15f / _tick);
            _roaming = true;
        }

        GoTo(_roamGoal, MoveSpeed.Run, MoveKind.Follow);
        return true;
    }

    // ------------------------------------------------------------------ parts

    private void GoTo(AiVector goal, MoveSpeed speed, MoveKind kind)
    {
        _hasGoal = true;
        _goal = goal;
        _move = new MoveIntent(kind, speed, 1, goal, default, default, default, Separation(), false, 0);
    }

    // The players in sight near the bot push its way aside (§9 Phase 5 step 4); carried in the move's direction.
    private readonly AiVector[] _near = new AiVector[32];

    private AiVector Separation()
    {
        int n = 0;
        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            if (n < _near.Length)
                _near[n++] = f.Origin;
        }

        return Steering.Separation(_self.Origin, _near.AsSpan(0, n));
    }

    /// <summary>
    /// A glance off the path while nobody is in sight (§3.4, by Alertness):
    /// at <c>glanceRate</c> a second, the look turns 50-100° to a side for a
    /// moment, as a player checks a doorway in passing.
    /// </summary>
    private void Glance()
    {
        if (_now < _glanceUntil)
        {
            _look = new LookIntent(LookKind.Glance, _glanceAt, ActorId.None, AimPoint.Body, 0f, 0);
            return;
        }

        if (_rng.NextFloat() >= _p.GlanceRate * DecideSchedule.Period * _tick)
            return;
        float yaw = MathF.Atan2(_goal.Y - _self.Origin.Y, _goal.X - _self.Origin.X);
        float off = (50f + 50f * _rng.NextFloat()) * (MathF.PI / 180f) * (_rng.NextFloat() < 0.5f ? -1f : 1f);
        _glanceAt = _self.Origin + new AiVector(MathF.Cos(yaw + off) * 256f, MathF.Sin(yaw + off) * 256f, 64f);
        _glanceUntil = _now + (int)(0.4f / _tick);
        Glances++;
        _look = new LookIntent(LookKind.Glance, _glanceAt, ActorId.None, AimPoint.Body, 0f, 0);
    }

    private Tick _glanceUntil;
    private AiVector _glanceAt;

    /// <summary>Glances taken, for the facts.</summary>
    public int Glances { get; private set; }

    /// <summary>
    /// A fight's sidestep (§3.4: aperiodic, by Agility): a side drawn anew
    /// after an exponential wait of mean <c>strafeMeanSeconds</c>, and with
    /// chance <c>strafeWeight</c> the stretch is a sidestep at all, not a stand;
    /// <paramref name="approach"/> mixes in moving toward (+) or away from (−)
    /// the target. Drawn by the stretch, not by the decide: on and off ten times a
    /// second the body jerks like nobody's does, and the bot's own view cannot
    /// follow it (Expert hit 37 % with Agility 5, 75 % with 0, §12.3).
    /// </summary>
    private void Strafe(float approach)
    {
        if (_now >= _strafeUntil)
        {
            _strafeSign = _rng.NextFloat() < 0.5f ? -1 : 1;
            _strafing = _rng.NextFloat() < _p.StrafeWeight;
            float u = Math.Max(_rng.NextFloat(), 1e-4f);
            float wait = -MathF.Log(u) * _p.StrafeMeanSeconds;
            _strafeUntil = _now + Math.Max(4, (int)(wait / _tick));
        }

        AiVector to = new AiVector(_targetPos.X - _self.Origin.X, _targetPos.Y - _self.Origin.Y, 0f).Normalised();
        var side = new AiVector(-to.Y, to.X, 0f);
        float strafe = _strafing ? _strafeSign : 0f;
        AiVector dir = to * approach + side * strafe;
        _move = dir.Length < 0.05f ? MoveIntent.Hold : new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, dir.Normalised(), false, 0);
    }

    /// <summary>How fast the aim's wander changes (§3.4.1's drift), seconds to fall to 1/e of its value.</summary>
    public const float AimWanderSeconds = 0.7f;

    /// <summary>The aim's error at first sight, on top of its settled size: 1.5 is 2.5 times the error (a first shot is usually worse than the fifth).</summary>
    public const float AimSettleExtra = 1.5f;

    /// <summary>How fast that extra error goes, seconds to fall to 1/e.</summary>
    public const float AimSettleSeconds = 0.6f;

    /// <summary>How smooth the wander's second filter makes it, seconds; its size stays nearly the same (a sixth smaller) at this.</summary>
    public const float AimSmoothSeconds = 0.25f;

    /// <summary>The aim's wander stops at this many of its standard deviations.</summary>
    public const float AimWanderClip = 2.5f;

    /// <summary>The aim's vertical error, as a share of its horizontal one: a body is taller than it is wide, and people misjudge across more than up.</summary>
    public const float AimErrorUpShare = 0.6f;

    private float _aimX, _aimZ, _aimSmoothX, _aimSmoothZ;
    private ActorId _aimOf;
    private Tick _aimSince;
    private bool _aimSighted;

    private float Gaussian()
    {
        // Both draws every time, so the stream does not depend on the value.
        float u = Math.Max(_rng.NextFloat(), 1e-6f), v = _rng.NextFloat();
        return MathF.Sqrt(-2f * MathF.Log(u)) * MathF.Cos(2f * MathF.PI * v);
    }

    /// <summary>
    /// The aim's error as a share of <c>aimErrorDeg</c> (§3.4.1): drawn when the
    /// target comes into sight, wandering slowly from there, not a new draw at
    /// each decide; a person's crosshair drifts, it does not jump.
    /// </summary>
    private void WanderAim()
    {
        bool fresh = _target!.Id != _aimOf || !_aimSighted;
        _aimSighted = _targetSighted;
        float a = Gaussian(), b = Gaussian();
        if (fresh)
        {
            _aimOf = _target.Id;
            _aimSince = _now;
            _aimX = a;
            _aimZ = b;
        }
        else
        {
            float rho = MathF.Exp(-DecideSchedule.Period * _tick / AimWanderSeconds);
            float rest = MathF.Sqrt(1f - rho * rho);
            _aimX = rho * _aimX + rest * a;
            _aimZ = rho * _aimZ + rest * b;
        }

        _aimX = Math.Clamp(_aimX, -AimWanderClip, AimWanderClip);
        _aimZ = Math.Clamp(_aimZ, -AimWanderClip, AimWanderClip);

        // Smoothed once more, so the crosshair's drift has no 10 Hz steps in it: each
        // would turn the view back and forth (yaw reversals, ai-replay metrics).
        float keep = fresh ? 0f : MathF.Exp(-DecideSchedule.Period * _tick / AimSmoothSeconds);
        _aimSmoothX = keep * _aimSmoothX + (1f - keep) * _aimX;
        _aimSmoothZ = keep * _aimSmoothZ + (1f - keep) * _aimZ;
    }

    private void LookAtTarget(bool fire)
    {
        if (_target is null)
            return;
        // Crouched, a body is half the height: what a client draws (§4.3).
        AiVector chest = _targetPos + new AiVector(0f, 0f, _targetCrouched ? ChestCrouched : 48f);
        float lead = 0f;
        if (_targetSighted)
        {
            // Where the target will be, seen from where the bot will be, when the
            // view gets there (§5.3.3, §3.4.1's pursuit): the view runs
            // trackingLagMs behind this point and the point is half a decide old
            // on average. A person allows for their own sidestep, and for the
            // target's by Accuracy; what they do not allow for is the error a
            // fast strafer costs.
            float allow = _p.TrackingLagMs / 1000f + (DecideSchedule.Period * 0.5f + 1f) * _tick;
            chest += (_targetVel * LeadNow - _self.Velocity) * allow;
        }

        if (_ownHasFacts && _ownFacts.ProjectileSpeed > 0f)
        {
            AiVector eye = _self.Origin + new AiVector(0f, 0f, 64f);
            AiVector point = AimLead.Point(eye, chest, _targetVel, _ownFacts.ProjectileSpeed, LeadNow);
            lead = (point - chest).Length;
            chest = point;
        }

        // The aim's error by Accuracy: a point off the target by its wander, wider at first sight.
        WanderAim();
        float seconds = (_now - _aimSince) * _tick;
        float size = MathF.Tan(AimErrorNow * (MathF.PI / 180f)) * _targetDist * (1f + AimSettleExtra * MathF.Exp(-seconds / AimSettleSeconds));
        float err = size * _aimSmoothX, errUp = size * _aimSmoothZ * AimErrorUpShare;
        AiVector to = new AiVector(chest.X - _self.Origin.X, chest.Y - _self.Origin.Y, 0f).Normalised();
        chest += new AiVector(-to.Y * err, to.X * err, errUp);
        _look = new LookIntent(LookKind.Actor, chest, _target.Id, AimPoint.Body, lead, 0);
        if (fire && _targetSighted && CanFire())
            _action = new ActionIntent(ActionKind.Fire, _use.Style, 0, 0);
    }

    private void LookAtTargetAndFire() => LookAtTarget(fire: true);

    private bool CanFire()
    {
        if (_targetDist > _use.EffectiveRange * _p.FireRangeGate)
            return false;
        if (_use.Melee)
            return true;
        if (!_ownHasFacts)
            return false;
        return _ownFacts.HasClip ? _self.Clip > 0 : _self.Reserve > 0;
    }

    /// <summary>
    /// What the hands do beside the branch (§5.4's own reloads, the weapon
    /// in hand): reload in a lull when the clip is under <c>reloadBelowPct</c>;
    /// otherwise switch to the best weapon held for the range, at most once a second.
    /// </summary>
    private void Overlays()
    {
        // A melee weapon or the gravity gun cannot reach what a gun can: in sight,
        // holding one beats swinging it.
        bool cannotReach = _targetSighted && (_use.Melee || _use.Family == "physgun");
        if (_action.Kind != ActionKind.None && !cannotReach)
            return;
        if (!_targetSighted && _ownHasFacts && _ownFacts.HasClip && _self.Reserve > 0 && !SelfReloading
            && _self.Clip < _p.ReloadBelowPct * _ownFacts.ClipSize)
        {
            _action = new ActionIntent(ActionKind.Reload, FireStyle.Tap, 0, ++_oneShot);
            return;
        }

        if ((_now - _lastSelect) * _tick < 1f)
            return;
        int best = BestHeld(out int bestKind);
        if (bestKind >= 0 && bestKind != _self.ActiveWeapon && (best > _use.Rank || InHandDry))
        {
            _lastSelect = _now;
            _action = new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, SelectByKind + bestKind, ++_oneShot);
        }
    }

    /// <summary>What the HUD says of the weapon in hand: empty, or loaded again; a weapon picked up again is loaded.</summary>
    private void NoteDry()
    {
        int held = _memory.HeldKinds;
        _dryKinds &= ~(held & ~_lastHeld);
        _lastHeld = held;
        if (_dryKinds != 0 && (_now - _dryAt) * _tick > DryForgetSeconds)
            _dryKinds = 0;
        int kind = _self.ActiveWeapon;
        if (kind is < 0 or >= 31 || !_ownHasFacts || Hl2mpWeaponRanges.For(_ownFacts.Name).Melee)
            return;
        if (_self.Clip + _self.Reserve > 0)
        {
            _dryKinds &= ~(1 << kind);
        }
        else if ((_dryKinds & (1 << kind)) == 0)
        {
            _dryKinds |= 1 << kind;
            _dryAt = _now;
        }
    }

    /// <summary>The gun in hand has nothing left at all: anything loaded beats it, whatever its rank.</summary>
    private bool InHandDry => _ownHasFacts && _ownFacts.HasClip && !_use.Melee && _self.Clip + _self.Reserve <= 0;

    /// <summary>The best rank among the held weapons (the HUD's), and its kind; with no holdings known, the one in hand.</summary>
    private int BestHeld(out int kind)
    {
        int held = _memory.HeldKinds;
        kind = _self.ActiveWeapon;
        int best = InHandDry ? -1 : _use.Rank;
        if (held == 0)
            return best;
        foreach (WeaponFacts w in _map.Weapons.All)
        {
            if (w.Kind < 0 || w.Kind >= 31 || (held & (1 << w.Kind)) == 0)
                continue;
            WeaponUse u = Hl2mpWeaponRanges.For(w.Name);
            // The one in hand needs rounds; the HUD's others are loaded unless found empty.
            bool loaded = u.Melee || (w.Kind == _self.ActiveWeapon ? _self.Clip + _self.Reserve > 0 : (_dryKinds & (1 << w.Kind)) == 0);
            if (u.Rank > best && loaded && w.Kind != _self.ActiveWeapon)
            {
                best = u.Rank;
                kind = w.Kind;
            }
        }

        return best;
    }

    private bool NearestWeaponToTake(out AiVector at)
    {
        at = default;
        float best = float.PositiveInfinity;
        int held = _memory.HeldKinds;
        ReadOnlySpan<ItemBelief> beliefs = _memory.Board.Items;
        for (int i = 0; i < _map.Items.Length; i++)
        {
            ItemSpawn item = _map.Items[i];
            if (item.Kind != ItemKind.Weapon || (i < beliefs.Length && beliefs[i].Present == false))
                continue;
            if (item.WeaponKind >= 0 && item.WeaponKind < 31 && (held & (1 << item.WeaponKind)) != 0)
                continue;
            if (Hl2mpWeaponRanges.For(item.ClassName).Rank < 3)
                continue;
            float d = _self.Origin.DistanceTo(item.Position);
            if (d < best && !SomeoneTakesFirst(item.Position, d))
            {
                best = d;
                at = item.Position;
            }
        }

        return float.IsFinite(best);
    }

    /// <summary>How far from a pickup a player in sight is about to take it.</summary>
    public const float TakesFirstReach = 256f;

    /// <summary>
    /// A player in sight nearer the pickup, and close to it, gets there first: the
    /// bot goes for another rather than push them for it (user report
    /// 2026-11-02: two stuck together on one goal). Ties go to the lower slot, so
    /// one of two always goes on.
    /// </summary>
    private bool SomeoneTakesFirst(AiVector item, float mine)
    {
        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            float theirs = f.Origin.DistanceTo(item);
            if (theirs < TakesFirstReach && (theirs < mine || (theirs == mine && f.Id.Slot < _bot.Slot)))
                return true;
        }

        return false;
    }

    private bool NearestItem(ItemKind kind, out AiVector at, out int index)
    {
        at = default;
        index = -1;
        float best = float.PositiveInfinity;
        ReadOnlySpan<ItemBelief> beliefs = _memory.Board.Items;
        for (int i = 0; i < _map.Items.Length; i++)
        {
            ItemSpawn item = _map.Items[i];
            if (item.Kind != kind || (i < beliefs.Length && beliefs[i].Present == false))
                continue;
            float d = _self.Origin.DistanceTo(item.Position);
            if (d < best && !SomeoneTakesFirst(item.Position, d))
            {
                best = d;
                at = item.Position;
                index = i;
            }
        }

        return index >= 0;
    }

    private string WeaponName(int kind) => _map.Weapons.TryGet(kind, out WeaponFacts w) ? w.Name : "";

    private AiVector Away(AiVector from, float distance)
    {
        AiVector d = new AiVector(_self.Origin.X - from.X, _self.Origin.Y - from.Y, 0f).Normalised();
        return _self.Origin + d * distance;
    }
}
