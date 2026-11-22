//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Player;

/// <summary>The classnames TF2 adds to the hierarchy.</summary>
public static class TFEntityNames
{
    /// <summary>The per-player predicted carrier (stage1's minimal carrier, Stage 2.3's <c>tf_player_data</c>).</summary>
    public const string PlayerData = "tf_player_data";

    /// <summary>Stock's <c>CTFPlayerResource</c>: the TF scoreboard columns (<c>LINK_ENTITY_TO_CLASS( tf_player_manager, ... )</c>).</summary>
    public const string PlayerResource = "tf_player_manager";

    /// <summary>Stock's <c>CTFWearable</c>: a worn cosmetic (<c>LINK_ENTITY_TO_CLASS( tf_wearable, CTFWearable )</c>, tf_item_wearable.cpp:27).</summary>
    public const string Wearable = "tf_wearable";

    /// <summary>The engineer's building status carrier (stock networks each building; ours carries the HUD's columns).</summary>
    public const string BuildingResource = "tf_building_resource";

    /// <summary>Stock's <c>CObjectSentrygun</c> (<c>CREATE_CLASSNAME( obj_sentrygun )</c>,
    /// tf_obj_sentrygun.cpp) — the sentry the server spawns and the client mirrors
    /// (the server's own list is <c>TFObjectClassNames</c>).</summary>
    public const string SentryGun = "obj_sentrygun";

    /// <summary>Stock's <c>CObjectDispenser</c> (<c>CREATE_CLASSNAME( obj_dispenser )</c>, tf_obj_dispenser.cpp).</summary>
    public const string Dispenser = "obj_dispenser";

    /// <summary>Stock's <c>CObjectTeleporter</c> (<c>CREATE_CLASSNAME( obj_teleporter )</c>, tf_obj_teleporter.cpp).</summary>
    public const string Teleporter = "obj_teleporter";

    /// <summary>Stock's <c>CObjectSapper</c> (<c>LINK_ENTITY_TO_CLASS( obj_attachment_sapper, CObjectSapper )</c>, tf_obj_sapper.cpp).</summary>
    public const string Sapper = "obj_attachment_sapper";
}

/// <summary>
/// What TF2 keeps per player beside the core's player, networked by the
/// <c>tf_player_data</c> carrier (one per player, owner = the player's entity
/// index) and predicted by the local player's client. The start of Stage 2.3's
/// <c>TFPlayerNetState</c> (stage1, the 2026-10-13 ruling): the fields the
/// movement rules (1.8) and the weapons (1.9) read and write during a command.
///
/// <para>
/// Each field is one of <c>CTFPlayerShared</c>'s (tf_player_shared.cpp:444-482,
/// <c>BEGIN_PREDICTION_DATA_NO_BASE( CTFPlayerShared )</c>) or
/// <c>CTFWeaponBase</c>'s (tf_weaponbase.cpp:231-248) predicted fields, named
/// for it; the class is <c>CTFPlayerClassShared::m_iClass</c>. Stock predicts
/// every one of them but <see cref="CritMult"/>, which it only sends
/// (tf_player_shared.cpp:539), and that is the one field here without
/// <c>[Predicted]</c>.
/// </para>
///
/// <para>
/// THE CRIT SLOT (for the weapons lane, 1.9). Stock keeps the crit roll's
/// predicted state on each weapon (<c>m_bCurrentAttackIsCrit</c>,
/// <c>m_iCurrentSeed</c>, <c>m_flLastCritCheckTime</c>) and the rapid-fire crit
/// window in <c>m_flCritTime</c>; the core carries ONE weapon entity per player
/// whose state is the core's, so the TF crit state rides here, per player, and
/// is the active weapon's. <see cref="CritMult"/> is the player's
/// <c>m_iCritMult</c> (0..255, the damage-dealt crit multiplier the server
/// keeps). A crit boost is not here: it is a condition
/// (<c>TFCond.Critboosted</c> and friends) in the five condition words.
/// </para>
/// </summary>
[StandaloneNetState(TFEntityNames.PlayerData)]
public partial class TFPlayerNetState
{
    /// <summary>Entindex of the player this carrier is for; 0 for nobody.</summary>
    [Predicted]
    public partial int OwnerIndex { get; set; }

    /// <summary><c>CTFPlayerClassShared::m_iClass</c>: a <see cref="TFClass"/>.</summary>
    [Predicted]
    public partial int PlayerClass { get; set; }

    /// <summary><c>m_nDisguiseClass</c>: the class a disguised spy shows (a <see cref="TFClass"/>).</summary>
    [Predicted]
    public partial int DisguiseClass { get; set; }

    /// <summary><c>m_nDisguiseTeam</c>: the team a disguised spy shows (a <see cref="TFTeam"/>).</summary>
    [Predicted]
    public partial int DisguiseTeam { get; set; }

    /// <summary>
    /// <c>m_nDesiredDisguiseClass</c>: the class of the disguise now going on
    /// (tf_player_shared.cpp:8246, where stock predicts it :465 and sends it
    /// :512). Sent, not predicted, here: the ask is a console line the server
    /// takes (<c>disguise</c>), outside the usercmd the replay reads, so the
    /// write is the arrival's word (the <see cref="NoAttackExpire"/> rule)
    /// and the completion — the field's reader — rides the same snapshot
    /// into the replay's <c>DisguiseTick</c>.
    /// </summary>
    public partial int DesiredDisguiseClass { get; set; }

