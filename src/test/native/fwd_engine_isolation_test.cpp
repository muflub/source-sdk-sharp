//========= SourceSharp — libss_fwd_engine.so, as the build links it =========//
//
// plan_engine_forwarders.md. The forwarder library exports its one function
// and nothing else, needs no other library, libc included (its forwarders are
// handed the engine object, so there is nothing to connect to, and it links no
// runtime: NO_RUNTIME in src/fwd/native/CMakeLists.txt), and both game DLLs need
// it by its SONAME, resolved beside them through RUNPATH $ORIGIN, and import
// SSFwdEngine_Table rather than carrying a copy of their own. Read off the
// built files with nm and readelf, as particles_lib_isolation_test reads
// libss_particles.so.
//=============================================================================//

#include <cstdio>
#include <set>
#include <string>

#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{
std::string Run( const std::string &cmd )
{
	FILE *pPipe = popen( ( cmd + " 2>/dev/null" ).c_str(), "r" );
	if ( !pPipe )
		return std::string();
	std::string out;
	char buf[ 4096 ];
	while ( fgets( buf, sizeof( buf ), pPipe ) )
		out += buf;
	pclose( pPipe );
	return out;
}

// The names of `nm <args>` lines whose type letter is one of strTypes.
std::set< std::string > Names( const std::string &strArgs, const std::string &strTypes )
{
	std::set< std::string > out;
	const std::string text = Run( "nm " + strArgs );
	size_t start = 0;
	while ( start < text.size() )
	{
		size_t end = text.find( '\n', start );
		if ( end == std::string::npos )
			end = text.size();
		const std::string line = text.substr( start, end - start );
		start = end + 1;
		if ( line.size() < 20 )
			continue;
		const char t = line[ 17 ];
		if ( strTypes.find( t ) == std::string::npos || line[ 18 ] != ' ' )
			continue;
		std::string name = line.substr( 19 );
		const size_t at = name.find( '@' );
		if ( at != std::string::npos )
			name = name.substr( 0, at );
		out.insert( name );
	}
	return out;
}

std::string Out( const char *pszFile ) { return "'" + cmake_graph::BuildDir() + "/out/" + pszFile + "'"; }
std::string Lib() { return Out( "libss_fwd_engine.so" ); }
} // namespace

TEST( FwdEngineIsolation, TheLibraryExportsItsTableFunctionAndNothingElse )
{
	// Proof of read: the library defines its forwarders locally.
	ASSERT_GE( Names( Lib(), "t" ).size(), 12u ) << "libss_fwd_engine.so read as nearly empty";

	const std::set< std::string > exports = Names( "-D --defined-only " + Lib(), "TDBRVWiu" );
	EXPECT_EQ( std::set< std::string >{ "SSFwdEngine_Table" }, exports );
}

TEST( FwdEngineIsolation, TheLibraryNeedsNoOtherLibraryAndImportsNoSymbol )
{
	// NO_RUNTIME: no SDK library, no C++ runtime, no libc and no system
	// library (libstdc++, libm, libuuid and libc came with the VPC link line),
	// and nothing left to resolve: not even the crt start files' weak
	// references to libc's __cxa_finalize.
	// ::Run: inside a TEST body, Run alone is gtest's Test::Run.
	const std::string lib = ::Run( "readelf -d " + Lib() );
	ASSERT_NE( std::string::npos, lib.find( "Library soname: [libss_fwd_engine.so]" ) )
	    << "readelf read libss_fwd_engine.so as having no dynamic section";

	std::set< std::string > needed;
	const std::string marker = "Shared library: [";
	for ( size_t at = lib.find( marker ); at != std::string::npos; at = lib.find( marker, at + 1 ) )
	{
		const size_t start = at + marker.size();
		needed.insert( lib.substr( start, lib.find( ']', start ) - start ) );
	}
	EXPECT_EQ( std::set< std::string >{}, needed );

	EXPECT_EQ( std::set< std::string >{}, Names( "-D --undefined-only " + Lib(), "Uwv" ) );
}

TEST( FwdEngineIsolation, BothGameLibrariesNeedItByItsSonameAndImportItsTableFunction )
{
	for ( const char *pszDll : { "server.so", "client.so" } )
	{
		const std::string dynamic = ::Run( "readelf -d " + Out( pszDll ) );
		ASSERT_NE( std::string::npos, dynamic.find( "(NEEDED)" ) ) << "readelf read " << pszDll << " as empty";
		EXPECT_NE( std::string::npos, dynamic.find( "Shared library: [libss_fwd_engine.so]" ) ) << pszDll;
		EXPECT_NE( std::string::npos, dynamic.find( "Library runpath: [$ORIGIN]" ) ) << pszDll;
		// A link by path would put the build tree into DT_NEEDED.
		EXPECT_EQ( std::string::npos, dynamic.find( cmake_graph::BuildDir() ) ) << pszDll;

		// Imported, never a copy of its own: the tables are the library's.
		EXPECT_TRUE( Names( "-D -u " + Out( pszDll ), "U" ).count( "SSFwdEngine_Table" ) )
		    << pszDll << " does not import SSFwdEngine_Table";
		EXPECT_FALSE( Names( "-D --defined-only " + Out( pszDll ), "TDBRVWiu" ).count( "SSFwdEngine_Table" ) )
		    << pszDll << " exports a SSFwdEngine_Table of its own";
	}
}
