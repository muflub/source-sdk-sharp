//====== SourceSharp — the client's named list, and what it evicted ===========//
//
// plan_cworld_trace.md Phase T4 -- Phase 7a's other half, inverted onto the
// client.
//
// THE RISK REVERSES AND SO DOES THE SYMPTOM. On the server, a game system that
// nothing walks eventually dereferences NULL and leaves a core file. On the
// CLIENT it does not crash: the frame draws wrong, or draws nothing, and the log
// is clean. So these facts are about the TABLE and the ABSENCE, which are the
// things a silent client cannot tell you about.
//
// SOURCE-READ RATHER THAN LINKED, forced rather than chosen:
// shell_clientsystems.cpp includes cbase.h's whole world and every row's
// accessor lives in a stock translation unit. The behavioural half of this gate
// is a running client that paints a HUD -- see docs/gamedll-ledger.md, T4.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <cctype>
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

const char *kTableFile  = "sourcesharp/game/client/shell_clientsystems.cpp";
const char *kBaseFile   = "sourcesharp/game/client/shell_clientsystem.h";
// WHERE THE RATIONALE LIVES NOW. It used to be a comment beside the $File line
// in the .vpc; the scripts were reformatted with every comment stripped out to
// this document, verbatim and keyed the same way. The convention is unchanged --
// a removed file is NAMED somewhere greppable, so a $File line deleted with no
// explanation is still distinguishable from one deleted by accident -- only the
// place it is named moved.

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

//-----------------------------------------------------------------------------
// One row of kClientSystems[]: the NAME and the ACCESSOR, in file order.
// Parsed rather than needled, so the facts below are about the table as shipped.
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

	const size_t start = body.find( "kClientSystems[] =" );
	EXPECT_NE( start, std::string::npos ) << "kClientSystems[] is not in " << kTableFile;
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
	EXPECT_NE( end, std::string::npos ) << "kClientSystems[]'s initialiser does not close";
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
		const size_t close = body.find( '}', q2 );
		if ( comma == std::string::npos || close == std::string::npos ) break;

		std::string accessor = body.substr( comma + 1, close - comma - 1 );
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

// The ten stages, and this list is the SUBJECT of two facts rather than a
// convenience: it is compared against the base's declarations in one direction
// and against the walkers' dispatches in the other.
const char *kStages[] =
{
	"Init", "PostInit", "Shutdown",
	"LevelInitPreEntity", "LevelInitPostEntity",
	"LevelShutdownPreEntity", "LevelShutdownPostEntity",
	"PreRender", "Update", "PostRender",
};
const int kStageCount = (int)( sizeof( kStages ) / sizeof( kStages[ 0 ] ) );

} // namespace

//=============================================================================
// 1. THE ROWS THEMSELVES
//=============================================================================

TEST( ClientSystemsTable, TheTableHasRowsAtAll )
{
	// Not a count that has to be maintained -- a floor, so that a table emptied
	// by a bad merge cannot make every other fact below pass vacuously. The live
	// census measured forty. 30 -> 25 when the input's
	// CRawInputConvarOnetimeReset row left (lane c6-input, 2026-10-11: 29 rows):
	// the port emptying it row by row, which a floor should not mistake for a
	// bad merge.
	// 28 rows since lane c5-particles' particles_ez.cpp eviction (CEZParticleInit);
	// the floor of 25 stands. 25 -> 20 in C9 (lane c6-input, 2026-10-12: 24 rows
	// once CColorCorrectionMgr left).
	const std::vector<Row> rows = Rows();
	EXPECT_GE( (int)rows.size(), 20 )
		<< "kClientSystems[] parsed as " << rows.size() << " rows; the client "
		<< "was measured running forty systems, so this is a parse failure or an "
		<< "emptied table, and either way every fact below would be vacuous";
}

