//==== SourceSharp — the server utility mechanism that names no entity =======//
//
// plan_entity.md Phase 4 exit, lane p4-util. `game/server/util.cpp` is one of
// SCC 11's members, and seven families of entity-free mechanism left it for
// `sourcesharp/game/server/server_util_library.cpp`.
//
// FIVE FAILURES THIS PINNED, none of which any build or gate here can see.
// util.cpp has since left the source tree (server.so does not compile it), so
// the halves of 1-3 that read it were culled; what is left pins the library.
//
//   1. A COPY LEFT BEHIND in `util.cpp`. Two bodies for one function link when
//      only one is reachable, and a test that reads the NEW address stays green
//      while the game runs the old one.
//
//   2. `g_NullSurface` SPLIT INTO TWO. Stock's was a FILE STATIC, and
//      `UTIL_SetTrace` — which could not travel, because it calls
//      `CBaseEntity::Instance`, a symbol that is not a cross-edge today, so
//      moving it would ADD one — still reads it. So the object moved with
//      EXTERNAL linkage and util.cpp declares it `extern`. Copying it instead
//      is neither a compile error nor a link error: it is two "**empty**"
//      surfaces that drift.
//
//   3. THE CHECK-CLIENT SUBSYSTEM SPLIT FROM ITS STATE. The shim named exactly
//      two of its eight members (`UTIL_SetClientVisibilityPVS` and the accessor
//      `shell_serversystems.cpp` registers). The other six all read
//      `g_CheckClient`'s fields directly and three are file statics no other TU
//      can name, so moving the two the shim named would have EXPORTED the state
//      rather than moved it.
//
//   4. A ONE-SIDED SOURCE ROW, in the other direction from the shared libraries':
//      `util.cpp` is SERVER-ONLY, so this file must be in server_sourcesharp's
//      source list (src/server/native/CMakeLists.txt) and NOT in
//      client_sourcesharp's (src/client/native/CMakeLists.txt). A client row would compile `engine->PrecacheDecal`
//      and `CServerSystem` into client.so.
//
//   5. `UTIL_IsCommandIssuedByServerAdmin` CLAIMED TWICE. It fits every
//      sentence above and is deliberately NOT here: lane `phase4-exit` has
//      already relocated it to `shell_commandclient.cpp`, which defines the one
//      symbol its body needs. Two lanes moving one body to two addresses is a
//      duplicate definition, so this suite asserts this file does NOT hold it.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <fstream>
#include <sstream>
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

const char *const kLibrary   = "sourcesharp/game/server/server_util_library.cpp";
const char *const kStockCpp  = "game/server/util.cpp";

// What each target compiles is read through cmake_graph.h: compile_commands.json
// cross-checked against the target's CMakeLists source list. Paths are
// farm-relative, the CMakeLists spelling.
const char *const kLibraryRow = "sourcesharp/game/server/server_util_library.cpp";

// The client's proof-of-read: a shim the client compiles and no eviction can
// take, so "the client does not compile the library" is never answered by a
// reader that saw nothing.
const char *const kClientProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

const char *const kMovedDefinitions[] = {
	"void UTIL_ClearTrace( trace_t &trace )",
	// The three temp-entity emitters, and the four helpers they could not
	// leave behind. 4e listed the emitters as unmoved because at that point
	// moving one CONVERTED an edge instead of removing one -- the helpers were
	// still in util.cpp. Taking all seven leaves nothing next door to land on.
	"void UTIL_BloodStream( const Vector &origin, const Vector &direction, int color, int amount )",
	"void UTIL_Smoke( const Vector &origin, const float scale, const float framerate )",
};
const size_t kNumMoved = sizeof( kMovedDefinitions ) / sizeof( kMovedDefinitions[ 0 ] );

