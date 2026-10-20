//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the tier0 entry points only client.so needs ========//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// tier0_spew_port.cpp (src/shared/native) holds what both DLLs call through
// public/tier0/dbg.h. What only the client calls lives here (user ruling
// 2026-10-12: what the server does not need is client-only).
//
// DevWarning( level ): bone_setup.cpp, shell_clientdll.cpp and the temp-entity
// shells call it in client.so; no server object imports it. DevMsg( level )
// and MemAllocOOMError joined it at plan_server_native_v2 T5, when the
// server's last importers left (nm U over its link edge).
//=============================================================================//

#include "tier0/dbg.h"
#include "tier0/mem.h"         // MemAllocOOMError
#include "tier0_spew_port.h"
#include "ss_platform.h"	// SS_BIND_IMPORT
#ifdef _MSC_VER
// This file defines functions the engine DLLs' headers declare dllimport
// (C4273); SS_BIND_IMPORT at the end binds this module's calls to them.
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

//-----------------------------------------------------------------------------
// MemAllocOOMError: tier0/memalloc.h's POSIX allocation inlines (the
// NO_MALLOC_OVERRIDE arm, plan_server_novalve.md P5 step 5) call it when malloc
// or memalign answers NULL. tier0's ends the process; so does this, through
// Error and the fatal report.
//-----------------------------------------------------------------------------
void MemAllocOOMError( size_t nSize )
{
	Error( "out of memory: an allocation of %zu bytes failed\n", nSize );
}

// Every call in this module of the functions above, made through the
// import pointer, reaches these definitions (ss_platform.h).
SS_BIND_IMPORT_AS( DevWarning, void ( * )( int, const tchar *, ... ) )
SS_BIND_IMPORT_AS( DevMsg, void ( * )( int, const tchar *, ... ) )
SS_BIND_IMPORT( MemAllocOOMError )
