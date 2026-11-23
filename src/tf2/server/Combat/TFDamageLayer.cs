//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Buildings;
using TF2.Server.Medigun;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Combat;

/// <summary>
/// The TF damage layer (Stage 2.7): the one <see cref="IDamageModifier"/> TF
/// registers, standing where stock's <c>CTFPlayer::OnTakeDamage</c> calls
/// <c>CTFGameRules::ApplyOnDamageModifyRules</c> and, after it, the alive
/// rules' ignition. One pass, in stock's order, over a hit on a TF player
/// (one with a carrier). A victim that is not a player and runs the damage
/// rules (<see cref="ScriptedEntity.RunsDamageRules"/>) takes the modify rules
/// alone, as stock's <c>CTFBaseBoss</c> does (<see cref="ModifyForOther"/>);
/// any other victim's hit passes untouched:
///
/// <para>
/// 1. THE HIT. Its bits, <see cref="TFDamageBits.Critical"/> added when the
///    weapon rolled a crit (<see cref="DamageContext.Critical"/>), its custom
///    kind and weapon from an open <see cref="TFDamageScopes"/> scope (an
///    afterburn tick), else the attacker's active weapon — and then the
///    weapon's own bits over them
///    (<c>CTFWeaponBase::GetDamageType</c>, <see cref="TFWeaponDamageTypes"/>).
/// </para>
/// <para>
/// 2. THE HEADSHOT (<c>CTFPlayer::TraceAttack</c>, <c>server/tf/tf_player.cpp:8311-8373</c>):
///    a shot with hit locations — the sniper rifles' while their owner is
///    zoomed (<c>CTFSniperRifle::GetDamageType</c>,
///    <c>tf_weapon_sniperrifle.cpp:907-917</c>) — on the head of a victim not
///    <c>TF_COND_INVULNERABLE</c> crits and is <c>TF_DMG_CUSTOM_HEADSHOT</c> when the
///    rifle may crit a headshot (<c>CTFSniperRifle::CanFireCriticalShot</c>
///    :1004-1062: a <c>set_weapon_mode</c> 1 rifle never does, one with
///    <c>sniper_no_headshot_without_full_charge</c> only on a full charge, and
///    one without <c>sniper_crit_no_scope</c> only zoomed at least
///    <c>TF_WEAPON_SNIPERRIFLE_NO_CRIT_AFTER_ZOOM_TIME</c> 0.2 s ago — the
///    zoom-in clock <see cref="TFZoomTimes"/> keeps). A body shot is a body
///    shot: TF has no hitgroup damage table.
/// </para>
/// <para>
/// 3. THE FALL (<see cref="TFFallDamage"/>): a landing's damage is TF's
///    formula over the landing speed, before the modify rules (stock's fall
///    damage reaches <c>OnTakeDamage</c> like any other).
/// </para>
/// <para>
/// 4. THE MODIFY RULES (<see cref="TFDamageModifyRules"/>): crit type,
///    defense buffs, range ramp-up and fall-off, the crit multiplier, over
///    the two players' carrier conditions and the two <c>WorldSpaceCenter</c>s.
/// </para>
/// <para>
/// 5. THE RESISTS (<see cref="TFDamageAliveRules"/>): the resist half of the
///    same call's alive rules — the typed damage resists, the spun-up
///    heavy's, the sentry's and the cloaked spy's, and <c>mult_dmgtaken</c>,
///    over the victim's own attributes — before the record, so the books
///    carry the final number. Stock runs them after the alive rules'
///    ignition, which touches no number.
/// </para>
/// <para>
/// 6. THE RECORD (<see cref="TFDamageRecords"/>): the last hit and, for an
///    enemy attacker, the damager history (<c>tf_player.cpp:9042-9044</c>).
/// </para>
/// <para>
/// 7. THE IGNITION (<c>ApplyOnDamageAliveModifyRules</c>,
///    <c>tf_gamerules.cpp:7117-7152</c>, then <c>Burn</c> at
///    <c>tf_player.cpp:10660-10663</c>): a <c>DMG_IGNITE</c> hit sets a player
///    who is out of water to the waist alight, unless fire-immune.
/// </para>
/// <para>
/// 8. THE GAME STATS (<c>CTFGameStats::Event_PlayerDamage</c>,
///    <c>tf_gamestats.cpp:1033-1089</c>, called at the tail of
///    <c>CTFPlayer::OnTakeDamage</c>, <c>tf_player.cpp:9659-9661</c>):
///    the attacker's stats gain the hit's taken damage — the ranged
///    number and, within it, the random-crit one, which the random crit's
///    observed rate is computed from.
/// </para>
/// </summary>
[SideService(Side.Server)]
[Precache(PrecacheKind.ParticleSystem, TFProjectileDefs.JarRifleImpactEffect)]
public sealed class TFDamageLayer(ITFPlayerData players, IEnginePlayers enginePlayers, IEngineEntities entities,
                                  IEntityDirectory directory, TFWeaponScripts scripts, TFClassTable classes,
                                  ITFWeaponOwners owners, ITFPlayerResupply resupply, AttributeHooks attributes,
                                  TFPlayerCarriers carriers,
                                  IUniformRandom random, IGameClock clock, TFDamageRecords records,
                                  TFDamageScopes scopes, TFAfterburns afterburns, TFBleeds bleeds,
                                  TFDamageRangeConVar range, TFDamageDisableSpreadConVar disableSpread,
                                  TFWeaponCriticalsDistanceFalloffConVar critFalloff,
                                  TFWeaponMinicritsDistanceFalloffConVar miniCritFalloff,
                                  TFFallDamageDisableSpreadConVar fallDisableSpread,
                                  TFDebugDamageConVar debug, TFZoomTimes zoomTimes, ILog log,
                                  ITFPlayerStats stats, DamageInflictors inflictors, TFSentryDamages sentryDamages,
                                  TFStealthDamageReductionConVar stealth, ITFPlayerConditions conditions,
                                  IEngineSound sound, IEngineGameEvents events, ITFMedigunWeapons mediguns,
                                  IEngineDamage hurt, ITFPlayerHealing healing,
                                  ITempEnts temps, IEnginePrecache precache,
                                  IEngineNetworking networking, IEngineWorldQuery world,
                                  TFRocketPackImpactPushMinConVar rocketPackPushMin,
                                  TFRocketPackImpactPushMaxConVar rocketPackPushMax,
                                  ITFSpyState spies)
    : IDamageModifier, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 701;

    /// <summary>The Gas Passer's weapon class, the gas burn's weapon.</summary>
    private const string GasCanName = "tf_weapon_jar_gas";

    /// <summary>
    /// <c>INSANE_PLAYER_DAMAGE</c> (<c>tf_gamestats.cpp:1036</c>): the most
    /// a hit may have taken and still be booked at all — stock's "defensive
    /// guard against insanely huge damage values". Stock's MvM arm of the
    /// number (5000) goes with the rest of MvM.
    /// </summary>
    public const int InsaneDamageTaken = 1500;

    /// <summary><c>INVALID_ITEM_DEF_INDEX</c>: the <c>player_healonhit</c>
    /// event's <c>weapon_def_index</c> when the held item has none
    /// (<c>tf_weaponbase.cpp:5249</c>).</summary>
    private const int NoItemDefIndex = -1;
    /// <summary>The milk's payback: <c>info.GetDamage() * 0.6f</c> (<c>tf_weaponbase.cpp:5176</c>).</summary>
    public const float MilkHealFraction = 0.6f;

    /// <summary><c>SCATTERGUN_KNOCKBACK_MIN_DMG</c> (<c>tf_weapon_shotgun.cpp:40</c>).</summary>
    public const float ScattergunKnockbackMinDamage = 30f;

    /// <summary><c>SCATTERGUN_KNOCKBACK_MIN_RANGE_SQ</c>, 400x400 (<c>tf_weapon_shotgun.cpp:41</c>).</summary>
    public const float ScattergunKnockbackMaxRangeSq = 160_000f;

    /// <summary>The shove's force scale, stock's fixed <c>flKnockbackMult</c>
    /// (<c>tf_weapon_shotgun.cpp:396</c>; the <c>scattergun_knockback_mult</c>
    /// override is carried by no row).</summary>
    public const float ScattergunKnockbackForceScale = 3f;

    /// <summary>The Fan O'War's mark attribute (<c>mark_for_death</c>,
    /// tf_weaponbase.cpp:5471).</summary>
    public const string MarkForDeathAttribute = "mark_for_death";

    /// <summary><c>tf_dev_marked_for_death_lifetime</c>'s default, the mark's
    /// seconds (tf_weaponbase.cpp:5049; the development-only convar's default
    /// is the constant, the miniboss half waits with that mode).</summary>
    public const float MarkForDeathLifetime = 15f;

    /// <summary>Each attacker's marked victim, stock's
    /// <c>m_pMarkedForDeathTarget</c> per player: the earlier mark loses its
    /// condition when a new one lands (:5479-5482).</summary>
    private readonly Dictionary<EntityHandle, EntityHandle> _markedForDeath = [];

    // The scattergun shove's banked pellet damage, per victim and attacker,
    // until the frame's flush totals the shot (the comment at the arm).
    private readonly List<(EntityHandle Victim, EntityHandle Attacker, float Damage)> _shoves = [];

    public int HookOrder => Order;

    public float Modify(in DamageContext hit, float damage)
    {
        int victimIndex = hit.Victim.EntryIndex;
        if (players.Predicted(victimIndex) is not { } victim)
            return directory.Get(hit.Victim) is { RunsDamageRules: true } ? ModifyForOther(in hit, damage) : damage;

        bool attackerValid = hit.Attacker != EntityHandle.None && entities.IsValid(hit.Attacker);
        bool attackerIsPlayer = attackerValid && entities.IsPlayer(hit.Attacker);
        bool self = hit.Attacker == hit.Victim;
        TFCondMask victimConds = victim.Conditions();
        var attackerPlayer = attackerIsPlayer ? players.Predicted(hit.Attacker.EntryIndex) : null;
        TFCondMask attackerConds = attackerPlayer?.Conditions() ?? default;

        var bits = (DamageType)hit.DamageType;
        if (hit.Critical)
            bits |= TFDamageBits.Critical;

        // The hit's custom kind and weapon: an open scope's, else the attacker's active weapon.
        TFWeaponId weapon = TFWeaponId.None;
        string weaponName = "";
        bool fromScope = scopes.TryCurrent(out var custom, out weapon, out weaponName);

        // Stock's OnTakeDamage sets the custom before the modify rules
        // (tf_player.cpp:9204-9236): a hit through the über from a trigger or
        // a flag-2 point_hurt arrives as TF_DMG_CUSTOM_TRIGGER_HURT, so the
        // kill record names the trigger and not a weapon. A telefrag's custom
        // is stock's info.GetDamageCustom(), which a scope carries and the
        // trigger arms overwrite.
        if (TFDamageRules.TriggerHurtCustom(entities, in hit))
            custom = TFDamageCustom.TriggerHurt;

        if (!fromScope && attackerIsPlayer)
        {
            weaponName = enginePlayers.GetActiveWeapon(hit.Attacker);
            weapon = weaponName.Length > 0 && scripts.Get(weaponName) is { } script ? script.Id : TFWeaponId.None;
        }

        // The weapon's own bits (CTFWeaponBase::GetDamageType over
        // g_aWeaponDamageTypes): a hit from a TF weapon carries the weapon
        // table's type, not the caller's — the rocket's blast falls off by
        // half, the shotgun's point blank is not damped, the flamethrower's
        // hit lights fires. The crit flag rides on top.
        if (!fromScope && attackerIsPlayer && !self && weapon != TFWeaponId.None)
        {
            // Stock's melee attack builds its info with DMG_MELEE beside the
            // club bit (tf_weaponbase_melee.cpp:817), and the consume arm reads
            // the melee flag off the info (tf_gamerules.cpp:6103), so the
            // caller's Melee bit rides on with the crit flag while the weapon
            // table's own bits answer the rest. The charge's mini-crit rides
            // too: the swing's DMG_RADIUS_MAX (tf_weaponbase_melee.cpp:860-863),
            // the modify rules' sword/bottle/wrench arm's marker
            // (tf_gamerules.cpp:6152).
            bits = (bits & (TFDamageBits.Critical | TFDamageBits.Melee | TFDamageBits.RadiusMax))
                 | TFWeaponDamageTypes.Of(weapon);

            // CTFSniperRifle::GetDamageType (tf_weapon_sniperrifle.cpp:907-917):
            // the rifle strips DMG_USE_HITLOCATIONS unless its owner is
            // zoomed. The Classic intentionally skips the override
            // (:1970-1973) and keeps its row.
            if (weapon is TFWeaponId.Sniperrifle or TFWeaponId.SniperrifleDecap
                && !attackerConds.Has(TFCond.Zoomed))
                bits &= ~TFDamageBits.UseHitLocations;

            // CTFSMG::GetDamageType (tf_weapon_smg.cpp:69-78) and
            // CTFRevolver::GetDamageType (tf_weapon_revolver.cpp:83-92): the
            // submachine gun's and the revolver's add DMG_USE_HITLOCATIONS
            // when set_weapon_mode is 1; CTFPistol_ScoutSecondary's
            // (tf_weapon_pistol.cpp:250-259) when back_headshot is set. The
            // revolver's own gate (:85) holds the bit until a second has
            // passed since the shot stamped the accuracy clock
            // (m_flLastAccuracyCheck, the behaviour's kept time).
            else if (weapon is TFWeaponId.Smg or TFWeaponId.ChargedSmg or TFWeaponId.Revolver)
            {
                if (attributes.Int(0, "set_weapon_mode", owners.ItemOf(hit.Attacker.EntryIndex, weaponName)) == 1
                    && (weapon != TFWeaponId.Revolver || AccuracyFresh(hit.Attacker)))
                    bits |= TFDamageBits.UseHitLocations;
            }
            else if (weapon == TFWeaponId.HandgunScoutSecondary
                     && attributes.Int(0, "back_headshot", owners.ItemOf(hit.Attacker.EntryIndex, weaponName)) != 0)
            {
                bits |= TFDamageBits.UseHitLocations;
            }
        }

        // THE REVENGE SHOT'S TAG (CTFShotgun_Revenge::GetCustomDamageType,
        // tf_weapon_shotgun.cpp:200-209): the owner's unspent window names the
        // hit while the Justice is the gun that dealt it — and the shot that
        // spends the last crit is tagged too, the decrement waiting for the
        // attack's tail (:143-145).
        if (!fromScope && weapon == TFWeaponId.SentryRevenge
            && players.Predicted(hit.Attacker.EntryIndex)?.RevengeCrits > 0)
            custom = TFDamageCustom.ShotgunRevengeCrit;

        // CTFPlayer::TraceAttack's headshot: hit locations are the zoomed sniper rifles'.
        if (attackerIsPlayer && TFDamageModifyRules.IsSniperRifle(weapon) && attackerConds.Has(TFCond.Zoomed)
            && hit.HitGroup == HitGroup.Head && !victimConds.Has(TFCond.Invulnerable))
        {
            // CanFireCriticalShot( true ) (tf_weapon_sniperrifle.cpp:1004-1062):
            // a zoomed rifle is the only one with hit locations, so a headshot
            // here is the headshot's shot. The arms in stock's order:
            //   :1020-1023 — a RIFLE_JARATE (set_weapon_mode 1) never auto-crits one;
            //   :1029-1035 — sniper_no_headshot_without_full_charge wants the charge full;
            //   :1037-1055 — without sniper_crit_no_scope, the zoom must be at least
            //     TF_WEAPON_SNIPERRIFLE_NO_CRIT_AFTER_ZOOM_TIME 0.2 s behind the shot
            //     (GetFOVTime, which TFZoomTimes keeps).
            var item = owners.ItemOf(hit.Attacker.EntryIndex, weaponName);
            if (attributes.Int(0, "set_weapon_mode", item) != 1
                && (attributes.Int(0, "sniper_no_headshot_without_full_charge", item) == 0
                    || FullyCharged(hit.Attacker))
                && (attributes.Int(0, "sniper_crit_no_scope", item) != 0
                    || clock.Now - zoomTimes.ZoomedAt(hit.Attacker.EntryIndex) >= TFSniperRifleBehaviour.NoCritAfterZoomTime))
            {
                bits |= TFDamageBits.Critical;
                custom = TFDamageCustom.Headshot;
            }
        }

        // TF's fall damage over the landing speed, in place of the core's.
        if ((bits & DamageType.Fall) != 0)
        {
            int maxHealth = classes[victim.Class()].MaxHealth;
            float fallSpeed = TFFallDamage.FallSpeedOf(damage);
            damage = TFFallDamage.Damage(fallSpeed, maxHealth, fallDisableSpread.Value, random.RandomFloat);

            // Stock's cancel arm (:13404-13407): CALL_ATTRIB_HOOK_INT_ON_OTHER's
            // cancel_falling_damage over the player's whole attribute manager,
            // the hook walking pPlayer; a yes zeroes the blow before the stomp
            // and thruster arms below read it.
            if (attributes.Int(0, "cancel_falling_damage", owners.ManagerOf(victimIndex)) != 0)
                damage = 0f;

            // The boots' stomp (CTFPlayerShared::CanFallStomp, tf_player_shared.cpp:7909-7918;
            // the transfer over the ground entity at tf_player.cpp:8962-8979): a faller landing
            // on another player whose rocketpack cond or boots_falling_stomp answers — the
            // attribute's ON_OTHER walk sits over the player, the manager's root — and whose
            // team differs hands the blow over instead of taking it: ten plus thrice the whole
            // fall to the one stood on, nothing to the faller (:8968-8973). It runs before the
            // thruster's landing below, which still opens its ring and floors the faller's
            // nothing at one (:9037). The ground is the running move's own: this hit lands
            // inside CheckFalling, before the command's end publishes the ground word.
            if (directory.Get(hit.Victim) is SsPlayer faller
                && faller.MoveGroundIndex >= 0
                && enginePlayers.GetHandle(faller.MoveGroundIndex) is { } under
                && entities.IsValid(under) && entities.IsPlayer(under)
                && (victimConds.Has(TFCond.Rocketpack)
                    || attributes.Int(0, "boots_falling_stomp", owners.ManagerOf(victimIndex)) != 0)
                && enginePlayers.GetTeam(hit.Victim) != enginePlayers.GetTeam(under))
            {
                hurt.TakeDamage(under, hit.Victim, 10f + damage * 3f, DamageType.Fall);
                damage = 0f;
            }

            // The thruster's landing (tf_player.cpp:8991-9038): the held
            // item's own word opens the impact ring, and the fall keeps a
            // quarter of its bite under the rocket-pack cond. Stock's stun
            // half (:9001-9007, falling_impact_radius_stun) rides the stun
            // stack's ledger and waits for that row.
            if (victimConds.Has(TFCond.Rocketpack))
            {
                if (attributes.Int(0, "falling_impact_radius_pushback",
                                  owners.ActiveItemOf(victimIndex)) != 0)
                {
                    float radius = RemapClamped(fallSpeed, 100f, 1000f, 150f, 220f);   // :8998
                    float force = RemapClamped(fallSpeed, 100f, 1000f,
                                               rocketPackPushMin.Value, rocketPackPushMax.Value);   // :8997
                    RocketPackImpact(hit.Victim, radius, force);   // :9009, :9013-9034
                }

                damage = Math.Max(damage * 0.25f, 1f);             // :9037
            }

            if (!(damage > 0f))
                return 0f;
        }

        // THE BODYSHOT MODIFIER (tf_player.cpp:8377-8384): a hit that was not
        // the location arm's headshot — any hitlocation head on a living
        // body is one, the Classic's bodyshots included — scales by the
        // attacker's HELD weapon's bodyshot_damage_modify, the held weapon and
        // not the hitting one (stock walks GetActiveTFWeapon here). Placed
        // after the fall: a fall carries no hitlocations, stock's arm skips
        // it, and here the fall's answer stands unmodified instead.
        bool headshot = attackerIsPlayer && (bits & TFDamageBits.UseHitLocations) != 0
                     && hit.HitGroup == HitGroup.Head && !victimConds.Has(TFCond.Invulnerable);
        if (attackerIsPlayer && !headshot)
            damage = attributes.Float(damage, "bodyshot_damage_modify",
                                      owners.ActiveItemOf(hit.Attacker.EntryIndex));

        // THE SENTRY'S MARK (CTFPlayer::ModifyDamageInfo,
        // tf_player_shared.cpp:10185-10204): an engineer whose HELD weapon
        // carries mult_dmg_bullet_vs_sentry_target over one hits for more
        // while the victim is who the builder's own sentry aims at — stock's
        // GetObjectOfType(OBJ_SENTRYGUN) walk, here the directory's live
        // sentries. Stock scales inside FireBullets, per trace, before the
        // modify rules read the number; the product commutes with them.
        if (attackerIsPlayer)
            damage *= SentryMark(in hit, attackerPlayer);

        float? distance = attackerValid && !self
            ? (entities.GetWorldSpaceCenter(hit.Victim) - entities.GetWorldSpaceCenter(hit.Attacker)).Length
            : null;

        // The inflictor's identity, where a blast dealt the hit: its open
        // <see cref="DamageInflictors"/> scope names it, and a deflected
        // rocket or regular pipe there is the reflected-blast arm's subject
        // (<c>tf_gamerules.cpp:6159</c> — stock reads it off
        // <c>info.GetInflictor()</c>, the grenade arm under
        // <c>ShouldMiniCritOnReflect()</c>, which the remote-detonate sticky
        // fails). A hit no blast dealt has no scope, and stock's projectile
        // cast of a player inflictor fails the same way.
        bool deflectedProjectile = DeflectedProjectile();

        // The rocket specialist's direct hit (tf_gamerules.cpp:6299-6306): the
        // inflictor a rocket whose enemy — the touch, stock's GetEnemy — is
        // this victim, and the attacker's item carries rocket_specialist. The
        // level itself is the radius arm's; here only its presence gates the
        // long-range falloff.
        bool directRocketHit = attackerIsPlayer && DirectRocketHit(in hit, weaponName);

        // The econ arms' walk root: the attacker's weapon item, whose owner
        // chain runs to the wearer's manager. A scoped hit (an afterburn tick)
        // names the burn's weapon, whose item the holder may no longer carry:
        // stock asks the weapon that dealt the hit, and only a held weapon's
        // item is askable here.
        var weaponItem = !fromScope && attackerIsPlayer && weaponName.Length > 0
            ? owners.ItemOf(hit.Attacker.EntryIndex, weaponName) : null;

        // The burn-victim arm (:7231) asks the weapon the attacker holds now;
        // a scoped hit's own weapon item being unreachable (above), the held
        // one stands in for it.
        if (weaponItem is null && attackerIsPlayer)
            weaponItem = owners.ActiveItemOf(hit.Attacker.EntryIndex);

        // The behind-tests' geometry (tf_gamerules.cpp:6050-6053, :6319-6323):
        // the way from the attacker to the victim, and the victim's eye angles.
        // The airborne arms' state (CTFPlayer::InAirDueToExplosion) per side.
        // The double donk's read (:6205) runs only under its own gate's shape,
        // a cannon's blast on a player (IsDoubleDonk, the weapon id's check).
        bool doubleDonk = weapon == TFWeaponId.Cannon && (bits & DamageType.Blast) != 0 && attackerIsPlayer
            && carriers.Part<TFDonkVictims>(hit.Attacker.EntryIndex) is { } donks && donks.Donked(hit.Victim);
        var input = new TFHit(damage, bits, custom, self, attackerIsPlayer, weapon, distance, victimConds, attackerConds,
                              weaponItem, SameClass: attackerPlayer is { } a && victim.Class() == a.Class(),
                              VictimWaterLevel: WaterLevel(hit.Victim), DeflectedProjectile: deflectedProjectile,
                              VictimEyeAngles: entities.GetEyeAngles(hit.Victim),
                              ToVictim: attackerValid
                                  ? entities.GetOrigin(hit.Victim) - entities.GetOrigin(hit.Attacker) : null,
                              AttackerAirborne: InAirDueToExplosion(hit.Attacker, attackerConds),
                              VictimAirborne: InAirDueToExplosion(hit.Victim, victimConds),
                              DirectRocketHit: directRocketHit,
                              VictimItem: owners.ActiveItemOf(victimIndex),
                              AfterburnDuration: afterburns.Of(victimIndex)?.Burn.AfterburnDuration,
                              DoubleDonk: doubleDonk);
        var settings = new TFDamageSettings(range.Value, disableSpread.Value, critFalloff.Value, miniCritFalloff.Value);
        var result = TFDamageModifyRules.Apply(in input, in settings, random.RandomFloat, attributes);

        // The consume arm's side effects (:6107-6108), run right where stock's
        // consume arm is: the flames come off the victim and the flame-out
        // sounds over them — the burn slice clears its clock on the next think
        // (<c>TFAfterburn.Tick</c>), as stock's <c>OnRemoveBurning</c> forgets
        // the burn when the condition goes. Before the alive rules read the
        // victim again (:7191), as stock orders them.
        if (result.Custom == TFDamageCustom.AxtinguisherBoosted && !self && attackerIsPlayer)
        {
            conditions.Remove(hit.Victim, TFCond.Burning);
            sound.EntityEmitSound(hit.Victim, "TFPlayer.FlameOut");
        }

        // The alive rules' resists (ApplyOnDamageAliveModifyRules,
        // tf_gamerules.cpp:7191-7336) over the modified hit, the victim's own
        // attributes the walk's root. Stock runs them after the same call's
        // ignition, which touches no number; they land here so the record and
        // the stats book the final damage. The spun-up gate (:7266) reads the
        // health before the resists, as stock's pre-reassembly
        // flRealDamage is; the sentry arm asks whether the inflictor is the
        // gun itself (:7330-7337), the same read the sentry-damager note
        // makes below.
        int victimMax = resupply.MaxHealth(hit.Victim);
        bool spunUp = victim.Class() == TFClass.HeavyWeapons
            && victimConds.Has(TFCond.Aiming) && victimMax > 0
            && (entities.GetHealth(hit.Victim) - result.Damage) / victimMax <= TFDamageAliveRules.HalfHealth;
        bool fromSentry = inflictors.TryCurrent(out EntityHandle sentryInflictor, out _)
            && directory.Get(sentryInflictor) is TFSentryGun;

        // The health the feign arm's save check compares against (:7413), read
        // only where that arm answers — behind the same gate the rules' arm
        // puts on itself, so a plain hit asks the engine nothing extra.
        float feignHealth = victim.Class() == TFClass.Spy && custom != TFDamageCustom.Telefrag
            && !victimConds.Has(TFCond.Taunting)
            && (victimConds.Has(TFCond.FeignDeath) || victim.FeignDeathReady)
            ? entities.GetHealth(hit.Victim) : 0f;

        // The medic's resist walks the condition's provider (:6961): whose ever bubble the
        // victim wears, that player's manager is the ON_OTHER root; nobody's cond asks nothing.
        Func<TFCond, IAttributeProvider?> medicOf = cond =>
            conditions.Of(victimIndex)?.Provider(cond) is { } medic && medic != default
                ? owners.ManagerOf(medic.EntryIndex)
                : null;

        result = result with
        {
            Damage = TFDamageAliveRules.Resist(result, in input, attributes, owners.ManagerOf(victimIndex),
                                               victim.Class(), stealth.Value, spunUp, fromSentry,
                                               victim.CloakMeter, victim.FeignDeathReady, feignHealth,
                                               out bool savedByFeign, medicOf)
        };

        // THE DEAD RINGER'S SAVE (tf_gamerules.cpp:7412-7422): the scale that
        // would have killed and did not tells the server, spy and attacker by
        // user id, the hit broadcast to nobody (:7420). An event the game's
        // event files never declared answers a zero handle from the create,
        // which stock's own guard skips just the same (:7416).
        if (savedByFeign)
        {
            IntPtr save = events.EventCreate("deadringer_cheat_death");
            if (save != IntPtr.Zero)
            {
                events.EventSetInt(save, "spy", enginePlayers.GetUserId(hit.Victim));
                events.EventSetInt(save, "attacker",
                                   hit.Attacker != EntityHandle.None ? enginePlayers.GetUserId(hit.Attacker) : -1);
                events.EventFire(save, true);                            // dontBroadcast (:7420)
            }
        }

        if (debug.Value)
            log.Msg($"[TF2/Damage] {hit.Victim} from {hit.Attacker} ({weaponName}): {hit.Damage:F2} -> {result.Damage:F2}, "
                  + $"crit {result.Crit}, custom {result.Custom}, bits 0x{(int)result.Bits:X}"
                  + (distance is { } d ? $", dist {d:F1}" : "") + "\n");

        float now = clock.Now;
        bool enemy = attackerIsPlayer && !self && enginePlayers.GetTeam(hit.Attacker) != enginePlayers.GetTeam(hit.Victim);

        // THE AMMO REFUND (CTFWeaponBase::ApplyOnHitAttributes's first arm,
        // tf_weaponbase.cpp:5059-5079, called at tf_gamerules.cpp:6876 once
        // the damage is final): the hitting item's add_onhit_addammo is the
        // percentage of the hit's damage the weapon owes its shooter, paid
        // into the pool the weapon's primary draws on — the metal, for the
        // item that names one. Stock banks the share on the weapon's
        // m_iAmmoToAdd and pays it at the NEXT shot's spend
        // (tf_weaponbase_gun.cpp:389-395); here the grant lands on the hit,
        // the pool the next refusal reads. A disguised spy that is not
        // stealthed pays nothing: stock's IsSpy check, so a refund's tick
        // cannot betray one (:5068-5076).
        if (enemy && weaponItem is { } refundItem)
        {
            int refundPct = attributes.Int(0, "add_onhit_addammo", refundItem);
            if (refundPct > 0)
            {
                float refundShare = refundPct / 100f;   // :5066
                if (victim.Class() == TFClass.Spy && victimConds.Has(TFCond.Disguised)
                    && victim.DisguiseTeam != enginePlayers.GetTeam(hit.Victim)
                    && !victimConds.Has(TFCond.Stealthed) && !victimConds.Has(TFCond.StealthedBlink))
                {
                    refundShare = 0f;   // :5075
                }

                int refund = (int)(refundShare * result.Damage);   // :5078's truncation
                string refundPool = attributes.Int(0, "mod_use_metal_ammo_type", refundItem) != 0
                    ? TFAmmoNames.Name(TFAmmo.Metal)
                    : scripts.Get(weaponName)?.Base.Ammo1 ?? "";
                if (refund > 0 && refundPool.Length > 0)
                    enginePlayers.GiveAmmo(hit.Attacker, refund, refundPool);
            }
        }
        // THE FISH KILL'S CUSTOM (CTFPlayer::OnTakeDamage's fish block,
        // tf_player.cpp:9743-9746): the bat's blow that leaves the victim dead
        // names itself TF_DMG_CUSTOM_FISH_KILL, the kill feed's fish notice —
        // the notice's own name the death walk reads off this record
        // (TFDeathNotice, the arm'd variant its fish_damage_override).
        if (weapon == TFWeaponId.BatFish && entities.GetHealth(hit.Victim) - result.Damage <= 0f)
            result = result with { Custom = TFDamageCustom.FishKill };

        records.Note(victimIndex,
                     new TFHitRecord(attackerValid ? hit.Attacker : EntityHandle.None, result.Damage, result.Bits,
                                     result.Crit, result.Custom, weapon, weaponName, now),
                     damager: enemy);

        // RECORD_DAMAGE_EVENT (CTFPlayerShared::RecordDamageEvent, tf_player_shared.cpp:9574-9660,
        // called at tf_player.cpp:10801-10804): every hit a TF player deals to another player joins
        // that player's damage-event list, from which the next half-second think weighs the crit
        // multiplier. The caller's gate is stock's -- a TF attacker who is not the victim himself
        // (:10801); health before and the kill stand in for stock's after-apply words (:10802-10803)
        // the way the heal_on_kill arm reads them (:1053-1055), this seam running before the apply.
        if (attackerIsPlayer && !self)
        {
            float prevHealth = entities.GetHealth(hit.Victim);                          // :10770
            carriers.Part<TFCritMultTicker>(hit.Attacker.EntryIndex)
                     ?.RecordDamageEvent(result, prevHealth - result.Damage <= 0f, prevHealth, now);
        }

        // OnDealtDamage's meter walk (tf_player.cpp:9968-10038), stock's call
        // on the attacker at the end of the victim's OnTakeDamage (:9790-9796):
        // the hitting item's DAMAGE or COMBO meter fills by the hit's share of
        // its item_meter_damage_for_full_charge — the Gas Passer's burning
        // ticks, credited to the thrower, ride this arm.
        if (enemy && attackerValid
            && directory.Get(enginePlayers.GetActiveWeaponEntity(hit.Attacker)) is SsWeapon { State: { } hitMeter })
        {
            TFItemMeter.DamageCharge(hitMeter, result.Damage, attributes, owners,
                                     hit.Attacker.EntryIndex, weaponName);
        }

        // The sentry-damager history (tf_player.cpp:9050-9055): an enemy
        // player's hit whose inflictor is the sentry records the pair, for
        // PlayerKilled's sentry assist (TFBuildingScores).
        if (enemy && inflictors.TryCurrent(out EntityHandle sentryHitBy, out _)
            && directory.Get(sentryHitBy) is TFSentryGun)
            sentryDamages.Note(victimIndex, hit.Attacker, sentryHitBy, now);

        // THE IGNITE BIT AND ITS ATTRIBUTE (tf_gamerules.cpp:7117-7133):
        // the hit's own DMG_IGNITE sets the victim alight out of the water;
        // beside it, a held item's set_dmgtype_ignite adds the bit to a
        // non-DOT hit (:7122-7130, IsDOTDmg :1295-1301) so the Sharpened
        // Volcano Fragment lights an airblast that carries no ignite of its
        // own. The DOT guard keeps the burn's ticks from re-lighting; the
        // fire-immune hard-out is stock's :7148-7152.
        bool igniting = (result.Bits & TFDamageBits.Ignite) != 0
                        || (custom is not (TFDamageCustom.Burning or TFDamageCustom.BurningFlare
                                           or TFDamageCustom.BurningArrow or TFDamageCustom.Bleeding)
                            && attributes.Int(0, "set_dmgtype_ignite", weaponItem) != 0);
        if (igniting && attackerIsPlayer && WaterLevel(hit.Victim) < TFBurn.WaterLevelWaist
            && !victimConds.Has(TFCond.FireImmune))
            afterburns.Of(victimIndex)?.Ignite(now, hit.Attacker, weapon, weaponName);

        // THE GAS IGNITION (tf_player.cpp:9869-9896): gas takes any damage — a
        // gassed player who takes damage is lit from the tosser's can, burned
        // for tf_afterburn_max_duration's ten (:9895, the constant at
        // tf_player_shared.cpp:188), the gas gone behind it (:9896), and a pyro
        // victim also wears BurningPyro for the same (:9884-9887). Stock's Burn
        // credits the tosser, and the victim when nobody tossed (:9895) — not
        // the flame-hitter — so the burn rides the condition's provider.
        if (victimConds.Has(TFCond.Gas) && result.Damage > 0f)
        {
            EntityHandle tosser = conditions.Of(victimIndex)?.Provider(TFCond.Gas) ?? EntityHandle.None;
            if (victim.Class() == TFClass.Pyro)
                conditions.Add(hit.Victim, TFCond.BurningPyro, TFBurn.MaxDuration);
            // The burn's weapon is the tosser's gas can, never the hitter's
            // (:9889-9895); with nobody tossing, no weapon.
            bool tossed = tosser != EntityHandle.None;
            afterburns.Of(victimIndex)?.Ignite(now, tossed ? tosser : hit.Victim,
                                               tossed ? TFWeaponId.JarGas : TFWeaponId.None,
                                               tossed ? GasCanName : "", TFBurn.MaxDuration);
            conditions.Remove(hit.Victim, TFCond.Gas);
        }

        // THE SPY-CICLE (tf_weaponbase.cpp:5528-5547, tf_weapon_knife.cpp:118-157): fire reaching a
        // player answers through the knife held in hand — stock calls the held weapon's
        // ApplyOnInjuredAttributes when its owner is hurt (tf_player.cpp:9504-9507), and the base's half
        // runs first (:5528-5546): the item's become_fireproof_on_hit_by_fire makes the holder fire-immune
        // for the fixed second (:5536), burns the flame off if one was on (:5538-5542), and raises the
        // afterburn immunity for the attribute's seconds (:5544). The knife's own half (:122-131) then
        // melts the knife for its seconds — stock's CanDeploy (:521-531) refuses its swings until the
        // window closes. Both walks stand where the burn gate does (:5534, :124).
        if ((result.Bits & DamageType.Burn) != 0 || (result.Bits & TFDamageBits.Ignite) != 0)
        {
            var heldItem = owners.ActiveItemOf(victimIndex);                                    // :9504
            int fireproof = attributes.Int(0, "become_fireproof_on_hit_by_fire", heldItem);    // :5533
            if (fireproof > 0)                                                                 // :5534
            {
                conditions.Add(hit.Victim, TFCond.FireImmune, 1f, hit.Attacker);               // :5536
                if (victimConds.Has(TFCond.Burning))                                           // :5538
                {
                    sound.EntityEmitSound(hit.Victim, "TFPlayer.FlameOut");                    // :5540
                    conditions.Remove(hit.Victim, TFCond.Burning);                             // :5541
                }
                conditions.Add(hit.Victim, TFCond.AfterburnImmune, fireproof, hit.Attacker);   // :5544
            }

            float meltSeconds = attributes.Float(0f, "melts_in_fire", heldItem);               // :123
            if (meltSeconds > 0f)
                spies.MeltKnife(victimIndex, now, meltSeconds);                                // :126-131
        }

        // THE BLEED (tf_player.cpp:10577-10585, :10665-10667): a hit whose
        // weapon carries bleeding_duration bleeds the victim, unless the hit
        // is itself a bleed tick or the attacker's own blast (the
        // bSelfBlastDmg arm of :10582).
        if (attackerIsPlayer && custom != TFDamageCustom.Bleeding
            && !(self && (result.Bits & DamageType.Blast) != 0))
        {
            float bleedingTime = attributes.Float(0f, "bleeding_duration",
                owners.ItemOf(hit.Attacker.EntryIndex, weaponName));
            if (bleedingTime > 0f)
                bleeds.Of(victimIndex)?.Make(now, enginePlayers.IsAlive(hit.Victim), hit.Attacker,
                                             weapon, weaponName, bleedingTime);
        }

        // THE JARATE (tf_player.cpp:10676-10730): a sniper rifle's hit — its
        // shooter zoomed, or the Classic's, which charges unzoomed — whose
        // shot had begun charging (GetJarateTime's m_flChargedDamage arm,
        // tf_weapon_sniperrifle.cpp:1215-1223) jars: the item's
        // jarate_duration is the seconds at full charge and the charge itself
        // remapped 50..150 onto 2..it (GetJarateTimeInternal :1228-1240 — the
        // meter is still the shot's, cleared only when PrimaryAttack
        // returns). A second or more of it, on a victim neither invulnerable,
        // phasing nor intercepting (:10685), takes the splash and the urine.
        // The splash lands on the victim's origin: the trace's end never
        // reaches this layer (docs/divergences.md).
        if (attackerIsPlayer && TFDamageModifyRules.IsSniperRifle(weapon)
            && (attackerConds.Has(TFCond.Zoomed) || weapon == TFWeaponId.SniperrifleClassic)
            && directory.Get(enginePlayers.GetActiveWeaponEntity(hit.Attacker)) is SsWeapon { State: { } rifle })
        {
            float jarate = TFSniperRifleBehaviour.Jarate(rifle, now,
                attributes.Float(0f, "jarate_duration", owners.ItemOf(hit.Attacker.EntryIndex, weaponName)));
            if (jarate >= 1f && !victimConds.Has(TFCond.Invulnerable)
                && !victimConds.Has(TFCond.Phase) && !victimConds.Has(TFCond.PasstimeInterception))
            {
                Vector at = entities.GetOrigin(hit.Victim);
                TeEffectData splat = new()
                {
                    Scale = 1f,
                    HitBox = precache.ParticleSystemIndex(TFProjectileDefs.JarRifleImpactEffect),
                    Origin = at,
                };
                temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in splat);
                conditions.Add(hit.Victim, TFCond.Urine, jarate);
            }
        }

        // THE SPY'S CLOAK (tf_gamerules.cpp:6802-6813): a Spy attacker with an
        // active weapon whose item carries add_cloak_on_hit fills their own
        // cloak meter by the attribute. Stock calls the shared
        // AddToSpyCloakMeter with bForce true (:6810): the watch's regen hooks
        // are skipped, the amount lands straight on the meter, clamped to
        // 0..100 (tf_player_shared.cpp:9731, :9753-9755); the watch's own gate
        // (:9727-9729) is the spy's class, as the heal and ammo-pack lanes
        // ask it. The weapon gate (:6804) is the held weapon's name.
        if (attackerIsPlayer && attackerPlayer is { } spy && spy.Class() == TFClass.Spy
            && weaponName.Length > 0)
        {
            int cloakOnHit = attributes.Int(0, "add_cloak_on_hit", weaponItem); // :6807
            if (cloakOnHit > 0)                                                  // :6808
            {
                spy.CloakMeter = Math.Clamp(spy.CloakMeter + cloakOnHit, 0f, 100f); // :9755
            }
        }

        // THE ENEMY SLOW (CTFWeaponBase::ApplyOnHitAttributes's slow arms,
        // tf_weaponbase.cpp:5443-5467): the held item's
        // <c>mult_onhit_enemyspeed</c> is the chance — the plain uniform roll
        // against the attribute (:5450) — that a hit slows the victim's feet
        // for 0.2 s, its strength remapped over the squared distance from 0.6
        // at 512 units to nothing at 1536 (:5454-5457); TF_STUN_MOVEMENT,
        // dealt by the attacker. The <c>_major</c> variant, asked next, needs
        // no roll: the attribute is the stun's seconds and the strength is the
        // fixed 0.4 (:5461-5466). Both arms are inside the health-buff gate
        // (:5444): a victim riding a medic's heal buff (<c>TF_COND_HEALTH_BUFF</c>)
        // keeps their feet. Stock's stun is the shared <see cref="TFStunStack"/>.
        if (attackerIsPlayer && !victimConds.Has(TFCond.HealthBuff))
        {
            float slowEnemy = attributes.Float(0f, "mult_onhit_enemyspeed", weaponItem);
            if (slowEnemy != 0f && random.RandomFloat(0f, 1f) < slowEnemy)
            {
                float length = distance ?? 0f;
                carriers.Part<TFStunStack>(victimIndex)?.Stun(hit.Victim, 0.2f,
                    TFDamageModifyRules.RemapValClamped(length * length, 512f * 512f, 1536f * 1536f, 0.6f, 0f),
                    TFStunFlags.Movement, hit.Attacker);
            }

            float slowMajor = attributes.Float(0f, "mult_onhit_enemyspeed_major", weaponItem);
            if (slowMajor != 0f)
                carriers.Part<TFStunStack>(victimIndex)?.Stun(hit.Victim, slowMajor, 0.4f, TFStunFlags.Movement, hit.Attacker);
        }

        // MARK FOR DEATH ON HIT (tf_weaponbase.cpp:5469-5488, the next arm of
        // the same walk): the Fan O'War's hit marks the victim
        // TF_COND_MARKEDFORDEATH for tf_dev_marked_for_death_lifetime 15 s
        // (the miniboss half waits with that mode), the attacker the
        // condition's provider — and the attacker's earlier mark loses its
        // cond first, the one-target bookkeeping stock itself calls
        // imperfect (:5474-5478): conditions carry no owner, so the
        // per-attacker target lives in the layer's dictionary, stock's
        // m_pMarkedForDeathTarget.
        if (attackerIsPlayer
            && attributes.Int(0, MarkForDeathAttribute, weaponItem) != 0)
        {
            if (_markedForDeath.TryGetValue(hit.Attacker, out EntityHandle previous)
                && previous != hit.Victim
                && entities.IsValid(previous))
                conditions.Remove(previous, TFCond.Markedfordeath);

            conditions.Add(hit.Victim, TFCond.Markedfordeath, MarkForDeathLifetime, hit.Attacker);
            _markedForDeath[hit.Attacker] = hit.Victim;
        }

        // THE DEAD RINGER'S FEIGN (tf_player.cpp:9673-9692): once the hit's
        // number settled, the spy's own arm answers — telefrag and croc hits
        // stand outside (:9673, the kinds the watch never answers through),
        // and otherwise the watch's ready gate, the taunt guard and the death
        // itself ride FeignDeathHit. Stock runs the arm in OnTakeDamage's
        // tail, right where the held weapon's post-hit effects follow
        // (:9695-9698), so it stands beside them here.
        if (victim.Class() == TFClass.Spy)
            spies.FeignDeathHit(victimIndex, hit.Attacker);

        // THE SCATTERGUN'S SHOVE (CTFScatterGun::ApplyPostHitEffects,
        // tf_weapon_shotgun.cpp:369-409, called from the tail of the victim's
        // OnTakeDamage, tf_player.cpp:9695-9698): the Force-a-Nature's hit
        // hurls the victim away from the attacker's centres, the force the
        // AirBurstDamageForce of their hull, the damage and 3x, reversed and
        // lifted a jump's worth, through the generic pushback (:3369-3400) —
        // the ground floor the flag answers, the impulse that strips it, and
        // the knocked-into-air condition — then the 0.3 s forward-only
        // movement stun and the once-per-airtime latch. The generic body's
        // truce and rune arms and the vulnerability scales wait with the
        // airblast's (TFFlameEffects' note); the spunup immunity needs the
        // heavy's attr, carried by no row yet.
        //
        // Stock's hit is the SHOT'S: its FireBullets walks the pellets into a
        // multi-damage (tf_fx_shared.cpp:396's ApplyMultiDamage), so the
        // OnTakeDamage that reaches here carries every pellet's summed damage
        // and the :52 gate reads the whole blast. So does ours: the arsenal
        // banks the shot's pellets into one hit (the ten-pellet fact), the
        // layer once per blast — but a banked entry can hold several of the
        // frame's shots, so the arm still defers to the shot's total at the
        // frame's flush (<see cref="FlushShoves"/>, the victim's
        // TFScattergunKnockback tick).
        if (!fromScope && attackerIsPlayer && weaponName.Length > 0
            && scripts.Get(weaponName) is { } shoveScript
            && TFWeaponKinds.Of(shoveScript) == TFWeaponKind.Shotgun
            && attributes.Int(0, TFScattergunBehaviour.KnockbackAttribute,
                              owners.ItemOf(hit.Attacker.EntryIndex, weaponName)) != 0
            && players.Predicted(victimIndex) is not { WeaponKnockback: true }
            && !victimConds.Has(TFCond.Megaheal) && !victimConds.Has(TFCond.ImmuneToPushback)
            && distance is { } shoveDistance && shoveDistance * shoveDistance < ScattergunKnockbackMaxRangeSq)
        {
            EntityHandle shoveVictim = hit.Victim;
            EntityHandle shoveAttacker = hit.Attacker;
            int at = _shoves.FindIndex(shove => shove.Victim == shoveVictim && shove.Attacker == shoveAttacker);
            if (at >= 0)
                _shoves[at] = (shoveVictim, shoveAttacker, _shoves[at].Damage + result.Damage);
            else
                _shoves.Add((shoveVictim, shoveAttacker, result.Damage));
        }

        // THE GAME STATS (Event_PlayerDamage, tf_gamestats.cpp:1033-1089,
        // called at the tail of CTFPlayer::OnTakeDamage,
        // tf_player.cpp:9659-9661): the two numbers the random crit's
        // observed rate is computed from, in stock's guard order (:1042,
        // :1047-1058, :1073): the taken damage sane; the attacker a TF
        // player — stock credits a sentry's builder, and ours already fires
        // with the builder as its attacker (TFSentryGun.cs:618) — and not
        // the victim's own hand; and the ranged pair skips a DMG_MELEE hit.
        // A crit books to the random number unless the attacker rode a
        // crit boost (:1079; the boosted stat itself is the scoring lanes',
        // and so are the other stats of :1062-1070). The taken damage is
        // the number about to be returned: TF players wear no armour (stock
        // skips CBasePlayer::OnTakeDamage, tf_player.cpp:9432), so it is
        // stock's release-arm m_iHealthBefore - GetHealth() — negative
        // health and all (core's DamageTaken leaves a player killed by 200
        // at -117, SsPlayer.cs:450-452). The DMG_MELEE test is the caller's
        // bits: stock's swing carries DMG_MELEE into OnTakeDamage
        // (tf_weaponbase_melee.cpp:817) and nothing there re-derives it,
        // while the weapon-table read above is the gun path's.
        int taken = (int)result.Damage;
        if (attackerIsPlayer && !self && taken is >= 0 and <= InsaneDamageTaken
            && ((DamageType)hit.DamageType & TFDamageBits.Melee) == 0)
        {
            stats.Increment(hit.Attacker, TFStat.DamageRanged, taken);
            if ((result.Bits & TFDamageBits.Critical) != 0
                && !new TFConditionEffects().IsCritBoosted(attackerConds, clientSide: false))
                stats.Increment(hit.Attacker, TFStat.DamageRangedCritRandom, taken);
        }

        // THE RAGE GAIN (CTFPlayer::HandleRageGain, tf_player.cpp:8718-8741,
        // run for the attacker at the tail of OnTakeDamage, :9717-9722): a hit
        // that bit fills the soldier's buff meter from the damage dealt — the
        // buff's scale over the damage and the divisor 6, the item's
        // rage_giving_scale riding on it (:9714-9715). Stock's owning test is
        // Weapon_OwnsThisID(TF_WEAPON_BUFF_ITEM) (:8725), here the loadout
        // row's item behind the buff weapon. The meter caps at full and
        // refuses gains while the deploy drains (SetRageMeter,
        // tf_player_shared.cpp:13870-13898).
        if (attackerIsPlayer && !self && result.Damage > 0f && attackerPlayer is { } attackerState
            && attackerState.Class() == TFClass.Soldier)
        {
            var buffItem = owners.ItemOf(hit.Attacker.EntryIndex,
                                         TFWeaponNames.Name(TFWeaponId.BuffItem).ToLowerInvariant());
            TFRageMeter.GainOnDamageDealt(attackerState, result.Damage,
                                          attributes.Float(1f, TFRageMeter.GivingScaleAttribute, weaponItem),
                                          attributes.Int(0, TFRageMeter.BuffTypeAttribute, buffItem));
        }

        // THE PYRO'S RAGE, the burn arm of the same block (tf_player.cpp:9730
        // -9735): a hit whose type carries DMG_BURN or DMG_PLASMA — the
        // afterburn tick's <see cref="TFBurn.TickDamageType"/> — fills the
        // pyro attacker's meter by the same shape, the divisor
        // <see cref="TFRageMeter.BurnDamageDivisor"/>. The flag is the buff
        // table's pyro rage alone (<c>g_RageBuffTypes</c>,
        // tf_shareddefs.h:2767), read off the flamethrower the attacker owns —
        // stock's Weapon_OwnsThisID(TF_WEAPON_FLAMETHROWER), :8745 — not the
        // scoped hit's weapon, which an afterburn tick does not name.
        if (attackerIsPlayer && !self && result.Damage > 0f && attackerPlayer is { } burner
            && burner.Class() == TFClass.Pyro
            && ((DamageType)hit.DamageType & (DamageType.Burn | DamageType.Plasma)) != 0)
        {
            var flameItem = owners.ItemOf(hit.Attacker.EntryIndex,
                                          TFWeaponNames.Name(TFWeaponId.Flamethrower).ToLowerInvariant());
            TFRageMeter.GainOnBurnDamage(burner, result.Damage,
                                         attributes.Float(1f, TFRageMeter.GivingScaleAttribute, flameItem),
                                         attributes.Int(0, TFRageMeter.BuffTypeAttribute, flameItem));
        }

        // THE SCOUT'S HYPE (CTFGameRules::ApplyOnDamageAliveModifyRules,
        // tf_gamerules.cpp:7526-7532, inside the attacker-but-not-self block
        // :7490): a hit the Soda Popper's hype_on_damage arms fills the
        // attacker's meter from the damage dealt — the 1..200 remap onto
        // 1..50, held at 100. The setter's buffed refusal and clamp are
        // TFHype's (SetScoutHypeMeter, tf_player_shared.cpp:14124-14141).
        if (attackerIsPlayer && !self && attackerPlayer is { } scout
            && attributes.Int(0, TFHype.GainAttribute, weaponItem) != 0)
            TFHype.GainOnDamage(scout, result.Damage);

        // THE BFB'S TAKEN TOLL (stock tf_player.cpp:9762-9774, OnTakeDamage's
        // tail): a scout hit loses hype by the held Baby Face's Blaster's
        // lose_hype_on_take_damage over the damage taken, through the same
        // setter. Stock's root is the player's own walk; the held weapon's
        // item is the askable root here, the stand-in the burn-victim arm
        // makes too.
        int hypeTaken = attributes.Int(0, TFHype.TakenDamageAttribute, owners.ActiveItemOf(victimIndex));
        if (victim.Class() == TFClass.Scout && hypeTaken != 0)
            TFHype.Lose(victim, hypeTaken * result.Damage);

        // THE ON-HIT HEALTH (stock tf_weaponbase.cpp:5142-5257): one accumulator in stock's
        // order. The held item's add_onhit_addhealth opens it (:5144), scaled down by how much
        // of the dealt damage survived into the modified hit (:5148) and floored at three when
        // set (:5149). A milked victim pays back sixty percent of the hit on top (:5176), and
        // the Concheror's buff adds its recover percentage of the hit (:5215, the scale's
        // float truncated into the int that heals). Nonzero heals through the resupply's
        // TakeHealth (:5231), which caps the heal at the item-inclusive max health the way
        // stock's TakeHealth does; a negative total hurts the carrier instead (:5241). Either
        // way the player_healonhit game event (:5244-5256) names the signed amount, the healed
        // edict and the held item's defindex (stock's -1 without a
        // GetAttributeContainer()->GetItem()). The buff's other half, the carried speed boost,
        // rides the conds' TF_COND_SPEED_BOOST (tf_player_shared.cpp:4234-4259), and the milk's
        // provider walk — the assist stat, the player_healed event and the provider's own
        // meter trickle (:5179-5209) — waits on that lane's carrier
        // (docs/plans/tf2/deferred.tsv). Stock's heal stat (:5236) is likewise not booked yet.
        int healOnHit = attributes.Int(0, "add_onhit_addhealth", weaponItem);             // :5144
        if (healOnHit != 0)
        {
            // :5148's Clamp(damage dealt over damage as dealt, 0, 1); the floor of three is stock's (:5149).
            float scale = Math.Clamp(hit.Damage > 0f ? result.Damage / hit.Damage : 1f, 0f, 1f);
            healOnHit = Math.Max(3, (int)(healOnHit * scale));
        }

        if (attackerIsPlayer && victimConds.Has(TFCond.MadMilk) && result.Damage > 0f)
            healOnHit += (int)(result.Damage * MilkHealFraction);                          // :5176's truncation
        if (attackerIsPlayer && !self && result.Damage > 0f
            && attackerConds.Has(TFCond.Regenondamagebuff) && attackerPlayer is { })
            healOnHit += (int)(result.Damage * TFRageMeter.HealOnHitScale);                // :5215's truncation

        if (healOnHit != 0)                                                                // :5227: heal-or-nothing
        {
            if (healOnHit > 0)
                resupply.TakeHealth(hit.Attacker, healOnHit);                              // :5231's TakeHealth(DMG_GENERIC)
            else
                hurt.TakeDamage(hit.Attacker, hit.Attacker, -healOnHit, DamageType.Generic); // :5241 hurts the carrier itself

            IntPtr ev = events.EventCreate("player_healonhit");                            // :5244
            if (ev != IntPtr.Zero)                                                         // :5245
            {
                events.EventSetInt(ev, "amount", healOnHit);                               // :5247
                events.EventSetInt(ev, "entindex", entities.GetIndex(hit.Attacker));      // :5248
                events.EventSetInt(ev, "weapon_def_index",                                 // :5249-5254
                                   weaponItem is AttributeContainer { Item: { } def }
                                       ? def.Defindex : NoItemDefIndex);
                events.EventFire(ev);                                                      // :5255
            }
        }

        // THE PERSUADER'S METER (stock tf_weaponbase.cpp:5152-5162, in
        // ApplyOnHitAttributes between the heal and the speed): the held item's
        // charge_meter_on_hit fraction onto the demoman's charge, the hook the
        // weapon's own walk — the item, its providers and the wearer's manager.
        // The rune arm's 0.2 (:5157-5160) waits for the powerup mode, as the
        // regen's does; the meter's ceiling is the field's 0..100.
        if (attackerIsPlayer && attackerPlayer is { } charger)
        {
            float chargeRefill = attributes.Float(0f, "charge_meter_on_hit", weaponItem);   // :5154
            if (chargeRefill > 0f)
                charger.ChargeMeter = Math.Clamp(charger.ChargeMeter + chargeRefill * 100f, 0f, 100f);   // :5161
        }

        // SPEED ON HIT (tf_weaponbase.cpp:5164-5170): the held item's
        // speed_boost_on_hit is the seconds the attacker rides the carried
        // speed boost — stock's AddCond takes the attribute whole as the
        // duration, no provider. The walk's arm (TFMaxSpeed) spends it.
        if (attackerIsPlayer)
        {
            int boostSeconds = attributes.Int(0, "speed_boost_on_hit", weaponItem);   // :5165-5166
            if (boostSeconds != 0)                                                     // :5167
                conditions.Add(hit.Attacker, TFCond.SpeedBoost, boostSeconds);         // :5169
        }

        // THE HOT HAND'S GRIP (CTFWeaponBaseMelee::ItemPostFrame, tf_weaponbase_melee.cpp:675-683):
        // a melee hit that lands on an enemy player — the else of stock's same-team check, the
        // trace's entity standing — puts the attacker on the held item's speed_boost_on_hit_enemy
        // seconds of the carried speed boost, the float whole as AddCond's duration (:681).
        // Stock's ally twin (:646-674) waits with its carriers (speed_buff_ally,
        // add_give_health_to_teammate_on_hit) at docs/plans/tf2/deferred.tsv.
        if (enemy && hit.Victim != EntityHandle.None                                       // :679's trace.m_pEnt
            && ((DamageType)hit.DamageType & TFDamageBits.Melee) != 0)
        {
            float enemyBoost = attributes.Float(0f, "speed_boost_on_hit_enemy", weaponItem);   // :678
            if (enemyBoost > 0f)                                                               // :679
                conditions.Add(hit.Attacker, TFCond.SpeedBoost, enemyBoost);                   // :681
        }

        // THE CARBINE'S BOOST (CTFChargedSMG::ApplyOnHitAttributes, tf_weapon_smg.cpp:178-196): while
        // the owner's buff is cold (:184), hits charge the SMG's own boost meter — the hit's surviving
        // damage times the held item's minicrit_boost_charge_rate (:186-189), capped at the meter's
        // 100 (:190-193). The charge is the weapon entity's, the gate the hit weapon's class.
        if (attackerIsPlayer && weapon == TFWeaponId.ChargedSmg && !attackerConds.Has(TFCond.EnergyBuff)
            && directory.Get(enginePlayers.GetActiveWeaponEntity(hit.Attacker)) is SsWeapon { State: { } boost })
        {
            boost.BehaviourMeter = Math.Min(TFWeaponBehaviour.ChargedSmgMeterCap,
                                            boost.BehaviourMeter
                                            + result.Damage * attributes.Float(0f, "minicrit_boost_charge_rate", weaponItem));
        }

        // THE UBERSAW'S CHARGE (stock tf_weaponbase.cpp:5259-5281): a medic's hit fills the
        // medic's own ÜberCharge meter from the held item's add_onhit_ubercharge, a float the
        // hook walks over the held weapon's attribute manager — providers and the wearer's
        // manager included. Stock's powerup-mode scaling of the bonus (:5269-5277) has no
        // carrier yet, the same way the crit arm's !IsPowerupMode gate is vacuous
        // (docs/plans/tf2/deferred.tsv); the bonus reaches AddCharge as it came through. The
        // meter's own clamp is the setter's (AddCharge caps at one, tf_weapon_medigun.cpp:1061,
        // and SetChargeLevel keeps the floor). Only a medic carries the arm (:5260); the
        // medic's medigun is asked through the medigun service, stock's Weapon_OwnsThisID
        // (TF_WEAPON_MEDIGUN). With no item attribute the meter stands.
        if (attackerIsPlayer && attackerPlayer is { } medic && medic.Class() == TFClass.Medic)
        {
            float bonus = attributes.Float(0f, "add_onhit_ubercharge", weaponItem);
            if (bonus != 0f)
                mediguns.SetChargeLevel(hit.Attacker.EntryIndex, mediguns.ChargeLevel(hit.Attacker.EntryIndex) + bonus);
        }

        // THE VITA-SAW'S ORGANS (CTFBonesaw::DoMeleeDamage, tf_weapon_bonesaw.cpp:81-118):
        // a melee hit on an enemy player counts the bearer one more organ and tells the
        // medigun to keep that many charges' worth through the next spawn — the count
        // times VITASAW_CHARGE_PER_HIT, capped inside the medigun by the owner-walked
        // ubercharge_preserved_on_spawn_max (:797, the same attribute that gated this
        // arm on the weapon). Stock's truce gate (:83) has no carrier in this mod, and
        // the add_head_on_hit twin (:93-99) is the Eyelander's row.
        if (attackerIsPlayer && attackerPlayer is { } sawBearer
            && ((DamageType)hit.DamageType & TFDamageBits.Melee) != 0
            && enemy)
        {
            float preserve = attributes.Float(0f, TFMedigunWeapon.VitaSawPreserveAttrib, weaponItem);
            if (preserve > 0f)
            {
                sawBearer.Decapitations += 1;   // :105's SetDecapitations(GetDecapitations() + 1)
                mediguns.PreserveCharge(hit.Attacker.EntryIndex, sawBearer.Decapitations * TFMedigunWeapon.VitaSawChargePerHit);
            }
        }

        // THE BFB'S BOOST (stock tf_weaponbase.cpp:5299-5308, the next arm of
        // the same on-hit walk): a hit the Baby Face's Blaster's
        // boost_on_damage arms pays the attacker's hype — the damage floored
        // at tf_scout_hype_pep_min_damage 5 over tf_scout_hype_pep_mod 1,
        // held at the pep max 99 — through the setter under TFHype.
        if (attackerIsPlayer && attackerPlayer is { } brawler
            && attributes.Int(0, TFHype.BoostAttribute, weaponItem) != 0)
            TFHype.GainBoostOnDamage(brawler, result.Damage);

        // THE KILL HEAL (stock tf_player.cpp:11545-11564, inside OnKilledOther_Effects, which
        // Event_KilledOther reaches once the victim is dead): the held item's heal_on_kill heals
        // the killer — MIN of the attribute and the headroom between the killer's health and the
        // item-inclusive buffed max — through TakeHealth (:11549), and fires player_healonhit
        // with the signed amount, the killer's edict and the held item's defindex (:11552-11563).
        // Stock's death is detected by the engine at the kill itself; here the kill is the same
        // before-minus-final arithmetic the spun-up gate uses, since the seam runs before the hit
        // is applied. The spy's cloak-on-kill (:11513-11521) and the speed boost
        // (:11567-11572) ride the spy slice (TFSpyComponent.Swing, where the knife's kill
        // lands); the restore-to-percentage arm (:11527-11543) waits on the heal lane's
        // carrier (docs/plans/tf2/deferred.tsv).
        if (attackerIsPlayer && !self)
        {
            int healOnKill = attributes.Int(0, "heal_on_kill", weaponItem);          // :11545
            if (healOnKill != 0                                                      // :11546
                && entities.GetHealth(hit.Victim) - result.Damage <= 0)              // the kill: before-minus-final health
            {
                // :11548: MIN of the attribute and the headroom under the buffed max.
                int amount = Math.Min(healOnKill, healing.MaxBuffedHealth(hit.Attacker) - entities.GetHealth(hit.Attacker));
                resupply.TakeHealth(hit.Attacker, amount);                             // :11549's TakeHealth(DMG_GENERIC)

                IntPtr ev = events.EventCreate("player_healonhit");                   // :11552
                if (ev != IntPtr.Zero)                                                // :11553
                {
                    events.EventSetInt(ev, "amount", amount);                         // :11555
                    events.EventSetInt(ev, "entindex", entities.GetIndex(hit.Attacker)); // :11556
                    events.EventSetInt(ev, "weapon_def_index",                        // :11557-11562
                                       weaponItem is AttributeContainer { Item: { } def }
                                           ? def.Defindex : NoItemDefIndex);
                    events.EventFire(ev);                                             // :11563
                }
            }
        }

        // THE FLINCH (CTFPlayer::OnTakeDamage, tf_player.cpp:9599-9621, the
        // tail after the on-hit walk): any hit whose bits name a kind punches
        // the victim's view two degrees down, unless the aimed sniper's
        // far-minigun courtesy holds (:9603-9612) or the punch is refused by
        // their own ApplyPunchImpulseX (:3406-3434) — the Cozy Camper's
        // no-flinch, the charged rifle's aim.
        Flinch(in hit, victim, victimConds, bits, result.Damage, weapon, attackerIsPlayer);

        // THE CONNECTED DAMAGE (tf_player.cpp:9445-9468, after the hit stands):
        // a weapon whose item carries damage_all_connected deals the whole hit
        // again to everyone on the victim's heal graph — the medics healing
        // them and the patient they themselves heal, the closure
        // <c>CTFPlayer::AddConnectedPlayers</c> (tf_player.cpp:10044-10067)
        // walks — each through the full pipeline; the pass's guard keeps it
        // from passing itself on (:9457's gs_pRecursivePlayerCheck).
        int connected = attributes.Int(0, DamageAllConnected, weaponItem);
        if (connected > 0 && attackerIsPlayer && !self && !_passingOn)
        {
            _passingOn = true;
            try
            {
                List<int> seen = [];
                CollectConnected(seen, victimIndex);
                foreach (int index in seen)
                {
                    if (index == victimIndex)
                        continue;

                    EntityHandle other = enginePlayers.GetHandle(index);
                    if (other != EntityHandle.None)
                        hurt.TakeDamage(other, hit.Attacker, result.Damage, (DamageType)hit.DamageType);
                }
            }
            finally
            {
                _passingOn = false;
            }
        }

        return result.Damage;
    }

    /// <summary>
    /// The modify rules for a victim that is not a player and runs the damage
    /// rules (<see cref="ScriptedEntity.RunsDamageRules"/>): stock's
    /// <c>CTFBaseBoss::OnTakeDamage</c> calling
    /// <c>ApplyOnDamageModifyRules( info, this, true )</c>
    /// (<c>player_vs_environment/tf_base_boss.cpp:442</c>), where
    /// <c>pVictim</c> is NULL and every arm behind it is skipped.
    ///
    /// <para>
    /// What such a victim takes is the attacker's half of the rules: the crit
    /// the weapon rolled and the attacker-side crits and mini-crits (the
    /// airborne attacker, the buff banner, the charge's first blow, a
    /// deflected projectile, the crit drink), the weapon's promotions and its
    /// vs-players multiplier (stock asks <c>mult_dmg_vs_players</c> of any
    /// victim that reaches the rules, :6394-6398), the range ramp-up and
    /// fall-off from the two <c>WorldSpaceCenter</c>s, and the crit
    /// multiplier. The engineer's sentry mark stands before them as for a
    /// player (<c>CTFPlayer::ModifyDamageInfo</c> names any target).
    /// </para>
    ///
    /// <para>
    /// What it does not take is the victim's half: no conditions (jarate,
    /// marked for death, the defense buffs, burning), no hit locations and no
    /// headshot (<c>CTFPlayer::TraceAttack</c>'s), no fall formula, no
    /// resists (the alive rules' block is <c>pVictim</c>'s, :7115), no record,
    /// no ignition, and none of the hit's effects on a player victim. The
    /// attacker's on-hit walk and rage (<c>tf_base_boss.cpp:445-470</c>) are
    /// not here either: <c>docs/plans/tf2/deferred.tsv</c>.
    /// </para>
    /// </summary>
    private float ModifyForOther(in DamageContext hit, float damage)
    {
        bool attackerValid = hit.Attacker != EntityHandle.None && entities.IsValid(hit.Attacker);
        bool attackerIsPlayer = attackerValid && entities.IsPlayer(hit.Attacker);
        bool self = hit.Attacker == hit.Victim;
        var attackerPlayer = attackerIsPlayer ? players.Predicted(hit.Attacker.EntryIndex) : null;
        TFCondMask attackerConds = attackerPlayer?.Conditions() ?? default;

        var bits = (DamageType)hit.DamageType;
        if (hit.Critical)
            bits |= TFDamageBits.Critical;

        // The hit's custom kind and weapon, and the weapon's own bits over the
        // caller's, exactly as for a player victim (Modify's opening).
        bool fromScope = scopes.TryCurrent(out var custom, out TFWeaponId weapon, out string weaponName);
        if (!fromScope && attackerIsPlayer)
        {
            weaponName = enginePlayers.GetActiveWeapon(hit.Attacker);
            weapon = weaponName.Length > 0 && scripts.Get(weaponName) is { } script ? script.Id : TFWeaponId.None;
        }

        if (!fromScope && attackerIsPlayer && !self && weapon != TFWeaponId.None)
            bits = (bits & (TFDamageBits.Critical | TFDamageBits.Melee | TFDamageBits.RadiusMax))
                 | TFWeaponDamageTypes.Of(weapon);

        if (attackerIsPlayer)
            damage *= SentryMark(in hit, attackerPlayer);

        float? distance = attackerValid && !self
            ? (entities.GetWorldSpaceCenter(hit.Victim) - entities.GetWorldSpaceCenter(hit.Attacker)).Length
            : null;

        var weaponItem = !fromScope && attackerIsPlayer && weaponName.Length > 0
            ? owners.ItemOf(hit.Attacker.EntryIndex, weaponName) : null;
        if (weaponItem is null && attackerIsPlayer)
            weaponItem = owners.ActiveItemOf(hit.Attacker.EntryIndex);

        var input = new TFHit(damage, bits, custom, self, attackerIsPlayer, weapon, distance,
                              Victim: default, attackerConds, weaponItem, SameClass: false,
                              VictimWaterLevel: 0, DeflectedProjectile: DeflectedProjectile(),
                              VictimEyeAngles: null, ToVictim: null,
                              AttackerAirborne: InAirDueToExplosion(hit.Attacker, attackerConds),
                              VictimAirborne: false,
                              DirectRocketHit: attackerIsPlayer && DirectRocketHit(in hit, weaponName),
                              VictimItem: null, AfterburnDuration: null, DoubleDonk: false,
                              VictimIsPlayer: false);
        var settings = new TFDamageSettings(range.Value, disableSpread.Value, critFalloff.Value, miniCritFalloff.Value);
        var result = TFDamageModifyRules.Apply(in input, in settings, random.RandomFloat, attributes);

        if (debug.Value)
            log.Msg($"[TF2/Damage] {hit.Victim} (not a player) from {hit.Attacker} ({weaponName}): {hit.Damage:F2} -> {result.Damage:F2}, "
                  + $"crit {result.Crit}, custom {result.Custom}, bits 0x{(int)result.Bits:X}"
                  + (distance is { } d ? $", dist {d:F1}" : "") + "\n");

        return result.Damage;
    }

    /// <summary>
    /// THE SENTRY'S MARK's factor (<c>CTFPlayer::ModifyDamageInfo</c>,
    /// tf_player_shared.cpp:10185-10204): the engineer's held weapon's
    /// <c>mult_dmg_bullet_vs_sentry_target</c> while the victim is who the
    /// builder's own live sentry aims at, one otherwise.
    /// </summary>
    private float SentryMark(in DamageContext hit, TFPlayerNetState? attackerPlayer)
    {
        if (attackerPlayer is not { } marker || marker.Class() != TFClass.Engineer)
            return 1f;

        float sentryMark = attributes.Float(1f, "mult_dmg_bullet_vs_sentry_target",
                                            owners.ActiveItemOf(hit.Attacker.EntryIndex));
        if (sentryMark > 1f)
        {
            foreach (ScriptedEntity candidate in directory.OfClass(TFSentryGun.MapClass))
            {
                if (candidate is TFSentryGun { IsDead: false } sentry
                    && sentry.Builder == hit.Attacker && sentry.Target == hit.Victim)
                    return sentryMark;
            }
        }

        return 1f;
    }

    /// <summary>The open blast's inflictor is a rocket or regular pipe a deflection sent back (tf_gamerules.cpp:6159).</summary>
    private bool DeflectedProjectile() =>
        inflictors.TryCurrent(out EntityHandle blastInflictor, out _)
        && directory.Get(blastInflictor) is TFRocket { Deflected: true } or TFPipebomb { DeflectedCount: > 0 };

    /// <summary>The open blast's inflictor is a rocket whose touch was this victim, its owner's item carrying
    /// <c>rocket_specialist</c> (tf_gamerules.cpp:6299-6306).</summary>
    private bool DirectRocketHit(in DamageContext hit, string weaponName) =>
        inflictors.TryCurrent(out EntityHandle rocketInflictor, out _)
        && directory.Get(rocketInflictor) is TFRocket { } directRocket
        && directRocket.DirectVictim == hit.Victim
        && attributes.Int(0, "rocket_specialist",
                          owners.ItemOf(hit.Attacker.EntryIndex, weaponName)) != 0;

    /// <summary>The connected hit's item attribute (<c>damage_all_connected</c>).</summary>
    public const string DamageAllConnected = "damage_all_connected";

    /// <summary>The pass's guard, stock's gs_pRecursivePlayerCheck (:9457) on the layer.</summary>
    private bool _passingOn;

    /// <summary>
    /// <c>CTFPlayer::AddConnectedPlayers</c> (tf_player.cpp:10044-10067): the
    /// player, then their own beam's patient and every healer keeping them up,
    /// each once — the dedup is stock's <c>vecPlayers.Find</c> (:10049).
    /// </summary>
    private void CollectConnected(List<int> seen, int index)
    {
        if (seen.Contains(index))
            return;

        seen.Add(index);

        if (players.Predicted(index) is { } linked && linked.HealTargetIndex > 0)
            CollectConnected(seen, linked.HealTargetIndex);

        foreach (int healer in healing.HealerIndexes(index))
            CollectConnected(seen, healer);
    }

    private int WaterLevel(EntityHandle player) => directory.Get(player) is SsPlayer p ? p.State.WaterLevel : 0;

    /// <summary>FL_ONGROUND, the airborne test's ground bit (const.h).</summary>
    private const uint FlOnGround = 1u << 0;

    /// <summary>
    /// <c>CTFPlayer::InAirDueToExplosion</c> (tf_player.h:599): off the ground,
    /// out of the water and riding a blast jump — whose state lives and dies
    /// with <c>TF_COND_BLASTJUMPING</c> (<c>SetBlastJumpState</c> /
    /// <c>ClearBlastJumpState</c>, tf_player.cpp:8701, 8712) — or the
    /// rocketpack's cond, which answers alone. No entity, no.
    /// </summary>
    private bool InAirDueToExplosion(EntityHandle player, TFCondMask conds)
    {
        if (directory.Get(player) is not SsPlayer p)
            return false;
        return ((p.State.Flags & FlOnGround) == 0 && p.State.WaterLevel == 0 && conds.Has(TFCond.Blastjumping))
            || conds.Has(TFCond.Rocketpack);
    }

    /// <summary>
    /// <c>CTFSniperRifle::IsFullyCharged</c> (tf_weapon_sniperrifle.cpp:565-568):
    /// the charge has reached <c>TF_WEAPON_SNIPERRIFLE_DAMAGE_MAX</c>. The
    /// charge is the attacker's active weapon's (<c>m_flChargedDamage</c>,
    /// <see cref="TFSniperRifleBehaviour.Charge"/>); an unreadable weapon is
    /// not charged.
    /// </summary>
    private bool FullyCharged(EntityHandle attacker) =>
        directory.Get(enginePlayers.GetActiveWeaponEntity(attacker)) is SsWeapon { State: { } state }
        && TFSniperRifleBehaviour.Charge(state, clock.Now) >= TFSniperRifleBehaviour.DamageMax;

    /// <summary>
    /// <c>CTFRevolver::GetDamageType</c>'s freshness gate
    /// (<c>tf_weapon_revolver.cpp:85</c>): a second past the shot's accuracy
    /// stamp (<c>m_flLastAccuracyCheck</c>, the behaviour's kept time on the
    /// held weapon). An unreadable weapon is not fresh; a never-fired gun's
    /// zeroed member is — stock's curtime over it.
    /// </summary>
    private bool AccuracyFresh(EntityHandle attacker) =>
        directory.Get(enginePlayers.GetActiveWeaponEntity(attacker)) is SsWeapon { State: { } state }
        && clock.Now - state.BehaviourKeptTime > 1f;

    /// <summary>The flinch's punch, in degrees of pitch (tf_player.cpp:9617's literal 2).</summary>
    public const float FlinchPunch = 2f;

    /// <summary>The aimed sniper's distance from a minigun beyond which the hit does not
    /// flinch — 750 squared (tf_player.cpp:9607-9608).</summary>
    internal const float MinigunFlinchRangeSq = 750f * 750f;

    /// <summary>The Cozy Camper's no-flinch attribute (tf_player.cpp:3419).</summary>
    public const string NoFlinchAttribute = "aiming_no_flinch";

    /// <summary>
    /// The hit's flinch (<c>CTFPlayer::OnTakeDamage</c>, tf_player.cpp:9599-9621):
    /// a hit with any kind of bit at all and damage that lands, unless the
    /// sniper holding their aim stands over 750 units from a minigun
    /// (:9603-9612). What survives asks the victim's own
    /// <c>ApplyPunchImpulseX</c> (:3406-3434), whose no-flinch is the sniper
    /// aiming through a fully charged sniper rifle whose item carries
    /// <see cref="NoFlinchAttribute"/> — the punch lands when any of that
    /// chain fails, the pitch set, the yaw and roll kept (:3430). The punch
    /// that lands plays the region flinch (<c>PlayFlinch</c>, :15072-15113):
    /// alive and undisguised, the event the hitgroup names.
    /// </summary>
    private void Flinch(in DamageContext hit, TFPlayerNetState victim, TFCondMask victimConds,
                        DamageType bits, float damage, TFWeaponId weapon, bool attackerIsPlayer)
    {
        if (bits == DamageType.Generic || !(damage > 0f))
            return;

        bool flinch = true;
        if (victim.Class() == TFClass.Sniper && victimConds.Has(TFCond.Aiming)
            && attackerIsPlayer && weapon == TFWeaponId.Minigun)
        {
            float dx = entities.GetOrigin(hit.Attacker).X - entities.GetOrigin(hit.Victim).X;
            float dy = entities.GetOrigin(hit.Attacker).Y - entities.GetOrigin(hit.Victim).Y;
            float dz = entities.GetOrigin(hit.Attacker).Z - entities.GetOrigin(hit.Victim).Z;
            if (dx * dx + dy * dy + dz * dz > MinigunFlinchRangeSq)                        // :9607-9608
                flinch = false;
        }

        if (flinch && victim.Class() == TFClass.Sniper && victimConds.Has(TFCond.Aiming))
        {
            string heldName = enginePlayers.GetActiveWeapon(hit.Victim);
            TFWeaponId held = heldName.Length > 0 && scripts.Get(heldName) is { } heldScript
                ? heldScript.Id : TFWeaponId.None;
            if (TFDamageModifyRules.IsSniperRifle(held) && FullyCharged(hit.Victim)
                && attributes.Int(0, NoFlinchAttribute, owners.ManagerOf(hit.Victim.EntryIndex)) != 0)
                flinch = false;                                                            // :3418-3423
        }

        if (!flinch)
            return;

        Vector punch = enginePlayers.GetPunch(hit.Victim);
        Vector velocity = directory.Get(hit.Victim) is SsPlayer holder
            ? holder.State.PunchAngleVel.AsVector() : default;
        enginePlayers.SetPunch(hit.Victim, new Vector(-FlinchPunch, punch.Y, punch.Z), velocity);   // :3430

        if (!enginePlayers.IsAlive(hit.Victim) || victimConds.Has(TFCond.Disguised))       // :15074, :15079
            return;

        SourceSharp.Shared.Anim.PlayerAnimEvent region = hit.HitGroup switch                                       // :15084-15110
        {
            HitGroup.Head => SourceSharp.Shared.Anim.PlayerAnimEvent.FlinchHead,
            HitGroup.LeftArm => SourceSharp.Shared.Anim.PlayerAnimEvent.FlinchLeftArm,
            HitGroup.RightArm => SourceSharp.Shared.Anim.PlayerAnimEvent.FlinchRightArm,
            HitGroup.LeftLeg => SourceSharp.Shared.Anim.PlayerAnimEvent.FlinchLeftLeg,
            HitGroup.RightLeg => SourceSharp.Shared.Anim.PlayerAnimEvent.FlinchRightLeg,
            _ => SourceSharp.Shared.Anim.PlayerAnimEvent.FlinchChest,
        };

        // The wire's copy, the eyes' PVS like every gesture (the owner survives
        // the suppression, their client replays the event from the TE).
        temps.PlayerAnimEvent(TeFilters.Pvs(entities.GetOrigin(hit.Victim)) with { IgnorePredictionCull = true },
                              0f, TempEntWriter.PackEHandle(hit.Victim), (int)region, 0);
    }

    /// <summary>
    /// The landing's two impact halves: the ring of living enemies thrown up
    /// and away (<c>CTFGameRules::PushAllPlayersAway</c>,
    /// <c>tf_gamerules.cpp:21509-21537</c>), then the burning teammates in
    /// sight of the landing put out (<c>tf_player.cpp:9014-9034</c>). The
    /// bonus-points half (:9033) rides the gamestats ledger, which waits
    /// there too.
    /// </summary>
    private void RocketPackImpact(EntityHandle landingPlayer, float radius, float force)
    {
        Vector origin = entities.GetOrigin(landingPlayer);
        int landingTeam = enginePlayers.GetTeam(landingPlayer);

        Span<EntityHandle> nearby = stackalloc EntityHandle[16];
        int count = world.EntitiesInSphere(origin, radius, nearby);

        // The ring (:21518-21530): living enemies whose eye sits inside it,
        // thrown along the flat line out with a full unit of lift.
        for (int i = 0; i < count; i++)
        {
            EntityHandle other = nearby[i];
            if (other == EntityHandle.None || other == landingPlayer
                || !enginePlayers.IsAlive(other) || enginePlayers.GetTeam(other) == landingTeam)
                continue;

            Vector toPlayer = entities.GetEyePosition(other) - origin;
            float dx = toPlayer.X, dy = toPlayer.Y, dz = toPlayer.Z;
            if (dx * dx + dy * dy + dz * dz >= radius * radius)
                continue;

            float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);                       // :21525
            Vector away = len > 0f ? new Vector(dx / len, dy / len, 1f) : new Vector(0f, 0f, 1f);   // :21524-21526
            entities.ApplyVelocityImpulse(other, away * force, clearGround: false);   // :21528-21530
        }

        // The teammates (:9014-9034): living, of the landing team, in sight —
        // the burning taken off and the flame-out heard.
        for (int i = 0; i < count; i++)
        {
            EntityHandle other = nearby[i];
            if (other == EntityHandle.None || !entities.IsPlayer(other) || !enginePlayers.IsAlive(other))
                continue;
            if (enginePlayers.GetTeam(other) != landingTeam)
                continue;

            Vector teammateOrigin = entities.GetOrigin(other);
            float ox = teammateOrigin.X - origin.X, oy = teammateOrigin.Y - origin.Y, oz = teammateOrigin.Z - origin.Z;
            if (ox * ox + oy * oy + oz * oz > radius * radius)
                continue;

            if (!conditions.InCond(other, TFCond.Burning))
                continue;

            // FVisible, MASK_OPAQUE (:9028): the landing eye's line to the
            // teammate, stopped only by the world and the teammate itself.
            TraceResult seen = world.TraceLine(entities.GetEyePosition(landingPlayer), teammateOrigin,
                                               TraceMask.Solid, landingPlayer);
            if (seen.Hit && seen.Entity != other)
                continue;

            conditions.Remove(other, TFCond.Burning);                        // :9031
            sound.EmitSoundAtPosition("TFPlayer.FlameOut", teammateOrigin);  // :9032
        }
    }

    /// <summary><c>RemapValClamped</c> (mathlib.h): <paramref name="val"/>'s
    /// place in the source range, taken on the destination range, ends
    /// included.</summary>
    private static float RemapClamped(float val, float aMin, float aMax, float bMin, float bMax)
        => bMin + (bMax - bMin) * Math.Clamp((val - aMin) / (aMax - aMin), 0f, 1f);

    /// <summary>
    /// The victim's banked shoves, spent: the frame's tick (the victim's
    /// <see cref="TFScattergunKnockback"/>) totals each attacker's banked
    /// damage and, one over <see cref="ScattergunKnockbackMinDamage"/>, shoves
    /// the pair — stock's gate over the shot's whole multi-damage
    /// (<c>tf_weapon_shotgun.cpp:52</c>). The first shove sets the airtime's
    /// latch; the victim's other banked pairs die with it, the same pairs
    /// stock's per-event latch would have refused. A shot that never reaches
    /// the gate leaves nothing banked behind.
    /// </summary>
    public void FlushShoves(int victimIndex)
    {
        bool banked = false;
        bool shoved = false;

        for (int i = 0; i < _shoves.Count; i++)
        {
            (EntityHandle victim, EntityHandle attacker, float damage) = _shoves[i];
            if (victim.EntryIndex != victimIndex)
                continue;

            banked = true;
            if (!shoved && damage > ScattergunKnockbackMinDamage)
            {
                Shove(victim, attacker, damage);
                shoved = true;
            }
        }

        if (banked)
            _shoves.RemoveAll(shove => shove.Victim.EntryIndex == victimIndex);
    }

    /// <summary>The shove itself, the arm's body over the shot's total damage.</summary>
    private void Shove(EntityHandle victim, EntityHandle attacker, float damage)
    {
        int victimIndex = victim.EntryIndex;
        Vector shoveDirection = (entities.GetWorldSpaceCenter(attacker)
                               - entities.GetWorldSpaceCenter(victim)).Normalized;
        (Vector shoveMins, Vector shoveMaxs) = entities.GetBounds(victim);
        Vector shove = shoveDirection * -TFRadiusDamage.DamageForce(shoveMaxs - shoveMins, damage,
                                                                   ScattergunKnockbackForceScale)
                     + new Vector(0f, 0f, TFAirBlastDefs.JumpMinSpeed);

        if (networking.EntityGetState(victim, out _).HasFlag(EntityState.OnGround)
            && shove.Z < TFAirBlastDefs.JumpMinSpeed)
            shove = new Vector(shove.X, shove.Y, TFAirBlastDefs.JumpMinSpeed);

        if (players.Predicted(victimIndex) is { } shovedState)
            shovedState.WeaponKnockback = true;

        conditions.Add(victim, TFCond.KnockedIntoAir);

        // The aim's brace on the shove (<c>CTFPlayer::ApplyAbsVelocityImpulse</c>,
        // tf_player.cpp:3339-3363): a sniper holding their aim takes every
        // impulse through its <c>mult_aiming_knockback_resistance</c>
        // (:3344-3346), the whole shove landing scaled (:3363). Stock's hook
        // walks the aiming player, so the brace reads the player's own manager:
        // the Cozy Camper is worn, never the held rifle.
        float brace = 1f;
        if (players.Predicted(victimIndex) is { } steady && steady.Class() == TFClass.Sniper
            && conditions.InCond(victim, TFCond.Aiming))
            brace = attributes.Float(brace, "mult_aiming_knockback_resistance", owners.ManagerOf(victimIndex));

        entities.ApplyVelocityImpulse(victim, shove * brace, clearGround: true);
        carriers.Part<TFStunStack>(victimIndex)?.Stun(victim, TFScattergunBehaviour.StunTime, 1f,
            TFStunFlags.Movement | TFStunFlags.MovementForwardOnly, attacker);
    }
}

