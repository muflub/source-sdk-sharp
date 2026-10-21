//====== SourceSharp — CSourceSharpStandaloneSystem, stage by stage ===========//
//
// plan_gamedll_after.md Phase A3, the fifth and last "C" system.
//
// IT IS THE ODD ONE OUT AND THE DISPOSITION IS NOT A PORT. The other four were
// stock SDK systems pulled across a boundary. This one is SourceSharp's own,
// and Part 9a's note about it — "already ours" — reads as an argument that it
// moves easily. Measured, it is the opposite: this system is the NATIVE FLOOR
// the managed hierarchy stands on, and a floor cannot stand on itself.
//
// So it was taken apart by STAGE, because the three stages have three different
// answers and the system's name hides that:
//
//   Init()                       DELETED. Dead by construction -- the tiers are
//                                already registered by the time the table walks
//                                this row, from the managed publish path.
//
//   FrameUpdatePostEntityThink() MOVED. A managed IFrameLifecycle participant
//                                driving two named native steps, so C# owns
//                                both the position and the "players first"
//                                order that used to be a C++ comment.
//
//   LevelShutdownPreEntity()     MOVED, and it needed an event point that did
//                                not exist: LifecycleDriver.LevelShutdownPreEntity
//                                had NO PRODUCTION CALLER -- the THIRD stage
//                                found in that state in this plan.
//
// SOURCE-SCANNED, AND THAT IS FORCED RATHER THAN CHOSEN, exactly as
// c_systems_managed_test.cpp records: every subject lives in a translation unit
// that includes cbase.h's whole world, so none can be linked into a gtest.
// What can be checked here is the set of properties no compiler and no `nm`
// count can see.
//
//=============================================================================//

#include <cctype>
#include <cstring>
#include <fstream>
#include <string>
#include <sys/stat.h>

#include "gtest/gtest.h"

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
// checks that cannot fail is seventeen long.
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

std::string ReadRaw( const char *pszRel )
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

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

std::string Read( const char *pszRel )
{
	return StripComments( ReadRaw( pszRel ) );
}

// Whether a file is in the tree at all, for a fact that a file stays gone (a
// missing file there is the pass, so it is not read through ReadRaw, whose
// miss is a failure).
bool Exists( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return true;
	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), std::ios::binary );
	return file.is_open();
}

int CountOf( const std::string &hay, const std::string &needle )
{
	int n = 0;
	size_t at = hay.find( needle );
	while ( at != std::string::npos ) { n++; at = hay.find( needle, at + needle.size() ); }
	return n;
}

const char *kStandalone = "sourcesharp/game/server/shell_standalone.cpp";
const char *kGameDll    = "sourcesharp/game/server/shell_gamedll.cpp";
const char *kModule     = "sourcesharp/managed/SourceSharp.Server/ServerModule.cs";
const char *kPublish    = "sourcesharp/managed/SourceSharp.Shared.Interop/StandaloneNetStates.cs";
// kServerSystems[]'s file until plan_server_native_v2 T4e made its order
// managed's; read only to hold that it stays gone.
const char *kTable      = "sourcesharp/game/server/shell_serversystems.cpp";
const char *kLevel      = "sourcesharp/game/server/shell_level.cpp";
const char *kSystems    = "sourcesharp/managed/SourceSharp.Server/ServerSystems.cs";
const char *kStages     = "sourcesharp/managed/SourceSharp.Server/LifecycleStages.cs";
const char *kGameSystem = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
// Where a slot is wired since the struct fill went: each side's generated
// shim-area forwards, and the slot table they are generated from. src/../docs
// is the repo's docs/ through the farm's link.
const char *kServerAreasImpl = "sourcesharp/game/shared/shell_shim_areas.inc";
const char *kClientAreasImpl = "sourcesharp/game/shared/shell_shim_areas_client.inc";
const char *kSlotTable       = "../docs/engineapi-areas.tsv";
const char *kAbiHeader  = "sourcesharp/host/sourcesharp.h";
const char *kServerDll  = "sourcesharp/managed/SourceSharp.Server/ServerGameDll.cs";

//-----------------------------------------------------------------------------
// HOW MANY GENERATED SHIM-AREA FORWARDS IN `inc` ARE `CShim...::<slot>( ... )`
// WITH THE ONE-LINE BODY `{ return ::<body>( ... ); }` -- or with ANY body when
// `body` is empty. Since plan_client_vtables CC-last a slot is wired by such a
// forward on each side: shell_shim_areas.inc on the server,
// shell_shim_areas_client.inc on the client (both GENERATED by tools/shim-areas
// from docs/engineapi-areas.tsv), and the struct fill that used to say
// `engineApi.<Slot> = <Body>;` is gone. Whitespace is ignored, so the
// generator's line wrapping is not part of the needle; the class name must
// carry "CShim", so the body's own `::<body>(` never counts as a slot.
//-----------------------------------------------------------------------------
int ForwardCount( const std::string &inc, const std::string &slot, const std::string &body )
{
	int n = 0;
	const std::string needle = "::" + slot + "(";
	for ( size_t at = inc.find( needle ); at != std::string::npos; at = inc.find( needle, at + 1 ) )
	{
		size_t b = at;
		while ( b > 0 && ( isalnum( (unsigned char)inc[ b - 1 ] ) || inc[ b - 1 ] == '_' ) )
			b--;
		if ( inc.substr( b, at - b ).find( "CShim" ) == std::string::npos )
			continue;

		const size_t semi = inc.find( ';', at );
		std::string window;
		for ( size_t k = at; k < inc.size() && k <= semi; k++ )
		{
			if ( !isspace( (unsigned char)inc[ k ] ) )
				window += inc[ k ];
		}
		if ( window.find( "){return::" + body ) != std::string::npos
		     && ( body.empty() || window.find( "){return::" + body + "(" ) != std::string::npos ) )
			n++;
	}
	return n;
}

} // namespace


