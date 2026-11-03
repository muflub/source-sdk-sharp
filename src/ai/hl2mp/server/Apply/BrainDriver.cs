using SourceSharp.Ai.Hl2mp.Nav;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// From a brain's intent to the commands of each tick between its decides
/// (plan_hl2mp_ai §2.3's path stage): a goal to walk to becomes a nav
/// follower's corners (rebuilt when the goal moves to another cell, at most
/// every <see cref="RegoalTicks"/>), the brain's look and hands are kept as
/// it said, and its one-shots keep their sequence so each is pressed once.
/// The brain chose; this only carries it out, as a client's movement code
/// carries out its player's keys.
/// </summary>
public sealed class BrainDriver
{
    /// <summary>A follower is rebuilt for a goal in another cell at most this often (0.5 s).</summary>
    public const int RegoalTicks = 33;

    private readonly Dictionary<int, Driven> _driven = new();
    private readonly Dictionary<int, FieldJob> _building = new();

    /// <summary>
    /// Where a new goal's field is built (§2.7's flow-field job): the AI's
    /// workers, or null to build it here on the main thread when first followed.
    /// A worker that will not take it (a stopped pool) is the same as none.
    /// </summary>
    public Func<IAiWork, bool>? Post { get; set; }

    /// <summary>Field builds the followers made on the main thread, for the cost counter.</summary>
    public int Builds { get; private set; }

    /// <summary>Field builds a worker made, for the cost counter.</summary>
    public int PooledBuilds { get; private set; }

    /// <summary>The follower carrying out the bot's goal now, or null: the brain has no goal to walk to.</summary>
    public NavFollower? FollowerOf(int slot) => _driven.TryGetValue(slot, out Driven? d) ? d.Follower : null;

    public void Forget(int slot)
    {
        _driven.Remove(slot);
        _building.Remove(slot);
        _scripted.Remove(slot);
        _climbs.Remove(slot);
        _progress.Remove(slot);
        _stuckSaid.Remove(slot);
        _giveWay.Forget(slot);
        _unreachable.Remove(slot);
    }

    /// <summary>
    /// This tick's intent for a bot whose brain last said <paramref name="brain"/>:
    /// the follower's move toward the brain's goal, or the brain's own move;
    /// on a ladder, the climb's, whatever the brain said.
    /// </summary>
    public IntentCommand Drive(in IntentCommand brain, AiVector origin, Tick tick, int seq, NavGraph? graph, NavObstacleState? obstacles,
                               bool onLadder = false, AiAngles view = default)
    {
        int slot = brain.Bot.Slot;
        MoveIntent move = brain.Move;
        LookIntent look = brain.Look;
        ActionIntent action = brain.Action;
        Driven? d = null;
        if (move.Kind is MoveKind.Follow or MoveKind.Retreat && move.CornerCount >= 1 && graph is not null && obstacles is not null)
        {
            move = Follow(slot, move, origin, tick, graph, obstacles);
            _driven.TryGetValue(slot, out d);
        }
        else
        {
            _driven.Remove(slot);
            _building.Remove(slot);
        }

        if (graph is not null)
            Ladder(slot, d, origin, view, tick, onLadder, graph, ref move, ref look, ref action);
        if (d is not null && !onLadder)
        {
            _giveWay.Step(slot, d.Follower, origin, tick, Players, ref move);
            if (_giveWay.Waiting)
                _progress.Remove(slot);
            else
                Watch(slot, d, origin, tick);
        }
        else
            _progress.Remove(slot);

        return new IntentCommand(brain.Bot, tick, seq, tick + IntentCommand.LifetimeTicks, move, look, action, brain.Reason);
    }

    private readonly Dictionary<int, Driven> _scripted = new();