    /// <summary>
    /// <c>m_nDesiredDisguiseTeam</c>: the team of the disguise now going on
    /// (tf_player_shared.cpp:8247, :466, :513). Sent, not predicted, as its
    /// class above; <c>OnRemoveDisguising</c> :7218 sends it back undefined
    /// with the disguising's end, on both sides.
    /// </summary>
    public partial int DesiredDisguiseTeam { get; set; }

    /// <summary>
    /// <c>m_flDisguiseCompleteTime</c>: when the disguising completes
    /// (tf_player_shared.cpp:8268, a plain <c>DEFINE_FIELD</c> :474). Stock
    /// sends it nowhere because its client runs the press and completes the
    /// disguise locally; here the press is the server's, so the deadline
    /// rides to the replay that honours it — sent, not predicted, the
    /// arrival's word. Zero once a completion has run (:8343).
    /// </summary>
    public partial float DisguiseCompleteTime { get; set; }

    /// <summary>
    /// <c>m_nMaskClass</c>: the class whose mask a spy disguised as a spy
    /// wears (tf_player_shared.cpp:8309, drawn at the completion). Sent, not
    /// predicted, as the target above: the roll is the server's
    /// <c>rand()</c>, no replay reproduces it, and the model that wears it is
    /// the disguise-display lane (docs/plans/tf2/deferred.tsv) — stock's
    /// readers are the masked skin and bodygroup, c_tf_player.cpp:7857, :9774.
    /// </summary>
    public partial int MaskClass { get; set; }

    /// <summary>
    /// <c>m_flCloakMeter</c>: the spy's cloak charge, 0..100 (tf_player_shared.cpp:475,
    /// predicted; drained by <c>UpdateCloakMeter</c>:14165 and filled by
    /// <c>m_flCloakRegenRate</c>). The invisibility watch's activation gates on
    /// it (<c>tf_weapon_invis.cpp:243</c>: over 8.0 to cloak).
    /// </summary>
    [Predicted]
    public partial float CloakMeter { get; set; }

    /// <summary>
    /// <c>m_flInvisChangeCompleteTime</c>: when the cloak's fade in or out is
    /// done (tf_player_shared.cpp:969, sent by SendPropTime :576 and predicted
    /// :458). <see cref="TFInvisibility"/> derives the invisibility level from
    /// it and the conditions, as stock's client recomputes them in
    /// <c>InvisibilityThink</c>.
    /// </summary>
    [Predicted]
    public partial float InvisChangeCompleteTime { get; set; }

    /// <summary>
    /// <c>m_flStealthNoAttackExpire</c>: until when a fresh decloak refuses
    /// the spy's attack (tf_player_shared.cpp:7968 stamps it from
    /// <c>tf_spy_cloak_no_attack_time</c>, the decloak-rate scale and the
    /// caller's, and <c>CanAttack</c> :12226 reads it). Sent by SendPropTime
    /// :515 and read by RecvPropTime :337, but absent from the prediction
    /// data :444-482 — sent, not predicted, the arrival's word (CritMult's
    /// rule): the server stamps it in a decloak's frame and the client
    /// believes the snapshot, so the refusal reads it on both sides.
    /// </summary>
    public partial float NoAttackExpire { get; set; }

    /// <summary>
    /// <c>m_bFeignDeathReady</c>: the Dead Ringer's armed-and-waiting bit
    /// (tf_player_shared.h:1087, sent by SendPropBool :583 and predicted
    /// :468). The watch's press arms it from a full meter and the feigned
    /// death spends it (tf_weapon_invis.cpp:237-241, tf_player.cpp:9678);
    /// the feign condition's own add takes it back (tf_player_shared.cpp:7188).
    /// </summary>
    [Predicted]
    public partial bool FeignDeathReady { get; set; }

    /// <summary>
    /// <c>m_angEyeAngles[PITCH]</c> (tf_player.cpp:731, :751): the player's eye
    /// pitch as stock sends it to the other players, an 8-bit
    /// <c>SendPropFloat</c> over [-90, 90] (<see cref="TFEyeAngleWire"/>).
    /// Copied from <c>EyeAngles()</c> each think (:2687); the observer's in-eye
    /// view looks along it. Not predicted, as stock's is not.
    /// </summary>
    [NetField(NetFieldKind.UInt, Bits = TFEyeAngleWire.PitchBits, Group = "eyes")]
    public partial int EyePitch { get; set; }

    /// <summary>
    /// <c>m_angEyeAngles[YAW]</c>: a 10-bit <c>SendPropAngle</c> (<see cref="TFEyeAngleWire"/>).
    /// </summary>
    [NetField(NetFieldKind.UInt, Bits = TFEyeAngleWire.YawBits, Group = "eyes")]
    public partial int EyeYaw { get; set; }

    /// <summary><c>m_iAirDash</c>: air dashes taken since the player last stood on ground.</summary>
    [Predicted]
    public partial int AirDash { get; set; }

