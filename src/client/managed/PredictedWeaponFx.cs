//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Sounds;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// One predicted command's shot, as the client's replay saw it: who fired,
/// from where, which way, and the command's random seed.
/// </summary>
/// <param name="Player">The local player.</param>
/// <param name="PlayerIndex">Its entity index, which the shot's trace passes through.</param>
/// <param name="Eye">The predicted eye: the command's origin plus the view offset.</param>
/// <param name="ViewAngles">The command's view angles (pitch, yaw, roll).</param>
/// <param name="CommandRandomSeed">The command's <c>random_seed</c>.</param>
public readonly record struct PredictedShot(EntityHandle Player, int PlayerIndex, Vector Eye, Vector ViewAngles,
                                            int CommandRandomSeed);

/// <summary>What the predicting client does for a weapon's first prediction of a command.</summary>
public interface IPredictedWeaponFx
{
    /// <summary><c>WeaponSound</c> on the client: the sound, from the shooter, to the local player.</summary>
    void Sound(in PredictedShot shot, string soundScript);

    /// <summary>The bullets' traces and their impacts (decal, particles, the surface's sound).</summary>
    void FireBullets(in PredictedShot shot, in WeaponType type, int pellets, float spreadDegrees);

    /// <summary>
    /// The bullets' traces when the behaviour named a fixed pattern
    /// (<see cref="PelletPattern"/>): the same pellets the server's damage rode,
    /// so the predicted impacts land where it did. The default keeps the
    /// random-sample arm above, for a recording fx that never saw a pattern.
    /// </summary>
    void FireBullets(in PredictedShot shot, in WeaponType type, int pellets, float spreadDegrees,
                     PelletPattern? fixedPellets)
        => FireBullets(in shot, in type, pellets, spreadDegrees);

    /// <summary>A swing's trace and, when it lands, its impact.</summary>
    void MeleeSwing(in PredictedShot shot, in WeaponType type);

    /// <summary>
    /// The muzzle flash of a predicted shot: the script's muzzle-flash
    /// particle, on <paramref name="bearer"/> — the entity whose model carries
    /// the <c>muzzle</c> attachment (the viewmodel attachment, TF's gun on the
    /// hands) — stock's <c>CTFWeaponBase::CreateMuzzleFlashEffects</c> particle
    /// branch. Nothing by default: a test's recording fx and any composition
    /// without the registry keep the behaviour they had.
    /// </summary>
    void MuzzleFlash(EntityHandle bearer, string particleEffect)
    {
    }

    /// <summary>
    /// The shell a predicted shot ejects, from the viewmodel's ejector —
    /// stock's <c>OnFireEvent</c> for <c>"TF_EjectBrass"</c>: the effect's
    /// data carries the carried item's definition index and the weapon's own
    /// id beside the ejector's place. Nothing by default, as
    /// <see cref="MuzzleFlash"/>.
    /// </summary>
    void ShellEject(EntityHandle viewModel, int itemDefIndex, int weaponId)
    {
    }
}

/// <summary>
/// The client's half of stock's predicted weapon effects.
///
/// <para>
/// Stock runs the weapon's code on the predicting client too, and its
/// effects there are the shooter's own: <c>CBaseCombatWeapon::WeaponSound</c>
/// plays the shot (its filter's <c>UsePredictionRules</c> keeps it to the
/// first prediction), <c>CBaseEntity::FireBullets</c> traces each pellet from
/// the same seed the server uses and calls <c>DoImpactEffect</c> ->
/// <c>UTIL_ImpactTrace</c> -> <c>C_BaseEntity::ImpactTrace</c>, which
/// dispatches <c>"Impact"</c> to this client's own callback (the decal, the
/// flecks and <c>PlayImpactSound</c>'s surface sound), and a bludgeon's
/// <c>ImpactEffect</c> does the same with DMG_CLUB. The server's copies of
/// all of it skip this player (SourceSharp.Server's PredictionSuppression),
/// so nothing is heard twice.
/// </para>
///
/// <para>
/// <c>cl_predictweapons 0</c> turns it off here, and the server, reading the
/// same userinfo, then sends this player its copies again.
/// </para>
/// </summary>
public sealed class PredictedWeaponFx : IPredictedWeaponFx
{
    private readonly IClientEntitySounds _sounds;
    private readonly IClientEffectDispatch _effects;
    private readonly IEngineEffects _particles;
    private readonly IEngineWorldQuery _world;
    private readonly IEngineEntityRender _render;
    private readonly IEngineEntities _entities;
    private readonly AmmoCatalog _ammo;
    private readonly PredictWeaponsConVar _predictWeapons;
    private readonly SeededUniformStream _spread = new();

