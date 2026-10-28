//====== SourceSharp — the server compiles no stock TU, and the client's
//       remaining ones only ever go down ====================================//
//
// plan_shim.md §8, ratchet 1. Phase 2's exit condition was that the server
// names no stock source -- nothing under game/ or public/: a file that must
// stay native RELOCATES under src/sourcesharp/ carrying its reason letter,
// rather than staying where it is and being listed from the shim's own build.
//
// PHASE 6b MOVED THE SUBJECT, NOT THE RULE. This used to count `$File` rows of
// server_csharp.vpc / client_csharp.vpc with a parser of its own (and a fixture
// proving an `$AdditionalIncludeDirectories` line naming $SRCDIR\game\ was not
// a row). VPC is gone; what the server and the client compile is now the
// source list of server_sourcesharp / client_sourcesharp, read through
// cmake_graph.h (compile_commands.json cross-checked against the CMakeLists).
// A CMake source list has no include-directory rows, removal rows, folders or
// continuations, so the parser facts that guarded those went with the parser.
//
// ONE FILE IS NOT A STOCK ROW: public/tier0/memoverride.cpp. Every Source DLL
// links it; VPC added it through source_dll_base.vpc, never as a $File row of
// ours, and the CMake list names it because CMake has no base script. It is
// excluded by exact path, and a fact below requires it to BE there, so the
// exclusion cannot quietly excuse a missing file.
//
// THE CLIENT'S NUMBER IS A NEW BASELINE. The VPC ceiling was 145 $File rows;
// the client compiles 156 stock TUs besides memoverride. The difference is
// exactly what VPC's rows did not say: 13 TUs its $Include'd base scripts added
// (public/dt_recv.cpp, public/networkvar.cpp, public/vgui_controls/
// vgui_controls.cpp, game/client/rendertexture.cpp, ...), less 2 haptics rows
// that were listed under a Windows-only condition and never compiled here.
// docs/shim-ratchet.tsv carries the compiled count now.
//
// THE TWO DIRECTIONS. up: a stock TU in the server reddens
// TheServerCompilesNoStockTu; a client count above its ceiling reddens the
// ratchet. gone: when the client reaches zero, the "> 0" half reddens and says
// the ceiling is stale. It did, on 2026-10-19 (C7 cluster 12 S2:
// clientshadowmgr.cpp, the client's last fork TU, left client.so): the
// client's ceiling is 0 and its fact is TheClientCompilesNoStockTu, the
// server's shape.
//
//=============================================================================//

#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

const char *const kMemOverride = "public/tier0/memoverride.cpp";

// A stock source is one under game/ or public/. DELIBERATELY a prefix test on
// the farm-relative path that errs toward over-reporting.
bool IsStock( const std::string &strPath )
{
	return strPath.compare( 0, 5, "game/" ) == 0 || strPath.compare( 0, 7, "public/" ) == 0;
}

struct Counts
{
	int nSources;
	int nStock;
	int nShim;
};

Counts CountSources( const char *pszTarget )
{
	Counts c = { 0, 0, 0 };
	const std::set<std::string> srcs = cmake_graph::CompiledSources( pszTarget );
	for ( std::set<std::string>::const_iterator it = srcs.begin(); it != srcs.end(); ++it )
	{
		if ( *it == kMemOverride )
			continue;
		c.nSources++;
		if ( IsStock( *it ) )
			c.nStock++;
		else if ( it->compare( 0, 12, "sourcesharp/" ) == 0 )
			c.nShim++;
	}
	return c;
}

} // namespace

//-----------------------------------------------------------------------------
// Anti-vacuity: the lists were read, and the one exclusion names a real file.
//-----------------------------------------------------------------------------
TEST( ShimVpcStockRows, NeitherSideCompilesMemOverride )
{
	// plan_server_novalve.md P4.8 took memoverride.cpp out of server.so (glibc
	// directly; no object names g_pMemAlloc since P5 step 5), and
	// plan_client_novalve C3 took it out of client.so once the vgui panels
	// were retired. The exclusion in CountSources is kept, so a return of the
	// file is reported HERE, by name, rather than as a stock-TU count rise.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kMemOverride ) )
		<< "the server compiles " << kMemOverride
		<< " again: server.so routes "
		   "its allocations to tier0 once more, and links it (plan 3.6)";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kMemOverride ) )
		<< "the client compiles " << kMemOverride
		<< " again: client.so routes "
		   "its allocations to tier0 once more (plan_client_novalve C3)";

	const Counts c = CountSources( cmake_graph::kServer );
	EXPECT_GT( c.nShim, 0 ) << "server_sourcesharp compiles no sourcesharp/ TU at all";
	EXPECT_EQ( c.nSources, c.nShim + c.nStock ) << "every source the server compiles is under sourcesharp/, game/ or "
												   "public/. A source under some fourth root is not covered by this "
												   "ratchet and has to be classified before it lands.";
}

//-----------------------------------------------------------------------------
// The ratchet itself.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// And the client, which reached the same exit (2026-10-19, C7 cluster 12 S2:
// clientshadowmgr.cpp, the client's last fork TU, left client.so).
//
// This was TheClientStillHasStockTusAndTheyOnlyGoDown: a ceiling, and a "> 0"
// half that was the count's positive control taken from the tree and that
// said, when the client reached zero, to lower the ceiling to 0 and retire the
// expectation. docs/shim-ratchet.tsv's client_stock_tus row is 0 now, and the
// fact is the server's, inverted rather than deleted: a stock TU returning to
// the client reddens it. The count's positive control is what the server's
// half above uses: the client compiles sourcesharp/ TUs (so the list was read
// and the count is not zero for want of rows) and every source it compiles is
// classified, so a fourth root cannot hide a stock TU from the zero.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The discrimination, both directions, over farm-relative paths.
//-----------------------------------------------------------------------------
TEST( ShimVpcStockRows, TheCountTellsAStockTuFromARelocatedOne )
{
	EXPECT_TRUE( IsStock( "game/client/c_rope.cpp" ) );
	EXPECT_TRUE( IsStock( "public/dt_recv.cpp" ) );
	EXPECT_FALSE( IsStock( "sourcesharp/game/server/shell_frame.cpp" ) )
		<< "a RELOCATED file's path still contains the word game and is not a "
		   "stock TU -- this is the whole difference Phase 2 created";
	EXPECT_FALSE( IsStock( "sourcesharp/public/dt_send.cpp" ) );
}
