//===== SourceSharp — a player who leaves is announced before the shell goes ===//
//
// THE BUG, AND ITS SYMPTOM WAS FOUR LAYERS AWAY FROM ITS CAUSE.
//
// plan_entity.md step 2c moved the removal announcement out of
// ~CStandaloneEntity and into EntityDirectory's free pass: managed code tells
// the entity it is going, writes the liveness record and unbinds the registry
// row, and only then asks native for the shell. That is right for every removal
// MANAGED starts, and it is why a level teardown stopped costing six hundred
// inbound crossings.
//
// There is exactly ONE removal native starts: CServerGameClients::ClientDisconnect
// does `delete pStandalone` for a standalone player. Managed code is not in that
// call, so it reached none of 2c's replacement — and the comment above the
// delete went on saying "and tells managed code it is gone", which had been
// true before 2c and was not after it.
//
// MEASURED. Two live tools/live-gate runs on dm_lockdown, `entity-port`
// 19765e427 and lane live-rows' probe build, after a second client joined and
// left:
//
//   reload: ss_player '' (281477122) could not be rebuilt (PlayerNetState
//     needs 692 byte(s) and entity 2 ('ss_player') offers 0)
//   reload: 480 of 481 entit(ies) restored; 1 FAILED to rebuild, first
//     'ss_player'
//
// The departed player's managed instance was still in the registry, so the hot
// reload carried a record for it; the entity it named was long gone, so the
// bind was refused — correctly — and the record could not be replayed. Two
// other consequences of the same orphan, neither of which announces itself:
// LivenessMirror went on answering "alive" about a freed entity, which is the
// direction that record must never fail in, and SsPlayer.OnRemoved never ran,
// so PlayerCommandQueue kept the leaver's queue until its 256-batch staleness
// sweep happened to find it.
//
// WHY THIS TEST IS A SOURCE READ. ClientDisconnect needs an edict, the engine's
// client list and a running CLR; there is no unit harness in this tree that can
// call it. So this pins the SHAPE — that the announcement exists, that it is
// made BEFORE the delete, and that it has exactly one caller — and the
// behaviour is pinned live: tools/live-gate's hot-reload step now asserts
// `reload.failed.entities == 0` and names the class that failed.
//
// Same idiom as standalone_reload_idempotent_test.cpp, INCLUDING stripping
// comments before scanning: this file's subject carries paragraphs that spell
// the very strings asserted about, and a scan that could not tell prose from
// code would force the explanations out of the source. Prose counting as code
// has bitten this port four times.
//
//=============================================================================//

#include <fstream>
#include <string>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

const char *kClients  = "sourcesharp/game/server/shell_gameclients.cpp";
const char *kDispatch = "sourcesharp/game/server/shell_dispatch.cpp";
const char *kHeader   = "sourcesharp/game/server/shell_standalone.h";

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

std::string ReadRaw( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line; all += '\n';
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

std::string Tight( const std::string &s )
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

int CountOf( const std::string &hay, const std::string &needle )
{
	if ( needle.empty() )
		return 0;

	int n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + needle.size() ) )
	{
		n++;
	}
	return n;
}

std::string Code( const char *pszFile )
{
	return Tight( StripComments( ReadRaw( pszFile ) ) );
}

} // namespace

//-----------------------------------------------------------------------------
// The announcement exists, and it is the op the managed side already receives.
//
// ENTITY_OP_REMOVED is number 4, is in ENTITY_OP_ALWAYS_MASK, and Entry.cs
// still has its arm — the receiver was never deleted, only the sender. So this
// costs no new ABI slot and no version bump.
//-----------------------------------------------------------------------------
TEST( PlayerDisconnectRemoval, TheAnnouncerSendsTheRemovalOp )
{
	const std::string s = Code( kDispatch );

	ASSERT_FALSE( s.empty() ) << kDispatch << " could not be read.";

	EXPECT_EQ( 1, CountOf( s, "voidSourceSharp_AnnounceStandaloneRemoved(CStandaloneEntity*pEntity)" ) )
		<< "shell_dispatch.cpp must define exactly one announcer.";

	EXPECT_EQ( 1, CountOf( s, "DispatchOp(pEntity,ENTITY_OP_REMOVED,args);" ) )
		<< "and it must send ENTITY_OP_REMOVED, which is the op Entry.cs still "
		   "has an arm for -- a new slot would be a new ABI version for a "
		   "receiver that already exists.";
}