    /// <summary>
    /// The ladder step for a follower that is not the driver's own (a
    /// scripted walk's, <c>ai_bot_goto</c>): the same mount and climb.
    /// </summary>
    public void LadderFor(int slot, NavFollower follower, AiVector origin, AiAngles view, Tick tick, bool onLadder, NavGraph graph,
                          ref MoveIntent move, ref LookIntent look, ref ActionIntent action)
    {
        if (!_scripted.TryGetValue(slot, out Driven? d) || d.Follower != follower)
            _scripted[slot] = d = new Driven(follower, tick.Value);
        Ladder(slot, d, origin, view, tick, onLadder, graph, ref move, ref look, ref action);
        if (!onLadder)
            _giveWay.Step(slot, follower, origin, tick, Players, ref move);
    }

    private readonly GiveWay _giveWay = new();

    /// <summary>The living players and where they are, as of the senses' last tick: who a bot walks into. The side sets it.</summary>
    public IReadOnlyList<NearbyPlayer> Players { get; set; } = [];

    /// <summary>How many times a bot has given way to a player, for the counters.</summary>
    public long Yields => _giveWay.Yields;

    /// <summary>The +use a mount holds, every this many ticks until the bot is on (0.5 s), and for this many.</summary>
    public const int MountEvery = 33, MountHold = 2;

    /// <summary>
    /// How near the pitch a climb wants the view must be before the bot mounts
    /// and holds forward. Forward with the view off the ladder's axis near its
    /// end is what gets a player off there (stock's sv_autoladderdismount; live,
    /// dm_lockdown: on and off at the foot in one tick, every 33).
    /// </summary>
    public const float MountPitchSlack = 8f;

    /// <summary>How long a mount waits for the view (2 s, an easy bot's turn), so a view that never gets there does not hold the bot off the ladder for good.</summary>
    public const int SettleTicks = 132;

    /// <summary>How many times a stalled bot asks +use to get off before it jumps off, as a player with no dismount in reach does.</summary>
    public const int UsesBeforeJump = 2;

    /// <summary>How far across the look point of a climb stays from the bot, so the view pitches along the ladder however far off the end's floor is.</summary>
    public const float LookReach = 24f;

    private readonly Dictionary<int, Climb> _climbs = new();

    /// <summary>
    /// A ladder on the route (a func_useableladder, climbed as a player climbs
    /// it): at the foot (or the top) the bot looks past the far end and holds
    /// +use to mount, then, on it, holds forward until the movement puts it off
    /// at the dismount; the follower takes it from there. A door on the route
    /// is walked into with +use held.
    /// </summary>
    private void Ladder(int slot, Driven? d, AiVector origin, AiAngles view, Tick tick, bool onLadder, NavGraph graph,
                        ref MoveIntent move, ref LookIntent look, ref ActionIntent action)
    {
        if (onLadder)
        {
            OnTheLadder(slot, d, origin, view, tick, graph, ref move, ref look, ref action);
            return;
        }

        // Come off, or a mount that did not take.
        if (_climbs.TryGetValue(slot, out Climb? climb) && (climb.WasOn || (climb.Asked != climb.Started && tick.Value - climb.Asked > MountEvery * 2)))
            _climbs.Remove(slot);
        if (d is null || !d.Follower.NextCell.IsValid)
            return;

        // A door on the route: walked into, and +use held at it as a player
        // opens one that does not open on a touch.
        if (d.Follower.NextVia == NavEdgeType.Door)
        {
            AiVector door = graph.Cell(d.Follower.NextCell).Centre;
            if (new AiVector(door.X - origin.X, door.Y - origin.Y, 0f).Length < 64f)
            {
                if (tick.Value - d.Mounted >= MountEvery)
                    d.Mounted = tick.Value;
                if (tick.Value - d.Mounted < MountHold && action.Kind == ActionKind.None)
                    action = new ActionIntent(ActionKind.Use, FireStyle.Tap, 0, 0);
            }

            return;
        }

        if (d.Follower.NextVia != NavEdgeType.Ladder || !d.Follower.LadderEnd().IsValid)
            return;

        AiVector to = graph.Cell(d.Follower.LadderEnd()).Centre;
        bool up = to.Z >= origin.Z;
        // Facing the ladder, or +use does not take it (stock's LookingAtLadder):
        // the far end's cell can be behind the bot when it has come past the
        // ladder (live, dm_lockdown: +use refused at its foot for good).
        AiVector way = Toward(origin, NearestLadder(origin) ?? to, new AiVector(1f, 0f, 0f));
        AiVector point = LadderLook(origin, to, up, way);
        look = new LookIntent(LookKind.Point, point, ActorId.None, AimPoint.Body, 0f, 0);
        Mounts++;
        if (climb is null || climb.To != to)
            _climbs[slot] = climb = new Climb(to, up, tick.Value);

        AiVector foot = graph.Cell(d.Follower.At).Centre;
        if (new AiVector(foot.X - origin.X, foot.Y - origin.Y, 0f).Length >= 48f)
            return;
        // Standing to mount: a walk into the ladder's cone mounts it as well, and
        // +use on the tick that mount lands is a +use on the ladder, which gets
        // the bot off again (live, dm_lockdown: on and off at z 0 once a climb in
        // two). The walk comes back if +use has not taken it by then.
        if (tick.Value - climb.Started < SettleTicks * 2)
            move = MoveIntent.Hold;

        // Looking along the ladder first: forward, the moment the mount lands,
        // gets a bot off again if it is still looking across it.
        if (tick.Value - d.Mounted >= MountEvery && (Pitched(origin, view, point) || tick.Value - climb.Started >= SettleTicks))
        {
            d.Mounted = tick.Value;
            climb.Asked = tick.Value;
        }

        if (tick.Value - d.Mounted < MountHold)
            action = new ActionIntent(ActionKind.Use, FireStyle.Tap, 0, 0);
    }

