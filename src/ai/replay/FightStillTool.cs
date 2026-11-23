using System.Globalization;
using System.Text;

namespace SourceSharp.Ai.Replay;

/// <summary>Why a fighting bot's body was still while its command moved it (<see cref="FightStillTool"/>).</summary>
public enum StillCause
{
    /// <summary>Holding duck: a crouched walk is a third of a run, under the moving speed by design.</summary>
    Crouched,

    /// <summary>Rising or falling: air control does not speed a body up.</summary>
    InTheAir,

    /// <summary>No floor a step to the commanded side: a wall, or a ledge above a step.</summary>
    Wall,

    /// <summary>The side a step on is lower than a step: a drop.</summary>
    Drop,

    /// <summary>The commanded side changed within <see cref="FightStillTool.ReversalTicks"/>: the body is turning round.</summary>
    Reversal,

    /// <summary>Another player within <see cref="FightStillTool.BodyUnits"/> of the step: a teammate or an enemy in the way.</summary>
    Body,

    /// <summary>The body stands on no cell of the graph.</summary>
    OffGraph,

    /// <summary>None of these: a prop, a slope, the start of a step.</summary>
    Else,
}

/// <summary>One still, commanded, sidestepping firing tick of a bot, and why.</summary>
public readonly record struct StillTick(ActorId Bot, int Tick, AiVector Origin, StillCause Cause, bool Reversed, bool Jumped = false, SideFloor Floor = SideFloor.Open);

/// <summary>The firing ticks a recording's bots were commanded to move and were still, sorted by cause (<see cref="FightStillTool"/>).</summary>
public sealed record FightStillReport(int FiringTicks, int CommandedTicks, int StillCommanded, int StillSidestep, IReadOnlyList<StillTick> Ticks)
{
    public int Count(StillCause cause) => Ticks.Count(t => t.Cause == cause);
}

/// <summary>
/// <c>ai-replay fightstill</c> (lane Gmove, 2026-11-16): of a recording's bots' firing ticks (as
/// <see cref="AiFightMotion"/> counts them) with a sidestep commanded and the body under
/// <see cref="AiFightMotion.MovingSpeed"/>, why it was still. In order, the first that holds:
/// crouched; in the air (rising or falling faster than <see cref="AirSpeed"/>); a wall or a drop a
/// step to the commanded side (the graph's answer: <see cref="SideRoom.Ahead"/> from the body's feet
/// along the side the command moves it, right of the view's yaw by the side move's sign, every door
/// open); a reversal (the side move's sign changed within <see cref="ReversalTicks"/>); another
/// player's body within <see cref="BodyUnits"/> of the step; off the graph; else. A wall tick that
/// was also a reversal is counted a wall and marked. Pure but for the graph it is handed.
/// </summary>
public static class FightStillTool
{
    /// <summary>A side change this recent is a reversal under way: a run's 240 u/s turned round passes under 120 u/s for about ten ticks at TF2's acceleration.</summary>
    public const int ReversalTicks = 16;

    /// <summary>Up or down faster than this the body is in the air.</summary>
    public const float AirSpeed = 60f;

    /// <summary>Another body this near the step blocks it: two hulls' half-widths and some.</summary>
    public const float BodyUnits = 56f;

    private const int Attack = 1, Jump = 2, Duck = 4;

    /// <summary>A jump pressed this recently (a second) puts a body in the air of its own doing: a hop, a rocket jump.</summary>
    public const int JumpedTicks = 66;

