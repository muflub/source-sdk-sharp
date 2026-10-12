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

const char *const kLibrary   = "sourcesharp/game/shared/util_shared_library.cpp";
const char *const kStockCpp  = "game/shared/util_shared.cpp";
const char *const kStockHdr  = "game/shared/util_shared.h";

const char *const kMoved[] = {
	"void UTIL_ParticleTracer( const char *pszTracerEffectName, const Vector &vecStart, const Vector &vecEnd, ",
	"void UTIL_Tracer( const Vector &vecStart, const Vector &vecEnd, int iEntIndex, ",
	"void UTIL_DecalTrace( trace_t *pTrace, char const *decalName )",
	"void UTIL_BloodDecalTrace( trace_t *pTrace, int bloodColor )",
	// Named by no shim TU, and that is the point: it is UTIL_BloodDrips'
	// callee, so leaving it behind is 4e's conversion one symbol to the left.
	"void UTIL_BloodImpact( const Vector &pos, const Vector &dir, int color, int amount )",
};
const size_t kNumMoved = sizeof( kMoved ) / sizeof( kMoved[ 0 ] );

const char *const kBloodDrips =
	"void UTIL_BloodDrips( const Vector &origin, const Vector &direction, int color, int amount )";

} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

TEST( BloodFamilyMove, EveryMovedMemberIsInTheSharedLibrary )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMoved[ i ] ) )
			<< kMoved[ i ] << " is not defined exactly once in " << kLibrary;
	}
}

// game/shared/util_shared.cpp DOES NOT EXIST. plan_shim.md Phase 2c, lane
// 2-utilshared, deleted it: 1,031 lines had zero importers on either link line,
// and the two definitions that did not (`hl2_episodic` and `UTIL_BloodDrips`)
// moved into sourcesharp/game/shared/util_shared_library.cpp, which is the file
// this test already reads. Both vpc rows went with it.
//
// AN ABSENCE OVER A FILE THAT IS NOT THERE IS VACUOUS, which is what every
// "did not load" assertion in this file exists to refuse -- so the absence is
// asserted as an absence of the FILE, and the successor file is proved read in
// the same breath.
TEST( BloodFamilyMove, TheStockFileIsGoneAndTheLibraryHasTheFamily )
{
	EXPECT_FALSE( FileExists( kStockCpp ) )
		<< kStockCpp << " exists again. Every member of this family is defined "
		<< "in " << kLibrary << "; a second definition of any of them in a file "
		<< "both server_sourcesharp and client_sourcesharp compile is a duplicate the linker picks between silently.";

	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load -- without this "
		<< "the absence above is satisfied by two missing files rather than one";
	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMoved[ i ] ) )
			<< kMoved[ i ] << " is not in " << kLibrary << " either. The family "
			<< "did not move, it evaporated.";
	}
}

TEST( BloodFamilyMove, UtilBloodImpactCameEvenThoughNoShimTuNamesIt )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() );

	EXPECT_EQ( 1u, CountOf( lib, kMoved[ 4 ] ) )
		<< "UTIL_BloodImpact is UTIL_BloodDrips' callee and they are now in the "
		<< "same translation unit. Splitting them again is 4e's measured "
		<< "conversion one symbol to the left: the family moves and the "
		<< "cross-edge simply changes name.";
}

//=============================================================================//
// 2. UTIL_BloodDrips STAYED, AND THE REASON IS ON THE OTHER SIDE
//=============================================================================//

// THIS FACT USED TO SAY UTIL_BloodDrips COULD NOT MOVE, AND IT IS REPLACED BY
// THE MEASUREMENT THAT SETTLED IT RATHER THAN DELETED.
//
// The old reason: on the CLIENT the body reaches UTIL_Smoke
// (game/client/cdll_util.cpp) and g_Language (game/shared/gamerules.cpp), and
// both of those files read zero shim->stock cross-edges. plan_shim.md Phase 2c,
// lane 2-utilshared, answered both halves:
//
//   * g_Language is sourcesharp/game/shared/shell_gamerules.cpp's now, and that
//     file is compiled into BOTH targets. gamerules.cpp is compiled into neither.
//   * UTIL_Smoke is still cdll_util.cpp's on the client -- and the client does
//     not call UTIL_BloodDrips. `tools/shim-census who-imports` reads ONE
//     client importer of the whole of util_shared.cpp and its symbol is
//     hl2_episodic.
//
// So the SERVER arm moved and the CLIENT arm was deleted, and the cross-edge
// the old fact refused to create is still not created. That is what the two
// clauses below assert, and the `#ifndef CLIENT_DLL` is the load-bearing half:
// without it this moves the edge it exists to prevent.
TEST( BloodFamilyMove, UtilBloodDripsMovedWithItsServerArmOnly )
{
	const std::string raw = ReadFile( kLibrary );
	ASSERT_FALSE( raw.empty() ) << kLibrary << " did not load";
	const std::string lib = StripComments( raw );

	EXPECT_EQ( 1u, CountOf( lib, kBloodDrips ) )
		<< kBloodDrips << " is not in " << kLibrary;

	// The guard, checked by POSITION rather than by presence: this file has
	// other CLIENT_DLL arms, so "the file contains #ifndef CLIENT_DLL" is
	// satisfied by any of them. The body must be INSIDE one.
	const size_t iGuard = lib.rfind( "#ifndef CLIENT_DLL", lib.find( kBloodDrips ) );
	ASSERT_NE( std::string::npos, iGuard )
		<< "UTIL_BloodDrips has no #ifndef CLIENT_DLL before it in " << kLibrary;
	const std::string between = lib.substr( iGuard, lib.find( kBloodDrips ) - iGuard );
	EXPECT_EQ( 0u, CountOf( between, "#endif" ) )
		<< "UTIL_BloodDrips is OUTSIDE the server-only guard: the nearest "
		<< "#ifndef CLIENT_DLL above it closes before the body. Compiled on the "
		<< "client it calls UTIL_Smoke, which is game/client/cdll_util.cpp's, "
		<< "and puts a shim->stock cross-edge back into the client link line "
		<< "for a body the client never calls (zero importers, measured).";
}

