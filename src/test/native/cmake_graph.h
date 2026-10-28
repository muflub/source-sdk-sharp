//========= SourceSharp ============//
//
// What the CMake build compiles and links, for tests that assert it.
//
// Phase 6b replaced VPC with CMake. The tests that used to read
// game/{server,client}/*_csharp.vpc or VPC's ninja graph
// (_vpc_/ninja/sdk_csharp_<cfg>.ninja) read the CMake build here instead:
//
//   * which sources a target compiles    -- compile_commands.json, cross-checked
//                                           against the target's hand-owned
//                                           CMakeLists source list
//   * what a target links                -- build.ninja's link edge
//   * which objects are on that edge     -- build.ninja, never an `ls` of the
//                                           object directory (ninja keeps the
//                                           .o of a TU whose edge went away)
//
// EVERY READER HERE FAILS LOUDLY. A missing, empty or unparsable input throws
// cmake_graph::Error, which gtest reports as a failure of the calling test. A
// reader that found nothing must never answer "not compiled" for free -- that is
// how a missing ninja graph once made every absence assertion in this suite
// pass (CLAUDE.md, "The check that cannot fail").
//
// STALENESS IS A FAILURE TOO. compile_commands.json is build output; the
// CMakeLists source lists are the source of truth. CompiledSources() reads
// both and throws when they disagree, so an old compile_commands.json (a build
// not re-run after a CMakeLists edit) cannot answer for the current tree.
//
// Paths are FARM-RELATIVE, the spelling the CMakeLists lists use:
// "game/server/player.cpp", "sourcesharp/game/server/shell_gamedll.cpp".
//
// Where the files are (the ctest contract, Phase 6b):
//   SOURCESHARP_BUILD_DIR  the CMake binary dir (compile_commands.json,
//                          build.ninja); default <cwd>/../build, since tests
//                          run with cwd farm/<cfg>/src
//   SOURCESHARP_REPO       the repo root (the CMakeLists files); default: walk
//                          up from the cwd to the first SourceSharp.slnx
//
// Header-only and engine-free, like every test here.
//=============================================================================//

#ifndef SOURCESHARP_TEST_CMAKE_GRAPH_H
#define SOURCESHARP_TEST_CMAKE_GRAPH_H

#include <sys/stat.h>
#include <unistd.h>

