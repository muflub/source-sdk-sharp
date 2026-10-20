//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>fx.cpp's routines the client's IEffects and the muzzle flashes end in (lane c5-particles).</summary>
public interface IFxRoutines
{
    /// <summary><c>FX_MuzzleEffect</c>: a world-space flash of eight sprites along the angles.</summary>
    void MuzzleEffect(Vector origin, QAngle angles, float scale);

    /// <summary><c>FX_GunshipMuzzleEffect</c>.</summary>
    void GunshipMuzzleEffect(Vector origin, QAngle angles, float scale);

    /// <summary><c>FX_StriderMuzzleEffect</c>.</summary>
    void StriderMuzzleEffect(Vector origin, QAngle angles, float scale);

    /// <summary><c>FX_MuzzleEffectAttached</c>: the same flash, local to an entity's attachment.</summary>
    void MuzzleEffectAttached(float scale, uint handle, int attachment, bool oneFrame);

    /// <summary><c>FX_Smoke( origin, angles, scale, numParticles, color, alpha )</c>.</summary>
    void Smoke(Vector origin, QAngle angles, float scale, int numParticles, Color32? color, int alpha);

    /// <summary><c>FX_RicochetSound</c>.</summary>
    void RicochetSound(Vector position);
}

/// <summary>What fx.cpp's routines read and draw natively: sounds, and the gunship impact's warp overlay.</summary>
public interface IFxRoutineEnvironment
{
    /// <summary>A game sound to the local player, from the world, at the point.</summary>
    void Sound(string script, Vector origin);

    /// <summary><c>FX_GunshipImpact</c>'s <c>CImpactOverlay</c>, drawn by the glow overlay system.</summary>
    void ImpactOverlay(Vector position, Vector normal, float r, float g, float b);
}

/// <summary>The real one: the sound through the game sounds, the overlay through the managed glow overlays (GlowOverlayCreators).</summary>
public sealed class FxRoutineEnvironment(IFxGameSounds sounds, IFxImpactOverlay overlay) : IFxRoutineEnvironment
{
    // CLocalPlayerFilter, SOUND_FROM_WORLD, the script's own parameters at the point.
    public void Sound(string script, Vector origin)
    {
        if (sounds.Parameters(script, out GameSoundParams p))
            sounds.EmitAtPosition(p, p.Volume, p.Pitch, origin);
    }

    // VectorMA( pos, 1.0f, normal, m_vPos ): "Doesn't show up on terrain if you don't do this".
    public void ImpactOverlay(Vector position, Vector normal, float r, float g, float b)
        => overlay.ImpactOverlay(position + normal * 1.0f, new Vector(r, g, b));
}

/// <summary>A sprite emitter spitting puffs from an entity's attachment for its life: <c>CSmokeEmitter</c>.</summary>
public sealed class SmokeSpurt(string material, IParticleRandom random, ParticleViews views,
                               IParticlePixelVisibility visibility, Func<float> curTime)
{
    public SimpleSheet Sheet { get; } = new(material, random, views, visibility, curTime);
    public int EntIndex;
    public int Attachment;
    public float DeathTime;
    public float LastParticleSpawnTime;
    public float SpawnRate;
    public Vector SpurtForward;
    public float[] SpurtColor = new float[4];
    public Vector SortOrigin;

    /// <summary>The stream its later spurts draw from (a replay swaps in each spurt's recorded draws).</summary>
    public IParticleRandom Random { get; set; } = random;
}