//=============================================================================//
// 3. THE REFUSAL INSIDE UTIL_DecalTrace IS BEHAVIOUR, NOT DEAD CODE
//=============================================================================//

// READ THE ASSERTIONS, NOT THE NAME. The refusal is still the subject, but the
// thing it refused ON has gone: TraceEntity was one of four families that
// bottomed out in IServerUnknown/IClientUnknown::GetBaseEntity(), every
// implementation of which is `mov $0x0,%eax; ret`, so it was deleted and
// UTIL_DecalTrace's subject is now a written-out NULL. The two halves below are
// the present half (the refusal, and the reason, both still in the file) and
// the absent half (the resolver call and the SDK forward it guarded).
TEST( BloodFamilyMove, TheDecalRefusalCameAcrossIntact )
{
	const std::string lib = ReadFile( kLibrary );
	ASSERT_FALSE( lib.empty() );

	// An entity outside the SDK hierarchy takes no decal; skipping IS the
	// answer, and the paragraph above the line in the source says so. Deleting
	// it as unreachable is the shape of all six recorded silent losses.
	EXPECT_EQ( 1u, CountOf( lib, "\tIGameEntity *pEntity = NULL;" ) )
		<< "the subject of the refusal must still be spelled out in "
		<< kLibrary << ". TraceEntity answered a constant NULL; the NULL is "
		   "what survived it, and it is what the refusal below reads.";
	EXPECT_EQ( 1u, CountOf( lib, "\tif ( !pEntity )\n\t\treturn;" ) )
		<< "the refusal itself. This is UTIL_DecalTrace's whole behaviour and "
		   "deleting it as unreachable is the shape of all six recorded "
		   "silent losses.";
	// The reason is load-bearing: without it the next reader sees a NULL that
	// looks like an unfinished stub and fills it in.
	EXPECT_LE( 1u, CountOf( lib, "does not take a decal yet" ) )
		<< "the paragraph that says WHY the skip is the answer and not a TODO "
		   "must stay above the NULL.";
}

TEST( BloodFamilyMove, TheDeadResolverAndTheArmItGuardedAreBothGone )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() );

	// Absence half. Stated alone this would also pass over an empty file,
	// which is why the fact above asserts the live successor first.
	EXPECT_EQ( 0u, CountOf( lib, "TraceEntity(" ) )
		<< "TraceEntity is deleted tree-wide; no call to it may come back into "
		<< kLibrary << ". Its replacement is TraceGameEntity, which asks the "
		   "virtual GetGameEntity() -- but UTIL_DecalTrace does not want one, "
		   "because DecalTrace is a CBaseEntity virtual with the studio and "
		   "brush decal machinery behind it and there is nothing to forward to.";
	EXPECT_EQ( 0u, CountOf( lib, "->DecalTrace( pTrace, decalName )" ) )
		<< "the SDK forward was reachable only when the resolver returned "
		   "non-NULL, which it never did. It goes WITH the resolver, never "
		   "instead of the refusal above it.";
}

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

TEST( BloodFamilyMove, EveryMovedNameIsStillDeclaredInUtilSharedH )
{
	const std::string hdr = StripComments( ReadFile( kStockHdr ) );
	ASSERT_FALSE( hdr.empty() );

	EXPECT_LE( 1u, CountOf( hdr, "UTIL_ParticleTracer" ) );
	EXPECT_LE( 1u, CountOf( hdr, "UTIL_Tracer" ) );
	EXPECT_LE( 1u, CountOf( hdr, "UTIL_DecalTrace" ) );
	EXPECT_LE( 1u, CountOf( hdr, "UTIL_BloodDecalTrace" ) );
	EXPECT_LE( 1u, CountOf( hdr, "UTIL_BloodImpact" ) );
	EXPECT_LE( 1u, CountOf( hdr, "UTIL_BloodDrips" ) );
}
