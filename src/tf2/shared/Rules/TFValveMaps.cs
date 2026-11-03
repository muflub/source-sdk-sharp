//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Rules;

/// <summary>
/// TF2's official map lists (<c>tf_gamerules.cpp:257-436</c>): <c>s_ValveMaps</c>
/// (Valve's own maps) and <c>s_CommunityMaps</c> (the community maps Valve
/// featured), each row a <c>MapInfo_t</c> — the disk name, the display name and
/// the game type's localization token. The lookup walks are stock's free
/// functions <c>IsValveMap</c> / <c>IsCommunityMap</c> (<c>:638-661</c>, a
/// <c>Q_stricmp</c> over the disk names); the <see cref="IsOfficialMap"/>
/// composition is <c>CTFGameRules::IsOfficialMap</c>'s body
/// (<c>:16654-16665</c>) before its base-class fallback, which answers false
/// (<c>gamerules.h:503</c>). The tables are switches (SS0010: no static tables
/// in a mod), so <see cref="Row"/> is the <c>MapInfo_t</c> a display-name or
/// game-type reader asks for — stock's <c>GetMapDisplayName</c>
/// (<c>:18974</c>) and <c>GetMapType</c> (<c>:19111</c>), client-only and not
/// ported, walk the same rows in the same order.
/// </summary>
public static class TFValveMaps
{
    /// <summary>Stock's <c>MapInfo_t</c>.</summary>
    public sealed record MapInfo(string DiskName, string DisplayName, string GameType);

    /// <summary>The row a map name holds, Valve's list first (stock's walk order), else null.</summary>
    public static MapInfo? Row(string? mapName) =>
        ValveRow(mapName) is { } valve ? new MapInfo(valve.DiskName, valve.DisplayName, valve.GameType)
        : CommunityRow(mapName) is { } community ? new MapInfo(community.DiskName, community.DisplayName, community.GameType)
        : null;

    /// <summary>Stock's free <c>IsValveMap</c> (<c>:638-648</c>): a <c>Q_stricmp</c> over <c>s_ValveMaps</c>' disk names.</summary>
    public static bool IsValveMap(string? mapName) => ValveRow(mapName) is not null;

    /// <summary>Stock's free <c>IsCommunityMap</c> (<c>:651-661</c>): a <c>Q_stricmp</c> over <c>s_CommunityMaps</c>' disk names.</summary>
    public static bool IsCommunityMap(string? mapName) => CommunityRow(mapName) is not null;

    /// <summary>
    /// <c>CTFGameRules::IsOfficialMap</c>'s body (<c>:16654-16665</c>) with the
    /// table ask inlined: either list's disk name answers. The base class's
    /// fallback (<c>gamerules.h:503</c>) answers false, so there is no third arm.
    /// </summary>
    public static bool IsOfficialMap(string? mapName)
        => ValveRow(mapName) is not null || CommunityRow(mapName) is not null;

