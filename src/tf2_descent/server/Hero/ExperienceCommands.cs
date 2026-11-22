using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules;
using TF2.Descent.Server.Horde;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// <c>tf2_descent_xp &lt;slot&gt; &lt;amount&gt;</c>: experience for a hero as
/// a kill pays it (<see cref="HeroExperience.Grant"/>: the rules' level-up,
/// its refill, notice and checkpoint), with no kill behind it.
///
/// <para>
/// The evidence stays honest: unhosted, the grant records the fewest kills of
/// the template that pays most per kill (<see cref="DevTemplate"/>) that could
/// explain the amount, so the pending evidence still passes the rules' bound,
/// and the bestiary counts none of them (no robot died). Hosted it is refused:
/// the host would be sent kills that never happened.
/// </para>
/// </summary>
[ConCommand("tf2_descent_xp", Help = "tf2_descent_xp <player slot> <amount>: give the hero experience as a kill pays it (sv_cheats; unhosted only).")]
public sealed class HeroXpCommand(SvCheatsConVar cheats, Heroes heroes, HeroExperience experience, HostService host,
                                  TF2DescentRules rules, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, [ArgRange(1, 100_000_000)] int amount)
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_xp", slot) is not { } hero)
            return;

        if (host.Mode == HostingMode.Hosted)
        {
            log.Warning("[TF2Descent/Hero] tf2_descent_xp: refused hosted, where the checkpoint would claim kills that never happened\n");
            return;
        }

        var (template, each) = DevTemplate(rules);
        int kills = (int)Math.Min(int.MaxValue, (amount + each - 1) / each);
        var kill = new Dictionary<string, int>(StringComparer.Ordinal) { [template] = kills };
        if (experience.Grant(hero, amount, kill, countKill: null, $"tf2_descent_xp ({kills} {template} as evidence)") is null)
            return;

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Hero] {slot} xp {hero.Sheet!.Xp}, level {hero.Sheet.Level}, points {hero.Sheet.AttributePoints}, skill points {hero.Sheet.SkillPoints}\n"));
    }

    /// <summary>The robot template the most XP a kill can pay is held to is highest for, and that bound.</summary>
    public static (string Template, long Each) DevTemplate(TF2DescentRules rules) =>
        rules.Content.Robots.Rows
            .Select(r => (r.Id, rules.Experience.MostXpPerKill(r.Id)))
            .OrderByDescending(t => t.Item2).ThenBy(t => t.Id, StringComparer.Ordinal)
            .First();
}

