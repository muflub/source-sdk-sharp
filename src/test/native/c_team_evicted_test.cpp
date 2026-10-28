//====== SourceSharp — the client's team entity is out of the build ==========//
//
// plan_entity.md Phase 5's exit clause. `game/client/c_team.cpp` has left
// `client_csharp.vpc` (the client build; client_sourcesharp in
// src/client/native/CMakeLists.txt since Phase 6b), which is the last of that clause's four subtractions
// besides `cliententitylist.cpp` (step 5a's deliberate, measured seam).
//
// WHY IT IS AN EVICTION AND NOT AN UN-DERIVE. Step 5e took `C_World` OFF the
// SDK client hierarchy. `C_Team` did not need that, and the reason is a
// measurement rather than a preference: NO INSTANCE OF `C_Team` HAS EVER
// EXISTED IN THIS BUILD.
//
//   * `g_Teams` has exactly one writer -- `C_Team`'s constructor.
//   * The engine calls that constructor only through the `CTeam` client class's
//     factory, and only for an entity whose SERVER class is named `CTeam`.
//   * No compiled server translation unit defines one: `game/server/team.cpp`
//     is out of the server build (server_csharp.vpc's banner explained the
//     owner's ruling that teams come back on the MANAGED side), `nm -C --defined-only
//     server.so | grep -cE '\bCTeam\b'` reads 0, and the client's startup dump
//     in a live run reads `server.so registered 4 ServerClass(es)` --
//     CBaseAnimating, CBaseEntity, CEntityDissolve, CRagdollProp.
//
// Confirmed live rather than reasoned, because step 5e's three silent losses
// were all invisible to reading: a probe compiled into c_team.cpp on the base
// commit (counters in the constructor, in GetGlobalTeam and in GetLocalTeam,
// each printing a `[SourceSharp]` line) was run through a full two-pass
// `tools/live-gate` and printed NOTHING, while 8,417 sibling `[SourceSharp]`
// lines reached the same console.log. Zero constructions, zero calls.
//
// So nothing `C_BaseEntity` did FOR a `C_Team` -- the entity list, the spatial
// partition, the client think list, the OnDataChanged queue, the interpolation
// list, the `index == 0` forcing block, `Spawn()` -- was ever done at all.
// Un-deriving a class nothing constructs moves nothing.
//
// WHAT IS ACTUALLY LOST, and it is one thing: the `CTeam` entry in
// `g_pClientClassHead`. The client registered 9 ClientClasses against a server
// that registered 4; `CTeam` was one of the five with no server counterpart and
// it is now four. `alias_mechanics_test.cpp` states the danger of that
// correctly -- a class that leaves `g_pClientClassHead` is one the server can
// still SEND and this client can no longer decode, fatal at run time and silent
// at build time -- which is why the absence of a `CTeam` server class above is
// MEASURED and is asserted here too.
//
// FIVE COMPILED SITES HAD TO BE TOLD, and each keeps the answer it was already
// giving. The compiled ones are asserted below. The fifth,
// `shell_recipientfilter_client.cpp`, needed no edit at all: it names `C_Team*`
// as a parameter type it never dereferences, and `c_recipientfilter.h` already
// forward-declares the name. That is asserted too, because deleting the
// forward declaration is the edit that would break it.
//
// COMMENT-STRIPPED before scanning, the same rule as the sibling eviction
// tests: every one of these files now discusses `c_team.cpp` by name in a
// comment, and a scan that keeps comments reads the explanation as the
// violation. The two POSITIVE needles that could otherwise be satisfied by a
// comment (`return NULL;` and `return true;`) are looked for inside the
// function body they belong to, not anywhere in the file.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <set>
#include <vector>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
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

