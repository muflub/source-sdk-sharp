//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Ropes;

namespace SourceSharp.Client.Beams;

/// <summary>
/// beamdraw.cpp's builders: a beam's shape, noise, fades and texture scroll
/// computed into strips and meshes for <see cref="IBeamDrawSink"/>. Every
/// expression keeps stock's float/double types; <c>BeamStockReplayTests</c>
/// holds the segments and vertices to the recorded ones bit for bit.
/// </summary>
public sealed class BeamDraw
{
    private const int RenderGlow = 3;   // kRenderGlow

    private readonly IBeamDrawSink _sink;
    private readonly IBeamDrawWorld _world;
    private BeamView _view;
    private readonly BeamSeg[] _segs = new BeamSeg[Beam.NoiseDivisions * 8 + 2];
    private readonly BeamVertex[] _verts = new BeamVertex[(Beam.NoiseDivisions * 8 + 2) * 2];

    public BeamDraw(IBeamDrawSink sink, IBeamDrawWorld world, in BeamView view)
    {
        _sink = sink;
        _world = world;
        _view = view;
    }

    /// <summary>The view this draw builds for; the beam system re-points one builder per draw.</summary>
    public BeamView View
    {
        get => _view;
        set => _view = value;
    }

    //-------------------------------------------------------------------------
    // Shared helpers.
    //-------------------------------------------------------------------------

    private static Vector Ma(in Vector start, float scale, in Vector dir)
        => new(start.X + dir.X * scale, start.Y + dir.Y * scale, start.Z + dir.Z * scale);

    private static Vector Cross(in Vector a, in Vector b)
        => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    // fmod( x, 1 ): C#'s double remainder is IEEE fmod, exact; rounded to float once.
    private static float Fmod1(double x) => (float)(x % 1.0);

    // ComputeBeamPerpendicular: the beam's direction crossed into the view's.
    private Vector BeamPerpendicular(in Vector delta)
    {
        Vector center = delta;
        RopeMath.Normalize(ref center);
        Vector perp = Cross(_view.Forward, center);
        RopeMath.Normalize(ref perp);
        return perp;
    }

    /// <summary>Draw_SetSpriteTexture, then (when it bound) the HDR scale on its material.</summary>
    private bool Bind(int model, int frame, int renderMode, float hdrColorScale, bool setHdr)
    {
        if (!_sink.BindSprite(model, frame, renderMode))
            return false;
        if (setHdr)
            _sink.SetHdrColorScale(model, renderMode, hdrColorScale);
        return true;
    }

    // The brightness a shaded segment carries, before the 0..1 clamp.
    private static float Shade(BeamFlags flags, float fraction, float fadeFraction, float brightness)
    {
        if ((flags & BeamFlags.ShadeIn) != 0 && (flags & BeamFlags.ShadeOut) != 0)
        {
            brightness = fraction < 0.5
                ? 2 * (fraction / fadeFraction)
                : (float)(2 * (1.0 - (fraction / fadeFraction)));
        }
        else if ((flags & BeamFlags.ShadeIn) != 0)
        {
            brightness = fraction / fadeFraction;
        }
        else if ((flags & BeamFlags.ShadeOut) != 0)
        {
            brightness = (float)(1.0 - (fraction / fadeFraction));
        }

        if (brightness < 0)
            brightness = 0;
        else if (brightness > 1)
            brightness = 1;

        return brightness;
    }

    //-------------------------------------------------------------------------
    // DrawSegs: a straight beam with noise, fades and a scrolling texture.
    //-------------------------------------------------------------------------

