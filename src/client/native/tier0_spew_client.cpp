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
// shells call it in client.so; no server object imports it.
//=============================================================================//

#include "tier0/dbg.h"
#include "tier0_spew_port.h"

void DevWarning( int level, const tchar *pMsg, ... )
{
	va_list args;
	va_start( args, pMsg );
	Tier0_DevSpewV( level, SPEW_WARNING, pMsg, args );
	va_end( args );
}
