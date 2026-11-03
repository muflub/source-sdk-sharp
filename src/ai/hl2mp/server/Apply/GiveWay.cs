using SourceSharp.Ai.Hl2mp.Nav;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>A player where the driver can feel them: a slot and a place.</summary>
public readonly record struct NearbyPlayer(int Slot, AiVector Origin);

/// <summary>
/// Two players who want each other's place (user report 2026-11-02: "head
/// on"): neither passes while each pushes. A bot held up by a player in front
/// gives way when it is the lower of the two (the higher slot; a person holds a
/// low slot, so a person never has to), or when it has waited out its patience
/// behind someone who is not moving at all: it steps to its right, or its left,
/// or with no room either side goes back the way it came until there is, and
/// lets them by. Only ever keys, as a player sidesteps one in a corridor.
/// </summary>
public sealed class GiveWay
{
    /// <summary>How near, across, a player in front holds a bot up: two half-hulls and a margin.</summary>
    public const float ContactRadius = 48f;

    /// <summary>How far a bot must get from where it was to be moving.</summary>
    public const float MovingRadius = 8f;

    /// <summary>How long with a player in front and no way gained is held up (0.2 s).</summary>
    public const int BlockedTicks = 13;

    /// <summary>How long the higher of the two presses a player who is not giving way, before it gives way itself (1 s).</summary>
    public const int PatienceTicks = 66;

    /// <summary>How long one giving-way lasts (3 s).</summary>
    public const int YieldTicks = 200;

    /// <summary>How long it waits once aside for someone who is not coming through (1 s): then it goes on past them.</summary>
    public const int WaitAsideTicks = 66;

    /// <summary>How long a step aside is given to get clear (0.5 s), before it goes back instead.</summary>
    public const int SideTicks = 33;

    /// <summary>How often going back looks for room aside (0.25 s).</summary>
    public const int LookAsideTicks = 16;

    /// <summary>How far aside is out of the way: a hull's width.</summary>
    public const float AsideReach = 32f;

    /// <summary>How far from a player is past them.</summary>
    public const float ClearRadius = 72f;

    private const float CrumbStep = 32f;
    private const int MaxCrumbs = 32;

    private readonly Dictionary<int, State> _states = new();

    /// <summary>How near its last corner a player stands to have the place taken.</summary>
    public const float GoalTakenRadius = 24f;

    /// <summary>Whether the last step left the bot standing off a place someone else is on: no stuck to report.</summary>
    public bool Waiting { get; private set; }

    /// <summary>How many times a bot has given way, for the facts and the counters.</summary>
    public long Yields { get; private set; }

    public void Forget(int slot) => _states.Remove(slot);

    /// <summary>
    /// This tick's walk for the bot in <paramref name="slot"/>: <paramref name="move"/>
    /// as it was, without the separation's push when it has a player right in
    /// front (the push only holds it back from them), or a step aside or back.
    /// </summary>
    public void Step(int slot, NavFollower follower, AiVector origin, Tick tick, IReadOnlyList<NearbyPlayer> players, ref MoveIntent move)
    {
        if (!_states.TryGetValue(slot, out State? s))
            _states[slot] = s = new State(origin, tick.Value);

        NearbyPlayer? other = Nearest(slot, origin, players);
        if (s.YieldUntil > tick.Value)
        {
            if (other is { } o && !Past(s.Way, origin, o.Origin))
            {
                Waiting = true;
                move = Direction(YieldWay(s, follower, origin, o.Origin, tick));
                return;
            }

            EndYield(s, origin, tick);
        }

        if ((origin - s.Anchor).Length > MovingRadius)
        {
            s.Anchor = origin;
            s.AnchorTick = tick.Value;
            s.Drop(origin);
        }

        Waiting = false;
        if (move.Kind is not (MoveKind.Follow or MoveKind.Retreat) || move.CornerCount < 1)
            return;

        // Someone is on the very place it is going: it stands off, and the brain
        // goes on to something else when it sees they have it.
        if (other is { } on && OnTheGoal(move, origin, on.Origin))
        {
            move = MoveIntent.Hold;
            s.AnchorTick = tick.Value;
            Waiting = true;
            return;
        }

        AiVector way = FirstWay(move, origin);
        if (way.LengthSquared <= 0f || other is not { } ahead || !InFront(way, origin, ahead.Origin))
            return;

        move = move with { Direction = default };
        int waited = tick.Value - s.AnchorTick;
        if (waited < BlockedTicks || (ahead.Slot < slot ? false : waited < PatienceTicks))
            return;

        // Waited out on someone who is not moving: round them, as a person would.
        if (ahead.Slot > slot && follower.RouteRound(ahead.Origin))
        {
            s.AnchorTick = tick.Value;
            return;
        }

        Yields++;
        s.Way = way;
        s.YieldUntil = tick.Value + YieldTicks;
        s.SideUntil = 0;
        s.NextLook = 0;
        move = Direction(YieldWay(s, follower, origin, ahead.Origin, tick));
    }

