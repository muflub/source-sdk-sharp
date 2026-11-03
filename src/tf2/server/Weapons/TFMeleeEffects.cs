//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Server.Weapons;

/// <summary>
/// The melee arm of the weapon effects (<c>ITFWeaponEffects.Melee</c>): what
/// stock's smack (<c>CTFWeaponBaseMelee::Smack</c>,
/// <c>shared/tf/tf_weaponbase_melee.cpp:702-790</c>) does at its time — the
/// swing's trace, and on a hit, the hit itself.
///
/// <para>
/// The trace is <c>DoSwingTraceInternal</c> (:392-540): a MASK_SOLID line
/// from the eye along the eye angles over the smack's reach first; when the
/// line met nothing, the smack's hull box swept over the same
/// reach. Stock's sapper arm (:469-494) waits for the wrench and its
/// <c>set_dmg_apply_to_sapper</c> attribute, its MvM teammate filter
/// (:429, :499-519) has no MvM to run in, and its
/// <c>FindHullIntersection</c> refinement (:521-534) needs a brush-model
/// read the world seam does not carry (docs/plans/tf2/deferred.tsv).
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFMeleeEffects)])]
public sealed class TFMeleeEffects(IEngineEntities entities, IEnginePlayers players,
                                   IEngineWorldQuery world, IEngineDamage damage,
                                   DamageHitGroups hitGroups, ILog log) : ITFMeleeEffects
{
    /// <summary><c>tf_meleeattackforcescale</c>'s 80 (<c>tf_weaponbase_melee.cpp:50</c>).</summary>
    public const float ForceScale = 80f;

    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace)
    {
        EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
        if (!entities.IsValid(owner))
            return;

        // The eye along the eye angles, over the smack's reach (:408-425).
        Vector from = players.GetShootOrigin(owner);
        Vector forward = Angles.Forward(entities.GetEyeAngles(owner));
        Vector to = from + forward * trace.Range;

        TraceResult line = world.TraceLine(from, to, TraceMask.Solid, owner);
        if (line.Hit)
        {
            Hit(in attack, script, owner, forward, shot, in trace, line.Entity, line.End, "line");
            return;
        }

        var half = new Vector(trace.Hull, trace.Hull, trace.Hull);
        SurfaceTraceResult hull = world.TraceBox(from, to, half * -1f, half, TraceMask.Solid, owner);
        if (!hull.Hit)
        {
            log.Msg($"[TF2/Melee] swing by {owner} met nothing (from {from} to {to})\n");
            return;
        }

        Hit(in attack, script, owner, forward, shot, in trace, hull.Entity, hull.End, "hull");
    }

    /// <summary>
    /// The hit (<c>OnSwingHit</c>, :553-694): the hit sound —
    /// <c>MELEE_HIT</c> for a player, <c>MELEE_HIT_WORLD</c> for the world
    /// and everything else (:566-611, :686-689) — then
    /// <c>DoMeleeDamage</c> (:797-967), the row's damage at the contact with
    /// the melee type and the melee force — whatever the trace stopped on, a
    /// brush with no entity behind it included (:846-874: the trace-attack
    /// and its impulse run for the world-stop too).
    /// </summary>
    private void Hit(in WeaponAttackContext attack, TFWeaponScript script, EntityHandle owner, Vector forward, in ShotDamage shot,
                     in SwingTrace trace, EntityHandle target, Vector end, string how)
    {
        WeaponScriptSound sound = entities.IsPlayer(target) ? WeaponScriptSound.MeleeHit
                                                            : WeaponScriptSound.MeleeHitWorld;
        string named = script.Sound(sound);
        log.Msg($"[TF2/Melee] swing by {owner} {how}-hit {target} at {end} ({named})\n");
        if (attack.EffectsLive && named.Length > 0)
            attack.Arms.Sound(named);

        // The melee type (DMG_MELEE | DMG_NEVERGIB | DMG_CLUB, :817) and the
        // melee force: CalculateMeleeDamageForce at 1/flDamage * the force
        // scale (:867-874), nothing when the damage is under a point.
        DamageType type = TFDamageBits.Melee | DamageType.NeverGib | DamageType.Club;

        // crit_from_behind (tf_weaponbase_melee.cpp:819-833): the victim
        // facing along the way we came — the unit from us to them against
        // their eye forward over cos 45 — crits the hit.
        bool critical = shot.Critical
                     || (trace.CritFromBehind && FacingAway(owner, target));

        Vector force = MathF.Abs(shot.Damage) >= 1f
            ? DamageForceRules.MeleeForce(forward, shot.Damage, scale: ForceScale / shot.Damage)
            : default;

        using (hitGroups.Push(HitGroup.Generic, critical))
            damage.ApplyAt(target, owner, shot.Damage, type, force, end);
    }

    /// <summary><c>crit_from_behind</c>'s dot, over the origins and the victim's eye angles (:823-829).</summary>
    private bool FacingAway(EntityHandle owner, EntityHandle victim)
    {
        Vector toVictim = entities.GetOrigin(victim) - entities.GetOrigin(owner);
        return toVictim.Normalized.Dot(Angles.Forward(entities.GetEyeAngles(victim))) > 0.7071f;
    }
}
