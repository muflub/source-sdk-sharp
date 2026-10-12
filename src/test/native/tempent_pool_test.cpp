//====== SourceSharp — the legacy temp entity pool's SEAM ===================//
//
// Lane C4, plan_tmp.md Part 5. docs/te-legacy-pool-survey.md is the map.
//
// The pool's BEHAVIOUR is tested in C#, where it now lives
// (SourceSharp.Tests/TempEntPoolTests.cs, 27 [Fact]s over a double that models
// the slot table). What is left on this side is a SEAM, and a seam has three
// ways of being wrong that no managed test can see:
//
//   1. A FIELD HALF-WIRED. SourceSharpTempEntLife_t is a VIEW of a live
//      C_LocalTempEntity: ReadLife fills it and WriteLife puts it back. A field
//      that appears in one and not the other compiles, passes the managed
//      round-trip (the double keeps whatever it was handed), and is a tempent
//      whose fade speed is read and silently discarded.
//
//   2. THE STOCK ARM DELETED. Nine bodies in c_te_legacytempents.cpp are
//      wrapped in #ifndef SOURCESHARP. The stock client still compiles that
//      file with SOURCESHARP undefined, and the SourceSharp build never
//      exercises the other arm -- so a lane that "tidied" a stock body away
//      would break a build nothing here runs and nothing here would notice.
//
//   3. A SECOND IMPLEMENTATION OF ONE RULE. FreeLowPriorityTempEnt and
//      KillAttachedTents are REFUSALS under SOURCESHARP rather than native
//      re-walks of the slot table, deliberately: two walks of one list that
//      agree today are free to disagree later. That decision is invisible
//      afterwards unless it is written down as a check.
//
// EVERY ASSERTION READS THE .cpp OFF DISK, with comments stripped first --
// which is not hygiene here either. This lane left long banners naming every
// construct it moved, so a scan that read them would find "m_TempEnts",
// "FreeLowPriorityTempEnt" and "AddNonNetworkableEntity" in files that do not
// use them, and would pass while measuring nothing. Same rule, and the same
// reason, as tempent_verdict_test.cpp.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <cctype>
#include <fstream>
#include <string>
#include <vector>
#include <sys/stat.h>
#include <iterator>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "..", "../src", "src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
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
	std::ifstream file( strAbsolute.c_str(), std::ios::binary );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
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

std::string ReadStripped( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	const std::string all = ReadRaw( std::string( pszRoot ) + "/" + strRelative );
	return all.empty() ? all : StripComments( all );
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

std::string ReadSquashed( const std::string &strRelative )
{
	return NoWhitespace( ReadStripped( strRelative ) );
}

int CountOf( const std::string &hay, const std::string &needle )
{
	int n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + needle.size() ) )
	{
		n++;
	}
	return n;
}

const char *kPoolCpp   = "sourcesharp/game/client/shell_te_pool_client.cpp";
// The thunk that replaced game/client/c_te_legacytempents.cpp when C2 evicted it.
const char *kLegacyCpp = "sourcesharp/game/client/shell_legacy_tempents_client.cpp";
const char *kLegacyH   = "game/client/c_te_legacytempents.h";
const char *kAbiHeader = "sourcesharp/host/sourcesharp.h";
// Where the client's slots are wired since plan_client_vtables CC-last deleted
// the struct fill in sourcesharp_gamesystem.cpp: its shim areas' generated forwards.
const char *kClientAreas = "sourcesharp/game/shared/shell_shim_areas_client.inc";
const char *kClientAreasH = "sourcesharp/game/shared/ishim_areas_client.h";
// The managed-built areas the client calls into (plan_client_vtables CE).
const char *kManagedAreasH = "sourcesharp/game/shared/imanaged_areas_client.h";

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

// The eight fields of SourceSharpTempEntLife_t, paired with the
// C_LocalTempEntity member each one is a view OF. Written out rather than
// parsed from the header on purpose: a list derived from the thing under test
// would agree with any change to it, which is this project's "do not compare a
// computation with itself".
struct LifeField
{
	const char *pszAbi;      // the struct's field
	const char *pszMember;   // what it reads and writes on the tempent
};

