//========= SourceSharp — server.so's R5 include set, both halves ===========//
// plan_server_novalve.md 2 (R5) and 3.4. A server TU that has moved off the
// legacy include set sees only the allowlisted fork directories. This file is
// compiled against exactly that set (the SS_SERVER_NOVALVE_INCLUDES property
// src/server/native/CMakeLists.txt publishes):
//
//   as a gtest            the allowlisted engine interface headers compile
//   NOVALVE_CANARY=1      cbase.h is not reachable (novalve_include_set_canary,
//                         a must-not-compile check expecting "file not found")
//
// The pair is the proof: the canary failing alone could be a broken set that
// finds nothing; the control compiling alone could be a set that finds all.
//=============================================================================//

#if NOVALVE_CANARY
#include "cbase.h"
#else
#include "tier0/platform.h"
#include "eiface.h"
#include "edict.h"

#include <gtest/gtest.h>

TEST( NovalveIncludeSet, TheEngineInterfaceHeadersCompileOnTheR5Set )
{
	EXPECT_GT( sizeof( edict_t ), 0u );
	EXPECT_GT( sizeof( CGlobalVars ), 0u );
}
#endif
