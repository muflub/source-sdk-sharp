using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Hero;

namespace TF2.Descent.Server.Horde.Boss;

/// <summary>What the boss commands share: the level's living boss row and setting a boss's life to a phase.</summary>
internal static class BossDev
{
    /// <summary>The roster's living boss row; -1 for none.</summary>
    public static int LivingBoss(RobotRoster roster)
    {
        for (int i = 0; i < roster.Count; i++)
            if (roster.IsBoss(i) && roster.State(i) is not (RobotState.Dead or RobotState.Empty))
                return i;
        return -1;
    }

    /// <summary>
    /// Sets the boss's life to just inside phase <paramref name="phase"/> (one point under its
    /// threshold; the first phase is full life), the body's health and bar with it. Answers the life.
    /// </summary>
    public static double ToPhase(HordeController c, int row, Rules.Bosses.BossScript script, int phase)
    {
        var roster = c.Roster!;
        double most = roster.Stats(row).Life;
        int p = Math.Clamp(phase, 0, script.Phases.Count - 1);
        double target = p == 0 ? most : Math.Max(1, Math.Floor(most * script.Phases[p].At / 100) - 1);
        double life = roster.Life(row);
        if (target < life)
            roster.Hurt(row, life - target);
        else if (target > life)
            roster.Heal(row, target - life);
        if (roster.Body(row) is int b and > 0)
            c.Bodies!.Body(b).Healed(roster.Life(row), most);
        return roster.Life(row);
    }
}

/// <summary><c>tf2_descent_boss</c>: the level's boss, its fight, its verbs, its adds, its way down and the typing hero's camps.</summary>
[ConCommand("tf2_descent_boss", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_boss: the level's boss: its row, phase and fight, its specials and adds, the Tank, the way down, and your camps")]
public sealed class BossCommand(HordeService horde, BossEncounter encounter, BossAbilities abilities, IBossAdds adds, IBossExit exit, BossFallen fallen,
                                IBossKills kills, IEnginePlayers players, ILog log)
{
    public void Execute()
    {
        if (horde.Current is not { Begun: true } c)
        {
            log.Msg("[TF2Descent/Boss] no horde this level\n");
            return;
        }

        foreach (string line in encounter.Lines(c))
            log.Msg($"[TF2Descent/Boss] {line}\n");
        log.Msg($"[TF2Descent/Boss] {abilities.Line()}\n");
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] adds this level: {(adds as BossAdds)?.Counts(c)} (put, lent, refused); the way down: {(exit.Closed ? "CLOSED" : "open")}, refusals {(exit as BossExit)?.Refusals ?? 0}\n"));
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] all levels: bosses killed {fallen.Kills}, camp records {fallen.Records} (already held {fallen.AlreadyHeld}), exits opened {fallen.Opened}, Tanks wrecked {fallen.Tanks}\n"));
        var who = DevContext.WhoTyped(players);
        if (who != EntityHandle.None)
        {
            int slot = (int)who.EntryIndex;
            var depths = kills.DepthsOf(slot);
            log.Msg($"[TF2Descent/Boss] player {slot}'s boss depths killed [{string.Join(',', depths)}]; the town's cage offers [{string.Join(',', Rules.TownDescents.Destinations(0, depths))}] from depth 0\n");
        }
    }
}