const LifeField kLife[] =
{
	{ "Die",         "die" },
	{ "Flags",       "flags" },
	{ "FadeSpeed",   "fadeSpeed" },
	{ "RenderAmt",   "tempent_renderamt" },
	{ "Priority",    "priority" },
	{ "ClientIndex", "clientIndex" },
	{ "RenderMode",  "m_nRenderMode" },
	{ "Alpha",       "m_clrRender.a" },
};

const size_t kLifeCount = sizeof( kLife ) / sizeof( kLife[ 0 ] );

//-----------------------------------------------------------------------------
// THE GATE. Every assertion below searches a string, and several are ABSENCES
// which hold trivially in an empty one -- which is how tempent_verdict_test.cpp
// once passed from a directory where src/ did not resolve.
//-----------------------------------------------------------------------------
TEST( TempEntPoolSeam, EverySubjectFileIsReadable )
{
	const char *kAll[] = { kPoolCpp, kLegacyCpp, kLegacyH, kAbiHeader, kClientAreas, kManagedAreasH };

	ASSERT_NE( SrcRoot(), (const char *)NULL )
		<< "src/ was not found from the working directory, so every scan below "
		   "would search an empty string.";

	for ( size_t i = 0; i < sizeof( kAll ) / sizeof( kAll[ 0 ] ); i++ )
	{
		EXPECT_FALSE( ReadStripped( kAll[ i ] ).empty() )
			<< kAll[ i ] << " could not be read or is empty. Nothing else in "
			                "this file is evidence until it can be.";
	}
}

//-----------------------------------------------------------------------------
// (1) THE VIEW IS WHOLE IN BOTH DIRECTIONS.
//-----------------------------------------------------------------------------
TEST( TempEntPoolSeam, EveryLifeFieldIsBothReadAndWritten )
{
	const std::string pool = ReadSquashed( kPoolCpp );

	ASSERT_FALSE( pool.empty() );

	for ( size_t i = 0; i < kLifeCount; i++ )
	{
		const std::string read  = std::string( "pOutLife->" ) + kLife[ i ].pszAbi + "=";
		const std::string write = std::string( "pLife->" ) + kLife[ i ].pszAbi;

		EXPECT_EQ( 1, CountOf( pool, read ) )
			<< "SourceSharpTempEntLife_t::" << kLife[ i ].pszAbi << " is filled exactly "
			   "once by SourceSharp_TempEntPoolReadLife. Zero means the managed pool "
			   "reads a default for it -- a tempent whose fadeSpeed is always 0 fades "
			   "instantly, and nothing anywhere logs.";

		EXPECT_LE( 1, CountOf( pool, write ) )
			<< "SourceSharpTempEntLife_t::" << kLife[ i ].pszAbi << " is put back by "
			   "SourceSharp_TempEntPoolWriteLife. A field read and never written is a "
			   "field the managed pool can change and the tempent will not see.";
	}
}

TEST( TempEntPoolSeam, EveryLifeFieldNamesTheTempentMemberItIsAViewOf )
{
	const std::string pool = ReadSquashed( kPoolCpp );

	ASSERT_FALSE( pool.empty() );

	for ( size_t i = 0; i < kLifeCount; i++ )
	{
		// pTemp->die, pTemp->m_clrRender.a and so on. TWICE at least: once
		// filling the view and once putting it back -- except the two that go
		// through setters, which are checked by name below.
		const std::string member = std::string( "pTemp->" ) + kLife[ i ].pszMember;

		EXPECT_LE( 1, CountOf( pool, member ) )
			<< "the view field " << kLife[ i ].pszAbi << " reads and writes "
			<< "C_LocalTempEntity::" << kLife[ i ].pszMember << ". If that member has "
			   "been renamed the seam is now a view of something else.";
	}
}

