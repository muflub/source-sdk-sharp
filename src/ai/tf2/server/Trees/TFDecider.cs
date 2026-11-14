using System.Collections.Immutable;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// A TF2 bot's decision layer (plan_bots_teams_tf2 C.3, C.6, P6 step 6): one tree, a subtree per
/// class (<see cref="TFClassTrees"/>: the Soldier's is the only real one, and every other class
/// runs it and says so), over the shared tactical layer and the CTF objectives the HUD shows.
///
/// <para>The root ranks, by utility: deliver the enemy's flag the bot carries, survive (a health
/// pack or the team's resupply when hurt), fight what it sees, chase the enemy carrying its flag,
/// guard its dropped flag, escort its carrier, fetch the enemy's flag, defend its own, and roam
/// toward the enemy. Beside the branch, every decide: the hands fire on what the bot sees, the
/// Soldier aims a rocket at the feet of a grounded target (<see cref="AimPoint.Feet"/>), keeps its
/// launcher loaded, swaps to the shotgun when the launcher is dry up close, and rocket-jumps on a
/// long run where the nav says the way ahead is clear.</para>
///
/// <para>Team roles: a bot is a defender or an attacker for the life of its slot, a third of
/// the slots defending (<see cref="DefendEvery"/>), which weighs the objective branches. What
/// it knows is its view and memory only; nothing here reads the server. Its own random streams
/// are drawn from its seed, so a replay of the same views makes the same intents.</para>
/// </summary>
public sealed partial class TFDecider : IDecider
{
    /// <summary>Every this many slots, one is a defender (the slot's remainder 0); the rest attack.</summary>
    public const int DefendEvery = 3;

    /// <summary>A target out of sight this long ago is still fought (a corner flickers sight on and off).</summary>
    public const float SightGraceSeconds = 0.75f;

    /// <summary>Unseen enemies further than this, or believed less than <see cref="HuntConfidence"/>, are no target.</summary>
    public const float HuntReach = 2048f, HuntConfidence = 0.05f;

    /// <summary>A goal this near is reached.</summary>
    public const float ArriveUnits = 96f;

    /// <summary>Survive: under this share of its health the bot goes to a pack or a cabinet within <see cref="HealReach"/>.</summary>
    public const float HurtShare = 0.4f, HealReach = 2500f;

    /// <summary>Defend: the bot keeps within this of its flag, picking a new spot near it this often.</summary>
    public const float DefendRadius = 384f, DefendSpotSeconds = 6f;

    /// <summary>Escort: the bot keeps this far behind the carrier, not on top of it.</summary>
    public const float EscortGap = 160f;

    /// <summary>The fight's ranges: a launcher's best is between these; nearer the blast hurts the bot too.</summary>
    public const float FightNear = 320f, FightFar = 900f, SelfBlastUnits = 160f;

    private readonly ActorId _bot;
    private readonly BotMemory _memory;
    private readonly DecisionParams _p;
    private readonly MapKnowledge _map;
    private readonly NavGraph? _graph;
    private readonly float _tick;
    private readonly AiRandom _rng;
    private readonly AiRandom _aimRng;
    private readonly BtRun _run;
    private readonly BehaviorTree<TFDecider> _tree;
    private readonly BtUtilitySelector<TFDecider> _root;
    private readonly string[] _names;
    private readonly string[] _branchNames;
    private readonly ImmutableArray<Knowledge.TFClassFacts> _classes;

    // This decide's inputs.
    private PerceivedView _view = null!;
    private SelfState _self;
    private Tick _now;
    private TFClass _class;
    private KnownActor? _target;
    private bool _targetSighted;
    private AiVector _targetPos;
    private AiVector _targetVel;
    private float _targetDist;
    private bool _interrupted;

    // This decide's output.
    private MoveIntent _move;
    private LookIntent _look;
    private ActionIntent _action;
    private string _leaf = "";
    private string _verdict = "";
    private bool _hasGoal;
    private AiVector _goal;

    // What outlives a decide.
    private int _oneShot;
    private KnownActor? _lastTarget;
    private int _lastDamageSeen;
    private int _strafeSign = 1;
    private Tick _strafeUntil;
    private AiVector _roamGoal;
    private Tick _roamUntil;
    private bool _roaming;
    private AiVector _defendSpot;
    private Tick _defendUntil;
    private bool _defendPicked;
    private TFClass _namedClass = TFClass.Undefined;
    private string _subtree = "";

