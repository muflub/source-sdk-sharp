//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Render;

/// <summary>A frame's shake: the view origin offset, the roll added, and the angle fed to the rumble.</summary>
public readonly record struct ShakeFrame(Vector Offset, float Angle, float RumbleAngle);

/// <summary>GetFadeParams' answer: the fade colour, its alpha, and whether it modulates.</summary>
public readonly record struct FadeParams(byte R, byte G, byte B, byte A, bool Modulate);

/// <summary>
/// view_effects.cpp's CViewEffects: the screen shakes (Shake, CalcShake,
/// ApplyShake) and fades (Fade, FadeCalculate, GetFadeParams), one per client.
/// Float arithmetic in stock's order (view-effects-stock.tsv). The intro's fade
/// override (g_pIntroData, set only by C_ScriptIntro, which this build does
/// not compile) is the <paramref name="intro"/> argument of
/// <see cref="GetFadeParams"/>; the fade-done callback stock kept had no
/// setter outside the file and is not carried.
/// </summary>
public sealed class ViewEffects
{
    /// <summary>MAX_SHAKES: so bad entity logic on the server cannot consume the client.</summary>
    public const int MaxShakes = 32;

    /// <summary>SCREENFADE_FRACBITS.</summary>
    public const int FadeFracBits = 9;

    private sealed class ScreenShake
    {
        public float EndTime, Duration, Amplitude, Frequency, NextShake, Angle;
        public Vector Offset;
        public ShakeCommand Command;
    }

    private sealed class ScreenFade
    {
        public float Speed, End, Reset;
        public byte R, G, B, Alpha;
        public FadeFlags Flags;
    }

    private readonly List<ScreenShake> _shakes = [];
    private readonly List<ScreenFade> _fades = [];
    private Vector _appliedOffset;
    private float _appliedAngle;
    private readonly int[] _fadeRgba = new int[4];
    private bool _modulate;

    /// <summary>Shakes and fades alive.</summary>
    public int ShakeCount => _shakes.Count;

    public int FadeCount => _fades.Count;

    /// <summary>The last CalcShake's offset and roll (what ApplyShake adds).</summary>
    public Vector AppliedOffset => _appliedOffset;

    public float AppliedAngle => _appliedAngle;

    /// <summary>LevelInit: every shake and fade goes.</summary>
    public void LevelInit()
    {
        ClearAllShakes();
        ClearAllFades();
    }

    /// <summary>shake_stop's body.</summary>
    public void ClearAllShakes() => _shakes.Clear();

    public void ClearAllFades() => _fades.Clear();

    /// <summary>ClearPermanentFades: the stay-out fades go.</summary>
    public void ClearPermanentFades()
    {
        for (int i = _fades.Count - 1; i >= 0; i--)
        {
            if ((_fades[i].Flags & FadeFlags.StayOut) != 0)
                _fades.RemoveAt(i);
        }
    }

    // FindLongestShake: the first of the longest, as stock's strict compare keeps.
    private ScreenShake? Longest()
    {
        ScreenShake? longest = null;
        foreach (ScreenShake s in _shakes)
        {
            if (longest == null || s.Duration > longest.Duration)
                longest = s;
        }
        return longest;
    }

    /// <summary>The Shake message (and env_shake): start, stop, or retune the longest shake.</summary>
    public void Shake(ShakeCommand command, float amplitude, float frequency, float duration, float curTime)
    {
        if ((command == ShakeCommand.Start || command == ShakeCommand.StartRumbleOnly) && _shakes.Count < MaxShakes)
        {
            _shakes.Add(new ScreenShake
            {
                Amplitude = amplitude, Frequency = frequency, Duration = duration, NextShake = 0,
                EndTime = curTime + duration, Command = command,
            });
        }
        else if (command == ShakeCommand.Stop)
        {
            ClearAllShakes();
        }
        else if (command == ShakeCommand.Amplitude)
        {
            if (Longest() is { } s)
                s.Amplitude = amplitude;
        }
        else if (command == ShakeCommand.Frequency)
        {
            if (Longest() is { } s)
                s.Frequency = frequency;
        }
    }

