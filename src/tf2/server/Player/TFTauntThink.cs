//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// The taunt half of the player think (tf_player.cpp:1485-1507): while
/// <c>TF_COND_TAUNTING</c> stands, <c>ShouldStopTaunting</c> is asked and a
/// yes is <c>CancelTaunt</c>. This build's stop is the water half of it
/// (tf_player_shared.cpp:13140-13144): past the waist, the taunt ends — the
/// condition down, as the shared condition think takes
/// <c>m_bAllowedToRemoveTaunt</c> stock's stop latches (:18136) and removes
/// on its next pass (:2761-2766). The CYOA half of
/// <c>ShouldStopTaunting</c> has no PDA in this build, and the
/// lost-ground/moved arms of the same block ride
/// <c>docs/plans/tf2/deferred.tsv</c>; the pose fields <c>StopTaunt</c>
/// clears (:18140-18174) belong to the prop and long taunts this build has
/// not. The command's own window stands through a stop, as stock's
/// <c>m_flTauntNextStartTime</c> is not reset by it.
/// </summary>
public sealed class TFTauntThink(TFPlayerConditions conditions, IEntityDirectory directory) : IPlayerComponent
{
    /// <summary>
    /// After the blast jump's (25): the taunt arm follows the landing arm in
    /// stock's think (tf_player.cpp:1485, after :1444-1483).
    /// </summary>
    public int Order => 26;

    private EntityHandle _player = EntityHandle.None;

    public void Attach(EntityHandle player) => _player = player;

    /// <summary>
    /// The stop, on the tick after the condition countdown's
    /// (<see cref="TFPlayerConditionTicker"/>) own.
    /// </summary>
    public void Tick(float now)
    {
        if (_player == EntityHandle.None
            || !conditions.InCond(_player, TFCond.Taunting)
            || WaterLevel() <= GameMovement.WlWaist)
            return;

        conditions.Remove(_player, TFCond.Taunting); // :1503-1506
    }

    public void Detach() => _player = EntityHandle.None;

    /// <summary><c>GetWaterLevel</c>: the player entity's movement state's, 0 for a player with none.</summary>
    private int WaterLevel() => directory.Get(_player) is SsPlayer player ? player.State.WaterLevel : 0;
}
