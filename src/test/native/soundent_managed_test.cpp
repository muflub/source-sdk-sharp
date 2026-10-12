//====== SourceSharp -- CSoundEnt becomes managed ==============================//
//
// plan_gamedll_after.md Phase A3, second pair.
//
// SOURCE-SCANNED, for c_systems_managed_test.cpp's reason and not a new one:
// every subject here lives in a translation unit that includes cbase.h's whole
// world -- the shim, the table, gameinterface.cpp -- so none of them can be
// linked into a gtest. What CAN be checked from here is the set of properties a
// compiler cannot see and an `nm` count cannot say WHY about.
//
// THE FAILURE THIS FILE EXISTS FOR IS SILENT. A row that leaves
// kServerSystems[] and gains no managed participant does not warn, does not
// fail to link and does not run -- the sound list would simply never be swept,
// and a list that is never swept keeps answering with entries that should have
// expired. Nothing prints, nothing crashes, and the AI hears gunfire from a
// minute ago.
//
// AND THE POOL IS NO LONGER REACHABLE AS MEMORY. That is what the pointer
// checks below exist for: SoundPointerForIndex handed a CSound * to two
// callers, and a port that left either of them holding one would be handing a
// raw pointer into a managed array.
//
//=============================================================================//

#include <cctype>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <string>
#include <vector>
#include <sys/stat.h>

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <algorithm>

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
// checks that cannot fail is long.
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

int CountOf( const std::string &hay, const std::string &needle )
{
	int n = 0;
	size_t at = hay.find( needle );
	while ( at != std::string::npos ) { n++; at = hay.find( needle, at + needle.size() ); }
	return n;
}

const char *kTable        = "sourcesharp/game/server/shell_serversystems.cpp";
const char *kShim         = "sourcesharp/game/server/shell_soundent.cpp";
const char *kShellSound   = "sourcesharp/game/server/shell_sound.cpp";
// The disconnect path -- CServerGameClients::ClientDisconnect. It was in
// game/server/gameinterface.cpp when A3 took the CSound * out of it, and it
// moved with its class to shell_gameclients.cpp at plan_gamedll_after.md Phase
// A9 step 2. The fact below is unchanged; only the file holding it is.
const char *kGameIface    = "sourcesharp/game/server/shell_gameclients.cpp";
const char *kAbiHeader    = "sourcesharp/host/sourcesharp.h";
const char *kRuntime      = "sourcesharp/game/server/managed_runtime_server.cpp";
// The client's slots since plan_client_vtables CC-last: its shim areas'
// generated forwards (the struct fill in sourcesharp_gamesystem.cpp is gone).
const char *kClientAreasImpl = "sourcesharp/game/shared/shell_shim_areas_client.inc";
// The slot table both forward files are generated from (tools/shim-areas),
// one row per slot. src/../docs is the repo's docs/ through the farm's link.
const char *kSlotTable    = "../docs/engineapi-areas.tsv";
// The server's slots since plan_engineapi_vtables C6c: shim-area methods,
// declared here and forwarded by the generated file below.
const char *kAreas        = "sourcesharp/game/shared/ishim_areas.h";
const char *kClientAreasH = "sourcesharp/game/shared/ishim_areas_client.h";
const char *kAreasImpl    = "sourcesharp/game/shared/shell_shim_areas.inc";
// The managed-built areas since plan_engineapi_vtables E (tools/managed-areas).
const char *kAreas2       = "sourcesharp/game/shared/imanaged_areas.h";
const char *kEnvShim      = "sourcesharp/game/server/shell_soundenvelope.cpp";
const char *kEnvCpp       = "game/shared/soundenvelope.cpp";
const char *kEnvSsHeader  = "sourcesharp/game/server/ss_soundcontroller.h";

// How many times server_sourcesharp's CMakeLists source list names a
// farm-relative path. The match is on the WHOLE path, so a bare
// "soundent.cpp" cannot be satisfied by shell_soundent.cpp -- the prefix shape
// this tree has been bitten by before. A count rather than a bool, so a
// duplicated row reads 2. Cross-checked against compile_commands.json through
// cmake_graph::Compiles, which throws on a missing, empty or stale input.
int NamedInServer( const char *pszFarmRelative )
{
	const std::vector< std::string > listed = cmake_graph::ListedSources( cmake_graph::kServer );
	const int n = (int)std::count( listed.begin(), listed.end(), std::string( pszFarmRelative ) );
	EXPECT_EQ( n > 0, cmake_graph::Compiles( cmake_graph::kServer, pszFarmRelative ) )
		<< pszFarmRelative << ": the CMakeLists list and compile_commands.json disagree";
	return n;
}

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

// How many rows of the slot table name `slot` in their first column.
int TableRows( const std::string &tsv, const std::string &slot )
{
	return CountOf( "\n" + tsv, "\n" + slot + "\t" );
}

} // namespace


//=============================================================================//
// 1. THE ROW LEFT THE TABLE, AND THE TABLE SAYS WHY
//=============================================================================//

