// tier1 and mathlib (cmake/sdk/tier1.cmake, mathlib.cmake) compile only what
// some link pulls in.
//
// They are static libraries built from Valve's source, and a static library's
// member is linked only when something references it. Valve's full lists were
// 43 and 22 files, of which every build tree linked 17 and 9 (2026-10-13): the
// rest were compiled four times per CI run (clang debug and release, gcc in the
// sniper container, MSVC) for nothing. The lists now name the used members.
//
// One direction needs no test: a member a link needs and the list lacks fails
// that link. This holds the other: every consumer of the two archives in this
// build tree is relinked with --trace, which names each archive member the
// linker pulls in, and every member of each archive must be pulled in by one
// of them. Census tier: it relinks about thirty binaries.

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

#include <map>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{

const char *const kArchives[] = { "lib/libtier1.a", "lib/libmathlib.a" };

std::string Capture( const std::string &strCommand, int *pExit )
{
	std::string out;
	FILE *p = popen( ( strCommand + " 2>&1" ).c_str(), "r" );
	if ( p == NULL )
	{
		*pExit = -1;
		return out;
	}
	char buf[ 4096 ];
	size_t n;
	while ( ( n = fread( buf, 1, sizeof( buf ), p ) ) > 0 )
		out.append( buf, n );
	const int status = pclose( p );
	*pExit = WIFEXITED( status ) ? WEXITSTATUS( status ) : -1;
	return out;
}

std::string InBuildDir( const std::string &strCommand )
{
	return "cd '" + cmake_graph::BuildDir() + "' && " + strCommand;
}

// Every link edge in build.ninja that reads one of the two archives: its
// first output.
std::vector< std::string > Consumers()
{
	std::vector< std::string > out;
	std::istringstream in( cmake_graph::BuildNinja() );
	std::string line;
	while ( std::getline( in, line ) )
	{
		if ( line.compare( 0, 6, "build " ) != 0 )
			continue;
		const std::vector< std::string > tokens = cmake_graph::NinjaTokens( line.substr( 6 ) );
		size_t colon = 0;
		while ( colon < tokens.size() && ( tokens[ colon ].empty() || tokens[ colon ].back() != ':' ) )
			colon++;
		if ( colon + 1 >= tokens.size() )
			continue;
		const std::string &rule = tokens[ colon + 1 ];
		if ( rule.find( "_LINKER" ) == std::string::npos || rule.find( "STATIC" ) != std::string::npos )
			continue;
		bool bReads = false;
		for ( size_t i = colon + 2; i < tokens.size(); i++ )
			for ( const char *pszArchive : kArchives )
				bReads = bReads || tokens[ i ] == pszArchive;
		if ( bReads )
		{
			std::string strOut = tokens[ 0 ];
			if ( colon == 0 )
				strOut.pop_back();   // "out/x.so:"
			out.push_back( strOut );
		}
	}
	return out;
}

// The members of an archive, by name.
std::set< std::string > Members( const char *pszArchive )
{
	int nExit = 0;
	const std::string text = Capture( InBuildDir( std::string( "ar t " ) + pszArchive ), &nExit );
	std::set< std::string > out;
	if ( nExit != 0 )
		return out;
	std::istringstream in( text );
	std::string line;
	while ( std::getline( in, line ) )
		if ( !line.empty() )
			out.insert( line );
	return out;
}

// The link of <strOutput> rerun with --trace into a scratch file: each
// archive's members it pulled in, by archive.
std::map< std::string, std::set< std::string > > Pulled( const std::string &strOutput, const std::string &strScratch,
                                                         std::string *pError )
{
	std::map< std::string, std::set< std::string > > out;
	int nExit = 0;
	const std::string commands = Capture( InBuildDir( "ninja -t commands -s '" + strOutput + "'" ), &nExit );
	if ( nExit != 0 )
	{
		*pError = "ninja -t commands " + strOutput + ": " + commands;
		return out;
	}
	// CMake writes the link as ": && <compiler> ... -o <output> ... && :", with
	// the deploy steps chained after it. The compiler's part is the one that
	// names the output with -o.
	std::string link;
	std::string rest = commands;
	const std::string needle = " -o " + strOutput + " ";
	size_t at = 0;
	while ( at <= rest.size() )
	{
		const size_t next = rest.find( "&&", at );
		const std::string part = rest.substr( at, next == std::string::npos ? std::string::npos : next - at );
		if ( ( part + " " ).find( needle ) != std::string::npos )
			link = part;
		if ( next == std::string::npos )
			break;
		at = next + 2;
	}
	if ( link.empty() )
	{
		*pError = "no link command for " + strOutput + " in: " + commands.substr( 0, 400 );
		return out;
	}
	const size_t o = ( link + " " ).find( needle );
	link.replace( o, needle.size() - 1, " -o '" + strScratch + "' -Wl,--trace" );
	const std::string trace = Capture( InBuildDir( link ), &nExit );
	if ( nExit != 0 )
	{
		*pError = "the relink of " + strOutput + " failed: " + trace.substr( 0, 400 );
		return out;
	}
	std::istringstream in( trace );
	std::string line;
	while ( std::getline( in, line ) )
	{
		for ( const char *pszArchive : kArchives )
		{
			const std::string open = std::string( pszArchive ) + "(";
			const size_t p = line.find( open );
			if ( p == std::string::npos || line.back() != ')' )
				continue;
			out[ pszArchive ].insert( line.substr( p + open.size(), line.size() - p - open.size() - 1 ) );
		}
	}
	return out;
}