    public void DrawSegs(float[] noise, int model, float frame, int renderMode, in Vector source, in Vector delta,
                         float startWidth, float endWidth, float scale, float freq, float speed, int segments,
                         BeamFlags flags, in Vector color, float fadeLength, float hdrColorScale = 1.0f)
    {
        if (!_sink.BindSprite(model, (int)frame, renderMode))
            return;
        if (segments < 2)
            return;
        _sink.SetHdrColorScale(model, renderMode, hdrColorScale);

        float length = delta.Length;
        float maxWidth = (startWidth > endWidth ? startWidth : endWidth) * 0.5f;
        float div = (float)(1.0 / (segments - 1));

        if (length * div < maxWidth * 1.414)
        {
            segments = (int)(length / (maxWidth * 1.414)) + 1;
            if (segments < 2)
                segments = 2;
        }

        if (segments > Beam.NoiseDivisions)
            segments = Beam.NoiseDivisions;

        div = (float)(1.0 / (segments - 1));
        length = (float)(length * 0.01);

        float vStep = (flags & BeamFlags.NoTile) != 0 ? div : length * div;
        float vLast = Fmod1(freq * speed);

        if ((flags & BeamFlags.SineNoise) != 0)
        {
            if (segments < 16)
            {
                segments = 16;
                div = (float)(1.0 / (segments - 1));
            }
            scale *= 100;
            length = (float)(segments * (1.0 / 10));
        }
        else
        {
            scale *= length;
        }

        int noiseStep = (int)((float)(Beam.NoiseDivisions - 1) * div * 65536.0f);
        int noiseIndex = 0;

        float brightness = 1.0f;
        if ((flags & BeamFlags.ShadeIn) != 0)
            brightness = 0;

        float fadeFraction = fadeLength / delta.Length;
        fadeFraction = Math.Clamp(fadeFraction, 1.0e-6f, 1.0f);

        Vector perp1 = BeamPerpendicular(delta);

        for (int i = 0; i < segments; i++)
        {
            ref BeamSeg seg = ref _segs[i];
            seg.Alpha = 1;
            float fraction = i * div;

            brightness = Shade(flags, fraction, fadeFraction, brightness);
            seg.Color = color * brightness;
            seg.Pos = Ma(source, fraction, delta);

            if (scale != 0)
            {
                float factor = noise[noiseIndex >> 16] * scale;
                if ((flags & BeamFlags.SineNoise) != 0)
                {
                    float r = (float)(fraction * Math.PI * length + freq);
                    float s = MathF.Sin(r), c = MathF.Cos(r);
                    seg.Pos = Ma(seg.Pos, factor * s, _view.Up);
                    seg.Pos = Ma(seg.Pos, factor * c, _view.Right);
                }
                else
                {
                    seg.Pos = Ma(seg.Pos, factor, perp1);
                }
            }

            seg.Width = endWidth == startWidth
                ? startWidth * 2
                : ((fraction * (endWidth - startWidth)) + startWidth) * 2;

            seg.TexCoord = vLast;
            vLast += vStep;
            noiseIndex += noiseStep;
        }

        _sink.Strip(_segs.AsSpan(0, segments));
    }

    //-------------------------------------------------------------------------
    // DrawTeslaSegs and CalcSegOrigin: a branching arc.
    //-------------------------------------------------------------------------

    private Vector SegOrigin(int point, float[] noise, in Vector source, in Vector delta, in Vector perp,
                             int segments, float freq, float scale, float fraction, BeamFlags flags)
    {
        float length = (float)(delta.Length * 0.01);
        float div = (float)(1.0 / (segments - 1));
        int noiseStep = (int)((float)(Beam.NoiseDivisions - 1) * div * 65536.0f);
        int noiseIndex = point * noiseStep;

        if ((flags & BeamFlags.SineNoise) != 0)
        {
            length = (float)(segments * (1.0 / 10));
            noiseIndex = 0;
        }

        Vector o = Ma(source, fraction, delta);

        if (scale != 0)
        {
            float factor = noise[noiseIndex >> 16] * scale;
            if ((flags & BeamFlags.SineNoise) != 0)
            {
                float r = (float)(fraction * Math.PI * length + freq);
                float s = MathF.Sin(r), c = MathF.Cos(r);
                o = Ma(o, factor * s, _view.MainUp);
                o = Ma(o, factor * c, _view.MainRight);
            }
            else
            {
                o = Ma(o, factor, perp);
            }
        }

        return o;
    }