#include <algorithm>
#include <cstdlib>
#include <fstream>
#include <map>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace cmake_graph
{

// The targets, by their CMake names.
static const char *const kServer = "server_sourcesharp";   // server.so
static const char *const kClient = "client_sourcesharp";   // client.so
static const char *const kHost   = "sourcesharp";          // libmanaged_host.so

class Error : public std::runtime_error
{
public:
	explicit Error( const std::string &strWhat ) : std::runtime_error( "cmake_graph: " + strWhat ) {}
};

inline bool FileExists( const std::string &strPath )
{
	struct stat st;
	return stat( strPath.c_str(), &st ) == 0 && S_ISREG( st.st_mode );
}

// The whole file, or throw: absent and empty are both failures.
inline std::string ReadRequired( const std::string &strPath, const char *pszWhy )
{
	std::ifstream file( strPath.c_str(), std::ios::binary );
	if ( !file.is_open() )
		throw Error( "cannot open " + strPath + " (" + pszWhy + ")" );
	std::ostringstream ss;
	ss << file.rdbuf();
	if ( ss.str().empty() )
		throw Error( strPath + " is EMPTY (" + pszWhy + ")" );
	return ss.str();
}

inline std::string Cwd()
{
	char szBuf[ 4096 ];
	if ( !getcwd( szBuf, sizeof( szBuf ) ) )
		throw Error( "getcwd failed" );
	return std::string( szBuf );
}

inline std::string Parent( const std::string &strPath )
{
	const size_t n = strPath.find_last_of( '/' );
	if ( n == std::string::npos || n == 0 )
		return std::string( "/" );
	return strPath.substr( 0, n );
}

inline std::string RepoRoot()
{
	const char *pszEnv = getenv( "SOURCESHARP_REPO" );
	if ( pszEnv && *pszEnv )
		return std::string( pszEnv );

	for ( std::string strDir = Cwd(); ; strDir = Parent( strDir ) )
	{
		if ( FileExists( strDir + "/SourceSharp.slnx" ) )
			return strDir;
		if ( strDir == "/" )
			break;
	}
	throw Error( "no SourceSharp.slnx above " + Cwd() + " and SOURCESHARP_REPO is unset" );
}

inline std::string BuildDir()
{
	const char *pszEnv = getenv( "SOURCESHARP_BUILD_DIR" );
	if ( pszEnv && *pszEnv )
		return std::string( pszEnv );
	return Parent( Cwd() ) + "/build";
}

// The farm's src/ that compile_commands.json's absolute paths are under:
// <farm>/build is the binary dir, <farm>/src the sources.
inline std::string FarmSrc()
{
	return Parent( BuildDir() ) + "/src";
}

//-----------------------------------------------------------------------------
// The CMakeLists that owns a target's source list, repo-relative.
//-----------------------------------------------------------------------------
inline std::string ListFile( const std::string &strTarget )
{
	if ( strTarget == kServer )
		return "src/server/native/CMakeLists.txt";
	if ( strTarget == kClient )
		return "src/client/native/CMakeLists.txt";
	if ( strTarget == kHost )
		return "src/host/native/CMakeLists.txt";
	if ( strTarget == "tier1" || strTarget == "mathlib" || strTarget == "raytrace" )
		return "cmake/sdk/" + strTarget + ".cmake";
	throw Error( "no CMakeLists is known for target '" + strTarget + "'" );
}

inline std::string ListText( const std::string &strTarget )
{
	return ReadRequired( RepoRoot() + "/" + ListFile( strTarget ), "the target's CMakeLists" );
}

// A CMake line with its `#` comment removed. CMake's `#` inside a quoted
// argument is not a comment, and none of the lists this reads quote a `#`.
inline std::string StripCMakeComment( const std::string &strLine )
{
	const size_t n = strLine.find( '#' );
	return n == std::string::npos ? strLine : strLine.substr( 0, n );
}

inline std::string Trim( const std::string &s )
{
	const size_t a = s.find_first_not_of( " \t\r" );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = s.find_last_not_of( " \t\r" );
	return s.substr( a, b - a + 1 );
}

//-----------------------------------------------------------------------------
// The body of `<command>(<first> ...)` in a CMakeLists: each whitespace token
// until the closing parenthesis, comments dropped. Throws when the block is
// absent or empty.
//-----------------------------------------------------------------------------
inline std::vector< std::string > CMakeBlock( const std::string &strText, const std::string &strOpen,
                                              const std::string &strWhere )
{
	const size_t nAt = strText.find( strOpen );
	if ( nAt == std::string::npos )
		throw Error( strWhere + " has no `" + strOpen + "` block" );
	if ( strText.find( strOpen, nAt + 1 ) != std::string::npos )
		throw Error( strWhere + " has more than one `" + strOpen + "` block" );

	std::vector< std::string > out;
	std::istringstream in( strText.substr( nAt + strOpen.size() ) );
	std::string strLine;
	bool bClosed = false;
	while ( !bClosed && std::getline( in, strLine ) )
	{
		std::string strCode = StripCMakeComment( strLine );
		const size_t nClose = strCode.find( ')' );
		if ( nClose != std::string::npos )
		{
			strCode = strCode.substr( 0, nClose );
			bClosed = true;
		}
		std::istringstream toks( strCode );
		std::string strTok;
		while ( toks >> strTok )
			out.push_back( strTok );
	}
	if ( !bClosed )
		throw Error( strWhere + ": `" + strOpen + "` is never closed" );
	if ( out.empty() )
		throw Error( strWhere + ": `" + strOpen + "` lists nothing" );
	return out;
}

//-----------------------------------------------------------------------------
// The farm-relative spelling of a file of this repo: the most specific
// tools/module-map.tsv row that covers it, as tools/setup links it. The lists
// name this repo's files where they live (${CMAKE_CURRENT_SOURCE_DIR}/x.cpp,
// ${PROJECT_SOURCE_DIR}/src/shared/native/x.cpp) and compile_commands.json does
// too; every caller asks by the farm spelling. A path no row covers throws.
//-----------------------------------------------------------------------------
inline std::string RepoToFarm( const std::string &strRepoRelative )
{
	// A vendored Valve file (vendor/<side>/<path>) is named by its Valve path,
	// as the farm named it.
	for ( const char *pszSide : { "shared/", "server/", "client/" } )
	{
		const std::string strVendor = std::string( "vendor/" ) + pszSide;
		if ( strRepoRelative.compare( 0, strVendor.size(), strVendor ) == 0 )
			return strRepoRelative.substr( strVendor.size() );
	}
	std::istringstream in( ReadRequired( RepoRoot() + "/tools/module-map.tsv", "the module map" ) );
	std::string strLine, strBestRel, strBestFarm;
	while ( std::getline( in, strLine ) )
	{
		const std::string strTrim = Trim( strLine );
		if ( strTrim.empty() || strTrim[ 0 ] == '#' )
			continue;
		const size_t t1 = strLine.find( '\t' );
		const size_t t2 = t1 == std::string::npos ? t1 : strLine.find( '\t', t1 + 1 );
		if ( t2 == std::string::npos )
			throw Error( "tools/module-map.tsv: bad row '" + strLine + "'" );
		const std::string strFarm = strLine.substr( 0, t1 );
		const std::string strKind = strLine.substr( t1 + 1, t2 - t1 - 1 );
		const std::string strRel = Trim( strLine.substr( t2 + 1 ) );
		if ( strRel.find( "{cfg}" ) != std::string::npos )
			continue;
		std::string strHit;
		if ( strKind == "file" )
		{
			if ( strRepoRelative == strRel )
				strHit = strFarm;
		}
		else if ( strRepoRelative.compare( 0, strRel.size() + 1, strRel + "/" ) == 0 )
		{
			strHit = strFarm + "/" + strRepoRelative.substr( strRel.size() + 1 );
		}
		if ( !strHit.empty() && strRel.size() > strBestRel.size() )
		{
			strBestRel = strRel;
			strBestFarm = strHit;
		}
	}
	if ( strBestFarm.compare( 0, 4, "src/" ) != 0 )
		throw Error( strRepoRelative + " is a file of this repo that no tools/module-map.tsv row links into the farm" );
	return strBestFarm.substr( 4 );
}

// One source-list entry, farm-relative: ${SS_SRC}/x is the farm's x;
// ${LOCAL}/x (${CMAKE_CURRENT_SOURCE_DIR}/x), ${SHARED}/x (src/shared/native)
// and ${PROJECT_SOURCE_DIR}/x are this repo's; a bare entry is already
// farm-relative (the Valve libraries' lists).
inline std::string ListToken( const std::string &strTok, const std::string &strTarget )
{
	static const std::string kFarm = "${SS_SRC}/";
	static const std::string kHere = "${CMAKE_CURRENT_SOURCE_DIR}/";
	static const std::string kLocal = "${LOCAL}/";
	static const std::string kShared = "${SHARED}/";
	static const std::string kRepo = "${PROJECT_SOURCE_DIR}/";
	if ( strTok.compare( 0, kFarm.size(), kFarm ) == 0 )
		return strTok.substr( kFarm.size() );
	if ( strTok.compare( 0, kHere.size(), kHere ) == 0 )
		return RepoToFarm( Parent( ListFile( strTarget ) ) + "/" + strTok.substr( kHere.size() ) );
	if ( strTok.compare( 0, kLocal.size(), kLocal ) == 0 )
		return RepoToFarm( Parent( ListFile( strTarget ) ) + "/" + strTok.substr( kLocal.size() ) );
	if ( strTok.compare( 0, kShared.size(), kShared ) == 0 )
		return RepoToFarm( "src/shared/native/" + strTok.substr( kShared.size() ) );
	if ( strTok.compare( 0, kRepo.size(), kRepo ) == 0 )
		return RepoToFarm( strTok.substr( kRepo.size() ) );
	if ( strTok.find( '$' ) != std::string::npos )
		throw Error( ListFile( strTarget ) + ": cannot read the source-list entry '" + strTok + "'" );
	return strTok;
}

//-----------------------------------------------------------------------------
// The target's source list, in order, farm-relative.
//-----------------------------------------------------------------------------
inline std::vector< std::string > ListedSources( const std::string &strTarget )
{
	std::vector< std::string > v = CMakeBlock( ListText( strTarget ), "set(" + strTarget + "_sources\n", ListFile( strTarget ) );
	for ( size_t i = 0; i < v.size(); i++ )
		v[ i ] = ListToken( v[ i ], strTarget );
	return v;
}

//-----------------------------------------------------------------------------
// The target's OPTIONAL set(<target>_debug_sources ...) list: sources the
// CMakeLists appends only in a debug build (the diagnostic console commands,
// user ruling 2026-10-01). Empty when the target has no such block; a file in
// both lists is an error.
//-----------------------------------------------------------------------------
inline std::vector< std::string > DebugListedSources( const std::string &strTarget )
{
	const std::string strText = ListText( strTarget );
	const std::string strOpen = "set(" + strTarget + "_debug_sources\n";
	if ( strText.find( strOpen ) == std::string::npos )
		return std::vector< std::string >();
	std::vector< std::string > v = CMakeBlock( strText, strOpen, ListFile( strTarget ) );
	const std::vector< std::string > base = ListedSources( strTarget );
	for ( size_t i = 0; i < v.size(); i++ )
	{
		v[ i ] = ListToken( v[ i ], strTarget );
		for ( size_t j = 0; j < base.size(); j++ )
			if ( base[ j ] == v[ i ] )
				throw Error( ListFile( strTarget ) + " lists " + v[ i ] + " in both " + strTarget
					+ "_sources and " + strTarget + "_debug_sources" );
	}
	return v;
}

// debug or release, read off <farm>/<config>/build.
inline std::string BuildConfig()
{
	const std::string strFarm = Parent( BuildDir() );
	const size_t nSlash = strFarm.find_last_of( '/' );
	return nSlash == std::string::npos ? strFarm : strFarm.substr( nSlash + 1 );
}

// The target's compile definitions as its CMakeLists writes them (NAME or
// NAME=VALUE). The config- and toolchain-wide ones live in
// cmake/SourceSharp.cmake and are in CompileCommand().
inline std::vector< std::string > ListedDefinitions( const std::string &strTarget )
{
	std::vector< std::string > v = CMakeBlock( ListText( strTarget ),
		"target_compile_definitions(" + strTarget + " PRIVATE\n", ListFile( strTarget ) );
	return v;
}

//-----------------------------------------------------------------------------
// compile_commands.json: a JSON array of flat objects whose values are all
// strings. Parsed exactly that far, and anything else is an Error.
//-----------------------------------------------------------------------------
struct CompileEntry
{
	std::string strFile;      // absolute, as CMake wrote it
	std::string strOutput;    // relative to the build dir
	std::string strCommand;
};

inline void SkipWs( const std::string &s, size_t &i )
{
	while ( i < s.size() && ( s[ i ] == ' ' || s[ i ] == '\t' || s[ i ] == '\n' || s[ i ] == '\r' ) )
		i++;
}

inline std::string JsonString( const std::string &s, size_t &i )
{
	if ( i >= s.size() || s[ i ] != '"' )
		throw Error( "compile_commands.json: expected a string at offset " + std::to_string( i ) );
	i++;
	std::string out;
	while ( i < s.size() && s[ i ] != '"' )
	{
		char c = s[ i++ ];
		if ( c == '\\' )
		{
			if ( i >= s.size() )
				break;
			const char e = s[ i++ ];
			switch ( e )
			{
			case 'n': c = '\n'; break;
			case 't': c = '\t'; break;
			case 'r': c = '\r'; break;
			case 'b': c = '\b'; break;
			case 'f': c = '\f'; break;
			case 'u': throw Error( "compile_commands.json: \\u escape not supported" );
			default:  c = e; break;   // \" \\ \/
			}
		}
		out += c;
	}
	if ( i >= s.size() )
		throw Error( "compile_commands.json: unterminated string" );
	i++;
	return out;
}

inline std::vector< CompileEntry > ParseCompileCommands( const std::string &s )
{
	std::vector< CompileEntry > out;
	size_t i = 0;
	SkipWs( s, i );
	if ( i >= s.size() || s[ i ] != '[' )
		throw Error( "compile_commands.json does not start with '['" );
	i++;
	for ( ;; )
	{
		SkipWs( s, i );
		if ( i < s.size() && s[ i ] == ']' )
			break;
		if ( i >= s.size() || s[ i ] != '{' )
			throw Error( "compile_commands.json: expected '{' at offset " + std::to_string( i ) );
		i++;
		CompileEntry e;
		for ( ;; )
		{
			SkipWs( s, i );
			if ( i < s.size() && s[ i ] == '}' )
			{
				i++;
				break;
			}
			const std::string strKey = JsonString( s, i );
			SkipWs( s, i );
			if ( i >= s.size() || s[ i ] != ':' )
				throw Error( "compile_commands.json: expected ':' after \"" + strKey + "\"" );
			i++;
			SkipWs( s, i );
			const std::string strValue = JsonString( s, i );
			if ( strKey == "file" )
				e.strFile = strValue;
			else if ( strKey == "output" )
				e.strOutput = strValue;
			else if ( strKey == "command" )
				e.strCommand = strValue;
			SkipWs( s, i );
			if ( i < s.size() && s[ i ] == ',' )
				i++;
		}
		if ( e.strFile.empty() || e.strOutput.empty() || e.strCommand.empty() )
			throw Error( "compile_commands.json: an entry lacks file, output or command" );
		out.push_back( e );
		SkipWs( s, i );
		if ( i < s.size() && s[ i ] == ',' )
			i++;
	}
	if ( out.empty() )
		throw Error( "compile_commands.json has no entries" );
	return out;
}

// Cached per build dir (a test can point SOURCESHARP_BUILD_DIR at a fixture).
inline const std::vector< CompileEntry > &CompileCommands()
{
	static std::map< std::string, std::vector< CompileEntry > > s_cache;
	const std::string strPath = BuildDir() + "/compile_commands.json";
	if ( !s_cache.count( strPath ) )
		s_cache[ strPath ] = ParseCompileCommands( ReadRequired( strPath,
			"CMake's compile database -- run `make debug` (or release) in this worktree" ) );
	return s_cache[ strPath ];
}

// The entries whose object goes into this target: CMake writes every object of
// target T under CMakeFiles/T.dir/.
inline std::vector< CompileEntry > TargetEntries( const std::string &strTarget )
{
	const std::string strDir = "/CMakeFiles/" + strTarget + ".dir/";
	std::vector< CompileEntry > out;
	const std::vector< CompileEntry > &all = CompileCommands();
	for ( size_t i = 0; i < all.size(); i++ )
	{
		if ( ( "/" + all[ i ].strOutput ).find( strDir ) != std::string::npos )
			out.push_back( all[ i ] );
	}
	if ( out.empty() )
		throw Error( "compile_commands.json names no object of target '" + strTarget + "'" );
	return out;
}

// The pristine clone's src/ ($SOURCESHARP_VALVE_SDK, else valve-sdk.lock.local),
// or empty when neither names one.
inline std::string StockSrc()
{
	std::string strRoot;
	if ( const char *pszEnv = getenv( "SOURCESHARP_VALVE_SDK" ) )
		strRoot = pszEnv;
	if ( strRoot.empty() )
	{
		std::ifstream f( RepoRoot() + "/valve-sdk.lock.local" );
		f >> strRoot;
	}
	if ( strRoot.empty() )
		return std::string();
	while ( strRoot.size() > 1 && strRoot[ strRoot.size() - 1 ] == '/' )
		strRoot.erase( strRoot.size() - 1 );
	return strRoot + "/src";
}

inline std::string FarmRelative( const std::string &strAbsolute )
{
	const std::string strPrefix = FarmSrc() + "/";
	if ( strAbsolute.compare( 0, strPrefix.size(), strPrefix ) == 0 )
		return strAbsolute.substr( strPrefix.size() );
	const std::string strRepo = RepoRoot() + "/";
	if ( strAbsolute.compare( 0, strRepo.size(), strRepo ) == 0
		&& strAbsolute.compare( 0, strRepo.size() + 5, strRepo + "farm/" ) != 0 )
		return RepoToFarm( strAbsolute.substr( strRepo.size() ) );
	// The pristine clone's src/: libss_particles, tier1 and mathlib build from it.
	const std::string strStock = StockSrc();
	if ( !strStock.empty() && strAbsolute.compare( 0, strStock.size() + 1, strStock + "/" ) == 0 )
		return strAbsolute.substr( strStock.size() + 1 );
	throw Error( strAbsolute + " is neither under the farm's src/ (" + strPrefix + ") nor a file of this repo" );
}

//-----------------------------------------------------------------------------
// THE ANSWER TO "IS X COMPILED INTO T": the sources compile_commands.json
// gives T, which must equal the CMakeLists list exactly (else the database is
// stale, or the list parser broke -- both are failures, never an answer).
//-----------------------------------------------------------------------------
inline std::set< std::string > CompiledSources( const std::string &strTarget )
{
	static std::map< std::string, std::set< std::string > > s_cache;
	const std::string strKey = BuildDir() + "|" + RepoRoot() + "|" + strTarget;
	if ( s_cache.count( strKey ) )
		return s_cache[ strKey ];

	std::set< std::string > compiled;
	const std::vector< CompileEntry > entries = TargetEntries( strTarget );
	for ( size_t i = 0; i < entries.size(); i++ )
		compiled.insert( FarmRelative( entries[ i ].strFile ) );

	const std::vector< std::string > listed = ListedSources( strTarget );
	std::set< std::string > listedSet( listed.begin(), listed.end() );
	if ( BuildConfig() == "debug" )
	{
		const std::vector< std::string > debugListed = DebugListedSources( strTarget );
		listedSet.insert( debugListed.begin(), debugListed.end() );
	}
	if ( listedSet != compiled )
	{
		std::string strWhy = "compile_commands.json disagrees with " + ListFile( strTarget )
			+ " about target '" + strTarget + "' -- a stale build (re-run make), or a list the "
			  "parser misread:";
		int n = 0;
		for ( std::set< std::string >::const_iterator it = listedSet.begin(); it != listedSet.end() && n < 10; ++it )
			if ( !compiled.count( *it ) )
				strWhy += "\n  listed, not compiled: " + *it, n++;
		for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end() && n < 10; ++it )
			if ( !listedSet.count( *it ) )
				strWhy += "\n  compiled, not listed: " + *it, n++;
		throw Error( strWhy );
	}
	s_cache[ strKey ] = compiled;
	return compiled;
}

