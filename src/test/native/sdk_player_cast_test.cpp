//========= SourceSharp — the shape that flips from safe to wrong ============//
//
// THE BUG THIS FILE IS ABOUT IS A SHAPE, NOT A VALUE, so this test reads source
// rather than running any.
//
//     CBasePlayer *p = ( CBasePlayer * )CBaseEntity::Instance( pEdict );
//     if ( p )
//         p->SomethingVirtual();
//
// Both CBaseEntity::Instance() and GetContainingEntity() are
// edict->GetUnknown()->GetBaseEntity(), and CStandaloneEntity::GetBaseEntity()
// returns NULL -- "that single NULL is what the whole parallel hierarchy rests
// on" (shell_standalone.h). So the cast is a cast of NULL, the `if` fails, and
// the site is harmless. docs/divergences.md found FOURTEEN of these, and
// thirteen were NULL-checked exactly like the above.
//
// PHASE 13 STEP 5 MAKES GetBaseEntity() RETURN `this`. Every one of those casts
// then yields a live pointer, every one of those `if`s then PASSES, and the
// call lands on the wrong vtable -- caused by a commit in a different file that
// nobody would think to review against these.
//
//=============================================================================//
// WHAT THE DRY RUN TAUGHT THIS FILE, AND WHY IT IS THREE BANS NOW
//=============================================================================//
//
// A step-5 SIMULATION (`ss_step5_sim`, retired at plan_entity.md step 4g --
// see CStandaloneEntity::GetBaseEntity for why) was armed on a live map and the
// process died in seconds, THREE TIMES, at three sites none of which this file
// could see. THE BANS BELOW OUTLIVE THE INSTRUMENT THAT FOUND THEM: what they
// forbid is asking the partition as `GetBaseEntity() == NULL`, and §3.3 makes
// that the wrong question whatever GetBaseEntity happens to answer.
//
//   1. gameinterface.cpp ProcessUsercmds -- the same cast-plus-NULL-check
//      shape, SPLIT OVER FOUR LINES. The old predicate needed the cast and the
//      Instance() call on ONE line, so a two-statement spelling of the exact
//      banned thing walked straight past it.
//
//          CBaseEntity *pEnt = CBaseEntity::Instance( player );
//          if ( pEnt && pEnt->IsPlayer() )                        <-- crash
//              pPlayer = static_cast< CBasePlayer * >( pEnt );
//
//   2. gameinterface.cpp MarkEntitiesAsTouching -- NOT A CAST AT ALL. Two
//      resolved pointers and `if ( entity && entityTouched )` choosing between
//      the SDK arm and the standalone arm. At step 5 both come back non-NULL,
//      the SDK arm runs on wrong-vtable pointers and the standalone arm below
//      becomes unreachable. No cast-scanner in any form could have matched it.
//
//   3. shell_standalone_player.cpp RunOneCommand, downstream through
//      gamemovement's traces.
//
// So the real hazard class is NOT "a cast". It is A NULL CHECK ON A POINTER
// THAT CAME FROM A GetBaseEntity()-DERIVED RESOLVER -- 48 server sites in 20
// files and 38 client, enumerated in docs/typedef-dryrun/nullflip_verbose.txt
// by the predicate this file now reimplements.
//
// THREE BANS, in order of how much they see:
//
//   A. the original one-line cast (kept verbatim; it is cheap and it documents)
//   B. the TWO-STATEMENT cast -- a resolver bound on one line and a player cast
//      of that variable within six -- which is casualty 1
//   C. the class itself -- a NULL test within six lines on a pointer bound from
//      a resolver THAT DOES NOT ASK THE PARTITION -- which is casualty 2 and
//      everything like it
//
// WHAT MAKES BAN C SATISFIABLE. The fix is not 86 call-site edits; it is the
// five resolvers those 86 sites all run through (GetContainingEntity,
// CBaseEntity::Instance, gEntList.GetBaseEntity, EntityFromEntityHandle, and
// the raw crossing in handle_cross.h) asking the partition the way that
// survives step 5 -- GetGameEntity() != NULL, never GetBaseEntity() == NULL.
// A fix at the resolver cannot miss a call site, which matters because the
// enumerated list was demonstrably incomplete: three of the three crashes were
// on nobody's list. So Ban C fires only on a RAW GetBaseEntity() -- the
// no-argument member call that bypasses those five.
//
// AND WHAT KEEPS BAN C FROM BEING VACUOUS. "Raw calls: zero" would be a proud
// report from a predicate that matched nothing at all -- this project has now
// found twenty-two checks that could not fail. So TheBanCPredicateSeesTheClass
// below runs the SAME predicate with the exemption removed, i.e. treating every
// resolver as raw, and requires it to find at least as many server sites as the
// dry run enumerated by hand. Ban C's zero means something only because that
// number is large.
//
//=============================================================================//
// SCOPE, stated rather than implied. game/server and game/shared for the server
// pass; game/client, game/client/replay and game/shared for the client. Top
// level otherwise -- the per-mod subdirectories (tf/, hl2/, episodic/,
// portal/, ...) are not compiled into this mod. Regions the preprocessor drops
// in a SOURCESHARP build are skipped, so the sanctioned cast in the #else arm
// of UTIL_SDKPlayerFromEdict does not count against it, and neither does stock
// code parked under #ifndef SOURCESHARP. Whole-line comments are skipped, which
// is what lets util.h and this file quote the banned forms in prose.
//
// THE CLIENT IS A BAN NOW, AND IT WAS A RATCHET.
//
// It was a ratchet because IClientUnknown had no GetGameEntity(): the partition
// could only be asked on the server, where it lives on IServerEntity, so the
// client's eighteen raw sites were counted and pinned rather than fixed, and
// the constant said out loud that it could fall and must not rise.
//
// Phase 13's revised step 1 landed the client's half -- on IClientEntity, since
// appending to IClientUnknown moves five engine-called vtable slots -- and gave
// the client two resolvers to funnel through, because unlike the server it had
// none to fix at. All eighteen are gone, and so are the raw sites that had no
// NULL check at all and were therefore invisible to this scanner. The count is
// zero and the fixture below now demands zero.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every ban it is asked to enforce. So the fixtures below assert a floor on the
// files opened, a floor on the lines read, that the eviction list was parsed
// and is load-bearing, that the files of the original inventory were among them
// or are accounted for where they went, that each predicate still recognises
// its own shape, and -- the one that matters most -- that Ban C's predicate
// finds the dry run's class when its exemption is lifted.
//
// AND EVERY ONE OF THOSE FIVE WAS RE-CALIBRATED AT THE FLIP, on the commit that
// put SS_CBASEENTITY_ALIAS into both .vpc files and took 35 stock `$File` rows
// out with it. Each of them had been counted or named against the pre-flip file
// set, so each went red on a tree where nothing was wrong: the floors because
// half the scanned source left the build, the inventory and the eviction rows
// because the files they named by hand are among the ~38. They are RE-STATED
// below, each with what the flip changed, what the number is now, and why it
// still catches what it was written to catch -- and the two floors that had
// been revised once per wave of progress were made RELATIVE while they were
// open, so that the next eviction moves neither.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"
#include <set>

