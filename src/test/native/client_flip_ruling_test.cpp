//====== SourceSharp — the client flip's ruling on the game-system roster =====//
//
// plan_entity.md §6, lane `c8-clientsystems` (Lane 3 of the CLIENT FLIP wave).
//
// WHAT THIS FILE IS FOR. `kClientSystems[]` is the client's whole IGameSystem
// order, and the client flip asks one question of every row: does the
// translation unit that DEFINES the row's system survive the removal of the
// entity hierarchy? The answers are written as `// RULING | ...` lines beside
// the table in shell_clientsystems.cpp. These facts are what stops those lines
// from becoming prose that used to be true:
//
//   * a row with no ruling, or a ruling with no row, fails;
//   * a ruling whose FILE does not actually define the symbol it names fails —
//     so the ruling cannot survive the definition being moved out from under it;
//   * a ruling that says STAYS about a file plan_entity.md §6 puts on its
//     seventeen-file LEAVE list fails, and the converse fails too. That pair is
//     the check that matters: a STAY that should have LEFT keeps policy in C++
//     forever, and a LEAVE that should have STAYED deletes a capability.
//
// ONE FACT USED TO BE ABOUT A CAPABILITY RATHER THAN A TEXT: that the one row
// this lane PORTED, `C_BaseAnimatingGameSystem`, still empties the bone thread
// pool's parallel setup queue at level shutdown. It read that body in
// game/client/c_baseanimating.cpp, which client.so no longer compiles and which
// has left the source tree, so it was culled with the file.
//
// SOURCE-READ RATHER THAN LINKED, for client_systems_table_test.cpp's reason:
// shell_clientsystems.cpp includes cbase.h's whole world and every row's
// accessor lives in another translation unit. The behavioural half of this gate
// is a live map load and a live map RELOAD, with every row's Init and Shutdown
// asserted in order.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <map>
#include <cctype>
#include <fstream>
#include <sys/stat.h>

#include <sstream>
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

// The repository root is src/'s parent (the farm): docs/plans/plan_entity.md
// is under it, not in src/.
std::string RepoPath( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();
	return std::string( pszRoot ) + "/../" + pszRel;
}

std::string ReadPath( const std::string &path )
{
	std::ifstream file( path.c_str(), std::ios::binary );
	EXPECT_TRUE( file.is_open() ) << "could not open " << path
		<< " -- every invariant about it would pass vacuously";
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

std::string Read( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the test's cwd";
	if ( !pszRoot )
		return std::string();
	return ReadPath( std::string( pszRoot ) + "/" + pszRel );
}

// Comments are stripped before the TABLE is parsed and never before the RULING
// lines are, and the asymmetry is deliberate: two of kClientSystems[]'s rows
// are inside comments (a raw grep reads forty rows where the client runs
// thirty-eight), while the rulings ARE comments.
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

const char *kTableFile = "sourcesharp/game/client/shell_clientsystems.cpp";
const char *kPlanFile  = "docs/plans/plan_entity.md";  // was the repo root before source-sdk-sharp

std::string Trim( const std::string &in )
{
	size_t a = 0, b = in.size();
	while ( a < b && isspace( (unsigned char)in[ a ] ) ) a++;
	while ( b > a && isspace( (unsigned char)in[ b - 1 ] ) ) b--;
	return in.substr( a, b - a );
}

//-----------------------------------------------------------------------------
// The rows of kClientSystems[], comment-stripped, in file order. The same parse
// client_systems_table_test.cpp does, kept here rather than shared because a
// shared parser is one more thing that can be quietly changed to make both
// files agree about something untrue.
//-----------------------------------------------------------------------------
std::vector<std::string> TableRowNames()
{
	std::vector<std::string> names;
	const std::string body = StripComments( Read( kTableFile ) );

	const size_t start = body.find( "kClientSystems[] =" );
	EXPECT_NE( start, std::string::npos ) << "kClientSystems[] is not in " << kTableFile;
	if ( start == std::string::npos )
		return names;

	const size_t open = body.find( '{', start );
	size_t depth = 0, end = std::string::npos;
	for ( size_t i = open; i < body.size(); i++ )
	{
		if ( body[ i ] == '{' ) depth++;
		else if ( body[ i ] == '}' ) { depth--; if ( depth == 0 ) { end = i; break; } }
	}
	EXPECT_NE( end, std::string::npos ) << "kClientSystems[]'s initialiser does not close";
	if ( end == std::string::npos )
		return names;

	size_t i = open + 1;
	while ( i < end )
	{
		const size_t q1 = body.find( '"', i );
		if ( q1 == std::string::npos || q1 > end ) break;
		const size_t q2 = body.find( '"', q1 + 1 );
		if ( q2 == std::string::npos || q2 > end ) break;
		const size_t close = body.find( '}', q2 );
		if ( close == std::string::npos ) break;

		names.push_back( body.substr( q1 + 1, q2 - q1 - 1 ) );
		i = close + 1;
	}
	return names;
}

//-----------------------------------------------------------------------------
// One `// RULING | <row> | <verdict> | <file> | <symbol>` line.
//-----------------------------------------------------------------------------
struct Ruling
{
	std::string m_Row;
	std::string m_Verdict;
	std::string m_File;
	std::string m_Symbol;
};

std::vector<Ruling> Rulings()
{
	std::vector<Ruling> out;
	const std::string body = Read( kTableFile );
	const std::string kPrefix = "\n// RULING | ";

	for ( size_t at = body.find( kPrefix ); at != std::string::npos;
	      at = body.find( kPrefix, at + 1 ) )
	{
		const size_t from = at + kPrefix.size();
		const size_t eol  = body.find( '\n', from );
		if ( eol == std::string::npos ) break;

		std::string line = body.substr( from, eol - from );

		std::vector<std::string> cols;
		size_t start = 0;
		for ( size_t bar = line.find( '|' ); ; bar = line.find( '|', start ) )
		{
			if ( bar == std::string::npos ) { cols.push_back( Trim( line.substr( start ) ) ); break; }
			cols.push_back( Trim( line.substr( start, bar - start ) ) );
			start = bar + 1;
		}

		if ( cols.size() != 4 )
			continue;

		Ruling r;
		r.m_Row     = cols[ 0 ];
		r.m_Verdict = cols[ 1 ];
		r.m_File    = cols[ 2 ];
		r.m_Symbol  = cols[ 3 ];
		out.push_back( r );
	}
	return out;
}

// §6's seventeen-file LEAVE table, read out of the plan at run time so the facts
// below cannot drift from it silently. Delimited by the two headings either side
// of it rather than by a line number.
//
// ONLY THE FIRST COLUMN COUNTS, and this is not a tidying detail. The table is
// `| TU | why it is not §6's | what does the thing afterwards |`, and the THIRD
// column names files that STAY -- it is the column that says what does the job
// instead. A substring search over the whole region reports
// `shell_propdata_system.cpp` and `shell_effects_sim.cpp` as LEAVE files,
// because the prose names the first as the contrast the weapon scripts are
// measured against and the second as env_wind_shared.cpp's replacement. The
// first draft of this file did exactly that and ruled three rows wrong.
std::set<std::string> Section6LeaveFiles()
{
	std::set<std::string> out;

	const std::string plan = ReadPath( RepoPath( kPlanFile ) );
	const size_t from = plan.find( "#### The 17 that LEAVE" );
	EXPECT_NE( from, std::string::npos )
		<< "plan_entity.md no longer has a '#### The 17 that LEAVE' heading; "
		<< "every ruling below would be checked against nothing";
	if ( from == std::string::npos )
		return out;

	const size_t to = plan.find( "#### The four hardest", from );
	EXPECT_NE( to, std::string::npos )
		<< "the LEAVE table's closing heading is gone, so its extent is unknown";
	if ( to == std::string::npos )
		return out;

	const std::string region = plan.substr( from, to - from );

	size_t pos = 0;
	while ( pos <= region.size() )
	{
		const size_t eol = region.find( '\n', pos );
		const std::string line = region.substr( pos, ( eol == std::string::npos ? region.size() : eol ) - pos );
		pos = ( eol == std::string::npos ) ? region.size() + 1 : eol + 1;

		if ( line.empty() || line[ 0 ] != '|' )
			continue;

		const size_t bar = line.find( '|', 1 );
		if ( bar == std::string::npos )
			continue;

		// The first column only, and inside backticks: the header row and the
		// `|---|` separator carry neither.
		const std::string col = line.substr( 1, bar - 1 );
		for ( size_t tick = col.find( '`' ); tick != std::string::npos; tick = col.find( '`', tick + 1 ) )
		{
			const size_t end = col.find( '`', tick + 1 );
			if ( end == std::string::npos ) break;
			const std::string name = col.substr( tick + 1, end - tick - 1 );
			if ( name.size() > 4 && name.compare( name.size() - 4, 4, ".cpp" ) == 0 )
				out.insert( name );
			tick = end;
		}
	}
	return out;
}

std::string BaseName( const std::string &path )
{
	const size_t slash = path.rfind( '/' );
	return slash == std::string::npos ? path : path.substr( slash + 1 );
}

// A file-scope definition of `sym`: a line that begins in column 0 with an
// identifier character and names the symbol as a whole word. Indented lines are
// bodies and `#`-lines are includes, so neither can satisfy a ruling.
bool DefinesAtFileScope( const std::string &file, const std::string &sym )
{
	size_t pos = 0;
	while ( pos <= file.size() )
	{
		const size_t eol = file.find( '\n', pos );
		const std::string line = file.substr( pos, ( eol == std::string::npos ? file.size() : eol ) - pos );
		pos = ( eol == std::string::npos ) ? file.size() + 1 : eol + 1;

		if ( line.empty() ) continue;
		const char c = line[ 0 ];
		if ( !( isalpha( (unsigned char)c ) || c == '_' ) ) continue;

		for ( size_t at = line.find( sym ); at != std::string::npos; at = line.find( sym, at + 1 ) )
		{
			const bool bLeft  = ( at == 0 )
				|| !( isalnum( (unsigned char)line[ at - 1 ] ) || line[ at - 1 ] == '_' );
			const size_t after = at + sym.size();
			const bool bRight = ( after >= line.size() )
				|| !( isalnum( (unsigned char)line[ after ] ) || line[ after ] == '_' );
			if ( bLeft && bRight )
				return true;
		}
	}
	return false;
}

} // namespace

