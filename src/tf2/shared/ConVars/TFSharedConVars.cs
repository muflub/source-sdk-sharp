//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared;

// TF2's convars that stock declares in SHARED code (both DLLs), so both
// sides declare them here: the shared source compiles into TF2.Server and
// TF2.Client, which keeps a replicated pair's defaults equal by construction.
// Names, defaults, flags, help and bounds are stock's declarations (stage1
// 1.3: what the milestone's movement, weapons, round rules, CTF and payload
// read). FCVAR_GAMEDLL/FCVAR_CLIENTDLL are left to the registration, which
// adds each DLL's own. TFConVarsTests holds every row to the pristine source.

// ---- shared/tf/tf_gamemovement.cpp ------------------------------------------

[ConVar("tf_avoidteammates", "1", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly,
        Help = "Controls how teammates interact when colliding.\n  0: Teammates block each other\n  1: Teammates pass through each other, but push each other away (default)")]
public sealed class TFAvoidTeammatesConVar : BoolConVar { }

[ConVar("tf_avoidteammates_pushaway", "1", Flags = ConVarFlags.Replicated,
        Help = "Whether or not teammates push each other away when occupying the same space")]
public sealed class TFAvoidTeammatesPushawayConVar : BoolConVar { }

[ConVar("tf_solidobjects", "1", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class TFSolidObjectsConVar : BoolConVar { }

[ConVar("tf_clamp_back_speed", "0.9", Flags = ConVarFlags.Replicated | ConVarFlags.DevelopmentOnly)]
public sealed class TFClampBackSpeedConVar : FloatConVar { }

[ConVar("tf_clamp_back_speed_min", "100", Flags = ConVarFlags.Replicated | ConVarFlags.DevelopmentOnly)]
public sealed class TFClampBackSpeedMinConVar : FloatConVar { }

[ConVar("tf_clamp_airducks", "1", Flags = ConVarFlags.Replicated)]
public sealed class TFClampAirducksConVar : BoolConVar { }

[ConVar("tf_resolve_stuck_players", "1", Flags = ConVarFlags.Replicated)]
public sealed class TFResolveStuckPlayersConVar : BoolConVar { }

[ConVar("tf_movement_aircurrent_friction_mult", "0.75", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat,
        Help = "Friction multiplier when sliding against surfaces while trapped in an air current")]
public sealed class TFMovementAircurrentFrictionMultConVar : FloatConVar { }

[ConVar("tf_movement_aircurrent_aircontrol_mult", "0.25", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat,
        Help = "Multiplier on air control when player is in an air current (such as airblast)")]
public sealed class TFMovementAircurrentAircontrolMultConVar : FloatConVar { }

[ConVar("tf_movement_lost_footing_restick", "50.0", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat,
        Help = "Early escape the lost footing condition if the player is moving slower than this across the ground")]
public sealed class TFMovementLostFootingRestickConVar : FloatConVar { }

[ConVar("tf_movement_lost_footing_friction", "0.1", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat,
        Help = "Ground friction for players who have lost their footing")]
public sealed class TFMovementLostFootingFrictionConVar : FloatConVar { }

// ---- shared/tf/tf_fx_shared.cpp ---------------------------------------------

[ConVar("tf_use_fixed_weaponspreads", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "If set to 1, weapons that fire multiple pellets per shot will use a non-random pellet distribution.")]
public sealed class TFUseFixedWeaponspreadsConVar : BoolConVar { }

// ---- shared/tf/tf_weaponbase.cpp, tf_weaponbase_melee.cpp --------------------

[ConVar("tf_weapon_criticals", "1", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Whether or not random crits are enabled")]
public sealed class TFWeaponCriticalsConVar : BoolConVar { }

[ConVar("tf_weapon_criticals_nopred", "1.0", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat)]
public sealed class TFWeaponCriticalsNopredConVar : BoolConVar { }

/// <summary>0, 1 or 2: read as an int (<c>GetInt() &gt; 1</c>, tf_weaponbase_melee.cpp).</summary>
[ConVar("tf_weapon_criticals_melee", "1", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Controls random crits for melee weapons. 0 - Melee weapons do not randomly crit. 1 - Melee weapons can randomly crit only if tf_weapon_criticals is also enabled. 2 - Melee weapons can always randomly crit regardless of the tf_weapon_criticals setting.")]
public sealed class TFWeaponCriticalsMeleeConVar : IntConVar { }

// ---- shared/basecombatweapon_shared.cpp (the TF crit bucket) -----------------

[ConVar("tf_weapon_criticals_bucket_cap", "1000.0", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat)]
public sealed class TFWeaponCriticalsBucketCapConVar : FloatConVar { }

[ConVar("tf_weapon_criticals_bucket_bottom", "-250.0", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat)]
public sealed class TFWeaponCriticalsBucketBottomConVar : FloatConVar { }

[ConVar("tf_weapon_criticals_bucket_default", "300.0", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat)]
public sealed class TFWeaponCriticalsBucketDefaultConVar : FloatConVar { }

// ---- shared/teamplayroundbased_gamerules.cpp (the shared block) -------------

[ConVar("mp_capstyle", "1", Flags = ConVarFlags.Replicated | ConVarFlags.DevelopmentOnly,
        Help = "Sets the style of capture points used. 0 = Fixed players required to cap. 1 = More players cap faster, but longer cap times.")]
public sealed class MpCapStyleConVar : IntConVar { }

[ConVar("mp_blockstyle", "1", Flags = ConVarFlags.Replicated | ConVarFlags.DevelopmentOnly,
        Help = "Sets the style of capture point blocking used. 0 = Blocks break captures completely. 1 = Blocks only pause captures.")]
public sealed class MpBlockStyleConVar : IntConVar { }

[ConVar("mp_respawnwavetime", "10.0", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "Time between respawn waves.")]
public sealed class MpRespawnWaveTimeConVar : FloatConVar { }

[ConVar("mp_capdeteriorate_time", "90.0", Flags = ConVarFlags.Replicated | ConVarFlags.DevelopmentOnly,
        Help = "Time it takes for a full capture point to deteriorate.")]
public sealed class MpCapDeteriorateTimeConVar : FloatConVar { }

[ConVar("mp_tournament", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify)]
public sealed class MpTournamentConVar : BoolConVar { }

[ConVar("mp_highlander", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Allow only 1 of each player class type.")]
public sealed class MpHighlanderConVar : BoolConVar { }

[ConVar("mp_teams_unbalance_limit", "1", Flags = ConVarFlags.Replicated,
        Help = "Teams are unbalanced when one team has this many more players than the other team. (0 disables check)",
        Min = 0, Max = 30)]
public sealed class MpTeamsUnbalanceLimitConVar : IntConVar { }

[ConVar("mp_maxrounds", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "max number of rounds to play before server changes maps", Min = 0)]
public sealed class MpMaxRoundsConVar : IntConVar { }

// ---- shared/tf/tf_gamerules.cpp (before its #ifdef GAME_DLL) ----------------

[ConVar("tf_autobalance_dead_candidates_maxtime", "15", Flags = ConVarFlags.Replicated)]
public sealed class TFAutobalanceDeadCandidatesMaxTimeConVar : FloatConVar { }

[ConVar("tf_autobalance_force_candidates_maxtime", "5", Flags = ConVarFlags.Replicated)]
public sealed class TFAutobalanceForceCandidatesMaxTimeConVar : FloatConVar { }

[ConVar("tf_autobalance_xp_bonus", "500", Flags = ConVarFlags.Replicated)]
public sealed class TFAutobalanceXpBonusConVar : IntConVar { }

/// <summary>
/// Stock's server arm also fires <c>winlimit_changed</c> on a change
/// (<c>WinlimitChanged</c>, teamplayroundbased_gamerules.cpp:188-195); the
/// round machine answers it through the convar's subscription.
/// </summary>
[ConVar("mp_winlimit", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Max score one team can reach before server changes maps", Min = 0)]
public sealed class MpWinLimitConVar : IntConVar { }

[ConVar("mp_windifference", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Score difference between teams before server changes maps", Min = 0)]
public sealed class MpWinDifferenceConVar : IntConVar { }

[ConVar("mp_windifference_min", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Minimum score needed for mp_windifference to be applied", Min = 0)]
public sealed class MpWinDifferenceMinConVar : IntConVar { }

[ConVar("mp_disable_respawn_times", "0", Flags = ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class MpDisableRespawnTimesConVar : BoolConVar { }

[ConVar("mp_bonusroundtime", "15", Flags = ConVarFlags.Replicated,
        Help = "Time after round win until round restarts", Min = 5, Max = 15)]
public sealed class MpBonusRoundTimeConVar : IntConVar { }

[ConVar("mp_stalemate_meleeonly", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Restrict everyone to melee weapons only while in Sudden Death.")]
public sealed class MpStalemateMeleeOnlyConVar : BoolConVar { }

// Stock declares tf_stalematechangeclasstime in tf_gamerules.cpp's shared
// block (tf_gamerules.cpp:781), beside the stalemate pair above.

// Stock declares tf_spawn_glows_duration in tf_gamerules.cpp's shared block
// (tf_gamerules.cpp:748), beside the stalemate pair above.

[ConVar("tf_spawn_glows_duration", "10", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "How long should teammates glow after respawning\n")]
public sealed class TFSpawnGlowsDurationConVar : IntConVar { }

// Stock declares the spectator xray in the same shared block
// (tf_gamerules.cpp:747), the pair the glow arms read. Its reader is the
// spectators' per-player client glow, deferred with the outline render it
// gates (docs/plans/tf2/deferred.tsv, the spectator-glows row); nothing
// reads it yet.
[ConVar("tf_spec_xray", "1", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "Allows spectators to see player glows. 1 = same team, 2 = both teams")]
public sealed class TFSpecXrayConVar : IntConVar { }

[ConVar("tf_stalematechangeclasstime", "20", Flags = ConVarFlags.Replicated | ConVarFlags.DevelopmentOnly,
        Help = "Amount of time that players are allowed to change class in stalemates.")]
public sealed class TFStalemateChangeClassTimeConVar : FloatConVar { }

[ConVar("tf_player_movement_restart_freeze", "1", Flags = ConVarFlags.Replicated,
        Help = "When set, prevent player movement during round restart")]
public sealed class TFPlayerMovementRestartFreezeConVar : BoolConVar { }

// Stock declares the suspension mode in tf_gamerules.cpp's shared block
// (tf_gamerules.cpp:1144). Its readers are the voice-command chain's
// admission gates: TFVoiceMenuCommand's mode-1 refusal (the unconditional
// one, tf_player.cpp:20030); the mode-2 token bucket rides the chat
// suspension, GC-only here, and is a divergence (docs/divergences.md).
[ConVar("tf_voice_command_suspension_mode", "2", Flags = ConVarFlags.Replicated,
        Help = "0 = None | 1 = No Voice Commands | 2 = Rate Limited")]
public sealed class TFVoiceCommandSuspensionModeConVar : IntConVar { }

/// <summary>The stalemate convars as gameplay reads them (server side).</summary>
public interface ITFStalemateSettings
{
    /// <summary><c>mp_stalemate_meleeonly</c>.</summary>
    bool MeleeOnly { get; }

    /// <summary><c>tf_stalematechangeclasstime</c>, seconds from the stalemate's start.</summary>
    float ChangeClassTime { get; }
}

/// <summary>The stalemate convars, read live.</summary>
[SideService(Side.Server, As = [typeof(ITFStalemateSettings)])]
public sealed class TFStalemateConVars(MpStalemateMeleeOnlyConVar meleeOnly,
                                       TFStalemateChangeClassTimeConVar changeClassTime) : ITFStalemateSettings
{
    public bool MeleeOnly => meleeOnly.Value;

    public float ChangeClassTime => changeClassTime.Value;
}

[ConVar("mp_forceautoteam", "0", Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Automatically assign players to teams when joining.")]
public sealed class MpForceAutoTeamConVar : BoolConVar { }

// ---- shared/tf/entity_capture_flag.cpp, shared/tf/tf_gamerules.cpp ----------

[ConVar("tf_flag_return_on_touch", "0", Flags = ConVarFlags.Replicated,
        Help = "If this is set, your flag must be at base in order to capture the enemy flag. Remote friendly flags return to your base instantly when you touch them")]
public sealed class TFFlagReturnOnTouchConVar : BoolConVar { }

[ConVar("tf_flag_return_time_credit_factor", "1.0", Flags = ConVarFlags.Replicated,
        Help = "Number of seconds the flag's return time will be credited for each second the flag is being carried.", Min = 0)]
public sealed class TFFlagReturnTimeCreditFactorConVar : FloatConVar { }

/// <summary>
/// Stock's server arm also resets every team's flag captures when this
/// drops to 0 or below (<c>ValidateCapturesPerRound</c>); that lands with
/// the CTF mode (Stage 2).
/// </summary>
[ConVar("tf_flag_caps_per_round", "3", Flags = ConVarFlags.Replicated,
        Help = "Number of captures per round on CTF and PASS Time maps. Set to 0 to disable.", Min = 0)]
public sealed class TFFlagCapsPerRoundConVar : IntConVar { }

// ---- shared/tf/tf_gamerules.cpp (the sticky's air-burst radius) -------------

// Stock declares these outside any GAME_DLL arm (both DLLs compile them),
// though only the server's GetDamageRadius reads them
// (tf_weapon_grenade_pipebomb.cpp:1426-1429).

[ConVar("tf_sticky_radius_ramp_time", "2.0",
        Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Cheat | ConVarFlags.Replicated,
        Help = "Amount of time to get full radius after arming")]
public sealed class TFStickyRadiusRampTimeConVar : FloatConVar { }

[ConVar("tf_sticky_airdet_radius", "0.85",
        Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.Cheat | ConVarFlags.Replicated,
        Help = "Radius Scale if detonated in the air")]
public sealed class TFStickyAirDetRadiusConVar : FloatConVar { }

// ---- shared/tf/tf_weapon_flamethrower.cpp ------------------------------------

[ConVar("tf_flamethrower_boxsize", "12.0", Flags = ConVarFlags.Cheat | ConVarFlags.Replicated,
        Help = "Size of flame damage entities.", Min = 1, Max = 24)]
public sealed class TFFlameThrowerBoxSizeConVar : FloatConVar { }

// tf_weapon_flamethrower.cpp:70, shared: the airblast's ammunition.
[ConVar("tf_flamethrower_burstammo", "20", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated,
        Help = "How much ammo does the air burst use per shot.")]
public sealed class TFFlameThrowerBurstammoConVar : IntConVar { }

// tf_weapon_flamethrower.cpp:71, shared: the legacy flame entity's life
// (tf_weapon_flamethrower.cpp:2816). The live manager's life is the item
// schema's flame_lifetime attribute, and the other reader — the Pyro rage's
// cooldown (tf_player_shared.cpp:13859) — lands with the rage itself
// (docs/plans/tf2/deferred.tsv row 77), so no managed read points here yet.
[ConVar("tf_flamethrower_flametime", "0.5", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly | ConVarFlags.Replicated,
        Help = "Time to live of flame damage entities.")]
public sealed class TFFlameThrowerFlametimeConVar : FloatConVar { }

// ---- shared/tf/tf_weapon_grenade_pipebomb.cpp (file scope, both DLLs) --------

[ConVar("tf_grenadelauncher_livetime", "0.8",
        Flags = ConVarFlags.Cheat | ConVarFlags.Replicated | ConVarFlags.DevelopmentOnly)]
public sealed class TFGrenadeLauncherLiveTimeConVar : FloatConVar { }