    // A splash's size, as stock's random->RandomFloat picks it: unseeded.
    private readonly Random _splashSize = new();

    // The entities a shot may stop on, by index: every networkable index but
    // the shooter's, built for the shooter it was last built for.
    private readonly EntityCandidates _candidates = new();

    public PredictedWeaponFx(IClientEntitySounds sounds, IClientEffectDispatch effects, IEngineEffects particles,
                             IEngineWorldQuery world,
                             IEngineEntityRender render, IEngineEntities entities,
                             AmmoCatalog ammo, PredictWeaponsConVar predictWeapons)
    {
        _sounds = sounds;
        _effects = effects;
        _particles = particles;
        _world = world;
        _render = render;
        _entities = entities;
        _ammo = ammo;
        _predictWeapons = predictWeapons;
    }

    /// <summary>Stock's MAX_TRACE_LENGTH, as the server's arms trace.</summary>
    public const float MaxTrace = 56756f;

    /// <summary>The impact's start, this far back along the shot: the server's ImpactBackoff.</summary>
    public const float ImpactBackoff = 32f;

    /// <summary><c>SURF_SKY</c>: UTIL_ImpactTrace marks nothing on the sky.</summary>
    public const int SurfSky = 0x0004;

    public void Sound(in PredictedShot shot, string soundScript)
    {
        if (!_predictWeapons.Value || soundScript.Length == 0)
            return;

        _sounds.EmitFrom(shot.Player, soundScript);
    }

    public void FireBullets(in PredictedShot shot, in WeaponType type, int pellets, float spreadDegrees)
        => FireBullets(in shot, in type, pellets, spreadDegrees, null);

    public void FireBullets(in PredictedShot shot, in WeaponType type, int pellets, float spreadDegrees,
                            PelletPattern? fixedPellets)
    {
        if (!_predictWeapons.Value || _ammo.For(type.AmmoName) is not AmmoType ammo)
            return;

        var aim = Angles.Vectors(shot.ViewAngles);
        float spread = ShotSpread.SpreadOf(spreadDegrees);
        int seed = ShotSpread.FirstSeed(shot.CommandRandomSeed);

        for (int i = 0; i < pellets; i++)
        {
            Vector dir = ShotSpread.Direction(aim, spread, seed + i, _spread, fixedPellets, i).Normalized;
            ImpactAlong(shot, dir, MaxTrace, ammo.DamageType, ammo);
        }
    }

    public void MeleeSwing(in PredictedShot shot, in WeaponType type)
    {
        if (!_predictWeapons.Value)
            return;

        ImpactAlong(shot, Angles.Vectors(shot.ViewAngles).Forward, type.MeleeRange, DamageType.Club);
    }

    /// <summary>
    /// <c>CTFWeaponBase::CreateMuzzleFlashEffects</c> (<c>tf_weaponbase.cpp:3113-3193</c>),
    /// particle branch, and its <c>DispatchMuzzleFlash</c> (:3195-3198): the
    /// script's particle starts on the entity that carries the gun — TF's
    /// viewmodel attachment, the c_model riding the hands, whose mesh names
    /// <c>muzzle</c> (the viewmodel itself is the class's arms and has none) —
    /// at that entity's <c>muzzle</c> attachment,
    /// <c>DispatchParticleEffect</c>'s <c>PATTACH_POINT_FOLLOW</c>. The
    /// particle seam is the client's owned system: one per bearer, the next
    /// shot replacing the last, the collection ending as the definition's
    /// duration runs out. Stock's dynamic light for the branch is commented
    /// out (:3150-3153), so there is none here either; the flash-model branch
    /// has no arm (no shipped weapon script names a
    /// <c>MuzzleFlashModel</c>).
    /// </summary>
    public void MuzzleFlash(EntityHandle bearer, string particleEffect)
    {
        if (!_predictWeapons.Value || particleEffect.Length == 0 || bearer == EntityHandle.None)
            return;

        int muzzle = _render.LookupAttachment(bearer, "muzzle");
        if (muzzle <= 0)
            return;

        if (_particles.ParticleSystemStart(bearer, particleEffect))
            _particles.ParticleSystemFollowEntity(bearer, 0, bearer, muzzle);
    }

