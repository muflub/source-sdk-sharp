// plan_client_novalve C9: bitmap.a and vtf.a are off client.so's link. Their
// last importers were the save-game screenshot (TGAWriter, CVTFTexture) and
// the post-process dump (TGAWriter); both files are now encoded in C#
// (ScreenshotEncoding, held to the pristine archives by
// image_encode_oracle_test) and native only reads back and writes. Two facts
// per archive: it is not on the link line, and no name it defines is in
// client.so (nothing of either is ours, so the match must be empty).

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

std::set<std::string> Defined( const std::string &strNmArgs, const char *pszTypes )
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
		const size_t a = line.find( ' ' );
		if ( a == std::string::npos || a + 3 > line.size() )
			continue;
		if ( strchr( pszTypes, line[a + 1] ) == NULL || line[a + 2] != ' ' )
			continue;
		out.insert( line.substr( a + 3 ) );
	}
	return out;
}

bool LinksArchive( const char *pszBase )
{
	for ( const std::string &tok : cmake_graph::LinkLibraries( cmake_graph::kClient ) )
	{
		const std::string base = tok.substr( tok.find_last_of( '/' ) + 1 );
		if ( base == std::string( pszBase ) + ".a" || base == std::string( "lib" ) + pszBase + ".a" ||
			 tok == std::string( "-l" ) + pszBase )
			return true;
	}
	return false;
}
} // namespace

TEST( ClientNoBitmapVtf, NeitherArchiveIsOnTheLinkLine )
{
	// Proof of read: libdl, which every Linux link line names (libmathlib.a,
	// the control this used, left client.so's line in C9).
	bool bDl = false;
	for ( const std::string &tok : cmake_graph::LinkLibraries( cmake_graph::kClient ) )
		bDl |= tok == "-ldl";
	ASSERT_TRUE( bDl ) << "client_sourcesharp's LINK_LIBRARIES did not name -ldl: nothing was read";

	EXPECT_FALSE( LinksArchive( "bitmap" ) ) << "client.so links bitmap.a again (plan_client_novalve C9)";
	EXPECT_FALSE( LinksArchive( "vtf" ) ) << "client.so links vtf.a again (plan_client_novalve C9)";
}

TEST( ClientNoBitmapVtf, NoNameEitherArchiveDefinesIsInClientSo )
{
	const std::string strLib = cmake_graph::FarmSrc() + "/lib/public/linux64/";
	const std::set<std::string> so = Defined( "'" + cmake_graph::BuildDir() + "/out/client.so'", "TtDdBbRrVvWw" );
	ASSERT_GT( so.size(), 1000u ) << "client.so read as nearly empty";
	for ( const char *pszArchive : { "bitmap.a", "vtf.a" } )
	{
		const std::set<std::string> archive = Defined( "'" + strLib + pszArchive + "'", "TDBR" );
		ASSERT_GT( archive.size(), 20u ) << pszArchive << " read as nearly empty";
		int nIn = 0;
		for ( const std::string &name : archive )
		{
			if ( so.count( name ) )
			{
				nIn++;
				ADD_FAILURE() << name << " (" << pszArchive << ") is in client.so";
			}
		}
		EXPECT_EQ( 0, nIn ) << pszArchive;
	}
}