TEST( SoundEntManaged, TheRowIsGone )
{
	const std::string table = Read( kTable );
	ASSERT_FALSE( table.empty() );

	// The ROW, spelled as a row. Matching the bare class name would also hit
	// the paragraph naming it as a removal, which is why comments are stripped
	// and why the needle is the row's shape.
	EXPECT_EQ( 0, CountOf( table, "{ \"CSoundEnt\"," ) );

	// ...and the accessor that existed only so the table could name it.
	EXPECT_EQ( 0, CountOf( table, "SoundEntGameSystem" ) );

	// VACUITY GUARD. Both counts are zero, and zero is also what a file that
	// failed to open answers. A row that IS still there proves the needle shape
	// finds one.
	EXPECT_EQ( 1, CountOf( table, "{ \"CPhysicsHook\"," ) )
		<< "the row needle matches nothing at all, so the zeroes above say "
		   "nothing either";
}

TEST( SoundEntManaged, TheRemovalCarriesItsReasonInTheTable )
{
	// A row deleted without a reason is indistinguishable from one forgotten.
	// Read RAW, because the reason IS a comment.
	const std::string raw = ReadRaw( kTable );
	ASSERT_FALSE( raw.empty() );

	const size_t at = raw.find( "//   CSoundEnt " );
	ASSERT_NE( at, std::string::npos )
		<< "CSoundEnt is not in the table's list of systems that are NOT rows, "
		   "so a reader cannot tell it was moved rather than forgotten";

	// The verdict has to be inside CSoundEnt's OWN block rather than anywhere
	// in the file. This is the ledger's H1 defect -- a verdict word surviving
	// elsewhere in the same file -- met by bounding the window at the next
	// entry rather than running it to end-of-file.
	//
	// AND THE BOUND IS "THE NEXT ENTRY", NOT A NAMED ONE. It said
	// `find( "//   CSourceSharpEventLog", at )`, which is the SAME DEFECT one
	// step further out: the window is only CSoundEnt's for as long as
	// CSourceSharpEventLog happens to be the entry after it. Phase A3's fifth
	// system inserted //   CSourceSharpStandaloneSystem between the two, this
	// window silently grew to cover that block as well, and mutation E2 --
	// taking the verdict off CSoundEnt -- SURVIVED, satisfied by another
	// system's. Found by the harness, not by reading the test.
	// c_systems_managed_test.cpp's twin already bounds on "\n//   C" and was
	// unaffected.
	const size_t next = raw.find( "\n//   C", at + 1 );
	ASSERT_NE( next, std::string::npos )
		<< "CSoundEnt's is the last entry in the list of what is NOT a row, so "
		   "this window would run to end-of-file and any later verdict would "
		   "satisfy it";

	const std::string block = raw.substr( at, next - at );

	EXPECT_NE( block.find( "NOT DELETED: MOVED" ), std::string::npos )
		<< "the row's block does not say it moved";
	EXPECT_NE( block.find( "FrameStage.SoundEnt" ), std::string::npos )
		<< "the row's block does not say WHERE it moved to";
}

//=============================================================================//
// 2. THE FILE LEFT THE SERVER'S PROJECT AND THE SHIM TOOK ITS PLACE
//=============================================================================//

TEST( SoundEntManaged, SoundEntCppIsNotInTheServersProject )
{
	EXPECT_EQ( 0, NamedInServer( "game/server/soundent.cpp" ) )
		<< "game/server/soundent.cpp is still compiled into server.so, so there "
		   "are two sound pools and only one of them is swept";

	// VACUITY GUARD, on the NEEDLE rather than on the file: a neighbouring entry
	// from the same directory proves the needle finds one at all. It also proves
	// the needle is not prefix-blind -- which is the shape this tree has been
	// bitten by.
	//
	// IT USED TO BE game/server/sound.cpp, whose name is a prefix of
	// soundent.cpp's and so made both points at once. plan_entity.md step 4a
	// took that file out of the build and off disk, so the control MOVED rather
	// than being dropped: physics.cpp is a prefix of physics_fx.cpp,
	// physics_main.cpp and physics_impact_damage.cpp, so a prefix-blind needle
	// would read more than one here and the EXPECT_EQ would say so.
	//
	// AND IT MOVED A SECOND TIME, FOR THE SAME REASON IN A DIFFERENT SHAPE.
	// plan_shim.md Phase 2b relocated physics.cpp and physics_fx.cpp out of
	// $SRCDIR\game\server\ and under $SRCDIR\sourcesharp\ -- Phase 2's exit
	// condition is that this script names no row under $SRCDIR\game\ at all.
	// Both are still compiled and both are still in THIS script, so the control
	// keeps making both of its points; only the directory in the needle
	// changed. Of the four prefix neighbours named above, physics_main.cpp and
	// physics_impact_damage.cpp had already left this script before the move --
	// verified here, not assumed: `grep -c physics_main server_csharp.vpc` was 0.
	// (Phase 6b: the needle is now server_sourcesharp's source list, matched on
	// the whole path; the control still proves the list was read.)
	EXPECT_EQ( 1, NamedInServer( "sourcesharp/game/server/physics.cpp" ) )
		<< "the needle matches nothing, so the zero above says nothing";
}

