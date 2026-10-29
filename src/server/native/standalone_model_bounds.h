//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Whether setting a standalone entity's model may also set its collision bounds.
// A player's bounds are its movement hull, never the model's (the mesh sits below
// the feet). Anything else adopts the model's bounds unless the model has none.

#pragma once

inline bool StandaloneModel_AdoptsBounds( bool bIsPlayer, bool bModelHasBounds )
{
	if ( bIsPlayer )
		return false;

	return bModelHasBounds;
}
