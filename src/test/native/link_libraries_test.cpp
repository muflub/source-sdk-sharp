//====== SourceSharp — what the two game DLLs link, and why ==================//
//
// The link-line facts that survived VPC (Phase 6b). Three VPC-era files kept
// them, each beside checks of VPC's own scripts that went with VPC:
//
//   vpc_link_libraries_test.cpp  plan_shim.md step 0f. choreoobjects was
//       dropped (links clean, both sides, both configs); dmxloader STAYS
//       although nothing of OURS imports from it -- particles.a's members
//       demand CDmxElement::/CDmxAttribute::,
//       so the row is demanded TRANSITIVELY, and a zero direct-import reading
//       does not make a library droppable. Removing it breaks the link.
//   vpc_no_protobuf_test.cpp     plan_shim.md step 0a. libprotobuf reached
//       both link lines through two VPC $Includes and nothing else; neither
//       DLL imports a google::protobuf symbol.
//   vpc_platforms_test.cpp       libsteam_api.so is on both link lines. Under
//       VPC it hung off an `[!$WIN64]` condition that READ as 32-bit Windows
//       and was TRUE on POSIX; deleting it as a leftover took Steam off the
//       Linux link.
//
// The subject now is build.ninja's link edge of server_sourcesharp and
// client_sourcesharp (LINK_LIBRARIES), read by cmake_graph.h -- the line the
// linker is actually given, not a script that describes it. A library is
// matched on a whole TOKEN of that line (a -l flag or an archive path's file
// name), never on a substring, so `particles` cannot be satisfied by some
// other path that happens to contain the word.
//
// WHAT THIS FILE DOES NOT ASSERT: the rest of the list. Those libraries are
// demanded today and each leaves when its demanders do; pinning them here
// would make this a test that has to be edited by the commit that changes the
// thing it watches.
//
//=============================================================================//

#include <string>
#include <vector>

#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

const char *const kTargets[] = { cmake_graph::kServer, cmake_graph::kClient };

// The library a LINK_LIBRARIES token names: "-lfoo" -> "foo",
// ".../libfoo.a" / ".../foo.a" / ".../libfoo.so" -> "foo". Anything else
// (flags, -Xlinker, --start-group) -> "".
std::string LibraryOf( const std::string &strTok )
{
	if ( strTok.compare( 0, 2, "-l" ) == 0 )
		return strTok.substr( 2 );
	std::string base = strTok.substr( strTok.find_last_of( '/' ) + 1 );
	const char *const kExts[] = { ".a", ".so" };
	for ( size_t i = 0; i < 2; i++ )
	{
		const std::string ext( kExts[i] );
		if ( base.size() > ext.size() && base.compare( base.size() - ext.size(), ext.size(), ext ) == 0 )
		{
			base = base.substr( 0, base.size() - ext.size() );
			if ( base.compare( 0, 3, "lib" ) == 0 )
				base = base.substr( 3 );
			return base;
		}
	}
	return std::string();
}

bool Links( const char *pszTarget, const char *pszLibrary )
{
	const std::vector<std::string> toks = cmake_graph::LinkLibraries( pszTarget );
	for ( size_t i = 0; i < toks.size(); i++ )
		if ( LibraryOf( toks[i] ) == pszLibrary )
			return true;
	return false;
}

} // namespace

TEST( LinkLibraries, TheMatcherReadsEachSpellingOfALibrary )
{
	EXPECT_EQ( "tier0", LibraryOf( "-ltier0" ) );
	EXPECT_EQ( "tier1", LibraryOf( "lib/libtier1.a" ) );
	EXPECT_EQ( "dmxloader", LibraryOf( "/x/farm/debug/src/lib/public/linux64/dmxloader.a" ) );
	EXPECT_EQ( "steam_api", LibraryOf( "/x/lib/public/linux64/libsteam_api.so" ) );
	EXPECT_EQ( "", LibraryOf( "-Xlinker" ) );
	EXPECT_EQ( "", LibraryOf( "--start-group" ) );
	EXPECT_NE( "particles", LibraryOf( "/x/particles_extra.a" ) );
}

TEST( LinkLibraries, TheMatcherFindsALibraryThatIsDefinitelyThere )
{
	// The positive control: without it every absence below is free. Neither
	// DLL links tier0 or vstdlib now (plan_server_novalve.md P4, plan_client_
	// novalve C9), and libdl is the one library both lines name on every Linux
	// build: loaded_symbol.cpp's dlopen/dlsym, through which both DLLs reach
	// the engine's tier0, needs it.
	EXPECT_TRUE( Links( kTargets[0], "dl" ) ) << kTargets[0];
	EXPECT_TRUE( Links( kTargets[1], "dl" ) ) << kTargets[1];
}

TEST( LinkLibraries, NeitherDllLinksMathlib )
{
	// Both DLLs' mathlib members are mathlib_port.cpp's (held to stock by
	// mathlib_port_stock_test); the archive is off both lines (the client's
	// since plan_client_novalve C9).
	ASSERT_STREQ( "server_sourcesharp", kTargets[0] );
	ASSERT_STREQ( "client_sourcesharp", kTargets[1] );
	EXPECT_FALSE( Links( kTargets[0], "mathlib" ) ) << "server.so links libmathlib.a again";
	EXPECT_FALSE( Links( kTargets[1], "mathlib" ) ) << "client.so links libmathlib.a again";
}

