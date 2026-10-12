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
const char *SourceRoot()
{
	static const char *kCandidates[] = { "game", "src/game", "../src/game" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		DIR *pDir = opendir( kCandidates[ i ] );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string Read( const char *pszRelativeToGame )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += pszRelativeToGame;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

//-----------------------------------------------------------------------------
// Comments are DATA to a scanner and must not be searched.
//
// TheResolverListIsHonest was mutated to stop asking the question it guards and
// stayed green, because the file's own PROSE contained the string being searched
// for. Every claim below is about CODE, so every line whose first non-space is
// `//` is dropped before searching, and so is everything between a /* and its
// closing */.
//-----------------------------------------------------------------------------
std::string CodeOnly( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );

	bool bInBlock = false;
	size_t at = 0;
	while ( at < s.size() )
	{
		size_t eol = s.find( '\n', at );
		if ( eol == std::string::npos )
			eol = s.size();

		std::string strLine = s.substr( at, eol - at );
		at = eol + 1;

		std::string strKept;
		for ( size_t i = 0; i < strLine.size(); i++ )
		{
			if ( bInBlock )
			{
				if ( i + 1 < strLine.size() && strLine[ i ] == '*' && strLine[ i + 1 ] == '/' )
				{
					bInBlock = false;
					i++;
				}
				continue;
			}

			if ( i + 1 < strLine.size() && strLine[ i ] == '/' && strLine[ i + 1 ] == '/' )
				break;

			if ( i + 1 < strLine.size() && strLine[ i ] == '/' && strLine[ i + 1 ] == '*' )
			{
				bInBlock = true;
				i++;
				continue;
			}

			strKept += strLine[ i ];
		}

		out += strKept;
		out += '\n';
	}

	return out;
}

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
std::string BodyAfter( const std::string &strSource, const char *pszNeedle )
{
	const size_t at = strSource.find( pszNeedle );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = strSource.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	int depth = 0;
	size_t i = open;
	for ( ; i < strSource.size(); i++ )
	{
		if ( strSource[ i ] == '{' ) depth++;
		else if ( strSource[ i ] == '}' && --depth == 0 ) break;
	}
	if ( i >= strSource.size() )
		return std::string();

	return strSource.substr( open, i - open + 1 );
}

//-----------------------------------------------------------------------------
// The SOURCESHARP-and-server arm inside such a body: everything between the
// guard and its `#else`. Empty when the guard is not there at all, which is the
// case the fixtures below are looking for.
//-----------------------------------------------------------------------------
// The server arm of a per-side body in the shim's trace-filter library.
//
// THE GUARD LOST ITS SOURCESHARP HALF, and that is a collapse rather than a
// change: this file is in client_sourcesharp and server_sourcesharp only
// (src/{client,server}/native/CMakeLists.txt), both of which define SOURCESHARP, so `defined( SOURCESHARP ) && !defined( CLIENT_DLL )`
// and `!defined( CLIENT_DLL )` select the same arm in every build that exists.
// `the-close` collapsed it with the tree's other always-true conditions. The
// SIDE half is real and stays: this file IS compiled into both DLLs.
//
// BOTH SPELLINGS OF THE SIDE GUARD, lane 3f. This recognised
// `#if !defined( CLIENT_DLL )` only, and returned an empty string -- which the
// callers correctly treat as "there is no server arm at all" -- for a body
// guarded `#ifndef CLIENT_DLL`. The two mean the same thing and the file uses
// both; a reader is not going to keep them apart, so the scanner does not
// ask it to. This was found the moment the filter list shrank onto a body
// written the other way, which is the shape a one-spelling scanner hides.
std::string ServerArmOf( const std::string &strBody )
{
	static const char *const kGuards[] = {
		"#if !defined( CLIENT_DLL )",
		"#ifndef CLIENT_DLL",
	};

	size_t at = std::string::npos;
	size_t len = 0;
	for ( size_t i = 0; i < sizeof( kGuards ) / sizeof( kGuards[ 0 ] ); i++ )
	{
		const size_t found = strBody.find( kGuards[ i ] );
		if ( found != std::string::npos && ( at == std::string::npos || found < at ) )
		{
			at = found;
			len = strlen( kGuards[ i ] );
		}
	}

	if ( at == std::string::npos )
		return std::string();

	const size_t from = at + len;
	const size_t to = strBody.find( "#else", from );
	if ( to == std::string::npos )
		return strBody.substr( from );

	return strBody.substr( from, to - from );
}

// The four subjects, read once each.
// PhysicsFx() IS GONE with physics_fx.cpp (2026-10-11: no caller).
const std::string &PhysicsCpp()     { static std::string s = CodeOnly( Read( "../sourcesharp/game/server/physics.cpp" ) ); return s; }
// BaseEntityH() IS GONE -- game/server/baseentity.h is compiled by nothing
// since plan_server_novalve.md P5 and left the farm in P6. Its absence is
// pinned in EveryFileWasReallyOpened; the facts that read its text retired.
const std::string &PhysicsSharedH() { static std::string s = CodeOnly( Read( "shared/physics_shared.h" ) ); return s; }
// AnimationCpp() IS GONE -- plan_shim.md Phase 2d deleted
// game/shared/animation.cpp with the whole activity-ordinal family. Its one
// fact retired with it; see the note where that fact used to be.
// ServerVpc() IS GONE -- Phase 6b retired VPC. What the server compiles is
// server_sourcesharp's list, read through cmake_graph.h.
const std::string &UtilShared()     { static std::string s = CodeOnly( Read( "shared/util_shared.cpp" ) ); return s; }
// plan_entity.md step 4e: the shared trace-filter family left util_shared.cpp
// for the shim, whole and unedited. Every assertion below that named a filter
// body reads this instead; what each one asserts is unchanged.
const std::string &TraceFilterLib() { static std::string s = CodeOnly( Read( "../sourcesharp/game/shared/trace_filter_library.cpp" ) ); return s; }
// Phase 4 EXIT: the shared-random family (SeedFileLineHash and the four
// SharedRandom* bodies) left util_shared.cpp for util_shared_library.cpp,
// whole and unedited, because SharedRandomInt was game/shared/animation.cpp's
// only tie into the stock link graph's one cycle. The fact below reads this
// instead -- and also asserts the bodies are NOT still in util_shared.cpp,
// because a move that leaves a copy behind passes a repointed read and links
// to the copy.
const std::string &UtilSharedLib()   { static std::string s = CodeOnly( Read( "../sourcesharp/game/shared/util_shared_library.cpp" ) ); return s; }
const std::string &BaseEntSharedH() { static std::string s = CodeOnly( Read( "shared/baseentity_shared.h" ) ); return s; }
// plan_entity.md step 4e: EntityPrediction_SetSeedFromCommand's BODY left
// baseentity_shared.cpp for the shim, together with the ConVar object and the
// two other lifted statics that had kept it there. What this section asserts is
// unchanged -- the setter writes entity_prediction_seed.h's storage and keeps
// the -1 arm -- only its address moved.
const std::string &EntStaticsC()    { static std::string s = CodeOnly( Read( "../sourcesharp/game/shared/entity_shared_statics.cpp" ) ); return s; }
// BaseEntSharedFwdC() WAS HERE, reading shared/baseentity_shared_forwarders.cpp
// for CBaseEntity::SetPredictionRandomSeed's class-scope name. That quarantine
// is retired -- every caller in this build names the free function directly --
// so there is no forwarder body left to read. These two are where the seed's
// remaining callers live, one per side. `..` out of game/ rather than a second
// root: SourceRoot() answers "game", "src/game" or "../src/game", and the
// relative step is correct against all three.
const std::string &ShellPlayerSvC() { static std::string s = CodeOnly( Read( "../sourcesharp/game/server/shell_standalone_player.cpp" ) ); return s; }
// plan_entity.md step 6f: the client player class is gone and its two seeding
// calls moved to the replay bracket in shell_prediction.cpp, which is where the
// client now enters and leaves one replayed command.
const std::string &ShellPlayerClC() { static std::string s = CodeOnly( Read( "../sourcesharp/game/client/shell_prediction.cpp" ) ); return s; }
const std::string &AnyEntityH()     { static std::string s = CodeOnly( Read( "../sourcesharp/game/server/anyentity.h" ) ); return s; }
const std::string &CBaseEntityH()   { static std::string s = CodeOnly( Read( "client/c_baseentity.h" ) ); return s; }

} // namespace

