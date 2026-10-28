//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// What SOURCESHARP_TRACE_FILTER_SKIP_PLAYER_AND_VIEW_MODEL lets a trace hit:
// flashlighteffect.cpp's CTraceFilterSkipPlayerAndViewModel::ShouldHitEntity,
// over the three facts it asks of an entity. Engine-free, so the flashlight
// oracle (flashlight_oracle_test) holds it to the pristine filter class for
// every combination, and shell_worldquery_client.cpp asks it of a client
// shell. A thing that is no entity never reaches it: stock hits those.
//
//=============================================================================

#pragma once

#include "const.h"

inline bool SkipPlayerAndViewModelHits( bool bPlayer, bool bViewModel, int nCollisionGroup )
{
	if ( bPlayer || bViewModel )
		return false;
	return nCollisionGroup != COLLISION_GROUP_DEBRIS && nCollisionGroup != COLLISION_GROUP_INTERACTIVE_DEBRIS;
}