//=============================================================================//
// 1. THE DEAD Init(), AND WHY ITS ABSENCE IS A MEASUREMENT
//
// The claim being pinned is an ORDER, and it lives in four files across a
// language boundary. Nothing in any one of them can state it, which is exactly
// why a second registration call survived here for as long as it did: it
// compiled, it linked, it ran, and it could only ever return at its own gate.
//=============================================================================//

TEST( StandaloneSystem, TheSystemAndItsDeadInitAreBothGone )
{
	const std::string src = Read( kStandalone );
	ASSERT_FALSE( src.empty() );

	// THE WHOLE CLASS, and the Init separately, because they left for different
	// reasons and a reader who only sees the class gone would take the Init's
	// deadness with it as an assumption. It was measured: see
	// TheManagedRuntimeIsUpBeforeTheSystemTableIsWalked below.
	EXPECT_EQ( 0, CountOf( src, "class CSourceSharpStandaloneSystem" ) )
		<< "the game system is back in this file";
	EXPECT_EQ( 0, CountOf( src, "CServerSystem *StandaloneGameSystem()" ) )
		<< "the accessor kServerSystems[] used to name is back, so the row can be "
		   "written again";
	EXPECT_EQ( 0, CountOf( src, "virtual bool Init() OVERRIDE" ) )
		<< "the row's Init is back. Its only statement could ever be a call to "
		   "RegisterStandaloneTiers that returns at `if ( g_bTiersRegistered )`, "
		   "because the managed publish path has already made it";

	// VACUITY GUARD. Every count above is zero, and zero is also what a file
	// that failed to open or a needle that matches nothing answers. The thing
	// this file DID keep proves the read reached real text.
	EXPECT_EQ( 1, CountOf( src, "static void RegisterStandaloneTiers( void )" ) )
		<< "the tier registration is not in this file either, so the three zeroes "
		   "above say nothing -- the read found no source at all";
}

TEST( StandaloneSystem, TheRegistrationHasExactlyOneReachableFirstCaller )
{
	const std::string src = Read( kStandalone );
	ASSERT_FALSE( src.empty() );

	// RegisterStandaloneTiers is idempotent and is called from four places. The
	// question is not how many call it -- it is which one can be FIRST, and the
	// answer is the one on the managed publish path.
	EXPECT_EQ( 1, CountOf( src, "int32_t SourceSharp_NetHierarchyDeclare( int32_t nBlobBytes )" ) )
		<< "the engine slot C# calls to declare the hierarchy's blob is not in this "
		   "file any more, so nothing here can be the registration's first caller";

	const size_t at = src.find( "int32_t SourceSharp_NetHierarchyDeclare( int32_t nBlobBytes )" );
	ASSERT_NE( at, std::string::npos );

	// Its FIRST statement, not merely somewhere in the body: the tables have to
	// exist before the number this function returns can pick one of them.
	const size_t brace = src.find( '{', at );
	ASSERT_NE( brace, std::string::npos );
	const std::string head = src.substr( brace, 200 );

	EXPECT_NE( head.find( "RegisterStandaloneTiers();" ), std::string::npos )
		<< "SourceSharp_NetHierarchyDeclare no longer registers the tiers first, "
		   "so the class list can be walked before it is built";
}

TEST( StandaloneSystem, TheManagedRuntimeIsUpBeforeTheSystemsInit )
{
	const std::string src = Read( kGameDll );
	ASSERT_FALSE( src.empty() );

	// The ORDER inside DLLInit, which is what makes the deleted Init dead. The
	// game systems' Init is the first thing OnSystemStart does since
	// kServerSystems[]'s order went managed (plan_server_native_v2 T4e:
	// ServerSystems.Init), so the runtime is up by construction before any
	// system's Init -- read as two positions, because both calls being there
	// is not the fact.
	const size_t load = src.find( "LoadManagedRuntime(this)" );
	const size_t init = src.find( "->OnSystemStart()" );

	ASSERT_NE( load, std::string::npos ) << "DLLInit no longer brings the CLR up here";
	ASSERT_NE( init, std::string::npos ) << "DLLInit no longer starts the systems here";

	EXPECT_LT( load, init )
		<< "the systems start BEFORE the managed runtime loads, so the standalone "
		   "tiers would not yet be registered when a system's Init ran -- which is "
		   "the state the deleted Init was correct for";
}

