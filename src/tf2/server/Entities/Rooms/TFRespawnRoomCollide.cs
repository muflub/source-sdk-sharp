//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rooms;
using TF2.Shared.Rules;

namespace TF2.Server.Entities.Rooms;

/// <summary>
/// The respawn-room visualizer's per-entity trace arm,
/// <c>CFuncRespawnRoomVisualizer::ShouldCollide</c> (func_respawnroom.cpp:494-523):
/// the shimmering wall over a respawn room's doors stops a player-movement
/// trace whose mask carries the wall's OWN team's contents bit — and the
/// mover's mask (<c>CTFGameMovement::PlayerSolidMask</c>, the
/// <see cref="IMovementRules"/> mask arm) carries only the OTHER team's, so
/// an enemy is blocked and a teammate passes. Nobody is blocked while the
/// round is in its win state (the rooms are open) or the wall has no team
/// yet; a trace in any other collision group — a bullet, a vision ray —
/// passes; and every entity that is not a visualizer is left to the chain's
/// other arms and the group matrix, as stock's hierarchy answers for them.
/// </summary>
[SideService(Side.Server)]
public sealed class TFRespawnRoomCollide(IEntityDirectory entities, ITeamRoundState rounds) : IEntityCollideRule, IOrderedHook
{
    /// <summary>Behind the core's teammates rule (-1000) and TFTeamCollide (0):
    /// registered under its own type, so the <see cref="IHooks{T}"/> chain of
    /// <see cref="IEntityCollideRule"/> collects it by assignability and every
    /// rule's order stays unique on the chain. Slot 940–945 of TF2's band is
    /// free: the medigun band ends at +929, the lone singletons sit at +931
    /// and +981–987.</summary>
    public const int Order = 1_000_000 - 1_000 + 945;

    public int HookOrder => Order;

    public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
    {
        if (entities.Get(entity) is not FuncRespawnRoomVisualizer visualizer)
            return true;

        return TFRespawnRoomCollision.Collides(rounds.State == RoundState.TeamWin, visualizer.Team, group, contentsMask);
    }
}