//=============================================================================//
// THE SCANNER, FIRST. Nothing below means anything if these fail.
//=============================================================================//

TEST( EngineCoreRetypeScanner, EveryFileWasReallyOpened )
{
	ASSERT_TRUE( SourceRoot() != NULL ) << "no game/ directory found from the cwd";

	// EVERY FLOOR IN THIS FACT NOW CARRIES A LIVE ANCHOR, which is the shape
	// the two entity headers below worked out and the server vpc took next
	// (05237b9c8). The argument is the same one step wider: EVERY subject here
	// is a STOCK file the port exists to delete from, so every byte threshold
	// over one of them reddens on a correct tree eventually -- it says "was not
	// really opened" about a file that was read perfectly. Measured rather than
	// guessed: comment-stripped, physics.cpp is 95,170 bytes against 50,000 and
	// baseentity.cpp 186,620 against 100,000, so these were half a Phase away
	// from the failure orphan_clientclass_test and this file have both already
	// had.
	//
	// Each floor drops to a value only an UNOPENED or TRUNCATED read can miss,
	// and the anchor does the identifying work a size never could: it names
	// something in the file's own code, so a read that resolved to the wrong
	// file fails where a size of the right order would have passed.
	// physics_fx.cpp IS GONE (2026-10-11: no caller): the
	// splash is SourceSharp.Server's PhysicsSplashPlan. physics.cpp below, one
	// directory over, reads, so this absence is evidence and not a bad path.
	EXPECT_TRUE( Read( "../sourcesharp/game/server/physics_fx.cpp" ).empty() )
		<< "game/server/physics_fx.cpp is readable from the farm again. Its "
		   "PhysicsSplash had no caller; the managed PhysicsSplashPlan is the "
		   "splash. A native copy back in the tree is a second decider.";
	EXPECT_GT( PhysicsCpp().size(), 500u );
	EXPECT_NE( PhysicsCpp().find( "void CPhysicsHook::FrameUpdatePostEntityThink" ), std::string::npos )
		<< "physics.cpp carries no CPhysicsHook frame, so it is not the file "
		   "this scanner means to be reading";
	// THE TWO ENTITY HEADERS SHRANK BY AN ORDER OF MAGNITUDE, and the floors
	// moved with them. `class CBaseEntity` / `class C_BaseEntity` were inside
	// `#ifndef SS_CBASEENTITY_ALIAS` -- source no project compiled, since both
	// vpcs define that macro -- and `the-close 2/N` deleted the arm. What is
	// left is the includes, the forward declarations and the free functions.
	// A FLOOR ALONE WOULD NOW BE WEAK, so each carries a live anchor too.
	// baseentity.h's floor and anchor became an ANCHORED ABSENCE: the header
	// is compiled by nothing since plan_server_novalve.md P5 and left the farm
	// in P6, which is the stronger form of every "baseentity.h declares X
	// again" this file used to make (PhysicsSplash, a class-scope
	// GetModelIndex, GetSoundEmissionOrigin, PrecacheModel). A sibling in the
	// same directory must read, or the absence is true of a tree never found.
	EXPECT_NE( Read( "server/entity_funcs.h" ).find( "#ifndef ENTITY_FUNCS_H" ), std::string::npos )
		<< "game/server/entity_funcs.h did not read, so the absence below is "
		   "not evidence";
	EXPECT_TRUE( Read( "server/baseentity.h" ).empty() )
		<< "game/server/baseentity.h is readable from the farm again. Nothing "
		   "has compiled it since plan_server_novalve.md P5; if a target needs "
		   "it back, so do the stale declarations this file used to forbid.";
	EXPECT_GT( PhysicsSharedH().size(), 500u );
	EXPECT_NE( PhysicsSharedH().find( "PHYSICS_SHARED_H" ), std::string::npos )
		<< "physics_shared.h is not physics_shared.h";
	// THE animation.cpp ANCHOR IS GONE with the file -- plan_shim.md Phase 2d.
	// It read `BuildAllAnimationEventIndexes`, which walked every sequence
	// writing registry ordinals into mdl memory, and it was a good anchor for
	// exactly the reason the file went: nothing compares those ordinals now.
	// THE server_csharp.vpc ANCHOR IS RETIRED IN 6b with the file: VPC is gone,
	// and what the server compiles is read through cmake_graph.h, which throws
	// on a missing, empty or stale list itself. PlayerCommandIsStillOutOfThe-
	// ServerProject keeps its own proof-of-read.
	// THE baseentity.cpp ANCHOR IS GONE with the file from the farm (culled
	// 2026-09-27): neither DLL compiles game/server/baseentity.cpp, and the
	// checks that read it left with it.
	EXPECT_GT( CBaseEntityH().size(), 2000u );
	EXPECT_NE( CBaseEntityH().find( "C_BASEENTITY_H" ), std::string::npos )
		<< "c_baseentity.h is not c_baseentity.h";
}

