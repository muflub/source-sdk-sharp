//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// <c>CClientSideEffect</c> (clientsideeffects.h): one effect on the client's
/// effect list. <see cref="Draw"/> advances it by a frame and hands its mesh to
/// native; the list removes it once it is no longer <see cref="IsActive"/>.
/// The four kinds below are stock's four (<c>fx_line.cpp</c>, <c>fx_quad.cpp</c>,
/// <c>fx_staticline.cpp</c>, <c>fx_discreetline.cpp</c>), each spelt as stock
/// spells its arithmetic so the mesh matches the recorded one bit for bit
/// (<c>Fixtures/effectlist-stock.tsv.gz</c>, ClientEffectListTests).
/// </summary>
public abstract class ClientSideEffect
{
    protected ClientSideEffect(string name, int material)
    {
        Name = name;
        Material = material;
    }

    /// <summary><c>GetName()</c>.</summary>
    public string Name { get; protected set; }

    /// <summary>The material id native gave out (0 once destroyed).</summary>
    public int Material { get; private set; }

    /// <summary>Advances the effect by <paramref name="frametime"/> and draws it.</summary>
    public abstract void Draw(double frametime, IFxRender render);

    /// <summary><c>IsActive()</c>.</summary>
    public abstract bool IsActive { get; }

    /// <summary><c>Destroy()</c>: gives the material's reference back, once.</summary>
    public virtual void Destroy(IFxRender render)
    {
        if (Material != 0)
        {
            render.MaterialRelease(Material);
            Material = 0;
        }
    }

    /// <summary>The kind, for the reload carrier and the effects route.</summary>
    public abstract string Kind { get; }

    /// <summary>
    /// Writes the effect's whole state for a managed reload (plan_client_novalve
    /// §3.8): the material id crosses as the integer native issued, and the
    /// reference it holds is carried with it, not taken again.
    /// </summary>
    internal virtual void Save(ReloadWriter writer)
    {
        writer.Write(Name);
        writer.Write(Material);
    }

    protected static void Write(ReloadWriter w, in Vector v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    protected static Vector ReadVector(ReloadReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());

    /// <summary>Four vertices of a quad strip around the segment <paramref name="a"/>-<paramref name="b"/>.</summary>
    protected static void Quad(Span<FxVertex> v, in Vector a, in Vector b, float scale, in Vector cross,
                               float c, float alpha, float sa, float ta, float sb, float tb, float sc, float tc, float sd, float td)
    {
        v[0] = new FxVertex(FxMath.MA(a, -scale, cross), sa, ta, c, c, c, alpha, cross);
        v[1] = new FxVertex(FxMath.MA(a, scale, cross), sb, tb, c, c, c, alpha, cross);
        v[2] = new FxVertex(FxMath.MA(b, scale, cross), sc, tc, c, c, c, alpha, cross);
        v[3] = new FxVertex(FxMath.MA(b, -scale, cross), sd, td, c, c, c, alpha, cross);
    }
}

/// <summary><c>FXLineData_t</c> (fx_line.h).</summary>
public struct FxLineData
{
    public Vector Start, End, StartVelocity, EndVelocity;
    public float StartAlpha, EndAlpha, StartScale, EndScale, DieTime, LifeTime;
    public int Material;
}

/// <summary><c>CFXLine</c> (fx_line.cpp): a segment that moves, scales and fades over its life.</summary>
public sealed class FxLine : ClientSideEffect
{
    private FxLineData _data;

    /// <summary>The constructor takes its own reference on the material, as stock's does.</summary>
    public FxLine(string name, in FxLineData data, IFxRender render) : base(name, data.Material)
    {
        _data = data;
        _data.LifeTime = 0.0f;
        if (data.Material != 0)
            render.MaterialAddRef(data.Material);
    }

    private FxLine(string name, in FxLineData data) : base(name, data.Material)
    {
        _data = data;
    }

    public override string Kind => "Line";

    public FxLineData Data => _data;

    internal override void Save(ReloadWriter w)
    {
        base.Save(w);
        Write(w, _data.Start); Write(w, _data.End); Write(w, _data.StartVelocity); Write(w, _data.EndVelocity);
        w.Write(_data.StartAlpha); w.Write(_data.EndAlpha); w.Write(_data.StartScale); w.Write(_data.EndScale);
        w.Write(_data.DieTime); w.Write(_data.LifeTime);
    }

