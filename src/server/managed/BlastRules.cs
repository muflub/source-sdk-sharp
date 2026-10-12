//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// WHAT AN EXPLOSION DOES TO WHAT IS NEAR IT — the arithmetic alone, with no
/// engine in it.
///
/// <para>
/// This was <c>CGameRules::RadiusDamage</c>'s inner arithmetic and
/// <c>blast_blocking.h</c>'s two predicates, in
/// <c>sourcesharp/game/shared/shell_gamerules.cpp</c>. §3.10 is explicit that
/// game rules have no native object and that the decision callbacks "become
/// ordinary managed calls with no native side"; §6 has no gamerules row at all.
/// How hard a blast hits at a distance is the most load-bearing single number
/// in this mod's combat, and until now it could not be asked a question without
/// a running Source.
/// </para>
///
/// <para>
/// <b>The falloff, exactly as stock spells it.</b> Two statements in
/// <c>gamerules.cpp</c>:
/// </para>
/// <code>
///     falloff          = info.GetDamage() / flRadius;
///     flAdjustedDamage = ( vecSrc - tr.endpos ).Length() * falloff;
///     flAdjustedDamage = info.GetDamage() - flAdjustedDamage;
/// </code>
/// <para>
/// which is <c>damage * (1 - distance/radius)</c> — linear in distance, zero
/// exactly at the radius, and negative beyond it (the sweep drops a victim
/// whose adjusted damage is not positive, so "negative" and "spared" are the
/// same outcome). It is NOT an inverse-square law and it is not clamped;
/// writing either would be a quiet rebalance of every explosive in the game.
/// </para>
///
/// <para>
/// <b>THE LIVE BUG THIS CURVE EXISTS TO PIN.</b> The native function's own
/// comment records it: a barrel of magnitude 120 and radius 256 dealt its whole
/// 120 to a player a couple of hundred units away on dm_lockdown, because the
/// blast had no inflictor to pass through, the first trace stopped at fraction
/// zero inside the exploding entity's own hull, and the distance term was
/// therefore zero for every victim in range. The RPG killed its own shooter
/// through cover. Nothing about that could be asserted while the curve was a
/// C++ function reachable only from an explosion.
/// </para>
///
/// <para>
/// <b>The comment's own pair of numbers does not lie on the comment's own
/// curve, and this class says so rather than repeating it.</b> The comment
/// reads "a player 252 units away … where stock's falloff gives 2.8". At
/// magnitude 120 and radius 256 the falloff is <c>120/256 = 0.46875</c> per
/// unit, so 252 units gives <c>120 - 252*0.46875 = 1.875</c>, and <c>2.8</c> is
/// the answer at <c>250.0</c> units. Both are pinned below, as separate facts,
/// because the point the comment was making survives either reading: the
/// correct answer out there is a couple of hit points and the bug delivered a
/// hundred and twenty.
/// </para>
///
/// <para>
/// <b>Why a class of its own and not a method on the sweep.</b> The sweep needs
/// a world; this needs nothing. Splitting them is what lets the curve be a set
/// of ordinary xUnit facts rather than a live run, which is the whole
/// measurable difference this move makes.
/// </para>
/// </summary>
public static class BlastRules
{
    /// <summary>
    /// Stock's <c>MASS_ABSORB_ALL_DAMAGE</c> in kilogrammes — the mass at which
    /// a blocker in the way keeps the entire blast. From
    /// <c>blast_blocking.h</c>, which lifted it out of
    /// <c>CGameRules::RadiusDamage</c>.
    /// </summary>
    public const float MassAbsorbsAll = 350f;

    /// <summary>
    /// What a blocker the solver does not move keeps: a flat quarter. It has no
    /// mass to weigh, so there is no ratio to take.
    /// </summary>
    public const float NoPhysicsShare = 0.25f;

    /// <summary>
    /// Damage lost per unit of distance — <c>damage / radius</c>.
    ///
    /// <para>
    /// A zero radius answers 1.0 rather than dividing by it. That is stock's
    /// own arm (<c>if ( flRadius ) … else falloff = 1.0</c>) and it is the
    /// reason a caller must not treat a missing radius key as "hurt the whole
    /// level".
    /// </para>
    /// </summary>
    public static float Falloff(float damage, float radius)
        => radius != 0f ? damage / radius : 1f;

