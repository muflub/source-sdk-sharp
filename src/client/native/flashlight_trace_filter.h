//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// CTraceFilterSkipPlayerAndViewModel::ShouldHitEntity over the three facts it asks of an entity.

#pragma once

inline bool SkipPlayerAndViewModelHits( bool bPlayer, bool bViewModel, int nCollisionGroup )
{
	if ( bPlayer || bViewModel )
		return false;
	return nCollisionGroup != COLLISION_GROUP_DEBRIS && nCollisionGroup != COLLISION_GROUP_INTERACTIVE_DEBRIS;
}
