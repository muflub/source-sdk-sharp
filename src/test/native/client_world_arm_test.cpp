//========= SourceSharp — the client's world arm, and the seam it empties =====//
//
// plan_entity.md §3.3, lane `c8-listseam`.
//
// WHAT THIS PINS, AND WHY IT IS A GTEST AND NOT A LIVE ASSERTION.
//
// The client's slot table binds SHELLS. Worldspawn is not one -- on this side
// it is a C_World, a process-lifetime singleton bound to entity 0 for the life
// of a map -- so §3.3's table has never held it and never will: a second type
// behind the same void * would make the resolver's single static_cast answer
// about whichever object happened to be there.
//
// That left exactly one thing able to answer "what is at entity 0": what is
// left of CClientEntityList, in game/client/cliententitylist.cpp, the file the
// client flip deletes. Lane `entlist-client` measured it -- 2.17M of the seam's
// 5.07M answers over a two-pass live gate were this one object asked BY INDEX,
// the engine's own currency -- and lane `c8-listseam` re-measured it on
// e27c719c0, where it is the ONLY population the seam has left:
//
//     port-seam answers: unknown-by-index 2349055  unknown-by-handle 0
//                        highest-index 7613  number-of-entities 0
//     port-seam answers BY CLASS (1 class(es)):
//       CWorld  by-index 2349055  by-handle 0  entries 0..0  (ONE entry)
//
// The arm that answers for it now is the SERVER'S arm, on this side:
// world_subject.h's "the world is its own arm, matched on its own handle". Its
// STORAGE is in world_adapter_client.cpp, beside the object that owns the
// handle, and its RULES are four inlines in shell_cliententitylist.h that take
// handles and nothing else -- which is what lets this file hold two handles and
// pin every case without a game.
//
// THE LIVE RUN IS STILL THE GATE. These facts say the rule is right; only a
// live two-pass run with a map reload says the game still draws. Both are
// reported. A lane whose only evidence is that a counter fell has not passed.
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <sstream>
#include <sys/stat.h>

#include "gtest/gtest.h"

#include "basehandle.h"
#include "shell_cliententitylist.h"

namespace
{

//-----------------------------------------------------------------------------
// A handle built from an entry and a serial, which is what every case here is.
//-----------------------------------------------------------------------------
CBaseHandle H( int nEntry, int nSerial )
{
	return CBaseHandle( nEntry, nSerial );
}

const CBaseHandle kUnbound = CBaseHandle();

//-----------------------------------------------------------------------------
// THE SOURCE READER, and it STRIPS COMMENTS.
//
// Three of the facts below are about which file a definition lives in, and the
// files this lane moved code OUT of now carry a comment naming what left. A raw
// grep would read that prose as the definition and report the move undone --
// COMMON.md's "prose counts as code to a raw grep", which has already cost this
// port a wrong census. So every scan runs over stripped text, and
// TheStripperRemovesACommentThatNamesADefinition proves the stripper works
// rather than assuming it.
//-----------------------------------------------------------------------------
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

std::string StripComments( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );

	enum { kCode, kLine, kBlock, kString, kChar } state = kCode;

	for ( size_t i = 0; i < in.size(); i++ )
	{
		const char c = in[ i ];
		const char n = ( i + 1 < in.size() ) ? in[ i + 1 ] : '\0';

		switch ( state )
		{
		case kCode:
			if ( c == '/' && n == '/' )      { state = kLine;  i++; }
			else if ( c == '/' && n == '*' ) { state = kBlock; i++; }
			else
			{
				if ( c == '"' )       state = kString;
				else if ( c == '\'' ) state = kChar;
				out += c;
			}
			break;

		case kLine:
			// The newline is KEPT, so line-oriented reading of the result still
			// works and two lines cannot be glued into one match.
			if ( c == '\n' ) { state = kCode; out += c; }
			break;

		case kBlock:
			if ( c == '*' && n == '/' ) { state = kCode; i++; }
			else if ( c == '\n' )       { out += c; }
			break;

		case kString:
		case kChar:
			out += c;
			if ( c == '\\' && i + 1 < in.size() ) { out += n; i++; }
			else if ( state == kString && c == '"' )  state = kCode;
			else if ( state == kChar   && c == '\'' ) state = kCode;
			break;
		}
	}

