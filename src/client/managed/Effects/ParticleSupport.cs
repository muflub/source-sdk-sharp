//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The two random draws the client's simple-particle sims take, as stock takes
/// them: <c>random-&gt;RandomFloat</c> (the client's <c>VEngineRandom001</c>) and
/// <c>rand()</c>, which client.so answers from vstdlib's <c>RandomInt(0,
/// VALVE_RAND_MAX)</c> (randoverride.cpp) and which <c>FRand</c> and
/// <c>Vector::Random</c> divide down. An interface so the oracle facts replay a
/// recorded stream; the game's is <see cref="ParticleStream"/>.
/// </summary>
public interface IParticleRandom
{
    /// <summary><c>IUniformRandomStream::RandomFloat</c>.</summary>
    float RandomFloat(float min, float max);

    /// <summary><c>IUniformRandomStream::RandomInt</c>: inclusive at both ends.</summary>
    int RandomInt(int min, int max);
}

/// <summary>Stock's particle-side random helpers, over an <see cref="IParticleRandom"/>.</summary>
public static class ParticleRandomExtensions
{
    /// <summary><c>VALVE_RAND_MAX</c>.</summary>
    public const int ValveRandMax = 0x7fff;

    /// <summary><c>rand()</c> in client.so: <c>RandomInt(0, VALVE_RAND_MAX)</c>.</summary>
    public static int Rand(this IParticleRandom random) => random.RandomInt(0, ValveRandMax);

    /// <summary>
    /// <c>FRand</c> (particle_util.h), term for term:
    /// <c>min + ((float)rand() / VALVE_RAND_MAX) * (max - min)</c>.
    /// </summary>
    public static float FRand(this IParticleRandom random, float min, float max)
        => min + ((float)random.Rand() / ValveRandMax) * (max - min);
}

/// <summary>
/// The game's particle stream: decoration drawn on one machine, so not the
/// engine's stream and not on the wire (the <c>TempEntRandom</c> argument),
/// seeded from the client module's <see cref="IRandomSeeds"/>.
/// </summary>
public sealed class ParticleStream(int seed) : IParticleRandom
{
    private readonly Random _stream = new(seed);

    public float RandomFloat(float min, float max) => min + (max - min) * _stream.NextSingle();

    public int RandomInt(int min, int max) => max <= min ? min : _stream.Next(min, max + 1);
}

/// <summary>
/// <c>TimedEvent</c> (timedevent.h): "rate events per second", counted down
/// across frames. A struct with stock's two members.
/// </summary>
public struct TimedEvent
{
    public float TimeBetweenEvents;
    public float NextEvent;

    /// <summary>Stock's constructor: not initialised (<c>-1</c>), nothing pending.</summary>
    public static TimedEvent Uninitialised => new() { TimeBetweenEvents = -1f, NextEvent = 0f };

    /// <summary>A timer already <see cref="Init"/>ed at <paramref name="rate"/>, as a constructor's <c>Init( rate )</c> leaves it.</summary>
    public static TimedEvent AtRate(float rate)
    {
        var e = Uninitialised;
        e.Init(rate);
        return e;
    }

    public void Init(float rate)
    {
        TimeBetweenEvents = 1.0f / rate;
        NextEvent = 0f;
    }

    public void ResetRate(float rate) => TimeBetweenEvents = 1.0f / rate;

    /// <summary>One event if this frame's remaining time reaches it; the time is spent from <paramref name="curDelta"/>.</summary>
    public bool Next(ref float curDelta)
    {
        if (curDelta >= NextEvent)
        {
            curDelta -= NextEvent;
            NextEvent = TimeBetweenEvents;
            return true;
        }

        NextEvent -= curDelta;
        return false;
    }
}

/// <summary>The mathlib helpers the sims lean on, spelled as mathlib spells them.</summary>
public static class ParticleMath
{
    /// <summary><c>Vector::operator/(float)</c> is <c>VectorDivide</c>: one reciprocal, three multiplies.</summary>
    public static Vector Divide(Vector a, float b)
    {
        float oob = 1.0f / b;
        return new Vector(a.X * oob, a.Y * oob, a.Z * oob);
    }

