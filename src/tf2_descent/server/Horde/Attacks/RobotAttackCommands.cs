using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Horde.Attacks;

/// <summary>
/// <c>tf2_descent_robot_attack &lt;robot&gt; [times]</c>: a roster robot attacks along its body's
/// current look, through the real hands (<see cref="RobotAttacks"/>), once a tick it is ready,
/// <c>times</c> times.
/// </summary>
[ConCommand("tf2_descent_robot_attack", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_robot_attack <robot or -1 for every lent robot> [times]: a robot attacks along its current look through the horde's attack sink, once each tick it is ready")]
public sealed class RobotAttackCommand(HordeService horde, RobotAttacks attacks, ILog log)
{
    public void Execute(int robot, [ArgRange(1, 1000)] int times = 1)
    {
        if (robot == -1 && horde.Current is { Begun: true, Bodies: { } bodies })
        {
            int asked = 0;
            for (int b = 1; b <= bodies.LastBody; b++)
            {
                if (bodies.Body(b).Parked)
                    continue;
                attacks.AskByHand(b, times);
                asked++;
            }

            log.Msg(string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Horde] robot_attack: {asked} lent robots asked for {times} attacks each\n"));
            return;
        }

        if (horde.Current is not { Begun: true, Roster: { } roster } || (uint)robot >= (uint)roster.Count)
        {
            log.Msg($"[TF2Descent/Horde] robot_attack: no robot {robot}\n");
            return;
        }

        int body = roster.Body(robot);
        if (body == 0)
        {
            log.Msg($"[TF2Descent/Horde] robot_attack: robot {robot} holds no body ({roster.State(robot)})\n");
            return;
        }

        attacks.AskByHand(body, times);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] robot_attack: robot {robot} ({roster.Template(robot)}) body {body}: {times} attacks asked along its look\n"));
    }
}

/// <summary><c>tf2_descent_horde_attacks</c>: the hands' counters by family: asked, fired, refused by kind, hero hits and damage; projectiles alive and the cap; the beams.</summary>
[ConCommand("tf2_descent_horde_attacks", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_attacks: the robots' attacks by family: asked, fired, refused by kind, hits on heroes, damage; projectiles alive of the cap")]
public sealed class HordeAttacksCommand(RobotAttacks attacks, ILog log)
{
    public void Execute()
    {
        foreach (string line in attacks.Lines())
            log.Msg($"[TF2Descent/Horde] attacks: {line}\n");
    }
}

/// <summary>
/// <c>tf2_descent_robot_drain &lt;robot&gt; &lt;life&gt;</c>: takes life from a roster robot as
/// data (no hit, no hit chain, no push), its body's health bar following: a medic's patient for
/// a live proof.
/// </summary>
[ConCommand("tf2_descent_robot_drain", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_robot_drain <robot> <life>: take life from a horde robot as data (no hit), its health bar following")]
public sealed class RobotDrainCommand(HordeService horde, ILog log)
{
    public void Execute(int robot, float life)
    {
        if (horde.Current is not { Begun: true, Roster: { } roster, Bodies: { } bodies } || (uint)robot >= (uint)roster.Count)
        {
            log.Msg($"[TF2Descent/Horde] robot_drain: no robot {robot}\n");
            return;
        }

        double most = roster.Stats(robot).Life;
        double left = Math.Max(1, roster.Life(robot) - life);
        roster.Hurt(robot, roster.Life(robot) - left);
        if (roster.Body(robot) is var b && b != 0)
            bodies.Body(b).Healed(roster.Life(robot), most);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Horde] robot_drain: robot {robot} life {roster.Life(robot):0.#} of {most:0.#}\n"));
    }
}
