//========= SourceSharp — alloc_check's unload report runs after static destructors
//
// The engine unloads server.so with dlclose. alloc_check's report used to be a
// destructor(65535) function, which at dlclose runs BEFORE the DSO's C++
// static destructors, so every static container's nodes were reported live
// (the P4.8 snapshot's 3505). alloc_unload_probe.so is built like server.so;
// dlclosing it must report exactly its one deliberate leak.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dlfcn.h>
#include <stdlib.h>
#include <unistd.h>

#include <fstream>
#include <regex>
#include <sstream>
#include <string>

TEST( AllocCheckUnload, DlcloseReportsOnlyWhatStaticDestructorsLeftBehind )
{
	char szLog[] = "/tmp/ss-alloc-unload-XXXXXX";
	const int fd = mkstemp( szLog );
	ASSERT_GE( fd, 0 );
	close( fd );
	setenv( "SOURCESHARP_ALLOC_CHECK", "report", 1 );
	setenv( "SOURCESHARP_ALLOC_CHECK_LOG", szLog, 1 );

	void *h = dlopen( SS_ALLOC_UNLOAD_PROBE, RTLD_NOW | RTLD_LOCAL );
	ASSERT_NE( nullptr, h ) << dlerror();
	auto pEntries = (int ( * )())dlsym( h, "AllocUnloadProbeEntries" );
	ASSERT_NE( nullptr, pEntries );
	EXPECT_EQ( 50, pEntries() ) << "the probe's registry did not fill: the stimulus never reached it";
	ASSERT_EQ( 0, dlclose( h ) );

	std::ifstream f( szLog );
	std::stringstream ss;
	ss << f.rdbuf();
	unlink( szLog );
	const std::string strLog = ss.str();

	std::smatch m;
	const std::regex re(
		R"(unload: (\d+) allocations, (\d+) releases, (\d+) mismatches, (\d+) foreign frees, (\d+) live)" );
	ASSERT_TRUE( std::regex_search( strLog, m, re ) ) << "no unload report in:\n" << strLog;
	EXPECT_GT( std::stoul( m[1] ), 50u ) << "the map's nodes were not seen allocated";
	EXPECT_EQ( "0", m[4].str() ) << "foreign frees in the probe:\n" << strLog;
	EXPECT_EQ( "1", m[5].str() )
		<< "the unload report counts " << m[5]
		<< " live blocks; only the one deliberate leak "
		   "should be left once static destructors have run. More means the report ran first.\n"
		<< strLog;
}
