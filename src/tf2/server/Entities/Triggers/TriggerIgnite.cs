//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities.Items;
using TF2.Shared;

namespace TF2.Server.Entities.Triggers;

/// <summary>
/// trigger_ignite (stock's <c>CTriggerIgnite</c>, tf_triggers.cpp): sets a
/// player alight on the way in, and burns everyone inside for
/// <c>damage_percent_per_second</c> of their maximum health every
/// <see cref="BurnInterval"/>, re-igniting whoever is not burning. Anything
/// else the map lets in is removed.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class TriggerIgnite(IGameClock clock, IEngineEntities entities, ITFMapPlayers players,
                                  IEngineDamage damage, IEngineSound sound, IEnginePrecache precache,
                                  ITempEnts temps)
    : TFPlayerTrigger(clock)
{
    public const string MapClass = "trigger_ignite";

    /// <summary><c>BURN_INTERVAL</c>.</summary>
    public const float BurnInterval = 0.1f;

    [ScriptVars] public TriggerIgniteVars IgniteVars = new();

    [SaveField] private float _lastBurnTime;
    [SaveField] private bool _burning;

    /// <summary>Whether the burn think is running (<c>m_pfnThink != NULL</c>).</summary>
    public bool Burning => _burning;

    /// <summary>
    /// Every toucher, not players only: stock's <c>IgniteEntity</c> removes
    /// what is not a player. Only a map that named the kinds lets one in
    /// (<see cref="Passes"/>).
    /// </summary>
    public override bool TouchedByAnything => true;

    public override void OnSpawn()
    {
        base.OnSpawn();
        if (IgniteVars.IgniteParticleName.Length > 0)
            precache.PrecacheParticleSystem(IgniteVars.IgniteParticleName);
        if (IgniteVars.IgniteSoundName.Length > 0)
            precache.PrecacheSound(IgniteVars.IgniteSoundName);
    }

    /// <summary>
    /// A player as the volume's kinds say; anything else only when the map's
    /// spawnflags name it (the core volume reads no spawnflags as "anybody",
    /// and here "anybody" would remove whatever crossed the volume).
    /// </summary>
    protected override bool Passes(EntityHandle toucher)
        => entities.IsPlayer(toucher) ? base.Passes(toucher) : Kinds != TriggerSpawnflags.None && base.Passes(toucher);

    /// <summary><c>CTriggerIgnite::StartTouch</c>.</summary>
    protected override void StartTouch(EntityHandle toucher)
    {
        IgniteEntity(toucher);

        if (!_burning)
        {
            _lastBurnTime = Clock.Now;
            _burning = true;
            SetNextThink(BurnInterval);
        }
    }

    /// <summary><c>BurnThink</c>: again while anyone burns.</summary>
    public override void OnThink()
    {
        if (BurnEntities() > 0)
            SetNextThink(BurnInterval);
        else
            _burning = false;
    }

    /// <summary>
    /// <c>IgniteEntity</c> (tf_triggers.cpp:645-679): the named particle at the
    /// toucher — <c>DispatchParticleEffect( name, vecEffectPos, vec3_angle )</c>,
    /// the world-position overload, so no entity rides the record — then the
    /// sound; a player not invulnerable and not already burning is set alight
    /// (<c>SelfBurn( burn_duration )</c>); anything else is removed.
    /// </summary>
    private void IgniteEntity(EntityHandle other)
    {
        Vector at = entities.GetOrigin(other);
        if (IgniteVars.IgniteParticleName.Length > 0)
        {
            // The default CEffectData: the scale is 1 (effect_dispatch_data.h);
            // no entity rides the record, so EntIndex 0, no flags, DamageType 0.
            TeEffectData data = new()
            {
                Scale = 1f,
                HitBox = precache.ParticleSystemIndex(IgniteVars.IgniteParticleName),
                Origin = at,
                Start = at,
            };
            temps.DispatchEffect(TeFilters.Pas(at), 0f, "ParticleEffect", in data);
        }

        if (IgniteVars.IgniteSoundName.Length > 0)
            sound.EmitSoundAtPosition(IgniteVars.IgniteSoundName, at);

        if (entities.IsPlayer(other))
        {
            if (!players.IsInvulnerable(other) && !players.InCond(other, TFCond.Burning))
                players.SelfBurn(other, IgniteVars.BurnDuration);
        }
        else
        {
            entities.Remove(other);
        }
    }

    /// <summary>
    /// <c>BurnEntities</c>: <c>dt * pct * 0.01 * GetMaxHealth()</c> of
    /// <c>DMG_BURN</c> to every player inside, re-igniting one that has gone
    /// out. Answers how many were burned.
    /// </summary>
    public int BurnEntities()
    {
        if (Disabled || Inside.Count == 0)
            return 0;

        float dt = Clock.Now - _lastBurnTime;
        _lastBurnTime = Clock.Now;
        int burned = 0;
        foreach (EntityHandle player in Inside.ToList())
        {
            if (!entities.IsPlayer(player))
                continue;

            float scale = IgniteVars.DamagePercentPerSecond * 0.01f;
            float amount = dt * scale * players.MaxHealth(player);

            // if player enters trigger invuln, we need to ignite them when it wears off.
            // We also don't want to ignite an already burning player
            if (!players.IsInvulnerable(player) && !players.InCond(player, TFCond.Burning))
                IgniteEntity(player);

            damage.TakeDamage(player, Handle, amount, DamageType.Burn);
            burned++;
        }

        return burned;
    }
}

/// <summary>The map keys <see cref="TriggerIgnite"/> declares (tf_triggers.cpp's datadesc), with the constructor's defaults.</summary>
[ScriptVars]
public partial class TriggerIgniteVars
{
    [VarName("burn_duration")] public float BurnDuration { get; set; } = 1f;

    [VarName("damage_percent_per_second")] public float DamagePercentPerSecond { get; set; } = 10f;

    [VarName("ignite_particle_name")] public string IgniteParticleName { get; set; } = string.Empty;

    [VarName("ignite_sound_name")] public string IgniteSoundName { get; set; } = string.Empty;
}
