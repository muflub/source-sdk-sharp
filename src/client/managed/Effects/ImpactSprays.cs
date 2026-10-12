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
/// <c>CDustParticle::UpdateVelocity</c>'s two statics (c_impact_effects.h),
/// shared by every dust emitter as stock's were: the decay is recomputed when
/// the step's dt changes, from the NEW dt, and starts at zero.
/// </summary>
public sealed class DustDecay
{
    public float DTime;
    public float Decay;

    public float For(float dt)
    {
        if (DTime != dt)
        {
            DTime = dt;
            const float expected = 0.5f;
            Decay = MathF.Exp(MathF.Log(0.0001f) * DTime / expected);
        }
        return Decay;
    }
}

/// <summary>
/// <c>CDustParticle</c>'s rules (c_impact_effects.h): a roll that slows by
/// e^-8 a second but never below 0.5, a velocity decayed by the shared
/// <see cref="DustDecay"/> that never drops below 32 units a second (the floor
/// keeps the direction), and an alpha that ramps down from 1, squared once it
/// is below 0.75.
/// </summary>
public sealed class DustParticleRules(DustDecay decay) : SimpleEmitterRules
{
    public override float UpdateRoll(ref SimpleParticle p, float dt)
    {
        p.Roll += p.RollDelta * dt;
        p.RollDelta += p.RollDelta * (dt * -8.0f);
        if (MathF.Abs(p.RollDelta) < 0.5f)
            p.RollDelta = p.RollDelta > 0.0f ? 0.5f : -0.5f;
        return p.Roll;
    }

    public override void UpdateVelocity(ref SimpleParticle p, float dt, IParticleRandom random)
    {
        Vector save = p.Velocity;
        float d = decay.For(dt);
        p.Velocity = p.Velocity * d;
        if (p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y + p.Velocity.Z * p.Velocity.Z < 32.0f * 32.0f)
        {
            FxMath.Normalize(ref save);
            p.Velocity = save * 32.0f;
        }
    }

    public override float UpdateAlpha(in SimpleParticle p)
    {
        float ramp = 1.0f - p.Lifetime / p.DieTime;
        if (ramp < 0.75f)
            ramp *= ramp;
        return ramp;
    }
}

/// <summary>The blood sprite temp entity's puff (<c>FX_Blood</c>).</summary>
public interface IImpactBlood
{
    /// <summary>The particles started.</summary>
    int Blood(Vector pos, Vector dir, float r, float g, float b, float a);
}