    internal static FxLine Restore(ReloadReader r)
    {
        string name = r.ReadString();
        int material = r.ReadInt();
        var d = new FxLineData
        {
            Material = material, Start = ReadVector(r), End = ReadVector(r), StartVelocity = ReadVector(r), EndVelocity = ReadVector(r),
            StartAlpha = r.ReadFloat(), EndAlpha = r.ReadFloat(), StartScale = r.ReadFloat(), EndScale = r.ReadFloat(),
            DieTime = r.ReadFloat(), LifeTime = r.ReadFloat(),
        };
        return new FxLine(name, d);
    }

    public override bool IsActive => _data.LifeTime < _data.DieTime;

    private void Update(double frametime)
    {
        _data.LifeTime = (float)(_data.LifeTime + frametime);
        float ft = (float)frametime;
        _data.Start = FxMath.MA(_data.Start, ft, _data.StartVelocity);
        _data.End = FxMath.MA(_data.End, ft, _data.EndVelocity);
    }

    public override void Draw(double frametime, IFxRender render)
    {
        Update(frametime);

        Vector lineDir = FxMath.Sub(_data.Start, _data.End);
        Vector viewDir = FxMath.Sub(_data.End, render.View().Origin);
        Vector cross = FxMath.Cross(lineDir, viewDir);
        FxMath.Normalize(ref cross);

        float scaleTimePerc = _data.LifeTime / _data.DieTime;
        float scale = _data.StartScale + ((_data.EndScale - _data.StartScale) * scaleTimePerc);

        float alpha = _data.StartAlpha + ((_data.EndAlpha - _data.StartAlpha) * scaleTimePerc);
        alpha = FxMath.Clamp(alpha, 0.0f, 1.0f);

        // color32 color = {255,255,255,255}; color.a *= alpha -- a byte times a
        // float, stored back into the byte.
        byte a = (byte)(255 * alpha);

        Span<FxVertex> v = stackalloc FxVertex[4];
        Quad(v, _data.Start, _data.End, scale, cross, 255f, a, 1f, 1f, 0f, 1f, 0f, 0f, 1f, 0f);
        render.DrawQuads(Material, v, byteColor: true);
    }
}

/// <summary><c>FXQuadData_t</c>'s flags (fx_quad.h).</summary>
[Flags]
public enum FxQuadFlags : uint
{
    None = 0,
    BiasScale = 0x0001,
    BiasAlpha = 0x0002,
    ColorFade = 0x0004,
}

/// <summary><c>FXQuadData_t</c> (fx_quad.h).</summary>
public struct FxQuadData
{
    public FxQuadFlags Flags;
    public int Material;
    public Vector Origin, Normal;
    public float StartScale, EndScale, DieTime, LifeTime, StartAlpha, EndAlpha;
    public Vector Color;
    public float Yaw, DeltaYaw, ScaleBias, AlphaBias;
}

/// <summary>
/// <c>CFXQuad</c> (fx_quad.cpp): a spinning, scaling, fading square on a plane.
/// Its material reference is the caller's (<c>FXQuadData_t::SetMaterial</c> took
/// it); the quad gives it back when destroyed.
/// </summary>
public sealed class FxQuad : ClientSideEffect
{
    private const string EffectName = "Quad";

    private FxQuadData _data;

    public FxQuad(in FxQuadData data, IFxRender render) : base(EffectName, data.Material)
    {
        _data = data;
        if (data.Material != 0)
        {
            string material = render.MaterialName(data.Material);
            if (material.Length > 0)
                Name = material;
        }
    }

    private FxQuad(string name, in FxQuadData data) : base(name, data.Material)
    {
        _data = data;
    }

    public override string Kind => "Quad";

    public FxQuadData Data => _data;

    internal override void Save(ReloadWriter w)
    {
        base.Save(w);
        w.Write((int)_data.Flags); Write(w, _data.Origin); Write(w, _data.Normal);
        w.Write(_data.StartScale); w.Write(_data.EndScale); w.Write(_data.DieTime); w.Write(_data.LifeTime);
        w.Write(_data.StartAlpha); w.Write(_data.EndAlpha); Write(w, _data.Color);
        w.Write(_data.Yaw); w.Write(_data.DeltaYaw); w.Write(_data.ScaleBias); w.Write(_data.AlphaBias);
    }