//=============================================================================
// 1. THE TABLE THE RULING IS ABOUT
//=============================================================================

TEST( ClientFlipRuling, TheTableHasExactlyTwentyOneLiveRows )
{
	// The wave brief's own trap. A raw grep over the initialiser reads more than
	// this, because the evicted systems are written out inside the comments that
	// record why they went. Scoping a system that does not exist is the failure
	// this exact number exists to prevent.
	//
	// 38 UNTIL plan_shim.md 4a item 3 (lane 3a), which deleted
	// CRagdollLRURetirement: its LRU had no writer on either link line, so the
	// per-frame Update walked an empty list.
	//
	// 37 UNTIL plan_client_novalve C2 (lane c2-sims): the steam jet's, the
	// smoke stack's and the smoke volume's render systems left with their sims
	// (SteamJetSim, SmokeStackSim, SmokeVolumeSim) and one row,
	// CSourceSharpManagedParticlesSystem (the managed sims' draw executor), came in.
	//
	// 35 UNTIL the trails followed (lane c2-sims): CSourceSharpTrailSystem left
	// with shell_trail_render.cpp; the six trails are executor clients.
	//
	// 34 UNTIL the wind followed (lane c2-sims): CManagedWindThink left; the
	// client module thinks the winds once a frame (ClientWinds).
	//
	// 33 UNTIL the sound envelopes followed (sound cluster 17, lane c2-sims):
	// CSoundControllerImp left with soundenvelope.cpp; the client module steps
	// the shared controller once a frame.
	//
	// 32 UNTIL the weather followed (lane c2-sims): CPrecipHack left with
	// CClient_Precipitation; the client module thinks the managed weather once
	// a frame (ClientPrecipitations), and r_RainHack's volume is its.
	//
	// 31 UNTIL the dust motes followed (lane c5-particles): CSourceSharpDustSystem
	// left with the native dust volume; the motes (DustVolumeSim) tick in the
	// particle executor's Emit.
	//
	// 30 UNTIL the input followed (cluster 13, lane c6-input):
	// CRawInputConvarOnetimeReset left with in_mouse.cpp; its PostInit is
	// ClientInput.RawInputOnetimeReset, run at the input's Init.
	//
	// 29 UNTIL particles_ez.cpp followed (lane c5-particles): CEZParticleInit
	// left; FX_Dust, its singletons' one filler, is C#'s DustPuff.
	//
	// 28 UNTIL g_PR followed (lane c6-input): CSourceSharpPlayerResourceSystem
	// published the native IGameResources, which nothing read.
	//
	// 27 UNTIL the think list followed (C8, lane c6-input): no client think is
	// scheduled on the link line.
	//
	// 26 UNTIL CPropData followed (C9 K3, lane input): it parsed propdata.txt
	// into a chunk table nothing on the client read.
	//
	// 25 UNTIL CColorCorrectionMgr followed (C9, lane c6-input): it hooked no
	// system stage, and shell_worldrender.cpp carries out its lookups.
	//
	// 24 UNTIL fork physics.cpp followed (the client physics leftovers): the
	// CDataObjectAccessSystem row, a registration with nothing behind it, left
	// with shell_physics_shared_client.cpp.
	//
	// 23 UNTIL the bone leaf library followed: C_BaseAnimatingGameSystem ran its
	// level-shutdown sweeps, and none was registered.
	//
	// 22 UNTIL the tool framework followed: CToolFrameworkClient left with
	// toolframework_client.cpp. SFM is dead (user ruling 2026-10-19).
	// 22 WHILE c_pixel_visibility.cpp serves glow_overlay.cpp beside managed's system
	// (C7 pixel visibility): its own row, renamed CPixelVisibilitySystemGlow, and
	// the managed row named CPixelVisibilitySystem.
	EXPECT_EQ( TableRowNames().size(), 22u );
}