TEST( ClientSystemsTable, NoSystemIsNamedTwice )
{
	// A system named twice is a system inited twice and shut down twice.
	const std::vector<Row> rows = Rows();
	std::set<std::string> names, accessors;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		EXPECT_TRUE( names.insert( rows[ i ].m_Name ).second )
			<< "kClientSystems[] names '" << rows[ i ].m_Name << "' twice";
		EXPECT_TRUE( accessors.insert( rows[ i ].m_Accessor ).second )
			<< "kClientSystems[] reaches '" << rows[ i ].m_Accessor << "' twice";
	}
}

TEST( ClientSystemsTable, TheStringPoolIsRowZeroSoItShutsDownLast )
{
	// stock's cdll_client_int.cpp Adds GameStringSystem first, and the shutdown
	// walk is this table backwards -- so row zero IS "shuts down last" rather
	// than a second promise somebody has to keep.
	const std::vector<Row> rows = Rows();
	ASSERT_FALSE( rows.empty() );
	EXPECT_EQ( rows[ 0 ].m_Name, "CGameStringPool" );
	EXPECT_EQ( rows[ 0 ].m_Accessor, "GameStringSystem" );
}

TEST( ClientSystemsTable, TheLeafSystemComesBeforeTheDetailObjectSystem )
{
	// "Client Leaf System has to be initialized first, since DetailObjectSystem
	// uses it" -- stock's comment above its Add block, and the only ordering in
	// the client's eleven that anybody wrote a reason for besides colour
	// correction. It is an ordering between two ROWS now, so it is checkable.
	const std::vector<Row> rows = Rows();
	int leaf = -1, detail = -1;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[ i ].m_Name == "CClientLeafSystem" )   leaf = (int)i;
		if ( rows[ i ].m_Name == "CDetailObjectSystem" ) detail = (int)i;
	}
	ASSERT_NE( leaf, -1 )   << "CClientLeafSystem is not a row";
	ASSERT_NE( detail, -1 ) << "CDetailObjectSystem is not a row";
	EXPECT_LT( leaf, detail )
		<< "DetailObjectSystem uses the leaf system and would init against an "
		<< "uninitialised one";
}

TEST( ClientSystemsTable, NeitherTheThinkListNorColourCorrectionIsARow )
{
	// "NOTE: This must happen prior to ClientThinkList (color correction is
	// updated there)" -- stock's own comment, on stock's own Add line. The
	// think list left in C8 (lane c6-input: no client think is scheduled), and
	// colour correction in C9: CColorCorrectionMgr hooked no system stage (it
	// was a row for its name only), and its lookups are carried out by
	// shell_worldrender.cpp, its weights reset in OnRenderStart as before.
	const std::vector<Row> rows = Rows();
	ASSERT_FALSE( rows.empty() );
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		EXPECT_NE( rows[ i ].m_Name, "CClientThinkList" ) << "the think list is a row again";
		EXPECT_NE( rows[ i ].m_Name, "CColorCorrectionMgr" ) << "colour correction is a row again";
	}
}

//=============================================================================
// 2. THE WALKS: ONE ARRAY, TWO DIRECTIONS, NO SECOND LIST
//=============================================================================