    /// <summary>
    /// CalcShake: each live shake's settling sine, summed, the shakes past their
    /// end retired, their amplitudes decayed. <paramref name="random"/> is the
    /// engine's stream (a shake redraws its extents at its frequency).
    /// <paramref name="show"/> gets shake_show's lines (index, text).
    /// </summary>
    public ShakeFrame CalcShake(float curTime, float frameTime, IUniformRandom random, Action<int, string>? show = null)
    {
        _appliedOffset = new Vector(0, 0, 0);
        _appliedAngle = 0;
        float rumbleAngle = 0;

        for (int n = _shakes.Count - 1; n >= 0; n--)
        {
            ScreenShake s = _shakes[n];
            if (s.EndTime == 0)
                continue;

            if (curTime > s.EndTime || s.Duration <= 0 || s.Amplitude <= 0 || s.Frequency <= 0)
            {
                // FastRemove: the last element takes this slot.
                _shakes[n] = _shakes[^1];
                _shakes.RemoveAt(_shakes.Count - 1);
                continue;
            }

            show?.Invoke(n + 2, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:00}: dur({1,8:F2}) amp({2,8:F2}) freq({3,8:F2})", n + 1, s.Duration, s.Amplitude, s.Frequency));

            if (curTime > s.NextShake)
            {
                s.NextShake = curTime + (1.0f / s.Frequency);
                float x = random.RandomFloat(-s.Amplitude, s.Amplitude);
                float y = random.RandomFloat(-s.Amplitude, s.Amplitude);
                float z = random.RandomFloat(-s.Amplitude, s.Amplitude);
                s.Offset = new Vector(x, y, z);
                s.Angle = random.RandomFloat((float)(-s.Amplitude * 0.25), (float)(s.Amplitude * 0.25));
            }

            float fraction = (s.EndTime - curTime) / s.Duration;
            float freq = fraction != 0 ? s.Frequency / fraction : 0;
            fraction *= fraction;
            float angle = curTime * freq;
            if (angle > 1e8)
                angle = 1e8f;
            fraction = fraction * MathF.Sin(angle);

            if (s.Command != ShakeCommand.StartNoRumble)
                rumbleAngle += s.Angle * fraction;

            if (s.Command != ShakeCommand.StartRumbleOnly)
            {
                _appliedOffset = new Vector(_appliedOffset.X + s.Offset.X * fraction, _appliedOffset.Y + s.Offset.Y * fraction,
                                            _appliedOffset.Z + s.Offset.Z * fraction);
                _appliedAngle += s.Angle * fraction;
            }

            s.Amplitude -= s.Amplitude * (frameTime / (s.Duration * s.Frequency));
        }
        return new ShakeFrame(_appliedOffset, _appliedAngle, rumbleAngle);
    }

    /// <summary>ApplyShake: <paramref name="factor"/> of the shake onto a view.</summary>
    public void ApplyShake(ref Vector origin, ref Vector angles, float factor)
    {
        origin = new Vector(origin.X + factor * _appliedOffset.X, origin.Y + factor * _appliedOffset.Y, origin.Z + factor * _appliedOffset.Z);
        angles = new Vector(angles.X, angles.Y, angles.Z + _appliedAngle * factor);
    }

    /// <summary>The Fade message (and env_fade, the level's start-dark fade, the engine's View_Fade).</summary>
    public void Fade(int duration, int holdTime, FadeFlags flags, byte r, byte g, byte b, byte a, float curTime)
    {
        var fade = new ScreenFade
        {
            End = duration * (1.0f / (float)(1 << FadeFracBits)),
            Reset = holdTime * (1.0f / (float)(1 << FadeFracBits)),
            R = r, G = g, B = b, Alpha = a, Flags = flags, Speed = 0,
        };

        if (duration > 0)
        {
            if ((flags & FadeFlags.Out) != 0)
            {
                if (fade.End != 0)
                    fade.Speed = -(float)fade.Alpha / fade.End;
                fade.End += curTime;
                fade.Reset += fade.End;
            }
            else
            {
                if (fade.End != 0)
                    fade.Speed = (float)fade.Alpha / fade.End;
                fade.Reset += curTime;
                fade.End += fade.Reset;
            }
        }

        if ((flags & FadeFlags.Purge) != 0)
            ClearAllFades();

        _fades.Add(fade);
    }

    /// <summary>FadeCalculate: finished fades retired, the rest summed into one colour and the highest alpha.</summary>
    public void FadeCalculate(float curTime)
    {
        for (int i = _fades.Count - 1; i >= 0; i--)
        {
            ScreenFade f = _fades[i];
            if ((f.Flags & FadeFlags.StayOut) != 0)
                f.Reset = (float)(curTime + 0.1);
            if (curTime > f.Reset && curTime > f.End)
                _fades.RemoveAt(i);
        }

        _modulate = false;
        _fadeRgba[0] = _fadeRgba[1] = _fadeRgba[2] = _fadeRgba[3] = 0;
        foreach (ScreenFade f in _fades)
        {
            _fadeRgba[0] += f.R;
            _fadeRgba[1] += f.G;
            _fadeRgba[2] += f.B;

            int alpha;
            if ((f.Flags & (FadeFlags.Out | FadeFlags.In)) != 0)
            {
                alpha = (int)(f.Speed * (f.End - curTime));
                if ((f.Flags & FadeFlags.Out) != 0)
                    alpha += f.Alpha;
                alpha = Math.Min(alpha, f.Alpha);
                alpha = Math.Max(0, alpha);
            }
            else
            {
                alpha = f.Alpha;
            }

            if (alpha > _fadeRgba[3])
                _fadeRgba[3] = alpha;
            if ((f.Flags & FadeFlags.Modulate) != 0)
                _modulate = true;
        }

        if (_fades.Count > 0)
        {
            _fadeRgba[0] /= _fades.Count;
            _fadeRgba[1] /= _fades.Count;
            _fadeRgba[2] /= _fades.Count;
        }
    }

    /// <summary>
    /// GetFadeParams: the intro's fade when one overrides (its alpha non-zero),
    /// else this frame's FadeCalculate.
    /// </summary>
    public FadeParams GetFadeParams(float curTime, (float R, float G, float B, float A)? intro = null)
    {
        if (intro is { } i && i.A != 0)
            return new FadeParams((byte)i.R, (byte)i.G, (byte)i.B, (byte)i.A, false);

        FadeCalculate(curTime);
        return new FadeParams((byte)_fadeRgba[0], (byte)_fadeRgba[1], (byte)_fadeRgba[2], (byte)_fadeRgba[3], _modulate);
    }
}