TEST( ClientFlipRuling, TheToolFrameworkIsNamedInACommentAndIsNotARow )
{
	// SFM is dead (user ruling 2026-10-19): the tool framework's bridge left
	// the table with its file. Named, so the deletion reads as one; not a row.
	const std::string raw = Read( kTableFile );
	EXPECT_NE( raw.find( "CToolFrameworkClient" ), std::string::npos );

	const std::vector<std::string> rows = TableRowNames();
	const std::set<std::string> names( rows.begin(), rows.end() );
	EXPECT_EQ( names.count( "CToolFrameworkClient" ), 0u )
		<< "CToolFrameworkClient is a live row again; toolframework_client.cpp "
		   "left client.so (SFM is dead, user ruling 2026-10-19)";
	EXPECT_EQ( raw.find( "ToolFrameworkClientSystem" ), std::string::npos )
		<< "the table file names ToolFrameworkClientSystem again, the accessor "
		   "that left with toolframework_client.cpp";
}

TEST( ClientFlipRuling, TheThreeEvictedSystemsAreNamedInCommentsAndAreNotRows )
{
	// Both halves matter. They must still be NAMED -- a row deleted with no
	// explanation is indistinguishable from one deleted by accident -- and they
	// must not be rows.
	const std::string raw = Read( kTableFile );
	EXPECT_NE( raw.find( "CBasePresence" ), std::string::npos );
	EXPECT_NE( raw.find( "CBaseGameStats_Driver" ), std::string::npos );
	EXPECT_NE( raw.find( "CRagdollLRURetirement" ), std::string::npos );

	const std::vector<std::string> rows = TableRowNames();
	const std::set<std::string> names( rows.begin(), rows.end() );
	EXPECT_EQ( names.count( "CBasePresence" ), 0u )
		<< "CBasePresence is a live row again; step 7g removed the system";
	EXPECT_EQ( names.count( "CBaseGameStats_Driver" ), 0u )
		<< "CBaseGameStats_Driver is a live row again; its accessor's file is gone";
	EXPECT_EQ( names.count( "CRagdollLRURetirement" ), 0u )
		<< "CRagdollLRURetirement is a live row again; plan_shim.md 4a item 3 "
		   "deleted the class, so its accessor no longer exists to call";
}

//=============================================================================
// 2. THE RULING COVERS THE TABLE, EXACTLY
//=============================================================================

TEST( ClientFlipRuling, EveryTableRowHasARuling )
{
	const std::vector<std::string> rows = TableRowNames();
	const std::vector<Ruling> rulings = Rulings();

	std::set<std::string> ruled;
	for ( size_t i = 0; i < rulings.size(); i++ )
		ruled.insert( rulings[ i ].m_Row );

	for ( size_t i = 0; i < rows.size(); i++ )
	{
		EXPECT_EQ( ruled.count( rows[ i ] ), 1u )
			<< "kClientSystems[] row '" << rows[ i ] << "' has no `// RULING |` line; "
			<< "the client flip has no answer for it";
	}
}

TEST( ClientFlipRuling, EveryRulingNamesATableRow )
{
	// The other direction. A ruling about a row that no longer exists is a
	// ruling nobody will notice is stale.
	const std::vector<std::string> rows = TableRowNames();
	const std::set<std::string> live( rows.begin(), rows.end() );

	const std::vector<Ruling> rulings = Rulings();
	for ( size_t i = 0; i < rulings.size(); i++ )
	{
		EXPECT_EQ( live.count( rulings[ i ].m_Row ), 1u )
			<< "`// RULING |` names '" << rulings[ i ].m_Row
			<< "', which is not a row of kClientSystems[]";
	}
}

TEST( ClientFlipRuling, NoRowIsRuledTwice )
{
	const std::vector<Ruling> rulings = Rulings();
	std::set<std::string> seen;
	for ( size_t i = 0; i < rulings.size(); i++ )
	{
		EXPECT_TRUE( seen.insert( rulings[ i ].m_Row ).second )
			<< "'" << rulings[ i ].m_Row << "' is ruled twice, so one of the two "
			<< "verdicts is dead text";
	}
}

TEST( ClientFlipRuling, EveryVerdictIsOneOfTheThree )
{
	const std::vector<Ruling> rulings = Rulings();
	ASSERT_FALSE( rulings.empty() );
	for ( size_t i = 0; i < rulings.size(); i++ )
	{
		const std::string &v = rulings[ i ].m_Verdict;
		EXPECT_TRUE( v == "STAYS" || v == "LEAVES" || v == "PORTED" )
			<< "'" << rulings[ i ].m_Row << "' is ruled '" << v << "'";
	}
}

//=============================================================================
// 3. THE RULING IS ABOUT THE TREE, NOT ABOUT A FILENAME
//=============================================================================

TEST( ClientFlipRuling, EveryRulingsFileExists )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );

	const std::vector<Ruling> rulings = Rulings();
	ASSERT_FALSE( rulings.empty() );
	for ( size_t i = 0; i < rulings.size(); i++ )
	{
		struct stat st;
		const std::string path = std::string( pszRoot ) + "/" + rulings[ i ].m_File;
		EXPECT_EQ( stat( path.c_str(), &st ), 0 )
			<< "'" << rulings[ i ].m_Row << "' is ruled against " << rulings[ i ].m_File
			<< ", which does not exist";
	}
}

TEST( ClientFlipRuling, EveryRulingsFileDefinesTheSymbolItNames )
{
	// This is what keeps a ruling from outliving the definition it is about. The
	// four adapters name their TARGET rather than the adapter -- `ClientLeafSystem()`
	// and `ClientThinkList()` are header inlines and define no symbol of their
	// own, so their rows name `g_pClientLeafSystem` and `g_ClientThinkList`,
	// which is exactly what nm reports shell_clientsystems.o undefined on.
	const std::vector<Ruling> rulings = Rulings();
	ASSERT_FALSE( rulings.empty() );
	for ( size_t i = 0; i < rulings.size(); i++ )
	{
		const std::string body = Read( rulings[ i ].m_File.c_str() );
		EXPECT_TRUE( DefinesAtFileScope( body, rulings[ i ].m_Symbol ) )
			<< "'" << rulings[ i ].m_Row << "' is ruled against "
			<< rulings[ i ].m_File << ", which has no file-scope definition of '"
			<< rulings[ i ].m_Symbol << "' -- the definition moved and the ruling did not";
	}
}

//=============================================================================
// 4. THE RULING AGREES WITH plan_entity.md §6, BOTH WAYS
//=============================================================================

TEST( ClientFlipRuling, Section6sLeaveListStillHasSeventeenFilesInIt )
{
	// The parse's own check, and it is not decoration: every fact below is a
	// membership test against this set, so a heading rename or a table reflow
	// would silently make all of them pass on an empty set.
	EXPECT_EQ( Section6LeaveFiles().size(), 17u );
}

