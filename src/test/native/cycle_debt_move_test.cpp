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

std::string ReadFile( const char *pPath )
{
	std::ifstream f( pPath );
	EXPECT_TRUE( f.good() ) << "cannot open " << pPath;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

// WHETHER THE PATH IS THERE AT ALL, which ReadFile above cannot answer: it
// EXPECTs the open, so asking it about a file that is supposed to be gone fails
// on the open instead of reporting the absence. Added at plan_shim.md Phase 2c,
// lane 2-utilshared, where game/shared/util_shared.cpp stopped existing and
// several facts here changed from "this needle is not in that file" to "that
// file is not in the tree" -- the stronger claim, and the one that cannot be
// satisfied vacuously.
bool FileExists( const char *pPath )
{
	std::ifstream f( pPath );
	return f.good();
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos;
	      i = hay.find( needle, i + needle.size() ) )
	{
		n++;
	}
	return n;
}

std::string StripComments( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	for ( size_t i = 0; i < s.size(); )
	{
		if ( s.compare( i, 2, "/*" ) == 0 )
		{
			size_t e = s.find( "*/", i + 2 );
			i = ( e == std::string::npos ) ? s.size() : e + 2;
		}
		else if ( s.compare( i, 2, "//" ) == 0 )
		{
			size_t e = s.find( '\n', i + 2 );
			i = ( e == std::string::npos ) ? s.size() : e;
		}
		else
		{
			out.push_back( s[ i++ ] );
		}
	}
	return out;
}

const char *const kSharedLib = "sourcesharp/game/shared/util_shared_library.cpp";
const char *const kServerLib = "sourcesharp/game/server/server_util_library.cpp";
const char *const kUtilShared = "game/shared/util_shared.cpp";
const char *const kUtilSharedHdr = "game/shared/util_shared.h";

// The five overloads of the vector<->angle family. debugoverlay_shared.cpp
// named two of them; the family moved whole because splitting an overload set
// across two translation units is how one of them silently stops being found by
// overload resolution in a TU that sees only one header.
const char *const kAngleFamily[] = {
	"float UTIL_VecToYaw( const Vector &vec )",
	"float UTIL_VecToPitch( const Vector &vec )",
	"float UTIL_VecToYaw( const matrix3x4_t &matrix, const Vector &vec )",
	"float UTIL_VecToPitch( const matrix3x4_t &matrix, const Vector &vec )",
	"Vector UTIL_YawToVector( float yaw )",
};
const size_t kNumAngle = sizeof( kAngleFamily ) / sizeof( kAngleFamily[ 0 ] );

// The declaration's head rather than its whole text: since P2.3b the fact is
// an ABSENCE, and a reformatted declaration must still count.
const char *const kDeveloperConVar = "ConVar developer(";

} // anonymous namespace

//=============================================================================//
// 1. THE ANGLE FAMILY IS WHOLE, AND IT IS IN THE SHIM
//=============================================================================//

// INVERTED BY plan_shim.md PHASE 2c, lane 2-utilshared, AND THE FAMILY RULE IS
// WHAT SURVIVES THE INVERSION.
//
// This fact was "all five are in the shim, exactly once each", and the reason
// was never the shim: it was WHOLENESS. An overload set split across two
// translation units is how one overload quietly stops being the one chosen, and
// that hazard is the same whichever direction the set moves.
//
// The five are now DELETED, and they went as a set for that reason. They were
// here for CYCLE MEMBERSHIP -- game/shared/debugoverlay_shared.cpp owed SCC 11
// two of them -- and debugoverlay_shared.cpp has a row in neither build, so
// there is no cycle left to be a member of. Measured before the deletion, over
// the VPC-era objects (_vpc_/ninja/debug/objs/{server,client}_sourcesharp):
//
//     nm -C -u <those objects> | grep -c 'UTIL_VecToYaw|UTIL_VecToPitch|UTIL_YawToVector'
//       ->  0 and 0
//
// and the tree links with all five gone, which is the same claim made by the
// linker instead of by a scanner.
//
// So the assertion flips and the FAMILY clause is kept: ALL FIVE are absent, and
// a partial return -- one overload back without its siblings -- reds this just
// as loudly as a partial departure used to.
TEST( CycleDebtMove, TheWholeAngleFamilyLeftTheSharedLibraryTogether )
{
	const std::string lib = StripComments( ReadFile( kSharedLib ) );
	ASSERT_FALSE( lib.empty() ) << kSharedLib << " did not load";

	// The file is proved read above AND proved to be the right file below, so
	// neither half of this can pass over an empty or renamed file.
	EXPECT_EQ( 1u, CountOf( lib, "bool UTIL_ShouldShowBlood( int color )" ) )
		<< kSharedLib << " no longer defines UTIL_ShouldShowBlood, so this "
		   "fact is not reading the file it thinks it is. (This anchor was "
		   "`developer` until plan_server_novalve.md P2.3b deleted that "
		   "declaration: the engine owns the name; then "
		   "UTIL_StringToFloatArray until C9 K3 deleted that, then GetCleanMapName "
		   "until plan_server_native_min P1 did, then UTIL_IsLowViolence until "
		   "plan_server_native_v2 T2 moved it to the client.)";

	for ( size_t i = 0; i < kNumAngle; i++ )
	{
		EXPECT_EQ( 0u, CountOf( lib, kAngleFamily[ i ] ) )
			<< kAngleFamily[ i ] << " is back in " << kSharedLib
			<< " with no compiled caller. If one has reappeared, the family "
			   "comes back WHOLE -- an overload set split across two "
			   "translation units is how one overload quietly stops being the "
			   "one chosen, and that is why all five left together.";
	}
}

