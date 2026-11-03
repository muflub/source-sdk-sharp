//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// The class game's veto on the spawn kit (<see cref="IKitGate"/>): stock's
/// <c>CTFPlayer::Spawn</c> hands out items only in the active state — the
/// kit arm runs <c>if ( m_Shared.InState( TF_STATE_ACTIVE ) )</c>
/// (tf_player.cpp:3618) and reaches <c>GiveDefaultItems()</c> through
/// <c>InitClass()</c> (:4157) — so a player with no class, the fresh spawn
/// in <c>InitialSpawn</c>'s <c>TF_STATE_WELCOME</c> (:3308) or the one on a
/// team who has yet to choose (:6556), never sees a weapon. They are the
/// observer <see cref="TFPlayerSpawns"/> settles into the chase cam for,
/// and the stock observer carries nothing.
///
/// <para>
/// The veto asks the same question as <c>TFPlayerSpawns.OnSpawned</c>'s
/// observer arm — the class on the carrier — so the player the hooks
/// declare an observer is exactly the player left weaponless, and the core
/// never decides who plays and who watches: in TF2 that is the class game's
/// call, taken over the core's gate.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFKitGate(TFPlayerCarriers carriers) : IKitGate, IOrderedHook
{
    /// <summary>The gate's order in the player band.</summary>
    public const int Order = TFPlayerOrders.KitGate;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public bool ShouldGiveKit(EntityHandle player)
        => carriers.ClassOf(player) is > TFClass.Undefined and < TFClass.CountAll;
}