/// <summary>
/// fx.cpp's live routines (lane c5-particles): the world and attached muzzle
/// flashes, the gunship's and strider's, the smoke puffs, the hose-gas spurt
/// (<c>CSmokeEmitter</c>), the gunship impact and command pointer overlays,
/// and the ricochet sound. Sampled from a managed stream; replayed against
/// <c>Fixtures/fx-stock.tsv.gz</c>.
/// </summary>
public sealed class FxEffects(ClientParticleEffects effects, ParticleViews views, IParticlePixelVisibility visibility,
                              IFxEmitters emitters, IFxFlashReads reads, IFxRender render, IFxDiscreetLineSink lines,
                              IFxRoutineEnvironment environment,
                              IEngineHost host, IRandomSeeds seeds) : IFxRoutines
{
    private readonly IParticleRandom _random = new ParticleStream(seeds.Next());

    public int Muzzles { get; private set; }

    public int Smokes { get; private set; }

    private static byte UChar(float f) => unchecked((byte)(int)f);

    private static Vector Forward(QAngle angles) => MoveVec.AngleForward(new Vector(angles.Pitch, angles.Yaw, angles.Roll));

    private static float ClampScale(float flScale) => flScale < 0.5f ? 0.5f : flScale > 8.0f ? 8.0f : flScale;

    private static Vector RandomVector(IParticleRandom random, float min, float max)
    {
        float x = random.RandomFloat(min, max);
        float y = random.RandomFloat(min, max);
        float z = random.RandomFloat(min, max);
        return new Vector(x, y, z);
    }

    private SimpleSheet Sheet(List<SimpleSheet> sheets, string material, IParticleRandom random)
    {
        foreach (var s in sheets)
        {
            if (s.Material == material)
                return s;
        }
        var sheet = new SimpleSheet(material, random, views, visibility, () => host.CurrentTime);
        sheets.Add(sheet);
        return sheet;
    }

    private void Start(List<SimpleSheet> sheets, Vector sortOrigin)
    {
        foreach (var s in sheets)
        {
            if (s.EffectId == 0 && s.Pending.Count > 0)
                s.EffectId = effects.Start(s, sortOrigin);
        }
    }

    public void MuzzleEffect(Vector origin, QAngle angles, float scale) => MuzzleEffect(origin, angles, scale, null, _random);

    /// <summary><c>FX_MuzzleEffect</c>; the sheets made, one per sprite material.</summary>
    public List<SimpleSheet> MuzzleEffect(Vector origin, QAngle angles, float scale, byte[]? flashColor, IParticleRandom random)
    {
        Muzzles++;
        var sheets = new List<SimpleSheet>();
        Vector forward = Forward(angles);
        float flScale = ClampScale(random.RandomFloat(scale - 0.25f, scale + 0.25f));

        for (int i = 1; i < 9; i++)
        {
            Vector offset = origin + forward * (i * 2.0f * scale);
            string material = "effects/muzzleflash" + random.RandomInt(1, 4);
            var p = new SimpleParticle { Position = offset, Lifetime = 0.0f, DieTime = 0.1f };
            p.R = flashColor?[0] ?? 255;
            p.G = flashColor?[1] ?? 255;
            p.B = flashColor?[2] ?? 255;
            p.StartAlpha = 255;
            p.EndAlpha = 128;
            p.StartSize = UChar((random.RandomFloat(6.0f, 9.0f) * (12 - i) / 9) * flScale);
            p.EndSize = p.StartSize;
            p.Roll = random.RandomInt(0, 360);
            p.RollDelta = 0.0f;
            Sheet(sheets, material, random).Add(p);
        }
        Start(sheets, origin);
        return sheets;
    }

    public void GunshipMuzzleEffect(Vector origin, QAngle angles, float scale) => GunshipMuzzleEffect(origin, angles, scale, _random);

    /// <summary><c>FX_GunshipMuzzleEffect</c>: one held flash at the muzzle.</summary>
    public SimpleSheet GunshipMuzzleEffect(Vector origin, QAngle angles, float scale, IParticleRandom random)
    {
        Muzzles++;
        var sheets = new List<SimpleSheet>();
        var p = new SimpleParticle { Position = origin, Lifetime = 0.0f, DieTime = 0.15f };
        p.StartSize = UChar(random.RandomFloat(40.0f, 50.0f));
        p.EndSize = p.StartSize;
        p.Roll = random.RandomInt(0, 360);
        p.RollDelta = 0.15f;
        p.R = p.G = p.B = 255;
        p.StartAlpha = 255;
        p.EndAlpha = 255;
        SimpleSheet sheet = Sheet(sheets, "effects/gunshipmuzzle", random);
        sheet.Add(p);
        Start(sheets, origin);
        return sheet;
    }

    /// <summary><c>FX_StriderMuzzleEffect</c>: five discreet lines along the angles, each half again as fast.</summary>
    public void StriderMuzzleEffect(Vector origin, QAngle angles, float scale)
    {
        Muzzles++;
        Vector dir = Forward(angles);
        const float life = 0.3f;
        float speed = 100.0f;
        for (int i = 0; i < 5; i++)
        {
            lines.AddDiscreetLine(origin, dir, speed, 32, speed * life, 5.0f, life, "effects/bluespark");
            speed *= 1.5f;
        }
    }

    public void MuzzleEffectAttached(float scale, uint handle, int attachment, bool oneFrame)
        => MuzzleEffectAttached(scale, handle, attachment, null, oneFrame, _random);

    /// <summary>
    /// <c>FX_MuzzleEffectAttached</c>: the flash in the attachment's space
    /// (the pool's local-space emitter). Stock's tool-recording tail runs only
    /// with -tools and is not carried.
    /// </summary>
    public void MuzzleEffectAttached(float scale, uint handle, int attachment, byte[]? flashColor, bool oneFrame,
                                     IParticleRandom random)
    {
        if ((reads.MaterialsCached() & 1) == 0)
            return;
        Muzzles++;
        int emitter = emitters.Create(FxEmitterKind.LocalSpace, "MuzzleFlash", handle, attachment, 0);
        if (emitter == 0)
            return;
        try
        {
            emitters.SetBBox(emitter, new Vector(16, 16, 16) * -scale, new Vector(16, 16, 16) * scale);
            var forward = new Vector(1, 0, 0);
            float flScale = ClampScale(random.RandomFloat(scale - 0.25f, scale + 0.25f));
            for (int i = 1; i < 9; i++)
            {
                Vector offset = forward * (i * 2.0f * scale);
                string material = "effects/muzzleflash" + (random.RandomInt(0, 3) + 1);
                int particle = emitters.Add(emitter, material, offset);
                if (particle < 0)
                    return;
                var p = new FxSimpleParticle { Lifetime = 0.0f, DieTime = oneFrame ? 0.0001f : 0.1f };
                p.R = flashColor?[0] ?? 255;
                p.G = flashColor?[1] ?? 255;
                p.B = flashColor?[2] ?? 255;
                p.StartAlpha = 255;
                p.EndAlpha = 128;
                p.StartSize = UChar((random.RandomFloat(6.0f, 9.0f) * (12 - i) / 9) * flScale);
                p.EndSize = p.StartSize;
                p.Roll = random.RandomInt(0, 360);
                p.RollDelta = 0.0f;
                emitters.SetSimple(emitter, particle, p);
            }
        }
        finally
        {
            emitters.Release(emitter);
        }
    }

    public void Smoke(Vector origin, QAngle angles, float scale, int numParticles, Color32? color, int alpha)
        => Smoke(origin, angles, scale, numParticles, color, alpha, _random);

    /// <summary>
    /// <c>FX_Smoke( origin, angles, ... )</c>: each puff drifts along the angles,
    /// grey if no colour is given. Stock made each puff its own emitter (the
    /// inner <c>FX_Smoke</c>); the port puts one call's puffs on one sheet.
    /// </summary>
    public SimpleSheet Smoke(Vector origin, QAngle angles, float scale, int numParticles, Color32? color, int alpha,
                             IParticleRandom random)
    {
        Smokes++;
        var sheets = new List<SimpleSheet>();
        SimpleSheet sheet = Sheet(sheets, "particle/particle_smokegrenade", random);
        for (int i = 0; i < numParticles; i++)
        {
            Vector forward = Forward(angles);
            Vector velocity = RandomVector(random, -0.5f, 0.5f) + forward;
            FxMath.Normalize(ref velocity);
            velocity = velocity * random.RandomFloat(16.0f, 32.0f);
            velocity.Z += random.RandomFloat(4.0f, 16.0f);

            byte r, g, b;
            if (color is not { } c)
            {
                int shade = random.RandomInt(64, 164);
                r = g = b = (byte)shade;
            }
            else
            {
                (r, g, b) = ((byte)c.R, (byte)c.G, (byte)c.B);
            }

            int a = alpha == -1 ? random.RandomInt(10, 25) : alpha;
            int iSize = (int)(random.RandomInt(4, 8) * scale);
            float roll = random.RandomInt(0, 360);
            float rollDelta = random.RandomFloat(-4.0f, 4.0f);
            float dieTime = random.RandomFloat(0.5f, 1.0f);

            var p = new SimpleParticle { Position = origin, Lifetime = 0.0f, DieTime = dieTime, Velocity = velocity };
            p.R = r;
            p.G = g;
            p.B = b;
            p.StartAlpha = unchecked((byte)a);
            p.EndAlpha = 0;
            p.StartSize = unchecked((byte)iSize);
            p.EndSize = unchecked((byte)(p.StartSize * 2));
            p.Roll = roll;
            p.RollDelta = rollDelta;
            sheet.Add(p);
        }
        Start(sheets, origin);
        return sheet;
    }

    public void RicochetSound(Vector position) => environment.Sound("FX_RicochetSound.Ricochet", position);

    /// <summary><c>FX_GunshipImpact</c>: the overlay a hair off the surface.</summary>
    public void GunshipImpact(Vector position, Vector normal, float r, float g, float b)
        => environment.ImpactOverlay(position, normal, r, g, b);

    /// <summary><c>commandercolors</c> (effect_color_tables.h): red, blue, green, yellow by index.</summary>
    public static (float R, float G, float B)? CommanderColor(int index) => index switch
    {
        0 => (1, 0, 0),
        1 => (0, 0, 1),
        2 => (0, 1, 0),
        3 => (1, 1, 0),
        _ => null,
    };

    public SmokeSpurt BuildSmoke(Vector origin, QAngle angles, int entIndex, int attachment, float lifeTime, float[] color)
        => BuildSmoke(origin, angles, entIndex, attachment, lifeTime, color, _random);

    /// <summary><c>FX_BuildSmoke</c>: a <c>CSmokeEmitter</c> spurting for its life, its first spurt now.</summary>
    public SmokeSpurt BuildSmoke(Vector origin, QAngle angles, int entIndex, int attachment, float lifeTime, float[] color,
                                 IParticleRandom random)
    {
        Smokes++;
        var spurt = new SmokeSpurt("particle/particle_smokegrenade", random, views, visibility, () => host.CurrentTime)
        {
            EntIndex = entIndex,
            Attachment = attachment,
            SortOrigin = origin,
            DeathTime = host.CurrentTime + lifeTime,
            SpurtForward = Forward(angles),
            SpawnRate = 0.03f,
        };
        Array.Copy(color, spurt.SpurtColor, 4);
        spurt.Sheet.Spawner = () => SpurtIfDue(spurt);
        CreateSpurtParticles(spurt, random);
        spurt.Sheet.EffectId = effects.Start(spurt.Sheet, origin);
        return spurt;
    }

    /// <summary>
    /// <c>CSmokeEmitter::SimulateParticles</c>'s head, run by the sheet before it
    /// steps: while it lives and its spawn time has come, spurt (once, as
    /// stock's per-particle check passes once and then waits on the new time).
    /// </summary>
    private void SpurtIfDue(SmokeSpurt spurt)
    {
        float now = host.CurrentTime;
        if (spurt.Sheet.Emitter.Particles.Count > 0 && spurt.DeathTime > now && spurt.LastParticleSpawnTime <= now)
            CreateSpurtParticles(spurt, spurt.Random);
    }

    // color[j][0..1]: MAX( 0, c - 64 ) and MIN( 255, c + 64 ), floats truncated into ints.
    private static int Low(SmokeSpurt spurt, int j) => (int)MathF.Max(0, spurt.SpurtColor[j] - 64);

    private static int High(SmokeSpurt spurt, int j) => (int)MathF.Min(255, spurt.SpurtColor[j] + 64);

    /// <summary><c>CSmokeEmitter::CreateSpurtParticles</c>.</summary>
    public void CreateSpurtParticles(SmokeSpurt spurt, IParticleRandom random)
    {
        Vector origin = spurt.SortOrigin;
        // Stock used what GetAttachment wrote and ignored its answer: the
        // attachment, or the entity's own origin for one its model lacks (0).
        if (spurt.Attachment != 0 && render.EntityAttachment(spurt.EntIndex, spurt.Attachment, out Vector at, out _) >= 0)
        {
            origin = at;
            spurt.SortOrigin = at;
        }

        int numParticles = random.RandomInt(1, 2);
        for (int i = 0; i < numParticles; i++)
        {
            var p = new SimpleParticle { Position = origin, Lifetime = 0.0f };
            p.DieTime = random.RandomFloat(0.5f, 1.0f);
            Vector velocity = RandomVector(random, -0.1f, 0.1f) + spurt.SpurtForward;
            FxMath.Normalize(ref velocity);
            p.Velocity = velocity * random.RandomFloat(160.0f, 640.0f);

            p.R = unchecked((byte)random.RandomInt(Low(spurt, 0), High(spurt, 0)));
            p.G = unchecked((byte)random.RandomInt(Low(spurt, 1), High(spurt, 1)));
            p.B = unchecked((byte)random.RandomInt(Low(spurt, 2), High(spurt, 2)));
            p.StartAlpha = UChar(spurt.SpurtColor[3]);
            p.EndAlpha = 0;
            p.StartSize = (byte)random.RandomInt(50, 60);
            p.EndSize = unchecked((byte)(p.StartSize * 3));
            p.Roll = random.RandomFloat(0, 360);
            p.RollDelta = random.RandomFloat(-4.0f, 4.0f);
            spurt.Sheet.Add(p);
        }
        spurt.LastParticleSpawnTime = host.CurrentTime + spurt.SpawnRate;
    }
}

