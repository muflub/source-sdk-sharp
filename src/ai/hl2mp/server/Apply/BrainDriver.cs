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

    /// <summary>Field builds the followers made, for the cost counter.</summary>
    public int Builds { get; private set; }

    public void Forget(int slot)
    {
        _driven.Remove(slot);
        _scripted.Remove(slot);
    }

    /// <summary>
    /// This tick's intent for a bot whose brain last said <paramref name="brain"/>:
    /// the follower's move toward the brain's goal, or the brain's own move.
    /// </summary>
    public IntentCommand Drive(in IntentCommand brain, AiVector origin, Tick tick, int seq, NavGraph? graph, NavObstacleState? obstacles,
                               bool onLadder = false)
    {
        MoveIntent move = brain.Move;
        LookIntent look = brain.Look;
        ActionIntent action = brain.Action;
        if (move.Kind is MoveKind.Follow or MoveKind.Retreat && move.CornerCount >= 1 && graph is not null && obstacles is not null)
        {
            move = Follow(brain.Bot.Slot, move, origin, tick, graph, obstacles);
            if (_driven.TryGetValue(brain.Bot.Slot, out Driven? d))
                Ladder(d, origin, tick, onLadder, graph, ref move, ref look, ref action);
        }
        else
        {
            _driven.Remove(brain.Bot.Slot);
        }

        return new IntentCommand(brain.Bot, tick, seq, tick + IntentCommand.LifetimeTicks, move, look, action, brain.Reason);
    }

    private readonly Dictionary<int, Driven> _scripted = new();

    /// <summary>
    /// The ladder step for a follower that is not the driver's own (a
    /// scripted walk's, <c>ai_bot_goto</c>): the same mount and climb.
    /// </summary>
    public void LadderFor(int slot, NavFollower follower, AiVector origin, Tick tick, bool onLadder, NavGraph graph,
                          ref MoveIntent move, ref LookIntent look, ref ActionIntent action)
    {
        if (!_scripted.TryGetValue(slot, out Driven? d) || d.Follower != follower)
            _scripted[slot] = d = new Driven(follower, tick.Value);
        Ladder(d, origin, tick, onLadder, graph, ref move, ref look, ref action);
    }

    /// <summary>The +use a mount holds, every this many ticks until the bot is on (0.5 s), and for this many.</summary>
    public const int MountEvery = 33, MountHold = 2;

    /// <summary>
    /// A ladder on the route (a func_useableladder, climbed as a player climbs
    /// it): at the foot (or the top) the bot holds +use to mount, then, on
    /// it, looks past the far end and holds forward until the movement puts
    /// it off at the dismount; the follower takes it from there.
    /// </summary>
    private void Ladder(Driven d, AiVector origin, Tick tick, bool onLadder, NavGraph graph,
                               ref MoveIntent move, ref LookIntent look, ref ActionIntent action)
    {
        // A door on the route: walked into, and +use held at it as a player
        // opens one that does not open on a touch.
        if (!onLadder && d.Follower.NextVia == NavEdgeType.Door && d.Follower.NextCell.IsValid)
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

        if (!onLadder && d.Follower.NextVia == NavEdgeType.Ladder && d.Follower.NextCell.IsValid)
        {
            AiVector top = graph.Cell(d.Follower.NextCell).Centre;
            d.LadderTo = top;
            AiVector foot = graph.Cell(d.Follower.At).Centre;
            if (new AiVector(foot.X - origin.X, foot.Y - origin.Y, 0f).Length < 48f)
            {
                if (tick.Value - d.Mounted >= MountEvery)
                    d.Mounted = tick.Value;
                if (tick.Value - d.Mounted < MountHold)
                    action = new ActionIntent(ActionKind.Use, FireStyle.Tap, 0, 0);
            }

            look = new LookIntent(LookKind.Point, LadderLook(origin, top), ActorId.None, AimPoint.Body, 0f, 0);
            Mounts++;
            return;
        }

        if (onLadder && d.LadderTo is { } to)
        {
            AiVector flat = new AiVector(to.X - origin.X, to.Y - origin.Y, 0f);
            AiVector dir = flat.Length2D > 4f ? flat.Normalised() : d.Facing;
            d.Facing = dir;
            look = new LookIntent(LookKind.Point, LadderLook(origin, to) + dir * 8f, ActorId.None, AimPoint.Body, 0f, 0);
            move = new MoveIntent(MoveKind.Strafe, MoveSpeed.Run, 0, default, default, default, default, dir, false, 0);
            if (action.Kind == ActionKind.Use)
                action = ActionIntent.None;
            // Stalled at the ladder's end (no height gained for half a second):
            // +use gets a player off at the nearest dismount (live, dm_lockdown's
            // ladder held the bot at its top, z 72, for ever).
            if (MathF.Abs(origin.Z - d.LastZ) > 2f)
            {
                d.LastZ = origin.Z;
                d.Stalled = tick.Value;
            }
            else if (tick.Value - d.Stalled >= MountEvery && (tick.Value - d.Stalled) % MountEvery < MountHold)
            {
                action = new ActionIntent(ActionKind.Use, FireStyle.Tap, 0, 0);
            }

            Climbs++;
            return;
        }

        if (!onLadder)
            d.LadderTo = null;
    }

    /// <summary>A point past the ladder's far end, so the view pitches up a climb or down a descent.</summary>
    private static AiVector LadderLook(AiVector origin, AiVector to)
        => to + new AiVector(0f, 0f, to.Z >= origin.Z ? 96f : -96f);

    /// <summary>Ticks spent mounting a ladder and climbing one, for the facts and the counters.</summary>
    public long Mounts { get; private set; }

    public long Climbs { get; private set; }

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

        if (!_driven.TryGetValue(slot, out Driven? d) || (d.Follower.Objective != cell && tick.Value - d.Built >= RegoalTicks))
        {
            d = new Driven(new NavFollower(graph, cell, goal, ClearanceClass.Crouch, obstacles), tick.Value);
            _driven[slot] = d;
        }

        int before = d.Follower.Builds;
        MoveIntent next = d.Follower.Next(origin, out FollowState state);
        Builds += d.Follower.Builds - before;
        return state switch
        {
            FollowState.Arrived => MoveIntent.Hold,
            FollowState.Moving => next with { Speed = move.Speed, Kind = next.Kind == MoveKind.Follow ? move.Kind : next.Kind, Direction = move.Direction },
            // Off the graph or with no route: straight at the goal, as a player would try.
            _ => move,
        };
    }

    private sealed class Driven(NavFollower follower, int built)
    {
        public NavFollower Follower { get; } = follower;
        public int Built { get; } = built;
        public AiVector? LadderTo { get; set; }
        public int Mounted { get; set; } = int.MinValue / 2;
        public AiVector Facing { get; set; } = new(1f, 0f, 0f);
        public float LastZ { get; set; } = float.NaN;
        public int Stalled { get; set; }
    }
}