    /// <summary>Stock's <c>s_ValveMaps</c> (<c>:264-298</c>), row by row.</summary>
    private static (string DiskName, string DisplayName, string GameType)? ValveRow(string? mapName) => Key(mapName) switch
    {
        "ctf_2fort" => ("ctf_2fort", "2Fort", "#Gametype_CTF"),
        "cp_dustbowl" => ("cp_dustbowl", "Dustbowl", "#TF_AttackDefend"),
        "cp_granary" => ("cp_granary", "Granary", "#Gametype_CP"),
        "cp_well" => ("cp_well", "Well", "#Gametype_CP"),
        "cp_foundry" => ("cp_foundry", "Foundry", "#Gametype_CP"),
        "cp_gravelpit" => ("cp_gravelpit", "Gravel Pit", "#TF_AttackDefend"),
        "tc_hydro" => ("tc_hydro", "Hydro", "#TF_TerritoryControl"),
        "ctf_well" => ("ctf_well", "Well", "#Gametype_CTF"),
        "cp_badlands" => ("cp_badlands", "Badlands", "#Gametype_CP"),
        "pl_goldrush" => ("pl_goldrush", "Gold Rush", "#Gametype_Escort"),
        "pl_badwater" => ("pl_badwater", "Badwater Basin", "#Gametype_Escort"),
        "plr_pipeline" => ("plr_pipeline", "Pipeline", "#Gametype_EscortRace"),
        "cp_gorge" => ("cp_gorge", "Gorge", "#TF_AttackDefend"),
        "ctf_doublecross" => ("ctf_doublecross", "Double Cross", "#Gametype_CTF"),
        "pl_thundermountain" => ("pl_thundermountain", "Thunder Mountain", "#Gametype_Escort"),
        "tr_target" => ("tr_target", "Target", "#GameType_Training"),
        "tr_dustbowl" => ("tr_dustbowl", "Dustbowl", "#GameType_Training"),
        "cp_manor_event" => ("cp_manor_event", "Mann Manor", "#TF_AttackDefend"),
        "cp_mountainlab" => ("cp_mountainlab", "Mountain Lab", "#TF_AttackDefend"),
        "cp_degrootkeep" => ("cp_degrootkeep", "DeGroot Keep", "#TF_MedievalAttackDefend"),
        "pl_barnblitz" => ("pl_barnblitz", "Barnblitz", "#Gametype_Escort"),
        "pl_upward" => ("pl_upward", "Upward", "#Gametype_Escort"),
        "plr_hightower" => ("plr_hightower", "Hightower", "#Gametype_EscortRace"),
        "koth_viaduct" => ("koth_viaduct", "Viaduct", "#Gametype_Koth"),
        "koth_viaduct_event" => ("koth_viaduct_event", "Eyeaduct", "#Gametype_Koth"),
        "koth_king" => ("koth_king", "Kong King", "#Gametype_Koth"),
        "koth_lakeside_event" => ("koth_lakeside_event", "Ghost Fort", "#Gametype_Koth"),
        "plr_hightower_event" => ("plr_hightower_event", "Helltower", "#Gametype_EscortRace"),
        "rd_asteroid" => ("rd_asteroid", "Asteroid", "#Gametype_RobotDestruction"),
        "pl_cactuscanyon" => ("pl_cactuscanyon", "Cactus Canyon", "#Gametype_Escort"),
        "sd_doomsday" => ("sd_doomsday", "Doomsday", "#Gametype_SD"),
        "sd_doomsday_event" => ("sd_doomsday_event", "Carnival of Carnage", "#Gametype_SD"),
        "cp_mercenarypark" => ("cp_mercenarypark", "Mercenary Park", "#TF_AttackDefend"),
        _ => null,
    };