TEST( SoundEntManaged, TheShimIsInTheServersProject )
{
	EXPECT_EQ( 1, NamedInServer( kShim ) )
		<< "shell_soundent.cpp is not compiled, so every CSoundEnt static the "
		   "tree spells is undefined -- which at least fails to link, unlike "
		   "everything else this file guards against";
}

// TheHeaderStillDeclaresTheStaticsTheShimDefines IS RETIRED. It pinned
// CSoundEnt's three static declarations (ResetClientSound, InitSoundEnt,
// ShutdownSoundEnt) in game/server/soundent.h, which is compiled by nothing
// since plan_server_novalve.md P5 and left the farm in P6. Every native caller
// spells ss_soundent.h's free functions now, which the next section pins.

//=============================================================================//
// 3. THE SHIM FORWARDS, GUARDS AND HOLDS NOTHING
//=============================================================================//

TEST( SoundEntManaged, TheShimDefinesEveryEntryPointTheTreeStillCalls )
{
	const std::string shim = Read( kShim );
	ASSERT_FALSE( shim.empty() );

	// ss_soundent.h's five, which every native caller spells since P5 lane SP.
	EXPECT_EQ( 1, CountOf( shim, "bool ss::SoundEnt_Init()" ) );
	EXPECT_EQ( 1, CountOf( shim, "void ss::SoundEnt_Shutdown()" ) );
	EXPECT_EQ( 1, CountOf( shim, "void ss::SoundEnt_InsertSound(" ) );
	EXPECT_EQ( 1, CountOf( shim, "void ss::SoundEnt_ResetClientSound(" ) );
	EXPECT_EQ( 1, CountOf( shim, "int ss::SoundEnt_ClientSoundIndex(" ) );

	// And CSoundEnt's statics stay gone: nm found no importer once the last
	// caller switched, and a second spelling of the same pool is a second
	// thing to keep in step.
	EXPECT_EQ( 0, CountOf( shim, "CSoundEnt::" ) )
		<< "shell_soundent.cpp defines a CSoundEnt static again";
}

TEST( SoundEntManaged, EveryForwardingStaticGuardsANullManagedTable )
{
	const std::string shim = Read( kShim );
	ASSERT_FALSE( shim.empty() );

	// FOUR asks and FOUR guards. The fifth static, ClientSoundIndex, asks
	// nothing -- it is arithmetic on an edict -- so it neither has nor needs a
	// guard, and counting "one guard per ask" rather than "one per function" is
	// what keeps that from reading as a missing one.
	//
	// The needle carries the ASSIGNMENT. `SourceSharp_GetManagedApi();` alone
	// answers five, because the file's own forward declaration of that function
	// ends in the same two characters -- a needle satisfied by a longer thing
	// than it meant, which is this tree's recurring harness defect and was
	// caught here by the count being one too high rather than by reading it.
	//
	// Since plan_engineapi_vtables E7 each asks for the managed-built area.
	EXPECT_EQ( 4, CountOf( shim, "IManagedSoundEnt *pApi = ManagedSoundEnt();" ) );
	EXPECT_EQ( 4, CountOf( shim, "if ( pApi == NULL )" ) );
}

TEST( SoundEntManaged, TheShimHoldsNoFileScopeState )
{
	const std::string shim = Read( kShim );
	ASSERT_FALSE( shim.empty() );

	// tools/statics-audit is the gate for the whole tree; this is the local
	// statement of the same rule, and it is stronger here because this file has
	// no legitimate static at all -- every function in it is a forward. The old
	// file's g_pSoundEnt gate is a FIELD on the managed service.
	EXPECT_EQ( 0, CountOf( shim, "static " ) )
		<< "shell_soundent.cpp has grown a static; nothing here needs one";
}

TEST( SoundEntManaged, TheOwnerAndTargetCrossAsHandlesRatherThanPointers )
{
	const std::string shim = Read( kShim );
	ASSERT_FALSE( shim.empty() );

	// shell_sound.cpp's own recorded correction, applied in the other
	// direction: resolving an EHANDLE through CHandle<CBaseEntity>::Get()
	// answers NULL for an owner on the parallel hierarchy, so a noise made by a
	// managed entity was reported as a world noise. The handle is the one
	// identity both hierarchies share.
	EXPECT_EQ( 2, CountOf( shim, "SourceSharp_HandleFromAnyEntity(" ) )
		<< "the owner and the target must BOTH cross as handles";

	// And the two shapes that would undo it.
	EXPECT_EQ( 0, CountOf( shim, "->GetRefEHandle()" ) );
	EXPECT_EQ( 0, CountOf( shim, "SourceSharp_HandleFromEntity(" ) )
		<< "HandleFromEntity is the CBaseEntity-only spelling; a noise from a "
		   "managed entity would be credited to nobody";
}

