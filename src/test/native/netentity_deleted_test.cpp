//========= SourceSharp — the netentity shell is gone, and stays gone ========//
//
// shim_underive_verdict_test.cpp recorded three shim TUs that still derived
// from CBaseEntity. Its third verdict — shell_netentity — was a "when", not a
// "whether": step 6 makes CBaseEntity an alias for IGameEntity, and a class
// deriving from the alias is ABSTRACT. Route (c), deleting the shell, was the
// only route that removed more C++ than it added. It is taken, so the verdict's
// assertions were deleted as they themselves instructed, and this replaced
// them: A DELETION NEEDS A GUARD AT LEAST AS MUCH AS A REFUSAL DOES. Three
// things could undo it and none would fail to compile:
//
//   1. THE CLASS COMES BACK. Abstract under the armed alias, and a class that
//      silently becomes abstract emits no diagnostic of its own — the error
//      lands wherever a member is asked for. That is the c_rope.h shape.
//
//   2. THE RETIRED ABI VALUE IS REUSED. SS_SHELL_NETENTITY = 0 is reserved, not
//      removed, because NetShellKind SENDS THESE NUMBERS.
//
//   3. THE FOUR DEAD ABI SLOTS ARE WIRED UP AGAIN, or reaped without pricing
//      the renumber of the thirty live slots after the first of them.
//
// THE STRIPPER IS LOAD-BEARING: the prose above spells the very strings the
// assertions look for, and an unstripped scan would read the explanation as the
// fact. ANCHORS: every file is identified before anything is asserted absent
// about it, so a renamed file is a red test and not a vacuous pass.
//
//=============================================================================//

#include <string>
#include <vector>
#include <utility>
#include <fstream>
#include <dirent.h>
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

std::string ReadRaw( const std::string &strAbsolute )
{
	std::ifstream file( strAbsolute.c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return all;
}

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' )
				i++;
			if ( i < strIn.size() )
				out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string ReadStripped( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	return StripComments( ReadRaw( std::string( pszRoot ) + "/" + strRelative ) );
}

std::string NoWhitespace( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

bool Exists( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;

	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + strRelative ).c_str(), &st ) == 0;
}

// Every .cpp and .h in a directory, comment-stripped, paired with its path.
void CollectStripped( const std::string &strRelDir,
                      std::vector< std::pair< std::string, std::string > > &out )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return;

	const std::string dir = std::string( pszRoot ) + "/" + strRelDir;
	DIR *pDir = opendir( dir.c_str() );
	if ( !pDir )
		return;

	for ( struct dirent *pEnt = readdir( pDir ); pEnt; pEnt = readdir( pDir ) )
	{
		const std::string name( pEnt->d_name );
		const bool bSource = name.size() > 4 && name.compare( name.size() - 4, 4, ".cpp" ) == 0;
		const bool bHeader = name.size() > 2 && name.compare( name.size() - 2, 2, ".h" ) == 0;
		if ( !bSource && !bHeader )
			continue;

		const std::string raw = ReadRaw( dir + "/" + name );
		if ( raw.empty() )
			continue;

		out.push_back( std::make_pair( strRelDir + "/" + name, StripComments( raw ) ) );
	}
	closedir( pDir );
}

// The whole shim, both sides plus the shared half. The ABI declaration lives in
// host/, which is deliberately NOT collected, so any hit in here is a USE.
void CollectShim( std::vector< std::pair< std::string, std::string > > &out )
{
	CollectStripped( "sourcesharp/game/server", out );
	CollectStripped( "sourcesharp/game/client", out );
	CollectStripped( "sourcesharp/game/shared", out );
}