// The two that must NOT be plain assignments, because tempent.h publishes
// setters for them and a second spelling here is a second description of one
// rule. SetRenderMode is what stock's IsActive calls when a fading tempent
// leaves the opaque pass; SetRenderColorA is what ramps the alpha.
TEST( TempEntPoolSeam, TheRenderStateGoesBackThroughTempentHsOwnSetters )
{
	const std::string pool = ReadSquashed( kPoolCpp );

	ASSERT_FALSE( pool.empty() );

	EXPECT_NE( pool.find( "pTemp->SetRenderMode((RenderMode_t)pLife->RenderMode);" ),
	           std::string::npos )
		<< "WriteLife no longer sets the render mode through SetRenderMode.";

	EXPECT_NE( pool.find( "pTemp->SetRenderColorA((byte)pLife->Alpha);" ),
	           std::string::npos )
		<< "WriteLife no longer sets the alpha through SetRenderColorA.";
}

//-----------------------------------------------------------------------------
// The six ABI slots exist here and are WIRED. A declared slot nobody wires is
// a null pointer every guarded call in Engine.cs skips in silence -- and for
// these that silence is a pool that allocates and never frees.
//-----------------------------------------------------------------------------
TEST( TempEntPoolSeam, AllSixSlotsAreImplementedHereAndWiredThere )
{
	// DECLARED is IShimTempEntPoolClient's method since plan_client_vtables
	// CC-last deleted the engine API struct the six were fields of.
	const std::string pool  = ReadSquashed( kPoolCpp );
	const std::string sys   = ReadSquashed( kClientAreas );
	const std::string areas = ReadSquashed( kClientAreasH );

	ASSERT_FALSE( pool.empty() );
	ASSERT_FALSE( sys.empty() );
	ASSERT_FALSE( areas.empty() );

	static const char *kSlots[] =
	{
		"TempEntPoolCapacity",
		"TempEntPoolDestroy",
		"TempEntPoolReadLife",
		"TempEntPoolWriteLife",
		"TempEntPoolSimulate",
		"TempEntPoolAddVisible",
	};

	for ( size_t i = 0; i < sizeof( kSlots ) / sizeof( kSlots[ 0 ] ); i++ )
	{
		// Squashed text: "virtual<ret><Name>(", the name running straight on
		// from its return type, which is int32_t or void for these six.
		const std::string decl = std::string( kSlots[ i ] ) + "(";
		EXPECT_EQ( 1, CountOf( areas, "virtualint32_t" + decl ) + CountOf( areas, "virtualvoid" + decl ) )
			<< kSlots[ i ] << " is declared in ishim_areas_client.h.";

		const std::string impl = std::string( "SourceSharp_" ) + kSlots[ i ] + "(";
		EXPECT_LE( 1, CountOf( pool, impl ) )
			<< kSlots[ i ] << " is implemented in shell_te_pool_client.cpp.";

		EXPECT_EQ( 1, ForwardCount( sys, kSlots[ i ], std::string( "SourceSharp_" ) + kSlots[ i ] ) )
			<< kSlots[ i ] << " is WIRED: CShimTempEntPoolClient's method forwards to its "
			   "body in shell_shim_areas_client.inc. Declared and unwired is a null "
			   "pointer Engine.cs skips without a word.";
	}
}

