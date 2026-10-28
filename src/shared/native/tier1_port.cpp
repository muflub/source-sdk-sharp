//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
// tier1 members both DLLs call, for DLLs that link no tier1.

#include "tier1/bitbuf.h"

// Flags a read past the end; asserts first when the buffer asked to.
void bf_read::SetOverflowFlag()
{
	Assert( !m_bAssertOnOverflow );
	m_bOverflow = true;
}