// EVERY .cpp AND .h THAT COULD COMPILE INTO EITHER DLL, recursively.
//
// CollectShim above is a FLOOR and step 8a needed better than one. It walks
// three directories, non-recursively, and none of them is `src/game` -- so a
// stock TU calling a dead slot, and 48 server plus 195 client stock TUs are
// still compiled, would not have been seen by it. The abi-requests/8a.md ask to
// REAP the four slots rests on "the declaration is the only occurrence in the
// tree", and that is a claim about the tree, not about the shim.
//
// The two exclusions are the two places the NAMES legitimately appear:
//   * sourcesharp/host/sourcesharp.h -- the declaration itself.
//   * sourcesharp/test             -- this file's own needles, and the prose in
//                                     managed_runtime_server_test.cpp.
// Everything else under src/game, src/public and src/sourcesharp is read.
void CollectRecursive( const std::string &strRelDir,
                       std::vector< std::pair< std::string, std::string > > &out )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return;

	const std::string dir = std::string( pszRoot ) + "/" + strRelDir;
	DIR *pDir = opendir( dir.c_str() );
	if ( !pDir )
		return;

	for ( struct dirent *pEnt = readdir( pDir ); pEnt; pEnt = readdir( pDir ) )
	{
		const std::string name( pEnt->d_name );
		if ( name == "." || name == ".." )
			continue;

		const std::string rel = strRelDir + "/" + name;

		if ( pEnt->d_type == DT_DIR )
		{
			if ( rel != "sourcesharp/test" )
				CollectRecursive( rel, out );
			continue;
		}

		const bool bSource = name.size() > 4 && name.compare( name.size() - 4, 4, ".cpp" ) == 0;
		const bool bHeader = name.size() > 2 && name.compare( name.size() - 2, 2, ".h" ) == 0;
		if ( !bSource && !bHeader )
			continue;

		if ( rel == "sourcesharp/host/sourcesharp.h" )
			continue;

		const std::string raw = ReadRaw( dir + "/" + name );
		if ( raw.empty() )
			continue;

		out.push_back( std::make_pair( rel, StripComments( raw ) ) );
	}
	closedir( pDir );
}

struct File_t { const char *pszRel; const char *pszAnchor; };

const File_t kGenericNetTable = { "sourcesharp/game/shared/generic_nettable.h",
	"enum SourceSharpNetShell" };
const File_t kStandaloneCpp = { "sourcesharp/game/server/shell_standalone.cpp",
	"int32_t SourceSharp_NetPublishType(" };
const File_t kRecvTable = { "sourcesharp/game/client/shell_recvtable.cpp",
	"int32_t SourceSharp_NetPublishType(" };
const File_t kHostHeader = { "sourcesharp/host/sourcesharp.h",
	"SourceSharp_LoadServer(" };
// The managed half. Until plan_client_vtables CE-last this was the C# mirror
// of the ManagedApi struct (LegacyStructs.cs); neither side has a struct now,
// and the side's load that filled one is where a managed slot would have to
// be bound to come back (the Bootstrap's Entry.cs until plan_side_entries SE2).
const File_t kAbiCs = { "sourcesharp/managed/SourceSharp.Shared.Interop/SideLoad.cs",
	"public static int Load(Side side, CrossingOp op, void* pShimRoot," };

std::string ReadAnchored( const File_t &file )
{
	const std::string body = ReadStripped( file.pszRel );

	EXPECT_FALSE( body.empty() )
		<< file.pszRel << " could not be read; every fact this file rests on "
		   "would otherwise pass vacuously.";

	if ( !body.empty() )
	{
		EXPECT_NE( body.find( file.pszAnchor ), std::string::npos )
			<< file.pszRel << " no longer contains its anchor \""
			<< file.pszAnchor << "\" — this file's scans are now aimed at "
			   "nothing. Fix the anchor before trusting a green.";
	}
	return body;
}

} // namespace