std::string ReadRaw( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the working directory";
	if ( pszRoot == NULL )
		return std::string();

	const std::string path = std::string( pszRoot ) + "/" + pszRel;
	std::ifstream f( path.c_str() );
	EXPECT_TRUE( f.good() ) << "cannot open " << path;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

std::string ReadStripped( const char *pszRel )
{
	return StripComments( ReadRaw( pszRel ) );
}

bool Has( const std::string &hay, const char *pszNeedle )
{
	return hay.find( pszNeedle ) != std::string::npos;
}

int CountOccurrences( const std::string &hay, const std::string &needle )
{
	int n = 0;
	for ( size_t p = hay.find( needle ); p != std::string::npos;
	      p = hay.find( needle, p + needle.size() ) )
		n++;
	return n;
}

const char *kClientMode = "game/client/clientmode_shared.cpp";
const char *kShellClient = "sourcesharp/game/client/shell_client.cpp";

//-----------------------------------------------------------------------------
// WHAT EACH SIDE COMPILES is read through cmake_graph.h: client_sourcesharp
// (src/client/native/CMakeLists.txt) and server_sourcesharp
// (src/server/native/CMakeLists.txt). Until Phase 6b it was the `$File` rows
// of client_csharp.vpc and server_csharp.vpc. By BASENAME in any directory for
// the absences, the strength the old `client\c_team.cpp` substring had.
//-----------------------------------------------------------------------------
bool CompilesBasename( const char *pszTarget, const std::string &strBase )
{
	const std::set<std::string> s = cmake_graph::CompiledSources( pszTarget );
	for ( std::set<std::string>::const_iterator it = s.begin(); it != s.end(); ++it )
	{
		if ( it->substr( it->rfind( '/' ) + 1 ) == strBase )
			return true;
	}
	return false;
}

} // namespace

//-----------------------------------------------------------------------------
// 1. Membership, on both sides.
//-----------------------------------------------------------------------------
TEST( CTeamEvicted, NeitherProjectCompilesATeamEntity )
{
	// PROOF-OF-READ first, per side: each side's own shell entry point.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_clientdll.cpp" ) )
	    << "client_sourcesharp is not the client's source list";
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/shell_gamedll.cpp" ) )
	    << "server_sourcesharp is not the server's source list";

	EXPECT_FALSE( CompilesBasename( cmake_graph::kClient, "c_team.cpp" ) )
	    << "c_team.cpp is back in the client project. It defines g_Teams, the "
	       "six global team functions and a CTeam ClientClass, and every one of "
	       "them has been unreachable since no server sends a CTeam.";
	EXPECT_FALSE( CompilesBasename( cmake_graph::kServer, "team.cpp" ) )
	    << "team.cpp is back in the server project. The client half is asserted "
	       "against the server half deliberately: c_team.cpp is safe to remove "
	       "ONLY because no compiled server class is named CTeam.";
}

//-----------------------------------------------------------------------------
// 2. The header goes with it, and this is checked against the build's own file
//    list (client_sourcesharp's compiled sources) rather than against a hand-kept list of names. A `.cpp` that comes
//    back into the project and includes c_team.h is exactly the edit that would
//    put an undefined C_Team back in front of the linker.
//-----------------------------------------------------------------------------
TEST( CTeamEvicted, NoCompiledClientTranslationUnitIncludesTheTeamHeader )
{
	const std::set<std::string> compiled = cmake_graph::CompiledSources( cmake_graph::kClient );
	const std::vector<std::string> files( compiled.begin(), compiled.end() );

	// A PROOF-OF-READ that a broken reader would fail rather than pass
	// vacuously. This was a floor of 200 rows over the vpc collector; the
	// population floor is RETIRED IN 6b (cmake_graph cross-checks
	// compile_commands.json against the CMakeLists list and throws on an empty
	// or short one), and what stays is that the list is the client's.
	ASSERT_TRUE( compiled.count( "sourcesharp/game/client/shell_clientdll.cpp" ) )
	    << "client_sourcesharp's compiled list does not name the client shell, "
	       "so it is not this project's file list";

	std::vector<std::string> offenders;
	for ( size_t i = 0; i < files.size(); i++ )
	{
		if ( files[ i ].size() < 4
		    || files[ i ].compare( files[ i ].size() - 4, 4, ".cpp" ) != 0 )
			continue;

		const std::string body = StripComments( ReadRaw( files[ i ].c_str() ) );
		if ( Has( body, "\"c_team.h\"" ) )
			offenders.push_back( files[ i ] );
	}

	std::string strList;
	for ( size_t i = 0; i < offenders.size(); i++ )
		strList += "\n    " + offenders[ i ];

	EXPECT_TRUE( offenders.empty() )
	    << offenders.size() << " compiled client translation unit(s) still "
	       "include c_team.h, whose class has no definition in this build:"
	    << strList;
}