/// <summary>The <c>Smoke</c> client effect: a grey hose-gas spurt for 100 seconds.</summary>
public sealed class SmokeEffect(FxEffects fx) : IClientEffectHandler
{
    public string EffectName => "Smoke";

    public void Dispatch(in EffectData data)
    {
        var angles = new QAngle(data.Angles.X, data.Angles.Y, data.Angles.Z);
        fx.BuildSmoke(data.Origin, angles, data.EntIndex, data.AttachmentIndex, 100.0f, [50, 50, 50, 255]);
    }
}

/// <summary>The <c>GunshipImpact</c> client effect.</summary>
public sealed class GunshipImpactEffect(FxEffects fx) : IClientEffectHandler
{
    public string EffectName => "GunshipImpact";

    public void Dispatch(in EffectData data) => fx.GunshipImpact(data.Origin, new Vector(0, 0, 1), 100, 0, 200);
}

/// <summary>The <c>CommandPointer</c> client effect: the commander colour's overlay, none for another index.</summary>
public sealed class CommandPointerEffect(FxEffects fx) : IClientEffectHandler
{
    public string EffectName => "CommandPointer";

    public void Dispatch(in EffectData data)
    {
        if (FxEffects.CommanderColor(data.Color) is { } c)
            fx.GunshipImpact(data.Origin, new Vector(0, 0, 1), c.R, c.G, c.B);
    }
}

