//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Server;

/// <summary>
/// The numbers a fire burns by, and the arithmetic that uses them.
///
/// <para>
/// Separated from the entity because every one of these is a balance decision
/// somebody will want to argue with, and because the arithmetic can then be
/// checked without a map, a flame or a tick. Stock keeps them as convars in
/// fire.cpp; they are constants here until something asks to turn one, which is
/// the same choice <see cref="Armour"/> made and for the same reason.
/// </para>
/// </summary>
public static class FireRules
{
    /// <summary>Full heat. Everything else is expressed as a fraction of it.</summary>
    public const float MaxHeatLevel = 64f;

    /// <summary>The height a fire of scale 1 is drawn at, which is what sizes are relative to.</summary>
    public const float Height = 256f;

    /// <summary>How wide a fire is compared to its height. Stock's, and it decides the damage radius.</summary>
    public const float Width = 128f;

    /// <summary>How often a burning fire updates itself.</summary>
    public const float ThinkInterval = 0.1f;

    /// <summary>
    /// The damage radius is this much larger than the flame looks.
    ///
    /// Stock's comment calls it a FIXME — "trying slightly larger radius for
    /// burning" — and it is kept because maps were built against the reach it
    /// actually has rather than the one it appears to have.
    /// </summary>
    public const float SpreadDamageMultiplier = 2f;

    // Stock's fire_* convars, as constants.
    public const float HeatAbsorbMax   = 50f;
    public const float AbsorbRate      = 3f;
    public const float ExtinguishScale = 12f;
    public const float ExtinguishAbsorb = 5f;
    public const float IncomingHeatScale = 0.1f;
    public const float DamageScale     = 0.1f;
    public const float DamageBase      = 1f;
    public const float GrowthRate      = 1f;
    public const float DamageInterval  = 1f;

    /// <summary>A fire's scale, as the fraction of full height its size represents.</summary>
    public static float ScaleFromSize(float fireSize) => fireSize / Height;

    /// <summary>
    /// How much heat a fire soaks before it catches, from the ignitionpoint a
    /// map keyed.
    ///
    /// The twentieth is stock's and is easy to miss, because the keyvalue looks
    /// like it is already in heat: a fire keyed 100 soaks 5, not 100, which is
    /// the difference between a fire that catches in about a second and one that
    /// never catches at all.
    /// </summary>
    public static float IgnitionAbsorb(float ignitionPoint) => ignitionPoint * 0.05f;

    /// <summary>
    /// How far a fire reaches, given how hot it currently is.
    ///
    /// <para>
    /// A cold fire is a small one. The floor of 16 units is stock's and matters:
    /// without it a fire that has only just caught has no reach at all and
    /// cannot spread to the thing that lit it.
    /// </para>
    /// </summary>
    public static float DamageRadius(float heatLevel, float maxHeat, float fireSize)
    {
        if (maxHeat <= 0f)
            return 0f;

        float scale = heatLevel / maxHeat;
        float radius = scale * fireSize * (Width / Height) * 0.5f * SpreadDamageMultiplier;

        return radius < 16f ? 16f : radius;
    }

    /// <summary>
    /// How far a fire BURNS, as opposed to how far it spreads.
    ///
    /// Half the spread reach. That asymmetry is the whole point of the spread
    /// multiplier: a fire reaches its neighbours from further away than it
    /// reaches you, so fire travels across a room faster than it hurts across
    /// one. Players are the exception and are burned at the full reach.
    /// </summary>
    public static float HurtRadius(float spreadRadius) => spreadRadius / SpreadDamageMultiplier;

    /// <summary>
    /// How much damage a fire deals in one interval, given how hot it is.
    ///
    /// The heat term is heat SQUARED over the maximum — stock computes it as
    /// strength times heat — so a fire at half strength does far less than half
    /// the damage. That curve is the reason a small fire is survivable and a
    /// large one is not.
    /// </summary>
    public static float DamagePerInterval(float heatLevel, float maxHeat, float damageScale)
    {
        if (maxHeat <= 0f)
            return 0f;

        float outputHeat = (heatLevel / maxHeat) * heatLevel;

        return (DamageBase + outputHeat * DamageScale * damageScale) * DamageInterval;
    }

    /// <summary>
    /// What heat actually reaches a fire, after what it can absorb.
    ///
    /// <para>
    /// A fire that has not caught yet soaks incoming heat rather than lighting
    /// instantly — that is its ignition point, and it is why a match does not
    /// light a bonfire. Heat arriving from ANOTHER fire is scaled down first
    /// when this one is already burning, so a row of fires does not run away.
    /// </para>
    /// </summary>
    /// <returns>The heat that got through, and the absorption left.</returns>
    public static (float Heat, float AbsorbLeft) Absorb(float heat, float absorb,
                                                        bool burning, bool selfHeat)
    {
        if (!selfHeat && burning)
            heat *= IncomingHeatScale;

        if (absorb <= 0f)
            return (heat, 0f);

        float absorbed = heat * AbsorbRate;

        if (absorbed > absorb)
            return (heat - (absorb / AbsorbRate), 0f);

        return (0f, absorb - absorbed);
    }
}