	return out;
}

std::string ReadStripped( const std::string &rel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the working directory";
	if ( pszRoot == NULL )
		return std::string();

	const std::string path = std::string( pszRoot ) + "/" + rel;
	std::ifstream f( path.c_str() );
	EXPECT_TRUE( f.good() ) << "cannot open " << path;
	std::stringstream ss;
	ss << f.rdbuf();
	return StripComments( ss.str() );
}

bool Has( const std::string &hay, const std::string &needle )
{
	return hay.find( needle ) != std::string::npos;
}

// The definitions this lane moved, by the text that IS the definition rather
// than by the bare name -- a bare name matches a call site as well.
//
// IT WAS ELEVEN UNTIL PHASE 1c, WHICH DELETED TWO OF THEM RATHER THAN MOVING
// THEM AGAIN. SourceSharp_LegacyListHighestEntityIndex (`return -1`) and
// SourceSharp_LegacyListNumberOfEntities (`return 0`) each had exactly one
// reader: an `if ( nLegacy > ours )` arm in shell_cliententitylist.cpp whose
// only effect was to bump a fallback counter that could therefore never move.
// Arm, counter and stub went together.
//
// A SHORTENED LIST IS NOT A WEAKENED TEST HERE, because the two are asserted
// absent from BOTH files by TheTwoCountStubsAreGoneFromBothFiles below --
// which is strictly more than the old list said about them (it allowed them to
// exist anywhere so long as it was not the stock file).
const char *kMovedDefinitions[] = {
	"CBaseHandle SourceSharp_LegacyListBind(",
	"void SourceSharp_LegacyListUnbind(",
	"IClientUnknown *SourceSharp_LegacyListUnknownFromIndex(",
	"IClientUnknown *SourceSharp_LegacyListUnknownFromHandle(",
};

const size_t kMovedCount = sizeof( kMovedDefinitions ) / sizeof( kMovedDefinitions[ 0 ] );

// THE SEVEN PHASE 1c DELETED, by BARE name -- here a call site matching is
// exactly what is wanted, because a reintroduced CALLER is the regression just
// as much as a reintroduced definition.
//
// Two are the count stubs (see the block above). The other five are the
// handle-crossing ask counters and their recorder: c_baseentity.h's
// SDKEntityFromUnknown bumped two and called the recorder, and
// cs_cl_handle_cross_check printed all four -- and that console command, the
// only reader any of them ever had, went with the client bring-up instruments.
// The SAFETY they watched (an IClientUnknown that is not an IClientEntity
// surviving the crossing) is asserted structurally by
// client_typed_handle_sweep_test.cpp and does not need a running count.
const char *kDeletedSeamCalls[] = {
	"SourceSharp_LegacyListHighestEntityIndex",
	"SourceSharp_LegacyListNumberOfEntities",
	"SourceSharp_RecordNonEntityAsk",
	"g_nSDKEntityAsks",
	"g_nSDKEntityAsksThatWereNotEntities",
	"g_nNonEntityAskNames",
	"g_pszNonEntityAskNames",
};

const size_t kDeletedCount = sizeof( kDeletedSeamCalls ) / sizeof( kDeletedSeamCalls[ 0 ] );

// The stock header SDKEntityFromUnknown is declared in. It is the FEEDER side
// of the ask counters, and it is a different file from either list, so a
// deletion that left the increments behind would break the link and not this
// test unless it is read here too.
const std::string kClientEntityHdr = "game/client/c_baseentity.h";