// GONE IN THE SERVER GC CULL (2026-10-01): no server object called these, and
// server.so's --print-gc-sections link dropped them. Asserted absent.
const char *const kGoneInTheCull[] = {
	"void DBG_AssertFunction( bool fExpr, const char *szExpr, const char *szFile, int szLine, const char *szMessage )",
	"static edict_t *UTIL_FindClientInPVSGuts(edict_t *pEdict, unsigned char *pvs, unsigned pvssize )",
	// Not the cull: plan_server_native_v2 T4d folded it into the managed
	// DecalPrecache (IVEngineServer's PrecacheDecal/IsDecalPrecached forwarders),
	// which was its one importer's (SourceSharp_PrecacheDecal's) replacement.
	"int UTIL_PrecacheDecal( const char *name, bool preload )",
	"edict_t *UTIL_FindClientInPVS(edict_t *pEdict)",
	"void UTIL_Bubbles( const Vector& mins, const Vector& maxs, int count )",
	"void UTIL_BubbleTrail( const Vector& from, const Vector& to, int count )",
	"float UTIL_WaterLevel( const Vector &position, float minz, float maxz )",
	"float UTIL_FindWaterSurface( const Vector &position, float minz, float maxz )",
	"bool UTIL_IsValidEntity( IServerEntity *pEnt )",
	// GONE IN plan_server_native_min P1: the check-client system (its two PVS
	// bitsets had no reader and its walk skipped every candidate) and
	// UTIL_VarArgs (its one caller formats into a local).
	"const char *UTIL_VarArgs( const char *format, ... )",
	"static int UTIL_GetNewCheckClient( int check )",
	"static edict_t *UTIL_GetCurrentCheckClient()",
	"void UTIL_SetClientVisibilityPVS( edict_t *pClient, const unsigned char *pvs, int pvssize )",
	"CServerSystem *CheckClientGameSystem()",
	"class CCheckClient : public CServerSystem",
	"class CWaterTraceFilter : public CTraceFilter",
	"CCheckClient g_CheckClient(",
	// GONE IN plan_server_native_v2 T5: no server object imported either (nm U
	// over the link edge). The managed motor port holds the axis snap
	// (MotorTests).
	"void UTIL_SnapDirectionToAxis( Vector &direction, float epsilon )",
	"void UTIL_ImpactTrace( trace_t *pTrace, int iDamageType, const char *pCustomImpactName )",
};

// The objects. Each of these is state, and each fails a different way if it is
// split from the code that reads it.
const char *const kMovedObjects[] = {
	// sv_strict_notarget's DECLARATION is managed now
	// (SourceSharp.Server/ConVars.cs); what must be in this file is the by-name
	// ref, because UTIL_FindClientInPVSGuts still reads it here and is asked
	// per candidate. See TheNotargetKnobIsDeclaredInManagedCode below for the
	// other half of the pair.
	"CManagedConVarRef sv_strict_notarget(",
};
const size_t kNumObjects = sizeof( kMovedObjects ) / sizeof( kMovedObjects[ 0 ] );

} // anonymous namespace

//=============================================================================//
// 1. EXACTLY ONE DEFINITION, AND IT IS THE SHIM'S
//=============================================================================//

TEST( ServerUtilLibrary, EveryMovedDefinitionIsInTheShimFile )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() ) << kLibrary << " did not load";

	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedDefinitions[ i ] ) )
			<< kMovedDefinitions[ i ] << " is not defined exactly once in "
			<< kLibrary;
	}
	for ( size_t i = 0; i < sizeof( kGoneInTheCull ) / sizeof( kGoneInTheCull[ 0 ] ); i++ )
	{
		EXPECT_EQ( 0u, CountOf( lib, kGoneInTheCull[ i ] ) )
			<< kGoneInTheCull[ i ] << " is defined in " << kLibrary
			<< " again, and nothing called it when the cull deleted it.";
	}
}

//=============================================================================//
// 2. THE NULL SURFACE IS ONE OBJECT, NOT TWO
//=============================================================================//

TEST( ServerUtilLibrary, TheNullSurfaceIsDefinedOnceWithExternalLinkage )
{
	// The util.cpp half (no second copy there, and its `extern` declaration)
	// was culled when server.so stopped compiling util.cpp and it left the
	// source tree.
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() );

	EXPECT_EQ( 1u, CountOf( lib, "csurface_t\tg_NullSurface = { \"**empty**\", 0 };" ) )
		<< "the definition must be in " << kLibrary;
	EXPECT_EQ( 0u, CountOf( lib, "static csurface_t\tg_NullSurface" ) )
		<< "it must NOT be a file static here -- util.cpp's UTIL_SetTrace reads "
		   "the same object.";
}

//=============================================================================//
// 3. THE CHECK-CLIENT SUBSYSTEM CAME AS A UNIT
//=============================================================================//

TEST( ServerUtilLibrary, TheCheckClientStateTravelledWithEveryReaderOfIt )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() );

	for ( size_t i = 0; i < kNumObjects; i++ )
	{
		EXPECT_EQ( 1u, CountOf( lib, kMovedObjects[ i ] ) )
			<< kMovedObjects[ i ] << " is not in " << kLibrary;
	}
	// The other half -- no g_CheckClient reader left in util.cpp -- was culled
	// when server.so stopped compiling util.cpp and it left the source tree.
}

