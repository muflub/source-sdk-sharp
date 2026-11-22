namespace SourceSharp.Ai.TF2.Trees;

/// <summary>What a bot in the team stance is doing with its team (lane Gc's second pass): nothing called, gathering at a rally, or in a push.</summary>
public enum TFTeamPlay : byte
{
    None = 0,
    Gather,
    Push,
}

/// <summary>
/// The team stance as gather, then push (lane Gc's second pass; the main plan's C.6.1 and B.5, ruled 2026-11-02, as the
/// team stance's body: the no-progress trigger, the Medic's charge and humans' pushes are not built here).
///
/// <list type="bullet">
/// <item><b>Gather.</b> A bot in the team stance goes to a rally: the one a teammate called (heard, as every callout is,
/// through the team layer's delay), coming <see cref="RallyLatePerTeamwork"/> a point of Teamwork under the cap late and
/// not at all at Teamwork <see cref="RallyIgnoreTeamwork"/> or under; with none, one it calls itself
/// (<see cref="CalloutKind.Rally"/>; any bot that heeds a rally calls one, see <see cref="RallyIgnoreTeamwork"/>) at <see cref="RallySpot"/>:
/// at its team's front (the one of itself and the teammates it can place nearest the point it would push,
/// <see cref="TeamFront"/>; lane Gobj, where the third pass took their middle), and only with that front within
/// <see cref="RallyFrontUnits"/> of the point (further, the bot goes to the front instead: "to the front"), moved toward
/// the point only as far as stays out of every enemy's last-seen sight, at most <see cref="RallyAdvanceUnits"/> and never
/// within <see cref="RallyShortUnits"/> of the point (the third pass), held back to it from a front nearer. A bot
/// that places no teammate calls none: it has no team to gather, and takes the points once within the front's reach of the
/// point (further, it goes to the front as a team would). Two rallies called within
/// <see cref="RallyClashSeconds"/> of each other: the lower slot's. On its way there it keeps with its group as a
/// pusher does (<see cref="TeammateFarBehind"/>, <see cref="PushWaitMostSeconds"/> a rally at most); at the rally it
/// waits, and the Fight branch takes what it sees.</item>
/// <item><b>Push.</b> The caller calls the push (<see cref="CalloutKind.Push"/>) when half its team's players (at least
/// <see cref="RallyLeast"/>) stand within <see cref="RallyGatherUnits"/> of the rally, itself and the teammates it sees there or saw within <see cref="RallySeenSeconds"/>,
/// or when <see cref="RallyTimeoutSeconds"/> (± <see cref="RallyJitterSeconds"/>, a draw of the rally's place and team)
/// have passed after the caller's own way there (<see cref="RallyTime"/>: the seconds it said with the call, its
/// distance at <see cref="RallyRunSpeed"/>, at most <see cref="RallyTravelMostSeconds"/>). A bot that hears its rally's push, or any push within <see cref="PushJoinSeconds"/> and
/// <see cref="PushJoinUnits"/> of its place, pushes; one whose caller never calls it pushes <see cref="RallyGraceSeconds"/> after the
/// time. A push goes for the point the team may capture (else an own point being taken), a rocket jumper by the Course
/// branch's way (the route's links and the heal rule, the third pass), and holds until the
/// point is taken, lost, or the bot dies, or <see cref="PushMostSeconds"/>. Unlike the points stance it never turns back
/// to defend (no other goal, no pull) and it keeps with the others: short of <see cref="PushCommitUnits"/> from the
/// point, a pusher whose nearest teammate it knows within <see cref="PushGroupUnits"/> is more than
/// <see cref="PushKeepUnits"/> behind it waits for them, <see cref="PushWaitMostSeconds"/> a push at most.</item>
/// <item><b>Joining from the points.</b> A bot in the points stance that heeds rallies (Teamwork over
/// <see cref="RallyIgnoreTeamwork"/>) and holds a teammate's, is not on a point in play nor going to an own point being
/// taken, and has the rally nearer than its point and within <see cref="RallyJoinReach"/>, joins it: it takes the team
/// stance. A bot about to take its point keeps on.</item>
/// </list>
///
/// <para>While a bot gathers or pushes its stretch does not end and no pull moves it. When a push is over (taken, lost,
/// its time) the bot takes the points for its next stretch: it has been with its team, and the objective is next. A
/// pusher that dies keeps its stance; back from the dead it joins its team's push it heard within
/// <see cref="PushMostSeconds"/>, from its spawn, and drops a rally whose time has passed. A bot with no rally to join and too little Teamwork to call one goes with the team as before
/// (<see cref="ReadTeamPlace"/>).</para>
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>
    /// Rally (C.6.1): the most Teamwork that ignores one, and how late a bot comes per point of Teamwork under the cap. In
    /// the team stance a bot over <see cref="RallyIgnoreTeamwork"/> also calls one: C.6.1's call at Teamwork 4 is the
    /// no-progress call's, and the team stance is already chosen by Teamwork (a preset's Teamwork is its mean level:
    /// "normal" bots are 3).
    /// </summary>
    public const float RallyIgnoreTeamwork = 2f, RallyLatePerTeamwork = 1f;

    /// <summary>A rally pushes with half the team's players within <see cref="RallyGatherUnits"/>, and never fewer than this.</summary>
    public const int RallyLeast = 2;

    public const float RallyGatherUnits = 384f;

    /// <summary>A teammate seen at the rally this lately still counts as there: a bot waiting looks about, and its eyes see 90°.</summary>
    public const float RallySeenSeconds = 3f;

    /// <summary>A rally's time (C.6.1: 25 s), the draw on it (± 10 s), and how long after it a bot whose caller called no push pushes itself.</summary>
    public const float RallyTimeoutSeconds = 25f, RallyJitterSeconds = 10f, RallyGraceSeconds = 5f;

    /// <summary>
    /// A rally's time counts after its caller's way there: the caller says how many seconds that is with the call (the
    /// callout's count, as a player says "in twenty"), its distance at a Soldier's run, at most this many.
    /// </summary>
    public const float RallyRunSpeed = 240f, RallyTravelMostSeconds = 40f;

    /// <summary>Two rallies called this near in time are one clash, the lower slot's kept.</summary>
    public const float RallyClashSeconds = 3f;

    /// <summary>
    /// A rally is called at the team's middle, moved toward the point by steps of <see cref="RallyAdvanceStep"/> while no
    /// enemy's last-seen place sees the spot, at most this far, and never within <see cref="RallyShortUnits"/> of the point
    /// (a gathering is not a fight on the point).
    /// </summary>
    public const float RallyAdvanceUnits = 600f, RallyAdvanceStep = 150f, RallyShortUnits = 900f;

    /// <summary>
    /// A rally is called only near the front (lane Gobj): with the team's front (the one of the bot and the teammates it
    /// can place nearest the point it would push, <see cref="TeamFront"/>) within this of the point. Further, the team is
    /// still on its way from its spawn or far behind, and a gathering there waits too far back (cp_granary, the base: a
    /// rally called 1,200 u behind the teammates in front, a team 6,000 u from the point gathering by its spawn); the team
    /// stance goes to the front instead, and calls its rally there. About two thirds of a 5-point map's point-to-point
    /// distance (cp_granary's 2,950 u), and over <see cref="RallyShortUnits"/> and a step.
    /// </summary>
    public const float RallyFrontUnits = 2000f;

    /// <summary>A rally's place is the nearest walkable cell within this of the line's spot.</summary>
    public const float RallySnapUnits = 256f;

    /// <summary>A bot in the points stance joins a rally this near (five seconds of a Soldier's run; the second pass's 2,400 u and its "on the way" pulled most of the points stance in).</summary>
    public const float RallyJoinReach = 1200f;

    /// <summary>A push heard this lately is joined (B.5's HL2MP push lasts 12 s).</summary>
    public const float PushJoinSeconds = 12f;

    /// <summary>A push ends after this long at the most: a Soldier's run across the map and a capture.</summary>
    public const float PushMostSeconds = 60f;

    /// <summary>A pusher waits for a teammate more than <see cref="PushKeepUnits"/> behind, not within <see cref="PushCommitUnits"/> of the point.</summary>
    public const float PushKeepUnits = 500f, PushCommitUnits = 800f, PushWaitMostSeconds = 15f;

    /// <summary>A pusher keeps with teammates it knows within this (in sight, or seen within <see cref="RallySeenSeconds"/>): its group, not the far side of the map.</summary>
    public const float PushGroupUnits = 1500f;

    /// <summary>A push heard is joined from within this of its place (twice the rally's reach), or by a bot holding its rally.</summary>
    public const float PushJoinUnits = 2f * RallyGatherUnits;

    private BotMemory.HeardCallout _rally, _rallyLastSeen;
    private bool _rallyHeld;
    private Tick _rallyComeAt;
    private bool _pushing, _pushOwnPoint;
    private int _pushTarget;
    private Tick _pushSince, _pushCall, _pushLastDecide;
    private int _pushWaitedTicks, _gatherWaitedTicks;
    private Tick _gatherLastDecide;
    private TFTeamPlay _play;
    private SayIntent _say;
    private bool _cameBack;

    /// <summary>What the bot in the team stance is doing with its team this decide.</summary>
    public TFTeamPlay TeamPlay => _play;

    /// <summary>The rally the bot holds (its own or a teammate's), or null.</summary>
    public AiVector? RallyPlace => _rallyHeld ? _rally.Place : null;

    /// <summary>The rally's tick and caller's slot, and the push call's tick (the push the bot is in): for the measurement route to group them.</summary>
    internal (int Tick, int Caller) RallyId => _rallyHeld ? (_rally.Tick.Value, _rally.Speaker.Slot) : (0, -1);

    internal int PushCallTick => _pushing ? _pushCall.Value : 0;

    /// <summary>The objective a push goes for, or -1.</summary>
    public int PushTarget => _pushing ? _pushTarget : -1;

    /// <summary>Rallies the bot called, rallies it joined from the points stance, pushes it called, pushes it began, and how they ended.</summary>
    public int RalliesCalled { get; private set; }

    public int RallyJoins { get; private set; }

    public int PushesCalled { get; private set; }

    public int PushesBegun { get; private set; }

    public int PushesTaken { get; private set; }

    public int PushesLost { get; private set; }

    public int PushesTimedOut { get; private set; }

    /// <summary>Lane Gobj: decides the team stance went to the front because its team's front was further than <see cref="RallyFrontUnits"/> from the point.</summary>
    public int ToFrontDecides { get; private set; }

    /// <summary>Third pass: rallies not called for want of a teammate to gather (the bot took the points), stretches of the points taken after a push, pushes joined back from the dead, and rocket jumps begun in a push.</summary>
    public int AloneToPoints { get; private set; }

    public int PointsAfterPush { get; private set; }

    public int PushRejoins { get; private set; }

    public int PushJumps { get; private set; }

    /// <summary>
    /// Whether the bot is gathering, pushing or on its way to the front to call a rally (lane Gobj: without the last a
    /// stretch ran out on the way, and the team stance's share fell to 0.27 and 0.30 of bot-seconds on cp_granary): its
    /// stretch holds and no pull moves it.
    /// </summary>
    private bool InTeamPlay => _pushing || (_rallyHeld && RallyLive(_rally)) || _toFront;

    private bool _toFront;

    /// <summary>
    /// The team stance's body each decide: a push goes on until it is over; else a push heard joins it; else the rally
    /// held gathers, and pushes when the caller says or its time is past; else one is called.
    /// </summary>
    private void ReadTeamPlay()
    {
        _play = TFTeamPlay.None;
        _toFront = false;
        HearRally();
        if (_cameBack && CameBack())
            return;
        if (_pushing && PushOver())
        {
            // Over: the stance is chosen again at the next decide, before anything new is called.
            EndPush();
            return;
        }

        if (_pushing)
        {
            _play = TFTeamPlay.Push;
            return;
        }

        BotMemory.HeardCallout push = _memory.LastPush;
        bool pushHeard = push.Kind == CalloutKind.Push && push.Speaker != _bot && _p.Teamwork > RallyIgnoreTeamwork
                         && (_now - push.Tick) * _tick <= PushJoinSeconds && push.Tick > _pushCall;
        if (pushHeard && (ForMyRally(push) || _self.Origin.DistanceTo(push.Place) <= PushJoinUnits) && BeginPush(push.Tick))
            return;

        if (_rallyHeld && !RallyLive(_rally))
            _rallyHeld = false;
        if (_rallyHeld)
        {
            float since = (_now - _rally.Tick) * _tick, timeout = RallyTime(_rally);
            if (_rally.Speaker == _bot && (Gathered(_rally.Place) >= RallyNeeded() || since >= timeout))
            {
                if (PushGoal() is not null)
                {
                    Say(CalloutKind.Push, _rally.Place);
                    _memory.Said(CalloutKind.Push, _rally.Place, _now);
                    PushesCalled++;
                    BeginPush(_now);
                    return;
                }
            }
            else if (_rally.Speaker != _bot && since >= timeout + RallyGraceSeconds && BeginPush(_now))
                return;

            if (_now >= _rallyComeAt)
                _play = TFTeamPlay.Gather;
            return;
        }

        if (_p.Teamwork > RallyIgnoreTeamwork && PushGoal() is { } goal)
        {
            AiVector front = TeamFront(goal.Position, out int placed);
            // Far from the point: no rally here; to the front, where one is called. Alone too (the team, wherever it is, is
            // going there): when going to the front needed a teammate placed at every decide, one decide with nobody in
            // view or seen lately sent the bot to the points, and the team stance's share fell to 0.24 to 0.30 on cp_granary.
            // Not alone while the Course branch has given the stage up: the bot has no way to the point.
            if (Flat(front, goal.Position) > RallyFrontUnits && (placed > 1 || _now >= _courseQuitUntil))
            {
                ToFrontDecides++;
                _toFront = true;
                (_teamPlaceKnown, _teamPlace, _teamWhy) = (true, goal.Position, "to the front");
                return;
            }

            if (placed > 1)
            {
                CallRally(goal, front);
                return;
            }

            // Alone: no team to gather; the points. Not while the Course branch has given the stage up: that sends the
            // bot to the team, and the points would send it back at once.
            if (_now >= _courseQuitUntil)
            {
                AloneToPoints++;
                TakeStance(TFStance.Points);
            }
        }
    }

    /// <summary>
    /// The first decide alive after a death: a rally whose time has passed is dropped (not walked to from the spawn); a
    /// push of the team heard within <see cref="PushMostSeconds"/> (the one the bot died in among them) is joined from here.
    /// </summary>
    private bool CameBack()
    {
        _cameBack = false;
        if (_rallyHeld && (_now - _rally.Tick) * _tick >= RallyTime(_rally))
            _rallyHeld = false;
        BotMemory.HeardCallout push = _memory.LastPush;
        if (push.Kind != CalloutKind.Push || _p.Teamwork <= RallyIgnoreTeamwork || (_now - push.Tick) * _tick > PushMostSeconds)
            return false;
        if (!BeginPush(push.Tick))
            return false;
        PushRejoins++;
        return true;
    }

    /// <summary>
    /// The team's front for <paramref name="point"/> (lane Gobj): of the bot and the teammates it can place as the team
    /// stance does (<see cref="ReadTeamPlace"/>: in sight, or seen within <see cref="TeamSeenSeconds"/> at a place the bot
    /// has not come to without seeing it there; <paramref name="placed"/> of them, itself counted), the one nearest the
    /// point on the ground plane.
    /// </summary>
    private AiVector TeamFront(AiVector point, out int placed)
    {
        AiVector best = _self.Origin;
        float bestD = Flat(best, point);
        int n = 1;
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
            n++;
            float d = Flat(at, point);
            if (d < bestD)
                (bestD, best) = (d, at);
        }

        placed = n;
        return best;
    }

    /// <summary>The distance on the ground plane.</summary>
    private static float Flat(AiVector a, AiVector b) => new AiVector(a.X - b.X, a.Y - b.Y, 0f).Length;

    /// <summary>
    /// A rally heard (or said) since the last decide is held when the bot holds none live, or when it clashes with the one
    /// held (called within <see cref="RallyClashSeconds"/>) from a lower slot. A teammate's comes late by the bot's
    /// Teamwork, and a bot of too little Teamwork holds none of theirs.
    /// </summary>
    private void HearRally()
    {
        BotMemory.HeardCallout r = _memory.LastRally;
        if (r.Kind != CalloutKind.Rally || (r.Tick == _rallyLastSeen.Tick && r.Speaker == _rallyLastSeen.Speaker))
            return;
        _rallyLastSeen = r;
        if (r.Speaker != _bot && _p.Teamwork <= RallyIgnoreTeamwork)
            return;
        bool clash = _rallyHeld && MathF.Abs((r.Tick - _rally.Tick) * _tick) <= RallyClashSeconds;
        if (_rallyHeld && RallyLive(_rally) && !(clash && r.Speaker.Slot < _rally.Speaker.Slot))
            return;
        Hold(r);
    }

    private void Hold(in BotMemory.HeardCallout rally)
    {
        (_rally, _rallyHeld, _gatherWaitedTicks, _gatherLastDecide) = (rally, true, 0, _now);
        float late = rally.Speaker == _bot ? 0f : MathF.Max(0f, StandardSkills.Cap - _p.Teamwork) * RallyLatePerTeamwork;
        _rallyComeAt = rally.Tick + (int)(late / _tick);
    }

    /// <summary>A rally not yet pushed and within its time, the draw and twice the grace.</summary>
    private bool RallyLive(in BotMemory.HeardCallout r)
    {
        if (r.Kind != CalloutKind.Rally)
            return false;
        BotMemory.HeardCallout push = _memory.LastPush;
        if (push.Kind == CalloutKind.Push && push.Tick >= r.Tick && (push.Speaker == r.Speaker || push.Place.DistanceTo(r.Place) <= RallyGatherUnits))
            return false;
        return (_now - r.Tick) * _tick <= RallyTime(r) + 2f * RallyGraceSeconds;
    }

    /// <summary>Whether <paramref name="push"/> is the push of the rally the bot holds: its caller's, or called at its place.</summary>
    private bool ForMyRally(in BotMemory.HeardCallout push)
        => _rallyHeld && push.Tick >= _rally.Tick && (push.Speaker == _rally.Speaker || push.Place.DistanceTo(_rally.Place) <= RallyGatherUnits);

    /// <summary>Half the team's players the bot's scoreboard names, itself among them, and never under <see cref="RallyLeast"/>.</summary>
    private int RallyNeeded()
    {
        int team = 1;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (a.Known && a.Id != _bot && _memory.IsFriend(a))
                team++;
        }

        return Math.Max(RallyLeast, (team + 1) / 2);
    }

    /// <summary>
    /// How many of the team stand within <see cref="RallyGatherUnits"/> of <paramref name="place"/>: the bot, and the
    /// teammates it sees there or saw there within <see cref="RallySeenSeconds"/>.
    /// </summary>
    private int Gathered(AiVector place)
    {
        int n = _self.Origin.DistanceTo(place) <= RallyGatherUnits ? 1 : 0;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead || a.Id == _bot || !_memory.IsFriend(a))
                continue;
            AiVector at;
            if (_view.TryActor(a.Id, out ActorFacts drawn))
                at = drawn.Origin;
            else if (a.EverSeen && (_now - a.LastSeenTick) * _tick <= RallySeenSeconds)
                at = a.LastSeenPos;
            else
                continue;
            if (at.DistanceTo(place) <= RallyGatherUnits)
                n++;
        }

        return n;
    }

    /// <summary>The seconds after its call a rally pushes on time: the caller's way there, then <see cref="RallyTimeoutSeconds"/> and the draw.</summary>
    private float RallyTime(in BotMemory.HeardCallout r)
        => MathF.Min(r.Count, RallyTravelMostSeconds) + RallyTimeoutSeconds + Jitter(r.Place);

    /// <summary>
    /// ± <see cref="RallyJitterSeconds"/> drawn from the rally's place and the team (C.6.1: so it does not look scripted):
    /// every bot of the team draws the same, the caller (who hears its words the tick it says them) and those who hear them later.
    /// </summary>
    private float Jitter(AiVector place)
    {
        ulong h = (ulong)(uint)(int)MathF.Round(place.X) * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)(int)MathF.Round(place.Y) * 0xBF58476D1CE4E5B9UL
                  ^ (ulong)(uint)_self.Team * 0xC2B2AE3D27D4EB4FUL;
        h ^= h >> 29;
        return ((h % 2001UL) / 1000f - 1f) * RallyJitterSeconds;
    }

    /// <summary>What a push goes for: the point the team may capture now (the Course branch's stage), else the nearest own point being taken.</summary>
    private Objective? PushGoal()
    {
        if (_courseStage is { } stage)
            return stage;
        Objective? taken = null;
        float best = float.PositiveInfinity;
        foreach (Objective o in _view.Objectives!.All)
        {
            if (o.Kind != ObjectiveKind.Capture || o.OwnerTeam != _self.Team || o.State != ObjectiveState.Capturing)
                continue;
            float d = _self.Origin.DistanceTo(o.Position);
            if (d < best)
                (best, taken) = (d, o);
        }

        return taken;
    }

    private void CallRally(Objective goal, AiVector front)
    {
        AiVector place = RallySpot(goal.Position, front);
        int travel = (int)MathF.Ceiling(MathF.Min(_self.Origin.DistanceTo(place) / RallyRunSpeed, RallyTravelMostSeconds));
        Say(CalloutKind.Rally, place, travel);
        _memory.Said(CalloutKind.Rally, place, _now);
        // The caller's own words, as they are heard: with the seconds it said.
        _rallyLastSeen = _memory.LastRally with { Count = travel };
        Hold(_rallyLastSeen);
        RalliesCalled++;
        _play = TFTeamPlay.Gather;
    }

    /// <summary>Says <paramref name="kind"/> this decide, after the bot's own callout delay, with a team chat line for the humans on its team.</summary>
    private void Say(CalloutKind kind, AiVector place, int count = 0)
    {
        int delay = Math.Max(AiSession.CalloutMinDelayTicks, (int)MathF.Ceiling(_p.CalloutDelayMs / 1000f / _tick));
        _say = new SayIntent(kind, place, count, 0, delay, _self.Team, Text: true);
    }

    /// <summary>
    /// Where a rally is called: at the team's <paramref name="front"/> (lane Gobj; the third pass called it at the team's
    /// middle, behind its front), held back to <see cref="RallyShortUnits"/> from the point when the front is nearer it,
    /// on a walkable cell of its piece of the map; then toward <paramref name="point"/> by steps of
    /// <see cref="RallyAdvanceStep"/>, each kept only while no enemy's last-seen place sees it, up to
    /// <see cref="RallyAdvanceUnits"/> and not within <see cref="RallyShortUnits"/> of the point. The start itself, seen or
    /// not, when no step is out of sight.
    /// </summary>
    private AiVector RallySpot(AiVector point, AiVector front)
    {
        AiVector middle = front;
        float toPoint = Flat(front, point);
        if (toPoint < RallyShortUnits && toPoint > 1f)
            middle = point + new AiVector(front.X - point.X, front.Y - point.Y, 0f) * (RallyShortUnits / toPoint) + new AiVector(0f, 0f, front.Z - point.Z);
        AiVector at = Snap(front, middle) ?? middle;
        var dir = new AiVector(point.X - middle.X, point.Y - middle.Y, 0f);
        float length = dir.Length;
        float most = MathF.Min(RallyAdvanceUnits, length - RallyShortUnits);
        if (most < RallyAdvanceStep)
            return at;
        dir *= 1f / length;
        for (float step = RallyAdvanceStep; step <= most + 1f; step += RallyAdvanceStep)
        {
            if (Snap(middle, middle + dir * step) is not { } next || EnemySees(next))
                break;
            at = next;
        }

        return at;
    }

    private readonly List<int> _cellsNear = new();

    /// <summary>The centre of the walkable cell nearest <paramref name="spot"/> within <see cref="RallySnapUnits"/>, in <paramref name="point"/>'s piece of the map (any, if it is on none); the spot itself without a graph.</summary>
    private AiVector? Snap(AiVector point, AiVector spot)
    {
        if (_graph is null)
            return spot;
        CellId home = _graph.FindCell(point + new AiVector(0f, 0f, 1f));
        int piece = home.IsValid ? _graph.ComponentOf(home.Index) : -1;
        _cellsNear.Clear();
        _graph.CellsNear(spot.X - RallySnapUnits, spot.Y - RallySnapUnits, spot.X + RallySnapUnits, spot.Y + RallySnapUnits, _cellsNear);
        AiVector? best = null;
        float bestD = float.PositiveInfinity;
        foreach (int c in _cellsNear)
        {
            if (piece >= 0 && _graph.ComponentOf(c) != piece)
                continue;
            AiVector centre = _graph.Cell(c).Centre;
            float d = centre.DistanceTo(spot);
            if (d < bestD)
                (bestD, best) = (d, centre);
        }

        return best;
    }

    /// <summary>Whether an enemy, from where the bot last saw it, would see <paramref name="at"/> (the map's static sight; with none, none sees).</summary>
    private bool EnemySees(AiVector at)
    {
        if (_memory.World.Sight is not { } sight || _graph is null)
            return false;
        CellId cell = _graph.FindCell(at + new AiVector(0f, 0f, 1f));
        if (!cell.IsValid)
            return false;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead || !a.EverSeen || _memory.IsFriend(a))
                continue;
            CellId from = _graph.FindCell(a.LastSeenPos + new AiVector(0f, 0f, 1f));
            if (from.IsValid && sight.Visible(from.Index, cell.Index))
                return true;
        }

        return false;
    }

    /// <summary>Into a push for its goal, the push called at <paramref name="call"/>; none without a goal.</summary>
    private bool BeginPush(Tick call)
    {
        if (PushGoal() is not { } goal)
            return false;
        (_pushing, _pushTarget, _pushOwnPoint, _pushSince, _pushCall, _pushWaitedTicks, _pushLastDecide) = (true, goal.Id, goal.OwnerTeam == _self.Team, _now, call, 0, _now);
        _rallyHeld = false;
        PushesBegun++;
        _play = TFTeamPlay.Push;
        return true;
    }

    /// <summary>A push is over when its point is taken (or held: no longer being taken), lost (or no longer the team's to take), or its time is past.</summary>
    private bool PushOver()
    {
        bool found = PushPoint(out Objective target);
        bool ours = found && target.OwnerTeam == _self.Team;
        if (!found || (_pushOwnPoint ? !ours : (target.MayAct & (1 << _self.Team)) == 0 && !ours))
        {
            PushesLost++;
            return true;
        }

        if (_pushOwnPoint ? target.State != ObjectiveState.Capturing : ours)
        {
            PushesTaken++;
            return true;
        }

        if ((_now - _pushSince) * _tick > PushMostSeconds)
        {
            PushesTimedOut++;
            return true;
        }

        return false;
    }

    /// <summary>The objective the push goes for, as the HUD has it now.</summary>
    private bool PushPoint(out Objective target)
    {
        foreach (Objective o in _view.Objectives!.All)
        {
            if (o.Id == _pushTarget)
            {
                target = o;
                return true;
            }
        }

        target = default;
        return false;
    }

    /// <summary>Out of the push (taken, lost, its time): the points for the next stretch.</summary>
    private void EndPush()
    {
        _pushing = false;
        PointsAfterPush++;
        TakeStance(TFStance.Points);
    }

    /// <summary>A death ends a push (C.6.1: until it is taken, lost or they die); the stance stays, and back from the dead the bot looks for its team's push (<see cref="CameBack"/>).</summary>
    private void TeamPlayDied()
    {
        (_pushing, _toFront) = (false, false);
        _cameBack = true;
    }

    /// <summary>
    /// The points stance holding a teammate's live rally, each decide: not on a point in play, not going to an own point
    /// being taken, the rally nearer than its point and within <see cref="RallyJoinReach"/>: the team stance.
    /// </summary>
    private bool JoinsRally()
    {
        HearRally();
        if (!_rallyHeld || _rally.Speaker == _bot || !RallyLive(_rally))
            return false;
        if (OnPointInPlay(_self.Origin) || (_pointsGoal is { } g && _pointsGoalOwn && g.State == ObjectiveState.Capturing))
            return false;
        float there = _self.Origin.DistanceTo(_rally.Place);
        if (_pointsGoal is not { } goal)
            return there <= RallyJoinReach;
        return there < _self.Origin.DistanceTo(goal.Position) && there <= RallyJoinReach;
    }

    /// <summary>To the push's point, keeping with the others; to the rally, and at it, wait.</summary>
    private bool GoTeamPlay()
    {
        if (_play == TFTeamPlay.Push)
        {
            if (!PushPoint(out Objective target))
                return false;
            AiVector at = target.Position;
            float mine = _self.Origin.DistanceTo(at);
            int sinceLast = _now - _pushLastDecide;
            _pushLastDecide = _now;
            if (mine < ArriveUnits + PointStandUnits)
            {
                StandApart(at, PointStandUnits);
                _verdict = "on the point in the push";
                return true;
            }

            if (mine > PushCommitUnits && _pushWaitedTicks * _tick < PushWaitMostSeconds && TeammateFarBehind(at, mine))
            {
                _pushWaitedTicks += sinceLast;
                (_hasGoal, _goal) = (true, _self.Origin);
                _verdict = "waiting for the push";
                return true;
            }

            // A rocket jumper takes the route's links and the heal rule (the Course branch's way) to the push's point.
            if (MayRocketJump && _now >= _courseQuitUntil)
            {
                _stage = target;
                if (GoCourse())
                {
                    if (_verdict is "to the next stage" or "taking the stage")
                        _verdict = "pushing";
                    return true;
                }
            }

            GoTo(at, MoveSpeed.Run);
            _verdict = "pushing";
            return true;
        }

        if (_self.Origin.DistanceTo(_rally.Place) < GatherArriveUnits)
        {
            StandApart(_rally.Place, GroupStandUnits);
            _verdict = "at the rally";
            return true;
        }

        // On the way the bot keeps with its group as a pusher does: one coming from the spawn walks with the others.
        float toRally = _self.Origin.DistanceTo(_rally.Place);
        int stepped = _now - _gatherLastDecide;
        _gatherLastDecide = _now;
        if (_gatherWaitedTicks * _tick < PushWaitMostSeconds && TeammateFarBehind(_rally.Place, toRally))
        {
            _gatherWaitedTicks += stepped;
            (_hasGoal, _goal) = (true, _self.Origin);
            _verdict = "waiting on the way to the rally";
            return true;
        }

        GoTo(_rally.Place, MoveSpeed.Run);
        _verdict = "to the rally";
        return true;
    }

    /// <summary>
    /// Whether the nearest teammate the bot knows within <see cref="PushGroupUnits"/> (in sight, or seen within
    /// <see cref="RallySeenSeconds"/>) is more than <see cref="PushKeepUnits"/> away and further from <paramref name="at"/> than it.
    /// </summary>
    private bool TeammateFarBehind(AiVector at, float mine)
    {
        float nearest = float.PositiveInfinity;
        AiVector who = default;
        Blackboard board = _memory.Board;
        for (int s = 0; s < board.Slots; s++)
        {
            KnownActor a = board.Actor(s);
            if (!a.Known || a.Dead || a.Id == _bot || !_memory.IsFriend(a))
                continue;
            AiVector there;
            if (_view.TryActor(a.Id, out ActorFacts drawn))
                there = drawn.Origin;
            else if (a.EverSeen && (_now - a.LastSeenTick) * _tick <= RallySeenSeconds)
                there = a.LastSeenPos;
            else
                continue;
            float d = _self.Origin.DistanceTo(there);
            if (d < nearest)
                (nearest, who) = (d, there);
        }

        return nearest > PushKeepUnits && nearest <= PushGroupUnits && who.DistanceTo(at) > mine;
    }
}
