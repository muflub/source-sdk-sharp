//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// An explosion, as a thing that HAPPENS rather than a thing that exists.
///
/// <para>
/// Stock's <c>ExplosionCreate</c> built a C++ env_explosion by classname, poked
/// keyvalues into it as formatted strings — <c>Q_snprintf(buf, "%3d",
/// magnitude)</c> — spawned it by hand, fired an input at it and let it delete
/// itself three tenths of a second later. It did all of that because reusing
/// CEnvExplosion's body was the only way C++ had to share it.
/// </para>
///
/// <para>
/// C# can just share it. <see cref="Detonate"/> is that body, called from two
/// places: <see cref="EnvExplosion"/>, the entity a MAPPER places, which keeps
/// its think and its REPEATABLE flag; and <see cref="Fire"/>, which is what
/// code uses and which creates nothing at all. A grenade no longer spawns and
/// destroys an entity per bang.
/// </para>
///
/// <para>
/// The class is PUBLIC because <see cref="Fire"/> is a mod's door: Phase 0.4's
/// rule is that a mod is not a test, so a behaviour's projectile that goes off
/// reaches the one blast the core owns through this API rather than through
/// <c>InternalsVisibleTo</c>. <c>Setup</c> and <c>Detonate</c> stay internal:
/// they are <see cref="EnvExplosion"/>'s own think path's business, and a
/// second door for code-made blasts is exactly the drift this file exists to
/// prevent.
/// </para>
/// </summary>
public static class Explosions
{
    /// <summary>
    /// Everything one explosion is, gathered so the two doors into
    /// <see cref="Detonate"/> cannot drift apart.
    /// </summary>
    /// <param name="Ignored">
    /// What the blast neither hurts nor is blocked BY. An entity that explodes
    /// where it stands must name itself, or the first trace of the traversal
    /// begins inside its own hull, stops at fraction zero, and every victim in
    /// the radius is scored at distance zero — and then takes the full
    /// magnitude however far away it is standing.
    /// </param>
    internal readonly record struct Setup(
        Vector Origin,
        int Magnitude,
        int RadiusOverride,
        EnvExplosion.SpawnFlag Flags,
        RenderMode Render,
        int FireballSprite,
        int SpriteScale,
        float DamageForce,
        EntityHandle Owner,
        EntityHandle Inflictor,
        EntityHandle Ignored,
        DamageType? CustomDamageType);

    /// <summary>
    /// The bang: a scorch mark, a temp entity and a radius of damage.
    ///
    /// <para>
    /// Decides nothing that <see cref="EnvExplosion.SpriteScale"/> and its
    /// neighbours have not already decided — everything arrives worked out,
    /// which is what lets the map's entity and a grenade share this without
    /// either being a special case of the other.
    /// </para>
    /// </summary>
    internal static void Detonate(IEngineWorldQuery world, IEngineEffects effects,
                                  IEngineDamage damage, IEngineHost host,
                                  IBlastSweep blast, IServerDecals decals, in Setup s)
    {
        // Look for the ground: eight units up, forty down. The blast is not
        // MOVED to what it finds -- stock tried that and stopped, because
        // moving it changes the force vector every ragdoll gets -- but the
        // FIREBALL is nudged out of the wall, and the scorch mark goes where
        // the trace landed.
        Vector from = s.Origin + new Vector(0f, 0f, 8f);
        TraceResult ground = world.TraceLine(from, from + new Vector(0f, 0f, -40f),
                                             TraceMask.BrushesAndWater, s.Ignored);

        Vector effectOrigin = ground.Fraction != 1f
            ? ground.End + ground.Normal * 24f
            : s.Origin;

        // Stock's UTIL_DecalTrace( &tr, "Scorch" ) (util_shared.cpp,
        // CWorld::DecalTrace): nothing when the trace found no ground, and the
        // image is the decal system's weighted pick from its "Scorch" group,
        // whose entries it precached at level init. The group's NAME is never
        // precached -- doing that here was a late precache on every load where
        // nothing had asked for it in time (UTIL_PrecacheDecal: too late).
        if (!s.Flags.HasFlag(EnvExplosion.SpawnFlag.NoDecal) && ground.Fraction != 1f)
        {
            int scorch = decals.IndexForName(EnvExplosion.ScorchDecal);
            if (scorch >= 0)
                effects.PaintDecal(scorch, ground.End, angles: null, projectDistance: 0f,
                                   permanent: false, lowPriority: false);
        }

        int radius = EnvExplosion.EffectiveRadius(s.Magnitude, s.RadiusOverride);

        // Presentation. Nothing here hurts anyone.
        effects.ExplosionEffect(effectOrigin, s.FireballSprite,
                                EnvExplosion.WireScale(s.SpriteScale, s.Flags),
                                EnvExplosion.FireballFrameRate,
                                (int)EnvExplosion.EffectFlags(s.Flags, s.Render),
                                radius, s.Magnitude);

        if (!s.Flags.HasFlag(EnvExplosion.SpawnFlag.NoDamage))
        {
            // At the explosion's OWN origin, not the wall-nudged one. Stock is
            // explicit about why: the nudge exists to keep the fireball out of
            // the wall, and using it here would change the direction every
            // victim is thrown.
            // THE TRAVERSAL IS MANAGED NOW. This was
            // damage.ExplosionDamage(...) -> SourceSharp_ExplosionDamage ->
            // the global RadiusDamage -> CGameRules::RadiusDamage, and the
            // falloff, the blocking rule and the water rule all lived at the
            // far end of that chain in C++. plan_entity.md §3.10: game rules
            // have no native object, and §6 has no gamerules row. See
            // BlastSweep for what moved and BlastRules for the curve, which is
            // now a set of xUnit facts rather than something only an explosion
            // could ask.
            blast.Apply(s.Origin, s.Magnitude, radius,
                        EnvExplosion.DamageTypeFor(s.Flags, s.CustomDamageType),
                        attacker: s.Owner, inflictor: s.Inflictor,
                        ignored: s.Ignored, explicitForce: s.DamageForce);
        }

        Sparks(world, effects, host, effectOrigin, s);
    }