TEST( StandaloneSystem, TheManagedSidePublishesTheHierarchyUnconditionally )
{
	const std::string module = Read( kModule );
	ASSERT_FALSE( module.empty() );

	// The other half of "dead by construction": DLLInit fails outright if
	// LoadManagedRuntime returns null, and LoadManagedRuntime returns null
	// unless ServerModule.Initialize ran to this line. So there is no reachable
	// order in which the row's Init could have been the first caller.
	EXPECT_EQ( 1, CountOf( module, "StandaloneNetStates.PublishAll(" ) )
		<< "ServerModule no longer publishes the hierarchy, or does it twice";

	const std::string publish = Read( kPublish );
	ASSERT_FALSE( publish.empty() );

	EXPECT_EQ( 1, CountOf( publish, "networking.NetHierarchyDeclare(blobBytes)" ) )
		<< "PublishAll no longer declares the hierarchy's blob, so the native "
		   "registration has no managed caller at all";
}


//=============================================================================//
// 2. THE PRE-ENTITY HALF OF LEVEL SHUTDOWN, WHICH HAD NO CALLER AT ALL
//
// The third stage this plan has found in that state. What can be checked from
// managed code is that the dispatch happens; what can only be checked HERE is
// that the dispatch is reached from the ROW, which is the whole of the
// position argument -- the pass is a BACKWARD walk of kServerSystems[], so a
// caller outside it runs after every row rather than at index 12.
//=============================================================================//

TEST( StandaloneSystem, TheManagedRowDrivesThePreEntityHalfOfShutdown )
{
	// The pass is managed's since plan_server_native_v2 T4e (ServerSystems):
	// the managed row's pre-entity shutdown stage, then CPhysicsHook's, the
	// table backwards. Its behaviour is ServerSystemsTests'; this holds the
	// shape mutation S7 found: the call must be IN the pre-entity pass, not
	// bolted onto the post-entity one, where it would run after the entity
	// list is cleared.
	const std::string systems = Read( kSystems );
	ASSERT_FALSE( systems.empty() );

	const size_t stage = systems.find( "public void LevelShutdownPreEntity()" );
	const size_t next = systems.find( "public void LevelShutdownPostEntity()" );
	ASSERT_NE( stage, std::string::npos ) << "ServerSystems has no pre-entity shutdown pass";
	ASSERT_NE( next, std::string::npos );
	ASSERT_LT( stage, next ) << "the two passes are not in the order this check reads them in";

	const size_t call = systems.find( "managed.LevelShutdownPreEntity();" );
	const size_t physics = systems.find( "level.PhysicsLevelShutdownPreEntity();" );
	ASSERT_NE( call, std::string::npos );
	ASSERT_NE( physics, std::string::npos );

	EXPECT_EQ( 1, CountOf( systems, "managed.LevelShutdownPreEntity();" ) );
	EXPECT_TRUE( call > stage && call < next )
		<< "the managed row's pre-entity shutdown stage is not inside the pre-entity pass";
	EXPECT_LT( call, physics ) << "the pass is not backwards: physics shut down before the managed row";
}

TEST( StandaloneSystem, ThePreEntityShutdownBodyLeftWithTheTable )
{
	// It was sourcesharp_gamesystem.cpp's body, called from the table's managed
	// row, and it called the runtime's OnMapUnloadPreEntity. Since T4e managed
	// runs the stage itself (ServerGameDll.ShutdownPreEntityParticipants), so
	// neither the body nor the slot is left to guard off the client.
	const std::string gs = Read( kGameSystem );
	ASSERT_FALSE( gs.empty() );
	EXPECT_EQ( 0, CountOf( gs, "void SourceSharp_ManagedLevelShutdownPreEntity(" ) );
	EXPECT_EQ( 1, CountOf( gs, "void SourceSharp_ManagedLevelInitPreEntity(" ) )
		<< "the control: the edge body that is left is found by the same read";
}

TEST( StandaloneSystem, ThePreEntityLoadIsARuntimeSlotAndTheUnloadIsNot )
{
	// Since plan_engineapi_vtables E1 the pre-entity pair was IManagedRuntimeServer's.
	// The load half stays: the WORLD's precache reaches it from native. The
	// unload half retired at plan_server_native_v2 T4e: managed's ServerSystems
	// runs that pass, so nothing native calls it.
	const std::string decl = Read( "sourcesharp/game/shared/imanaged_runtime.h" );
	ASSERT_FALSE( decl.empty() );

	EXPECT_EQ( 1, CountOf( decl, "void ( *OnMapLoadPreEntity )( IManagedRuntimeServer *self, const char *pszMapName );" ) );
	EXPECT_EQ( 0, CountOf( decl, "OnMapUnloadPreEntity" ) );
}

TEST( StandaloneSystem, TheMapUnloadSlotIsNoStructField )
{
	const std::string abi = Read( kAbiHeader );
	ASSERT_FALSE( abi.empty() );

	// It was appended to the ManagedApi struct after ServerWorldKeyValue; since
	// plan_engineapi_vtables E1 both were methods of the managed-built
	// IManagedRuntimeServer, E-last removed the server's fields and
	// plan_client_vtables CE-last the struct itself. Neither may come back as
	// a function pointer anywhere in the ABI header.
	EXPECT_EQ( 0, CountOf( abi, "(*ServerMapUnloadPreEntity)" ) );
	EXPECT_EQ( 0, CountOf( abi, "(*ServerWorldKeyValue)" ) );
	EXPECT_EQ( 1, CountOf( abi, "(*SourceSharpLogFn)" ) )
		<< "the control: the header's surviving function pointer is found by the same needle";
}

