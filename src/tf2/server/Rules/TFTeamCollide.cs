//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Rules;

/// <summary>
/// TF2's per-entity trace arm, <c>CTFPlayer::ShouldCollide</c>
/// (tf_player.cpp:10159-10178): in a player-movement trace, while
/// <c>tf_avoidteammates</c> stands, a player is hit only by a mask carrying
/// ITS OWN team's contents bit — and the mover's mask
/// (<c>CTFGameMovement::PlayerSolidMask</c>, the <see cref="IMovementRules"/>
/// mask arm) carries only the OTHER team's, so a teammate is refused and an
/// enemy is not. The team is <see cref="ITeamRoundRoster.TeamOf"/>, the real
/// one: a disguised spy keeps colliding as its colours' team.
///
/// <para>
/// Servers only. The client's movement traces are the native
/// <c>CTraceFilterSimple</c>'s, whose answer never reaches this chain — the
/// client's pass-through is the mask arm alone, which is also why the two
/// must agree bit for bit.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IEntityCollideRule)])]
public sealed class TFTeamCollide(TFAvoidTeammatesConVar avoid, ITeamRoundRoster roster) : IEntityCollideRule
{
    /// <summary>The trace group the arm narrows: stock's <c>COLLISION_GROUP_PLAYER_MOVEMENT</c>.</summary>
    private const CollisionGroup PlayerMovement = (CollisionGroup)EngineCollisionGroup.PlayerMovement;

    /// <summary>
    /// Stock's switch, its fall-through included: a spectator, an unassigned
    /// entity and every non-player pass untouched, for the chain's other arms
    /// and the group matrix to answer as they did.
    /// </summary>
    public bool ShouldCollide(EntityHandle entity, CollisionGroup group, int contentsMask)
    {
        if (group != PlayerMovement || !avoid.Value)
            return true;

        return roster.TeamOf(entity) switch
        {
            TFTeam.Red => (contentsMask & TFContents.RedTeam) != 0,
            TFTeam.Blue => (contentsMask & TFContents.BlueTeam) != 0,
            _ => true,
        };
    }
}
