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

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

// Comments stripped first and always. Every needle below would otherwise match
// the paragraph explaining what it is for, and this tree's running list of
// checks that cannot fail is nine long.
std::string StripComments( const std::string &in )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < in.size() && in[ i ] == '*' && in[ i + 1 ] == '/' ) { bInBlock = false; i++; }
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' ) { bInBlock = true; i++; continue; }
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			if ( i < in.size() ) out += '\n';
			continue;
		}
		out += in[ i ];
	}
	return out;
}

std::string Read( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	EXPECT_TRUE( file.is_open() ) << "could not open " << pszRel
		<< " -- every invariant about it would pass vacuously";
	if ( !file.is_open() )
		return std::string();

	return StripComments( std::string( ( std::istreambuf_iterator<char>( file ) ),
	                                   std::istreambuf_iterator<char>() ) );
}

int CountOf( const std::string &hay, const std::string &needle )
{
	int n = 0;
	size_t at = hay.find( needle );
	while ( at != std::string::npos ) { n++; at = hay.find( needle, at + needle.size() ); }
	return n;
}

const char *kTable      = "sourcesharp/game/server/shell_serversystems.cpp";
// WHAT EACH SIDE COMPILES is read through cmake_graph.h: server_sourcesharp
// (src/server/native/CMakeLists.txt) and client_sourcesharp
// (src/client/native/CMakeLists.txt). Until Phase 6b it was the `$File` rows
// of server_csharp.vpc and client_csharp.vpc.
const char *kShim       = "sourcesharp/game/server/shell_decals.cpp";
const char *kSteps      = "sourcesharp/game/server/shell_propdata.cpp";
const char *kDecalsCpp  = "sourcesharp/game/client/shell_decals_client.cpp";
const char *kGameSystem = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";

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

TEST( CSystemsManaged, NeitherSystemIsARowAnyMore )
{
	const std::string table = Read( kTable );
	ASSERT_FALSE( table.empty() );

	// The ROW, spelled as a row -- name and accessor between braces. Matching on
	// the bare class name would also hit the paragraph naming it as a removal,
	// which is why comments are stripped and why the needle is the row's shape.
	EXPECT_EQ( 0, CountOf( table, "{ \"CPropData\"," ) );
	EXPECT_EQ( 0, CountOf( table, "{ \"CDecalEmitterSystem\"," ) );

	// ...and the accessors that existed only so the table could name them.
	EXPECT_EQ( 0, CountOf( table, "PropDataGameSystem" ) );
	EXPECT_EQ( 0, CountOf( table, "DecalEmitterGameSystem" ) );

	// VACUITY GUARD. Every count above is zero, and zero is also what a file
	// that failed to open answers. A row that IS still there proves the needle
	// shape finds one.
	EXPECT_EQ( 1, CountOf( table, "{ \"CPhysicsHook\"," ) )
		<< "the row needle matches nothing at all, so the four zeroes above say "
		   "nothing either";
}

TEST( CSystemsManaged, BothRemovalsCarryTheirReasonInTheTable )
{
	// A row deleted without a reason is indistinguishable from one forgotten --
	// the rule the six existing non-rows are written under. Read from the FILE
	// rather than the stripped text, because the reason IS a comment.
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );

	std::ifstream f( ( std::string( pszRoot ) + "/" + kTable ).c_str(), std::ios::binary );
	ASSERT_TRUE( f.is_open() );
	const std::string raw( ( std::istreambuf_iterator<char>( f ) ),
	                       std::istreambuf_iterator<char>() );

	// ONE WINDOW PER SYSTEM, and this is the THIRD spelling of this check.
	//
	// It first asked whether "NOT DELETED: MOVED" appeared AT ALL, and stayed
	// green through a mutation that took it off one of the two -- the other
	// still carried it, so half its subject had lost its verdict. That is this
	// tree's recorded H1 defect.
	//
	// The repair was EXPECT_EQ( 2, CountOf( raw, ... ) ), and that was the SAME
	// DEFECT one step further out: a whole-file count keyed on how many
	// removals existed the day it was written. Phase A3's SECOND PAIR moved
	// CSoundEnt and CSoundControllerImp out of the same table with the same
	// phrase, the count read 4, and this fact went red on a tree where all four
	// of its subjects were correct. Stale by construction -- exactly like a
	// needle keyed on the ABI version, which this tree has had to fix three
	// times.
	//
	// So: each system's OWN block, bounded at the next entry. It cannot go
	// stale when a later phase adds a fifth removal, and it still cannot be
	// satisfied by the other system's verdict.
	static const char *kBlocks[] = { "//   CPropData", "//   CDecalEmitterSystem" };

	for ( size_t i = 0; i < sizeof( kBlocks ) / sizeof( kBlocks[ 0 ] ); i++ )
	{
		const size_t at = raw.find( kBlocks[ i ] );

		EXPECT_NE( at, std::string::npos )
			<< kBlocks[ i ] << " is not in the table's list of what is NOT a row";
		if ( at == std::string::npos )
			continue;

		// The window ends at the next "//   C" entry. Running it to
		// end-of-file is the shape that lets one system's verdict satisfy
		// another's check.
		const size_t next = raw.find( "\n//   C", at + 1 );
		const std::string block = raw.substr( at, next == std::string::npos
		                                          ? std::string::npos : next - at );

		EXPECT_NE( block.find( "NOT DELETED: MOVED" ), std::string::npos )
			<< kBlocks[ i ] << "'s own block does not say it MOVED, so a reader "
			   "cannot tell it from the ones that were deleted";
	}
}

