//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Effects;

/// <summary><c>ptype_t</c> (c_te_particlesystem.h): how a temp entity particle moves and colours.</summary>
public enum TeParticleType : byte
{
    Static = 0,
    Grav = 1,
    SlowGrav = 2,
    Fire = 3,
    Explode = 4,
    Explode2 = 5,
    Blob = 6,
    Blob2 = 7,
    VoxSlowGrav = 8,
    VoxGrav = 9,
}

/// <summary>
/// <c>StandardParticle_t</c> as <c>CTEParticleRenderer</c> uses it: a countdown
/// lifetime, a <see cref="TeParticleType"/>, the colour ramp's 6.10 fixed-point
/// word, and RGBA bytes.
/// </summary>
public struct TeParticle
{
    public int Id;
    public Vector Position;
    public Vector Velocity;
    public float Lifetime;
    public TeParticleType Type;
    public ushort Word;
    public byte R, G, B, A;

    /// <summary><c>StandardParticle_t::SetColor</c>: each 0..1 channel times 255.9, truncated to its byte.</summary>
    public void SetColor(float r, float g, float b)
    {
        R = ToByte(r * 255.9f);
        G = ToByte(g * 255.9f);
        B = ToByte(b * 255.9f);
    }

    /// <summary><c>StandardParticle_t::SetAlpha</c>.</summary>
    public void SetAlpha(float a) => A = ToByte(a * 255.9f);

    /// <summary>
    /// The float-to-byte conversion stock's compiler emits: truncate to 32
    /// bits, keep the low byte. A blood stream's red is 247 times a 0.7..1
    /// draw before the 255.9, so its byte is that product modulo 256.
    /// </summary>
    public static byte ToByte(float f) => unchecked((byte)(int)f);
}

/// <summary>
/// <c>CTEParticleRenderer</c> (c_te_particlesystem.cpp, carried in
/// shell_te_particle_render.cpp until lane c5-particles) and the two legacy
/// temp entity effects that build one: <c>TE_BloodStream</c> and
/// <c>C_TEShowLine</c>'s line of dots. Replayed bit for bit against
/// <c>Fixtures/teparticles-stock.tsv.gz</c>.
/// </summary>
public static class TeParticles
{
    /// <summary><c>SIMSHIFT</c>: the ramp word's fixed-point shift.</summary>
    public const int SimShift = 10;

    /// <summary>Stock's <c>m_ParticleSize</c>.</summary>
    public const float ParticleSize = 1.5f;

    public const string Material = "particle/particledefault";

    private static ReadOnlySpan<byte> Ramp1 => [255, 243, 227, 223, 171, 39, 191, 119, 47, 127, 59, 43, 99, 47, 31, 75, 35, 19, 47, 23, 11, 175, 103, 35];

    private static ReadOnlySpan<byte> Ramp2 => [255, 243, 227, 239, 203, 31, 223, 171, 39, 207, 143, 43, 191, 119, 47, 175, 99, 47, 143, 67, 51, 115, 55, 35];

    private static ReadOnlySpan<byte> Ramp3 => [223, 171, 39, 191, 119, 47, 91, 91, 91, 75, 75, 75, 63, 63, 63, 47, 47, 47];

    /// <summary><c>gSparkRamp</c>, <c>SPARK_COLORCOUNT</c> rows.</summary>
    private static ReadOnlySpan<byte> SparkRamp => [255, 255, 255, 255, 247, 199, 255, 243, 147, 255, 243, 27, 239, 203, 31, 223, 171, 39, 207, 143, 43, 127, 59, 43, 35, 19, 7];

    public const int SparkColorCount = 9;

    private static void RampColor(ref TeParticle p, ReadOnlySpan<byte> ramp, int row)
        => p.SetColor(ramp[row * 3] / 255.0f, ramp[row * 3 + 1] / 255.0f, ramp[row * 3 + 2] / 255.0f);