    /// <summary>
    /// <c>m_nAirDucked</c> (tf_player_shared.h:543-544, :1114): how many ducks
    /// this airtime has had, counted on each duck release in the air and
    /// refused past <c>TF_AIRDUCKED_COUNT</c> (2, tf_gamemovement.cpp:90, the
    /// one-duck-per-air-event arm of <c>DuckOverrides</c> :3219-3222). Reset
    /// with the air dash and the ground touch (:1258, :2959) and on the self
    /// blast's push (tf_player.cpp:10277-10278). Predicted as stock's: the
    /// send rides <c>SendPropInt</c> 2-bit unsigned
    /// (tf_player_shared.cpp:541) and the replay's word is
    /// <c>DEFINE_PRED_FIELD</c> (:456).
    /// </summary>
    [Predicted]
    public partial int AirDuckedCount { get; set; }

    /// <summary>
    /// <c>m_flDuckTimer</c> (tf_player_shared.h:545, :1115): when the next duck
    /// is allowed after a release — a release stamps it at
    /// <c>curtime + TF_TIME_TO_DUCK</c> (0.3, tf_gamemovement.cpp:89;
    /// <c>OnUnDuck</c> :3293-3295) and <c>DuckOverrides</c> :3194-3197 hides
    /// the duck button while it runs and the player is on the ground.
    /// Predicted as stock's (<c>DEFINE_PRED_FIELD</c>, tf_player_shared.cpp:457).
    /// </summary>
    [Predicted]
    public partial float DuckTimer { get; set; }

    /// <summary>
    /// <c>m_bScattergunJump</c> (tf_player_shared.h:1218, predicted at
    /// tf_player_shared.cpp:481): the Force-a-Nature's own shove has lifted
    /// this airtime, set by the gun's fire (<c>CTFScatterGun::FireBullet</c>,
    /// tf_weapon_shotgun.cpp:333) and cleared with the air dash on landing
    /// (<c>CTFGameMovement::SetGroundEntity</c>, tf_gamemovement.cpp:2957).
    /// </summary>
    [Predicted]
    public partial bool ScattergunJump { get; set; }

    /// <summary>
    /// <c>m_iWeaponKnockbackID &gt; -1</c>, the airtime half the stock field
    /// carries (tf_player_shared.h:1125): the player has already been shoved
    /// by a gun's hit (<c>CTFScatterGun::ApplyPostHitEffects</c>' gate,
    /// tf_weapon_shotgun.cpp:383) and waits on the ground to be shovable
    /// again (cleared beside the dash, :2956). Stock's id names the shoving
    /// user for the MvM quest counters; the shove once per airtime is the
    /// player's part, so the bit stands in for the id here.
    /// </summary>
    [Predicted]
    public partial bool WeaponKnockback { get; set; }

    /// <summary>
    /// <c>m_hDisguiseTarget</c>: the player the disguise borrows its name
    /// from, as an entity index; -1 for none. Sent, not predicted, as
    /// stock's (SendPropEHandle, tf_player_shared.cpp:581, with no
    /// DEFINE_PRED_FIELD at :447-473): the server's choice, news to the
    /// client rather than a prediction (CritMult's rule). Readers ask only
    /// under <c>TF_COND_DISGUISED</c>, which the server raises with the
    /// target written, so the blob's zeroed start is never read.
    /// </summary>
    public partial int DisguiseTargetIndex { get; set; }

    /// <summary>
    /// <c>m_iDisguiseHealth</c>: the health an enemy reading the disguised
    /// spy is shown (tf_hud_target_id.cpp:765-838). Set at the disguise's
    /// completion — the target's health, the disguise class's max when the
    /// target is dead, a half-to-full roll of the spy's own max with no
    /// target (CompleteDisguise, tf_player_shared.cpp:8321-8334) — raised by
    /// healers and reset to none at the disguise's end. It does not drain
    /// with the spy's real damage. Sent, not predicted, as stock's
    /// (SendPropInt SPROP_VARINT, tf_player_shared.cpp:582).
    /// </summary>
    public partial int DisguiseHealth { get; set; }

    /// <summary><c>m_nPlayerCond</c>: conditions 0-31 (<see cref="Conditions.TFCondMask.Word0"/>).</summary>
    [Predicted]
    public partial int PlayerCond { get; set; }

    /// <summary><c>m_nPlayerCondEx</c>: conditions 32-63.</summary>
    [Predicted]
    public partial int PlayerCondEx { get; set; }

    /// <summary><c>m_nPlayerCondEx2</c>: conditions 64-95.</summary>
    [Predicted]
    public partial int PlayerCondEx2 { get; set; }

    /// <summary><c>m_nPlayerCondEx3</c>: conditions 96-127.</summary>
    [Predicted]
    public partial int PlayerCondEx3 { get; set; }

    /// <summary><c>m_nPlayerCondEx4</c>: conditions 128-130.</summary>
    [Predicted]
    public partial int PlayerCondEx4 { get; set; }

    /// <summary>
    /// <c>m_iCritMult</c>: the crit chance multiplier earned by damage dealt,
    /// 0..255 for 1x..4x (<c>RemapValClamped</c>, tf_player_shared.cpp:9506).
    /// Sent, not predicted, as stock's (tf_player_shared.cpp:539); written only
    /// by the server.
    /// </summary>
    public partial int CritMult { get; set; }

