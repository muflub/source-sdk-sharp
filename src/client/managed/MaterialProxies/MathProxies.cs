//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// A <c>Vector</c> temporary as mathproxy.cpp's binds use one: read with
/// <c>GetVecValue( a.Base(), vecSize )</c>, three components operated on
/// (VectorAdd and its siblings touch x, y, z), written back with
/// <c>SetVecValue( a.Base(), vecSize )</c>.
///
/// <para>
/// FOUR WIDE, WHERE STOCK'S IS THREE. A four-component result makes stock's
/// <c>GetVecValue( a.Base(), 4 )</c> write past the 12-byte <c>Vector</c>; in
/// the recorded debug build that fourth float lands on the <c>vecSize</c> local
/// beside it, so the write that follows is <c>SetVecValue( a, 0 )</c> (the
/// source's fourth component was 0.0f). That is undefined behaviour whose
/// answer is the compiler's stack layout, not a rule a port can keep: this
/// keeps the fourth component as read and writes all four.
/// MaterialProxyOracleTests names the recorded binds this covers.
/// </para>
/// </summary>
internal struct Vec4Temp
{
    public float X, Y, Z, W;

    public static Vec4Temp Read(ProxyVar v, int vecSize)
    {
        var t = new Vec4Temp();
        if (vecSize > 0) t.X = v.V0;
        if (vecSize > 1) t.Y = v.V1;
        if (vecSize > 2) t.Z = v.V2;
        if (vecSize > 3) t.W = v.V3;
        return t;
    }

    public readonly void Write(IProxyBind bind, int slot, int vecSize)
    {
        Span<float> v = stackalloc float[4] { X, Y, Z, W };
        bind.SetVec(slot, v, vecSize);
    }

    /// <summary><c>Vector::IsZero( 0.01f )</c>.</summary>
    public readonly bool IsZero()
        => X > -0.01f && X < 0.01f && Y > -0.01f && Y < 0.01f && Z > -0.01f && Z < 0.01f;
}

/// <summary>Add, Subtract, Multiply, Divide: <c>CFunctionProxy</c>s that need both sources.</summary>
public abstract class BinaryProxy : FunctionProxy
{
    public override bool Init(IProxyInit init) => base.Init(init) && Src2 >= 0;

    public override void Bind(IProxyBind bind)
    {
        MaterialVarType resultType = ComputeResultType(bind, out int vecSize);
        ProxyVar s1 = bind.Var(Src1);
        ProxyVar s2 = bind.Var(Src2);
        switch (resultType)
        {
            case MaterialVarType.Vector:
            {
                Vec4Temp a = Vec4Temp.Read(s1, vecSize);
                Vec4Temp b = Vec4Temp.Read(s2, vecSize);
                Vec4Temp c = a;
                c.X = Vector(a.X, b.X);
                c.Y = Vector(a.Y, b.Y);
                c.Z = Vector(a.Z, b.Z);
                c.Write(bind, Result, vecSize);
                break;
            }
            case MaterialVarType.Float:
                FloatCase(bind, s1, s2);
                break;
            case MaterialVarType.Int:
                IntCase(bind, s1, s2);
                break;
        }
    }

    protected abstract float Vector(float a, float b);

    protected abstract void FloatCase(IProxyBind bind, ProxyVar s1, ProxyVar s2);

    protected abstract void IntCase(IProxyBind bind, ProxyVar s1, ProxyVar s2);
}

/// <summary>mathproxy.cpp <c>CAddProxy</c> ("Add").</summary>
public sealed class AddProxy : BinaryProxy
{
    protected override float Vector(float a, float b) => a + b;

    protected override void FloatCase(IProxyBind bind, ProxyVar s1, ProxyVar s2) => SetFloatResult(bind, s1.Float + s2.Float);

    protected override void IntCase(IProxyBind bind, ProxyVar s1, ProxyVar s2) => bind.SetFloat(Result, unchecked(s1.Int + s2.Int));
}

/// <summary>mathproxy.cpp <c>CSubtractProxy</c> ("Subtract").</summary>
public sealed class SubtractProxy : BinaryProxy
{
    protected override float Vector(float a, float b) => a - b;

    protected override void FloatCase(IProxyBind bind, ProxyVar s1, ProxyVar s2) => SetFloatResult(bind, s1.Float - s2.Float);

    protected override void IntCase(IProxyBind bind, ProxyVar s1, ProxyVar s2) => bind.SetFloat(Result, unchecked(s1.Int - s2.Int));
}