//=============================================================================//
// 1. THE SHELL IS GONE, and nothing names it.
//
// Both halves are asserted, and neither alone is a check. A missing FILE with a
// live reference somewhere is a build that does not link; a present file that
// nothing references is a class quietly waiting to be constructed again.
//=============================================================================//
TEST( NetEntityDeleted, NeitherHalfOfTheShellExists )
{
	// The directory is real, so "the file is not there" cannot be "the tree is
	// not there".
	ASSERT_TRUE( Exists( "sourcesharp/game/shared" ) )
		<< "the shared shim directory is missing; this test would pass vacuously.";
	ASSERT_TRUE( Exists( "sourcesharp/game/shared/generic_payload.cpp" ) )
		<< "a file that IS expected in that directory is missing too — the path "
		   "this test looks under has moved, and its absence checks mean nothing.";

	EXPECT_FALSE( Exists( "sourcesharp/game/shared/shell_netentity.h" ) )
		<< "shell_netentity.h is back. It declares CSourceSharpNetEntity, which "
		   "derives from CBaseEntity — an alias for IGameEntity once step 6 is "
		   "armed, which makes the class ABSTRACT and unconstructable. A class "
		   "that silently becomes abstract emits NO diagnostic of its own; the "
		   "error lands wherever a member is asked for. If the shell is genuinely "
		   "wanted again, route (c) has been reversed and this whole file is "
		   "obsolete — say so and delete it.";

	EXPECT_FALSE( Exists( "sourcesharp/game/shared/shell_netentity.cpp" ) )
		<< "shell_netentity.cpp is back. See the header assertion above.";

	EXPECT_FALSE( Exists( "sourcesharp/test/netentity_table_pairing_test.cpp" ) )
		<< "the netentity pairing test is back. It diffed two hand-copied prop "
		   "lists that no longer exist; if something needs diffing again, the "
		   "shell has returned and this file is obsolete.";
}

TEST( NetEntityDeleted, NoShimFileNamesTheShell )
{
	std::vector< std::pair< std::string, std::string > > files;
	CollectShim( files );

	ASSERT_FALSE( files.empty() )
		<< "scanned no files — this test would pass vacuously.";
	ASSERT_GT( files.size(), (size_t)20 )
		<< "scanned only " << files.size() << " files. The shim is far bigger "
		   "than that, so the walk is looking in the wrong place and every "
		   "absence below is meaningless.";

	// The class on both sides, and the header. Whitespace-insensitive for the
	// same reason the verdict file's checks are: a line break inside a
	// declaration must not hide it.
	static const char *const kGone[] = {
		"CSourceSharpNetEntity",
		"C_SourceSharpNetEntity",
		"shell_netentity.h",
		"SourceSharp_GetTierServerClass",
		"SourceSharp_NetEntityFitBlobToTier",
		"SourceSharp_NetEntityResyncSchema",
		"SOURCESHARP_TIER_PROPS_NETENTITY",
	};

	for ( size_t iFile = 0; iFile < files.size(); iFile++ )
	{
		const std::string body = NoWhitespace( files[ iFile ].second );

		for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
		{
			EXPECT_EQ( body.find( NoWhitespace( kGone[ i ] ) ), std::string::npos )
				<< files[ iFile ].first << " names \"" << kGone[ i ] << "\", which "
				<< "went with the deleted netentity shell. Either the shell is "
				   "coming back — see NeitherHalfOfTheShellExists — or this is a "
				   "merge that resurrected a line.";
		}
	}
}

//=============================================================================//
// 2. THE RETIRED ABI VALUE IS STILL RESERVED, and still refused at both
//    publish paths.
//
// The values are the ABI: NetShellKind in NetSchema.cs sends these numbers.
// Reusing 0 changes what one side means by a number the other reads.
//=============================================================================//
TEST( NetEntityDeleted, TheRetiredShellValueIsReservedNotReused )
{
	const std::string tiers = NoWhitespace( ReadAnchored( kGenericNetTable ) );

	EXPECT_NE( tiers.find( "SS_SHELL_NETENTITY=0" ), std::string::npos )
		<< "SS_SHELL_NETENTITY is no longer declared as 0. It is a RESERVED ABI "
		   "value, kept deliberately after the shell was deleted: NetShellKind "
		   "sends these numbers, so removing it invites 0 being handed to "
		   "something else and STANDALONE being renumbered onto it.";

	EXPECT_NE( tiers.find( "SS_SHELL_STANDALONE=1" ), std::string::npos )
		<< "SS_SHELL_STANDALONE is no longer 1. That is a wire-visible "
		   "renumbering of the only live shell.";
}