    /// <summary>Stock's <c>s_CommunityMaps</c> (<c>:300-436</c>), row by row.</summary>
    private static (string DiskName, string DisplayName, string GameType)? CommunityRow(string? mapName) => Key(mapName) switch
    {
        "pl_borneo" => ("pl_borneo", "Borneo", "#Gametype_Escort"),
        "koth_suijin" => ("koth_suijin", "Suijin", "#Gametype_Koth"),
        "cp_snowplow" => ("cp_snowplow", "Snowplow", "#TF_AttackDefend"),
        "koth_probed" => ("koth_probed", "Probed", "#Gametype_Koth"),
        "pd_watergate" => ("pd_watergate", "Watergate", "#Gametype_PlayerDestruction"),
        "arena_byre" => ("arena_byre", "Byre", "#Gametype_Arena"),
        "ctf_2fort_invasion" => ("ctf_2fort_invasion", "2Fort Invasion", "#Gametype_CTF"),
        "cp_sunshine_event" => ("cp_sunshine_event", "Sinshine", "#Gametype_CP"),
        "pl_millstone_event" => ("pl_millstone_event", "Hellstone", "#Gametype_Escort"),
        "cp_gorge_event" => ("cp_gorge_event", "Gorge Event", "#TF_AttackDefend"),
        "koth_moonshine_event" => ("koth_moonshine_event", "Moonshine Event", "#Gametype_Koth"),
        "pl_snowycoast" => ("pl_snowycoast", "Snowycoast", "#Gametype_Escort"),
        "cp_vanguard" => ("cp_vanguard", "Vanguard", "#Gametype_CP"),
        "ctf_landfall" => ("ctf_landfall", "Landfall", "#Gametype_CTF"),
        "koth_highpass" => ("koth_highpass", "Highpass", "#Gametype_Koth"),
        "koth_maple_ridge_event" => ("koth_maple_ridge_event", "Maple Ridge Event", "#Gametype_Koth"),
        "pl_fifthcurve_event" => ("pl_fifthcurve_event", "Brimstone", "#Gametype_Escort"),
        "pd_pit_of_death_event" => ("pd_pit_of_death_event", "Pit of Death", "#Gametype_PlayerDestruction"),
        "cp_mossrock" => ("cp_mossrock", "Mossrock", "#TF_AttackDefend"),
        "koth_lazarus" => ("koth_lazarus", "Lazarus", "#Gametype_Koth"),
        "plr_bananabay" => ("plr_bananabay", "Banana Bay", "#Gametype_EscortRace"),
        "pl_enclosure_final" => ("pl_enclosure_final", "Enclosure", "#Gametype_Escort"),
        "koth_brazil" => ("koth_brazil", "Brazil", "#Gametype_Koth"),
        "koth_bagel_event" => ("koth_bagel_event", "Cauldron", "#Gametype_Koth"),
        "pl_rumble_event" => ("pl_rumble_event", "Gravestone", "#Gametype_Escort"),
        "koth_slasher" => ("koth_slasher", "Slasher", "#Gametype_Koth"),
        "pd_cursed_cove_event" => ("pd_cursed_cove_event", "Cursed Cove", "#Gametype_PlayerDestruction"),
        "pd_monster_bash" => ("pd_monster_bash", "Monster Bash", "#Gametype_PlayerDestruction"),
        "koth_slaughter_event" => ("koth_slaughter_event", "Laughter", "#Gametype_Koth"),
        "pl_precipice_event_final" => ("pl_precipice_event_final", "Precipice", "#Gametype_Escort"),
        "koth_megalo" => ("koth_megalo", "Megalo", "#Gametype_Koth"),
        "pl_hasslecastle" => ("pl_hasslecastle", "Hassle Castle", "#Gametype_Escort"),
        "pl_bloodwater" => ("pl_bloodwater", "Bloodwater", "#Gametype_Escort"),
        "koth_undergrove_event" => ("koth_undergrove_event", "Moldergrove", "#Gametype_Koth"),
        "pl_pier" => ("pl_pier", "Pier", "#Gametype_Escort"),
        "pd_snowville_event" => ("pd_snowville_event", "SnowVille", "#Gametype_PlayerDestruction"),
        "ctf_snowfall_final" => ("ctf_snowfall_final", "Snowfall", "#Gametype_CTF"),
        "pl_wutville_event" => ("pl_wutville_event", "Wutville", "#Gametype_Escort"),
        "pd_farmageddon" => ("pd_farmageddon", "Farmageddon", "#Gametype_PlayerDestruction"),
        "koth_los_muertos" => ("koth_los_muertos", "Los Muertos", "#Gametype_Koth"),
        "cp_ambush_event" => ("cp_ambush_event", "Erebus", "#TF_AttackDefend"),
        "pl_terror_event" => ("pl_terror_event", "Terror", "#Gametype_Escort"),
        "arena_lumberyard_event" => ("arena_lumberyard_event", "Graveyard", "#Gametype_Arena"),
        "koth_synthetic_event" => ("koth_synthetic_event", "Sinthetic", "#Gametype_Koth"),
        "pl_coal_event" => ("pl_coal_event", "Polar", "#Gametype_Escort"),
        "pl_breadspace" => ("pl_breadspace", "Bread Space", "#Gametype_Escort"),
        "pl_chilly" => ("pl_chilly", "Chilly", "#Gametype_Escort"),
        "koth_cascade" => ("koth_cascade", "Cascade", "#Gametype_Koth"),
        "cp_altitude" => ("cp_altitude", "Altitude", "#TF_AttackDefend"),
        "ctf_doublecross_snowy" => ("ctf_doublecross_snowy", "Doublefrost", "#Gametype_CTF"),
        "ctf_crasher" => ("ctf_crasher", "Crasher!", "#Gametype_CTF"),
        "ctf_helltrain_event" => ("ctf_helltrain_event", "Helltrain", "#Gametype_CTF"),
        "pl_sludgepit_event" => ("pl_sludgepit_event", "Ghoulpit", "#Gametype_Escort"),
        "cp_spookeyridge" => ("cp_spookeyridge", "Spookeyridge", "#TF_AttackDefend"),
        "koth_sawmill_event" => ("koth_sawmill_event", "Soul-Mill", "#Gametype_Koth"),
        "plr_hacksaw_event" => ("plr_hacksaw_event", "Bonesaw", "#Gametype_EscortRace"),
        "cp_frostwatch" => ("cp_frostwatch", "Frostwatch", "#TF_AttackDefend"),
        "pl_frostcliff" => ("pl_frostcliff", "Frostcliff", "#Gametype_Escort"),
        "pl_rumford_event" => ("pl_rumford_event", "Rumford", "#Gametype_Escort"),
        "ctf_frosty" => ("ctf_frosty", "Frosty", "#Gametype_CTF"),
        "cp_gravelpit_snowy" => ("cp_gravelpit_snowy", "Coal Pit", "#TF_AttackDefend"),
        "koth_sharkbay" => ("koth_sharkbay", "Sharkbay", "#Gametype_Koth"),
        "koth_rotunda" => ("koth_rotunda", "Rotunda", "#Gametype_Koth"),
        "pl_phoenix" => ("pl_phoenix", "Phoenix", "#Gametype_Escort"),
        "pl_cashworks" => ("pl_cashworks", "Cashworks", "#Gametype_Escort"),
        "pl_venice" => ("pl_venice", "Venice", "#Gametype_Escort"),
        "cp_reckoner" => ("cp_reckoner", "Reckoner", "#Gametype_CP"),
        "cp_sulfur" => ("cp_sulfur", "Sulfur", "#TF_AttackDefend"),
        "cp_hardwood_final" => ("cp_hardwood_final", "Hardwood", "#TF_AttackDefend"),
        "ctf_pelican_peak" => ("ctf_pelican_peak", "Pelican Peak", "#Gametype_CTF"),
        "pd_selbyen" => ("pd_selbyen", "Selbyen", "#Gametype_PlayerDestruction"),
        "vsh_tinyrock" => ("vsh_tinyrock", "Tiny Rock", "#GameType_VSH"),
        "vsh_distillery" => ("vsh_distillery", "Distillery", "#GameType_VSH"),
        "vsh_skirmish" => ("vsh_skirmish", "Skirmish", "#GameType_VSH"),
        "vsh_nucleus" => ("vsh_nucleus", "Nucleus VSH", "#GameType_VSH"),
        "arena_perks" => ("arena_perks", "Perks", "#Gametype_Arena"),
        "koth_slime" => ("koth_slime", "Slime", "#Gametype_Koth"),
        "cp_lavapit_final" => ("cp_lavapit_final", "Lava Pit", "#TF_AttackDefend"),
        "pd_mannsylvania" => ("pd_mannsylvania", "Mannsylvania", "#Gametype_PlayerDestruction"),
        "cp_degrootkeep_rats" => ("cp_degrootkeep_rats", "Sandcastle", "#TF_MedievalAttackDefend"),
        "pl_spineyard" => ("pl_spineyard", "Spineyard", "#Gametype_Escort"),
        "pl_corruption" => ("pl_corruption", "Corruption", "#Gametype_Escort"),
        "zi_murky" => ("zi_murky", "Murky", "#GameType_ZI"),
        "zi_atoll" => ("zi_atoll", "Atoll", "#GameType_ZI"),
        "zi_woods" => ("zi_woods", "Woods", "#GameType_ZI"),
        "zi_sanitarium" => ("zi_sanitarium", "Sanitarium", "#GameType_ZI"),
        "zi_devastation_final1" => ("zi_devastation_final1", "Devastation", "#GameType_ZI"),
        "koth_snowtower" => ("koth_snowtower", "Snowtower", "#Gametype_Koth"),
        "koth_krampus" => ("koth_krampus", "Krampus", "#Gametype_Koth"),
        "ctf_haarp" => ("ctf_haarp", "Haarp", "#TF_AttackDefend"),
        "cp_brew" => ("cp_brew", "Brew", "#TF_AttackDefend"),
        "plr_hacksaw" => ("plr_hacksaw", "Hacksaw", "#Gametype_EscortRace"),
        "ctf_turbine_winter" => ("ctf_turbine_winter", "Turbine Event", "#Gametype_CTF"),
        "cp_carrier" => ("cp_carrier", "Carrier", "#TF_AttackDefend"),
        "pd_galleria" => ("pd_galleria", "Galleria", "#Gametype_PlayerDestruction"),
        "pl_emerge" => ("pl_emerge", "Emerge", "#Gametype_Escort"),
        "pl_camber" => ("pl_camber", "Camber", "#Gametype_Escort"),
        "pl_embargo" => ("pl_embargo", "Embargo", "#Gametype_Escort"),
        "pl_odyssey" => ("pl_odyssey", "Odyssey", "#Gametype_Escort"),
        "koth_megaton" => ("koth_megaton", "Megaton", "#Gametype_Koth"),
        "koth_cachoeira" => ("koth_cachoeira", "Cachoeira", "#Gametype_Koth"),
        "cp_overgrown" => ("cp_overgrown", "Overgrown", "#TF_AttackDefend"),
        "cp_hadal" => ("cp_hadal", "Hadal", "#TF_AttackDefend"),
        "ctf_applejack" => ("ctf_applejack", "Applejack", "#Gametype_CTF"),
        "pd_atom_smash" => ("pd_atom_smash", "Atom Smash", "#Gametype_PlayerDestruction"),
        "cp_canaveral_5cp" => ("cp_canaveral_5cp", "Canaveral", "#Gametype_CP"),
        "cp_burghausen" => ("cp_burghausen", "Burghausen", "#TF_MedievalAttackDefend"),
        "koth_toxic" => ("koth_toxic", "Toxic", "#Gametype_Koth"),
        "cp_darkmarsh" => ("cp_darkmarsh", "Darkmarsh", "#TF_AttackDefend"),
        "cp_freaky_fair" => ("cp_freaky_fair", "Freaky Fair", "#Gametype_CP"),
        "tow_dynamite" => ("tow_dynamite", "Dynamite", "#GameType_TOW"),
        "pd_circus" => ("pd_circus", "Circus", "#Gametype_PlayerDestruction"),
        "vsh_outburst" => ("vsh_outburst", "Outburst", "#GameType_VSH"),
        "zi_blazehattan" => ("zi_blazehattan", "Blazehattan", "#GameType_ZI"),
        "koth_overcast_final" => ("koth_overcast_final", "Overcast", "#Gametype_Koth"),
        "cp_fortezza" => ("cp_fortezza", "Fortezza", "#TF_AttackDefend"),
        "ctf_penguin_peak" => ("ctf_penguin_peak", "Penguin Peak", "#Gametype_CTF"),
        "pl_patagonia" => ("pl_patagonia", "Patagonia", "#Gametype_Escort"),
        "plr_cutter" => ("plr_cutter", "Cutter", "#Gametype_EscortRace"),
        "vsh_maul" => ("vsh_maul", "Maul", "#GameType_VSH"),
        "pl_citadel" => ("pl_citadel", "Citadel", "#Gametype_Escort"),
        "pl_aquarius" => ("pl_aquarius", "Aquarius", "#Gametype_Escort"),
        "cp_fulgur" => ("cp_fulgur", "Fulgur", "#TF_AttackDefend"),
        "cp_cargo" => ("cp_cargo", "Cargo", "#TF_AttackDefend"),
        "cp_conifer" => ("cp_conifer", "Conifer", "#TF_AttackDefend"),
        "koth_boardwalk" => ("koth_boardwalk", "Boardwalk", "#Gametype_Koth"),
        "koth_blowout" => ("koth_blowout", "Blowout", "#Gametype_Koth"),
        "koth_mannhole" => ("koth_mannhole", "Mannhole", "#Gametype_Koth"),
        "koth_demolition" => ("koth_demolition", "Demolition", "#Gametype_Koth"),
        "ctf_pressure" => ("ctf_pressure", "Pressure", "#Gametype_CTF"),
        "cp_cowerhouse" => ("cp_cowerhouse", "Cowerhouse", "#Gametype_CP"),
        "koth_dusker" => ("koth_dusker", "Dusker", "#Gametype_Koth"),
        "arena_afterlife" => ("arena_afterlife", "Afterlife", "#Gametype_Arena"),
        "ctf_doublecross_event" => ("ctf_doublecross_event", "Devilcross", "#Gametype_CTF"),
        "sd_marshlands" => ("sd_marshlands", "Marshlands", "#GameType_HTF"),
        _ => null,
    };

    /// <summary>Q_stricmp's case-insensitive compare, as the switch's one spelling of the name.</summary>
    private static string? Key(string? mapName) => mapName?.ToLowerInvariant();
}
