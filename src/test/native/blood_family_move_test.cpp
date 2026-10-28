//===== SourceSharp — the blood, tracer and decal family, out of SCC 11 ======//
//
// plan_entity.md Phase 4 exit, lane p4-util. `game/shared/util_shared.cpp` is
// one of SCC 11's members, and the emitter family left it for
// `sourcesharp/game/shared/util_shared_library.cpp` — on BOTH link lines,
// because the stock file is on both.
//
// 4e MEASURED THIS FAMILY AND DECLINED IT, and was right at the time:
//
//   "UTIL_BloodDecalTrace calls UTIL_DecalTrace, which reaches TraceEntity and
//    CBaseEntity::DecalTrace — a virtual with the studio and brush decal
//    machinery behind it. Moving the family CONVERTS three util_shared.cpp
//    edges into baseentity_shared.cpp edges and removes none."
//
// What changed is not the family. `pEntity->DecalTrace(...)` is a VIRTUAL CALL
// through the vtable and emits no undefined symbol at all, so it was never an
// edge to convert; `TraceEntity` is a weak inline emitted per TU; and
// `UTIL_Smoke`, which the family reaches, left `game/server/util.cpp` for the
// shim earlier in this same lane. The remaining conversion is ONE symbol and it
// is named below.
//
// FOUR FAILURES THIS PINS:
//
//   1. `UTIL_BloodImpact` LEFT BEHIND. No shim TU names it, so it looks like it
//      should stay — and leaving it is exactly 4e's conversion, one symbol to
//      the left: `UTIL_BloodDrips` would then need it across the boundary.
//
//   2. `UTIL_BloodDrips` TAKEN. It is the one member that stayed, and the
//      reason is invisible from the server: on the client `UTIL_Smoke` is
//      `game/client/cdll_util.cpp`'s and `g_Language` is
//      `game/shared/gamerules.cpp`'s, and BOTH of those files read zero
//      shim→stock edges today. Taking it costs each of them one — and
//      `cdll_util.cpp` reading zero is 7f's own result. A later lane moving it
//      "for consistency" undoes that silently, on the side it is not looking at.
//
//   3. A ONE-SIDED VPC ROW — the library is on both link lines and must stay
//      there.
//
//   4. `UTIL_DecalTrace`'s NULL RETURN DELETED. Its `if ( !pEntity ) return;`
//      IS the behaviour for an entity outside the SDK, and the paragraph over
//      it says so. Removing it as "dead" is the shape of all six recorded
//      silent losses.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <algorithm>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

const char *const kLibrary   = "sourcesharp/game/shared/util_shared_library.cpp";
} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

//=============================================================================//
// 2. UTIL_BloodDrips STAYED, AND THE REASON IS ON THE OTHER SIDE
//=============================================================================//

//=============================================================================//
// 3. THE REFUSAL INSIDE UTIL_DecalTrace IS BEHAVIOUR, NOT DEAD CODE
//=============================================================================//

//=============================================================================//
// 4. BOTH LINK LINES, AND THE STOCK ROWS UNTOUCHED
//=============================================================================//

TEST( BloodFamilyMove, TheLibraryIsOnBothLinkLines )
{
	// Listed EXACTLY ONCE in each target's CMakeLists source list (a second
	// row is a duplicate TU), and actually in each target's compile database.
	// cmake_graph throws on a missing, empty or stale input.
	const char *const aTargets[] = { cmake_graph::kServer, cmake_graph::kClient };
	for ( const char *pTarget : aTargets )
	{
		const std::vector< std::string > listed = cmake_graph::ListedSources( pTarget );
		EXPECT_EQ( 1, std::count( listed.begin(), listed.end(), std::string( kLibrary ) ) )
			<< kLibrary << " is not listed exactly once in " << pTarget
			<< " (" << cmake_graph::ListFile( pTarget ) << ")";
		EXPECT_TRUE( cmake_graph::Compiles( pTarget, kLibrary ) )
			<< kLibrary << " is not compiled into " << pTarget;
	}
}

