//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// The client's half of the published pool: the local player's carrier rows
/// (<see cref="TFPlayerNetState.Ammo"/>), by <see cref="TFAmmo"/> — the word
/// stock's client receives on the player entity (c_baseplayer.cpp:228) and
/// its switch spends (<c>CBaseCombatWeapon::GetPrimaryAmmoType</c>'s row).
/// A row that is not TF's, or a player whose carrier has not arrived, is
/// unpublished and the arsenal keeps its own answer.
/// </summary>
[SideService(Side.Client, As = [typeof(ILocalAmmoPools)])]
public sealed class TFClientAmmoPools(TFClientCarriers carriers, IEngineHud hud) : ILocalAmmoPools
{
    /// <inheritdoc/>
    public bool PoolOf(string ammoName, out int count)
    {
        count = 0;
        if (TFAmmoTypes.Of(ammoName) is not { } ammo
            || carriers.Of(hud.LocalPlayerIndex)?.State is not { } state)
            return false;

        count = state.Ammo[(int)ammo];
        return true;
    }
}
