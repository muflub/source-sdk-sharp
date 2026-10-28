//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec

#include "shell_standalone.h"

#include "ss_string.h"

bool SourceSharp_IsStandaloneTierServerClass( const ServerClass *pClass );
bool SourceSharp_IsTempEntServerClass( const ServerClass *pClass );
bool SourceSharp_IsWorldServerClass( const ServerClass *pClass );

// Whether a ServerClass was declared by C#, answered by identity against the
// three places SourceSharp constructs one. Anything else counts as stock.
bool SourceSharp_IsManagedServerClass( const ServerClass *pClass )
{
	return SourceSharp_IsStandaloneTierServerClass( pClass ) || SourceSharp_IsTempEntServerClass( pClass ) ||
		SourceSharp_IsWorldServerClass( pClass );
}
