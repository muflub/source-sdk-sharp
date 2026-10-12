//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Takes everything off whoever set it off.
///
/// The opposite number of game_player_equip, and the one place in the mod that
/// destroys a player's inventory rather than moving it: dropping a weapon leaves
/// it in the world for somebody else, and a map that strips a player before a
/// scripted sequence means the gear to be GONE.
///
/// <para>
/// Which is why it is not built out of point_teleport's neighbour
/// PlayerDropActiveWeapon: that would drop one weapon, visibly, at the player's
/// feet, and leave the rest of the inventory alone. The ABI call underneath is
/// the mirror of GivePlayerItem instead.
/// </para>
///
/// <para>
/// The suit is the second input rather than a keyvalue, because in stock it is
/// the caller who decides: the same entity can be wired to strip weapons for one
/// event and weapons-and-suit for another.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PlayerWeaponstrip)]
public sealed class PlayerWeaponStrip : ScriptedEntity
{
    public PlayerWeaponStrip(IEnginePlayers players)
    {
        Players = players;
    }

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    [ScriptedEvent("Strip")]
    private void OnStrip(InputData input) => Strip(input.Activator, stripSuit: false);

    [ScriptedEvent("StripWeaponsAndSuit")]
    private void OnStripWeaponsAndSuit(InputData input) => Strip(input.Activator, stripSuit: true);

    /// <summary>
    /// Only the activator, which is stock's whole rule here in a multiplayer
    /// build.
    ///
    /// Stock has a second branch reaching UTIL_GetLocalPlayer when there is no
    /// player activator, guarded by <c>!IsDeathmatch()</c> — and
    /// CMultiplayRules::IsDeathmatch returns true unconditionally, so on this
    /// build's rules the branch is unreachable. Silence for a map that fires
    /// this from a timer is therefore what stock does too, not a change.
    /// </summary>
    private void Strip(EntityHandle activator, bool stripSuit)
    {
        if (!Entities.IsPlayer(activator))
            return;

        Players.RemoveAllItems(activator, stripSuit);
    }
}