struct Survey_t
{
	std::vector< std::string > consumers;
	std::map< std::string, std::set< std::string > > pulled;           // archive -> members any link pulled
	std::map< std::string, std::map< std::string, std::set< std::string > > > byConsumer;
	std::vector< std::string > errors;
};

const Survey_t &Survey()
{
	static Survey_t s_survey;
	static bool s_bDone = false;
	if ( s_bDone )
		return s_survey;
	s_bDone = true;
	char dir[] = "/tmp/sdk_lib_members_XXXXXX";
	if ( mkdtemp( dir ) == NULL )
	{
		s_survey.errors.push_back( "mkdtemp failed" );
		return s_survey;
	}
	const std::string strScratch = std::string( dir ) + "/relinked";
	s_survey.consumers = Consumers();
	for ( const std::string &strOutput : s_survey.consumers )
	{
		std::string error;
		const auto pulled = Pulled( strOutput, strScratch, &error );
		if ( !error.empty() )
			s_survey.errors.push_back( error );
		s_survey.byConsumer[ strOutput ] = pulled;
		for ( const auto &a : pulled )
			s_survey.pulled[ a.first ].insert( a.second.begin(), a.second.end() );
	}
	unlink( strScratch.c_str() );
	rmdir( dir );
	return s_survey;
}

} // namespace

TEST( SdkLibMembers, TheGameLibrariesAreAmongTheConsumers )
{
	const Survey_t &s = Survey();
	std::set< std::string > consumers( s.consumers.begin(), s.consumers.end() );
	EXPECT_TRUE( consumers.count( "out/libss_particles.so" ) ) << "the particles library no longer links tier1/mathlib?";
	// Neither game DLL links either archive: server.so's tier1 members are
	// tier1_port.cpp's and mathlib's are mathlib_port.cpp's, and client.so's
	// last importers of both left it (plan_client_novalve C9). Their absence
	// here is that fact read off the graph; the particles library above is the
	// control that the reading still finds a consumer.
	EXPECT_FALSE( consumers.count( "out/client.so" ) ) << "client.so links tier1 or mathlib again";
	EXPECT_FALSE( consumers.count( "out/server.so" ) ) << "server.so links tier1 or mathlib again";
	for ( const std::string &e : s.errors )
		ADD_FAILURE() << e;
}

TEST( SdkLibMembers, TheTraceSeesTheParticlesLibraryPullMembersOfBoth )
{
	// The control: a trace that came back empty would make every member look
	// unused, and the fact below would blame the lists for the instrument.
	const Survey_t &s = Survey();
	const auto it = s.byConsumer.find( "out/libss_particles.so" );
	ASSERT_NE( it, s.byConsumer.end() );
	for ( const char *pszArchive : kArchives )
	{
		const auto a = it->second.find( pszArchive );
		EXPECT_TRUE( a != it->second.end() && !a->second.empty() )
			<< "the particles library's relink named no member of " << pszArchive;
	}
}

TEST( SdkLibMembers, EveryMemberIsPulledInBySomeLink )
{
	const Survey_t &s = Survey();
	for ( const char *pszArchive : kArchives )
	{
		const std::set< std::string > members = Members( pszArchive );
		ASSERT_FALSE( members.empty() ) << "ar t " << pszArchive << " listed nothing";
		const auto pulled = s.pulled.find( pszArchive );
		for ( const std::string &m : members )
			EXPECT_TRUE( pulled != s.pulled.end() && pulled->second.count( m ) )
				<< m << " is in " << pszArchive << " and no link pulls it in: drop its source from "
				<< "cmake/sdk/" << ( std::string( pszArchive ).find( "tier1" ) != std::string::npos ? "tier1" : "mathlib" )
				<< ".cmake (and run tools/sdk-census --write)";
	}
}