// The frame and the level teardown still go the other way. The two callbacks
// the retired exports used to invoke are asserted DEAD HERE and ALIVE THERE:
// still declared in IManagedTempEntsClient (the area's slots leave in the
// serialised ABI pass, which renumbers the layout), and called from nothing.
// A slot kept while its caller was deleted is honest bookkeeping; a slot whose
// body was deleted too would leave the generated vtable pointing at nothing.
TEST( TempEntPoolSeam, TheTwoLiveCallbacksAreCalledFromNativeAndTheDeadOnesAreNot )
{
	const std::string abi    = ReadSquashed( kManagedAreasH );
	const std::string pool   = ReadSquashed( kPoolCpp );

	ASSERT_FALSE( abi.empty() );
	ASSERT_FALSE( pool.empty() );

	static const char *kLive[] = { "OnTempEntPoolUpdate", "OnTempEntPoolClear" };

	for ( size_t i = 0; i < sizeof( kLive ) / sizeof( kLive[ 0 ] ); i++ )
	{
		const std::string decl = std::string( "virtual" ) + "void" + kLive[ i ] + "(";
		EXPECT_NE( abi.find( decl ), std::string::npos )
			<< kLive[ i ] << " is declared in IManagedTempEntsClient.";

		const std::string call = std::string( "pTempEnts->" ) + kLive[ i ] + "(";
		EXPECT_LE( 1, CountOf( pool, call ) )
			<< kLive[ i ] << " is actually CALLED from shell_te_pool_client.cpp. "
			   "A callback declared, registered on the managed side and never invoked "
			   "is a pool the game never asks anything of -- and the symptom is every "
			   "gib and shell casing in the game silently not spawning.";
	}

	// DEAD, and kept as a slot: still declared, called from nowhere. The
	// positive arm against a vacuous read is the two assertions above finding
	// the live spellings in the SAME two strings this half scans.
	static const char *kDead[] = { "OnTempEntPoolAlloc", "OnTempEntEjectBrass" };

	for ( size_t i = 0; i < sizeof( kDead ) / sizeof( kDead[ 0 ] ); i++ )
	{
		const std::string decl = std::string( "virtual" ) + "int32_t" + kDead[ i ] + "(";
		EXPECT_NE( abi.find( decl ), std::string::npos )
			<< kDead[ i ] << " stays DECLARED: the area's slot table is the ABI, "
			   "and its slots leave in the serialised pass, not with their last caller.";

		const std::string call = std::string( "pTempEnts->" ) + kDead[ i ] + "(";
		EXPECT_EQ( 0, CountOf( pool, call ) )
			<< kDead[ i ] << " is called again. Its only caller was the export the "
			   "retirement removed; a second native path into a slot nothing audits "
			   "is how the alloc/eject pair came back.";
	}
}

// The slot table is sized by the pool's own constant. A literal 500 here would
// be exactly the stand-in this tree's memory says drifts silently, and the
// drift is a write past the end of the table.
TEST( TempEntPoolSeam, TheSlotTableIsSizedByTheEnumAndNotByALiteral )
{
	const std::string pool = ReadSquashed( kPoolCpp );

	ASSERT_FALSE( pool.empty() );

	EXPECT_NE( pool.find( "g_pTempEntSlots[CTempEnts::MAX_TEMP_ENTITIES]" ),
	           std::string::npos )
		<< "the slot table is no longer sized by CTempEnts::MAX_TEMP_ENTITIES.";

	EXPECT_NE( pool.find( "return(int32_t)CTempEnts::MAX_TEMP_ENTITIES;" ),
	           std::string::npos )
		<< "TempEntPoolCapacity no longer ANSWERS the enum. C# asks rather than "
		   "restating 500, and this is the answer it asks for.";

	EXPECT_EQ( pool.find( "500" ), std::string::npos )
		<< "a literal 500 has appeared in the pool seam. The capacity has exactly one "
		   "home and it is CTempEnts::MAX_TEMP_ENTITIES.";
}

