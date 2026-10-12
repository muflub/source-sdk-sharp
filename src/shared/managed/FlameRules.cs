//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// How big the flame on a burning entity is, how far it reaches and how much it
/// hurts — stock's <c>CEntityFlame</c>, as arithmetic.
///
/// <para>
/// Here rather than on <c>SsEntityFlame</c> because every one of these is a
/// number derived from another number, with no entity involved: a size from a
/// pair of bounds, a radius from a size, a per-tick damage from a per-second
/// rate. That is the part worth testing, and the part where this build's
/// recurring defect lives.
/// </para>
///
/// <para>
/// THE DIVIDE-BY-DEFAULT SHAPE, which is why <see cref="DamageRadius"/> exists
/// as its own function instead of being <c>size / 2</c> written at the one call
/// site. Stock computes the burn radius as <c>m_flSize / 2</c> and hands it
/// straight to <c>RadiusDamage</c>, which divides the damage BY the radius to
/// get its falloff. A flame whose size never arrived is therefore a divide by
/// zero inside the engine, and the NaN that comes out of it compares false
/// against every bound the victim loop tests — the same shape as
/// <c>EnvSmokeStackVars.JetLength</c>, and the same fix: finite and strictly
/// positive, not <c>!= 0</c>. See <c>src/sourcesharp/game/shared/jet_lifetime.h</c>.
/// </para>
/// </summary>
public static class FlameRules
{
    /// <summary>
    /// Seconds between damage passes — stock's <c>FLAME_DAMAGE_INTERVAL</c>.
    ///
    /// It is also the flame's think interval, because the damage pass is the
    /// only thing the think does.
    /// </summary>
    public const float DamageInterval = 0.2f;

    /// <summary>
    /// What the thing actually on fire takes, per second, before the interval
    /// is applied — stock's <c>FLAME_DIRECT_DAMAGE_PER_SEC</c>.
    /// </summary>
    public const float DirectDamagePerSecond = 5.0f;

    /// <summary>
    /// What everything else standing in the flame takes, per second — stock's
    /// <c>FLAME_RADIUS_DAMAGE_PER_SEC</c>. Lower than the direct rate on
    /// purpose: being on fire is worse than standing next to something that is.
    /// </summary>
    public const float RadiusDamagePerSecond = 4.0f;

    /// <summary>Per pass, which is what a think applies. Stock's <c>FLAME_DIRECT_DAMAGE</c>.</summary>
    public const float DirectDamage = DirectDamagePerSecond * DamageInterval;

    /// <summary>Per pass. Stock's <c>FLAME_RADIUS_DAMAGE</c>.</summary>
    public const float RadiusDamage = RadiusDamagePerSecond * DamageInterval;

    /// <summary>
    /// How hot a flame makes the air around it, which is what lets a burning
    /// thing set fire to what it is standing next to. Stock's argument to
    /// <c>FireSystem_AddHeatInRadius</c> from <c>CEntityFlame::FlameThink</c>.
    /// </summary>
    public const float HeatPerPass = 2.0f;

    /// <summary>
    /// The smallest flame there is, in world units — stock's floor in
    /// <c>CEntityFlame::Create</c>.
    ///
    /// It exists because the size is derived from the target's own width, and a
    /// thin target would otherwise get a flame too small to see. It is also what
    /// this class answers for a target whose bounds are unusable, so an
    /// unmeasurable thing burns like a small thing rather than like nothing or
    /// like everything.
    /// </summary>
    public const float MinimumSize = 16.0f;

    /// <summary>
    /// What the client draws a flame with — stock's <c>burning_character</c>,
    /// out of <c>burning_fx.pcf</c>.
    ///
    /// Shared rather than on either half, because the server precaches it and
    /// the client starts it, and a name spelled twice is a name that can be
    /// spelled differently. Stock has a second one behind
    /// <c>#ifdef TF_CLIENT_DLL</c> — <c>burningplayer_red</c> — which is TF2's
    /// and is not a choice this mod has.
    /// </summary>
    public const string ParticleSystem = "burning_character";

    /// <summary>Burning flesh: stock's sound for something alive.</summary>
    public const string BurningFleshSound = "General.BurningFlesh";

    /// <summary>Burning anything else.</summary>
    public const string BurningObjectSound = "General.BurningObject";

    /// <summary>Going out.</summary>
    public const string StopBurningSound = "General.StopBurning";

    /// <summary>
    /// Finite and strictly positive — the same test <c>FireParticleNames</c>
    /// and <c>SourceSharp_JetIsUsable</c> make, for the same reason.
    /// </summary>
    private static bool IsUsable(float f) => float.IsFinite(f) && f > 0f;