TEST( StandaloneSystem, TheDispatchIsNotAlsoWrittenIntoMapUnload )
{
	const std::string dll = Read( kServerDll );
	ASSERT_FALSE( dll.empty() );

	// THE OBVIOUS REPAIR, REFUSED. A stage with no caller invites a line in the
	// method that owns the teardown sequence, and that line would run -- so
	// every behavioural fact about the stage would pass -- in the wrong place.
	// MapUnload is OUTSIDE the backward walk of kServerSystems[]; the two
	// participants that need this stage both inherit a position from a row in
	// the middle of it.
	EXPECT_EQ( 1, CountOf( dll, "_lifecycle.LevelShutdownPreEntity();" ) )
		<< "the pre-entity shutdown stage is dispatched from more than one place, "
		   "or from none";

	const size_t at   = dll.find( "_lifecycle.LevelShutdownPreEntity();" );
	const size_t pre  = dll.find( "public void MapUnloadPreEntity()" );
	const size_t full = dll.find( "public void MapUnload()" );

	ASSERT_NE( at,   std::string::npos );
	ASSERT_NE( pre,  std::string::npos );
	ASSERT_NE( full, std::string::npos );
	ASSERT_LT( pre, full ) << "the two methods are not in the order this check reads them in";

	EXPECT_TRUE( at > pre && at < full )
		<< "the dispatch is not inside MapUnloadPreEntity. Written into MapUnload it "
		   "runs after the whole native pre-entity pass instead of at index 12 of it";
}


//=============================================================================//
// 3. THE ROW LEFT THE TABLE, AND WHAT REPLACED IT IS AN ORDER
//
// The failure a row leaving creates is silent in this hierarchy's own way. A
// standalone entity is NOT on gEntList -- the entity list is IHandleEntity-typed
// and its iteration walks CBaseEntity -- so a drive that stops running does not
// empty the world: every standalone entity freezes where it stands and goes on
// transmitting, and a teardown that stops running leaves the previous map's
// entities standing in the next one, holding edicts and answering to their
// names. Nothing warns, nothing fails to link, and `./debug-api ents "*"` still
// counts them.
//=============================================================================//

TEST( StandaloneSystem, TheRowIsGoneAndTheTableWithIt )
{
	// kServerSystems[] went managed (plan_server_native_v2 T4e); the native
	// systems' stages are shell_level.cpp's steps, and the standalone system is
	// not one of them, nor is its accessor.
	const std::string level = Read( kLevel );
	ASSERT_FALSE( level.empty() );

	EXPECT_EQ( 0, CountOf( level, "StandaloneGameSystem" ) )
		<< "the accessor is back, which is a row waiting to be written";
	EXPECT_FALSE( Exists( kTable ) ) << kTable << " is back";

	// VACUITY GUARD: the steps' needle finds the system they DO run.
	EXPECT_GE( CountOf( level, "PhysicsGameSystem()->" ), 1 )
		<< "the steps' needle matches nothing at all, so the zero above says nothing";
}

TEST( StandaloneSystem, TheRemovalCarriesItsReasonWhereItsStagesAre )
{
	// A row deleted without a reason is indistinguishable from one forgotten.
	// The reason was the table's comment; since the table went it is the
	// managed stages' own (LifecycleStages.cs), read RAW because it IS a
	// comment, in the window that ends at the stage it explains.
	const std::string raw = ReadRaw( kStages );
	ASSERT_FALSE( raw.empty() );

	const size_t stage = raw.find( "Standalone = 1100," );
	ASSERT_NE( stage, std::string::npos );
	const size_t open = raw.rfind( "// ----", stage );
	ASSERT_NE( open, std::string::npos );

	const std::string block = raw.substr( open, stage - open );
	EXPECT_NE( block.find( "CSourceSharpStandaloneSystem" ), std::string::npos )
		<< "the stage's own block does not name the row it took over";
}

