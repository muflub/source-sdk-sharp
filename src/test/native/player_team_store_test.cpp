//========= SourceSharp — the player's team is one native store ===============//
//
// WHAT THIS PINS (tf2 stage 0.5). The team became real storage:
// CStandalonePlayer::m_iTeamNum, written by the PlayerSetTeam slot that
// managed ITeams.Join calls, and read back by the two readers the engine and
// plugins ask -- SourceSharp_GetPlayerTeam (IEnginePlayers.GetTeam, the
// scoreboard, ent_info) and IPlayerInfo::GetTeamIndex. Before 0.5 both readers
// answered a flat 0, and a revert to that compiles, links and passes every
// managed fact (they run over a test double); only a live join would notice.
//
// WHY IT READS SOURCE. The four bodies hang off engine state: the slot resolves
// a handle through the engine's edict table, the player is a CStandalonePlayer
// whose header pulls in the SDK prelude, and the info view is built by the
// engine's player-info manager. No test binary here links any of them
// (playerinfo_block_test.cpp makes the same argument for the block it reads).
// So each body is extracted with comments stripped and must reach the store:
// the setter writes through ChangeTeam, both readers return TeamNumber(), and
// those two accessors are the member assignment and read -- a chain with no
// link a `return 0` can replace unseen.
//
// THE EXTRACTOR IS PROVED FIRST: it must find a body that exists and refuse
// one that does not, or two empty strings would "contain" nothing and every
// assertion below would be vacuous.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>

#include <fstream>
#include <string>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string probe = std::string( kCandidates[ i ] ) + "/sourcesharp/game/server";
		if ( stat( probe.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
			return kCandidates[ i ];
	}
	return NULL;
}

// Comments out, string literals kept (a // inside a string is not a comment).
std::string StripComments( const std::string &in )
{
	std::string out;
	bool bBlock = false, bString = false;

	for ( size_t i = 0; i < in.size(); i++ )
	{
		const char c = in[ i ];

		if ( bBlock )
		{
			if ( c == '*' && i + 1 < in.size() && in[ i + 1 ] == '/' ) { bBlock = false; i++; }
			continue;
		}

		if ( bString )
		{
			out += c;
			if ( c == '\\' && i + 1 < in.size() ) { out += in[ ++i ]; }
			else if ( c == '"' ) bString = false;
			continue;
		}

		if ( c == '"' ) { bString = true; out += c; continue; }
		if ( c == '/' && i + 1 < in.size() && in[ i + 1 ] == '*' ) { bBlock = true; i++; continue; }
		if ( c == '/' && i + 1 < in.size() && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			out += '\n';
			continue;
		}

		out += c;
	}

	return out;
}

// The file, comments stripped and every run of whitespace collapsed to one
// space, so a reformat cannot break a needle and a needle cannot match prose.
std::string ReadNormalised( const std::string &relative )
{
	const char *root = SrcRoot();
	if ( !root )
		return std::string();

	std::ifstream file( ( std::string( root ) + "/" + relative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all( ( std::istreambuf_iterator< char >( file ) ), std::istreambuf_iterator< char >() );
	const std::string stripped = StripComments( all );

	std::string out;
	bool bSpace = false;
	for ( size_t i = 0; i < stripped.size(); i++ )
	{
		const char c = stripped[ i ];
		const bool ws = c == ' ' || c == '\t' || c == '\n' || c == '\r';
		if ( ws ) { bSpace = true; continue; }
		if ( bSpace && !out.empty() ) out += ' ';
		bSpace = false;
		out += c;
	}
	return out;
}

// The brace-balanced body that follows the first occurrence of `head`, or ""
// when `head` is absent or has no body.
std::string BodyAfter( const std::string &text, const std::string &head )
{
	const size_t at = text.find( head );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = text.find( '{', at + head.size() );
	if ( open == std::string::npos )
		return std::string();

	// No statement between the head and its brace: a declaration followed by
	// some later body is not this function's body.
	if ( text.find( ';', at + head.size() ) < open )
		return std::string();

	int depth = 0;
	for ( size_t i = open; i < text.size(); i++ )
	{
		if ( text[ i ] == '{' ) depth++;
		else if ( text[ i ] == '}' && --depth == 0 )
			return text.substr( open, i - open + 1 );
	}
	return std::string();
}

const char *const kEntityApi = "sourcesharp/game/server/shell_entityapi.cpp";
const char *const kPlayerHeader = "sourcesharp/game/server/shell_standalone_player.h";
const char *const kPlayerSource = "sourcesharp/game/server/shell_standalone_player.cpp";

}  // namespace

//-----------------------------------------------------------------------------
// The extractor finds a real body and refuses an absent one.
//-----------------------------------------------------------------------------
TEST( PlayerTeamStore, TheBodyExtractorFindsWhatIsThereAndNothingElse )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot find sourcesharp/game/server from the cwd";

	const std::string api = ReadNormalised( kEntityApi );
	ASSERT_FALSE( api.empty() ) << kEntityApi << " read as empty";

	EXPECT_FALSE( BodyAfter( api, "int32_t SourceSharp_GetPlayerTeam( uint32_t hPlayer )" ).empty() );
	EXPECT_TRUE( BodyAfter( api, "int32_t SourceSharp_NoSuchFunctionAnywhere(" ).empty() );
}