TEST( EngineCoreRetypeScanner, CommentStrippingReallyStrips )
{
	// Both spellings, because the mutation that beat the last scanner of this
	// kind hid in a whole-line // comment, and a block comment would hide just
	// as well.
	const std::string strIn =
		"alpha();\n"
		"// beta();\n"
		"   // gamma();\n"
		"/* delta();\n"
		"   epsilon(); */\n"
		"zeta(); // theta();\n";

	const std::string strOut = CodeOnly( strIn );

	EXPECT_NE( strOut.find( "alpha" ), std::string::npos );
	EXPECT_NE( strOut.find( "zeta" ), std::string::npos );
	EXPECT_EQ( strOut.find( "beta" ), std::string::npos );
	EXPECT_EQ( strOut.find( "gamma" ), std::string::npos );
	EXPECT_EQ( strOut.find( "delta" ), std::string::npos );
	EXPECT_EQ( strOut.find( "epsilon" ), std::string::npos );
	EXPECT_EQ( strOut.find( "theta" ), std::string::npos );
}

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

TEST( FluidThrottle, TheKeyIsAHandleAndNotAnEntity )
{
	const std::string &strH = PhysicsSharedH();
	ASSERT_FALSE( strH.empty() );

	const size_t at = strH.find( "struct fluidevent_t" );
	ASSERT_NE( at, std::string::npos );

	const size_t close = strH.find( "};", at );
	ASSERT_NE( close, std::string::npos );

	const std::string strDecl = strH.substr( at, close - at );
	EXPECT_NE( strDecl.find( "CBaseHandle" ), std::string::npos )
		<< "fluidevent_t::hEntity stopped being a handle";
	EXPECT_EQ( strDecl.find( "EHANDLE" ), std::string::npos )
		<< "fluidevent_t::hEntity is an EHANDLE again; it resolves one hierarchy "
		   "and keys every other entity to NULL";
}

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
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kServer );
	std::string strFound;
	for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
	{
		if ( it->substr( it->rfind( '/' ) + 1 ) == "player_command.cpp" )
			strFound = *it;
	}
	EXPECT_EQ( strFound, "" )
		<< strFound << " is compiled by server_sourcesharp, so the prediction "
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

