//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Which slot a client-side recipient filter holds, decided from the engine's local player
// index rather than a C_BasePlayer. An empty filter makes the temp-entity dispatchers drop
// the effect, so this must hold the viewer.
#pragma once

// nLocalSlot is the engine's GetLocalPlayer(), zero or negative before there is a viewer;
// a filter holds nobody then.
inline bool LocalRecipient_ShouldHold( int nSlot, int nLocalSlot )
{
	if ( nSlot < 1 || nLocalSlot < 1 )
		return false;

	return nSlot == nLocalSlot;
}