    /// <summary>
    /// <c>CTEParticleRenderer::SimulateParticles</c>, one particle: the
    /// lifetime counts down and a particle under zero dies; the rest move by
    /// their type and then by their velocity. False when it died.
    /// </summary>
    public static bool Step(ref TeParticle p, float dt, float gravity, IParticleRandom random)
    {
        p.Lifetime = p.Lifetime - dt;
        if (p.Lifetime < 0)
            return false;

        float ft = dt;
        float time3 = (float)(15.0 * ft);
        float time2 = (float)(10.0 * ft);
        float time1 = (float)(5.0 * ft);
        float dvel = 4 * ft;
        float grav = ft * gravity * 0.05f;
        int ramp;

        switch (p.Type)
        {
            case TeParticleType.Static:
                break;

            case TeParticleType.Fire:
                p.Word = unchecked((ushort)(p.Word + (ushort)(int)(time1 * (1 << SimShift))));
                ramp = p.Word >> SimShift;
                if (ramp >= 6)
                    p.Lifetime = -1;
                else
                    RampColor(ref p, Ramp3, ramp);
                p.Velocity.Z += grav;
                break;

            case TeParticleType.Explode:
                p.Word = unchecked((ushort)(p.Word + (ushort)(int)(time2 * (1 << SimShift))));
                ramp = p.Word >> SimShift;
                if (ramp >= 8)
                    p.Lifetime = -1;
                else
                    RampColor(ref p, Ramp1, ramp);
                p.Velocity = p.Velocity + p.Velocity * dvel;
                p.Velocity.Z -= grav;
                break;

            case TeParticleType.Explode2:
                p.Word = unchecked((ushort)(p.Word + (ushort)(int)(time3 * (1 << SimShift))));
                ramp = p.Word >> SimShift;
                if (ramp >= 8)
                    p.Lifetime = -1;
                else
                    RampColor(ref p, Ramp2, ramp);
                p.Velocity = p.Velocity - p.Velocity * ft;
                p.Velocity.Z -= grav;
                break;

            case TeParticleType.Grav:
                p.Velocity.Z -= grav * 20;
                break;

            case TeParticleType.SlowGrav:
                // Stock assigns rather than subtracts here, and it is kept.
                p.Velocity.Z = grav;
                break;

            case TeParticleType.VoxGrav:
                p.Velocity.Z -= grav * 8;
                break;

            case TeParticleType.VoxSlowGrav:
                p.Velocity.Z -= grav * 4;
                break;

            case TeParticleType.Blob:
            case TeParticleType.Blob2:
                p.Word = unchecked((ushort)(p.Word + (ushort)(int)(time2 * (1 << SimShift))));
                ramp = p.Word >> SimShift;
                if (ramp >= SparkColorCount)
                {
                    p.Word = 0;
                    ramp = 0;
                }

                RampColor(ref p, SparkRamp, ramp);
                p.Velocity.X = (float)(p.Velocity.X - p.Velocity.X * 0.5 * ft);
                p.Velocity.Y = (float)(p.Velocity.Y - p.Velocity.Y * 0.5 * ft);
                p.Velocity.Z -= grav * 5;
                if (random.RandomInt(0, 3) != 0)
                {
                    p.Type = TeParticleType.Blob;
                    p.SetAlpha(0);
                }
                else
                {
                    p.Type = TeParticleType.Blob2;
                    p.SetAlpha(255.9f);
                }
                break;
        }

        p.Position = p.Position + p.Velocity * ft;
        return true;
    }

    /// <summary>
    /// <c>CTEParticleRenderer::RenderParticles</c>, one particle: unrotated,
    /// <see cref="ParticleSize"/> wide, its colour and alpha over 255.9, sorted
    /// by its view-space depth (the executor's own transform).
    /// </summary>
    public static ParticleQuadRaw Quad(in TeParticle p) => new()
    {
        Id = p.Id,
        Kind = (int)ParticleQuadKind.ColorSize,
        Position = p.Position,
        Color = new Vector(p.R / 255.9f, p.G / 255.9f, p.B / 255.9f),
        Alpha = p.A / 255.9f,
        Size = ParticleSize,
    };