/// <summary>
/// TF's half of the damage question (<see cref="IDamageRule"/>): an
/// invulnerable player takes nothing (<c>ApplyOnDamageModifyRules</c>,
/// <c>tf_gamerules.cpp:6366-6391</c>, which leaves an übered player the
/// damage events and no damage), except from what stock lets through the
/// über (<c>CTFPlayer::OnTakeDamage</c>, <c>tf_player.cpp:9204-9236</c>): a
/// <c>trigger_hurt</c>, a <c>point_hurt</c> with the <c>SF_PHURT_HURT_UBER</c>
/// spawnflag, and a hit that arrived carrying the telefrag custom.
/// </summary>
[SideService(Side.Server)]
public sealed class TFDamageRules(ITFPlayerData players, IEngineEntities entities, TFDamageScopes scopes,
                                  TFPlayerCarriers carriers)
    : IDamageRule, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 702;

    /// <summary>The trigger whose damage stock lets through the über.</summary>
    public const string TriggerHurt = "trigger_hurt";

    /// <summary>The hurting point entity (pointhurt.h's <c>LINK_ENTITY_TO_CLASS</c>).</summary>
    public const string PointHurt = "point_hurt";

    /// <summary><c>SF_PHURT_HURT_UBER</c> (pointhurt.h:5).</summary>
    public const int HurtUberSpawnflag = 2;

    public int HookOrder => Order;

    public Decision Decide(in DamageContext hit)
    {
        if (players.Predicted(hit.Victim.EntryIndex) is not { } victim)
            return Decision.UseStockRule;

        TFCondMask conds = victim.Conditions();

        // Stock's phase arm (tf_player.cpp:9329-9361): while phasing (or on
        // PASS Time interception), nothing lands while the caller asked the
        // plain way; the dodged damage is tallied on the phase for the
        // slowdown the phase's end pays out (:9355-9359, only the phase
        // itself tallies). What arrives carrying the allow-damage names —
        // the same inflictor arms the über's passthrough asks about — passes
        // the refuse as stock's <c>bAllowDamage == false</c> gate does.
        if (conds.Has(TFCond.Phase) || conds.Has(TFCond.PasstimeInterception))
        {
            if (TriggerHurtCustom(in hit))
                return Decision.UseStockRule;

            carriers.ConditionsOf(hit.Victim.EntryIndex)?.AddPreventedDamage(TFCond.Phase, (int)hit.Damage);
            return Decision.No;
        }

        if (!new TFConditionEffects().IsInvulnerable(in conds))
            return Decision.UseStockRule;

        // Stock's über answer (tf_gamerules.cpp:6366-6391): with the über on,
        // the hit is not refused outright — the victim's health is held whole
        // but the hit's events and push still run, DAMAGE_EVENTS_ONLY. Only
        // the bAllowDamage passthrough lands the hit in full.
        return UberPassthrough(in hit) ? Decision.UseStockRule : Decision.EventsOnly;
    }

    /// <summary>
    /// Stock's <c>bAllowDamage</c> arms, in their order (tf_player.cpp:9204-9236).
    /// A point_hurt answers only for its own spawnflag — the arms are an
    /// else-if chain, so a flagless point_hurt is refused even when a telefrag
    /// custom rides the hit — then a trigger_hurt, then the telefrag custom.
    /// The attacker stands where stock reads the inflictor: our damage context
    /// carries no inflictor, and every inflictor this asks about reaches
    /// OnTakeDamage as the attacker here (docs/divergences.md).
    /// </summary>
    public bool UberPassthrough(in DamageContext hit)
    {
        if (IsPointHurt(hit.Attacker))
            return (entities.GetSpawnFlags(hit.Attacker) & HurtUberSpawnflag) != 0;

        if (TriggerHurtCustom(in hit))
            return true;

        return scopes.TryCurrent(out var custom, out _, out _) && custom == TFDamageCustom.Telefrag;
    }

    /// <summary>
    /// Whether the hit rides in as a <c>trigger_hurt</c>'s custom — the two
    /// arms of stock's <c>OnTakeDamage</c> that call
    /// <c>info.SetDamageCustom( TF_DMG_CUSTOM_TRIGGER_HURT )</c>
    /// (tf_player.cpp:9208-9211, 9218-9221) before the modify rules, so the
    /// kill record names the trigger rather than a weapon. A flagless
    /// point_hurt sets nothing.
    /// </summary>
    public bool TriggerHurtCustom(in DamageContext hit)
        => TriggerHurtCustom(entities, in hit);

    /// <summary>The static form, for the damage layer, which holds no rules.</summary>
    public static bool TriggerHurtCustom(IEngineEntities entities, in DamageContext hit)
        => (IsPointHurt(entities, hit.Attacker) && (entities.GetSpawnFlags(hit.Attacker) & HurtUberSpawnflag) != 0)
        || IsTriggerHurt(entities, hit.Attacker);

    private bool IsPointHurt(EntityHandle attacker) => IsPointHurt(entities, attacker);

    private bool IsTriggerHurt(EntityHandle attacker) => IsTriggerHurt(entities, attacker);

    private static bool IsPointHurt(IEngineEntities entities, EntityHandle attacker)
        => IsNamed(entities, attacker, PointHurt);

    private static bool IsTriggerHurt(IEngineEntities entities, EntityHandle attacker)
        => IsNamed(entities, attacker, TriggerHurt);

    private static bool IsNamed(IEngineEntities entities, EntityHandle attacker, string className)
        => attacker != EntityHandle.None && entities.IsValid(attacker)
        && string.Equals(entities.GetClassName(attacker), className, StringComparison.Ordinal);
}
