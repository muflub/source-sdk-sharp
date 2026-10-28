//========= SourceSharp — the server core's retypes, and what holds them =====//
//
// plan_baseentity.md Phase 13 retypes the SERVER'S ENGINE-FACING CORE — the
// entity-system plumbing rather than the individual game entities. Several of
// those retypes are only correct because of a fact somewhere ELSE in the tree,
// and the failure mode when such a fact stops being true is always the same and
// always the worst one: the retyped code still compiles, still runs, and quietly
// does something different.
//
// This file pins those facts. Every claim here is a claim about SOURCE, so
// source is what it reads — the same method as sdk_player_cast_test and
// collision_origin_identity_test, and for the same reason: the subjects need a
// running engine, or a class this phase is deleting, to be asked at runtime.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given, which is the failure this project has now found more
// than twenty instances of. So the fixtures below assert that each file was
// really opened, that a floor of bytes came back, and — for the claims that are
// absences — that the same search finds the thing it is looking for in a
// hand-written positive fixture.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <set>

#include <dirent.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Comments are DATA to a scanner and must not be searched.
//
// TheResolverListIsHonest was mutated to stop asking the question it guards and
// stayed green, because the file's own PROSE contained the string being searched
// for. Every claim below is about CODE, so every line whose first non-space is
// `//` is dropped before searching, and so is everything between a /* and its
// closing */.
//-----------------------------------------------------------------------------
int CountOf( const std::string &strHay, const char *pszNeedle )
{
	int n = 0;
	size_t at = 0;
	const size_t len = strlen( pszNeedle );
	for ( ;; )
	{
		at = strHay.find( pszNeedle, at );
		if ( at == std::string::npos )
			break;
		n++;
		at += len;
	}
	return n;
}

//-----------------------------------------------------------------------------
// The body of a free or member function, by the text that starts its
// definition. Walks forward to the first '{' and matches braces. Empty when not
// found, which the fixtures distinguish from "found and empty".
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The SOURCESHARP-and-server arm inside such a body: everything between the
// guard and its `#else`. Empty when the guard is not there at all, which is the
// case the fixtures below are looking for.
//-----------------------------------------------------------------------------
} // namespace

//=============================================================================//
// THE SCANNER, FIRST. Nothing below means anything if these fail.
//=============================================================================//

TEST( EngineCoreRetypeScanner, CountOfCounts )
{
	EXPECT_EQ( CountOf( "aXbXcX", "X" ), 3 );
	EXPECT_EQ( CountOf( "aXbXcX", "Y" ), 0 );
	EXPECT_EQ( CountOf( "XXXX", "XX" ), 2 );
}

//=============================================================================//
// 1. THE SPLASH HOOK -- RETIRED WITH physics_fx.cpp (2026-10-11).
//
// The two facts here pinned the free PhysicsSplash's retyped shape: an owner
// parameter, no `pEntity->`, the object as the authority for its own position.
// The function had no caller left: the server's fluid splash is
// SourceSharp.Server's PhysicsSplashPlan, driven by PhysicsFluidEvents
// (PhysicsFluidEventsTests pins the splash it plans), and the native object was
// garbage-collected out of server_srv.so. Its absence from the farm is asserted
// in EveryFileWasReallyOpened.
//=============================================================================//

//=============================================================================//
// 2. THE FLUID THROTTLE IS KEYED BY A HANDLE
//
// fluidevent_t::hEntity is what DeltaTimeSinceLastFluid compares to decide
// whether this entity has splashed recently. As an EHANDLE it resolved to a
// CBaseEntity and answered NULL for everything outside the SDK, so every such
// entity shared the single key NULL and one splash silenced them all — the same
// argument PhysicsOwner_t::Key() makes one file over.
//=============================================================================//

