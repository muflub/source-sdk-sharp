using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Server.Horde.Motion;
using TF2.Descent.Server.Horde.Session;

namespace TF2.Descent.Server.Horde.Nav;

/// <summary>
/// <c>tf2_descent_horde_nav</c>: the level's navigation: its state, the graph (cells, edges,
/// obstacles), the cells each robot class stands on, how long the derivation took and when the
/// graph was handed over, the static-prop probe, the brains' searches and their cost, the cache
/// (none: derived each load), and the stepper the bodies run on.
/// </summary>
[ConCommand("tf2_descent_horde_nav", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_nav: the level's navigation graph, its classes, derivation time, prop probe, path searches and the motion's stepper")]
public sealed class HordeNavCommand(HordeNav nav, RobotMotion motion, ILog log)
{
    public const string Tag = "[TF2Descent/Horde] nav:";

    public void Execute()
    {
        var p = nav.Paths;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"{Tag} {nav.State} on {(nav.Map.Length == 0 ? "no level" : nav.Map)}; levels {nav.Levels}, derived {nav.Derived}, dropped stale {nav.DroppedStale}, threads alive {nav.ThreadsAlive}; cache none (derived each load: the AI's graph cache is in Ai.Server)\n"));
        if (nav.Data is { } d)
        {
            var classes = new StringBuilder();
            for (int c = 0; c < 2 + HordeHulls.Scales.Length; c++)
            {
                if (c == (int)ClearanceClass.Crouch)
                    continue;
                classes.Append(CultureInfo.InvariantCulture, $" {HordeHulls.NameOf((ClearanceClass)c)} {d.CellsOf((ClearanceClass)c)}");
            }

            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"{Tag} graph {d.Graph.CellCount} cells, {d.Graph.EdgeCount} edges, {d.Graph.ObstacleCount} obstacles ({nav.ArenaBlocking} arena brush(es) blocking, the rest open); cells by class:{classes}; derived in {d.DeriveMs:0} ms (+{d.ColumnsMs:0} ms voxels {d.Columns.Width}x{d.Columns.Depth}); ready {nav.TicksToReady} ticks after the level began; epoch {p.Epoch:x8}\n"));
        }

        if (nav.Data is { } a && p.Obstacles is { } now)
        {
            var arena = new StringBuilder();
            for (int o = 0; o < a.Nav.Obstacles.Length && o < now.Count; o++)
            {
                var info = a.Nav.Obstacles[o];
                if (info.Kind == SourceSharp.Ai.Server.Nav.NavObstacleKind.Toggle && HordeArenaObstacles.RoleOf(info.Name) != ArenaBrushRole.None)
                    arena.Append(CultureInfo.InvariantCulture, $" {info.Name} {(now.Blocking(o) ? "BLOCKS" : "open")};");
            }

            if (arena.Length > 0)
                log.Msg($"{Tag} arena:{arena}\n");
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"{Tag} props:{nav.Probed} of {nav.Candidates} cells probed, {nav.Blocked} blocked, {nav.ProbeMicros:0} us on the game thread\n"));
        double mean = p.Searches > 0 ? p.Micros / p.Searches : 0;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"{Tag} searches {p.Searches}: found {p.Found}, nearest place of the floor {p.Nearest}, unreachable {p.Unreachable}, out of budget {p.OutOfBudget}; {mean:0} us each on the workers (longest {p.MostMicros:0} us), {p.CellsOnPaths} cells on found paths; components under the arena made {p.JoinsWindowed} by windows and {p.JoinsWhole} whole, {p.JoinMicros:0} us in all on the workers (longest {p.MostJoinMicros:0} us)\n"));
        var s = motion.Stepper;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"{Tag} stepper {nav.StepperName} ({s?.GetType().Name}): {s?.StepsRun} steps at {(s is { StepsRun: > 0 } r ? r.StepMicros / r.StepsRun : 0):0.0} us cpu each; switches {motion.StepperSwitches}\n"));
    }
}

/// <summary>
/// <c>tf2_descent_horde_path &lt;robot&gt;</c>: a robot's path: the corners its brain's walker holds
/// from where the body is (read without waiting: "busy" while its decide runs), the walker's
/// counts, and the corners of the order its motion carries out now.
/// </summary>
[ConCommand("tf2_descent_horde_path", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_path <robot>: a robot's path corners (its brain's walker) and its motion's order")]
public sealed class HordePathCommand(HordeSession session, RobotMotion motion, ILog log)
{
    public void Execute(int robot)
    {
        if (session.Brains is not { } b || session.Horde?.Roster is not { } roster || (uint)robot >= (uint)roster.Count)
        {
            log.Msg($"{HordeNavCommand.Tag} path: no robot {robot}\n");
            return;
        }

        int body = roster.Body(robot);
        if (body == 0 || !b.BrainAt(body).IsValid)
        {
            log.Msg($"{HordeNavCommand.Tag} path: robot {robot} ({roster.State(robot)}) has no body\n");
            return;
        }

        string walker = b.Session.TryReadBrain(body, br => br.Decider is IHordeRouted { Route: { } r } ? Describe(r) : "no walker", out string? read)
            ? read! : "busy";
        var at = roster.Position(robot);
        string order = motion.TryView(body, out var view) && view.Ordered
            ? $"{view.Move.Kind} {Corners(view.Move)} (corner {view.Corner})"
            : "none";
        var arrived = motion.Arrived(body);
        string arrivals = arrived.Ever
            ? string.Create(CultureInfo.InvariantCulture, $"; arrived {arrived.Count} (last at ({arrived.At.X:0},{arrived.At.Y:0},{arrived.At.Z:0}) tick {arrived.Tick})")
            : "; arrived 0";
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"{HordeNavCommand.Tag} path: robot {robot} body {body} at ({at.X:0},{at.Y:0},{at.Z:0}) class {HordeHulls.NameOf(HordeHulls.ClassOf((float)roster.Scale(robot)))}: walker {walker}; motion order {order}{arrivals}\n"));
    }

    private static string Describe(HordeRoute r)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{(r.HasPath ? "on the graph" : r.Held ? "HELD at the nearest place of its floor (the goal is off it)" : "room route")}, searches {r.Searches}, moves on graph {r.OnGraph} by rooms {r.ByRooms}, holds {r.Holds}; corners");
        foreach (AiVector c in r.Left.ToArray())
            sb.Append(CultureInfo.InvariantCulture, $" ({c.X:0},{c.Y:0},{c.Z:0})");
        return sb.ToString();
    }

    private static string Corners(in MoveIntent move)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < move.CornerCount; i++)
        {
            var c = move.Corner(i);
            sb.Append(CultureInfo.InvariantCulture, $"({c.X:0},{c.Y:0},{c.Z:0}) ");
        }

        return sb.ToString().TrimEnd();
    }
}
