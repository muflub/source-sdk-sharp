//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The save comment, in stock's format with the map name as the title. The engine
// hands GetSaveComment an uninitialised stack buffer, so it must always be filled.
// The save UI parses it by column. Engine-free so it can be unit tested.

#pragma once

#include <stdio.h>

// Writes nothing for a NULL buffer or a non-positive length.
inline void ShellGameDll_FormatSaveComment( char *pOut, int nMaxLength, const char *pMapName, float flMinutes,
											float flSeconds, bool bNoTime )
{
	if ( pOut == NULL || nMaxLength <= 0 )
		return;

	const char *pName = ( pMapName != NULL ) ? pMapName : "";

	if ( bNoTime )
	{
		snprintf( pOut, (size_t)nMaxLength, "%-64.64s", pName );
		pOut[nMaxLength - 1] = '\0';
		return;
	}

	int nMinutes = (int)flMinutes;
	int nSeconds = (int)flSeconds;

	// Stock's clamp. It runs before the seconds roll over, so 999:120 still prints
	// 1001 minutes; kept, as the output must match stock byte for byte.
	if ( nMinutes >= 1000 )
	{
		nMinutes = 999;
		nSeconds = 59;
	}

	const int nMinutesAdd = ( nSeconds / 60 );
	nSeconds %= 60;

	snprintf( pOut, (size_t)nMaxLength, "%-64.64s %03d:%02d", pName, ( nMinutes + nMinutesAdd ), nSeconds );
	pOut[nMaxLength - 1] = '\0';
}
