//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The physcannon's claws on the viewmodel: C_WeaponPhysCannon's
/// m_ElementParameter (a CInterpolatedValue, fx_interpvalue.cpp) and the
/// parity check OnDataChanged runs on m_bOpen, which UpdateElementPosition
/// writes to the viewmodel's "active" pose parameter every client think.
/// Opening splines to 1 over 0.2 s, closing to 0 over 0.5 s, each from
/// wherever the claws are when the change arrives.
/// </summary>
public sealed class PhysCannonElements
{
    /// <summary>The pose parameter the claws are on (v_physcannon's "active").</summary>
    public const string PoseParameter = "active";

    private float _startTime, _endTime, _startValue, _endValue;
    private bool _open;

    /// <summary>
    /// The claws' position at <paramref name="now"/>, after taking in whether
    /// the gun says they are <paramref name="open"/>.
    /// </summary>
    public float Advance(bool open, float now)
    {
        if (open != _open)
        {
            // InitFromCurrent( goal, dt, INTERP_SPLINE )
            float from = Interp(now);
            _startValue = from;
            _endValue = open ? 1.0f : 0.0f;
            _startTime = now;
            _endTime = now + (open ? 0.2f : 0.5f);
            _open = open;
        }

        return Interp(now);
    }

    /// <summary>Shut, at once: a gun that has left the hands starts its next deploy closed.</summary>
    public void Reset()
    {
        _open = false;
        _startValue = _endValue = 0f;
        _startTime = _endTime = 0f;
    }

    private float Interp(float now)
    {
        if (now >= _endTime)
            return _endValue;

        if (now <= _startTime)
            return _startValue;

        return PhysCannonTuning.SimpleSplineRemapVal(now, _startTime, _endTime, _startValue, _endValue);
    }
}