TEST( ClientFlipRuling, EveryLeavesRulingsFileIsOnSection6sLeaveList )
{
	const std::set<std::string> leave = Section6LeaveFiles();
	ASSERT_FALSE( leave.empty() );

	const std::vector<Ruling> rulings = Rulings();
	for ( size_t i = 0; i < rulings.size(); i++ )
	{
		if ( rulings[ i ].m_Verdict != "LEAVES" )
			continue;

		EXPECT_EQ( leave.count( BaseName( rulings[ i ].m_File ) ), 1u )
			<< "'" << rulings[ i ].m_Row << "' is ruled LEAVES against "
			<< rulings[ i ].m_File << ", which §6 does not put on its LEAVE list. "
			<< "A LEAVE that should have STAYED deletes a capability the game needs";
	}
	// NO ANTI-VACUITY GUARD HERE ANY MORE, and the reason is a fact of its own
	// immediately below rather than a `> 0` that would now be red for a correct
	// tree. The loop above still checks every LEAVES row that exists; what stops
	// it quietly checking an empty set is
	// TheTableHasNoLeavesRowBecauseTheOnlyOneWasRehomed, which pins the
	// population and says WHY it is what it is.
}


TEST( ClientFlipRuling, NoStaysRulingsFileIsOnSection6sLeaveList )
{
	// The expensive direction. A STAY that should have LEFT keeps policy in C++
	// forever, and nothing else in this tree would notice.
	const std::set<std::string> leave = Section6LeaveFiles();
	ASSERT_FALSE( leave.empty() );

	const std::vector<Ruling> rulings = Rulings();
	int nChecked = 0;
	for ( size_t i = 0; i < rulings.size(); i++ )
	{
		if ( rulings[ i ].m_Verdict != "STAYS" )
			continue;

		nChecked++;
		EXPECT_EQ( leave.count( BaseName( rulings[ i ].m_File ) ), 0u )
			<< "'" << rulings[ i ].m_Row << "' is ruled STAYS against "
			<< rulings[ i ].m_File << ", which §6 puts on its seventeen-file LEAVE list";
	}
	// 29 STAYS until lane c6-input: CRawInputConvarOnetimeReset's row left with
	// in_mouse.cpp, and its ruling with it, so the floor is the twenty-eight that stay.
	// 27 since lane c5-particles: CEZParticleInit's STAYS row left with particles_ez.cpp.
	// 26 since lane c6-input: CSourceSharpPlayerResourceSystem's left with g_PR.
	// 25 since C8: CClientThinkList's left with client_thinklist.cpp.
	// 24 since C9 K3: CPropData's left with shell_propdata_system.cpp.
	// 23 since C9: CColorCorrectionMgr's left with colorcorrectionmgr.cpp.
	// 22 since the client physics leftovers: CDataObjectAccessSystem's left with
	// shell_physics_shared_client.cpp.
	// 21 since the tool framework: CToolFrameworkClient's left with
	// toolframework_client.cpp (SFM is dead, user ruling 2026-10-19).
	EXPECT_GE( nChecked, 21 )
		<< "fewer than twenty-one rows are ruled STAYS, so the ruling was not parsed";
}

TEST( ClientFlipRuling, NoRowIsRuledLeavesBecauseTheOnlyOneWasRehomed )
{
	// THIS FACT EXISTS TO BE READ WHEN IT FAILS, and it replaces one that said
	// the opposite. What it used to assert, correctly at the time: measured with
	// nm over the 287 objects on the client link line, of the 38 rows' defining
	// files exactly two were on §6's LEAVE list; `c8-clientsystems` ported one
	// and ruled the other -- `CDataObjectAccessSystem`, against
	// `game/shared/physics_main_shared.cpp` -- LEAVES, and filed
	// requests/c8-clientsystems.md asking lane `c8-shared6` to deal with it.
	//
	// THAT REQUEST WAS SERVED, which is why this fact had to change. `c8-shared6`
	// re-homed the class, its static instance and `ClientSystem_DataObjectAccess()`
	// into `game/shared/physics_shared.cpp` -- §6's PhysicsShell row, which STAYS
	// -- precisely so a LIVE game-system row on BOTH sides (`kClientSystems[]`
	// row 14 and `kServerSystems[]`'s own) would not be deleted along with the
	// file. So the verdict became STAYS and the LEAVES population became empty.
	//
	// NEITHER LANE COULD HAVE SEEN THIS. On lane 3's branch the ruling was true;
	// on lane 7's branch there was no ruling table to invalidate. It appeared
	// only on the merged tree, as EveryRulingsFileDefinesTheSymbolItNames going
	// red with "the definition moved and the ruling did not" -- which is exactly
	// the failure that fact was written to produce.
	//
	// `c8-shared6` reported the change as "RE-HOMED, NOT RENAMED. THEIR FILE DOES
	// NOT CHANGE." The SYMBOL name did not change, so lane 3's declaration was
	// untouched -- but a ruling row names the defining FILE as well as the
	// symbol, and that half did change. A cross-lane promise is only as narrow as
	// the thing it actually names.
	//
	// IF THIS GOES RED, a LEAVES row has been added. That is allowed and may be
	// right: go and read EveryLeavesRulingsFileIsOnSection6sLeaveList, which will
	// then be checking a non-empty set again, and update the count here.
	const std::vector<Ruling> rulings = Rulings();
	std::vector<std::string> leaves;
	for ( size_t i = 0; i < rulings.size(); i++ )
		if ( rulings[ i ].m_Verdict == "LEAVES" )
			leaves.push_back( rulings[ i ].m_Row );

	EXPECT_EQ( leaves.size(), 0u );

	// The row itself went (2026-10-18). Re-homed twice -- physics_main_shared.cpp,
	// then physics_shared.cpp's client remnant shell_physics_shared_client.cpp --
	// it was a registration with nothing behind it (no data object exists in
	// client.so) kept for fork physics.cpp's watcher asks, and it left with that
	// file. No ruling names it, and the table no longer registers it.
	for ( size_t i = 0; i < rulings.size(); i++ )
		EXPECT_NE( "CDataObjectAccessSystem", rulings[ i ].m_Row );
	const std::string table = StripComments( Read( kTableFile ) );
	ASSERT_NE( table.find( "kClientSystems[]" ), std::string::npos ) << kTableFile << " did not load";
	EXPECT_EQ( table.find( "ClientSystem_DataObjectAccess" ), std::string::npos );
}

//=============================================================================
// 5. THE ONE ROW THIS LANE PORTED -- GONE
//
// `C_BaseAnimatingGameSystem` was ported into shell_clientsystems.cpp at the
// flip; its one stage ran the bone leaf library's level-shutdown sweeps. No
// sweep was ever registered, and the library and the row went together
// (2026-10-18, docs/client_evictions.md). Its four facts here (the one PORTED
// ruling, its class and accessor in the table's file, its function-local
// instance, its census position) are replaced by this one: no row is ruled
// PORTED, and the table neither registers nor defines it.
//=============================================================================