//=============================================================================//
// 3. IsInPrediction IS VACUOUS ON THE SERVER
//
// animation.cpp's server arm returns a constant false. That is a fact about
// this build, not a decision: m_pPredictionPlayer is a STATIC member of
// CBaseEntity — so the alias stops it parsing rather than merely linking — and
// its ONE server writer is CPlayerMove::StartCommand/FinishCommand in
// player_command.cpp, which server_sourcesharp does not compile.
//
// Two ways for that to stop being true, and this pins both: a server source
// starts calling SetPredictionPlayer, or player_command.cpp comes back.
//=============================================================================//

TEST( PredictionVacuity, PlayerCommandIsStillOutOfTheServerProject )
{
	// The proof-of-read FIRST: the port's own shell, which no flip can evict.
	// If sourcesharp/game/server/shell_gamedll.cpp is not in server_sourcesharp
	// there is no server DLL at all, and the absence below would be free.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/shell_gamedll.cpp" ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) does not list the "
		   "port's own shell_gamedll.cpp; this test is not reading the file list it "
		   "thinks it is";

	// This used to look for the `-$File` that removed the file, then (once
	// server_csharp.vpc was flattened) for the name anywhere in the script.
	// Since Phase 6b it asks server_sourcesharp's compiled list, by BASENAME in
	// any directory -- the strength the substring search had.
	const std::set<std::string> compiled = cmake_graph::CompiledSources( cmake_graph::kServer );
	std::string strFound;
	for ( std::set<std::string>::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
	{
		if ( it->substr( it->rfind( '/' ) + 1 ) == "player_command.cpp" )
			strFound = *it;
	}
	EXPECT_EQ( strFound, "" ) << strFound
							  << " is compiled by server_sourcesharp, so the prediction "
								 "player has a writer again and animation.cpp's server arm is a lie";
}

//-----------------------------------------------------------------------------
// TheServerArmIsWhereTheTestSaysItIs IS RETIRED WITH ITS SUBJECT --
// plan_shim.md Phase 2d.
//
// It read game/shared/animation.cpp's IsInPrediction and asserted four things
// about it: the body splits on CLIENT_DLL; the server arm returns the constant
// the vacuity argument rests on; the client arm asks
// EntityPrediction_PlayerStorage rather than the class-scope spelling; and
// CBaseEntity::GetPredictionPlayer is named nowhere in the file's CODE.
//
// animation.cpp is deleted, with the whole activity-ordinal family. There is
// no function to read.
//
// WHAT THE VACUITY ARGUMENT RESTED ON IS NOT LOST, and it did not live here:
// PlayerCommandIsStillOutOfTheServerProject and
// NoServerSourceSetsThePredictionPlayer are the two facts this one was the
// positive control FOR, and both are about the server PROJECT rather than
// about this file. They are untouched, and each carries its own anchor --
// which is what this file's own prose asks of every floor.
//
// THE ABSENCE HALF IS THE ONE WORTH NAMING. It required that
// `CBaseEntity::GetPredictionPlayer` -- a static member of a class this port
// aliases away -- not come BACK into animation.cpp's code. A file that does
// not exist cannot name it, so the claim is satisfied by construction rather
// than weakened; and the same needle over the rest of the shim is what
// cpp_alias_t5_test's collapsed-site table carries.
//-----------------------------------------------------------------------------

//=============================================================================//
// 4. THE SHARED TRACE FILTERS SEE BOTH HIERARCHIES
//
// util_shared.cpp's filter family resolved every candidate with
// EntityFromEntityHandle, which answers NULL for a static prop and for every
// entity on the parallel hierarchy, and then read that NULL as "not there":
//
//   CTraceFilterLOS              dereferenced it, with no check at all
//   CTraceFilterSkipClassname    skipped every managed entity, whatever its name
//   CTraceFilterSkipTwoClassnames        "
//   CTraceFilterSimpleClassnameList      "
//   CTraceFilterNoNPCsOrPlayer   refused to hit the very things it exists to hit
//   CTraceFilterOnlyNPCsAndPlayer could not see the one player this build has
//   PassServerEntityFilter       never filtered a projectile against its owner
//
// Each server arm resolves through AnyEntity_t instead. This pins the SHAPE of
// each arm, because the failure when one is reverted is silent: the trace still
// runs, the filter still answers, and the answer is the opposite one.
//
// SIX OF THE SEVEN NO LONGER EXIST -- plan_shim.md §5 3f, lane 3f. Nine of
// trace_filter_library.cpp's ten filter classes had no caller anywhere on
// either link line, measured three ways (`shim-census who-imports`, which sees
// only the ConVar and CTraceFilterSimple; `nm -C` over both link lines for
// `vtable for CTraceFilter*`, defined AND undefined, which is what catches an
// inline construction a per-function census cannot see; and a text scan of
// every live TU's whole header closure). They are deleted.
//
// SO THE RULE IS ASSERTED OVER THE THREE THAT ARE LEFT, and the six are
// asserted to be ABSENT -- which, unlike the shape rule, is a claim that
// survives them. The positive half is what keeps it from being vacuous: a
// file that stopped resolving anything at all fails
// EveryRetypedArmResolvesThroughAnyEntity, and a file that grew one of the
// six back fails TheDeletedFiltersHaveNotComeBack.
//=============================================================================//

//=============================================================================//
// 5. THE PREDICTION SEED HAS EXACTLY ONE STORAGE
//
// m_nPredictionRandomSeed and m_nPredictionRandomSeedServer were static members
// of CBaseEntity and C_BaseEntity. They stop PARSING under the alias, and the
// two lines that owned them are in one of step 3's five deleted files, so they
// moved to entity_prediction_seed.h.
//
// The seed's whole job is that the client and the server agree, so TWO storages
// with one name would be exactly the bug it exists to prevent: every
// SharedRandomInt/Float/Vector/Angle would diverge, silently, and only under
// prediction. So the class's getter and setter must be FORWARDERS, and nothing
// may read the members again.
//=============================================================================//

//=============================================================================//
// 6. THE MODEL, ASKED THROUGH THE INDEX
//
// util_shared.cpp's StandardFilterRules used to ask `pCollide->GetModel()`, and
// that was util_shared.o's last undefined CBaseEntity symbol: GetModel is NOT
// virtual on the server, so every call to it was a link dependency on
// baseentity.o. It now asks `modelinfo->GetModel( pCollide->GetModelIndex() )`,
// which is the route StandaloneFilterRules twenty lines above already takes for
// the other hierarchy.
//
// THAT IS AN IDENTITY CLAIMED ABOUT A DIFFERENT FILE. If CBaseEntity::GetModel
// ever grows a cache, a NULL guard or a branch, the retyped line still compiles,
// still runs, and quietly answers something else -- which is precisely the
// failure mode this file exists for.
//
// And the reason it is under #ifdef rather than swept: the CLIENT'S GetModel is
// a DIFFERENT FUNCTION. C_BaseEntity::GetModel returns the cached `model`
// pointer rather than looking the index up, so the two are not the same
// question, and the client pays nothing for its version anyway because there it
// is virtual.
//=============================================================================//

//=============================================================================//
// 7. WHAT MAKES recipientfilter.o COST NOTHING
//
// shell_recipientfilter.cpp holds eight `entity->GetSoundEmissionOrigin()` asks and
// owes baseentity.o NOTHING for them, because the member is VIRTUAL: every one
// is a vtable dispatch that emits no symbol. That fact -- one keyword in
// baseentity.h -- is the whole reason the object is at zero consumers, and it is
// invisible at the call sites. De-virtualise it and eight quiet link
// dependencies come back, and the file's comment saying they are free becomes a
// lie that still compiles.
//
// Also pinned here: PrecacheModel's body, because the plan's step-3 note dates
// sound_emitter.cpp's seventeen `static` members and CBaseEntity::
// PrecacheModel to a LIFT rather than a deletion, and the lift is only free
// while the body really is a bare forward to the free function that already
// exists.
//=============================================================================//