TEST( StandaloneSystem, TheThreeFrameStepsAreThreeBecauseTheOrderIsADecision )
{
	const std::string steps = Read( "sourcesharp/game/server/shell_frame.cpp" );
	ASSERT_FALSE( steps.empty() );

	// THREE BODIES, not one. The row ran the commands and then the thinks with
	// the ORDER written as a C++ comment; IEngineFrame's own rule is that native
	// exposes one step per irreducible piece and C# owns the order, and "a step
	// is never a decision". One step doing all of it would put the decision
	// back.
	//
	// TWO OF THE THREE ARE plan_entity.md STEP 1g's: StandaloneRunFrame used to
	// run the thinks itself, crossing into managed code once per thinking
	// entity, which §3.4 forbids. The thinks are managed now and run BETWEEN
	// Update and Finish, so what was one step is two with the managed pass
	// between them -- and that placement is exactly the kind of order this
	// arm exists to keep in C#.
	// THE STEPS ARE VTABLE METHODS NOW, not free functions. plan_shim.md 5 3a
	// (lane 3b-enginebind) folded shell_framesteps.cpp's free functions into
	// the CShimFrame methods that used to forward to them, so the needles are
	// the methods. Two of them since the serialised ABI pass (2026-10-20)
	// removed StandaloneUpdate, whose body had been empty since P1.
	EXPECT_EQ( 1, CountOf( steps, "virtual void StandalonePlayerCommands() OVERRIDE" ) );
	EXPECT_EQ( 0, CountOf( steps, "StandaloneUpdate" ) );
	EXPECT_EQ( 1, CountOf( steps, "virtual void StandaloneFinish() OVERRIDE" ) );

	// NO THINK LEFT ON THIS SIDE. A native step that dispatched a think would be
	// the per-entity crossing coming back.
	EXPECT_EQ( 0, CountOf( steps, "StandaloneRunFrame" ) );
	EXPECT_EQ( 0, CountOf( steps, "StandaloneEntities_RunFrame" ) );

	// And no step decides anything: exactly one call each, no condition.
	EXPECT_EQ( 1, CountOf( steps, "CStandalonePlayer::RunAllQueuedCommands();" ) );
	// TAB-ANCHORED, because this file also carries the forward DECLARATIONS --
	// `void StandaloneEntities_Update();` at file scope -- and the bare name
	// with its parentheses matches both. Counted without the tab this read TWO
	// and the fact went red on a correct tree, which is this tree's own
	// "a needle satisfied by a longer thing than it meant" defect met again.
	// TWO TABS, NOT ONE, and for the reason the comment above gives one level
	// deeper: the bodies are inside a class now, so a step's one call is
	// indented twice. The forward DECLARATIONS are still at file scope with no
	// tab at all, which is what the anchor is for.
	// StandaloneEntities_Update was an empty body the managed frame never
	// reached (plan_server_native_min P1), and its slot left in the serialised
	// ABI pass. No call, at any indent.
	EXPECT_EQ( 0, CountOf( steps, "StandaloneEntities_Update();" ) );
	EXPECT_EQ( 1, CountOf( steps, "\n\t\tStandaloneEntities_Finish();" ) );

	// The ORDER is C#'s now, and the calls are in ONE managed method so the
	// order is readable in one place rather than inferred from three.
	const std::string cs =
		Read( "sourcesharp/managed/SourceSharp.Server/StandaloneHierarchy.cs" );
	ASSERT_FALSE( cs.empty() );

	const size_t players = cs.find( "_frame.StandalonePlayerCommands();" );
	const size_t update  = cs.find( "_motion.RunFrame(" );
	const size_t think   = cs.find( "_directory.RunFrame(" );
	const size_t finish  = cs.find( "_frame.StandaloneFinish();" );

	ASSERT_NE( players, std::string::npos );
	ASSERT_NE( update,  std::string::npos );
	ASSERT_NE( think,   std::string::npos );
	ASSERT_NE( finish,  std::string::npos );

	// THE MIDDLE STEP IS NOT THE NATIVE ONE ANY MORE -- plan_entity.md step 2g.
	// StandaloneEntities_Update had three lines: the thinks (taken by 1g), the
	// touch sweep (2b) and the movers (2g). With the last one gone the function
	// is empty, so the managed frame stopped calling it -- an empty step is a
	// crossing a frame for nothing. What holds this position now is the managed
	// integrator's own pass, which is where the movers moved to, and the
	// ordering claims below are unchanged because the POSITION is unchanged.
	//
	// The call must be GONE and not merely moved: the native function still
	// exists (its ABI slot may only be removed by the integrator), so a build
	// that calls it again is paying for nothing and would not otherwise say so.
	EXPECT_EQ( std::string::npos, cs.find( "_frame.StandaloneUpdate();" ) )
		<< "StandaloneEntities_Update is empty since step 2g; calling it is a "
		   "crossing a frame that does nothing";

	EXPECT_LT( players, update )
		<< "the entity work runs before the players' commands, so an entity's "
		   "think this tick sees where the players were LAST tick -- which is the "
		   "order stock keeps by simulating players inside the physics pass and "
		   "thinking entities after it";

	// THE THINK PASS IS BETWEEN THE TWO NATIVE HALVES, and each side of it is a
	// separate claim because each has its own reason. Before it: the touch
	// links, so a think asking "is anybody standing in me" gets this frame's
	// answer. After it: the attachment compose (a think can advance a parent's
	// sequence) and the deferred deletes (an entity that dies in its own think
	// must not be freed under the caller, and must not survive the frame).
	EXPECT_LT( update, think )
		<< "the managed think pass runs before the movers are stepped, so a "
		   "think reads last frame's world";

	EXPECT_LT( think, finish )
		<< "the attachment compose and the deferred deletes run before the "
		   "thinks, so a child sits on last frame's pose and an entity that "
		   "removed itself in its think survives the frame";

	// AND THE DEFERRED DELETES ARE THE LAST THING THE FRAME DOES --
	// plan_entity.md step 2c. The list is EntityDirectory's now; what the native
	// step still drains is the fall-back, for a shell the directory never bound.
	//
	// Every earlier line of this method is a reason this one is last. After the
	// thinks, so an entity that decides to die inside its own think is not freed
	// under the caller that killed it. After the parenting sweep, so a child is
	// never composed against a parent that has already gone. And inside the
	// frame, so a handle does not survive into the next one -- which is what
	// makes "a handle stays valid until end of frame" a bound rather than a
	// hope. Moving it above the sweep or above the thinks breaks one of the
	// three silently, which is why it is asserted rather than commented.
	const size_t sweep = cs.find( "Parenting.RunFrame();" );
	const size_t free  = cs.find( "_directory.FreeRemoved();" );

	ASSERT_NE( sweep, std::string::npos );
	ASSERT_NE( free,  std::string::npos );

	EXPECT_LT( think, sweep );
	EXPECT_LT( sweep, free )
		<< "the deferred deletes run before the parenting sweep, so a child can "
		   "be composed against a parent that is already gone";

	EXPECT_LT( finish, free )
		<< "the managed removal pass runs before the native fall-back drain, so "
		   "the two halves of one frame's deletions are not interleaved";
}

