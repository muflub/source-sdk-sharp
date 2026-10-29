//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md

#include "vstdlib/random.h"

#ifdef GNUC
#define __cdecl
#endif

void __cdecl srand( unsigned int ) {}

int __cdecl rand()
{
	return RandomInt( 0, VALVE_RAND_MAX );
}