    internal static FxQuad Restore(ReloadReader r)
    {
        string name = r.ReadString();
        int material = r.ReadInt();
        var d = new FxQuadData
        {
            Material = material, Flags = (FxQuadFlags)r.ReadInt(), Origin = ReadVector(r), Normal = ReadVector(r),
            StartScale = r.ReadFloat(), EndScale = r.ReadFloat(), DieTime = r.ReadFloat(), LifeTime = r.ReadFloat(),
            StartAlpha = r.ReadFloat(), EndAlpha = r.ReadFloat(), Color = ReadVector(r),
            Yaw = r.ReadFloat(), DeltaYaw = r.ReadFloat(), ScaleBias = r.ReadFloat(), AlphaBias = r.ReadFloat(),
        };
        return new FxQuad(name, d);
    }

    public override bool IsActive => _data.LifeTime < _data.DieTime;

    public override void Destroy(IFxRender render)
    {
        Name = EffectName;
        base.Destroy(render);
    }

    public override void Draw(double frametime, IFxRender render)
    {
        _data.LifeTime = (float)(_data.LifeTime + frametime);

        float scaleTimePerc = (_data.Flags & FxQuadFlags.BiasScale) != 0
            ? FxMath.Bias(_data.LifeTime / _data.DieTime, _data.ScaleBias)
            : _data.LifeTime / _data.DieTime;
        float scale = _data.StartScale + ((_data.EndScale - _data.StartScale) * scaleTimePerc);

        float alphaTimePerc = (_data.Flags & FxQuadFlags.BiasAlpha) != 0
            ? FxMath.Bias(_data.LifeTime / _data.DieTime, _data.AlphaBias)
            : _data.LifeTime / _data.DieTime;
        float alpha = _data.StartAlpha + ((_data.EndAlpha - _data.StartAlpha) * alphaTimePerc);
        alpha = FxMath.Clamp(alpha, 0.0f, 1.0f);

        // A quad with nothing to show does not draw, and does not spin either.
        if (alpha == 0)
            return;

        _data.Yaw = FxMath.AngleMod((float)(_data.Yaw + (_data.DeltaYaw * frametime)));

        float r = _data.Color.X, g = _data.Color.Y, b = _data.Color.Z;
        if ((_data.Flags & FxQuadFlags.ColorFade) != 0)
        {
            r *= alpha;
            g *= alpha;
            b *= alpha;
        }

        FxMath.VectorVectors(_data.Normal, out Vector vRight, out Vector vUp);

        float yawRad = FxMath.DegToRad(_data.Yaw);
        float yaw90Rad = FxMath.DegToRad(_data.Yaw + 90.0f);
        Vector rRight = FxMath.Sub(FxMath.Mul(vRight, MathF.Cos(yawRad)), FxMath.Mul(vUp, MathF.Sin(yawRad)));
        Vector rUp = FxMath.Sub(FxMath.Mul(vRight, MathF.Cos(yaw90Rad)), FxMath.Mul(vUp, MathF.Sin(yaw90Rad)));

        vRight = FxMath.Mul(rRight, scale * 0.5f);
        vUp = FxMath.Mul(rUp, scale * 0.5f);

        Vector o = _data.Origin;
        Span<FxVertex> v = stackalloc FxVertex[4];
        v[0] = new FxVertex(FxMath.Sub(FxMath.Add(o, vRight), vUp), 1f, 1f, r, g, b, alpha, _data.Normal);
        v[1] = new FxVertex(FxMath.Sub(FxMath.Sub(o, vRight), vUp), 0f, 1f, r, g, b, alpha, _data.Normal);
        v[2] = new FxVertex(FxMath.Add(FxMath.Sub(o, vRight), vUp), 0f, 0f, r, g, b, alpha, _data.Normal);
        v[3] = new FxVertex(FxMath.Add(FxMath.Add(o, vRight), vUp), 1f, 0f, r, g, b, alpha, _data.Normal);
        render.DrawQuads(Material, v, byteColor: false);
    }
}

/// <summary><c>CFXStaticLine</c>'s flags (fx_staticline.h).</summary>
[Flags]
public enum FxStaticLineFlags : uint
{
    None = 0,
    FlipHorizontal = 0x00000001,
    FlipVertical = 0x00000002,
}

/// <summary><c>CFXStaticLine</c> (fx_staticline.cpp): a fixed segment for a fixed time.</summary>
public sealed class FxStaticLine : ClientSideEffect
{
    private readonly Vector _start, _end;
    private readonly FxStaticLineFlags _flags;
    private readonly float _scale;
    private float _life;

