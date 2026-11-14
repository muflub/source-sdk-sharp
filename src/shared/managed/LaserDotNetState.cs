//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What the RPG's laser dot (env_laserdot, weapon_rpg.cpp's CLaserDot) tells
/// the client. Stock's DT_LaserDot is empty: the client draws from the base
/// entity's origin and its m_hOwnerEntity, and the owner is what this carries,
/// as an entindex (players are 1..maxplayers on both sides). Beside the owner
/// rides its team, the way the sniper dot's does
/// (<c>TF2.Shared.Weapons.SniperDotNetState</c>): a TF2 holder's sprite
/// is the team's own (<c>tf_weapon_laser_pointer.cpp:41-42</c>), and the
/// client picks it off this byte; an RPG holder leaves it 0 and keeps the
/// plain sprite of that side's stock.
/// </summary>
[StandaloneNetState(EntityNames.EnvLaserdot)]
public partial class LaserDotNetState
{
    /// <summary>Entindex of the player whose RPG paints this dot; 0 for nobody.</summary>
    public partial int OwnerIndex { get; set; }

    /// <summary>
    /// The owner's team, a <c>TFTeam</c>: which team sprite draws. 0 (the
    /// RPG's own arm, which never sets it) keeps the plain sprite.
    /// </summary>
    [NetField(Bits = 2)] public partial byte Team { get; set; }
}
