//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Server.Entities.Triggers;

/// <summary>
/// trigger_particle (stock's <c>CTriggerParticle</c>, tf_triggers.cpp:719-798):
/// every toucher the map's kinds let in is dressed with the named particle
/// system. It thinks never — the effect is spawned on the way in and nothing
/// stops it; stock has no spawn/stop inputs on this class either.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class TriggerParticle(IGameClock clock, IEngineEntities entities, IEnginePrecache precache,
                                    ITempEnts temps)
    : TFPlayerTrigger(clock)
{
    public const string MapClass = "trigger_particle";

    /// <summary><c>PARTICLE_DISPATCH_FROM_ENTITY</c> (particle_parse.h:37).</summary>
    private const int FromEntity = 1 << 0;

    /// <summary>The follow attach types stock's broadcast filter names (particle_parse.cpp:329-330).</summary>
    private const int PattachAbsoriginFollow = 1, PattachPointFollow = 4, PattachRootboneFollow = 6;

    [ScriptVars] public TriggerParticleVars ParticleVars = new();

    /// <summary>Any toucher: stock's StartTouch runs the filter, not IsPlayer (tf_triggers.cpp:775-798).</summary>
    public override bool TouchedByAnything => true;

    public override void OnSpawn()
    {
        base.OnSpawn();
        if (ParticleVars.ParticleName.Length > 0)
            precache.PrecacheParticleSystem(ParticleVars.ParticleName);
    }

    /// <summary>
    /// <c>CTriggerParticle::StartTouch</c> (tf_triggers.cpp:775-798), through
    /// <c>DispatchParticleEffect( name, m_nAttachType, pOther, iAttachment )</c>
    /// (particle_parse.cpp:305-340): the record names the entity, its origin,
    /// the attach type and the looked-up attachment (-1 when the map named
    /// none). The system index is the table's; a missing precache is 0, the
    /// error system. Stock's broadcast filter is only for the follow attach
    /// types; ABSORIGIN and every other one take the PAS filter of the origin.
    /// </summary>
    protected override void StartTouch(EntityHandle toucher)
    {
        // The default CEffectData: the scale is 1 (effect_dispatch_data.h).
        TeEffectData data = new()
        {
            Scale = 1f,
            HitBox = precache.ParticleSystemIndex(ParticleVars.ParticleName),
            EntIndex = toucher.EntryIndex,
            Flags = FromEntity,
            Origin = entities.GetOrigin(toucher),
            DamageType = ParticleVars.AttachmentType,
            AttachmentIndex = ParticleVars.AttachmentName.Length > 0
                ? entities.LookupAttachmentAny(toucher, ParticleVars.AttachmentName)
                : -1,
        };

        TeFilter filter = ParticleVars.AttachmentType
                          is PattachAbsoriginFollow or PattachPointFollow or PattachRootboneFollow
            ? TeFilters.Broadcast
            : TeFilters.Pas(data.Origin);

        temps.DispatchEffect(filter, 0f, "ParticleEffect", in data);
    }
}

/// <summary>The map keys <see cref="TriggerParticle"/> declares (tf_triggers.cpp:738-742), with the constructor's defaults.</summary>
[ScriptVars]
public partial class TriggerParticleVars
{
    [VarName("particle_name")] public string ParticleName { get; set; } = string.Empty;

    [VarName("attachment_name")] public string AttachmentName { get; set; } = string.Empty;

    /// <summary>A <c>ParticleAttachment_t</c>; stock's constructor default is <c>PATTACH_ABSORIGIN</c> (0).</summary>
    [VarName("attachment_type")] public int AttachmentType { get; set; }
}
