//====== SourceSharp — five files, eight symbols, one translation unit =======//
//
// plan_server_all.md section 9, Wave 8, names five files and says one file can
// hold them: globals.cpp, init_factory.cpp, variant_t.cpp, server_class.cpp
// and networkvar.cpp. sourcesharp/game/server/shell_globals.cpp is that file.
//
// A RELOCATION AND NOT AN EVICTION, which is the only reason it was possible.
// docs/server_eviction_graph.md computes, over all 95 stock objects, that
// eighty-one of them form one mutually entangled knot in which no member's
// $File line can go until essentially all of them do -- and FOUR OF THESE FIVE
// ARE IN THAT KNOT. Nothing stopped naming these symbols and nothing had to:
// the definitions moved and the link graph did not change. The closure
// arithmetic constrains deletion, not movement.
//
// So what has to be checked is different from every other test in this series.
// The others ask "is this file out of the list, and did the thing that needed
// it survive". This one asks "are all eight definitions still HERE, exactly
// once".
//
// The eight, counted before and after with nm:
//
//   globals.cpp       g_vecAttackDir  g_iSkillLevel  g_fGameOver
//   init_factory.cpp  FactoryList_Store  FactoryList_Retrieve
//   variant_t.cpp     variant_t::SetEntity
//   server_class.cpp  g_pServerClassHead
//   networkvar.cpp    g_bUseNetworkVars
//
// SIX SINCE plan_server_native_v2 T5 (2026-10-19): g_vecAttackDir and
// g_iSkillLevel went, no object on server.so's link importing either (nm U),
// and their needles below went with them (user ruling 2026-10-19: tests of
// unused code go with it). FIVE SINCE T6: g_fGameOver went the same way.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <set>
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

bool Exists( const std::string &strPath )
{
	struct stat st;
	return stat( strPath.c_str(), &st ) == 0 && S_ISREG( st.st_mode );
}

size_t CountOf( const std::string &hay, const char *needle )
{
	size_t n = 0;
	const size_t len = strlen( needle );
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + len ) )
	{
		n++;
	}
	return n;
}

std::string Tight( const char *pszRel, const char *pszAnchor )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL );
	if ( !pszRoot )
		return std::string();

	const std::string raw = ReadRaw( std::string( pszRoot ) + "/" + pszRel );
	EXPECT_FALSE( raw.empty() ) << "could not open " << pszRel;

	const std::string tight = NoWhitespace( StripComments( raw ) );
	EXPECT_NE( tight.find( NoWhitespace( pszAnchor ) ), std::string::npos )
		<< pszRel << " no longer contains its anchor \"" << pszAnchor << "\"";
	return tight;
}

// Does the target compile a source whose farm-relative path's BASENAME is
// pszBasename, in any directory? The old vpc reader matched the basename after
// a quote or a path separator, so a file moved to another directory still
// counted; this keeps that. Read through cmake_graph.h, which throws on a
// missing, empty or stale input.
bool CompilesBasename( const char *pszTarget, const char *pszBasename )
{
	const std::set< std::string > src = cmake_graph::CompiledSources( pszTarget );
	const std::string strTail = std::string( "/" ) + pszBasename;
	for ( std::set< std::string >::const_iterator it = src.begin(); it != src.end(); ++it )
	{
		if ( *it == pszBasename
			|| ( it->size() >= strTail.size()
				&& it->compare( it->size() - strTail.size(), strTail.size(), strTail ) == 0 ) )
			return true;
	}
	return false;
}

const char *kMerged = "sourcesharp/game/server/shell_globals.cpp";

} // namespace

