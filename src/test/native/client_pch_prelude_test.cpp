//====== SourceSharp — there is no forced prelude any more ===================//
//
// plan_shim.md step 0b. This file used to be about the client's FORCED
// PRELUDE: `client_csharp.vpc` said
//
//     $Create/UsePrecompiledHeader  "Use Precompiled Header (/Yu)"
//     $Create/UsePCHThroughFile     "cbase.h"
//
// the ninja generator turned that into `-include .../cbase.h` on 142 client and
// 139 server compile lines, and a per-file `"Not Using Precompiled Headers"`
// block was the only way to take one TU off it. Deleting `#include "cbase.h"`
// from a .cpp therefore changed nothing on its own, and the ten cases this file
// carried existed to keep the source half and the vpc half of step 7h from
// drifting apart.
//
// 0b removed the mechanism. Neither C# vpc has a PCH directive, neither has a
// `stdafx.cpp` row, and both `stdafx.cpp` files are off the disk -- so every
// translation unit now sees exactly what it includes, and the drift the ten
// cases watched for cannot happen: a file that does not include what it uses
// does not compile.
//
// What replaces them is three facts, and the third is the one with teeth:
//
//   1. no compile command of ours forces a prelude (`-include`, a .pch/.gch)
//      and no CMakeLists asks for one (the mechanism cannot come back quietly);
//   2. no target compiles a stdafx.cpp, and neither stdafx.cpp is on disk (the
//      file that CREATED the header is gone, not merely unreferenced);
//   3. every .cpp the game DLLs and the host compile that names a Source type
//      includes a Source header of its own.
//
// PHASE 6b (VPC -> CMake). The subject was the three .vpc scripts; it is now
// what CMake compiles, read through cmake_graph.h (compile_commands.json
// cross-checked against the CMakeLists lists). Two VPC-only cases were
// retired: "no vpc row is named twice" (a source list is a set in CMake, and a
// duplicate would be a CMake error) and the `\`-continued `$File` group
// ratchet (CMake lists have no continuation syntax, and the private vpc
// collectors that could not read one are gone with the vpcs). The on-disk
// half of the first survives as case 4.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <fstream>
#include <sstream>
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
			return kCandidates[ i ];
	}
	return NULL;
}

std::string ReadRaw( const std::string &strRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the working directory";
	if ( pszRoot == NULL )
		return std::string();

	const std::string path = std::string( pszRoot ) + "/" + strRel;
	std::ifstream f( path.c_str() );
	if ( !f.good() )
		return std::string();
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

bool Exists( const std::string &strRel )
{
	const char *pszRoot = SrcRoot();
	if ( pszRoot == NULL )
		return false;
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + strRel ).c_str(), &st ) == 0
	       && S_ISREG( st.st_mode );
}

std::vector< std::string > Lines( const std::string &strIn )
{
	std::vector< std::string > out;
	size_t at = 0;
	while ( at <= strIn.size() )
	{
		const size_t nl = strIn.find( '\n', at );
		if ( nl == std::string::npos )
		{
			if ( at < strIn.size() )
				out.push_back( strIn.substr( at ) );
			break;
		}
		out.push_back( strIn.substr( at, nl - at ) );
		at = nl + 1;
	}
	return out;
}

std::string Trim( const std::string &s )
{
	size_t a = 0, b = s.size();
	while ( a < b && ( s[ a ] == ' ' || s[ a ] == '\t' || s[ a ] == '\r' ) ) a++;
	while ( b > a && ( s[ b - 1 ] == ' ' || s[ b - 1 ] == '\t' || s[ b - 1 ] == '\r' ) ) b--;
	return s.substr( a, b - a );
}

std::string StripComments( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	for ( size_t i = 0; i < s.size(); i++ )
	{
		if ( s[ i ] == '/' && i + 1 < s.size() && s[ i + 1 ] == '/' )
		{
			while ( i < s.size() && s[ i ] != '\n' )
				i++;
			if ( i < s.size() )
				out += '\n';
			continue;
		}
		if ( s[ i ] == '/' && i + 1 < s.size() && s[ i + 1 ] == '*' )
		{
			i += 2;
			while ( i + 1 < s.size() && !( s[ i ] == '*' && s[ i + 1 ] == '/' ) )
				i++;
			i++;
			continue;
		}
		out += s[ i ];
	}
	return out;
}