/// <summary>
/// The impact sprays (lane c5-particles), shell_impact_effects.cpp's simple
/// sheets: <c>FX_DebrisFlecks</c>' dust trail, puffs and capper behind the
/// flecks, <c>FX_DustImpact</c> on dirt and sand, <c>FX_Blood</c> (the blood
/// sprite temp entity's puff) and <c>FX_BugBlood</c>, each from its
/// ImpactEffectPolicy block. One sheet per material, as the glass dust is.
/// Replayed against <c>Fixtures/sprays-stock.tsv.gz</c>.
/// </summary>
public sealed class ImpactSprays(ClientParticleEffects effects, ParticleViews views, IParticlePixelVisibility visibility,
                                 FleckSystems flecks, IEngineHost host, IRandomSeeds seeds)
    : IClientImpactSprays, IImpactBlood
{
    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());
    private readonly DustDecay _dustDecay = new();

    /// <summary>What one call made: its sheets (each burst's material in first-use order) and the flecks it started.</summary>
    public sealed record Made(List<SimpleSheet> Sheets, int Flecks);

    private static byte UChar(float f) => unchecked((byte)(int)f);

    private static float RandF(IParticleRandom random, float lo, float hi) => lo == hi ? lo : random.RandomFloat(lo, hi);

    private static int RandI(IParticleRandom random, float lo, float hi)
    {
        int nLo = (int)lo, nHi = (int)hi;
        return nLo == nHi ? nLo : random.RandomInt(nLo, nHi);
    }

    /// <summary><c>Vector::Random</c>: three draws, x then y then z.</summary>
    private static Vector RandomVector(IParticleRandom random, float lo, float hi)
    {
        float x = random.RandomFloat(lo, hi);
        float y = random.RandomFloat(lo, hi);
        float z = random.RandomFloat(lo, hi);
        return new Vector(x, y, z);
    }

    private SimpleSheet SheetFor(List<SimpleSheet> sheets, ImpactMaterial slot, IParticleRandom random, SimpleEmitterRules? rules)
    {
        string material = ShardEffects.Sheet(slot);
        SimpleSheet? sheet = sheets.FirstOrDefault(x => x.Material == material);
        if (sheet is null)
        {
            sheet = new SimpleSheet(material, random, views, visibility, () => host.CurrentTime, rules: rules);
            sheets.Add(sheet);
        }
        return sheet;
    }

    private static float[]? Block(ImpactEffect effect, int material, float scale, bool noFlecks)
    {
        float[] p = new float[ImpactEffectPolicy.MaxFloats];
        return ImpactEffectPolicy.Build(effect, material, scale, noFlecks, p) < 0 ? null : p;
    }

    private Made Started(Made made, Vector origin)
    {
        foreach (var sheet in made.Sheets)
            sheet.EffectId = effects.Start(sheet, origin);
        return made;
    }

    int IClientImpactSprays.Debris(Vector endPos, Vector normal, Vector color, int material, int scale, bool noFlecks, float throttle)
    {
        float[]? p = Block(ImpactEffect.DebrisFlecks, material, scale, noFlecks);
        if (p is null)
            return 0;
        Made made = Debris(endPos, normal, color, scale, throttle, p, _random);
        Vector offset = endPos + normal * p[(int)ImpactHeader.HeadC];
        Started(made, offset);
        return made.Flecks + made.Sheets.Sum(s => s.Pending.Count);
    }

    /// <summary>
    /// <c>FX_DebrisFlecks</c> past its gate and block: the flecks (burst 0,
    /// <see cref="FleckSystems.ImpactFlecks"/>, skipped for a zero count) and
    /// the three dust bursts behind them, not yet started.
    /// </summary>
    public Made Debris(Vector endPos, Vector normal, Vector color, int scale, float throttle, float[] p, IParticleRandom random)
    {
        float H(ImpactHeader f) => p[(int)f];
        float B(int n, ImpactBurst f) => p[ImpactEffectPolicy.BurstAt(n) + (int)f];
        int bursts = (int)H(ImpactHeader.Bursts);

        int nFlecks = B(0, ImpactBurst.CountMax) > 0.0f ? flecks.ImpactFlecks(endPos, normal, color, scale, throttle, p, random) : 0;

        var sheets = new List<SimpleSheet>();
        Vector offset = endPos + normal * H(ImpactHeader.HeadC);
        for (int nB = 1; nB < bursts; nB++)
        {
            int count = RandI(random, B(nB, ImpactBurst.CountMin), B(nB, ImpactBurst.CountMax));
            bool indexScale = B(nB, ImpactBurst.ExtraA) != 0.0f;
            bool assignZ = B(nB, ImpactBurst.ExtraB) != 0.0f;
            SimpleSheet sheet = SheetFor(sheets, (ImpactMaterial)(int)B(nB, ImpactBurst.Material), random, null);
            for (int i = 0; i < count; i++)
            {
                float step = indexScale ? i + 1 : 1.0f;
                var d = new SimpleParticle { Position = offset, Lifetime = 0.0f };
                d.DieTime = RandF(random, B(nB, ImpactBurst.LifeMin), B(nB, ImpactBurst.LifeMax));
                float dx = normal.X + RandF(random, B(nB, ImpactBurst.SprayMin), B(nB, ImpactBurst.SprayMax));
                float dy = normal.Y + RandF(random, B(nB, ImpactBurst.SprayMin), B(nB, ImpactBurst.SprayMax));
                float dz = normal.Z + RandF(random, B(nB, ImpactBurst.SprayMin), B(nB, ImpactBurst.SprayMax));
                d.StartSize = unchecked((byte)(RandI(random, B(nB, ImpactBurst.SizeMin), B(nB, ImpactBurst.SizeMax)) * (indexScale ? scale : 1)));
                d.EndSize = UChar(d.StartSize * B(nB, ImpactBurst.SizeEndMul) * (indexScale ? scale : 1));
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
                d.R = UChar(MathF.Min(1.0f, color.X * ramp) * 255.0f);
                d.G = UChar(MathF.Min(1.0f, color.Y * ramp) * 255.0f);
                d.B = UChar(MathF.Min(1.0f, color.Z * ramp) * 255.0f);
                sheet.Add(d);
            }
        }
        return new Made(sheets, nFlecks);
    }

    int IClientImpactSprays.Dust(Vector origin, Vector normal, Vector color, float scale, float throttle)
    {
        float[]? p = Block(ImpactEffect.Dust, 0, scale, false);
        if (p is null)
            return 0;
        Made made = Started(Dust(origin, normal, color, scale, throttle, p, _random), origin);
        return made.Sheets.Sum(s => s.Pending.Count);
    }

    // "To get a decent spread even when scaling down the number of particles..."
    private static ReadOnlySpan<int> ParticleIds => [3, 1, 2, 0];

    /// <summary><c>FX_DustImpact</c> past its block, on <see cref="DustParticleRules"/>, not yet started.</summary>
    public Made Dust(Vector origin, Vector normal, Vector color, float scale, float throttle, float[] p, IParticleRandom random)
    {
        float H(ImpactHeader f) => p[(int)f];
        float B(int n, ImpactBurst f) => p[ImpactEffectPolicy.BurstAt(n) + (int)f];
        int bursts = (int)H(ImpactHeader.Bursts);
        float spread = H(ImpactHeader.AngularSpray);
        float numParticles = H(ImpactHeader.HeadA) * throttle;
        var rules = new DustParticleRules(_dustDecay);

        var sheets = new List<SimpleSheet>();
        for (int nB = 0; nB < bursts; nB++)
        {
            int count = (int)(B(nB, ImpactBurst.ExtraA) + numParticles);
            bool cap = B(nB, ImpactBurst.SpeedMin) == 0.0f;
            SimpleSheet sheet = SheetFor(sheets, (ImpactMaterial)(int)B(nB, ImpactBurst.Material), random, rules);
            for (int i = 0; i < count && i < 4; i++)
            {
                int id = cap ? 0 : ParticleIds[i];
                var d = new SimpleParticle { Position = origin, Lifetime = 0.0f };
                d.DieTime = RandF(random, B(nB, ImpactBurst.LifeMin), B(nB, ImpactBurst.LifeMax));

                if (cap)
                {
                    Vector offset = origin;
                    offset.X += random.RandomFloat(-H(ImpactHeader.HeadB), H(ImpactHeader.HeadB));
                    offset.Y += random.RandomFloat(-H(ImpactHeader.HeadB), H(ImpactHeader.HeadB));
                    d.Position = offset;
                    float capSpread = B(nB, ImpactBurst.SprayMax);
                    d.Velocity = RandomVector(random, -capSpread, capSpread) + normal;
                }
                else
                {
                    d.Velocity = RandomVector(random, -spread, spread);
                    d.Velocity = d.Velocity + normal * RandF(random, B(nB, ImpactBurst.SprayMin), B(nB, ImpactBurst.SprayMax));
                }

                FxMath.Normalize(ref d.Velocity);

                float force = RandF(random, B(nB, ImpactBurst.SpeedMin), B(nB, ImpactBurst.SpeedMax)) * (cap ? 1.0f : (float)id);
                d.Velocity = d.Velocity * (cap ? force : force * scale);

                float ramp = RandF(random, B(nB, ImpactBurst.RampMin), B(nB, ImpactBurst.RampMax));
                d.R = UChar(MathF.Min(1.0f, color.X * ramp) * 255.0f);
                d.G = UChar(MathF.Min(1.0f, color.Y * ramp) * 255.0f);
                d.B = UChar(MathF.Min(1.0f, color.Z * ramp) * 255.0f);

                int size = RandI(random, B(nB, ImpactBurst.SizeMin), B(nB, ImpactBurst.SizeMax));
                d.StartSize = cap
                    ? unchecked((byte)size)
                    : UChar((B(nB, ImpactBurst.ExtraB) != 0.0f ? scale : 1.0f) * size * (id + 1));
                d.EndSize = UChar(d.StartSize * B(nB, ImpactBurst.SizeEndMul));

                d.StartAlpha = unchecked((byte)RandI(random, B(nB, ImpactBurst.AlphaMin), B(nB, ImpactBurst.AlphaMax)));
                d.EndAlpha = 0;
                d.Roll = RandF(random, B(nB, ImpactBurst.RollMin), B(nB, ImpactBurst.RollMax));
                d.RollDelta = RandF(random, B(nB, ImpactBurst.RollDeltaMin), B(nB, ImpactBurst.RollDeltaMax));
                sheet.Add(d);
            }
        }
        return new Made(sheets, 0);
    }

    /// <summary>The blood sprite temp entity's puff (<c>FX_Blood</c>), started at the wound.</summary>
    public int Blood(Vector pos, Vector dir, float r, float g, float b, float a)
    {
        float[]? p = Block(ImpactEffect.Blood, 0, 1.0f, false);
        if (p is null)
            return 0;
        Made made = Started(Blood(pos, dir, r, g, b, a, p, _random), pos);
        int n = made.Sheets.Sum(s => s.Pending.Count);
        BloodParticles += n;
        return n;
    }

    /// <summary>
    /// Blood-puff particles started since load: what step 7a's gate reads as
    /// "impact blood" (cs_cl_fx_report). The native ledger's row had no writer
    /// once FX_Blood left native code.
    /// </summary>
    public int BloodParticles { get; private set; }

    /// <summary><c>FX_Blood</c> past its block: one wobbled direction, two bursts, not yet started.</summary>
    public Made Blood(Vector pos, Vector dir, float r, float g, float b, float a, float[] p, IParticleRandom random)
    {
        float H(ImpactHeader f) => p[(int)f];
        float B(int n, ImpactBurst f) => p[ImpactEffectPolicy.BurstAt(n) + (int)f];
        int bursts = (int)H(ImpactHeader.Bursts);

        float wobble = H(ImpactHeader.HeadA);
        float vx = dir.X + random.RandomFloat(-wobble, wobble);
        float vy = dir.Y + random.RandomFloat(-wobble, wobble);
        float vz = dir.Z + random.RandomFloat(-wobble, wobble);
        Vector vDir = new(vx, vy, vz);
        FxMath.Normalize(ref vDir);

        var sheets = new List<SimpleSheet>();
        for (int nB = 0; nB < bursts; nB++)
        {
            int count = RandI(random, B(nB, ImpactBurst.CountMin), B(nB, ImpactBurst.CountMax));
            bool indexSpeed = B(nB, ImpactBurst.ExtraA) != 0.0f;
            SimpleSheet sheet = SheetFor(sheets, (ImpactMaterial)(int)B(nB, ImpactBurst.Material), random, null);
            for (int i = 0; i < count; i++)
            {
                var d = new SimpleParticle { Position = pos, Lifetime = 0.0f };
                d.DieTime = RandF(random, B(nB, ImpactBurst.LifeMin), B(nB, ImpactBurst.LifeMax));
                float speed = RandF(random, B(nB, ImpactBurst.SpeedMin), B(nB, ImpactBurst.SpeedMax));
                d.Velocity = vDir * (speed * (indexSpeed ? i : 1));
                if (B(nB, ImpactBurst.ZMin) != 0.0f || B(nB, ImpactBurst.ZMax) != 0.0f)
                    d.Velocity.Z += RandF(random, B(nB, ImpactBurst.ZMin), B(nB, ImpactBurst.ZMax));
                d.R = UChar(r);
                d.G = UChar(g);
                d.B = UChar(b);
                d.StartAlpha = B(nB, ImpactBurst.AlphaMax) != 0.0f
                    ? unchecked((byte)RandI(random, B(nB, ImpactBurst.AlphaMin), B(nB, ImpactBurst.AlphaMax)))
                    : UChar(a);
                d.EndAlpha = 0;
                d.StartSize = unchecked((byte)RandI(random, B(nB, ImpactBurst.SizeMin), B(nB, ImpactBurst.SizeMax)));
                d.EndSize = UChar(d.StartSize * B(nB, ImpactBurst.SizeEndMul));
                d.Roll = RandF(random, B(nB, ImpactBurst.RollMin), B(nB, ImpactBurst.RollMax));
                d.RollDelta = RandF(random, B(nB, ImpactBurst.RollDeltaMin), B(nB, ImpactBurst.RollDeltaMax));
                sheet.Add(d);
            }
        }
        return new Made(sheets, 0);
    }

    /// <summary>
    /// <c>FX_BugBlood</c>: yellow spurts and splats from one wobbled direction,
    /// bounded by a box around the wound. Nothing in this mod calls it (stock's
    /// callers are the antlions and other bugs); the port is kept with its
    /// stock replay.
    /// </summary>
    public Made? BugBlood(Vector pos, Vector dir, IParticleRandom random)
    {
        float[]? p = Block(ImpactEffect.BugBlood, 0, 1.0f, false);
        return p is null ? null : BugBlood(pos, dir, p, random);
    }

    public Made BugBlood(Vector pos, Vector dir, float[] p, IParticleRandom random)
    {
        float H(ImpactHeader f) => p[(int)f];
        float B(int n, ImpactBurst f) => p[ImpactEffectPolicy.BurstAt(n) + (int)f];
        int bursts = (int)H(ImpactHeader.Bursts);
        float box = H(ImpactHeader.HeadB);

        float wobble = H(ImpactHeader.HeadA);
        float vx = dir.X + random.RandomFloat(-wobble, wobble);
        float vy = dir.Y + random.RandomFloat(-wobble, wobble);
        float vz = dir.Z + random.RandomFloat(-wobble, wobble);
        Vector vDir = new(vx, vy, vz);
        FxMath.Normalize(ref vDir);

        var sheets = new List<SimpleSheet>();
        for (int nB = 0; nB < bursts; nB++)
        {
            int count = RandI(random, B(nB, ImpactBurst.CountMin), B(nB, ImpactBurst.CountMax));
            bool splat = B(nB, ImpactBurst.SprayMax) != 0.0f;
            SimpleSheet sheet = SheetFor(sheets, (ImpactMaterial)(int)B(nB, ImpactBurst.Material), random, null);
            sheet.Box = (pos - new Vector(box, box, box), pos + new Vector(box, box, box));
            for (int i = 0; i < count; i++)
            {
                Vector at = pos;
                if (splat)
                    at = at + RandomVector(random, B(nB, ImpactBurst.SprayMin), B(nB, ImpactBurst.SprayMax));
                var d = new SimpleParticle { Position = at, Lifetime = 0.0f };
                d.DieTime = RandF(random, B(nB, ImpactBurst.LifeMin), B(nB, ImpactBurst.LifeMax));
                if (splat)
                {
                    float step = ((float)i / (float)count) + 1.0f;
                    float spreadV = B(nB, ImpactBurst.ExtraA);
                    d.Velocity = RandomVector(random, -spreadV, spreadV);
                    d.Velocity = d.Velocity + vDir * -(B(nB, ImpactBurst.SpeedMin) * step);
                    d.Velocity.Z -= B(nB, ImpactBurst.ZMin) * step;
                }
                else
                {
                    float speed = RandF(random, B(nB, ImpactBurst.SpeedMin), B(nB, ImpactBurst.SpeedMax));
                    d.Velocity = vDir * -speed;
                    d.Velocity.Z -= B(nB, ImpactBurst.ZMin);
                }
                d.R = UChar(B(nB, ImpactBurst.TintR));
                d.G = UChar(B(nB, ImpactBurst.TintG));
                d.B = UChar(B(nB, ImpactBurst.TintB));
                d.StartAlpha = unchecked((byte)RandI(random, B(nB, ImpactBurst.AlphaMin), B(nB, ImpactBurst.AlphaMax)));
                d.EndAlpha = 0;
                d.StartSize = unchecked((byte)RandI(random, B(nB, ImpactBurst.SizeMin), B(nB, ImpactBurst.SizeMax)));
                int endMul = B(nB, ImpactBurst.SizeEndMul) != 0.0f
                    ? (int)B(nB, ImpactBurst.SizeEndMul)
                    : RandI(random, B(nB, ImpactBurst.ExtraA), B(nB, ImpactBurst.ExtraB));
                d.EndSize = unchecked((byte)(d.StartSize * endMul));
                d.Roll = RandF(random, B(nB, ImpactBurst.RollMin), B(nB, ImpactBurst.RollMax));
                d.RollDelta = RandF(random, B(nB, ImpactBurst.RollDeltaMin), B(nB, ImpactBurst.RollDeltaMax));
                sheet.Add(d);
            }
        }
        return new Made(sheets, 0);
    }
}