//=============================================================================//
// 4. NOTHING NATIVE HOLDS A CSound * ANY MORE
//=============================================================================//

TEST( SoundEntManaged, TheDisconnectPathNoLongerTakesAPointerIntoThePool )
{
	const std::string gi = Read( kGameIface );
	ASSERT_FALSE( gi.empty() );

	EXPECT_EQ( 0, CountOf( gi, "SoundPointerForIndex" ) )
		<< "the disconnect path still asks for a CSound *, which is a raw "
		   "pointer into an array that is managed now";

	EXPECT_EQ( 1, CountOf( gi,
		"ss::SoundEnt_ResetClientSound( ss::SoundEnt_ClientSoundIndex( pEdict ) )" ) )
		<< "and it does not say the thing that replaced it";
}

//-----------------------------------------------------------------------------
// AND IT IS IN THE ARM THAT RUNS -- plan_shim.md \u00a75 3a, lane 3b-enginebind.
//
// THE FACT ABOVE WAS TRUE OF A BUILD THAT NEVER RESET A SOUND. The call sat
// inside ClientDisconnect's SDK arm, `if ( player )`, where player came from
// UTIL_SDKPlayerFromEdict -- whose every return statement is `return NULL`, so
// the guard can never be taken. Counting the call ANYWHERE in the file is
// satisfied by a call nothing can reach, which is this project's own
// "a check that cannot fail" shape met from the other side: the assertion was
// green on a tree where the behaviour it describes did not happen.
//
// So the position is the fact, not the presence. The reset must be inside the
// CStandalonePlayer::FromEdict arm, which is the only arm a disconnecting
// client of this build takes, and it must be BEFORE the `delete pStandalone`
// that ends it.
//
// WHY BEFORE, since the call does not touch the object: because `return` is
// the next statement after the delete, so a reset written below it is not
// merely late, it is unreachable -- the same defect one line further on.
//-----------------------------------------------------------------------------
TEST( SoundEntManaged, TheDisconnectResetIsInsideTheArmThatActuallyRuns )
{
	const std::string gi = Read( kGameIface );
	ASSERT_FALSE( gi.empty() );

	const size_t arm = gi.find(
		"if ( CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEdict ) )" );
	ASSERT_NE( arm, std::string::npos )
		<< "ClientDisconnect's standalone arm is gone -- without it this whole "
		   "test is about a function that disconnects nobody";

	const size_t reset = gi.find(
		"ss::SoundEnt_ResetClientSound( ss::SoundEnt_ClientSoundIndex( pEdict ) )", arm );
	const size_t del = gi.find( "delete pStandalone;", arm );

	ASSERT_NE( del, std::string::npos )
		<< "the standalone arm no longer deletes the shell";
	ASSERT_NE( reset, std::string::npos )
		<< "the client sound is not reset anywhere at or after the standalone "
		   "arm. If it is only reset in the SDK arm it is never reset at all: "
		   "UTIL_SDKPlayerFromEdict answers NULL on every path.";

	EXPECT_LT( reset, del )
		<< "the reset is written after `delete pStandalone;`, and the statement "
		   "after that delete is `return;` -- so it never runs.";
}

TEST( SoundEntManaged, TheShellsSoundFileNoLongerWalksTheList )
{
	const std::string s = Read( kShellSound );
	ASSERT_FALSE( s.empty() );

	// The two engine-slot bodies are gone, and with them every CSound member
	// read. Checked as SOURCE rather than as a symbol count because the whole
	// question is whether the walk is written down, not whether it linked.
	EXPECT_EQ( 0, CountOf( s, "SourceSharp_SoundEntQuery" ) );
	EXPECT_EQ( 0, CountOf( s, "SourceSharp_SoundEntInsert" ) );
	EXPECT_EQ( 0, CountOf( s, "CSoundEnt::" ) );
	EXPECT_EQ( 0, CountOf( s, "SOUNDLIST_EMPTY" ) );
	EXPECT_EQ( 0, CountOf( s, "#include \"soundent.h\"" ) );

	// VACUITY GUARD: the file is still the sound shell and still has bodies in
	// it, so the five zeroes above are about what was removed and not about a
	// file that failed to open or was emptied.
	EXPECT_LT( 0, CountOf( s, "GameSound_Emit" ) );

	// And the include's removal is EXPLAINED. Read raw: a stripped read cannot
	// tell "removed" from "removed and noted", and an unexplained deletion in a
	// file this size reads as an oversight.
	const std::string raw = ReadRaw( kShellSound );
	ASSERT_FALSE( raw.empty() );
	EXPECT_NE( raw.find( "NO soundent.h" ), std::string::npos )
		<< "the include is gone with no note saying so";
}

//=============================================================================//
// 5. THE ABI: FOUR SLOTS ADDED, TWO REMOVED, AND THE FOUR ARE REQUIRED
//=============================================================================//