//-----------------------------------------------------------------------------
// (2) THE STOCK ARM IS GONE, AND WHAT IS ASSERTED IS THAT IT IS GONE CLEANLY.
//
// THIS SECTION USED TO SAY: "THE STOCK ARM IS STILL THERE. The SourceSharp
// client never compiles these, so nothing this build runs can notice they have
// gone -- and the stock client, which client_base.vpc still builds, would stop
// linking."
//
// THE SECOND CLAUSE WAS WRONG, and it is the clause the whole section rested
// on. client_base.vpc lists c_te_legacytempents.cpp, but NOTHING IN THIS TREE
// BUILDS client_base.vpc: the two projects that exist are client_csharp.vpc and
// server_csharp.vpc and both define SOURCESHARP (:74 and :69 respectively). So
// "the stock client would stop linking" named a build that does not happen
// here, and every `#ifndef SOURCESHARP` arm in this file was source no compiler
// had ever seen.
//
// The `residue` wave (74b071771..a46e8d645) deleted them: 634 lines out of
// c_te_legacytempents.cpp, 18 `#ifdef SOURCESHARP` and 5 `#ifndef SOURCESHARP`
// directives down to none, and the client`s object files came out identical --
// same count, same DEFINED and UNDEFINED symbol sets.
//-----------------------------------------------------------------------------
TEST( TempEntPoolSeam, TheStockPoolStillCompilesBesideTheManagedOne )
{
	// READ THE ASSERTIONS, NOT THE NAME. There is no stock pool beside the
	// managed one any more; the name is kept so the fact keeps its identity.
	//
	// THE SUCCESSOR PROPERTY, and why it is worth as much as the old one. The old
	// fact guarded against a lane "tidying away" a stock body that only a build
	// this tree does not run would have noticed. The thing now worth guarding is
	// the OTHER half of the same removal: that what replaced those bodies is the
	// managed path, present and unconditional, rather than nothing at all. A wave
	// that deleted a dead arm AND the live one beside it would have left this
	// file compiling and every temp entity silently unallocated.
	//
	// So this is two-sided by construction: the four stock spellings are asserted
	// ABSENT, and the four managed ones that took their place are asserted
	// PRESENT. Neither half can pass over an empty or unreadable read -- the
	// presence half fails immediately -- and neither half can pass over a file
	// that has had both pools removed.
	const std::string legacy = ReadSquashed( kLegacyCpp );

	ASSERT_FALSE( legacy.empty() );

	// PRESENCE: the object still comes from the class memory pool, and the
	// list's three questions go to the managed pool. Since the C2 eviction the
	// thunk reserves nothing itself: a slot is the managed create's
	// (SourceSharp_TempEntPoolCreate), so there is no reservation here to leak.
	EXPECT_NE( legacy.find( "returnm_TempEntsPool.AllocZero();" ), std::string::npos )
		<< "TempEntAlloc no longer takes an object from the class memory pool. The "
		   "managed pool owns the LIST; the OBJECT is still allocated here and "
		   "nowhere else.";

	EXPECT_NE( legacy.find( "SourceSharpTePool_Update(gpGlobals->frametime);" ), std::string::npos )
		<< "CTempEnts::Update no longer hands the frame to the managed pool, so no "
		   "tempent ages, moves or dies.";

	EXPECT_NE( legacy.find( "SourceSharpTePool_Clear();" ), std::string::npos )
		<< "CTempEnts::Clear no longer empties the managed pool, so a level change "
		   "keeps the last level's tempents.";

	EXPECT_NE( legacy.find( "returnSourceSharpTePool_Find(nID,nSubID);" ), std::string::npos )
		<< "FindTempEntByID no longer asks the managed pool, which holds the only list.";

	EXPECT_EQ( legacy.find( "SourceSharpTePool_Reserve(" ), std::string::npos )
		<< "the thunk reserves a slot again. Slots are the managed create's; a native "
		   "reservation is a second way into the pool.";

	// ABSENCE: the four stock spellings the residue wave removed. Each one was
	// inside a `#ifndef SOURCESHARP` arm at 74b071771 and none was ever compiled.
	EXPECT_EQ( legacy.find( "m_TempEnts.AddToTail(pTemp);" ), std::string::npos )
		<< "stock's TempEntAlloc appends to m_TempEnts again. The managed pool holds "
		   "the active list; a native append is a second list that agrees today and "
		   "is free to disagree later, which is the exact shape docs/te-ledger.md "
		   "exists to refuse.";

	EXPECT_EQ( legacy.find( "m_TempEnts.Count()>=MAX_TEMP_ENTITIES" ), std::string::npos )
		<< "stock's capacity check is back. The managed pool has already said yes or "
		   "no by the time TempEntAlloc() runs, so a second capacity answer here can "
		   "only disagree with the first.";

	EXPECT_EQ( legacy.find( "staticintgTempEntFrame=0;" ), std::string::npos )
		<< "stock's 0..31 frame counter is back in Update. It lives in C# now "
		   "(ClientTempEntPool); two counters would drive the same modulo from two "
		   "places.";

	EXPECT_EQ( legacy.find( "voidCTempEnts::TempEntFree(intindex)" ), std::string::npos )
		<< "CTempEnts::TempEntFree has a body again. Its `index` is a position in "
		   "m_TempEnts and the managed pool addresses a SLOT, so the name over the "
		   "new meaning compiles at every call site and means something else at each "
		   "-- the reason this file leaves the declaration UNDEFINED, so that a "
		   "native caller appearing later is a link error and not a silent "
		   "index/slot confusion.";

	// AND THE VESTIGE, NAMED. m_TempEnts is still declared
	// (c_te_legacytempents.h:150) and still purged in ~CTempEnts, which is the
	// only use of it left in the file. That is an honest description of the tree
	// and it is asserted so that it stays one: an append or a Count() read
	// appearing beside it is caught by the absences above.
	EXPECT_NE( legacy.find( "m_TempEnts.RemoveAll();" ), std::string::npos )
		<< "~CTempEnts no longer purges m_TempEnts. The list is vestigial but it is "
		   "still a member, and a member that is never cleared is a member that "
		   "holds dangling C_LocalTempEntity pointers across a level change.";
}