//=============================================================================//
// 2. THE PRE-ENTITY HALF HAS A CALLER NOW, AND HAD NONE BEFORE
//=============================================================================//

TEST( CSystemsManaged, TheTablesManagedRowDrivesThePreEntityHalf )
{
	const std::string table = Read( kTable );
	ASSERT_FALSE( table.empty() );

	// The row is the ONLY thing on the server that can reach this half: the
	// pass is driven from world_adapter.cpp's call to
	// SourceSharp_LevelInitPreEntityAllSystems, which walks these rows.
	EXPECT_EQ( 1, CountOf( table, "SourceSharp_ManagedLevelInitPreEntity(" ) )
		<< "exactly one call, from the row that owns the managed frame";
	EXPECT_NE( table.find( "virtual void LevelInitPreEntity() OVERRIDE" ), std::string::npos )
		<< "and it is an override of the stage, not a call bolted onto another one";
}

TEST( CSystemsManaged, EachManagedEventPointStillHasExactlyOneBody )
{
	// Phase 7's invariant, extended to the fifth point and then to the SIXTH.
	// Two DRIVERS was the design; two BODIES would be the bug, and it is the
	// kind that agrees with itself until somebody edits one of them. Counted
	// across BOTH files, because the realistic place a second body gets written
	// is the table's own file -- the row is the caller that "only needs a little
	// more".
	//
	// THE LIST GROWS WITH THE POINTS AND THAT IS THE OPPOSITE OF THE DEFECT THIS
	// FILE ALREADY RECORDS. BothRemovalsCarryTheirReasonInTheTable was stale by
	// construction because it counted how many REMOVALS existed the day it was
	// written -- a population that later phases add to for reasons unrelated to
	// the fact. This list is an ENUMERATION of the managed event points, and a
	// point that is not in it is a point nothing checks: it has to grow, and a
	// phase that adds a point and not a row here has left its own body
	// unchecked. A3's fifth system added SourceSharp_ManagedLevelShutdownPreEntity.
	const std::string gs = Read( kGameSystem );
	const std::string table = Read( kTable );
	ASSERT_FALSE( gs.empty() );
	ASSERT_FALSE( table.empty() );

	static const char *kBodies[] = {
		"void SourceSharp_ManagedLevelInitPreEntity(",
		"void SourceSharp_ManagedLevelInit(",
		"void SourceSharp_ManagedLevelShutdownPreEntity(",
		"void SourceSharp_ManagedLevelShutdown(",
		"void SourceSharp_ManagedFramePreThink(",
		"void SourceSharp_ManagedFrameThink(",
	};

	for ( size_t i = 0; i < sizeof( kBodies ) / sizeof( kBodies[ 0 ] ); i++ )
	{
		EXPECT_EQ( 1, CountOf( gs, kBodies[ i ] ) + CountOf( table, kBodies[ i ] ) )
			<< kBodies[ i ];
	}
}

TEST( CSystemsManaged, ThePreEntityBodyIsGuardedOffTheClient )
{
	// sourcesharp_gamesystem.cpp compiles into client.so as well, and the four
	// bodies beside this one are inside one `#ifndef CLIENT_DLL` block that runs
	// for several hundred lines. A body written just outside it exists on the
	// client, where ServerMapLoadPreEntity is a null slot -- so it would be a
	// function nobody calls on one side and the phase's whole mechanism on the
	// other. That cost a build in this phase and is pinned rather than
	// remembered.
	const std::string gs = Read( kGameSystem );
	ASSERT_FALSE( gs.empty() );

	const size_t body = gs.find( "void SourceSharp_ManagedLevelInitPreEntity(" );
	const size_t four = gs.find( "void SourceSharp_ManagedLevelInit(" );
	ASSERT_NE( body, std::string::npos );
	ASSERT_NE( four, std::string::npos );

	// BOTH DIRECTIONS, and the backward one is the load-bearing half.
	//
	// Asked only "is there an #endif between this body and the next", the fact
	// stayed green through the realistic edit -- closing the block ABOVE the
	// body and reopening it inside, which puts the body outside the guard and
	// leaves nothing between it and its neighbour.
	const size_t opener = gs.rfind( "#ifndef CLIENT_DLL", body );
	ASSERT_NE( opener, std::string::npos );

	EXPECT_EQ( 0, CountOf( gs.substr( opener, body - opener ), "#endif" ) )
		<< "the guard that encloses the pre-entity body was closed before it, "
		   "so the body is on the client's side of the line";
	EXPECT_EQ( 0, CountOf( gs.substr( body, four - body ), "#endif" ) )
		<< "the pre-entity body is outside the #ifndef CLIENT_DLL block the "
		   "other four are in";
}

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

