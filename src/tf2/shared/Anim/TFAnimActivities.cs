//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Anim;

/// <summary>
/// The <c>ACT_</c> names the TF2 anim layer claims that the core's
/// <c>MpActivities</c> does not carry — the multiplayer anim state's base
/// claims them by name in <c>CTFPlayerAnimState</c>'s handlers
/// (<c>tf_playeranimstate.cpp:1265-1538</c>) and condition tables
/// (:157-189), and the <c>s_acttable*</c> rows
/// (<c>tf_weaponbase.cpp:3660-4181</c>) are keyed by them.
///
/// <para>
/// Only the names this build's logic claims or compares go here; the tables
/// quote their rows as literals, one line per stock line, so each table
/// diffs against its C++ block line for line. Names already on
/// <c>MpActivities</c> (STAND_IDLE, RUN, CROUCH_IDLE, CROUCHWALK, SWIM,
/// JUMP, ACT_DIESIMPLE and the attack/reload/flinch families) are used from
/// there — one name, one constant.
/// </para>
/// </summary>
public static class TFAnimActivities
{
    // The deployed family — HandleMoving's aiming arms
    // (tf_playeranimstate.cpp:1316, :1320, :1323-1327) and HandleDucking's
    // (:1353-1357, :1370-1384), claimed base-side and translated by the
    // weapon table.

    /// <summary><c>ACT_MP_DEPLOYED</c>.</summary>
    public const string Deployed = "ACT_MP_DEPLOYED";

    /// <summary><c>ACT_MP_DEPLOYED_IDLE</c>.</summary>
    public const string DeployedIdle = "ACT_MP_DEPLOYED_IDLE";

    /// <summary><c>ACT_MP_CROUCH_DEPLOYED</c>.</summary>
    public const string CrouchDeployed = "ACT_MP_CROUCH_DEPLOYED";

    /// <summary><c>ACT_MP_CROUCH_DEPLOYED_IDLE</c>.</summary>
    public const string CrouchDeployedIdle = "ACT_MP_CROUCH_DEPLOYED_IDLE";

    /// <summary><c>ACT_MP_SWIM_DEPLOYED</c> (tf_playeranimstate.cpp:1274-1285).</summary>
    public const string SwimDeployed = "ACT_MP_SWIM_DEPLOYED";

    // The jump family — HandleJumping's claims (:1461-1530) and the land
    // gesture (:1453, :1507), which TF's RestartGesture override (:958-963)
    // translates through the weapon table before it reaches the slot.

    /// <summary><c>ACT_MP_AIRWALK</c>.</summary>
    public const string Airwalk = "ACT_MP_AIRWALK";

    /// <summary><c>ACT_MP_JUMP_START</c>.</summary>
    public const string JumpStart = "ACT_MP_JUMP_START";

    /// <summary><c>ACT_MP_JUMP_FLOAT</c>.</summary>
    public const string JumpFloat = "ACT_MP_JUMP_FLOAT";

    /// <summary><c>ACT_MP_JUMP_LAND</c>.</summary>
    public const string JumpLand = "ACT_MP_JUMP_LAND";

    /// <summary><c>ACT_MP_DOUBLEJUMP_CROUCH</c> (tf_playeranimstate.cpp:1361-1363).</summary>
    public const string DoubleJumpCrouch = "ACT_MP_DOUBLEJUMP_CROUCH";

    // The three stand names the winner check compares the translated gait
    // against (tf_playeranimstate.cpp:143-151) — names the weapon table
    // answers with, not names the handlers claim.

    /// <summary><c>ACT_MP_STAND_PRIMARY</c>.</summary>
    public const string StandPrimary = "ACT_MP_STAND_PRIMARY";

    /// <summary><c>ACT_MP_STAND_MELEE</c>.</summary>
    public const string StandMelee = "ACT_MP_STAND_MELEE";

    /// <summary><c>ACT_MP_STAND_SECONDARY</c>.</summary>
    public const string StandSecondary = "ACT_MP_STAND_SECONDARY";