TEST( LinkLibraries, TheServerLinksNoTierLibrary )
{
	// plan_server_novalve.md P4: R2-R4 at zero.
	ASSERT_STREQ( "server_sourcesharp", kTargets[0] );
	for ( const char *pLib : { "tier0", "tier1", "vstdlib" } )
		EXPECT_FALSE( Links( kTargets[0], pLib ) ) << "server.so links " << pLib << " again";
}

TEST( LinkLibraries, TheClientLinksNoTierLibrary )
{
	// plan_client_novalve C9: client.so's tier0 and vstdlib names are its own
	// (tier0_vprof_client.cpp, tier0_thread_client.cpp) or bound to the copies
	// the engine loaded (tier_bindings_client.cpp), and no member of
	// libtier1.a is pulled any more (the last importers, detailobjectsystem.cpp
	// and particlemgr.cpp, left client.so).
	ASSERT_STREQ( "client_sourcesharp", kTargets[1] );
	for ( const char *pLib : { "tier0", "tier1", "vstdlib" } )
		EXPECT_FALSE( Links( kTargets[1], pLib ) ) << "client.so links " << pLib << " again";
}

TEST( LinkLibraries, ChoreoobjectsIsGoneFromBothLinkLines )
{
	for ( size_t t = 0; t < 2; t++ )
		EXPECT_FALSE( Links( kTargets[t], "choreoobjects" ) )
			<< kTargets[t]
			<< " links choreoobjects again. plan_shim.md step 0f "
			   "dropped it: nothing on either side demands it, directly or through "
			   "another archive, and both configs link clean without it.";
}

// plan_particles_so.md: particles.a, and dmxloader.a for it (plan_shim.md 0f:
// particles.a's members demand CDmxElement::/CDmxAttribute::), moved from
// client.so's link line to libss_particles.so's. The positive arm says where
// they went, so the client's absence is a move and not a loss.
TEST( LinkLibraries, TheParticleLibraryLinksParticlesAndDmxloader )
{
	EXPECT_TRUE( Links( "ss_particles", "particles" ) );
	EXPECT_TRUE( Links( "ss_particles", "dmxloader" ) )
		<< "libss_particles.so no longer links dmxloader, but particles.a's members "
		   "demand it: drop it and ld fails with ~10 undefined references.";
}

TEST( LinkLibraries, TheClientLinksNeitherParticlesNorDmxloader )
{
	EXPECT_FALSE( Links( cmake_graph::kClient, "particles" ) )
		<< "client.so links particles.a again: its particle names are forwards "
		   "to libss_particles.so (particles_lib_client.cpp), and a second copy "
		   "would be a second particle system manager.";
	EXPECT_FALSE( Links( cmake_graph::kClient, "dmxloader" ) );
}

TEST( LinkLibraries, TheServerLinksNeitherParticlesNorDmxloader )
{
	// plan_server_novalve.md P2.1: the server read one thing from
	// CParticleSystemMgr -- which systems to always precache -- and reads it in
	// C# now (ParticleManifest.cs). Both archives came off its link group.
	EXPECT_FALSE( Links( cmake_graph::kServer, "particles" ) );
	EXPECT_FALSE( Links( cmake_graph::kServer, "dmxloader" ) );
}

TEST( LinkLibraries, NeitherLinkLineNamesLibprotobuf )
{
	for ( size_t t = 0; t < 2; t++ )
		EXPECT_FALSE( Links( kTargets[t], "protobuf" ) ) << kTargets[t]
														 << " links libprotobuf again. Neither DLL imports a "
															"google::protobuf symbol (plan_shim.md step 0a).";
}

TEST( LinkLibraries, NeitherSideLinksSteamApi )
{
	// Proof of read: the client's link line is parsed (libdl is on it).
	ASSERT_TRUE( Links( cmake_graph::kClient, "dl" ) );

	// plan_client_novalve C6, Steam to managed (lane c6-input): the client's
	// Steam is C#'s too -- ClientSteamContext, the five callbacks, the name
	// filter -- through the same flat API, so client.so neither links
	// libsteam_api nor imports a Steam symbol. (Under VPC the row read
	// [!$WIN64], TRUE on POSIX; it was kept until the last importer left.)
	EXPECT_FALSE( Links( cmake_graph::kClient, "steam_api" ) ) << "the client links libsteam_api again";

	// plan_server_novalve.md P2.5: the server's Steam use is C#'s own, through
	// libsteam_api.so's flat API (SteamGameServerFlat, RTLD_NOLOAD: the
	// engine's copy), so server.so neither links it nor needs it.
	EXPECT_FALSE( Links( cmake_graph::kServer, "steam_api" ) ) << "the server links libsteam_api again";
}

TEST( LinkLibraries, BothGameDllsLinkTheManagedHost )
{
	for ( size_t t = 0; t < 2; t++ )
		EXPECT_TRUE( Links( kTargets[t], "managed_host" ) ) << kTargets[t];
}