//-----------------------------------------------------------------------------
// plan_entity.md step 2c. The frame's removals cross ONCE, and the native side
// keeps no policy about which entity goes or when.
//-----------------------------------------------------------------------------
TEST( StandaloneSystem, TheShellDestroyIsABatchAndTheDestructorKeepsNoPolicy )
{
	const std::string base = Read( "sourcesharp/game/server/shell_standalone_base.cpp" );
	ASSERT_FALSE( base.empty() );

	// A SPAN AND A COUNT. One call per frame, not one per entity: §3.4's rule,
	// and the signature is where it is enforced -- a per-entity slot would take
	// a bare handle and nothing would read as wrong.
	EXPECT_EQ( 1, CountOf( base,
		"void SourceSharp_DestroyShell( const uint32_t *pHandles, int32_t nCount )" ) )
		<< "the batch destroy is the shape the crossing budget needs";

	// AND THE DESTRUCTOR IS DOWN TO MECHANISM. Both of these were policy calls
	// standing in ~CStandaloneEntity; both are managed calls now, made by the
	// directory's removal pass while the handle still resolves.
	const size_t dtor = base.find( "CStandaloneEntity::~CStandaloneEntity()" );
	ASSERT_NE( dtor, std::string::npos );

	const size_t dtorEnd = base.find( "\n}\n", dtor );
	ASSERT_NE( dtorEnd, std::string::npos );

	const std::string body = base.substr( dtor, dtorEnd - dtor );

	EXPECT_EQ( 0, CountOf( body, "OnManagedRemoved();" ) )
		<< "the destructor still makes the per-entity removal crossing";
	EXPECT_EQ( 0, CountOf( body, "StandaloneHierarchy_EntityRemoved( this );" ) )
		<< "the destructor still decides what happens to this entity's children";

	// The mechanism it DOES keep, so this fact cannot pass by the destructor
	// having been emptied or renamed.
	EXPECT_EQ( 1, CountOf( body, "DestroyPhysics();" ) );
	EXPECT_EQ( 1, CountOf( body, "BlockHandle().Term();" ) );
	EXPECT_EQ( 1, CountOf( body, "engine->RemoveEdict( m_pEdict );" ) );

	// AND THE MARK IS ASKED FOR BEFORE THE FALL-BACK LIST IS TOUCHED. Reversed,
	// every removal would take the native path and the directory would never
	// hear about one.
	const size_t remove = base.find( "void CStandaloneEntity::Remove()" );
	ASSERT_NE( remove, std::string::npos );

	const size_t ask  = base.find( "EntityRemoveRequested(", remove );
	const size_t list = base.find( "g_PendingDeletes.push_back( this );", remove );

	ASSERT_NE( ask,  std::string::npos );
	ASSERT_NE( list, std::string::npos );
	EXPECT_LT( ask, list );
}

TEST( StandaloneSystem, TheTeardownStepIsPreEntityAndBeforeTheEntityListIsCleared )
{
	const std::string steps = Read( "sourcesharp/game/server/shell_level.cpp" );
	ASSERT_FALSE( steps.empty() );

	// THE STEPS ARE VTABLE METHODS NOW. plan_shim.md 5 3a (lane 3b-enginebind)
	// folded shell_levelshim.cpp into shell_levelsteps.cpp as shell_level.cpp,
	// so each step's body lives in the method that used to forward to it.
	EXPECT_EQ( 1, CountOf( steps, "virtual void ShutdownStandaloneEntities() OVERRIDE" ) );
	// TAB-ANCHORED, for the reason the frame step's twin records: the forward
	// declaration in this file matches the bare name too. TWO tabs now, because
	// the call is inside a class.
	EXPECT_EQ( 1, CountOf( steps, "\n\t\tStandaloneEntities_LevelShutdown();" ) );

	// THE POSITION, as text, because it is the one thing the C++ can state about
	// itself: the step has to be defined before the entity-list clear so that a
	// reader of this file in order sees the sequence the managed side drives.
	// The RUNTIME position is the participant's stage, which is asserted below.
	const size_t teardown = steps.find( "virtual void ShutdownStandaloneEntities() OVERRIDE" );
	const size_t clear    = steps.find( "virtual void ClearEntityList() OVERRIDE" );

	ASSERT_NE( teardown, std::string::npos );
	ASSERT_NE( clear,    std::string::npos );

	EXPECT_LT( teardown, clear )
		<< "the hierarchy's teardown is written after gEntList.Clear()'s step, "
		   "which reads as the order it runs in and is not";
}

