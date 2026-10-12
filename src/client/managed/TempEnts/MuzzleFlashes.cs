//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>ITempEnts::MuzzleFlash</c>'s two overloads, as the animation events call
/// them (stock's C_BaseAnimating::DispatchMuzzleEffect and FireObsoleteEvent).
/// </summary>
public interface ILegacyMuzzleFlashes
{
    /// <summary><c>MuzzleFlash( type, hEntity, attachmentIndex, firstPerson )</c>.</summary>
    void MuzzleFlash(int type, uint handle, int attachment, bool firstPerson);

    /// <summary><c>MuzzleFlash( pos, angles, type, hEntity, firstPerson )</c>.</summary>
    void MuzzleFlashAt(int type, uint handle, bool firstPerson);
}

/// <summary><c>ITempEnts::EjectBrass( pos, angles, gunAngles, type )</c>, as the animation events call it.</summary>
public interface ILegacyBrass
{
    void EjectBrass(in Vector origin, in QAngle angles, in QAngle gunAngles, int type);
}

/// <summary><c>muzzleflash_light</c> (fx.cpp's ConVar, managed since lane c5-particles): whether a flash lights.</summary>
public interface IMuzzleFlashLight
{
    bool On { get; }
}

/// <summary>
/// c_te_legacytempents.cpp's muzzle flashes: <c>CTempEnts::MuzzleFlash</c>'s
/// two overloads (which variant a weapon type and person draw) and every
/// variant's particles (the Combine, SMG1, shotgun, 357 and pistol flashes,
/// the NPCs' fx.cpp hand-offs, the Combine NPC's elight). The particles are the
/// particle library's, made through <see cref="IFxEmitters"/>; every number on
/// them is decided here. Replayed against the recorded flashes
/// (MuzzleFlashTests).
/// </summary>
public sealed class MuzzleFlashes(IEngineHost random, IFxEmitters fx, IFxFlashReads reads, Effects.IFxRoutines routines,
                                  IMuzzleFlashLight light,
                                  IGameClock clock, ILog log) : ILegacyMuzzleFlashes
{
    // MUZZLEFLASH_* (shareddefs.h).
    public const int Ar2 = 0, Shotgun = 1, Smg1 = 2, Smg2 = 3, Pistol = 4, Combine = 5, Magnum357 = 6, Rpg = 7, CombineTurret = 8;

    // FLE_VIEWMODEL (particles_localspace.h); bitsPARTICLE_TRAIL_FADE (fx_sparks.h).
    private const int FleViewModel = 0x1;
    private const int TrailFade = 0x00000004;

    // LIGHT_INDEX_MUZZLEFLASH (cdll_util.h).
    private const int LightIndexMuzzleFlash = 0x40000000;

    // The materials, by the names CacheMuzzleFlashes and FX_CacheMaterialHandles fetch.
    private static readonly IReadOnlyList<string> PlayerFlash = ["effects/muzzleflash1_noz", "effects/muzzleflash2_noz", "effects/muzzleflash3_noz", "effects/muzzleflash4_noz"];
    private static readonly IReadOnlyList<string> CombinePlayerFlash = ["effects/combinemuzzle1_noz", "effects/combinemuzzle2_noz"];
    private static readonly IReadOnlyList<string> CombineFlash = ["effects/combinemuzzle1", "effects/combinemuzzle2", "effects/strider_muzzle"];
    private const string SmgFlash = "effects/muzzleflash1";
    private const string DustPuff = "particle/particle_smokegrenade";

    private readonly bool[] _reported = new bool[CombineTurret + 1];

    /// <summary>Variants drawn since the load (cs_cl_fx_report).</summary>
    public long Drawn { get; private set; }

    /// <summary><c>MuzzleFlash( type, hEntity, attachmentIndex, firstPerson )</c>: the attachment-local path.</summary>
    public void MuzzleFlash(int type, uint handle, int attachment, bool firstPerson)
    {
        switch (type)
        {
            case Combine:
                if (firstPerson) CombinePlayer(handle, attachment); else CombineNpc(handle, attachment);
                break;
            case Smg1:
                if (firstPerson) Smg1Player(handle, attachment); else Smg1Npc(handle, attachment);
                break;
            case Pistol:
                if (firstPerson) PistolPlayer(handle, attachment); else PistolNpc(handle, attachment);
                break;
            case Shotgun:
                if (firstPerson) ShotgunPlayer(handle, attachment); else ShotgunNpc(handle, attachment);
                break;
            case Magnum357:
                if (firstPerson) Magnum357Player(handle, attachment);
                break;
            case Rpg:
                if (!firstPerson) RpgNpc(handle, attachment);
                break;
            default:
                // Stock's Assert( 0 ): no flash of this type on this path.
                break;
        }
    }

    /// <summary>
    /// <c>MuzzleFlash( pos, angles, type, hEntity, firstPerson )</c>: the older
    /// world-space path, which reads neither the place nor the angles and
    /// draws on attachment 1.
    /// </summary>
    public void MuzzleFlashAt(int type, uint handle, bool firstPerson)
    {
        switch (type)
        {
            case Shotgun:
                if (firstPerson) ShotgunPlayer(handle, 1); else ShotgunNpc(handle, 1);
                break;
            case Smg1:
                if (firstPerson) Smg1Player(handle, 1); else Smg1Npc(handle, 1);
                break;
            case Smg2:
            case Pistol:
                if (firstPerson) PistolPlayer(handle, 1); else PistolNpc(handle, 1);
                break;
            case Combine:
                if (firstPerson) CombinePlayer(handle, 1); else CombineNpc(handle, 1);
                break;
            default:
                if (type >= 0 && type <= CombineTurret && !_reported[type])
                {
                    _reported[type] = true;
                    log.Msg($"MuzzleFlash: type {type} has no flash in the obsolete world-space path; the attachment-local one draws it\n");
                }
                break;
        }
    }

    // Adds a particle and, when it was made, writes its fields; false = the emitter is full (stock returns).
    private bool AddSimple(int emitter, string material, in Vector position, Func<FxSimpleParticle> fields)
    {
        int p = fx.Add(emitter, material, position);
        if (p < 0)
            return false;
        fx.SetSimple(emitter, p, fields());
        return true;
    }

    // The material index is drawn before AddParticle is called: it is one of its arguments.
    private string Pick(IReadOnlyList<string> materials, int lo, int hi) => materials[random.RandomInt(lo, hi)];

    private static byte B(float f) => (byte)(int)f;

    private void CombinePlayer(uint handle, int attachment)
    {
        Drawn++;
        int emitter = fx.Create(FxEmitterKind.LocalSpace, "MuzzleFlash", handle, attachment, FleViewModel);
        try
        {
            var forward = new Vector(1, 0, 0);
            float flScale = random.RandomFloat(2.0f, 2.25f);
            fx.DrawBeforeViewModel(emitter);

            for (int i = 1; i < 6; i++)
            {
                Vector offset = FxMath.Mul(forward, i * 8.0f * flScale);
                int k = i;
                if (!AddSimple(emitter, Pick(CombinePlayerFlash, 0, 1), offset, () =>
                    {
                        var p = new FxSimpleParticle { Lifetime = 0f, DieTime = 0.025f, R = 255, G = 255 };
                        p.B = (byte)(200 + random.RandomInt(0, 55));
                        p.StartAlpha = 255;
                        p.EndAlpha = 255;
                        p.StartSize = B(random.RandomFloat(6.0f, 8.0f) * (12 - k) / 12 * flScale);
                        p.EndSize = p.StartSize;
                        p.Roll = random.RandomInt(0, 360);
                        p.RollDelta = 0f;
                        return p;
                    }))
                    return;
            }

            AddSimple(emitter, Pick(CombinePlayerFlash, 0, 1), default, () =>
            {
                var p = new FxSimpleParticle { Lifetime = 0f, DieTime = 0.025f, R = 255, G = 255, B = 255 };
                p.StartAlpha = (byte)random.RandomInt(64, 128);
                p.EndAlpha = 32;
                p.StartSize = B(random.RandomFloat(10.0f, 16.0f));
                p.EndSize = p.StartSize;
                p.Roll = random.RandomInt(0, 360);
                p.RollDelta = 0f;
                return p;
            });
        }
        finally
        {
            fx.Release(emitter);
        }
    }

    private void CombineNpc(uint handle, int attachment)
    {
        if ((reads.MaterialsCached() & 2) == 0)
            return;

        Drawn++;
        int emitter = fx.Create(FxEmitterKind.LocalSpace, "MuzzleFlash_Combine_NPC", handle, attachment, 0);
        try
        {
            var forward = new Vector(1, 0, 0);
            float flScale = random.RandomFloat(1.0f, 1.5f);
            float burstSpeed = random.RandomFloat(50.0f, 150.0f);

            const int frontLength = 6;
            for (int i = 1; i < frontLength; i++)
            {
                Vector offset = FxMath.Mul(forward, i * 2.0f * flScale);
                int k = i;
                float speed = burstSpeed;
                if (!AddSimple(emitter, Pick(CombineFlash, 0, 1), offset, () =>
                    {
                        var p = new FxSimpleParticle { Lifetime = 0f, DieTime = 0.1f, R = 255, G = 255, B = 255 };
                        p.Velocity = FxMath.Mul(forward, speed);
                        p.StartAlpha = 255;
                        p.EndAlpha = 0;
                        p.StartSize = B(random.RandomFloat(6.0f, 8.0f) * (frontLength * 1.25f - k) / frontLength * flScale);
                        p.EndSize = p.StartSize;
                        p.Roll = random.RandomInt(0, 360);
                        p.RollDelta = 0f;
                        return p;
                    }))
                    return;
            }

            var right = new Vector(0, 1, 0);
            var up = new Vector(0, 0, 1);
            const int sideLength = 6;

            // Three arms: right - up outward, right + up inward, up outward.
            (Vector Dir, bool Negate)[] arms = [(FxMath.Sub(right, up), false), (FxMath.Add(right, up), true), (up, false)];
            foreach (var (dir, negate) in arms)
            {
                burstSpeed = random.RandomFloat(50.0f, 150.0f);
                for (int i = 1; i < sideLength; i++)
                {
                    Vector offset = negate ? FxMath.Mul(Neg(dir), i * flScale) : FxMath.Mul(dir, i * flScale);
                    int k = i;
                    float speed = burstSpeed;
                    if (!AddSimple(emitter, Pick(CombineFlash, 0, 1), offset, () =>
                        {
                            var p = new FxSimpleParticle { Lifetime = 0f, DieTime = 0.2f, R = 255, G = 255, B = 255 };
                            p.Velocity = negate
                                ? FxMath.Mul(FxMath.Mul(dir, -speed), 0.25f)
                                : FxMath.Mul(FxMath.Mul(dir, speed), 0.25f);
                            p.StartAlpha = 255;
                            p.EndAlpha = 0;
                            p.StartSize = B(random.RandomFloat(2.0f, 4.0f) * (sideLength - k) / (sideLength * 0.5f) * flScale);
                            p.EndSize = p.StartSize;
                            p.Roll = random.RandomInt(0, 360);
                            p.RollDelta = 0f;
                            return p;
                        }))
                        return;
                }
            }

            if (!AddSimple(emitter, CombineFlash[2], default, () =>
                {
                    var p = new FxSimpleParticle { Lifetime = 0f, R = 255, G = 255, B = 255 };
                    p.DieTime = random.RandomFloat(0.3f, 0.4f);
                    p.StartAlpha = 255;
                    p.EndAlpha = 0;
                    p.StartSize = B(flScale * random.RandomFloat(12.0f, 16.0f));
                    p.EndSize = 0;
                    p.Roll = random.RandomInt(0, 360);
                    p.RollDelta = 0f;
                    return p;
                }))
                return;

            if (!reads.AttachmentMatrixOrigin(handle, attachment, out Vector origin))
                return;

            if (light.On)
            {
                int entIndex = reads.EntityIndex(handle);
                if (entIndex >= 0)
                {
                    float radius = random.RandomInt(32, 128);
                    reads.Elight(LightIndexMuzzleFlash + entIndex, origin, 64, 128, 255, 5, radius, radius / 0.05f, clock.Now + 0.05f);
                }
            }
        }
        finally
        {
            fx.Release(emitter);
        }
    }

    private static Vector Neg(in Vector v) => new(-v.X, -v.Y, -v.Z);

    private void Smg1Npc(uint handle, int attachment) { Drawn++; routines.MuzzleEffectAttached(1.0f, handle, attachment, true); }

    private void PistolNpc(uint handle, int attachment) { Drawn++; routines.MuzzleEffectAttached(0.5f, handle, attachment, true); }

    private void RpgNpc(uint handle, int attachment) { Drawn++; routines.MuzzleEffectAttached(1.5f, handle, attachment, false); }

    private void Smg1Player(uint handle, int attachment)
    {
        Drawn++;
        int emitter = fx.Create(FxEmitterKind.LocalSpace, "MuzzleFlash_SMG1_Player", handle, attachment, FleViewModel);
        try
        {
            var forward = new Vector(1, 0, 0);
            float flScale = random.RandomFloat(1.25f, 1.5f);
            fx.DrawBeforeViewModel(emitter);
            for (int i = 1; i < 6; i++)
            {
                Vector offset = FxMath.Mul(forward, i * 8.0f * flScale);
                if (!AddSimple(emitter, Pick(PlayerFlash, 0, 3), offset, FlashCore(i, flScale, 0.025f)))
                    return;
            }
        }
        finally
        {
            fx.Release(emitter);
        }
    }

    // The five-particle core the player flashes share: white with a random
    // blue-yellow, a size falling off along the barrel.
    private Func<FxSimpleParticle> FlashCore(int i, float flScale, float dieTime) => () =>
    {
        var p = new FxSimpleParticle { Lifetime = 0f, DieTime = dieTime, R = 255, G = 255 };
        p.B = (byte)(200 + random.RandomInt(0, 55));
        p.StartAlpha = 255;
        p.EndAlpha = 255;
        p.StartSize = B(random.RandomFloat(6.0f, 8.0f) * (8 - i) / 6 * flScale);
        p.EndSize = p.StartSize;
        p.Roll = random.RandomInt(0, 360);
        p.RollDelta = 0f;
        return p;
    };

    // The world-space player flashes' opening: the emitter, its box, the barrel's direction.
    private int WorldFlash(string name, uint handle, int attachment, out Vector origin, out Vector forward)
    {
        Drawn++;
        int emitter = fx.Create(FxEmitterKind.Simple, name, handle, attachment, 0);
        fx.DrawBeforeViewModel(emitter);
        reads.AttachmentTransform(handle, attachment, out origin, out QAngle angles);
        fx.SetBBox(emitter, FxMath.Sub(origin, new Vector(4, 4, 4)), FxMath.Add(origin, new Vector(4, 4, 4)));
        MoveVec.AngleVectors(new Vector(angles.Pitch, angles.Yaw, angles.Roll), out forward, out _, out _);
        return emitter;
    }

    private void ShotgunPlayer(uint handle, int attachment)
    {
        int emitter = WorldFlash("MuzzleFlash_Shotgun_Player", handle, attachment, out Vector origin, out Vector forward);
        try
        {
            float flScale = random.RandomFloat(1.25f, 1.5f);
            for (int i = 1; i < 6; i++)
            {
                Vector offset = FxMath.Add(origin, FxMath.Mul(forward, i * 8.0f * flScale));
                if (!AddSimple(emitter, Pick(PlayerFlash, 0, 3), offset, FlashCore(i, flScale, 0.0001f)))
                    return;
            }
        }
        finally
        {
            fx.Release(emitter);
        }
    }

    private void Magnum357Player(uint handle, int attachment)
    {
        int emitter = WorldFlash("MuzzleFlash_357_Player", handle, attachment, out Vector origin, out Vector forward);
        try
        {
            Vector offset = FxMath.Add(origin, FxMath.Mul(forward, 8.0f));
            if (!AddSimple(emitter, DustPuff, offset, () => Smoke(forward, 0.5f, 1.0f, 8.0f, 64.0f, 8.0f, 0.5f)))
                return;

            float flScale = random.RandomFloat(1.25f, 1.5f);
            for (int i = 1; i < 6; i++)
            {
                offset = FxMath.Add(origin, FxMath.Mul(forward, i * 8.0f * flScale));
                if (!AddSimple(emitter, Pick(PlayerFlash, 0, 3), offset, FlashCore(i, flScale, 0.01f)))
                    return;
            }
        }
        finally
        {
            fx.Release(emitter);
        }
    }

    // The smoke puff the revolver and the pistol leave: drifting forward and up, grey, growing.
    private FxSimpleParticle Smoke(in Vector forward, float dieLo, float dieHi, float speedLo, float speedHi, float grow, float rollDelta)
    {
        var p = new FxSimpleParticle { Lifetime = 0f };
        p.DieTime = random.RandomFloat(dieLo, dieHi);
        Vector v = FxMath.Mul(forward, random.RandomFloat(speedLo, speedHi));
        v.Z += random.RandomFloat(4.0f, 16.0f);
        p.Velocity = v;
        int color = random.RandomInt(200, 255);
        p.R = p.G = p.B = (byte)color;
        p.StartAlpha = (byte)random.RandomInt(64, 128);
        p.EndAlpha = 0;
        p.StartSize = (byte)random.RandomInt(2, 4);
        p.EndSize = B(p.StartSize * grow);
        p.Roll = random.RandomInt(0, 360);
        p.RollDelta = random.RandomFloat(-rollDelta, rollDelta);
        return p;
    }

    private void PistolPlayer(uint handle, int attachment)
    {
        int emitter = WorldFlash("MuzzleFlash_Pistol_Player", handle, attachment, out Vector origin, out Vector forward);
        try
        {
            Vector offset = FxMath.Add(origin, FxMath.Mul(forward, 8.0f));
            if (random.RandomInt(0, 3) != 0)
            {
                if (!AddSimple(emitter, DustPuff, offset, () => Smoke(forward, 0.25f, 0.5f, 48.0f, 64.0f, 4.0f, 0.1f)))
                    return;
            }

            float flScale = random.RandomFloat(1.0f, 1.25f);
            for (int i = 1; i < 6; i++)
            {
                offset = FxMath.Add(origin, FxMath.Mul(forward, i * 4.0f * flScale));
                if (!AddSimple(emitter, Pick(PlayerFlash, 0, 3), offset, FlashCore(i, flScale, 0.01f)))
                    return;
            }
        }
        finally
        {
            fx.Release(emitter);
        }
    }

    private void ShotgunNpc(uint handle, int attachment)
    {
        Drawn++;
        routines.MuzzleEffectAttached(0.75f, handle, attachment, false);

        if ((reads.MaterialsCached() & 1) == 0)
            return;

        if (!reads.RenderableAttachment(handle, attachment, out Vector origin, out QAngle angles))
            return;

        MoveVec.AngleVectors(new Vector(angles.Pitch, angles.Yaw, angles.Roll), out Vector forward, out _, out _);

        if (random.RandomInt(0, 2) == 0)
        {
            int embers = fx.Create(FxEmitterKind.Ember, "muzzle_embers", 0, 0, 0);
            try
            {
                fx.SetSortOrigin(embers, origin);
                int numEmbers = random.RandomInt(0, 4);
                for (int i = 0; i < numEmbers; i++)
                {
                    if (!AddSimple(embers, SmgFlash, origin, () =>
                        {
                            var p = new FxSimpleParticle { Lifetime = 0f };
                            p.DieTime = random.RandomFloat(0.2f, 0.4f);
                            Vector v = reads.VectorRandom(-0.05f, 0.05f);
                            v = FxMath.Add(v, forward);
                            FxMath.Normalize(ref v);
                            p.Velocity = FxMath.Mul(v, random.RandomFloat(64.0f, 256.0f));
                            p.R = 255;
                            p.G = 128;
                            p.B = 64;
                            p.StartAlpha = 255;
                            p.EndAlpha = 0;
                            p.StartSize = 1;
                            p.EndSize = 0;
                            p.Roll = 0;
                            p.RollDelta = 0;
                            return p;
                        }))
                        return;
                }
            }
            finally
            {
                fx.Release(embers);
            }
        }

        int trails = fx.Create(FxEmitterKind.Trail, "MuzzleFlash_Shotgun_NPC", 0, 0, 0);
        try
        {
            fx.SetSortOrigin(trails, origin);
            fx.SetTrailFlag(trails, TrailFade);
            fx.SetCollisionGravity(trails, 0.0f);

            int count = random.RandomInt(4, 8);
            for (int i = 0; i < count; i++)
            {
                int p = fx.Add(trails, SmgFlash, origin);
                if (p < 0)
                    return;

                var t = new FxTrailParticle { Lifetime = 0f };
                t.DieTime = random.RandomFloat(0.1f, 0.2f);
                const float spread = 0.05f;
                Vector v = reads.VectorRandom(-spread, spread);
                v = FxMath.Add(v, forward);
                FxMath.Normalize(ref v);
                FxMath.Normalize(ref forward);
                float dot = FxMath.Dot(forward, v);
                dot = (1.0f - MathF.Abs(dot)) / spread;
                t.Velocity = FxMath.Mul(v, random.RandomFloat(256.0f, 1024.0f) * (1.0f - dot));
                t.R = 255;
                t.G = 242;
                t.B = 191;
                t.A = 255;
                t.Length = 0.05f;
                t.Width = random.RandomFloat(0.25f, 0.5f);
                fx.SetTrail(trails, p, t);
            }
        }
        finally
        {
            fx.Release(trails);
        }
    }
}
