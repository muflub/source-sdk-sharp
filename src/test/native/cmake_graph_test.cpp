//========= SourceSharp ============//
//
// cmake_graph.h, the one reader every "is X compiled / linked" test stands on.
//
// Two halves. Against the REAL build: the readers find the three game targets
// and agree with each other. Against FIXTURES in a temp dir: every way the
// input can be missing, empty, stale or misread is an Error, never an answer --
// a reader that found nothing must not let an absence assertion pass.
//=============================================================================//

#include "cmake_graph.h"

#include <gtest/gtest.h>

#include <stdlib.h>
#include <sys/stat.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

using namespace cmake_graph;

//-----------------------------------------------------------------------------
// The real build.
//-----------------------------------------------------------------------------

TEST( CMakeGraphReal, EachGameTargetCompilesItsOwnShim )
{
	// Positive controls, one per target: a file the target is made of.
	EXPECT_TRUE( Compiles( kServer, "sourcesharp/game/server/shell_gamedll.cpp" ) );
	EXPECT_TRUE( Compiles( kClient, "sourcesharp/game/client/shell_clientdll.cpp" ) );
	EXPECT_TRUE( Compiles( kHost, "sourcesharp/host/sourcesharp.cpp" ) );
	// ...and not the other side's.
	EXPECT_FALSE( Compiles( kServer, "sourcesharp/game/client/shell_clientdll.cpp" ) );
	EXPECT_FALSE( Compiles( kClient, "sourcesharp/game/server/shell_gamedll.cpp" ) );
}

// The lists name this repo's files where they live; every caller asks by the
// farm spelling, so the map back through tools/module-map.tsv takes the MOST
// SPECIFIC row (a `file` row over src/shared/native's broad `files` row) and
// refuses a path no row links.
TEST( CMakeGraphReal, ALocalPathMapsBackToItsFarmSpelling )
{
	EXPECT_EQ( "sourcesharp/game/server/physics.cpp", RepoToFarm( "src/server/native/physics.cpp" ) );
	EXPECT_EQ( "sourcesharp/game/shared/shell_shared.cpp", RepoToFarm( "src/shared/native/shell_shared.cpp" ) );
	EXPECT_EQ( "sourcesharp/public/dt_send.cpp", RepoToFarm( "src/shared/native/dt_send.cpp" ) );
	EXPECT_EQ( "sourcesharp/game/server/shell_gamedll.cpp",
	           FarmRelative( RepoRoot() + "/src/server/native/shell_gamedll.cpp" ) );
	bool bThrew = false;
	try { RepoToFarm( "docs2/nowhere.cpp" ); } catch ( const std::exception & ) { bThrew = true; }
	EXPECT_TRUE( bThrew ) << "a local path no module-map row links was given a farm spelling";
	bThrew = false;
	try { ListToken( "${UNKNOWN}/x.cpp", kServer ); } catch ( const std::exception & ) { bThrew = true; }
	EXPECT_TRUE( bThrew ) << "an unreadable source-list entry was accepted";
}

TEST( CMakeGraphReal, TheLinkEdgeNamesOneObjectPerCompiledSource )
{
	// Two independent readings: compile_commands.json's entries and
	// build.ninja's link edge. They must count the same objects.
	const char *const targets[] = { kServer, kClient, kHost };
	for ( size_t i = 0; i < 3; i++ )
	{
		const std::vector< std::string > objects = LinkObjects( targets[ i ] );
		EXPECT_EQ( CompiledSources( targets[ i ] ).size(), objects.size() ) << targets[ i ];
		for ( size_t j = 0; j < objects.size(); j++ )
			EXPECT_NE( objects[ j ].find( std::string( "CMakeFiles/" ) + targets[ i ] + ".dir/" ),
				std::string::npos ) << objects[ j ];
	}
}

TEST( CMakeGraphReal, TheGameDllsLinkTheHost )
{
	const std::vector< std::string > server = LinkLibraries( kServer );
	const std::vector< std::string > client = LinkLibraries( kClient );
	EXPECT_NE( std::find( server.begin(), server.end(), "out/libmanaged_host.so" ), server.end() );
	EXPECT_NE( std::find( client.begin(), client.end(), "out/libmanaged_host.so" ), client.end() );
	EXPECT_EQ( Link( kServer ).strOutput, "out/server.so" );
	EXPECT_EQ( Link( kClient ).strOutput, "out/client.so" );
	EXPECT_EQ( Link( kHost ).strOutput, "out/libmanaged_host.so" );
}

TEST( CMakeGraphReal, TheServerIsCompiledWithItsOwnDefinitions )
{
	const std::set< std::string > defs = CompileDefinitions( kServer );
	EXPECT_TRUE( defs.count( "GAME_DLL" ) );
	EXPECT_TRUE( defs.count( "SOURCESHARP" ) );
	EXPECT_FALSE( defs.count( "CLIENT_DLL" ) );
	const std::vector< std::string > listed = ListedDefinitions( kServer );
	EXPECT_NE( std::find( listed.begin(), listed.end(), "GAME_DLL" ), listed.end() );
}