// THE BOOKKEEPING HAS NO GUARDS AROUND IT ANY MORE, AND THAT IS WHAT IS
// ASSERTED. This comment used to read: "Nine bodies are guarded, and each pairs
// an #ifdef/#ifndef with an #else or an #endif. A count rather than a shape:
// the shapes differ (some are #ifdef with an #else, one is #ifndef with no
// else) and a test that demanded one of them would be describing this lane's
// formatting rather than its meaning."
//
// The `residue` wave (74b071771..a46e8d645) took c_te_legacytempents.cpp from
// 18 `#ifdef SOURCESHARP` and 5 `#ifndef SOURCESHARP` to ZERO of each, deleting
// 634 lines. Every deleted arm was a `#ifndef SOURCESHARP` body -- source that
// no project in this tree compiles, because client_csharp.vpc:74 and
// server_csharp.vpc:69 both define SOURCESHARP -- and every surviving body is
// what the `#ifdef` arm already selected. The compiler`s input did not change.
TEST( TempEntPoolSeam, TheGuardsAreWhereTheBookkeepingIs )
{
	// READ THE ASSERTIONS, NOT THE NAME. The guards are nowhere; the bookkeeping
	// is unconditional. The name is kept so the fact keeps its identity.
	//
	// A COUNT OF ZERO IS EXACTLY THE ASSERTION A BROKEN COUNTER GIVES, so the
	// zero is not left standing on its own. The same CountOf, over the same two
	// needles, is run against c_te_legacytempents.h -- which the residue wave did
	// not touch, because it only walked .cpp TUs -- and must find the ONE
	// `#ifdef SOURCESHARP` still there at :177. That guard is LIVE, not dead: its
	// arm is the one SOURCESHARP selects, and it publishes the three members
	// (PoolFreeObject, PoolAddVisible, PoolCreateObject) the slot table in
	// shell_te_pool_client.cpp cannot reach any other way. So the pair is
	// "counter finds a guard where there is one, and none where there is none".
	const std::string legacy = ReadStripped( kLegacyCpp );

	ASSERT_FALSE( legacy.empty() );

	const int nIfdef  = CountOf( legacy, "#ifdef SOURCESHARP" );
	const int nIfndef = CountOf( legacy, "#ifndef SOURCESHARP" );

	EXPECT_EQ( 0, nIfdef )
		<< "a #ifdef SOURCESHARP is in shell_legacy_tempents_client.cpp. Every project "
		   "in this tree defines SOURCESHARP, so a guard here selects the arm that "
		   "was going to be compiled anyway and hides an `#else` that never is.";

	EXPECT_EQ( 0, nIfndef )
		<< "a #ifndef SOURCESHARP is in shell_legacy_tempents_client.cpp. Nothing "
		   "compiles that arm; the residue wave removed five of them from this file "
		   "for that reason and the objects were byte-identical afterwards.";

	// THE CONTROL, RE-POINTED. The header used to be it: it carried one LIVE
	// `#ifdef SOURCESHARP` at :177. the-close 2/N collapsed that one too --
	// every project that reaches this header defines SOURCESHARP, so the guard
	// selected the arm that was going to be compiled anyway -- so the header is
	// now a second zero rather than a one, and a control that is also zero
	// proves nothing about the counter.
	//
	// The counter is therefore proved against a fixture that DOES contain both
	// needles, and the header is asserted for what its guard used to buy.
	const std::string strGuardFixture =
		"int a;\n"
		"#ifdef SOURCESHARP\n"
		"int b;\n"
		"#else\n"
		"int c;\n"
		"#endif\n"
		"#ifndef SOURCESHARP\n"
		"int d;\n"
		"#endif\n";

	EXPECT_EQ( 1, CountOf( strGuardFixture, "#ifdef SOURCESHARP" ) )
		<< "the counter cannot find an #ifdef SOURCESHARP that is right in "
		   "front of it, so the zero above is the counter's and not the file's.";
	EXPECT_EQ( 1, CountOf( strGuardFixture, "#ifndef SOURCESHARP" ) )
		<< "the counter cannot find an #ifndef SOURCESHARP that is right in "
		   "front of it, so the zero above is the counter's and not the file's.";

	const std::string legacyH = ReadStripped( kLegacyH );

	ASSERT_FALSE( legacyH.empty() );

	EXPECT_EQ( 0, CountOf( legacyH, "#ifdef SOURCESHARP" ) )
		<< "a #ifdef SOURCESHARP is back in c_te_legacytempents.h. Every "
		   "project that reaches this header defines SOURCESHARP, so the guard "
		   "would select the arm that compiles anyway and hide an #else that "
		   "never does.";
	EXPECT_EQ( 0, CountOf( legacyH, "#ifndef SOURCESHARP" ) )
		<< "a #ifndef SOURCESHARP is back in c_te_legacytempents.h -- an arm no "
		   "compiler sees, and a fact anchored in one cannot fail.";

	EXPECT_NE( legacyH.find( "C_LocalTempEntity\t\t*PoolCreateObject();" ), std::string::npos )
		<< "the header's live SOURCESHARP arm no longer publishes PoolCreateObject, "
		   "so the guard the control above counted is guarding something else.";

	// The pool file is the ONLY place the SourceSharp bookkeeping lives. A
	// second slot table would be the state this whole design avoids.
	const std::string pool = ReadSquashed( kPoolCpp );
	EXPECT_EQ( CountOf( pool, "staticC_LocalTempEntity*g_pTempEntSlots" ), 1 )
		<< "there is exactly one slot table.";
}

