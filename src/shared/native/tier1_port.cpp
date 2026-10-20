//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — tier1's small members, as both DLLs use them =========//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// plan_server_novalve.md P3. Neither DLL links tier1. Each member here is one
// a header on the allowlist declares and code in both DLLs (or inline code in
// that header) calls, answered with tier1's documented behaviour. The
// strtools.cpp members only client.so imports are strtools_client.cpp's
// (plan_server_native_v2 T5: a shared TU keeps only what both DLLs import).
//
//=============================================================================//

#include "tier1/bitbuf.h"

//-----------------------------------------------------------------------------
// bitbuf.h's inline Seek (and the reads) flag a read past the end through this.
// A buffer that asked to assert on overflow does; release builds only set it.
//-----------------------------------------------------------------------------
void bf_read::SetOverflowFlag()
{
	Assert( !m_bAssertOnOverflow );
	m_bOverflow = true;
}