    /// <summary><c>ACT_MP_COMPETITIVE_WINNERSTATE</c> (tf_playeranimstate.cpp:143-151).</summary>
    public const string CompetitiveWinnerState = "ACT_MP_COMPETITIVE_WINNERSTATE";

    /// <summary><c>ACT_MP_COMPETITIVE_LOSERSTATE</c> (the table at :186-189).</summary>
    public const string CompetitiveLoserState = "ACT_MP_COMPETITIVE_LOSERSTATE";

    // The Halloween kart table (tf_playeranimstate.cpp:157-168): the kart
    // condition replaces the gait and the jump before any weapon table.

    /// <summary><c>ACT_KART_IDLE</c>.</summary>
    public const string KartIdle = "ACT_KART_IDLE";

    /// <summary><c>ACT_KART_ACTION_SHOOT</c>.</summary>
    public const string KartActionShoot = "ACT_KART_ACTION_SHOOT";

    /// <summary><c>ACT_KART_JUMP_START</c>.</summary>
    public const string KartJumpStart = "ACT_KART_JUMP_START";

    /// <summary><c>ACT_KART_JUMP_FLOAT</c>.</summary>
    public const string KartJumpFloat = "ACT_KART_JUMP_FLOAT";

    /// <summary><c>ACT_KART_JUMP_LAND</c>.</summary>
    public const string KartJumpLand = "ACT_KART_JUMP_LAND";

    // The gesture family the TF DoAnimationEvent switch claims
    // (tf_playeranimstate.cpp:969-1259) — the fire arms, the super fire, the
    // crouch secondary, the pre/post windups, the airwalk reload trio, the
    // dash gesture and the stun/passtime/CYOA trios. The generic stand/swim
    // primary and secondary fire and the stand/crouch/swim reload trios are
    // already on <c>MpActivities</c>; the weapon tables translate these
    // names where they have a row (e.g. the deployed fires at
    // tf_weaponbase.cpp:3680-3682), and pass them through where they do not
    // (no table has a PREFIRE row).

    /// <summary><c>ACT_MP_ATTACK_CROUCH_PRIMARYFIRE</c> (:1005, :1032).</summary>
    public const string AttackCrouchPrimaryFire = "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE";

    /// <summary><c>ACT_MP_ATTACK_CROUCH_SECONDARYFIRE</c> (:1063).</summary>
    public const string AttackCrouchSecondaryFire = "ACT_MP_ATTACK_CROUCH_SECONDARYFIRE";

    /// <summary><c>ACT_MP_ATTACK_STAND_PRIMARYFIRE_DEPLOYED</c> (:1022).</summary>
    public const string AttackStandPrimaryFireDeployed = "ACT_MP_ATTACK_STAND_PRIMARYFIRE_DEPLOYED";

    /// <summary><c>ACT_MP_ATTACK_CROUCH_PRIMARYFIRE_DEPLOYED</c> (:1018).</summary>
    public const string AttackCrouchPrimaryFireDeployed = "ACT_MP_ATTACK_CROUCH_PRIMARYFIRE_DEPLOYED";

    /// <summary><c>ACT_MP_ATTACK_STAND_PRIMARY_SUPER</c> (:1043).</summary>
    public const string AttackStandPrimarySuper = "ACT_MP_ATTACK_STAND_PRIMARY_SUPER";

    /// <summary><c>ACT_MP_ATTACK_CROUCH_PRIMARY_SUPER</c> (:1043).</summary>
    public const string AttackCrouchPrimarySuper = "ACT_MP_ATTACK_CROUCH_PRIMARY_SUPER";

    /// <summary><c>ACT_MP_ATTACK_SWIM_PRIMARY_SUPER</c> (:1046).</summary>
    public const string AttackSwimPrimarySuper = "ACT_MP_ATTACK_SWIM_PRIMARY_SUPER";

    /// <summary><c>ACT_MP_ATTACK_STAND_PREFIRE</c> (:1101).</summary>
    public const string AttackStandPreFire = "ACT_MP_ATTACK_STAND_PREFIRE";

