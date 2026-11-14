//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

/// <summary>
/// TF2's per-entity trace arm on the server, <c>CTFPlayer::ShouldCollide</c>
/// (tf_player.cpp:10159-10178), answered by <see cref="TFTeamCollision"/>
/// over <see cref="ITeamRoundRoster.TeamOf"/>, the real team: a disguised spy
/// keeps colliding as its colours' team. The mover is not asked: stock's
/// answer is the mask's (the <see cref="IMovementRules"/> mask arm carries
/// the mover's other team's bit).
///
/// <para>
/// The client's half is <c>TFClientTeamCollide</c>, the same answer over the
/// player resource's team, asked by the prediction's player clip.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IEntityCollideRule)])]
public sealed class TFTeamCollide(TFAvoidTeammatesConVar avoid, ITeamRoundRoster roster) : IEntityCollideRule
{
    public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
        => group != TFTeamCollision.PlayerMovement || !avoid.Value
           || TFTeamCollision.Collides(group, true, roster.TeamOf(entity), contentsMask);
}