/// <summary>
/// <c>tf2_descent_boss_spawn [boss] [phase]</c>: stands the level's boss (wakes its room and lends
/// it its body where it stands); on a level whose boss is not <c>boss</c> (or that has none), a
/// dev boss of <c>boss</c> at the crosshair, if no other boss stands. With a phase, its life is
/// set inside that phase.
/// </summary>
[ConCommand("tf2_descent_boss_spawn", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_boss_spawn [boss] [phase]: stand the level's boss (or a dev boss of that template at the crosshair), in that phase")]
public sealed class BossSpawnCommand(HordeService horde, IHordeRules rules, IHordeClock clock, IEnginePlayers players, IEngineEntities entities,
                                     IEngineWorldQuery world, ILog log)
{
    public void Execute(string boss = "", int phase = 0)
    {
        if (horde.Current is not { Begun: true, Roster: { } roster } c)
        {
            log.Msg("[TF2Descent/Boss] boss_spawn: no horde this level\n");
            return;
        }

        int row = BossDev.LivingBoss(roster);
        if (row >= 0 && (boss.Length == 0 || boss == roster.Template(row)))
        {
            Stand(c, row, phase, "the level's boss");
            return;
        }

        if (row >= 0)
        {
            log.Msg($"[TF2Descent/Boss] boss_spawn: {roster.Template(row)} (row {row}) stands on this level: kill it first (tf2_descent_boss_kill)\n");
            return;
        }

        string template = boss.Length > 0 ? boss : rules.Rules.Bosses.AtDepth(c.Level!.Depth)?.Id ?? rules.Rules.Bosses.Ids[0];
        if (rules.Rules.Bosses.Of(template) is null)
        {
            log.Msg($"[TF2Descent/Boss] boss_spawn: '{template}' is not a boss ({string.Join(' ', rules.Rules.Bosses.Ids)})\n");
            return;
        }

        var who = DevContext.WhoTyped(players);
        if (who == EntityHandle.None)
        {
            log.Msg("[TF2Descent/Boss] boss_spawn: no player to aim\n");
            return;
        }

        Vector eye = entities.GetEyePosition(who), ang = entities.GetEyeAngles(who);
        float pitch = ang.X * MathF.PI / 180f, yaw = ang.Y * MathF.PI / 180f;
        var forward = new Vector(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
        var hit = world.TraceLine(eye, eye + forward * DevContext.PickRange, TraceMask.Brushes, who);
        var at = hit.End - forward * 64f;
        var down = world.TraceLine(at, at - new Vector(0f, 0f, 512f), TraceMask.Brushes, who);
        var feet = HordeVectors.ToAi(down.End);
        int room = c.Level!.RoomAt(feet);
        if (room < 0)
        {
            log.Msg("[TF2Descent/Boss] boss_spawn: the crosshair is in no room of the level\n");
            return;
        }

        ulong seed = (ulong)clock.Tick * 0x9E3779B97F4A7C15UL;
        var id = roster.Add(template, null, room, new PackMarker(feet, ang.Y + 180f), seed, Math.Max(1, c.PartySize), boss: true);
        if (!id.IsValid)
        {
            log.Msg("[TF2Descent/Boss] boss_spawn: the roster is full\n");
            return;
        }

        Stand(c, id.Index, phase, $"a dev boss in room {room} at ({feet.X:0},{feet.Y:0},{feet.Z:0})");
    }

    private void Stand(HordeController c, int row, int phase, string what)
    {
        var roster = c.Roster!;
        if (roster.State(row) == RobotState.Asleep)
            roster.WakeRoom(roster.Room(row));
        string lent = roster.Body(row) != 0 ? $"holds body {roster.Body(row)}" : c.LendByHand(row, clock.Tick, out string why) ? why : $"no body: {why}";
        double life = rules.Rules.Bosses.Of(roster.Template(row)) is { } script ? BossDev.ToPhase(c, row, script, phase) : roster.Life(row);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] boss_spawn: {roster.Template(row)} row {row}, {what}: {lent}; life {life:0} of {roster.Stats(row).Life:0} (phase {phase} asked), arena room {roster.HomeRoom(row)}\n"));
    }
}

/// <summary><c>tf2_descent_boss_phase &lt;n&gt;</c>: sets the level's boss's life just inside phase n (a phase once entered holds).</summary>
[ConCommand("tf2_descent_boss_phase", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_boss_phase <n>: set the level's boss's life just inside phase n (0-based)")]
public sealed class BossPhaseCommand(HordeService horde, IHordeRules rules, ILog log)
{
    public void Execute(int phase)
    {
        if (horde.Current is not { Begun: true, Roster: { } roster } c || BossDev.LivingBoss(roster) is var row && row < 0)
        {
            log.Msg("[TF2Descent/Boss] boss_phase: no living boss this level\n");
            return;
        }

        var script = rules.Rules.Bosses.Of(roster.Template(row))!;
        double life = BossDev.ToPhase(c, row, script, phase);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] boss_phase: {roster.Template(row)} row {row} set to life {life:0} of {roster.Stats(row).Life:0} for phase {phase} (threshold {script.Phases[Math.Clamp(phase, 0, script.Phases.Count - 1)].At} %)\n"));
    }
}

/// <summary>
/// <c>tf2_descent_boss_kill [player]</c>: kills the level's boss the way it dies in play (a lethal
/// hit on its body by that player, else its roster's death), so the whole kill chain runs: XP,
/// its own table, the way down, the camp record.
/// </summary>
[ConCommand("tf2_descent_boss_kill", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_boss_kill [player slot]: kill the level's boss through the normal path, as that player")]
public sealed class BossKillCommand(HordeService horde, RobotKills kills, IEngineDamage damage, IEnginePlayers players, ILog log)
{
    public void Execute(int slot = 0)
    {
        if (horde.Current is not { Begun: true, Roster: { } roster, Bodies: { } bodies } || BossDev.LivingBoss(roster) is var row && row < 0)
        {
            log.Msg("[TF2Descent/Boss] boss_kill: no living boss this level\n");
            return;
        }

        uint before = kills.Told;
        string how = "the roster (no body)";
        if (roster.State(row) == RobotState.Embodied && roster.Body(row) != 0)
        {
            var attacker = slot > 0 ? players.GetHandle(slot) : EntityHandle.None;
            damage.TakeDamage(bodies.Body(roster.Body(row)).Handle, attacker, KillRobotCommand.Lethal, DamageType.Generic);
            how = slot > 0 ? $"a hit by player {slot}" : "a hit by no player";
        }

        if (roster.State(row) != RobotState.Dead)
            roster.Hurt(row, roster.Life(row));
        kills.Sweep();
        log.Msg($"[TF2Descent/Boss] boss_kill: {roster.Template(row)} row {row} killed by {how}; {kills.Told - before} kill(s) told\n");
    }
}
