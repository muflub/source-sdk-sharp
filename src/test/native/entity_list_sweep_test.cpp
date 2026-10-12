//========= SourceSharp — plan_entity.md step 1d, as facts ===================//
//
// THE SHIM'S ENTITY-LIST SURFACE, AND THE TWO RESOLVERS THAT REPLACED PART OF
// IT. Step 1d's own gate is a census reading: "zero gEntList / g_pEntityList in
// these files, and the cast census falls by the sites named". A census is run
// by hand and compared against a committed baseline; what it cannot say is WHY
// a number is what it is, so a site added back into a file that already has one
// moves the total by one and reads exactly like a site legitimately still
// there. This file is the why.
//
// SOURCE-READ, and it is forced rather than lazy. Every subject here includes
// cbase.h's whole world -- shell_entityapi.cpp, shell_soundenvelope.cpp,
// world_adapter.cpp and toucher.h cannot be linked into a gtest, and neither
// can the shell they resolve to. The behavioural half of this step is the live
// gate; what is here is what a compiler cannot check and a reader cannot check
// by eye.
//
// TWO KINDS OF FACT, and the difference decides whether comments are stripped:
//
//   THE LEDGER (LastFileAtZero..., TheRemainingSitesAre...) reads RAW TEXT,
//   comments included, because tools/entity-census greps raw text and a fact
//   that disagreed with the instrument it is standing in for would be worse
//   than no fact at all. That is also why the sweep's own comments never spell
//   `gEntList.` or `g_pEntityList->`: a paragraph explaining a deleted call
//   would be counted as the call.
//
//   EVERY OTHER FACT strips comments first. All four subjects describe the
//   operations they perform at length, so an unstripped search for
//   "SourceSharp_ShellFromIndex" finds the paragraph explaining it and passes
//   over a body that no longer calls it -- the shape this tree lists as its
//   fifth check-that-cannot-fail.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <algorithm>
#include <fstream>
#include <dirent.h>
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

bool Exists( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;

	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), &st ) == 0;
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

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

std::string StripComments( const std::string &src )
{
	std::string out;
	out.reserve( src.size() );

	for ( size_t i = 0; i < src.size(); )
	{
		if ( src[ i ] == '/' && i + 1 < src.size() && src[ i + 1 ] == '/' )
		{
			while ( i < src.size() && src[ i ] != '\n' ) i++;
			continue;
		}
		if ( src[ i ] == '/' && i + 1 < src.size() && src[ i + 1 ] == '*' )
		{
			i += 2;
			while ( i + 1 < src.size() && !( src[ i ] == '*' && src[ i + 1 ] == '/' ) ) i++;
			i = ( i + 1 < src.size() ) ? i + 2 : src.size();
			continue;
		}
		out += src[ i++ ];
	}

	return out;
}

std::string Code( const char *pszRel ) { return StripComments( Read( pszRel ) ); }

bool Has( const std::string &hay, const char *pszNeedle )
{
	return hay.find( pszNeedle ) != std::string::npos;
}

// How many LINES carry a needle -- the same unit tools/entity-census counts in,
// because it runs `grep -cE`. Counting occurrences instead would disagree with
// the instrument on a line that names the list twice, which is exactly the
// shape of a FirstEnt/NextEnt loop.
int LinesWith( const std::string &src, const char *pszNeedle )
{
	int nLines = 0;
	size_t at = 0;

	while ( at < src.size() )
	{
		const size_t eol = src.find( '\n', at );
		const std::string line = src.substr( at, ( eol == std::string::npos ) ? std::string::npos : eol - at );

		if ( line.find( pszNeedle ) != std::string::npos )
			nLines++;

		if ( eol == std::string::npos )
			break;

		at = eol + 1;
	}

	return nLines;
}

// A line naming the SDK entity list in either of its two spellings. The census
// pattern is `gEntList\.|g_pEntityList->` and this is that, in the same unit.
int EntityListLines( const char *pszRel )
{
	const std::string src = Read( pszRel );
	int nLines = 0;
	size_t at = 0;

	while ( at < src.size() )
	{
		const size_t eol = src.find( '\n', at );
		const std::string line = src.substr( at, ( eol == std::string::npos ) ? std::string::npos : eol - at );

		if ( line.find( "gEntList." ) != std::string::npos
			|| line.find( "g_pEntityList->" ) != std::string::npos )
		{
			nLines++;
		}

		if ( eol == std::string::npos )
			break;

		at = eol + 1;
	}

	return nLines;
}

// The game-boundary refusal's BODY, brace-matched out of the header it now lives
// in.
//
// WITHOUT THIS the bans below would be read against the whole of networkvar.h,
// which is 900 lines of prelude with its own loops and its own casts -- every
// EXPECT_FALSE would fail on somebody else's code and every EXPECT_TRUE would
// pass on it. The extraction is the check: an empty result means the signature
// moved, and each test asserts it is non-empty before banning anything in it.
std::string RefusalBody()
{
	const std::string src = Code( "public/networkvar.h" );
	const char *const pszSig =
		"inline CBaseHandle SourceSharp_EntityHandle( const IGameEntity *pEntity )";

	const size_t at = src.find( pszSig );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = src.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	int nDepth = 0;
	for ( size_t i = open; i < src.size(); i++ )
	{
		if ( src[ i ] == '{' ) nDepth++;
		else if ( src[ i ] == '}' && --nDepth == 0 )
			return src.substr( at, i - at + 1 );
	}

	return std::string();
}

const char *const kEntityApi     = "sourcesharp/game/server/shell_entityapi.cpp";
const char *const kToucher       = "sourcesharp/game/server/toucher.h";
const char *const kSoundEnvelope = "sourcesharp/game/server/shell_soundenvelope.cpp";
const char *const kStandalone    = "sourcesharp/game/server/shell_standalone.cpp";
const char *const kWorldAdapter  = "sourcesharp/game/server/world_adapter.cpp";
// cs_sv_trace_subject is a diagnostic and is compiled only into a debug build.
const char *const kWorldAdapterDebug = "sourcesharp/game/server/world_adapter_debug.cpp";
// WHERE THE REFUSAL LIVES NOW. It was sourcesharp/game/shared/gameentity_handle
// .cpp, one translation unit in both DLLs holding one function; plan_entity.md's
// Phase 1 exit lists `gameentity_handle.*` among the things that are gone, and
// it is: the body is inline in the header that declares the overload set, and
// ehandle.h includes that header rather than re-declaring the arm.
const char *const kReverseRoute  = "public/networkvar.h";
const char *const kDeletedRoute  = "sourcesharp/game/shared/gameentity_handle.cpp";
const char *const kResolveHeader = "sourcesharp/game/server/shell_resolve.h";

} // namespace

//=============================================================================
// 1. THE LEDGER.
//
// Step 1d owns thirteen files. SIX of them are at zero and MUST STAY at zero;
// the other five carry eleven calls between them, every one of which is either
// the SDK entity list's own LIFECYCLE (create, delete-list, clear) or a walk of
// the SDK HIERARCHY, and both of those leave with entitylist.cpp in Phase 4
// rather than with this step.
//
// PINNED IN BOTH DIRECTIONS, which is the whole value of the pair. An exact
// count goes red when a lane puts a call back into a file this step emptied
// AND when a call listed below is removed without the ledger moving with it --
// the second of which is the one a ratchet would miss, because a number that
// only ever falls cannot report that the reason for it changed.
//=============================================================================