    /// <summary>
    /// <c>TE_BloodStream</c>: 100 falling drops down a drooping arc, then
    /// <c>amount / 5</c> slower rogue drops each with two chunky neighbours.
    /// </summary>
    public static List<TeParticle> BloodStream(Vector origin, Vector direction, int r, int g, int b, int a, int amount,
                                               IParticleRandom random)
    {
        var made = new List<TeParticle>();
        float arc = (float)0.05;
        float speedCopy = amount;
        Vector dir = direction;
        FxMath.Normalize(ref dir);

        for (int count = 0; count < 100; count++)
        {
            var p = new TeParticle();
            p.SetColor(r * random.RandomFloat(0.7f, 1.0f), g, b);
            p.SetAlpha(a);
            p.Position = origin;
            p.Lifetime = 2;
            p.Type = TeParticleType.VoxGrav;

            Vector dirCopy = dir;
            dirCopy.Z -= arc;
            arc = (float)(arc - 0.005);

            p.Velocity = new Vector(dirCopy.X * speedCopy, dirCopy.Y * speedCopy, dirCopy.Z * speedCopy);
            speedCopy = (float)(speedCopy - 0.00001);
            made.Add(p);
        }

        arc = (float)0.075;
        for (int count = 0; count < amount / 5; count++)
        {
            var p = new TeParticle { Lifetime = 3 };
            p.SetColor(r * random.RandomFloat(0.7f, 1.0f), g, b);
            p.SetAlpha(a);
            p.Position = origin;
            p.Type = TeParticleType.VoxSlowGrav;

            Vector dirCopy = dir;
            dirCopy.Z -= arc;
            arc = (float)(arc - 0.005);

            float num = random.RandomFloat(0, 1);
            speedCopy = amount * num;
            num = (float)(num * 1.7);
            dirCopy = new Vector(dirCopy.X * num, dirCopy.Y * num, dirCopy.Z * num);
            p.Velocity = dirCopy * speedCopy;
            made.Add(p);

            for (int count2 = 0; count2 < 2; count2++)
            {
                var chunky = new TeParticle { Lifetime = 3 };
                chunky.SetColor(random.RandomFloat(0.7f, 1.0f), g, b);
                chunky.SetAlpha(a);
                float x = origin.X + random.RandomFloat(-1, 1);
                float y = origin.Y + random.RandomFloat(-1, 1);
                float z = origin.Z + random.RandomFloat(-1, 1);
                chunky.Position = new Vector(x, y, z);
                chunky.Type = TeParticleType.VoxSlowGrav;

                Vector chunkyDir = dir;
                chunkyDir.Z -= arc;
                chunkyDir = new Vector(chunkyDir.X * num, chunkyDir.Y * num, chunkyDir.Z * num);
                chunky.Velocity = chunkyDir * speedCopy;
                made.Add(chunky);
            }
        }

        return made;
    }

    /// <summary>
    /// <c>C_TEShowLine::PostDataUpdate</c>: a cyan dot every three units from
    /// the start towards the end, each still and lasting thirty seconds.
    /// </summary>
    public static List<TeParticle> ShowLine(Vector start, Vector end)
    {
        var made = new List<TeParticle>();
        Vector vec = end - start;
        float len = FxMath.Normalize(ref vec);
        const int dec = 3;
        vec = new Vector(vec.X * dec, vec.Y * dec, vec.Z * dec);
        Vector at = start;

        while (len > 0)
        {
            len -= dec;
            var p = new TeParticle { Velocity = default, Lifetime = 30, Type = TeParticleType.Static, Position = at };
            p.SetColor(0, 1, 1);
            p.SetAlpha(1);
            at = at + vec;
            made.Add(p);
        }

        return made;
    }
}

