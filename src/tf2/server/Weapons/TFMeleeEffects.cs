//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Player;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Weapons;

/// <summary>
/// The melee arm of the weapon effects (<c>ITFWeaponEffects.Melee</c>): what
/// stock's smack (<c>CTFWeaponBaseMelee::Smack</c>,
/// <c>shared/tf/tf_weaponbase_melee.cpp:702-790</c>) does at its time — the
/// swing's trace, and on a hit, the hit itself, inside the lag compensation
/// stock's smack opens around both.
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
                                   DamageHitGroups hitGroups, ILog log,
                                   ITFWeaponOwners? owners = null, AttributeHooks? hooks = null,
                                   ILagCompensation? lagCompensation = null, TFDamageScopes? scopes = null,
                                   ITFPlayerConditions? conditions = null) : ITFMeleeEffects
{
    /// <summary><c>tf_meleeattackforcescale</c>'s 80 (<c>tf_weaponbase_melee.cpp:50</c>).</summary>
    public const float ForceScale = 80f;

    /// <summary>
    /// The forced laugh's window: the base taunt scene and its tail
    /// (<see cref="TFTauntCommand.BaseTauntSceneSeconds"/> plus
    /// <see cref="TFTauntCommand.TauntTailSeconds"/>) — stock's
    /// <c>Taunt(TAUNT_MISC_ITEM, MP_CONCEPT_TAUNT_LAUGH)</c> rides the
    /// scene's length, this side's fallback the taunt command documents
    /// stands in until the gesture's own sequence length arrives.
    /// </summary>
    public const float ForcedLaughSeconds = TFTauntCommand.BaseTauntSceneSeconds + TFTauntCommand.TauntTailSeconds;

    private readonly TFDamageScopes? _scopes = scopes;

    private readonly ITFWeaponOwners? _owners = owners;

    private readonly AttributeHooks? _hooks = hooks;

    /// <summary>Stock's <c>looseSwingRange</c> over <c>GetSwingRange</c> (<c>tf_weaponbase_melee.cpp:309</c>).</summary>
    public const float LooseSwingScale = 1.2f;

    private readonly Dictionary<int, List<EntityHandle>> potentialVictims = new();

    public void MeleeSwingStarted(in WeaponAttackContext attack, float swingRange)
    {
        int ownerIndex = attack.Command.OwnerIndex;
        if (!potentialVictims.TryGetValue(ownerIndex, out List<EntityHandle>? victims))
            potentialVictims[ownerIndex] = victims = new List<EntityHandle>();

        victims.Clear();
        EntityHandle owner = players.GetHandle(ownerIndex);
        if (!entities.IsValid(owner))
            return;

        int team = players.GetTeam(owner);
        Vector eye = players.GetShootOrigin(owner);
        float reachSquared = LooseSwingScale * swingRange * (LooseSwingScale * swingRange);
        for (int index = 1; index < TFPlayerResourceNetState.Slots; index++)
        {
            EntityHandle player = players.GetHandle(index);
            int theirs = entities.IsValid(player) ? players.GetTeam(player) : 0;
            if (theirs != team && theirs is (int)TFTeam.Red or (int)TFTeam.Blue && players.IsAlive(player)
                && (entities.GetWorldSpaceCenter(player) - eye).LengthSquared < reachSquared)
                victims.Add(player);
        }
    }

    /// <summary>
    /// Stock's clean miss (:742-753): nobody the swing started near is still
    /// alive, so nobody was dodging it.
    /// </summary>
    private bool IsCleanMiss(int ownerIndex)
    {
        if (!potentialVictims.TryGetValue(ownerIndex, out List<EntityHandle>? victims))
            return true;

        foreach (EntityHandle victim in victims)
        {
            if (entities.IsValid(victim) && players.IsAlive(victim))
                return false;
        }

        return true;
    }

    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace)
    {
        EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
        if (!entities.IsValid(owner))
            return;

        // Smack's lag compensation (:711-712, :788): the other players where
        // the swinger saw them for the swing's traces and its hit.
        lagCompensation?.Start(owner);
        try
        {
            Smack(in attack, script, shot, in trace, owner);
        }
        finally
        {
            lagCompensation?.Finish(owner);
        }
    }

    private void Smack(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace,
                       EntityHandle owner)
    {
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

            // hit_self_on_miss (:755-763): a clean miss hurts its own owner at half the melee
            // blow — DoMeleeDamage's half-modifier path (:802-835), the same melee bits and
            // force, the miss's own contact position, and the swing's crit (:855-858).
            if (trace.SelfOnMiss && IsCleanMiss(attack.Command.OwnerIndex))
            {
                float halfDamage = shot.Damage * 0.5f;
                DamageType missTaxType = TFDamageBits.Melee | DamageType.NeverGib | DamageType.Club;
                Vector missTaxForce = MathF.Abs(halfDamage) >= 1f
                    ? DamageForceRules.MeleeForce(forward, halfDamage, scale: ForceScale / halfDamage)
                    : default;
                using (hitGroups.Push(HitGroup.Generic, shot.Critical))
                    damage.ApplyAt(owner, owner, halfDamage, missTaxType, missTaxForce, hull.End);
            }
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

        // The charge's mini-crit (tf_weaponbase_melee.cpp:860-863): the swing's
        // mini latch rides the type as DMG_RADIUS_MAX, the modify rules'
        // sword/bottle/wrench arm's marker — under the roll's own crit, as
        // stock's else-if.
        if (shot.Mini && !shot.Critical)
            type |= TFDamageBits.RadiusMax;

        // crit_from_behind (tf_weaponbase_melee.cpp:819-833): the victim
        // facing along the way we came — the unit from us to them against
        // their eye forward over cos 45 — crits the hit.
        bool critical = shot.Critical
                     || (trace.CritFromBehind && FacingAway(owner, target));

        // The honourbound duel (CTFKatana::GetMeleeDamage,
        // tf_weapon_sword.cpp:548-571): an honourbound katana meeting a
        // victim whose own held weapon is honourbound too strikes for no
        // less than thrice the victim's health, and the blow stays out of
        // their crit-rate accounting (DMG_DONT_COUNT_DAMAGE_TOWARDS_CRIT_RATE,
        // tf_shareddefs.h:1171).
        float strike = shot.Damage;
        if (string.Equals(script.Name, TFWeaponTypes.Katana, StringComparison.Ordinal)
            && _owners is { } owners && _hooks is { } hooks && entities.IsPlayer(target)
            && HonourBound(hooks, owners.ItemOf(attack.Command.OwnerIndex, script.Name))
            && HonourBound(hooks, owners.ActiveItemOf(target.EntryIndex)))
        {
            strike = MathF.Max(strike, entities.GetHealth(target) * TFSwordHook.DuelHealthScale);
            type |= TFDamageBits.DontCountCritRate;
        }

        Vector force = MathF.Abs(strike) >= 1f
            ? DamageForceRules.MeleeForce(forward, strike, scale: ForceScale / strike)
            : default;

        // The Gunslinger's count (CTFRobotArm::Smack, tf_weapon_wrench.cpp:502-518):
        // an enemy player hit counts toward the third's big swing and stamps the
        // clock the grant's timeout reads; a world hit or a teammate resets. The
        // third hit's count folds back to none, its swing already gone.
        if (TFMeleeBehaviour.IsRobotArm(in script))
        {
            int counted = entities.IsPlayer(target) && players.GetTeam(target) != players.GetTeam(owner)
                ? TFMeleeBehaviour.ComboOf(attack.Weapon) + 1
                : 0;
            if (counted == 3)
                counted = 0;

            if (counted > 0)
                attack.Weapon.BehaviourTimeB = attack.Now;
            attack.Weapon.BehaviourMode = (attack.Weapon.BehaviourMode & ~TFMeleeBehaviour.ComboCountMask)
                                        | (counted << (TFMeleeBehaviour.ComboCountShift));
        }


        // The decapitators' damage custom (CTFDecapitationMeleeWeaponBase::
        // GetMeleeDamage, tf_weapon_sword.cpp:77-84): every sword hit is
        // TF_DMG_CUSTOM_DECAPITATION, so the kill record names the swing.
        IDisposable? custom = null;
        if (script.Id == TFWeaponId.Sword && _scopes is { } scopes)
            custom = scopes.Push(TFDamageCustom.Decapitation, script.Id, script.Name);
        using (custom)
        using (hitGroups.Push(HitGroup.Generic, critical))
            damage.ApplyAt(target, owner, strike, type, force, end);

        // The Holiday Punch's two laugh arms, stock's non-truce block after the
        // hit (tf_weaponbase_melee.cpp:882-927). Both need a living player on
        // the other team; the forced laugh additionally rides the swing's crit
        // (:886), the tickle compares the two players' active items
        // (:911-919). Stock's truce gate and its CanBeForcedToLaugh stun checks
        // wait with the other condition reads on the deferred list.
        if (!attack.EffectsLive || !entities.IsPlayer(target) || !players.IsAlive(target)
            || players.GetTeam(owner) == players.GetTeam(target))
            return;

        if (trace.ForcesLaugh && critical)
            conditions?.Add(target, TFCond.Taunting, ForcedLaughSeconds);

        if (trace.TicklesSameWeapon && SameActiveItem(owner, target))
            conditions?.Add(target, TFCond.Taunting, ForcedLaughSeconds);
    }

    /// <summary>
    /// The tickle's match (:911-919): both players' active weapons, both items,
    /// equal defindexes — null on either side answers no.
    /// </summary>
    private bool SameActiveItem(EntityHandle owner, EntityHandle target)
    {
        if (_owners is null)
            return false;

        IAttributeProvider? mine = _owners.ActiveItemOf(entities.GetIndex(owner));
        IAttributeProvider? theirs = _owners.ActiveItemOf(entities.GetIndex(target));
        return mine is AttributeContainer { Item: { } myItem }
            && theirs is AttributeContainer { Item: { } theirItem }
            && myItem.Defindex == theirItem.Defindex;
    }

    private bool HonourBound(AttributeHooks hooks, IAttributeProvider? item)
        => item is not null && hooks.Int(0, TFSwordHook.HonorBoundAttrib, item) != 0;

    /// <summary><c>crit_from_behind</c>'s dot, over the origins and the victim's eye angles (:823-829).</summary>
    private bool FacingAway(EntityHandle owner, EntityHandle victim)
    {
        Vector toVictim = entities.GetOrigin(victim) - entities.GetOrigin(owner);
        return toVictim.Normalized.Dot(Angles.Forward(entities.GetEyeAngles(victim))) > 0.7071f;
    }
}
