//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Ropes;

namespace SourceSharp.Client.Effects;

/// <summary>What an env_spritetrail's ribbon is drawn with: DT_SpriteTrail's numbers.</summary>
public struct SpriteTrailParams
{
    public int ModelIndex;
    public float LifeTime, StartWidth, EndWidth, StartWidthVariance, TextureRes, MinFadeLength;
    public int RenderMode, R, G, B, Brightness;
    public bool Visible;
}

/// <summary>
/// One env_spritetrail's ribbon (SpriteTrail.cpp's <c>CSpriteTrail</c> client
/// half, as our shell_sprite_render.cpp carried it): a ring of up to 256
/// points laid behind the moving entity, each with a death time, a texture
/// coordinate and a width jitter. <see cref="Tick"/> is what was ClientThink
/// (lay a point, re-measure the box); <see cref="Draw"/> is DrawModel's
/// segments, and retires the points that have died, as stock's draw did.
/// Replayed against the recorded ribbon (ClientSpriteTrailTests).
/// </summary>
public sealed class SpriteTrail
{
    // NOTE: # of points max must be a power of two!
    public const int MaxPoints = 256;
    private const int Mask = MaxPoints - 1;

    private struct TrailPoint
    {
        public Vector Pos;
        public float DieTime;
        public float TexCoord;
        public float WidthVariance;
    }

    private readonly TrailPoint[] _steps = new TrailPoint[MaxPoints];
    private int _firstStep;
    private int _stepCount;
    private float _updateTime;

    /// <summary>The ribbon's numbers; the end width starts at -1, "the start width all the way".</summary>
    public SpriteTrailParams Params = new() { EndWidth = -1.0f, Visible = true };

    /// <summary>The box about the render origin (GetRenderBounds), after the last tick.</summary>
    public Vector RenderMins, RenderMaxs;

    public int StepCount => _stepCount;

    /// <summary>DrawModel's two gates before it binds anything: a point to draw from, and switched on.</summary>
    public bool WouldDraw => _stepCount >= 1 && Params.Visible;

    /// <summary>The ring's head and the next time a point may be laid (for the replay).</summary>
    internal int FirstStep => _firstStep;

    internal float UpdateTime => _updateTime;

    private ref TrailPoint Point(int n) => ref _steps[(n + _firstStep) & Mask];

    /// <summary>UpdateTrail then UpdateBoundingBox, at the followed origin.</summary>
    public void Tick(float curTime, in Vector origin, IEngineHost random)
    {
        UpdateTrail(curTime, origin, random);
        UpdateBoundingBox(origin);
    }

    private void UpdateTrail(float curTime, in Vector screenPos, IEngineHost random)
    {
        // Can't update too quickly
        if (_updateTime > curTime)
            return;

        int last = _stepCount != 0 ? (_stepCount - 1 + _firstStep) & Mask : -1;

        if (last < 0 || DistToSqr(_steps[last].Pos, screenPos) > 4.0f)
        {
            // If we're over our limit, steal the last point and put it up front
            if (_stepCount >= MaxPoints)
            {
                --_stepCount;
                ++_firstStep;
            }

            ref TrailPoint newPoint = ref Point(_stepCount);
            newPoint.Pos = screenPos;
            newPoint.DieTime = curTime + Params.LifeTime;
            newPoint.WidthVariance = random.RandomFloat(-Params.StartWidthVariance, Params.StartWidthVariance);

            if (last >= 0)
                newPoint.TexCoord = _steps[last].TexCoord + DistTo(_steps[last].Pos, screenPos) * Params.TextureRes;
            else
                newPoint.TexCoord = 0.0f;

            ++_stepCount;
        }

        // Don't update again for a bit
        _updateTime = curTime + (Params.LifeTime / (float)MaxPoints);
    }

    private void UpdateBoundingBox(in Vector renderOrigin)
    {
        RenderMins = renderOrigin;
        RenderMaxs = renderOrigin;

        float maxWidth = Params.StartWidth;

        if ((Params.EndWidth >= 0.0f) && (Params.EndWidth > Params.StartWidth))
            maxWidth = Params.EndWidth;

        for (int i = 0; i < _stepCount; ++i)
        {
            ref TrailPoint point = ref Point(i);

            float actualWidth = (maxWidth + point.WidthVariance) * 0.5f;
            var size = new Vector(actualWidth, actualWidth, actualWidth);
            Vector mins = FxMath.Sub(point.Pos, size);
            Vector maxs = FxMath.Add(point.Pos, size);

            RenderMins = new Vector(FxMath.Min(RenderMins.X, mins.X), FxMath.Min(RenderMins.Y, mins.Y), FxMath.Min(RenderMins.Z, mins.Z));
            RenderMaxs = new Vector(FxMath.Max(RenderMaxs.X, maxs.X), FxMath.Max(RenderMaxs.Y, maxs.Y), FxMath.Max(RenderMaxs.Z, maxs.Z));
        }

        RenderMins = FxMath.Sub(RenderMins, renderOrigin);
        RenderMaxs = FxMath.Sub(RenderMaxs, renderOrigin);
    }