    /// <summary><c>m_bCurrentAttackIsCrit</c>: the attack being fired rolled a crit.</summary>
    [Predicted]
    public partial bool CurrentAttackIsCrit { get; set; }

    /// <summary><c>m_iCurrentSeed</c>: the seed the last crit roll used.</summary>
    [Predicted]
    public partial int CurrentCritSeed { get; set; }

    /// <summary><c>m_flLastCritCheckTime</c>, at stock's <c>TD_MSECTOLERANCE</c>.</summary>
    [Predicted(0.001f)]
    public partial float LastCritCheckTime { get; set; }

    /// <summary><c>m_flCritTime</c>: until when a rapid-fire weapon's crit window lasts.</summary>
    [Predicted(0.001f)]
    public partial float CritTime { get; set; }

    /// <summary>
    /// <c>m_iDecapitations</c> (tf_player_shared.h:1157): the Eyelander's
    /// collected heads. Sent, not predicted — the kill is the server's news
    /// (CritMult's rule); the health and speed bonuses read it.
    /// </summary>
    public partial int Decapitations { get; set; }

    /// <summary>
    /// <c>m_flChargeMeter</c> (tf_player_shared.h:1102): the demoman shield's
    /// charge, 0..100 — filled by <c>UpdateChargeMeter</c>'s regen
    /// (tf_player_shared.cpp:6037-6064), spent by the charge
    /// (<c>DoCharge</c> through <c>EndCharge</c>'s drain to the impact) and
    /// the swing's <c>EndSpecialAction</c>. Predicted, as stock's
    /// (<c>DEFINE_PRED_FIELD</c>, tf_player_shared.cpp:453): the meter's drain
    /// and refill run in the shared think both sides mirror
    /// (<c>ConditionThink</c>, :3118).
    /// </summary>
    [Predicted]
    public partial float ChargeMeter { get; set; }

    /// <summary>
    /// <c>m_flEnergyDrinkMeter</c> (tf_player_shared.h:1098): the scout's
    /// energy drink charge, 0..100, zero from the ctor (:887) — spent by the
    /// drink's boost at the consume rate and regained toward full between
    /// boosts at the regen rate
    /// (<c>UpdateEnergyDrinkMeter</c>, tf_player_shared.cpp:14080-14119),
    /// snapped full by <c>Regenerate</c> (tf_player.cpp:4097). Predicted, as
    /// stock's (<c>DEFINE_PRED_FIELD</c> :451, sent too by
    /// <c>SendPropFloat</c> :569): the meter's drain and refill run in the
    /// shared think both sides mirror (<c>ConditionThink</c>, :3118).
    /// </summary>
    [Predicted]
    public partial float EnergyDrinkMeter { get; set; }

    /// <summary>
    /// <c>m_iNextMeleeCrit</c> (tf_player_shared.h:1169): the next melee
    /// swing's granted crit — the charge end's <c>CalcChargeCrit</c> ladder
    /// (:6125-6146), spent by the melee crit helpers
    /// (tf_weaponbase_melee.cpp:1053-1058, 1100-1105) and cleared by
    /// <c>RemoveMeleeCrit</c> 0.3 s later (:6221-6231). Sent, not predicted,
    /// as stock's (SendPropInt :557, absent from the prediction table
    /// :444-482): the grant is the server's news (CritMult's rule), the same
    /// ride <see cref="Decapitations"/> takes.
    /// </summary>
    public partial int NextMeleeCrit { get; set; }

    /// <summary>
    /// <c>m_iRevengeCrits</c> (tf_player_shared.h:1167): the revenge window —
    /// the Frontier Justice's crits the owner's sentry's death pays
    /// (<c>CTFShotgun_Revenge::SentryKilled</c>, tf_weapon_shotgun.cpp:151-163),
    /// spent one a shot, and the Manmelter's banked vengeance, clamped by
    /// <c>CTFPlayerShared::SetRevengeCrits</c> (tf_player_shared.cpp:14474).
    /// Sent, not predicted, as stock's (SendPropInt 7 unsigned bits,
    /// tf_player_shared.cpp:559, absent from the prediction table :444-482):
    /// the grant is the sentry's dying news (CritMult's rule), the same ride
    /// <see cref="Decapitations"/> takes.
    /// </summary>
    public partial int RevengeCrits { get; set; }

    /// <summary>
    /// <c>m_flBurnEffectStartTime</c>: when the burn's skin ramp started. Stock
    /// keeps it on the client only (C_TFPlayer, set as
    /// <c>TF_COND_BURNING</c> arrives, c_tf_player.cpp:7306); here the server
    /// writes it at ignition and <c>BurnLevel</c>'s proxy reads it — sent and
    /// not predicted, as the clock is news, not a misprediction (CritMult's
    /// rule).
    /// </summary>
    public partial float BurnEffectStartTime { get; set; }

    /// <summary>
    /// <c>m_bCarryingObject</c>: the player holds a building in the arms —
    /// stock's bool half of <c>SetCarriedObject</c> (tf_player_shared.cpp:14562),
    /// sent not predicted (:562). Written by the object taken up and put down.
    /// </summary>
    public partial bool CarryingObject { get; set; }

