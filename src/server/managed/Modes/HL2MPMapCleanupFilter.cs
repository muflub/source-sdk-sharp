//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <b>The core's map cleanup filter</b>: <c>CHL2MPRules::CleanUpMap</c>'s two
/// rules (hl2mp_gamerules.cpp). A weapon stays while a player owns it and goes
/// when nobody does ("Weapons with owners don't want to be removed"); any
/// other entity stays when its class is in <see cref="StockPreserved"/>, the
/// file's <c>s_PreserveEnts</c>, or <see cref="PortPreserved"/>; and the map
/// creates again every class neither list names (<c>CHL2MPMapEntityFilter</c>).
/// Matched ignoring case, as stock's <c>FindInList</c> (<c>Q_stricmp</c>).
/// </summary>
public sealed class HL2MPMapCleanupFilter : IMapCleanupFilter
{
    /// <summary>
    /// <c>s_PreserveEnts</c> (hl2mp_gamerules.cpp), in its order, the END
    /// marker dropped. The oracle fact holds it to the pristine file.
    /// </summary>
    public static readonly ImmutableArray<string> StockPreserved =
    [
        "ai_network",
        "ai_hint",
        "hl2mp_gamerules",
        "team_manager",
        "player_manager",
        "env_soundscape",
        "env_soundscape_proxy",
        "env_soundscape_triggerable",
        "env_sun",
        "env_wind",
        "env_fog_controller",
        "func_brush",
        "func_wall",
        "func_buyzone",
        "func_illusionary",
        "infodecal",
        "info_projecteddecal",
        "info_node",
        "info_target",
        "info_node_hint",
        "info_player_deathmatch",
        "info_player_combine",
        "info_player_rebel",
        "info_map_parameters",
        "keyframe_rope",
        "move_rope",
        "info_ladder",
        "player",
        "point_viewcontrol",
        "scene_manager",
        "shadow_control",
        "sky_camera",
        "soundent",
        "trigger_soundscape",
        "viewmodel",
        "predicted_viewmodel",
        "worldspawn",
        "point_devshot_camera",
    ];

    /// <summary>
    /// The classes this port gives objects stock's list already keeps, and its
    /// own service-made entities no map entity string holds: the player
    /// (<c>player</c>), its view model (<c>viewmodel</c> /
    /// <c>predicted_viewmodel</c>), and the Pickups service's health kits,
    /// which the service places itself and the map would never bring back.
    /// </summary>
    public static readonly ImmutableArray<string> PortPreserved =
    [
        EntityNames.Player,
        EntityNames.PredictedViewModel,
        EntityNames.SsPickup,
    ];

    private static readonly ImmutableHashSet<string> Preserved =
        ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, StockPreserved.Concat(PortPreserved));

    /// <inheritdoc/>
    public bool RoundCleanupShouldIgnore(ScriptedEntity entity)
    {
        // Weapons with owners don't want to be removed; an ownerless one goes
        // whatever the list says.
        if (entity is SsWeapon weapon)
            return weapon.State.OwnerIndex > 0;

        return Preserved.Contains(entity.ClassName);
    }

    /// <inheritdoc/>
    public bool ShouldCreateEntity(string className) => !Preserved.Contains(className);
}
