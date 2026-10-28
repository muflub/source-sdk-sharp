// plan_client_novalve C9: tier3.a is off client.so's link. tier_connect.cpp
// defines the tier3 connect and the vgui/mdlcache interface globals it filled,
// and the last import from the archive, CJiggleBones, went with the client's
// managed bones (62's port, on main at 4335ea97a). Neither particles.a nor
// dmxloader.a imports from it (client-novalve census, imports:tier3<-* 0); both
// are libss_particles.so's now (plan_particles_so.md).
// Two facts: the archive is not on the link line, and every tier3.a name
// client.so defines comes from one of our objects, so no member of the
// archive reached the link by any other route.

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
	char buf[ 4096 ];
	while ( fgets( buf, sizeof( buf ), pPipe ) )
		out += buf;
	pclose( pPipe );
	return out;
}

// The names `nm --defined-only` lists with one of pszTypes (T/D/B/R: global
// strong definitions; lower case: local; V/W: weak COMDAT copies, which any
// object may emit and so name no member).
std::set< std::string > Defined( const std::string &strNmArgs, const char *pszTypes = "TtDdBbRrVvWw" )
{
	std::set< std::string > out;
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
		const char t = line[ a + 1 ];
		if ( strchr( pszTypes, t ) == NULL || line[ a + 2 ] != ' ' )
			continue;
		out.insert( line.substr( a + 3 ) );
	}
	return out;
}

std::string Tier3Archive() { return cmake_graph::FarmSrc() + "/lib/public/linux64/tier3.a"; }

bool LinesNameTier3()
{
	const std::vector< std::string > libs = cmake_graph::LinkLibraries( cmake_graph::kClient );
	for ( const std::string &tok : libs )
	{
		const std::string base = tok.substr( tok.find_last_of( '/' ) + 1 );
		if ( tok == "-ltier3" || base == "tier3.a" || base == "libtier3.a" )
			return true;
	}
	return false;
}
} // namespace

TEST( ClientNoTier3, TheArchiveIsNotOnTheLinkLine )
{
	const std::vector< std::string > libs = cmake_graph::LinkLibraries( cmake_graph::kClient );
	// Proof of read: a library that stays is there (particles.a moved to
	// libss_particles.so, plan_particles_so.md; libmathlib.a left too, C9):
	// libdl, which every Linux link line names.
	bool bDl = false;
	for ( const std::string &tok : libs )
		bDl |= tok == "-ldl";
	ASSERT_TRUE( bDl ) << "client_sourcesharp's LINK_LIBRARIES did not name -ldl: nothing was read";

	EXPECT_FALSE( LinesNameTier3() ) << "client.so links tier3.a again (plan_client_novalve C9)";
}

TEST( ClientNoTier3, EveryTier3NameInClientSoIsOneOfOurObjects )
{
	const std::set< std::string > archive = Defined( "'" + Tier3Archive() + "'", "TDBR" );
	ASSERT_GT( archive.size(), 40u ) << Tier3Archive() << " read as nearly empty";

	const std::string strBuild = cmake_graph::BuildDir();
	std::string objs;
	size_t nObjects = 0;
	for ( const std::string &rel : cmake_graph::LinkObjects( cmake_graph::kClient ) )
	{
		if ( rel.size() > 2 && rel.compare( rel.size() - 2, 2, ".o" ) == 0 )
		{
			objs += " '" + ( rel[ 0 ] == '/' ? rel : strBuild + "/" + rel ) + "'";
			nObjects++;
		}
	}
	ASSERT_GT( nObjects, 100u ) << "the client's link edge named too few objects";
	const std::set< std::string > ours = Defined( objs );

	const std::set< std::string > so = Defined( "'" + strBuild + "/out/client.so'" );
	ASSERT_GT( so.size(), 1000u ) << "client.so read as nearly empty";

	int nShared = 0;
	for ( const std::string &name : archive )
	{
		if ( !so.count( name ) )
			continue;
		nShared++;
		EXPECT_TRUE( ours.count( name ) ) << name << " is in client.so and only tier3.a defines it";
	}
	// The stimulus reached the subject: the names ours took over are there
	// (the interface globals tier_connect.cpp fills; the connect pair left
	// with the VR stand-in, its last client reference, lane M1-B).
	EXPECT_GE( nShared, 8 );
	const char *kLocalize = "g_pVGuiLocalize";
	EXPECT_TRUE( so.count( kLocalize ) );
	EXPECT_TRUE( ours.count( kLocalize ) );
}
