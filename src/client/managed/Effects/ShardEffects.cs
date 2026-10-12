//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.TempEnts;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The shards (lane c5-particles): <c>FX_GlassImpact</c> (fx_impact.cpp, the
/// GlassImpact client effect: spinning glass on Simple3D and the dust behind
/// it) and <c>C_TEShatterSurface::PostDataUpdate</c> (a pane of glass or tile
/// coming apart, a grid of shards on Simple3D). Replayed against
/// <c>Fixtures/shards-stock.tsv.gz</c>.
/// </summary>
public sealed class ShardEffects(ClientParticleEffects effects, ParticleViews views, IParticlePixelVisibility visibility,
                                 IParticleWorldTrace world, IClientComputeLighting lighting, IEngineHost host, IRandomSeeds seeds,
                                 IFxRoutineEnvironment sounds)
    : IClientShatter
{
    // c_te_glassshatter.cpp's own five.
    private const float ShatterMinLife = 2, ShatterMaxLife = 5, ShatterNoise = 0.3f, ShatterGravity = 500, ShatterDamping = 0.3f;

    /// <summary>The shard ceiling the decode applied (shell_te_backend_args.h's kShatterMaxShards).</summary>
    public const int MaxShards = 4096;

    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());

    public int Impacts { get; private set; }

    public int Shatters { get; private set; }

    /// <summary>What one call made: the shards, the dust sheets, and the collision it set up.</summary>
    public sealed record Made(Simple3DSystem Shards, List<SimpleSheet> Dust, Vector Direction, float Spread, float MinSpeed,
                              float MaxSpeed, float Gravity, float Dampen);

    private static byte UChar(float f) => unchecked((byte)(int)f);

    /// <summary><c>ImpactRandF</c>: a range with equal ends is a fixed value and draws nothing.</summary>
    private static float RandF(IParticleRandom random, float lo, float hi) => lo == hi ? lo : random.RandomFloat(lo, hi);

    /// <summary><c>ImpactRandI</c>: the ends truncated, equal ends draw nothing.</summary>
    private static int RandI(IParticleRandom random, float lo, float hi)
    {
        int nLo = (int)lo, nHi = (int)hi;
        return nLo == nHi ? nLo : random.RandomInt(nLo, nHi);
    }

    /// <summary><c>VectorLerp( v, (1,1,1), t )</c>: "blend a little toward white to match the materials".</summary>
    private static Vector TowardWhite(Vector v, float t)
        => new(v.X + (1 - v.X) * t, v.Y + (1 - v.Y) * t, v.Z + (1 - v.Z) * t);

    /// <summary>The impact material slot's sheet (ImpactMaterialPair's first).</summary>
    public static string Sheet(ImpactMaterial slot) => slot switch
    {
        ImpactMaterial.DustPuff2 => "particle/particle_noisesphere",
        ImpactMaterial.BloodPuff => "effects/blood",
        ImpactMaterial.BloodPuff2 => "effects/blood2",
        _ => FleckSystems.Sheets(slot).Item1,
    };

    public Made? GlassImpact(Vector pos, Vector normal) => GlassImpact(pos, normal, _random);

    /// <summary><c>FX_GlassImpact</c>, its numbers ImpactEffectPolicy's Glass block.</summary>
    public Made? GlassImpact(Vector pos, Vector normal, IParticleRandom random)
    {
        float[] p = new float[ImpactEffectPolicy.MaxFloats];
        if (ImpactEffectPolicy.Build(ImpactEffect.Glass, 0, 1.0f, false, p) < 0)
            return null;
        Impacts++;
        float H(ImpactHeader f) => p[(int)f];
        int bursts = (int)H(ImpactHeader.Bursts);
        float B(int n, ImpactBurst f) => p[ImpactEffectPolicy.BurstAt(n) + (int)f];

        var shards = new Simple3DSystem(effects, views, world, random, pos);
        Vector light = TowardWhite(lighting.ComputeLighting(pos, []), H(ImpactHeader.HeadA));
        float shardSize = RandF(random, B(0, ImpactBurst.SizeMin), B(0, ImpactBurst.SizeMax));
        byte tr = UChar(B(0, ImpactBurst.TintR)), tg = UChar(B(0, ImpactBurst.TintG)), tb = UChar(B(0, ImpactBurst.TintB));
        (string first, string second) = FleckSystems.Sheets((ImpactMaterial)(int)B(0, ImpactBurst.Material));

        int numShards = RandI(random, B(0, ImpactBurst.CountMin), B(0, ImpactBurst.CountMax));
        for (int i = 0; i < numShards; i++)
        {
            string material = random.RandomInt(0, 1) == 0 ? first : second;
            var s = new Particle3D { Position = pos };
            s.LifeRemaining = RandF(random, B(0, ImpactBurst.LifeMin), B(0, ImpactBurst.LifeMax));
            // Per axis, which is what makes glass scatter rather than cone.
            float vx = (normal.X + RandF(random, B(0, ImpactBurst.SprayMin), B(0, ImpactBurst.SprayMax)))
                       * RandF(random, B(0, ImpactBurst.SpeedMin), B(0, ImpactBurst.SpeedMax));
            float vy = (normal.Y + RandF(random, B(0, ImpactBurst.SprayMin), B(0, ImpactBurst.SprayMax)))
                       * RandF(random, B(0, ImpactBurst.SpeedMin), B(0, ImpactBurst.SpeedMax));
            float vz = (normal.Z + RandF(random, B(0, ImpactBurst.SprayMin), B(0, ImpactBurst.SprayMax)))
                       * RandF(random, B(0, ImpactBurst.SpeedMin), B(0, ImpactBurst.SpeedMax));
            s.Velocity = new Vector(vx, vy, vz);
            float wobble = B(0, ImpactBurst.ExtraA) * shardSize;
            s.Size = UChar(shardSize + random.RandomFloat(-wobble, wobble));
            // RandomAngle: Vector::Random's three components.
            float ax = random.RandomFloat(B(0, ImpactBurst.RollMin), B(0, ImpactBurst.RollMax));
            float ay = random.RandomFloat(B(0, ImpactBurst.RollMin), B(0, ImpactBurst.RollMax));
            float az = random.RandomFloat(B(0, ImpactBurst.RollMin), B(0, ImpactBurst.RollMax));
            s.Angles = new Vector(ax, ay, az);
            s.AngSpeed = RandF(random, B(0, ImpactBurst.RollDeltaMin), B(0, ImpactBurst.RollDeltaMax));
            s.FrontR = s.BackR = UChar(tr * light.X);
            s.FrontG = s.BackG = UChar(tg * light.Y);
            s.FrontB = s.BackB = UChar(tb * light.Z);
            shards.Add(material, s);
        }

        float spread = H(ImpactHeader.AngularSpray), min = H(ImpactHeader.CollMin), max = H(ImpactHeader.CollMax);
        float gravity = H(ImpactHeader.Gravity), dampen = H(ImpactHeader.Dampen);
        shards.Collision.Setup(pos, normal, spread, min, max, gravity, dampen, world);
        shards.Start();

        // The dust: what the frame was bedded in.
        var dust = new List<SimpleSheet>();
        Vector offset = pos + normal * H(ImpactHeader.SpawnOffset);
        for (int nB = 1; nB < bursts; nB++)
        {
            int count = RandI(random, B(nB, ImpactBurst.CountMin), B(nB, ImpactBurst.CountMax));
            bool indexScale = B(nB, ImpactBurst.ExtraA) != 0.0f;
            bool assignZ = B(nB, ImpactBurst.ExtraB) != 0.0f;
            string material = Sheet((ImpactMaterial)(int)B(nB, ImpactBurst.Material));
            SimpleSheet? sheet = dust.FirstOrDefault(x => x.Material == material);
            if (sheet is null)
            {
                sheet = new SimpleSheet(material, random, views, visibility, () => host.CurrentTime);
                dust.Add(sheet);
            }
            for (int i = 0; i < count; i++)
            {
                float step = indexScale ? i + 1 : 1.0f;
                var d = new SimpleParticle { Position = offset, Lifetime = 0.0f };
                d.DieTime = RandF(random, B(nB, ImpactBurst.LifeMin), B(nB, ImpactBurst.LifeMax));
                float dx = normal.X + RandF(random, B(nB, ImpactBurst.SprayMin), B(nB, ImpactBurst.SprayMax));
                float dy = normal.Y + RandF(random, B(nB, ImpactBurst.SprayMin), B(nB, ImpactBurst.SprayMax));
                float dz = normal.Z + RandF(random, B(nB, ImpactBurst.SprayMin), B(nB, ImpactBurst.SprayMax));
                d.StartSize = unchecked((byte)RandI(random, B(nB, ImpactBurst.SizeMin), B(nB, ImpactBurst.SizeMax)));
                d.EndSize = UChar(d.StartSize * B(nB, ImpactBurst.SizeEndMul));
                d.Velocity = new Vector(dx, dy, dz) * RandF(random, B(nB, ImpactBurst.SpeedMin), B(nB, ImpactBurst.SpeedMax)) * step;
                float z = RandF(random, B(nB, ImpactBurst.ZMin), B(nB, ImpactBurst.ZMax));
                if (assignZ)
                    d.Velocity.Z = z;
                else
                    d.Velocity.Z -= z * step;
                d.StartAlpha = unchecked((byte)RandI(random, B(nB, ImpactBurst.AlphaMin), B(nB, ImpactBurst.AlphaMax)));
                d.EndAlpha = 0;
                d.Roll = RandF(random, B(nB, ImpactBurst.RollMin), B(nB, ImpactBurst.RollMax));
                d.RollDelta = RandF(random, B(nB, ImpactBurst.RollDeltaMin), B(nB, ImpactBurst.RollDeltaMax));
                float ramp = RandF(random, B(nB, ImpactBurst.RampMin), B(nB, ImpactBurst.RampMax));
                d.R = UChar(MathF.Min(1.0f, B(nB, ImpactBurst.TintR) * ramp) * 255.0f);
                d.G = UChar(MathF.Min(1.0f, B(nB, ImpactBurst.TintG) * ramp) * 255.0f);
                d.B = UChar(MathF.Min(1.0f, B(nB, ImpactBurst.TintB) * ramp) * 255.0f);
                sheet.Add(d);
            }
        }
        foreach (var sheet in dust)
            sheet.EffectId = effects.Start(sheet, offset);
        return new Made(shards, dust, normal, spread, min, max, gravity, dampen);
    }

    void IClientShatter.ShatterSurface(Vector origin, Vector angles, Vector force, Vector forcePos, float width, float height,
                                       float shardSize, int surfaceType, Color32 front, Color32 back)
        => ShatterSurface(origin, angles, force, forcePos, width, height, shardSize, surfaceType, front, back, _random);

    /// <summary>
    /// <c>C_TEShatterSurface::PostDataUpdate</c>, with the decode's refusals
    /// (no shard size, no grid, more than <see cref="MaxShards"/> shards).
    /// </summary>
    /// <summary>
    /// <c>FX_AntlionImpact</c> (c_impact_effects.cpp): chitin flecks sprayed back
    /// along the shot from a point two units off the shell, warm dust behind them,
    /// and the shell-impact sound, its numbers ImpactEffectPolicy's Antlion block.
    /// STOCK GATES IT ON THE HIT ENTITY, reading its bounding radius
    /// (<c>CollisionProp()-&gt;BoundingRadius()</c>) and returning without one.
    /// This ABI carries no entity bounds, so no impact passes
    /// <paramref name="boundingRadius"/> and the effect is not live (docs/todo.md);
    /// the body is the port, kept, with stock-derived facts on it.
    /// </summary>
    public Made? AntlionImpact(Vector start, Vector end, float? boundingRadius, IParticleRandom random)
    {
        if (boundingRadius is null)
            return null;

        float[] p = new float[ImpactEffectPolicy.MaxFloats];
        if (ImpactEffectPolicy.Build(ImpactEffect.Antlion, 0, 1.0f, false, p) < 0)
            return null;
        Impacts++;
        float H(ImpactHeader f) => p[(int)f];
        float B(int n, ImpactBurst f) => p[ImpactEffectPolicy.BurstAt(n) + (int)f];

        Vector shotDir = start - end;
        FxMath.Normalize(ref shotDir);
        Vector spawnOffset = end + shotDir * H(ImpactHeader.SpawnOffset);

        var flecks = new Simple3DSystem(effects, views, world, random, spawnOffset);
        float spread = H(ImpactHeader.AngularSpray), min = H(ImpactHeader.CollMin), max = H(ImpactHeader.CollMax);
        float gravity = H(ImpactHeader.Gravity), dampen = H(ImpactHeader.Dampen);
        flecks.Collision.Setup(spawnOffset, shotDir, spread, min, max, gravity, dampen, world);

        (string first, string second) = FleckSystems.Sheets((ImpactMaterial)(int)B(0, ImpactBurst.Material));
        byte front = UChar(B(0, ImpactBurst.ExtraA)), back = UChar(B(0, ImpactBurst.ExtraB));
        int numFlecks = RandI(random, B(0, ImpactBurst.CountMin), B(0, ImpactBurst.CountMax));
        for (int i = 0; i < numFlecks; i++)
        {
            string material = random.RandomInt(0, 1) == 0 ? first : second;
            var f = new Particle3D { Position = spawnOffset };
            f.LifeRemaining = RandF(random, B(0, ImpactBurst.LifeMin), B(0, ImpactBurst.LifeMax));
            float dx = shotDir.X + RandF(random, B(0, ImpactBurst.SprayMin), B(0, ImpactBurst.SprayMax));
            float dy = shotDir.Y + RandF(random, B(0, ImpactBurst.SprayMin), B(0, ImpactBurst.SprayMax));
            float dz = shotDir.Z + RandF(random, B(0, ImpactBurst.SprayMin), B(0, ImpactBurst.SprayMax));
            f.Size = unchecked((byte)RandI(random, B(0, ImpactBurst.SizeMin), B(0, ImpactBurst.SizeMax)));
            f.Velocity = new Vector(dx, dy, dz) * RandF(random, B(0, ImpactBurst.SpeedMin), B(0, ImpactBurst.SpeedMax));
            // m_vAngles.Random: Vector::Random's three components.
            float ax = random.RandomFloat(B(0, ImpactBurst.RollMin), B(0, ImpactBurst.RollMax));
            float ay = random.RandomFloat(B(0, ImpactBurst.RollMin), B(0, ImpactBurst.RollMax));
            float az = random.RandomFloat(B(0, ImpactBurst.RollMin), B(0, ImpactBurst.RollMax));
            f.Angles = new Vector(ax, ay, az);
            f.AngSpeed = RandF(random, B(0, ImpactBurst.RollDeltaMin), B(0, ImpactBurst.RollDeltaMax));
            f.FrontR = f.FrontG = f.FrontB = front;
            f.BackR = f.BackG = f.BackB = back;
            flecks.Add(material, f);
        }
        flecks.Start();

        // The dust trail, warm: its burst carries the colour a shell's surface would have given.
        Vector offset = end + shotDir * H(ImpactHeader.HeadB);
        var sheet = new SimpleSheet(Sheet((ImpactMaterial)(int)B(1, ImpactBurst.Material)), random, views, visibility, () => host.CurrentTime);
        int nDust = RandI(random, B(1, ImpactBurst.CountMin), B(1, ImpactBurst.CountMax));
        for (int i = 0; i < nDust; i++)
        {
            var d = new SimpleParticle { Position = offset, Lifetime = 0.0f };
            d.DieTime = RandF(random, B(1, ImpactBurst.LifeMin), B(1, ImpactBurst.LifeMax));
            float dx = shotDir.X + RandF(random, B(1, ImpactBurst.SprayMin), B(1, ImpactBurst.SprayMax));
            float dy = shotDir.Y + RandF(random, B(1, ImpactBurst.SprayMin), B(1, ImpactBurst.SprayMax));
            float dz = shotDir.Z + RandF(random, B(1, ImpactBurst.SprayMin), B(1, ImpactBurst.SprayMax));
            d.StartSize = unchecked((byte)RandI(random, B(1, ImpactBurst.SizeMin), B(1, ImpactBurst.SizeMax)));
            d.EndSize = UChar(d.StartSize * B(1, ImpactBurst.SizeEndMul));
            d.Velocity = new Vector(dx, dy, dz) * RandF(random, B(1, ImpactBurst.SpeedMin), B(1, ImpactBurst.SpeedMax));
            d.StartAlpha = unchecked((byte)RandI(random, B(1, ImpactBurst.AlphaMin), B(1, ImpactBurst.AlphaMax)));
            d.EndAlpha = 0;
            d.Roll = RandF(random, B(1, ImpactBurst.RollMin), B(1, ImpactBurst.RollMax));
            d.RollDelta = RandF(random, B(1, ImpactBurst.RollDeltaMin), B(1, ImpactBurst.RollDeltaMax));
            float ramp = RandF(random, B(1, ImpactBurst.RampMin), B(1, ImpactBurst.RampMax));
            d.R = UChar(MathF.Min(1.0f, B(1, ImpactBurst.TintR) * ramp) * 255.0f);
            d.G = UChar(MathF.Min(1.0f, B(1, ImpactBurst.TintG) * ramp) * 255.0f);
            d.B = UChar(MathF.Min(1.0f, B(1, ImpactBurst.TintB) * ramp) * 255.0f);
            sheet.Add(d);
        }
        sheet.EffectId = effects.Start(sheet, offset);

        sounds.Sound("FX_AntlionImpact.ShellImpact", end);
        return new Made(flecks, [sheet], shotDir, spread, min, max, gravity, dampen);
    }

    public Made? ShatterSurface(Vector origin, Vector angles, Vector force, Vector forcePos, float width, float height, float shardSize,
                                int surfaceType, Color32 front, Color32 back, IParticleRandom random)
    {
        if (shardSize <= 0.0f)
            return null;
        int wide = (int)(width / shardSize), high = (int)(height / shardSize);
        if (wide <= 0 || high <= 0 || (long)wide * high > MaxShards)
            return null;
        Shatters++;

        var shards = new Simple3DSystem(effects, views, world, random, origin);
        Vector light = TowardWhite(lighting.ComputeLighting(origin, []), 0.3f);
        (string first, string second) = surfaceType == 0
            ? ("effects/fleck_glass1", "effects/fleck_glass2")
            : ("effects/fleck_tile1", "effects/fleck_tile2");

        MoveVec.AngleVectors(angles, out _, out Vector widthStep, out Vector heightStep);
        widthStep = widthStep * shardSize;
        heightStep = heightStep * shardSize;

        // 0.5 is stock's double: the half shard is added in double and rounded once.
        Vector cur = origin;
        cur.X = (float)(cur.X + 0.5 * shardSize);
        cur.Z = (float)(cur.Z + 0.5 * shardSize);
        float halfShard = (float)(0.5 * shardSize);

        float minSpeed = 9999999999.0f, maxSpeed = 0;
        for (int w = 0; w < wide; w++)
        {
            for (int h = 0; h < high; h++)
            {
                string material = random.RandomInt(0, 1) == 0 ? first : second;
                Vector forceVel = default;
                if (random.RandomInt(0, 3) != 0)
                {
                    Vector d = cur - forcePos;
                    float distSqr = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                    forceVel = force;
                    if (distSqr > 0)
                        forceVel = forceVel * (40.0f / distSqr);
                }

                var s = new Particle3D { Position = cur };
                s.LifeRemaining = random.RandomFloat(ShatterMinLife, ShatterMaxLife);
                float rx = random.RandomFloat(-25, 25), ry = random.RandomFloat(-25, 25), rz = random.RandomFloat(-25, 25);
                s.Velocity = forceVel + new Vector(rx, ry, rz);
                s.Size = UChar(shardSize + random.RandomFloat(-halfShard, halfShard));
                s.Angles = angles;
                s.AngSpeed = random.RandomFloat(-400, 400);
                s.FrontR = UChar(front.R * light.X);
                s.FrontG = UChar(front.G * light.Y);
                s.FrontB = UChar(front.B * light.Z);
                s.BackR = UChar(back.R * light.X);
                s.BackG = UChar(back.G * light.Y);
                s.BackB = UChar(back.B * light.Z);
                shards.Add(material, s);

                float speed = MathF.Sqrt(forceVel.X * forceVel.X + forceVel.Y * forceVel.Y + forceVel.Z * forceVel.Z);
                if (speed > maxSpeed)
                    maxSpeed = speed;
                if (speed < minSpeed)
                    minSpeed = speed;
                cur = cur + heightStep;
            }
            cur = cur - heightStep * high;
            cur = cur + widthStep;
        }

        Vector dir = force;
        FxMath.Normalize(ref dir);
        shards.Collision.Setup(origin, dir, ShatterNoise, minSpeed, maxSpeed, ShatterGravity, ShatterDamping, world);
        shards.Start();
        return new Made(shards, [], dir, ShatterNoise, minSpeed, maxSpeed, ShatterGravity, ShatterDamping);
    }
}

/// <summary>The <c>GlassImpact</c> client effect (fx_impact.cpp's GlassImpactCallback).</summary>
public sealed class GlassImpactEffect(ShardEffects shards) : IClientEffectHandler
{
    public string EffectName => "GlassImpact";

    public void Dispatch(in EffectData data) => shards.GlassImpact(data.Origin, data.Normal);
}
