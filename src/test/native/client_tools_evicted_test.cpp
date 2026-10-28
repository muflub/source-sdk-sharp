// SFM is dead (user ruling 2026-10-19): toolframework_client.cpp and
// entity_client_tools.cpp left client.so, and the tools-off stand-ins
// (shell_tools_off_client.cpp) followed with clientshadowmgr.cpp, their last
// importer (C7 cluster 12 S2). Read off the build: none is compiled (the
// shadow manager's shell is, the control that the list was read), and the
// built client.so names neither
// tool interface -- it exports no VCLIENTTOOLS001 and asks the engine for no
// VCLIENTENGINETOOLS001. The interface the engine does ask for by name,
// VClient017, is the positive control that the binary was read.

#include <cstdio>
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
	char buf[4096];
	while ( fgets( buf, sizeof( buf ), pPipe ) )
		out += buf;
	pclose( pPipe );
	return out;
}

// strings -a over the built client.so, one string per line.
std::string ClientStrings()
{
	return Run( "strings -a '" + cmake_graph::BuildDir() + "/out/client.so'" );
}

bool HasLine( const std::string &text, const std::string &line )
{
	return ( "\n" + text ).find( "\n" + line + "\n" ) != std::string::npos;
}
} // namespace

TEST( ClientToolsEvicted, NoToolTuIsCompiled )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_shadows_client.cpp" ) )
		<< "client_sourcesharp does not compile shell_shadows_client.cpp: this is not the client's list";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_tools_off_client.cpp" ) )
		<< "shell_tools_off_client.cpp is compiled into client.so again; its last importer, clientshadowmgr.cpp, "
		   "left at C7 cluster 12 S2";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/toolframework_client.cpp" ) )
		<< "toolframework_client.cpp is compiled into client.so again (SFM is dead, 2026-10-19)";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/entity_client_tools.cpp" ) )
		<< "entity_client_tools.cpp is compiled into client.so again (SFM is dead, 2026-10-19)";
}

TEST( ClientToolsEvicted, TheBuiltClientNamesNoToolInterface )
{
	const std::string text = ClientStrings();
	ASSERT_TRUE( HasLine( text, "VClient017" ) )
		<< "client.so's strings do not hold VClient017, the interface the engine asks it for: nothing was read";
	EXPECT_FALSE( HasLine( text, "VCLIENTTOOLS001" ) )
		<< "client.so carries VCLIENTTOOLS001 again: something exports the tool bridge";
	EXPECT_FALSE( HasLine( text, "VCLIENTENGINETOOLS001" ) )
		<< "client.so carries VCLIENTENGINETOOLS001 again: something asks the engine for the tool framework";
}
