//====== SourceSharp — the named list, and the invariant it exists to hold =====//
//
// plan_gamedll.md Part 3a and Part 9a, Phase 7.
//
// THE PHASE'S REAL RISK IS NOT A CRASH. It is that a participant ends up driven
// TWICE per frame, or driven by NOBODY -- and the second of those is silent by
// construction: a CAutoGameSystem that nothing walks does not warn, does not
// fail to link, and does not run. So the facts below are about the TABLE, which
// is the thing that decides both, rather than about a running server.
//
// FIVE PROPERTIES, and each one is a way the phase could be wrong:
//
//   1. The rows are unique. A system named twice is a system inited twice.
//   2. Every INIT stage walks the rows FORWARD and every SHUTDOWN stage walks
//      them BACKWARD -- derived from the same array, not from a second list.
//      That is what makes stock's "GameStringSystem must init first + shutdown
//      last" a property rather than a comment somebody has to notice.
//   3. GameStringSystem is row zero, so 2 puts it last on the way out.
//   4. CSourceSharpSystem IS NOT A ROW -- and after Phase 7a it is not in the
//      server binary at all, because Part 3 put the class and its instance
//      behind #ifdef CLIENT_DLL. The table must still not reach it: the client
//      keeps it, this file is the server's, and a row naming it would be both
//      a double drive and a reference to a type that does not exist here.
//   5. The managed side's four server event points have exactly ONE BODY EACH.
//      Phase 7 had TWO drivers calling them -- CSourceSharpSystem in delegate
//      mode and the table's row in shell mode -- and Phase 7a removed the
//      delegate, so the table's row is now the only server caller. The bodies
//      are what is pinned, not the number of callers: two bodies is the bug,
//      and it is the kind that agrees with itself until somebody edits one.
//
// SOURCE-READ RATHER THAN LINKED, and that is forced rather than chosen:
// shell_serversystems.cpp includes cbase.h's whole world and every row's
// accessor lives in a stock translation unit. The behavioural half of this gate
// is a running server -- see docs/gamedll-ledger.md, Phase 7.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <cstring>
#include <fstream>
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

// Comments are stripped FIRST and always. Every needle below would otherwise
// match the paragraph explaining what it is for, which is this tree's running
// list of checks that cannot fail.
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

const char *kTableFile   = "sourcesharp/game/server/shell_serversystems.cpp";
const char *kGameSystem  = "sourcesharp/game/shared/sourcesharp_gamesystem.cpp";
const char *kWorldAdapter = "sourcesharp/game/server/world_adapter.cpp";
const char *kShellGameDll = "sourcesharp/game/server/shell_gamedll.cpp";

//-----------------------------------------------------------------------------
// One row of kServerSystems[]: the NAME and the ACCESSOR, in file order.
//
// Parsed rather than needled, so that the facts below are about the table as
// shipped. A row is `{ "Name", Accessor },` inside the initialiser.
//-----------------------------------------------------------------------------
struct Row
{
	std::string m_Name;
	std::string m_Accessor;
};