TEST( SharedTraceFilters, EveryRetypedArmResolvesThroughAnyEntity )
{
	const std::string &strCpp = TraceFilterLib();
	ASSERT_FALSE( strCpp.empty() );

	// The survivors, and they are the ones every trace on both link lines
	// actually reaches: util_shared.h's three UTIL_Trace* inlines construct
	// CTraceFilterSimple, whose ShouldHitEntity calls StandardFilterRules,
	// which calls StandaloneFilterRules. Its one body is spelled
	// SS_SIMPLE_FILTER:: since P5 (the server's class is ss::CTraceFilterSimple).
	static const char *kFilters[] = {
		"bool PassServerEntityFilter",
		"SS_SIMPLE_FILTER::ShouldHitEntity",
	};

	for ( size_t i = 0; i < sizeof( kFilters ) / sizeof( kFilters[ 0 ] ); i++ )
	{
		const std::string strBody = BodyAfter( strCpp, kFilters[ i ] );
		ASSERT_FALSE( strBody.empty() ) << kFilters[ i ] << " was not found";

		const std::string strArm = ServerArmOf( strBody );
		ASSERT_FALSE( strArm.empty() )
			<< kFilters[ i ] << " has no SourceSharp server arm; it is back to "
			   "resolving one hierarchy and reading NULL as absence";

		// Two spellings, one rule. PassServerEntityFilter needs an
		// AnyEntity_t because it compares two candidates and one of them can
		// be on either hierarchy; CTraceFilterSimple asks for the game entity
		// and is written as an `if` over the answer. Both RESOLVE and both
		// CHECK, which is the property that was missing before.
		const bool bAny = strArm.find( "AnyEntityOf(" ) != std::string::npos;
		const bool bGame = strArm.find( "GameEntityFromEntityHandle(" ) != std::string::npos;
		EXPECT_TRUE( bAny || bGame )
			<< kFilters[ i ] << " resolves through neither AnyEntityOf nor "
			   "GameEntityFromEntityHandle, so it is reading one hierarchy "
			   "again and NULL as absence";

		const bool bChecked = strArm.find( "IsValid()" ) != std::string::npos
			|| strArm.find( "if ( IGameEntity *" ) != std::string::npos;
		EXPECT_TRUE( bChecked )
			<< kFilters[ i ] << " stopped checking that the resolve answered";

		// The needle is the OLD resolver on its own. Both live spellings end
		// in it, so it is matched with its leading boundary rather than as a
		// substring -- which is the mistake that made this row unfalsifiable
		// the first time it was written.
		EXPECT_EQ( CountOf( strArm, " EntityFromEntityHandle(" ), 0 )
			<< kFilters[ i ] << " is resolving one hierarchy again";
	}
}

// TheLosFilterChecksBeforeItAsks IS RETIRED WITH ITS SUBJECT -- lane 3f.
//
// It pinned the ORDER of two lines inside CTraceFilterLOS::ShouldHitEntity's
// server arm: check that the resolve answered, THEN ask BlocksLOS(), because
// the arm this port replaced asked first and dereferenced the NULL that
// EntityFromEntityHandle returns for a static prop. That was the one filter in
// the family that crashed rather than answering wrongly.
//
// CTraceFilterLOS has no caller on either link line and is deleted, so there is
// no order left to pin. THE CHECK-THEN-ASK RULE IS NOT LOST: it is asserted
// over the two filters that remain by the loop above, which requires a resolve
// and a check of it in every server arm it reads, and the ABSENCE of the six
// deleted ones is asserted below so this cannot quietly become a test about
// nothing.
TEST( SharedTraceFilters, TheDeletedFiltersHaveNotComeBack )
{
	const std::string &strCpp = TraceFilterLib();
	ASSERT_FALSE( strCpp.empty() );

	// The positive control: the file is the one this test thinks it is, and it
	// still holds the filter every trace on both link lines constructs.
	ASSERT_NE( strCpp.find( "SS_SIMPLE_FILTER::ShouldHitEntity" ), std::string::npos )
		<< "trace_filter_library.cpp no longer defines CTraceFilterSimple -- "
		   "the absences below are then statements about the wrong file";

	static const char *kGone[] = {
		"CTraceFilterLOS::ShouldHitEntity",
		"CTraceFilterSkipClassname::ShouldHitEntity",
		"CTraceFilterSkipTwoClassnames::ShouldHitEntity",
		"CTraceFilterSimpleClassnameList::ShouldHitEntity",
		"CTraceFilterNoNPCsOrPlayer::ShouldHitEntity",
		"CTraceFilterOnlyNPCsAndPlayer::ShouldHitEntity",
		"CTraceFilterSkipTwoEntities::ShouldHitEntity",
		"CTraceFilterSimpleList::ShouldHitEntity",
		"CTraceFilterChain::ShouldHitEntity",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_EQ( CountOf( strCpp, kGone[ i ] ), 0 )
			<< kGone[ i ] << " is defined again. It had no caller on either "
			   "link line when it was deleted; if that has changed, the plan "
			   "is the place to say so.";
	}
}

