using SourceSharp.Abi;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// A hit on a lent, living robot as a guard sees it (HO12), after the rules have taken its
/// resistance and before the roster takes its life: the body and its roster row, who hit it,
/// the damage type, where it landed, the life the rules would take, and the robot's life and
/// most now.
/// </summary>
public readonly record struct RobotGuardHit(RobotBody Body, int Row, EntityHandle Attacker, string DamageType, Vector Position,
                                            double Taken, double Life, double Most);

/// <summary>
/// THE SEAM WHERE A ROBOT'S HIT CAN BE TURNED ASIDE (HO12): each member, in its order, answers
/// the life the hit takes, never more than it was handed (<see cref="RobotGuardHit.Taken"/>);
/// the next member is handed that. A champion's über and its medic shield, and a sentry
/// buster's spin, are its members. A guard decides nothing about IMMUNE: an immune hit comes
/// to it taking nothing already.
/// </summary>
public interface IRobotDamageGuard
{
    double Guard(in RobotGuardHit hit);
}

/// <summary>The guard chain as <see cref="RobotBodies"/> asks it: each member in its order, each answer clamped to what it was handed.</summary>
public sealed class RobotGuardChain(IHooks<IRobotDamageGuard> hooks)
{
    public double Guard(RobotGuardHit hit)
    {
        var all = hooks.All;
        for (int k = 0; k < all.Count; k++)
        {
            double taken = all[k].Guard(in hit);
            hit = hit with { Taken = Math.Clamp(taken, 0, hit.Taken) };
        }

        return hit.Taken;
    }
}

/// <summary>
/// A horde service that needs the game's tick while a level's horde runs (HO12: the champions'
/// timers), called by the horde's hands each server tick (<see cref="Attacks.RobotAttacks"/>),
/// in its order, with the controller.
/// </summary>
public interface IHordeTicker
{
    void Tick(HordeController horde, long now);
}