std::vector<Row> Rows()
{
	std::vector<Row> rows;
	const std::string body = StripComments( Read( kTableFile ) );

	const size_t start = body.find( "kServerSystems[] =" );
	EXPECT_NE( start, std::string::npos ) << "kServerSystems[] is not in " << kTableFile;
	if ( start == std::string::npos )
		return rows;

	const size_t open = body.find( '{', start );
	size_t depth = 0;
	size_t end = std::string::npos;
	for ( size_t i = open; i < body.size(); i++ )
	{
		if ( body[ i ] == '{' ) depth++;
		else if ( body[ i ] == '}' ) { depth--; if ( depth == 0 ) { end = i; break; } }
	}
	EXPECT_NE( end, std::string::npos ) << "kServerSystems[]'s initialiser does not close";
	if ( end == std::string::npos )
		return rows;

	size_t i = open + 1;
	while ( i < end )
	{
		const size_t q1 = body.find( '"', i );
		if ( q1 == std::string::npos || q1 > end ) break;
		const size_t q2 = body.find( '"', q1 + 1 );
		if ( q2 == std::string::npos || q2 > end ) break;

		const size_t comma = body.find( ',', q2 );
		size_t close = body.find( '}', q2 );
		if ( comma == std::string::npos || close == std::string::npos ) break;

		std::string accessor = body.substr( comma + 1, close - comma - 1 );
		// trim
		while ( !accessor.empty() && isspace( (unsigned char)accessor.front() ) ) accessor.erase( accessor.begin() );
		while ( !accessor.empty() && isspace( (unsigned char)accessor.back() ) )  accessor.pop_back();

		Row row;
		row.m_Name = body.substr( q1 + 1, q2 - q1 - 1 );
		row.m_Accessor = accessor;
		rows.push_back( row );

		i = close + 1;
	}

	return rows;
}

// The body of one function in a file, brace-matched from its signature.
std::string BodyOf( const std::string &file, const char *pszSignature )
{
	const size_t at = file.find( pszSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = file.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	size_t depth = 0;
	for ( size_t i = open; i < file.size(); i++ )
	{
		if ( file[ i ] == '{' ) depth++;
		else if ( file[ i ] == '}' ) { depth--; if ( depth == 0 ) return file.substr( open, i - open + 1 ); }
	}
	return std::string();
}

int CountOf( const std::string &haystack, const std::string &needle )
{
	int n = 0;
	for ( size_t at = haystack.find( needle ); at != std::string::npos;
	      at = haystack.find( needle, at + needle.size() ) )
	{
		n++;
	}
	return n;
}

//=============================================================================
// 1. THE ROWS THEMSELVES
//=============================================================================

TEST( ServerSystemsTable, TheTableHasRowsAtAll )
{
	// Without this every fact below passes vacuously on an empty parse, which
	// is the failure mode a source-read test has that a linked one does not.
	//
	// IT USED TO BE `EXPECT_GE( rows, 15 )` AND THAT WAS STALE BY CONSTRUCTION.
	// Fifteen was the population the day A3's second pair landed, and this plan
	// is a sequence of phases that REMOVE rows -- so the floor went red for the
	// fifth system doing exactly what the phase was for, and the repair would
	// have been to type a smaller number and arrange the same failure for the
	// next one. That is this tree's recorded "a needle keyed on the ABI
	// version" defect wearing a different hat, and it is the third time a count
	// keyed on today's population has had to be replaced here.
	//
	// A VACUITY GUARD SHOULD SAY THE PARSE WORKED, NOT HOW BIG THE TABLE IS.
	// So: the row parser's answer is compared against an INDEPENDENT count of
	// the row lines in the same file, and the one row that cannot leave the
	// table is named. That is strictly stronger than a floor -- a parser that
	// silently drops rows goes red at any population, which is precisely what
	// a floor of fifteen could not see while the table had sixteen.
	const std::string file = StripComments( Read( kTableFile ) );
	ASSERT_FALSE( file.empty() );

	const std::vector<Row> rows = Rows();

	// Every row line, counted by its shape rather than by its contents. The
	// leading tab and the opening brace-quote are what keep this off the
	// rationale block below the table, where the non-rows are named in prose.
	const int nLines = CountOf( file, "\n\t{ \"" );

	EXPECT_EQ( (int)rows.size(), nLines )
		<< "the row parser and a plain count of the row lines disagree, so some "
		   "row is being read as something else -- and every fact below is then "
		   "about a table that is not the one the build walks";

	EXPECT_GT( (int)rows.size(), 0 )
		<< "kServerSystems[] parsed to no rows at all";

	bool bFoundManaged = false;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[ i ].m_Name == "SourceSharpManaged" )
			bFoundManaged = true;
	}

	EXPECT_TRUE( bFoundManaged )
		<< "the managed row is not in the parsed table. It is the one row that "
		   "cannot leave -- without it the whole managed side stops being driven "
		   "and nothing else in this file would notice";
}

