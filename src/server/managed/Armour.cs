//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// How much of a hit armour soaks, and what that costs the wearer.
///
/// <para>
/// A game rule, and one that used to be stuck in C++ for a reason worth
/// remembering: the shell asks managed code how much damage to apply and then
/// hands the answer to <c>CBasePlayer::OnTakeDamage</c>, which applied armour to
/// it. Absorbing here as well would have absorbed twice. The native block is now
/// guarded out, which is what lets this exist.
/// </para>
///
/// <para>
/// Static and pure so the arithmetic can be checked without a player, a server
/// or a hit — which matters more here than usual, because every branch is a
/// number a mapper or an admin can feel and none of them is observable except by
/// being shot.
/// </para>
/// </summary>
public static class Armour
{
    /// <summary>
    /// The fraction of a hit that reaches HEALTH. Stock's 0.2 — armour takes 80%.
    /// </summary>
    public const float DefaultRatio = 0.2f;

    /// <summary>
    /// How many points of armour one point of absorbed damage costs. Stock's
    /// modern value is 1.0; the <c>old_armor</c> rule was 0.5, which made a
    /// point of armour worth two of health.
    /// </summary>
    public const float DefaultBonus = 1.0f;

    /// <summary>
    /// Damage types armour does not help against.
    ///
    /// Falling, drowning, poison and radiation, which is stock's list and reads
    /// as a sensible one: a vest does not help you hit the ground more softly.
    /// </summary>
    public const DamageType Unprotected = DamageType.Fall | DamageType.Drown
                                 | DamageType.Poison | DamageType.Radiation;

    /// <summary>What a hit costs, after armour has taken its share.</summary>
    public readonly record struct Result(float Damage, int ArmourLeft, float Absorbed);

    /// <summary>
    /// Applies armour to one hit.
    ///
    /// <para>
    /// Stock's arithmetic, branch for branch. The part that looks wrong and is
    /// not is the overflow case: when the wearer has less armour than the hit
    /// would consume, the REMAINING armour is converted back through the bonus
    /// to work out how much damage it actually stopped — so the last few points
    /// of armour still do their proportional job rather than vanishing.
    /// </para>
    /// </summary>
    public static Result Apply(float damage, int armour, DamageType damageType,
                               float ratio = DefaultRatio, float bonus = DefaultBonus,
                               bool multiplayer = false)
    {
        // No armour, or a kind of harm it does not help with.
        if (armour <= 0 || (damageType & Unprotected) != 0 || damage <= 0f)
            return new Result(damage, armour, 0f);

        // The modern rule (stock's !player_old_armor), read off the bonus the
        // caller chose BEFORE a blast doubles it.
        bool modern = bonus >= DefaultBonus;

        // "blasts damage armor more" -- in a multiplayer game only.
        if ((damageType & DamageType.Blast) != 0 && multiplayer)
            bonus *= 2f;

        float toHealth = damage * ratio;
        float fromArmour = (damage - toHealth) * bonus;

        // A minimum of one point, which stops a stream of tiny hits passing
        // through armour untouched. Stock applies this only under the modern
        // rule, and so does this.
        if (modern && fromArmour < 1f)
            fromArmour = 1f;

        if (fromArmour > armour)
        {
            // More than there is. What is left still absorbs its share.
            fromArmour = armour;
            toHealth = damage - (fromArmour * (1f / bonus));

            return new Result(MathF.Max(toHealth, 0f), 0, fromArmour);
        }

        // int m_ArmorValue -= float flArmor: the subtraction in float, then truncated.
        return new Result(toHealth, (int)(armour - fromArmour), fromArmour);
    }
}