//-----------------------------------------------------------------------------
// (3) THE TWO REFUSALS ARE REFUSALS AND NOT SECOND IMPLEMENTATIONS.
//-----------------------------------------------------------------------------
TEST( TempEntPoolSeam, TheEvictionAndTheAttachmentKillAreRefusedNotReimplemented )
{
	const std::string legacy = ReadSquashed( kLegacyCpp );

	ASSERT_FALSE( legacy.empty() );

	// KillAttachedTents has no caller in this build; the thunk says so ONCE and
	// then goes quiet -- silence would look like the rule still running.
	EXPECT_NE( legacy.find( "voidCTempEnts::KillAttachedTents(int){SS_TEMPENTS_NO_CALLER(\"KillAttachedTents\");}" ),
	           std::string::npos )
		<< "KillAttachedTents is no longer the announced no-caller stub. A refusal "
		   "nobody can see is indistinguishable from a rule that still runs.";

	EXPECT_EQ( 1, CountOf( legacy, "s_bSaid=true;" ) )
		<< "the once-only announcement is no longer the one macro every no-caller "
		   "stub shares.";

	// And none of them grew a native walk of the slot table. The managed
	// pool is the only thing that decides which tempent to give up and which
	// attachments to kill.
	EXPECT_EQ( legacy.find( "SourceSharpTePool_KillAttached" ), std::string::npos )
		<< "a native KillAttachedTents has appeared. That rule is "
		   "ClientTempEntPool.KillAttachedTents's, and two walks of one list that agree "
		   "today are free to disagree later -- the symptom would be a spray that "
		   "outlives the player it is stuck to.";
}