TEST( SharedTraceFilters, OwnershipIsComparedAsHandles )
{
	const std::string strBody = BodyAfter( TraceFilterLib(), "bool PassServerEntityFilter" );
	ASSERT_FALSE( strBody.empty() );

	const std::string strArm = ServerArmOf( strBody );
	ASSERT_FALSE( strArm.empty() )
		<< "PassServerEntityFilter is back to comparing CBaseEntity pointers, so "
		   "a managed projectile clips against the thing that fired it";

	EXPECT_NE( strArm.find( "GetOwnerHandle() == pPass" ), std::string::npos );
	EXPECT_NE( strArm.find( "GetOwnerHandle() == pTouch" ), std::string::npos );
	EXPECT_EQ( CountOf( strArm, "GetOwnerEntity" ), 0 );
}

TEST( SharedTraceFilters, TheNeutralsAreTheOnesTheFiltersWereWrittenAgainst )
{
	// AnyEntity_t's three new answers, pinned by BODY rather than by presence.
	// Each neutral is a decision -- flip one and a filter's verdict for every
	// managed entity in the map flips with it, silently.
	const std::string &strH = AnyEntityH();
	ASSERT_FALSE( strH.empty() );

	// Blocks sight until something says otherwise, which is also the SDK's own
	// default.
	// RE-ANCHORED. This read the `: true` of `pBase ? pBase->BlocksLOS() : true`
	// -- and the `pBase ?` half was inside `#ifndef SS_CBASEENTITY_ALIAS`, an
	// arm no project compiled, so the NEUTRAL was always the whole answer. The
	// arm is deleted and the neutral is now written plainly. Same decision,
	// finally anchored on the line that runs.
	EXPECT_NE( BodyAfter( strH, "bool BlocksLOS() const" ).find( "return true;" ),
	           std::string::npos )
		<< "AnyEntity_t::BlocksLOS's neutral changed; every LOS trace past a "
		   "managed entity changes with it";

	// A player only if the relay says so -- never a blanket false, which would
	// hide the one player this build has from every player-seeking filter.
	EXPECT_NE( BodyAfter( strH, "bool IsPlayer() const" ).find( "StandaloneEntity_IsPlayer" ),
	           std::string::npos );

	// Stock's own matching rule, reached by stock's own route.
	EXPECT_NE( BodyAfter( strH, "bool ClassMatches(" ).find( "EntityNames_Match" ),
	           std::string::npos );
}

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

TEST( PredictionSeed, TheSeedHasExactlyOneWayIn )
{
	// RENAMED, NOT MOVED: CBaseEntity IS IGameEntity -- ss_cbaseentity_alias.h
	// is deleted and the 1,543 code tokens now read IGameEntity -- so the
	// forwarder is at the same address under the type's real name. The ~200
	// call sites are unchanged, which is what the forwarder exists for.
	const std::string strGet = BodyAfter( BaseEntSharedH(), "inline int IGameEntity::GetPredictionRandomSeed(" );
	ASSERT_FALSE( strGet.empty() ) << "the getter is gone from baseentity_shared.h";

	EXPECT_NE( strGet.find( "EntityPrediction_Seed(" ), std::string::npos )
		<< "IGameEntity::GetPredictionRandomSeed reads its own storage again";
	EXPECT_EQ( CountOf( strGet, "m_nPredictionRandomSeed" ), 0 );

	// THE SETTER MOVED, and only its scope did. SetPredictionRandomSeed is one
	// of the four `static` members of baseentity_shared.cpp lifted out of the
	// class scope: the CODE is the free function EntityPrediction_SetSeedFromCommand,
	// which step 4e moved on to the shim TU. So this reads the free function
	// for the behaviour. (The check that the old address in
	// game/shared/baseentity_shared.cpp is empty left with that file: neither
	// DLL compiles it, so a copy there cannot link -- culled 2026-09-27.)
	const std::string strSet = BodyAfter( EntStaticsC(), "void EntityPrediction_SetSeedFromCommand(" );
	ASSERT_FALSE( strSet.empty() ) << "the setter is gone from sourcesharp/game/shared/entity_shared_statics.cpp";

	EXPECT_NE( strSet.find( "EntityPrediction_SetSeed(" ), std::string::npos )
		<< "the prediction seed setter writes its own storage again, so "
		   "the client and the server would seed from two different variables";
	EXPECT_EQ( CountOf( strSet, "m_nPredictionRandomSeed" ), 0 );

	// The -1 that the four SharedRandom* asserts are written against survives in
	// the NULL-command arm. Losing it would make "not inside a usercmd"
	// indistinguishable from seed zero.
	EXPECT_NE( strSet.find( "EntityPrediction_SetSeed( -1 )" ), std::string::npos );

	// And there is now only ONE way in. The class-scope
	// CBaseEntity::SetPredictionRandomSeed used to be a one-line forwarder in
	// baseentity_shared_forwarders.cpp, and what was pinned here was that it
	// never grew a body -- because a second body would mean the SDK's callers
	// and the port's own seeding differently, which is the two-storages bug this
	// section exists to prevent wearing a new hat.
	//
	// The quarantine is retired, so that divergence has nowhere to happen any
	// more: there is no second implementation to diverge from. What replaces the
	// assertion is a stronger one -- both remaining callers, one per side, name
	// the free function and NOT the class.
	ASSERT_FALSE( ShellPlayerSvC().empty() ) << "server shell_standalone_player.cpp did not load";
	ASSERT_FALSE( ShellPlayerClC().empty() ) << "client shell_prediction.cpp did not load";

	EXPECT_EQ( CountOf( ShellPlayerSvC(), "EntityPrediction_SetSeedFromCommand(" ), 2 )
		<< "the server player no longer seeds through the lifted free function.";
	EXPECT_EQ( CountOf( ShellPlayerClC(), "EntityPrediction_SetSeedFromCommand(" ), 2 )
		<< "the client replay bracket no longer seeds through the lifted free\n"
		   " function. Both calls are load-bearing: the set on the way in and the\n"
		   " NULL clear on the way out.";

	EXPECT_EQ( CountOf( ShellPlayerSvC(), "CBaseEntity::SetPredictionRandomSeed" ), 0 )
		<< "the server player reaches the class-scope setter again. Nothing "
		   "defines it in this build.";
	EXPECT_EQ( CountOf( ShellPlayerClC(), "C_BaseEntity::SetPredictionRandomSeed" ), 0 )
		<< "the client player reaches the class-scope setter again.";
}

