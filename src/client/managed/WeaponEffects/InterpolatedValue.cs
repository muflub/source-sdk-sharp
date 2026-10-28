//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared;

namespace SourceSharp.Client.WeaponEffects;

/// <summary>
/// <c>CInterpolatedValue</c> (fx_interpvalue.cpp): a value moving from one
/// number to another between two times, linearly or along a spline; before
/// the start it is the start, after the end the end. The clock is the
/// caller's (stock read <c>gpGlobals-&gt;curtime</c>).
/// </summary>
public sealed class InterpolatedValue
{
    /// <summary><c>InterpType_t</c>.</summary>
    public enum Kind
    {
        Linear = 0,
        Spline,
    }

    private float _startTime, _endTime, _startValue, _endValue;
    private Kind _kind = Kind.Linear;

    /// <summary><c>SetAbsolute</c>: the value with no range, from now.</summary>
    public void SetAbsolute(float value, float now)
    {
        _startValue = _endValue = value;
        _startTime = _endTime = now;
        _kind = Kind.Linear;
    }

    /// <summary><c>Init</c>: from one value to another over <paramref name="dt"/> seconds from now.</summary>
    public void Init(float startValue, float endValue, float dt, float now, Kind kind = Kind.Linear)
    {
        if (dt <= 0.0f)
        {
            SetAbsolute(endValue, now);
            return;
        }

        _startTime = now;
        _endTime = now + dt;
        _startValue = startValue;
        _endValue = endValue;
        _kind = kind;
    }

    /// <summary><c>InitFromCurrent</c>: from where it is now towards a value.</summary>
    public void InitFromCurrent(float endValue, float dt, float now, Kind kind = Kind.Linear)
        => Init(Interp(now), endValue, dt, now, kind);

    /// <summary><c>Interp</c>: the value at <paramref name="time"/>.</summary>
    public float Interp(float time)
    {
        if (time >= _endTime)
            return _endValue;

        if (time <= _startTime)
            return _startValue;

        return _kind == Kind.Spline
            ? PhysCannonTuning.SimpleSplineRemapVal(time, _startTime, _endTime, _startValue, _endValue)
            : _startValue + (_endValue - _startValue) * (time - _startTime) / (_endTime - _startTime);
    }
}