TEST( EntityListSweep, TheSixEmptiedSubjectsNameTheEntityListNowhere )
{
	// The six the sweep took to zero. Each is named rather than counted so a
	// failure says which one grew a call back. FIVE FILES AND ONE FUNCTION: the
	// sixth subject was gameentity_handle.cpp, whose one function is now inline
	// in public/networkvar.h, so it is read as a body rather than as a file --
	// the surrounding prelude has entity-list lines of its own and reading the
	// whole header would fail on somebody else's code.
	EXPECT_EQ( 0, EntityListLines( kEntityApi ) )
		<< "shell_entityapi.cpp's two finders walk the shim's own list";
	EXPECT_EQ( 0, EntityListLines( kToucher ) )
		<< "toucher.h resolves through CBaseHandle::Get() and the slot table";
	EXPECT_EQ( 0, EntityListLines( kSoundEnvelope ) )
		<< "shell_soundenvelope.cpp resolves through the slot table";
	EXPECT_EQ( 0, EntityListLines( kStandalone ) )
		<< "shell_standalone.cpp's parent command asks only the standalone list";
	EXPECT_EQ( 0, LinesWith( RefusalBody(), "gEntList." ) )
		<< "the game-boundary refusal's reverse scan is deleted";
	EXPECT_EQ( 0, LinesWith( RefusalBody(), "g_pEntityList->" ) )
		<< "the game-boundary refusal's reverse scan is deleted";
	EXPECT_EQ( 0, EntityListLines( kResolveHeader ) )
		<< "shell_resolve.h declares resolvers and reaches no list";
}

//-----------------------------------------------------------------------------
// THE LEDGER WAS COUNTING DEAD SOURCE, AND THIS IS WHERE THAT SHOWS.
//
// READ THE ASSERTIONS, NOT THE NAME -- the name is kept because the fact's JOB
// is unchanged (pin each file's count in both directions), but seven of the
// eleven "remaining sites" it pinned were inside `#ifndef SS_CBASEENTITY_ALIAS`
// and were never compiled. SS_CBASEENTITY_ALIAS is defined in every shipped
// build, so `FindEntityInSphere`, the FirstEnt/NextEnt mdlcache walk, all four
// of shell_level.cpp's lifecycle calls and two of
// shell_restoreentities.cpp's three were text. f288a5e11 deleted them -- "the
// shim's 240 dead guarded arms, and nothing else" -- and NOTHING THE RUNNING
// GAME DID CHANGED, which is the proof they were never doing it.
//
// The ledger's argument moves with them. It used to read "these leave in Phase
// 4 with game/server/entitylist.cpp, and deleting one now deletes a
// capability". That argument was about compiled calls; it never applied to
// these. What is left, below, is FIVE LINES IN FIVE FILES, and every one of
// them is a COMMENT -- which is why this fact is now paired with
// NoShimFileMakesAnEntityListCallAtAll, the fact that reads the same files with
// the comments stripped. A count of raw lines alone can no longer tell prose
// from a call, so it may no longer be asserted alone.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheRemainingSitesAreLifecycleOrStockWalksAndAreCounted )
{
	// Each count is a claim about WHICH lines are left, not just how many. The
	// message names them, so a diff that moves one of these numbers has to say
	// which line it added or removed.
	EXPECT_EQ( 0, EntityListLines( "sourcesharp/game/server/shell_worldquery.cpp" ) )
		<< "FindEntityInSphere's SDK half was the one line here and it was inside "
		   "`#ifndef SS_CBASEENTITY_ALIAS` -- never compiled, so the sphere query "
		   "armed has always been the shim's own walk alone. Nothing may put a "
		   "live one back: gate 2 counts it.";

	EXPECT_EQ( 0, EntityListLines( "sourcesharp/game/server/shell_engineservices.cpp" ) )
		<< "the FirstEnt/NextEnt walk that was said to free each "
		   "CBaseAnimating's m_pStudioHdr. It was guarded out, so it freed "
		   "nothing in any shipped build -- the same finding as the client's "
		   "twin in ClientMdlCacheSources.";

	EXPECT_EQ( 0, EntityListLines( "sourcesharp/game/server/shell_level.cpp" ) )
		<< "all four -- CleanupDeleteList, ResetDeleteList, the Activate walk and "
		   "Clear -- were in the same dead arm. The LIVE lifecycle is the slot "
		   "table's, and SourceSharp_SlotClearAll is what the level steps call.";

	EXPECT_EQ( 1, EntityListLines( "sourcesharp/game/server/shell_restoreentities.cpp" ) )
		<< "one, and it is PROSE: the line explaining that the stock restore walk "
		   "yields 0 of the 473 entities on a loaded map. The two CleanupDeleteList "
		   "calls that stood beside it were guarded out and are gone.";

	EXPECT_EQ( 0, EntityListLines( "sourcesharp/game/server/baseentity_statics.cpp" ) )
		<< "ZERO since P5 lane E. The one line was prose: the NotifyCreateEntity "
		   "that stood in EntityCreate_NoSpawn, which fired the list's "
		   "IEntityListener callbacks for a STOCK entity. The function went as "
		   "dead code (no caller; server.so held none of it), and the paragraph "
		   "about a line inside it went with it.";

	EXPECT_EQ( 1, EntityListLines( kWorldAdapter ) )
		<< "one, and it is a COMMENT about the level-shutdown order rather than "
		   "a call. The world's registration and its removal were the other two "
		   "and they are gone -- plan_entity.md §3.3, lane entlist-server: the "
		   "world issues its own handle in AttachToEdictZero and "
		   "SourceSharp_WorldFromHandle resolves it, because the slot table "
		   "binds SHELLS and one type behind its void * is what makes its single "
		   "static_cast sound. The release the purge used to perform moved with "
		   "it -- see WorldRelease.TheLevelClearStepReleasesTheWorldBeforeIt"
		   "ClearsTheList, which is what stops the crash that shipped once.";

	EXPECT_EQ( 0, EntityListLines( "sourcesharp/game/server/shell_standalone_base.cpp" ) )
		<< "the KEYSTONE file, and lane p4-flip's whole finding: two of the "
		   "three registrations that made the entity list 'not the SDK's list "
		   "any more but the port's own handle table' were here, plus the "
		   "removal and two resolves. Every managed shell in this mod went "
		   "through them. The table of plan_entity.md §3.3 is what they go "
		   "through now -- SourceSharp_SlotIssue on the way in, "
		   "SourceSharp_EntityFromHandle on the way back.";

	EXPECT_EQ( 0, EntityListLines( "sourcesharp/game/server/shell_standalone_player.cpp" ) )
		<< "the player's ground entity resolved through the list because the "
		   "ground can be on either hierarchy. SourceSharp_EntityFromHandle is "
		   "that union now, and its WORLD arm is the one that matters here: a "
		   "player standing on the map is standing on worldspawn.";

	EXPECT_EQ( 0, EntityListLines( "sourcesharp/game/shared/takedamageinfo_library.cpp" ) )
		<< "the inflictor's classname and the multi-damage target, both resolved "
		   "through the union rather than through the list. This file is "
		   "compiled into BOTH DLLs, so these two counted on both sides.";

	EXPECT_EQ( 0, EntityListLines( "sourcesharp/game/server/shell_entityslots.cpp" ) )
		<< "ZERO, AND THE SHIM HAS NO RESOLUTION PATH THAT REACHES THE SDK'S LIST "
		   "AT ALL. The third arm of SourceSharp_EntityFromHandle was the last "
		   "one; armed, no compiled server TU registers an entity of that "
		   "hierarchy, so the arm asked a list with no population and NULL is the "
		   "true answer rather than a stub's. What the two live arms are, and "
		   "that NULL is returned rather than fallen through to, is "
		   "TheUnionResolveAsksTheTableThenTheWorldAndHasNothingElseToAsk.";
}

