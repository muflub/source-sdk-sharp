//====== SourceSharp — the damage-info value type, moved out of SCC 11 ========//
//
// plan_entity.md Phase 4 exit, lane p4-util. `game/shared/takedamageinfo.cpp`
// is one of the 31 translation units of SCC 11, and every definition it held
// now lives in `sourcesharp/game/shared/takedamageinfo_library.cpp`.
//
// WHY THE WHOLE TU AND NOT A FAMILY, which is the thing this suite exists to
// stop being undone: step 7f measured `ClearMultiDamage()` on its own and
// recorded it as a CONVERSION —
//
//     ClearMultiDamage() -> g_MultiDamage -> CMultiDamage::Init
//                        -> CTakeDamageInfo::Init + CTakeDamageInfo::m_DataMap
//
// all of which were out of line in stock's object. Move the function and the
// cross-edge lands on the symbol next door; move the whole unit and there is
// nothing next door to land on. Any future "tidy-up" that puts one of these
// bodies back into `game/shared/takedamageinfo.cpp` re-creates exactly that.
// The stock file's CONTENTS are no longer read -- it is compiled into neither
// DLL, which is asserted below -- so the library side is what is pinned.
//
// THREE FAILURES THIS PINS:
//
//   1. A ONE-SIDED SOURCE ROW. takedamageinfo.cpp was compiled into server.so
//      AND client.so, so the shim TU is in BOTH targets' source lists. The recorded lesson —
//      a shared file read on one side compiles green and breaks the other at a
//      later step — is exactly this shape.
//
//   2. THE DATADESCS SPLIT FROM THEIR CLASSES. `BEGIN_SIMPLE_DATADESC(
//      CTakeDamageInfo )` defines `CTakeDamageInfo::m_DataMap`, and
//      `BEGIN_SIMPLE_DATADESC_( CMultiDamage, CTakeDamageInfo )` NAMES the
//      base's. Split across two TUs they still link; what breaks is the save
//      game, silently.
//
//   3. `phys_pushscale` LEFT BEHIND. It is a plain (non-static) ConVar that
//      four other files declare `extern`, so a copy left in the stock TU is a
//      duplicate registration, which the ConVar system reports at runtime and
//      no compile or link step sees.
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

const char *const kLibrary = "sourcesharp/game/shared/takedamageinfo_library.cpp";
const char *const kStockCpp = "game/shared/takedamageinfo.cpp";
//-----------------------------------------------------------------------------
// THE FOUR DEFINITIONS PHASE 3f DELETED, and the reason they are a list rather
// than four missing rows.
//
// plan_shim.md §5 3f. Each had 0 importers on either link line AND no caller
// inside this translation unit -- both halves were needed, because
// `orphan-syms` cannot see an intra-TU caller and two of this file's other
// orphans (SetCritType, ApplyMultiDamage) turn out to have one. SetCritType is
// called from AddMultiDamage at the point where the accumulator takes the
// incoming crit type, which is why it is still in kMovedDefinitions above and
// why the plan's own list for this file is one name too long.
//
// A ROW THAT SIMPLY VANISHES IS NOT A CHECK. These assert the ABSENCE, paired
// with the surviving declaration in stock's header, so the pair cannot be
// satisfied by the file being gone or by the header being gutted -- and a lane
// that brings one of these back has to say so here.
//-----------------------------------------------------------------------------
} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

//=============================================================================//
// 2. THE OBJECTS TRAVELLED WITH THE CODE THAT READS THEM
//=============================================================================//

//=============================================================================//
// 3. THE SERVER'S LINK LINE COMPILES IT
//=============================================================================//

// Listed EXACTLY ONCE in the target's CMakeLists (a second row is a duplicate
// TU) and present in its compile database. cmake_graph throws on a missing,
// empty or stale input.
static void ExpectCompiledOnce( const char *pTarget )
{
	const std::vector<std::string> listed = cmake_graph::ListedSources( pTarget );
	EXPECT_EQ( 1, std::count( listed.begin(), listed.end(), std::string( kLibrary ) ) )
		<< kLibrary << " is not listed exactly once in " << pTarget << " (" << cmake_graph::ListFile( pTarget ) << ")";
	EXPECT_TRUE( cmake_graph::Compiles( pTarget, kLibrary ) ) << kLibrary << " is not compiled into " << pTarget;
}

TEST( TakeDamageInfoLibrary, TheServerCompilesIt )
{
	ExpectCompiledOnce( cmake_graph::kServer );
	if ( HasFailure() )
		ADD_FAILURE() << "server.so needs ApplyMultiDamage, g_MultiDamage and "
						 "CTakeDamageInfo::Init, and takedamageinfo.cpp no longer defines them.";
}

TEST( TakeDamageInfoLibrary, TheClientDoesNotCompileIt )
{
	// client.so stopped asking for it with its stock headers (2026-10-25):
	// shell_gamerules.cpp's ClearMultiDamage went, because nothing fills the
	// accumulator on either side, and no client object names CTakeDamageInfo.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kLibrary ) );
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kLibrary ) )
		<< kLibrary << " is compiled into client_sourcesharp again";
}

TEST( TakeDamageInfoLibrary, TheStockTranslationUnitIsCompiledOnNeitherSide )
{
	// THIS ASSERTION IS THE OTHER WAY ROUND SINCE plan_shim.md PHASE 1d, and
	// the flip is the point rather than an edit to get a suite green.
	//
	// What it said was that both vpcs still named game/shared/takedamageinfo.cpp
	// -- because the lane that emptied that TU moved behaviour only, and the
	// Phase 4 exit was to remove all 31 SCC 11 rows in one commit. Phase 1d
	// is a different commit and a narrower claim: this TU is not one of the
	// 31 waiting on a flip, it is an EMPTY translation unit. It defines 0
	// strong symbols on either side -- its eight weak ones are mathlib header
	// inlines (IsFinite, the Vector2D/4D constructors) that every other
	// object emits for itself -- and tools/shim-census reports 0 objects
	// importing it on server and 0 on client. Compiling 81 lines of
	// commented-out DMG_ table into both DLLs is the whole of what the two
	// rows bought.
	//
	// The file itself is untouched and still on disk (Valve's stock build
	// lists it), and every fact above about what it must not contain reads it
	// from disk and goes on working.
	//
	// Proof-of-read for the absences: the successor IS compiled on the server.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kLibrary ) )
		<< "server_sourcesharp does not compile " << kLibrary;

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kStockCpp ) )
		<< "the stock TU is back in server_sourcesharp "
		   "(src/server/native/CMakeLists.txt). It defines nothing; if "
		   "something has been typed back into it, that is a SECOND definition "
		   "of whatever "
		<< kLibrary << " already holds.";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kStockCpp ) )
		<< "the stock TU is back in client_sourcesharp -- see above, and note "
		   "that a row added to one side only is the shared-file failure this "
		   "suite's header lists second.";
}

//=============================================================================//
// 4. THE HEADER DID NOT MOVE, SO NO CALLER DID
//=============================================================================//

//=============================================================================//
// 5. THE NEEDLES ARE CODE, NOT PROSE
//
// Both files carry a banner naming what moved. A needle that occurs only in a
// comment would make every fact above pass whatever the code did.
//=============================================================================//

//=============================================================================//
// phys_pushscale MOVED TO MANAGED CODE (plan_shim.md Phase 5, lane 5d)
//
// A deletion that is only an absence is not a check: the file could have been
// emptied and every "is it gone" assertion would still pass. So this pins the
// pair -- no native DECLARATION left in the shim TU, and the three READS still
// here, through the by-name handle that replaced it.
//=============================================================================//