    public void DrawTeslaSegs(float[] noise, int model, float frame, int renderMode, in Vector source, in Vector delta,
                              float startWidth, float endWidth, float scale, float freq, float speed, int segments,
                              BeamFlags flags, in Vector color, float fadeLength, float hdrColorScale = 1.0f)
    {
        if (!_sink.BindSprite(model, (int)frame, renderMode))
            return;
        if (segments < 2)
            return;
        _sink.SetHdrColorScale(model, renderMode, hdrColorScale);

        if (segments > Beam.NoiseDivisions)
            segments = Beam.NoiseDivisions;

        float length = (float)(delta.Length * 0.01);
        float div = (float)(1.0 / (segments - 1));
        float vStep = length * div;
        float vLast = Fmod1(freq * speed);

        float brightness = 1.0f;
        if ((flags & BeamFlags.ShadeIn) != 0)
            brightness = 0;

        float fadeFraction = fadeLength / delta.Length;
        fadeFraction = Math.Clamp(fadeFraction, 1.0e-6f, 1.0f);

        Vector perp = BeamPerpendicular(delta);

        int branches = 0;
        Vector vecStart = default, vecEnd = default;
        float flWidth = 0;
        float flEndWidth = 0;

        for (int i = 0; i < segments; i++)
        {
            ref BeamSeg seg = ref _segs[i];
            seg.Alpha = 1;
            float fraction = i * div;

            brightness = Shade(flags, fraction, fadeFraction, brightness);
            seg.Color = color * brightness;
            seg.Pos = SegOrigin(i, noise, source, delta, perp, segments, freq, scale, fraction, flags);

            seg.Width = endWidth == startWidth
                ? startWidth * 2
                : ((fraction * (endWidth - startWidth)) + startWidth) * 2;

            for (int j = 0; j < branches; j++)
                seg.Width = (float)(seg.Width * 0.5);

            seg.TexCoord = vLast;
            vLast += vStep;

            if (i == (segments * 0.5))
            {
                flWidth = (float)(seg.Width * 0.25);
                if (flWidth > 1)
                {
                    branches++;
                    vecStart = seg.Pos;
                    vecEnd = source + delta + (_view.MainUp * 32) + (_view.MainRight * 32);
                    vecEnd -= vecStart;
                    flEndWidth = endWidth;
                    for (int j = 0; j < branches; j++)
                        flEndWidth = (float)(flEndWidth * 0.5);
                }
            }
        }

        _sink.Strip(_segs.AsSpan(0, segments));

        if (branches != 0)
        {
            DrawTeslaSegs(noise, model, frame, renderMode, vecStart, vecEnd, flWidth, flEndWidth, scale, freq,
                          speed, segments, flags, color, fadeLength, hdrColorScale);
        }
    }

    //-------------------------------------------------------------------------
    // DrawSplineSegs: a Catmull-Rom curve through the attachments.
    //-------------------------------------------------------------------------

    /// <summary><c>Catmull_Rom_Spline</c> (mathlib_base.cpp): the matrix rows accumulated as stock does.</summary>
    public static Vector CatmullRomSpline(in Vector p1, in Vector p2, in Vector p3, in Vector p4, float t)
    {
        float tSqr = t * t * 0.5f;
        float tSqrSqr = t * tSqr;
        t *= 0.5f;

        Vector output = default;

        output = (p1 * -tSqrSqr) + output;
        output = (p2 * (tSqrSqr * 3)) + output;
        output = (p3 * (tSqrSqr * -3)) + output;
        output = (p4 * tSqrSqr) + output;

        output = (p1 * (tSqr * 2)) + output;
        output = (p2 * (tSqr * -5)) + output;
        output = (p3 * (tSqr * 4)) + output;
        output = (p4 * -tSqr) + output;

        output = (p1 * -t) + output;
        output = (p3 * t) + output;

        output = p2 + output;
        return output;
    }