    /// <param name="material">An id <see cref="IFxRender.MaterialFind"/> answered, its reference this line's.</param>
    public FxStaticLine(string name, Vector start, Vector end, float scale, float life, int material, FxStaticLineFlags flags)
        : base(name, material)
    {
        _start = start;
        _end = end;
        _flags = flags;
        _life = life;
        _scale = scale * 0.5f;
    }

    public override string Kind => "StaticLine";

    internal override void Save(ReloadWriter w)
    {
        base.Save(w);
        Write(w, _start); Write(w, _end); w.Write((int)_flags); w.Write(_scale); w.Write(_life);
    }

    internal static FxStaticLine Restore(ReloadReader r)
    {
        string name = r.ReadString();
        int material = r.ReadInt();
        Vector start = ReadVector(r), end = ReadVector(r);
        var flags = (FxStaticLineFlags)r.ReadInt();
        float halfScale = r.ReadFloat(), life = r.ReadFloat();
        return new FxStaticLine(name, start, end, halfScale * 2.0f, life, material, flags) { };
    }

    public (Vector Start, Vector End, float HalfScale, float Life, FxStaticLineFlags Flags) State => (_start, _end, _scale, _life, _flags);

    public override bool IsActive => _life > 0.0;

    public override void Draw(double frametime, IFxRender render)
    {
        _life = (float)(_life - frametime);

        Vector lineDir = FxMath.Sub(_end, _start);
        Vector viewDir = FxMath.Sub(_end, render.View().Origin);
        Vector cross = FxMath.Cross(lineDir, viewDir);
        FxMath.Normalize(ref cross);

        bool flipVertical = (_flags & FxStaticLineFlags.FlipVertical) != 0;
        bool flipHorizontal = (_flags & FxStaticLineFlags.FlipHorizontal) != 0;

        Span<FxVertex> v = stackalloc FxVertex[4];
        if (flipHorizontal)
            Quad(v, _start, _end, _scale, cross, 255f, 255f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f);
        else if (flipVertical)
            Quad(v, _start, _end, _scale, cross, 255f, 255f, 0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f);
        else
            Quad(v, _start, _end, _scale, cross, 255f, 255f, 1f, 1f, 0f, 1f, 0f, 0f, 1f, 0f);
        render.DrawQuads(Material, v, byteColor: true);
    }
}

/// <summary>
/// <c>CFXDiscreetLine</c> (fx_discreetline.cpp): a tracer's streak, a segment of
/// fixed length running along a direction, optionally clipped, with
/// <c>tracer_extra</c>'s screen-space widening and halo.
/// </summary>
public sealed class FxDiscreetLine : ClientSideEffect
{
    private readonly Vector _origin, _direction;
    private readonly float _velocity, _clipLength, _scale, _length;
    private readonly Func<bool> _tracerExtra;
    private float _life, _startTime;

    /// <param name="material">An id <see cref="IFxRender.MaterialFind"/> answered, its reference this line's.</param>
    public FxDiscreetLine(string name, Vector start, Vector direction, float velocity, float length, float clipLength,
                          float scale, float life, int material, Func<bool> tracerExtra)
        : base(name, material)
    {
        _origin = start;
        _direction = direction;
        _velocity = velocity;
        _clipLength = clipLength;
        _scale = scale;
        _life = life;
        _startTime = 0.0f;
        _length = length;
        _tracerExtra = tracerExtra;
    }

    public override string Kind => "DiscreetLine";

