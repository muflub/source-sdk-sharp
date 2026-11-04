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

    /// <summary><c>m_iAirDash</c>: air dashes taken since the player last stood on ground.</summary>
    [Predicted]
    public partial int AirDash { get; set; }

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

    /// <summary>CWeaponMedigun's m_hHealTarget: the beam's patient, as an
    /// entity index; -1 for none.</summary>
    [Predicted]
    public partial int HealTargetIndex { get; set; }

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

    /// <summary>How many rows <see cref="Clips"/> covers: the weapon catalog's
    /// rows, which TF2's <see cref="TFWeaponTypes"/> names (the composition
    /// fact's 32; a row past this width is a weapon the carrier cannot name —
    /// the fact tied to the count says so).</summary>
    public const int ClipRows = 32;

    /// <summary>
    /// The player's carried clips, by weapon-catalog row — the server's word on
    /// the stored clip of each weapon it carries (stock's <c>m_iClip1</c>, a
    /// field of every weapon entity's send table,
    /// <c>basecombatweapon_shared.cpp</c>, so a holstered weapon's clip reaches
    /// the client the moment the server moves it). It rides the carrier so the
    /// client's weapon mirror deploys an unheld weapon with the clip the server
    /// holds — the resupply's rewrite included — where the core's mirror held
    /// only the active weapon's last clip.
    ///
    /// <para>
    /// Minus one is no clip: a row the player does not carry, a weapon without
    /// one, or a carrier that has not spoken yet. Sent, not predicted, on the
    /// <see cref="Ammo"/> row's showing: the mirror runs no holstered-clip
    /// arithmetic between commands, the switch reads the arrival.
    /// </para>
    /// </summary>
    [NetArray(ClipRows)] public partial NetArray<int> Clips { get; }

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
}