#include <dirent.h>
#include <stdio.h>
#include <string.h>

#include <algorithm>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Squeezing, and what counts as prose.
//
// Whitespace is squeezed out first so that "( CBasePlayer * )", "(CBasePlayer*)"
// and "static_cast<CBasePlayer*>" all read the same. Every predicate below
// works on the squeezed form, which is why none of them needs to care about
// how a line was wrapped.
//-----------------------------------------------------------------------------

std::string Squeezed( const std::string &line )
{
	std::string out;
	out.reserve( line.size() );
	for ( size_t i = 0; i < line.size(); i++ )
	{
		const char c = line[i];
		if ( c != ' ' && c != '\t' && c != '\r' )
			out += c;
	}
	return out;
}

bool IsWholeLineComment( const std::string &line )
{
	size_t i = 0;
	while ( i < line.size() && ( line[i] == ' ' || line[i] == '\t' ) )
		i++;

	if ( i >= line.size() )
		return true;

	// "//", "/*" and the " * " continuation of a block comment. Enough for the
	// way this codebase writes prose, and deliberately not a C++ lexer: a
	// scanner nobody can predict the behaviour of is a scanner nobody trusts
	// when it goes red.
	if ( line.compare( i, 2, "//" ) == 0 )
		return true;
	if ( line.compare( i, 2, "/*" ) == 0 )
		return true;
	if ( line[i] == '*' )
		return true;

	return false;
}