TEST( ClientSystemsTable, ShutdownIsInitReversedAndIsDerivedFromTheSameRows )
{
	// The property the whole file exists for. Every INIT stage walks forward and
	// every SHUTDOWN stage walks backward, and both walks index the SAME array --
	// so "shut down in reverse order from which they are initialized"
	// (igamesystem.h's own comment) cannot drift, because there is nothing for it
	// to drift from.
	const std::string t = StripComments( Read( kTableFile ) );

	struct { const char *fn; bool bForward; } kExpect[] =
	{
		{ "SourceSharp_ClientSystemsPostInit",                 true  },
		{ "SourceSharp_ClientSystemsLevelInitPreEntity",       true  },
		{ "SourceSharp_ClientSystemsLevelInitPostEntity",      true  },
		{ "SourceSharp_ClientSystemsShutdown",                 false },
		{ "SourceSharp_ClientSystemsLevelShutdownPreEntity",   false },
		{ "SourceSharp_ClientSystemsLevelShutdownPostEntity",  false },
	};

	for ( size_t i = 0; i < sizeof( kExpect ) / sizeof( kExpect[ 0 ] ); i++ )
	{
		const std::string body = BodyOf( t, kExpect[ i ].fn );
		ASSERT_FALSE( body.empty() ) << kExpect[ i ].fn << " has no body in " << kTableFile;

		const bool bWalksForward  = body.find( "WalkForward(" )  != std::string::npos;
		const bool bWalksBackward = body.find( "WalkBackward(" ) != std::string::npos;

		if ( kExpect[ i ].bForward )
		{
			EXPECT_TRUE( bWalksForward )   << kExpect[ i ].fn << " must walk the rows forward";
			EXPECT_FALSE( bWalksBackward ) << kExpect[ i ].fn << " walks backward";
		}
		else
		{
			EXPECT_TRUE( bWalksBackward ) << kExpect[ i ].fn
				<< " is a shutdown stage and must walk the rows BACKWARD -- forwards "
				<< "is the bug stock's GameStringSystem comment is about";
			EXPECT_FALSE( bWalksForward ) << kExpect[ i ].fn << " walks forward";
		}
	}
}

TEST( ClientSystemsTable, BothWalkersReadTheOneArrayAndThereIsNoSecondList )
{
	// A second array -- stock kept s_GameSystemsPerFrame beside s_GameSystems --
	// is the thing that can disagree with the first.
	const std::string t = StripComments( Read( kTableFile ) );
	EXPECT_EQ( CountOf( t, "kClientSystems[] =" ), 1 )
		<< "there is more than one table";
	EXPECT_EQ( CountOf( t, "= ARRAYSIZE( kClientSystems )" ), 1 );
	// AND NOT A SECOND ARRAY UNDER ANOTHER NAME, which is what the needle above
	// cannot see: `kClientSystemsPerFrame[]` does not contain `kClientSystems[]`.
	// The TYPE is what a second list would have to share.
	EXPECT_EQ( CountOf( t, "ClientSystemRow_t k" ), 1 )
		<< "a second array of table rows exists -- stock kept s_GameSystemsPerFrame "
		<< "beside s_GameSystems and the two could disagree, which is the whole "
		<< "reason the per-frame walk FILTERS the one table instead";

	const std::string fwd = BodyOf( t, "static void WalkForward(" );
	const std::string bwd = BodyOf( t, "static void WalkBackward(" );
	ASSERT_FALSE( fwd.empty() );
	ASSERT_FALSE( bwd.empty() );
	EXPECT_NE( fwd.find( "kClientSystems[ i ]" ), std::string::npos );
	EXPECT_NE( bwd.find( "kClientSystems[ i ]" ), std::string::npos );

	// and forwards really is forwards, backwards really is backwards
	EXPECT_NE( fwd.find( "i = 0; i < kClientSystemCount; i++" ), std::string::npos );
	EXPECT_NE( bwd.find( "i = kClientSystemCount; --i >= 0" ), std::string::npos );
}

TEST( ClientSystemsTable, TheThreePerFrameStagesAllWalkForward )
{
	// PreRender, Update and PostRender are not shutdown stages: a frame is not a
	// teardown, and walking one backwards would silently reverse draw order.
	const std::string t = StripComments( Read( kTableFile ) );
	const char *kFrame[] =
	{
		"SourceSharp_ClientSystemsPreRender",
		"SourceSharp_ClientSystemsPostRender",
		"SourceSharp_ClientSystemsUpdate",
	};
	for ( size_t i = 0; i < sizeof( kFrame ) / sizeof( kFrame[ 0 ] ); i++ )
	{
		const std::string body = BodyOf( t, kFrame[ i ] );
		ASSERT_FALSE( body.empty() ) << kFrame[ i ] << " has no body";
		EXPECT_EQ( body.find( "WalkBackward" ), std::string::npos )
			<< kFrame[ i ] << " walks the frame backwards";
		const bool bForward =
			body.find( "i = 0; i < kClientSystemCount; i++" ) != std::string::npos ||
			body.find( "WalkForwardPerFrame" ) != std::string::npos;
		EXPECT_TRUE( bForward ) << kFrame[ i ] << " does not walk the rows forward";
	}
}