TEST( ServerSystemsTable, NoSystemIsNamedTwice )
{
	const std::vector<Row> rows = Rows();

	std::set<std::string> names;
	std::set<std::string> accessors;

	for ( size_t i = 0; i < rows.size(); i++ )
	{
		EXPECT_TRUE( names.insert( rows[ i ].m_Name ).second )
			<< "'" << rows[ i ].m_Name << "' is a row twice -- it would init twice, "
			<< "shut down twice, and run twice in every per-frame pass";
		EXPECT_TRUE( accessors.insert( rows[ i ].m_Accessor ).second )
			<< rows[ i ].m_Accessor << "() is named by two rows, so one OBJECT is "
			<< "on the list twice under two names";
	}
}

TEST( ServerSystemsTable, TheStringSystemIsRowZeroSoItShutsDownLast )
{
	const std::vector<Row> rows = Rows();
	ASSERT_FALSE( rows.empty() );

	// gameinterface.cpp:780 -- "The string system must init first + shutdown
	// last". First here, and LAST on the way out is a consequence of the
	// backwards walk rather than a second statement that could disagree.
	EXPECT_EQ( rows[ 0 ].m_Accessor, "GameStringSystem" )
		<< "GameStringSystem is not row zero; every pooled string_t is then freed "
		<< "before its users shut down";
}

TEST( ServerSystemsTable, PhysicsComesBeforeTheSoundEnvelopeManager )
{
	// STOCK'S CONSTRAINT SURVIVES ITS SUBJECT LEAVING THE TABLE, and this fact
	// is the second spelling of it.
	//
	// It used to find SoundControllerGameSystem's row and require
	// PhysicsGameSystem's to come first. plan_gamedll_after.md Phase A3's
	// second pair moved CSoundControllerImp to SourceSharp.Server, so there is
	// no such row and the ASSERT_GE on it failed -- on a tree where the
	// constraint is still met.
	//
	// WHAT THE CONSTRAINT ACTUALLY IS: CPhysicsHook::Init assigns
	// `physcollision`, and the sound envelope manager must not run before that
	// happens. The manager is a managed IFrameLifecycle participant now, driven
	// from ServerGameDll.PreClientUpdate -- which runs AFTER the whole table has
	// been walked, and therefore after CPhysicsHook::Init, whatever row it is
	// on. So the ordering is no longer a property of two positions; it is a
	// property of the table being FULLY INITIALISED before any managed frame
	// participant runs.
	//
	// Both halves are checked, because either alone is satisfiable by an
	// accident: that the physics hook is still an early row, and that nothing
	// named the sound envelope manager is a row at all -- so a future edit that
	// puts one back is caught here rather than silently re-creating the
	// two-position question this fact stopped asking.
	const std::vector<Row> rows = Rows();

	int nPhysics = -1, nSoundController = -1;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[ i ].m_Accessor == "PhysicsGameSystem" )         nPhysics = (int)i;
		if ( rows[ i ].m_Accessor == "SoundControllerGameSystem" ) nSoundController = (int)i;
	}

	ASSERT_GE( nPhysics, 0 )
		<< "CPhysicsHook is not in the table at all, so nothing assigns "
		   "physcollision and the first prop with a collision model dereferences "
		   "NULL in BuildPropList";

	EXPECT_EQ( -1, nSoundController )
		<< "the sound envelope manager is a row again. It is a managed "
		   "IFrameLifecycle participant (FrameStage.SoundController) and a row "
		   "as well would run it twice -- and the row's per-frame stage is "
		   "PreClientUpdate, so the two would fight over the same tick";

	// It is EARLY, which is the half of stock's comment that still has a
	// position to be about: physcollision has to be standing before anything
	// that spawns can ask for it, and every later row may.
	EXPECT_LT( nPhysics, 3 )
		<< "physics is row " << nPhysics << ". Stock's own comment is that it "
		   "must occur before the sound envelope manager, and what that is "
		   "really about is physcollision existing before anything reads it";
}

