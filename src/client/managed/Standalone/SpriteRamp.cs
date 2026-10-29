//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Standalone;

/// <summary>
/// A sprite's scale and brightness as the client draws them while a change
/// arrives over time: C_Sprite's OnDataChanged, ClientThink, GetRenderScale
/// and GetRenderBrightness (Sprite.cpp).
///
/// <para>
/// The server networks the target and how long the change takes
/// (CSprite::SetScale / SetBrightness with a time); the client notices a new
/// target, starts from the previous target -- stock's, not the value it was
/// part way to -- and lerps until the time is up.
/// </para>
/// </summary>
public sealed class SpriteRamp
{
    private float _startScale, _destScale, _scaleTimeStart;
    private int _startBrightness, _destBrightness;
    private float _brightnessTimeStart;

    /// <summary>OnDataChanged's DATA_UPDATE_CREATED: no ramp, the values as they arrived.</summary>
    public void Created(float scale, int brightness)
    {
        _startScale = _destScale = scale;
        _startBrightness = _destBrightness = brightness;
    }

    /// <summary>ClientThink: a target that moved starts a ramp from the old target, now.</summary>
    public void Think(float now, float scale, int brightness)
    {
        if (scale != _destScale)
        {
            _startScale = _destScale;
            _destScale = scale;
            _scaleTimeStart = now;
        }

        if (brightness != _destBrightness)
        {
            _startBrightness = _destBrightness;
            _destBrightness = brightness;
            _brightnessTimeStart = now;
        }
    }

    /// <summary>GetRenderScale: the target once the time is up (or with none), the lerp before.</summary>
    public float Scale(float now, float scale, float scaleTime)
    {
        if (scaleTime == 0f || _scaleTimeStart + scaleTime < now)
            return scale;

        float timeDelta = (now - _scaleTimeStart) / scaleTime;
        return _startScale + (_destScale - _startScale) * timeDelta;
    }

    /// <summary>GetRenderBrightness, truncated to an integer as stock's is.</summary>
    public int Brightness(float now, int brightness, float brightnessTime)
    {
        if (brightnessTime == 0f || _brightnessTimeStart + brightnessTime < now)
            return brightness;

        float timeDelta = (now - _brightnessTimeStart) / brightnessTime;
        return (int)(_startBrightness + (_destBrightness - _startBrightness) * timeDelta);
    }
}