bool IsWordChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
	       || ( c >= '0' && c <= '9' ) || c == '_';
}

// A whole-word search. "Vector" is a substring of "CUtlVector", and a
// substring hit would convict a file that names neither.
bool NamesWord( const std::string &strHaystack, const std::string &strWord )
{
	size_t at = 0;
	while ( ( at = strHaystack.find( strWord, at ) ) != std::string::npos )
	{
		const bool bLeft = at > 0 && IsWordChar( strHaystack[ at - 1 ] );
		const size_t end = at + strWord.size();
		const bool bRight = end < strHaystack.size() && IsWordChar( strHaystack[ end ] );
		if ( !bLeft && !bRight )
			return true;
		at = end;
	}
	return false;
}

// Does the file include a header of its own -- a quoted include, which is how
// every Source header is asked for? `<...>` is the toolchain's own.
bool IncludesAQuotedHeader( const std::string &strRel )
{
	const std::vector< std::string > lines = Lines( ReadRaw( strRel ) );
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trim( lines[ i ] );
		if ( t.compare( 0, 9, "#include " ) == 0 && t.find( '"' ) != std::string::npos )
			return true;
	}
	return false;
}

// The three targets that are ours: server.so, client.so, libmanaged_host.so.
const char *const kTargets[] = { cmake_graph::kServer, cmake_graph::kClient, cmake_graph::kHost };
const size_t kNumTargets = sizeof( kTargets ) / sizeof( kTargets[ 0 ] );

// The tokens that mean "this TU is built against Source". Every one of them is
// declared in a Source header and in no system header.
const char *const kSourceTypes[] = {
	"CBaseEntity", "C_BaseEntity", "CBasePlayer", "C_BasePlayer",
	"ConVar", "CUtlVector", "Vector", "QAngle", "edict_t", "KeyValues",
	"IServerEntity", "IClientEntity", "CRecipientFilter", "IGameEntity",
};
const size_t kNumSourceTypes = sizeof( kSourceTypes ) / sizeof( kSourceTypes[ 0 ] );

} // namespace

//-----------------------------------------------------------------------------
// 1. NO PRECOMPILED HEADER, IN ANY SPELLING. VPC's generator turned a PCH
//    directive into `-include .../cbase.h` on every compile line; CMake's
//    target_precompile_headers does the same with a .pch/.gch. Either coming
//    back puts a prelude on every TU again.
//-----------------------------------------------------------------------------
TEST( ClientPchPrelude, NoCompileCommandForcesAPrelude )
{
	for ( size_t t = 0; t < kNumTargets; t++ )
	{
		const std::string list = cmake_graph::ListText( kTargets[ t ] );
		EXPECT_EQ( list.find( "precompile_headers" ), std::string::npos )
			<< cmake_graph::ListFile( kTargets[ t ] ) << " asks for a precompiled "
			   "header again. In this project that is a FORCED PRELUDE, and every "
			   "TU's include list stops being what it compiles against.";

		const std::vector< cmake_graph::CompileEntry > entries = cmake_graph::TargetEntries( kTargets[ t ] );
		ASSERT_FALSE( entries.empty() ) << kTargets[ t ];
		for ( size_t i = 0; i < entries.size(); i++ )
		{
			std::istringstream toks( entries[ i ].strCommand );
			std::string tok;
			while ( toks >> tok )
			{
				EXPECT_NE( tok, "-include" ) << entries[ i ].strFile << " is compiled with a forced -include";
				EXPECT_EQ( tok.find( "-include-pch" ), std::string::npos ) << entries[ i ].strFile;
				EXPECT_EQ( tok.find( ".gch" ), std::string::npos ) << entries[ i ].strFile;
				EXPECT_EQ( tok.find( ".pch" ), std::string::npos ) << entries[ i ].strFile;
			}
		}
	}
}