//=============================================================================
// 2. FORWARD FOR INIT, BACKWARD FOR SHUTDOWN -- FROM THE SAME ROWS
//=============================================================================

TEST( ServerSystemsTable, ShutdownIsInitReversedAndIsDerivedFromTheSameRows )
{
	const std::string file = StripComments( Read( kTableFile ) );

	struct Stage { const char *m_pszFn; bool m_bForward; };
	static const Stage kStages[] =
	{
		{ "SourceSharp_ServerSystemsPostInit",                              true  },
		{ "SourceSharp_ServerSystemsLevelInitPostEntity",                   true  },
		{ "SourceSharp_ServerSystemsShutdown",                              false },
		{ "SourceSharp_ServerSystemsLevelShutdownPreClearSteamAPIContext",  false },
		{ "SourceSharp_ServerSystemsLevelShutdownPreEntity",                false },
		{ "SourceSharp_ServerSystemsLevelShutdownPostEntity",               false },
	};

	for ( size_t i = 0; i < sizeof( kStages ) / sizeof( kStages[ 0 ] ); i++ )
	{
		const std::string body = BodyOf( file, kStages[ i ].m_pszFn );
		ASSERT_FALSE( body.empty() ) << kStages[ i ].m_pszFn << " has no body";

		const bool bForward  = body.find( "WalkForward(" ) != std::string::npos;
		const bool bBackward = body.find( "WalkBackward(" ) != std::string::npos;

		if ( kStages[ i ].m_bForward )
		{
			EXPECT_TRUE( bForward && !bBackward )
				<< kStages[ i ].m_pszFn << " must walk the rows FORWARD";
		}
		else
		{
			EXPECT_TRUE( bBackward && !bForward )
				<< kStages[ i ].m_pszFn << " must walk the rows BACKWARD -- a shutdown "
				<< "that runs in init order is a use-after-free of everything row zero owns";
		}
	}
}

TEST( ServerSystemsTable, BothWalkersReadTheOneArrayAndThereIsNoSecondList )
{
	const std::string file = StripComments( Read( kTableFile ) );

	const std::string fwd = BodyOf( file, "void WalkForward(" );
	const std::string bwd = BodyOf( file, "void WalkBackward(" );

	ASSERT_FALSE( fwd.empty() );
	ASSERT_FALSE( bwd.empty() );

	// The reversal is a property of the WALK, not of a second table that could
	// drift out of step with the first. Both name kServerSystems and nothing
	// else, and the file declares exactly one array.
	EXPECT_NE( fwd.find( "kServerSystems[" ), std::string::npos );
	EXPECT_NE( bwd.find( "kServerSystems[" ), std::string::npos );
	EXPECT_EQ( CountOf( file, "kServerSystems[] =" ), 1 )
		<< "a second table is a second order that can disagree with the first";

	// And the direction is what differs: forward counts up from zero, backward
	// counts down from the count.
	EXPECT_NE( fwd.find( "i = 0" ), std::string::npos );
	EXPECT_NE( bwd.find( "--i >= 0" ), std::string::npos );
}

