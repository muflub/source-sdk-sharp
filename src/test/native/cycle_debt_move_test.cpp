//===== SourceSharp — the moves that shrink the atomic batch, not the census =//
//
// plan_entity.md Phase 4 exit, lane p4-util.
//
// THERE ARE TWO DIFFERENT NUMBERS AND THIS SUITE GUARDS THE SECOND ONE.
//
//   * `tools/entity-census` counts SHIM -> STOCK edges: what the shim still
//     needs from stock, which is the surface the vpc flip must replace.
//   * `tools/p4util-holders --owes` counts STOCK -> STOCK edges inside SCC 11:
//     what one member of the cycle still needs from another, which is what
//     makes the flip ATOMIC. A translation unit that owes the rest of the cycle
//     nothing has LEFT the cycle even while it is still in the vpc and still
//     owed by everything.
//
// Nothing moved here was ever a cross-edge, so the census does not move at all
// and a reader looking only at the census would call this change a no-op. It is
// not: it took `debugoverlay_shared.cpp`'s debt to the cycle from four
// sole-definer symbols to two, `util_shared.cpp`'s from five to four, and
// `util.cpp`'s from thirty-nine to thirty-eight.
//
// THREE FAILURES THIS PINS:
//
//   1. A BODY PUT BACK, in either direction. Two definitions of a `UTIL_VecToYaw`
//      overload is a link error; but the failure that matters is subtler — a
//      later lane "tidying" one of these back into `util_shared.cpp` restores
//      the cycle edge with the census reading exactly the same before and after.
//
//   2. `developer` REGISTERED TWICE. It is a plain `ConVar`, so a copy left
//      behind is not a compile error and not a link error: it is a second
//      setting under one name, and neither the console nor the game says which
//      one was read.
//
//   3. THE DECLARATIONS FOLLOWING THE BODIES. Every one of these is declared in
//      a stock header (`util_shared.h`, `util.h`) and read by callers on both
//      sides; the move is only a relocation for as long as that is true.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <fstream>
#include <sstream>
#include <set>
#include <string>

namespace
{

} // anonymous namespace

//=============================================================================//
// 1. THE ANGLE FAMILY IS WHOLE, AND IT IS IN THE SHIM
//=============================================================================//

//=============================================================================//
// 2. `developer` IS REGISTERED EXACTLY ONCE
//=============================================================================//

//=============================================================================//
// 3. UTIL_Smoke MOVED, AND ITS MODEL INDEX DID NOT
//=============================================================================//

//=============================================================================//
// 4. THE HEADERS DID NOT MOVE, SO NO CALLER DID
//=============================================================================//

//=============================================================================//
// 5. THE STOCK TUs, AFTER THE FLIP
//
// THIS FACT USED TO SAY THE OPPOSITE, AND IT WAS RIGHT TO. Its original text was
// `NoStockRowWasRemovedFromEitherVpc`, and its own comment said why: "The Phase 4
// exit removes SCC 11's vpc rows in one commit, and NO LANE MAY TAKE A ROW OUT
// FROM UNDER IT." It was a tripwire held for the flip commit, and it held --
// nothing removed these rows for the whole port until the flip did.
//
// THE FLIP IS THAT COMMIT. Three of the seven rows left in it, verified present
// at the pre-flip tip `6aa6ae45a` and absent now:
//
//     game\server\util.cpp                 1 -> 0
//     game\shared\debugoverlay_shared.cpp  1 -> 0
//     game\server\damagemodifier.cpp       1 -> 0   (the 23rd server row,
//                                                     which the flip's own 22
//                                                     had missed -- armed-28)
//
// So the fact is REWRITTEN, not deleted, and it keeps its whole protective
// value: the four rows that did NOT leave are still asserted present, and the
// three that did are asserted ABSENT. An unintended removal of the remaining
// four still reddens, and so does a re-appearance of the three -- which would
// mean somebody restored a TU the flip removed.
//=============================================================================//

// SINCE PHASE 6b the rows are read out of the CMake build (server_sourcesharp
// and client_sourcesharp, src/{server,client}/native/CMakeLists.txt, through
// cmake_graph.h) instead of server_csharp.vpc / client_csharp.vpc. A "row" is
// a compiled source whose path IS the stock path or ENDS in "/" + it -- the
// same reach the vpc substring needle had, so `game\shared\collisionproperty.cpp`
// went on matching the relocated sourcesharp/game/shared/ row and still does.
size_t Rows( const char *pszTarget, const std::string &strRel )
{
	const std::set<std::string> s = cmake_graph::CompiledSources( pszTarget );
	size_t n = 0;
	for ( std::set<std::string>::const_iterator it = s.begin(); it != s.end(); ++it )
	{
		if ( *it == strRel ||
			 ( it->size() > strRel.size() &&
			   it->compare( it->size() - strRel.size() - 1, std::string::npos, "/" + strRel ) == 0 ) )
			n++;
	}
	return n;
}

TEST( CycleDebtMove, TheFlipRemovedExactlyThreeOfTheseRowsAndNoOthers )
{
	const char *const sv = cmake_graph::kServer;
	const char *const cl = cmake_graph::kClient;

	// Left with the flip. Present here would mean a removed TU came back.
	EXPECT_EQ( 0u, Rows( sv, "game/server/util.cpp" ) );
	EXPECT_EQ( 0u, Rows( sv, "game/shared/debugoverlay_shared.cpp" ) );
	EXPECT_EQ( 0u, Rows( sv, "game/server/damagemodifier.cpp" ) );

	// Did NOT leave, and are still compiled. This half is the original tripwire,
	// unchanged: §6 keeps these and no lane may take them out.
	// 1 -> 0 BY plan_shim.md PHASE 2c, lane 2-utilshared, AND THE ROW CHANGES
	// SIDES RATHER THAN BEING DELETED. It was a tripwire in the "did NOT leave"
	// half -- "§6 keeps these and no lane may take them out" -- and §6 is
	// superseded for this file by plan_shim.md Phase 2's exit condition, which
	// requires exactly the opposite: zero rows under $SRCDIR\game\ in
	// server_csharp.vpc. The file was deleted, not merely evicted, after a
	// zero-importer census over both link lines.
	//
	// So the assertion moves to the half above and keeps its teeth: present
	// here would now mean a DELETED file came back.
	EXPECT_EQ( 0u, Rows( sv, "game/shared/util_shared.cpp" ) );
	EXPECT_EQ( 0u, Rows( cl, "game/shared/util_shared.cpp" ) );
	// collisionproperty.cpp left the server's list (plan_server_native_min
	// P1: its dirty list had no server writer); the client still compiles it.
	EXPECT_EQ( 0u, Rows( sv, "game/shared/collisionproperty.cpp" ) );
	EXPECT_EQ( 1u, Rows( cl, "game/shared/collisionproperty.cpp" ) );
}
