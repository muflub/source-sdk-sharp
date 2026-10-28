//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Which MOVETYPE_* a standalone entity reports across the ABI: a player its own,
// an entity with a physics object the movetype managed wrote into its engine
// block, anything else NONE.

#pragma once

#include "sourcesharp.h"

// nPlayerMoveType is read only for a player, nHeldMoveType only with a physics
// object. MOVETYPE_* and SOURCESHARP_MOVETYPE_* share their numbers.
inline int32_t StandaloneMoveType_Report( bool bIsPlayer, int nPlayerMoveType, bool bHasPhysics,
                                          int nHeldMoveType )
{
	if ( bIsPlayer )
		return (int32_t)nPlayerMoveType;

	if ( bHasPhysics )
		return (int32_t)nHeldMoveType;

	return SOURCESHARP_MOVETYPE_NONE;
}