const std::string kStockH   = "game/client/cliententitylist.h";
const std::string kPortCpp  = "sourcesharp/game/client/shell_cliententitylist.cpp";
const std::string kPortH    = "sourcesharp/game/client/shell_cliententitylist.h";
const std::string kWorldCpp = "sourcesharp/game/client/world_adapter_client.cpp";

} // namespace


//=============================================================================
// THE STRIPPER, first, because three facts below rest on it
//=============================================================================

TEST( ClientWorldArm, TheStripperRemovesACommentThatNamesADefinition )
{
	const std::string s = StripComments(
		"// void SourceSharp_RecordNonEntityAsk( x )\n"
		"int kept = 1;\n" );

	EXPECT_FALSE( Has( s, "SourceSharp_RecordNonEntityAsk" ) )
		<< "the stripper left a line comment in, so every move assertion below "
		   "would read the note saying what LEFT as the thing itself";
	EXPECT_TRUE( Has( s, "int kept = 1;" ) );
}

TEST( ClientWorldArm, TheStripperRemovesABlockCommentAndKeepsAStringLiteral )
{
	const std::string s = StripComments(
		"/* int g_nSDKEntityAsks = 0; */\n"
		"const char *p = \"// not a comment\";\n" );

	EXPECT_FALSE( Has( s, "g_nSDKEntityAsks" ) );
	EXPECT_TRUE( Has( s, "\"// not a comment\"" ) )
		<< "a // inside a string literal is not a comment, and a stripper that "
		   "ate it would silently shorten every file it read";
}


//=============================================================================
// HALF A: the port's eleven definitions are in the port's own list file
//=============================================================================

TEST( ClientWorldArm, ThePortsOwnListFileDefinesAllOfThem )
{
	const std::string port = ReadStripped( kPortCpp );

	for ( size_t i = 0; i < kMovedCount; i++ )
	{
		EXPECT_TRUE( Has( port, kMovedDefinitions[ i ] ) )
			<< kPortCpp << " does not define " << kMovedDefinitions[ i ]
			<< " -- the move is only half done if the definition is gone from "
			   "the stock file and has not arrived here";
	}
}

TEST( ClientWorldArm, TheStockClientListHeaderDeclaresNoneOfTheSeamCalls )
{
	const std::string stock = ReadStripped( kStockH );

	EXPECT_FALSE( Has( stock, "SourceSharp_LegacyListBind(" ) );
	EXPECT_FALSE( Has( stock, "SourceSharp_LegacyListUnbind(" ) );
	EXPECT_FALSE( Has( stock, "SourceSharp_LegacyListUnknownFromIndex(" ) );
	EXPECT_FALSE( Has( stock, "SourceSharp_LegacyListUnknownFromHandle(" ) );
}

TEST( ClientWorldArm, ThePortsOwnListHeaderDeclaresAllOfTheSeamCalls )
{
	const std::string port = ReadStripped( kPortH );

	EXPECT_TRUE( Has( port, "SourceSharp_LegacyListBind(" ) );
	EXPECT_TRUE( Has( port, "SourceSharp_LegacyListUnbind(" ) );
	EXPECT_TRUE( Has( port, "SourceSharp_LegacyListUnknownFromIndex(" ) );
	EXPECT_TRUE( Has( port, "SourceSharp_LegacyListUnknownFromHandle(" ) );
}