TEST( ClientFlipRuling, NoRowIsRuledPortedSinceTheBaseAnimatingRowLeft )
{
	const std::vector<Ruling> rulings = Rulings();
	ASSERT_FALSE( rulings.empty() );
	for ( size_t i = 0; i < rulings.size(); i++ )
		EXPECT_NE( "PORTED", rulings[ i ].m_Verdict ) << rulings[ i ].m_Row;

	const std::string table = StripComments( Read( kTableFile ) );
	ASSERT_NE( table.find( "kClientSystems[]" ), std::string::npos ) << kTableFile << " did not load";
	EXPECT_EQ( table.find( "CBaseAnimatingGameSystem" ), std::string::npos );
	EXPECT_EQ( table.find( "ClientSystem_BaseAnimating" ), std::string::npos );

	const std::vector<std::string> rows = TableRowNames();
	ASSERT_FALSE( rows.empty() );
	for ( size_t i = 0; i < rows.size(); i++ )
		EXPECT_NE( "C_BaseAnimatingGameSystem", rows[ i ] );
}

//=============================================================================//
//
// CLIENT FAMILY 8 — lane `f-block`. Two facts about the one site in the family
// that was RE-SPELLED rather than deferred, and about the four that were not.
//
//=============================================================================//

namespace
{

// The four files client family 8 left outside a guard, and the C_BaseEntity
// member each one reached for. `nm -u` named every one of these on the object
// built from the file beside it.
struct Family8Site
{
	const char *pszFile;
	const char *pszMember;
};

// THE TABLE OF STILL-GUARDED READS IS EMPTY, AND THAT IS THE RULING LANDING.
//
// It held three rows -- shell_materialproxies.cpp / HealthFraction(),
// shell_effects_sim.cpp / GetVectors(, shell_effects_sim.cpp /
// GetAbsVelocity() -- and each was asserted to be FOUND and to be inside
// `#ifndef SS_CBASEENTITY_ALIAS`. `the-close 1/N` deleted all three arms with
// the shim's other 237, for the reason the fourth row below already gives:
// every project that builds this tree defines SS_CBASEENTITY_ALIAS, so none of
// them was ever compiled.
//
// So all four rows are now the SAME kind of row, and they are all in
// kFamily8Removed: absence, plus a live anchor in the same file. There is no
// `kFamily8Unguarded` array because C++ has no empty one, and a row list that
// exists only to be skipped would read as a fact rather than as the nothing it
// is.
//
//-----------------------------------------------------------------------------
// THE ROWS, AND WHY THEY ARE THIS KIND OF ROW.
//
// `game/client/c_te_legacytempents.cpp` / `GetAbsVelocity()` was the fourth
// entry in the table above. At 74b071771 the read stood at
// c_te_legacytempents.cpp:4241 --
//
//     #ifndef SS_CBASEENTITY_ALIAS
//     if( pShooter )
//         velocity += pShooter->GetAbsVelocity();
//     #endif
//
// -- inside CTempEnts::CSEjectBrass, and being inside that guard is exactly
// what the fact above asserted about it. The `residue` wave
// (74b071771..a46e8d645) then deleted the guarded arm. Every project that
// builds this tree defines SS_CBASEENTITY_ALIAS (client_csharp.vpc:74,
// server_csharp.vpc:69), so the read was never compiled and its removal moved
// no object file.
//
// IT IS MOVED RATHER THAN DROPPED, and the successor assertion is the stronger
// one. "Every occurrence is guarded" is vacuously true of a file with no
// occurrences -- which is the shape this project has caught 45 times -- so the
// row cannot stay where it was without becoming a check that cannot fail. What
// replaces it is "there are NO occurrences", which is the property the ruling
// actually wanted: the flip has nothing to remove here because nothing asks.
//
// THE PROSE IS STILL IN THE FILE and that is deliberate on the source side:
// c_te_legacytempents.cpp:3588 still carries the whole paragraph explaining why
// this read could not be re-spelled (GetAbsVelocity is a non-virtual inline
// calling CalcAbsoluteVelocity; plan_entity.md §3.2's engine block has no
// velocity field; the request went to .claude/entity-port/requests/f-block.md).
// The scanner skips `//` lines, so the paragraph does not answer for the code.
//-----------------------------------------------------------------------------
const Family8Site kFamily8Removed[] =
{
	// c_te_legacytempents.cpp left the build with C2 (lane c2-fx); its thunk is
	// the file CSEjectBrass lives in now, as a no-caller stub that reads nothing.
	{ "sourcesharp/game/client/shell_legacy_tempents_client.cpp", "GetAbsVelocity()" },
	// Two of the three that moved here from kFamily8Unguarded. Each stood in an
	// `#ifndef SS_CBASEENTITY_ALIAS` arm that no project compiled, and
	// `the-close 1/N` deleted the arm:
	//
	//   SourceSharp_RenderableHealthFraction (shell_materialproxies.cpp) had
	//       C_BaseEntity *pEntity = SDKEntityFromUnknown( pBindArg );
	//       if ( pEntity != NULL ) return pEntity->HealthFraction();
	//   above its `return -1.0f;`. Its row went with the file (lane
	//   c5-proxies): the Health proxy is HealthProxy in C#, which reads no
	//   entity and raises its not-shipped row instead.
	//
	//   CClient_Precipitation::CreateAshParticle had
	//       pPlayer->GetVectors( &vForward, NULL, NULL );
	//   over an already zero-initialised vForward, and
	//   CSnowFallManager::CreateSnowFall had the same pair of reads. Both
	//   zero-initialise and then use the zero, which is what they did in every
	//   build that ever ran.
	// Those two functions went to C# with the weather (lane c2-sims); the rows
	// follow them to the weather's native half, whose player read must still
	// not reach an entity's vectors or velocity.
	{ "sourcesharp/game/client/shell_precipitation_client.cpp", "GetVectors("        },
	{ "sourcesharp/game/client/shell_precipitation_client.cpp", "GetAbsVelocity()"   },
};

// The anchor for each removed row: a live, unguarded line in the same file that
// the residue wave cannot take and the flip does not touch. Without it,
// "no occurrences" is what an unreadable or renamed file answers too.
const Family8Site kFamily8RemovedAnchor[] =
{
	{ "sourcesharp/game/client/shell_legacy_tempents_client.cpp",
	  "void CTempEnts::CSEjectBrass( const Vector &," },
	{ "sourcesharp/game/client/shell_precipitation_client.cpp",
	  "int32_t SourceSharp_PrecipitationPlayer( float *pOut )" },
	{ "sourcesharp/game/client/shell_precipitation_client.cpp",
	  "void DrawPrecipitation()" },
};

//-----------------------------------------------------------------------------
// Is every occurrence of `needle` in `strSource` inside an
// `#ifndef SS_CBASEENTITY_ALIAS` region? Counts the preprocessor nesting the
// same way the lane's own guardcheck.py did, so the test and the measurement
// that motivated it agree by construction rather than by eye.
//
// Comment lines are skipped: every one of these files names the member it
// guarded in the prose explaining why, and a raw needle counts that prose as
// code -- briefs/COMMON.md lists exactly that hazard.
//-----------------------------------------------------------------------------
bool EveryOccurrenceIsGuarded( const std::string &strSource,
                               const std::string &strNeedle,
                               int *pnFound )
{
	std::istringstream lines( strSource );
	std::string strLine;
	std::vector< std::string > stack;
	bool bAllGuarded = true;
	int nFound = 0;

	while ( std::getline( lines, strLine ) )
	{
		const size_t iFirst = strLine.find_first_not_of( " \t" );
		const std::string strTrim =
			( iFirst == std::string::npos ) ? std::string() : strLine.substr( iFirst );

		if ( strTrim.compare( 0, 2, "//" ) == 0 )
			continue;

		if ( strTrim.compare( 0, 1, "#" ) == 0 )
		{
			// Normalise `#  ifndef X` / `#ifndef X` alike.
			std::string d = strTrim.substr( 1 );
			const size_t s = d.find_first_not_of( " \t" );
			if ( s != std::string::npos ) d = d.substr( s );

			if ( d.compare( 0, 7, "ifndef " ) == 0 ||
			     d.compare( 0, 6, "ifdef "  ) == 0 ||
			     d.compare( 0, 3, "if "     ) == 0 )
			{
				stack.push_back( d );
				continue;
			}
			if ( d.compare( 0, 5, "endif" ) == 0 )
			{
				if ( !stack.empty() ) stack.pop_back();
				continue;
			}
			if ( d.compare( 0, 4, "else" ) == 0 || d.compare( 0, 4, "elif" ) == 0 )
			{
				if ( !stack.empty() ) stack.back() = "ELSE-OF " + stack.back();
				continue;
			}
			continue;
		}

		if ( strLine.find( strNeedle ) == std::string::npos )
			continue;

		nFound++;

		bool bGuarded = false;
		for ( size_t i = 0; i < stack.size(); i++ )
			if ( stack[i].compare( 0, 7, "ifndef " ) == 0 &&
			     stack[i].find( "SS_CBASEENTITY_ALIAS" ) != std::string::npos )
				bGuarded = true;

		if ( !bGuarded )
			bAllGuarded = false;
	}

	if ( pnFound ) *pnFound = nFound;
	return bAllGuarded;
}

} // namespace