    /// <summary><c>VectorsAreEqual</c> / <c>QAnglesAreEqual</c>: each component within the tolerance.</summary>
    public static bool AreEqual(Vector a, Vector b, float tolerance)
        => MathF.Abs(a.X - b.X) <= tolerance && MathF.Abs(a.Y - b.Y) <= tolerance && MathF.Abs(a.Z - b.Z) <= tolerance;

    /// <summary><c>MIN</c> / <c>MAX</c> (basetypes.h): the ternaries, not IEEE minNum.</summary>
    public static float MinF(float a, float b) => a < b ? a : b;

    public static float MaxF(float a, float b) => a > b ? a : b;

    /// <summary><c>VectorMin</c> / <c>VectorMax</c>: <c>MIN</c>/<c>MAX</c> per component.</summary>
    public static Vector Min(Vector a, Vector b) => new(MinF(a.X, b.X), MinF(a.Y, b.Y), MinF(a.Z, b.Z));

    public static Vector Max(Vector a, Vector b) => new(MaxF(a.X, b.X), MaxF(a.Y, b.Y), MaxF(a.Z, b.Z));

    /// <summary><c>M_PI_F</c>.</summary>
    public const float PiF = (float)Math.PI;

    /// <summary><c>DEG2RAD</c>: one multiply by the float constant.</summary>
    public static float Deg2Rad(float degrees) => degrees * (float)(PiF / 180.0f);

    /// <summary>
    /// mathlib's <c>TableCos</c>, the bit trick included: the angle scaled into
    /// table units plus <c>FTOIBIAS</c> leaves the index in the float's low
    /// mantissa bits. The table entry is computed rather than held (no static
    /// state): <c>InitSinCosTable</c>'s <c>sin(i * 2π / 256)</c> in double,
    /// stored as float.
    /// </summary>
    public static float TableCos(float theta)
    {
        float f = theta * (float)(256 / (2.0f * Math.PI)) + (12582912.0f + (256 / 4));
        int index = BitConverter.SingleToInt32Bits(f) & 255;
        return (float)Math.Sin(index * 2.0 * Math.PI / 256);
    }

    /// <summary><c>FLerp</c> (particle_util.h).</summary>
    public static float FLerp(float min, float max, float t) => min + (max - min) * t;
}

/// <summary>
/// jet_lifetime.h's rule: a particle's lifetime (or its reciprocal) from a
/// length and a speed, falling back to the class default when either is not a
/// positive finite number, so a jet whose map set no keys is not 0/0.
/// </summary>
public static class JetLifetime
{
    public const float SteamJetDefaultLength = 80.0f;
    public const float SteamJetDefaultSpeed = 120.0f;
    public const float SmokeStackDefaultLength = 180.0f;
    public const float SmokeStackDefaultSpeed = 30.0f;

    private static bool Usable(float f) => float.IsFinite(f);

    public static float Quotient(float numerator, float denominator, float defaultNumerator, float defaultDenominator)
    {
        float fallback = defaultNumerator / defaultDenominator;
        if (!Usable(numerator) || numerator <= 0f)
            return fallback;
        if (!Usable(denominator) || denominator <= 0f)
            return fallback;
        float result = numerator / denominator;
        return !Usable(result) || result <= 0f ? fallback : result;
    }

    public static float SteamJet(float jetLength, float speed)
        => Quotient(jetLength, speed, SteamJetDefaultLength, SteamJetDefaultSpeed);

    public static float SmokeStackInverse(float speed, float jetLength)
        => Quotient(speed, jetLength, SmokeStackDefaultSpeed, SmokeStackDefaultLength);
}

/// <summary>
/// One quad for the native draw executor: where, what colour, how big, turned
/// how far. The managed mirror of <c>SourceSharpParticleDraw_t</c>.
/// </summary>
public struct ParticleQuad
{
    public Vector Position;
    public Vector Color;
    public float Alpha;
    public float Size;
    public float Roll;
    public Vector Normal;
    public ParticleQuadKind Kind;