/// <summary>
/// The <c>MuzzleFlash</c> client effect, stock's "old-style muzzle flashes":
/// the temp entities' world-space flash (which reads neither the place nor the
/// angles) for the entity, none when an entity index names no entity.
/// </summary>
public sealed class MuzzleFlashEffect(TempEnts.MuzzleFlashes muzzle, IClientStandalones registry) : IClientEffectHandler
{
    /// <summary><c>MUZZLEFLASH_FIRSTPERSON</c> (shareddefs.h).</summary>
    public const int FirstPerson = 0x100;

    public string EffectName => "MuzzleFlash";

    public void Dispatch(in EffectData data)
    {
        var entity = registry.GetByIndex(data.EntIndex);
        if (data.EntIndex > 0 && entity is null)
            return;
        uint handle = entity?.Handle.Value ?? EntityHandle.None.Value;
        muzzle.MuzzleFlashAt(data.Flags & ~FirstPerson, handle, (data.Flags & FirstPerson) != 0);
    }
}

/// <summary>
/// The <c>TeslaHitboxes</c> and <c>TeslaZap</c> client effects: stock's bodies
/// here draw nothing (each returns on an entity no managed entity can be), and
/// the registrations are kept so the names are claimed as stock's were.
/// </summary>
public sealed class EmptyEffect(string name) : IClientEffectHandler
{
    public string EffectName { get; } = name;

    public void Dispatch(in EffectData data)
    {
    }
}
