//====== SourceSharp — the server runtime's sources, read ====================//
//
// plan_gamedll.md Phase 2 and Part 3b; plan_engineapi_vtables D5. What is left
// of the server runtime's native half, read from the sources: the ABI slot list
// the loader validates, the shell's shutdown and reload arrangement, and the
// absence of the adapter IManagedRuntimeServer's managed-built table replaced.
//=============================================================================//

#include <string.h>

#include "gtest/gtest.h"

//=============================================================================//
// SECTIONS 1-3 RAN THE ADAPTER, AND THE ADAPTER IS GONE (plan_engineapi_vtables
// D5). IManagedRuntimeServer is managed-built: ManagedVtableGenerator writes its
// table from SourceSharp.Server's ServerRuntimeBinding, whose slots the
// generator checks against the header at compile time (SSVT002..004), and
// ManagedVtableGeneratorTests runs a table the same way native calls it. The
// reload property those sections proved -- nothing kept across a reload -- is
// now structural: the shell keeps no pointer and asks ManagedRuntimeServer(),
// which every load acquires again.
//=============================================================================//

//=============================================================================//
// 4. The ABI list, and the adapter that no longer reads it.
//
// Source-scanned rather than run, because the thing being asserted is that no
// slot was added to one of the two lists and forgotten in the other -- which is
// a statement about text, and is invisible to every test above: a slot nothing
// forwards to is never called and therefore never wrong.
//=============================================================================//

#include <fstream>
#include <string>
#include <sys/stat.h>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string s = std::string( kCandidates[ i ] ) + "/sourcesharp/host/sourcesharp.h";
		if ( stat( s.c_str(), &st ) == 0 )
			return kCandidates[ i ];
	}
	return NULL;
}

std::string StripComments( const std::string &strIn )
{
	// CODE ONLY, and this is not caution -- it is a bug this test found in its
	// own first run. The comment above the tier unwind NAMES ConVar_Unregister
	// while explaining why it sits inside the tier pair, so a raw-text search
	// found the word in the explanation rather than in the statement and read
	// the order as wrong. A source scan that reads comments as code can fail on
	// prose and, worse, PASS on a commented-out line -- which is exactly the
	// state these checks exist to reject.
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string Read( const std::string &strPath )
{
	std::ifstream f( strPath.c_str(), std::ios::binary );
	if ( !f.is_open() )
		return std::string();
	return StripComments( std::string( ( std::istreambuf_iterator<char>( f ) ),
	                                   std::istreambuf_iterator<char>() ) );
}

// Comments kept: the SS_RETIRED markers ARE comments.
std::string ReadRaw( const std::string &strPath )
{
	std::ifstream f( strPath.c_str(), std::ios::binary );
	if ( !f.is_open() )
		return std::string();
	return std::string( ( std::istreambuf_iterator<char>( f ) ), std::istreambuf_iterator<char>() );
}

int CountOf( const std::string &strHaystack, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t at = strHaystack.find( strNeedle ); at != std::string::npos;
	      at = strHaystack.find( strNeedle, at + 1 ) )
	{
		n++;
	}
	return n;
}