    /// <summary>
    /// The spark shower, which stock throws ONLY when the blast went off in
    /// water.
    ///
    /// <para>
    /// That reads backwards and is not a transcription slip: stock's test is
    /// <c>if ( UTIL_PointContents( GetAbsOrigin() ) &amp; CONTENTS_WATER )</c>,
    /// so sparks fly underwater and never fly in air. Preserved exactly — a
    /// behaviour change hidden inside a port is the kind nobody finds later.
    /// </para>
    ///
    /// <para>
    /// Stock spawned up to three <c>spark_shower</c> entities, each a small
    /// gravity-thrown object that sparked as it bounced. That entity left with
    /// explode.cpp; a burst at the point replaces it, which is the same effect
    /// without an entity per spark.
    /// </para>
    /// </summary>
    private static void Sparks(IEngineWorldQuery world, IEngineEffects effects,
                               IEngineHost host, Vector effectOrigin, in Setup s)
    {
        if (s.Flags.HasFlag(EnvExplosion.SpawnFlag.NoSparks))
            return;

        if (!world.PointContents(s.Origin).HasFlag(Contents.Water))
            return;

        // Zero to three inclusive, from the ENGINE's stream: demo playback has
        // to replay server randomness, so managed code may ask for a number
        // without being the source of one.
        int count = (int)host.RandomFloat(0f, 3.999f);

        for (int i = 0; i < count; i++)
            effects.SparkEffect(effectOrigin, magnitude: 1, trailLength: 1, makeSound: false);
    }

    /// <summary>
    /// The code door: what <c>ExplosionCreate</c> was, without the entity.
    ///
    /// <para>
    /// Public API, not <c>InternalsVisibleTo</c>: a mod's projectile that goes
    /// off is a mod, not a test, and its blast has to go through the one blast
    /// the core owns (the falloff, the line of sight, the scorch decals) rather
    /// than reimplement one that will drift.
    /// </para>
    ///
    /// <para>
    /// The render mode is <see cref="RenderMode.TransAdd"/> because that is what
    /// ExplosionCreate set on the entity it built, and it is why a code-made
    /// explosion looks different from a bare map-placed one — see
    /// <see cref="EnvExplosion.EffectFlags"/>.
    /// </para>
    /// </summary>
    public static void Fire(IEngineWorldQuery world, IEngineEffects effects,
                              IEngineDamage damage, IEnginePrecache precache, IEngineHost host,
                              IBlastSweep blast, IServerDecals decals,
                              Vector origin, float magnitude, float radius,
                              EntityHandle owner, EntityHandle ignored,
                              EnvExplosion.SpawnFlag flags = EnvExplosion.SpawnFlag.None,
                              float damageForce = 0f,
                              EntityHandle? inflictor = null,
                              DamageType? customDamageType = null)
    {
        int scaled = (int)magnitude;

        Detonate(world, effects, damage, host, blast, decals, new Setup(
            Origin: origin,
            Magnitude: scaled,
            RadiusOverride: (int)radius,
            Flags: flags,
            Render: RenderMode.TransAdd,
            FireballSprite: precache.PrecacheModel(EnvExplosion.DefaultFireballSprite),
            SpriteScale: EnvExplosion.SpriteScale(scaled, flags),
            DamageForce: damageForce,
            Owner: owner,
            // EntityHandle.None and NOT default(EntityHandle): a default handle
            // is ZERO, and zero is a real entity index -- the world. Handing
            // that over would name worldspawn as what the blast passes through
            // instead of naming nothing.
            Inflictor: inflictor ?? EntityHandle.None,
            Ignored: ignored,
            CustomDamageType: customDamageType));
    }
}