    /// <summary>
    /// DrawModel's segments into <paramref name="segs"/> (at least
    /// <see cref="StepCount"/> + 1 long): how many were built, 0 when the
    /// ribbon draws nothing. Points that have died are retired as they are
    /// passed, so a second draw in the same frame sees fewer.
    /// </summary>
    public int Draw(float curTime, in Vector origin, Span<BeamSeg> segs)
    {
        // Must have at least one point
        if (_stepCount < 1 || !Params.Visible)
            return 0;

        // Setup the first point, always emanating from the attachment point
        TrailPoint lastPoint = Point(_stepCount - 1);
        var currentPoint = new TrailPoint
        {
            DieTime = curTime + Params.LifeTime,
            Pos = origin,
        };
        currentPoint.TexCoord = lastPoint.TexCoord + DistTo(currentPoint.Pos, lastPoint.Pos) * Params.TextureRes;
        currentPoint.WidthVariance = 0.0f;

        bool hasPrev = false;
        Vector prevPos = default;
        float tailAlphaDist = Params.MinFadeLength;
        int n = 0;

        var color = new Vector((float)Params.R / 255.0f, (float)Params.G / 255.0f, (float)Params.B / 255.0f);

        for (int i = 0; i <= _stepCount; ++i)
        {
            // This makes it so that we're always drawing to the current location
            TrailPoint point = (i != _stepCount) ? Point(i) : currentPoint;

            float lifePerc = LifeFraction(point.DieTime - curTime, Params.LifeTime);

            float alphaFade = lifePerc;

            if (tailAlphaDist > 0.0f)
            {
                if (hasPrev)
                {
                    float dist = DistTo(point.Pos, prevPos);
                    tailAlphaDist -= dist;
                }

                if (tailAlphaDist > 0.0f)
                {
                    float tailFade = Lerp((Params.MinFadeLength - tailAlphaDist) / Params.MinFadeLength, 0.0f, 1.0f);

                    if (tailFade < alphaFade)
                        alphaFade = tailFade;
                }
            }

            var seg = new BeamSeg
            {
                Color = color,
                Alpha = ((float)Params.Brightness / 255.0f) * alphaFade,
                Pos = point.Pos,
            };

            if (Params.EndWidth >= 0.0f)
                seg.Width = Lerp(lifePerc, Params.EndWidth, Params.StartWidth);
            else
                seg.Width = Params.StartWidth;

            seg.Width += point.WidthVariance;

            if (seg.Width < 0.0f)
                seg.Width = 0.0f;

            seg.TexCoord = point.TexCoord;

            segs[n++] = seg;

            // See if we're done with this bad boy
            if (point.DieTime <= curTime)
            {
                // Push this back onto the top for use
                ++_firstStep;
                --i;
                --_stepCount;
            }

            hasPrev = true;
            prevPos = point.Pos;
        }

        return n;
    }

    /// <summary>The ring forgets everything (a new level, a new entity in the slot).</summary>
    public void Clear()
    {
        _firstStep = 0;
        _stepCount = 0;
        _updateTime = 0;
        RenderMins = RenderMaxs = default;
    }

    // Vector::DistToSqr / DistTo: the difference's squared length, and sqrtf of it.
    private static float DistToSqr(in Vector a, in Vector b)
    {
        Vector d = FxMath.Sub(a, b);
        return d.X * d.X + d.Y * d.Y + d.Z * d.Z;
    }

    private static float DistTo(in Vector a, in Vector b) => MathF.Sqrt(DistToSqr(a, b));

    // Lerp( t, A, B ) = A + (B - A) * t.
    private static float Lerp(float t, float a, float b) => a + (b - a) * t;

    // SourceSharp_LifeFraction (scripted_var_defaults.h): a lifetime that is not
    // a usable positive reads as finished (1), never as 0/0.
    private static float LifeFraction(float remaining, float lifeTime)
    {
        if (!(float.IsFinite(lifeTime) && lifeTime > 0.0f))
            return 1.0f;
        if (!float.IsFinite(remaining) || remaining <= 0.0f)
            return 0.0f;
        float fraction = remaining / lifeTime;
        if (!float.IsFinite(fraction))
            return 1.0f;
        if (fraction < 0.0f)
            return 0.0f;
        if (fraction > 1.0f)
            return 1.0f;
        return fraction;
    }
}