TEST( StandaloneSystem, TheParticipantRunsFirstInTheFrameAndLastAtShutdown )
{
	const std::string cs =
		Read( "sourcesharp/managed/SourceSharp.Server/StandaloneHierarchy.cs" );
	ASSERT_FALSE( cs.empty() );

	// BOTH CONTRACTS ON ONE OBJECT, which is what makes the two positions one
	// decision rather than two that can drift.
	EXPECT_NE( cs.find( "IFrameLifecycle, IGameLifecycle" ), std::string::npos )
		<< "the frame drive and the teardown are no longer the same participant";

	EXPECT_NE( cs.find( "(int)FrameStage.Standalone" ), std::string::npos );
	EXPECT_NE( cs.find( "(int)LifecycleStage.Standalone" ), std::string::npos );

	// The two stage VALUES, read out of the enum rather than written down here.
	// A fixture standing in for something another file owns is this tree's own
	// recorded defect, and the numbers are the whole position argument.
	const std::string stages =
		Read( "sourcesharp/managed/SourceSharp.Server/LifecycleStages.cs" );
	ASSERT_FALSE( stages.empty() );

	const size_t standaloneFrame = stages.find( "Standalone = 50," );
	const size_t soundscapes     = stages.find( "Soundscapes = 100," );

	EXPECT_NE( standaloneFrame, std::string::npos )
		<< "FrameStage.Standalone is not 50 any more. It has to sort below every "
		   "other frame stage: the row it replaced was index 11 of "
		   "kServerSystems[] and SourceSharpManaged is index 12, so its "
		   "post-entity think ran immediately before every participant that runs "
		   "from inside row 12's body";
	EXPECT_NE( soundscapes, std::string::npos );
	EXPECT_LT( standaloneFrame, soundscapes )
		<< "the two frame stages are not in the order this check reads them in";

	EXPECT_NE( stages.find( "Standalone = 1100," ), std::string::npos )
		<< "LifecycleStage.Standalone is not 1100 any more. It has to sort AFTER "
		   "SoundController (1000): the pre-entity shutdown pass is a BACKWARD "
		   "walk, and CSoundControllerImp was row 18 to this system's row 15, so "
		   "the controller reset before the hierarchy was torn down";
}

