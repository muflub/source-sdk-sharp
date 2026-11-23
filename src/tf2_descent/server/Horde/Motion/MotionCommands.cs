using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Motion;
using TF2.Descent.Ai.Roster;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>The dev drive's shared lookups: a robot row's body, and the motion's lines.</summary>
public static class MotionLines
{
    public const string Tag = "[TF2Descent/Motion]";

    /// <summary>The body robot row <paramref name="robot"/> holds, or 0 with why not.</summary>
    public static int BodyOf(HordeController c, int robot, out string why)
    {
        var roster = c.Roster!;
        if ((uint)robot >= (uint)roster.Count)
        {
            why = $"no robot {robot}: the roster has {roster.Count}";
            return 0;
        }

        int body = roster.Body(robot);
        why = body == 0 ? $"robot {robot} ({roster.State(robot)}) holds no body" : "";
        return body;
    }

    /// <summary>One body's motion on a line.</summary>
    public static string Body(RobotMotion motion, int body, long now)
    {
        if (!motion.TryView(body, out var v))
            return $"body {body}: no motion";

        var m = v.Move;
        string order = !v.Ordered
            ? "no order"
            : FormattableString.Invariant(
                $"{m.Kind} {m.Speed} corner {v.Corner}/{m.CornerCount}{(m.CornerCount > 0 && v.Corner < m.CornerCount ? $" next ({m.Corner(v.Corner).X:0},{m.Corner(v.Corner).Y:0},{m.Corner(v.Corner).Z:0})" : "")} look {v.Look.Kind} {(v.Expires == long.MaxValue ? "never expires" : now >= v.Expires ? "EXPIRED" : $"expires in {v.Expires - now}")}");
        var b = v.Blocked;
        string blocked = b.Blocked ? FormattableString.Invariant($"BLOCKED by {b.Kind} {b.Who} since tick {b.Since} for {b.For(now)} ticks ({b.Ticks} blocked)") : "not blocked";
        var l = v.Landed;
        string landed = l.Ever ? FormattableString.Invariant($"last landed tick {l.Tick} after {l.AirTicks} ticks up, fall {l.FallSpeed:0} u/s, rise {l.PeakRise:0.0}") : "never landed";
        return FormattableString.Invariant(
            $"body {body}: at ({v.Position.X:0.0},{v.Position.Y:0.0},{v.Position.Z:0.00}) vel ({v.Velocity.X:0},{v.Velocity.Y:0},{v.Velocity.Z:0}) speed {v.Velocity.Length2D:0} {(v.OnGround ? "ground" : $"AIR since {v.AirborneSince}")} facing {v.Facing:0.0} gait {v.Gait}; {order}; route left {motion.RouteLeft(body)}; {blocked}; {landed}");
    }
}