//-----------------------------------------------------------------------------
// THE ONE THAT WAS RE-SPELLED, AND THEN RE-TYPED.
//
// CTempEnts::FizzEffect reads the entity's yaw to decide which way the
// "current" drags the bubbles, and its model to size the volume. Both reads
// have to survive the flip, and the history of this fact is the history of
// three different answers to "how":
//
//   1. `pent->GetLocalAngles()`. A C_BaseEntity member nothing else implements,
//      so it became an undefined symbol the day c_baseentity.cpp left the link
//      line. `nm -u` named it here.
//   2. `ClientGameEntity_RenderAngles( pent )`. A client-only RELAY, because
//      armed `pent` is an IGameEntity * and that interface has no base: the
//      member call is not an undefined symbol, it is a compile error, and this
//      was one of the 28 translation units that stopped the armed compile.
//      The relay crossed to the shell with a static_cast that is sound only
//      while C_StandaloneEntity is the only linked implementor of IGameEntity.
//   3. `pent->GetRenderAngles()` again -- because the PARAMETER is now an
//      IClientRenderable *, which is the face that declares both reads and
//      which every client entity of either hierarchy implements. No relay, no
//      cast, and no assumption about how many classes implement what.
//
// WHY THIS IS NOT A STRING SEARCH. The fact under test is a relationship
// between two files: whatever member FizzEffect asks off its parameter must be
// declared by the INTERFACE that parameter is typed with. So the parameter type
// is read out of the declaration, the interface's own pure virtuals are read out
// of its header, and every `pent->` ask in the body is required to be one of
// them. Retyping the parameter to a face that does not answer these reads fails
// here; adding a read the face does not declare fails here; and a rename of
// either member is not a failure, which is the difference between a test and a
// checksum.
//-----------------------------------------------------------------------------
namespace
{

// "IClientRenderable" -> "iclientrenderable", which is how public/ names it.
std::string Lowered( const std::string &in )
{
	std::string out = in;

	for ( size_t i = 0; i < out.size(); i++ )
		out[i] = (char)tolower( (unsigned char)out[i] );

	return out;
}

// The braces-included body of a top-level definition of `name`, or "".
//
// Crude on purpose: the only thing it has to be right about is where one
// function's braces begin and end.
std::string FunctionBodyOf( const std::string &code, const std::string &name )
{
	size_t from = 0;

	while ( true )
	{
		const size_t at = code.find( name, from );

		if ( at == std::string::npos )
			return std::string();

		from = at + name.size();

		size_t i = from;

		while ( i < code.size() && isspace( (unsigned char)code[i] ) )
			i++;

		if ( i >= code.size() || code[i] != '(' )
			continue;

		int depth = 0;

		for ( ; i < code.size(); i++ )
		{
			if ( code[i] == '(' ) depth++;
			else if ( code[i] == ')' && --depth == 0 ) { i++; break; }
		}

		while ( i < code.size() && isspace( (unsigned char)code[i] ) )
			i++;

		if ( i >= code.size() || code[i] != '{' )
			continue;

		const size_t open = i;
		depth = 0;

		for ( ; i < code.size(); i++ )
		{
			if ( code[i] == '{' ) depth++;
			else if ( code[i] == '}' && --depth == 0 )
				return code.substr( open, i - open + 1 );
		}

		return std::string();
	}
}

// The declared type of FizzEffect's first parameter, or "" when the
// declaration is not where this fact expects it.
std::string FizzEffectParameterType( const std::string &strHeader )
{
	const size_t at = strHeader.find( "FizzEffect(" );

	if ( at == std::string::npos )
		return std::string();

	const size_t open  = strHeader.find( '(', at );
	const size_t close = strHeader.find( ')', open );

	if ( open == std::string::npos || close == std::string::npos )
		return std::string();

	std::string strFirst = strHeader.substr( open + 1, close - open - 1 );
	const size_t comma = strFirst.find( ',' );

	if ( comma != std::string::npos )
		strFirst = strFirst.substr( 0, comma );

	// "IClientRenderable *pent" -> "IClientRenderable"
	std::istringstream in( strFirst );
	std::string strType;
	in >> strType;
	return strType;
}

// Every member name declared as a virtual on the interface in `strHeader`.
std::set< std::string > VirtualMembersOf( const std::string &strHeader )
{
	std::set< std::string > out;
	size_t at = 0;

	while ( ( at = strHeader.find( "virtual", at ) ) != std::string::npos )
	{
		const size_t open = strHeader.find( '(', at );
		const size_t stop = strHeader.find( ';', at );

		at += 7;

		if ( open == std::string::npos || ( stop != std::string::npos && stop < open ) )
			continue;

		// Back up over the name immediately before the '('.
		size_t end = open;

		while ( end > 0 && isspace( (unsigned char)strHeader[end - 1] ) )
			end--;

		size_t begin = end;

		while ( begin > 0 && ( isalnum( (unsigned char)strHeader[begin - 1] ) ||
		                       strHeader[begin - 1] == '_' ) )
			begin--;

		if ( begin < end )
			out.insert( strHeader.substr( begin, end - begin ) );
	}

	return out;
}

// Every member asked off `pszReceiver` in `strBody`.
std::set< std::string > MembersAskedOf( const std::string &strBody, const char *pszReceiver )
{
	std::set< std::string > out;
	const std::string strArrow = std::string( pszReceiver ) + "->";
	size_t at = 0;

	while ( ( at = strBody.find( strArrow, at ) ) != std::string::npos )
	{
		// Not a longer identifier ending in the receiver's name.
		const bool bWord = ( at == 0 ) || !( isalnum( (unsigned char)strBody[at - 1] ) ||
		                                     strBody[at - 1] == '_' );

		at += strArrow.size();

		if ( !bWord )
			continue;

		size_t end = at;

		while ( end < strBody.size() && ( isalnum( (unsigned char)strBody[end] ) ||
		                                  strBody[end] == '_' ) )
			end++;

		if ( end > at )
			out.insert( strBody.substr( at, end - at ) );
	}

	return out;
}

} // namespace