//-----------------------------------------------------------------------------
// AND THE SEVEN THAT PHASE 1c DELETED ARE IN NONE OF THE FILES, BY NAME.
//
// This is the assertion the shortened kMovedDefinitions list would otherwise
// have given up, and it is stronger than what it replaces: the old list only
// required them to be somewhere other than the stock .cpp. A stub answering a
// constant that nothing reads is not a seam -- it is a line that makes the
// seam look wider than it is -- and the four fallback counters the two used to
// feed were pinned at zero on every run that could ever be taken, because the
// arms reading them compare `> -1` and `> 0` against a table count that is
// never smaller.
//-----------------------------------------------------------------------------
TEST( ClientWorldArm, TheDeletedSeamAndCounterNamesAreInNoneOfTheFiles )
{
	// game/client/cliententitylist.cpp was read here too; client.so no longer
	// compiles it and it has left the source tree.
	const std::string port  = ReadStripped( kPortCpp );
	const std::string portH = ReadStripped( kPortH );
	const std::string hdr   = ReadStripped( kClientEntityHdr );

	ASSERT_FALSE( hdr.empty() ) << "could not read " << kClientEntityHdr
		<< " -- every absence below would pass vacuously";

	for ( size_t i = 0; i < kDeletedCount; i++ )
	{
		EXPECT_FALSE( Has( port, kDeletedSeamCalls[ i ] ) )
			<< kPortCpp << " names " << kDeletedSeamCalls[ i ] << " again. "
			<< "Phase 1c deleted it because it had no reader left -- a "
			   "comparison against a constant, or a console command that no "
			   "longer exists; if it is wanted again, say which caller reads "
			   "it.";
		EXPECT_FALSE( Has( portH, kDeletedSeamCalls[ i ] ) )
			<< kPortH << " declares " << kDeletedSeamCalls[ i ] << " again.";
		EXPECT_FALSE( Has( hdr, kDeletedSeamCalls[ i ] ) )
			<< kClientEntityHdr << " names " << kDeletedSeamCalls[ i ]
			<< " again. This is the FEEDER side: an increment here with no "
			   "definition anywhere is a link error, and an increment here "
			   "with a definition is the instrument back.";
	}
}


//=============================================================================
// HALF B, THE RULE: does the world answer for this index?
//=============================================================================

TEST( ClientWorldArm, ABoundWorldAnswersForItsOwnEntry )
{
	EXPECT_TRUE( SourceSharp_ClientWorldAnswersForIndex( H( 0, 7 ), 0 ) );
}

TEST( ClientWorldArm, ABoundWorldRefusesEveryOtherEntry )
{
	EXPECT_FALSE( SourceSharp_ClientWorldAnswersForIndex( H( 0, 7 ), 1 ) );
	EXPECT_FALSE( SourceSharp_ClientWorldAnswersForIndex( H( 0, 7 ), 2047 ) );
}

TEST( ClientWorldArm, AnUnboundWorldRefusesEntryZero )
{
	EXPECT_FALSE( SourceSharp_ClientWorldAnswersForIndex( kUnbound, 0 ) )
		<< "between maps there is no world, and answering for entry 0 would "
		   "hand the engine the previous level's object";
}

TEST( ClientWorldArm, AnUnboundWorldRefusesTheLastEntry )
{
	// THE GUARD THAT IS EASY TO LEAVE OUT. CBaseHandle::GetEntryIndex() answers
	// NUM_ENT_ENTRIES-1 for an INVALID handle, on purpose (basehandle.h:145) --
	// so `hWorld.GetEntryIndex() == nEntry` without the IsValid() test says
	// "that is the world" to every caller asking about the last entry while no
	// level is loaded.
	EXPECT_FALSE( SourceSharp_ClientWorldAnswersForIndex( kUnbound, NUM_ENT_ENTRIES - 1 ) );
}


//=============================================================================
// HALF B, THE RULE: does the world answer for this handle?
//=============================================================================

TEST( ClientWorldArm, ABoundWorldAnswersForItsOwnHandle )
{
	EXPECT_TRUE( SourceSharp_ClientWorldAnswersForHandle( H( 0, 7 ), H( 0, 7 ) ) );
}

TEST( ClientWorldArm, ABoundWorldRefusesItsOwnEntryAtAnotherSerial )
{
	// THE RELOAD BUG, as a fact. C_World is a singleton reused for every map and
	// the engine forces a fresh serial with each create call, so a handle taken
	// on the PREVIOUS map names entry 0 at a serial this map's world does not
	// have. An entry-only compare would resolve it to the new world and the
	// caller would never know it was holding a dead reference.
	EXPECT_FALSE( SourceSharp_ClientWorldAnswersForHandle( H( 0, 8 ), H( 0, 7 ) ) );
}

