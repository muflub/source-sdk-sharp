namespace SourceSharp.Ai.Hl2mp.Trees;

/// <summary>
/// The two team roles that read the level's routes (plan_bots_teams_tf2 B.5, P2 step 8):
/// Crossfire, where of two teammates on one way to a known enemy the one behind takes the
/// other door, and Chase with a partner, where the nearer of two hunting teammates leads and
/// the other holds the way out on the enemy's far side. Both refine the tree's Hunt and run
/// only on a team, after the tree has decided: a deathmatch bot's decides are untouched.
/// </summary>
public sealed partial class Hl2mpDecider
{
    /// <summary>A teammate this near the bot can be its partner on a route.</summary>
    public const float PartnerReach = 1024f;

    /// <summary>A teammate within this of the bot's shortest way to the enemy is on the same path.</summary>
    public const float SamePathReach = 384f;

    /// <summary>The other door is taken only when its way is at most this many times the shortest.</summary>
    public const float CrossfireDetour = 1.75f;

    /// <summary>The shortest way is closed to the other route this far either side of it, except near the bot and the enemy.</summary>
    public const float CrossfireCorridor = 192f, CrossfireKeep = 256f;

    /// <summary>A teammate's "going around" heard this recently: it goes, this bot leads.</summary>
    public const float GoingAroundHeardSeconds = 6f;

    /// <summary>The exits a chase's follower holds lie this far along the graph from the enemy's believed cell.</summary>
    public const float ChaseExitNear = 384f, ChaseExitFar = 896f;

    /// <summary>The follower plans again once the belief has moved this far, or this long after its last plan.</summary>
    public const float ChaseReplanUnits = 256f, ChaseReplanSeconds = 1f;

    /// <summary>Within this of the exit it holds, the follower stands and watches the enemy's way.</summary>
    public const float ChaseHoldUnits = 96f;

    private readonly AiRandom _crossRng;
    private FlowField? _routeField, _altField;
    private NavObstacleState? _openDoors;
    private readonly HashSet<int> _corridor = [];
    private readonly List<int> _routeCells = [];
    private ActorId _crossOf = ActorId.None, _crossPartner = ActorId.None;
    private bool _crossing;
    private AiVector _crossVia;
    private CellId _exitFrom = CellId.None;
    private Tick _exitPlanned;
    private AiVector _exitAt;
    private bool _exitKnown;

    /// <summary>Crossfires begun (a partner on the path, the draw taken, another door found), for the facts and the jank run.</summary>
    public int Crossfires { get; private set; }

    /// <summary>Decides on which the bot held a chase's exit for a teammate who led.</summary>
    public int ChaseCovers { get; private set; }

    /// <summary>The way the bot is taking round to its enemy while crossing, for the facts.</summary>
    public AiVector CrossVia => _crossVia;

    /// <summary>
    /// The tree hunts an enemy out of sight and no role outranked it: a crossfire or a chase
    /// with a teammate may take the way there. Neither touches the target, the look at a fight
    /// or the fire gate; only where the bot walks to meet it.
    /// </summary>
    private bool RouteRoles()
    {
        if (_target is null || _targetSighted || _graph is null)
        {
            _crossOf = _crossPartner = ActorId.None;
            _crossing = false;
            return false;
        }

        if (Crossfire())
        {
            Role("crossfire", _crossVia, MoveSpeed.Run);
            return true;
        }

        if (Chase(out AiVector exit, out AiVector watch))
        {
            Role("chase exit", exit, _self.Origin.DistanceTo(exit) <= ChaseHoldUnits ? MoveSpeed.Walk : MoveSpeed.Run);
            _look = new LookIntent(LookKind.Point, watch + new AiVector(0f, 0f, PlaceHeight), _target.Id, AimPoint.Body, 0f, 0);
            ChaseCovers++;
            return true;
        }

        return false;
    }

    private void Role(string role, AiVector goal, MoveSpeed speed)
    {
        _teamRole = role;
        _leaf = role;
        _verdict = "team: " + role;
        GoTo(goal, speed, MoveKind.Follow);
        TeamRoleDecides++;
    }