    /// <summary>
    /// <c>CTFWeaponBase::OnFireEvent</c>'s brass (<c>tf_weaponbase.cpp:4950-4977</c>):
    /// the "eject_brass" attachment's place and angles, and the effect's data
    /// carries the item's definition index and the weapon's id — the effect
    /// picks the shell model by both. Stock's fallback to the event's
    /// <c>atoi(options)</c> attachment has no equivalent: the predicted ask
    /// carries no event options, so no ejector means no brass.
    /// </summary>
    public void ShellEject(EntityHandle viewModel, int itemDefIndex, int weaponId)
    {
        if (!_predictWeapons.Value)
            return;

        int eject = _render.LookupAttachment(viewModel, "eject_brass");
        if (eject <= 0 ||
            !_render.GetAttachment(viewModel, eject, out Vector origin, out Vector angles))
            return;

        var data = new EffectData
        {
            Origin = origin,
            Angles = angles,
            Scale = 1f,
            DamageType = itemDefIndex,
            HitBox = weaponId,
        };
        _effects.Dispatch("TF_EjectBrass", in data);
    }

    /// <summary>
    /// The shot's trace (MASK_SHOT, passing the shooter) and, where it
    /// stops, <c>UTIL_ImpactTrace</c>: nothing on the sky, else the
    /// <c>"Impact"</c> effect <c>C_BaseEntity::ImpactTrace</c> dispatches
    /// with the trace's surfaceprop, hitbox and entity.
    ///
    /// <para>
    /// TWO HALVES, because the client's world trace sees the world and nothing
    /// else (the shells are in no spatial partition): the world's answer, then
    /// the same ray up to that point clipped against every entity but the
    /// shooter. Without the second the round sailed through a prop, a brush
    /// entity or a player and marked the wall behind it, while the server's
    /// round stopped on the entity (measured: 2 of 8 AR2 rounds on ss_sandbox).
    /// </para>
    ///
    /// <para>
    /// EVERY INDEX, NOT THE DIRECTORY'S: the brush entities a round stops on
    /// (func_brush, func_wall_toggle, the buttons) have no managed client
    /// class, so the directory does not list them. The clip passes over what
    /// is not there or not solid.
    /// </para>
    /// </summary>
    private void ImpactAlong(in PredictedShot shot, Vector dir, float range, DamageType damageType,
                             AmmoType? round = null)
    {
        Vector end = shot.Eye + dir * range;
        ClientTraceResult tr = _world.ClientTraceLine(shot.Eye, end, TraceMask.Shot,
                                                      TraceFilter.Simple, shot.PlayerIndex);

        ClientTraceResult entity = _world.ClientClipRayToEntities(shot.Eye, tr.Hit ? tr.End : end,
                                                                  TraceMask.Shot, _candidates.AllBut(shot.PlayerIndex));
        if (entity.Hit)
            tr = entity;

        if (!tr.Hit)
            return;

        // A round that ends under water splashes where it went in, and marks
        // nothing below (HandleShotImpactingWater).
        if (round is { } ammo && (_world.PointContents(tr.End) & Liquid) != 0 && SplashInto(shot, end, in ammo))
            return;

        if ((tr.SurfaceFlags & SurfSky) != 0)
            return;

        var data = new EffectData
        {
            Origin = tr.End,
            Start = tr.End - dir * ImpactBackoff,
            Scale = 1f,
            SurfaceProp = tr.SurfaceProps,
            DamageType = (int)damageType,
            HitBox = tr.Hitbox,
            EntIndex = tr.EntityIndex,
        };

        _effects.Dispatch("Impact", in data);
    }

    /// <summary>
    /// HandleShotImpactingWater's client half: the shot again, stopping at the
    /// water (the round met nothing solid before it), and the gunshotsplash
    /// where it went in, the ammo's size, slime's own in slime.
    /// </summary>
    private bool SplashInto(in PredictedShot shot, Vector end, in AmmoType ammo)
    {
        ClientTraceResult surface = _world.ClientTraceLine(shot.Eye, end, TraceMask.BrushesAndWater,
                                                           TraceFilter.Simple, shot.PlayerIndex);
        Contents below = _world.PointContents(surface.End - new Vector(0f, 0f, 0.1f));
        if ((below & Liquid) == 0)
            return false;

        var data = new EffectData
        {
            Origin = surface.End,
            Normal = surface.Normal,
            Scale = ammo.MinSplashSize + (float)_splashSize.NextDouble() * (ammo.MaxSplashSize - ammo.MinSplashSize),
            Flags = (below & Contents.Slime) != 0 ? FxWaterInSlime : 0,
        };
        _effects.Dispatch("gunshotsplash", in data);
        return true;
    }

    /// <summary>CONTENTS_WATER | CONTENTS_SLIME.</summary>
    private const Contents Liquid = Contents.Water | Contents.Slime;

    /// <summary>FX_WATER_IN_SLIME (shareddefs.h).</summary>
    private const int FxWaterInSlime = 0x1;
}