TEST( CSystemsManaged, TheClientsCopyKeepsItsOwnBaseUnguarded )
{
	const std::string decals = Read( kDecalsCpp );
	ASSERT_FALSE( decals.empty() );

	// FOLLOWED THE WORK at plan_cworld_trace.md T4, and the RENAME is the honest
	// part: the name said "keeps its STOCK base", and after T4 the client has no
	// stock base either. What the fact is for did not change -- the client's decal
	// system must still have a base, unguarded, in a file only the client
	// compiles -- but the claim in the name would have been false while the body
	// went on being true, which is how a test stops meaning what it says.
	//
	// The file is the client's alone, so the Phase 7a scaffolding went with the
	// removal: no CServerSystem, no shell_serversystem.h. What is left is
	// CClientSystem, and the parity harness is what says the client's
	// PREPROCESSED text did not move.
	EXPECT_EQ( 0, CountOf( decals, "CServerSystem" ) )
		<< "the server's base has no business in a file the server does not compile";
	EXPECT_EQ( 0, CountOf( decals, "shell_serversystem.h" ) );

	// THE DECLARATION LINE, not "the string appears somewhere". The class's
	// CONSTRUCTOR names the base too, so `CountOf( "CAutoGameSystem" ) > 0`
	// stayed true through a mutation that took the base off the declaration --
	// leaving a class that is not a game system with an initialiser list that
	// still says it is.
	EXPECT_EQ( 1, CountOf( decals,
		"class CShellDecalSystemClient : public IDecalEmitterSystem, public CClientSystem" ) )
		<< "the client's own base is still on the class, or the client's decal "
		   "system stopped initialising -- silently, because a decal table that "
		   "was never inited answers -1 exactly as a working one does for a "
		   "decal that is not there";

	// AND IT IS A ROW. Before T4 the base was CAutoGameSystem and having it WAS
	// registering; now the base does nothing on its own and the only thing that
	// runs the system is being named in kClientSystems[]. The failure mode in the
	// message above -- a table that was never inited answering -1 like a working
	// one -- is reachable from either half, so both are asserted.
	const std::string table = Read( "sourcesharp/game/client/shell_clientsystems.cpp" );
	ASSERT_FALSE( table.empty() );
	// The ROW, not the name: the accessor is also forward-declared at the top of
	// that file, so counting the bare name reads 2 and would keep reading 1 with
	// the row deleted and the declaration left behind -- which is the exact
	// no-row state this asserts against.
	EXPECT_EQ( 1, CountOf( table, "\"CDecalEmitterSystem\",                ClientSystem_DecalEmitter" ) )
		<< "the client's decal system is not a kClientSystems[] row";

	// plan_client_novalve C2 (lane c2-sims): the binding answers stock's three
	// interface methods and runs stock's three system steps from managed code,
	// once each. A method that answered natively would be a second
	// implementation of a table with one owner.
	EXPECT_EQ( 1, CountOf( decals, "pArea->IndexForName(" ) );
	EXPECT_EQ( 1, CountOf( decals, "pArea->NameForIndex(" ) );
	EXPECT_EQ( 1, CountOf( decals, "pArea->TranslateForGameMaterial(" ) );
	EXPECT_EQ( 1, CountOf( decals, "pArea->Init()" ) );
	EXPECT_EQ( 1, CountOf( decals, "pArea->LevelInitPreEntity()" ) );
	EXPECT_EQ( 1, CountOf( decals, "pArea->Shutdown()" ) );
}

//=============================================================================//
// 4. THE SHIM: STOCK'S INTERFACE, ANSWERED FROM MANAGED CODE
//=============================================================================//