    internal override void Save(ReloadWriter w)
    {
        base.Save(w);
        Write(w, _origin); Write(w, _direction);
        w.Write(_velocity); w.Write(_clipLength); w.Write(_scale); w.Write(_length); w.Write(_life); w.Write(_startTime);
    }

    internal static FxDiscreetLine Restore(ReloadReader r, Func<bool> tracerExtra)
    {
        string name = r.ReadString();
        int material = r.ReadInt();
        Vector origin = ReadVector(r), direction = ReadVector(r);
        float velocity = r.ReadFloat(), clip = r.ReadFloat(), scale = r.ReadFloat(), length = r.ReadFloat();
        float life = r.ReadFloat(), startTime = r.ReadFloat();
        return new FxDiscreetLine(name, origin, direction, velocity, length, clip, scale, life, material, tracerExtra)
        {
            _startTime = startTime,
        };
    }

    public (Vector Origin, Vector Direction, float Velocity, float Length, float Clip, float Scale, float Life, float StartTime) State
        => (_origin, _direction, _velocity, _length, _clipLength, _scale, _life, _startTime);

    public override bool IsActive => _life > 0.0;

    public override void Draw(double frametime, IFxRender render)
    {
        _startTime = (float)(_startTime + frametime);
        _life = (float)(_life - frametime);

        float sDistance = _velocity * _startTime;
        float eDistance = sDistance - _length;

        sDistance = FxMath.Max(0.0f, sDistance);
        eDistance = FxMath.Max(0.0f, eDistance);

        if ((sDistance == 0.0f) && (eDistance == 0.0f))
            return;

        if (_clipLength != 0.0f)
        {
            sDistance = FxMath.Min(sDistance, _clipLength);
            eDistance = FxMath.Min(eDistance, _clipLength);
        }

        float dDistance = MathF.Abs(sDistance - eDistance);
        float dTotal = (_length != 0.0f) ? _length : 0.01f;
        float fOffset = dDistance / dTotal;

        Vector vecEnd = FxMath.MA(_origin, sDistance, _direction);
        Vector vecStart = FxMath.MA(_origin, eDistance, _direction);

        FxView view = render.View();
        Vector lineDir = FxMath.Sub(vecEnd, vecStart);
        Vector viewDir = FxMath.Sub(vecEnd, view.Origin);
        Vector cross = FxMath.Cross(lineDir, viewDir);
        FxMath.Normalize(ref cross);

        if (_tracerExtra())
        {
            float flScreenWidth = view.ScreenWidth;
            float flHalfScreenWidth = flScreenWidth * 0.5f;

            float zCoord = FxMath.Dot(view.Forward, FxMath.Sub(vecStart, view.Origin));
            float flScreenSpaceWidth = _scale * flHalfScreenWidth / zCoord;

            float flAlpha;
            float flScale;

            if (flScreenSpaceWidth < 0.5f)
            {
                flAlpha = FxMath.RemapVal(flScreenSpaceWidth, 0.25f, 2.0f, 0.3f, 1.0f);
                flAlpha = FxMath.Clamp(flAlpha, 0.25f, 1.0f);
                flScale = 0.5f * zCoord / flHalfScreenWidth;
            }
            else
            {
                flAlpha = 1.0f;
                flScale = _scale;
            }

            // float color = (int) 255.0f * flAlpha; handed to Color4ub, which
            // takes it as an unsigned char.
            Span<FxVertex> v = stackalloc FxVertex[8];
            float color = (byte)(255 * flAlpha);
            Quad(v[..4], vecStart, vecEnd, flScale, cross, color, 255f, 1f, 0f, 0f, 0f, 0f, fOffset, 1f, fOffset);

            flScale = flScale * 2.0f;
            color = (byte)(64 * flAlpha);
            Quad(v[4..], vecStart, vecEnd, flScale, cross, color, 255f, 1f, 0f, 0f, 0f, 0f, fOffset, 1f, fOffset);
            render.DrawQuads(Material, v, byteColor: true);
        }
        else
        {
            Span<FxVertex> v = stackalloc FxVertex[4];
            Quad(v, vecStart, vecEnd, _scale, cross, 255f, 255f, 1f, 0f, 0f, 0f, 0f, fOffset, 1f, fOffset);
            render.DrawQuads(Material, v, byteColor: true);
        }
    }
}