    /// <summary>The report of <paramref name="recording"/> over <paramref name="graph"/>.</summary>
    public static FightStillReport Read(Stream recording, NavGraph graph)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(graph);
        AiRecordFormat.ReadFileHeader(recording);
        var bots = new List<ActorId>();
        var cmds = new Dictionary<ActorId, List<AiUserCmd>>();
        var frames = new Dictionary<ActorId, SortedList<int, AiActorFrame>>();
        while (AiRecordFormat.TryReadRecord(recording, out AiRecordType type, out byte[] payload))
        {
            using var r = new BinaryReader(new MemoryStream(payload, writable: false));
            switch (type)
            {
                case AiRecordType.BotAdded:
                    ActorId bot = AiRecordFormat.ReadActor(r);
                    if (!bots.Contains(bot))
                        bots.Add(bot);
                    break;
                case AiRecordType.UserCmd:
                    AiUserCmd c = AiRecordFormat.ReadUserCmd(r);
                    if (!cmds.TryGetValue(c.Actor, out List<AiUserCmd>? list))
                        cmds[c.Actor] = list = new List<AiUserCmd>();
                    list.Add(c);
                    break;
                case AiRecordType.Frame:
                    AiActorFrame f = AiRecordFormat.ReadFrame(r);
                    if (!frames.TryGetValue(f.Actor, out SortedList<int, AiActorFrame>? at))
                        frames[f.Actor] = at = new SortedList<int, AiActorFrame>();
                    at[f.Tick.Value] = f;
                    break;
            }
        }

