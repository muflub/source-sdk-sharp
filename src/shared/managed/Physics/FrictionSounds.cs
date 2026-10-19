//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>The physics surface table, one surfaceprop at a time.</summary>
public interface IPhysicsSurfaceTable
{
    MoveSurface Of(int surfaceProps);
}

/// <summary>The engine's surface table, through the one lookup it answers.</summary>
public sealed class EnginePhysicsSurfaceTable(PhysicsSurfaces surfaces) : IPhysicsSurfaceTable
{
    public MoveSurface Of(int surfaceProps) => surfaces.Of(surfaceProps);
}

/// <summary>The two effects PhysFrictionEffect throws, as a side's g_pEffects makes them.</summary>
public interface IFrictionEffects
{
    /// <summary><c>g_pEffects-&gt;Dust( point, direction, 1, 16 )</c>.</summary>
    void Dust(Vector point, Vector direction);

    /// <summary><c>g_pEffects-&gt;MetalSparks( point, direction )</c>.</summary>
    void MetalSparks(Vector point, Vector direction);
}

/// <summary>
/// The scrape a sliding physics object makes (physics stock parity D2):
/// stock's server CCollisionEvent::Friction -> VPhysicsFriction ->
/// PhysFrictionSound, and UpdateFrictionSounds' stop. Fed by
/// the server's <c>PhysicsEventRouter</c> from the ring's Friction kind, and
/// updated once a frame after it.
///
/// <para>
/// The rules are stock's, multiplayer arm: no sound under 75 energy or against
/// an invisible ('X') material on either side; volume is the square of energy
/// / 15500, clamped to 1, and nothing under 1/128; the surface's rough scrape,
/// or its smooth one when the hit surface's roughness is under this surface's
/// threshold; a new scrape only above 0.1 of the script's volume, on CHAN_BODY
/// at the entity; an ongoing one re-ramped over 0.1 s at most every 0.5 s and
/// otherwise only kept alive; and one not kept alive for 0.1 s shut down. Four
/// at once, the size of stock's friction table.
/// </para>
/// </summary>
public sealed class FrictionSounds
{
    /// <summary>ENERGY_VOLUME_SCALE.</summary>
    public const float EnergyVolumeScale = 1f / 15500f;

    /// <summary>The friction table's size (CCollisionEvent::m_current).</summary>
    public const int MaxScrapes = 4;

    private const float MinEnergy = 75f;
    private const int InvisibleMaterial = 'X';
    private const int ChanBody = 4;
    private const float EffectInterval = 0.5f;
    private const float StopAfter = 0.1f;
    private const float RampTime = 0.1f;

    private readonly IEngineSound _sound;
    private readonly ISoundPatchRamps _controller;
    private readonly IPhysicsSurfaceTable _surfaces;
    private readonly List<Scrape> _scrapes = new(MaxScrapes);

    /// <summary>
    /// Either side's: the two effects a scrape throws off, as that side's
    /// g_pEffects makes them. <paramref name="soundSource"/> names the entity
    /// a scrape's sound is created on, where that is not the scraping entity
    /// itself: the client's PhysFrictionSound lets the world play it for a
    /// clientside entity ("clientside created entites doesn't have a valid
    /// entindex"). The scrape is still the entity's.
    /// </summary>
    public FrictionSounds(IEngineSound sound, ISoundPatchRamps controller, IPhysicsSurfaceTable surfaces,
                          IFrictionEffects effects, Func<EntityHandle, EntityHandle>? soundSource = null)
    {
        _sound = sound;
        _controller = controller;
        _surfaces = surfaces;
        _effects = effects;
        _soundSource = soundSource;
    }

    private readonly Func<EntityHandle, EntityHandle>? _soundSource;

    /// <summary>the dust and the sparks a scrape throws off (stock's PhysFrictionEffect, g_pEffects).</summary>
    private readonly IFrictionEffects _effects;

    /// <summary>Scrapes playing now.</summary>
    public int Active => _scrapes.Count;

    /// <summary>Scrapes started since the module started. For cs_sv_physics_events and the gate.</summary>
    public long Started { get; private set; }