TEST( NetEntityDeleted, BothPublishPathsRefuseTheRetiredShell )
{
	// One assertion per SIDE, because the failure this guards is one side
	// refusing and the other silently accepting — which puts a managed type on
	// tables only one end believes in, and the engine reports neither.
	const std::string server = NoWhitespace( ReadAnchored( kStandaloneCpp ) );
	const std::string client = NoWhitespace( ReadAnchored( kRecvTable ) );

	EXPECT_NE( server.find( "if(nShell==SS_SHELL_NETENTITY)" ), std::string::npos )
		<< "the SERVER's SourceSharp_NetPublishType no longer refuses the retired "
		   "shell. A managed build older than this native one would have its "
		   "types land on the hierarchy's tables with nothing said.";

	EXPECT_NE( client.find( "if(nShell==SS_SHELL_NETENTITY)" ), std::string::npos )
		<< "the CLIENT's SourceSharp_NetPublishType no longer refuses the retired "
		   "shell. The two halves must stay mirrors — a type refused on one side "
		   "and published on the other is the failure with no symptom.";
}

//=============================================================================//
// 3. THE TIER LIST COLLAPSED TO ONE NAME PAIR, which is the C++ the deletion
//    actually removed from the shared header.
//=============================================================================//
TEST( NetEntityDeleted, EveryTierCarriesExactlyOneNamePair )
{
	const std::string tiers = NoWhitespace( ReadAnchored( kGenericNetTable ) );

	// The second pair's members are gone from the struct.
	EXPECT_EQ( tiers.find( "constchar*pStdClass;" ), std::string::npos )
		<< "SourceSharpTier_t has a second class-name member again. Four names "
		   "per tier existed because two UNRELATED C++ classes each needed their "
		   "own ServerClass at every size; there is one shell now.";
	EXPECT_EQ( tiers.find( "constchar*pStdTable;" ), std::string::npos )
		<< "SourceSharpTier_t has a second table-name member again.";

	// And the surviving pair is the hierarchy's, at every size. Asserted per
	// row rather than by counting, so a row that lost its names names itself.
	static const char *const kRows[] = {
		"\"CSourceSharpStdEnt32\",\"DT_SourceSharpStdEnt32\",32",
		"\"CSourceSharpStdEnt64\",\"DT_SourceSharpStdEnt64\",64",
		"\"CSourceSharpStdEnt128\",\"DT_SourceSharpStdEnt128\",128",
		"\"CSourceSharpStdEnt256\",\"DT_SourceSharpStdEnt256\",256",
		"\"CSourceSharpStdEnt512\",\"DT_SourceSharpStdEnt512\",512",
		"\"CSourceSharpStdEnt1024\",\"DT_SourceSharpStdEnt1024\",1024",
		"\"CSourceSharpStdEnt2048\",\"DT_SourceSharpStdEnt2048\",2048",
	};

	for ( size_t i = 0; i < sizeof( kRows ) / sizeof( kRows[ 0 ] ); i++ )
	{
		EXPECT_NE( tiers.find( kRows[ i ] ), std::string::npos )
			<< "tier row " << i << " is not \"" << kRows[ i ] << "\". These names "
			   "are what a SendTable and its RecvTable PAIR on, and they are "
			   "matched case-insensitively — a stem change is a client that "
			   "begins joining and stops.";
	}

	// The deleted shell's real-prop count went with it; the hierarchy's stays.
	EXPECT_NE( tiers.find( "SOURCESHARP_TIER_PROPS_STANDALONE=11" ), std::string::npos )
		<< "the hierarchy's real-prop count has moved. Anything reporting a "
		   "tier's prop total reads this rather than computing 10 + bytes/4.";
}