    public void DrawSplineSegs(float[] noise, int beamModel, int haloModel, float haloScale, float frame, int renderMode,
                               int numAttachments, Vector[] attachment, float startWidth, float endWidth, float scale,
                               float freq, float speed, int segments, BeamFlags flags, in Vector color,
                               float fadeLength, float hdrColorScale = 1.0f)
    {
        if (!_sink.BindSprite(beamModel, (int)frame, renderMode))
            return;
        if (segments < 2)
            return;
        _sink.SetHdrColorScale(beamModel, renderMode, hdrColorScale);

        if (segments > Beam.NoiseDivisions)
            segments = Beam.NoiseDivisions;

        if ((flags & BeamFlags.SineNoise) != 0 && segments < 16)
            segments = 16;

        // The halo's glow material is only looked up here; it is bound after the strip.
        bool halo = _sink.HasGlow(haloModel);
        int strip = 0;

        float widthStep = startWidth != endWidth ? (endWidth - startWidth) / numAttachments : 0;

        float beamLength = (attachment[0] - attachment[numAttachments - 1]).Length;
        float fadeFraction = fadeLength / beamLength;
        if (fadeFraction > 1)
            fadeFraction = 1;

        Vector pPre, pStart, pEnd = default, pNext;
        Vector scaledColor = default;

        for (int j = 0; j < numAttachments - 1; j++)
        {
            if (j == 0)
            {
                pPre = attachment[0];
            }
            else
            {
                pPre = attachment[j - 1];
            }

            pStart = attachment[j];
            pEnd = attachment[j + 1];
            pNext = j + 2 >= numAttachments - 1 ? attachment[j + 1] : attachment[j + 2];

            Vector vDelta = pEnd - pStart;
            float length = (float)(vDelta.Length * 0.01);
            if (length < 0.5)
                length = 0.5f;

            float div = (float)(1.0 / (segments - 1));
            float vStep = length * div;
            float vLast = Fmod1(freq * speed);

            if ((flags & BeamFlags.SineNoise) != 0)
            {
                scale = scale * 100;
                length = (float)(segments * (1.0 / 10));
            }
            else
            {
                scale = scale * length;
            }

            int noiseStep = (int)((float)(Beam.NoiseDivisions - 1) * div * 65536.0f);
            int noiseIndex = noiseStep;
            if ((flags & BeamFlags.SineNoise) != 0)
                noiseIndex = 0;

            float brightness = 1.0f;
            if ((flags & BeamFlags.ShadeIn) != 0)
                brightness = 0;

            BeamSeg seg = default;
            seg.Alpha = 1;
            scaledColor = color * brightness;
            seg.Color = scaledColor;

            float startSegWidth = startWidth + (widthStep * j);
            float endSegWidth = startWidth + (widthStep * (j + 1));

            for (int i = 1; i < segments; i++)
            {
                float fraction = i * div;
                if ((flags & BeamFlags.ShadeIn) != 0)
                {
                    brightness = fraction / fadeFraction;
                    if (brightness > 1)
                        brightness = 1;
                }
                else if ((flags & BeamFlags.ShadeOut) != 0)
                {
                    float fadeFractionOut = fadeLength / length;
                    brightness = (float)(1.0 - (fraction / fadeFractionOut));
                    if (brightness < 0)
                        brightness = 0;
                }

                // (Stock tracks the closest-to-the-eye point here for a halo that
                // an `if ( false && ... )` then never draws.)
                seg.Pos = CatmullRomSpline(pPre, pStart, pEnd, pNext, fraction);

                if (scale != 0)
                {
                    float factor = noise[noiseIndex >> 16] * scale;
                    if ((flags & BeamFlags.SineNoise) != 0)
                    {
                        float r = (float)(fraction * Math.PI * length + freq);
                        float s = MathF.Sin(r), c = MathF.Cos(r);
                        seg.Pos = Ma(seg.Pos, factor * s, _view.Up);
                        seg.Pos = Ma(seg.Pos, factor * c, _view.Right);
                    }
                    else
                    {
                        seg.Pos = Ma(seg.Pos, factor, _view.Up);
                        factor = (float)(noise[noiseIndex >> 16] * scale * Math.Cos(fraction * Math.PI * 3 + freq));
                        seg.Pos = Ma(seg.Pos, factor, _view.Right);
                    }
                }

                seg.Width = startWidth != endWidth
                    ? ((fraction * (endSegWidth - startSegWidth)) + startSegWidth) * 2
                    : startWidth * 2;

                seg.TexCoord = vLast;
                _segs[strip++] = seg;
                vLast += vStep;
                noiseIndex += noiseStep;
            }
        }

        _sink.Strip(_segs.AsSpan(0, strip));

        if (halo)
        {
            _sink.BindGlow(haloModel);
            DrawHaloMesh(haloModel, RenderGlow, pEnd, haloScale, scaledColor, hdrColorScale);
        }
    }