    /// <summary>
    /// Crossfire (B.5): approaching a known enemy with a teammate ahead on the same way, the
    /// bot behind goes round by the other route so the two arrive from two doors, and says
    /// "going around". Drawn once per enemy with <c>crossfireWeight</c> (Teamwork); a
    /// partner who turns foe ends it at once (B.9).
    /// </summary>
    private bool Crossfire()
    {
        if (_crossing)
        {
            if (_crossOf != _target!.Id || _memory.Board.Of(_crossPartner, make: false) is not { } mate || !_memory.IsFriend(mate) || mate.Dead
                || _self.Origin.DistanceTo(_crossVia) < FlankArrive)
                _crossing = false;   // through the other door, or the pairing is gone: the hunt goes on as the tree has it
            return _crossing;
        }

        if (_crossOf == _target!.Id || _p.CrossfireWeight <= 0f || !Partner(out KnownActor? partner))
            return false;
        AiVector enemy = _target.BelievedAt(_graph);
        if (!ShortestWay(enemy, out float shortest, out CellId from) || !SamePathAhead(partner!, shortest))
            return false;
        // One draw per enemy once a partner is on the path, from the team layer's own stream.
        _crossOf = _target.Id;
        if (_crossRng.NextFloat() >= _p.CrossfireWeight || !OtherDoor(from, shortest, out _crossVia))
            return false;
        _crossing = true;
        _crossPartner = partner!.Id;
        Crossfires++;
        if (!_say.Says && _self.Alive)
            Say(CalloutKind.GoingAround, CalloutPlace(_crossVia), 0, _target.Id.Slot + 1);
        return true;
    }

