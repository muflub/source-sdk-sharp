//====== SourceSharp — the two "C" systems become managed =====================//
//
// plan_gamedll_after.md Phase A3, the CPropData / CDecalEmitterSystem pair.
//
// SOURCE-SCANNED, AND THAT IS FORCED RATHER THAN CHOSEN. Every subject here
// lives in a translation unit that includes cbase.h's whole world -- the shim
// behind `decalsystem`, the two native steps, props_shared.cpp, the table -- so
// none of them can be linked into a gtest. What CAN be checked from here is the
// set of properties a compiler cannot see and a `nm` count cannot say WHY
// about, which is the same seam managed_runtime_server_test.cpp and
// stringtable_index0_test.cpp already work through.
//
// THE FAILURE THIS FILE EXISTS FOR IS SILENT, twice over:
//
//   * a row that leaves kServerSystems[] and gains no managed participant does
//     not warn, does not fail to link and does not run. The decal table would
//     be empty and every lookup would answer -1 -- which is exactly what a
//     WORKING decal system answers for a decal that is genuinely absent.
//
//   * decals.cpp leaving the server build is T1's hazard in its exact shape.
//     The client compiles the same file, and the reason the removal is safe is
//     that the client's copy is untouched -- which is a property of a file the
//     server no longer builds, so no server-side gate can see it.
//
//=============================================================================//

#include <fstream>
#include <string>
#include <vector>
#include <sys/stat.h>

#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

// Whether a target compiles a farm-relative path. EXACT path, not a
// substring: a bare "decals.cpp" also matches shell_decals.cpp, and this tree
// has already been bitten by exactly that shape -- gib_verdict_test's anchor
// on "class CRagdollProp" was satisfied for its whole life by
// CRagdollPropAttached, because the name is a prefix.
int NamedInBuild( const char *pszTarget, const char *pszSrcRelative )
{
	return cmake_graph::Compiles( pszTarget, pszSrcRelative ) ? 1 : 0;
}

} // namespace

//=============================================================================//
// 1. THE ROWS LEFT THE TABLE, AND THE TABLE SAYS WHY
//=============================================================================//

//=============================================================================//
// 2. THE PRE-ENTITY HALF HAS A CALLER NOW, AND HAD NONE BEFORE
//=============================================================================//

//=============================================================================//
// 3. decals.cpp LEFT THE SERVER'S PROJECT, AND THE CLIENT STILL COMPILES IT
//=============================================================================//

TEST( CSystemsManaged, TheDecalScriptIsManagedOnBothSides )
{
	const char *const server = cmake_graph::kServer;
	const char *const client = cmake_graph::kClient;

	EXPECT_EQ( 0, NamedInBuild( server, "game/shared/decals.cpp" ) )
		<< "the server's decal emitter is managed; decals.cpp is not in its build";
	// plan_client_novalve C2 (lane c2-sims): the client's tables are managed too
	// (ClientDecals over the shared DecalScript), so neither build names it, and
	// the client's decalsystem is its binding.
	EXPECT_EQ( 0, NamedInBuild( client, "game/shared/decals.cpp" ) )
		<< "the client's decal tables are managed; decals.cpp is not in its build";
	EXPECT_EQ( 1, NamedInBuild( client, "sourcesharp/game/client/shell_decals_client.cpp" ) )
		<< "the client's decalsystem binding is in its build";

	// VACUITY GUARD, in both directions: a NamedInBuild that matched nothing would
	// make the first expectation pass on any file at all.
	//
	// THE SUBJECT MOVED. It was props_shared.cpp, which plan_shared.md Phase 5
	// took out of both projects -- so the guard would have started reading 0/0
	// and agreeing with the very failure it exists to catch. util_shared.cpp is
	// its replacement and is a deliberate choice: plan_shared.md §3d holds it
	// as the widest fan-in in the shared set, 24 server and 39 client
	// consumers, and its exit is "Phase-H demolition, last of the utility
	// layer". A guard needs a subject that outlives the thing it is guarding.
	// THE SUBJECT MOVED AGAIN, plan_shim.md Phase 2c, lane 2-utilshared, and for
	// the third time for the same reason the note above gives: "a guard needs a
	// subject that outlives the thing it is guarding." util_shared.cpp was
	// deleted -- Phase 2's exit condition is zero rows under $SRCDIR\game\ in
	// server_csharp.vpc, so every remaining stock row in this vpc is a subject
	// with a scheduled death and none of them can hold this guard.
	//
	// sourcesharp/game/shared/util_shared_library.cpp is the file that took
	// util_shared.cpp's residue. It is in BOTH builds, it is a shim file rather
	// than a stock one, and no phase of this plan removes it -- which is what a
	// vacuity guard needs.
	EXPECT_EQ( 1, NamedInBuild( server, "sourcesharp/game/shared/util_shared_library.cpp" ) );
	EXPECT_EQ( 1, NamedInBuild( client, "sourcesharp/game/shared/util_shared_library.cpp" ) );

	// AND THE OLD SUBJECT'S ABSENCE, asserted rather than assumed: the guard
	// above proves NamedInBuild matches something, and this proves the file this
	// test used to read really went.
	EXPECT_EQ( 0, NamedInBuild( server, "game/shared/util_shared.cpp" ) );
	EXPECT_EQ( 0, NamedInBuild( client, "game/shared/util_shared.cpp" ) );
}

//=============================================================================//
// 4. THE SHIM: STOCK'S INTERFACE, ANSWERED FROM MANAGED CODE
//=============================================================================//

//=============================================================================//
// 5. CPropData: THE HALF THAT MOVED, AND THE HALF THAT DID NOT
//=============================================================================//

//=============================================================================//
// 6. THE FILE READ LEFT NATIVE CODE (2026-10-19): the user exposed file handles
// to managed, and the read is Open/Size/Read/Close through IBaseFileSystem's
// forwarders (EngineBaseFileSystem.ReadAll). Its three rules -- no truncation,
// a missing file is not an empty one, the GAME path id -- are GameFileSideTests'.
//=============================================================================//
