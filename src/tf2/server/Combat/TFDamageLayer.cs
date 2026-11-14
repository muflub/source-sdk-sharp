//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

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
/// (one with a carrier; any other victim's hit passes untouched):
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
                                  IEngineDamage hurt, ITFPlayerHealing healing)
    : IDamageModifier, IOrderedHook
{
    /// <summary>Stage 2.7's band (+700..+799 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 701;

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

    public int HookOrder => Order;

    public float Modify(in DamageContext hit, float damage)
    {
        int victimIndex = hit.Victim.EntryIndex;
        if (players.Predicted(victimIndex) is not { } victim)
            return damage;

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
            // table's own bits answer the rest.
            bits = (bits & (TFDamageBits.Critical | TFDamageBits.Melee)) | TFWeaponDamageTypes.Of(weapon);

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
            // revolver's stock gate on its last accuracy check more than a
            // second ago (m_flLastAccuracyCheck, :85) has no clock here
            // (docs/divergences.md).
            else if (weapon is TFWeaponId.Smg or TFWeaponId.ChargedSmg or TFWeaponId.Revolver)
            {
                if (attributes.Int(0, "set_weapon_mode", owners.ItemOf(hit.Attacker.EntryIndex, weaponName)) == 1)
                    bits |= TFDamageBits.UseHitLocations;
            }
            else if (weapon == TFWeaponId.HandgunScoutSecondary
                     && attributes.Int(0, "back_headshot", owners.ItemOf(hit.Attacker.EntryIndex, weaponName)) != 0)
            {
                bits |= TFDamageBits.UseHitLocations;
            }
        }

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
            damage = TFFallDamage.Damage(TFFallDamage.FallSpeedOf(damage), maxHealth, fallDisableSpread.Value,
                                         random.RandomFloat);
            if (!(damage > 0f))
                return 0f;
        }

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
        bool deflectedProjectile = inflictors.TryCurrent(out EntityHandle blastInflictor, out _)
            && directory.Get(blastInflictor) is TFRocket { Deflected: true } or TFPipebomb { DeflectedCount: > 0 };

        // The rocket specialist's direct hit (tf_gamerules.cpp:6299-6306): the
        // inflictor a rocket whose enemy — the touch, stock's GetEnemy — is
        // this victim, and the attacker's item carries rocket_specialist. The
        // level itself is the radius arm's; here only its presence gates the
        // long-range falloff.
        bool directRocketHit = attackerIsPlayer
            && inflictors.TryCurrent(out EntityHandle rocketInflictor, out _)
            && directory.Get(rocketInflictor) is TFRocket { } directRocket
            && directRocket.DirectVictim == hit.Victim
            && attributes.Int(0, "rocket_specialist",
                              owners.ItemOf(hit.Attacker.EntryIndex, weaponName)) != 0;

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
                              AfterburnDuration: afterburns.Of(victimIndex)?.Burn.AfterburnDuration);
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

        result = result with
        {
            Damage = TFDamageAliveRules.Resist(result, in input, attributes, owners.ManagerOf(victimIndex),
                                               victim.Class(), stealth.Value, spunUp, fromSentry)
        };

        if (debug.Value)
            log.Msg($"[TF2/Damage] {hit.Victim} from {hit.Attacker} ({weaponName}): {hit.Damage:F2} -> {result.Damage:F2}, "
                  + $"crit {result.Crit}, custom {result.Custom}, bits 0x{(int)result.Bits:X}"
                  + (distance is { } d ? $", dist {d:F1}" : "") + "\n");

        float now = clock.Now;
        bool enemy = attackerIsPlayer && !self && enginePlayers.GetTeam(hit.Attacker) != enginePlayers.GetTeam(hit.Victim);
        records.Note(victimIndex,
                     new TFHitRecord(attackerValid ? hit.Attacker : EntityHandle.None, result.Damage, result.Bits,
                                     result.Crit, result.Custom, weapon, weaponName, now),
                     damager: enemy);

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

        // THE KILL HEAL (stock tf_player.cpp:11545-11564, inside OnKilledOther_Effects, which
        // Event_KilledOther reaches once the victim is dead): the held item's heal_on_kill heals
        // the killer — MIN of the attribute and the headroom between the killer's health and the
        // item-inclusive buffed max — through TakeHealth (:11549), and fires player_healonhit
        // with the signed amount, the killer's edict and the held item's defindex (:11552-11563).
        // Stock's death is detected by the engine at the kill itself; here the kill is the same
        // before-minus-final arithmetic the spun-up gate uses, since the seam runs before the hit
        // is applied. The spy's cloak-on-kill (:11513-11521), the restore-to-percentage arm
        // (:11527-11543) and the speed boost (:11567 onward) wait on their lanes' carriers.
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

        return result.Damage;
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
                                  TFPlayerCarriers? carriers = null)
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

            carriers?.ConditionsOf(hit.Victim.EntryIndex)?.AddPreventedDamage(TFCond.Phase, (int)hit.Damage);
            return Decision.No;
        }

        if (!new TFConditionEffects().IsInvulnerable(in conds))
            return Decision.UseStockRule;

        return UberPassthrough(in hit) ? Decision.UseStockRule : Decision.No;
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