TEST( SoundEntManaged, TheFourManagedSlotsAreAreaMethodsAndNotRequired )
{
	const std::string abi = Read( kAbiHeader );
	const std::string rt  = Read( kRuntime );
	ASSERT_FALSE( abi.empty() );
	ASSERT_FALSE( rt.empty() );

	static const char *kSlots[] =
	{
		"ServerSoundEntInit",
		"ServerSoundEntShutdown",
		"ServerSoundEntInsert",
		"ServerSoundEntResetClientSound",
	};

	// Since plan_engineapi_vtables E7 the four are methods of the managed-built
	// IManagedSoundEnt, which the generated table cannot leave unfilled -- the
	// "required" guard is structural -- and the retired rows are not required.
	const std::string areas = Read( kAreas2 );
	ASSERT_FALSE( areas.empty() );

	for ( size_t i = 0; i < sizeof( kSlots ) / sizeof( kSlots[ 0 ] ); i++ )
	{
		EXPECT_EQ( 1, CountOf( areas, std::string( " " ) + kSlots[ i ] + "(" ) )
			<< kSlots[ i ] << " is not a method of the managed-built area exactly once";

		EXPECT_EQ( 0, CountOf( rt, std::string( "f( " ) + kSlots[ i ] + " )" ) )
			<< kSlots[ i ] << " is retired and filled by nobody; requiring it fails every load";
	}
}

TEST( SoundEntManaged, TheTwoEngineSlotsAreGoneFromBothSidesOfTheBoundary )
{
	const std::string abi    = Read( kAbiHeader );
	const std::string client = Read( kClientAreasImpl );
	const std::string server = Read( kAreasImpl );
	const std::string table  = ReadRaw( kSlotTable );
	ASSERT_FALSE( abi.empty() );
	ASSERT_FALSE( client.empty() );
	ASSERT_FALSE( server.empty() );
	ASSERT_FALSE( table.empty() );

	// The list was never the engine's -- a fixed array in a game-DLL file swept
	// by a game-DLL think -- and a dead engine slot is the shape that survives
	// every check in this tree while meaning nothing.
	EXPECT_EQ( 0, CountOf( abi, "(*SoundEntQuery)" ) );
	EXPECT_EQ( 0, CountOf( abi, "(*SoundEntInsert)" ) );

	// Not wired on either side: a slot is wired by a generated shim-area
	// forward on each (C6c on the server, plan_client_vtables CC-last on the
	// client), and not back in the table those forwards are generated from.
	EXPECT_EQ( 0, ForwardCount( client, "SoundEntQuery", "" ) );
	EXPECT_EQ( 0, ForwardCount( client, "SoundEntInsert", "" ) );
	EXPECT_EQ( 0, ForwardCount( server, "SoundEntQuery", "" ) );
	EXPECT_EQ( 0, ForwardCount( server, "SoundEntInsert", "" ) );
	EXPECT_EQ( 0, TableRows( table, "SoundEntQuery" ) );
	EXPECT_EQ( 0, TableRows( table, "SoundEntInsert" ) );

	// And not back as shim-area methods, where a slot lives now: the server's
	// (C6c) or the client's (plan_client_vtables CC-last).
	const std::string areas = Read( kAreas );
	const std::string clientAreas = Read( kClientAreasH );
	ASSERT_FALSE( areas.empty() );
	ASSERT_FALSE( clientAreas.empty() );
	EXPECT_EQ( 0, CountOf( areas, " SoundEntQuery(" ) );
	EXPECT_EQ( 0, CountOf( areas, " SoundEntInsert(" ) );
	EXPECT_EQ( 0, CountOf( clientAreas, " SoundEntQuery(" ) );
	EXPECT_EQ( 0, CountOf( clientAreas, " SoundEntInsert(" ) );

	// VACUITY GUARD on every file, because zeroes over a read is exactly the
	// shape a failed open answers. EmitAmbientSound is a server area method
	// since C6c; ConNPrintf a client-only area method; SourceSharpLogFn the
	// one function-pointer declaration the header still has, found by the same
	// "(*Name)" needle shape (the engine API struct went at plan_client_vtables
	// CC-last, the ManagedApi struct at CE-last);
	// EntityGetModelName a shared slot both sides forward, the client to its
	// own body.
	EXPECT_EQ( 1, CountOf( areas, " EmitAmbientSound(" ) );
	EXPECT_EQ( 1, CountOf( clientAreas, " ConNPrintf(" ) );
	EXPECT_EQ( 1, CountOf( abi, "(*SourceSharpLogFn)" ) );
	EXPECT_EQ( 1, ForwardCount( client, "EntityGetModelName", "SourceSharp_ClientEntityGetModelName" ) );
	EXPECT_EQ( 1, ForwardCount( server, "EntityGetModelName", "SourceSharp_EntityGetModelName" ) );
	EXPECT_EQ( 1, TableRows( table, "EntityGetModelName" ) );
}

