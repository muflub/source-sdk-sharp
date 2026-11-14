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
using TF2.Server.Projectiles;

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
                                   ITFWeaponOwners owners, AttributeHooks hooks,
                                   ILagCompensation lagCompensation, TFDamageScopes scopes,
                                   ITFPlayerConditions conditions, TFBlastApplier blast,
                                   ITFPlayerResupply health,
                                   IEngineEntityRender render, IEntityDirectory directory) : ITFMeleeEffects
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

    private readonly TFDamageScopes _scopes = scopes;

    private readonly TFBlastApplier _blast = blast;

    private readonly ITFWeaponOwners _owners = owners;

    private readonly AttributeHooks _hooks = hooks;

    private readonly ITFPlayerResupply _health = health;

    private readonly IEngineEntityRender _render = render;

    private readonly IEntityDirectory _directory = directory;

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
        lagCompensation.Start(owner);
        try
        {
            Smack(in attack, script, shot, in trace, owner);
        }
        finally
        {
            lagCompensation.Finish(owner);
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

            // CTFRobotArm::Smack's else (tf_weapon_wrench.cpp:502-518): a clean miss ends the combo.
            if (TFMeleeBehaviour.IsRobotArm(in script))
                attack.Weapon.BehaviourMode &= ~TFMeleeBehaviour.ComboCountMask;

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

        // GetMeleeDamage's health arms (tf_weaponbase_melee.cpp:1004-1015): the
        // swinger under half their maximum takes the held item's
        // mult_dmg_bonus_while_half_dead, at half or above its
        // mult_dmg_penalty_while_half_alive — each through the walk over the
        // melee's own item (stock's CALL_ATTRIB_HOOK_FLOAT over the owner's
        // health fraction). The honourbound floor below rides the outcome, as
        // stock's katana override rides its base call.
        if (entities.IsPlayer(owner))
        {
            IAttributeProvider? halfItem = _owners.ItemOf(attack.Command.OwnerIndex, script.Name);
            strike = entities.GetHealth(owner) < _health.MaxHealth(owner) * 0.5f
                ? _hooks.Float(strike, "mult_dmg_bonus_while_half_dead", halfItem)
                : _hooks.Float(strike, "mult_dmg_penalty_while_half_alive", halfItem);
        }

        if (string.Equals(script.Name, TFWeaponTypes.Katana, StringComparison.Ordinal)
            && entities.IsPlayer(target)
            && HonourBound(_hooks, _owners.ItemOf(attack.Command.OwnerIndex, script.Name))
            && HonourBound(_hooks, _owners.ActiveItemOf(target.EntryIndex)))
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
        if (script.Id == TFWeaponId.Sword)
            custom = _scopes.Push(TFDamageCustom.Decapitation, script.Id, script.Name);
        using (custom)
        using (hitGroups.Push(HitGroup.Generic, critical))
            damage.ApplyAt(target, owner, strike, type, force, end);

        // The disciplinary speed boost (tf_weaponbase_melee.cpp:646-656): a melee hit
        // that lands on a same-team player answers speed_buff_ally off the melee's
        // own item, and both players catch TF_COND_SPEED_BOOST — the one hit for two
        // seconds (:652), the swinger 3.6 ("give the soldier a bit of additional time
        // to allow them to keep up better with faster classes", :653). The disguise
        // half of the same-team gate (:646) waits with the disguise reads.
        if (entities.IsPlayer(target)
            && players.GetTeam(owner) == players.GetTeam(target)
            && _hooks.Int(0, "speed_buff_ally", _owners.ItemOf(attack.Command.OwnerIndex, script.Name)) > 0)
        {
            conditions.Add(target, TFCond.SpeedBoost, 2f);        // :652
            conditions.Add(owner, TFCond.SpeedBoost, 3.6f);       // :653
        }

        // ConnectedHit is an enemy or the world (tf_weaponbase_melee.cpp:959-966): a
        // teammate's shoulder leaves the Caber its one blast.
        if (players.GetTeam(target) != players.GetTeam(owner))
            Kaboom(in attack, script, owner, critical);

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
            conditions.Add(target, TFCond.Taunting, ForcedLaughSeconds);

        if (trace.TicklesSameWeapon && SameActiveItem(owner, target))
            conditions.Add(target, TFCond.Taunting, ForcedLaughSeconds);
    }

    /// <summary>
    /// The tickle's match (:911-919): both players' active weapons, both items,
    /// equal defindexes — null on either side answers no.
    /// </summary>
    private bool SameActiveItem(EntityHandle owner, EntityHandle target)
    {
        IAttributeProvider? mine = _owners.ActiveItemOf(entities.GetIndex(owner));
        IAttributeProvider? theirs = _owners.ActiveItemOf(entities.GetIndex(target));
        return mine is AttributeContainer { Item: { } myItem }
            && theirs is AttributeContainer { Item: { } theirItem }
            && myItem.Defindex == theirItem.Defindex;
    }

    /// <summary>
    /// The stickbomb's kaboom (<c>CTFStickBomb::Smack</c>'s tail,
    /// <c>shared/tf/tf_weapon_bottle.cpp:219-274</c>): once, on the connected
    /// smack after the melee hit it rode, the swing detonates — the blast from
    /// the swing's own shoot position (<c>vecSwingStart</c>; stock computes
    /// the swing end and never reads it), 75 through <c>mult_dmg</c> over a
    /// radius of 100 through <c>mult_explosion_radius</c>, the bits
    /// <c>DMG_BLAST | DMG_USEDISTANCEMOD | DMG_MELEE</c> carrying the swing's
    /// crit and the kill its <c>TF_DMG_CUSTOM_STICKBOMB_EXPLOSION</c>. The
    /// TE_TFExplosion is the client broadcast stock wraps it in; the broken
    /// body group its <c>m_bBroken</c> is the swap below, ahead of the blast
    /// as stock's <c>SwitchBodyGroups</c> is (:226-228) — the detonated
    /// attachment wears the exploded caber until the next deploy's dress
    /// answers the plain row model after the reset cleared the spent bit
    /// (:276-283).
    /// </summary>
    private void Kaboom(in WeaponAttackContext attack, TFWeaponScript script, EntityHandle owner, bool critical)
    {
        if (script.Id != TFWeaponId.Stickbomb || TFMeleeBehaviour.Spent(attack.Weapon))
            return;

        attack.Weapon.BehaviourMode |= TFMeleeBehaviour.ModeStickbombSpent;

        // Stock's SwitchBodyGroups (:226-228, ahead of the blast): the one
        // attachment riding this owner's hands wears the exploded caber for
        // the rest of the deploy; the next deploy's dress answers the plain
        // row model once the deploy's reset clears the spent bit (:276-283).
        int swingOwner = attack.Command.OwnerIndex;
        if (_directory.OfClass(EntityNames.SsViewmodelAttachment)
                .OfType<SsViewmodelAttachment>()
                .FirstOrDefault(a => a.State.OwnerIndex == swingOwner) is { } attached)
            _render.SetModel(attached.Handle, ExplodedCaber);

        IAttributeProvider? item = attack.Command.OwnerIndex > 0
            ? _owners.ItemOf(attack.Command.OwnerIndex, script.Name)
            : null;
        float damage = _hooks.Float(TFStickbombDamage, "mult_dmg", item);
        float radius = _hooks.Float(TFStickbombRadius, "mult_explosion_radius", item);

        _blast.Apply(players.GetShootOrigin(owner),
                    TFBlast.Of(damage, radius, DamageType.Blast | TFDamageBits.UseDistanceMod | TFDamageBits.Melee),
                    owner, owner, ignored: EntityHandle.None, script.Id, script.Name, critical,
                    custom: TFDamageCustom.StickbombExplosion);
    }

    /// <summary>The kaboom's damage, stock's <c>flDamage = 75.0f</c> (:261).</summary>
    public const float TFStickbombDamage = 75f;

    /// <summary>The kaboom's radius, stock's <c>flRadius = 100.f</c> (:266).</summary>
    public const float TFStickbombRadius = 100f;

    /// <summary>Stock's <c>TF_WEAPON_STICKBOMB_BROKEN_MODEL</c> (<c>tf_weapon_bottle.h:98</c>).</summary>
    public const string ExplodedCaber = "models/workshop/weapons/c_models/c_caber/c_caber_exploded.mdl";

    private bool HonourBound(AttributeHooks hooks, IAttributeProvider? item)
        => item is not null && hooks.Int(0, TFSwordHook.HonorBoundAttrib, item) != 0;

    /// <summary><c>crit_from_behind</c>'s dot, over the origins and the victim's eye angles (:823-829).</summary>
    private bool FacingAway(EntityHandle owner, EntityHandle victim)
    {
        Vector toVictim = entities.GetOrigin(victim) - entities.GetOrigin(owner);
        return toVictim.Normalized.Dot(Angles.Forward(entities.GetEyeAngles(victim))) > 0.7071f;
    }
}