TEST( ClientSystemsTable, TheBaseDeclaresExactlyTheStagesTheWalkersDispatch )
{
	// One-to-one, in BOTH directions, which is the fact Phase 7a's ledger records
	// as the one that keeps catching things. A stage on the base with no walker
	// is a method nothing can reach; a walker for a stage the base does not
	// declare will not compile, but a walker that dispatches a stage nobody
	// listed here is a stage this file has stopped describing.
	const std::string base  = StripComments( Read( kBaseFile ) );
	const std::string table = StripComments( Read( kTableFile ) );

	for ( int i = 0; i < kStageCount; i++ )
	{
		const std::string decl = std::string( "virtual " );
		// declared on the base (either half of it)
		const bool bDeclared =
			base.find( std::string( " " ) + kStages[ i ] + "()" ) != std::string::npos ||
			base.find( std::string( " " ) + kStages[ i ] + "( float" ) != std::string::npos;
		EXPECT_TRUE( bDeclared ) << "stage '" << kStages[ i ]
			<< "' is dispatched but the base does not declare it";

		// dispatched by a walker
		const bool bDispatched =
			table.find( std::string( "::" ) + kStages[ i ] ) != std::string::npos ||
			table.find( std::string( "->" ) + kStages[ i ] + "(" ) != std::string::npos;
		EXPECT_TRUE( bDispatched ) << "stage '" << kStages[ i ]
			<< "' is declared on the base and no walker dispatches it";
	}

	// AND THE OTHER DIRECTION, which the loop above cannot see: it iterates
	// kStages, so a stage ADDED to the base under a name nobody listed here is
	// invisible to it. Every `virtual` the base declares must be either a stage
	// a walker dispatches or one of the three bookkeeping methods -- so a new
	// method on the base is a compile-time-silent, dispatch-time-silent addition
	// that this notices.
	std::vector<std::string> virtuals;
	for ( size_t at = base.find( "virtual " ); at != std::string::npos;
	      at = base.find( "virtual ", at + 8 ) )
	{
		const size_t paren = base.find( '(', at );
		if ( paren == std::string::npos ) break;
		size_t nameEnd = paren;
		while ( nameEnd > at && isspace( (unsigned char)base[ nameEnd - 1 ] ) ) nameEnd--;
		size_t nameStart = nameEnd;
		while ( nameStart > at &&
		        ( isalnum( (unsigned char)base[ nameStart - 1 ] ) || base[ nameStart - 1 ] == '_' ) )
			nameStart--;
		virtuals.push_back( base.substr( nameStart, nameEnd - nameStart ) );
	}
	EXPECT_GE( (int)virtuals.size(), kStageCount )
		<< "the base declares fewer virtuals than there are stages -- parse failure";

	for ( size_t i = 0; i < virtuals.size(); i++ )
	{
		const std::string &v = virtuals[ i ];
		if ( v == "Name" || v == "IsPerFrame" || v == "CClientSystem" ) continue;

		bool bKnown = false;
		for ( int s = 0; s < kStageCount; s++ )
			if ( v == kStages[ s ] ) bKnown = true;

		EXPECT_TRUE( bKnown ) << "the base declares a virtual '" << v
			<< "' that is not one of the ten stages -- so no walker dispatches it "
			<< "and it is a callback nothing can reach, which is exactly the shape "
			<< "this phase removed from stock";
	}

	// AND THE FOUR THAT ARE GONE ARE GONE. Each was checked from both ends
	// before it was dropped -- see the base header. Their coming back is the
	// regression this half of the fact is for.
	const char *kRefused[] = { "OnSave", "OnRestore", "SafeRemoveIfDesired",
	                           "LevelShutdownPreClearSteamAPIContext" };
	for ( size_t i = 0; i < sizeof( kRefused ) / sizeof( kRefused[ 0 ] ); i++ )
	{
		EXPECT_EQ( base.find( kRefused[ i ] ), std::string::npos )
			<< "the client base declares '" << kRefused[ i ] << "' again -- it has "
			<< "no driver on this side, so it is a callback nothing can reach";
		EXPECT_EQ( table.find( kRefused[ i ] ), std::string::npos )
			<< "a walker dispatches '" << kRefused[ i ] << "' again";
	}
}