    /// <summary>
    /// What a victim <paramref name="distance"/> units from the blast takes,
    /// before any blocker's share is taken off it.
    ///
    /// <para>
    /// May be zero or negative, and the caller must treat both as "spared":
    /// stock's <c>if ( flAdjustedDamage &lt;= 0 ) continue;</c>. Clamping here
    /// instead would turn a victim outside the radius into a victim taking
    /// nothing, which is the same number and a different event — no force, no
    /// sound, no trigger walk.
    /// </para>
    /// </summary>
    public static float AtDistance(float damage, float radius, float distance)
        => damage - distance * Falloff(damage, radius);

    /// <summary>
    /// Whether what a trace stopped on is in the way of the blast, over
    /// identities rather than over one hierarchy's pointer type.
    ///
    /// <para>
    /// <c>blast_blocking.h</c>'s <c>BlastBlockerCounts</c>, and its three
    /// clauses in the order it states them: nothing there is not a blocker, the
    /// victim does not shield itself, and a thing OWNED by the victim does not
    /// shield it either — stock's "shouldn't let children block parents".
    /// </para>
    /// </summary>
    /// <param name="victim">The entity the blast is trying to reach.</param>
    /// <param name="blocker">
    /// What the trace stopped on, or <see cref="EntityHandle.None"/> for the
    /// world, a static prop, or anything that is not an entity at all. None of
    /// those reach this rule: the world is handled by the solid retrace above
    /// it, and a static prop can neither be hurt nor own anything.
    /// </param>
    /// <param name="blockerOwner">Who owns the blocker, or none.</param>
    public static bool BlockerCounts(EntityHandle victim, EntityHandle blocker,
                                     EntityHandle blockerOwner)
    {
        if (blocker == EntityHandle.None)
            return false;

        if (blocker == victim)
            return false;

        if (blockerOwner != EntityHandle.None && blockerOwner == victim)
            return false;

        return true;
    }

    /// <summary>
    /// The share of the blast a blocker keeps for itself, as a fraction of what
    /// would otherwise have landed. <c>blast_blocking.h</c>'s
    /// <c>BlastBlockedFraction</c>.
    ///
    /// <para>
    /// 1.0 or more means it kept everything and the victim takes nothing at
    /// all. The caller tests for that rather than this clamping, because
    /// "absorbed it all" and "absorbed all but a rounding error" are different
    /// events there: the first skips the victim entirely, forces and sounds
    /// included.
    /// </para>
    /// </summary>
    /// <param name="hasPhysics">
    /// Whether the solver has an object for the blocker. A prop with motion
    /// disabled still HAS one and still answers its mass, which is right: a
    /// pinned crate weighs what it weighs.
    /// </param>
    /// <param name="mass">That object's mass in kilogrammes; ignored without one.</param>
    public static float BlockedShare(bool hasPhysics, float mass)
        => hasPhysics ? mass / MassAbsorbsAll : NoPhysicsShare;

    /// <summary>
    /// Whether a blast can reach a victim through the surface of the water —
    /// stock's "blasts don't travel into or out of water", spelled as one
    /// predicate instead of as two <c>continue</c>s a hundred lines apart.
    ///
    /// <para>
    /// A blast in the water reaches nothing that is not at least ankle deep; a
    /// blast in the air reaches nothing that is fully submerged. Everything
    /// else is reached. <paramref name="victimWaterLevel"/> is
    /// <c>GetWaterLevel</c>'s 0..3.
    /// </para>
    /// </summary>
    public static bool ReachesThroughWater(bool blastInWater, int victimWaterLevel)
    {
        if (blastInWater && victimWaterLevel == 0)
            return false;

        if (!blastInWater && victimWaterLevel == 3)
            return false;

        return true;
    }

    /// <summary>
    /// Where a blast aims at a victim: the middle of its collision box, taken
    /// through the collision-to-world transform.
    ///
    /// <para>
    /// <b>This is the OBB's centre and NOT the midpoint of the axis-aligned box
    /// that encloses it</b>, and the two are a different point.
    /// <c>anyentity.h</c>'s <c>BodyTarget</c> carries the measurement: a
    /// standalone entity's surrounding bounds are never rotated, while its
    /// collision-to-world transform is <c>AngleMatrix( angles, origin )</c>, so
    /// for a rotated prop with an off-centre box the two answers part. A blast
    /// beside such a prop was tracing its line of sight at a point the prop is
    /// not at, and could stop on scenery and dock the damage the prop should
    /// have taken.
    /// </para>
    /// </summary>
    public static Vector BodyTarget(Transform collisionToWorld, Vector mins, Vector maxs)
        => collisionToWorld.TransformPoint((mins + maxs) * 0.5f);
}
