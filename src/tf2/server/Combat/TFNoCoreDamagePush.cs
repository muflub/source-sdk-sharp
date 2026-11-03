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
/// A TF2 player's hit does not reach the core's shove: <c>CTFPlayer::OnTakeDamage_Alive</c>
/// (tf_player.cpp) pushes through <c>ApplyPushFromDamage</c>, which this mod's blast applier
/// ports, and never calls CBasePlayer's.
/// </summary>
[SideService(Side.Server, As = [typeof(IPlayerDamagePushRule)])]
public sealed class TFNoCoreDamagePush : IPlayerDamagePushRule, IOrderedHook
{
    /// <summary>A stage of its own in TF2's thousand (see <see cref="TFSpawnSelector.Order"/>).</summary>
    public const int Order = 1_000_000 - 1_000 + 33;

    public int HookOrder => Order;

    public bool PushesFromDamage(EntityHandle player) => false;
}