//=============================================================================
// 3. THE BASE: NO REGISTRATION, NO STATE
//=============================================================================

TEST( ClientSystemsTable, TheClientBaseHasNoConstructorSideEffectAndNoState )
{
	// The whole point of the class. Constructing one appends to nothing, so the
	// only way a system runs is by being a row -- a property of the type rather
	// than of anybody's discipline.
	const std::string base = StripComments( Read( kBaseFile ) );

	EXPECT_EQ( base.find( "Add(" ), std::string::npos )
		<< "the client base registers itself again";
	EXPECT_EQ( base.find( "m_pNext" ), std::string::npos )
		<< "the client base has an intrusive list pointer again";
	EXPECT_EQ( base.find( "AllSystems" ), std::string::npos )
		<< "the client base names one of stock's list walks";
	EXPECT_EQ( base.find( "static " ), std::string::npos )
		<< "the client base has file-scope or class-scope static state; "
		<< "igamesystem.cpp's s_GameSystems is exactly what this phase removed";

	// The constructor takes a name and stores it, and does nothing else.
	const std::string ctor = BodyOf( base, "CClientSystem( char const *pszName = NULL )" );
	EXPECT_TRUE( ctor == "{}" || ctor.empty() )
		<< "CClientSystem's constructor has a body: " << ctor;
}

TEST( ClientSystemsTable, TheClientBaseRefusesToBeIncludedByTheServer )
{
	// T1's hazard, pointing the other way. Including this header from the SERVER
	// half of a shared file is the mistake this phase is arranged around, and it
	// is refused at compile time rather than discovered as a server that quietly
	// stopped initialising.
	const std::string base = Read( kBaseFile );
	EXPECT_NE( base.find( "#ifndef CLIENT_DLL" ), std::string::npos );
	EXPECT_NE( base.find( "#error" ), std::string::npos )
		<< "shell_clientsystem.h no longer refuses a server include";
	EXPECT_LT( base.find( "#ifndef CLIENT_DLL" ), base.find( "class CClientSystem" ) )
		<< "the refusal is below the class, so the server would compile the class "
		<< "before reaching it";
}

TEST( ClientSystemsTable, TheSecureLaunchSystemIsNotARow )
{
	// Measured, and it is the one the live census could not see: stock constructs
	// it from RegisterSecureLaunchProcessFunc at the END of CHLClient::Init --
	// AFTER InitAllSystems has run -- so it took CAutoGameSystem's `already
	// initted` branch, joined the list too late for its own Init() to be called,
	// and overrides no stage anyway. A registrant that ran nothing. Its header
	// left with secure_command_line.cpp (C6, Steam to managed: SecureLaunch.cs);
	// what stays true is that the table never grew a row for it.
	const std::vector<Row> rows = Rows();
	ASSERT_FALSE( rows.empty() ) << "the table parsed no rows: the absence below would pass vacuously";
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		EXPECT_NE( rows[ i ].m_Name, "CSecureLaunchSystem" )
			<< "it is a row now, which would give it an Init() call it has never had";
	}
}