/// <summary><c>tf2_descent_horde_goto &lt;robot&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt;</c>: walks a robot's body to a point, through the room graph's doors.</summary>
[ConCommand("tf2_descent_horde_goto", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_goto <robot> <x> <y> <z>: walk a robot's body to a point through the level's doors")]
public sealed class HordeGotoCommand(HordeService horde, RobotMotion motion, ILog log)
{
    public void Execute(int robot, float x, float y, float z)
    {
        if (horde.Current is not { Begun: true } c)
        {
            log.Msg($"{MotionLines.Tag} goto: no horde this level\n");
            return;
        }

        int body = MotionLines.BodyOf(c, robot, out string why);
        if (body == 0)
        {
            log.Msg($"{MotionLines.Tag} goto: {why}\n");
            return;
        }

        log.Msg($"{MotionLines.Tag} goto: robot {robot} {HordeGo.To(c, motion, body, new AiVector(x, y, z))}\n");
    }
}

/// <summary><c>tf2_descent_horde_goto_hero &lt;robot|-1&gt;</c>: walks a robot's body (every lent body for -1) to the hero who typed it.</summary>
[ConCommand("tf2_descent_horde_goto_hero", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_goto_hero <robot|-1>: walk a robot's body (-1: every lent body) to the hero through the level's doors, stopping short of him")]
public sealed class HordeGotoHeroCommand(HordeService horde, RobotMotion motion, IEnginePlayers players, IEngineEntities entities,
                                         IHordeHeroes heroes, ILog log)
{
    private readonly HeroPlace[] _places = new HeroPlace[HordeController.MaxHeroes];
    private readonly HeroEye[] _eyes = new HeroEye[HordeController.MaxHeroes];

    public void Execute(int robot = -1)
    {
        if (horde.Current is not { Begun: true } c)
        {
            log.Msg($"{MotionLines.Tag} goto_hero: no horde this level\n");
            return;
        }

        AiVector hero;
        var who = DevContext.WhoTyped(players);
        if (who != EntityHandle.None)
        {
            hero = HordeVectors.ToAi(entities.GetOrigin(who));
        }
        else if (heroes.Gather(c.Level!, _places, _eyes) > 0)
        {
            hero = _places[0].Position;
        }
        else
        {
            log.Msg($"{MotionLines.Tag} goto_hero: no hero\n");
            return;
        }

        if (robot >= 0)
        {
            int body = MotionLines.BodyOf(c, robot, out string why);
            log.Msg(body == 0
                ? $"{MotionLines.Tag} goto_hero: {why}\n"
                : $"{MotionLines.Tag} goto_hero: robot {robot} {HordeGo.ToHero(c, motion, body, hero)}\n");
            return;
        }

        int n = 0;
        for (int b = 1; b <= c.Bodies!.LastBody; b++)
        {
            if (!motion.TryBody(b, out _))
                continue;
            HordeGo.ToHero(c, motion, b, hero);
            n++;
        }

        log.Msg(FormattableString.Invariant($"{MotionLines.Tag} goto_hero: {n} bodies walk to the hero at ({hero.X:0},{hero.Y:0},{hero.Z:0})\n"));
    }
}

/// <summary>The dev drive's routes: to a point, or to a hero, stopping short of him by the two hulls and a gap.</summary>
public static class HordeGo
{
    /// <summary>The gap a body stops short of a hero by, beyond the two hulls' half-widths.</summary>
    public const float HeroGap = 16f;

    public static string To(HordeController c, RobotMotion motion, int body, AiVector to)
    {
        var from = c.Bodies!.Body(body).Position;
        var corners = new List<AiVector>();
        DoorRoute.Build(c.Level!, from, to, corners);
        if (corners.Count == 0 || !motion.Route(body, corners))
            return FormattableString.Invariant($"(body {body}): no route from ({from.X:0},{from.Y:0},{from.Z:0}) to ({to.X:0},{to.Y:0},{to.Z:0})");

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"(body {body}) from ({from.X:0},{from.Y:0},{from.Z:0}) room {DoorRoute.RoomOf(c.Level!, from)} to room {DoorRoute.RoomOf(c.Level!, to)}: {corners.Count} corners");
        foreach (var k in corners)
            sb.Append(CultureInfo.InvariantCulture, $" ({k.X:0},{k.Y:0},{k.Z:0})");
        return sb.ToString();
    }

    public static string ToHero(HordeController c, RobotMotion motion, int body, AiVector hero)
    {
        var b = c.Bodies!.Body(body);
        float stop = RobotHull.Maxs.X * b.Scale + RobotHull.Maxs.X + HeroGap;
        var corners = new List<AiVector>();
        DoorRoute.Build(c.Level!, b.Position, hero, corners);
        if (corners.Count == 0)
            return $"(body {body}): no route to the hero";

        // The last leg ends short of the hero, on the line it comes in along.
        var from = corners.Count > 1 ? corners[^2] : b.Position;
        var dir = MotionSteering.Flat(hero - from);
        corners[^1] = hero - dir * stop;
        return To(c, motion, body, corners[^1]);
    }
}

/// <summary><c>tf2_descent_horde_motion [robot]</c>: the motion's state, every lent body's (or one robot's) order, reports and costs.</summary>
[ConCommand("tf2_descent_horde_motion", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_motion [robot]: the bodies' motion: orders, Blocked and Landed reports, costs")]
public sealed class HordeMotionCommand(HordeService horde, RobotMotion motion, RobotBlastPush blasts, IHordeClock clock, ILog log)
{
    public void Execute(int robot = -1)
    {
        if (horde.Current is not { Begun: true } c)
        {
            log.Msg($"{MotionLines.Tag} no horde this level\n");
            return;
        }

        long now = clock.Tick;
        var stepper = motion.Stepper;
        double perStep = stepper is { StepsRun: > 0 } s ? s.StepMicros / s.StepsRun : 0;
        log.Msg(FormattableString.Invariant(
            $"{MotionLines.Tag} stepper {stepper?.GetType().Name}: steps {stepper?.StepsRun} at {perStep:0.0} us cpu each; ticks {motion.Ticks}, orders {motion.OrdersTaken} (dropped {motion.OrdersDropped}), pushes {motion.Pushes} (blasts {blasts.Pushed}, last ({blasts.LastPush.X:0},{blasts.LastPush.Y:0},{blasts.LastPush.Z:0})), clips {motion.Clips}, slides {motion.Slides}, holds {motion.Holds}\n"));

        if (robot >= 0)
        {
            int body = MotionLines.BodyOf(c, robot, out string why);
            log.Msg(body == 0 ? $"{MotionLines.Tag} {why}\n" : $"{MotionLines.Tag} robot {robot} {MotionLines.Body(motion, body, now)}\n");
            return;
        }

        for (int b = 1; b <= c.Bodies!.LastBody; b++)
            if (motion.Moving(b))
                log.Msg($"{MotionLines.Tag} robot {c.Bodies.Body(b).Robot.Index} {MotionLines.Body(motion, b, now)}\n");
    }
}

/// <summary><c>tf2_descent_horde_motion_time &lt;start|stop&gt;</c>: measures the motion's main-thread cost a tick.</summary>
[ConCommand("tf2_descent_horde_motion_time", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_motion_time <start|stop>: time the bodies' motion on the main thread (stopwatch, thread CPU, step age)")]
public sealed class HordeMotionTimeCommand(RobotMotion motion, ILog log)
{
    public void Execute(string verb)
    {
        if (verb == "start")
        {
            motion.Costs.Start();
            log.Msg($"{MotionLines.Tag} time: started\n");
            return;
        }

        motion.Costs.Stop();
        log.Msg($"{MotionLines.Tag} time: {motion.Costs.Report()}\n");
    }
}

/// <summary><c>tf2_descent_horde_motion_audit &lt;start|stop&gt;</c>: each tick, tests every moving body's hull where it was put, and counts the ticks it was in solid.</summary>
[ConCommand("tf2_descent_horde_motion_audit", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_motion_audit <start|stop|show>: count the ticks each body's hull is inside solid where its motion put it")]
public sealed class HordeMotionAuditCommand(RobotMotion motion, ILog log)
{
    public void Execute(string verb)
    {
        if (verb == "start")
        {
            motion.Audit.Start();
            log.Msg($"{MotionLines.Tag} audit: started\n");
            return;
        }

        if (verb == "stop")
            motion.Audit.Stop();
        log.Msg($"{MotionLines.Tag} audit: {motion.Audit.Report()}\n");
    }
}