TEST( PredictionSeed, TheSharedRandomFunctionsAskTheHeaderAndNotTheClass )
{
	// REPOINTED, NOT RELAXED. The four SharedRandom* bodies and the file-static
	// hash they share moved to sourcesharp/game/shared/util_shared_library.cpp
	// at the Phase 4 exit; what this fact asserts about them is unchanged, and
	// it gained the assertion a repoint would otherwise have lost -- that the
	// bodies are not ALSO still at the old address.
	const std::string &strLib = UtilSharedLib();
	ASSERT_FALSE( strLib.empty() );

	// Four functions, two asks each: the assert and the hash.
	EXPECT_EQ( CountOf( strLib, "EntityPrediction_Seed()" ), 8 );
	EXPECT_EQ( CountOf( strLib, "CBaseEntity::GetPredictionRandomSeed" ), 0 )
		<< "the shared-random family is reading the seed through the class "
		   "again, which is a static member of a name that is about to be an "
		   "alias";

	// THE OLD ADDRESS IS GONE, which is the strongest form this half can take.
	// plan_shim.md Phase 2c, lane 2-utilshared, deleted
	// game/shared/util_shared.cpp: 1,031 of its lines had zero importers on
	// either link line, and the two definitions that did -- `hl2_episodic` and
	// `UTIL_BloodDrips` -- moved into util_shared_library.cpp, the same file
	// this fact reads above. Both vpc rows went with it.
	//
	// The claim is unchanged ("the bodies are not ALSO still at the old
	// address") and it is now unconditional: there is no old address. Read()
	// answers empty for a path that is not there, and `strLib` above is proved
	// non-empty in the same fact, so this cannot pass by reading nothing twice.
	EXPECT_TRUE( UtilShared().empty() )
		<< "game/shared/util_shared.cpp is back. It is where SeedFileLineHash "
		   "and the four SharedRandom* bodies were, and a second copy of them "
		   "is a second seed hash: the linker answers stock's callers with "
		   "whichever it picked and the two sides of prediction disagree "
		   "without anything failing to build.";
}

TEST( SharedTraceFilters, TheBodyExtractorsReallyExtract )
{
	// Positive and negative controls for BodyAfter and ServerArmOf, because
	// every assertion above is of the form "this body contains that": a
	// body-getter that returned the whole file would pass all of them.
	const std::string strFixture =
		"void Before() { NOISE; }\n"
		"void Subject( int a )\n"
		"{\n"
		"#if !defined( CLIENT_DLL )\n"
		"	INSIDE_ARM;\n"
		"#else\n"
		"	OUTSIDE_ARM;\n"
		"#endif\n"
		"	{ nested; }\n"
		"}\n"
		"void After() { MORE_NOISE; }\n";

	const std::string strBody = BodyAfter( strFixture, "void Subject(" );
	ASSERT_FALSE( strBody.empty() );
	EXPECT_NE( strBody.find( "nested" ), std::string::npos ) << "stopped at the first nested brace";
	EXPECT_EQ( strBody.find( "MORE_NOISE" ), std::string::npos ) << "ran past the end of the body";
	EXPECT_EQ( strBody.find( "NOISE;" ), std::string::npos ) << "started before the body";

	const std::string strArm = ServerArmOf( strBody );
	ASSERT_FALSE( strArm.empty() );
	EXPECT_NE( strArm.find( "INSIDE_ARM" ), std::string::npos );
	EXPECT_EQ( strArm.find( "OUTSIDE_ARM" ), std::string::npos );

	// And both report EMPTY rather than something, for what is not there.
	EXPECT_TRUE( BodyAfter( strFixture, "void NoSuchFunction(" ).empty() );
	EXPECT_TRUE( ServerArmOf( BodyAfter( strFixture, "void After()" ) ).empty() );
}

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