//-----------------------------------------------------------------------------
// 2. AND THE FILE THAT CREATED IT IS GONE. A stdafx.cpp with no PCH directive
//    compiles cbase.h into an object nothing needs; left on disk it is an
//    invitation to wire the mechanism back up.
//-----------------------------------------------------------------------------
TEST( ClientPchPrelude, NoTargetCompilesAStdafxAndNeitherFileIsOnDisk )
{
	for ( size_t t = 0; t < kNumTargets; t++ )
	{
		const std::set< std::string > srcs = cmake_graph::CompiledSources( kTargets[ t ] );
		ASSERT_FALSE( srcs.empty() ) << kTargets[ t ];
		for ( std::set< std::string >::const_iterator it = srcs.begin(); it != srcs.end(); ++it )
			EXPECT_EQ( it->find( "stdafx" ), std::string::npos )
				<< kTargets[ t ] << " compiles " << *it << " again.";
	}

	EXPECT_FALSE( Exists( "game/server/stdafx.cpp" ) )
		<< "game/server/stdafx.cpp is back on disk. It was `#include \"cbase.h\"` "
		   "and nothing else -- its only purpose was to create the precompiled "
		   "header this step removed.";
	EXPECT_FALSE( Exists( "game/client/stdafx.cpp" ) )
		<< "game/client/stdafx.cpp is back on disk. Same file, same reason.";
}

//-----------------------------------------------------------------------------
// 3. THE CASE WITH TEETH. With no prelude, a TU that names a Source type and
//    includes no Source header of its own cannot compile -- so this is the
//    compile the build would fail, asserted over the whole source list rather
//    than over whichever TUs a given build happened to reach.
//-----------------------------------------------------------------------------
TEST( ClientPchPrelude, EveryTuThatNamesASourceTypeIncludesASourceHeaderOfItsOwn )
{
	int nChecked = 0;
	for ( size_t t = 0; t < kNumTargets; t++ )
	{
		const std::set< std::string > srcs = cmake_graph::CompiledSources( kTargets[ t ] );
		for ( std::set< std::string >::const_iterator it = srcs.begin(); it != srcs.end(); ++it )
		{
			const std::string &p = *it;
			if ( p.size() < 5 || p.compare( p.size() - 4, 4, ".cpp" ) != 0 )
				continue;
			if ( !Exists( p ) )
				continue;

			const std::string text = StripComments( ReadRaw( p ) );
			const char *pszWhy = NULL;
			for ( size_t k = 0; k < kNumSourceTypes && pszWhy == NULL; k++ )
				if ( NamesWord( text, kSourceTypes[ k ] ) )
					pszWhy = kSourceTypes[ k ];
			if ( pszWhy == NULL )
				continue;

			nChecked++;
			EXPECT_TRUE( IncludesAQuotedHeader( p ) )
				<< p << " names " << pszWhy << " and includes no header of its "
				   "own. It used to be handed cbase.h by the vpc; nothing hands "
				   "it anything now, so this TU does not compile.";
		}
	}

	// A floor against a walk that stopped early, not a target: the port shrinks
	// it. 196 checked at lane ui's rebase onto client-novalve 26adc6bf (debug),
	// after C3 evicted the stock viewport panels, clientmode_shared, vgui_int
	// and the voice manager; the two DLLs compile 310 TUs there. 150 checked at
	// plan_server_native_v2 T3 (the shrink the comment above expects), so the
	// floor is 120.
	EXPECT_GT( nChecked, 120 )
		<< "only " << nChecked << " translation units were checked, and the two "
		   "game DLLs compile more than 300 between them -- a number this low "
		   "means the walk stopped early rather than that the work is done";
}

//-----------------------------------------------------------------------------
// 4. NOT ABOUT THE PRELUDE, KEPT DELIBERATELY. Every source the client
//    compiles is a file on disk in the farm -- the precondition for every
//    reader of the source text in this directory.
//-----------------------------------------------------------------------------
TEST( ClientPchPrelude, EveryCompiledClientSourceIsOnDisk )
{
	const std::set< std::string > srcs = cmake_graph::CompiledSources( cmake_graph::kClient );
	ASSERT_FALSE( srcs.empty() );
	for ( std::set< std::string >::const_iterator it = srcs.begin(); it != srcs.end(); ++it )
		EXPECT_TRUE( Exists( *it ) ) << "client_sourcesharp compiles " << *it << ", which is not on disk";
}