//=============================================================================//
// 4. THE SERVER COMPILES IT AND THE CLIENT DOES NOT
//=============================================================================//

TEST( ServerUtilLibrary, TheServerCompilesIt )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kLibraryRow ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) does not "
		   "compile " << kLibraryRow << ": "
		<< "server.so still compiles util.cpp, which no longer defines "
		   "UTIL_ClearTrace.";
}

TEST( ServerUtilLibrary, TheClientDoesNotCompileIt )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kClientProofOfRead ) )
		<< "client_sourcesharp does not compile " << kClientProofOfRead
		<< " -- the reader is not reading the client's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kLibraryRow ) )
		<< "client_sourcesharp (src/client/native/CMakeLists.txt) compiles "
		<< kLibraryRow << ": " "util.cpp is server-only and so is this file; a client row would "
		   "compile CServerSystem and engine->PrecacheDecal into client.so.";
}

TEST( ServerUtilLibrary, TheStockTranslationUnitHasLeftAndTheLibraryHasNot )
{
	// INVERTED, NOT DELETED. This read `EXPECT_EQ( 1u, ... )` with the note
	// "the Phase 4 exit removes SCC 11's vpc rows in ONE commit; this lane moves
	// behaviour and leaves every stock row exactly where it is." That exit has
	// LANDED -- armed-rehearsal's fd5f3bc8d, "the removal-set rows out of both
	// vpcs" -- so the fact came true and the assertion went red, which is the
	// only way anyone would have found out that it had.
	//
	// The fact is worth keeping in BOTH polarities, so it is stated as the pair
	// it always was: the stock TU is gone AND the library that took its
	// behaviour is still compiled. Asserting only the absence would pass on a
	// build that had dropped both, which is exactly the accident this file
	// exists to catch -- every UTIL_ symbol below would then be measured against
	// a translation unit nothing links.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kStockCpp ) )
		<< "game/server/util.cpp is in server_sourcesharp's source list again. It is a removal-set file "
		   "and its symbols are this library's now; compiling both gives the "
		   "link two definitions of every one of them.";

	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kLibraryRow ) )
		<< "the stock TU is gone AND so is the library that replaced it. Every "
		   "UTIL_ fact below is then about code nothing compiles.";
}

//=============================================================================//
// 5. THE CONTESTED SYMBOL IS NOT CLAIMED HERE
//=============================================================================//

TEST( ServerUtilLibrary, TheAdminCommandPredicateIsLeftToTheOtherLane )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() );

	EXPECT_EQ( 0u, CountOf( lib, "bool UTIL_IsCommandIssuedByServerAdmin( void )" ) )
		<< "lane phase4-exit has already relocated this to "
		   "shell_commandclient.cpp, which defines UTIL_GetCommandClientIndex, "
		   "the one symbol its body needs. Two addresses for one body is a "
		   "duplicate definition at link.";
}

//=============================================================================//
// 6. THE HEADER DID NOT MOVE, SO NO CALLER DID -- RETIRED
//
// EveryDeclarationStaysInUtilH asserted six declarations stayed in
// game/server/util.h. game/server/util.h is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6, so the fact lost its
// subject.
//=============================================================================//

//=============================================================================//
// 7. THE NEEDLES ARE CODE, NOT PROSE
//=============================================================================//

TEST( ServerUtilLibrary, NoNeedleIsSatisfiedByProseAlone )
{
	const std::string libRaw  = ReadFile( kLibrary );
	const std::string libCode = StripComments( libRaw );
	ASSERT_FALSE( libCode.empty() );

	for ( size_t i = 0; i < kNumMoved; i++ )
	{
		EXPECT_EQ( CountOf( libRaw, kMovedDefinitions[ i ] ),
		           CountOf( libCode, kMovedDefinitions[ i ] ) )
			<< kMovedDefinitions[ i ] << " occurs in a COMMENT in " << kLibrary;
	}
	for ( size_t i = 0; i < kNumObjects; i++ )
	{
		EXPECT_EQ( CountOf( libRaw, kMovedObjects[ i ] ),
		           CountOf( libCode, kMovedObjects[ i ] ) )
			<< kMovedObjects[ i ] << " occurs in a COMMENT in " << kLibrary;
	}
}