    /// <summary><c>ACT_MP_ATTACK_CROUCH_PREFIRE</c> (:1096).</summary>
    public const string AttackCrouchPreFire = "ACT_MP_ATTACK_CROUCH_PREFIRE";

    /// <summary><c>ACT_MP_ATTACK_SWIM_PREFIRE</c> (:1091).</summary>
    public const string AttackSwimPreFire = "ACT_MP_ATTACK_SWIM_PREFIRE";

    /// <summary><c>ACT_MP_ATTACK_STAND_POSTFIRE</c> (:1130).</summary>
    public const string AttackStandPostFire = "ACT_MP_ATTACK_STAND_POSTFIRE";

    /// <summary><c>ACT_MP_ATTACK_CROUCH_POSTFIRE</c> (:1125).</summary>
    public const string AttackCrouchPostFire = "ACT_MP_ATTACK_CROUCH_POSTFIRE";

    /// <summary><c>ACT_MP_ATTACK_SWIM_POSTFIRE</c> (:1120).</summary>
    public const string AttackSwimPostFire = "ACT_MP_ATTACK_SWIM_POSTFIRE";

    /// <summary><c>ACT_MP_RELOAD_AIRWALK</c> (:1143).</summary>
    public const string ReloadAirwalk = "ACT_MP_RELOAD_AIRWALK";

    /// <summary><c>ACT_MP_RELOAD_AIRWALK_LOOP</c> (:1156).</summary>
    public const string ReloadAirwalkLoop = "ACT_MP_RELOAD_AIRWALK_LOOP";

    /// <summary><c>ACT_MP_RELOAD_AIRWALK_END</c> (:1169).</summary>
    public const string ReloadAirwalkEnd = "ACT_MP_RELOAD_AIRWALK_END";

    /// <summary><c>ACT_MP_DOUBLEJUMP</c> (:1202).</summary>
    public const string DoubleJump = "ACT_MP_DOUBLEJUMP";

    /// <summary><c>ACT_MP_DOUBLEJUMP_LOSERSTATE</c> (:1198).</summary>
    public const string DoubleJumpLoserState = "ACT_MP_DOUBLEJUMP_LOSERSTATE";

    /// <summary><c>ACT_MP_STUN_BEGIN</c> (:1215).</summary>
    public const string StunBegin = "ACT_MP_STUN_BEGIN";

    /// <summary><c>ACT_MP_STUN_MIDDLE</c> (:1218).</summary>
    public const string StunMiddle = "ACT_MP_STUN_MIDDLE";

    /// <summary><c>ACT_MP_STUN_END</c> (:1221).</summary>
    public const string StunEnd = "ACT_MP_STUN_END";

    /// <summary><c>ACT_MP_PASSTIME_THROW_BEGIN</c> (:1224).</summary>
    public const string PasstimeThrowBegin = "ACT_MP_PASSTIME_THROW_BEGIN";

    /// <summary><c>ACT_MP_PASSTIME_THROW_MIDDLE</c> (:1227).</summary>
    public const string PasstimeThrowMiddle = "ACT_MP_PASSTIME_THROW_MIDDLE";

    /// <summary><c>ACT_MP_PASSTIME_THROW_END</c> (:1230).</summary>
    public const string PasstimeThrowEnd = "ACT_MP_PASSTIME_THROW_END";

    /// <summary><c>ACT_MP_CYOA_PDA_INTRO</c> (:1233).</summary>
    public const string CyoaPdaIntro = "ACT_MP_CYOA_PDA_INTRO";

    /// <summary><c>ACT_MP_CYOA_PDA_IDLE</c> (:1236).</summary>
    public const string CyoaPdaIdle = "ACT_MP_CYOA_PDA_IDLE";

    /// <summary><c>ACT_MP_CYOA_PDA_OUTRO</c> (:1239).</summary>
    public const string CyoaPdaOutro = "ACT_MP_CYOA_PDA_OUTRO";
}