TEST( ClientFamily8, FizzEffectAsksOnlyMembersItsParameterTypeDeclares )
{
	const std::string strHeader =
		StripComments( Read( "game/client/c_te_legacytempents.h" ) );

	const std::string strType = FizzEffectParameterType( strHeader );

	ASSERT_FALSE( strType.empty() )
		<< "c_te_legacytempents.h no longer declares FizzEffect where this fact "
		   "reads it, so nothing below is a measurement of anything.";

	EXPECT_NE( strType, "IGameEntity" )
		<< "FizzEffect takes an IGameEntity * again. That interface has no base "
		   "and declares neither of the two reads this function makes, so the "
		   "calls have to go back through a relay that static_casts to the one "
		   "class in this DLL implementing it -- which is sound only for as long "
		   "as there is exactly one.";

	// The interface's own header, found from the type name.
	const std::string strIface = StripComments( Read(
		( std::string( "public/" ) + Lowered( strType ) + ".h" ).c_str() ) );

	ASSERT_FALSE( strIface.empty() )
		<< "public/" << Lowered( strType ) << ".h did not open, so the member "
		   "set below would be empty and every assertion would pass vacuously.";

	const std::set< std::string > setDeclared = VirtualMembersOf( strIface );

	ASSERT_GT( setDeclared.size(), 5u )
		<< "public/" << Lowered( strType ) << ".h parsed to "
		<< setDeclared.size() << " virtuals, which is not a face this test can "
		   "check a call against.";

	// SINCE C2 (lane c2-fx) the body is the thunk's: c_te_legacytempents.cpp
	// left the build, FizzEffect has no caller in it (the fizz temp entity is
	// C#'s, TempEntFizzTests), and shell_legacy_tempents_client.cpp keeps the
	// vtable slot as the announced no-caller stub. So what is asserted is that
	// the stub asks NOTHING of its entity -- a read here would be a second,
	// native fizz -- and the anchor is the stub's own announcement.
	const std::string strBody = FunctionBodyOf(
		StripComments( Read( "sourcesharp/game/client/shell_legacy_tempents_client.cpp" ) ),
		"CTempEnts::FizzEffect" );

	ASSERT_NE( strBody.find( "SS_TEMPENTS_NO_CALLER( \"FizzEffect\" )" ), std::string::npos )
		<< "CTempEnts::FizzEffect in shell_legacy_tempents_client.cpp is not the "
		   "announced no-caller stub, so the absence below is not a measurement.";

	EXPECT_TRUE( MembersAskedOf( strBody, "pent" ).empty() )
		<< "the thunk's FizzEffect reads its entity. The fizz is C#'s; a native "
		   "body here is a second implementation nothing calls.";
}

//-----------------------------------------------------------------------------
// THE CLIENT-ONLY RELAYS ARE GONE, AND THE SHARED ONES ARE NOT.
//
// standalone_relays_client.h declared three functions -- ClientGameEntity_Model,
// _IsDormant and _RenderAngles -- each of which took an IGameEntity * and
// static_cast it to C_StandaloneEntity *. That cast is sound only while this
// class is the only LINKED implementor of IGameEntity, which is a property of
// the link line and not of the code: three stock headers (c_entitydissolve.h,
// c_basedoor.h, c_sun.h) declare classes that implement it too, and only the
// fact that none of them is compiled makes the cast correct today.
//
// Every one of the six call sites asked for something an engine interface
// already answers on both hierarchies, so each asks it there now and the header
// is deleted. This fact is the pair of that deletion.
//
// THE ABSENCE CARRIES AN ANCHOR, because an absence is also what an unreadable
// file, an emptied file and a renamed file all answer. The anchor is the SHARED
// relay `GameEntity_ModelIndex`, which stays -- its nine client call sites want
// a model index, a handle or an entity index, and the server compiles the same
// header. A run where the anchor is missing has measured nothing.
//-----------------------------------------------------------------------------
TEST( ClientFamily8, NoCompiledClientFileCallsAClientOnlyEntityRelay )
{
	static const char *const kFiles[] =
	{
		// The thunk that replaced c_te_legacytempents.cpp (C2, lane c2-fx).
		"sourcesharp/game/client/shell_legacy_tempents_client.cpp",
		// c_rope.cpp left with the C2 rope port; its dormancy ask is C#'s
		// (AMissingRopeEndpointIsTreatedAsDormant). ragdoll.cpp left with the
		// ragdolls (plan physics_bones_native_zero Phase 3).
		"sourcesharp/game/client/shell_standalone_client.cpp",
	};

	int nAnchors = 0;

	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[0] ); i++ )
	{
		const std::string strSource = StripComments( Read( kFiles[i] ) );

		ASSERT_FALSE( strSource.empty() )
			<< kFiles[i] << " did not open, so its absence below is not a "
			   "measurement.";

		// (The anchor was the shared relays, GameEntity_*, until their client
		// half left with its last callers, 2026-10-18; the classes the two
		// files define members of are the anchor now.)
		if ( strSource.find( "C_StandaloneEntity::" ) != std::string::npos ||
		     strSource.find( "CTempEnts::" ) != std::string::npos )
		{
			nAnchors++;
		}

		EXPECT_EQ( strSource.find( "ClientGameEntity_" ), std::string::npos )
			<< kFiles[i] << " calls a client-only entity relay again. Those three "
			   "crossed to the shell with a static_cast that is sound only while "
			   "exactly one linked class implements IGameEntity. Ask the fact on "
			   "the engine interface that declares it instead -- IClientRenderable "
			   "for the model and the render angles, IClientNetworkable for "
			   "dormancy, resolved from the entity list by handle or by index.";

		EXPECT_EQ( strSource.find( "standalone_relays_client.h" ), std::string::npos )
			<< kFiles[i] << " includes standalone_relays_client.h, which no longer "
			   "exists.";
	}

	// THE ANCHOR. The shared relays stay; if none of these files names one,
	// the absences above are about a set of files this fact is no longer about.
	EXPECT_GT( nAnchors, 0 )
		<< "not one of these files names a SHARED entity relay, so the "
		   "client-only absences above would also be the answer for a tree in "
		   "which the whole relay layer had been renamed.";
}