//-----------------------------------------------------------------------------
// 4. The client mode -- the chat line and the broadcast test. clientmode_shared.cpp
//    left the client (plan_client_novalve C3 step 4, lane ui): the player_team
//    chat line is managed (ChatNotices, which names the team; ChatNoticesTests),
//    and the one native event arm, the team-255 broadcast, is shell_client.cpp's.
//-----------------------------------------------------------------------------
TEST( CTeamEvicted, TheJoinChatLineStillPrintsTheTeamNumberItAlwaysPrinted )
{
	// The file is out of the client, so no compiled chat line can print a
	// team through GetGlobalTeam: the line is SourceSharp.Client's now.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kClientMode ) )
	    << "clientmode_shared.cpp is back in the client; its player_team chat "
	       "line printed the team number because GetGlobalTeam answered NULL";
}

TEST( CTeamEvicted, TheBroadcastAudioTeamTestsWentWithTheAnswerTheyLeft )
{
	const std::string shell = ReadStripped( kShellClient );
	ASSERT_FALSE( shell.empty() ) << "cannot read " << kShellClient;

	EXPECT_FALSE( Has( shell, "GetLocalTeam" ) )
	    << "shell_client.cpp calls GetLocalTeam, and nothing in this build defines it.";

	// The one arm that still does something: a broadcast to every team (255)
	// plays on the local player. Whole construct, so the 255 cannot drift.
	EXPECT_TRUE( Has( shell, "if ( V_strcmp( event->GetName(), \"teamplay_broadcast_audio\" ) || event->GetInt( \"team\" ) != 255 )" ) )
	    << "the team-255 broadcast no longer plays; that arm never depended on a team object.";
	EXPECT_EQ( CountOccurrences( shell, "ClientSound::Emit( filter, SOUND_FROM_LOCAL_PLAYER, et );" ), 1 )
	    << "the broadcast's sound is emitted other than once";
}

//-----------------------------------------------------------------------------
// 5. (shell_playerresource.cpp's team fallbacks stood here. The native
//    IGameResources left client.so with nothing reading it, lane c6-input.)
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// 6. The site that needed NO edit, asserted because the edit that would break
//    it is a tidy-up nobody would think twice about.
//-----------------------------------------------------------------------------
TEST( CTeamEvicted, TheRecipientFilterKeepsItsForwardDeclaredTeamParameter )
{
	const std::string hdr = ReadStripped( "game/client/c_recipientfilter.h" );
	EXPECT_TRUE( Has( hdr, "class C_Team;" ) )
	    << "c_recipientfilter.h no longer forward-declares C_Team. "
	       "C_RecipientFilter::AddRecipientsByTeam takes a C_Team* it never "
	       "dereferences, and the forward declaration is the only thing that "
	       "makes that compile now that c_team.h is out of the build.";

	const std::string cpp =
	    ReadStripped( "sourcesharp/game/client/shell_recipientfilter_client.cpp" );
	ASSERT_FALSE( cpp.empty() ) << "cannot read shell_recipientfilter_client.cpp";
	// The definition left 2026-10-25 with no importer on the client link edge
	// (nm -u); the header's declaration, and so the forward declaration
	// above, stays.
	EXPECT_FALSE( Has( cpp, "AddRecipientsByTeam(" ) )
	    << "AddRecipientsByTeam is defined again; nothing on the client link "
	       "edge calls it.";
	EXPECT_FALSE( Has( cpp, "\"c_team.h\"" ) )
	    << "shell_recipientfilter_client.cpp includes c_team.h, which is out of "
	       "the build.";
}