    /// <summary>
    /// On the ladder: the climb runs to its end whatever the goal has become,
    /// since the brain's new goal is a new follower and the follower has no
    /// cell mid-ladder (live, dm_lockdown: held at z 72 for ever, or at the top
    /// pressing forward at a goal behind it). A bot put on one no route of its
    /// climbs (a walk into its cone, a fall past it) asks to get off. Stalled,
    /// by the end or by someone on it: +use, then a jump, as a player gets off.
    /// </summary>
    private void OnTheLadder(int slot, Driven? d, AiVector origin, AiAngles view, Tick tick, NavGraph graph,
                             ref MoveIntent move, ref LookIntent look, ref ActionIntent action)
    {
        Climbs++;
        if (!_climbs.TryGetValue(slot, out Climb? c))
        {
            bool routed = d is not null && d.Follower.NextVia == NavEdgeType.Ladder && d.Follower.LadderEnd().IsValid;
            AiVector? to = routed ? graph.Cell(d!.Follower.LadderEnd()).Centre : null;
            _climbs[slot] = c = new Climb(to, to is { } t && t.Z >= origin.Z, tick.Value);
        }

        if (!c.WasOn)
        {
            c.WasOn = true;
            c.OnSince = tick.Value;
            c.LastZ = origin.Z;
            c.Stalled = c.To is null ? tick.Value - MountEvery : tick.Value;
        }

        if (action.Kind == ActionKind.Use)
            action = ActionIntent.None;

        if (c.To is { } end)
        {
            c.Facing = Toward(origin, end, c.Facing);
            AiVector point = LadderLook(origin, end, c.Up, c.Facing);
            look = new LookIntent(LookKind.Point, point, ActorId.None, AimPoint.Body, 0f, 0);
            c.Settled = c.Settled || Pitched(origin, view, point) || tick.Value - c.OnSince >= SettleTicks;
            if (!c.Settled)
            {
                move = MoveIntent.Hold;
                c.Stalled = tick.Value;
                return;
            }

            move = new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, c.Facing, false, 0);
        }
        else
        {
            move = MoveIntent.Hold;
        }

        if (MathF.Abs(origin.Z - c.LastZ) > 1f)
        {
            c.LastZ = origin.Z;
            c.Stalled = tick.Value;
            c.Asks = 0;
            return;
        }