TEST( CMakeGraphReal, EveryFileThisTreeCompilesTurnsAWarningIntoAnError )
{
	// Zero warnings holds by construction only if a warning stops the build:
	// ninja prints one on the build that compiles the file and never again,
	// because the object stays. -Werror leaves no object to keep. Every entry
	// but googletest's own (fetched, not ours) must carry it, and none may carve
	// an exemption back out with -Wno-error.
	int nChecked = 0;
	std::vector< std::string > missing, exempted;
	for ( const CompileEntry &e : CompileCommands() )
	{
		if ( e.strFile.find( "/_deps/" ) != std::string::npos )
			continue;
		nChecked++;
		const std::string cmd = " " + e.strCommand + " ";
		if ( cmd.find( " -Werror " ) == std::string::npos )
			missing.push_back( e.strFile );
		if ( cmd.find( " -Wno-error" ) != std::string::npos )
			exempted.push_back( e.strFile );
	}
	EXPECT_GT( nChecked, 500 ) << "compile_commands.json names too few files to be this tree";
	EXPECT_TRUE( missing.empty() ) << missing.size() << " file(s) compile without -Werror, e.g. "
	                               << ( missing.empty() ? "" : missing.front() );
	EXPECT_TRUE( exempted.empty() ) << exempted.size() << " file(s) carry a -Wno-error, e.g. "
	                                << ( exempted.empty() ? "" : exempted.front() );
}

//-----------------------------------------------------------------------------
// Fixtures: a fake repo + build dir, pointed at through the env contract.
//-----------------------------------------------------------------------------

class CMakeGraphFixture : public ::testing::Test
{
protected:
	std::string m_strRoot;

	void SetUp() override
	{
		char szTemplate[] = "/tmp/cmake_graph_test.XXXXXX";
		ASSERT_NE( mkdtemp( szTemplate ), (char *)NULL );
		m_strRoot = szTemplate;
		mkdir( ( m_strRoot + "/repo" ).c_str(), 0755 );
		mkdir( ( m_strRoot + "/repo/cmake" ).c_str(), 0755 );
		mkdir( ( m_strRoot + "/repo/cmake/sdk" ).c_str(), 0755 );
		mkdir( ( m_strRoot + "/farm" ).c_str(), 0755 );
		mkdir( ( m_strRoot + "/farm/build" ).c_str(), 0755 );
		setenv( "SOURCESHARP_REPO", ( m_strRoot + "/repo" ).c_str(), 1 );
		setenv( "SOURCESHARP_BUILD_DIR", ( m_strRoot + "/farm/build" ).c_str(), 1 );
	}

	void TearDown() override
	{
		const std::string cmd = "rm -rf '" + m_strRoot + "'";
		ASSERT_EQ( 0, system( cmd.c_str() ) );
	}

	void Write( const std::string &strRel, const std::string &strText )
	{
		std::ofstream f( ( m_strRoot + "/" + strRel ).c_str() );
		f << strText;
	}

	// raytrace, because its fixture file name is the only one ListFile derives.
	void WriteList( const std::string &strBody )
	{
		Write( "repo/cmake/sdk/raytrace.cmake",
			"# raytrace\nset(raytrace_sources\n" + strBody + ")\n" );
	}

	std::string Entry( const std::string &strSrc )
	{
		return "{\n  \"directory\": \"" + m_strRoot + "/farm/build\",\n"
			"  \"command\": \"/usr/bin/c++ -DX=\\\"y\\\" -c " + m_strRoot + "/farm/src/" + strSrc + "\",\n"
			"  \"file\": \"" + m_strRoot + "/farm/src/" + strSrc + "\",\n"
			"  \"output\": \"cmake/sdk/CMakeFiles/raytrace.dir/__/" + strSrc + ".o\"\n}";
	}

	std::string ErrorOf( void ( *pfn )() )
	{
		try
		{
			pfn();
		}
		catch ( const Error &e )
		{
			return e.what();
		}
		return std::string();
	}
};

void ReadRaytrace()
{
	CompiledSources( "raytrace" );
}

TEST_F( CMakeGraphFixture, AFreshMatchingPairIsRead )
{
	WriteList( "    a/one.cpp\n    a/two.cpp  # a comment\n" );
	const std::vector< std::string > listed = ListedSources( "raytrace" );
	ASSERT_EQ( 2u, listed.size() );
	EXPECT_EQ( "a/one.cpp", listed[ 0 ] );
	EXPECT_EQ( "a/two.cpp", listed[ 1 ] );

	const std::vector< CompileEntry > v =
		ParseCompileCommands( "[\n" + Entry( "a/one.cpp" ) + ",\n" + Entry( "a/two.cpp" ) + "\n]\n" );
	ASSERT_EQ( 2u, v.size() );
	EXPECT_EQ( "a/one.cpp", FarmRelative( v[ 0 ].strFile ) );
	EXPECT_NE( std::string::npos, v[ 0 ].strCommand.find( "-DX=\"y\"" ) ) << v[ 0 ].strCommand;
}