//-----------------------------------------------------------------------------
// All eight definitions are here, and each exactly once.
//
// "Exactly once" matters as much as "present": a merge that copies rather than
// moves gives two definitions of g_pServerClassHead, and the second one is a
// second list head -- ServerClasses register into one and the engine walks the
// other.
//-----------------------------------------------------------------------------
TEST( ServerGlobalsMerge, TheEightDefinitionsAreAllHereAndEachExactlyOnce )
{
	const std::string g = Tight( kMerged, "FactoryList_Store" );
	ASSERT_FALSE( g.empty() );

	struct Def_t { const char *pszNeedle; const char *pszWhy; };
	static const Def_t kDefs[] = {
		// boolg_fGameOver; went in plan_server_native_v2 T6: CWorld::Precache
		// wrote it and no server object read it (nm U).
		// ss:: since P5 lane R: server.so's own list type (ss_init_factory.h).
		{ "voidss::FactoryList_Store(constss::factorylist_t&sourceData)",
		  "the engine hands the game DLL its factories at load through this" },
		{ "voidss::FactoryList_Retrieve(ss::factorylist_t&destData)",
		  "...and asks for them back through this" },
		// RENAMED, NOT MOVED: `CBaseEntity` is not a name any more -- the alias
		// header retired and 1,543 code tokens in 220 files now read
		// `IGameEntity`. The parameter type is the only part of this needle that
		// changed; the definition is still variant_t.cpp's one assignment, still
		// out of line for the same reason, and still here exactly once.
		{ "voidvariant_t::SetEntity(IGameEntity*val)",
		  "variant_t.cpp's one assignment -- out of line because variant_t.h "
		  "cannot name the entity type" },
		{ "ServerClass*g_pServerClassHead=0;",
		  "the head of the list the ENGINE walks to find this DLL's "
		  "ServerClasses. Two of these is two lists: the classes register into "
		  "one and the engine reads the other" },
		{ "boolg_bUseNetworkVars=true;",
		  "read by the _DEBUG network-var assertions -- twenty-six objects "
		  "name it in a debug build and none in release" },
	};

	for ( size_t i = 0; i < sizeof( kDefs ) / sizeof( kDefs[ 0 ] ); i++ )
	{
		const size_t n = CountOf( g, kDefs[ i ].pszNeedle );
		EXPECT_EQ( n, 1u )
			<< kMerged << " has " << n << " definitions of \""
			<< kDefs[ i ].pszNeedle << "\", want exactly 1 -- "
			<< kDefs[ i ].pszWhy;
	}
}

TEST( ServerGlobalsMerge, TheStaticLinkedGuardCameAcrossWithTheBoolItGuards )
{
	const std::string g = Tight( kMerged, "g_bUseNetworkVars" );
	ASSERT_FALSE( g.empty() );

	// Stock's guard, kept verbatim rather than dropped as obviously true here:
	// it is what makes the same source legal in a statically linked build.
	EXPECT_NE( g.find( "#if!defined(_STATIC_LINKED)||defined(_SHARED_LIB)" ),
	           std::string::npos )
		<< "the _STATIC_LINKED guard around g_bUseNetworkVars is gone.";
}

//-----------------------------------------------------------------------------
// The five are out of the file list, and the merged file is in it.
//-----------------------------------------------------------------------------
TEST( ServerGlobalsMerge, TheFiveAreOutOfTheListAndTheMergedFileIsIn )
{
	// server_sourcesharp's source list (src/server/native/CMakeLists.txt). The
	// merged file's presence is asserted FIRST and is the proof-of-read: a
	// reader that saw nothing cannot then answer "the five are out" for free.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kMerged ) )
		<< "shell_globals.cpp is not compiled by server_sourcesharp, and the five "
		<< "files it replaced are not either -- so eight definitions are missing.";

	static const char *kGone[] = { "globals.cpp", "init_factory.cpp",
	                               "variant_t.cpp", "server_class.cpp",
	                               "networkvar.cpp" };
	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_FALSE( CompilesBasename( cmake_graph::kServer, kGone[ i ] ) )
			<< kGone[ i ] << " is in the server list again, and "
			<< "shell_globals.cpp defines everything it defines. That is a "
			<< "duplicate-symbol link at best; for g_pServerClassHead it would "
			<< "be two list heads.";
	}
}

TEST( ServerGlobalsMerge, TheThreeServerOnlyFilesAreDeletedFromTheTree )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );

	// Deleted rather than left unlisted, and the reason is Phase 13 rather
	// than tidiness -- the same choice bmodels.cpp went out on. variant_t.cpp
	// names CBaseEntity in a signature; once CBaseEntity is a typedef for
	// IGameEntity a file left in the tree for later is a file that cannot be
	// put back. Leaving them would also give the next person two copies of
	// g_iSkillLevel to choose between with nothing to say which is live.
	static const char *kDeleted[] = { "game/server/globals.cpp",
	                                  "game/server/init_factory.cpp",
	                                  "game/server/variant_t.cpp" };
	for ( size_t i = 0; i < sizeof( kDeleted ) / sizeof( kDeleted[ 0 ] ); i++ )
	{
		EXPECT_FALSE( Exists( std::string( pszRoot ) + "/" + kDeleted[ i ] ) )
			<< kDeleted[ i ] << " is back in the tree. Its definitions are in "
			<< "shell_globals.cpp; two copies is a question with no answer.";
	}
}
