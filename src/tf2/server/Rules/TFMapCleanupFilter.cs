//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared.Player;
using TF2.Shared.Resources;

namespace TF2.Server.Rules;

/// <summary>
/// <b>TF2's map cleanup filter</b>: <c>CTFGameRules::RoundCleanupShouldIgnore</c>
/// and <c>ShouldCreateEntity</c> (tf_gamerules.cpp) over
/// <c>CTeamplayRoundBasedRules</c>'s (teamplayroundbased_gamerules.cpp). An
/// entity stays when its class is in TF's <c>s_PreserveEnts</c>
/// (<see cref="StockTFPreserved"/>), when its class has <c>tf_weapon_</c> in it,
/// or when it is in the teamplay base's <c>s_PreserveEnts</c>
/// (<see cref="StockTeamplayPreserved"/>); the map creates again every class
/// neither list names. Lists match ignoring case (<c>FindInList</c>'s
/// <c>Q_stricmp</c>); the weapon test is <c>Q_strstr</c>'s, case and all.
///
/// <para>
/// <see cref="PortPreserved"/> is what this port names differently from stock
/// or makes where stock makes nothing: each row says which stock class or
/// member it stands for.
/// </para>
/// </summary>
public sealed class TFMapCleanupFilter : IMapCleanupFilter
{
    /// <summary>
    /// TF's <c>s_PreserveEnts</c> (tf_gamerules.cpp), in its order, the END
    /// marker dropped and the <c>#ifdef TF_RAID_MODE</c> row out (no build
    /// defines it). The oracle fact holds it to the pristine file.
    /// </summary>
    public ImmutableArray<string> StockTFPreserved { get; } =
    [
        "tf_gamerules",
        "tf_team_manager",
        "tf_player_manager",
        "tf_team",
        "tf_objective_resource",
        "keyframe_rope",
        "move_rope",
        "tf_viewmodel",
        "tf_logic_training",
        "tf_logic_training_mode",
        "tf_powerup_bottle",
        "tf_mann_vs_machine_stats",
        "tf_wearable",
        "tf_wearable_demoshield",
        "tf_wearable_robot_arm",
        "tf_wearable_vm",
        "tf_logic_bonusround",
        "vote_controller",
        "monster_resource",
        "tf_logic_medieval",
        "tf_logic_cp_timer",
        "tf_logic_tower_defense",
        "tf_logic_mann_vs_machine",
        "func_upgradestation",
        "entity_rocket",
        "entity_carrier",
        "entity_sign",
        "entity_saucer",
        "tf_halloween_gift_pickup",
        "tf_logic_competitive",
        "tf_wearable_razorback",
        "entity_soldier_statue",
    ];

    /// <summary>
    /// <c>CTeamplayRoundBasedRules</c>' <c>s_PreserveEnts</c>
    /// (teamplayroundbased_gamerules.cpp), in its order (its one repeated row,
    /// <c>point_commentary_node</c>, kept), the END marker dropped.
    /// </summary>
    public ImmutableArray<string> StockTeamplayPreserved { get; } =
    [
        "player",
        "viewmodel",
        "worldspawn",
        "soundent",
        "ai_network",
        "ai_hint",
        "env_soundscape",
        "env_soundscape_proxy",
        "env_soundscape_triggerable",
        "env_sprite",
        "env_sun",
        "env_wind",
        "env_fog_controller",
        "func_wall",
        "func_illusionary",
        "info_node",
        "info_target",
        "info_node_hint",
        "point_commentary_node",
        "point_viewcontrol",
        "func_precipitation",
        "func_team_wall",
        "shadow_control",
        "sky_camera",
        "scene_manager",
        "trigger_soundscape",
        "commentary_auto",
        "point_commentary_node",
        "point_commentary_viewpoint",
        "bot_roster",
        "info_populator",
    ];

    /// <summary>
    /// What this port names differently: the player (<c>player</c>), its view
    /// model (<c>tf_viewmodel</c>), its weapons (<c>tf_weapon_*</c>: every TF
    /// weapon is an <c>ss_weapon</c> row here), the TF player's shared state
    /// (<c>CTFPlayer::m_Shared</c>, a member of the player in stock and its
    /// <c>tf_player_data</c> carrier here), the team table (stock's
    /// <c>tf_team</c> entities), the core's scoreboard resource
    /// (<c>player_manager</c>, beside stock's <c>tf_player_manager</c>), the
    /// core Pickups service's own kits, which no map entity string holds, and
    /// the engineer HUD's building carrier (<c>tf_building_resource</c>, made
    /// once at level init where stock's HUD reads each <c>C_BaseObject</c>; a
    /// map never makes it again).
    /// </summary>
    public ImmutableArray<string> PortPreserved { get; } =
    [
        EntityNames.Player,
        EntityNames.PredictedViewModel,
        EntityNames.SsWeapon,
        TFEntityNames.PlayerData,
        ObjectiveClassNames.TeamResource,
        EntityNames.PlayerManager,
        EntityNames.SsPickup,
        TFEntityNames.BuildingResource,
    ];

    /// <summary>The substring stock's <c>Q_strstr( GetClassname(), "tf_weapon_" )</c> keeps.</summary>
    public const string WeaponPrefix = "tf_weapon_";

    // Instance state, not statics (a mod holds none, SS0010): the lists as
    // the two sets the questions below are asked of.
    private readonly ImmutableHashSet<string> _tf;
    private readonly ImmutableHashSet<string> _teamplay;

    public TFMapCleanupFilter()
    {
        _tf = ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, StockTFPreserved.Concat(PortPreserved));
        _teamplay = ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, StockTeamplayPreserved);
    }

    /// <inheritdoc/>
    public bool RoundCleanupShouldIgnore(ScriptedEntity entity)
    {
        if (_tf.Contains(entity.ClassName))
            return true;

        // Every weapon class, by its prefix rather than a row each.
        if (entity.ClassName.Contains(WeaponPrefix, StringComparison.Ordinal))
            return true;

        return _teamplay.Contains(entity.ClassName);
    }

    /// <inheritdoc/>
    public bool ShouldCreateEntity(string className)
        => !_tf.Contains(className) && !_teamplay.Contains(className);
}