inline bool Compiles( const std::string &strTarget, const std::string &strFarmRelative )
{
	return CompiledSources( strTarget ).count( strFarmRelative ) > 0;
}

// The compile command of one source of a target; throws if it is not compiled.
inline std::string CompileCommand( const std::string &strTarget, const std::string &strFarmRelative )
{
	const std::vector< CompileEntry > entries = TargetEntries( strTarget );
	for ( size_t i = 0; i < entries.size(); i++ )
		if ( FarmRelative( entries[ i ].strFile ) == strFarmRelative )
			return entries[ i ].strCommand;
	throw Error( strFarmRelative + " is not compiled into '" + strTarget + "'" );
}

// Every -D the target's first source is compiled with, as NAME or NAME=VALUE.
// (CMake gives every source of a target the same definitions unless a
// per-source property says otherwise; none here does.)
inline std::set< std::string > CompileDefinitions( const std::string &strTarget )
{
	const std::vector< CompileEntry > entries = TargetEntries( strTarget );
	std::set< std::string > out;
	std::istringstream toks( entries[ 0 ].strCommand );
	std::string strTok;
	while ( toks >> strTok )
		if ( strTok.compare( 0, 2, "-D" ) == 0 && strTok.size() > 2 )
			out.insert( strTok.substr( 2 ) );
	if ( out.empty() )
		throw Error( "the compile command of '" + strTarget + "' has no -D at all" );
	return out;
}

