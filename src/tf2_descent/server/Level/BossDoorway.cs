using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Descent.Server.Hero;

namespace TF2.Descent.Server.Level;

/// <summary>Where a hero stands to go into the level's boss arena: outside one of its doorways, facing in.</summary>
public static class BossDoorway
{
    /// <summary>How far back from the doorway's sill, on the side away from the arena, the hero stands.</summary>
    public const float StepBack = 64f;

    /// <summary>How far above the sill his feet are put (the floor takes him the rest).</summary>
    public const float Lift = 8f;

    /// <summary>
    /// The spot outside the boss arena's first doorway (by door id) and the yaw that faces into
    /// the arena; null when the level has no arena or no door into it.
    /// </summary>
    public static (Vector At, float Yaw)? Outside(LevelData level)
    {
        if (level.BossArena is not { } arena)
            return null;
        foreach (var door in level.Doors.OrderBy(d => d.Id))
        {
            if (door.RoomA != arena && door.RoomB != arena)
                continue;
            // Normal points from RoomA into RoomB: into the arena when it is RoomB.
            float into = door.RoomB == arena ? 1f : -1f;
            var inward = new Vector(door.Normal.X * into, door.Normal.Y * into, 0f);
            var at = new Vector(door.Position.X - inward.X * StepBack, door.Position.Y - inward.Y * StepBack, door.Position.Z + Lift);
            float yaw = MathF.Atan2(inward.Y, inward.X) * 180f / MathF.PI;
            return (at, yaw);
        }
        return null;
    }
}

/// <summary>
/// <c>tf2_descent_goto_boss &lt;slot&gt;</c>: the hero in the slot is moved outside the boss arena's
/// door, facing in (the admin menu's Go to the boss).
/// </summary>
[ConCommand("tf2_descent_goto_boss", Help = "tf2_descent_goto_boss <player slot>: move the hero outside the boss arena's door, facing in (sv_cheats).")]
public sealed class GotoBossCommand(SvCheatsConVar cheats, Heroes heroes, ILevelData level, IEngineEntities entities, ILog log)
{
    public void Execute([ArgRange(1, 64)] int slot)
    {
        var gate = new HeroCommandGate(cheats, heroes, log);
        if (gate.Hero("tf2_descent_goto_boss", slot) is not { } hero)
            return;
        if (level.Current is not { } current || BossDoorway.Outside(current) is not { } spot)
        {
            log.Warning("[TF2Descent/Level] tf2_descent_goto_boss: this level has no boss arena with a door\n");
            return;
        }
        if (hero.Player == EntityHandle.None || !entities.IsValid(hero.Player))
        {
            log.Warning($"[TF2Descent/Level] tf2_descent_goto_boss: player {slot} has no body\n");
            return;
        }

        entities.Teleport(hero.Player, spot.At, new Vector(0f, spot.Yaw, 0f), zeroVelocity: true);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Level] {slot} goes to the boss arena (room {current.BossArena}): at {spot.At.X:0} {spot.At.Y:0} {spot.At.Z:0}, yaw {spot.Yaw:0}\n"));
    }
}