TEST( ClientWorldArm, ABoundWorldRefusesAnInvalidHandle )
{
	EXPECT_FALSE( SourceSharp_ClientWorldAnswersForHandle( H( 0, 7 ), kUnbound ) );
}

TEST( ClientWorldArm, AnUnboundWorldRefusesAnInvalidHandle )
{
	// NOTHING IS NOT THE WORLD, EVEN WHEN THERE IS NO WORLD -- world_subject.h's
	// own defect, arriving from the other direction. Both sides invalid is the
	// one case the IsValid() guard is needed for at all, and it is the case a
	// caller meets during level load.
	EXPECT_FALSE( SourceSharp_ClientWorldAnswersForHandle( kUnbound, kUnbound ) );
}

TEST( ClientWorldArm, ABoundWorldRefusesAnotherEntitysHandle )
{
	EXPECT_FALSE( SourceSharp_ClientWorldAnswersForHandle( H( 0, 7 ), H( 12, 7 ) ) );
}


//=============================================================================
// HALF B, THE RULE: the two counts IClientEntityList maintains
//=============================================================================

TEST( ClientWorldArm, AnUnboundWorldLeavesAnEmptyTablesHighestAtMinusOne )
{
	EXPECT_EQ( -1, SourceSharp_ClientHighestWithWorld( kUnbound, -1 ) );
}

TEST( ClientWorldArm, AnUnboundWorldDoesNotContributeTheLastEntryToTheHighest )
{
	// The same GetEntryIndex() trap as above, and here it is worse: this answer
	// is a LOOP BOUND the engine walks (1,184 calls, all from engine.so), so an
	// unbound world contributing 8191 sends it over eight thousand entries that
	// do not exist.
	EXPECT_EQ( 40, SourceSharp_ClientHighestWithWorld( kUnbound, 40 ) );
}

TEST( ClientWorldArm, ABoundWorldAtEntryZeroRaisesAnEmptyTablesHighestToZero )
{
	EXPECT_EQ( 0, SourceSharp_ClientHighestWithWorld( H( 0, 7 ), -1 ) );
}

TEST( ClientWorldArm, ABoundWorldAtEntryZeroDoesNotLowerAHigherTableAnswer )
{
	EXPECT_EQ( 40, SourceSharp_ClientHighestWithWorld( H( 0, 7 ), 40 ) );
}

TEST( ClientWorldArm, AWorldAboveTheTablesHighestRaisesIt )
{
	// Not reachable today -- worldspawn is entity 0 -- and asserted anyway,
	// because the rule is a maximum and a rule that only ever sees one of its
	// two branches is a rule nobody has checked.
	EXPECT_EQ( 41, SourceSharp_ClientHighestWithWorld( H( 41, 7 ), 40 ) );
}

TEST( ClientWorldArm, AnUnboundWorldAddsNothingToTheEntityCount )
{
	EXPECT_EQ( 12, SourceSharp_ClientCountWithWorld( kUnbound, 12 ) );
}

TEST( ClientWorldArm, ABoundWorldAddsExactlyOneToTheEntityCount )
{
	EXPECT_EQ( 13, SourceSharp_ClientCountWithWorld( H( 0, 7 ), 12 ) );
}

TEST( ClientWorldArm, ABoundWorldIsTheWholeCountWhenTheTableIsEmpty )
{
	EXPECT_EQ( 1, SourceSharp_ClientCountWithWorld( H( 0, 7 ), 0 ) );
}


//=============================================================================
// HALF B, THE WIRING: the arm is asked, and asked in the right ORDER
//=============================================================================

