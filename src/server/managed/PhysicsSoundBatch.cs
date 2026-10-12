//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>What the queued physics sounds read and do, outside the batch.</summary>
public interface IPhysicsSoundWorld
{
    /// <summary>Where the entity's physics object is, or null when it has none.</summary>
    Vector? ObjectPosition(EntityHandle entity);

    /// <summary>
    /// A breaking entity's collision origin and its physics object's material,
    /// or null when it has no object or no networked body.
    /// </summary>
    (Vector Origin, int Material)? BreakSource(EntityHandle entity);

    /// <summary><c>physprops-&gt;GetSurfaceData( surfaceProps )</c>'s sounds, or null.</summary>
    SurfaceImpactSounds? Surface(int surfaceProps);

    /// <summary>
    /// The carry-out of one queued sound: the script's parameters, a PAS
    /// attenuation filter at <paramref name="origin"/> at their level, and an
    /// emit from the world on <paramref name="channel"/> at the script's volume
    /// times <paramref name="volume"/>. False when the script has no parameters
    /// (stock's GameSound_GetParameters failing).
    /// </summary>
    bool Emit(string soundName, Vector origin, int channel, float volume);
}

/// <summary>
/// The server's queued impact and break sounds (stock's
/// CPhysicsHook::m_impactSounds / m_breakSounds and vphysics_sound.h's
/// AddImpactSound / AddBreakSound / PlayImpactSounds / PlayBreakSounds;
/// native's physics.cpp and ss_physics_sound.h at bc21e4d46), managed
/// (plan_physics_bones_native_zero 2.3).
///
/// <para>
/// A step's impacts fold by surface, five at most; its breaks fold by surface
/// once more than two are queued. Every 50 ms of frame time, at the frame's
/// client update, the impacts play newest first -- the soft sound against a
/// softer surface or under the hard-velocity threshold -- and then the breaks.
/// Two of stock's quirks are kept: an impact whose script has no parameters
/// ends the walk but the queue is still cleared, while a missing break script
/// returns with its queue NOT cleared.
/// </para>
/// </summary>
public sealed class PhysicsSoundBatch : IGameLifecycle, IFrameLifecycle
{
    /// <summary>CHAN_STATIC: every break is played on it.</summary>
    public const int ChanStatic = 6;

    /// <summary>How much frame time passes between plays.</summary>
    public const float PlayInterval = 0.05f;

    /// <summary>Past this many queued impacts, every new one folds.</summary>
    private const int FoldAll = 4;

    /// <summary>Past this many queued breaks, a break of a queued surface folds.</summary>
    private const int FoldBreaks = 2;

    private struct Impact
    {
        public EntityHandle Entity;
        public int Channel;
        public float Volume;
        public float ImpactSpeed;
        public int SurfaceProps;
        public int SurfacePropsHit;
        public Vector Origin;
    }

    private struct Break
    {
        public Vector Origin;
        public int SurfaceProps;
    }

    private readonly IPhysicsSoundWorld _world;
    private readonly List<Impact> _impacts = new();
    private readonly List<Break> _breaks = new();
    private float _sinceLastPlay;

    public PhysicsSoundBatch(IPhysicsSoundWorld world) => _world = world;

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PhysicsSoundBatch;

    int IFrameLifecycle.FrameOrder => (int)FrameStage.PhysicsSounds;

    /// <summary>Impacts queued for the next play.</summary>
    public int QueuedImpacts => _impacts.Count;

    /// <summary>Breaks queued for the next play.</summary>
    public int QueuedBreaks => _breaks.Count;

    /// <summary>Sounds handed to the engine since the module started.</summary>
    public long Played { get; private set; }