//-----------------------------------------------------------------------------
// AND NOT ONE OF THE FIVE SURVIVING LINES IS A CALL.
//
// The counts above are raw-text lines, in the unit tools/entity-census uses.
// That unit could always be satisfied by a comment, and for most of the port
// that did not matter because the live calls dominated. It matters now: the
// shim is down to five lines and all five are prose, so a raw-line ledger on
// its own can no longer distinguish "the shim stopped calling the list" from
// "somebody wrote a paragraph about the list". Stripping the comments and
// requiring zero is the half that makes the counts above mean something.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, NoShimFileMakesAnEntityListCallAtAll )
{
	static const char *const kLedgered[] = {
		"sourcesharp/game/server/shell_worldquery.cpp",
		"sourcesharp/game/server/shell_engineservices.cpp",
		"sourcesharp/game/server/shell_level.cpp",
		"sourcesharp/game/server/shell_restoreentities.cpp",
		"sourcesharp/game/server/baseentity_statics.cpp",
		"sourcesharp/game/server/world_adapter.cpp",
		"sourcesharp/game/server/shell_entityslots.cpp",
		"sourcesharp/game/server/shell_standalone_hierarchy.cpp",
		"sourcesharp/game/server/baseentity_statics.h",
	};

	for ( size_t i = 0; i < sizeof( kLedgered ) / sizeof( kLedgered[ 0 ] ); i++ )
	{
		const std::string src = Code( kLedgered[ i ] );
		ASSERT_FALSE( src.empty() ) << kLedgered[ i ] << " read back empty; the "
			"assertions below would hold over a file that is not there.";

		EXPECT_EQ( 0, LinesWith( src, "gEntList." ) )
			<< kLedgered[ i ] << " makes a gEntList call with the comments stripped.";
		EXPECT_EQ( 0, LinesWith( src, "g_pEntityList->" ) )
			<< kLedgered[ i ] << " makes a g_pEntityList call with the comments stripped.";
	}
}

//-----------------------------------------------------------------------------
// THE SHIM'S WHOLE REMAINING SURFACE, AS ONE NUMBER.
//
// The per-file facts above say WHICH calls are left; this says HOW MANY, over
// every shim file at once, so a call added to a file that has no fact of its
// own cannot arrive unnoticed. That is the hole a per-file ledger has: it can
// only refuse growth in the files somebody thought to name.
//
// IT WAS A RATCHET; IT IS A PAIR NOW. While the shim still made live calls,
// "the number only falls" was the whole fact, guarded by COMMON.md's "a compile
// gate selects for deletion" -- twelve of the sixteen were SDK-hierarchy
// operations with live subjects and deleting one deleted a capability.
//
// That reading did not survive f288a5e11. Eleven of the sixteen were inside
// `#ifndef SS_CBASEENTITY_ALIAS` and had no subjects at all, live or otherwise,
// because the compiler never saw them; removing them deleted no capability and
// the number fell to five. So the fact is now TWO numbers: the CODE count,
// which is zero and is the clause-2 property, and the RAW-TEXT count, which is
// five and is all prose. Asserting either alone is the hole -- raw text alone
// cannot see a call arriving in a file with a paragraph already in it, and code
// alone cannot see the last comment explaining why a thing is not done being
// deleted along with the code it explains.
//-----------------------------------------------------------------------------
//=============================================================================
// WHAT REPLACED THEM -- because "zero entity-list lines" cannot tell a call
// that MOVED from a call that was DELETED.
//
// The ledger above is a refusal and nothing more: a shell that stopped
// registering ANYWHERE would satisfy every count in it, and would also make
// every handle to every managed entity in the map resolve to NULL. So each
// emptied site has a positive fact here naming what stands in its place, and
// each of those is read out of the FUNCTION's own body rather than grepped for
// over the file.
//=============================================================================

//-----------------------------------------------------------------------------
// The body of a function, from its signature to the line that closes it at
// column zero. Same shape world_release_test.cpp uses, and for the same reason:
// "the file contains this text" passes for a paragraph explaining the call.
//-----------------------------------------------------------------------------
std::string BodyOf( const std::string &src, const char *pszSignature )
{
	const size_t at = src.find( pszSignature );

	if ( at == std::string::npos )
		return std::string();

	const size_t open = src.find( '{', at );

	if ( open == std::string::npos )
		return std::string();

	const size_t close = src.find( "\n}", open );

	if ( close == std::string::npos )
		return src.substr( open );

	return src.substr( open, close - open );
}

//-----------------------------------------------------------------------------
// THE KEYSTONE LINE. plan_entity.md §3.3, and lane p4-flip's whole finding:
// every managed shell in this mod registered in the entity list and every
// handle to one resolved through the result.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheShellRegistersItselfInTheSlotTableAtItsEdictIndex )
{
	const std::string body = BodyOf(
		Code( "sourcesharp/game/server/shell_standalone_base.cpp" ),
		"void CStandaloneEntity::PostConstruct( const StandaloneClassDesc_t *pClass )" );

	ASSERT_FALSE( body.empty() )
		<< "CStandaloneEntity::PostConstruct has no body -- the registration "
		   "moved to a function this fact cannot see, which is not the same "
		   "thing as the registration being right.";

	// THREE LINES AND ALL THREE ARE ASSERTED: the entry is the edict index the
	// engine just handed out, the handle comes from the slot table, and
	// SetRefEHandle is what binds it. Matching only the last would pass for a
	// build that stored a handle nothing issued.
	EXPECT_TRUE( Has( body, "const int nEntry = engine->IndexOfEdict( m_pEdict );" ) )
		<< "the entry is not the edict index. Body was: " << body;
	EXPECT_TRUE( Has( body, "const CBaseHandle hIssued = SourceSharp_SlotIssue( nEntry );" ) )
		<< "the handle does not come from the slot table. Without it NOTHING "
		   "binds the entity, every EHANDLE to it resolves to NULL, and the "
		   "ledger above is green about it. Body was: " << body;
	EXPECT_TRUE( Has( body, "SetRefEHandle( hIssued );" ) )
		<< "the issued handle is never stored, so it is never bound. Body was: "
		<< body;

	// AND A REFUSED ISSUE IS PRINTED. An entity with an invalid handle is
	// constructed, spawned and standing in the map while nothing resolves it;
	// CGlobalEntityList only Assert()ed the same state, and an assert compiles
	// out of a shipping build.
	EXPECT_TRUE( Has( body, "if ( !hIssued.IsValid() )" ) )
		<< "a refused issue is silent. Body was: " << body;
}

//-----------------------------------------------------------------------------
// The world's, which is not the slot table's -- world_subject.h has why.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheWorldIssuesItsOwnHandleAtItsOwnEdictIndex )
{
	const std::string body = BodyOf(
		Code( "sourcesharp/game/server/world_adapter.cpp" ),
		"void CWorld::AttachToEdictZero()" );

	ASSERT_FALSE( body.empty() ) << "CWorld::AttachToEdictZero has no body";

	EXPECT_TRUE( Has( body, "SetRefEHandle( CBaseHandle( RequiredEdictIndex(), SourceSharp_WorldIssueSerial() ) )" ) )
		<< "the world takes no handle. Nothing else gives it one -- it is in "
		   "neither the slot table nor the list -- so every trace that hits "
		   "nothing hits an entity that cannot be named. Body was: " << body;

	// AND NOT THE SLOT TABLE. The table binds shells; a CWorld behind the same
	// void * would make SourceSharp_ShellFromHandle's static_cast -- the one
	// downcast plan_entity.md permits, allowlisted by file and line -- answer
	// about whichever object was there.
	EXPECT_FALSE( Has( body, "SourceSharp_SlotBind" ) )
		<< "the world is bound in the slot table. The table's single allowlisted "
		   "static_cast is sound only because ONE type goes in.";
}

