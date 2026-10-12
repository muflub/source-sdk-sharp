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
/// Setting things alight, which is one decision with two callers.
///
/// <para>
/// It is a class of its own rather than a method on <see cref="SsEntityFlame"/>
/// because its callers do not have a flame — they have something they want to
/// burn. <see cref="EnvEntityIgniter"/> is one. The other is the ENGINE:
/// <c>CBaseAnimating::Ignite</c> and <c>env_shooter</c>'s flaming gib come back
/// through <c>OnEntityIgnite</c>, because the <c>CEntityFlame</c> they used to
/// construct has left the build.
/// </para>
///
/// <para>
/// WHAT IT REPLACES. <c>CEntityFlame::Create</c>, which built the entity,
/// measured the target, parented it, set a lifetime and started a think. All of
/// that is <see cref="SsEntityFlame.AttachTo"/>. What is HERE is the part
/// <c>Create</c> did not do and every caller then did differently: noticing that
/// the target is already on fire.
/// </para>
///
/// <para>
/// STOCK STACKS FLAMES. <c>CEntityFlame::Create</c> has no idea whether its
/// target is already burning; the only thing that stopped a second one was
/// <c>CBaseAnimating::Ignite</c>'s <c>if ( IsOnFire() ) return;</c>, a flag
/// that only that one path sets. <c>env_entity_igniter</c> did not go through
/// it — its <c>InputIgnite</c> calls <c>Create</c> directly — so a map that
/// fired Ignite twice at the same crate got two flames, two burn rates and two
/// sets of sounds, and the second one's <c>SetLifetime</c> only moved its own
/// deadline. Here a target that is already alight has its deadline moved, which
/// is what <c>IgniteLifetime</c> was reaching through <c>GetEffectEntity</c> to
/// say.
/// </para>
///
/// <para>
/// NO REGISTRY OF ITS OWN. Which entities are flames is a question
/// <see cref="IEntityDirectory.OfClass"/> already answers, and answers from the
/// live instance table rather than from a list this class would have to
/// remember to clear at level shutdown. <see cref="EnvSmokeStack"/> made the
/// same choice for the same reason.
/// </para>
/// </summary>
public sealed class Flames
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public Flames(ILog log, IEntityDirectory directory, IEngineEntities entities)
    {
        _log = log;
        Directory = directory;
        Entities = entities;
    }

    /// <summary>finding the flames that already exist.</summary>
    private readonly IEntityDirectory Directory;

    /// <summary>making one that does not.</summary>
    private readonly IEngineEntities Entities;

    /// <summary>
    /// What a flame is drawn and heard with, loaded at level init.
    ///
    /// <para>
    /// HERE rather than on the flame itself, because a flame is created when
    /// something catches fire and that is long after the only moment a precache
    /// is allowed. Stock says the same thing with
    /// <c>PRECACHE_REGISTER(entityflame)</c> and <c>CEntityFlame::Precache</c> —
    /// a registration that ran at level load for a class no map contains — and
    /// that registration left the build with <c>EntityFlame.cpp</c>. Without
    /// this call nothing precaches the burning sounds at all.
    /// </para>
    ///
    /// <para>
    /// The PARTICLE SYSTEM is an addition rather than a copy. Stock never
    /// precached <c>burning_character</c> from the server:
    /// <c>C_EntityFlame::CreateEffect</c> asks <c>ParticleProp()</c> for it by
    /// name on the client. The managed flame's client half goes through the
    /// engine's own precache table instead, and <see cref="EnvFire"/> found and
    /// fixed exactly this gap for the eight <c>env_fire_*</c> systems: a
    /// particle system that was never precached is simply not in the table the
    /// client looks in, and nothing says so.
    /// </para>
    /// </summary>
    public static void Precache(IEnginePrecache precache)
    {
        precache.PrecacheSound(FlameRules.BurningFleshSound);
        precache.PrecacheSound(FlameRules.BurningObjectSound);
        precache.PrecacheSound(FlameRules.StopBurningSound);
        precache.PrecacheParticleSystem(FlameRules.ParticleSystem);
    }

    /// <summary>Every flame currently burning.</summary>
    public IEnumerable<SsEntityFlame> Live
        => Directory.OfClass(EntityNames.SsEntityFlame).OfType<SsEntityFlame>();

    /// <summary>The flame already burning on this target, or null.</summary>
    public SsEntityFlame? On(EntityHandle target)
    {
        foreach (var flame in Live)
        {
            if (flame.Attached == target)
                return flame;
        }

        return null;
    }

    /// <summary>
    /// Sets something alight for a while, and answers the flame doing it.
    ///
    /// <para>
    /// <paramref name="sizeOverride"/> is stock's optional <c>flSize</c> in
    /// world units; zero means "measure the target", which is what
    /// <c>CEntityFlame::Create</c> did from its collision bounds.
    /// </para>
    /// </summary>
    public SsEntityFlame? Ignite(EntityHandle target, float lifetime, float sizeOverride = 0f)
    {
        if (!Entities.IsValid(target))
            return null;

        var existing = On(target);

        if (existing is not null)
        {
            existing.SetLifetime(lifetime);
            return existing;
        }

        var handle = Entities.Create(EntityNames.SsEntityFlame);

        if (!Entities.IsValid(handle))
        {
            _log.Warning("[SourceSharp] could not create a flame\n");
            return null;
        }

        // Spawned BEFORE it is attached, because attaching PARENTS it, and the
        // hierarchy refuses a link whose child has not spawned.
        Entities.Spawn(handle);

        if (Directory.Get(handle) is not SsEntityFlame flame)
        {
            _log.Warning("[SourceSharp] ss_entityflame has no managed half\n");
            Entities.Remove(handle);
            return null;
        }

        flame.AttachTo(target, lifetime, sizeOverride);
        return flame;
    }
}