// Phase 2's thirteen lifecycle slots, then Phase 5's five Part 6 answers, in
// declaration order. ONE ARRAY AND NOT TWO because every check below wants the
// whole block -- each slot is forwarded to exactly once, is in the validator's
// required list, and is in the table's tail -- and two arrays would be two
// places to remember to add a slot to.
const char *kSlots[] = {
	// Phase 2.
	"ServerSystemStart", "ServerGameStart", "ServerGamePostStart", "ServerGameStop",
	"ServerMapLoadTransition", "ServerMapLoadSaveGame", "ServerMapLoadBackground",
	"ServerMapLoadNewGame", "ServerMapUnload", "ServerActivate", "ServerGameFrame",
	"ServerPreClientUpdate", "ServerCreateNetworkStringTables",

	// Phase 5, Part 6. FIVE and not six: GetGameDescription has had a slot
	// since long before this block and sits far above it, so it is not part of
	// the tail and must not be added here.
	"ServerTickInterval", "ServerShouldHideServer", "ServerBrowserMapOverride",
	"ServerBrowserGameData", "ServerIsManualMapChangeOkay",

	// Phase 8, Part 10's three loose ends. THIS LIST GROWING IS THE FACT
	// PASSING, not the fact being weakened: Phase 8 appended three slots by
	// exactly the rule this test exists to enforce, so the literal reading went
	// red for the work being done right -- and the way to make it pass again
	// without touching this line would have been to insert them in the middle,
	// which is the one edit it refuses. Phases 4 and 5 hit the same thing; see
	// docs/gamedll-ledger.md.
	"ServerQueryCvarValueFinished", "ServerHibernation", "ServerStatusLines",

	// Phase 6. IServerGameDLL::Think -- once per frame EVEN WITH NO LEVEL, and
	// the reason it exists at all is that the dev API's command pump rides the
	// managed frame, which nothing drives in shell mode until Phase 7. Appended
	// by the same rule this fact exists to enforce.
	"ServerThink",

	// Phase A2, Part 11a2's three gaps. Sixth time this list has grown by the
	// rule it exists to enforce. All three were EMPTY BODIES with no TODO, so
	// nothing in plan_gamedll.md was going to catch them; they are implemented
	// rather than refused because all three are reached BY DESIGN, which is
	// also why not one of them carries a refusal counter.
	"ServerInvalidateMdlCache", "ServerSteamGameServerActivated",
	"ServerSteamGameServerShutdown",

	// Phase A3, the five "C" systems. SEVENTH time this list has grown by the
	// rule it exists to enforce.
	//
	// ServerMapLoadPreEntity is the interesting one: IGameLifecycle has had an
	// OnLevelInitPreEntity since Part 3a's stage split and NOTHING EVER
	// DISPATCHED IT -- ServerModule called only the PostEntity half -- so a
	// participant that declared the pre-entity stage was silently never
	// reached. Both of A3's systems do their work in that half, because both
	// PRECACHE and the precache window closes before the first Spawn().
	"ServerMapLoadPreEntity", "ServerDecalIndexForName", "ServerDecalNameForIndex",
	"ServerDecalTranslateForGameMaterial", "ServerPropDataRandomChunkModel",

	// Phase A3, SECOND PAIR -- CSoundEnt and CSoundControllerImp. EIGHTH time
	// this list has grown by the rule it exists to enforce, and the largest
	// single append it has taken: seventeen.
	//
	// The four CSoundEnt slots are shell_soundent.cpp's, standing in for
	// game/server/soundent.cpp, which left server_csharp.vpc. The thirteen
	// CSoundControllerImp slots are shell_soundenvelope.cpp's, standing in for
	// game/shared/soundenvelope.cpp, which left it too.
	//
	// NONE OF THE SEVENTEEN IS THE ADAPTER'S, for the same reason A3's first
	// five are not: the engine never asks CShellGameDll any of them. They are
	// asked by two shims answering stock's own interfaces -- the CSoundEnt
	// statics the tree still spells, and CSoundEnvelopeController::GetController.
	"ServerSoundEntInit", "ServerSoundEntShutdown", "ServerSoundEntInsert",
	"ServerSoundEntResetClientSound",

	"ServerSoundControllerCreate", "ServerSoundControllerDestroy",
	"ServerSoundControllerPlay", "ServerSoundControllerChangePitch",
	"ServerSoundControllerChangeVolume", "ServerSoundControllerFadeOut",
	"ServerSoundControllerShutdown", "ServerSoundControllerCommandAdd",
	"ServerSoundControllerCommandClear", "ServerSoundControllerPlayEnvelope",
	"ServerSoundControllerReset", "ServerSoundControllerGetPitch",
	"ServerSoundControllerGetVolume",
};
const int kSlotCount = (int)( sizeof( kSlots ) / sizeof( kSlots[ 0 ] ) );