TEST( CSystemsManaged, TheServersDecalSystemIsAShimOverTheManagedTable )
{
	const std::string shim = Read( kShim );
	ASSERT_FALSE( shim.empty() );

	// All three of IDecalEmitterSystem's methods reach managed code, once each.
	// A method that answered natively would be a second implementation of a
	// table with one owner.
	EXPECT_EQ( 1, CountOf( shim, "pArea->ServerDecalIndexForName(" ) );
	EXPECT_EQ( 1, CountOf( shim, "pArea->ServerDecalNameForIndex(" ) );
	EXPECT_EQ( 1, CountOf( shim, "pArea->ServerDecalTranslateForGameMaterial(" ) );

	// And the server reaches it through ss_decals.h's three entry points since
	// P5 lane SP: `decalsystem` lost its last caller (world_adapter.cpp) and
	// left, so a second spelling of the same table cannot come back unnoticed.
	// Two of the three went in the server gc cull (2026-10-01): nothing called
	// ss::DecalNameForIndex or ss::DecalTranslateForGameMaterial.
	EXPECT_EQ( 1, CountOf( shim, "int ss::DecalIndexForName(" ) );
	EXPECT_EQ( 0, CountOf( shim, "ss::DecalNameForIndex(" ) );
	EXPECT_EQ( 0, CountOf( shim, "ss::DecalTranslateForGameMaterial(" ) );
	EXPECT_EQ( 0, CountOf( shim, "decalsystem =" ) )
		<< "the Valve-typed decalsystem pointer is back; nothing on the server reads it";
}

TEST( CSystemsManaged, EveryShimMethodGuardsANullManagedTable )
{
	const std::string shim = Read( kShim );
	ASSERT_FALSE( shim.empty() );

	// The shim exists from static-init time and the CLR does not. An unguarded
	// forward is a null dereference at whatever the first thing to ask for a
	// decal happens to be -- and on a server that is a map load.
	// Since plan_engineapi_vtables E3 the table is the managed-built
	// IManagedResources area, NULL outside a load.
	EXPECT_EQ( 3, CountOf( shim, "pArea == NULL" ) )
		<< "one guard per interface method";
}

TEST( CSystemsManaged, TheShimHoldsNoFileScopeState )
{
	const std::string shim = Read( kShim );
	ASSERT_FALSE( shim.empty() );

	// The project rule, and the buffers are the case it is about: two `char`
	// arrays at file scope would be the obvious way to answer a `const char *`.
	// They are MEMBERS, and the instance itself is a function-local static --
	// the population tools/statics-audit deliberately does not cover and the
	// rule permits.
	EXPECT_EQ( 0, CountOf( shim, "\nstatic char " ) );

	// BY NAME, not by shape. `CountOf( "\tchar m_sz" ) == 2` was the first
	// spelling and a mutation that RENAMED one buffer left it green -- the
	// count is the same whatever the members are called, so it was a fact about
	// there being two of something.
	EXPECT_EQ( 1, CountOf( shim, "char m_szName[ 260 ];" ) );
	EXPECT_EQ( 1, CountOf( shim, "char m_szTranslated[ 260 ];" ) )
		<< "both answer buffers are fields of the shim, by the names the "
		   "methods above write into";
	EXPECT_EQ( 1, CountOf( shim, "static CShellDecalEmitter s_Emitter;" ) )
		<< "the one instance is function-local";
}

//=============================================================================//
// 5. CPropData: THE HALF THAT MOVED, AND THE HALF THAT DID NOT
//=============================================================================//

// plan_server_novalve.md P2.4: THE OTHER HALF MOVED TOO. The tree's one
// consumer, SourceSharp_EntityGetPropData, is C#'s (PropData.ModelPropData),
// so the file that held the load and free steps is gone and IShimLevel's two
// slots are retired -- empty bodies, kept because the interface is
// append-only.
TEST( CSystemsManaged, ThePropDataStepsAreRetiredAndTheirFileIsGone )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );

	std::ifstream gone( ( std::string( pszRoot ) + "/" + kSteps ).c_str() );
	EXPECT_FALSE( gone.is_open() ) << kSteps << " is back";

	// Comment-stripped, then whitespace-free: the bodies are a comment and
	// nothing else.
	std::string level;
	for ( char c : Read( "sourcesharp/game/server/shell_level.cpp" ) )
		if ( !isspace( (unsigned char)c ) )
			level.push_back( c );
	ASSERT_FALSE( level.empty() );
	EXPECT_EQ( 1, CountOf( level, "virtualvoidPropDataLoadFile()OVERRIDE{}" ) )
		<< "PropDataLoadFile's body is not the retired, empty one";
	EXPECT_EQ( 1, CountOf( level, "virtualvoidPropDataClearFile()OVERRIDE{}" ) )
		<< "PropDataClearFile's body is not the retired, empty one";
	EXPECT_EQ( 0, CountOf( level, "g_PropDataSystem" ) );
}

//=============================================================================//
// 6. THE FILE READ LEFT NATIVE CODE (2026-10-19): the user exposed file handles
// to managed, and the read is Open/Size/Read/Close through IBaseFileSystem's
// forwarders (EngineBaseFileSystem.ReadAll). Its three rules -- no truncation,
// a missing file is not an empty one, the GAME path id -- are GameFileSideTests'.
//=============================================================================//
