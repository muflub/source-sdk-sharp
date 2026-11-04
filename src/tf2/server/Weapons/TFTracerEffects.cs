//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Weapons;

/// <summary>
/// The rail tracer (<c>CTFPlayer::MaybeDrawRailgunBeam</c>,
/// <c>tf_player_shared.cpp:10094-10135</c>): the fired shot of an item with
/// <c>sniper_fires_tracer</c> draws a rail beam from the muzzle to the shot's
/// end, blue or red by the shooter's team, the classic rifle's mode
/// (<c>GetRifleType</c>, <c>tf_weapon_sniperrifle.h:116</c>, the item's
/// <c>set_weapon_mode</c>) taking the distortion trail instead. Stock hangs the
/// complex particle between the hacked muzzle position and the trace's end;
/// this side stretches it from the eyes along the aim for the shot's range,
/// the line the shot's own trace follows.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFTracerEffects)])]
[Precache(PrecacheKind.ParticleSystem, RailBlue)]
[Precache(PrecacheKind.ParticleSystem, RailRed)]
[Precache(PrecacheKind.ParticleSystem, DistortionTrail)]
public sealed class TFTracerEffects(IEnginePlayers players, IEngineEntities entities, IEnginePrecache precache,
                                    ITempEnts temps, ITFWeaponOwners owners, AttributeHooks? attributes = null)
    : ITFTracerEffects
{
    /// <summary>The blue team's rail (:10119).</summary>
    public const string RailBlue = "dxhr_sniper_rail_blue";

    /// <summary>The red team's rail (:10119).</summary>
    public const string RailRed = "dxhr_sniper_rail_red";

    /// <summary>The classic rifle's trail (:10123).</summary>
    public const string DistortionTrail = "tfc_sniper_distortion_trail";

    /// <summary><c>RIFLE_CLASSIC</c> (<c>tf_weapon_sniperrifle.h:28</c>).</summary>
    public const int RifleClassic = 3;

    private readonly AttributeHooks _attributes = attributes ?? new AttributeHooks();

    public void Tracer(in WeaponAttackContext attack, TFWeaponScript script)
    {
        EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
        if (!entities.IsValid(owner))
            return;

        // The name choice (:10117-10124): the classic mode's distortion trail,
        // else the rail in the shooter's team colour.
        int mode = _attributes.Int(0, "set_weapon_mode", owners.ItemOf(attack.Command.OwnerIndex, script.Name));
        string particle = mode == RifleClassic
            ? DistortionTrail
            : players.GetTeam(owner) == (int)TFTeam.Blue ? RailBlue : RailRed;

        // The beam's line: the eyes, and the shot's range along the aim.
        Vector start = entities.GetEyePosition(owner);
        Vector end = start + Angles.Forward(entities.GetEyeAngles(owner)) * script.Primary.Range;

        // Stock hangs the complex particle's control point at the trace's end
        // (:10127); here the beam's two ends ride the dispatch as its two
        // points — the muzzle at the first, the aim end at the second.
        TeEffectData effect = new()
        {
            Scale = 1f,
            HitBox = precache.ParticleSystemIndex(particle),
            Origin = start,
            Start = end,
        };
        temps.DispatchEffect(TeFilters.Pvs(start), 0f, "ParticleEffect", in effect);
    }
}