// HOW MANY OF THE ABOVE THE ADAPTER FORWARDS, and the rest are reached from
// somewhere else in the server.
//
// Every slot up to here is a method the ENGINE asks CShellGameDll, so the
// adapter is its only possible caller and "forwarded exactly once" is the right
// property. Phase A3's five are not: the shell is never asked any of them.
// SourceSharp_ManagedLevelInitPreEntity is a shared body in
// sourcesharp_gamesystem.cpp, and the other four are asked by the two shims
// that answer stock's own interfaces -- shell_decals.cpp behind `decalsystem`
// and CPropData::GetRandomChunkModel's server arm.
//
// SPLIT RATHER THAN DROPPED FROM THE LIST. All thirty are still required, still
// checked against SOURCESHARP_SHELL_REQUIRED and still checked to be the
// table's tail; what differs is only who calls them, and the fact below asserts
// the adapter forwards the five ZERO times so the split cannot be used to
// smuggle a slot out of the forwarding check.
//-----------------------------------------------------------------------------
// (kAfterShellBlock, the list of slots after the shell block in the
// ManagedApi struct, went with the struct at plan_client_vtables CE-last:
// its only reader was the struct-tail fact.)
//
// A3's SECOND PAIR added seventeen more of the same kind -- shell-asked, never
// adapter-asked -- so the subtraction is twenty-two now. Written as a sum of the
// two phases rather than as 22, because the number is two decisions and not one,
// and a single literal would hide which of them a later change was about.
const int kAdapterSlotCount = kSlotCount - 5 - 17;

} // namespace

TEST( ManagedRuntimeServerSources, TheRuntimeSlotsHaveNoNativeCallerLeft )
{
	// D5: the shell calls the managed-built runtime, so the ManagedApi slots the
	// adapter forwarded to are reached by no native code at all -- Phase E
	// retires them. A caller appearing here is a second route to one answer.
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL ) << "source tree not found next to the test binary";

	const std::string strAdapter =
		StripComments( Read( std::string( pRoot ) + "/sourcesharp/game/server/managed_runtime_server.h" ) );
	const std::string strShell =
		StripComments( Read( std::string( pRoot ) + "/sourcesharp/game/server/shell_gamedll.cpp" ) );
	ASSERT_FALSE( strAdapter.empty() );
	ASSERT_FALSE( strShell.empty() );

	EXPECT_EQ( std::string::npos, strAdapter.find( "class CManagedRuntimeServer" ) );

	for ( int i = 0; i < kAdapterSlotCount; i++ )
	{
		const std::string strCall = std::string( "->" ) + kSlots[ i ] + "(";
		EXPECT_EQ( 0, CountOf( strAdapter, strCall ) ) << kSlots[ i ];
		EXPECT_EQ( 0, CountOf( strShell, strCall ) ) << kSlots[ i ];
	}

	// The control: the shell really does call the runtime, through the accessor.
	EXPECT_GE( CountOf( strShell, "ManagedRuntimeServer()->" ), 20 );
}

TEST( ManagedRuntimeServerSources, EveryRuntimeSlotIsRetiredAndNothingIsRequired )
{
	// THE LIST SourceSharp_ValidateServerApi WALKED IS GONE (plan_engineapi_vtables
	// E17b). It had to be the same list as the slots the adapter forwarded, so a
	// slot nobody filled was refused at load rather than faulting on first use.
	// Since E1 every one of those slots is a method of the managed-built
	// IManagedRuntimeServer -- a pure virtual the generated table must fill --
	// so each is SS_RETIRED in the struct, and nothing may require one: a
	// required retired slot would refuse every correct load.
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strLoader =
		Read( std::string( pRoot ) + "/sourcesharp/game/server/managed_runtime_server.cpp" );
	const std::string strAbi = ReadRaw( std::string( pRoot ) + "/sourcesharp/host/sourcesharp.h" );
	ASSERT_FALSE( strLoader.empty() );
	ASSERT_FALSE( strAbi.empty() );

	// Retired, then REMOVED at E-last (and the struct at CE-last): no function
	// pointer in the ABI header is named for one.
	for ( int i = 0; i < kSlotCount; i++ )
	{
		EXPECT_EQ( 0, CountOf( strAbi, std::string( "(*" ) + kSlots[ i ] + ")" ) )
			<< kSlots[ i ] << " is still a ManagedApi field";
	}
	EXPECT_EQ( 1, CountOf( strAbi, "(*SourceSharpLogFn)" ) )
		<< "the control: the header's surviving function pointer is found by the same needle";

	EXPECT_EQ( 0, CountOf( strLoader, "SOURCESHARP_SHELL_REQUIRED" ) )
		<< "a required-slot list is back in the loader, naming slots no server fills";
	EXPECT_EQ( 0, CountOf( strLoader, "SOURCESHARP_NULL_SLOT" ) )
		<< "the forced-null control is back without a list for it to null";
}


