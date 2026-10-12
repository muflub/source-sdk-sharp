//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>ParticleEffectCallback</c> (c_particle_system.cpp), the
/// <c>ParticleEffect</c> client effect: what <c>DispatchParticleEffect</c>
/// (fx_blood, fx_water, the particle tracer, the server's dispatches) asks
/// for. Neither it nor <c>ParticleEffectStop</c> existed in this build, so
/// every such dispatch drew nothing (lane c5-particles, a dead stock arm
/// ported).
/// </summary>
public sealed class ParticleEffectHandler(IClientParticleOneShots oneShots, IEngineEffects effects,
                                          IClientStandalones registry) : IClientEffectHandler
{
    /// <summary><c>PARTICLE_DISPATCH_FROM_ENTITY</c>.</summary>
    public const int FromEntity = 1 << 0;

    /// <summary><c>PARTICLE_DISPATCH_RESET_PARTICLES</c>.</summary>
    public const int ResetParticles = 1 << 1;

    /// <summary><c>CUSTOM_COLOR_CP1</c> and <c>CUSTOM_COLOR_CP2</c> (effect_dispatch_data.h).</summary>
    public const int CustomColorPoint1 = 9, CustomColorPoint2 = 10;

    /// <summary><c>PATTACH_ABSORIGIN</c>, <c>PATTACH_ABSORIGIN_FOLLOW</c>.</summary>
    public const int AttachAbsOrigin = 0, AttachAbsOriginFollow = 1;

    public string EffectName => "ParticleEffect";

    /// <summary>
    /// Entity dispatches whose attachment needs a transform the managed
    /// entities do not offer yet (a custom origin, an attachment point, the
    /// root bone): counted, not drawn. Stock created them through the
    /// entity's ParticleProp; the follow-up is with fx.cpp's local-space
    /// effects (plan_client_novalve §2.2.1).
    /// </summary>
    public int UnsupportedAttachments { get; private set; }

    public void Dispatch(in EffectData data)
    {
        // SuppressingParticleEffects() is false on the client: nothing here
        // calls SuppressParticleEffects (effect_dispatch_data.cpp's client
        // half left with no importer, docs/client_evictions.md).
        string? name = oneShots.NameFromIndex(data.HitBox);
        if (name is null)
            return;

        if ((data.Flags & FromEntity) != 0)
        {
            DispatchOnEntity(data, name);
            return;
        }

        // GameRules()->TranslateEffectForVisionFilter( "particles", name ):
        // CGameRules answers the name unchanged and HL2MP does not override it.
        Span<ParticleControlPoint> points = stackalloc ParticleControlPoint[5];
        int n = 0;
        points[n++] = new ParticleControlPoint(0, data.Origin);
        points[n++] = new ParticleControlPoint(1, data.Start);
        if (data.CustomColors != 0)
        {
            points[n++] = new ParticleControlPoint(CustomColorPoint1, data.Color1);
            points[n++] = new ParticleControlPoint(CustomColorPoint2, data.Color2);
        }
        if (data.ControlPoint1 != 0)
            points[n++] = new ParticleControlPoint(1, data.ControlPointOffset);

        MoveVec.AngleVectors(data.Angles, out Vector forward, out Vector right, out Vector up);
        oneShots.Start(name, data.Origin, points[..n], forward, right, up);
    }

    private void DispatchOnEntity(in EffectData data, string name)
    {
        if (registry.GetByIndex(data.EntIndex) is not { } entity)
            return;

        int attach = data.DamageType;
        if (attach != AttachAbsOrigin && attach != AttachAbsOriginFollow)
        {
            UnsupportedAttachments++;
            return;
        }

        // ParticleProp()->StopEmission() before a reset; the shell keeps one
        // system per entity, so a new start replaces the old one either way.
        // PATTACH_ABSORIGIN is created at the origin and not followed in stock;
        // the shell's owned systems follow their owner (one known difference).
        if ((data.Flags & ResetParticles) != 0)
            effects.ParticleSystemStop(entity.Handle);
        effects.ParticleSystemStart(entity.Handle, name);
    }
}

/// <summary>
/// <c>ParticleEffectStopCallback</c> (c_particle_system.cpp), the
/// <c>ParticleEffectStop</c> client effect: the entity's systems stop
/// emitting.
/// </summary>
public sealed class ParticleEffectStopHandler(IEngineEffects effects, IClientStandalones registry) : IClientEffectHandler
{
    public string EffectName => "ParticleEffectStop";

    public void Dispatch(in EffectData data)
    {
        if (registry.GetByIndex(data.EntIndex) is { } entity)
            effects.ParticleSystemStop(entity.Handle);
    }
}
