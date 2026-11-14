using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Skills;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// <c>tf2_descent_robot_status &lt;robot&gt; &lt;condition&gt; &lt;seconds&gt; [player]</c>: a skill's
/// condition word on a robot row's body, through the real sink (<see cref="RobotStatusEffects.Apply"/>),
/// by the player in <c>player</c> (none: by no player), for every robot (<c>enemies</c>).
/// </summary>
[ConCommand("tf2_descent_robot_status", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_robot_status <robot> <condition> <seconds> [player]: put a skill's condition (stun, emp_stun, slowed, burn, bleed, mini_crit_marked, knock_up) on a robot through the horde's status sink")]
public sealed class RobotStatusCommand(IHordeCurrent horde, RobotStatusEffects sink, IEnginePlayers players, ILog log)
{
    public void Execute(int robot, string condition, float seconds, int player = 0)
    {
        if (RobotStatusLines.Body(horde, robot, out string why) is not { } body)
        {
            log.Msg($"[TF2Descent/Status] robot_status: {why}\n");
            return;
        }

        var hero = player > 0 ? players.GetHandle(player) : EntityHandle.None;
        var answer = sink.Apply(body.Handle, condition, seconds, hero, "dev", "enemies");
        log.Msg(string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Status] robot_status: {condition} on robot {robot} for {seconds:0.##}s by {(player > 0 ? "player " + player : "no player")}: {answer}\n"));
        log.Msg($"[TF2Descent/Status] {sink.Line(robot)}\n");
    }
}

/// <summary>
/// <c>tf2_descent_robot_statuses [robot]</c>: each robot's statuses with their end ticks and
/// appliers, its slow, stun and damage-taken multiplier, a taunt and a debuff, its immunity,
/// resistances and the attacker level a hero's armour is measured against; with no robot, every
/// robot that carries a status or a body.
/// </summary>
[ConCommand("tf2_descent_robot_statuses", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_robot_statuses [robot]: robots' statuses, end ticks, immunity and resistances")]
public sealed class RobotStatusesCommand(IHordeCurrent horde, RobotStatusEffects sink, IAttackerLevel levels, ILog log)
{
    public void Execute(int robot = -1)
    {
        if (horde.Current is not { Roster: { } roster, Bodies: { } bodies })
        {
            log.Msg("[TF2Descent/Status] no horde this level\n");
            return;
        }

        int shown = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            if (robot >= 0 ? i != robot : roster.StatusBits(i) == 0 && roster.Body(i) == 0)
                continue;
            int b = roster.Body(i);
            string level = b != 0 ? $"; attacker level {levels.LevelOf(bodies.Body(b).Handle)}" : "";
            log.Msg($"[TF2Descent/Status] {sink.Line(i)}{level}\n");
            shown++;
        }

        log.Msg($"[TF2Descent/Status] {shown} robot(s) of {roster.Count}; status ticks dealt to bodies {sink.TicksDealt}, asleep rows ticked {sink.AsleepTicks}\n");
    }
}

/// <summary>
/// <c>tf2_descent_robot_hurt &lt;robot&gt; &lt;type&gt; &lt;amount&gt; [player]</c>: a hit of a damage
/// type (fire, blast, bullet, shock) on a robot's body by the player in <c>player</c>, dealt as a
/// status's tick is (<see cref="IRobotStatusDamage"/>: the engine's damage path, the body taking
/// the type and amount given), so resistance, immunity and IMMUNE show without the right weapon.
/// </summary>
[ConCommand("tf2_descent_robot_hurt", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_robot_hurt <robot> <fire|blast|bullet|shock> <amount> [player]: a typed hit on a robot's body through the hit path")]
public sealed class RobotHurtCommand(IHordeCurrent horde, IRobotStatusDamage damage, IHordeRules rules, RobotHitTally tally, ILog log)
{
    public void Execute(int robot, string type, float amount, int player = 0)
    {
        if (RobotStatusLines.Body(horde, robot, out string why) is not { } body)
        {
            log.Msg($"[TF2Descent/Status] robot_hurt: {why}\n");
            return;
        }

        if (!rules.Rules.Content.DamageTypes.Contains(type))
        {
            log.Msg($"[TF2Descent/Status] robot_hurt: '{type}' is not a damage type (fire, blast, bullet, shock)\n");
            return;
        }

        int before = tally.Hits;
        damage.Deal(horde.Current!.Bodies!, body, player, amount, type, "dev_hurt");
        log.Msg(tally.Hits > before && tally.Last is { } h
            ? string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Status] robot_hurt: robot {robot} took {h.Taken:0.###} of {h.Dealt:0.###} {h.DamageType} from {(player > 0 ? "player " + player : "no player")}; immune {h.Immune}, left {h.LifeLeft:0.###}, killed {h.Killed}\n")
            : $"[TF2Descent/Status] robot_hurt: robot {robot}: the hit reached no body\n");
    }
}

/// <summary>The dev commands' shared reads.</summary>
public static class RobotStatusLines
{
    /// <summary>The lent, living body of roster row <paramref name="robot"/>; null with why not.</summary>
    public static RobotBody? Body(IHordeCurrent horde, int robot, out string why)
    {
        if (horde.Current is not { Roster: { } roster, Bodies: { } bodies })
        {
            why = "no horde this level";
            return null;
        }

        if ((uint)robot >= (uint)roster.Count)
        {
            why = $"no robot {robot}: the roster has {roster.Count}";
            return null;
        }

        if (roster.State(robot) != RobotState.Embodied || roster.Body(robot) == 0)
        {
            why = $"robot {robot} is {roster.State(robot)} with no body (lend it one: tf2_descent_horde_lend {robot})";
            return null;
        }

        why = "";
        return bodies.Body(roster.Body(robot));
    }
}