TEST( ClientWorldArm, TheWorldArmIsDefinedBesideTheObjectThatOwnsTheHandle )
{
	const std::string world = ReadStripped( kWorldCpp );

	EXPECT_TRUE( Has( world, "CBaseHandle SourceSharp_ClientWorldHandle( void )" ) );
	EXPECT_TRUE( Has( world, "IClientUnknown *SourceSharp_ClientWorldFromIndex( int nEntry )" ) );
	EXPECT_TRUE( Has( world, "IClientUnknown *SourceSharp_ClientWorldFromHandle( const CBaseHandle &hEnt )" ) );
}

TEST( ClientWorldArm, TheWorldArmDoesNotGoThroughTheAssertingFactoryAccessor )
{
	const std::string world = ReadStripped( kWorldCpp );

	const size_t handle = world.find( "CBaseHandle SourceSharp_ClientWorldHandle( void )" );
	ASSERT_NE( std::string::npos, handle );

	const size_t end = world.find( "\n}", handle );
	ASSERT_NE( std::string::npos, end );

	EXPECT_FALSE( Has( world.substr( handle, end - handle ), "GetClientWorldEntity()" ) )
		<< "GetClientWorldEntity() Asserts the singleton is non-NULL, and this "
		   "arm's correct answer when there is none is `no`, not a debug break";
}

TEST( ClientWorldArm, TheIndexResolverAsksTheTableThenTheWorldThenTheSeam )
{
	const std::string port = ReadStripped( kPortCpp );

	const size_t fn = port.find( "static IClientUnknown *ClientUnknownFromIndex( int nEntry )" );
	ASSERT_NE( std::string::npos, fn );

	const size_t table = port.find( "SourceSharp_ClientShellFromIndex(", fn );
	const size_t world = port.find( "SourceSharp_ClientWorldFromIndex(", fn );
	const size_t seam  = port.find( "SourceSharp_LegacyListUnknownFromIndex(", fn );

	ASSERT_NE( std::string::npos, table );
	ASSERT_NE( std::string::npos, world );
	ASSERT_NE( std::string::npos, seam );

	EXPECT_LT( table, world ) << "the port's own answer comes first";
	EXPECT_LT( world, seam )
		<< "the world must be asked BEFORE what is left of CClientEntityList, "
		   "or the seam keeps answering for entity 0 and the arm is dead code";
}

TEST( ClientWorldArm, TheHandleResolverAsksTheTableThenTheWorldThenTheSeam )
{
	const std::string port = ReadStripped( kPortCpp );

	const size_t fn = port.find( "static IClientUnknown *ClientUnknownFromHandle( const CBaseHandle &hEnt )" );
	ASSERT_NE( std::string::npos, fn );

	const size_t table = port.find( "SourceSharp_ClientShellFromHandle(", fn );
	const size_t world = port.find( "SourceSharp_ClientWorldFromHandle(", fn );
	const size_t seam  = port.find( "SourceSharp_LegacyListUnknownFromHandle(", fn );

	ASSERT_NE( std::string::npos, table );
	ASSERT_NE( std::string::npos, world );
	ASSERT_NE( std::string::npos, seam );

	EXPECT_LT( table, world );
	EXPECT_LT( world, seam );
}

TEST( ClientWorldArm, TheIndexToHandleInverseAsksTheWorldToo )
{
	const std::string port = ReadStripped( kPortCpp );

	const size_t fn = port.find( "CBaseHandle SourceSharp_ClientHandleFromIndex( int nEntry )" );
	ASSERT_NE( std::string::npos, fn );

	const size_t world = port.find( "SourceSharp_ClientWorldAnswersForIndex(", fn );
	const size_t seam  = port.find( "SourceSharp_LegacyListUnknownFromIndex(", fn );

	ASSERT_NE( std::string::npos, world );
	ASSERT_NE( std::string::npos, seam );
	EXPECT_LT( world, seam );
}

