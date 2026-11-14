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

namespace TF2.Client.Player;

/// <summary>
/// TF2's per-entity trace arm on the client, <c>C_TFPlayer::ShouldCollide</c>
/// (c_tf_player.cpp:7705-7724): the server's <c>TFTeamCollide</c> answer
/// (<see cref="TFTeamCollision"/>) over the player resource's team
/// (<see cref="ITFPlayerTeam"/>, the read the movement's mask arm makes), so
/// the prediction's player clip passes a teammate while
/// <c>tf_avoidteammates</c> stands and stops at an enemy, as the server's
/// trace filter does. Without it the clip would block every teammate and
/// every predicted walk through one would be corrected.
/// </summary>
[SideService(Side.Client, As = [typeof(IEntityCollideRule)])]
public sealed class TFClientTeamCollide(TFAvoidTeammatesConVar avoid, ITFPlayerTeam teams) : IEntityCollideRule
{
    public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
        => group != TFTeamCollision.PlayerMovement || !avoid.Value
           || TFTeamCollision.Collides(group, true, teams.Of(entity.EntryIndex), contentsMask);
}