//-----------------------------------------------------------------------------
// 2c IS NOT COMING BACK, and this is the assertion that says so.
//
// The relay that made a level teardown six hundred crossings was
// CStandaloneEntity::OnManagedRemoved, called from the base destructor. It
// stays deleted. What this test's subject adds is one crossing per CLIENT
// DISCONNECT, of which a listen server sees a handful in a map's life.
//-----------------------------------------------------------------------------
TEST( PlayerDisconnectRemoval, TheDestructorRelayStaysDeleted )
{
	const std::string s = Code( kDispatch );

	ASSERT_FALSE( s.empty() ) << kDispatch << " could not be read.";

	EXPECT_EQ( 0, CountOf( s, "CStandaloneEntity::OnManagedRemoved" ) )
		<< "the per-entity destructor relay step 2c deleted must not return: it "
		   "was one inbound crossing per entity removed, six hundred at a level "
		   "teardown.";
}

//-----------------------------------------------------------------------------
// EXACTLY ONE CALLER, and it is the one path EntityDirectory's free pass cannot
// reach. A second caller would be a removal announced twice, or the per-entity
// relay reintroduced under a new name.
//-----------------------------------------------------------------------------
TEST( PlayerDisconnectRemoval, TheAnnouncerHasExactlyOneCaller )
{
	const std::string clients  = Code( kClients );
	const std::string dispatch = Code( kDispatch );
	const std::string header   = Code( kHeader );

	ASSERT_FALSE( clients.empty() )  << kClients  << " could not be read.";
	ASSERT_FALSE( dispatch.empty() ) << kDispatch << " could not be read.";
	ASSERT_FALSE( header.empty() )   << kHeader   << " could not be read.";

	EXPECT_EQ( 1, CountOf( clients, "SourceSharp_AnnounceStandaloneRemoved(pStandalone);" ) )
		<< "ClientDisconnect is the one caller.";

	EXPECT_EQ( 1, CountOf( header, "voidSourceSharp_AnnounceStandaloneRemoved(CStandaloneEntity*pEntity);" ) )
		<< "declared once in shell_standalone.h.";

	// The definition, the declaration and the one call: three mentions across
	// the three files, and no fourth.
	EXPECT_EQ( 1, CountOf( dispatch, "SourceSharp_AnnounceStandaloneRemoved" ) );
	EXPECT_EQ( 1, CountOf( clients,  "SourceSharp_AnnounceStandaloneRemoved" ) );
	EXPECT_EQ( 1, CountOf( header,   "SourceSharp_AnnounceStandaloneRemoved" ) );
}

//-----------------------------------------------------------------------------
// BEFORE THE DELETE, WHICH IS THE HALF AN EXISTENCE CHECK CANNOT SEE.
//
// The announcement carries the entity's handle and a span over its blob, and
// both are memory `delete pStandalone` returns to the allocator. Announcing
// after it would be a use-after-free that reads exactly like a working build
// until the allocator reuses the block.
//-----------------------------------------------------------------------------
TEST( PlayerDisconnectRemoval, TheAnnouncementIsMadeBeforeTheShellIsFreed )
{
	const std::string s = Code( kClients );

	ASSERT_FALSE( s.empty() ) << kClients << " could not be read.";

	const size_t announce = s.find( "SourceSharp_AnnounceStandaloneRemoved(pStandalone);" );
	const size_t destroy  = s.find( "deletepStandalone;" );

	ASSERT_NE( std::string::npos, announce ) << "the announcement is missing.";
	ASSERT_NE( std::string::npos, destroy )  << "the delete is missing.";

	EXPECT_LT( announce, destroy )
		<< "the announcement carries the handle and the blob, and the delete "
		   "frees both.";
}

//-----------------------------------------------------------------------------
// AND THE STALE COMMENT IS GONE.
//
// The line that said the destructor "tells managed code it is gone" was true
// before step 2c and false after it, and it is the reason this defect stood:
// anybody reading ClientDisconnect was told the announcement had already
// happened. Read from the RAW source, because a comment is exactly what the
// other tests here strip.
//-----------------------------------------------------------------------------
TEST( PlayerDisconnectRemoval, TheCommentThatSaidTheDestructorAnnouncedIsGone )
{
	const std::string raw = ReadRaw( kClients );

	ASSERT_FALSE( raw.empty() ) << kClients << " could not be read.";

	EXPECT_EQ( 0, CountOf( Tight( raw ), "andtellsmanagedcodeitisgone." ) )
		<< "ClientDisconnect must not claim the destructor makes the "
		   "announcement; step 2c moved it out and this call is what puts it "
		   "back explicitly.";
}
