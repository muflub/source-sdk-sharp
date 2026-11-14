//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// The client's half of the held-aside item meter: the local player's
/// carrier rows (<see cref="TFPlayerNetState.ItemMeters"/> and
/// <see cref="TFPlayerNetState.ItemMeterTimes"/>), by weapon-catalog row —
/// the pair stock's client receives on the player
/// (<c>CTFPlayerShared::UpdateItemChargeMeters</c>'s per-slot charge,
/// tf_player_shared.cpp:7870-7904) and its switch's restore advances. A row
/// the player does not carry, or a player whose carrier has not arrived, is
/// unpublished and the arsenal keeps the meter the hands' last deploy left.
/// </summary>
[SideService(Side.Client, As = [typeof(ILocalItemMeters)])]
public sealed class TFClientItemMeters(TFClientCarriers carriers, IEngineHud hud) : ILocalItemMeters
{
    /// <inheritdoc/>
    public bool MeterOf(int typeIndex, out float meter, out float stamp)
    {
        meter = 0f;
        stamp = 0f;

        if (carriers.Of(hud.LocalPlayerIndex)?.State is not { } state
            || state.CarriedSlotOf(typeIndex) is not (>= 0 and var slot))
            return false;

        meter = state.ItemMeters[slot];
        stamp = state.ItemMeterTimes[slot];
        return true;
    }
}