    /// <summary>
    /// <c>GetAlphaDistanceFade</c>'s near and far, in view units: when
    /// <see cref="FadeFar"/> is above zero the executor multiplies the alpha by
    /// the fade at the quad's view depth. The smoke volume's 10..30.
    /// </summary>
    public float FadeNear;
    public float FadeFar;

    /// <summary>Sort by <see cref="SortKey"/> rather than by view depth (the smoke volume's constant 1).</summary>
    public bool FixedSortKey;
    public float SortKey;

    /// <summary>Sort by the view depth truncated to an integer (<c>CSimpleEmitter</c>'s <c>(int) tPos.z</c>).</summary>
    public bool TruncatedDepthSort;

    /// <summary>
    /// <see cref="ParticleQuadKind.Tracer"/>'s third and fourth view-space
    /// corners (<see cref="Position"/> and <see cref="Normal"/> are the first two).
    /// </summary>
    public Vector Corner2;
    public Vector Corner3;
}

/// <summary>Which of particle_util.h's quad builders draws a <see cref="ParticleQuad"/>.</summary>
public enum ParticleQuadKind
{
    /// <summary><c>RenderParticle_ColorSizeAngle</c>.</summary>
    ColorSizeAngle = 0,

    /// <summary>steamjet's <c>RenderParticle_ColorSizePerturbNormal</c> (the heatwave).</summary>
    ColorSizePerturbNormal = 1,

    /// <summary>
    /// <c>CParticleSphereRenderer::RenderParticle</c>'s quad,
    /// <c>RenderParticle_Color255SizeNormalAngle</c>: the colour is already lit
    /// (<see cref="SphereLighting"/>, 0..255) by the effect's ambient and
    /// directional lights, with
    /// <see cref="ParticleQuad.Alpha"/> on stock's 0..255 scale and
    /// <see cref="ParticleQuad.Roll"/> in radians. The smoke stack's.
    /// </summary>
    SphereLit = 2,

    /// <summary><c>RenderParticle_ColorSize</c>: unrotated. The spore trail's.</summary>
    ColorSize = 3,

    /// <summary>
    /// <c>Tracer_Draw</c>'s quad, already in view space: the four corners
    /// <see cref="TracerGeometry.ComputeVerts"/> made (<see cref="ParticleQuad.Position"/>,
    /// <see cref="ParticleQuad.Normal"/>, <see cref="ParticleQuad.Corner2"/>,
    /// <see cref="ParticleQuad.Corner3"/>), the colour and alpha as floats, sorted by
    /// <see cref="ParticleQuad.SortKey"/>; a <see cref="ParticleQuad.Size"/> of 0
    /// draws nothing (the tracer was clipped away). The sparks' trails.
    /// </summary>
    Tracer = 4,

    /// <summary>
    /// <c>RenderParticle_ColorSizeAngles</c>'s quad, in world space:
    /// <see cref="ParticleQuad.Position"/> the first corner,
    /// <see cref="ParticleQuad.Normal"/> the face's normal,
    /// <see cref="ParticleQuad.Corner2"/> and <see cref="ParticleQuad.Corner3"/>
    /// the width and height edges (each times the size), sorted by
    /// <see cref="ParticleQuad.SortKey"/>. CSimple3DEmitter's shards and flecks.
    /// </summary>
    Angles3D = 5,

    /// <summary>
    /// <c>RenderParticle_Color255Size</c> on a point ALREADY in view space
    /// (<see cref="ParticleQuad.Position"/>): the colour and alpha on stock's
    /// 0..255 scale, unrotated, sorted by <see cref="ParticleQuad.SortKey"/>.
    /// The dust motes' (<see cref="DustVolumeSim"/>), whose alpha and size
    /// depend on the view-space depth managed already took.
    /// </summary>
    ViewColor255Size = 6,
}