//-----------------------------------------------------------------------------
// THE UNION, AND THE ORDER OF ITS ARMS -- OF WHICH THERE ARE NOW TWO.
//
// The five sites that spelled the entity list's LookupEntity did not want the
// SDK's list; they wanted the union of both hierarchies, and the list was the
// only thing that was one. The order is asserted because the arms have to be
// asked cheapest-first.
//
// THE THIRD ARM IS GONE AND THAT IS NOT A TRIM. It used to be asserted PRESENT
// here, on the argument that info_player_deathmatch, prop_ragdoll and the
// physics solvers are still LINK_ENTITY_TO_CLASS'd in compiled stock TUs and
// dropping it would make them unresolvable. What the close established is that
// armed, NO compiled server TU registers an entity of that hierarchy -- the
// level steps measured the residue -- so the arm asked a list with no
// population. NULL is therefore the TRUE answer and not a stub's: the two live
// arms are the whole population and are disjoint from the removed one by
// construction, so it can shadow neither.
//
// WHICH IS WHY THE ABSENCE IS ASSERTED WITH THE RETURN. "No LookupEntity here"
// alone is half a fact -- it holds over a function that fell off the end
// without returning anything, which on this path is a resolver handing back an
// uninitialised pointer for every handle in the map.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheUnionResolveAsksTheTableThenTheWorldAndHasNothingElseToAsk )
{
	const std::string body = BodyOf(
		Code( "sourcesharp/game/server/shell_entityslots.cpp" ),
		"IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle )" );

	ASSERT_FALSE( body.empty() ) << "SourceSharp_EntityFromHandle has no body";

	const size_t nShell = body.find( "SourceSharp_ShellFromHandle( handle )" );
	const size_t nWorld = body.find( "SourceSharp_WorldFromHandle( handle )" );

	ASSERT_NE( std::string::npos, nShell ) << "no slot-table arm. Body was: " << body;
	ASSERT_NE( std::string::npos, nWorld ) << "no world arm -- worldspawn is in "
		"neither of the other two, so it would resolve as gone. Body was: " << body;

	EXPECT_LT( nShell, nWorld ) << "the world is asked before the slot table";

	// THE INVALID HANDLE IS REFUSED FIRST. Every unbound entity carries it, and
	// so does the world between AttachToEdictZero and ~CWorld's Term(); without
	// the guard the world arm's packed compare would match one against another.
	EXPECT_LT( body.find( "!handle.IsValid()" ), nShell )
		<< "the invalid handle reaches the arms. Body was: " << body;

	// THE ABSENCE, AND THE ANSWER THAT REPLACED IT.
	EXPECT_EQ( std::string::npos, body.find( "LookupEntity( handle )" ) )
		<< "the SDK arm is back. It asks a list with no population armed, and "
		   "gate 2 counts naming it. Body was: " << body;
	EXPECT_LT( nWorld, body.rfind( "return NULL;" ) )
		<< "nothing answers after the world arm. A resolver that runs off the end "
		   "hands back an indeterminate pointer for every handle neither arm "
		   "claims, which is every static prop the game traces against. Body was: "
		<< body;
}

//-----------------------------------------------------------------------------
// The server-only arm REFUSES rather than silently binding nothing (§7).
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheServerOnlyShellRefusalIsPrintedRatherThanSilent )
{
	const std::string body = BodyOf(
		Code( "sourcesharp/game/server/shell_standalone_base.cpp" ),
		"void CStandaloneEntity::PostConstruct( const StandaloneClassDesc_t *pClass )" );

	ASSERT_FALSE( body.empty() ) << "CStandaloneEntity::PostConstruct has no body";

	const size_t nArm = body.find( "pClass->bServerOnly" );
	ASSERT_NE( std::string::npos, nArm )
		<< "the server-only arm is gone entirely. It is UNREACHABLE, not absent: "
		   "step 2e made every server-only class shell-less, and the arm is what "
		   "reports a regression of that rule.";

	// THE ARM'S OWN BLOCK, NOT THE REST OF THE FUNCTION. `find( "Warning(",
	// nArm )` was the first draft of this and it CANNOT FAIL: PostConstruct
	// carries another Warning further down -- the one for an edict it could not
	// get -- so replacing this arm's Warning with a DevMsg left the assertion
	// green, which was mutation-proved and is why the slice is bounded here.
	const size_t nEnd = body.find( "\n\t}", nArm );
	ASSERT_NE( std::string::npos, nEnd ) << "the server-only arm has no closing brace";

	const std::string arm = body.substr( nArm, nEnd - nArm );

	EXPECT_TRUE( Has( arm, "Warning(" ) )
		<< "the server-only arm is a silent no-op. plan_entity.md §7: a state the "
		   "port does not implement is a DECLARED, PRINTED refusal -- otherwise an "
		   "entity that could not be bound looks exactly like one that was. The "
		   "arm was: " << arm;

	EXPECT_FALSE( Has( arm, "SourceSharp_SlotIssue" ) )
		<< "the server-only arm binds a slot. It has no entry to bind at: the "
		   "engine-invisible half of the range belongs to the managed "
		   "EntityDirectory, whose AllocateInvisible hands entries out DOWNWARD "
		   "from 8191 so as not to meet an upward allocator. Arm was: " << arm;
}

//-----------------------------------------------------------------------------
// THE UNIVERSAL RESOLVE, AND THE FAILURE THAT FOUND IT.
//
// CBaseHandle::Get() is what EHANDLE, CHandle<T> and every implicit conversion
// off a handle come through, in both DLLs. It read the entity list, so the day
// the shells left the list every one of those answered NULL AT ONCE -- and
// nothing said so.
//
// MEASURED, NOT PREDICTED. The registrations moved first and this did not, and
// the live gate answered in one run: move, jump, push, second-client and lift
// all failed with the player displaced EXACTLY 0.0 units, on a box where the
// untouched tree passed all twenty steps minutes later. One line carries it --
// `UTIL_TraceRay( ray, mask, mv->m_nPlayerHandle.Get(), ... )` -- where the
// trace's PASS entity is a handle resolved here. NULL there makes the filter
// pass everything, the player's own ground trace hits the player's own bounding
// box, and a movement loop that cannot find the floor cannot walk.
//
// So this fact is not decoration on a working build; it is the one that was red
// while every other check in this file was green.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheHandlesOwnGetResolvesThroughTheUnion )
{
	const std::string body = BodyOf( Code( "game/shared/ehandle.h" ),
	                                 "inline IHandleEntity* CBaseHandle::Get() const" );

	ASSERT_FALSE( body.empty() ) << "CBaseHandle::Get has no body in game/shared/ehandle.h";

	EXPECT_TRUE( Has( body, "SourceSharp_EntityFromHandle( *this )" ) )
		<< "CBaseHandle::Get does not ask the union. Every EHANDLE in the DLL "
		   "then answers NULL for every managed shell -- there are 34 of them in "
		   "the server's shim alone -- and the first thing that breaks is player "
		   "movement, silently. Body was: " << body;
}

//-----------------------------------------------------------------------------
// AND IT IS NO LONGER GUARDED TO THE SERVER, which is lane `f-list`'s edit and
// the one this fact exists to hold down.
//
// It used to be, and the reason was true when it was written: the client's
// shells were still in cl_entitylist, so the stock line WAS the whole client
// answer. Once SourceSharp_ClientEntityBind binds the client slot table and
// C_World holds entry 0 on its own handle, that stops being true -- and the
// failure mode is the server's, on the other side: every client EHANDLE
// answering NULL for every managed entity at once, with nothing logged.
//
// MUTATION-VERIFIED as a CHANGE and not only as a deletion: putting
// `&& !defined( CLIENT_DLL )` back is what a merge or a revert would do, and
// that is the mutation this fact was watched to fail against.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheHandlesOwnGetArmIsNotGuardedToTheServer )
{
	const std::string body = BodyOf( Code( "game/shared/ehandle.h" ),
	                                 "inline IHandleEntity* CBaseHandle::Get() const" );

	ASSERT_FALSE( body.empty() ) << "CBaseHandle::Get has no body in game/shared/ehandle.h";

	EXPECT_FALSE( Has( body, "!defined( CLIENT_DLL )" ) )
		<< "the arm is guarded back to the server. The client's shells are in "
		   "the client SLOT TABLE and not in cl_entitylist, so the stock line "
		   "below the guard resolves nothing of this port's on that side: every "
		   "client EHANDLE answers NULL for every managed entity at once. Body "
		   "was: " << body;

	EXPECT_TRUE( Has( body, "#if defined( SOURCESHARP )" ) )
		<< "the arm is not gated on SOURCESHARP at all. The stock SDK build has "
		   "no SourceSharp_EntityFromHandle to call and must keep the entity "
		   "list line. Body was: " << body;
}