//=============================================================================//
// 6. CSoundControllerImp -- the same phase's other system.
//
// A DIFFERENT SHAPE OF FAILURE from CSoundEnt's, and worth its own section.
// soundenvelope.cpp is SHARED: it compiles into client.so as well, and its class
// was ALREADY split down the middle by #ifdef CLIENT_DLL. The reason removing
// the server's arm is safe is a property of a file the server no longer builds,
// so no server-side gate can see it -- which is T1's hazard in its exact shape,
// and is why client_shared_parity.sh has to keep the file in scope BY NAME after
// it drops out of the derived intersection.
//=============================================================================//

TEST( SoundControllerManaged, TheRowIsGone )
{
	const std::string table = Read( kTable );
	ASSERT_FALSE( table.empty() );

	EXPECT_EQ( 0, CountOf( table, "{ \"CSoundControllerImp\"," ) );
	EXPECT_EQ( 0, CountOf( table, "SoundControllerGameSystem" ) );

	// VACUITY GUARD, as above.
	EXPECT_EQ( 1, CountOf( table, "{ \"CPhysicsHook\"," ) );
}

TEST( SoundControllerManaged, TheRemovalCarriesItsReasonInTheTable )
{
	const std::string raw = ReadRaw( kTable );
	ASSERT_FALSE( raw.empty() );

	const size_t at = raw.find( "//   CSoundControllerImp " );
	ASSERT_NE( at, std::string::npos )
		<< "CSoundControllerImp is not in the table's list of systems that are "
		   "NOT rows";

	// Bounded at the NEXT entry rather than run to end-of-file: the ledger's H1
	// defect is a verdict word surviving elsewhere in the same file.
	// "The next entry", not a named one -- see TheRemovalCarriesItsReasonInTheTable
	// above for the mutation that got past the named form.
	const size_t next = raw.find( "\n//   C", at + 1 );
	ASSERT_NE( next, std::string::npos );

	const std::string block = raw.substr( at, next - at );

	EXPECT_NE( block.find( "NOT DELETED: MOVED" ), std::string::npos );
	EXPECT_NE( block.find( "FrameStage.SoundController" ), std::string::npos )
		<< "the block does not say WHERE the per-frame half went";
	EXPECT_NE( block.find( "LifecycleStage.SoundController" ), std::string::npos )
		<< "the block does not say where the LEVEL half went, and this system has "
		   "two stages where CSoundEnt had one";
}

TEST( SoundControllerManaged, SoundenvelopeCppIsNotInTheServersProjectAndTheShimIs )
{
	// Proof-of-read before the absence: the shim below IS listed.
	ASSERT_EQ( 1, NamedInServer( kEnvShim ) )
		<< "shell_soundenvelope.cpp is not compiled, so "
		   "CSoundEnvelopeController::GetController() is undefined";

	EXPECT_EQ( 0, NamedInServer( kEnvCpp ) )
		<< "game/shared/soundenvelope.cpp is still compiled into server.so, so "
		   "there are two sound controllers and only one of them is driven";
}

TEST( SoundControllerManaged, TheShimAnswersTheServersOwnInterface )
{
	const std::string shim = Read( kEnvShim );
	ASSERT_FALSE( shim.empty() );

	// P5 lane SP: physics.cpp's friction sounds were the last caller of
	// CSoundEnvelopeController::GetController(), and they ask
	// ss::SoundController() now (ss_soundcontroller.h). nm found no server object
	// importing GetController after that, so the Valve class and its sixteen
	// methods nobody called left the shim.
	EXPECT_EQ( 1, CountOf( shim, "ss::ISoundController &ss::SoundController()" ) )
		<< "nothing defines ss::SoundController, so physics.cpp's friction sounds "
		   "do not link";
	EXPECT_EQ( 0, CountOf( shim, "CSoundEnvelopeController::GetController" ) )
		<< "the shim answers the Valve controller again; nothing in server.so "
		   "asks for it";

	// THE INTERFACE IS ABSTRACT, and the number of methods it declares is READ
	// OUT OF THE HEADER rather than written down here.
	const std::string header = Read( kEnvSsHeader );
	ASSERT_FALSE( header.empty() );

	const size_t at  = header.find( "class ISoundController" );
	ASSERT_NE( at, std::string::npos );
	const size_t end = header.find( "};", at );
	ASSERT_NE( end, std::string::npos );

	const int nPureVirtuals = CountOf( header.substr( at, end - at ), "= 0;" );

	EXPECT_LT( 0, nPureVirtuals ) << "the header's class body was not found";

	// Every OVERRIDE in the shim, minus the four the recipient-filter copy
	// declares -- a different interface in the same file.
	const int nFilterOverrides = 4;

	EXPECT_EQ( nPureVirtuals + nFilterOverrides, CountOf( shim, "OVERRIDE" ) )
		<< "ss_soundcontroller.h declares " << nPureVirtuals << " pure virtuals and "
		   "the shim must override every one, and nothing more";
}

