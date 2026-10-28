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
/// as an entindex (players are 1..maxplayers on both sides).
/// </summary>
[StandaloneNetState(EntityNames.EnvLaserdot)]
public partial class LaserDotNetState
{
    /// <summary>Entindex of the player whose RPG paints this dot; 0 for nobody.</summary>
    public partial int OwnerIndex { get; set; }
}