TEST( EntityListSweep, TheHandlesOwnGetKeepsTheStockArmForTheNonPortBuild )
{
	const std::string body = BodyOf( Code( "game/shared/ehandle.h" ),
	                                 "inline IHandleEntity* CBaseHandle::Get() const" );

	ASSERT_FALSE( body.empty() ) << "CBaseHandle::Get has no body in game/shared/ehandle.h";

	EXPECT_TRUE( Has( body, "g_pEntityList->LookupEntity( *this )" ) )
		<< "the stock arm is gone. This header is compiled into the unported SDK "
		   "too, where CBaseEntityList is the whole answer; removing the line "
		   "leaves that build with no resolve at all. Body was: " << body;
}

//-----------------------------------------------------------------------------
// AND BOTH SIDES ACTUALLY DEFINE IT. The guard above widened the DECLARATION to
// both DLLs; a declaration with no definition on one side is a link error that
// only the flip's relink arm would have shown, because the client's copy of the
// inline is a weak COMDAT that the ordinary build still resolves out of
// cliententitylist.cpp.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, BothSidesDefineTheUnionResolve )
{
	EXPECT_FALSE( BodyOf( Code( "sourcesharp/game/server/shell_entityslots.cpp" ),
	                      "IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle )" ).empty() )
		<< "the SERVER has no definition of SourceSharp_EntityFromHandle.";

	EXPECT_FALSE( BodyOf( Code( "sourcesharp/game/client/shell_cliententitylist.cpp" ),
	                      "IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle )" ).empty() )
		<< "the CLIENT has no definition of SourceSharp_EntityFromHandle, so "
		   "ehandle.h's unguarded arm is an undefined reference in every client "
		   "TU that resolves a handle.";
}

//-----------------------------------------------------------------------------
// THE CLIENT'S UNION ASKS THE SAME THREE ARMS THE SERVER'S DOES, and asks them
// through ClientUnknownFromHandle rather than open-coding a second order.
//
// Two orders for one question is how the two halves of a resolve come to
// disagree: `c8-listseam` recorded exactly that happening between
// SourceSharp_RenderableFromHandle's two arms and ClientUnknownFromHandle's
// three. This fact keeps the union from becoming a third.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheClientUnionDelegatesToTheOneResolveOrder )
{
	const std::string body =
		BodyOf( Code( "sourcesharp/game/client/shell_cliententitylist.cpp" ),
		        "IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle )" );

	ASSERT_FALSE( body.empty() ) << "the client union resolve has no body";

	EXPECT_TRUE( Has( body, "ClientUnknownFromHandle( handle )" ) )
		<< "the client union does not delegate to ClientUnknownFromHandle. A "
		   "second hand-written arm order is a second thing to keep in step. "
		   "Body was: " << body;

	EXPECT_TRUE( Has( body, "!handle.IsValid()" ) )
		<< "the client union does not reject an invalid handle first. "
		   "CBaseHandle::GetEntryIndex() answers NUM_ENT_ENTRIES-1 for an "
		   "invalid handle BY DESIGN (basehandle.h), so an unguarded resolve "
		   "asks the table about entry 8191 on every default-constructed "
		   "handle. Body was: " << body;
}

//-----------------------------------------------------------------------------
// AND THE LEVEL SHUTDOWN USES THE ONE THAT KEEPS THE SERIALS.
//
// slot_table_test.cpp proves the two clears BEHAVE differently; this says which
// one the level shutdown calls, because that is the half a unit test cannot see
// and the half that crashed. The edict's m_NetworkSerialNumber survives a map
// load, so a table that rewound would hand map 2's entity at edict 5 the serial
// map 1's had and the engine would pack it as a continuation of the dead one.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheLevelShutdownClearsTheSlotTableWithoutRewindingItsSerials )
{
	const std::string body = BodyOf(
		Code( "sourcesharp/game/server/shell_entityslots.cpp" ),
		"void SourceSharp_ResetEntitySlots( void )" );

	ASSERT_FALSE( body.empty() ) << "SourceSharp_ResetEntitySlots has no body";

	EXPECT_TRUE( Has( body, "SourceSharp_SlotTable().ClearBindings()" ) )
		<< "the level shutdown does not clear the slot table with ClearBindings. "
		   "Body was: " << body;

	EXPECT_FALSE( Has( body, "SourceSharp_SlotTable().Reset()" ) )
		<< "the level shutdown REWINDS the slot table's serials. The edict's "
		   "mirror survives the map, so the next occupant of an edict would "
		   "carry the serial the last one did and SV_PackEntity would reuse its "
		   "PackedEntity. Body was: " << body;
}

//-----------------------------------------------------------------------------
// EVERY SHIM FILE, WALKED RATHER THAN LISTED.
//
// The per-file facts above say WHICH calls are left in the files somebody
// thought to name; this says HOW MANY over the WHOLE shim, so a call added to a
// file with no fact of its own cannot arrive unnoticed. The first draft of this
// summed a hard-coded list and was mutation-proved USELESS: putting the entity
// list back into shell_standalone_player.cpp -- which was not on the list --
// left it green. A ledger that can only refuse growth where it already looks is
// the shape this tree calls a check that cannot fail.
//
// It is a RATCHET and not a target. The number falls when the atomic flip
// deletes game/server/entitylist.cpp and the hierarchy it serves; until then
// every one of these has a live subject, and deleting one to move the number is
// what COMMON.md's "a compile gate selects for deletion" is about.
//-----------------------------------------------------------------------------
void CollectShimSources( const std::string &dir, std::vector< std::string > &into )
{
	DIR *pDir = opendir( dir.c_str() );

	if ( !pDir )
		return;

	while ( struct dirent *pEnt = readdir( pDir ) )
	{
		const std::string name = pEnt->d_name;

		if ( name == "." || name == ".." )
			continue;

		const std::string path = dir + "/" + name;

		struct stat st;
		if ( stat( path.c_str(), &st ) != 0 )
			continue;

		if ( S_ISDIR( st.st_mode ) )
		{
			CollectShimSources( path, into );
			continue;
		}

		const size_t dot = name.rfind( '.' );

		if ( dot == std::string::npos )
			continue;

		const std::string ext = name.substr( dot );

		if ( ext == ".cpp" || ext == ".h" )
			into.push_back( path );
	}

	closedir( pDir );
}