    /// <summary>The teammate nearest the bot within <see cref="PartnerReach"/>, alive and seen lately enough to trust.</summary>
    private bool Partner(out KnownActor? partner)
    {
        partner = null;
        float nearest = PartnerReach;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead || !_memory.IsFriend(a) || !a.EverSeen || a.Confidence < 0.2f)
                continue;
            float d = _self.Origin.DistanceTo(a.LastSeenPos);
            if (d < nearest)
                (nearest, partner) = (d, a);
        }

        return partner is not null;
    }

    /// <summary>The shortest way from the bot to <paramref name="enemy"/> on the graph, its cells in <see cref="_routeCells"/>, the bot first.</summary>
    private bool ShortestWay(AiVector enemy, out float cost, out CellId from)
    {
        cost = float.PositiveInfinity;
        from = _graph!.FindCell(_self.Origin + new AiVector(0f, 0f, 1f));
        CellId to = _graph.FindCell(enemy + new AiVector(0f, 0f, 1f));
        if (!from.IsValid || !to.IsValid || from == to || _graph.ComponentOf(from.Index) != _graph.ComponentOf(to.Index))
            return false;
        _openDoors ??= new NavObstacleState(_graph.ObstacleCount);
        _routeField ??= new FlowField();
        _routeField.Build(_graph, to, ClearanceClass.Crouch, _openDoors);
        cost = _routeField.CostAt(from.Index);
        if (!float.IsFinite(cost))
            return false;
        Walk(_routeField, from, _routeCells);
        return true;
    }

    /// <summary>The cells <paramref name="field"/> walks from <paramref name="from"/> to its objective.</summary>
    private void Walk(FlowField field, CellId from, List<int> into)
    {
        into.Clear();
        int at = from.Index;
        for (int guard = 0; guard < _graph!.CellCount && at >= 0; guard++)
        {
            into.Add(at);
            CellId next = field.NextFrom(at);
            at = next.IsValid ? next.Index : -1;
        }
    }

    /// <summary>
    /// The partner stands on the bot's shortest way (within <see cref="SamePathReach"/> of a
    /// cell of it) and no farther from the enemy along it than the bot, so this bot is the one
    /// behind; level, the higher slot goes round. A partner who has just said it goes round leads nobody.
    /// </summary>
    private bool SamePathAhead(KnownActor partner, float shortest)
    {
        BotMemory.HeardCallout around = _memory.LastGoingAround;
        if (around.Kind == CalloutKind.GoingAround && around.Speaker == partner.Id && (_now - around.Tick) * _tick <= GoingAroundHeardSeconds)
            return false;
        AiVector at = partner.LastSeenPos;
        bool on = false;
        foreach (int c in _routeCells)
        {
            if (_graph!.Cell(c).Centre.DistanceTo(at) <= SamePathReach)
            {
                on = true;
                break;
            }
        }

        if (!on)
            return false;
        CellId cell = _graph!.FindCell(at + new AiVector(0f, 0f, 1f));
        float theirs = cell.IsValid ? _routeField!.CostAt(cell.Index) : float.PositiveInfinity;
        if (!float.IsFinite(theirs))
            return false;
        return theirs < shortest - 1f || (MathF.Abs(theirs - shortest) <= 1f && partner.Id.Slot < _bot.Slot);
    }

    /// <summary>
    /// The other door (B.5, over the routes and <see cref="ChokeMap"/>): the shortest way with
    /// the first one's corridor closed, no more than <see cref="CrossfireDetour"/> times as long;
    /// the bot heads for that way's strongest choke past its middle (the door it arrives by),
    /// else the cell at three quarters of it.
    /// </summary>
    private bool OtherDoor(CellId from, float shortest, out AiVector via)
    {
        via = default;
        AiVector self = _graph!.Cell(from).Centre;
        AiVector enemy = _graph.Cell(_routeField!.Objective).Centre;
        _corridor.Clear();
        var near = new List<int>();
        foreach (int c in _routeCells)
        {
            AiVector centre = _graph.Cell(c).Centre;
            if (centre.DistanceTo(self) <= CrossfireKeep || centre.DistanceTo(enemy) <= CrossfireKeep)
                continue;
            near.Clear();
            _graph.CellsNear(centre.X - CrossfireCorridor, centre.Y - CrossfireCorridor, centre.X + CrossfireCorridor, centre.Y + CrossfireCorridor, near);
            foreach (int n in near)
            {
                AiVector nc = _graph.Cell(n).Centre;
                if (nc.DistanceTo(centre) <= CrossfireCorridor && nc.DistanceTo(self) > CrossfireKeep && nc.DistanceTo(enemy) > CrossfireKeep)
                    _corridor.Add(n);
            }
        }

        if (_corridor.Count == 0)
            return false;
        _altField ??= new FlowField();
        _altField.Build(_graph, _routeField.Objective, ClearanceClass.Crouch, _openDoors!, _corridor);
        float other = _altField.CostAt(from.Index);
        if (!float.IsFinite(other) || other > CrossfireDetour * shortest)
            return false;
        var way = new List<int>();
        Walk(_altField, from, way);
        ChokeMap? chokes = _memory.Chokes;
        int pick = -1;
        float best = 0f;
        foreach (int c in way)
        {
            float left = _altField.CostAt(c);
            if (left > other * 0.5f || left < CrossfireKeep)
                continue;
            float score = chokes is not null && c < chokes.CellCount ? chokes.ScoreOf(c) : 0f;
            if (score > best)
                (best, pick) = (score, c);
        }

        if (pick < 0)
        {
            foreach (int c in way)
            {
                if (_altField.CostAt(c) <= other * 0.25f)
                {
                    pick = c;
                    break;
                }
            }
        }

        if (pick < 0)
            return false;
        via = _graph.Cell(pick).Centre;
        return true;
    }

    /// <summary>
    /// Chase with a partner (B.5): hunting with a teammate near, the one nearer the enemy's
    /// believed cell (the position predictor's) leads, and the other holds the way out on the
    /// enemy's far side from the leader: a cell <see cref="ChaseExitNear"/>-<see cref="ChaseExitFar"/>
    /// along the graph from the believed cell (the follower's flow field from it), watching the belief.
    /// </summary>
    private bool Chase(out AiVector exit, out AiVector watch)
    {
        exit = watch = default;
        if (_target!.Where is not { Active: true } belief || !belief.ArgMax.IsValid || !Partner(out KnownActor? leader))
            return false;
        CellId centre = belief.ArgMax;
        watch = _graph!.Cell(centre).Centre;
        float mine = _self.Origin.DistanceTo(watch), theirs = leader!.LastSeenPos.DistanceTo(watch);
        if (mine < theirs || (mine == theirs && _bot.Slot < leader.Id.Slot))
            return false;   // this bot is the nearer: it leads, by the tree's own hunt
        bool stale = !_exitKnown || _graph.Cell(_exitFrom).Centre.DistanceTo(watch) > ChaseReplanUnits || (_now - _exitPlanned) * _tick >= ChaseReplanSeconds;
        if (stale)
        {
            _exitFrom = centre;
            _exitPlanned = _now;
            _exitKnown = PlanExit(centre, leader.LastSeenPos, out _exitAt);
        }

        exit = _exitAt;
        return _exitKnown;
    }

    /// <summary>The way out of <paramref name="centre"/> most directly away from <paramref name="leader"/>, nearest the bot among those.</summary>
    private bool PlanExit(CellId centre, AiVector leader, out AiVector exit)
    {
        exit = default;
        _openDoors ??= new NavObstacleState(_graph!.ObstacleCount);
        _altField ??= new FlowField();
        _altField.Build(_graph!, centre, ClearanceClass.Crouch, _openDoors);
        AiVector c0 = _graph!.Cell(centre).Centre;
        AiVector away = new AiVector(c0.X - leader.X, c0.Y - leader.Y, 0f);
        float len = away.Length;
        if (len < 1f)
            return false;
        away *= 1f / len;
        float best = float.NegativeInfinity;
        for (int c = 0; c < _graph.CellCount; c++)
        {
            float cost = _altField.CostAt(c);
            if (cost < ChaseExitNear || cost > ChaseExitFar)
                continue;
            AiVector at = _graph.Cell(c).Centre;
            AiVector out1 = new AiVector(at.X - c0.X, at.Y - c0.Y, 0f);
            float ol = out1.Length;
            if (ol < 1f)
                continue;
            float along = (out1.X * away.X + out1.Y * away.Y) / ol;
            if (along < 0.5f)
                continue;
            float score = along - _self.Origin.DistanceTo(at) / 2048f;
            if (score > best)
                (best, exit) = (score, at);
        }

        return best > float.NegativeInfinity;
    }
}