// READ THE ASSERTIONS, NOT THE NAME. THE INDEX IS ASKED OF IServerEntity NOW,
// AND THERE IS NO CLASS LEFT TO ASK.
//
// `inline int CBaseEntity::GetModelIndex()` was inside `class CBaseEntity` in
// baseentity.h -- an `#ifndef SS_CBASEENTITY_ALIAS` arm no project compiled --
// and `the-close 2/N` deleted it with the class. The SDK arm of
// StandardFilterRules that asked it is gone too: it resolved through
// EntityFromEntityHandle, which answered a constant NULL, so the NULL branch
// WAS the whole function.
//
// The shape clause is now StandaloneFilterRules', and it takes the index off
// the ENGINE-FACING face -- IServerEntity::GetModelIndex -- which is the same
// question stock asked of a CBaseEntity and is answerable for both hierarchies.
// That is what is asserted, plus the absence of the class-scope one.
TEST( ModelThroughTheIndex, TheServerGetModelIndexIsTheBareFieldRead )
{
	// The class-scope absence read game/server/baseentity.h, which is compiled
	// by nothing since plan_server_novalve.md P5 and left the farm in P6; the
	// header's own absence (EveryFileWasReallyOpened) replaces it.

	const std::string &strLib = TraceFilterLib();
	ASSERT_FALSE( strLib.empty() );

	EXPECT_EQ( CountOf( strLib,
		"modelinfo->GetModelType( modelinfo->GetModel( pServerEntity->GetModelIndex() ) ) == mod_brush," ), 1 )
		<< "StandaloneFilterRules no longer takes the model type off "
		   "IServerEntity::GetModelIndex. That read is the brush clause of the "
		   "standard filter rule, and without it every managed brush is waved "
		   "through every world-only trace as though it were a static prop -- "
		   "which is what cost the blast's blocking rule outright.";

	// EntityFromEntityHandle is DELETED tree-wide -- one of four families that
	// bottomed out in IServerUnknown/IClientUnknown::GetBaseEntity(), every
	// implementation of which is `mov $0x0,%eax; ret`. Its replacement,
	// GameEntityFromEntityHandle, asks the virtual GetGameEntity() and CONTAINS
	// the old name as a substring -- so the old name is counted by subtraction,
	// not by a bare find.
	EXPECT_EQ( CountOf( strLib, "EntityFromEntityHandle" )
	         - CountOf( strLib, "GameEntityFromEntityHandle" ), 0 )
		<< "the deleted SDK arm's resolver is back. It answered a constant "
		   "NULL, so anything branching on it has only one reachable arm.";
	EXPECT_LT( 0, CountOf( strLib, "GameEntityFromEntityHandle( pHandleEntity )" ) )
		<< "the live successor is gone too, so the filters resolve nothing at "
		   "all -- which is not what the deletion of the dead resolver bought.";
}

// READ THE ASSERTIONS, NOT THE NAME. THE PER-SIDE PAIR IS GONE AND THE SIDE
// GUARD IS NOT.
//
// This fact guarded a `#ifdef CLIENT_DLL` pair in the filter rules --
// `modelinfo->GetModel( pCollide->GetModelIndex() )` on the server,
// `pCollide->GetModel()` on the client -- and existed to stop anyone collapsing
// it while the two really were different functions. Both lines were in the SDK
// arm of StandardFilterRules, which resolved through EntityFromEntityHandle and
// so had one reachable branch; the arm is deleted and
// `inline const model_t *C_BaseEntity::GetModel` went with `class C_BaseEntity`.
//
// THE CLIENT GAP THE GUARD WAS PROTECTING IS STILL OPEN, and that is what is
// asserted now: the shape clause is server-only, the client has none, and the
// file says so in its own words rather than leaving it for a reader to notice.
TEST( ModelThroughTheIndex, TheClientGetModelIsNotThatAndKeepsItsOwnArm )
{
	EXPECT_EQ( CBaseEntityH().find( "C_BaseEntity::GetModel(" ), std::string::npos )
		<< "c_baseentity.h defines a class-scope GetModel again; there is no "
		   "class for it to be scoped to.";

	const std::string &strLib = TraceFilterLib();
	ASSERT_FALSE( strLib.empty() );

	EXPECT_EQ( CountOf( strLib, "pCollide->GetModel()" ), 0 )
		<< "the client's cached-model read is back in the filter rules. It was "
		   "reachable only through EntityFromEntityHandle, which answers a "
		   "constant NULL.";

	// The side guard survives, and so does the shape clause it selects.
	EXPECT_EQ( CountOf( strLib,
		"static bool StandaloneFilterRules( IHandleEntity *pHandleEntity, int fContentsMask )" ), 1 )
		<< "the parallel hierarchy's shape clause is gone, so a managed brush "
		   "is waved through every world-only trace again.";

	const std::string strLibRaw = Read( "../sourcesharp/game/shared/trace_filter_library.cpp" );
	ASSERT_FALSE( strLibRaw.empty() );
	EXPECT_NE( strLibRaw.find( "THE CLIENT GAP IS UNCHANGED AND STILL OPEN" ), std::string::npos )
		<< "the file no longer records that StandaloneFilterRules is "
		   "`#ifndef CLIENT_DLL`, so on the CLIENT a managed entity is still "
		   "filtered as though it were a static prop. That is a known, written "
		   "-down divergence; losing the note turns it into a silent one.";
}

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

