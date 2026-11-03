using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using SourceSharp.OracleTests;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// TF2's convars (<c>src/tf2/shared/ConVars</c>, <c>src/tf2/server/ConVars</c>)
/// held against their declarations in the PRISTINE clone (<see cref="ValveSdk"/>):
/// one row per convar names the stock file and the side whose DLL declares it.
/// Each row parses that file's <c>ConVar x( "name", "default", flags, "help",
/// bMin, min, bMax, max, callback )</c>, works out from the enclosing
/// <c>#if</c> arms which DLL compiles it, and asserts the C# declaration has
/// the same default, flags (less FCVAR_GAMEDLL/FCVAR_CLIENTDLL, which each
/// DLL's registration adds), help and bounds, on exactly that side.
///
/// <para>
/// The declarations are read as attribute data (<see cref="CustomAttributeData"/>),
/// not as typed attributes: <c>ConVarAttribute</c> compiles into each side's
/// interop, so the server's and the client's are different types.
/// </para>
/// </summary>
public sealed class TFConVarsTests
{
    private const string Movement = "src/game/shared/tf/tf_gamemovement.cpp";
    private const string WeaponBase = "src/game/shared/tf/tf_weaponbase.cpp";
    private const string Melee = "src/game/shared/tf/tf_weaponbase_melee.cpp";
    private const string SniperRifle = "src/game/shared/tf/tf_weapon_sniperrifle.cpp";
    private const string ViewModel = "src/game/shared/tf/tf_viewmodel.cpp";
    private const string CombatWeapon = "src/game/shared/basecombatweapon_shared.cpp";
    private const string RoundRules = "src/game/shared/teamplayroundbased_gamerules.cpp";
    private const string Flag = "src/game/shared/tf/entity_capture_flag.cpp";
    private const string TFRules = "src/game/shared/tf/tf_gamerules.cpp";
    private const string TrainWatcher = "src/game/server/team_train_watcher.cpp";
    private const string RoundTimer = "src/game/shared/teamplay_round_timer.cpp";
    private const string AreaCapture = "src/game/server/trigger_area_capture.cpp";
    private const string IConVarH = "src/public/tier1/iconvar.h";
    private const string PlayerShared = "src/game/shared/tf/tf_player_shared.cpp";
    private const string TFPlayer = "src/game/server/tf/tf_player.cpp";
    private const string Medigun = "src/game/shared/tf/tf_weapon_medigun.cpp";
    private const string TFPlayerClient = "src/game/client/tf/c_tf_player.cpp";
    private const string ClientMode = "src/game/client/tf/clientmode_tf.cpp";
    private const string Pipebomb = "src/game/shared/tf/tf_weapon_grenade_pipebomb.cpp";
    private const string AmmoPack = "src/game/server/tf/tf_ammo_pack.cpp";
    private const string DroppedWeapon = "src/game/shared/tf/tf_dropped_weapon.cpp";
    private const string TFAccount = "src/game/client/tf/tf_hud_account.cpp";
    private const string ObjectiveStatus = "src/game/client/tf/tf_hud_objectivestatus.cpp";
    private const string TFNotification = "src/game/client/tf/tf_hud_notification_panel.cpp";
    private const string HudCrosshair = "src/game/client/tf/tf_hud_crosshair.cpp";
    private const string Teleporter = "src/game/server/tf/tf_obj_teleporter.cpp";
    private const string SentryGun = "src/game/server/tf/tf_obj_sentrygun.cpp";
    private const string Sapper = "src/game/server/tf/tf_obj_sapper.cpp";
    private const string Flamethrower = "src/game/shared/tf/tf_weapon_flamethrower.cpp";
    private const string FxShared = "src/game/shared/tf/tf_fx_shared.cpp";
    private const string SharedDefs = "src/game/shared/shareddefs.h";

    private const string ConVarAttributeName = "SourceSharp.Abi.ConVarAttribute";
    private const uint DllFlags = (1u << 2) | (1u << 3); // FCVAR_GAMEDLL | FCVAR_CLIENTDLL

    public enum StockSide { Both, Server, Client, NotInRelease }

    /// <summary>A stock declaration's arguments, as written.</summary>
    public sealed record Stock(string Name, string Default, uint Flags, string Help, float Min, float Max);

    /// <summary>A C# declaration's attribute data.</summary>
    public sealed record Managed(string Name, string Default, uint Flags, string Help, float Min, float Max, Type Type);

    // ---- the rows: every TF2 convar but the pyro_* family (PyroVisionTests) ----