    /// <summary>CWeaponMedigun's m_bCharging: the ÜberCharge release is on
    /// (the ready pulse's stop, tf_hud_mediccharge.cpp:269-277). The charge
    /// LEVEL rides the carried weapon's own state, as stock networks it on
    /// DT_WeaponMedigun.</summary>
    [Predicted]
    public partial bool ChargeReleasing { get; set; }

    /// <summary>
    /// CWeaponMedigun's m_flChargeLevel, 0..1, mirrored to the player: the
    /// hierarchy holds one weapon entity per player, so the holstered gun's
    /// meter has no entity of its own to ride and the speed the level feeds —
    /// the Overdose's <c>mult_player_movespeed_resource_level</c>
    /// (tf_player_shared.cpp:11043-11056), asked while another gun is in hand —
    /// reads it here, the way the release word and the patient above are
    /// carried. Written by the medigun's publish, every think.
    /// Sent, not predicted, on the <see cref="PipebombCount"/> row's showing:
    /// the meter moves outside the command's tick.
    /// </summary>
    public partial float MedicCharge { get; set; }

    /// <summary>CWeaponMedigun's m_hHealTarget: the beam's patient, as an
    /// entity index; -1 for none.</summary>
    [Predicted]
    public partial int HealTargetIndex { get; set; }

    /// <summary>
    /// The burn's attacker, as an entity index; -1 none. Stock keeps
    /// <c>m_hBurnAttacker</c> server-side (<c>Burn</c>, tf_player_shared.cpp:6751),
    /// but the burn tick's rage credits the attacker through the victim's
    /// OnTakeDamage (tf_player.cpp:9730-9735) — news the attacker's client
    /// cannot predict without knowing the fire is its own. Sent, not
    /// predicted, with the clock and life below: the burn is the server's to
    /// run (the <see cref="BurnEffectStartTime"/> rule).
    /// </summary>
    public partial int BurnAttackerIndex { get; set; }

    /// <summary>
    /// <c>m_flFlameBurnTime</c>: when the victim's next burn tick is due. Sent,
    /// not predicted, with <see cref="BurnAttackerIndex"/>: the tick schedule is
    /// the server's think (<c>ConditionGameRulesThink</c>,
    /// tf_player_shared.cpp:2895), outside any command.
    /// </summary>
    public partial float BurnNextTickTime { get; set; }

    /// <summary>
    /// <c>m_flAfterburnDuration</c>: the seconds of fire the victim has left.
    /// Sent, not predicted, with <see cref="BurnAttackerIndex"/> — the drain
    /// runs in the server's think, outside any command.
    /// </summary>
    public partial float BurnDuration { get; set; }

    /// <summary>
    /// <c>m_flRageMeter</c>: the soldier's buff meter, 0..100 — predicted
    /// (<c>tf_player_shared.cpp:448</c>) and sent bounded 0..100 (:517). It
    /// builds from damage dealt (<see cref="TFRageMeter.GainOnDamageDealt"/>)
    /// and the buff item's HUD bar is the meter over 100
    /// (<c>CTFBuffItem::GetProgress</c>, tf_weapon_buff_item.cpp:444-451).
    /// </summary>
    [Predicted]
    public partial float RageMeter { get; set; }

    /// <summary>
    /// <c>m_bRageDraining</c>: the buff deploy's drain is on — stock stamps it
    /// at the deploy (<c>ActivateRageBuff</c>, tf_player_shared.cpp:13794) and
    /// clears it as the meter empties (:13834); predicted (:449). While it is
    /// on, gains are refused (<c>SetRageMeter</c>, :13875).
    /// </summary>
    [Predicted]
    public partial bool RageDraining { get; set; }

    /// <summary>
    /// <c>m_flHypeMeter</c> (tf_player_shared.h:1099): the scout's hype, 0..100
    /// — filled from damage dealt (<see cref="TFHype.GainOnDamage"/>, the Soda
    /// Popper's <c>hype_on_damage</c> arm) and spent by the drink
    /// (<c>UpdateEnergyDrinkMeter</c>, tf_player_shared.cpp:14071-14078).
    /// Predicted and sent bounded 0..100, as stock's (<c>DEFINE_PRED_FIELD</c>
    /// :452, <c>SendPropFloat</c> :570).
    /// </summary>
    [Predicted]
    public partial float HypeMeter { get; set; }

    /// <summary>
    /// <c>m_iBuffTypeActive</c>: the deployed buff's type — stock's slot stamp
    /// of <c>SetupRageBuffTimer</c> (tf_player_shared.cpp:13743), so every pulse
    /// asks the buff the deploy was made with even after the held item changes.
    /// Sent, not predicted: the server's deploy bookkeeping, news rather than a
    /// misprediction (CritMult's rule).
    /// </summary>
    public partial int RageBuffType { get; set; }

    /// <summary>
    /// <c>m_iBuffPulseCount</c>: the pulses the deployed buff still owes —
    /// stock stamps it the buff's pulse count at the deploy
    /// (<c>SetupRageBuffTimer</c>, tf_player_shared.cpp:13744) and spends one per
    /// pulse second (:13859). Sent, not predicted (CritMult's rule).
    /// </summary>
    public partial int RageBuffPulsesLeft { get; set; }

