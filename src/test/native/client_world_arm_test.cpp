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
// The stock header SDKEntityFromUnknown is declared in. It is the FEEDER side
// of the ask counters, and it is a different file from either list, so a
// deletion that left the increments behind would break the link and not this
// test unless it is read here too.
const std::string kClientEntityHdr = "game/client/c_baseentity.h";

const std::string kStockH = "game/client/cliententitylist.h";
const std::string kPortCpp = "sourcesharp/game/client/shell_cliententitylist.cpp";
const std::string kPortH = "sourcesharp/game/client/shell_cliententitylist.h";
const std::string kWorldCpp = "sourcesharp/game/client/world_adapter_client.cpp";

} // namespace

//=============================================================================
// THE STRIPPER, first, because three facts below rest on it
//=============================================================================

//=============================================================================
// HALF A: the port's eleven definitions are in the port's own list file
//=============================================================================

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
	EXPECT_EQ( 0, SourceSharp_ClientIndexOfHit( H( 0, 3 ), true ) ); // the world
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