    public static TheoryData<string, string, StockSide> Rows() => new()
    {
        // tf_gamemovement.cpp:42-77, all outside any #if.
        { "tf_avoidteammates", Movement, StockSide.Both },
        { "tf_avoidteammates_pushaway", Movement, StockSide.Both },
        { "tf_solidobjects", Movement, StockSide.Both },
        { "tf_clamp_back_speed", Movement, StockSide.Both },
        { "tf_clamp_back_speed_min", Movement, StockSide.Both },
        { "tf_clamp_airducks", Movement, StockSide.Both },
        { "tf_resolve_stuck_players", Movement, StockSide.Both },
        { "tf_movement_aircurrent_friction_mult", Movement, StockSide.Both },
        { "tf_movement_aircurrent_aircontrol_mult", Movement, StockSide.Both },
        { "tf_movement_lost_footing_restick", Movement, StockSide.Both },
        { "tf_movement_lost_footing_friction", Movement, StockSide.Both },
        // tf_weaponbase.cpp:277 ConVar tf_weapon_criticals( "tf_weapon_criticals", "1", FCVAR_REPLICATED | FCVAR_NOTIFY, "Whether or not random crits are enabled" );
        { "tf_weapon_criticals", WeaponBase, StockSide.Both },
        // tf_weaponbase.cpp:83 ConVar tf_weapon_criticals_nopred( "tf_weapon_criticals_nopred", "1.0", FCVAR_REPLICATED | FCVAR_CHEAT );
        { "tf_weapon_criticals_nopred", WeaponBase, StockSide.Both },
        // tf_weaponbase_melee.cpp:26 ConVar tf_weapon_criticals_melee( "tf_weapon_criticals_melee", "1", FCVAR_REPLICATED | FCVAR_NOTIFY, "Controls random crits ..." );
        { "tf_weapon_criticals_melee", Melee, StockSide.Both },
        // tf_weaponbase_melee.cpp:50, under #ifndef CLIENT_DLL: "80.0", FCVAR_CHEAT | FCVAR_GAMEDLL | FCVAR_DEVELOPMENTONLY
        { "tf_meleeattackforcescale", Melee, StockSide.Server },
        // basecombatweapon_shared.cpp:60-62, "1000.0" / "-250.0" / "300.0", FCVAR_REPLICATED | FCVAR_CHEAT
        { "tf_weapon_criticals_bucket_cap", CombatWeapon, StockSide.Both },
        { "tf_weapon_criticals_bucket_bottom", CombatWeapon, StockSide.Both },
        { "tf_weapon_criticals_bucket_default", CombatWeapon, StockSide.Both },
        // teamplayroundbased_gamerules.cpp:198-238, the shared block.
        { "mp_capstyle", RoundRules, StockSide.Both },
        { "mp_blockstyle", RoundRules, StockSide.Both },
        { "mp_respawnwavetime", RoundRules, StockSide.Both },
        { "mp_capdeteriorate_time", RoundRules, StockSide.Both },
        { "mp_tournament", RoundRules, StockSide.Both },
        { "mp_highlander", RoundRules, StockSide.Both },
        { "mp_teams_unbalance_limit", RoundRules, StockSide.Both },
        { "mp_maxrounds", RoundRules, StockSide.Both },
        { "mp_winlimit", RoundRules, StockSide.Both },
        // tf_gamerules.cpp:810-811 (shared): the lead that ends the map, and its floor.
        { "mp_windifference", TFRules, StockSide.Both },
        { "mp_windifference_min", TFRules, StockSide.Both },
        { "mp_disable_respawn_times", RoundRules, StockSide.Both },
        { "mp_bonusroundtime", RoundRules, StockSide.Both },
        { "mp_stalemate_meleeonly", RoundRules, StockSide.Both },
        // tf_gamerules.cpp:781 (shared).
        { "tf_stalematechangeclasstime", TFRules, StockSide.Both },
        { "mp_forceautoteam", RoundRules, StockSide.Both },
        // teamplayroundbased_gamerules.cpp:242-401, #ifdef GAME_DLL.
        { "mp_enableroundwaittime", RoundRules, StockSide.Server },
        { "mp_restartround", RoundRules, StockSide.Server },
        { "mp_stalemate_timelimit", RoundRules, StockSide.Server },
        { "mp_autoteambalance", RoundRules, StockSide.Server },
        { "mp_stalemate_enable", RoundRules, StockSide.Server },
        { "mp_match_end_at_timelimit", RoundRules, StockSide.Server },
        { "mp_scrambleteams_auto", RoundRules, StockSide.Server },
        { "mp_scrambleteams_auto_windifference", RoundRules, StockSide.Server },
        // entity_capture_flag.cpp:54,57 and tf_gamerules.cpp:1299 (shared).
        { "tf_flag_return_on_touch", Flag, StockSide.Both },
        { "tf_flag_return_time_credit_factor", Flag, StockSide.Both },
        { "tf_flag_caps_per_round", TFRules, StockSide.Both },
        // tf_gamerules.cpp:1092,1093,1099,1112,1114, #ifdef GAME_DLL.
        { "tf_gamemode_cp", TFRules, StockSide.Server },
        { "tf_gamemode_ctf", TFRules, StockSide.Server },
        { "tf_gamemode_payload", TFRules, StockSide.Server },
        { "tf_gravetalk", TFRules, StockSide.Server },
        { "tf_allow_player_name_change", TFRules, StockSide.Server },
        { "mp_spectators_restricted", TFRules, StockSide.Server },
        { "tf_ctf_bonus_time", TFRules, StockSide.Server },
        // tf_player_shared.cpp:113,118, outside any #if: "1" REPLICATED|CHEAT|DEVELOPMENTONLY, "999" DEVELOPMENTONLY|REPLICATED.
        { "tf_scout_air_dash_count", PlayerShared, StockSide.Both },
        { "tf_spy_max_cloaked_speed", PlayerShared, StockSide.Both },
        // team_train_watcher.cpp:413-414 (a server file): "30" / "5", 0, "", true, 0, false, 0
        { "tf_escort_recede_time", TrainWatcher, StockSide.Server },
        { "tf_escort_recede_time_overtime", TrainWatcher, StockSide.Server },
        // stage 2.4's objectives: tf_gamerules.cpp:780 (shared), trigger_area_capture.cpp:297 (a server file).
        { "tf_caplinear", TFRules, StockSide.Both },
        { "mp_simulatemultiplecappers", AreaCapture, StockSide.Server },
        // stage 2.7's damage layer: tf_player_shared.cpp:170 (shared), tf_gamerules.cpp:733-734 and
        // :13366 (#ifdef GAME_DLL), server/tf/tf_player.cpp:197 and :8791 (a server file).
        { "tf_damage_disablespread", PlayerShared, StockSide.Both },
        { "tf_weapon_criticals_distance_falloff", TFRules, StockSide.Server },
        { "tf_weapon_minicrits_distance_falloff", TFRules, StockSide.Server },
        { "tf_fall_damage_disablespread", TFRules, StockSide.Server },
        { "tf_damage_range", TFPlayer, StockSide.Server },
        { "tf_debug_damage", TFPlayer, StockSide.Server },
        // stage 2.3's gib rule: tf_player.cpp:188 (server), the ShouldGib mode.
        { "tf_playergib", TFPlayer, StockSide.Server },
        // stage 2.12's cloaked spy: tf_gamerules.cpp:690, inside the GAME_DLL block (672-741).
        { "tf_stealth_damage_reduction", TFRules, StockSide.Server },
        // stage 2.14's humans' team: tf_gamerules.cpp:838, inside the GAME_DLL block (834+).
        { "mp_humans_must_join_team", TFRules, StockSide.Server },
        // stage 2.3's class limit: tf_gamerules.cpp:825 (shared).
        { "tf_classlimit", TFRules, StockSide.Both },
        // stage 2.13's restart freeze: tf_gamerules.cpp:826 (shared, the line before #ifdef GAME_DLL).
        { "tf_player_movement_restart_freeze", TFRules, StockSide.Both },
        // stage 2.3's spawn glows: tf_gamerules.cpp:748 (shared).
        { "tf_spawn_glows_duration", TFRules, StockSide.Both },
        // stage 2.3's voice-command suspension: tf_gamerules.cpp:1144 (shared, between the GAME_DLL blocks).
        { "tf_voice_command_suspension_mode", TFRules, StockSide.Both },
        // stage 2.3's spectator xray: tf_gamerules.cpp:747 (shared, beside the spawn glows).
        { "tf_spec_xray", TFRules, StockSide.Both },
        // mp_autoteambalance 2's knobs: tf_gamerules.cpp:828-831 (shared, before the #ifdef GAME_DLL).
        { "tf_autobalance_dead_candidates_maxtime", TFRules, StockSide.Both },
        { "tf_autobalance_force_candidates_maxtime", TFRules, StockSide.Both },
        { "tf_autobalance_xp_bonus", TFRules, StockSide.Both },
        // stage 2.12's spy: tf_player_shared.cpp:115-116 (shared) and :166-168 (shared).
        { "tf_spy_invis_time", PlayerShared, StockSide.Both },
        { "tf_spy_invis_unstealth_time", PlayerShared, StockSide.Both },
        { "tf_spy_cloak_consume_rate", PlayerShared, StockSide.Both },
        { "tf_spy_cloak_regen_rate", PlayerShared, StockSide.Both },
        { "tf_spy_cloak_no_attack_time", PlayerShared, StockSide.Both },
        // stage 2.9's medigun and healing: tf_player_shared.cpp:120,121 (shared) and :136 (#ifdef GAME_DLL).
        { "tf_max_health_boost", PlayerShared, StockSide.Both },
        { "tf_invuln_time", PlayerShared, StockSide.Both },
        { "tf_boost_drain_time", PlayerShared, StockSide.Server },
        // tf_weapon_medigun.cpp:76, outside any #if: the meter's build, read by the
        // server's charge arm and the client's predicted replay alike.
        { "weapon_medigun_charge_rate", Medigun, StockSide.Both },
        // tf_weapon_medigun.cpp:82, #ifdef CLIENT_DLL: the client declares it, the server reads it per player.
        { "tf_medigun_autoheal", Medigun, StockSide.Client },
        // tf_weapon_medigun.cpp:86, #if !defined (CLIENT_DLL): the heal traces' lag-compensation gate.
        { "tf_medigun_lagcomp", Medigun, StockSide.Server },
        // c_tf_player.cpp:211, the client declares it and the server reads it per player
        // (tf_gamerules.cpp:10269): USERINFO | ARCHIVE.
        { "cl_autoreload", TFPlayerClient, StockSide.Client },
        // tf_weaponbase.cpp:4707-4708, CLIENT_DLL: the bob's period and peak, CHEAT, no help.
        { "cl_bobcycle", WeaponBase, StockSide.Client },
        { "cl_bobup", WeaponBase, StockSide.Client },
        // tf_weapon_sniperrifle.cpp:49, CLIENT_DLL: the full charge's bell,
        // ARCHIVE, no help; the client charge arms (:386-396, :1931-1941) read it.
        { "tf_sniper_fullcharge_bell", SniperRifle, StockSide.Client },
        // tf_viewmodel.cpp:125 and :127, CLIENT_DLL: the min mode's pair —
        // ARCHIVE ("Use minimized viewmodels.") and CHEAT (the x y z override).
        { "tf_use_min_viewmodels", ViewModel, StockSide.Client },
        { "tf_viewmodels_offset_override", ViewModel, StockSide.Client },
        // clientmode_tf.cpp:114, the client declares it and the server clamps it onto
        // the player's record (tf_gamerules.cpp:10275-10279): ARCHIVE | USERINFO, 20..90.
        { "fov_desired", ClientMode, StockSide.Client },
        // c_tf_player.cpp:1702, the client's own file: "0.95", CHEAT | DEVELOPMENTONLY.
        { "tf_teammate_max_invis", TFPlayerClient, StockSide.Client },
        // c_tf_player.cpp:5533, beside the taunt camera that reads it: a static
        // ConVar, "150", CHEAT | DEVELOPMENTONLY, no help.
        { "tf_tauntcam_dist", TFPlayerClient, StockSide.Client },
        // tf_hud_account.cpp:79, the account panel's switch: USERINFO | ARCHIVE | ARCHIVE_XBOX, no help.
        { "hud_combattext", TFAccount, StockSide.Client },
        // tf_hud_notification_panel.cpp:29, the notification banner's duration (stage 2.5): no flags.
        { "tf_hud_notification_duration", TFNotification, StockSide.Client },
        // tf_weapon_grenade_pipebomb.cpp:949-953, inside the #ifdef GAME_DLL block.
        { "tf_grenade_forcefrom_bullet", Pipebomb, StockSide.Server },
        { "tf_grenade_forcefrom_buckshot", Pipebomb, StockSide.Server },
        { "tf_grenade_forcefrom_blast", Pipebomb, StockSide.Server },
        { "tf_grenade_force_sleeptime", Pipebomb, StockSide.Server },
        { "tf_pipebomb_force_to_move", Pipebomb, StockSide.Server },
        // the deflected count's clock: tf_weapon_grenade_pipebomb.cpp:954
        // (the same GAME_DLL block), "10.0", CHEAT | DEVELOPMENTONLY, no help.
        { "tf_pipebomb_deflect_reset_time", Pipebomb, StockSide.Server },
        // stage 2.8's sticky air burst: tf_gamerules.cpp:859-860 — outside any
        // GAME_DLL arm (both DLLs declare them), though only the server's
        // GetDamageRadius reads them.
        { "tf_sticky_radius_ramp_time", TFRules, StockSide.Both },
        { "tf_sticky_airdet_radius", TFRules, StockSide.Both },
        // the sticky's arm time: tf_weapon_grenade_pipebomb.cpp:49-53, file
        // scope after the CLIENT_DLL include (both DLLs), "0.8", CHEAT |
        // REPLICATED | DEVELOPMENTONLY, no help, no bounds.
        { "tf_grenadelauncher_livetime", Pipebomb, StockSide.Both },
        // the flame's collision box: tf_weapon_flamethrower.cpp:60 (shared),
        // "12.0", CHEAT | REPLICATED, 1..24.
        { "tf_flamethrower_boxsize", Flamethrower, StockSide.Both },
        // the airblast's ammunition: tf_weapon_flamethrower.cpp:70 (shared),
        // "20", CHEAT | DEVELOPMENTONLY | REPLICATED, help text.
        { "tf_flamethrower_burstammo", Flamethrower, StockSide.Both },
        // the legacy flame entity's life: tf_weapon_flamethrower.cpp:71
        // (shared), "0.5", CHEAT | DEVELOPMENTONLY | REPLICATED, help text.
        // The live manager's life is the schema's flame_lifetime attribute and
        // the rage-cooldown reader is deferred (row 77), so no managed read
        // points here yet — the declaration is the port.
        { "tf_flamethrower_flametime", Flamethrower, StockSide.Both },
        // the ammo pack's ragdoll throw: server/tf/tf_ammo_pack.cpp:112-114 (a server file),
        // "100" / "150" / "300", FCVAR_CHEAT | FCVAR_DEVELOPMENTONLY, no help, no bounds.
        { "tf_weapon_ragdoll_velocity_min", AmmoPack, StockSide.Server },
        { "tf_weapon_ragdoll_velocity_max", AmmoPack, StockSide.Server },
        { "tf_weapon_ragdoll_maxspeed", AmmoPack, StockSide.Server },
        // the dropped weapon's lifetime: shared/tf/tf_dropped_weapon.cpp:27 (a
        // GAME_DLL arm), "30", FCVAR_CHEAT, no help, no bounds.
        { "tf_dropped_weapon_lifetime", DroppedWeapon, StockSide.Server },
        // tf_hud_objectivestatus.cpp:42, the clock's server-time switch: "0",
        // FCVAR_ARCHIVE, "Display time left before the current map ends."
        { "tf_hud_show_servertimelimit", ObjectiveStatus, StockSide.Client },
        // the round timer's overtime nag: teamplay_round_timer.cpp:86, under
        // #if defined( GAME_DLL ) && defined( TF_DLL ) — the server DLL's.
        { "tf_overtime_nag", RoundTimer, StockSide.Server },
        // tf_hud_crosshair.cpp:23-29, the crosshair family: the three colours
        // and the scale the paint reads, and the custom-file switch. All five
        // plain FCVAR_ARCHIVE, no help, outside any #if.
        { "cl_crosshair_red", HudCrosshair, StockSide.Client },
        { "cl_crosshair_green", HudCrosshair, StockSide.Client },
        { "cl_crosshair_blue", HudCrosshair, StockSide.Client },
        { "cl_crosshair_file", HudCrosshair, StockSide.Client },
        { "cl_crosshair_scale", HudCrosshair, StockSide.Client },
        // the arrival's unzoom: server/tf/tf_obj_teleporter.cpp:82-83 (a server
        // file), "120" / "0.5", FCVAR_CHEAT | FCVAR_DEVELOPMENTONLY, the start
        // floored at 1.
        { "tf_teleporter_fov_start", Teleporter, StockSide.Server },
        { "tf_teleporter_fov_time", Teleporter, StockSide.Server },
        // the sentry's shells: tf_obj_sentrygun.cpp:134 (a server file), "16",
        // CHEAT | DEVELOPMENTONLY, no help.
        { "tf_sentrygun_damage", SentryGun, StockSide.Server },
        // the sapper's drain: tf_obj_sapper.cpp:43 (a server file), "25", no flags.
        { "obj_sapper_amount", Sapper, StockSide.Server },
        // the airblast's arm: tf_weapon_flamethrower.cpp:84, inside GAME_DLL, "1", CHEAT.
        { "tf_airblast_cray", Flamethrower, StockSide.Server },
        // the old arm's lift: tf_weapon_flamethrower.cpp:52, GAME_DLL, "350",
        // CHEAT | DEVELOPMENTONLY, no help.
        { "tf_flamethrower_burst_zvelocity", Flamethrower, StockSide.Server },
        // the fixed multi-pellet spread: tf_fx_shared.cpp:22 (shared).
        { "tf_use_fixed_weaponspreads", FxShared, StockSide.Both },
        // the pre-round freeze's push waiver: tf_player.cpp:10231 (a server
        // file), "0", FCVAR_NONE; ApplyPushFromDamage (:10235) reads it.
        { "tf_preround_push_from_damage_enable", TFPlayer, StockSide.Server },
        // the debug push scales: tf_weapon_flamethrower.cpp:1255-1258, inside
        // #ifdef _DEBUG — "1.0" twice, no flags, no help. The release build
        // has neither, so only TF2.Server declares them.
        { "tf_pushbackscalescale", Flamethrower, StockSide.NotInRelease },
        { "tf_pushbackscalescale_vertical", Flamethrower, StockSide.NotInRelease },
        // the voice-menu repeat's cap: tf_player.cpp:202 (server), "1.5",
        // FCVAR_DEVELOPMENTONLY, min 0.1; NoteSpokeVoiceCommand (:20066).
        { "tf_max_voice_speak_delay", TFPlayer, StockSide.Server },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_convar_is_declared_as_stock_declares_it_on_the_side_stock_does(string name, string stockFile, StockSide side)
    {
        string text = ValveSdk.Read(stockFile);
        var (stock, at) = Declaration(text, name);

        Assert.Equal(side, SideAt(text, at, stockFile));

        var server = Declared(typeof(TF2.Server.TFClassTableLoader).Assembly);
        var client = Declared(ClientAssembly());
        if (side == StockSide.Client)
        {
            // The CLIENT_DLL arm: only the client's DLL compiles it, so only TF2.Client declares it.
            Assert.False(server.ContainsKey(name), name + " is client-only in stock and TF2.Server declares it");
            Assert.True(client.TryGetValue(name, out var own), name + " is not declared by TF2.Client");
            Assert.Equal(stock.Default, own.Default);
            Assert.Equal(Hex(stock.Flags & ~DllFlags), Hex(own.Flags));
            Assert.Equal(stock.Help, own.Help);
            Assert.Equal(stock.Min, own.Min);
            Assert.Equal(stock.Max, own.Max);
            return;
        }
        Assert.True(server.TryGetValue(name, out var mine), name + " is not declared by TF2.Server");
        if (side == StockSide.Both)
        {
            Assert.True(client.TryGetValue(name, out var mirror), name + " is shared in stock and not declared by TF2.Client");
            Assert.Equal(mine with { Type = typeof(object) }, mirror with { Type = typeof(object) });
        }
        else
        {
            Assert.False(client.ContainsKey(name), name + " is server-only in stock and TF2.Client declares it");
        }

        Assert.Equal(stock.Default, mine.Default);
        Assert.Equal(Hex(stock.Flags & ~DllFlags), Hex(mine.Flags));
        Assert.Equal(stock.Help, mine.Help);
        Assert.Equal(stock.Min, mine.Min);
        Assert.Equal(stock.Max, mine.Max);
    }

    [Fact]
    public void Every_tf_and_mp_convar_TF2_declares_has_a_row()
    {
        var rows = Rows().Select(r => (string)r[0]).ToHashSet(StringComparer.Ordinal);
        var declared = Declared(typeof(TF2.Server.TFClassTableLoader).Assembly).Keys
            .Concat(Declared(ClientAssembly()).Keys)
            .Where(n => !n.StartsWith("pyro_", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(declared.Count >= 44, "only " + declared.Count + " TF2 convars found: the scan is not reading the assemblies");
        Assert.Empty(declared.Except(rows).Order());
        Assert.Empty(rows.Except(declared).Order());
    }

    [Fact]
    public void Every_TF2_convar_bears_a_prefix_stock_names_its_family_by()
    {
        // pyro_*: viewpostprocess.cpp's pyro vision family, declared by TF2.Client since C7.
        // hud_*: tf_hud_account.cpp's user settings, declared by TF2.Client since the combat text.
        // cl_crosshair_*: tf_hud_crosshair.cpp:23-29's crosshair family, declared by
        // TF2.Client since the bloom row — stock's own cl_-named client convars.
        // weapon_*: tf_weapon_medigun.cpp:74-79's medigun family — stock's own name.
        // obj_*: tf_obj_sapper.cpp:43's sapper — stock's own name.
        // cl_autoreload: c_tf_player.cpp:211, cl_bobcycle/cl_bobup: tf_weaponbase.cpp:4707-4708, and fov_desired:
        // clientmode_tf.cpp:114 — stock's own client-side view and player convars.
        var names = Declared(typeof(TF2.Server.TFClassTableLoader).Assembly).Keys
            .Concat(Declared(ClientAssembly()).Keys).ToArray();

        Assert.Contains("pyro_vignette", names);
        Assert.DoesNotContain(names, n => !(n.StartsWith("tf_", StringComparison.Ordinal)
                                           || n.StartsWith("mp_", StringComparison.Ordinal)
                                           || n.StartsWith("hud_", StringComparison.Ordinal)
                                           || n.StartsWith("pyro_", StringComparison.Ordinal)
                                           || n.StartsWith("weapon_", StringComparison.Ordinal)
                                           || n.StartsWith("obj_", StringComparison.Ordinal)
                                           || n.StartsWith("cl_crosshair", StringComparison.Ordinal)
                                           || n.StartsWith("cl_autoreload", StringComparison.Ordinal)
                                           || n.StartsWith("cl_bob", StringComparison.Ordinal)
                                           || n.StartsWith("fov_", StringComparison.Ordinal)));
    }

    /// <summary>A name both the core and TF2 declare throws at registration (one DLL, one name).</summary>
    [Fact]
    public void No_TF2_convar_is_one_the_core_declares()
    {
        var core = CoreConVarNames();
        Assert.Contains("mp_waitingforplayers_time", core);
        Assert.Contains("mp_timelimit", core);

        var tf2 = Declared(typeof(TF2.Server.TFClassTableLoader).Assembly).Keys
            .Concat(Declared(ClientAssembly()).Keys).Distinct();

        Assert.DoesNotContain(tf2, core.Contains);
    }

    // ---- the oracle's own controls ------------------------------------------

    [Fact]
    public void The_oracle_refuses_a_name_the_file_does_not_declare()
    {
        string text = ValveSdk.Read(Movement);

        Assert.Throws<Xunit.Sdk.XunitException>(() => Declaration(text, "tf_no_such_convar"));
    }

    [Fact]
    public void The_oracle_reads_bounds_and_a_callback_after_them()
    {
        // mp_winlimit( "mp_winlimit", "0", FCVAR_REPLICATED | FCVAR_NOTIFY, "...", true, 0, false, 0 #ifdef GAME_DLL , WinlimitChanged #endif );
        var (winlimit, _) = Declaration(ValveSdk.Read(RoundRules), "mp_winlimit");
        Assert.Equal(0f, winlimit.Min);
        Assert.True(float.IsNaN(winlimit.Max));
        Assert.Equal((1u << 13) | (1u << 8), winlimit.Flags);

        var (unbalance, _) = Declaration(ValveSdk.Read(RoundRules), "mp_teams_unbalance_limit");
        Assert.Equal(0f, unbalance.Min);
        Assert.Equal(30f, unbalance.Max);
        Assert.StartsWith("Teams are unbalanced", unbalance.Help, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tf_weapon_criticals_force_random", CombatWeapon, StockSide.NotInRelease)]   // #ifdef _DEBUG
    [InlineData("tf_weapon_criticals_debug", WeaponBase, StockSide.NotInRelease)]           // #ifdef _DEBUG
    [InlineData("cl_crosshaircolor", WeaponBase, StockSide.Client)]                         // #else of #if !defined( CLIENT_DLL )
    [InlineData("cl_flag_return_size", Flag, StockSide.Client)]                             // #ifdef CLIENT_DLL
    [InlineData("cl_flag_return_height", Flag, StockSide.Server)]                           // its #else
    [InlineData("mp_developer", RoundRules, StockSide.NotInRelease)]                        // #if defined( _DEBUG ) || defined( STAGING_ONLY )
    [InlineData("tf_show_train_path", TrainWatcher, StockSide.Server)]                      // a server/ file
    public void The_side_oracle_tells_the_arms_apart(string name, string stockFile, StockSide side)
    {
        string text = ValveSdk.Read(stockFile);
        var (_, at) = Declaration(text, name);

        Assert.Equal(side, SideAt(text, at, stockFile));
    }

    // ---- reading the C# declarations -----------------------------------------

    private static Assembly ClientAssembly() => typeof(TFClassTableTests).Assembly.GetReferencedAssemblies()
        .Where(a => a.Name == "TF2.Client").Select(Assembly.Load).Single();

    private static Dictionary<string, Managed> Declared(Assembly assembly)
    {
        var byName = new Dictionary<string, Managed>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes())
        {
            foreach (var data in type.GetCustomAttributesData())
            {
                if (data.AttributeType.FullName != ConVarAttributeName)
                    continue;
                var managed = Read(type, data);
                Assert.True(byName.TryAdd(managed.Name, managed), assembly.GetName().Name + " declares " + managed.Name + " twice");
            }
        }
        return byName;
    }

    private static Managed Read(Type type, CustomAttributeData data)
    {
        object? Named(string member) => data.NamedArguments.FirstOrDefault(n => n.MemberName == member).TypedValue.Value;

        return new Managed(
            (string)data.ConstructorArguments[0].Value!,
            (string)data.ConstructorArguments[1].Value!,
            Named("Flags") is { } flags ? Convert.ToUInt32(flags, CultureInfo.InvariantCulture) : 0u,
            (string?)Named("Help") ?? string.Empty,
            Named("Min") is float min ? min : float.NaN,
            Named("Max") is float max ? max : float.NaN,
            type);
    }

    private static HashSet<string> CoreConVarNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        string[] core =
        [
            "SourceSharp.Abi", "SourceSharp.Server", "SourceSharp.Server.Interop", "SourceSharp.Server.Interface",
            "SourceSharp.Client", "SourceSharp.Client.Interop", "SourceSharp.Client.Interface",
        ];
        foreach (string name in core)
        {
            var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, name + ".dll"));
            foreach (var type in assembly.GetTypes())
            {
                foreach (var data in type.GetCustomAttributesData())
                {
                    if (data.AttributeType.FullName == ConVarAttributeName)
                        names.Add((string)data.ConstructorArguments[0].Value!);
                }
            }
        }
        return names;
    }

    // ---- reading the stock declarations --------------------------------------

    /// <summary>The one <c>ConVar x( "name", ... )</c> of <paramref name="name"/> in a file, and where it starts.</summary>
    private static (Stock Stock, int At) Declaration(string text, string name)
    {
        var matches = Regex.Matches(text, @"(?m)^[ \t]*(?:static[ \t]+)?ConVar[ \t]+\w+[ \t]*\(\s*""" + Regex.Escape(name) + @"""");
        if (matches.Count != 1)
            throw new Xunit.Sdk.XunitException($"{name}: {matches.Count} declarations found, want exactly one");

        int at = matches[0].Index;
        int open = text.IndexOf('(', at);
        var args = Arguments(text, open);
        if (args.Count < 2)
            throw new Xunit.Sdk.XunitException($"{name}: {args.Count} arguments, want a name and a default");

        uint flags = args.Count > 2 ? FlagValue(args[2]) : 0u;
        string help = args.Count > 3 && args[3].StartsWith('"') ? CString(args[3]) : string.Empty;
        float min = float.NaN, max = float.NaN;
        if (args.Count >= 8 && (args[4] == "true" || args[4] == "false"))
        {
            if (args[4] == "true")
                min = Number(args[5]);
            if (args[6] == "true")
                max = Number(args[7]);
        }

        return (new Stock(CString(args[0]), CString(args[1]), flags, help, min, max), at);
    }

    /// <summary>
    /// The top-level arguments of the call whose '(' is at <paramref name="open"/>,
    /// with comments and preprocessor lines inside the call dropped.
    /// </summary>
    private static List<string> Arguments(string text, int open)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        for (int i = open + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                int end = i + 1;
                while (text[end] != '"')
                    end += text[end] == '\\' ? 2 : 1;
                current.Append(text, i, end - i + 1);
                i = end;
            }
            else if (c == '/' && text[i + 1] == '/')
            {
                i = text.IndexOf('\n', i) - 1;
            }
            else if (c == '/' && text[i + 1] == '*')
            {
                i = text.IndexOf("*/", i, StringComparison.Ordinal) + 1;
            }
            else if (c == '#' && LineStart(text, i))
            {
                i = text.IndexOf('\n', i) - 1;
            }
            else if (c == '(')
            {
                depth++;
                current.Append(c);
            }
            else if (c == ')')
            {
                if (depth-- == 0)
                {
                    Flush();
                    return args;
                }
                current.Append(c);
            }
            else if (c == ',' && depth == 0)
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }
        throw new Xunit.Sdk.XunitException("a ConVar declaration never closes");

        void Flush()
        {
            string arg = current.ToString().Trim();
            if (arg.Length > 0)
                args.Add(Regex.Replace(arg, @"""\s+""", "")); // adjacent literals concatenate
            current.Clear();
        }
    }

    private static bool LineStart(string text, int i)
    {
        for (int j = i - 1; j >= 0 && text[j] != '\n'; j--)
        {
            if (!char.IsWhiteSpace(text[j]))
                return false;
        }
        return true;
    }

    private static string CString(string literal)
    {
        Assert.True(literal.Length >= 2 && literal[0] == '"' && literal[^1] == '"', "not a string literal: " + literal);
        string body = literal[1..^1];
        var sb = new StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\')
            {
                sb.Append(body[i]);
                continue;
            }
            char e = body[++i];
            sb.Append(e switch { 'n' => '\n', 't' => '\t', '"' => '"', '\\' => '\\', _ => throw new Xunit.Sdk.XunitException("escape \\" + e) });
        }
        return sb.ToString();
    }

    private static float Number(string text)
    {
        string atom = text.TrimEnd('f');
        if (float.TryParse(atom, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            return value;
        // A named bound, such as MAX_FOV (shareddefs.h:274) closing fov_desired
        // (clientmode_tf.cpp:114): read the define, not a table copied by hand.
        Match define = Regex.Match(ValveSdk.Read(SharedDefs), @"(?m)^#define\s+" + Regex.Escape(atom) + @"\s+([\d.]+)\s");
        Assert.True(define.Success, $"'{text}' is neither a number nor a define of {SharedDefs}");
        return float.Parse(define.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static uint FlagValue(string expression)
    {
        var defines = FcvarDefines();
        uint value = 0;
        foreach (string term in expression.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (term == "0")
                continue;
            if (!defines.TryGetValue(term, out uint bit))
                throw new Xunit.Sdk.XunitException($"'{term}' is not an FCVAR_ define of {IConVarH}");
            value |= bit;
        }
        return value;
    }

    private static Dictionary<string, uint> FcvarDefines()
    {
        var defines = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(ValveSdk.Read(IConVarH), @"(?m)^#define\s+(FCVAR_\w+)\s+(?:\(1<<(\d+)\)|(0))\s"))
            defines[m.Groups[1].Value] = m.Groups[3].Success ? 0u : 1u << int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        Assert.True(defines.Count >= 25, $"{IConVarH} gave only {defines.Count} FCVAR_ defines");
        return defines;
    }

    // ---- which DLL compiles a declaration ------------------------------------

    /// <summary>
    /// The side whose DLL compiles the text at <paramref name="at"/>: the
    /// <c>#if</c> arms open there, each read as GAME_DLL/TF_DLL (server),
    /// CLIENT_DLL/TF_CLIENT_DLL (client), _DEBUG/STAGING_ONLY (not in a
    /// release build) or both TF DLLs (neither). Any other condition is
    /// refused, so a new arm cannot be misread as "both".
    /// </summary>
    private static StockSide SideAt(string text, int at, string file)
    {
        var frames = new Stack<(string Condition, bool Else)>();
        foreach (string raw in text[..at].Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith('#'))
                continue;
            string directive = Regex.Replace(Regex.Replace(line, @"//.*$", ""), @"\s+", "");
            if (directive.StartsWith("#ifdef", StringComparison.Ordinal) || directive.StartsWith("#ifndef", StringComparison.Ordinal)
                || (directive.StartsWith("#if", StringComparison.Ordinal) && !directive.StartsWith("#include", StringComparison.Ordinal)))
                frames.Push((directive, false));
            else if (directive.StartsWith("#else", StringComparison.Ordinal))
                frames.Push((frames.Pop().Condition, true));
            else if (directive.StartsWith("#elif", StringComparison.Ordinal))
                throw new Xunit.Sdk.XunitException($"{file}: an #elif encloses the declaration; the side oracle does not read one");
            else if (directive.StartsWith("#endif", StringComparison.Ordinal))
                frames.Pop();
        }

        var side = file.StartsWith("src/game/server/", StringComparison.Ordinal) ? StockSide.Server
            : file.StartsWith("src/game/client/", StringComparison.Ordinal) ? StockSide.Client
            : StockSide.Both;
        foreach (var (condition, inElse) in frames)
        {
            var arm = Arm(condition, file);
            if (arm == StockSide.NotInRelease)
            {
                if (!inElse)
                    return StockSide.NotInRelease;
                continue;
            }
            if (arm == StockSide.Both)
                continue;
            if (inElse)
                arm = arm == StockSide.Server ? StockSide.Client : StockSide.Server;
            if (side != StockSide.Both && side != arm)
                throw new Xunit.Sdk.XunitException($"{file}: nested arms disagree ({side} inside {arm})");
            side = arm;
        }
        return side;
    }

    private static StockSide Arm(string directive, string file)
    {
        switch (directive)
        {
            // teamplay_round_timer.cpp:85-86, tf_overtime_nag.
            case "#ifdefGAME_DLL" or "#ifdefTF_DLL" or "#ifndefCLIENT_DLL" or "#if!defined(CLIENT_DLL)"
                 or "#ifdefined(GAME_DLL)" or "#ifdefined(GAME_DLL)&&defined(TF_DLL)":
                return StockSide.Server;
            case "#ifdefCLIENT_DLL" or "#ifdefTF_CLIENT_DLL" or "#ifndefGAME_DLL" or "#ifdefined(CLIENT_DLL)":
                return StockSide.Client;
            case "#ifdef_DEBUG" or "#ifdefSTAGING_ONLY" or "#ifdefined(_DEBUG)||defined(STAGING_ONLY)":
                return StockSide.NotInRelease;
            case "#ifdefined(TF_CLIENT_DLL)||defined(TF_DLL)" or "#ifdefined(TF_DLL)||defined(TF_CLIENT_DLL)":
                return StockSide.Both;
            default:
                throw new Xunit.Sdk.XunitException($"{file}: '{directive}' encloses the declaration and the side oracle does not read it");
        }
    }

    private static string Hex(uint flags) => "0x" + flags.ToString("X8", CultureInfo.InvariantCulture);
}