TEST( ServerSystemsTable, TheThreePerFrameStagesAllWalkForward )
{
	const std::string file = StripComments( Read( kTableFile ) );

	static const char *kFrameStages[] =
	{
		"SourceSharp_ServerSystemsFramePreThink",
		"SourceSharp_ServerSystemsFramePostThink",
		"SourceSharp_ServerSystemsPreClientUpdate",
	};

	for ( size_t i = 0; i < sizeof( kFrameStages ) / sizeof( kFrameStages[ 0 ] ); i++ )
	{
		const std::string body = BodyOf( file, kFrameStages[ i ] );
		ASSERT_FALSE( body.empty() ) << kFrameStages[ i ] << " has no body";

		// Stock's InvokePerFrameMethod walks forwards for all three; only the
		// level and lifetime shutdowns are reversed.
		EXPECT_NE( body.find( "WalkForwardPerFrame(" ), std::string::npos )
			<< kFrameStages[ i ] << " must walk the rows forward, like stock's "
			<< "InvokePerFrameMethod";
		EXPECT_EQ( body.find( "WalkBackward" ), std::string::npos );
	}
}

//=============================================================================
// 3. NOTHING RUNS TWICE
//=============================================================================

TEST( ServerSystemsTable, TheAutoRegisteredManagedGameSystemIsNotARow )
{
	const std::vector<Row> rows = Rows();
	const std::string file = StripComments( Read( kTableFile ) );

	// Until Phase 7a CSourceSharpSystem was a CAutoGameSystemPerFrame in this
	// same binary, driving the managed frame in delegate mode while the shell's
	// row drove it in shell mode -- two rows and every managed participant ran
	// twice a tick. Phase 7a put the class behind #ifdef CLIENT_DLL, so on the
	// server the type does not exist and this cannot happen by construction.
	// The fact stays because the table is where it WOULD come back: somebody
	// making the server's row "the real one again" writes exactly this row.
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		EXPECT_NE( rows[ i ].m_Name, "SourceSharp" )
			<< "the auto-registered CSourceSharpSystem is a row -- with the "
			<< "SourceSharpManaged row too, every managed participant runs twice";
	}

	EXPECT_EQ( file.find( "CSourceSharpSystem" ), std::string::npos )
		<< "the table file must not reach the auto-registered game system at all";
}

TEST( ServerSystemsTable, TheShellNeverWalksStocksAnonymousList )
{
	const std::string file = StripComments( Read( kTableFile ) );

	// STRONGER AFTER PHASE 7a, NOT WEAKER. This used to name the eleven
	// *AllSystems passes one at a time and allow exactly ONE exception -- the
	// pre-entity fork, whose delegate arm reached stock's list. The delegate is
	// gone and igamesystem.cpp is off the server's link line, so the honest
	// assertion is now the total one: this file does not name the type at all.
	// A needle list can be evaded by a pass nobody wrote a needle for; this
	// cannot.
	EXPECT_EQ( CountOf( file, "IGameSystem" ), 0 )
		<< "the table file reaches stock's IGameSystem somewhere. There is no "
		<< "anonymous list on the server any more -- igamesystem.cpp is out of "
		<< "server_csharp.vpc -- so this is either a link error or a second "
		<< "list somebody has just re-created";

	// And the fork that remains is a walk of the table and nothing else. The
	// function keeps its name because world_adapter.cpp's call site is the
	// landmine below and the name is what makes it legible beside stock's.
	const std::string body = BodyOf( file, "void SourceSharp_LevelInitPreEntityAllSystems(" );
	ASSERT_FALSE( body.empty() );
	EXPECT_NE( body.find( "WalkForward(" ), std::string::npos )
		<< "the pre-entity pass must walk the table";
	EXPECT_EQ( body.find( "SourceSharp_IsShellGameDll" ), std::string::npos )
		<< "the -shellgamedll fork is back. There is nothing to fork BETWEEN: "
		<< "the delegate was deleted at Phase 7a because stock's DLLInit is what "
		<< "called InitAllSystems, so a delegating server would start with no "
		<< "game system initialised at all";
}