        return From(bots, cmds, frames, graph);
    }

    /// <summary>The report of <paramref name="bots"/>' commands (each in tick order) over everyone's <paramref name="frames"/> and <paramref name="graph"/>.</summary>
    public static FightStillReport From(IReadOnlyList<ActorId> bots, IReadOnlyDictionary<ActorId, List<AiUserCmd>> cmds,
                                        Dictionary<ActorId, SortedList<int, AiActorFrame>> frames, NavGraph graph)
    {
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(cmds);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(graph);
        var doors = new NavObstacleState(graph.ObstacleCount);
        var ticks = new List<StillTick>();
        int firing = 0, commanded = 0, stillCommanded = 0, stillSide = 0;
        foreach (ActorId b in bots.OrderBy(b => b.Slot).ThenBy(b => b.Serial))
        {
            if (!cmds.TryGetValue(b, out List<AiUserCmd>? list) || !frames.TryGetValue(b, out SortedList<int, AiActorFrame>? own))
                continue;
            int lastSideTick = int.MinValue / 2, lastSide = 0, lastJump = int.MinValue / 2;
            foreach (AiUserCmd c in list)
            {
                int t = c.Tick.Value;
                int side = Math.Sign(c.SideMove);
                if ((c.Buttons & Jump) != 0)
                    lastJump = t;
                bool reversed = side != 0 && lastSide != 0 && side != lastSide;
                if (side != 0)
                {
                    if (reversed)
                        lastSideTick = t;
                    lastSide = side;
                }

                if ((c.Buttons & Attack) == 0 || AiFightMotion.Motion(own, t) is not { } velocity)
                    continue;
                firing++;
                bool moved = c.ForwardMove != 0f || c.SideMove != 0f;
                commanded += moved ? 1 : 0;
                if (!moved || velocity.Length2D >= AiFightMotion.MovingSpeed)
                    continue;
                stillCommanded++;
                if (side == 0)
                    continue;
                stillSide++;
                AiVector origin = FrameAt(own, t)!.Value.Origin;
                float yaw = c.View.Yaw * (MathF.PI / 180f);
                var way = new AiVector(MathF.Sin(yaw) * side, -MathF.Cos(yaw) * side, 0f);
                bool turning = t - lastSideTick < ReversalTicks;
                StillCause cause = Cause(graph, doors, frames, b, t, c.Buttons, velocity, origin, way, turning);
                SideFloor floor = SideRoom.Ahead(graph, doors, origin, way, ClearanceClass.Crouch);
                ticks.Add(new StillTick(b, t, origin, cause, turning, t - lastJump < JumpedTicks, floor));
            }
        }

        return new FightStillReport(firing, commanded, stillCommanded, stillSide, ticks);
    }

    private static StillCause Cause(NavGraph graph, NavObstacleState doors, Dictionary<ActorId, SortedList<int, AiActorFrame>> frames, ActorId self, int tick,
                                    int buttons, AiVector velocity, AiVector origin, AiVector way, bool turning)
    {
        if ((buttons & Duck) != 0)
            return StillCause.Crouched;
        if (MathF.Abs(velocity.Z) > AirSpeed)
            return StillCause.InTheAir;
        SideFloor floor = SideRoom.Ahead(graph, doors, origin, way, ClearanceClass.Crouch);
        if (floor is SideFloor.Wall)
            return StillCause.Wall;
        if (floor is SideFloor.Drop)
            return StillCause.Drop;
        if (turning)
            return StillCause.Reversal;
        AiVector step = origin + way * SideRoom.StepUnits;
        foreach ((ActorId other, SortedList<int, AiActorFrame> at) in frames)
        {
            if (other != self && FrameAt(at, tick) is { } f && MathF.Abs(f.Origin.Z - origin.Z) < 72f
                && new AiVector(f.Origin.X - step.X, f.Origin.Y - step.Y, 0f).Length < BodyUnits)
                return StillCause.Body;
        }

        return floor is SideFloor.OffGraph ? StillCause.OffGraph : StillCause.Else;
    }

    /// <summary>The frame at <paramref name="tick"/> or within four before, as <see cref="AiFightMotion.Motion"/> reads it.</summary>
    private static AiActorFrame? FrameAt(SortedList<int, AiActorFrame> frames, int tick)
    {
        for (int t = tick; t >= tick - 4; t--)
        {
            if (frames.TryGetValue(t, out AiActorFrame f))
                return f;
        }

        return null;
    }

    /// <summary>The report's text: the chain's counts, each cause's share of the still sidestepping ticks, and the <paramref name="places"/> places with most wall ticks (64-unit squares).</summary>
    public static string Table(FightStillReport report, int places = 3)
    {
        ArgumentNullException.ThrowIfNull(report);
        var w = new StringBuilder();
        double Share(int n, int of) => of > 0 ? n / (double)of : double.NaN;
        w.Append(CultureInfo.InvariantCulture,
                 $"fightstill: firing {report.FiringTicks} commanded {report.CommandedTicks} ({Share(report.CommandedTicks, report.FiringTicks):P1}) still while commanded {report.StillCommanded} ({Share(report.StillCommanded, report.FiringTicks):P1} of firing) of which sidestepping {report.StillSidestep}\n");
        foreach (StillCause cause in Enum.GetValues<StillCause>())
        {
            int n = report.Count(cause);
            w.Append(CultureInfo.InvariantCulture, $"fightstill: {cause.ToString().ToLowerInvariant()} {n} ({Share(n, report.StillSidestep):P1} of still sidestepping, {Share(n, report.FiringTicks):P1} of firing)\n");
        }

        int wallTurning = report.Ticks.Count(t => t.Cause == StillCause.Wall && t.Reversed);
        w.Append(CultureInfo.InvariantCulture, $"fightstill: wall ticks also within a reversal {wallTurning}\n");
        List<StillTick> air = report.Ticks.Where(t => t.Cause == StillCause.InTheAir).ToList();
        w.Append(CultureInfo.InvariantCulture,
                 $"fightstill: in the air {air.Count}: a jump pressed within {JumpedTicks} ticks {air.Count(t => t.Jumped)}, a wall or drop a step to the side {air.Count(t => t.Floor is SideFloor.Wall or SideFloor.Drop)}\n");
        foreach (var place in report.Ticks.Where(t => t.Cause == StillCause.Wall)
                                       .GroupBy(t => ((int)MathF.Floor(t.Origin.X / 64f), (int)MathF.Floor(t.Origin.Y / 64f)))
                                       .OrderByDescending(g => g.Count()).Take(places))
        {
            StillTick first = place.First();
            w.Append(CultureInfo.InvariantCulture,
                     $"fightstill: wall place ({place.Key.Item1 * 64 + 32} {place.Key.Item2 * 64 + 32} {first.Origin.Z:F0}) {place.Count()} ticks, bots {string.Join(",", place.Select(t => t.Bot.Slot).Distinct().Order())}, first slot {first.Bot.Slot} tick {first.Tick}\n");
        }

        return w.ToString();
    }
}