/// <summary>mathproxy.cpp <c>CMultiplyProxy</c> ("Multiply").</summary>
public sealed class MultiplyProxy : BinaryProxy
{
    protected override float Vector(float a, float b) => a * b;

    protected override void FloatCase(IProxyBind bind, ProxyVar s1, ProxyVar s2) => SetFloatResult(bind, s1.Float * s2.Float);

    protected override void IntCase(IProxyBind bind, ProxyVar s1, ProxyVar s2) => bind.SetFloat(Result, unchecked(s1.Int * s2.Int));
}

/// <summary>mathproxy.cpp <c>CDivideProxy</c> ("Divide"): a zero divisor passes the dividend through.</summary>
public sealed class DivideProxy : BinaryProxy
{
    // VectorDivide is a component divide with no zero test.
    protected override float Vector(float a, float b) => a / b;

    protected override void FloatCase(IProxyBind bind, ProxyVar s1, ProxyVar s2)
    {
        if (s2.Float != 0)
            SetFloatResult(bind, s1.Float / s2.Float);
        else
            SetFloatResult(bind, s1.Float);
    }

    protected override void IntCase(IProxyBind bind, ProxyVar s1, ProxyVar s2)
    {
        // int / int truncates; int.MinValue / -1 traps in C as here.
        if (s2.Int != 0)
            bind.SetFloat(Result, s1.Int / s2.Int);
        else
            bind.SetFloat(Result, s1.Int);
    }
}

/// <summary>mathproxy.cpp <c>CClampProxy</c> ("Clamp").</summary>
public sealed class ClampProxy : FunctionProxy
{
    private ProxyFloatInput _min;
    private ProxyFloatInput _max;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        if (!_min.Init(init, "min", 0))
            return false;
        return _max.Init(init, "max", 1);
    }

    public override void Bind(IProxyBind bind)
    {
        MaterialVarType resultType = ComputeResultType(bind, out int vecSize);
        float flMin = _min.Get(bind);
        float flMax = _max.Get(bind);
        if (flMin > flMax)
            (flMin, flMax) = (flMax, flMin);

        ProxyVar s1 = bind.Var(Src1);
        switch (resultType)
        {
            case MaterialVarType.Vector:
            {
                // A Vector read, clamped component by component for vecSize
                // (stock's loop indexes the Vector, so it runs to vecSize).
                Span<float> a = stackalloc float[4];
                for (int i = 0; i < vecSize && i < 4; ++i)
                {
                    float x = s1[i];
                    if (x < flMin)
                        x = flMin;
                    else if (x > flMax)
                        x = flMax;
                    a[i] = x;
                }
                bind.SetVec(Result, a, vecSize);
                break;
            }
            case MaterialVarType.Float:
            {
                float src = s1.Float;
                if (src < flMin)
                    src = flMin;
                else if (src > flMax)
                    src = flMax;
                SetFloatResult(bind, src);
                break;
            }
            case MaterialVarType.Int:
            {
                // int compared with float, the float assigned back: truncation.
                int src = s1.Int;
                if (src < flMin)
                    src = (int)flMin;
                else if (src > flMax)
                    src = (int)flMax;
                bind.SetInt(Result, src);
                break;
            }
        }
    }
}

/// <summary>mathproxy.cpp <c>CSineProxy</c> ("Sine").</summary>
public sealed class SineProxy : ResultProxy
{
    private const double Pi = 3.14159265358979323846;

    private ProxyFloatInput _period;
    private ProxyFloatInput _max;
    private ProxyFloatInput _min;
    private ProxyFloatInput _timeOffset;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        if (!_period.Init(init, "sinePeriod", 1.0f))
            return false;
        if (!_max.Init(init, "sineMax", 1.0f))
            return false;
        if (!_min.Init(init, "sineMin", 0.0f))
            return false;
        return _timeOffset.Init(init, "timeOffset", 0.0f);
    }

    public override void Bind(IProxyBind bind)
    {
        float offset = _timeOffset.Get(bind);
        float max = _max.Get(bind);
        float min = _min.Get(bind);
        float period = _period.Get(bind);
        if (period == 0)
            period = 1;

        // sin( 2.0f * M_PI * (curtime - offset) / period ) is double throughout.
        float value = (float)(Math.Sin(2.0f * Pi * (bind.CurTime - offset) / period) * 0.5f + 0.5f);
        value = (max - min) * value + min;
        SetFloatResult(bind, value);
    }
}