//-----------------------------------------------------------------------------
// THE CLIENT'S SPELLING, WHICH THE RATCHET BELOW DOES NOT COUNT -- lane
// sub-entitylist.
//
// EveryShimFileTogetherNamesTheEntityListSixteenTimes walks the whole shim for
// `gEntList.` and `g_pEntityList->`. Those are the SERVER's two spellings. A
// game/shared shim file compiled into BOTH DLLs can name the client's list
// instead, inside `#ifdef CLIENT_DLL`, and the ratchet counts none of it: it
// was 16 before this lane removed two such sites and it is 16 after, which is
// correct arithmetic and a hole.
//
// The two were sound_emitter.cpp's `ClientEntityList().GetClientEntity(
// entindex )` and util_shared_library.cpp's `ClientEntityList().EntIndexToHandle(
// iEntIndex )`. Both are index lookups, both now take
// shell_cliententitylist.h's slot-table-first resolvers, and NOTHING WOULD HAVE
// SAID SO IF EITHER CAME BACK. This closes that.
//
// COMMENTS ARE STRIPPED, unlike the ratchet below, and that is not a
// discrepancy to tidy: this expects ZERO, and four shim files explain in prose
// what `cl_entitylist` and `ClientEntityList()` used to do here. Counting prose
// as code is the mistake this tree has made six times, and a zero-expecting
// check is exactly where it would bite. The ratchet below expects a number that
// INCLUDES its comments and says so in its own name, so it must not strip.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, NoShimFileNamesTheCLIENTSEntityListInCodeEither )
{
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL ) << "cannot find the source root";

	std::vector< std::string > files;
	CollectShimSources( std::string( pRoot ) + "/sourcesharp/game/server", files );
	CollectShimSources( std::string( pRoot ) + "/sourcesharp/game/shared", files );

	// The same floor the ratchet carries, for the same reason: a walk that
	// found nothing sums to zero, and zero is what this expects.
	ASSERT_GT( files.size(), 100u )
		<< "the shim walk found " << files.size() << " files, so a zero below "
		   "would mean the walk and not the code.";

	int nTotal = 0;
	std::string named;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		std::ifstream in( files[i].c_str(), std::ios::binary );
		if ( !in )
			continue;

		const std::string code = StripComments(
			std::string( ( std::istreambuf_iterator< char >( in ) ),
			               std::istreambuf_iterator< char >() ) );

		// `ClientEntityList` WITH ITS OPENING PARENTHESIS, so that
		// CSourceSharpClientEntityList and SourceSharp_ClientEntityList -- the
		// port's own names -- are not read as calls to the stock accessor. The
		// same rule client_entity_resolve_test.cpp's NamesTheStockList carries,
		// and for the same reason.
		int nHere = LinesWith( code, "ClientEntityList()" )
		          + LinesWith( code, "ClientEntityList ()" )
		          + LinesWith( code, "cl_entitylist" );

		if ( nHere > 0 )
		{
			nTotal += nHere;
			named += "\n    " + files[i];
		}
	}

	EXPECT_EQ( 0, nTotal )
		<< "a shim file names the CLIENT's stock entity list in code this build "
		   "compiles. plan_entity.md clause 2 says no compiled TU may, and every "
		   "such question a shim file asks is an INDEX or a HANDLE lookup that "
		   "sourcesharp/game/client/shell_cliententitylist.h already answers off "
		   "the slot table -- SourceSharp_ClientEntityFromIndex, "
		   "SourceSharp_ClientHandleFromIndex and the four beside them. Route it "
		   "there rather than deleting it: deleting sound_emitter.cpp's is a "
		   "silent GENDER_NONE for every managed entity's sound, which is the "
		   "defect step 7e closed. The files carrying them:"
		<< named;
}

TEST( EntityListSweep, EveryShimFileTogetherNamesTheEntityListFiveTimesAndOnlyInProse )
{
	const char *pRoot = SrcRoot();
	ASSERT_TRUE( pRoot != NULL ) << "cannot find the source root";

	std::vector< std::string > files;
	CollectShimSources( std::string( pRoot ) + "/sourcesharp/game/server", files );
	CollectShimSources( std::string( pRoot ) + "/sourcesharp/game/shared", files );

	// THE WALK ITSELF IS CHECKED. An opendir that quietly found nothing would
	// sum to zero, which is a number BELOW the ratchet and therefore reads as
	// progress -- the exact failure the walk was written to stop.
	ASSERT_GT( files.size(), 100u )
		<< "the shim walk found " << files.size() << " files. It is meant to find "
		   "every .cpp and .h under sourcesharp/game/{server,shared}; a short walk "
		   "sums to a number that looks like progress.";

	int nTotal = 0;
	int nCode  = 0;
	std::vector< std::string > carriers;
	std::vector< std::string > callers;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		std::ifstream in( files[i].c_str(), std::ios::binary );
		if ( !in )
			continue;

		const std::string src( ( std::istreambuf_iterator< char >( in ) ),
		                         std::istreambuf_iterator< char >() );

		int nHere = 0;
		size_t at = 0;

		while ( at < src.size() )
		{
			const size_t eol = src.find( '\n', at );
			const std::string line = src.substr(
				at, ( eol == std::string::npos ) ? std::string::npos : eol - at );

			if ( line.find( "gEntList." ) != std::string::npos
				|| line.find( "g_pEntityList->" ) != std::string::npos )
				nHere++;

			if ( eol == std::string::npos )
				break;

			at = eol + 1;
		}

		if ( nHere > 0 )
		{
			nTotal += nHere;
			carriers.push_back( files[i] );
		}

		// THE SAME FILE WITH THE COMMENTS TAKEN OUT. Five lines is small enough
		// that "the shim stopped calling the list" and "somebody wrote a
		// paragraph about the list" produce the same raw number, so the raw
		// number can no longer be asserted on its own.
		const std::string code = StripComments( src );
		const int nCodeHere = LinesWith( code, "gEntList." )
		                    + LinesWith( code, "g_pEntityList->" );
		if ( nCodeHere > 0 )
		{
			nCode += nCodeHere;
			callers.push_back( files[i] );
		}
	}

	std::sort( carriers.begin(), carriers.end() );
	std::sort( callers.begin(), callers.end() );

	std::string named;
	for ( size_t i = 0; i < carriers.size(); i++ )
		named += "\n    " + carriers[i];

	std::string namedCallers;
	for ( size_t i = 0; i < callers.size(); i++ )
		namedCallers += "\n    " + callers[i];

	// THE NUMBER THAT MATTERS, and it is zero. Every gEntList call the shim
	// ever made is either gone or was never compiled: f288a5e11 removed the
	// guarded arms that carried most of them, and clause 2 of the definition of
	// done says the count that is allowed to stand is nil.
	EXPECT_EQ( 0, nCode )
		<< "a shim file CALLS the SDK entity list. Gate 2 counts these and "
		   "requires zero; a call here is also a call into a list with no "
		   "population, because armed no compiled server TU registers an entity "
		   "of that hierarchy. The files calling it:" << namedCallers;

	// AND THE RAW-TEXT SURFACE, pinned in both directions as it always was --
	// the unit tools/entity-census counts in, so the two instruments can still
	// be compared. It went 16 -> 5 at f288a5e11; all survivors are prose, which
	// is what the zero above says.
	//
	// 5 -> 7 AT plan_shim.md PHASE 2a, AND NOT ONE WORD OF PROSE WAS WRITTEN.
	// The two added lines are in game/server/physics.cpp, which lane 2-physics
	// RELOCATED into sourcesharp/game/server/ -- Phase 2's exit condition is
	// that server_csharp.vpc (now server_sourcesharp's CMakeLists list) names
	// no row under game/, so a file that
	// stays native leaves that tree instead. This scan is over the SHIM, so the
	// file's existing comments arrived in its population unchanged. Same text,
	// same translation unit, different directory.
	//
	// THE ZERO ABOVE IS WHAT MAKES THAT SAFE TO SAY rather than something to
	// take on trust: physics.cpp appears in `carriers` and NOT in `callers`, so
	// both of its lines are comments -- gEntList and
	// CGlobalEntityList::FindEntityGeneric named in the block explaining why
	// FindPhysicsObjectByName cannot resolve a name through the SDK list. The
	// rule this fact enforces is unchanged in both directions: a call coming
	// back still fails the zero, and a deleted paragraph still fails this.
	// 7 -> 5 AT P5 LANE E: the two baseentity_statics lines described
	// EntityCreate_NoSpawn's NotifyCreateEntity and the moved-not-lifted
	// Instance forwarder. The first went with the dead function it was inside;
	// the second with the header's history of statics that no longer exist.
	// 5 -> 6 AT P5 LANE E, AND NOT ONE WORD OF PROSE WAS WRITTEN: anyentity.h
	// left the overlay for src/server/native/, so its existing comment on the
	// SDK list arrived in this scan's population unchanged -- the relocation
	// case named below. The zero above still holds over it.
	EXPECT_EQ( 6, nTotal )
		<< "the shim's raw-text entity-list surface moved. All six are COMMENTS "
		   "-- shell_restoreentities.cpp on what the stock restore walk yields, "
		   "world_adapter.cpp on the level-shutdown order, "
		   "shell_standalone_hierarchy.cpp on FindEntityByName, anyentity.h on "
		   "the arm it no longer has, and "
		   "physics.cpp (twice) on why FindPhysicsObjectByName cannot resolve a "
		   "name through the SDK list. If this "
		   "number went UP, either a call came back (which the zero above catches), "
		   "or a new paragraph was written, or a native file was relocated into "
		   "the shim tree carrying comments it already had; if it went DOWN, a "
		   "comment recording "
		   "why something is NOT done was deleted, and that history is the only "
		   "thing standing between this file and a re-litigation. The files "
		   "carrying them:" << named;
}