    //-------------------------------------------------------------------------
    // Halos and sprites: one camera-facing quad.
    //-------------------------------------------------------------------------

    /// <summary><c>DrawHalo</c> over whatever material is bound.</summary>
    private void DrawHaloMesh(int model, int renderMode, in Vector source, float scale, in Vector color, float hdrColorScale)
    {
        _sink.SetHdrColorScale(model, renderMode, hdrColorScale);

        Span<BeamVertex> v = _verts.AsSpan(0, 4);
        Vector up = _view.Up, right = _view.Right;

        v[0] = Vert(Ma(Ma(source, -scale, up), -scale, right), color, 0, 1);
        v[1] = Vert(Ma(Ma(source, scale, up), -scale, right), color, 0, 0);
        v[2] = Vert(Ma(Ma(source, scale, up), scale, right), color, 1, 0);
        v[3] = Vert(Ma(Ma(source, -scale, up), scale, right), color, 1, 1);

        _sink.Mesh(BeamPrimitive.Quads, 1, BeamColorKind.Float, v);
    }

    private static BeamVertex Vert(in Vector pos, in Vector color, float u, float v)
        => new() { Pos = pos, R = color.X, G = color.Y, B = color.Z, A = 1, U = u, V = v };

    /// <summary><c>BeamDrawHalo</c>: bind the halo sprite for the render mode, then its quad.</summary>
    public void BeamDrawHalo(int model, float frame, int renderMode, in Vector source, float scale, in Vector color,
                             float hdrColorScale = 1.0f)
    {
        if (!_sink.BindSprite(model, (int)frame, renderMode))
            return;
        DrawHaloMesh(model, renderMode, source, scale, color, hdrColorScale);
    }

    /// <summary>
    /// beamdraw.cpp's <c>DrawSprite</c>: a quad facing the view at a point, its
    /// axes rebuilt from the eye direction; over the bound material.
    /// </summary>
    public void DrawSprite(in Vector origin, float width, float height, byte r, byte g, byte b, byte a)
    {
        width *= 0.5f;
        height *= 0.5f;

        Vector fwd = _view.Origin - origin;
        Vector right = new(1, 0, 0), up = new(0, 1, 0);
        float dist = RopeMath.Normalize(ref fwd);

        if (dist >= 1e-3)
        {
            right = Cross(_view.Up, fwd);
            dist = RopeMath.Normalize(ref right);
            if (dist >= 1e-3)
            {
                up = Cross(fwd, right);
            }
            else
            {
                up = Cross(fwd, _view.Right);
                RopeMath.Normalize(ref up);
                right = Cross(up, fwd);
            }
        }

        Span<BeamVertex> v = _verts.AsSpan(0, 4);
        var c = new Vector(r, g, b);

        v[0] = ByteVert(Ma(Ma(origin, -height, up), -width, right), c, a, 0, 1);
        v[1] = ByteVert(Ma(Ma(origin, height, up), -width, right), c, a, 0, 0);
        v[2] = ByteVert(Ma(Ma(origin, height, up), width, right), c, a, 1, 0);
        v[3] = ByteVert(Ma(Ma(origin, -height, up), width, right), c, a, 1, 1);

        _sink.Mesh(BeamPrimitive.Quads, 1, BeamColorKind.Rgba, v);
    }

