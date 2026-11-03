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
/// The client's half of the carried clip: the local player's carrier rows
/// (<see cref="TFPlayerNetState.Clips"/>), by weapon-catalog row — the word
/// stock's client receives on each weapon entity, held or holstered
/// (<c>basecombatweapon_shared.cpp</c>'s send table), and its switch deploys
/// with. A row past the carrier's width, or a player whose carrier has not
/// arrived, is unpublished and the arsenal keeps its own answer.
/// </summary>
[SideService(Side.Client, As = [typeof(ILocalWeaponClips)])]
public sealed class TFClientWeaponClips(TFClientCarriers carriers, IEngineHud hud) : ILocalWeaponClips
{
    /// <inheritdoc/>
    public bool ClipOf(int typeIndex, out int clip)
    {
        clip = -1;
        if (typeIndex < 0
            || carriers.Of(hud.LocalPlayerIndex)?.State is not { } state
            || typeIndex >= state.Clips.Length)
            return false;

        clip = state.Clips[typeIndex];
        return true;
    }
}