TEST( ClientWorldArm, BothCountsAreTakenThroughTheWorldRuleAndNotOpenCoded )
{
	const std::string port = ReadStripped( kPortCpp );

	EXPECT_TRUE( Has( port, "SourceSharp_ClientCountWithWorld(" ) )
		<< "NumberOfEntities must add the world through the rule this file "
		   "pins, or the count and the rule can part company";
	EXPECT_TRUE( Has( port, "SourceSharp_ClientHighestWithWorld(" ) )
		<< "GetHighestEntityIndex must raise for the world through the rule "
		   "this file pins";
}

//=============================================================================
// THE INDEX OF WHAT A TRACE HIT (C6, gametrace_client.cpp's eviction).
//
// The world query's trace slot answers the hit entity's index. It was
// CGameTrace::GetEntityIndex -- the hit's networkable's entindex(), or -1 --
// and is now read off the hit's own handle: the entry index when this side's
// table resolves the handle to a NETWORKED entry, else -1. Networked entries
// are below MAX_EDICTS, which is where entindex() equals the entry; a
// client-only entity (upper half) answers -1, as its entindex() did.
//=============================================================================

TEST( ClientWorldArm, ATraceThatHitNothingHasNoIndex )
{
	EXPECT_EQ( -1, SourceSharp_ClientIndexOfHit( kUnbound, false ) );
	// The guard that matters: an invalid handle's entry reads NUM_ENT_ENTRIES-1.
	EXPECT_EQ( -1, SourceSharp_ClientIndexOfHit( kUnbound, true ) );
}

TEST( ClientWorldArm, ANetworkedHitAnswersItsEntry )
{
	EXPECT_EQ( 0, SourceSharp_ClientIndexOfHit( H( 0, 3 ), true ) );      // the world
	EXPECT_EQ( 42, SourceSharp_ClientIndexOfHit( H( 42, 9 ), true ) );
	EXPECT_EQ( MAX_EDICTS - 1, SourceSharp_ClientIndexOfHit( H( MAX_EDICTS - 1, 1 ), true ) );
}

TEST( ClientWorldArm, AHandleTheTableDoesNotResolveHasNoIndex )
{
	// A stale handle (the entry's occupant changed) or a static prop's.
	EXPECT_EQ( -1, SourceSharp_ClientIndexOfHit( H( 42, 9 ), false ) );
}

TEST( ClientWorldArm, AClientOnlyEntityHasNoIndex )
{
	EXPECT_EQ( -1, SourceSharp_ClientIndexOfHit( H( MAX_EDICTS, 1 ), true ) );
	EXPECT_EQ( -1, SourceSharp_ClientIndexOfHit( H( NUM_ENT_ENTRIES - 2, 1 ), true ) );
}

//=============================================================================
// AND NO CLIENT CALLER ASKS A CGameTrace MEMBER: gametrace_client.cpp, which
// defined them, left client.so. The world compare is the shared rule
// (SourceSharp_IsTheWorld, world_adapter_client.cpp), the index the rule above.
//=============================================================================

TEST( ClientWorldArm, NoClientCallerAsksATraceMember )
{
	const char *const kCallers[] = {
		"sourcesharp/game/client/shell_impact_effects.cpp",
		"sourcesharp/game/client/shell_worldquery_client.cpp",
		"sourcesharp/game/shared/shell_movement.cpp",
	};
	for ( const char *pszFile : kCallers )
	{
		const std::string text = ReadStripped( pszFile );
		ASSERT_FALSE( text.empty() ) << pszFile << " did not load";
		EXPECT_EQ( std::string::npos, text.find( ".DidHitWorld()" ) ) << pszFile;
		EXPECT_EQ( std::string::npos, text.find( "->DidHitWorld()" ) ) << pszFile;
		EXPECT_EQ( std::string::npos, text.find( ".GetEntityIndex()" ) ) << pszFile;
		EXPECT_EQ( std::string::npos, text.find( ".DidHitNonWorldEntity()" ) ) << pszFile;
	}
}
