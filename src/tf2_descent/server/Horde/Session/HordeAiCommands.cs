using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Horde.Session;

/// <summary>
/// <c>tf2_descent_horde_ai &lt;robot&gt;</c>: a robot row's brain: its actor and
/// body, what its decider is doing (read without waiting: "busy" while a step
/// of it runs), its senses' parameters and which heroes it holds sight of.
/// </summary>
[ConCommand("tf2_descent_horde_ai", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_ai <robot>: a robot row's brain: what its decider does, its senses, the heroes it sees")]
public sealed class HordeAiCommand(HordeSession session, HordeService horde, ILog log)
{
    public void Execute(int robot)
    {
        if (session.Brains is not { } b || horde.Current?.Roster is not { } roster || (uint)robot >= (uint)roster.Count)
        {
            log.Msg($"[TF2Descent/Horde] ai: no brain for robot {robot}\n");
            return;
        }

        int body = roster.Body(robot);
        if (body == 0 || !b.BrainAt(body).IsValid)
        {
            log.Msg($"[TF2Descent/Horde] ai: robot {robot} ({roster.State(robot)}) has no brain; carried {roster.Carried(robot)}\n");
            return;
        }

        string doing = b.Session.TryReadBrain(body, br => br.Decider is { } d ? $"{d.Doing.Branch}: {d.Doing.Leaf} goal ({d.Doing.Goal.X:0},{d.Doing.Goal.Y:0},{d.Doing.Goal.Z:0}) target {d.Doing.Target}{(d.Doing.Verdict.Length > 0 ? " (" + d.Doing.Verdict + ")" : "")}" : "no decider", out string? read)
            ? read! : "busy";
        var (range, cos, recognize, hearing) = b.SenseOf(body);
        var seen = new System.Text.StringBuilder();
        for (int h = 0; h < Ai.Brains.HordeSlots.Heroes; h++)
            if (b.Vision.Holds(body, h))
                seen.Append(CultureInfo.InvariantCulture, $" {b.HeroSlots.ActorAt(h)}");
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] ai: robot {robot} body {body} brain {b.BrainAt(body)}: {doing}; range {range:0} cone {MathF.Acos(cos) * 180f / MathF.PI:0} deg recognise {recognize} ticks hearing x{hearing:0.00}; sees{(seen.Length == 0 ? " nobody" : seen.ToString())}; carried {roster.Carried(robot)}\n"));
    }
}

/// <summary>
/// <c>tf2_descent_horde_call &lt;robot&gt; [player]</c>: the robot's pack told where the hero in that
/// slot (default 1) stands, however far, as a pack call tells it (<see cref="Ai.Brains.HordeBrains.CallPackTo"/>):
/// its members with a brain go to look, its far and sleeping robots converge (dev: the measure of a
/// hunt along the level's paths from any distance).
/// </summary>
[ConCommand("tf2_descent_horde_call", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_call <robot> [player slot]: tell the robot's pack where that hero stands, however far: its brains go to look, its far robots converge")]
public sealed class HordeCallCommand(HordeSession session, HordeService horde, IEnginePlayers players, IEngineEntities entities, IHordeClock clock, ILog log)
{
    public void Execute(int robot, int slot = 1)
    {
        if (session.Brains is not { } b || horde.Current?.Roster is not { } roster || (uint)robot >= (uint)roster.Count)
        {
            log.Msg($"[TF2Descent/Horde] call: no robot {robot}\n");
            return;
        }

        var hero = players.GetHandle(slot);
        if (hero == EntityHandle.None)
        {
            log.Msg($"[TF2Descent/Horde] call: no player {slot}\n");
            return;
        }

        var at = HordeVectors.ToAi(entities.GetOrigin(hero));
        int told = b.CallPackTo(clock.Tick, robot, slot, at);
        int pack = roster.Pack(robot);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] call: pack {pack} of robot {robot} told player {slot} stands at ({at.X:0},{at.Y:0},{at.Z:0}): {told} brain(s) told, contact room {roster.ContactRoom(pack)}\n"));
    }
}

/// <summary>
/// <c>tf2_descent_horde_ai_time &lt;start|stop&gt;</c>: times the AI session's
/// part of the horde's tick on the game thread (heroes, drain and apply, brain
/// sync, sight, hearing, snapshot and publish), stopwatch and thread CPU; stop
/// prints the distributions, the sight lines and the workers' CPU a decide.
/// </summary>
[ConCommand("tf2_descent_horde_ai_time", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_ai_time <start|stop>: time the horde AI session's main-thread tick (stopwatch and thread CPU)")]
public sealed class HordeAiTimeCommand(HordeSession session, HordeService horde, ILog log)
{
    public void Execute(string verb)
    {
        if (verb == "start")
        {
            session.Timer.Start();
            session.Brains?.Session.Counters.ResetBrainTimes();
            log.Msg("[TF2Descent/Horde] ai time: started\n");
            return;
        }

        session.Timer.Stop();
        var (wall, cpu) = session.Timer.Report();
        int lent = horde.Current?.Bodies?.Lent ?? 0;
        log.Msg($"[TF2Descent/Horde] ai time: {lent} lent bodies: tick wall {wall}\n");
        log.Msg($"[TF2Descent/Horde] ai time: {lent} lent bodies: tick cpu  {cpu}\n");
        if (session.Brains is { } b)
        {
            var c = b.Session.Counters;
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Horde] ai time: workers: {c.StepsRun} decides, {c.BrainMicrosMean:0.0} us wall and {c.BrainCpuMicrosMean:0.0} us cpu a decide (most {c.BrainMicrosMax:0.0} us wall); sight lines {b.Vision.Traces}, most a tick {b.Vision.MostTraces} of {b.Vision.Budget}\n"));
        }
    }
}
