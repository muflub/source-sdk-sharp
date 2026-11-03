//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Server;

// TF2's convars that stock declares in the game DLL only (a GAME_DLL arm of
// shared code, or a server/ file), so only the server declares them, even
// where stock flags one FCVAR_REPLICATED (mp_enableroundwaittime: the client
// never declared it). The same rules as TFSharedConVars.cs; TFConVarsTests
// holds every row to the pristine source, and its side.

// ---- shared/teamplayroundbased_gamerules.cpp (#ifdef GAME_DLL) --------------

/// <summary>Stock reads it as a float (<c>if ( mp_enableroundwaittime.GetFloat() )</c>), so it is one.</summary>
[ConVar("mp_enableroundwaittime", "1", Flags = ConVarFlags.Replicated,
        Help = "Enable timers to wait between rounds.")]
public sealed class MpEnableRoundWaitTimeConVar : FloatConVar { }

[ConVar("mp_restartround", "0",
        Help = "If non-zero, the current round will restart in the specified number of seconds")]
public sealed class MpRestartRoundConVar : IntConVar { }

[ConVar("mp_stalemate_timelimit", "240", Flags = ConVarFlags.Replicated,
        Help = "Timelimit (in seconds) of the stalemate round.")]
public sealed class MpStalemateTimeLimitConVar : IntConVar { }

/// <summary>
/// Stock <c>tf_gamerules.cpp:731</c>, the rename gate
/// <c>CTFGameRules::ChangePlayerName</c> (tf_gamerules.cpp:10217) reads.
/// </summary>
[ConVar("tf_allow_player_name_change", "1", Flags = ConVarFlags.Notify,
        Help = "Allow player name changes.")]
public sealed class TFAllowPlayerNameChangeConVar : BoolConVar { }

/// <summary>
/// Stock <c>tf_gamerules.cpp:736</c>, the spectator join's unbalance arm
/// <c>CTFPlayer::HandleCommand_JoinTeam</c> (tf_player.cpp:6364) reads beside
/// <c>mp_teams_unbalance_limit</c>.
/// </summary>
[ConVar("mp_spectators_restricted", "0",
        Help = "Prevent players on game teams from joining team spectator if it would unbalance the teams.")]
public sealed class MpSpectatorsRestrictedConVar : BoolConVar { }

[ConVar("mp_autoteambalance", "1", Flags = ConVarFlags.Notify,
        Help = "Automatically balance the teams based on mp_teams_unbalance_limit. 0 = off, 1 = forcibly switch, 2 = ask volunteers",
        Min = 0, Max = 2)]
public sealed class MpAutoTeamBalanceConVar : IntConVar { }

[ConVar("mp_stalemate_enable", "0", Flags = ConVarFlags.Notify,
        Help = "Enable/Disable stalemate mode.")]
public sealed class MpStalemateEnableConVar : BoolConVar { }

[ConVar("mp_match_end_at_timelimit", "0", Flags = ConVarFlags.Notify,
        Help = "Allow the match to end when mp_timelimit hits instead of waiting for the end of the current round.")]
public sealed class MpMatchEndAtTimeLimitConVar : BoolConVar { }

[ConVar("mp_scrambleteams_auto", "1", Flags = ConVarFlags.Notify,
        Help = "Server will automatically scramble the teams if criteria met.  Only works on dedicated servers.")]
public sealed class MpScrambleTeamsAutoConVar : BoolConVar { }

[ConVar("mp_scrambleteams_auto_windifference", "2", Flags = ConVarFlags.Notify,
        Help = "Number of round wins a team must lead by in order to trigger an auto scramble.")]
public sealed class MpScrambleTeamsAutoWinDifferenceConVar : IntConVar { }

// ---- server/team_train_watcher.cpp ------------------------------------------

[ConVar("tf_escort_recede_time", "30", Min = 0)]
public sealed class TFEscortRecedeTimeConVar : FloatConVar { }

[ConVar("tf_escort_recede_time_overtime", "5", Min = 0)]
public sealed class TFEscortRecedeTimeOvertimeConVar : FloatConVar { }

// ---- shared/tf/tf_gamerules.cpp (#ifdef GAME_DLL) ---------------------------

[ConVar("tf_gamemode_cp", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify | ConVarFlags.DevelopmentOnly)]
public sealed class TFGameModeCpConVar : BoolConVar { }

