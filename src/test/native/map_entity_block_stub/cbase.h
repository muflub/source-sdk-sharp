//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ==================================================//
//
// A `cbase.h` FOR ONE TRANSLATION UNIT, so that map_entity_block_test can
// compile the REAL game/shared/mapentities_shared.cpp.
//
// Same shape and same reason as sourcesharp/test/entitylist_stub/cbase.h:
// mapentities_shared.cpp opens with `#include "cbase.h"`, which in the game
// build is the whole SDK prelude. The native test legs are compiled with plain
// g++ and have no engine in them -- and neither has the subject. What is under
// test is a tokeniser over a byte buffer.
//
// Measured, not guessed: with Assert compiled out by -DNDEBUG, the object
// file's undefined symbols are `Warning` and the two Q_ string functions the
// parser calls, all of which the test defines. Nothing else in the prelude is
// referenced.
//
// This shim is on the include path of exactly ONE build_and_run line in
// src/buildcpptests. It is not reachable from the game build, whose include
// paths come from the .vpc files and do not name this directory.
//
//=============================================================================

#ifndef SOURCESHARP_TEST_MAPENTITYBLOCK_CBASE_H
#define SOURCESHARP_TEST_MAPENTITYBLOCK_CBASE_H

#include "tier0/platform.h"
#include "tier0/basetypes.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"

// game/server/util.h:193 and game/client/cdll_util.h:164, verbatim and
// identical in both. It is in a prelude header rather than a library, and
// MapEntity_SkipToNextEntity is the one function in this file that uses it.
inline bool FStrEq( const char *sz1, const char *sz2 )
{
	return ( sz1 == sz2 || V_stricmp( sz1, sz2 ) == 0 );
}

#endif // SOURCESHARP_TEST_MAPENTITYBLOCK_CBASE_H
