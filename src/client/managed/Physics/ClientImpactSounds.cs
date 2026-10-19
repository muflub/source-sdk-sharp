//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Physics;

/// <summary>
/// The client's impact sounds, as stock's client physics.cpp and vphysics_sound.h
/// made them: CCollisionEvent::PostCollision's gate, ObjectSound's volume,
/// CPhysicsSystem::AddImpactSound's list (physicssound::AddImpactSound, from the
/// world on CHAN_STATIC) and physicssound::PlayImpactSounds at the end of the
/// physics frame (plan_client_novalve C6, cluster 9; lane c6-input).
/// </summary>
public sealed class ClientImpactSounds(IClientImpactSoundWorld world, IClientImpactSoundEmitter sounds) : IClientImpactSink
{
    /// <summary>CHAN_STATIC: CPhysicsSystem::AddImpactSound's channel.</summary>
    public const int ChanStatic = 6;

    /// <summary>SOUND_FROM_WORLD: the entity every impact sound is played from.</summary>
    public const int SoundFromWorld = 0;

    /// <summary>"max volume at 320 in/s".</summary>
    private const float FullVolumeSpeed = 320f;

    /// <summary>physicssound::AddImpactSound: past this many queued sounds, every impact merges.</summary>
    private const int MergeAll = 4;

    private struct QueuedSound
    {
        public nint GameData;
        public int EntityIndex;
        public int Channel;
        public float Volume;
        public float ImpactSpeed;
        public int SurfaceProps;
        public int SurfacePropsHit;
        public Vector Origin;
    }

    private readonly List<QueuedSound> _list = new();

    /// <summary>The sounds queued for the frame's end.</summary>
    public int Queued => _list.Count;

    /// <summary>Sounds handed to the engine so far.</summary>
    public long Played { get; private set; }

    /// <summary>CCollisionEvent::PostCollision: a hard enough, spaced enough impact sounds both sides.</summary>
    public void PostCollision(in VCollisionEvent ev)
    {
        if (ev.DeltaCollisionTime > 0.1f && ev.CollisionSpeed > 70f)
        {
            ObjectSound(0, in ev);
            ObjectSound(1, in ev);
        }
    }

    /// <summary>CCollisionEvent::ObjectSound.</summary>
    private void ObjectSound(int index, in VCollisionEvent ev)
    {
        nint obj = ev.Object(index);
        if (obj == 0 || world.IsStatic(obj))
            return;

        // Stock's `speed` is the collision speed SQUARED, and it is what reaches
        // AddImpactSound as the impact speed too.
        float speed = ev.CollisionSpeed * ev.CollisionSpeed;
        int surfaceProps = index == 0 ? ev.SurfaceProps0 : ev.SurfaceProps1;
        int surfacePropsHit = index == 0 ? ev.SurfaceProps1 : ev.SurfaceProps0;

        nint gameData = world.GameData(obj);
        if (gameData == 0)
            return;

        float volume = speed * (1.0f / (FullVolumeSpeed * FullVolumeSpeed));
        if (volume > 1.0f)
            volume = 1.0f;

        if (surfaceProps >= 0)
            AddImpactSound(gameData, SoundFromWorld, ChanStatic, obj, surfaceProps, surfacePropsHit, volume, speed);
    }

    /// <summary>physicssound::AddImpactSound: one slot per material, everything merged past four.</summary>
    private void AddImpactSound(nint gameData, int entityIndex, int channel, nint obj, int surfaceProps,
                                int surfacePropsHit, float volume, float impactSpeed)
    {
        impactSpeed += 1e-4f;
        for (int i = _list.Count - 1; i >= 0; --i)
        {
            QueuedSound sound = _list[i];
            if (surfaceProps == sound.SurfaceProps || _list.Count > MergeAll)
            {
                if (volume > sound.Volume)
                {
                    sound.Origin = world.Position(obj);
                    sound.GameData = gameData;
                    sound.EntityIndex = entityIndex;
                    sound.Channel = channel;
                    sound.SurfacePropsHit = surfacePropsHit;
                }
                sound.Volume += volume;
                sound.ImpactSpeed = MathF.Max(impactSpeed, sound.ImpactSpeed);
                _list[i] = sound;
                return;
            }
        }

        _list.Add(new QueuedSound
        {
            GameData = gameData,
            EntityIndex = entityIndex,
            Channel = channel,
            Origin = world.Position(obj),
            SurfaceProps = surfaceProps,
            SurfacePropsHit = surfacePropsHit,
            Volume = volume,
            ImpactSpeed = impactSpeed,
        });
    }

    /// <summary>
    /// physicssound::PlayImpactSounds: newest first, the soft sound against a
    /// softer surface or under the hard-velocity threshold, and the frame's
    /// list emptied whatever happened -- including stock's stop at the first
    /// sound whose script has no parameters.
    /// </summary>
    public void Play()
    {
        for (int i = _list.Count - 1; i >= 0; --i)
        {
            QueuedSound sound = _list[i];
            if (world.Surface(sound.SurfaceProps) is not { } surf || surf.ImpactHard is null)
                continue;

            string name = surf.ImpactHard;
            if (world.Surface(sound.SurfacePropsHit) is { } hit && surf.ImpactSoft is not null)
            {
                if (hit.HardnessFactor < surf.HardThreshold ||
                    (surf.HardVelocityThreshold > 0 && surf.HardVelocityThreshold > sound.ImpactSpeed))
                {
                    name = surf.ImpactSoft;
                }
            }

            float volume = sound.Volume > 1 ? 1 : sound.Volume;
            if (!sounds.EmitImpactSound(name, sound.Origin, sound.Channel, volume))
                break;
            Played++;
        }

        _list.Clear();
    }

    /// <summary>A level's end: nothing queued outlives the environment it came from.</summary>
    public void Clear() => _list.Clear();
}
