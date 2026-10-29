//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The tier0 entry points only client.so calls.
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md

#include "tier0/dbg.h"
#include "tier0/platform.h"
#include "tier0/wchartypes.h"
#include "tier0/mem.h"
#include "tier0_spew_port.h"
#include "ss_platform.h"
#ifdef _MSC_VER
// Defines functions the engine headers declare dllimport (C4273).
#pragma warning( disable : 4273 )
#endif

void DevWarning( int level, const tchar *pMsg, ... )
{
	va_list args;
	va_start( args, pMsg );
	Tier0_DevSpewV( level, SPEW_WARNING, pMsg, args );
	va_end( args );
}

void DevMsg( int level, const tchar *pMsg, ... )
{
	va_list args;
	va_start( args, pMsg );
	Tier0_DevSpewV( level, SPEW_MESSAGE, pMsg, args );
	va_end( args );
}

// tier0/memalloc.h's allocation inlines call this when malloc or memalign returns NULL.
void MemAllocOOMError( size_t nSize )
{
	Error( "out of memory: an allocation of %zu bytes failed\n", nSize );
}

// Binds this module's calls through the import pointers to the definitions above.
SS_BIND_IMPORT_AS( DevWarning, void ( * )( int, const tchar *, ... ) )
SS_BIND_IMPORT_AS( DevMsg, void ( * )( int, const tchar *, ... ) )
SS_BIND_IMPORT( MemAllocOOMError )