//=============================================================================
// 2. THE REVERSE SCAN IS GONE.
//=============================================================================

TEST( EntityListSweep, TheReverseScanHeaderAndItsTestAreDeleted )
{
	EXPECT_FALSE( Exists( "sourcesharp/game/shared/gameentity_handle.h" ) )
		<< "the scan template outlived the scan";
	EXPECT_FALSE( Exists( "sourcesharp/test/gameentity_handle_test.cpp" ) )
		<< "seven tests for a walk that no longer exists are seven tests that "
		   "cannot fail";
}

TEST( EntityListSweep, TheGameEntityOverloadIsStillDefinedBecauseTwoPreludesNameIt )
{
	// It cannot simply be deleted: ehandle.h's CHandle<IGameEntity>::Set and
	// networkvar.h's CNetworkHandleBase::Set both name it. Deleting the
	// definition would turn a refusal that reports itself into a link error in
	// whatever TU first instantiates the template.
	EXPECT_FALSE( RefusalBody().empty() )
		<< "the game-boundary overload networkvar.h declares must still be "
		   "defined, inline, under the signature this test brace-matches from";
}

//-----------------------------------------------------------------------------
// AND ITS FILE IS GONE. plan_entity.md's Phase 1 exit names `gameentity_handle.*`
// among the things that are; the .cpp was one translation unit compiled into
// BOTH DLLs to hold one function that answers a constant.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheReverseRouteHasNoTranslationUnitOfItsOwn )
{
	EXPECT_FALSE( Exists( kDeletedRoute ) )
		<< "a refusal is not a translation unit's worth of code";

	// Neither target may compile it either, under ANY directory -- the file
	// being gone does not by itself say the build stopped asking for it (VPC
	// warned on a missing $File; a stale compile_commands.json would too). What
	// each target compiles is read through cmake_graph.h, whose own staleness
	// check pairs compile_commands.json with the CMakeLists list; the port's
	// shim is each target's proof-of-read.
	const char *const kTargets[] = { cmake_graph::kServer, cmake_graph::kClient };
	for ( size_t t = 0; t < sizeof( kTargets ) / sizeof( kTargets[ 0 ] ); t++ )
	{
		const std::set< std::string > compiled = cmake_graph::CompiledSources( kTargets[ t ] );
		ASSERT_TRUE( compiled.count( "sourcesharp/game/shared/shell_shared.cpp" ) )
			<< kTargets[ t ] << " does not compile sourcesharp/game/shared/shell_shared.cpp "
			   "-- the reader is not reading its source list";
		for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
			EXPECT_FALSE( Has( *it, "gameentity_handle.cpp" ) )
				<< kTargets[ t ] << " (" << cmake_graph::ListFile( kTargets[ t ] )
				<< ") compiles " << *it;
	}

	// And ehandle.h reaches the definition rather than re-declaring the arm:
	// its CHandle<IGameEntity>::Set specialisation is the second caller, and a
	// bare declaration would link only in TUs that happened to pull networkvar.h
	// in by another road.
	EXPECT_TRUE( Has( Code( "game/shared/ehandle.h" ), "#include \"networkvar.h\"" ) )
		<< "ehandle.h must SEE the inline definition, not just its name";
}

TEST( EntityListSweep, TheOverloadSearchesNothingAndReturnsTheInvalidHandle )
{
	const std::string src = RefusalBody();
	ASSERT_FALSE( src.empty() ) << "no refusal body in " << kReverseRoute;

	EXPECT_FALSE( Has( src, "for (" ) ) << "a loop is the scan coming back";
	EXPECT_FALSE( Has( src, "while (" ) ) << "a loop is the scan coming back";
	EXPECT_FALSE( Has( src, "FirstEntInfo" ) );
	EXPECT_FALSE( Has( src, "NextEntInfo" ) );
	EXPECT_FALSE( Has( src, "SourceSharp_ScanForGameEntity" ) );
	EXPECT_TRUE( Has( src, "return CBaseHandle();" ) )
		<< "the invalid handle is the only thing it can honestly answer";
}

TEST( EntityListSweep, TheRefusalIsLoudAndRateLimited )
{
	const std::string src = RefusalBody();
	ASSERT_FALSE( src.empty() ) << "no refusal body in " << kReverseRoute;

	// A silent invalid handle here is a network var that stops replicating with
	// no error anywhere. That was the whole reason the deleted scan returned
	// three outcomes instead of a bare handle, and the reasoning outlives it.
	EXPECT_TRUE( Has( src, "Warning(" ) ) << "the refusal has to report itself";
	EXPECT_TRUE( Has( src, "AssertMsg(" ) );
	EXPECT_TRUE( Has( src, "s_pLastComplaint" ) )
		<< "the caller is a setter reachable every tick; an unlimited Warning is "
		   "how a log becomes unreadable and therefore unread";
}

TEST( EntityListSweep, TheReverseRouteAddsNoCast )
{
	const std::string src = RefusalBody();
	ASSERT_FALSE( src.empty() ) << "no refusal body in " << kReverseRoute;

	// The obvious replacement for the scan is a downcast: IGameEntity is a
	// non-virtual base of the one shell class, so static_cast would even be
	// adjusted correctly. Section 3.16 permits exactly ONE downcast on an entity
	// path and it is already spent, inside the slot-table resolver.
	EXPECT_FALSE( Has( src, "static_cast" ) );
	EXPECT_FALSE( Has( src, "reinterpret_cast" ) );
	EXPECT_FALSE( Has( src, "dynamic_cast" ) );
	EXPECT_FALSE( Has( src, "CStandaloneEntity" ) )
		<< "naming the concrete shell here is the cast arriving by another road";
}

//=============================================================================
// 3. THE TWO RESOLVERS THIS STEP ADDED.
//=============================================================================

TEST( EntityListSweep, ShellFromEdictIsGone )
{
	// Section 3.16 family 2's edict-keyed resolver had no caller left, and the
	// server gc cull (2026-10-01) deleted it with its declaration: server.so's
	// --print-gc-sections link dropped it. Each file is proved read first.
	const std::string header = Code( kResolveHeader );
	const std::string src = Code( kEntityApi );
	ASSERT_NE( std::string::npos, header.find( "SourceSharp_GameEntityFromHandleEntity" ) );
	ASSERT_NE( std::string::npos, src.find( "SourceSharp_GameEntityFromHandleEntity" ) );
	EXPECT_FALSE( Has( header, "SourceSharp_ShellFromEdict" ) )
		<< "the edict resolver is declared again; say which caller needs it";
	EXPECT_FALSE( Has( src, "SourceSharp_ShellFromEdict" ) )
		<< "the edict resolver is defined again; say which caller needs it";
}

TEST( EntityListSweep, TheGameEntityForwardCastsNothingAndCallsTheOneResolver )
{
	const std::string src = Code( kEntityApi );
	const size_t at = src.find( "IGameEntity *SourceSharp_GameEntityFromHandleEntity( IHandleEntity *pHandleEntity )" );

	ASSERT_NE( at, std::string::npos )
		<< "toucher.h's game arm needs a forward that does not drag the whole "
		   "shell header into 26 translation units";

	const std::string body = src.substr( at, src.find( "\n}", at ) - at );

	EXPECT_TRUE( Has( body, "return SourceSharp_ShellFromHandleEntity( pHandleEntity );" ) )
		<< "it must be the SAME resolver, not a second one that indexes the table";
	EXPECT_FALSE( Has( body, "static_cast" ) );
	EXPECT_FALSE( Has( body, "reinterpret_cast" ) );
	EXPECT_FALSE( Has( body, "GetRefEHandle" ) )
		<< "indexing the table here would be the second resolver 1c's handover "
		   "forbids";
}