TEST( ServerSystemsTable, TheWorldsPreEntityPassGoesThroughTheForkAndNotToStock )
{
	const std::string world = StripComments( Read( kWorldAdapter ) );

	// THE LANDMINE. Stock calls LevelInitPreEntityAllSystems from inside the
	// WORLD ENTITY's precache, not from LevelInit -- so with nothing on
	// s_GameSystems in shell mode that call walks nothing, and nine systems'
	// pre-entity stage stops silently. CPhysicsHook's is one of them, and it is
	// what creates physenv.
	EXPECT_EQ( world.find( "IGameSystem::LevelInitPreEntityAllSystems" ), std::string::npos )
		<< "world_adapter must not call stock's pass directly -- in shell mode it "
		<< "would walk an empty list and physenv would never be created";
	EXPECT_EQ( CountOf( world, "SourceSharp_LevelInitPreEntityAllSystems(" ), 1 )
		<< "exactly one pre-entity pass, through the fork";
}

TEST( ServerSystemsTable, TheReloadIsNotServicedFromTheTablesManagedRow )
{
	const std::string file = StripComments( Read( kTableFile ) );

	// docs/gamedll-ledger.md owner rule 2: the ONE service point on the shell
	// path is IServerGameDLL::Think, because that is the only per-frame entry
	// that runs with no level loaded. A second one in the frame row would
	// reintroduce the interleaving Part 3b exists to prevent.
	EXPECT_EQ( file.find( "SourceSharp_ServiceReloadRequest" ), std::string::npos )
		<< "the table's managed row must not service a reload; Think does";

	const std::string shell = StripComments( Read( kShellGameDll ) );
	EXPECT_EQ( CountOf( shell, "SourceSharp_ServiceReloadRequest()" ), 1 )
		<< "exactly one service call on the shell";
}

//=============================================================================
// 4. ONE BODY PER MANAGED EVENT POINT
//=============================================================================

TEST( ServerSystemsTable, EachManagedEventPointHasExactlyOneBody )
{
	// BOTH FILES THAT COULD HOLD ONE, summed. Counting definitions in the file
	// that has them today would pass a second copy written into the other one,
	// which is precisely where a second copy would be written: the shell's row
	// is the caller that "only needs a little more" than the shared body.
	//
	// The mutation that found this counted the wrong thing and went green.
	const std::string gs    = StripComments( Read( kGameSystem ) );
	const std::string table = StripComments( Read( kTableFile ) );

	static const char *kPoints[] =
	{
		"SourceSharp_ManagedLevelInit",
		"SourceSharp_ManagedLevelShutdown",
		"SourceSharp_ManagedFramePreThink",
		"SourceSharp_ManagedFrameThink",
	};

	for ( size_t i = 0; i < sizeof( kPoints ) / sizeof( kPoints[ 0 ] ); i++ )
	{
		// A definition is `void Name(` at the start of a line; a call is
		// indented. Two definitions is the failure this exists to catch, and it
		// is the kind that agrees with itself until one is edited.
		const std::string def = std::string( "\nvoid " ) + kPoints[ i ] + "(";
		EXPECT_EQ( CountOf( gs, def ) + CountOf( table, def ), 1 )
			<< kPoints[ i ] << " must have exactly one body -- two drivers is the "
			<< "design, two bodies is the bug";
	}

	// And no NEAR-DUPLICATE either: a body copied under a slightly different
	// name is the same defect wearing a disguise, and it is what a
	// definition-count keyed on the exact name cannot see.
	EXPECT_EQ( CountOf( gs, "SourceSharp_ManagedFrameThink_" ), 0 );
	EXPECT_EQ( CountOf( table, "SourceSharp_ManagedFrameThink_" ), 0 );
}