[ConVar("tf_gamemode_ctf", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify | ConVarFlags.DevelopmentOnly)]
public sealed class TFGameModeCtfConVar : BoolConVar { }

[ConVar("tf_gamemode_payload", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify | ConVarFlags.DevelopmentOnly)]
public sealed class TFGameModePayloadConVar : BoolConVar { }

/// <summary>The humans' team (<c>tf_gamerules.cpp:838</c>, inside the GAME_DLL block): the name
/// stock's <c>GetAssignedHumanTeam</c> matches, any team otherwise.</summary>
[ConVar("mp_humans_must_join_team", "any", Flags = ConVarFlags.Replicated,
        Help = "Restricts human players to a single team {any, blue, red, spectator}")]
public sealed class MpHumansMustJoinTeamConVar : StringConVar { }

[ConVar("tf_ctf_bonus_time", "10", Flags = ConVarFlags.Notify,
        Help = "Length of team crit time for CTF capture.")]
public sealed class TFCTFBonusTimeConVar : FloatConVar { }

// ---- shared/tf/tf_weaponbase_melee.cpp (#ifndef CLIENT_DLL) -----------------

[ConVar("tf_meleeattackforcescale", "80.0", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFMeleeAttackForceScaleConVar : FloatConVar { }

// ---- shared/tf/tf_weapon_grenade_pipebomb.cpp (#ifdef GAME_DLL) -------------

[ConVar("tf_grenade_forcefrom_bullet", "2.0", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFGrenadeForceFromBulletConVar : FloatConVar { }

[ConVar("tf_grenade_forcefrom_buckshot", "0.75", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFGrenadeForceFromBuckshotConVar : FloatConVar { }

[ConVar("tf_grenade_forcefrom_blast", "0.15", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFGrenadeForceFromBlastConVar : FloatConVar { }

// "How long after being shot will we re-stick to the world." is a trailing
// comment in stock, not a help string (tf_weapon_grenade_pipebomb.cpp:952).
[ConVar("tf_grenade_force_sleeptime", "1.0", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFGrenadeForceSleeptimeConVar : FloatConVar { }

[ConVar("tf_pipebomb_force_to_move", "1500.0", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFPipebombForceToMoveConVar : FloatConVar { }

// tf_weapon_grenade_pipebomb.cpp:954: how long a deflected sticky keeps its count.
[ConVar("tf_pipebomb_deflect_reset_time", "10.0", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFPipebombDeflectResetTimeConVar : FloatConVar { }

// ---- server/tf/tf_ammo_pack.cpp ----------------------------------------------

// The ragdoll band the ammo pack's throw (:153-169) draws from. The ammo pack
// itself, and the CTFDroppedWeapon it shadows, are both ragdolls in spirit.

[ConVar("tf_weapon_ragdoll_velocity_min", "100", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFWeaponRagdollVelocityMinConVar : FloatConVar { }

[ConVar("tf_weapon_ragdoll_velocity_max", "150", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFWeaponRagdollVelocityMaxConVar : FloatConVar { }

[ConVar("tf_weapon_ragdoll_maxspeed", "300", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFWeaponRagdollMaxSpeedConVar : FloatConVar { }

// ---- shared/tf/tf_dropped_weapon.cpp -----------------------------------------

[ConVar("tf_dropped_weapon_lifetime", "30", Flags = ConVarFlags.Cheat)]
public sealed class TFDroppedWeaponLifetimeConVar : FloatConVar { }

// ---- server/tf/tf_player.cpp -------------------------------------------------

[ConVar("tf_playergib", "1", Flags = ConVarFlags.Notify,
        Help = "Allow player gibbing. 0: never, 1: normal, 2: always", Min = 0, Max = 2)]
public sealed class TFPlayerGibConVar : IntConVar { }

// ---- server/tf/tf_obj_teleporter.cpp -----------------------------------------

/// <summary>The arrival unzoom's foot (<c>tf_obj_teleporter.cpp:82</c>): the scoped width the view eases back from.</summary>
[ConVar("tf_teleporter_fov_start", "120", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly,
        Help = "Starting FOV for teleporter zoom.", Min = 1)]
public sealed class TFTeleporterFovStartConVar : IntConVar { }

/// <summary>How long the ease takes (<c>tf_obj_teleporter.cpp:83</c>).</summary>
[ConVar("tf_teleporter_fov_time", "0.5", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly,
        Help = "How quickly to restore FOV after teleport.", Min = 0)]
public sealed class TFTeleporterFovTimeConVar : FloatConVar { }

// ---- server/tf/tf_obj_sentrygun.cpp -----------------------------------------

/// <summary>The shells' damage (<c>tf_obj_sentrygun.cpp:134</c>, no help in stock;
/// the mini building's own <c>tf_sentrygun_mini_damage</c> has no arm here yet).</summary>
[ConVar("tf_sentrygun_damage", "16", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFSentryGunDamageConVar : FloatConVar { }

// ---- server/tf/tf_obj_sapper.cpp --------------------------------------------

/// <summary>The sapper's drain (<c>tf_obj_sapper.cpp:43</c>, no flags in stock).</summary>
[ConVar("obj_sapper_amount", "25", Help = "Amount of health inflicted by a Sapper object per second")]
public sealed class ObjSapperAmountConVar : FloatConVar { }

// ---- shared/tf/tf_weapon_flamethrower.cpp (#if defined(GAME_DLL)) ------------

/// <summary>The airblast's arm (<c>tf_weapon_flamethrower.cpp:84</c>): the cray force, or the old pushback it replaced.</summary>
[ConVar("tf_airblast_cray", "1", Flags = ConVarFlags.Cheat,
        Help = "Use alternate cray airblast logic globally.")]
public sealed class TFAirBlastCrayConVar : BoolConVar { }

/// <summary>The old arm's vertical kick (<c>tf_weapon_flamethrower.cpp:52</c>, no help in stock).</summary>
[ConVar("tf_flamethrower_burst_zvelocity", "350", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFFlameThrowerBurstZvelocityConVar : FloatConVar { }

// ---- server/tf/tf_player.cpp -------------------------------------------------

/// <summary>The pre-round freeze's push waiver, stock <c>tf_player.cpp:10231</c> reads in <c>ApplyPushFromDamage</c>.</summary>
[ConVar("tf_preround_push_from_damage_enable", "0",
        Help = "If enabled, this will allow players using certain type of damage to move during pre-round freeze time.")]
public sealed class TFPreroundPushFromDamageEnableConVar : BoolConVar { }

// The debug push scales (tf_weapon_flamethrower.cpp:1255-1258, #ifdef _DEBUG,
// no flags or help in stock): the release build has neither, so TF2.Client
// declares neither — the CrayForce power lands under one, the result's Z
// under the other, and the old arm's push and lift likewise (:1330-1333,
// :1574-1577, :1789, :1811).
[ConVar("tf_pushbackscalescale", "1.0")]
public sealed class TFPushbackScaleConVar : FloatConVar { }

[ConVar("tf_pushbackscalescale_vertical", "1.0")]
public sealed class TFPushbackScaleVerticalConVar : FloatConVar { }

/// <summary>The voice-menu repeat's cap (<c>tf_player.cpp:202</c>): the scene's length
/// <c>NoteSpokeVoiceCommand</c> (:20066) holds the next word under.</summary>
[ConVar("tf_max_voice_speak_delay", "1.5", Flags = ConVarFlags.DevelopmentOnly,
        Help = "Max time after a voice command until player can do another one", Min = 0.1f)]
public sealed class TFMaxVoiceSpeakDelayConVar : FloatConVar { }
// ---- shared/tf/tf_gamerules.cpp (#ifdef GAME_DLL) ----------------------------

/// <summary>
/// Stock <c>tf_gamerules.cpp:1112</c>, the dead-talk gate
/// <c>CTFPlayer::CanHearAndReadChatFrom</c> (tf_player.cpp:19812) and
/// <c>CTFGameRules::TFVoiceManager</c> (:7743) read.
/// </summary>
[ConVar("tf_gravetalk", "1", Flags = ConVarFlags.Notify,
        Help = "Allows living players to hear dead players using text/voice chat.", Min = 0, Max = 1)]
public sealed class TFGravetalkConVar : BoolConVar { }
// ---- shared/multiplay_gamerules.cpp:131 #ifndef TF_DLL -----------------------
// "TF overrides the default value of this convar": the core declares 0 behind
// the ifndef and the game DLL declares its own (tf_gamerules.cpp:1089, X360
// 15). ConVarDefault replaces the core declaration's default; the flags and
// help are the core's, as stock's redeclaration's are.

/// <summary>The PC number; this build has no X360 arm to read 15.</summary>
[ConVarDefault("mp_waitingforplayers_time", "30")]
public sealed class TFWaitingForPlayersTimeDefault { }