    private static BeamVertex ByteVert(in Vector pos, in Vector color, float a, float u, float v)
        => new() { Pos = pos, R = color.X, G = color.Y, B = color.Z, A = a, U = u, V = v };

    //-------------------------------------------------------------------------
    // DrawDisk, DrawCylinder, DrawRing: meshes of their own.
    //-------------------------------------------------------------------------

    public void DrawDisk(int model, float frame, int renderMode, in Vector source, in Vector delta, float width,
                         float scale, float freq, float speed, int segments, in Vector color, float hdrColorScale = 1.0f)
    {
        if (!_sink.BindSprite(model, (int)frame, renderMode))
            return;
        if (segments < 2)
            return;
        _sink.SetHdrColorScale(model, renderMode, hdrColorScale);

        if (segments > Beam.NoiseDivisions)
            segments = Beam.NoiseDivisions;

        float length = (float)(delta.Length * 0.01);
        if (length < 0.5)
            length = 0.5f;

        float div = (float)(1.0 / (segments - 1));
        float vStep = length * div;
        float vLast = Fmod1(freq * speed);
        float w = freq * delta.Z;

        int n = 0;
        for (int i = 0; i < segments; i++)
        {
            float fraction = i * div;

            _verts[n++] = Vert(source, color, 1.0f, vLast);

            float r = (float)(fraction * 2 * Math.PI);
            float s = MathF.Sin(r), c = MathF.Cos(r);
            _verts[n++] = Vert(new Vector(s * w + source.X, c * w + source.Y, source.Z), color, 0.0f, vLast);

            vLast += vStep;
        }

        _sink.Mesh(BeamPrimitive.TriangleStrip, (segments - 1) * 2, BeamColorKind.Float, _verts.AsSpan(0, n));
    }

    public void DrawCylinder(int model, float frame, int renderMode, in Vector source, in Vector delta, float width,
                             float scale, float freq, float speed, int segments, in Vector color,
                             float hdrColorScale = 1.0f)
    {
        if (!_sink.BindSprite(model, (int)frame, renderMode))
            return;
        if (segments < 2)
            return;
        _sink.SetHdrColorScale(model, renderMode, hdrColorScale);

        if (segments > Beam.NoiseDivisions)
            segments = Beam.NoiseDivisions;

        float length = (float)(delta.Length * 0.01);
        if (length < 0.5)
            length = 0.5f;

        float div = (float)(1.0 / (segments - 1));
        float vStep = length * div;
        float vLast = Fmod1(freq * speed);
        float radius = delta.Z;

        int n = 0;
        for (int i = 0; i < segments; i++)
        {
            float fraction = i * div;
            float r = (float)(fraction * 2 * Math.PI);
            float s = MathF.Sin(r), c = MathF.Cos(r);

            _verts[n++] = Vert(new Vector(s * freq * radius + source.X, c * freq * radius + source.Y, source.Z + width),
                               default, 1.0f, vLast);
            _verts[n++] = Vert(new Vector(s * freq * (radius + width) + source.X, c * freq * (radius + width) + source.Y,
                                          source.Z - width),
                               color, 0.0f, vLast);

            vLast += vStep;
        }

        _sink.Mesh(BeamPrimitive.TriangleStrip, (segments - 1) * 2, BeamColorKind.Float, _verts.AsSpan(0, n));
    }

