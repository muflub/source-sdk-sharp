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

    /// <summary>A swing's trace and, when it lands, its impact.</summary>
    void MeleeSwing(in PredictedShot shot, in WeaponType type);
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
    private readonly IEngineWorldQuery _world;
    private readonly AmmoCatalog _ammo;
    private readonly PredictWeaponsConVar _predictWeapons;
    private readonly SeededUniformStream _spread = new();

    // The entities a shot may stop on, by index: every networkable index but
    // the shooter's, built for the shooter it was last built for.
    private readonly int[] _candidates = new int[EngineLimits.MaxEdicts - 2];
    private int _candidatesFor = -1;

    public PredictedWeaponFx(IClientEntitySounds sounds, IClientEffectDispatch effects, IEngineWorldQuery world,
                             AmmoCatalog ammo, PredictWeaponsConVar predictWeapons)
    {
        _sounds = sounds;
        _effects = effects;
        _world = world;
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
    {
        if (!_predictWeapons.Value || _ammo.For(type.AmmoName) is not AmmoType ammo)
            return;

        var aim = Angles.Vectors(shot.ViewAngles);
        float spread = ShotSpread.SpreadOf(spreadDegrees);
        int seed = ShotSpread.FirstSeed(shot.CommandRandomSeed);

        for (int i = 0; i < pellets; i++)
        {
            Vector dir = ShotSpread.Apply(aim, spread, seed + i, _spread).Normalized;
            ImpactAlong(shot, dir, MaxTrace, ammo.DamageType);
        }
    }

    public void MeleeSwing(in PredictedShot shot, in WeaponType type)
    {
        if (!_predictWeapons.Value)
            return;

        ImpactAlong(shot, Angles.Vectors(shot.ViewAngles).Forward, type.MeleeRange, DamageType.Club);
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
    private void ImpactAlong(in PredictedShot shot, Vector dir, float range, DamageType damageType)
    {
        Vector end = shot.Eye + dir * range;
        ClientTraceResult tr = _world.ClientTraceLine(shot.Eye, end, TraceMask.Shot,
                                                      TraceFilter.Simple, shot.PlayerIndex);

        ClientTraceResult entity = _world.ClientClipRayToEntities(shot.Eye, tr.Hit ? tr.End : end,
                                                                  TraceMask.Shot, Candidates(shot.PlayerIndex));
        if (entity.Hit)
            tr = entity;

        if (!tr.Hit || (tr.SurfaceFlags & SurfSky) != 0)
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

    /// <summary>Every entity index from 1 but the shooter's.</summary>
    private ReadOnlySpan<int> Candidates(int shooterIndex)
    {
        if (_candidatesFor != shooterIndex)
        {
            int count = 0;
            for (int index = 1; index < EngineLimits.MaxEdicts && count < _candidates.Length; index++)
            {
                if (index != shooterIndex)
                    _candidates[count++] = index;
            }

            _candidatesFor = shooterIndex;
        }

        return _candidates;
    }
}