TEST( ServerSystemsTable, TheTablesRowIsTheOnlyServerCallerOfTheFourBodies )
{
	const std::string gs = StripComments( Read( kGameSystem ) );
	const std::string table = StripComments( Read( kTableFile ) );

	// FOLLOWED THE WORK. This was BothDriversCallTheSameFourBodies and it went
	// red at Phase 7a for the right reason: there is only one driver on the
	// server now. What it was protecting -- one body per event point, never two
	// -- is unchanged and is asserted here and by
	// EachManagedEventPointHasExactlyOneBody. What it can additionally say now
	// is that the SECOND caller is gone, which is a statement it could not make
	// while the second caller was the point.
	EXPECT_NE( table.find( "SourceSharp_ManagedLevelInit(" ), std::string::npos );
	EXPECT_NE( table.find( "SourceSharp_ManagedLevelShutdown();" ), std::string::npos );
	EXPECT_NE( table.find( "SourceSharp_ManagedFramePreThink();" ), std::string::npos );
	EXPECT_NE( table.find( "SourceSharp_ManagedFrameThink();" ), std::string::npos );

	// The four bodies are still DEFINED, on the server's side of the file. If
	// they went with their old caller the row above would not link -- but a
	// body accidentally moved inside the client's guard WOULD still link on the
	// client and vanish here, which is the failure this names.
	EXPECT_NE( gs.find( "void SourceSharp_ManagedLevelInit(" ), std::string::npos );
	EXPECT_NE( gs.find( "void SourceSharp_ManagedLevelShutdown(" ), std::string::npos );
	EXPECT_NE( gs.find( "void SourceSharp_ManagedFramePreThink(" ), std::string::npos );
	EXPECT_NE( gs.find( "void SourceSharp_ManagedFrameThink(" ), std::string::npos );

	// The old second caller is gone with the class it was on. CSourceSharpSystem
	// forwarding to a shared body again means the class came back to the server,
	// and then the managed frame runs twice.
	EXPECT_EQ( gs.find( "SourceSharp_ManagedLevelInit( MapName() )" ), std::string::npos )
		<< "CSourceSharpSystem is driving the server's event points again";
	EXPECT_EQ( gs.find( "SourceSharp_ManagedFramePreThink();" ), std::string::npos )
		<< "CSourceSharpSystem is driving the server's frame again";

	// And the row does not reach the managed table itself, which is what a
	// second copy of a body would look like.
	EXPECT_EQ( table.find( "g_ManagedApi" ), std::string::npos )
		<< "the table's row must go through the shared body, not around it";
}

TEST( ServerSystemsTable, TheManagedRowDoesNoInitAndNoShutdownOfItsOwn )
{
	const std::string file = StripComments( Read( kTableFile ) );

	// The CLR is up before the table is walked (LoadManagedRuntime, earlier in
	// DLLInit) and it is torn down after (OnSystemStop, then
	// UnloadManagedRuntime, in DLLShutdown). A row that loaded or unloaded it
	// would be a second attempt at a paired sequence that has already run.
	const std::string body = BodyOf( file, "class CSourceSharpManagedSystem" );
	ASSERT_FALSE( body.empty() );

	EXPECT_EQ( body.find( "SourceSharp_LoadManagedSide" ), std::string::npos );
	EXPECT_EQ( body.find( "SourceSharp_UnloadManagedSide" ), std::string::npos );
}

TEST( ServerSystemsTable, TheManagedRowsObjectDoesNotRegisterItself )
{
	const std::string file = StripComments( Read( kTableFile ) );

	// CServerSystemPerFrame -- SourceSharp's own base
	// (sourcesharp/game/server/shell_serversystem.h), whose constructor appends
	// to nothing. It read CBaseGameSystemPerFrame until Phase 7a; both of
	// stock's bases left the server with igamesystem.cpp, so naming either one
	// here is a link error today and an auto-registration the moment that file
	// comes back.
	EXPECT_NE( file.find( "class CSourceSharpManagedSystem : public CServerSystemPerFrame" ),
	           std::string::npos )
		<< "the shell's row must not self-register";
	EXPECT_EQ( file.find( "CAutoGameSystem" ), std::string::npos );
	EXPECT_EQ( file.find( "CBaseGameSystem" ), std::string::npos );
}

} // namespace