/// <summary>
/// One <c>CTEParticleRenderer</c> on the particle executor: the particles an
/// effect made, stepped by <see cref="TeParticles.Step"/> at the engine's
/// gravity and drawn by <see cref="TeParticles.Quad"/>. It finishes when the
/// last one dies, as stock's dynamically allocated renderer deleted itself.
/// </summary>
public sealed class TeParticleSheet(Func<float> gravity, IParticleRandom random, Action<int>? drawn = null) : IClientParticleEffect
{
    private readonly List<TeParticle> _pending = new();
    private readonly List<TeParticle> _live = new();
    private int _nextId = 1;

    public string Material => TeParticles.Material;

    public string ReloadKind => "te-particles";

    public int EffectId { get; set; }

    public IReadOnlyList<TeParticle> Live => _live;

    public bool Finished => EffectId != 0 && _live.Count == 0 && _pending.Count == 0;

    public void Add(TeParticle p)
    {
        p.Id = _nextId++;
        _pending.Add(p);
    }

    public int Emit(ref ParticleEmit emit, Span<int> idsOut)
    {
        int n = Math.Min(_pending.Count, idsOut.Length);
        for (int i = 0; i < n; i++)
        {
            _live.Add(_pending[i]);
            idsOut[i] = _pending[i].Id;
        }
        _pending.RemoveRange(0, n);
        TrailEmitter.SetBounds(ref emit, null, _live.Select(p => p.Position));
        return n;
    }

    public int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        float g = gravity();
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            TeParticle p = _live[i];
            if (TeParticles.Step(ref p, dt, g, random))
                _live[i] = p;
            else
                _live.RemoveAt(i);
        }
        for (int i = 0; i < ids.Length; i++)
        {
            int id = ids[i];
            aliveOut[i] = _live.Exists(p => p.Id == id) ? (byte)1 : (byte)0;
        }
        return 1;
    }

    public int Draw(Span<ParticleQuadRaw> quadsOut)
    {
        if (_live.Count > quadsOut.Length)
            return _live.Count;
        int n = 0;
        foreach (var p in _live.OrderByDescending(p => p.Id))
            quadsOut[n++] = TeParticles.Quad(p);
        drawn?.Invoke(n);
        return n;
    }

    public void Save(BinaryWriter writer) { }

    public void Restore(BinaryReader reader) { }
}

/// <summary>The blood stream and the debug line temp entities, started (TE_BloodStream, C_TEShowLine).</summary>
public interface IClientTeParticles
{
    /// <summary>The particles started.</summary>
    int BloodStream(Vector origin, Vector direction, int r, int g, int b, int a, int amount);

    /// <summary>The dots started.</summary>
    int ShowLine(Vector start, Vector end);
}

/// <summary>
/// <see cref="TeParticles"/> started on the executor: one
/// <see cref="TeParticleSheet"/> per effect at its origin, stepped at the
/// engine's current gravity.
/// </summary>
public sealed class ClientTeParticles(ClientParticleEffects effects, IEngineEffects engine, IRandomSeeds seeds) : IClientTeParticles
{
    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());

    /// <summary>Particles started since load.</summary>
    public int Particles { get; private set; }

    /// <summary>Quads handed to the executor since load (cs_cl_fx_report): a stream that draws moves this.</summary>
    public long QuadsDrawn { get; private set; }

    public int BloodStream(Vector origin, Vector direction, int r, int g, int b, int a, int amount)
        => Start(origin, TeParticles.BloodStream(origin, direction, r, g, b, a, amount, _random));

    public int ShowLine(Vector start, Vector end) => Start(start, TeParticles.ShowLine(start, end));

    private int Start(Vector origin, List<TeParticle> made)
    {
        var sheet = new TeParticleSheet(engine.CurrentGravity, _random, n => QuadsDrawn += n);
        foreach (var p in made)
            sheet.Add(p);
        sheet.EffectId = effects.Start(sheet, origin);
        Particles += made.Count;
        return made.Count;
    }
}