/// <summary>mathproxy.cpp <c>CEqualsProxy</c> ("Equals").</summary>
public sealed class EqualsProxy : FunctionProxy
{
    public override void Bind(IProxyBind bind)
    {
        MaterialVarType resultType = ComputeResultType(bind, out int vecSize);
        ProxyVar s1 = bind.Var(Src1);
        switch (resultType)
        {
            case MaterialVarType.Vector:
                Vec4Temp.Read(s1, vecSize).Write(bind, Result, vecSize);
                break;
            case MaterialVarType.Float:
                SetFloatResult(bind, s1.Float);
                break;
            case MaterialVarType.Int:
                bind.SetInt(Result, s1.Int);
                break;
        }
    }
}

/// <summary>mathproxy.cpp <c>CFracProxy</c> ("Frac") and <c>CIntProxy</c> ("Int").</summary>
public sealed class FracIntProxy : FunctionProxy
{
    private readonly bool _frac;

    public FracIntProxy(bool frac) => _frac = frac;

    private float Apply(float a) => _frac ? a - (float)(int)a : (float)(int)a;

    public override void Bind(IProxyBind bind)
    {
        MaterialVarType resultType = ComputeResultType(bind, out int vecSize);
        ProxyVar s1 = bind.Var(Src1);
        switch (resultType)
        {
            case MaterialVarType.Vector:
            {
                Vec4Temp a = Vec4Temp.Read(s1, vecSize);
                a.X = Apply(a.X);
                a.Y = Apply(a.Y);
                a.Z = Apply(a.Z);
                a.Write(bind, Result, vecSize);
                break;
            }
            case MaterialVarType.Float:
            {
                // Frac's float case is `a -= ( int )a`, the int converted to float.
                float a = s1.Float;
                SetFloatResult(bind, Apply(a));
                break;
            }
            case MaterialVarType.Int:
                bind.SetInt(Result, s1.Int);
                break;
        }
    }
}

/// <summary>mathproxy.cpp <c>CLinearRampProxy</c> ("LinearRamp").</summary>
public sealed class LinearRampProxy : ResultProxy
{
    private ProxyFloatInput _rate;
    private ProxyFloatInput _initial;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        if (!_rate.Init(init, "rate", 1))
            return false;
        return _initial.Init(init, "initialValue", 0);
    }

    public override void Bind(IProxyBind bind)
    {
        float value = _rate.Get(bind) * bind.CurTime + _initial.Get(bind);
        SetFloatResult(bind, value);
    }
}

/// <summary>mathproxy.cpp <c>CUniformNoiseProxy</c> ("UniformNoise").</summary>
public sealed class UniformNoiseProxy : ResultProxy
{
    private ProxyFloatInput _min;
    private ProxyFloatInput _max;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        if (!_min.Init(init, "minVal", 0))
            return false;
        return _max.Init(init, "maxVal", 1);
    }

    public override void Bind(IProxyBind bind)
    {
        SetFloatResult(bind, bind.RandomFloat(_min.Get(bind), _max.Get(bind)));
    }
}

/// <summary>mathproxy.cpp <c>CGaussianNoiseProxy</c> ("GaussianNoise").</summary>
public sealed class GaussianNoiseProxy : ResultProxy
{
    private ProxyFloatInput _mean;
    private ProxyFloatInput _stdDev;
    private ProxyFloatInput _min;
    private ProxyFloatInput _max;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        if (!_mean.Init(init, "mean", 0.0f))
            return false;
        if (!_stdDev.Init(init, "halfwidth", 1.0f))
            return false;
        if (!_min.Init(init, "minVal", -float.MaxValue))
            return false;
        return _max.Init(init, "maxVal", float.MaxValue);
    }

    public override void Bind(IProxyBind bind)
    {
        float mean = _mean.Get(bind);
        float stdDev = _stdDev.Get(bind);
        float value = bind.RandomGaussian(mean, stdDev);
        float max = _max.Get(bind);
        float min = _min.Get(bind);
        if (min > max)
            (min, max) = (max, min);

        if (value < min)
            value = min;
        else if (value > max)
            value = max;
        SetFloatResult(bind, value);
    }
}

/// <summary>mathproxy.cpp <c>CExponentialProxy</c> ("Exponential").</summary>
public sealed class ExponentialProxy : FunctionProxy
{
    private readonly IProxyMath _math;
    private ProxyFloatInput _scale;
    private ProxyFloatInput _offset;
    private ProxyFloatInput _min;
    private ProxyFloatInput _max;