//-----------------------------------------------------------------------------
// The setter writes the store: the resolved standalone player's ChangeTeam.
//-----------------------------------------------------------------------------
TEST( PlayerTeamStore, PlayerSetTeamWritesThroughChangeTeam )
{
	const std::string body = BodyAfter( ReadNormalised( kEntityApi ),
		"void SourceSharp_PlayerSetTeam( uint32_t hPlayer, int32_t nTeam )" );
	ASSERT_FALSE( body.empty() ) << "SourceSharp_PlayerSetTeam's body was not found";

	EXPECT_NE( body.find( "SourceSharp_ResolveStandalonePlayer( hPlayer )" ), std::string::npos )
		<< "the setter no longer resolves the player it was handed: " << body;
	EXPECT_NE( body.find( "->ChangeTeam( nTeam );" ), std::string::npos )
		<< "the setter no longer writes the team into the store: " << body;
}

//-----------------------------------------------------------------------------
// The engine-side reader answers the store, not a constant.
//-----------------------------------------------------------------------------
TEST( PlayerTeamStore, GetPlayerTeamReturnsTheStoredTeam )
{
	const std::string body = BodyAfter( ReadNormalised( kEntityApi ),
		"int32_t SourceSharp_GetPlayerTeam( uint32_t hPlayer )" );
	ASSERT_FALSE( body.empty() ) << "SourceSharp_GetPlayerTeam's body was not found";

	EXPECT_NE( body.find( "return pStandalone->TeamNumber();" ), std::string::npos )
		<< "GetPlayerTeam no longer returns the stored team (IEnginePlayers.GetTeam, "
		<< "the scoreboard and ent_info all read this): " << body;
	EXPECT_EQ( body.find( "return 0;" ), std::string::npos )
		<< "GetPlayerTeam answers a flat 0 again: " << body;
}

// The plugin-side reader (IPlayerInfo::GetTeamIndex in shell_playerinfo.cpp)
// went with the plugin ABI (plan_server_native_min P1, ruling 1); the
// engine-side reader above is the one reader left.

//-----------------------------------------------------------------------------
// And the two accessors are the member itself: the write assigns it, the read
// returns it, and a new player starts unassigned (team 0).
//-----------------------------------------------------------------------------
TEST( PlayerTeamStore, TheAccessorsAreTheMember )
{
	const std::string header = ReadNormalised( kPlayerHeader );
	ASSERT_FALSE( header.empty() ) << kPlayerHeader << " read as empty";

	EXPECT_NE( header.find( "int TeamNumber() const { return m_iTeamNum; }" ), std::string::npos )
		<< "CStandalonePlayer::TeamNumber no longer reads m_iTeamNum";
	EXPECT_NE( header.find( "void ChangeTeam( int iTeamNum ) { m_iTeamNum = iTeamNum; }" ), std::string::npos )
		<< "CStandalonePlayer::ChangeTeam no longer assigns m_iTeamNum";

	EXPECT_NE( ReadNormalised( kPlayerSource ).find( "m_iTeamNum( 0 )" ), std::string::npos )
		<< "a new standalone player no longer starts on team 0 (unassigned)";
}