TEST( ClientSystemsTable, TheMapNameIsAskedForRatherThanHeld )
{
	// igamesystem.cpp's s_pMapName had THREE live client readers and they all had
	// to survive. A file-scope static could not: the standing rule forbids one and
	// tools/statics-audit enforces it. So the helper takes the caller's buffer and
	// holds nothing.
	const std::string t = StripComments( Read( kTableFile ) );
	const std::string body = BodyOf( t, "const char *SourceSharp_ClientMapName(" );
	ASSERT_FALSE( body.empty() ) << "SourceSharp_ClientMapName has no body";
	EXPECT_EQ( body.find( "static" ), std::string::npos )
		<< "the map-name helper holds state again, which is s_pMapName under a new "
		<< "name";
	EXPECT_NE( body.find( "GetLevelName" ), std::string::npos )
		<< "it no longer asks the engine, so it must be holding the answer somewhere";

	// and the readers read it rather than stock's static
	// game/shared/GameStats.cpp WAS the third reader and is not in the build
	// any more -- plan_shared.md Phase 4, docs/client_evictions.md. It is
	// dropped from this list rather than kept, because a file no project
	// compiles is a file whose contents this gate has no business asserting:
	// it would pass on dead text forever.
	const char *kReaders[] =
	{
		// MOVED by plan_entity.md step 7e to
		// sourcesharp/game/client/shell_soundscape_client.cpp, and by sound
		// cluster 17 (lane c2-sims) to the client-system shell that hands the
		// map name to the managed soundscapes. The row follows the code:
		// Read() fails loudly on a path it cannot open, so a stale one here
		// would be red rather than vacuous.
		"sourcesharp/game/client/shell_soundscape_audio_client.cpp",
		"sourcesharp/game/shared/sourcesharp_gamesystem.cpp",
	};
	for ( size_t i = 0; i < sizeof( kReaders ) / sizeof( kReaders[ 0 ] ); i++ )
	{
		const std::string r = StripComments( Read( kReaders[ i ] ) );
		EXPECT_NE( r.find( "SourceSharp_ClientMapName(" ), std::string::npos )
			<< kReaders[ i ] << " stopped asking for the map name; stock's reading "
			<< "was live and dropping it is silent";
	}
}

TEST( ClientSystemsTable, TheNativeGameResourcesAreGone )
{
	// Lane c6-input, 2026-10-11: CSourceSharpGameResources published g_PR, the
	// native IGameResources, once a frame from the managed player resource's
	// bytes. Nothing read it: no object on client.so's link line imported g_PR
	// or GameResources() (nm -u), and no engine binary names the interface
	// (strings over bin/linux64). The scoreboard and every name the UI shows
	// are managed's (IPlayerNames, PlayerResourceNetState), held by their own
	// facts. So the system row is gone, and so is the file.
	const std::vector<Row> rows = Rows();
	ASSERT_FALSE( rows.empty() ) << "the table parsed no rows: the absence below would pass vacuously";
	for ( size_t i = 0; i < rows.size(); i++ )
		EXPECT_NE( rows[ i ].m_Name, "CSourceSharpPlayerResourceSystem" );

	std::ifstream cpp( "sourcesharp/game/client/shell_playerresource.cpp" );
	EXPECT_FALSE( cpp.good() ) << "shell_playerresource.cpp is back";
	std::ifstream hdr( "sourcesharp/game/client/shell_playerresource.h" );
	EXPECT_FALSE( hdr.good() ) << "shell_playerresource.h is back";

	// Proof the relative paths above resolve: a sibling that stays opens.
	std::ifstream sibling( "sourcesharp/game/client/shell_clientsystems.cpp" );
	EXPECT_TRUE( sibling.good() ) << "the working directory is not farm/<config>/src: the two absences prove nothing";
}