// RE-SHAPED BY plan_shim.md Phase 2c, lane 2-utilshared: kUtilShared does not
// exist. An absence over a missing file is vacuous, so the absence is the
// FILE's -- and the file coming back is exactly the failure this fact watched
// for, because that is where the family was.
TEST( CycleDebtMove, NoAngleOverloadWasLeftBehindInUtilShared )
{
	EXPECT_FALSE( FileExists( kUtilShared ) )
		<< kUtilShared << " exists again, and it is where the vector<->angle "
		<< "family was. A second definition there restores "
		<< "debugoverlay_shared.cpp's edge into SCC 11 with the census reading "
		<< "identically before and after.";

	// The vacuity guard: the file this fact is really about must read.
	const std::string lib = StripComments( ReadFile( kSharedLib ) );
	ASSERT_FALSE( lib.empty() ) << kSharedLib << " did not load";
}

//=============================================================================//
// 2. `developer` IS REGISTERED EXACTLY ONCE
//=============================================================================//

// plan_server_novalve.md P2.3b: ONCE, AND BY THE ENGINE. engine.so declares
// `developer` and registers it before either game DLL loads, so the shared
// library's copy was a duplicate of a name the engine already owned; no object
// on either link line imported the symbol. It was deleted, and this fact now
// requires that it stays gone from the file that held it.
TEST( CycleDebtMove, TheDeveloperConVarIsTheEnginesAndNotTheSharedLibrarys )
{
	const std::string lib = StripComments( ReadFile( kSharedLib ) );
	ASSERT_FALSE( lib.empty() );
	ASSERT_EQ( 1u, CountOf( lib, "bool UTIL_ShouldShowBlood( int color )" ) )
		<< kSharedLib << " is not the file this fact means to read";

	EXPECT_EQ( 0u, CountOf( lib, kDeveloperConVar ) )
		<< kSharedLib << " declares `developer` again; the engine owns it";
	// kUtilShared is gone (Phase 2c, lane 2-utilshared). The absence is the
	// file's, and it is worth keeping: that file is where `developer` was, so
	// its return is where a second registration would come from. A second
	// setting under one name is neither a compile error nor a link error, and
	// nothing reports which one a reader got.
	EXPECT_FALSE( FileExists( kUtilShared ) )
		<< kUtilShared << " exists again and could declare `developer` a second "
		<< "time.";
}

//=============================================================================//
// 3. UTIL_Smoke MOVED, AND ITS MODEL INDEX DID NOT
//=============================================================================//

TEST( CycleDebtMove, TheSmokeModelIndexIsStillCombatWeaponsAndIsOnlyDeclared )
{
	const std::string lib = StripComments( ReadFile( kServerLib ) );
	ASSERT_FALSE( lib.empty() );

	// g_sModelIndexSmoke is DEFINED in combatweapon.cpp. Defining it here as
	// well would link (one is the tentative definition of an int) and the two
	// would be different objects on some toolchains -- so the shim declares it
	// and never defines it.
	EXPECT_EQ( 1u, CountOf( lib, "extern short g_sModelIndexSmoke;" ) );
	EXPECT_EQ( 0u, CountOf( lib, "short g_sModelIndexSmoke =" ) );
}

//=============================================================================//
// 4. THE HEADERS DID NOT MOVE, SO NO CALLER DID
//=============================================================================//

TEST( CycleDebtMove, EveryMovedNameIsStillDeclaredInItsStockHeader )
{
	const std::string ush = StripComments( ReadFile( kUtilSharedHdr ) );
	ASSERT_FALSE( ush.empty() );

	EXPECT_LE( 1u, CountOf( ush, "UTIL_VecToYaw" ) );
	EXPECT_LE( 1u, CountOf( ush, "UTIL_VecToPitch" ) );
	EXPECT_LE( 1u, CountOf( ush, "UTIL_YawToVector" ) );
	// UTIL_Smoke's half (still declared in game/server/util.h) is retired:
	// game/server/util.h is compiled by nothing since plan_server_novalve.md P5
	// and left the farm in P6.
}

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
	const std::set< std::string > s = cmake_graph::CompiledSources( pszTarget );
	size_t n = 0;
	for ( std::set< std::string >::const_iterator it = s.begin(); it != s.end(); ++it )
	{
		if ( *it == strRel
		     || ( it->size() > strRel.size()
		          && it->compare( it->size() - strRel.size() - 1, std::string::npos, "/" + strRel ) == 0 ) )
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
