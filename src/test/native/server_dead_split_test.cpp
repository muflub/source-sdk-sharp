// What the server does not need lives in a client-only file (user ruling
// 2026-10-12): each such symbol beside a shared port is held here, per
// symbol, to "no object on server.so's link imports it and server.so does not
// define it", with the client file that now defines it still on client.so's
// link. A server object that starts calling one again reddens this, and the
// symbol goes back to the shared file.

#include <cstdio>
#include <string>
#include <vector>

#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{
struct Split_t
{
	const char *pszSymbol;      // as nm prints it, mangled
	const char *pszClientFile;  // farm-relative
};

const Split_t kSplits[] = {
	// DevWarning( int level, ... ): dbg.h declares it extern "C".
	{ "DevWarning", "sourcesharp/game/client/tier0_spew_client.cpp" },
	{ "ThreadInMainThread", "sourcesharp/game/client/tier0_thread_client.cpp" },
	// tier1 convar.cpp's constructors only client objects call (C9 tier1).
	{ "_ZN6ConVarC1EPKcS1_iS1_", "sourcesharp/game/client/convar_client.cpp" },
	{ "_ZN6ConVarC1EPKcS1_iS1_PFvP7IConVarS1_fE", "sourcesharp/game/client/convar_client.cpp" },
	{ "_ZN9ConVarRefC1EPKcb", "sourcesharp/game/client/convar_client.cpp" },
	{ "_ZN10ConCommandC1EPKcPFvRK8CCommandES1_iPFiS1_PA64_cE", "sourcesharp/game/client/convar_client.cpp" },
	{ "_ZN10ConCommandC1EPKcPFvvES1_iPFiS1_PA64_cE", "sourcesharp/game/client/convar_client.cpp" },
};

std::string NmRun( const std::string &cmd )
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

std::string ObjectsOf( const char *pszTarget, size_t *pn )
{
	const std::string strBuild = cmake_graph::BuildDir();
	std::string objs;
	*pn = 0;
	for ( const std::string &rel : cmake_graph::LinkObjects( pszTarget ) )
	{
		if ( rel.size() > 2 && rel.compare( rel.size() - 2, 2, ".o" ) == 0 )
		{
			objs += " '" + ( rel[ 0 ] == '/' ? rel : strBuild + "/" + rel ) + "'";
			( *pn )++;
		}
	}
	return objs;
}

bool HasLine( const std::string &text, const std::string &suffix )
{
	size_t start = 0;
	while ( start < text.size() )
	{
		size_t end = text.find( '\n', start );
		if ( end == std::string::npos )
			end = text.size();
		const std::string line = text.substr( start, end - start );
		if ( line.size() >= suffix.size() && line.compare( line.size() - suffix.size(), suffix.size(), suffix ) == 0 )
			return true;
		start = end + 1;
	}
	return false;
}
} // namespace

TEST( ServerDeadSplit, NoServerObjectImportsASplitSymbolAndServerSoDoesNotDefineIt )
{
	size_t nObjects = 0;
	const std::string undef = NmRun( "nm -u" + ObjectsOf( cmake_graph::kServer, &nObjects ) );
	ASSERT_GT( nObjects, 100u ) << "the server's link edge named too few objects";
	// Proof of read: something the server does import.
	ASSERT_TRUE( HasLine( undef, " U malloc" ) ) << "nm -u over the server objects read nothing";

	const std::string so = NmRun( "nm '" + cmake_graph::BuildDir() + "/out/server.so'" );
	ASSERT_GT( so.size(), 10000u ) << "server.so read as nearly empty";

	for ( const Split_t &s : kSplits )
	{
		EXPECT_FALSE( HasLine( undef, std::string( " U " ) + s.pszSymbol ) )
			<< s.pszSymbol << " is imported by a server object again: it goes back to the shared port";
		EXPECT_FALSE( HasLine( so, std::string( " " ) + s.pszSymbol ) )
			<< s.pszSymbol << " is defined in server.so";
	}
}

TEST( ServerDeadSplit, TheClientFileStillDefinesEachSplitSymbol )
{
	for ( const Split_t &s : kSplits )
		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, s.pszClientFile ) )
			<< s.pszClientFile << " left client.so, taking " << s.pszSymbol << " with it";

	const std::string so = NmRun( "nm '" + cmake_graph::BuildDir() + "/out/client.so'" );
	ASSERT_GT( so.size(), 10000u ) << "client.so read as nearly empty";
	for ( const Split_t &s : kSplits )
		EXPECT_TRUE( HasLine( so, std::string( " T " ) + s.pszSymbol ) || HasLine( so, std::string( " t " ) + s.pszSymbol ) )
			<< "client.so does not define " << s.pszSymbol;
}