// READ THE ASSERTIONS, NOT THE NAME. THERE ARE NO EIGHT ASKS AND NO MEMBER.
//
// The eight `entity->GetSoundEmissionOrigin()` asks were the four entity-shaped
// CPASAttenuationFilter constructors' SDK arms, and the member was declared in
// `class CBaseEntity` -- both inside `#ifndef SS_CBASEENTITY_ALIAS`, both
// deleted. "It costs nothing because it is virtual" was an argument about asks
// that the compiler never saw.
//
// WHAT THE OBJECT COSTS IS STILL ZERO, and the reason is now stronger: the file
// asks the member nowhere at all. What it asks instead is
// SourceSharpEntity_t::Center(), which is the same quantity -- origin plus the
// middle of the OBB -- for both hierarchies, and the file records why that is
// the right substitution and where it is not (CBeam overrides the member away
// from the centre). Both halves are asserted, because the absence alone is
// satisfied by a file that stopped locating its sound sources at all.
TEST( VirtualsCostNothingAtLink, GetSoundEmissionOriginIsStillVirtual )
{
	// The declaration's absence read game/server/baseentity.h, which is
	// compiled by nothing since plan_server_novalve.md P5 and left the farm in
	// P6; the header's own absence (EveryFileWasReallyOpened) replaces it.

	const std::string strFilter = CodeOnly( Read( "../sourcesharp/game/server/shell_recipientfilter.cpp" ) );
	ASSERT_FALSE( strFilter.empty() );

	EXPECT_EQ( CountOf( strFilter, "GetSoundEmissionOrigin" ), 0 )
		<< "shell_recipientfilter.cpp asks GetSoundEmissionOrigin in CODE "
		   "again. Every such ask is an undefined reference the moment the "
		   "class is gone -- which it is -- and nothing at the call site says so.";

	// The live successor, in the handle-shaped constructor that replaced them.
	EXPECT_NE( strFilter.find( "const Vector vecCenter = ent.Center();" ), std::string::npos )
		<< "the handle-shaped CPASAttenuationFilter no longer takes the "
		   "entity's centre, so a managed sound source is cropped around "
		   "something else -- and the sound is unaffected when the entity has "
		   "an edict, which is exactly what makes the wrong answer invisible.";
	EXPECT_NE( strFilter.find( "if ( !ent.IsValid() )" ), std::string::npos )
		<< "the constructor no longer checks that the source resolved. An "
		   "unresolvable source must be heard by nobody, not by everyone in "
		   "the PAS of the world origin.";
}

// SOURCESHARP: THE FORWARDER MOVED INTO THE CLASS BODY, so this reads
// baseentity.h now. What is pinned is unchanged -- the body must still be a bare
// forward to Precache_Model, because that is what makes lifting it free -- and
// the expected string is byte-for-byte what it was.
//
// It moved because step 3 DELETES baseentity.cpp: under
// `using CBaseEntity = IGameEntity;` an alias is not a class scope, so
// `int CBaseEntity::PrecacheModel(...)` is a compile error rather than a
// definition. In the class body the forwarder emits no symbol and is deleted by
// the same edit that deletes the class body.
//
// The needle carries the full argument list because `PrecacheModel` is a suffix
// of nothing but IS a prefix of `PrecacheModelComponents`, and `ScriptPrecacheModel`
// ends with it -- a short needle finds the wrong declaration and then reports
// whatever brace comes next as the body.
TEST( VirtualsCostNothingAtLink, PrecacheModelIsStillABareForwardAndCanBeLifted )
{
	// READ THE ASSERTIONS, NOT THE NAME. THE LIFT HAPPENED; THERE IS NO
	// FORWARDER LEFT TO BE BARE.
	//
	// The forwarder was in the class body in baseentity.h, inside
	// `#ifndef SS_CBASEENTITY_ALIAS`, and went with the class. What made the
	// lift free was that the body was a bare forward to a free function that
	// already existed -- and that free function is now what the 734 unqualified
	// call sites reach directly, through the include baseentity.h keeps.
	//
	// Both baseentity.h facts -- that it names no PrecacheModel, and that it
	// includes precache_model.h -- are retired: the header is compiled by
	// nothing since plan_server_novalve.md P5 and left the farm in P6. The
	// absence survives as the header's own (EveryFileWasReallyOpened).

	const std::string strDecls = CodeOnly( Read( "../sourcesharp/game/server/precache_model.h" ) );
	ASSERT_FALSE( strDecls.empty() ) << "precache_model.h did not load";
	EXPECT_EQ( CountOf( strDecls, "int Precache_Model( const char *name, bool bPreload = true );" ), 1 )
		<< "the free function the call sites now reach is no longer declared.";
	// (The check that game/server/baseentity.cpp holds no out-of-line
	// CBaseEntity::PrecacheModel left with that file: neither DLL compiles it --
	// culled 2026-09-27.)
}
