//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

using TF2.Shared.Rooms;
using TF2.Shared.Rules;

namespace TF2.Client.Entities;

/// <summary>
/// The respawn-room visualizer's per-entity trace arm on the client,
/// <c>C_FuncRespawnRoomVisualizer::ShouldCollide</c> (c_func_respawnroom.cpp:69-96):
/// the server's <c>TFRespawnRoomCollide</c> answer
/// (<see cref="TFRespawnRoomCollision"/>) over the wall's networked team and the
/// client's round state, so the prediction's entity clip
/// (<see cref="PredictionEntityClip"/>) stops an enemy at the shimmering wall
/// and walks a teammate through it, as the server's trace filter does. Without
/// it the clip would stop every player at his own room's doorway and every
/// predicted walk out of spawn would be corrected.
/// </summary>
[SideService(Side.Client)]
public sealed class TFClientRespawnRoomCollide(IClientStandalones standalones, ITFRoundStateView rounds)
    : IEntityCollideRule, IOrderedHook
{
    /// <summary>Beside the server's arm (<c>TFRespawnRoomCollide.Order</c>, +945): TF2's
    /// orders are unique across both sides, and +946 is free. Registered
    /// under its own type, as that arm is, so the <see cref="IHooks{T}"/> chain of
    /// <see cref="IEntityCollideRule"/> collects it by assignability: a second
    /// registration AS the interface beside TFClientTeamCollide's is refused at
    /// composition ("registered twice"), and the client side does not load.</summary>
    public const int Order = 1_000_000 - 1_000 + 946;

    public int HookOrder => Order;

    public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
        => standalones.GetByIndex(entity.EntryIndex) is not ClientRespawnRoomVisualizer wall || wall.Handle != entity
           || TFRespawnRoomCollision.Collides(rounds.State == RoundState.TeamWin, wall.State.Team, group, contentsMask);
}