// The three members the seam needed from CTempEnts, and the reason each one is
// there rather than being reachable already: m_TempEntsPool and
// AddVisibleTempEntity are private.
TEST( TempEntPoolSeam, TheHeaderExposesExactlyTheTwoMembersTheSeamNeeds )
{
	const std::string hdr  = ReadSquashed( kLegacyH );
	const std::string pool = ReadSquashed( kPoolCpp );

	ASSERT_FALSE( hdr.empty() );
	ASSERT_FALSE( pool.empty() );

	static const char *kMembers[] =
	{
		"voidPoolFreeObject(C_LocalTempEntity*pTemp);",
		"intPoolAddVisible(C_LocalTempEntity*pTemp);",
	};

	for ( size_t i = 0; i < sizeof( kMembers ) / sizeof( kMembers[ 0 ] ); i++ )
	{
		EXPECT_NE( hdr.find( kMembers[ i ] ), std::string::npos )
			<< "c_te_legacytempents.h declares " << kMembers[ i ];
	}

	// They are published UNCONDITIONALLY now. The `#ifdef SOURCESHARP` that
	// used to wrap them was collapsed by the-close 2/N -- every project that
	// reaches this header defines SOURCESHARP, so its arm was the one being
	// compiled -- and what the guard BOUGHT is what is asserted instead: the
	// three members, public, and nothing else between the `public:` and the
	// `private:` that closes the block. A fourth member added here would be a
	// wider hole in a stock class than the seam asked for, and this needle
	// catches it in the same breath as the three.
	EXPECT_NE( hdr.find( "public:"
	                     "voidPoolFreeObject(C_LocalTempEntity*pTemp);"
	                     "intPoolAddVisible(C_LocalTempEntity*pTemp);"
	                     "C_LocalTempEntity*PoolCreateObject();"
	                     "private:" ), std::string::npos )
		<< "the seam's public block on CTempEnts is no longer exactly "
		   "PoolFreeObject, PoolAddVisible and PoolCreateObject. They must be "
		   "public because the slot table in shell_te_pool_client.cpp cannot "
		   "reach m_TempEntsPool or AddVisibleTempEntity, which are private.";
	EXPECT_EQ( hdr.find( "#ifdefSOURCESHARP" ), std::string::npos )
		<< "the members have been put back behind #ifdef SOURCESHARP. That "
		   "macro is defined by every project that reaches this header, so the "
		   "guard is decoration and its #else is source no compiler sees.";

	// AND NO MORE THAN TWO. A third accessor added here and not used is a wider
	// hole in a stock class than the seam needs -- an earlier draft of this lane
	// carried a PoolAllocObject with no caller, and removing it is what this
	// assertion is for. The managed-originated create path (survey s6, step 1)
	// will want one back; adding it WITH its caller is the difference.
	EXPECT_EQ( hdr.find( "PoolAllocObject" ), std::string::npos )
		<< "an unused third pool accessor is back on CTempEnts.";
}

}  // namespace