    /// <summary>
    /// <c>m_flNextBuffPulseTime</c>: when the deployed buff pulses next — the
    /// deploy stamps it one second out (:13745) and each pulse advances it a
    /// second (:13858). Sent, not predicted (CritMult's rule).
    /// </summary>
    public partial float RageBuffNextPulseTime { get; set; }

    /// <summary>
    /// How many weapon-catalogue rows <see cref="WeaponItemDefIndex"/> covers.
    /// The core's table is twelve rows and the TF mod's source adds its
    /// class-weapons, builder and sapper, so this stands well above what the
    /// shipped game builds; a row past it (a future mod's weapon) simply keeps
    /// its zero — no item on the client — rather than breaking the wire.
    /// </summary>
    public const int WeaponRows = 128;

    /// <summary>
    /// The item definition index behind each weapon-catalogue row, as the
    /// loadout handed it out: what stock networks on every weapon's own item
    /// view (<c>DT_ScriptCreatedItem</c> sends <c>m_iItemDefinitionIndex</c>,
    /// econ_item_view.cpp:182, 20-bit unsigned), kept here as one array keyed
    /// by the catalogue row — the same index space as the carried weapon's
    /// <c>WeaponNetState.WeaponTypeId</c>, which both sides build identically.
    /// The client rebuilds the item from its own parsed schema by the defindex
    /// (<c>CEconItemView::GetItemDefinition</c>, econ_item_view.h:194, whose
    /// <c>GetStaticData</c> looks <c>m_iItemDefinitionIndex</c> up in the
    /// schema at econ_item_view.cpp:435, the index set at <c>Init</c>'s :319),
    /// so no attribute
    /// list crosses: the schema's statics are the same table on both sides.
    /// The wire keeps <c>defindex + 1</c>: the untouched zero is no item (stock
    /// answers no item with its own -1), and the schema's own item 0 — the bat,
    /// the scout's base melee — stays nameable. Sent, not predicted: the
    /// loadout's choice, news to the client rather than a prediction
    /// (CritMult's rule); the server writes it as the kit goes out
    /// (<c>TFLoadoutProvider.Weapons</c>).
    /// </summary>
    [NetArray(WeaponRows)] public partial NetArray<int> WeaponItemDefIndex { get; }

    /// <summary>
    /// <c>m_bPlayerDominated</c> (tf_player_shared.h:1130): on the player,
    /// the row set when the player dominates that player — stock's
    /// <c>SetPlayerDominated</c> (tf_player_shared.cpp:7701-7706), written
    /// by the server. Sent, not predicted: the server's word, news to the
    /// client rather than a prediction (CritMult's rule). Indexed by player
    /// index, where stock's array is indexed by entindex — one apart, the
    /// same rows.
    /// </summary>
    [NetArray(TFPlayerResourceNetState.Slots)] public partial NetArray<bool> PlayerDominated { get; }

    /// <summary>
    /// <c>m_bPlayerDominatingMe</c> (tf_player_shared.h:1131): on the
    /// player, the row set when that player dominates them — stock's
    /// <c>SetPlayerDominatingMe</c>, through <c>SetPlayerDominated</c>'s
    /// second line (tf_player_shared.cpp:7705). Sent, not predicted. The
    /// client reads only its local player's two rows (the scoreboard's
    /// nemesis pair, tf_clientscoreboard.cpp:1507-1519), so this mirror is
    /// what the local player's own carrier carries for them.
    /// </summary>
    [NetArray(TFPlayerResourceNetState.Slots)] public partial NetArray<bool> PlayerDominatingMe { get; }

    // The stun mirror: the four words stock's UpdateLegacyStunSystem
    // (tf_player_shared.cpp:7441-7465) copies from the server's active stun
    // entry so the CLIENT's CTFGameMovement::StunMove can decide from them
    // (that copy runs after the append :9889 and after the expiry arm :3211,
    // only while there is an active entry). The client never writes them and
    // the server writes them outside commands, so all four are sent, not
    // predicted — the server's word, news rather than a misprediction
    // (CritMult's rule). Stock predicts them (DEFINE_PRED_FIELD for the index
    // at :480); plain is faithful because our client has nothing to predict
    // here. Readers gate on StunIndex and TF_COND_STUNNED first, as stock's
    // gates stand on the active entry and the condition (:9938-9975), so the
    // stale value words behind a cleared index answer nothing. Written by
    // TFStunStack's MirrorToCarrier; read by TFMovementRules.MovementStun.

    /// <summary>
    /// <c>m_iStunIndex</c> (sent as <c>SendPropInt</c>, 8-bit,
    /// <c>tf_player_shared.cpp:597</c>; <c>-1</c> for none — the ctor and
    /// Spawn clear it (:893, :1025), the expiry arm and
    /// <c>OnRemoveStunned</c> too (:3185, :7389)): the active entry's row in
    /// the server's stun stack. The movement's only question of it is
    /// whether an entry is active at all.
    /// </summary>
    public partial int StunIndex { get; set; }

