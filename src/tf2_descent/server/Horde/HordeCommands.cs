using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// <c>tf2_descent_horde [robot]</c>: the roster by state, the pool lent and
/// free, the sight and decision counters and the census; with a robot row,
/// that robot and its body.
/// </summary>
[ConCommand("tf2_descent_horde", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde [robot]: the horde's roster, pool and census; with a robot row, that robot and its body")]
public sealed class HordeCommand(HordeService horde, HordeCensus census, RobotHitTally hits, Session.HordeSession ai, Attacks.RobotAttacks attacks,
                                  IHordeClock clock, ILog log)
{
    public void Execute(int robot = -1)
    {
        if (horde.Current is not { Begun: true } c)
        {
            log.Msg("[TF2Descent/Horde] no horde this level\n");
            return;
        }

        var roster = c.Roster!;
        var pool = c.Pool!;
        if (robot >= 0)
        {
            log.Msg($"[TF2Descent/Horde] {HordeLines.Robot(c, robot)}\n");
            return;
        }

        int asleep = 0, far = 0, embodied = 0, dead = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            switch (roster.State(i))
            {
                case RobotState.Asleep: asleep++; break;
                case RobotState.Far: far++; break;
                case RobotState.Embodied: embodied++; break;
                case RobotState.Dead: dead++; break;
            }
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] controller #{horde.ControllersMade} entity {c.Handle.EntryIndex}: {roster.Count} robots in {roster.PackCount} packs ({asleep} asleep, {far} far, {embodied} embodied, {dead} dead); ");
        sb.Append(CultureInfo.InvariantCulture,
            $"pool {pool.LastBody} bodies, lent {pool.LastBody - FreeBodies(pool)}, free {FreeBodies(pool)} (pool free {pool.FreePoolBodies}); ");
        sb.Append(CultureInfo.InvariantCulture,
            $"tick {clock.Tick}, ticks {c.Ticks}, decisions {c.Decisions} (every {c.DecideInterval}), far ticks {c.FarTicks} (every {c.FarInterval}), heroes {c.Heroes}; ");
        sb.Append(CultureInfo.InvariantCulture,
            $"sight traces {c.Sight!.Traces} (last tick {c.Sight.TracesThisTick} of {c.Sight.TraceBudget}, over budget {c.Sight.OverBudgetThisTick}); ");
        sb.Append(CultureInfo.InvariantCulture, $"hand lends {c.HandLends}, returns {c.HandReturns}");
        if (hits is RobotHitTally tally)
        {
            sb.Append(CultureInfo.InvariantCulture, $"; hits {tally.Hits}, kills {tally.Kills}");
            if (tally.Last is { } h)
                sb.Append(CultureInfo.InvariantCulture,
                    $"; last hit {h.Robot} body {h.Body} dealt {h.Dealt:0.###} {h.DamageType} ({h.Bits}) took {h.Taken:0.###} left {h.LifeLeft:0.###} killed {h.Killed} group {h.HitGroup}");
        }
        log.Msg(sb.Append('\n').ToString());
        log.Msg($"[TF2Descent/Horde] {census.Line(c)}\n");
        log.Msg($"[TF2Descent/Horde] {ai.Line()}\n");
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] attacks: asked {attacks.TotalAsked}, fired {attacks.TotalFired}, projectiles alive {attacks.Flying} (most {attacks.MostFlying}), healed {attacks.Healed:0.#} (tf2_descent_horde_attacks: by family)\n"));
        for (int r = 0; r < c.Level!.RoomCount; r++)
            log.Msg($"[TF2Descent/Horde]   room {r}: {(roster.RoomAwake(r) ? "awake" : "asleep")}, {c.Level.Rooms[r].Markers.Length} markers\n");
    }

    private static int FreeBodies(TF2.Descent.Ai.Bodies.BodyPool pool)
    {
        int n = 0;
        for (int b = 1; b <= pool.LastBody; b++)
            if (pool.IsFree(b)) n++;
        return n;
    }
}

/// <summary>The dev commands' lines.</summary>
public static class HordeLines
{
    public static string Robot(HordeController c, int i)
    {
        var roster = c.Roster!;
        if ((uint)i >= (uint)roster.Count)
            return $"no robot {i}: the roster has {roster.Count}";

        var p = roster.Position(i);
        var line = FormattableString.Invariant(
            $"robot {i} {roster.Id(i)} {roster.Template(i)} scale {roster.Scale(i):0.000} level {roster.MonsterLevel(i)} {roster.State(i)} room {roster.Room(i)} at ({p.X:0.0},{p.Y:0.0},{p.Z:0.0}) yaw {roster.Yaw(i):0} life {roster.Life(i):0.###} of {roster.Stats(i).Life:0.###} champion {roster.IsChampion(i)} body {roster.Body(i)}");
        int b = roster.Body(i);
        if (b == 0)
            return line;

        var body = c.Bodies!.Body(b);
        return line + FormattableString.Invariant(
            $"; body entity {body.Handle.EntryIndex} health {body.EngineHealth} life byte {body.State.Life} flags {body.State.Flags} model {body.Model} scale {body.Scale:0.000} seq {body.State.Sequence} serial {body.State.SequenceSerial} running {body.Running}; weapon entity {body.Weapon?.Handle.EntryIndex} shown {body.Weapon?.Shown} {body.Weapon?.Model}");
    }
}

/// <summary><c>tf2_descent_horde_wake &lt;room&gt;</c>: wakes a room; its robots go far, and the rules lend them bodies when a hero is within two rooms.</summary>
[ConCommand("tf2_descent_horde_wake", Flags = ConVarFlags.Cheat, Help = "tf2_descent_horde_wake <room>: wake a room of the horde")]
public sealed class HordeWakeCommand(HordeService horde, ILog log)
{
    public void Execute(int room)
    {
        if (horde.Current is not { Begun: true } c || (uint)room >= (uint)c.Level!.RoomCount)
        {
            log.Msg($"[TF2Descent/Horde] wake: no room {room}\n");
            return;
        }

        c.Roster!.WakeRoom(room);
        log.Msg($"[TF2Descent/Horde] wake: room {room} is awake\n");
    }
}

/// <summary><c>tf2_descent_horde_lend &lt;robot&gt;</c>: lends a robot a body by hand, where it stands.</summary>
[ConCommand("tf2_descent_horde_lend", Flags = ConVarFlags.Cheat, Help = "tf2_descent_horde_lend <robot>: lend a robot row a body where it stands")]
public sealed class HordeLendCommand(HordeService horde, IHordeClock clock, ILog log)
{
    public void Execute(int robot)
    {
        if (horde.Current is not { Begun: true } c)
        {
            log.Msg("[TF2Descent/Horde] lend: no horde this level\n");
            return;
        }

        c.LendByHand(robot, clock.Tick, out string why);
        log.Msg($"[TF2Descent/Horde] lend: {why}\n");
    }
}

/// <summary><c>tf2_descent_horde_return &lt;robot&gt;</c>: takes a robot's body back by hand.</summary>
[ConCommand("tf2_descent_horde_return", Flags = ConVarFlags.Cheat, Help = "tf2_descent_horde_return <robot>: take a robot row's body back")]
public sealed class HordeReturnCommand(HordeService horde, IHordeClock clock, ILog log)
{
    public void Execute(int robot)
    {
        if (horde.Current is not { Begun: true } c)
        {
            log.Msg("[TF2Descent/Horde] return: no horde this level\n");
            return;
        }

        c.ReturnByHand(robot, clock.Tick, out string why);
        log.Msg($"[TF2Descent/Horde] return: {why}\n");
    }
}

/// <summary><c>tf2_descent_horde_cycle &lt;robot&gt; &lt;n&gt;</c>: lends and returns a robot's body n times, one step a tick.</summary>
[ConCommand("tf2_descent_horde_cycle", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_cycle <robot> <n>: lend and return a robot row's body n times, one step a server tick")]
public sealed class HordeCycleCommand(HordeService horde, ILog log)
{
    public void Execute(int robot, [ArgRange(1, 100_000)] int cycles)
    {
        if (horde.Current is not { Begun: true } c)
        {
            log.Msg("[TF2Descent/Horde] cycle: no horde this level\n");
            return;
        }

        c.Cycle(robot, cycles, out string why);
        log.Msg($"[TF2Descent/Horde] cycle: {why}\n");
    }
}

/// <summary><c>tf2_descent_horde_time &lt;start|stop&gt;</c>: times the controller's tick; stop prints the distributions.</summary>
[ConCommand("tf2_descent_horde_time", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_time <start|stop>: time the horde controller's tick (stopwatch and thread CPU)")]
public sealed class HordeTimeCommand(HordeTickTimer timer, HordeService horde, ILog log)
{
    public void Execute(string verb)
    {
        if (verb == "start")
        {
            timer.Start();
            log.Msg("[TF2Descent/Horde] time: started\n");
            return;
        }

        timer.Stop();
        var (wall, cpu) = timer.Report();
        int lent = horde.Current?.Bodies?.Lent ?? 0;
        log.Msg($"[TF2Descent/Horde] time: {lent} lent bodies: tick wall {wall}\n");
        log.Msg($"[TF2Descent/Horde] time: {lent} lent bodies: tick cpu  {cpu}\n");
    }
}

/// <summary>
/// <c>tf2_descent_spawn_robot &lt;template&gt; [scale] [affix] [join]</c>: a new robot on the
/// roster where the player's crosshair meets the world, lent a body at once; with
/// an affix, a champion of that champions.json affix (<c>fireproof</c>: immune to fire;
/// <c>-</c> for none); with join 1 it joins the newest pack (a pack built by hand: a
/// medic and its patient).
/// </summary>
[ConCommand("tf2_descent_spawn_robot", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_spawn_robot <template> [scale] [affix] [join]: put a robot on the roster at the crosshair and lend it a body; scale 0 rolls it; an affix makes it a champion (- for none); join 1 puts it in the newest pack (the last robot spawned's)")]
public sealed class SpawnRobotCommand(HordeService horde, IHordeClock clock, IEnginePlayers players, IEngineEntities entities,
                                      IEngineWorldQuery world, ILog log)
{
    public void Execute(string template, float scale = 0f, string affix = "", int join = 0)
    {
        if (horde.Current is not { Begun: true } c)
        {
            log.Msg("[TF2Descent/Horde] spawn_robot: no horde this level\n");
            return;
        }

        var who = DevContext.WhoTyped(players);
        if (who == EntityHandle.None)
        {
            log.Msg("[TF2Descent/Horde] spawn_robot: no player to aim\n");
            return;
        }

        Vector eye = entities.GetEyePosition(who);
        Vector ang = entities.GetEyeAngles(who);
        float pitch = ang.X * MathF.PI / 180f, yaw = ang.Y * MathF.PI / 180f;
        var forward = new Vector(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
        var hit = world.TraceLine(eye, eye + forward * DevContext.PickRange, TraceMask.Brushes, who);
        var at = hit.End - forward * 32f;
        var down = world.TraceLine(at, at - new Vector(0f, 0f, 512f), TraceMask.Brushes, who);
        var feet = HordeVectors.ToAi(down.End);

        ulong seed = (ulong)clock.Tick * 0x9E3779B97F4A7C15UL;
        var id = c.SpawnByHand(template, scale > 0f ? scale : null, feet, ang.Y + 180f, seed, clock.Tick, out string why,
                               affix.Length > 0 && affix != "-" ? [affix] : null, joinLastPack: join != 0);
        log.Msg(id.IsValid
            ? $"[TF2Descent/Horde] spawn_robot: {id}: {why}; {HordeLines.Robot(c, id.Index)}\n"
            : $"[TF2Descent/Horde] spawn_robot: {why}\n");
    }
}
