namespace SourceSharp.Ai.TF2.Trees;

/// <summary>A bot's stance on a control-point level (lane Gc): none off such a level, else the points or the team.</summary>
public enum TFStance : byte
{
    None = 0,
    Points,
    Team,
}

/// <summary>
/// A control-point level (lane Gc; the user's ruling of 2026-11-13: "bots should do both on control point maps, jump to
/// objectives some times and fight with team other times"). A level of control points with no flag and no course ends
/// (<see cref="ReadCourse"/>'s course, not a jump map's), where the bot's scoreboard names a teammate, is a points level:
/// a bot there holds one of two stances for a stretch, and changes between them.
///
/// <list type="bullet">
/// <item><b>The points</b> (<see cref="PointsBranch"/>): to an own point the HUD's feed says is being taken; else to an
/// own point the enemy may capture with an enemy the bot sees on it; else to the nearest point its team may capture now
/// (the Course branch's stage, by the route's blast links where they pay: <see cref="GoCourse"/> for a bot that rocket
/// jumps). On the point it stands. Not gated on the rocket jump: a bot that does not jump walks. A quiet own point is no
/// goal (the second pass: a bot does not stand on one while its team has a point to take).</item>
/// <item><b>The team</b> (<see cref="TeamBranch"/>): gather at a rally, then push (<see cref="ReadTeamPlay"/>, the
/// second pass). A lone wolf (Teamwork 2 or under: it neither heeds nor calls a rally): to a teammate's call for help heard within the bot's Teamwork's
/// <c>followSeconds</c>; else to the teammate furthest forward (nearest the point in play) of those whose place the bot
/// knows (in sight, or seen within <see cref="TeamSeenSeconds"/> at a place the bot has not come to without seeing it
/// there), or, when the bot is ahead of them all, back to the
/// nearest of them; within <see cref="GatherArriveUnits"/> it stays with them, and the Fight branch takes what it sees.
/// With no teammate's place known, where the team is fighting: a teammate's enemy callout heard within its
/// <c>followSeconds</c>, else a point in play whose capture is under way; with neither, the tree falls to roaming.</item>
/// </list>
///
/// <para>A stance is held for <see cref="StanceHoldMin"/> plus a draw up to <see cref="StanceHoldSpread"/>; then the bot
/// chooses again (<see cref="PointsChance"/>: half each, leaned by its Aggression over its Teamwork, and by how many of
/// the teammates it knows the place of are on a point in play). A bot in the team stance is pulled to the points at once
/// by a point in play within <see cref="PullReach"/> that matters now: its own being taken, its team's capture under
/// way, or an enemy it sees on its own point. A stage given up (<see cref="CannotGoOn"/>) sends it to the team.</para>
///
/// <para>A course plays as it did: a level with course ends, or a bot whose scoreboard names no teammate (our generated
/// courses run one bot), is no points level, and the Course branch runs as before, the rocket jump's gate and all.</para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>The two branches' names.</summary>
    public const string PointsBranch = "Points", TeamBranch = "Team";

    /// <summary>Each stance's branch weight: the Course branch's (under a fight in sight and Survive, over Defend, Search and Roam).</summary>
    public const float PointsWeight = CourseWeight, TeamWeight = CourseWeight;

    /// <summary>
    /// A stance is held at least this long, and up to this much longer by a draw: about a Soldier's run across half a map
    /// (240 u/s), so a stretch reaches a point or the team and does something there; the spread keeps a team's bots from
    /// all choosing at once.
    /// </summary>
    public const float StanceHoldMin = 30f, StanceHoldSpread = 30f;

    /// <summary>
    /// The chance of the points stance before anything leans it (half the time each); with half or more of the teammates
    /// the bot knows the place of on a point in play, at most <see cref="EnoughOnPoint"/>; never under
    /// <see cref="StanceLeast"/> nor over one less that, so each stance stays. (The first pass lifted it to 0.65 with none
    /// of them on a point, so that someone would go for it; since the third pass the team stance's pushes go for the
    /// point too, and that lift went.)
    /// </summary>
    public const float PointsShare = 0.5f, EnoughOnPoint = 0.25f, StanceLeast = 0.1f;

    /// <summary>A body this near a point's place is on it: about a capture area's half-diagonal (cp_gorge's two are 386 u by 258 to 386 u, 170 u tall).</summary>
    public const float OnPointUnits = 256f;

    /// <summary>A point in play this near pulls a bot in the team stance to the points: ten seconds of a Soldier's run; further, it comes after the capture is decided.</summary>
    public const float PullReach = 2400f;

    /// <summary>
    /// A teammate seen this long ago is still where it was seen, for the team stance: the role deal's
    /// <see cref="PersonSeenSeconds"/>; and a bot this near the team's place is with it.
    /// </summary>
    public const float TeamSeenSeconds = PersonSeenSeconds, GatherArriveUnits = 300f;

    /// <summary>
    /// Where teammates stand they keep this far apart (lane Gobj; the user, 2026-11-15: "and stand too close to each
    /// other"): over a rocket's splash (146 u) by about a body's width, so one blast takes one of them.
    /// </summary>
    public const float StandApartUnits = 200f;

    /// <summary>
    /// A stand spot is chosen within this of a point's place (inside its capture area: <see cref="OnPointUnits"/> is about
    /// its half-diagonal), or within this of a rally or a teammate (a gathering within <see cref="RallyGatherUnits"/>).
    /// </summary>
    public const float PointStandUnits = 192f, GroupStandUnits = 240f;

    /// <summary>A stand spot is kept this long, unless its place moves or a teammate comes within <see cref="StandApartUnits"/> of it; and the bot stands at it within this.</summary>
    public const float StandHoldSeconds = 4f, StandArriveUnits = 40f;

    private AiVector _standSpot, _standCentre;
    private Tick _standUntil;
    private bool _standKnown;
    private readonly List<AiVector> _matesAt = new();

    private AiRandom _pointsRng = null!;
    private bool _onPoints;
    private TFStance _stance;
    private Tick _stanceUntil;
    private Objective? _pointsGoal, _courseStage;
    private bool _pointsGoalOwn;
    private bool _teamPlaceKnown;
    private AiVector _teamPlace;
    private string _teamWhy = "";
    private int _matesPlaced, _matesOnPoint;
    private Tick _jumpSeen;

    /// <summary>The stance's own stream, drawn from the bot's seed, so the tree's draws are what they were.</summary>
    private void StartPoints(ulong seed) => _pointsRng = new AiRandom(seed ^ 0x5446_5054UL);

    /// <summary>Whether this decide's level is a points level for the bot (control points, no flag, no course ends, a teammate on its scoreboard).</summary>
    public bool OnPoints => _onPoints;

    /// <summary>The bot's stance; <see cref="TFStance.None"/> off a points level.</summary>
    public TFStance Stance => _stance;

    /// <summary>The goal the points stance made for this decide, or null.</summary>
    public Objective? PointsGoal => _pointsGoal;

    /// <summary>Where the team stance would go this decide, if anywhere.</summary>
    public AiVector? TeamPlace => _teamPlaceKnown ? _teamPlace : null;

    /// <summary>How often the stance changed (the first choice is none), and how often a pull made it.</summary>
    public int StanceChanges { get; private set; }

    public int StancePulls { get; private set; }

    /// <summary>Decides spent in each stance, and rocket jumps begun in each.</summary>
    public int PointsStanceDecides { get; private set; }

    public int TeamStanceDecides { get; private set; }

    public int PointsStanceJumps { get; private set; }

    public int TeamStanceJumps { get; private set; }

    /// <summary>The chance of the points the bot worked out at its last choice (<see cref="PointsChance"/>), for the facts.</summary>
    internal float LastPointsChance { get; private set; } = float.NaN;

    /// <summary>For the facts: the bot takes <paramref name="stance"/> for a stretch from now, as a choice would.</summary>
    internal void HoldStance(TFStance stance) => TakeStance(stance);

    /// <summary>The bot's own state at its last decide, for the measurement route.</summary>
    internal SelfState LastSelf => _self;

    internal string StanceName => _stance switch
    {
        TFStance.Points => "points",
        TFStance.Team => "team",
        _ => "none",
    };

    /// <summary>
    /// The chance a bot choosing its stance takes the points: <see cref="PointsShare"/>, times its Aggression's factor on
    /// going for an objective over its Teamwork's on going with a teammate; then, of the <paramref name="placed"/>
    /// teammates whose place it knows, half or more on a point in play caps it at <see cref="EnoughOnPoint"/>; between
    /// <see cref="StanceLeast"/> and one less that. Pure.
    /// </summary>
    public static float PointsChance(float fightDrive, float escortDrive, int placed, int onPoint)
    {
        float p = PointsShare * fightDrive / MathF.Max(escortDrive, 0.01f);
        if (placed > 0 && onPoint * 2 >= placed)
            p = MathF.Min(p, EnoughOnPoint);
        return Math.Clamp(p, StanceLeast, 1f - StanceLeast);
    }

    /// <summary>
    /// Every decide alive, after <see cref="ReadCourse"/>: whether the level is a points level for the bot, the points'
    /// goal (and the Course branch's stage with it), the team's place, and the stance: chosen when none or its stretch is
    /// over, pulled to the points, sent to the team when the stage was given up.
    /// </summary>
    private void ReadPoints()
    {
        NotePointsJump();
        NoteOpening();
        _onPoints = _onCourse && _ends.IsEmpty && KnowsTeammate();
        if (!_onPoints)
        {
            (_stance, _pointsGoal, _teamPlaceKnown, _play) = (TFStance.None, null, false, TFTeamPlay.None);
            return;
        }

        // A points level is never finished: with every point the team's, the points are the ones to hold.
        _courseDone = false;
        _courseStage = _stage;
        ReadPointsGoal();
        _stage = _pointsGoal;
        ReadTeamPlace();

        // While the stage is given up the points have no way, so the team; and no pull undoes that.
        bool quit = _now < _courseQuitUntil;
        // Gathering or pushing, the stretch holds and no pull moves the bot.
        bool playing = _stance == TFStance.Team && InTeamPlay;
        if (_stance == TFStance.None || (_now >= _stanceUntil && !playing))
        {
            LastPointsChance = PointsChance(_p.FightDrive, _p.EscortDrive, _matesPlaced, _matesOnPoint);
            TakeStance(_pointsRng.NextFloat() < LastPointsChance && !quit ? TFStance.Points : TFStance.Team);
        }
        else if (_stance == TFStance.Team && !quit && !playing && Pulled())
        {
            StancePulls++;
            TakeStance(TFStance.Points);
        }

        if (_stance == TFStance.Points && quit)
            TakeStance(TFStance.Team);
        if (_stance == TFStance.Points && JoinsRally())
        {
            RallyJoins++;
            TakeStance(TFStance.Team);
        }

        _play = TFTeamPlay.None;
        if (_stance == TFStance.Team)
            ReadTeamPlay();
        _cameBack = false;
        if (_stance == TFStance.Points)
            PointsStanceDecides++;
        else
            TeamStanceDecides++;
    }

    /// <summary>A stance for a new stretch; a change from another counts.</summary>
    private void TakeStance(TFStance stance)
    {
        if (_stance != TFStance.None && _stance != stance)
            StanceChanges++;
        _stance = stance;
        _toFront = false;
        _stanceUntil = _now + (int)((StanceHoldMin + _pointsRng.NextFloat() * StanceHoldSpread) / _tick);
    }

    /// <summary>A rocket jump begun since the last decide counts for the stance the bot held then.</summary>
    private void NotePointsJump()
    {
        if (_jumpBegan == _jumpSeen)
            return;
        _jumpSeen = _jumpBegan;
        if (_stance == TFStance.Points)
            PointsStanceJumps++;
        else if (_stance == TFStance.Team)
            TeamStanceJumps++;
        if (_play == TFTeamPlay.Push)
            PushJumps++;
    }

    private bool KnowsTeammate()
    {
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (a.Known && a.Id != _bot && _memory.IsFriend(a))
                return true;
        }

        return false;
    }

    /// <summary>The teams other than the bot's, as the feed's bits.</summary>
    private int EnemyBits => ~(1 << _self.Team);

    /// <summary>
    /// The points stance's goal: the nearest own point being taken; else the nearest own point the enemy may capture with
    /// an enemy the bot sees on it; else the Course branch's stage.
    /// </summary>
    private void ReadPointsGoal()
    {
        Objective? taken = null, contested = null;
        float takenD = float.PositiveInfinity, contestedD = float.PositiveInfinity;
        foreach (Objective o in _view.Objectives!.All)
        {
            if (o.Kind != ObjectiveKind.Capture || o.OwnerTeam != _self.Team)
                continue;
            float d = _self.Origin.DistanceTo(o.Position);
            if (o.State == ObjectiveState.Capturing && d < takenD)
                (takenD, taken) = (d, o);
            if ((o.MayAct & EnemyBits) == 0 || o.State == ObjectiveState.Locked)
                continue;
            if (EnemyOn(o.Position) && d < contestedD)
                (contestedD, contested) = (d, o);
        }

        _pointsGoal = taken ?? contested ?? _courseStage;
        _pointsGoalOwn = _pointsGoal is { } g && g.OwnerTeam == _self.Team;
    }

    /// <summary>Whether the bot sees an enemy within <see cref="OnPointUnits"/> of <paramref name="at"/>.</summary>
    private bool EnemyOn(AiVector at)
    {
        foreach (ref readonly ActorFacts f in _view.Sighted)
        {
            if (!TeamRule.Friends(_self.Team, f.Team) && f.Origin.DistanceTo(at) <= OnPointUnits)
                return true;
        }

        return false;
    }

    /// <summary>Whether a point in play within <see cref="PullReach"/> matters now: an own one being taken or with an enemy seen on it, or the team's capture under way.</summary>
    private bool Pulled()
    {
        int mine = 1 << _self.Team;
        foreach (Objective o in _view.Objectives!.All)
        {
            if (o.Kind != ObjectiveKind.Capture || _self.Origin.DistanceTo(o.Position) > PullReach)
                continue;
            bool own = o.OwnerTeam == _self.Team;
            if (own && o.State == ObjectiveState.Capturing)
                return true;
            if (!own && (o.MayAct & mine) != 0 && o.State == ObjectiveState.Capturing)
                return true;
            if (own && (o.MayAct & EnemyBits) != 0 && o.State != ObjectiveState.Locked && EnemyOn(o.Position))
                return true;
        }

        return false;
    }

    /// <summary>Whether <paramref name="at"/> is on a point in play: one the team may capture now, or an own one the enemy may.</summary>
    private bool OnPointInPlay(AiVector at)
    {
        int mine = 1 << _self.Team;
        foreach (Objective o in _view.Objectives!.All)
        {
            if (o.Kind != ObjectiveKind.Capture || o.State == ObjectiveState.Locked || at.DistanceTo(o.Position) > OnPointUnits)
                continue;
            if (o.OwnerTeam == _self.Team ? (o.MayAct & EnemyBits) != 0 : (o.MayAct & mine) != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Where the team stance goes: a teammate's call for help heard within the bot's <c>followSeconds</c>; else the
    /// teammate whose place the bot knows that is nearest the point in play (the points' goal), or with no point in play
    /// the nearest; and back to the nearest when the bot is ahead of them all. Counts the teammates placed and on a point.
    /// </summary>
    private void ReadTeamPlace()
    {
        (_teamPlaceKnown, _matesPlaced, _matesOnPoint, _teamWhy) = (false, 0, 0, "");
        AiVector? front = _pointsGoal?.Position;
        float bestFront = float.PositiveInfinity, bestNear = float.PositiveInfinity;
        AiVector frontAt = default, nearAt = default;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead || a.Id == _bot || !_memory.IsFriend(a))
                continue;
            AiVector at;
            if (_view.TryActor(a.Id, out ActorFacts drawn))
                at = drawn.Origin;
            else if (a.EverSeen && (_now - a.LastSeenTick) * _tick < TeamSeenSeconds && _self.Origin.DistanceTo(a.LastSeenPos) >= GatherArriveUnits)
                at = a.LastSeenPos;
            else
                continue;
            _matesPlaced++;
            if (OnPointInPlay(at))
                _matesOnPoint++;
            float near = _self.Origin.DistanceTo(at);
            if (near < bestNear)
                (bestNear, nearAt) = (near, at);
            float forward = front is { } p ? at.DistanceTo(p) : near;
            if (forward < bestFront)
                (bestFront, frontAt) = (forward, at);
        }

        BotMemory.HeardCallout call = _memory.LastHelpCall;
        if (call.Kind == CalloutKind.Help && call.Speaker != _bot && (_now - call.Tick) * _tick < _p.FollowSeconds)
        {
            (_teamPlaceKnown, _teamPlace, _teamWhy) = (true, call.Place, "to the call");
            return;
        }

        if (_matesPlaced == 0)
        {
            TeamFightPlace();
            return;
        }

        bool ahead = front is { } f && _self.Origin.DistanceTo(f) < bestFront;
        (_teamPlaceKnown, _teamPlace, _teamWhy) = ahead ? (true, nearAt, "back to the team") : (true, frontAt, "to the team");
    }

    /// <summary>
    /// With no teammate's place known, where the team is fighting as the bot hears or reads it: a teammate's enemy callout
    /// heard within the bot's <c>followSeconds</c>; else a point in play whose capture is under way (the HUD's), the nearest.
    /// </summary>
    private void TeamFightPlace()
    {
        BotMemory.HeardCallout enemy = _memory.LastEnemyCall;
        if (enemy.Kind == CalloutKind.Enemy && enemy.Speaker != _bot && (_now - enemy.Tick) * _tick < _p.FollowSeconds)
        {
            (_teamPlaceKnown, _teamPlace, _teamWhy) = (true, enemy.Place, "to the fight called");
            return;
        }

        float best = float.PositiveInfinity;
        foreach (Objective o in _view.Objectives!.All)
        {
            if (o.Kind != ObjectiveKind.Capture || o.State != ObjectiveState.Capturing || !OnPointInPlay(o.Position))
                continue;
            float d = _self.Origin.DistanceTo(o.Position);
            if (d < best)
                (best, _teamPlace, _teamPlaceKnown, _teamWhy) = (d, o.Position, true, "to the fight at the point");
        }
    }

    private float PointsUtility() => _onPoints && _stance == TFStance.Points && _pointsGoal is not null ? PointsWeight : 0f;

    /// <summary>The team's weight: in the team stance gathering or pushing, or with a teammate's place to go to; or in the points stance with no point to go to and such a place.</summary>
    private float TeamUtility()
        => _onPoints && ((_stance == TFStance.Team && (_play != TFTeamPlay.None || _teamPlaceKnown || RallyComing)) || (_pointsGoal is null && _teamPlaceKnown)) ? TeamWeight : 0f;

    /// <summary>To the points' goal, and on it, stand: by the Course branch's way for a bot that rocket jumps, else on foot.</summary>
    private bool GoPoints()
    {
        if (_pointsGoal is not { } goal)
            return false;
        if (_self.Origin.DistanceTo(goal.Position) < ArriveUnits + PointStandUnits)
        {
            StandApart(goal.Position, PointStandUnits);
            _verdict = _pointsGoalOwn ? "holding our point" : "on the point";
            return true;
        }

        if (MayRocketJump)
        {
            bool going = GoCourse();
            if (going && _pointsGoalOwn && _verdict is "to the next stage" or "taking the stage")
                _verdict = "to our point";
            return going;
        }

        GoTo(goal.Position, MoveSpeed.Run);
        _verdict = _pointsGoalOwn ? "to our point" : "to the point";
        return true;
    }

    /// <summary>To the team's place, and with it, stay.</summary>
    private bool GoTeam()
    {
        if (_stance == TFStance.Team && _play != TFTeamPlay.None)
            return GoTeamPlay();
        if (RallyComing)
            return GoToRallyLate();
        if (!_teamPlaceKnown)
            return false;
        if (_self.Origin.DistanceTo(_teamPlace) < GatherArriveUnits)
        {
            StandApart(_teamPlace, GroupStandUnits);
            _verdict = "with the team";
            return true;
        }

        GoTo(_teamPlace, MoveSpeed.Run);
        _verdict = _teamWhy;
        return true;
    }

    /// <summary>
    /// Stands at <paramref name="centre"/> apart from its teammates (<see cref="StandSpot"/>): to the spot, and there, holds.
    /// The bot's goal is the spot either way.
    /// </summary>
    private void StandApart(AiVector centre, float radius)
    {
        AiVector spot = StandSpot(centre, radius);
        if (Flat(_self.Origin, spot) <= StandArriveUnits)
            (_hasGoal, _goal) = (true, spot);
        else
            GoTo(spot, MoveSpeed.Run);
    }

    /// <summary>
    /// Where the bot stands at <paramref name="centre"/> (a point, a rally, a teammate): of where it stands (within
    /// <paramref name="radius"/> of the centre), the centre and the walkable cells on two rings within the radius of it
    /// (twelve ways round each), the nearest to the bot of those at
    /// least <see cref="StandApartUnits"/> from every teammate it sees or saw within <see cref="RallySeenSeconds"/>; with none
    /// that far, the one furthest from them. Kept <see cref="StandHoldSeconds"/>, unless the centre moves or a teammate comes
    /// within <see cref="StandApartUnits"/> of it.
    /// </summary>
    private AiVector StandSpot(AiVector centre, float radius)
    {
        MatesAt();
        if (_standKnown && Flat(centre, _standCentre) <= 1f && _now < _standUntil && ApartFromMates(_standSpot) >= StandApartUnits)
            return _standSpot;
        // Where the bot stands is a spot too, the nearest of all: a bot already apart stays.
        AiVector best = centre;
        float bestScore = float.NegativeInfinity;
        if (Flat(_self.Origin, centre) <= radius)
            (best, bestScore) = (_self.Origin, MathF.Min(ApartFromMates(_self.Origin), StandApartUnits) * 1000f);
        for (int ring = 0; ring <= 2; ring++)
        {
            int ways = ring == 0 ? 1 : 12;
            for (int k = 0; k < ways; k++)
            {
                float r = radius * ring * 0.5f, angle = k * (MathF.PI / 6f);
                AiVector want = centre + new AiVector(MathF.Cos(angle) * r, MathF.Sin(angle) * r, 0f);
                if (Snap(centre, want) is not { } at || Flat(at, centre) > radius + StandArriveUnits)
                    continue;
                // Apart first (enough is enough), then the nearest.
                float score = MathF.Min(ApartFromMates(at), StandApartUnits) * 1000f - Flat(_self.Origin, at);
                if (score > bestScore)
                    (bestScore, best) = (score, at);
            }
        }

        (_standSpot, _standCentre, _standKnown, _standUntil) = (best, centre, true, _now + (int)(StandHoldSeconds / _tick));
        return best;
    }

    /// <summary>The teammates' places the bot knows now: in sight, or seen within <see cref="RallySeenSeconds"/>.</summary>
    private void MatesAt()
    {
        _matesAt.Clear();
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead || a.Id == _bot || !_memory.IsFriend(a))
                continue;
            if (_view.TryActor(a.Id, out ActorFacts drawn))
                _matesAt.Add(drawn.Origin);
            else if (a.EverSeen && (_now - a.LastSeenTick) * _tick <= RallySeenSeconds)
                _matesAt.Add(a.LastSeenPos);
        }
    }

    /// <summary>How far <paramref name="at"/> is from the nearest teammate in <see cref="_matesAt"/>, on the ground plane.</summary>
    private float ApartFromMates(AiVector at)
    {
        float least = float.PositiveInfinity;
        foreach (AiVector m in _matesAt)
            least = MathF.Min(least, Flat(at, m));
        return least;
    }
}