TEST( SoundControllerManaged, EveryForwardGuardsANullManagedTable )
{
	const std::string shim = Read( kEnvShim );
	ASSERT_FALSE( shim.empty() );

	// The window is real: this object is a function-local static constructed on
	// first use, and DLLInit runs LoadManagedRuntime after the engine has handed
	// the game DLL its interfaces.
	//
	// AND THERE IS DELIBERATELY NO DO-NOTHING TABLE. A zeroed
	// SourceSharpManagedApi is a struct of NULL function pointers, so "return a
	// table of stubs" turns the window from a no-op into a null call -- which
	// looks like a guard, reads like a guard, and crashes. The first draft of
	// this file did exactly that, and this is the check that would have caught
	// it.
	EXPECT_EQ( 0, CountOf( shim, "SourceSharpManagedApi m_Absent" ) )
		<< "a fallback TABLE is a struct of null function pointers, not a set of "
		   "stubs; every call through it is a null call";

	// One Ask() per forward, and every result tested before it is used. The
	// count is the thing: a forward added without a guard is invisible until the
	// one window between DLL load and the CLR coming up.
	const int nAsks = CountOf( shim, "= Ask();" );
	const int nGuards = CountOf( shim, "if ( !pApi" )
	                  + CountOf( shim, "if ( pApi )" )
	                  + CountOf( shim, "( pApi && pSound )" );

	EXPECT_LT( 0, nAsks ) << "no forward asks for the managed table at all";
	EXPECT_EQ( nAsks, nGuards )
		<< nAsks << " calls to Ask() and " << nGuards << " guards on the result";
}

TEST( SoundControllerManaged, TheShimHoldsNoFileScopeState )
{
	const std::string shim = Read( kEnvShim );
	ASSERT_FALSE( shim.empty() );

	// tools/statics-audit is the tree-wide gate and it draws the distinction
	// this check has to draw too: a static FUNCTION has internal linkage and is
	// fine, a static VARIABLE at file scope is the subject. Counting the word
	// `static` conflates them -- it read three here and called that a failure,
	// when the third is `Ask()`, a static MEMBER FUNCTION.
	//
	// So the three are NAMED, one line each, and the count is what refuses a
	// fourth. Named rather than only counted because this tree's recorded
	// defect 3 is a count that survives a rename.
	EXPECT_EQ( 1, CountOf( shim, "static IManagedSoundController *Ask()" ) )
		<< "a static MEMBER FUNCTION -- internal to the class, holds nothing";
	EXPECT_EQ( 1, CountOf( shim, "static CShellSoundController *ShellSoundController()" ) )
		<< "a static FUNCTION at file scope -- internal linkage, holds nothing";
	EXPECT_EQ( 1, CountOf( shim, "static CShellSoundController s_Controller;" ) )
		<< "the one static VARIABLE, and it is FUNCTION-LOCAL: the population "
		   "A1's gate deliberately does not cover, and the shape shell_decals.cpp "
		   "already uses";

	EXPECT_EQ( 3, CountOf( shim, "static " ) )
		<< "shell_soundenvelope.cpp has grown a fourth static beyond the two "
		   "functions and the accessor's own local instance";

	// And the thing the rule is actually about: no static at FILE scope holding
	// state. The patch table is a MEMBER of the controller object.
	EXPECT_EQ( 0, CountOf( shim, "\nstatic CUtlVector" ) );
	EXPECT_EQ( 1, CountOf( shim, "CUtlVector< CSoundPatch * > m_Patches;" ) )
		<< "the patch table is not a member of the controller, so it is state "
		   "somewhere this file should not have any";
}

TEST( SoundControllerManaged, ThePatchCrossesAsAHandleAndNeverAsAPointer )
{
	const std::string abi = Read( kAbiHeader );
	const std::string rt  = Read( kRuntime );
	ASSERT_FALSE( abi.empty() );
	ASSERT_FALSE( rt.empty() );

	// A slot taking a pointer would be handing managed code the address of a
	// native object, which is the thing this whole split exists to avoid.
	EXPECT_EQ( 0, CountOf( abi, "CSoundPatch" ) )
		<< "the ABI header names CSoundPatch, so something crosses as a pointer";

	static const char *kManaged[] =
	{
		"ServerSoundControllerCreate",
		"ServerSoundControllerDestroy",
		"ServerSoundControllerPlay",
		"ServerSoundControllerChangePitch",
		"ServerSoundControllerChangeVolume",
		"ServerSoundControllerFadeOut",
		"ServerSoundControllerShutdown",
		"ServerSoundControllerCommandAdd",
		"ServerSoundControllerCommandClear",
		"ServerSoundControllerPlayEnvelope",
		"ServerSoundControllerReset",
		"ServerSoundControllerGetPitch",
		"ServerSoundControllerGetVolume",
	};

	// Since plan_engineapi_vtables E6 the thirteen are methods of the
	// managed-built IManagedSoundController: a pure virtual the generated
	// table cannot leave unfilled, so the "required" guard is structural now
	// and the retired ManagedApi rows must NOT be required.
	const std::string areas = Read( kAreas2 );
	ASSERT_FALSE( areas.empty() );
	EXPECT_EQ( 0, CountOf( areas, "CSoundPatch" ) )
		<< "the managed-built area names CSoundPatch, so something crosses as a pointer";

	for ( size_t i = 0; i < sizeof( kManaged ) / sizeof( kManaged[ 0 ] ); i++ )
	{
		EXPECT_EQ( 1, CountOf( areas, std::string( " " ) + kManaged[ i ] + "(" ) )
			<< kManaged[ i ] << " is not a method of the managed-built area exactly once";

		EXPECT_EQ( 0, CountOf( rt, std::string( "f( " ) + kManaged[ i ] + " )" ) )
			<< kManaged[ i ] << " is retired and filled by nobody; requiring it fails every load";
	}
}