        int stalled = tick.Value - c.Stalled;
        if (stalled < MountEvery)
            return;
        int phase = (stalled - MountEvery) % MountEvery;
        if (phase == 0)
            c.Asks++;
        if (phase >= MountHold)
            return;
        if (c.Asks <= UsesBeforeJump)
            action = new ActionIntent(ActionKind.Use, FireStyle.Tap, 0, 0);
        else
            move = new MoveIntent(MoveKind.Jump, MoveSpeed.Run, 0, default, default, default, default, default, false, 0);
    }

    /// <summary>How far across a ladder is looked at to mount it: stock finds one for +use within 64.</summary>
    public const float LadderReach = 64f;

    /// <summary>The map's func_useableladders, one point on each's line: what a bot looks at to mount one. The side sets it per level.</summary>
    public IReadOnlyList<AiVector> Ladders { get; set; } = [];

    private AiVector? NearestLadder(AiVector origin)
    {
        AiVector? best = null;
        float bestFlat = LadderReach;
        foreach (AiVector l in Ladders)
        {
            float flat = new AiVector(l.X - origin.X, l.Y - origin.Y, 0f).Length;
            if (flat < bestFlat)
            {
                bestFlat = flat;
                best = l;
            }
        }

        return best;
    }

    /// <summary>The flat way to <paramref name="to"/>, or <paramref name="otherwise"/> when the bot is over it.</summary>
    private static AiVector Toward(AiVector origin, AiVector to, AiVector otherwise)
    {
        var flat = new AiVector(to.X - origin.X, to.Y - origin.Y, 0f);
        return flat.Length2D > 4f ? flat.Normalised() : otherwise;
    }

    /// <summary>
    /// A point past the ladder's far end, near across, so the view pitches up a
    /// climb or down a descent for the whole of it. The way is the climb's from
    /// its start: asked from where the bot is, a climb past its top floor (the
    /// ladder goes on above it) would look down and climb back.
    /// </summary>
    private static AiVector LadderLook(AiVector origin, AiVector to, bool up, AiVector way)
    {
        return new AiVector(origin.X + way.X * LookReach, origin.Y + way.Y * LookReach, to.Z + (up ? 96f : -96f));
    }

    /// <summary>
    /// Whether the view has reached the look at <paramref name="point"/>: its
    /// pitch closely, and near enough its yaw that forward is forward (turning
    /// past the far end's way with forward held, the bot pressed back and side
    /// and went down the ladder and off it, live).
    /// </summary>
    private static bool Pitched(AiVector origin, AiAngles view, AiVector point)
    {
        AiAngles want = BotCommandApplier.AnglesTo(origin + new AiVector(0f, 0f, EyeHeight), point);
        return MathF.Abs(want.Pitch - view.Pitch) <= MountPitchSlack && MathF.Abs(HumanMotion.AngleDelta(want.Yaw, view.Yaw)) <= MountYawSlack;
    }

    /// <summary>How near the yaw a climb wants the view must be before forward is held.</summary>
    public const float MountYawSlack = 30f;

    /// <summary>A standing player's eye over its feet (hull's view offset).</summary>
    private const float EyeHeight = 64f;

    /// <summary>Ticks spent mounting a ladder and climbing one, for the facts and the counters.</summary>
    public long Mounts { get; private set; }

    public long Climbs { get; private set; }

    /// <summary>How long a goal may have no route before the bot goes as near as it can instead (0.5 s).</summary>
    public const int NoRouteTicks = 33;

    /// <summary>How long a bot may make no progress toward its goal before it acts (3 s).</summary>
    public const int StuckTicks = 200;

    /// <summary>How many times a bot of the brains has acted on being stuck, for the counters.</summary>
    public long Stucks { get; private set; }

    private readonly Dictionary<int, Progress> _progress = new();
    private readonly Dictionary<int, string> _stuckSaid = new();

    /// <summary>What a bot's last stuck spell did, once, for the log line; null when nothing has happened since it was last asked.</summary>
    public string? TakeStuckLine(int slot)
    {
        if (!_stuckSaid.Remove(slot, out string? line))
            return null;
        return line;
    }

    /// <summary>
    /// A bot of the brains making no progress toward its goal for
    /// <see cref="StuckTicks"/>, whatever edge it is on, reports it to its
    /// follower, as a scripted walk's does. Progress is the field's cost
    /// falling; off the field, moving at all. A new goal's field is a new
    /// measure of the cost and not a new patience: a hunt re-goaling every
    /// half second stood at the walkway's wall for good.
    /// </summary>
    private void Watch(int slot, Driven d, AiVector origin, Tick tick)
    {
        if (d.State == FollowState.Arrived)
        {
            _progress.Remove(slot);
            return;
        }

        float cost = d.Follower.CostLeft;
        if (!_progress.TryGetValue(slot, out Progress? p))
        {
            _progress[slot] = new Progress(d.Follower, cost, origin, tick.Value);
            return;
        }

        if (p.Follower != d.Follower)
        {
            p.Follower = d.Follower;
            p.Best = cost;
        }

        bool progressed = float.IsFinite(cost)
            ? cost < p.Best - 1f
            : (origin - p.Anchor).Length > ProgressRadius;
        if (progressed)
        {
            p.Best = cost;
            p.Anchor = origin;
            p.Tick = tick.Value;
            return;
        }

        if (tick.Value - p.Tick <= StuckTicks)
            return;
        string where = System.FormattableString.Invariant($"({origin.X:F0} {origin.Y:F0} {origin.Z:F0})");
        string next = d.Follower.NextStep();
        int at = d.Follower.At.Index;
        // Stuck in the objective's own cell: nothing more the route can do.
        if (d.Follower.At == d.Follower.Objective)
            _unreachable[slot] = d.Follower.Target;
        bool round = d.Follower.ReportStuck(origin);
        Stucks++;
        _stuckSaid[slot] = $"is stuck at {where}, cell {at}, {d.State}, next {next}; "
                           + (round ? $"routes round {d.Follower.Avoided.Count} cells" : "no way round: jumps at it");
        p.Best = float.PositiveInfinity;
        p.Anchor = origin;
        p.Tick = tick.Value;
    }

    /// <summary>How far a bot off the field must move from where it was to count as moving.</summary>
    public const float ProgressRadius = 48f;

    private MoveIntent Follow(int slot, MoveIntent move, AiVector origin, Tick tick, NavGraph graph, NavObstacleState obstacles)
    {
        AiVector goal = move.Corner0;
        CellId cell = graph.FindCell(goal + new AiVector(0f, 0f, 1f));
        if (!cell.IsValid)
        {
            foreach (CellId near in NavPoints.CellsNear(graph, goal, ClearanceClass.Crouch))
            {
                cell = near;
                break;
            }
        }

        if (!cell.IsValid)
            return move;

        // A field a worker finished for this bot's newer goal takes over.
        if (_building.TryGetValue(slot, out FieldJob? built) && built.Done)
        {
            _building.Remove(slot);
            PooledBuilds++;
            if (_driven.TryGetValue(slot, out Driven? old))
                built.Follower.GoOnFrom(old.Follower);
            _driven[slot] = new Driven(built.Follower, built.Asked);
        }

        bool have = _driven.TryGetValue(slot, out Driven? d);
        if (!have || (d!.Follower.Objective != cell && d.Instead != cell && tick.Value - d.Built >= RegoalTicks))
        {
            var fresh = new NavFollower(graph, cell, goal, ClearanceClass.Crouch, obstacles);
            if (_building.ContainsKey(slot))
            {
                // A field is on its way (one at a time a bot): keep to the old one meanwhile.
            }
            else if (Post is { } post && post(_building[slot] = new FieldJob(fresh, graph.FindCell(origin + new AiVector(0f, 0f, 1f)), obstacles.Clone(), tick.Value)))
            {
                // Built on a worker: until it is done the bot keeps its old way,
                // or, with none, goes straight at the goal as a player would.
            }
            else
            {
                _building.Remove(slot);
                if (have)
                    fresh.GoOnFrom(d!.Follower);
                d = new Driven(fresh, tick.Value);
                _driven[slot] = d;
                have = true;
            }

            if (!have)
                return move;
        }

        Driven driven = d!;
        int before = driven.Follower.Builds;
        MoveIntent next = driven.Follower.Next(origin, out FollowState state);
        Builds += driven.Follower.Builds - before;

        // A goal nothing reaches (behind glass, a shut gate): as near as it can
        // get, not a stand at the first wall (live: 45 s at "NoRoute", jumping).
        if (state == FollowState.NoRoute && driven.Follower.At.IsValid)
        {
            if (driven.NoRouteSince < 0)
                driven.NoRouteSince = tick.Value;
            else if (tick.Value - driven.NoRouteSince >= NoRouteTicks)
            {
                CellId near = graph.NearestReachable(driven.Follower.At, goal, ClearanceClass.Crouch, obstacles.Shoved());
                var instead = new NavFollower(graph, near, graph.Cell(near).Centre, ClearanceClass.Crouch, obstacles);
                instead.GoOnFrom(driven.Follower);
                driven = new Driven(instead, tick.Value) { Instead = cell };
                _driven[slot] = driven;
                _unreachable[slot] = goal;
                next = instead.Next(origin, out state);
                Builds += instead.Builds;
            }
        }

        driven.State = state;
        return state switch
        {
            FollowState.Arrived => MoveIntent.Hold,
            FollowState.Moving => next with { Speed = move.Speed, Kind = next.Kind == MoveKind.Follow ? move.Kind : next.Kind, Direction = move.Direction },
            // Off the graph or with no route: back to a cell that has one, or
            // with none near, straight at the goal, as a player would try.
            _ => next.CornerCount >= 1 ? next with { Speed = move.Speed } : move,
        };
    }

    private readonly Dictionary<int, AiVector> _unreachable = new();

    /// <summary>
    /// A goal this bot has just found it cannot get to, once, for the brain to
    /// hear (a pickup behind glass is no pickup for now): it stood 111 s going
    /// "to heal" as near as it could get to one (live, dm_lockdown).
    /// </summary>
    public AiVector? TakeUnreachable(int slot) => _unreachable.Remove(slot, out AiVector goal) ? goal : null;

    /// <summary>A new goal's first field, built on a worker over the obstacles as they stood when it was asked.</summary>
    private sealed class FieldJob(NavFollower follower, CellId from, NavObstacleState obstacles, int asked) : IAiWork
    {
        private volatile bool _done;

        public NavFollower Follower { get; } = follower;

        public int Asked { get; } = asked;

        public bool Done => _done;

        public void Run()
        {
            Follower.BuildFrom(from, obstacles);
            _done = true;
        }
    }

    private sealed class Driven(NavFollower follower, int built)
    {
        public NavFollower Follower { get; } = follower;
        public int Built { get; } = built;
        public int Mounted { get; set; } = int.MinValue / 2;
        public FollowState State { get; set; }
        public int NoRouteSince { get; set; } = -1;
        public CellId Instead { get; set; } = CellId.None;
    }

    /// <summary>One bot's climb, from the mount to getting off; a null <see cref="To"/> is a ladder it means to get off.</summary>
    private sealed class Climb(AiVector? to, bool up, int started)
    {
        public AiVector? To { get; } = to;
        public bool Up { get; } = up;
        public int Started { get; } = started;
        public int Asked { get; set; } = started;
        public bool WasOn { get; set; }
        public int OnSince { get; set; }
        public bool Settled { get; set; }
        public AiVector Facing { get; set; } = new(1f, 0f, 0f);
        public float LastZ { get; set; }
        public int Stalled { get; set; }
        public int Asks { get; set; }
    }

    /// <summary>A bot's progress toward its goal.</summary>
    private sealed class Progress(NavFollower follower, float best, AiVector anchor, int tick)
    {
        public NavFollower Follower { get; set; } = follower;
        public float Best { get; set; } = best;
        public AiVector Anchor { get; set; } = anchor;
        public int Tick { get; set; } = tick;
    }
}