//=============================================================================//
// 8. THE EMITTERS' MODEL INDICES ARE DECLARED HERE AND DEFINED IN combatweapon
//
// g_sModelIndexSmoke, g_sModelIndexBubbles, g_sModelIndexBloodDrop and
// g_sModelIndexBloodSpray are combatweapon.cpp's. util.cpp reached them by
// `extern short`, and so does this file. DEFINING one here instead links --
// a `short x;` at namespace scope is a tentative definition -- and gives the
// emitters a model index that is always zero, which draws nothing and reports
// nothing.
//=============================================================================//

TEST( ServerUtilLibrary, TheModelIndicesAreDeclaredNeverDefined )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() );

	static const char *const kIndices[] = {
		"g_sModelIndexSmoke", "g_sModelIndexBubbles",
		"g_sModelIndexBloodDrop", "g_sModelIndexBloodSpray",
	};
	for ( size_t i = 0; i < 4; i++ )
	{
		const std::string decl = std::string( "extern short " ) + kIndices[ i ] + ";";
		EXPECT_EQ( 1u, CountOf( lib, decl ) )
			<< decl << " is missing from " << kLibrary;
		const std::string def = std::string( "\nshort " ) + kIndices[ i ];
		EXPECT_EQ( 0u, CountOf( lib, def ) )
			<< kIndices[ i ] << " is DEFINED in " << kLibrary
			<< ". combatweapon.cpp owns it; a second one is always zero and "
			   "the emitter that reads it draws nothing, silently.";
	}
}

//=============================================================================//
// 9. THE ONE CONVERSION THE EMITTER MOVE COST, RECORDED RATHER THAN HIDDEN
//
// UTIL_BubbleTrail calls IntersectRayWithAAPlane, which public/collisionutils.cpp
// defines: that file went from one shim->stock edge to two. It is one of the
// four leaves plan_entity.md 6 says STAYS in the build, so the edge is not a
// debt any lane has to clear -- but it is a cost and it is named.
//
// The German-blood arm of UTIL_BloodStream reads `developer`-style ConVar
// g_Language, and it was NOT deleted although it looks unreachable. Deleting an
// arm that looks dead is the shape of all six recorded silent losses.
//=============================================================================//

TEST( ServerUtilLibrary, TheGermanBloodArmCameAcrossRatherThanBeingDeleted )
{
	const std::string lib = StripComments( ReadFile( kLibrary ) );
	ASSERT_FALSE( lib.empty() );

	// Spelled with ss_shareddefs.h's names since P5 (the TU is on the R5
	// include set); ss_shareddefs_oracle_test holds them to upstream's
	// LANGUAGE_GERMAN and BLOOD_COLOR_RED.
	EXPECT_EQ( 1u, CountOf( lib, "if ( s_Language.GetInt() == ss::kLanguageGerman && color == ss::BLOOD_COLOR_RED )" ) )
		<< "UTIL_BloodStream's German-blood arm is stock's behaviour and is "
		   "live. A move is a relocation only while every arm comes with it.";
}

//=============================================================================//
// THE NOTARGET KNOB: DECLARED IN MANAGED CODE, READ NATIVELY BY NAME
//
// plan_entity.md §6: registration is mechanism the engine owns, but no
// ConVar's default or semantics is declared natively. sv_strict_notarget
// decides whether a notarget entity is invisible to the AI rather than merely
// untargeted, which is as squarely "decides what the game does" as a convar
// gets -- so the declaration moved and the rule did not.
//
// Both halves are asserted, because either alone is a bug that runs: a
// declaration with no reader is a setting that does nothing, and a reader with
// no declaration reads zero forever.
//=============================================================================//

TEST( ServerUtilLibrary, TheNotargetKnobIsDeclaredInManagedCode )
{
	const std::string cs = ReadFile( "sourcesharp/managed/SourceSharp.Server/ConVars.cs" );
	ASSERT_FALSE( cs.empty() ) << "SourceSharp.Server/ConVars.cs did not load";

	EXPECT_EQ( 1u, CountOf( cs, "[ConVar(\"sv_strict_notarget\", \"0\"" ) )
		<< "sv_strict_notarget must be declared exactly once in managed code, "
		   "with stock's default of 0.";
}

TEST( ServerUtilLibrary, TheNotargetKnobIsNotAlsoDeclaredNatively )
{
	const std::string code = ReadFile( kLibrary );
	ASSERT_FALSE( code.empty() ) << kLibrary << " did not load";

	EXPECT_EQ( 0u, CountOf( code, "ConVar sv_strict_notarget(" ) )
		<< "a native declaration is back beside the managed one, so the game "
		   "has two convars of this name and nothing says which one the "
		   "notarget test read.";
}
