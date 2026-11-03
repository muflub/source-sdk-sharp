//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Entities;

/// <summary>
/// TF2's players do not kill for a spawn: <c>CTFPlayer::EntSelectSpawnPoint</c>
/// (tf_player.cpp) overrides the walk that does, and its third pass ignores who is
/// standing there. <see cref="TFSpawnSelector"/> answers nothing for a spectator,
/// who then reaches the core's walk, which must not kill on stock's HL2MP rule.
/// </summary>
[SideService(Side.Server, As = [typeof(ISpawnFragRule)])]
public sealed class TFSpawnNoFrag : ISpawnFragRule, IOrderedHook
{
    /// <summary>A stage of its own in TF2's thousand (see <see cref="TFSpawnSelector.Order"/>).</summary>
    public const int Order = 1_000_000 - 1_000 + 32;

    public int HookOrder => Order;

    public bool Frags(EntityHandle spawning) => false;
}