    /// <summary>
    /// How big a flame the thing being set alight gets, from its bounds.
    ///
    /// <para>
    /// Stock's <c>CEntityFlame::Create</c>: the mean of the collision box's X
    /// and Y extents, floored at <see cref="MinimumSize"/>. The Z extent is
    /// deliberately not in it — a flame is as wide as what is burning, and a
    /// lamp post is not a bonfire.
    /// </para>
    ///
    /// <para>
    /// WHAT IS ADDED. Stock subtracts <c>OBBMins</c> from <c>OBBMaxs</c> and
    /// trusts the result. An entity with no collision model answers the same
    /// vector for both, so the mean is zero and the floor catches it; an entity
    /// whose bounds are inverted answers a NEGATIVE extent, and stock's mean of
    /// two negatives is still below the floor, so that is caught too. What is
    /// not caught by a floor is a NaN, which compares false against
    /// <c>&lt; 16.0f</c> and passes through as a flame size of NaN — and from
    /// there into <see cref="DamageRadius"/>, into the engine's falloff divide,
    /// and into the particle system's bucket. Rejected here, once, at the only
    /// place a size is made.
    /// </para>
    /// </summary>
    public static float SizeForBounds(Vector mins, Vector maxs)
    {
        float xSize = maxs.X - mins.X;
        float ySize = maxs.Y - mins.Y;

        float size = (xSize + ySize) * 0.5f;

        // NOT `size < MinimumSize`, which a NaN answers false to. The usable
        // test comes first and the comparison only runs on a real number.
        if (!IsUsable(size) || size < MinimumSize)
            return MinimumSize;

        return size;
    }

    /// <summary>
    /// How big a flame this target gets, given what the caller asked for.
    ///
    /// <para>
    /// Stock's rule, which is two lines apart in <c>CBaseAnimating::Ignite</c>:
    /// <c>CEntityFlame::Create</c> measures the target, and then
    /// <c>if ( flSize &gt; 0.0f ) pFlame-&gt;SetSize( flSize )</c> overrides it
    /// with whatever the level designer typed. The override is NOT floored at
    /// <see cref="MinimumSize"/> — a designer asking for a four-unit flame gets
    /// one, which is the point of an override — but it must still be a number,
    /// so a NaN or an infinity in a map key falls back to the measurement
    /// rather than through it.
    /// </para>
    ///
    /// <para>
    /// Zero is "no override", not "a flame of no size". That is stock's own
    /// reading of the argument and it is also the safe one: <c>flSize</c>
    /// defaults to <c>0.0f</c> in the signature, so every caller that does not
    /// care passes it.
    /// </para>
    /// </summary>
    public static float SizeFor(Vector mins, Vector maxs, float overrideUnits)
        => IsUsable(overrideUnits) ? overrideUnits : SizeForBounds(mins, maxs);

    /// <summary>
    /// How far from the flame its heat and its splash damage reach — stock's
    /// <c>m_flSize / 2</c>.
    ///
    /// <para>
    /// Always finite and strictly positive, because both of its consumers
    /// divide by it: the engine's radius-damage falloff is
    /// <c>damage / radius</c>, and a fire's heat is spread over the radius it
    /// was given. A zero or a NaN there does not fail — it produces an infinity
    /// or a NaN that every subsequent comparison answers false to, which is a
    /// flame that hurts everything or nothing and says neither.
    /// </para>
    /// </summary>
    public static float DamageRadius(float size)
    {
        if (!IsUsable(size))
            size = MinimumSize;

        return size * 0.5f;
    }

    /// <summary>
    /// When a flame lit now, for this many seconds, should go out.
    ///
    /// <para>
    /// Stock's <c>SetLifetime</c> is <c>curtime + lifetime</c> and takes
    /// whatever it is given, which means a map that leaves
    /// <c>env_entity_igniter</c>'s <c>lifetime</c> key unset asks for a flame
    /// that expires the instant it is created — <c>m_flLifetime &lt; curtime</c>
    /// is already true on the first think. That is stock's behaviour for a
    /// zero and it is kept: a zero lifetime is a mapper saying nothing, not a
    /// mapper asking for an eternal fire, and this build has burned itself once
    /// already on a var with no initialiser meaning "forever".
    /// </para>
    ///
    /// <para>
    /// A NEGATIVE or non-finite lifetime is different: it cannot have been
    /// meant, and <c>curtime + NaN</c> is a deadline no comparison ever passes,
    /// which is a flame that burns for the rest of the map. Clamped to zero so
    /// it goes out on the first think, like the unset one.
    /// </para>
    /// </summary>
    public static float ExpiryFor(float now, float lifetimeSeconds)
    {
        if (!float.IsFinite(lifetimeSeconds) || lifetimeSeconds < 0f)
            lifetimeSeconds = 0f;

        return now + lifetimeSeconds;
    }
}