//=============================================================================//
// 4. THE FOUR DEAD SLOTS ARE REAPED, AND STAY REAPED.
//
// This section used to guard the opposite decision: the four callbacks were
// LEFT in the ABI struct because removing them renumbered 141 live slots in a
// struct several lanes were appending to concurrently.
//
// They are gone as of SOURCESHARP_ABI_VERSION 240. What closed it was the live
// half of step 8a's differential, which 8a itself could not take because the
// box had no GPU: row 2 of .claude/entity-port/DEFERRED_LIVE_GATES.md armed
// all four slots on BOTH sides with an [UnmanagedCallersOnly] probe, and
// wrapped the LIVE FramePreThink slot in the same probe as a CONTROL. Two
// passes of tools/live-gate over 20 steps, 0 skipped, exit 0: the CONTROL
// printed in three separate game processes and no probe on any of the four
// ever fired. The control arm is why that is evidence -- without it, a build
// that failed to deploy and a log written elsewhere both look identical to it.
//
// So these two tests now assert the REMOVAL rather than the presence, which is
// what stops a later lane re-adding a slot with no subject.
//=============================================================================//
TEST( NetEntityDeleted, TheFourNetEntitySlotsAreReapedFromBothSides )
{
	// Anchored, so a file that could not be read fails rather than passing for
	// having found nothing -- which is the exact shape an absence check fails in.
	const std::string host = ReadAnchored( kHostHeader );
	const std::string abi  = ReadAnchored( kAbiCs );
	ASSERT_FALSE( host.empty() );
	ASSERT_FALSE( abi.empty() );

	// THE POSITIVE CONTROL. A needle that IS there, found by the same machinery
	// over the same two strings. Without it, a ReadStripped that started
	// returning the wrong file would report every slot below as absent and this
	// test would pass by finding nothing anywhere.
	//
	// RE-AIMED at plan_client_vtables CE-last: the needle was OnCreateMove, a
	// ManagedApi slot, and the struct is gone from both files. The load is the
	// live crossing both of them still declare (per side since plan_side_entries
	// SE1).
	EXPECT_NE( host.find( "SourceSharp_LoadServer(" ), std::string::npos )
		<< "host/sourcesharp.h does not name SourceSharp_LoadServer, the live "
		   "load export. This search is not reading what it thinks it is, so the "
		   "absences below mean nothing.";
	EXPECT_NE( abi.find( "LoadSide(" ), std::string::npos )
		<< "Entry.cs does not name LoadSide, the live managed load. "
		   "Same conclusion as above.";

	static const char *const kSlots[] = {
		"OnNetEntitySpawned",
		"OnNetEntityDataChanged",
		"OnNetEntityDestroyed",
		"OnNetEntityPayloadChanged",
	};

	for ( size_t i = 0; i < sizeof( kSlots ) / sizeof( kSlots[ 0 ] ); i++ )
	{
		EXPECT_EQ( host.find( kSlots[ i ] ), std::string::npos )
			<< "host/sourcesharp.h declares " << kSlots[ i ] << " again. It was "
			   "reaped at ABI 240 after a live run proved nothing reached it. "
			   "Re-adding a slot is an ABI change: it needs its own version bump "
			   "and, if it is a ManagedApi slot, a caller. A slot with neither is "
			   "the thing this file exists to stop.";

		EXPECT_EQ( abi.find( kSlots[ i ] ), std::string::npos )
			<< "Entry.cs names " << kSlots[ i ] << " again. It was reaped at "
			   "ABI 240; a managed entry for it has no native caller.";
	}
}