//=============================================================================
// 4. THE TOUCHER.
//=============================================================================

TEST( EntityListSweep, ToucherNoLongerPullsTheEntityListIntoTwentySixTranslationUnits )
{
	const std::string src = Code( kToucher );

	EXPECT_FALSE( Has( src, "#include \"entitylist.h\"" ) )
		<< "this header is read by 26 server TUs and needed the list for one line";
	EXPECT_TRUE( Has( src, "#include \"shell_resolve.h\"" ) );
}

TEST( EntityListSweep, ToucherResolvesAHandleThroughTheHandlesOwnRead )
{
	const std::string src = Code( kToucher );

	EXPECT_TRUE( Has( src, "return ToucherFromEntityHandle( handle.Get() );" ) )
		<< "CBaseHandle::Get() is the read every EHANDLE in the tree performs; "
		   "naming the list object was the only thing that needed entitylist.h";
}

TEST( EntityListSweep, TouchersGameArmGoesThroughTheSlotTable )
{
	const std::string src = Code( kToucher );

	EXPECT_TRUE( Has( src, "toucher.pGame = SourceSharp_GameEntityFromHandleEntity( pHandleEntity );" ) )
		<< "section 3.16 family 3: the C-style (IServerEntity *) cast in "
		   "GameEntityFromEntityHandle becomes a virtual plus a table lookup";
	EXPECT_FALSE( Has( src, "GameEntityFromEntityHandle(" ) )
		<< "the util_shared.h helper is the cast this step replaces";
}

//=============================================================================
// 5. THE SOUND PATCH.
//=============================================================================

TEST( EntityListSweep, SoundPatchEntIndexResolvesThroughTheSlotTable )
{
	const std::string src = Code( kSoundEnvelope );

	EXPECT_TRUE( Has( src, "if ( !SourceSharp_ShellFromHandle( m_hEnt ) )" ) )
		<< "the serial check is what makes a stale handle answer -1";
	EXPECT_TRUE( Has( src, "CSourceSharpSlotTable::IsEdictEntry( nEntry ) ? nEntry : -1" ) )
		<< "an entry above MAX_EDICTS is a server-only entity with no index to "
		   "give, and -1 is what the old route answered for it too";
}

TEST( EntityListSweep, SoundPatchEntIndexNoLongerWalksTheNetworkableFace )
{
	const std::string src = Code( kSoundEnvelope );

	EXPECT_FALSE( Has( src, "static_cast< IServerUnknown * >( pEnt )" ) )
		<< "the table already knows which entry the shell is bound at; the cast "
		   "and the two virtuals after it were the long way round";
}

TEST( EntityListSweep, SoundPatchBuildResolvesTheNetworkIndexThroughTheTable )
{
	const std::string src = Code( kSoundEnvelope );

	EXPECT_TRUE( Has( src, "? SourceSharp_ShellFromIndex( nEntIndex )" ) )
		<< "ResolveByIndex has exactly the contract LookupEntityByNetworkIndex "
		   "had: whoever occupies that entry right now, no serial to check";
}

//=============================================================================
// 6. THE FINDERS: the name walk left for the managed directory, the classname
// finder stays.
//=============================================================================

// THE NAME WALK IS GONE (plan_engineapi_vtables Phase 0). Names are answered by
// the managed directory since server-novalve's names steps 1-3, the
// FindEntityByName slot was removed at ABI 252, and so was
// SourceSharp_FindEntityByName. The rules this section used to pin in its body
// (the trailing-star matcher, the order, every match reached) are managed facts
// now: EntityFirstByNameTests. What is left to pin natively is the absence.
TEST( EntityListSweep, TheNativeNameWalkIsGone )
{
	const std::string src = Code( kEntityApi );

	// The control: the classname finder below still lives in this file.
	ASSERT_TRUE( Has( src, "EntityNames_Match( pClassName, \"worldspawn\" )" ) );
	EXPECT_FALSE( Has( src, "SourceSharp_FindEntityByName(" ) )
		<< "the native name walk is back; names are the managed directory's";
	EXPECT_FALSE( Has( src, "EntityNames_Match( pName, pEntityName )" ) );
}

TEST( EntityListSweep, TheClassnameFinderStillAnswersForTheWorldFirst )
{
	const std::string src = Code( kEntityApi );

	// The world is on NEITHER hierarchy, so it was missed by both halves of the
	// old walk and every enumeration in the game silently lost it. Dropping the
	// SDK half must not take the repair with it.
	EXPECT_TRUE( Has( src, "EntityNames_Match( pClassName, \"worldspawn\" )" ) );
	EXPECT_TRUE( Has( src, "if ( CWorld *pWorld = GetWorldEntity() )" ) );
}

//=============================================================================
// 7. THE TRACE DIAGNOSTIC.
//=============================================================================

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. Two of the three clauses below were the
// other way round until the close, and the spelling they turned on has changed
// meaning underneath them.
//
// `TraceEntity` WAS the cast: util_shared.h's C-style cast of tr.m_pEnt to the
// server-entity interface followed by ->GetBaseEntity(). Every one of the eight
// implementations of GetBaseEntity() in the two DLLs is `mov $0x0,%eax; ret`,
// so that arm answered NULL for everything and was deleted as dead code with
// its whole family (brief item 3). `TraceGameEntity` is its LIVE SUCCESSOR and
// is not a cast of the kind gate 3 counts: it asks the virtual GetGameEntity(),
// which every hierarchy in this build really implements.
//
// So the fact keeps its shape -- TWO ARMS PRINTED BESIDE EACH OTHER, because
// the two coming apart is the failure this command exists to catch -- and the
// arm that is required has swapped identity. The last clause is what stops that
// being a rename: it reads TraceGameEntity's own definition and requires it to
// reach the virtual, so "not a cast" is checked where it is true rather than
// asserted where it is convenient.
//-----------------------------------------------------------------------------
TEST( EntityListSweep, TheTraceSubjectCommandPrintsTheTablesAnswerRatherThanACast )
{
	const std::string src = Code( kWorldAdapterDebug );
	ASSERT_FALSE( src.empty() ) << kWorldAdapterDebug << " read back empty.";

	EXPECT_TRUE( Has( src, "SourceSharp_ShellFromHandleEntity( tr.m_pEnt )" ) )
		<< "the slot table's arm is gone. It is the one that can tell 'not one of "
		   "ours' from 'one of ours': it asks the object for its handle through a "
		   "virtual it really has and indexes the table, so a static prop, a "
		   "detail model or a recycled entry all answer NULL.";

	EXPECT_TRUE( Has( src, "TraceGameEntity( tr )" ) )
		<< "the other arm is gone and there is nothing left to compare the table "
		   "against. A one-armed diagnostic cannot report the disagreement it was "
		   "written to find.";

	EXPECT_FALSE( Has( src, "TraceEntity( tr )" ) )
		<< "the DELETED arm is back. TraceEntity bottoms out in "
		   "IServerUnknown::GetBaseEntity(), which is `mov $0x0,%eax; ret` in "
		   "both DLLs -- printing it would put a column of NULLs beside the "
		   "table's answer and read as a permanent disagreement.";

	// AND THE SUCCESSOR IS NOT A CAST WHERE IT IS DEFINED.
	const std::string util = Code( "game/shared/util_shared.h" );
	ASSERT_FALSE( util.empty() ) << "util_shared.h read back empty.";
	const std::string body = BodyOf( util, "inline IGameEntity *TraceGameEntity( const trace_t &tr )" );
	ASSERT_FALSE( body.empty() ) << "TraceGameEntity has no body in util_shared.h.";
	EXPECT_TRUE( Has( body, "GameEntityFromEntityHandle( tr.m_pEnt )" ) )
		<< "TraceGameEntity stopped going through the virtual route. Body was: " << body;
	EXPECT_FALSE( Has( body, "GetBaseEntity" ) )
		<< "TraceGameEntity reaches GetBaseEntity, which returns 0 in all eight "
		   "implementations. Body was: " << body;
}