    public TFDecider(BrainSetup setup, ImmutableArray<Knowledge.TFClassFacts> classes)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _classes = classes.IsDefault ? [] : classes;
        _bot = setup.Bot;
        _memory = setup.Memory;
        _p = setup.Params;
        _map = setup.Memory.World.Map;
        _graph = setup.Memory.World.Graph;
        _tick = setup.TickSeconds;
        _rng = new AiRandom(setup.Seed ^ 0x5446_5452UL);
        _aimRng = new AiRandom(setup.Seed ^ 0x5446_4149UL);
        StartFeel(setup.Seed);
        Role = _bot.Slot % DefendEvery == 0 ? TFTeamRole.Defend : TFTeamRole.Attack;
        _launcher = KindNamed(SoldierLauncher);
        _shotgun = KindNamed(SoldierShotgun);
        _rocketJumpRow = AbilityRow(Knowledge.TFAbilities.RocketJump);
        Trace = new BtTrace();
        _run = new BtRun(_rng, Trace, _tick);
        _root = Build();
        _tree = new BehaviorTree<TFDecider>(_root);
        _branchNames = new string[_root.Children.Length];
        _names = new string[_tree.Nodes.Length];
        for (int i = 0; i < _names.Length; i++)
            _names[i] = _tree.Nodes[i].Name;
    }

    public BtTrace Trace { get; }

    public DoingRecord Doing { get; private set; }

    public IReadOnlyList<string> NodeNames => _names;

    public IReadOnlyList<int> NodeBranches => _tree.Branches();

    /// <summary>The bot's team role, by its slot.</summary>
    public TFTeamRole Role { get; }

    /// <summary>The subtree the bot ran at its last decide alive: "Soldier", or "Soldier (as Scout)" for a class with none of its own.</summary>
    public string Subtree => _subtree;

    public IntentCommand Decide(Tick tick, PerceivedView view, int seq)
    {
        _view = view;
        _self = view.Self;
        _now = tick;
        _move = MoveIntent.Hold;
        _look = LookIntent.AlongPath;
        _action = ActionIntent.None;
        _leaf = "";
        _verdict = "";
        _hasGoal = false;
        Streak();

        if (!_self.Alive)
        {
            _tree.Reset();
            _roaming = _defendPicked = _healing = false;
            _lastTarget = null;
            _jumpUntil = default;
            Trace.Begin(tick);
            Trace.End(default);
            Doing = new DoingRecord("dead", "", false, default, ActorId.None, "", 0f, 0f, 0f, 0f, 0f, 0f, tick);
            return IntentCommand.Hold(_bot, tick, seq);
        }

        _class = (TFClass)_self.Class;
        if (_class != _namedClass)
        {
            _namedClass = _class;
            _subtree = TFClassTrees.Name(_class);
            ReadOnlySpan<BtNode<TFDecider>> branches = _root.Children;
            for (int i = 0; i < branches.Length; i++)
                _branchNames[i] = $"{_subtree}: {branches[i].Name}";
        }

        ReadObjectives();
        ChooseTarget();
        _interrupted = NoteInterrupts();
        _tree.Decide(this, _run, tick);
        Engage();
        RocketJump();
        Hands();
        Glance();
        FriendGate();

        var reason = new DecisionReason(LeafId(), _root.Chosen?.LastScore ?? 0f, 0f, 0f);
        Trace.End(in reason);
        Tick expires = tick + IntentCommand.LifetimeTicks;
        Doing = WithStreak(new DoingRecord(BranchName(), _leaf, _hasGoal, _goal, _target?.Id ?? ActorId.None, _verdict, 0f, 0f, 0f, 0f, 0f, 0f, expires));
        return new IntentCommand(_bot, tick, seq, expires, _move, _look, _action, reason);
    }

    /// <summary>The doing record's branch: the subtree's name and the root's chosen child, made once per class, not per decide.</summary>
    private string BranchName()
    {
        ReadOnlySpan<BtNode<TFDecider>> branches = _root.Children;
        for (int i = 0; i < branches.Length; i++)
        {
            if (ReferenceEquals(branches[i], _root.Chosen))
                return _branchNames[i];
        }

        return $"{_subtree}: none";
    }

    private int LeafId()
    {
        ReadOnlySpan<BtTraceEntry> e = Trace.Count > 0 ? Trace.EntriesAt(0) : default;
        for (int i = e.Length - 1; i >= 0; i--)
        {
            if (e[i].Status != BtStatus.Failure && _tree.Nodes[e[i].NodeId] is BtAction<TFDecider>)
                return e[i].NodeId;
        }

        return -1;
    }

    // ------------------------------------------------------------------ the tree

    private BtUtilitySelector<TFDecider> Build()
    {
        static BtNode<TFDecider> Act(string name, Func<TFDecider, bool> act)
            => new BtAction<TFDecider>(name, (d, _) =>
            {
                if (!act(d))
                    return BtStatus.Failure;
                d._leaf = name;
                return BtStatus.Success;
            });
        static BtNode<TFDecider> If(string name, Func<TFDecider, bool> test) => new BtCondition<TFDecider>(name, test);

        var fight = new BtSelector<TFDecider>("fight",
            new BtSequence<TFDecider>("dry", If("launcher dry", d => d.LauncherDry), Act("back off and load", d => d.BackOffDry())),
            new BtSequence<TFDecider>("hold", If("in sight", d => d._targetSighted || d.SightJustLost), Act("fight at range", d => d.FightAtRange())),
            Act("chase", d => d.ChaseTarget()));

        return new BtUtilitySelector<TFDecider>("root",
            new BtScored<TFDecider>(Deliver, d => d.DeliverUtility(), Act("deliver", d => d.GoDeliver()), new BtCommit(1f, 0.05f)),
            new BtScored<TFDecider>(Survive, d => d.SurviveUtility(), Act("go heal", d => d.GoHeal()), new BtCommit(3f, 0.1f)),
            new BtScored<TFDecider>(Fight, d => d.FightUtility(), fight, new BtCommit(1.5f, 0.1f)),
            new BtScored<TFDecider>(ChaseCarrier, d => d.ChaseCarrierUtility(), Act("chase carrier", d => d.GoChaseCarrier()), new BtCommit(2f, 0.1f)),
            new BtScored<TFDecider>(Guard, d => d.GuardUtility(), Act("guard dropped flag", d => d.GoGuard()), new BtCommit(2f, 0.1f)),
            new BtScored<TFDecider>(Escort, d => d.EscortUtility(), Act("escort", d => d.GoEscort()), new BtCommit(2f, 0.1f)),
            new BtScored<TFDecider>(Fetch, d => d.FetchUtility(), Act("fetch", d => d.GoFetch()), new BtCommit(2f, 0.1f)),
            new BtScored<TFDecider>(Defend, d => d.DefendUtility(), Act("defend", d => d.GoDefend()), new BtCommit(3f, 0.1f)),
            new BtScored<TFDecider>(Roam, _ => 0.1f, Act("roam", d => d.GoRoam()), new BtCommit(1f, 0.05f)))
        {
            Interrupt = d => d._interrupted,
        };
    }

    /// <summary>The root's branch names, which the doing record carries after the subtree's.</summary>
    public const string Deliver = "Deliver", Survive = "Survive", Fight = "Fight", ChaseCarrier = "ChaseCarrier", Guard = "Guard",
                        Escort = "Escort", Fetch = "Fetch", Defend = "Defend", Roam = "Roam";

    /// <summary>
    /// What ends a commitment at once: an enemy newly in sight, or damage taken since the last
    /// decide. Runs once a decide, before the tree.
    /// </summary>
    private bool NoteInterrupts()
    {
        bool fresh = _targetSighted && !ReferenceEquals(_target, _lastTarget);
        _lastTarget = _targetSighted ? _target : null;
        int damage = _memory.Board.LastDamageTick.Value;
        bool hurt = damage != _lastDamageSeen;
        _lastDamageSeen = damage;
        return fresh || hurt;
    }

    // ------------------------------------------------------------------ targets

    /// <summary>
    /// The enemy to fight: someone in sight before anyone remembered (one close enough to go
    /// after), the best of them by <see cref="TakesTarget"/>. Never a friend as the bot's client
    /// draws them (a disguised Spy is drawn on the bot's team, and is no target here), nor an enemy
    /// standing in its own respawn room, which the game does not let anyone fight in.
    /// </summary>
    private void ChooseTarget()
    {
        BeginTargets();
        _target = null;
        _targetSighted = false;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead || _memory.IsFriend(a))
                continue;
            bool sighted = _view.TryActor(a.Id, out ActorFacts f);
            if (sighted && TeamRule.Friends(_self.Team, f.Team))
                continue;
            AiVector at = sighted ? f.Origin : a.BelievedAt(_graph);
            if (InEnemyRoom(at, sighted ? f.Team : a.Team))
                continue;
            float dist = _self.Origin.DistanceTo(at);
            if ((!sighted && (a.Confidence < HuntConfidence || dist > HuntReach)) || !TakesTarget(a, sighted, dist))
                continue;

            _target = a;
            _targetSighted = sighted;
            _targetPos = at;
            _targetVel = sighted ? f.Velocity : a.LastSeenVel;
            _targetDist = dist;
        }
    }

    /// <summary>Whether <paramref name="at"/> lies in a respawn room of <paramref name="team"/>, a game team that is not the bot's.</summary>
    private bool InEnemyRoom(AiVector at, int team)
    {
        if (!TeamRule.IsGameTeam(team) || team == _self.Team)
            return false;
        foreach (TeamZone z in _map.TeamZones)
        {
            if (z.Kind == ZoneKind.RespawnRoom && z.Team == team && z.Contains(at))
                return true;
        }

        return false;
    }

    private bool SightJustLost => _target is not null && !_targetSighted && _target.EverSeen && (_now - _target.LastSeenTick) * _tick < SightGraceSeconds;

    private float HealthShare => Math.Clamp(_self.Health / (float)MaxHealth, 0f, 1f);

    /// <summary>The class's spawn health, as its HUD's bar is drawn against (the class table's); a Soldier's when the table had none.</summary>
    private int MaxHealth
    {
        get
        {
            foreach (Knowledge.TFClassFacts c in _classes)
            {
                if (c.Class == _class && c.MaxHealth > 0)
                    return c.MaxHealth;
            }

            return SoldierHealth;
        }
    }

    /// <summary>A Soldier's spawn health, stock's 200, read when the class table answered nothing.</summary>
    public const int SoldierHealth = 200;

    // ------------------------------------------------------------------ utilities

    private float SurviveUtility()
    {
        if (Carrying || HealthShare >= HurtShare || !HealPlace(out _))
            return 0f;
        return _targetSighted ? 0.82f : 0.9f;
    }

    /// <summary>
    /// The fight's weight: an enemy in sight near enough to stop for, else one further off, else one
    /// remembered. An attacker with the enemy's flag to take stops only for an enemy within
    /// <see cref="AttackerStopsFor"/>: further off it goes on for the flag, the hands firing as it goes
    /// (live, ctf_2fort: attackers who stopped for every enemy on the bridge took one flag in nine minutes).
    /// </summary>
    private float FightUtility()
    {
        if (_target is null)
            return 0f;
        if (!_targetSighted && !SightJustLost)
            return 0.3f;
        float near = Role == TFTeamRole.Attack && FetchUtility() > 0f ? AttackerStopsFor : FightFar;
        return _targetDist < near ? 0.8f : 0.6f;
    }

    /// <summary>How near an enemy an attacker on its way to the enemy's flag stops to fight.</summary>
    public const float AttackerStopsFor = 480f;

    private float DeliverUtility() => Carrying && _ownZone is not null ? 1f : 0f;

    // ------------------------------------------------------------------ the tactical layer's leaves

    private bool GoHeal()
    {
        if (!HealPlace(out AiVector at))
            return false;
        if (!_healing || at.DistanceTo(_healAt) > 1f)
            (_healing, _healAt, _healSince) = (true, at, _now);
        else if ((_now - _healSince) * _tick > HealPatienceSeconds)
        {
            // Not healed there in that long (the pack taken, or out of reach): another place, for a while.
            (_healSkip, _healSkipUntil, _healing) = (at, _now + (int)(HealSkipSeconds / _tick), false);
            if (!HealPlace(out at))
                return false;
            (_healing, _healAt, _healSince) = (true, at, _now);
        }

        GoTo(at, MoveSpeed.Run);
        _verdict = "hurt";
        return true;
    }

    /// <summary>A heal place not reached and healed at in this long is given up for <see cref="HealSkipSeconds"/>.</summary>
    public const float HealPatienceSeconds = 15f, HealSkipSeconds = 30f;

    private bool _healing;
    private AiVector _healAt, _healSkip;
    private Tick _healSince, _healSkipUntil;

    /// <summary>The nearest health pack, or the team's own resupply cabinet, within <see cref="HealReach"/>.</summary>
    private bool HealPlace(out AiVector at)
    {
        at = default;
        float best = HealReach;
        ReadOnlySpan<ItemBelief> beliefs = _memory.Board.Items;
        for (int i = 0; i < _map.Items.Length; i++)
        {
            ItemSpawn item = _map.Items[i];
            // A pack the bot saw taken (or found it could not get to) is not there for now.
            if (item.Kind is not (ItemKind.HealthSmall or ItemKind.HealthMedium or ItemKind.HealthLarge or ItemKind.Health)
                || (i < beliefs.Length && beliefs[i].Present == false) || Skipped(item.Position))
                continue;
            float d = _self.Origin.DistanceTo(item.Position);
            if (d < best)
                (best, at) = (d, item.Position);
        }

        foreach (TeamZone z in _map.TeamZones)
        {
            if (z.Kind != ZoneKind.Resupply || z.Team != _self.Team)
                continue;
            var mid = new AiVector((z.Mins.X + z.Maxs.X) * 0.5f, (z.Mins.Y + z.Maxs.Y) * 0.5f, z.Mins.Z);
            if (Skipped(mid))
                continue;
            float d = _self.Origin.DistanceTo(mid);
            if (d < best)
                (best, at) = (d, mid);
        }

        return best < HealReach;
    }

    private bool Skipped(AiVector at) => _now < _healSkipUntil && at.DistanceTo(_healSkip) <= 1f;

    private bool BackOffDry()
    {
        if (!_targetSighted)
            return false;
        Strafe(-0.6f);
        _verdict = "launcher dry";
        return true;
    }

    /// <summary>In sight: hold the launcher's range, sidestepping, nearer when far and back when the blast would reach the bot.</summary>
    private bool FightAtRange()
    {
        float approach = _targetDist > FightFar ? 0.8f : _targetDist < FightNear ? -0.6f : 0f;
        Strafe(approach);
        _verdict = approach > 0f ? "closing" : approach < 0f ? "backing off" : "holding range";
        return true;
    }

    private bool ChaseTarget()
    {
        if (_target is null)
            return false;
        GoTo(_targetPos, MoveSpeed.Run);
        _verdict = "chasing";
        return true;
    }

    private bool GoRoam()
    {
        if (!_roaming || _now >= _roamUntil || _self.Origin.DistanceTo(_roamGoal) < ArriveUnits)
        {
            // Toward the enemy: one of their spawns, else any spawn, else a pack.
            int enemies = 0, all = _map.TeamSpawns.Length;
            foreach (TeamSpawn s in _map.TeamSpawns)
            {
                if (TeamRule.IsGameTeam(s.Team) && s.Team != _self.Team)
                    enemies++;
            }

            if (all == 0 && _map.Items.Length == 0)
                return false;
            if (enemies > 0)
            {
                int pick = _rng.NextInt(enemies);
                foreach (TeamSpawn s in _map.TeamSpawns)
                {
                    if (TeamRule.IsGameTeam(s.Team) && s.Team != _self.Team && pick-- == 0)
                        _roamGoal = s.Position;
                }
            }
            else
            {
                int pick = _rng.NextInt(all + _map.Items.Length);
                _roamGoal = pick < all ? _map.TeamSpawns[pick].Position : _map.Items[pick - all].Position;
            }

            _roamUntil = _now + (int)(20f / _tick);
            _roaming = true;
        }

        GoTo(_roamGoal, MoveSpeed.Run);
        return true;
    }

    // ------------------------------------------------------------------ parts

    private void GoTo(AiVector goal, MoveSpeed speed)
    {
        _hasGoal = true;
        _goal = goal;
        _move = new MoveIntent(MoveKind.Follow, speed, 1, goal, default, default, default, Separation(), false, 0);
    }

    private readonly AiVector[] _near = new AiVector[32];

    /// <summary>The enemies in sight near the bot push its way aside; a teammate is walked through (plan A.7).</summary>
    private AiVector Separation()
    {
        int n = 0;
        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            if (n < _near.Length && !TeamRule.Friends(_self.Team, f.Team))
                _near[n++] = f.Origin;
        }

        return Steering.Separation(_self.Origin, _near.AsSpan(0, n));
    }

    /// <summary>A fight's sidestep: a side drawn anew after an exponential wait (by Agility's curves), mixed with <paramref name="approach"/>.</summary>
    private void Strafe(float approach)
    {
        if (_now >= _strafeUntil)
        {
            _strafeSign = _rng.NextFloat() < 0.5f ? -1 : 1;
            bool strafing = _rng.NextFloat() < _p.StrafeWeight;
            if (!strafing)
                _strafeSign = 0;
            float u = Math.Max(_rng.NextFloat(), 1e-4f);
            _strafeUntil = _now + Math.Max(4, (int)(-MathF.Log(u) * Math.Max(0.2f, _p.StrafeMeanSeconds) / _tick));
        }

        AiVector to = new AiVector(_targetPos.X - _self.Origin.X, _targetPos.Y - _self.Origin.Y, 0f).Normalised();
        var side = new AiVector(-to.Y, to.X, 0f);
        AiVector dir = to * approach + side * _strafeSign;
        _move = dir.Length < 0.05f ? MoveIntent.Hold : new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, dir.Normalised(), false, 0);
    }
}

/// <summary>A TF2 bot's part in its team's play (plan_bots_teams_tf2 C.6): it weighs the objective branches.</summary>
public enum TFTeamRole : byte
{
    Attack = 0,
    Defend,
}
