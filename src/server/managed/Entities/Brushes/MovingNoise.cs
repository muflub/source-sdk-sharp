//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// CBasePlatTrain's moving noise (PlayMovingSound, StopMovingSound): a
/// CHAN_STATIC sound patch at ATTN_NORM, played at the platform's volume,
/// and destroyed when it stops. One per platform; it holds only the patch.
/// </summary>
public sealed class MovingNoise
{
    /// <summary>CHAN_STATIC, and ATTN_NORM's level.</summary>
    private const int ChannelStatic = 6, SoundLevelNorm = 75;

    /// <summary>CBasePlatTrain::Precache's volume for a map that gave none.</summary>
    public const float DefaultVolume = 0.85f;

    private int _patch = -1;

    /// <summary>The noise started afresh, at <paramref name="volume"/>; 0 is <see cref="DefaultVolume"/>.</summary>
    public void Play(IEngineSound sound, EntityHandle entity, string name, float volume)
    {
        Stop(sound);
        if (name.Length == 0)
            return;

        _patch = sound.SoundPatchCreate(entity, ChannelStatic, name, SoundLevelNorm);
        sound.SoundPatchEmit(_patch, volume == 0f ? DefaultVolume : volume, 100f, 0, 0f, emitCaption: true);
    }

    /// <summary>SoundDestroy: stopped and forgotten.</summary>
    public void Stop(IEngineSound sound)
    {
        if (_patch < 0)
            return;

        sound.SoundPatchStop(_patch);
        sound.SoundPatchFree(_patch);
        _patch = -1;
    }
}