//=============================================================================//
// 5. DLLShutdown unloads what DLLInit loaded. plan_gamedll.md Part 10b.
//
// Source-scanned for the reason the shell is not otherwise testable from here:
// CShellGameDll is an engine class in an engine translation unit. What can be
// asserted is the two things that were WRONG, both of which are visible in the
// text and neither of which any other test in this tree can see -- the shell's
// EXPOSE_ is commented out until Phase 4, so nothing calls DLLShutdown at all.
//=============================================================================//

TEST( ShellGameDllShutdown, TheRuntimeForwardIsGuarded )
{
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strShell =
		Read( std::string( pRoot ) + "/sourcesharp/game/server/shell_gamedll.cpp" );
	ASSERT_FALSE( strShell.empty() );

	const size_t at = strShell.find( "void DLLShutdown() override" );
	ASSERT_TRUE( at != std::string::npos );

	const std::string strBody = strShell.substr( at, strShell.find( "\n    }", at ) - at );

	// The engine calls DLLShutdown after a FAILED DLLInit, and three of
	// DLLInit's four false paths leave no runtime. An unguarded
	// forward turns "the managed runtime would not load" into a null
	// dereference, which is the one outcome that hides the diagnostic naming
	// what was missing.
	EXPECT_TRUE( strBody.find( "if (ManagedRuntimeServer() != nullptr)" ) != std::string::npos );
	EXPECT_TRUE( strBody.find( "SourceSharp_UnloadManagedSide();" ) != std::string::npos );
}

TEST( ShellGameDllShutdown, TheThreeTiersAndTheConVarsAreUnwoundInReverse )
{
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strShell =
		Read( std::string( pRoot ) + "/sourcesharp/game/server/shell_gamedll.cpp" );
	ASSERT_FALSE( strShell.empty() );

	const size_t at = strShell.find( "void DLLShutdown() override" );
	ASSERT_TRUE( at != std::string::npos );
	const std::string strBody = strShell.substr( at, strShell.find( "\n    }", at ) - at );

	// Stock's order, backwards (gameinterface.cpp:879-882). ORDER and not mere
	// presence: ConVar_Unregister needs g_pCVar, which DisconnectTier1Libraries
	// takes away, so putting tier1 first would unregister nothing and leave the
	// engine holding this DLL's ConVar objects after it unloads.
	// Tier3's disconnect is tier_connect.cpp's own since tier3 left the link
	// (plan_server_novalve.md P3); it stays first.
	const size_t t3 = strBody.find( "SourceSharp_DisconnectTier3()" );
	const size_t cv = strBody.find( "ConVar_Unregister" );
	const size_t t1 = strBody.find( "SourceSharp_DisconnectTier1()" );

	ASSERT_TRUE( t3 != std::string::npos );
	ASSERT_TRUE( cv != std::string::npos );
	ASSERT_TRUE( t1 != std::string::npos );

	// tier2's disconnect went with g_pFullFileSystem (plan_server_novalve.md P3).
	EXPECT_EQ( std::string::npos, strBody.find( "Tier2" ) );
	EXPECT_LT( t3, cv );
	EXPECT_LT( cv, t1 );
}

