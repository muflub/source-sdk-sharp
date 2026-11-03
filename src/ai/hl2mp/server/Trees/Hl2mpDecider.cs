//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Ai.Hl2mp.Trees;

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

    /// <summary>A one-shot's weapon slot asks for a weapon by catalog kind: this plus the kind (the applier's convention).</summary>
    public const int SelectByKind = 1000;

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

    // This decide's inputs.
    private PerceivedView _view = null!;
    private SelfState _self;
    private Tick _now;
    private KnownActor? _target;
    private bool _targetSighted;
    private AiVector _targetPos;
    private AiVector _targetVel;
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
    private Tick _strafeUntil;
    private AiVector _roamGoal;
    private Tick _roamUntil;
    private bool _roaming;
    private Tick _ambushUntil;
    private AiVector _ambushSpot, _ambushWatch;
    private Tick _lastSelect;

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

        if (!_self.Alive)
        {
            _tree.Reset();
            _roaming = false;
            _ambushUntil = default;
            Trace.Begin(tick);
            Trace.End(default);
            Doing = new DoingRecord("dead", "", false, default, ActorId.None, "", 0f, 0f, 0f, 0f, 0f, 0f, tick);
            return IntentCommand.Hold(_bot, tick, seq);
        }

        _ownHasFacts = _map.Weapons.TryGet(_self.ActiveWeapon, out _ownFacts);
        _use = Hl2mpWeaponRanges.For(_ownHasFacts ? _ownFacts.Name : "");
        ChooseTarget();

        _tree.Decide(this, _run, tick);
        if (!_targetSighted && _hasGoal && _look.Kind == LookKind.Path)
            Glance();
        Overlays();

        string branch = _root.Chosen?.Name ?? "none";
        var reason = new DecisionReason(LeafId(), _root.Chosen is null ? 0f : Utility(_root.Chosen), _scoreboardTerm, _ammoTerm);
        Trace.End(in reason);
        Tick expires = tick + IntentCommand.LifetimeTicks;
        Doing = new DoingRecord(branch, _leaf, _hasGoal, _goal, _target?.Id ?? ActorId.None, _verdict, _window.PEmpty, _window.PReloading,
                                _window.SecondsLeft, _window.ArriveSeconds, _scoreboardTerm, _ammoTerm, expires);
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
            new BtSequence<Hl2mpDecider>("hold", If("in sight", d => d._targetSighted), Act("hold range", d => d.HoldRange())),
            Act("chase", d => d.Chase()));

        return new BtUtilitySelector<Hl2mpDecider>("root",
            new BtScored<Hl2mpDecider>("Survive", d => d.SurviveUtility(), Act("go heal", d => d.GoHeal())),
            new BtScored<Hl2mpDecider>("Fight", d => d.FightUtility(), fight),
            new BtScored<Hl2mpDecider>("Hunt", d => d.HuntUtility(), Act("hunt", d => d.Hunt())),
            new BtScored<Hl2mpDecider>("Search", d => d.SearchUtility(), Act("search", d => d.Search())),
            new BtScored<Hl2mpDecider>("Ambush", d => d.AmbushUtility(), Act("ambush", d => d.Ambush())),
            new BtScored<Hl2mpDecider>("Deny", d => d.DenyUtility(), Act("deny", d => d.Deny())),
            new BtScored<Hl2mpDecider>("Arm", d => d.ArmUtility(), Act("arm", d => d.Arm())),
            new BtScored<Hl2mpDecider>("Roam", _ => 0.1f, Act("roam", d => d.Roam())));
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
        float best = 0f;
        int rows = 0;
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
            float proximity = 1f / (1f + dist / 768f);
            float priority = (sighted ? 1f : 0.5f * a.Confidence) * (proximity * _p.EasyTargetDiscount + rankTerm);
            if (priority > best)
            {
                best = priority;
                _target = a;
                _targetSighted = sighted;
                _targetPos = at;
                _targetVel = sighted ? f.Velocity : a.LastSeenVel;
                _targetDist = dist;
                _scoreboardTerm = rankTerm;
            }
        }
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

    private bool SelfReloading => (_self.Flags & SelfFlags.Reloading) != 0;

    private bool ClipEmpty => _ownHasFacts && _ownFacts.HasClip && _self.Clip == 0 && _self.Reserve > 0;

    private float HealthShare => Math.Clamp(_self.Health / 100f, 0f, 1f);

    // ------------------------------------------------------------------ utilities

    private float SurviveUtility()
    {
        bool threatened = _targetSighted || (_now - _memory.Board.LastDamageTick) * _tick < 3f;
        if (HealthShare < _p.RetreatHealthPct && threatened)
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
        return 0.45f + 0.3f * _scoreboardTerm;
    }

    private float SearchUtility()
        => _memory.Board.TryBestInvestigation(_now, _tick, out Investigation i) && (_now - i.Tick) * _tick < _p.SearchSeconds ? 0.4f : 0f;

    private float AmbushUtility()
    {
        if (_targetSighted || _p.AmbushWeight <= 0f)
            return 0f;
        if (_now < _ambushUntil)
            return 0.35f + 0.5f * _p.AmbushWeight;
        return PlanAmbush() ? 0.2f + 0.4f * _p.AmbushWeight : 0f;
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
        Strafe(-0.8f);
        LookAtTarget(fire: false);
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
        return true;
    }

    private bool Chase()
    {
        if (_target is null)
            return false;
        _verdict = "chase";
        GoTo(_target.LastSeenPos, MoveSpeed.Run, MoveKind.Follow);
        _look = new LookIntent(LookKind.Point, _target.LastSeenPos + new AiVector(0f, 0f, 48f), _target.Id, AimPoint.Body, 0f, 0);
        return true;
    }

    private bool Hunt()
    {
        if (_target is null)
            return false;
        GoTo(_targetPos, MoveSpeed.Run, MoveKind.Follow);
        return true;
    }

    private bool Search()
    {
        if (!_memory.Board.TryBestInvestigation(_now, _tick, out Investigation i))
            return false;
        GoTo(i.Position, MoveSpeed.Run, MoveKind.Follow);
        if (_self.Origin.DistanceTo(i.Position) < 96f)
            _memory.Board.Resolve(i.Position, 128f, i.Source);
        return true;
    }

    private bool PlanAmbush()
    {
        if (_graph is null)
            return false;
        Blackboard board = _memory.Board;
        float best = 0f;
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
                _ambushWatch = _graph.Cell(cell).Centre;
            }
        }

        if (best <= 0f)
            return false;
        // A spot a few hundred units back along the line from the watched cell to the bot.
        AiVector toBot = (_self.Origin - _ambushWatch).Normalised();
        _ambushSpot = _ambushWatch + toBot * 384f;
        return true;
    }

    private bool Ambush()
    {
        if (_now >= _ambushUntil)
            _ambushUntil = _now + (int)(8f / _tick);
        GoTo(_ambushSpot, MoveSpeed.Walk, MoveKind.Follow);
        _look = new LookIntent(LookKind.Point, _ambushWatch + new AiVector(0f, 0f, 48f), ActorId.None, AimPoint.Body, 0f, 0);
        return true;
    }

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
    /// after an exponential wait of mean <c>strafeMeanSeconds</c>, and the
    /// share <c>strafeWeight</c> of decides strafing at all;
    /// <paramref name="approach"/> mixes in moving toward (+) or away from (−)
    /// the target.
    /// </summary>
    private void Strafe(float approach)
    {
        if (_now >= _strafeUntil)
        {
            _strafeSign = _rng.NextFloat() < 0.5f ? -1 : 1;
            float u = Math.Max(_rng.NextFloat(), 1e-4f);
            float wait = -MathF.Log(u) * _p.StrafeMeanSeconds;
            _strafeUntil = _now + Math.Max(4, (int)(wait / _tick));
        }

        AiVector to = new AiVector(_targetPos.X - _self.Origin.X, _targetPos.Y - _self.Origin.Y, 0f).Normalised();
        var side = new AiVector(-to.Y, to.X, 0f);
        float strafe = _rng.NextFloat() < _p.StrafeWeight ? _strafeSign : 0f;
        AiVector dir = to * approach + side * strafe;
        _move = dir.Length < 0.05f ? MoveIntent.Hold : new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, dir.Normalised(), false, 0);
    }

    private void LookAtTarget(bool fire)
    {
        if (_target is null)
            return;
        AiVector chest = _targetPos + new AiVector(0f, 0f, 48f);
        float lead = 0f;
        if (_ownHasFacts && _ownFacts.ProjectileSpeed > 0f)
        {
            AiVector eye = _self.Origin + new AiVector(0f, 0f, 64f);
            AiVector point = AimLead.Point(eye, chest, _targetVel, _ownFacts.ProjectileSpeed, _p.LeadQuality);
            lead = (point - chest).Length;
            chest = point;
        }

        // The aim's error by Accuracy: a point off the target by the error cone at its distance.
        float err = MathF.Tan(_p.AimErrorDeg * (MathF.PI / 180f)) * _targetDist * (_rng.NextFloat() * 2f - 1f);
        float errUp = MathF.Tan(_p.AimErrorDeg * (MathF.PI / 180f)) * _targetDist * (_rng.NextFloat() - 0.5f);
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
        if (_action.Kind != ActionKind.None)
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
        if (bestKind >= 0 && bestKind != _self.ActiveWeapon && best > _use.Rank)
        {
            _lastSelect = _now;
            _action = new ActionIntent(ActionKind.SelectWeapon, FireStyle.Tap, SelectByKind + bestKind, ++_oneShot);
        }
    }

    /// <summary>The best rank among the held weapons (the HUD's), and its kind; with no holdings known, the one in hand.</summary>
    private int BestHeld(out int kind)
    {
        int held = _memory.HeldKinds;
        kind = _self.ActiveWeapon;
        int best = _use.Rank;
        if (held == 0)
            return best;
        foreach (WeaponFacts w in _map.Weapons.All)
        {
            if (w.Kind < 0 || w.Kind >= 31 || (held & (1 << w.Kind)) == 0)
                continue;
            WeaponUse u = Hl2mpWeaponRanges.For(w.Name);
            // The one in hand needs rounds; the HUD's other weapons are taken as loaded.
            bool loaded = w.Kind != _self.ActiveWeapon || u.Melee || _self.Clip + _self.Reserve > 0;
            if (u.Rank > best && loaded)
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
            if (d < best)
            {
                best = d;
                at = item.Position;
            }
        }

        return float.IsFinite(best);
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
            if (d < best)
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