    /// <summary>
    /// <c>m_iStunFlags</c> (<c>UpdateLegacyStunSystem</c>'s copy,
    /// <c>tf_player_shared.cpp:7453</c>): the active entry's
    /// <see cref="TFStunFlags"/> — what <c>IsControlStunned</c> /
    /// <c>IsLoserStateStunned</c> / <c>GetStunFlags</c> read without the
    /// expiry (:9952-9975, tf_player_shared.h:570), and the movement gate
    /// with the expiry (:9938-9947).
    /// </summary>
    public partial int StunFlags { get; set; }

    /// <summary>
    /// <c>m_iMovementStunAmount</c> (:7452): the active entry's slow,
    /// 0..255 — <c>GetAmountStunned</c>'s number, clamped and divided by 255
    /// at the read (:9944).
    /// </summary>
    public partial float MovementStunAmount { get; set; }

    /// <summary>
    /// <c>m_flStunEnd</c> (:7451): when the active entry's slow stops —
    /// <c>GetAmountStunned</c>'s expiry, read as a strict
    /// <c>flExpireTime &gt; curtime</c> (:9940).
    /// </summary>
    public partial float StunExpireTime { get; set; }

    /// <summary>How many rows <see cref="Ammo"/> covers: every <see cref="TFAmmo"/>, the
    /// rows TF2's <c>GetAmmoDef</c> defines (tf_gamerules.cpp:18369) against stock's
    /// wider <c>MAX_AMMO_SLOTS</c> table (shareddefs.h:124).</summary>
    public const int AmmoSlots = (int)TFAmmo.Count;

    /// <summary>
    /// The player's ammo pools, by <see cref="TFAmmo"/> row — the server's word
    /// on <c>m_iAmmo</c>, which stock networks per player in the
    /// DT_LocalPlayerExclusive array of player.cpp:7929 and the client reads on
    /// any weapon switch (c_baseplayer.cpp:228's receive). It rides the carrier
    /// so the client's weapon mirror deploys an unheld weapon with the pool the
    /// server holds — the switch's own number — where the core's mirror held
    /// only the active weapon's reserve and inferred the rest.
    ///
    /// <para>
    /// Sent, not predicted: the server re-writes it outside the command's tick
    /// and the mirror reads it on a switch, so the arrival's word is the pool
    /// (CritMult's rule). Stock does predict the array
    /// (c_basecombatcharacter.cpp:192); the divergence is
    /// docs/divergences.md's, our client having no pool arithmetic of its own
    /// to run between commands.
    /// </para>
    /// </summary>
    [NetArray(AmmoSlots)] public partial NetArray<int> Ammo { get; }

    /// <summary>How many weapons the carrier names: a player's kit, every class's
    /// loadout plus its builder and PDAs, is well inside this. Keyed by what's carried
    /// and not by catalog row, because the shipped schema's rows run far past what a
    /// 2048-byte carrier could hold one word each for.</summary>
    public const int CarriedSlots = 12;

    /// <summary>The catalog row each carried slot holds, plus one; zero, an empty slot.
    /// <see cref="Clips"/>, <see cref="ItemMeters"/> and <see cref="ItemMeterTimes"/>
    /// read by the same slot.</summary>
    [NetArray(CarriedSlots)] public partial NetArray<int> CarriedRows { get; }

    /// <summary>The carried slot holding catalog row <paramref name="typeIndex"/>; minus one, none.</summary>
    public int CarriedSlotOf(int typeIndex)
    {
        if (typeIndex < 0)
            return -1;

        for (int slot = 0; slot < CarriedRows.Length; slot++)
        {
            if (CarriedRows[slot] == typeIndex + 1)
                return slot;
        }

        return -1;
    }

    /// <summary>
    /// The player's carried clips, by carried slot — the server's word on
    /// the stored clip of each weapon it carries (stock's <c>m_iClip1</c>, a
    /// field of every weapon entity's send table,
    /// <c>basecombatweapon_shared.cpp</c>, so a holstered weapon's clip reaches
    /// the client the moment the server moves it). It rides the carrier so the
    /// client's weapon mirror deploys an unheld weapon with the clip the server
    /// holds — the resupply's rewrite included — where the core's mirror held
    /// only the active weapon's last clip.
    ///
    /// <para>
    /// Minus one is no clip: an empty slot, a weapon without
    /// one, or a carrier that has not spoken yet. Sent, not predicted, on the
    /// <see cref="Ammo"/> row's showing: the mirror runs no holstered-clip
    /// arithmetic between commands, the switch reads the arrival.
    /// </para>
    /// </summary>
    [NetArray(CarriedSlots)] public partial NetArray<int> Clips { get; }

    /// <summary>
    /// The player's held-aside item meters, by carried slot — the
    /// server's word on the charge each carried weapon holstered with, and,
    /// beside it in <see cref="ItemMeterTimes"/>, the server second that was:
    /// the pair stock's per-slot charge rides the inventory with
    /// (tf_player_shared.cpp:7870-7904), read once, at the switch whose
    /// restore advances it. The hands' own meter is the blob's
    /// (<see cref="SourceSharp.Shared.WeaponNetState.ItemMeter"/>), predicted
    /// as it charges.
    ///
    /// <para>
    /// Sent, not predicted, on the <see cref="Clips"/> row's showing: the
    /// mirror runs no held-aside arithmetic between commands, the switch
    /// reads the arrival — the restore's one advance is the deploy's own.
    /// </para>
    /// </summary>
    [NetArray(CarriedSlots)] public partial NetArray<float> ItemMeters { get; }