TEST( ShellGameDllShutdown, TheReloadIsServicedInThinkAndNowhereElse )
{
	// Part 3b point 1 says this belongs at the top of GameFrame. It does not,
	// and eiface.h is what settles it:
	//
	//   GameFrame (:503) "The server should run physics/think on all edicts"
	//             -- a level is loaded, or the engine does not call it.
	//   Think     (:560) "Called once per frame EVEN WHEN NO LEVEL IS LOADED."
	//
	// With the service point in GameFrame a cs_sv_reload typed at the main menu
	// or between maps is never serviced: the request sits in g_bReloadRequested
	// and then fires on the first frame of the next map, which is worse than not
	// reloading because it lands at a moment nobody asked for.
	//
	// The torn-frame objection -- the only reason to have preferred GameFrame --
	// does not apply. The three managed frame points (FramePreThink, Think,
	// FramePostThink) all run INSIDE GameFrame, and Think is a separate
	// engine->shell entry that is never nested in it. So a reload serviced from
	// Think is atomic with respect to the frame: all three points either ran
	// against the outgoing assembly or will run against the incoming one, never
	// a mixture.
	//
	// AND NOWHERE ELSE is half the test. Part 3b's property is ONE service
	// point; two would reintroduce the interleaving it exists to prevent, and a
	// leftover call in GameFrame is exactly what an incomplete move leaves
	// behind.
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strShell =
		Read( std::string( pRoot ) + "/sourcesharp/game/server/shell_gamedll.cpp" );
	ASSERT_FALSE( strShell.empty() );

	const size_t atThink = strShell.find( "void Think(bool finalTick) override" );
	ASSERT_TRUE( atThink != std::string::npos );
	const std::string strThink =
		strShell.substr( atThink, strShell.find( "\n    }", atThink ) - atThink );

	EXPECT_NE( std::string::npos,
	           strThink.find( "SourceSharp_ServiceReloadRequest()" ) )
		<< "Think() is the one service point for a queued cs_sv_reload";

	const size_t atFrame = strShell.find( "void GameFrame(bool simulating) override" );
	ASSERT_TRUE( atFrame != std::string::npos );
	const std::string strFrame =
		strShell.substr( atFrame, strShell.find( "\n    }", atFrame ) - atFrame );

	EXPECT_EQ( std::string::npos,
	           strFrame.find( "SourceSharp_ServiceReloadRequest()" ) )
		<< "GameFrame() must NOT also service it -- Part 3b's property is one "
		   "service point, and two can interleave";

	// And the whole file holds exactly one call, so a third site somewhere else
	// on the class fails here rather than being found on a reload.
	size_t n = 0;
	for ( size_t at = strShell.find( "SourceSharp_ServiceReloadRequest()" );
	      at != std::string::npos;
	      at = strShell.find( "SourceSharp_ServiceReloadRequest()", at + 1 ) )
		n++;
	EXPECT_EQ( (size_t)1, n ) << "exactly one service call in the shell";
}

TEST( ShellGameDllShutdown, AFailedReloadIsFatalRatherThanAWarning )
{
	// Part 3b point 2, and it is what entitles every body in the adapter to
	// forward without a null test. While this was a Warning the game carried on
	// with a memset table -- correct only while native could fall back to
	// stock, and there is nothing behind the shell.
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL );

	const std::string strSys =
		Read( std::string( pRoot ) + "/sourcesharp/game/shared/sourcesharp_gamesystem.cpp" );
	ASSERT_FALSE( strSys.empty() );

	const size_t at = strSys.find( "managed reload FAILED" );
	ASSERT_TRUE( at != std::string::npos ) << "the failed-reload diagnostic is gone";

	// The call that carries it. Comments are already gone (see Read), so the
	// statement is whatever follows the previous brace or semicolon.
	size_t stmt = strSys.find_last_of( "{};", at );
	ASSERT_TRUE( stmt != std::string::npos );
	const std::string strCall = strSys.substr( stmt, at - stmt );

	EXPECT_TRUE( strCall.find( "Error(" ) != std::string::npos );
	EXPECT_TRUE( strCall.find( "Warning(" ) == std::string::npos );
}