    /// <summary>
    /// Stock's PhysicsImpactSound into the batch: the entity's object's
    /// position, folded by surface. Nothing for an entity with no object.
    /// </summary>
    public void AddImpactSound(EntityHandle entity, int channel, int surfaceProps, int surfacePropsHit,
                               float volume, float impactSpeed)
    {
        if (_world.ObjectPosition(entity) is not Vector at)
            return;

        impactSpeed += 1e-4f;
        for (int i = _impacts.Count - 1; i >= 0; --i)
        {
            Impact queued = _impacts[i];
            if (surfaceProps == queued.SurfaceProps || _impacts.Count > FoldAll)
            {
                // A louder hit takes the entry over.
                if (volume > queued.Volume)
                {
                    queued.Origin = at;
                    queued.Entity = entity;
                    queued.Channel = channel;
                    queued.SurfacePropsHit = surfacePropsHit;
                }

                queued.Volume += volume;
                queued.ImpactSpeed = MathF.Max(impactSpeed, queued.ImpactSpeed);
                _impacts[i] = queued;
                return;
            }
        }

        _impacts.Add(new Impact
        {
            Entity = entity,
            Channel = channel,
            Origin = at,
            SurfaceProps = surfaceProps,
            SurfacePropsHit = surfacePropsHit,
            Volume = volume,
            ImpactSpeed = impactSpeed,
        });
    }

    /// <summary>
    /// Stock's PhysBreakSound into the batch: the entity's collision origin and
    /// its object's material, if that surface has a break sound at all.
    /// </summary>
    public void AddBreakSound(EntityHandle entity)
    {
        if (_world.BreakSource(entity) is not var (origin, surfaceProps))
            return;

        if (_world.Surface(surfaceProps) is not { BreakSound: not null })
            return;

        for (int i = _breaks.Count - 1; i >= 0; --i)
        {
            Break queued = _breaks[i];
            if (_breaks.Count > FoldBreaks && surfaceProps == queued.SurfaceProps)
            {
                queued.Origin = (queued.Origin + origin) * 0.5f;
                _breaks[i] = queued;
                return;
            }
        }

        _breaks.Add(new Break { Origin = origin, SurfaceProps = surfaceProps });
    }

    /// <summary>CPhysicsHook::PreClientUpdate: every 50 ms of frame time, the impacts then the breaks.</summary>
    public void OnPreClientUpdate(float frameTime)
    {
        _sinceLastPlay += frameTime;
        if (_sinceLastPlay > PlayInterval)
        {
            PlayImpacts();
            _sinceLastPlay = 0f;
            PlayBreaks();
        }
    }

    private void PlayImpacts()
    {
        for (int i = _impacts.Count - 1; i >= 0; --i)
        {
            Impact queued = _impacts[i];
            if (_world.Surface(queued.SurfaceProps) is not { ImpactHard: { } hard } surface)
                continue;

            // The soft variant when the other surface is softer than this one's
            // threshold, or the hit slower than its velocity threshold.
            string name = hard;
            if (surface.ImpactSoft is { } soft && _world.Surface(queued.SurfacePropsHit) is { } other)
            {
                bool softer = other.HardnessFactor < surface.HardThreshold;
                bool slower = surface.HardVelocityThreshold > 0 && surface.HardVelocityThreshold > queued.ImpactSpeed;
                if (softer || slower)
                    name = soft;
            }

            if (!_world.Emit(name, queued.Origin, queued.Channel, MathF.Min(queued.Volume, 1f)))
                break;
            Played++;
        }

        _impacts.Clear();
    }

    private void PlayBreaks()
    {
        for (int i = _breaks.Count - 1; i >= 0; --i)
        {
            Break queued = _breaks[i];
            string? name = _world.Surface(queued.SurfaceProps)?.BreakSound;
            if (name is null || !_world.Emit(name, queued.Origin, ChanStatic, 1f))
                return;
            Played++;
        }

        _breaks.Clear();
    }

    /// <summary>A level's end: nothing queued outlives the entities it named.</summary>
    public void OnLevelShutdownPostEntity()
    {
        _impacts.Clear();
        _breaks.Clear();
    }
}