    /// <summary>
    /// <c>DrawRing</c>. Re-noises the beam's noise through
    /// <paramref name="renoise"/> every eighth of the way round while it has an
    /// amplitude, which is why a ring's draw is not idempotent.
    /// </summary>
    public void DrawRing(float[] noise, Action<float[], int, float> renoise, int model, float frame, int renderMode,
                         in Vector source, in Vector delta, float width, float amplitude, float freq, float speed,
                         int segments, in Vector color, float hdrColorScale = 1.0f)
    {
        if (!_sink.BindSprite(model, (int)frame, renderMode))
            return;
        _sink.SetHdrColorScale(model, renderMode, hdrColorScale);

        Vector d = delta;
        if (segments < 2)
            return;

        segments = (int)(segments * Math.PI);
        if (segments > Beam.NoiseDivisions * 8)
            segments = Beam.NoiseDivisions * 8;

        float length = (float)(d.Length * 0.01 * Math.PI);
        if (length < 0.5)
            length = 0.5f;

        float div = (float)(1.0 / (segments - 1));
        float vStep = (float)(length * div / 8.0);
        float vLast = Fmod1(freq * speed);
        float scale = (float)(amplitude * length / 8.0);

        int noiseStep = (int)((Beam.NoiseDivisions - 1) * div * 65536.0) * 8;
        int noiseIndex = 0;

        d = d * (float)0.5;
        Vector center = source + d;
        Vector xaxis = d;
        float radius = xaxis.Length;

        Vector last1 = new(radius, radius, scale);
        Vector tmp = center + last1;
        Vector screen = center - last1;

        if (!_world.IsBoxVisible(screen, tmp) || _world.CullBox(screen, tmp))
            return;

        Vector yaxis = new(xaxis.Y, -xaxis.X, 0);
        RopeMath.Normalize(ref yaxis);
        yaxis = yaxis * radius;

        int j = segments / 8;
        Vector screenLast = default;
        int n = 0;

        for (int i = 0; i < segments + 1; i++)
        {
            float fraction = i * div;
            float rad = (float)(fraction * 2 * Math.PI);
            float x = MathF.Sin(rad), y = MathF.Cos(rad);

            Vector point = new(xaxis.X * x + yaxis.X * y + center.X,
                               xaxis.Y * x + yaxis.Y * y + center.Y,
                               xaxis.Z * x + yaxis.Z * y + center.Z);

            if (scale != 0.0f)
            {
                float factor = noise[(noiseIndex >> 16) & 0x7F] * scale;
                point = Ma(point, factor, _view.Up);
                factor = (float)(noise[(noiseIndex >> 16) & 0x7F] * scale * Math.Cos(fraction * Math.PI * 3 * 8 + freq));
                point = Ma(point, factor, _view.Right);
            }

            _view.ScreenTransform(point, out screen);

            if (i != 0)
            {
                tmp = screen - screenLast;
                tmp.Z = 0;
                RopeMath.Normalize(ref tmp);

                Vector normal = _view.Up * tmp.X;
                normal = Ma(normal, -tmp.Y, _view.Right);
                last1 = Ma(point, width, normal);
                Vector last2 = Ma(point, -width, normal);

                vLast += vStep;
                _verts[n++] = Vert(last2, color, 1.0f, vLast);
                _verts[n++] = Vert(last1, color, 0.0f, vLast);
            }

            screenLast = screen;
            noiseIndex += noiseStep;

            j--;
            if (j == 0 && amplitude != 0)
            {
                j = segments / 8;
                renoise(noise, Beam.NoiseDivisions, 1.0f);
            }
        }

        _sink.Mesh(BeamPrimitive.TriangleStrip, segments * 2, BeamColorKind.Float, _verts.AsSpan(0, n));
    }

