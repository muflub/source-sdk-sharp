using SourceSharp.Abi;

using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;

namespace TF2.Descent.Server.Horde.Attacks;

/// <summary>
/// A blast round a robot (HO12: the sentry buster's, a champion's death): every hero whose chest
/// lies within the radius of the robot's middle, with a clear line from it, takes the damage as
/// a blast in the robot's name, whole, as stock's buster deals it (no falloff). Robots are one
/// team: no other robot is hurt (the robot itself is the caller's to kill, L12).
/// </summary>
public static class RobotBlast
{
    /// <summary>Hurts the heroes in reach of <paramref name="body"/>'s middle; answers how many it hurt.</summary>
    public static int Hit(IRobotAttackWorld world, IHordeHeroes heroes, HordeLevel level, RobotBody body, float radius, float damage,
                          Span<HeroPlace> places, Span<HeroEye> eyes)
    {
        int n = heroes.Gather(level, places, eyes);
        var middle = new Vector(body.Position.X, body.Position.Y, body.Position.Z + RobotHull.Maxs.Z * 0.5f * body.Scale);
        int hit = 0;
        for (int h = 0; h < n; h++)
        {
            var feet = HordeVectors.ToEngine(places[h].Position);
            Vector chest = feet + (eyes[h].Eye - feet) * 0.6f;
            Vector to = chest - middle;
            float d = to.Length;
            if (d > radius)
                continue;
            var line = world.Line(middle, chest, body.Handle);
            if (line.Hit && line.Entity != eyes[h].Player)
                continue;
            world.Hurt(eyes[h].Player, body.Handle, damage, DamageType.Blast, d > 1e-3f ? to * (1f / d) : new Vector(0f, 0f, 1f));
            hit++;
        }

        return hit;
    }
}
