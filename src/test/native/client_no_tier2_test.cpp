// plan_client_novalve C9: tier2.a is off client.so's link. Our own objects
// stopped importing from it (tier_connect.cpp, mesh_index_client.cpp,
// material_reference_client.cpp, managed BeamSegDraw), and what particles.a
// still imports from it is stubbed in particles_lib/particles_tier2_stubs.cpp
// (user ruling 2026-10-12). Two facts: the archive is not on the link line, and every
// tier2.a name client.so defines comes from one of our objects, so no member
// of the archive reached the link by any other route.
//
// plan_particles_so.md: particles.a moved to libss_particles.so with the
// stand-ins, so both facts are held for both libraries, and the stand-ins'
// positive arm is the library's.

#include <cstdio>
#include <cstring>
#include <set>
#include <string>
#include <vector>

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

// The names `nm --defined-only` lists with one of pszTypes (T/D/B/R: global
// strong definitions; lower case: local; V/W: weak COMDAT copies, which any
// object may emit and so name no member).
std::set<std::string> Defined( const std::string &strNmArgs, const char *pszTypes = "TtDdBbRrVvWw" )
{
	std::set<std::string> out;
	const std::string text = Run( "nm --defined-only " + strNmArgs );
	size_t start = 0;
	while ( start < text.size() )
	{
		size_t end = text.find( '\n', start );
		if ( end == std::string::npos )
			end = text.size();
		const std::string line = text.substr( start, end - start );
		start = end + 1;
		// "address type name"
		const size_t a = line.find( ' ' );
		if ( a == std::string::npos || a + 3 > line.size() )
			continue;
		const char t = line[a + 1];
		if ( strchr( pszTypes, t ) == NULL || line[a + 2] != ' ' )
			continue;
		out.insert( line.substr( a + 3 ) );
	}
	return out;
}

std::string Tier2Archive()
{
	return cmake_graph::FarmSrc() + "/lib/public/linux64/tier2.a";
}

struct Lib_t
{
	const char *pszTarget; // the CMake target
	const char *pszOutput; // its link output, build-dir relative
};

const Lib_t kLibs[] = {
	{ cmake_graph::kClient, "out/client.so" },
	{ "ss_particles", "out/libss_particles.so" },
};

bool LinesNameTier2( const char *pszTarget )
{
	const std::vector<std::string> libs = cmake_graph::LinkLibraries( pszTarget );
	for ( const std::string &tok : libs )
	{
		const std::string base = tok.substr( tok.find_last_of( '/' ) + 1 );
		if ( tok == "-ltier2" || base == "tier2.a" || base == "libtier2.a" )
			return true;
	}
	return false;
}
} // namespace

TEST( ClientNoTier2, TheArchiveIsNotOnTheLinkLine )
{
	for ( const Lib_t &lib : kLibs )
	{
		const std::vector<std::string> libs = cmake_graph::LinkLibraries( lib.pszTarget );
		// Proof of read: a library that stays is there: libdl, which every
		// Linux link line names (libmathlib.a left client.so's, C9).
		bool bDl = false;
		for ( const std::string &tok : libs )
			bDl |= tok == "-ldl";
		ASSERT_TRUE( bDl ) << lib.pszTarget << "'s LINK_LIBRARIES did not name -ldl: nothing was read";

		EXPECT_FALSE( LinesNameTier2( lib.pszTarget ) )
			<< lib.pszTarget << " links tier2.a again (plan_client_novalve C9, user ruling 2026-10-12)";
	}
}

TEST( ClientNoTier2, EveryTier2NameInEachLibraryIsOneOfOurObjects )
{
	const std::set<std::string> archive = Defined( "'" + Tier2Archive() + "'", "TDBR" );
	ASSERT_GT( archive.size(), 50u ) << Tier2Archive() << " read as nearly empty";

	const std::string strBuild = cmake_graph::BuildDir();
	const char *const kStart = "_ZN12CBeamSegDraw5StartEP17IMatRenderContextiP9IMaterialP12CMeshBuilderi";
	for ( const Lib_t &lib : kLibs )
	{
		std::string objs;
		size_t nObjects = 0;
		for ( const std::string &rel : cmake_graph::LinkObjects( lib.pszTarget ) )
		{
			if ( rel.size() > 2 && rel.compare( rel.size() - 2, 2, ".o" ) == 0 )
			{
				objs += " '" + ( rel[0] == '/' ? rel : strBuild + "/" + rel ) + "'";
				nObjects++;
			}
		}
		ASSERT_GT( nObjects, 2u ) << lib.pszTarget << "'s link edge named too few objects";
		const std::set<std::string> ours = Defined( objs );

		const std::set<std::string> so = Defined( "'" + strBuild + "/" + lib.pszOutput + "'" );
		ASSERT_GT( so.size(), 1000u ) << lib.pszOutput << " read as nearly empty";

		int nShared = 0;
		for ( const std::string &name : archive )
		{
			if ( !so.count( name ) )
				continue;
			nShared++;
			EXPECT_TRUE( ours.count( name ) ) << name << " is in " << lib.pszOutput << " and only tier2.a defines it";
		}
		// The stimulus reached the subject: the names ours took over are there
		// (the tier2 connect and the generators in client.so; the generators
		// and the stubs particles.a links against in libss_particles.so).
		EXPECT_GT( nShared, 4 ) << lib.pszOutput;
	}
	// The stand-ins particles.a links against are the library's own.
	const std::set<std::string> lib = Defined( "'" + strBuild + "/out/libss_particles.so'" );
	EXPECT_TRUE( lib.count( kStart ) ) << "libss_particles.so has no CBeamSegDraw::Start";
}