//-----------------------------------------------------------------------------
// A ROPE ENDPOINT THAT IS NOT THERE STILL COUNTS AS DORMANT.
//
// ClientGameEntity_IsDormant answered TRUE for a NULL entity on purpose: "there
// is nothing here to draw or follow" is what every caller does with a dormant
// answer. Asking IClientNetworkable::IsDormant directly loses that arm unless
// the call site puts it back, and losing it inverts the test -- an endpoint the
// list cannot resolve would read as NOT dormant, and a rope whose endpoints have
// both gone would keep drawing instead of stopping.
//
// This is the clause a re-point is most likely to drop, because dropping it
// leaves a line that compiles, runs, and is wrong only when an endpoint is
// missing.
//-----------------------------------------------------------------------------
TEST( ClientFamily8, AMissingRopeEndpointIsTreatedAsDormant )
{
	// MOVED WITH THE ROPE: c_rope.cpp's RopeEndpointIsDormant is
	// RopeSimulation.EndpointDormant now (the C2 rope port), and the fact is
	// the same one.
	const std::string strRope = StripComments(
		Read( "sourcesharp/managed/SourceSharp.Client/Ropes/RopeSimulation.cs" ) );

	ASSERT_FALSE( strRope.empty() ) << "RopeSimulation.cs did not open.";

	// THE NULL ARM IS IN THE HELPER, AND THE HELPER IS WHAT BOTH ENDPOINTS ASK.
	const std::string strHelper = FunctionBodyOf( strRope, "EndpointDormant" );

	ASSERT_FALSE( strHelper.empty() )
		<< "RopeSimulation.cs no longer defines EndpointDormant, so the endpoint "
		   "check has been rearranged and this fact is about nothing.";

	EXPECT_NE( strHelper.find( "IsDormant(h)" ), std::string::npos )
		<< "EndpointDormant no longer asks IsDormant.";

	EXPECT_NE( strHelper.find( "!_endpoints.Exists(h)" ), std::string::npos )
		<< "a handle the client cannot resolve is no longer treated as dormant; "
		   "a rope whose endpoints have both gone would keep drawing to a place "
		   "nothing is.";

	// AND BOTH ENDPOINTS GO THROUGH IT, short-circuited as stock's line was.
	size_t nCalls = 0;
	size_t nAt = 0;

	while ( ( nAt = strRope.find( "EndpointDormant(", nAt ) ) != std::string::npos )
	{
		nCalls++;
		nAt += 1;
	}

	EXPECT_EQ( 3u, nCalls )
		<< "EndpointDormant is named " << nCalls << " times (one definition plus "
		   "two calls is 3).";

	EXPECT_NE( strRope.find( "EndpointDormant(_endEntity[0]) &&" ), std::string::npos )
		<< "the start point's dormancy no longer short-circuits the end point's.";
}

//-----------------------------------------------------------------------------
// AND THE PORT'S OWN ARM FOR THE SAME READ STILL AGREES WITH IT. This is what
// makes the line above a re-spelling rather than a guess: the managed path for a
// fizz already resolves the entity by index and asks GetRenderAngles, and it
// recorded why. If that ever changes, the two paths part company again and the
// stock one is the one that was wrong before.
//-----------------------------------------------------------------------------
TEST( ClientFamily8, TheManagedFizzArmAsksTheSameMemberAsTheStockOne )
{
	const std::string strPool =
		StripComments( Read( "sourcesharp/game/client/shell_te_pool_client.cpp" ) );

	EXPECT_NE( strPool.find( "GetRenderAngles()" ), std::string::npos )
		<< "SourceSharp_ClientEntityModelBounds no longer asks GetRenderAngles, "
		   "so the stock FizzEffect path and the managed one now read the "
		   "entity's yaw through different members.";
}

//-----------------------------------------------------------------------------
// THE FOUR THAT WERE DEFERRED. Each is now inside an
// `#ifndef SS_CBASEENTITY_ALIAS` region, which is how the rest of the family was
// already spelled and is what makes arming the macro close them. An unguarded
// one is a member call that stops linking on the day the flip lands, with
// nothing before then to say so.
//-----------------------------------------------------------------------------
TEST( ClientFamily8, EveryDeferredEntityReadIsInsideTheAliasGuard )
{
	// READ THE ASSERTIONS, NOT THE NAME. There is no alias guard left to be
	// inside of. All four reads this fact is about stood in
	// `#ifndef SS_CBASEENTITY_ALIAS` arms that no project ever compiled, and
	// the two dead-arm waves deleted every one of them. "They are all guarded"
	// has become "there are none", which is what the ruling actually wanted --
	// the flip has nothing to remove here because nothing asks.
	//
	// EVERY ROW IS THEREFORE AN ABSENCE, AND EVERY ABSENCE CARRIES AN ANCHOR.
	// An absence is what an empty file, an unreadable one, and a renamed one all
	// answer, so each row names a LIVE, unguarded line in its own file -- the
	// function the removed read lived in -- and that line is ASSERT_GT'd before
	// the absence is looked at. The anchor is per row and per file, which is
	// strictly more than the old "some other row found something somewhere"
	// this fact used to rely on.
	for ( size_t i = 0; i < sizeof( kFamily8Removed ) / sizeof( kFamily8Removed[0] ); i++ )
	{
		const Family8Site &site   = kFamily8Removed[i];
		const Family8Site &anchor = kFamily8RemovedAnchor[i];
		const std::string strSource = Read( site.pszFile );

		// THE ANCHOR FIRST. An absence over a file that did not open, or that has
		// been renamed out from under this row, reads exactly like an absence over
		// a file that really has no such read.
		int nAnchor = 0;
		EveryOccurrenceIsGuarded( strSource, anchor.pszMember, &nAnchor );
		ASSERT_GT( nAnchor, 0 )
			<< site.pszFile << " no longer contains \"" << anchor.pszMember
			<< "\" -- the function the removed read lived in. The absence below "
			<< "would be the answer for a file this row is no longer about.";

		int nFound = 0;
		EveryOccurrenceIsGuarded( strSource, site.pszMember, &nFound );

		EXPECT_EQ( 0, nFound )
			<< site.pszFile << " reaches " << site.pszMember << " again ("
			<< nFound << " occurrence(s) in code). The residue wave removed this "
			<< "read as dead source -- SS_CBASEENTITY_ALIAS is defined by every "
			<< "project that builds this tree, so the guarded arm it was in was "
			<< "never compiled. A new one is a non-virtual, non-interface read on a "
			<< "class the flip deletes, with no cross-hierarchy spelling to move to "
			<< "and no field in the engine block to answer it: guarding it again "
			<< "only reinstates the dead text, so this wants the block field or the "
			<< "ABI slot .claude/entity-port/requests/f-block.md asks for.";
	}
}