    /// <summary>One friction report for the entity's object, at <paramref name="now"/>.</summary>
    /// <summary>
    /// Stock's CCollisionEvent::Friction past its owner lookup: inside the
    /// 500 ms MP throttle nothing; otherwise the entity's VPhysicsFriction (the
    /// scrape sound), then PhysFrictionEffect at the contact
    /// (<paramref name="point"/>) against the object's
    /// <paramref name="velocity"/> there.
    /// </summary>
    public void Friction(EntityHandle entity, float energy, int surfaceProps, int surfacePropsHit, float now,
                         Vector point = default, Vector velocity = default)
    {
        int index = _scrapes.FindIndex(s => s.Entity == entity);

        // In MP, play sound and effects once every 500 ms, no ongoing updates.
        if (index >= 0 && _scrapes[index].LastEffect + EffectInterval > now)
        {
            _scrapes[index] = _scrapes[index] with { LastUpdate = now };
            return;
        }

        Sound(entity, energy, surfaceProps, surfacePropsHit, now, index);
        Effect(energy, surfaceProps, surfacePropsHit, point, velocity);
    }

    /// <summary>PhysFrictionEffect: the dust or the sparks a scrape throws off at the contact, against the velocity there.</summary>
    public void Effect(float energy, int surfaceProps, int surfacePropsHit, Vector point, Vector velocity)
    {
        if (surfaceProps < 0 || surfacePropsHit < 0)
            return;

        FrictionEffect effect = PhysicsFrictionEffect.Choose(_surfaces.Of(surfaceProps).Material,
                                                             _surfaces.Of(surfacePropsHit).Material, energy);
        if (effect == FrictionEffect.None)
            return;

        Vector against = (velocity * -1f).Normalized;
        if ((effect & FrictionEffect.Dust) != 0)
            _effects.Dust(point, against);
        if ((effect & FrictionEffect.MetalSparks) != 0)
            _effects.MetalSparks(point, against);
    }

    // PhysFrictionSound( entity, object, energy, surface, hit ).
    private void Sound(EntityHandle entity, float energy, int surfaceProps, int surfacePropsHit, float now, int index)
    {
        if (energy < MinEnergy || surfaceProps < 0)
            return;

        MoveSurface hit = _surfaces.Of(surfacePropsHit);
        MoveSurface self = _surfaces.Of(surfaceProps);
        if (hit.Material == InvisibleMaterial || self.Material == InvisibleMaterial)
            return;

        float scaled = energy * EnergyVolumeScale;
        float volume = Math.Clamp(scaled * scaled, 0f, 1f);

        string? name = _sound.SurfaceScrapeSound(surfaceProps, smooth: false);
        string? smooth = _sound.SurfaceScrapeSound(surfaceProps, smooth: true);
        if (smooth is not null && hit.RoughnessFactor < self.RoughThreshold)
            name = smooth;

        if (name is null || volume <= 1f / 128f)
            return;

        // The name-based half: a slot in the table, the script's parameters.
        if (index < 0 && _scrapes.Count >= MaxScrapes)
            return;

        if (_sound.SoundScriptParameters(name) is not { } script)
            return;

        if (index < 0)
        {
            // Don't create really quiet scrapes.
            if (script.Volume * volume <= 0.1f)
                return;

            int patch = _sound.SoundPatchCreate(_soundSource?.Invoke(entity) ?? entity, ChanBody, name, script.SoundLevel);
            if (patch == 0)
                return;

            _controller.Play(patch, script.Volume * volume, script.Pitch, 0f);
            _scrapes.Add(new Scrape(entity, patch, now, now));
            Started++;
            return;
        }

        Scrape scrape = _scrapes[index];
        float pitch = volume * (script.PitchHigh - script.PitchLow) + script.PitchLow;
        _controller.ChangeVolume(scrape.Patch, script.Volume * volume, RampTime);
        _controller.ChangePitch(scrape.Patch, pitch, RampTime);
        _scrapes[index] = scrape with { LastUpdate = now, LastEffect = now };
    }

    /// <summary>UpdateFrictionSounds: a scrape not kept alive for 0.1 s has finished.</summary>
    public void Update(float now)
    {
        for (int i = _scrapes.Count - 1; i >= 0; i--)
        {
            if (_scrapes[i].LastUpdate >= now - StopAfter)
                continue;

            // ShutdownFriction: SoundDestroy, the managed half and the native.
            _controller.Destroy(_scrapes[i].Patch);
            _sound.SoundPatchFree(_scrapes[i].Patch);
            _scrapes.RemoveAt(i);
        }
    }

    private readonly record struct Scrape(EntityHandle Entity, int Patch, float LastUpdate, float LastEffect);
}