    //-------------------------------------------------------------------------
    // DrawBeamFollow: the trail of a moving entity, in quads.
    //-------------------------------------------------------------------------

    public void DrawBeamFollow(int model, BeamTrail head, int frame, int renderMode, in Vector delta, Vector screen,
                               Vector screenLast, float die, BeamFlags flags, float width, float amplitude,
                               float freq, in Vector color, float hdrColorScale = 1.0f)
    {
        if (!_sink.BindSprite(model, frame, renderMode))
            return;
        _sink.SetHdrColorScale(model, renderMode, hdrColorScale);

        Vector tmp = screen - screenLast;
        tmp.Z = 0;
        RopeMath.Normalize(ref tmp);
        Vector normal = _view.Up * tmp.X;
        normal = Ma(normal, -tmp.Y, _view.Right);

        Vector last1 = Ma(delta, width, normal);
        Vector last2 = Ma(delta, -width, normal);

        float div = (float)(1.0 / amplitude);
        float fraction = (die - _view.CurTime) * div;

        Vector nColor = ByteColor(color * fraction);

        int count = 0;
        for (BeamTrail? t = head; t is not null; t = t.Next)
            count++;

        int n = 0;
        for (BeamTrail? h = head; h is not null; h = h.Next)
        {
            _verts[n++] = ByteVert(last1, nColor, 255, 0.0f, 0.0f);
            _verts[n++] = ByteVert(last2, nColor, 255, 1.0f, 0.0f);

            _view.ScreenTransform(h.Org, out screen);
            tmp = screen - screenLast;
            tmp.Z = 0;
            RopeMath.Normalize(ref tmp);
            normal = _view.Up * tmp.X;
            normal = Ma(normal, -tmp.Y, _view.Right);
            last1 = Ma(h.Org, width, normal);
            last2 = Ma(h.Org, -width, normal);

            if (h.Next is not null)
            {
                fraction = (h.Die - _view.CurTime) * div;
                nColor = ByteColor(color * fraction);
            }
            else
            {
                nColor = default;
            }

            _verts[n++] = ByteVert(last2, nColor, 255, 1.0f, 1.0f);
            _verts[n++] = ByteVert(last1, nColor, 255, 0.0f, 1.0f);

            screenLast = screen;
        }

        _sink.Mesh(BeamPrimitive.Quads, count, BeamColorKind.Rgb, _verts.AsSpan(0, n));
    }

    // (unsigned char)clamp( (int)(c * 255.0f), 0, 255 ), per component.
    private static Vector ByteColor(in Vector c)
        => new(Math.Clamp((int)(c.X * 255.0f), 0, 255), Math.Clamp((int)(c.Y * 255.0f), 0, 255),
               Math.Clamp((int)(c.Z * 255.0f), 0, 255));

    //-------------------------------------------------------------------------
    // DrawBeamQuadratic: a curve through a control point, over the bound material.
    //-------------------------------------------------------------------------

    public void DrawBeamQuadratic(in Vector start, in Vector control, in Vector end, float width, in Vector color,
                                  float scrollOffset)
    {
        const int subdivisions = 16;

        BeamSeg seg = default;
        seg.Alpha = 1.0f;
        seg.Width = width;

        float t = 0;
        float u = Fmod1(scrollOffset);
        float dt = (float)(1.0 / (float)subdivisions);

        for (int i = 0; i <= subdivisions; i++, t += dt)
        {
            float omt = 1 - t;
            float p0 = omt * omt;
            float p1 = 2 * t * omt;
            float p2 = t * t;

            seg.Pos = p0 * start + p1 * control + p2 * end;
            seg.TexCoord = u - t;
            seg.Color = i == 0 || i == subdivisions ? default : color;
            _segs[i] = seg;
        }

        _sink.Strip(_segs.AsSpan(0, subdivisions + 1));
    }
}