    private static MoveIntent Direction(AiVector way)
        => way.LengthSquared > 0f ? new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, way, false, 0) : MoveIntent.Hold;

    private static void EndYield(State s, AiVector origin, Tick tick)
    {
        s.YieldUntil = 0;
        s.Anchor = origin;
        s.AnchorTick = tick.Value;
    }

    /// <summary>
    /// Aside if there is room to clear them, to the right first as both do when
    /// they meet; with none, or if it does not get there, back along where it
    /// came from until there is; once clear of them across, still.
    /// </summary>
    private static AiVector YieldWay(State s, NavFollower follower, AiVector origin, AiVector other, Tick tick)
    {
        var right = new AiVector(s.Way.Y, -s.Way.X, 0f);
        var from = new AiVector(origin.X - other.X, origin.Y - other.Y, 0f);
        float across = from.X * right.X + from.Y * right.Y;
        if (MathF.Abs(across) >= AsideReach - 1f)
        {
            s.YieldUntil = Math.Min(s.YieldUntil, tick.Value + WaitAsideTicks);
            return default;
        }

        if (tick.Value < s.SideUntil)
            return s.Side;
        if (tick.Value >= s.NextLook)
        {
            s.NextLook = tick.Value + LookAsideTicks;
            foreach (AiVector side in (ReadOnlySpan<AiVector>)[right, -right])
            {
                float needed = AsideReach - (from.X * side.X + from.Y * side.Y);
                if (follower.Clear(origin, origin + side * needed))
                {
                    s.Side = side;
                    s.SideUntil = tick.Value + SideTicks;
                    return side;
                }
            }
        }

        while (s.Crumbs.Count > 0)
        {
            AiVector crumb = s.Crumbs[^1];
            var to = new AiVector(crumb.X - origin.X, crumb.Y - origin.Y, 0f);
            if (to.Length > CrumbStep * 0.5f)
                return to.Normalised();
            s.Crumbs.RemoveAt(s.Crumbs.Count - 1);
        }

        return -s.Way;
    }

    private static bool OnTheGoal(in MoveIntent move, AiVector origin, AiVector other)
    {
        AiVector goal = move.Corner(move.CornerCount - 1);
        return new AiVector(goal.X - other.X, goal.Y - other.Y, 0f).Length < GoalTakenRadius
               && new AiVector(goal.X - origin.X, goal.Y - origin.Y, 0f).Length < ContactRadius + GoalTakenRadius;
    }

    private static NearbyPlayer? Nearest(int slot, AiVector origin, IReadOnlyList<NearbyPlayer> players)
    {
        NearbyPlayer? best = null;
        float nearest = ClearRadius;
        for (int i = 0; i < players.Count; i++)
        {
            NearbyPlayer p = players[i];
            if (p.Slot == slot || MathF.Abs(p.Origin.Z - origin.Z) > 56f)
                continue;
            float flat = new AiVector(p.Origin.X - origin.X, p.Origin.Y - origin.Y, 0f).Length;
            if (flat < nearest)
            {
                nearest = flat;
                best = p;
            }
        }

        return best;
    }

    private static bool InFront(AiVector way, AiVector origin, AiVector other)
    {
        var to = new AiVector(other.X - origin.X, other.Y - origin.Y, 0f);
        float along = to.X * way.X + to.Y * way.Y, across = MathF.Abs(to.X * way.Y - to.Y * way.X);
        return to.Length < ContactRadius && along > 0f && across < AsideReach - 1f;
    }

    /// <summary>They have gone by: behind the way it was going, or out of reach.</summary>
    private static bool Past(AiVector way, AiVector origin, AiVector other)
    {
        var to = new AiVector(other.X - origin.X, other.Y - origin.Y, 0f);
        return to.X * way.X + to.Y * way.Y < -AsideReach * 0.5f;
    }

    private static AiVector FirstWay(in MoveIntent move, AiVector origin)
    {
        for (int i = 0; i < move.CornerCount; i++)
        {
            AiVector c = move.Corner(i);
            var to = new AiVector(c.X - origin.X, c.Y - origin.Y, 0f);
            if (to.Length > 1f)
                return to.Normalised();
        }

        return default;
    }

    private sealed class State(AiVector anchor, int tick)
    {
        public AiVector Anchor { get; set; } = anchor;
        public int AnchorTick { get; set; } = tick;
        public int YieldUntil { get; set; }
        public AiVector Way { get; set; }
        public AiVector Side { get; set; }
        public int SideUntil { get; set; }
        public int NextLook { get; set; }
        public List<AiVector> Crumbs { get; } = new();

        /// <summary>Where it walked, so it can go back the same way.</summary>
        public void Drop(AiVector at)
        {
            if (Crumbs.Count > 0 && (Crumbs[^1] - at).Length < CrumbStep)
                return;
            if (Crumbs.Count == MaxCrumbs)
                Crumbs.RemoveAt(0);
            Crumbs.Add(at);
        }
    }
}