//-----------------------------------------------------------------------------
// THE THREE SLOTS THAT DROVE THE PARALLEL HIERARCHY ARE ALL SHIM SLOTS NOW,
// and this arm is what says so rather than what has been deleted.
//
// It required FrameStandalonePlayerCommands, FrameStandaloneRunFrame (now
// FrameStandaloneUpdate and FrameStandaloneFinish -- plan_entity.md step 1g
// split it) and LevelShutdownStandaloneEntities to be declared in SourceSharpEngineApi
// exactly once, after the tail, and to be assigned in the fill -- because a
// declared engine slot nobody assigns is not a crash and not a warning:
// Engine.cs tested the pointer and RETURNED, so the hierarchy would simply stop
// being driven, which is the exact silent failure this whole file exists for.
//
// plan_native_api.md Phases 2, 4 and 6 moved all three. The first two are slots
// 15 and 16 of IShimFrame; the third is slot 14 of IShimLevel. The failure mode
// they were guarded against CANNOT HAPPEN to a shim slot -- there is no fill to
// leave out, and the impl's one-line delegation does not compile if the free
// function's signature moves. What CAN still happen is a reorder inside a shim
// header, which no count can see, and the arms for that are
// VtableLayoutLength.TheShimFrameSlotsAreInTheHeadersOwnOrder and
// ...TheShimLevelSlotsAreInTheHeadersOwnOrder, each asking the COMPILER where
// three named methods sit.
//
// So what is left here is the other direction: none of the three may come BACK
// as a flat slot, because a second route to a step that already has one is how
// a frame ends up driven twice.
//-----------------------------------------------------------------------------
TEST( StandaloneSystem, TheThreeEngineSlotsHaveLeftTheFlatTableAndMustNotReturn )
{
	const std::string abi = Read( kAbiHeader );
	ASSERT_FALSE( abi.empty() );

	// THE FLAT TABLE ITSELF IS GONE: plan_client_vtables CC-last deleted the
	// engine struct (and CE-last the ManagedApi one), so the way back is a
	// function pointer declared anywhere in the ABI header. Finding the one
	// that remains, and the exports, is what keeps the zero-expectations below
	// from passing against a failed read. A file this could not parse would
	// satisfy every one of them.
	EXPECT_EQ( abi.find( "} SourceSharpEngineApi;" ), std::string::npos )
		<< "the engine table is declared again";
	EXPECT_EQ( 1, CountOf( abi, "(*SourceSharpLogFn)" ) )
		<< "the ABI header's one surviving function pointer was not found by "
		   "the \"(*Name)\" needle the absences below use";
	EXPECT_GE( CountOf( abi, "SOURCESHARP_EXPORT " ), 10 )
		<< "the ABI header parsed to fewer than ten exports, so the "
		   "absences asserted below are not evidence of anything";

	static const char *kRetired[] = {
		"(*FrameStandalonePlayerCommands)",
		"(*FrameStandaloneRunFrame)",
		"(*FrameStandaloneUpdate)",
		"(*FrameStandaloneFinish)",
		"(*LevelShutdownStandaloneEntities)",
	};

	// NOR WIRED ON EITHER SIDE. A slot is wired by a generated shim-area
	// forward on each (plan_engineapi_vtables C6c on the server,
	// plan_client_vtables CC-last on the client, which deleted the struct fill
	// this used to read), generated from one row of the slot table.
	const std::string server = Read( kServerAreasImpl );
	const std::string client = Read( kClientAreasImpl );
	const std::string table  = ReadRaw( kSlotTable );
	ASSERT_FALSE( server.empty() );
	ASSERT_FALSE( client.empty() );
	ASSERT_FALSE( table.empty() );
	EXPECT_GT( CountOf( server, "{ return ::" ), 100 )
		<< "the server's forwards parsed to fewer than a hundred, so the "
		   "absences asserted below are not evidence of anything";
	EXPECT_GT( CountOf( client, "{ return ::" ), 100 )
		<< "the client's forwards parsed to fewer than a hundred, so the "
		   "absences asserted below are not evidence of anything";
	EXPECT_GT( CountOf( table, "\tEngineApi\t" ), 100 )
		<< "the slot table parsed to fewer than a hundred rows, so the "
		   "absences asserted below are not evidence of anything";

	for ( size_t i = 0; i < sizeof( kRetired ) / sizeof( kRetired[ 0 ] ); i++ )
	{
		EXPECT_EQ( 0, CountOf( abi, kRetired[ i ] ) )
			<< kRetired[ i ] << " is declared in the ABI header again. It was "
			   "retired when its step became a shim slot; a re-added slot is a "
			   "SECOND route to a step that already has one.";

		// "(*Name)" -> "Name"
		const std::string slot( kRetired[ i ] + 2, strlen( kRetired[ i ] ) - 3 );
		EXPECT_EQ( 0, ForwardCount( server, slot, "" ) ) << slot << " is wired on the server again";
		EXPECT_EQ( 0, ForwardCount( client, slot, "" ) ) << slot << " is wired on the client again";
		EXPECT_EQ( 0, CountOf( "\n" + table, "\n" + slot + "\t" ) ) << slot << " is a slot-table row again";
	}

	// AND THE STEPS THEMSELVES ARE STILL DECLARED AND STILL IMPLEMENTED. The
	// slots went; the functions did not, and the shim's impl delegates to them.
	// Without this the three arms above would go on passing after somebody
	// deleted the steps entirely, which is the same silent stop-being-driven
	// failure one level along.
	const std::string level = Read( "sourcesharp/game/server/shell_level.cpp" );
	const std::string frame = Read( "sourcesharp/game/server/shell_frame.cpp" );

	ASSERT_FALSE( level.empty() );
	ASSERT_FALSE( frame.empty() );

	// THE NAME AND NOT A SIGNATURE. Read() strips comments and nothing else, so
	// a needle carrying a return type and a parenthesis has to reproduce this
	// file's exact spacing -- and a needle that stops matching because somebody
	// reformatted a line is a check that failed for a reason nobody meant.
	EXPECT_LE( 1, CountOf( frame, "StandalonePlayerCommands" ) );
	EXPECT_LE( 1, CountOf( frame, "StandaloneFinish" ) );
	EXPECT_LE( 1, CountOf( level, "ShutdownStandaloneEntities" ) );

	// THERE IS NO SECOND FILE ON THE FRAME SIDE ANY MORE, and the check it
	// carried changed shape with it. It read "TWICE IN EACH SHIM: the forward
	// declaration at the top of the file and the one-line delegation in the
	// impl class" -- two, because one of them would mean the delegation was
	// declared and never written, which links and drives nothing.
	//
	// plan_shim.md 5 3a (lane 3b-enginebind) merged shell_frameshim.cpp into
	// shell_framesteps.cpp as shell_frame.cpp, so the delegation is gone: the
	// step's body IS the method. A declaration that is never written is not a
	// state this shape can be in. What survives, and is pinned here, is that
	// each step is a DEFINED vtable method rather than a name the file only
	// mentions -- the needle carries the body's opening brace.
	const std::string levelShim = Read( "sourcesharp/game/server/shell_level.cpp" );
	ASSERT_FALSE( levelShim.empty() );

	EXPECT_EQ( 1, CountOf( frame, "virtual void StandalonePlayerCommands() OVERRIDE\n\t{" ) );
	EXPECT_EQ( 1, CountOf( frame, "virtual void StandaloneFinish() OVERRIDE\n\t{" ) );
	// ONE, NOT TWO, AND THERE IS NO SECOND FILE ANY MORE. This required the
	// forward declaration at the top of shell_levelshim.cpp AND the one-line
	// delegation in its impl class, because one of them alone would mean the
	// delegation was declared and never written -- which links, and drives
	// nothing. The merge (plan_shim.md 5 3a, lane 3b-enginebind) removed the
	// delegation layer, so the step's body IS the method and that state is not
	// reachable. The needle carries the body's opening brace instead.
	EXPECT_EQ( 1, CountOf( levelShim,
		"virtual void ShutdownStandaloneEntities() OVERRIDE\n\t{" ) );
}