    public ExponentialProxy(IProxyMath math) => _math = math;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        if (!_scale.Init(init, "scale", 1.0f))
            return false;
        if (!_offset.Init(init, "offset", 0.0f))
            return false;
        if (!_min.Init(init, "minVal", -float.MaxValue))
            return false;
        return _max.Init(init, "maxVal", float.MaxValue);
    }

    public override void Bind(IProxyBind bind)
    {
        float value = _math.Exponential(_scale.Get(bind), bind.Var(Src1).Float + _offset.Get(bind));
        float max = _max.Get(bind);
        float min = _min.Get(bind);
        if (min > max)
            (min, max) = (max, min);

        if (value < min)
            value = min;
        else if (value > max)
            value = max;
        SetFloatResult(bind, value);
    }
}

/// <summary>mathproxy.cpp <c>CAbsProxy</c> ("Abs").</summary>
public sealed class AbsProxy : FunctionProxy
{
    public override void Bind(IProxyBind bind)
    {
        SetFloatResult(bind, MathF.Abs(bind.Var(Src1).Float));
    }
}

/// <summary>mathproxy.cpp <c>CEmptyProxy</c> ("Empty"): comments a proxy block out.</summary>
public sealed class EmptyProxy : MaterialProxy
{
    public override bool Init(IProxyInit init) => true;

    public override void Bind(IProxyBind bind)
    {
    }
}

/// <summary>mathproxy.cpp <c>CLessOrEqualProxy</c> ("LessOrEqual").</summary>
public sealed class LessOrEqualProxy : FunctionProxy
{
    private int _less = -1;
    private int _greater = -1;

    public override bool Init(IProxyInit init)
    {
        string less = init.Keys.GetString("lessEqualVar");
        _less = init.FindVar(less, true, out bool found);
        if (!found)
            return false;

        string greater = init.Keys.GetString("greaterVar");
        _greater = init.FindVar(greater, true, out found);
        if (!found)
            return false;

        return base.Init(init) && Src2 >= 0;
    }

    public override void Bind(IProxyBind bind)
    {
        int source = bind.Var(Src1).Float <= bind.Var(Src2).Float ? _less : _greater;
        ProxyVar src = bind.Var(source);

        int vecSize = 0;
        ProxyVar r = bind.Var(Result);
        var resultType = (MaterialVarType)r.Type;
        if (resultType == MaterialVarType.Vector)
        {
            if (ResultComponent >= 0)
                resultType = MaterialVarType.Float;
            vecSize = r.VecSize;
        }
        else if (resultType == MaterialVarType.Undefined)
        {
            resultType = (MaterialVarType)src.Type;
            if (resultType == MaterialVarType.Vector)
                vecSize = src.VecSize;
        }

        switch (resultType)
        {
            case MaterialVarType.Vector:
                Vec4Temp.Read(src, vecSize).Write(bind, Result, vecSize);
                break;
            case MaterialVarType.Float:
                SetFloatResult(bind, src.Float);
                break;
            case MaterialVarType.Int:
                bind.SetFloat(Result, src.Int);
                break;
        }
    }
}

/// <summary>mathproxy.cpp <c>CWrapMinMaxProxy</c> ("WrapMinMax").</summary>
public sealed class WrapMinMaxProxy : FunctionProxy
{
    private ProxyFloatInput _min;
    private ProxyFloatInput _max;

    public override bool Init(IProxyInit init)
    {
        if (!base.Init(init))
            return false;
        if (!_min.Init(init, "minVal", 0))
            return false;
        return _max.Init(init, "maxVal", 1);
    }

    public override void Bind(IProxyBind bind)
    {
        if (_max.Get(bind) <= _min.Get(bind))
        {
            SetFloatResult(bind, _min.Get(bind));
        }
        else
        {
            float result = (bind.Var(Src1).Float - _min.Get(bind)) / (_max.Get(bind) - _min.Get(bind));
            if (result >= 0.0f)
                result -= (float)(int)result;
            else
                result -= (float)((int)result - 1);

            result *= _max.Get(bind) - _min.Get(bind);
            result += _min.Get(bind);
            SetFloatResult(bind, result);
        }
    }
}

/// <summary>mathproxy.cpp <c>CSelectFirstIfNonZeroProxy</c> ("SelectFirstIfNonZero").</summary>
public sealed class SelectFirstIfNonZeroProxy : FunctionProxy
{
    public override bool Init(IProxyInit init) => base.Init(init) && Src2 >= 0;

