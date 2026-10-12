//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.MaterialProxies;

/// <summary>A source of uniform draws: the engine's <c>VEngineRandom001</c> in the game.</summary>
public interface IProxyUniform
{
    /// <summary><c>IUniformRandomStream::RandomFloat( lo, hi )</c>.</summary>
    float RandomFloat(float lo, float hi);
}

/// <summary>
/// <c>CGaussianRandomStream</c> (vstdlib) over the engine's uniform
/// stream: Box-Muller with rejection, one value of each pair kept for the next
/// call. The client's <c>randomgaussian</c> was one stream attached to
/// <c>random</c> in <c>IBaseClientDLL::Init</c>, and the GaussianNoise proxy
/// was its only reader in this build, so this service is that stream. The
/// pending value is state a reload carries.
/// </summary>
public sealed class GaussianRandom
{
    private readonly IProxyMath _math;
    private bool _haveValue;
    private float _value;

    public GaussianRandom(IProxyMath math) => _math = math;

    public float RandomFloat(IProxyUniform uniform, float mean, float stdDev)
    {
        if (!_haveValue)
        {
            float v1, v2, rsq;
            do
            {
                v1 = 2.0f * uniform.RandomFloat(0f, 1f) - 1.0f;
                v2 = 2.0f * uniform.RandomFloat(0f, 1f) - 1.0f;
                rsq = v1 * v1 + v2 * v2;
            } while (rsq > 1.0f || rsq == 0.0f);

            float fac = _math.BoxMullerFactor(rsq);

            _value = v1 * fac;
            _haveValue = true;
            return stdDev * (v2 * fac) + mean;
        }

        _haveValue = false;
        return stdDev * _value + mean;
    }

    public void Save(ReloadWriter writer)
    {
        writer.Write(_haveValue);
        writer.Write(_value);
    }

    public void Restore(ReloadReader reader)
    {
        _haveValue = reader.ReadBool();
        _value = reader.ReadFloat();
    }
}