/// <summary>
/// <c>tf2_descent_kill_robot &lt;robot&gt; [slot]</c>: kills a roster robot the
/// way it would die in play, so the whole kill chain runs. A robot with a
/// body is hurt through the engine (<see cref="IEngineDamage.TakeDamage"/>) by
/// the player in <c>slot</c> (none: the body itself, a kill by no player), so
/// the body, the roster and the kill seam take it as a hit; one with no body
/// loses its life in the roster and the kill seam's sweep reports it
/// (<see cref="RobotKills.Sweep"/>), as a death no hit reported.
/// </summary>
[ConCommand("tf2_descent_kill_robot", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_kill_robot <robot> [player slot]: kill a horde robot through the normal path (a hit on its body, else the roster's death)")]
public sealed class KillRobotCommand(HordeService horde, RobotKills kills, IEngineDamage damage, IEnginePlayers players, ILog log)
{
    /// <summary>More than any robot's life after any resistance short of immunity.</summary>
    public const float Lethal = 1_000_000f;

    public void Execute(int robot, int slot = 0)
    {
        if (horde.Current is not { Begun: true, Roster: { } roster, Bodies: { } bodies })
        {
            log.Msg("[TF2Descent/Kills] no horde this level\n");
            return;
        }

        if (robot < 0 || robot >= roster.Count || roster.State(robot) is RobotState.Dead or RobotState.Empty)
        {
            log.Warning($"[TF2Descent/Kills] tf2_descent_kill_robot: robot {robot} is not a living row of {roster.Count}\n");
            return;
        }

        uint before = kills.Told;
        string how = Kill(roster, bodies, robot, slot, players, damage);
        kills.Sweep();
        log.Msg($"[TF2Descent/Kills] tf2_descent_kill_robot {robot}: killed by {how}; {kills.Told - before} kill(s) told\n");
    }

    /// <summary>
    /// Kills a living row through the normal path: a lethal hit on its body (by the player in
    /// <paramref name="slot"/>, or none), else (no body, or one the hit did not kill) the roster's
    /// own death, which the next sweep reports. Answers how it died.
    /// </summary>
    internal static string Kill(RobotRoster roster, RobotBodies bodies, int robot, int slot, IEnginePlayers players, IEngineDamage damage)
    {
        string how = "the roster (no body)";
        if (roster.State(robot) == RobotState.Embodied && roster.Body(robot) != 0)
        {
            var attacker = slot > 0 ? players.GetHandle(slot) : EntityHandle.None;
            damage.TakeDamage(bodies.Body(roster.Body(robot)).Handle, attacker, Lethal, DamageType.Generic);
            how = slot > 0 ? $"a hit by player {slot}" : "a hit by no player";
            if (roster.State(robot) != RobotState.Dead)
                how += " it did not die of, then the roster";
        }

        if (roster.State(robot) != RobotState.Dead)
            roster.Hurt(robot, roster.Life(robot));
        return how;
    }
}

/// <summary>
/// <c>tf2_descent_kill_robots &lt;slot&gt; &lt;room|level&gt;</c>: every living robot of the room the
/// hero in the slot stands in, or of the whole level, killed as <c>tf2_descent_kill_robot</c> kills
/// one, each credited to that hero (the admin menu's Kill entries).
/// </summary>
[ConCommand("tf2_descent_kill_robots", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_kill_robots <player slot> <room|level>: kill every living robot of the room the player stands in, or of the level, credited to him")]
public sealed class KillRobotsCommand(HordeService horde, RobotKills kills, IEngineDamage damage, IEnginePlayers players, IEngineEntities entities, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot, string where = "room")
    {
        if (horde.Current is not { Begun: true, Roster: { } roster, Bodies: { } bodies, Level: { } level })
        {
            log.Msg("[TF2Descent/Kills] no horde this level\n");
            return;
        }

        bool all = string.Equals(where, "level", StringComparison.OrdinalIgnoreCase);
        int room = -1;
        if (!all)
        {
            EntityHandle player = players.GetHandle(slot);
            if (player == EntityHandle.None || !entities.IsValid(player))
            {
                log.Warning($"[TF2Descent/Kills] tf2_descent_kill_robots: no player in slot {slot}\n");
                return;
            }
            room = level.RoomAt(HordeVectors.ToAi(entities.GetOrigin(player)));
            if (room < 0)
            {
                log.Warning($"[TF2Descent/Kills] tf2_descent_kill_robots: player {slot} stands in no room\n");
                return;
            }
        }

        uint before = kills.Told;
        var rows = Living(roster, all ? null : room);
        foreach (int robot in rows)
            KillRobotCommand.Kill(roster, bodies, robot, slot, players, damage);
        kills.Sweep();
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Kills] tf2_descent_kill_robots {slot} {(all ? "level" : $"room {room}")}: {rows.Count} robot(s) killed; {kills.Told - before} kill(s) told\n"));
    }

    /// <summary>The living rows (neither dead nor empty), of <paramref name="room"/> only when one is named.</summary>
    public static List<int> Living(RobotRoster roster, int? room)
    {
        var rows = new List<int>();
        for (int i = 0; i < roster.Count; i++)
            if (roster.State(i) is not (RobotState.Dead or RobotState.Empty) && (room is not { } r || roster.Room(i) == r))
                rows.Add(i);
        return rows;
    }
}