//-----------------------------------------------------------------------------
// build.ninja's link edge of a target.
//-----------------------------------------------------------------------------
struct LinkEdge
{
	std::string strOutput;                     // e.g. out/server.so
	std::vector< std::string > objects;        // explicit inputs, build-dir-relative
	std::map< std::string, std::string > vars; // LINK_LIBRARIES, LINK_FLAGS, ...
};

inline const std::string &BuildNinja()
{
	static std::map< std::string, std::string > s_cache;
	const std::string strPath = BuildDir() + "/build.ninja";
	if ( !s_cache.count( strPath ) )
		s_cache[ strPath ] = ReadRequired( strPath,
			"CMake's ninja file -- run `make debug` (or release) in this worktree" );
	return s_cache[ strPath ];
}

// A ninja token list with `$ `, `$:` and `$$` unescaped.
inline std::vector< std::string > NinjaTokens( const std::string &strLine )
{
	std::vector< std::string > out;
	std::string cur;
	bool bHave = false;
	for ( size_t i = 0; i < strLine.size(); i++ )
	{
		const char c = strLine[ i ];
		if ( c == '$' && i + 1 < strLine.size() )
		{
			cur += strLine[ ++i ];
			bHave = true;
		}
		else if ( c == ' ' || c == '\t' )
		{
			if ( bHave )
				out.push_back( cur );
			cur.clear();
			bHave = false;
		}
		else
		{
			cur += c;
			bHave = true;
		}
	}
	if ( bHave )
		out.push_back( cur );
	return out;
}

