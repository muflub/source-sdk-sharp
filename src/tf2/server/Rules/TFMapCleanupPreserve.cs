//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared.Player;
using TF2.Shared.Resources;

namespace TF2.Server.Rules;

/// <summary>
/// TF2's share of every round cleanup's preserve list (D10), naming the
/// classes TF2's services make — the player's <c>tf_player_data</c> carrier,
/// the scoreboard, the wearables' carrier, the engineer HUD's building
/// resource, the objective and team resources — none of which any map block
/// holds, so a cleanup that removes one never gets it back.
///
/// <para>
/// A TF2 mode's own cleanup already keeps them (the lists of
/// <see cref="TFMapCleanupFilter"/>, TF's <c>s_PreserveEnts</c> and the port
/// rows beside it); this is the answer for the cleanups that run under some
/// other filter — the mode switch's <c>sv_mode null</c>, whose NullMode
/// carries no filter of its own and so cleans with the core's HL2MP list,
/// which names none of these. Before it named them, that cleanup killed a
/// joined player's carrier and the movement rules read the dead binding
/// (the round gate's mode step).
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IMapCleanupPreserve)])]
public sealed class TFMapCleanupPreserve : IMapCleanupPreserve, IOrderedHook
{
    /// <summary>After the rules' commands and selector, before the next band.</summary>
    public int HookOrder => TeamRoundOrders.MapCleanupPreserve;

    // The service-made classes, as the set the question is asked of. The stock
    // names among them (tf_player_manager, tf_wearable, tf_objective_resource)
    // are the s_PreserveEnts rows this answers for under a foreign filter;
    // tf_player_data, tf_building_resource and tf_team_resource are the port's
    // own service-made entities (see TFMapCleanupFilter.PortPreserved).
    private readonly ImmutableHashSet<string> _classes =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
                                TFEntityNames.PlayerData,
                                TFEntityNames.PlayerResource,
                                TFEntityNames.Wearable,
                                TFEntityNames.BuildingResource,
                                ObjectiveClassNames.ObjectiveResource,
                                ObjectiveClassNames.TeamResource);

    /// <inheritdoc/>
    public bool Preserves(string className) => _classes.Contains(className);
}