//-----------------------------------------------------------------------------
// BAN A -- the original one-line shape, unchanged.
//
// The line must contain BOTH a cast to a player-ish SDK type AND one of the two
// ways of asking an edict for its entity. Either alone is fine: casting is how
// the SDK is written, and Instance()/GetContainingEntity() answering a
// CBaseEntity * is their job. It is the pair on one line that is the hazard.
//-----------------------------------------------------------------------------

const char *kPlayerCasts[] = {
	"(CBasePlayer*)",
	"<CBasePlayer*>",
	"(CBaseCombatCharacter*)",
	"<CBaseCombatCharacter*>",
	"(CBaseMultiplayerPlayer*)",
	"<CBaseMultiplayerPlayer*>",
	"(C_BasePlayer*)",
	"<C_BasePlayer*>",
};
const size_t kNumPlayerCasts = sizeof( kPlayerCasts ) / sizeof( kPlayerCasts[0] );

bool HasPlayerCast( const std::string &squeezed )
{
	for ( size_t i = 0; i < kNumPlayerCasts; i++ )
	{
		if ( squeezed.find( kPlayerCasts[i] ) != std::string::npos )
			return true;
	}
	return false;
}

bool LineHasBannedCast( const std::string &line )
{
	if ( IsWholeLineComment( line ) )
		return false;

	const std::string s = Squeezed( line );

	if ( !HasPlayerCast( s ) )
		return false;

	// The first three are deleted from the tree and kept for the reason
	// kPartitionedResolvers gives: a name that is gone is free to ban, and the
	// ban is what makes typing it back visible. The fourth is the live
	// successor -- a player cast off GameEntityFromEntityHandle is exactly as
	// wrong as one off the three that went, and it is the one that can happen.
	return s.find( "CBaseEntity::Instance(" ) != std::string::npos ||
		s.find( "C_BaseEntity::Instance(" ) != std::string::npos ||
		s.find( "GetContainingEntity(" ) != std::string::npos ||
		s.find( "GameEntityFromEntityHandle(" ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// The resolvers, and which of them ask the partition.
//
// PARTITIONED -- these five reach GetBaseEntity() through
// SourceSharp_SDKEntityFromUnknown, which asks GetGameEntity() first and hands
// back NULL for anything on the parallel hierarchy whatever step 5 does to
// GetBaseEntity(). A pointer from one of these is safe to NULL-check.
//
// RAW -- a bare `X->GetBaseEntity()` with NO ARGUMENTS. That is the member call
// on IServerUnknown/IClientUnknown that the five wrap, and calling it directly
// is the bypass. The empty parentheses are what distinguish it from
// `gEntList.GetBaseEntity( hEnt )` and `cl_entitylist->GetBaseEntity( i )`,
// which are two of the five and always take one.
//
// TheResolverListIsHonest below reads each of the five out of the tree and
// requires it to name the partition, so this classification cannot rot into a
// list of names that no longer do what it says they do.
//-----------------------------------------------------------------------------

enum ResolverKind_t
{
	RESOLVER_NONE = 0,
	RESOLVER_PARTITIONED, // goes through the five; its NULL is trustworthy
	RESOLVER_RAW, // X->GetBaseEntity(), the bypass
};

//-----------------------------------------------------------------------------
// One line, one binding. `<ident> = <resolver>( ... )`.
//
// Only the FIRST assignment on the line is considered, which is all any of the
// enumerated sites needs and keeps the predicate something a reader can hold in
// their head.
//-----------------------------------------------------------------------------
struct Binding_t
{
	Binding_t()
		: kind( RESOLVER_NONE )
	{
	}

	std::string strVar;
	ResolverKind_t kind;
};

//-----------------------------------------------------------------------------
// A NULL test on a named variable, within the window.
//
// "if ( pEnt )", "if ( !pEnt )", "if ( pEnt && ... )", "while ( ent )",
// "if ( !pEnt || ... )" and "Assert( pEnt )" all count -- and so does
// "if ( !pEntity->BlocksLOS() )", which is a site with NO check at all and is
// the worse half of the same class. What they share is that the pointer's value
// decides what happens next, which is exactly what step 5 changes.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Whether a line is compiled in a SOURCESHARP build.
//
// Only the SOURCESHARP conditionals are evaluated; every other #if is treated
// as live. Being wrong in that direction costs a false positive somebody has to
// look at, which is the right way round for a ban.
//-----------------------------------------------------------------------------

class CGuardStack
{
public:
	CGuardStack() {}

	// True when the line just past the directives seen so far is compiled.
	bool Live() const
	{
		for ( size_t i = 0; i < m_Stack.size(); i++ )
		{
			if ( !m_Stack[i] )
				return false;
		}
		return true;
	}

	void Directive( const std::string &line )
	{
		const std::string s = Squeezed( line );
		if ( s.empty() || s[0] != '#' )
			return;

		// Longest keyword first: "ifndef" would otherwise be read as "if".
		if ( Starts( s, "#ifndef" ) )
		{
			const bool bOurs = Starts( s, "#ifndefSOURCESHARP" );
			Push( /*bLive=*/!bOurs, bOurs );
			return;
		}
		if ( Starts( s, "#ifdef" ) )
		{
			const bool bOurs = Starts( s, "#ifdefSOURCESHARP" );
			Push( /*bLive=*/true, bOurs );
			return;
		}
		if ( Starts( s, "#if" ) )
		{
			// #if defined(SOURCESHARP) / #if !defined(SOURCESHARP), and every
			// other #if, which is treated as live.
			const bool bOursDefined = ( s.find( "#ifdefined(SOURCESHARP)" ) == 0 );
			const bool bOursNotDefined = ( s.find( "#if!defined(SOURCESHARP)" ) == 0 );
			Push( /*bLive=*/!bOursNotDefined, bOursDefined || bOursNotDefined );
			return;
		}
		if ( Starts( s, "#elif" ) )
		{
			// An #elif arm of a SOURCESHARP conditional is not this build's.
			if ( !m_Stack.empty() && m_IsSourceSharp.back() )
				m_Stack.back() = false;
			return;
		}
		if ( Starts( s, "#else" ) )
		{
			if ( !m_Stack.empty() && m_IsSourceSharp.back() )
				m_Stack.back() = !m_Stack.back();
			return;
		}
		if ( Starts( s, "#endif" ) )
		{
			if ( !m_Stack.empty() )
			{
				m_Stack.pop_back();
				m_IsSourceSharp.pop_back();
			}
			return;
		}
	}

private:
	static bool Starts( const std::string &s, const char *pszPrefix )
	{
		return s.compare( 0, strlen( pszPrefix ), pszPrefix ) == 0;
	}

	void Push( bool bLive, bool bIsSourceSharp )
	{
		m_Stack.push_back( bLive );
		m_IsSourceSharp.push_back( bIsSourceSharp );
	}

	std::vector<bool> m_Stack;
	std::vector<bool> m_IsSourceSharp;
};

//-----------------------------------------------------------------------------
// The escape hatch, and the only one.
//
// CServerNetworkProperty has a member called GetBaseEntity() that has nothing
// to do with the partition: it returns m_pOuter, the entity that OWNS the
// property, and a standalone entity has no CServerNetworkProperty at all so it
// can never see one. A name collision, not a hazard.
//
// Marked rather than pattern-matched, because "is this receiver an
// IServerUnknown?" is a type question and this is a line scanner. The marker is
// counted and its files are named by TheExemptionsAreWhereTheySay, so it cannot
// be sprinkled around to silence a real finding without that fixture noticing.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The scan.
//-----------------------------------------------------------------------------

struct Hit_t
{
	std::string strFile;
	int nLine;
	std::string strText;
	int nFlagLine;
	std::string strFlagText;
};

struct ScanResult_t
{
	ScanResult_t()
		: nFiles( 0 )
		, nLines( 0 )
		, nSkipped( 0 )
		, nHere( 0 )
		, nEvictions( 0 )
		, nExemptions( 0 )
	{
	}

	int nFiles;
	int nLines;
	int nSkipped; // files present on disk but evicted from this .so
	int nHere; // of those, the ones naming a file this scan WALKS --
	// the honest denominator for the coverage ratio, since
	// an entry under sourcesharp/ was never going to be read
	int nEvictions; // entries in the target's compiled-source list (the
	// name is from the removal-list era) -- see CompiledPaths
	int nExemptions; // SS_NOT_THE_PARTITION markers honoured

	std::vector<Hit_t> CastHits; // ban A
	std::vector<Hit_t> TwoStatementHits; // ban B
	std::vector<Hit_t> RawNullCheckHits; // ban C
	std::vector<Hit_t> EveryResolverHits; // ban C with the exemption lifted

	std::vector<std::string> FilesRead;
	std::vector<std::string> ExemptionFiles;
};

//-----------------------------------------------------------------------------
// Which files this mod actually compiles.
//
// The tree carries far more source than the mod builds, and a banned shape
// inside a file the compiler never sees is not a hazard. On the first run this
// mattered immediately -- the scan reported three sites, and two of them
// (sceneentity.cpp, teamplay_gamerules.cpp) were evicted files. The third,
// baseentity.cpp, was live and real, and was not in the hand-taken inventory
// of fourteen.
//
// Read from the build rather than listed here, so that a file returning to the
// build brings its sites back under the ban on the same commit, with nobody
// having to remember. Since Phase 6b that is the target's compiled sources --
// server_sourcesharp / client_sourcesharp, read through cmake_graph.h from
// compile_commands.json and cross-checked against the target's CMakeLists; it
// throws on a missing, empty or stale input.
//
// BOTH PROJECTS ANSWER THIS THE SAME WAY NOW, and it is the better of the two
// answers they used to give.
//
// This block used to say that the client "still includes client_base.vpc and
// subtracts from it, so what it does NOT compile is what its `-$File` lines
// name", and that the flattened server answered it the other way round. Both
// are flattened now -- plan_vpc.md and plan_vpc_client.md -- and neither has a
// `-$File` at all. Each lists the files it compiles (145 and 287 translation
// units) and nothing else, so the question is A FILE IS NOT IN THE BUILD
// UNLESS A `$File` NAMES IT, on both sides.
//
// That is strictly better than the subtractive form and the reason is worth
// keeping: a removal list can only ever name files somebody thought to write
// down, so every file that was never in the project at all read as PRESENT.
// The additive list is exact by construction. On the client that turned 229
// `-$File` basenames into 288 `$File` ones and brought several hundred files
// that were never in the build under the ban for the first time.
//
// FULL PATHS, AND THEY USED TO BE BASENAMES. The membership question is still
// answered on the basename -- see IsEvicted, whose behaviour has not changed --
// but the DIRECTORY is what says whether this scan could ever have read an
// entry at all, and that is the other question the same parse now answers.
//
// WHY THE SECOND QUESTION EXISTS. The ratio arm in TheScannerFoundTheSourceTree
// holds the files read against the entries listed, and it went red on a commit
// that ADDED a translation unit -- because the entry landed in the denominator
// (every $File is counted) and never in the numerator (this scan walks
// game/server and game/shared, and the file was under sourcesharp/game/shared).
// That is not a coincidence about one file: this port's whole direction is
// moving code INTO sourcesharp/, so the denominator grows on exactly the
// commits that represent progress and the ratio falls on every one of them.
// It is the same disease the absolute floor had, one level up -- a number that
// has to be revised every time the port succeeds is not measuring what it was
// written to measure -- so the denominator is now the entries the scan can
// reach rather than all of them.
//
// The paths themselves are farm-relative ("game/server/x.cpp", once
// "$SRCDIR\game\server\x.cpp" in the vpc), and two files with the same
// basename in game/server and game/shared do not exist.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The basename out of one listed path. Backslashes AND forward slashes (the
// vpc era wrote the first; the CMake lists write the second).
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// "Is this file outside the build?"
//
// ABSENCE from the compiled list, on both sides -- see CompiledPaths above.
// The function's name is from the era when the client answered it the other
// way round; what it means has not changed, and neither has what it compares:
// the basename, taken from the stored path at the point of comparison rather
// than at the point of parsing.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// How many listed entries name a file THIS SCAN WALKS -- the honest denominator
// for the coverage ratio.
//
// ScanDirectory is pointed at game/server and game/shared (and game/client and
// game/client/replay on the other side); an entry under sourcesharp/, public/
// or common/ is one the scan was never going to read, so counting it as a miss
// says the scan lost coverage when nothing of the sort happened.
//
// The prefix test is on "game/" at the START of the farm-relative path, and it
// is not the same as asking whether the path CONTAINS "/game/": every
// sourcesharp entry contains it, because they are spelled
// "sourcesharp/game/shared/x.cpp". That near-miss is the whole reason this is
// a function with a comment on it.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// plan_entity.md step 4e moved the shared trace-filter family out of
// game/shared/util_shared.cpp and into the shim, and SEVEN of this scan's
// server resolver sites went with it -- the walk covers src/game/server and
// src/game/shared, so an eviction into sourcesharp/ takes its sites out of
// reach exactly as saverestore.cpp's did.
//
// SO THE FILE IS SCANNED BY NAME rather than the floor being lowered. Lowering
// it would have been the wrong repair twice over: the sites did not go away,
// and a floor that follows the measurement down stops being a floor. Widening
// the walk to the whole of sourcesharp/game/shared would be a different change
// with a different owner -- this scan's coverage of the shim is a decision
// recorded at TheInventoryOfScannedFiles, and one file the ban already covered
// staying covered after it moved is not that decision.
//
// Both sides name it, because both link lines compile it.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The Phase 4 exit's cluster-E lane (p4-util) did the same thing three more
// times, and it took the SERVER count 25 -> 22 before these lines were added:
//
//   * UTIL_ImpactTrace's `CBaseEntity *pEntity = TraceEntity( *pTrace );`
//     followed by `if ( !pEntity )` left game/server/util.cpp for
//     server_util_library.cpp, along with the two GetContainingEntity sites
//     inside the check-client subsystem;
//   * UTIL_DecalTrace's identical pair left game/shared/util_shared.cpp for
//     util_shared_library.cpp;
//   * ApplyMultiDamage's entity-list lookup left game/shared/takedamageinfo.cpp
//     for takedamageinfo_library.cpp.
//
// Same repair, for the same two reasons 4e gave: the sites did not go away, and
// a floor that follows the measurement down stops being a floor. This is NOT
// the decision to widen the walk to all of sourcesharp/game -- it is the
// existing coverage of three files following them to their new addresses.
//
// server_util_library.cpp is SERVER-ONLY, because game/server/util.cpp is; the
// other two are on both link lines and both scans name them.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Did a predicate find anything IN THIS FILE?
//
// The counterpart of ScannedFileNamed, and it exists for the re-key in
// TheBanCPredicateSeesTheClass: that fact used to prove the predicate still
// matched by counting its hits, and the flip took the count below its floor by
// evicting the files the hits were in. A count cannot tell "the predicate
// broke" from "a file left the build"; a NAMED FILE can, because an eviction
// reds the row for the file it evicted and leaves the others green.
//
// Matched on a suffix for the same reason ScannedFileNamed is: the recorded
// path is whatever ScanDirectory or ScanFile was handed, which carries a
// "game/../sourcesharp/..." spelling for the files scanned by name.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// FIRST: the scanner works. Every assertion below this point is worthless
// without these, because a scanner that reads nothing reports no violations.
//-----------------------------------------------------------------------------

TEST( SdkPlayerCast, TheScannerStillRecognisesTheOneLineShape )
{
	// The mutation check, written down rather than performed by hand once. If
	// the predicate is ever loosened into uselessness, this is what says so.
	EXPECT_TRUE( LineHasBannedCast( "\tCBasePlayer *p = ( CBasePlayer * )CBaseEntity::Instance( pEdict );" ) );
	EXPECT_TRUE( LineHasBannedCast( "\tCBasePlayer *p = (CBasePlayer*)GetContainingEntity( pEdict );" ) );
	EXPECT_TRUE(
		LineHasBannedCast( "\tCBasePlayer *p = static_cast<CBasePlayer*>( GetContainingEntity( pEdict ) );" ) );

	// And the things that must NOT trip it, or the ban is unusable.
	EXPECT_FALSE( LineHasBannedCast( "\t// CBasePlayer *p = ( CBasePlayer * )CBaseEntity::Instance( pEdict );" ) );
	EXPECT_FALSE( LineHasBannedCast( "//     ( CBasePlayer * )CBaseEntity::Instance( pEdict )" ) );
	EXPECT_FALSE( LineHasBannedCast( "\tCBaseEntity *p = CBaseEntity::Instance( pEdict );" ) );
	EXPECT_FALSE( LineHasBannedCast( "\tCBasePlayer *p = ToBasePlayer( pEntity );" ) );
	EXPECT_FALSE( LineHasBannedCast( "\tCBasePlayer *p = UTIL_SDKPlayerFromEdict( pEdict );" ) );
}

TEST( SdkPlayerCast, TheGuardStackDropsWhatThisBuildDoesNotCompile )
{
	CGuardStack guards;
	EXPECT_TRUE( guards.Live() );

	guards.Directive( "#ifndef SOURCESHARP" );
	EXPECT_FALSE( guards.Live() ) << "#ifndef SOURCESHARP is not compiled here";

	guards.Directive( "#endif" );
	EXPECT_TRUE( guards.Live() );

	// The sanctioned cast in UTIL_SDKPlayerFromEdict lives in exactly this arm.
	guards.Directive( "#ifdef SOURCESHARP" );
	EXPECT_TRUE( guards.Live() );
	guards.Directive( "#else" );
	EXPECT_FALSE( guards.Live() ) << "the #else of #ifdef SOURCESHARP is stock's arm";
	guards.Directive( "#endif" );
	EXPECT_TRUE( guards.Live() );

	// A guard this scanner does not evaluate stays live, so its contents are
	// still reported. Wrong in the direction of a false positive, on purpose.
	guards.Directive( "#ifdef TF_DLL" );
	EXPECT_TRUE( guards.Live() );
	guards.Directive( "#endif" );
	EXPECT_TRUE( guards.Live() );
}

//-----------------------------------------------------------------------------
// THE ONE THAT KEEPS BAN C HONEST.
//
// Ban C reports zero. So would a predicate that matched nothing whatsoever, and
// this project has now found twenty-two checks that could not fail -- six of
// them the very gates that were supposed to guard this step. So the SAME
// predicate is run with its exemption lifted, treating every resolver as raw,
// and required to find the class the dry run enumerated by hand.
//
// If Ban C's zero is ever produced by a broken predicate rather than by a fixed
// tree, this is what says so.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THEN: the bans themselves.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// AND THE CLIENT, WHICH IS A BAN.
//
// This was a RATCHET on eighteen counted sites, and the constant it pinned was
// the twenty-third check on this project that could not fail in the direction
// anybody wanted: IClientUnknown had no GetGameEntity(), so there was nothing
// for a client resolver to ask and no edit that could have taken the number to
// zero. It said so in its own fixture name.
//
// The eighteen, for the record, since a deleted list is a deleted argument:
//   alphamaterialproxy.cpp:50            IClientEntity      *pEnt
//   animatedentitytextureproxy.cpp:32,46 pRend->GetIClientUnknown()
//   c_sprite.cpp:393                     IClientEntity      *attachedto
//   cdll_util.cpp:669                    ternary on pClientEntity
//   cliententitylist.cpp:337,385,418,446 IClientUnknown     *pUnknown / pUnk
//   clientleafsystem.cpp:347,407,1133    pUnk / pRenderable->GetIClientUnknown()
//   clientshadowmgr.cpp:3492,3617        pRenderable->GetIClientUnknown()
//   particles_localspace.cpp:142         pRenderable->GetIClientUnknown()
//   proxypupil.cpp:76                    ternary on pEntity
//   studio_stats.cpp:118                 g_pStudioStatsEntity->GetIClientUnknown()
//   viewrender.cpp:4169                  ternary + chain -- THE ONE THAT KILLED
//                                        THE PROCESS under the step-5 canary
//
// Eleven of them were the CHAINED or TERNARY spellings, which the dry run's
// census could not match at all. All eighteen now ask SDKEntityFromUnknown or
// SDKEntityFromRenderable, and so do nine more that had NO NULL CHECK AT ALL
// and were therefore invisible to this scanner in either direction.
//-----------------------------------------------------------------------------

} // namespace