TEST( SoundControllerManaged, TheFourEngineSlotsAreDeclaredAndBound )
{
	// Server shim-area methods since plan_engineapi_vtables C6c.
	const std::string areas = Read( kAreas );
	const std::string impl  = Read( kAreasImpl );
	ASSERT_FALSE( areas.empty() );
	ASSERT_FALSE( impl.empty() );

	static const char *kEngine[] =
	{
		"SoundPatchEmit", "SoundPatchStop", "SoundPatchState", "SoundPatchFree",
	};

	for ( size_t i = 0; i < sizeof( kEngine ) / sizeof( kEngine[ 0 ] ); i++ )
	{
		EXPECT_EQ( 1, CountOf( areas, std::string( " " ) + kEngine[ i ] + "(" ) )
			<< kEngine[ i ] << " is not declared on a server shim area";

		// A pure virtual cannot be left unbound, but its forward can name the
		// wrong body: the only thing that makes the slot this one is the call.
		EXPECT_EQ( 1, CountOf( impl, std::string( "{ return ::SourceSharp_" ) + kEngine[ i ] + "(" ) )
			<< kEngine[ i ] << " is declared but its area method does not forward "
			   "to SourceSharp_" << kEngine[ i ];
	}
}

TEST( SoundControllerManaged, TheSaveRestoreOpsLeftWithTheValveController )
{
	const std::string shim = Read( kEnvShim );
	ASSERT_FALSE( shim.empty() );

	// soundenvelope.h declares GetSoundSaveRestoreOps for DEFINE_SOUNDPATCH. The
	// shim defined it (answering NULL) only because the header promised it;
	// no server object imported it (nm), and with the Valve controller gone
	// the shim no longer includes soundenvelope.h. A server TU that used the
	// macro would now fail to link, which is the loud failure wanted.
	EXPECT_EQ( 0, CountOf( shim, "GetSoundSaveRestoreOps" ) )
		<< "the shim defines GetSoundSaveRestoreOps again";
	EXPECT_EQ( 0, CountOf( shim, "#include \"soundenvelope.h\"" ) )
		<< "the shim includes soundenvelope.h again";
}

//=============================================================================//
// 7. THE FOURTH FRAME POINT, WHICH HAD NO PRODUCTION CALLER
//
// The second stage in this tree found in that state -- the first pair found
// OnLevelInitPreEntity the same way. A stage with a driver, a dispatcher and its
// own tests, and nothing in production calling it, passes every check that
// exists and does nothing.
//=============================================================================//

TEST( SoundControllerManaged, ThePreClientUpdateStageHasAProductionCaller )
{
	const std::string gameDll = Read( "sourcesharp/managed/SourceSharp.Server/ServerGameDll.cs" );
	const std::string module  = Read( "sourcesharp/managed/SourceSharp.Server/ServerModule.cs" );
	ASSERT_FALSE( gameDll.empty() );
	ASSERT_FALSE( module.empty() );

	EXPECT_EQ( 1, CountOf( gameDll, "FrameParticipants?.Invoke(" ) )
		<< "ServerGameDll.PreClientUpdate does not dispatch the managed frame "
		   "participants, so SoundController's per-frame pass never runs -- and "
		   "nothing warns, because the stage exists and is simply not called";

	EXPECT_EQ( 1, CountOf( module, "_gameDll.FrameParticipants = FramePreClientUpdate;" ) )
		<< "nothing SETS the delegate, so the call above invokes null and is "
		   "silently nothing";

	EXPECT_EQ( 1, CountOf( module, "_frames?.PreClientUpdate(frameTime)" ) )
		<< "FramePreClientUpdate does not reach the driver";

	// AND IT RUNS AFTER THE NATIVE WALK, which is an ordering claim rather than
	// a wiring one. The only other row declaring PreClientUpdate is CPhysicsHook
	// at index 1, and stock ran it first.
	const size_t walk = gameDll.find( "_frame.ServerSystemsPreClientUpdate();" );
	const size_t call = gameDll.find( "FrameParticipants?.Invoke(" );

	ASSERT_NE( walk, std::string::npos );
	ASSERT_NE( call, std::string::npos );

	EXPECT_LT( walk, call )
		<< "the managed participants are dispatched BEFORE the native table walk, "
		   "which moves the sound controller ahead of CPhysicsHook -- stock ran "
		   "the physics hook first";
}