    public override void Bind(IProxyBind bind)
    {
        MaterialVarType resultType = ComputeResultType(bind, out int vecSize);
        ProxyVar s1 = bind.Var(Src1);
        ProxyVar s2 = bind.Var(Src2);
        switch (resultType)
        {
            case MaterialVarType.Vector:
            {
                Vec4Temp a = Vec4Temp.Read(s1, vecSize);
                Vec4Temp b = Vec4Temp.Read(s2, vecSize);
                (a.IsZero() ? b : a).Write(bind, Result, vecSize);
                break;
            }
            case MaterialVarType.Float:
                SetFloatResult(bind, s1.Float != 0 ? s1.Float : s2.Float);
                break;
            case MaterialVarType.Int:
                bind.SetFloat(Result, s1.Int != 0 ? s1.Int : s2.Int);
                break;
        }
    }
}

/// <summary>timematerialproxy.cpp <c>CTimeMaterialProxy</c> ("CurrentTime").</summary>
public sealed class CurrentTimeProxy : ResultProxy
{
    public override void Bind(IProxyBind bind) => SetFloatResult(bind, bind.CurTime);
}

/// <summary>matrixproxy.cpp <c>CTextureTransformProxy</c> ("TextureTransform").</summary>
public sealed class TextureTransformProxy : ResultProxy
{
    private readonly IProxyMath _math;
    private int _center = -1;
    private int _scale = -1;
    private int _rotate = -1;
    private int _translate = -1;

    public TextureTransformProxy(IProxyMath math) => _math = math;

    public override bool Init(IProxyInit init)
    {
        // All optional; a var asked for and not found still holds its slot.
        _center = Optional(init, "centerVar");
        _scale = Optional(init, "scaleVar");
        _rotate = Optional(init, "rotateVar");
        _translate = Optional(init, "translateVar");
        return base.Init(init);
    }

    internal static int Optional(IProxyInit init, string key)
    {
        string name = init.Keys.GetString(key);
        return name.Length > 0 ? init.FindVar(name, false, out _) : -1;
    }

    public override void Bind(IProxyBind bind)
    {
        float centerX = 0.5f, centerY = 0.5f;
        if (_center >= 0)
        {
            ProxyVar c = bind.Var(_center);
            centerX = c.V0;
            centerY = c.V1;
        }
        VMatrix mat = VMatrixMath.Translation(-centerX, -centerY, 0.0f);

        if (_scale >= 0)
        {
            ProxyVar s = bind.Var(_scale);
            VMatrix temp = VMatrixMath.Scale(s.V0, s.V1, 1.0f);
            mat = VMatrixMath.Multiply(temp, mat);
        }

        if (_rotate >= 0)
        {
            VMatrix temp = VMatrixMath.RotateZ(bind.Var(_rotate).Float, _math);
            mat = VMatrixMath.Multiply(temp, mat);
        }

        mat = VMatrixMath.Multiply(VMatrixMath.Translation(centerX, centerY, 0.0f), mat);

        if (_translate >= 0)
        {
            ProxyVar t = bind.Var(_translate);
            mat = VMatrixMath.Multiply(VMatrixMath.Translation(t.V0, t.V1, 0.0f), mat);
        }

        bind.SetMatrix(Result, mat);
    }
}

/// <summary>matrixproxy.cpp <c>CMatrixRotateProxy</c> ("MatrixRotate").</summary>
public sealed class MatrixRotateProxy : ResultProxy
{
    private readonly IProxyMath _math;
    private ProxyFloatInput _angle;
    private int _axis = -1;

    public MatrixRotateProxy(IProxyMath math) => _math = math;

    public override bool Init(IProxyInit init)
    {
        _axis = TextureTransformProxy.Optional(init, "axisVar");
        if (!_angle.Init(init, "angle", 0))
            return false;
        return base.Init(init);
    }

    public override void Bind(IProxyBind bind)
    {
        var axis = new Vector(0, 0, 1);
        if (_axis >= 0)
        {
            ProxyVar a = bind.Var(_axis);
            axis = new Vector(a.V0, a.V1, a.V2);
            if (MoveVec.Normalize(ref axis) < 1e-3)
                axis = new Vector(0, 0, 1);
        }

        VMatrix mat = VMatrixMath.RotationAboutAxis(axis, _angle.Get(bind), _math);
        bind.SetMatrix(Result, mat);
    }
}
