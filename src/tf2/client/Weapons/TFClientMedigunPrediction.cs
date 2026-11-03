//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Client.Weapons;

/// <summary>
/// The medigun's replayed half: the target pick stock runs inside
/// <c>CWeaponMedigun::ItemPostFrame</c> on BOTH sides
/// (tf_weapon_medigun.cpp:1214, FindAndHealTargets's maintain-or-find), so the
/// client's prediction of a command that locks the beam lands on the same
/// patient and the same clocks the server's word will name — without it the
/// lock command mispredicts <c>HealTargetIndex</c> and the clocks with it. The
/// arm reads and writes the declared states only: the carrier's beam index,
    /// the weapon's mode word (the press edge) and its clocks.
///
/// <para>
/// The server's own arm (TFMedigunWeapon, src/tf2/server/Medigun) is the same
/// search over the seam's handles; this is its client image, fed from the
/// prediction's eye and the networked world. What the server keeps off the
/// wire the replay does without: the holstered arm (the machine ticks the
/// active weapon alone, so a replayed frame is never holstered — the server's
/// <c>m_bHolstered</c> check, tf_weapon_medigun.cpp:1575) and the release's
/// detached-patient list (server state, no predicted word).
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFMedigunPrediction)])]
public sealed class TFClientMedigunPrediction(IEngineWorldQuery world, IEngineEffects effects,
                                              IClientStandalones standalones, ITFPlayerData data,
                                              TFClientPlayerResources resources,
                                              TFMedigunAutoHealConVar? autoHeal = null)
    : ITFMedigunPrediction
{
    // The stick rule and the check gate, as the server's arm keeps them
    // (GetStickRange's 1.2 and TargetCheckInterval's second).
    private const float StickRangeMult = 1.2f;
    private const float TargetCheckInterval = 1f;

    // The entity indices the clips run against: every index from 1 but the
    // tracer's own, as the arsenal's. Built once per tracer and kept.
    private readonly int[] _candidates = new int[EngineLimits.MaxEdicts - 2];
    private int _builtFor = -1;

    /// <summary>One command's frame: the press edge, then maintain-or-find.</summary>
    public void Frame(in WeaponAttackContext attack, TFWeaponScript script, bool held)
    {
        var weapon = attack.Weapon;
        int owner = attack.Command.OwnerIndex;

        // The press edge reads the mode word's bit — the client's image of the
        // server's m_bCanChangeTarget (:1555), published beside the clocks — and
        // writes the bit back, as the server's arm records its own.
        bool attack1Down = (weapon.BehaviourMode & ITFMedigunPrediction.Attack1Down) != 0;
        bool press = held && !attack1Down;
        weapon.BehaviourMode = held ? ITFMedigunPrediction.Attack1Down : 0;

        if (data.Predicted(owner) is not { } carrier)
            return;

        if (autoHeal is { Value: true })
        {
            // The toggle arms (:1555-1583): a press drops the beam and the
            // frame re-aims in the same command — the next if re-reads the
            // target the drop emptied; a taunting medic's beam drops and does
            // not re-fire.
            if (press && carrier.HealTargetIndex >= 0)
                Drop(attack, carrier);

            if (carrier.HealTargetIndex >= 0 && carrier.InCond(TFCond.Taunting))
                Drop(attack, carrier);
            else if (carrier.HealTargetIndex >= 0 || held)
                Pick(in attack, carrier, script.Primary);
        }
        else if (held)
            Pick(in attack, carrier, script.Primary);
        else if (carrier.HealTargetIndex >= 0)
            Drop(attack, carrier);
    }

    /// <summary>
    /// <c>PrimaryAttack</c> (:1752) through <c>FindAndHealTargets</c> (:1214):
    /// maintain the beam on a living patient or find a new one — never both,
    /// and a stale patient is dropped before the find (:1233, :720).
    /// </summary>
    private void Pick(in WeaponAttackContext attack, TFPlayerNetState carrier, TFWeaponModeData mode)
    {
        // No eye, no aim: a replay told none has no trace to run.
        if (!attack.Arms.Aim(out Vector eye, out Vector forward))
            return;

        int current = carrier.HealTargetIndex;
        if (current >= 0 && IsAlive(current))
            Maintain(in attack, carrier, mode, current, eye, forward);
        else
        {
            // FindNewTargetInSlot clears the stale beam itself (:720-723) before
            // it traces; the replay's drop carries the same two predicted words.
            if (current >= 0)
                Drop(attack, carrier);
            FindNew(in attack, carrier, mode, eye, forward);
        }
    }

    /// <summary>
    /// <c>MaintainTargetInSlot</c> (:650): the stick rule, then the three
    /// visibility traces once a second — the gate and its reset live here
    /// (:671-674), not around the call.
    /// </summary>
    private void Maintain(in WeaponAttackContext attack, TFPlayerNetState carrier, TFWeaponModeData mode,
                          int target, Vector eye, Vector forward)
    {
        float now = attack.Now;

        // The stick rule: the beam holds to 1.2x the script range and no
        // further (:669), measured to the patient's centre.
        if ((eye - CenterOf(target)).Length >= mode.Range * StickRangeMult)
        {
            Drop(attack, carrier);
            return;
        }
        if (now < attack.Weapon.BehaviourTimeC)
            return;                                    // :671-672: between checks the beam holds untraced
        attack.Weapon.BehaviourTimeC = now + TargetCheckInterval;    // :674

        Vector end = eye + forward * mode.Range;

        // Trace 1: the aim line, hull — the target must still be what it hits.
        var aim = LineAhead(in attack, eye, end, TraceMask.ShotHull);
        if (aim.Hit && aim.EntityIndex == target)
            return;

        // Traces 2 and 3: to the target's centre and its eyes — nothing but
        // the target and the medic's own team may stand in the way (:685-698).
        if (Past(in attack, target, eye, CenterOf(target))
            && Past(in attack, target, eye, EyeOf(target)))
            return;

        Drop(attack, carrier);
    }

    /// <summary>
    /// <c>FindNewTargetForSlot</c> (:713): the lenient hull trace
    /// (<c>MASK_SHOT &amp; ~CONTENTS_HITBOX</c>) gives the candidate; the hitbox
    /// trace replaces it only with a different allowed entity (:733-750), so a
    /// refused nearest hitbox leaves the hull target standing.
    /// </summary>
    private void FindNew(in WeaponAttackContext attack, TFPlayerNetState carrier, TFWeaponModeData mode,
                         Vector eye, Vector forward)
    {
        Vector end = eye + forward * mode.Range;

        var loose = LineAhead(in attack, eye, end, TraceMask.ShotHull);
        if (!loose.Hit)
            return;
        int candidate = loose.EntityIndex;

        var hitbox = LineAhead(in attack, eye, end, TraceMask.Shot);
        if (hitbox.Hit && hitbox.EntityIndex != candidate && Allowed(in attack, carrier, hitbox.EntityIndex))
            candidate = hitbox.EntityIndex;
        else if (!Allowed(in attack, carrier, candidate))
            return;

        // StartHealingTarget's predicted half (:1018's carrier word) and the
        // check clock the find sets with it (:765).
        carrier.HealTargetIndex = candidate;
        attack.Weapon.BehaviourTimeC = attack.Now + TargetCheckInterval;
    }

    /// <summary>
    /// <c>RemoveHealingTarget</c>'s (:1655) predicted words: the beam index and
    /// the clock the meter builds on (:1726's tail). The release's detached
    /// list and the last-target memory are the server's own.
    /// </summary>
    private static void Drop(in WeaponAttackContext attack, TFPlayerNetState carrier)
    {
        carrier.HealTargetIndex = -1;
        attack.Weapon.BehaviourTimeA = -1f;
    }

    /// <summary>
    /// <c>AllowedToHealTarget</c> (:563): alive, visible (or the healer itself
    /// cloaked), own team — or an enemy under <c>TF_COND_DISGUISED</c> whose
    /// disguise team is the medic's (:590-599).
    /// </summary>
    private bool Allowed(in WeaponAttackContext attack, TFPlayerNetState carrier, int targetIndex)
    {
        if (standalones.GetByIndex(targetIndex) is not ClientSsPlayer target || !target.State.Alive)
            return false;

        var targetState = data.Networked(targetIndex);
        if ((targetState?.IsStealthed() ?? false) && !carrier.IsStealthed())
            return false;

        int ownerTeam = TeamOf(attack.Command.OwnerIndex);
        return TeamOf(targetIndex) == ownerTeam
            || ((targetState?.InCond(TFCond.Disguised) ?? false)
                && (targetState?.DisguiseTeam ?? (int)TFTeam.Unassigned) == ownerTeam);
    }

    /// <summary>The beam's patient on its feet — stock's maintain/find split checks it (:1233).</summary>
    private bool IsAlive(int index)
        => standalones.GetByIndex(index) is ClientSsPlayer player && player.State.Alive;

    /// <summary>
    /// The past-traces' filter, <c>CMedigunTraceFilter</c>'s own-team walk
    /// (tf_weapon_medigun.cpp:629-648): the trace ignores the medic's teammates
    /// and walks through, so it re-launches past each — an enemy, the world or
    /// a prop stops it. Four hops, as the server's arm.
    /// </summary>
    private bool Past(in WeaponAttackContext attack, int target, Vector from, Vector to)
    {
        int owner = attack.Command.OwnerIndex;
        int ownerTeam = TeamOf(owner);
        Vector start = from;
        for (int hop = 0; hop < 4; hop++)
        {
            var hit = LineAhead(in attack, start, to, TraceMask.Shot);
            if (!hit.Hit || hit.EntityIndex == target)
                return true;
            if (standalones.GetByIndex(hit.EntityIndex) is not ClientSsPlayer
                || TeamOf(hit.EntityIndex) != ownerTeam)
                return false;           // an enemy, the world or a prop: the beam is blocked
            start = hit.End;            // a teammate: the filter ignores them, re-trace past
        }
        return false;
    }

    /// <summary>A line from the medic as the server's would stop: the world's trace, then the entities before its stop.</summary>
    private ClientTraceResult LineAhead(in WeaponAttackContext attack, Vector from, Vector to, TraceMask mask)
    {
        int owner = attack.Command.OwnerIndex;
        var worldHit = world.ClientTraceLine(from, to, mask, TraceFilter.Simple, owner);
        var entityHit = world.ClientClipRayToEntities(from, worldHit.Hit ? worldHit.End : to, mask,
                                                      Candidates(owner));
        return entityHit.Hit ? entityHit : worldHit;
    }

    /// <summary>The entity's world centre: the model bounds' midpoint over the origin, stock's own.</summary>
    private Vector CenterOf(int index)
    {
        if (world.ClientEntityModelBounds(index, out Vector mins, out Vector maxs, out _)
            && effects.ClientEntityOrigin(index, out Vector origin))
            return origin + (mins + maxs) * 0.5f;

        return standalones.GetByIndex(index) is ClientSsPlayer player ? player.State.Origin : default;
    }

    /// <summary>The entity's eyes: origin plus view offset, the networked word (stock's GetEyePosition).</summary>
    private Vector EyeOf(int index)
        => standalones.GetByIndex(index) is ClientSsPlayer player
            ? player.State.Origin + player.State.ViewOffset
            : CenterOf(index);

    /// <summary>The player's team from the scoreboard, 0 outside its slots.</summary>
    private int TeamOf(int playerIndex)
        => playerIndex > 0 && playerIndex < TFPlayerResourceNetState.Slots
            ? resources.Live?.State.Team[playerIndex] ?? 0
            : 0;

    private ReadOnlySpan<int> Candidates(int tracerIndex)
    {
        if (_builtFor != tracerIndex)
        {
            int count = 0;
            for (int index = 1; index < EngineLimits.MaxEdicts && count < _candidates.Length; index++)
            {
                if (index != tracerIndex)
                    _candidates[count++] = index;
            }

            _builtFor = tracerIndex;
        }

        return _candidates;
    }
}