    /// <summary>The server second each <see cref="ItemMeters"/> row was; zero, a charge that has run since the world's own zero.</summary>
    [NetArray(CarriedSlots)] public partial NetArray<float> ItemMeterTimes { get; }

    /// <summary>
    /// The sticky bombs this player has out — stock's pipebomb launcher's
    /// <c>m_iPipebombCount</c> (<c>tf_weapon_pipebomblauncher.cpp:46</c>),
    /// which the HUD reads through the launcher the player owns
    /// (<c>CTFPlayer::GetNumActivePipebombs</c>, tf_player_shared.cpp:13065).
    /// It rides the carrier as the clips do: the hierarchy holds one weapon
    /// entity per player, whichever is in hand, so a holstered launcher's
    /// count has no entity of its own to ride. Written by the tray on every
    /// stickying and unstickying (<see cref="TF2.Server.Projectiles.TFStickyTray"/>).
    /// Sent, not predicted: the tray writes outside the command's tick, as
    /// the <see cref="Ammo"/> rows' rule.
    /// </summary>
    public partial int PipebombCount { get; set; }

    /// <summary>
    /// When the Spy-cicle's knife melted — stock's
    /// <c>m_flKnifeMeltTimestamp</c> (<c>tf_weapon_knife.cpp:39</c>), written
    /// by the server's melt (<c>ApplyOnInjuredAttributes</c> through
    /// <c>TF2.Server.Player</c>'s spy slice, the stamp <c>:126-129</c> keeps).
    /// It rides the carrier as the clips do: the melted knife is refused at
    /// the switch back (<c>CanDeploy</c>, <c>:521-531</c>) while holstered,
    /// and the hierarchy's one weapon entity is whichever is in hand.
    /// </summary>
    public partial float KnifeMeltTimestamp { get; set; }

    /// <summary>
    /// How long that melt takes to regenerate — stock's
    /// <c>m_flKnifeRegenerateDuration</c> (<c>tf_weapon_knife.cpp:40</c>), the
    /// window <c>CanDeploy</c> holds the switch for (<c>:523</c>). Zero with a
    /// zero stamp is stock's fresh <c>m_bKnifeExists</c> (<c>:77</c>): the
    /// formula answers ready. Sent, not predicted, on the stamps' row's
    /// showing: the client's replay runs no melt arithmetic, the switch reads
    /// the arrival.
    /// </summary>
    public partial float KnifeRegenerateDuration { get; set; }

    // The player's attribute walk, for every client (user ruling L11,
    // 2026-11-11: all players see what the others have equipped): stock
    // networks the player's m_AttributeManager (tf_player.h:647, sent at
    // tf_player.cpp:831) and each item's own m_AttributeList on its item view;
    // here the server's mirror (TF2.Server.Player.TFAttributeListMirror)
    // writes the walk's order and every provider's own values in one run, and
    // TFAttributeWire reads it back. Sent, not predicted: the server's word
    // (CritMult's rule). Empty — AttributeProviderCount 0 — while no provider
    // holds a value of its own: the client then builds the kit's items from
    // WeaponItemDefIndex alone.

    /// <summary>How many slots of <see cref="AttributeProviders"/> the walk fills; zero, the lists are empty.</summary>
    public partial int AttributeProviderCount { get; set; }

    /// <summary>Moves on every rewrite of the lists: the client's cache key.</summary>
    public partial int AttributeSerial { get; set; }

    /// <summary>The providers <see cref="AttributeProviders"/> holds at most (<c>TFAttributeWire.MaxProviders</c>).</summary>
    public const int AttributeProviderSlots = 16;

    /// <summary>The values <see cref="AttributeDefindex"/> and <see cref="AttributeBits"/> hold at most (<c>TFAttributeWire.MaxValues</c>).</summary>
    public const int AttributeValueSlots = 32;

    /// <summary>
    /// Each provider of the walk, in its order: the player's own list, then
    /// the manager's providers in <c>m_Providers</c>' order, then the kit's
    /// items that do not provide. One word each, <c>TFAttributeWire</c>'s
    /// packing: the item definition index, the weapon row, the provider type,
    /// whether it provides, whether it is the player's own list.
    /// </summary>
    [NetArray(AttributeProviderSlots)] public partial NetArray<int> AttributeProviders { get; }

    /// <summary>How many of the value rows, in order, each provider owns.</summary>
    [NetArray(AttributeProviderSlots)] public partial NetArray<byte> AttributeProviderValues { get; }

    /// <summary>Each value's attribute definition index.</summary>
    [NetArray(AttributeValueSlots)] public partial NetArray<ushort> AttributeDefindex { get; }

    /// <summary>Each value's 32 bits (<c>attribute_data_union_t</c>, a float's pattern or a stored integer).</summary>
    [NetArray(AttributeValueSlots)] public partial NetArray<uint> AttributeBits { get; }
}