inline LinkEdge Link( const std::string &strTarget )
{
	const std::string &g = BuildNinja();
	// CMake names the rule <LANG>_<KIND>_LINKER__<target>_<Config>.
	const std::string strShared = "_LIBRARY_LINKER__" + strTarget + "_";

	std::istringstream in( g );
	std::string strLine;
	LinkEdge edge;
	bool bFound = false;
	while ( std::getline( in, strLine ) )
	{
		// Join ninja's `$`-newline continuations.
		while ( !strLine.empty() && strLine[ strLine.size() - 1 ] == '$'
			&& ( strLine.size() < 2 || strLine[ strLine.size() - 2 ] != '$' ) )
		{
			std::string strNext;
			if ( !std::getline( in, strNext ) )
				break;
			strLine = strLine.substr( 0, strLine.size() - 1 ) + Trim( strNext );
		}

		if ( !bFound )
		{
			if ( strLine.compare( 0, 6, "build " ) != 0 )
				continue;
			const std::vector< std::string > toks = NinjaTokens( strLine.substr( 6 ) );
			// toks: out... ':' is attached to the last output as "out:" (the
			// `:` of an output path is escaped as `$:` and was unescaped).
			size_t k = 0;
			std::string strOut;
			for ( ; k < toks.size(); k++ )
			{
				if ( !toks[ k ].empty() && toks[ k ][ toks[ k ].size() - 1 ] == ':' )
				{
					strOut = toks[ k ].substr( 0, toks[ k ].size() - 1 );
					k++;
					break;
				}
			}
			if ( k >= toks.size() || toks[ k ].find( strShared ) == std::string::npos
				|| toks[ k ].find( "_LIBRARY_LINKER__" ) == std::string::npos )
				continue;
			// The rule is exactly <..>_LIBRARY_LINKER__<target>_<Config>: make
			// sure <target> is not merely a prefix of another target's name.
			const std::string &strRule = toks[ k ];
			const size_t nAt = strRule.find( strShared );
			const std::string strRest = strRule.substr( nAt + strShared.size() );
			if ( strRest.find( '_' ) != std::string::npos )
				continue;
			edge.strOutput = strOut;
			for ( k++; k < toks.size(); k++ )
			{
				if ( toks[ k ] == "|" || toks[ k ] == "||" )
					break;
				edge.objects.push_back( toks[ k ] );
			}
			bFound = true;
			continue;
		}

		// The edge's variables are the indented lines right after it.
		if ( strLine.empty() || ( strLine[ 0 ] != ' ' && strLine[ 0 ] != '\t' ) )
			break;
		const size_t nEq = strLine.find( " = " );
		if ( nEq != std::string::npos )
			edge.vars[ Trim( strLine.substr( 0, nEq ) ) ] = strLine.substr( nEq + 3 );
	}
	if ( !bFound )
		throw Error( "build.ninja has no link edge for target '" + strTarget + "'" );
	if ( edge.objects.empty() )
		throw Error( "build.ninja's link edge for '" + strTarget + "' names no object" );
	return edge;
}

// The objects on the target's link edge, build-dir-relative (CMake names an
// object CMakeFiles/<target>.dir/<mangled source path>.o).
inline std::vector< std::string > LinkObjects( const std::string &strTarget )
{
	return Link( strTarget ).objects;
}

// The LINK_LIBRARIES of the edge, tokenised (-lfoo, lib/libtier1.a, -Xlinker, ...).
inline std::vector< std::string > LinkLibraries( const std::string &strTarget )
{
	const LinkEdge edge = Link( strTarget );
	std::map< std::string, std::string >::const_iterator it = edge.vars.find( "LINK_LIBRARIES" );
	if ( it == edge.vars.end() || Trim( it->second ).empty() )
		throw Error( "build.ninja's link edge for '" + strTarget + "' has no LINK_LIBRARIES" );
	return NinjaTokens( it->second );
}

} // namespace cmake_graph

#endif // SOURCESHARP_TEST_CMAKE_GRAPH_H