TEST_F( CMakeGraphFixture, AStaleDatabaseIsAnErrorNotAnAnswer )
{
	// The list gained a file the last build never compiled (or lost one it
	// still does): the database cannot answer for this tree.
	WriteList( "    a/one.cpp\n    a/two.cpp\n" );
	Write( "farm/build/compile_commands.json", "[\n" + Entry( "a/one.cpp" ) + "\n]\n" );
	const std::string strWhy = ErrorOf( ReadRaytrace );
	EXPECT_NE( std::string::npos, strWhy.find( "listed, not compiled: a/two.cpp" ) ) << strWhy;
}

TEST_F( CMakeGraphFixture, AMatchingDatabaseAnswersBothWays )
{
	WriteList( "    a/one.cpp\n" );
	Write( "farm/build/compile_commands.json", "[\n" + Entry( "a/one.cpp" ) + "\n]\n" );
	EXPECT_TRUE( Compiles( "raytrace", "a/one.cpp" ) );
	EXPECT_FALSE( Compiles( "raytrace", "a/two.cpp" ) );
	EXPECT_NE( std::string::npos, CompileCommand( "raytrace", "a/one.cpp" ).find( " -c " ) );
	EXPECT_THROW( CompileCommand( "raytrace", "a/two.cpp" ), Error );
}

TEST_F( CMakeGraphFixture, ATargetTheDatabaseDoesNotNameIsAnError )
{
	// A database of some other target's objects says nothing about this one.
	Write( "farm/build/compile_commands.json", "[\n" + Entry( "a/one.cpp" ) + "\n]\n" );
	EXPECT_THROW( TargetEntries( "mathlib" ), Error );
	EXPECT_THROW( CompileDefinitions( "mathlib" ), Error );
	EXPECT_EQ( 1u, TargetEntries( "raytrace" ).size() );
}

TEST_F( CMakeGraphFixture, AMissingDatabaseIsAnErrorNotAnEmptySet )
{
	WriteList( "    a/one.cpp\n" );
	const std::string strWhy = ErrorOf( ReadRaytrace );
	EXPECT_NE( std::string::npos, strWhy.find( "compile_commands.json" ) ) << strWhy;
}

TEST_F( CMakeGraphFixture, AnEmptyDatabaseIsAnError )
{
	EXPECT_THROW( ParseCompileCommands( "[\n]\n" ), Error );
	Write( "farm/build/compile_commands.json", "" );
	EXPECT_THROW( ReadRequired( m_strRoot + "/farm/build/compile_commands.json", "t" ), Error );
}

TEST_F( CMakeGraphFixture, AMissingOrEmptyListIsAnError )
{
	EXPECT_THROW( ListedSources( "raytrace" ), Error );   // no file
	WriteList( "" );
	EXPECT_THROW( ListedSources( "raytrace" ), Error );   // empty block
	Write( "repo/cmake/sdk/raytrace.cmake", "add_library(raytrace STATIC)\n" );
	EXPECT_THROW( ListedSources( "raytrace" ), Error );   // no block
	EXPECT_THROW( ListFile( "not_a_target" ), Error );
}

TEST_F( CMakeGraphFixture, TheReaderRefusesAFileOutsideTheFarm )
{
	EXPECT_THROW( FarmRelative( "/elsewhere/a/one.cpp" ), Error );
}

TEST_F( CMakeGraphFixture, ALinkEdgeIsFoundByItsTargetNotAPrefixOfIt )
{
	Write( "farm/build/build.ninja",
		"build out/x_more.so: CXX_SHARED_LIBRARY_LINKER__x_more_Debug wrong.o\n"
		"  LINK_LIBRARIES = -lwrong\n"
		"\n"
		"build out/x.so: CXX_SHARED_LIBRARY_LINKER__x_Debug a$ b.o c.o | dep.so || order\n"
		"  LINK_LIBRARIES = -lfoo  lib/libbar.a\n"
		"  TARGET_FILE = out/x.so\n"
		"\n" );
	const LinkEdge edge = Link( "x" );
	EXPECT_EQ( "out/x.so", edge.strOutput );
	ASSERT_EQ( 2u, edge.objects.size() );
	EXPECT_EQ( "a b.o", edge.objects[ 0 ] );
	EXPECT_EQ( "c.o", edge.objects[ 1 ] );
	const std::vector< std::string > libs = LinkLibraries( "x" );
	ASSERT_EQ( 2u, libs.size() );
	EXPECT_EQ( "-lfoo", libs[ 0 ] );
	EXPECT_THROW( Link( "absent" ), Error );
}

} // namespace