TEST( NetEntityDeleted, NothingAnywhereInTheTreeNamesTheFourReapedSlots )
{
	// THE SAME QUESTION, ASKED OF THE WHOLE TREE -- step 8a's scan, kept and
	// inverted. Before the reap it asked "does anything CALL these"; now it
	// asks "has anything brought them back", and the answer has to be about
	// every file that could compile into either DLL -- 48 stock server TUs and
	// 195 stock client ones are still in the build -- rather than about the
	// port's own files.
	std::vector< std::pair< std::string, std::string > > files;
	CollectRecursive( "game", files );
	CollectRecursive( "public", files );
	CollectRecursive( "sourcesharp", files );

	// THE VACUITY FLOOR, and it is deliberately large. `src/game` alone is over
	// a thousand files; a walk that stopped at a permission error or a renamed
	// directory would otherwise report "nobody names them" about nothing.
	// 1000 -> 900: the port shrinks the tree (994 read at plan_server_native_v2 T3).
	ASSERT_GT( files.size(), 900u )
		<< "only " << files.size() << " source files were read. The recursive "
		   "walk is not reaching src/game, src/public and src/sourcesharp, so "
		   "this test is not asking the question it claims to.";

	// ...AND THE WALK REACHED THE PLACES THAT MATTER, named rather than counted:
	// a file from each of the three roots, so a walk that read a thousand files
	// out of one of them still fails.
	//
	// RE-ANCHORED when the farm stopped carrying stock files neither DLL
	// compiles. The two stock needles were game/server/baseentity.cpp and
	// game/client/c_baseentity.cpp, and neither is compiled any more, so the
	// walk no longer sees them. They are now a stock server header and a stock
	// client TU the build still compiles.
	//
	// RE-ANCHORED AGAIN at plan_server_novalve.md P6: game/server/player.h is
	// compiled by nothing since P5 and left the farm, and no stock file is left
	// under game/server at all. The server needle is now a stock header under
	// public/, the one root of the three this list had never named.
	bool bStockPublic = false, bStockClient = false, bShim = false;
	for ( size_t i = 0; i < files.size(); i++ )
	{
		bStockPublic = bStockPublic || files[ i ].first == "public/tier0/dbg.h";
		bStockClient = bStockClient || files[ i ].first == "game/client/clientleafsystem.cpp";
		bShim        = bShim        || files[ i ].first == "sourcesharp/game/server/shell_dispatch.cpp";
	}
	EXPECT_TRUE( bStockPublic ) << "public/tier0/dbg.h was not read";
	// (cdll_util.cpp was the client needle until it left the build in C8.)
	EXPECT_TRUE( bStockClient ) << "game/client/clientleafsystem.cpp was not read";
	EXPECT_TRUE( bShim ) << "sourcesharp/game/server/shell_dispatch.cpp was not read";

	// THE POSITIVE CONTROL FOR THE SEARCH ITSELF. `OnCreateMove` is a live slot
	// and the shim reaches it, so the same find() over the same collected
	// bodies must hit. A needle that matches nothing anywhere is how a
	// whole-tree absence check passes while reading nothing useful.
	size_t nLiveHits = 0;
	for ( size_t i = 0; i < files.size(); i++ )
		if ( files[ i ].second.find( "OnCreateMove" ) != std::string::npos )
			nLiveHits++;
	EXPECT_GT( nLiveHits, 0u )
		<< "not one of the " << files.size() << " files read names OnCreateMove, "
		   "a slot the shim demonstrably reaches. The bodies being searched are "
		   "not the files being counted.";

	static const char *const kSlots[] = {
		"OnNetEntitySpawned",
		"OnNetEntityDataChanged",
		"OnNetEntityDestroyed",
		"OnNetEntityPayloadChanged",
	};

	std::string offenders;
	for ( size_t iFile = 0; iFile < files.size(); iFile++ )
	{
		for ( size_t i = 0; i < sizeof( kSlots ) / sizeof( kSlots[ 0 ] ); i++ )
		{
			if ( files[ iFile ].second.find( kSlots[ i ] ) != std::string::npos )
				offenders += "\n    " + files[ iFile ].first + " names " + kSlots[ i ];
		}
	}

	EXPECT_TRUE( offenders.empty() )
		<< "a reaped OnNetEntity* slot is named again:"
		<< offenders
		<< "\n  All four were removed from SourceSharpManagedApi at ABI 240, on "
		   "the evidence of a whole-tree scan PLUS a live run with every one of "
		   "them armed and a CONTROL probe on FramePreThink. A name here is "
		   "either a slot coming back -- which needs its own version bump and a "
		   "caller -- or a stale reference to something that no longer exists.";
}
