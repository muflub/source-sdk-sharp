// plan_client_novalve C9: three members of tier1.a are no longer linked into
// client.so. ilocalize.cpp.o went when keyvalues_client.cpp defined
// ILocalize::ConvertUnicodeToANSI; KeyValues.cpp.o when it defined the
// KeyValues members client.so's objects (and particles.a's) call; and
// strtools_unicode.cpp.o with them, those two being its only importers;
// checksum_crc.cpp.o when tier1_client.cpp defined the CRC32 functions;
// rangecheckedvar.cpp.o with its g_bDoRangeChecks; interface.cpp.o when
// shell_factory_client.cpp defined InterfaceReg and CreateInterface;
// mempool.cpp.o when tier1_client.cpp defined CUtlMemoryPool; stringpool.cpp.o
// and generichash.cpp.o when the client's game string pool took the server's
// storage (server_string_pool.h); utlstring.cpp.o when particle_manifest.cpp's
// file list became a std::vector.
// tier1.a stays on the line for its other members. Since cmake/sdk/tier1.cmake
// builds only the members some link pulls in (sdk_lib_members_test), five of
// these are not in the archive at all, which is the stronger answer: a member
// that is not built cannot be linked. For those the proof below still holds
// client.so's copy of the name to our objects. The fact, per member:
// every name it defines that client.so also defines comes from one of our
// link-edge objects, so the archive's copy was not linked. A proof of read
// requires names to be shared at all.

#include <cstdio>
#include <cstring>
#include <map>
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

// nm --defined-only's names with one of pszTypes, per member when the input
// is an archive ("member.o:" lines head each member's block).
std::map< std::string, std::set< std::string > > Defined( const std::string &strNmArgs, const char *pszTypes )
{
	std::map< std::string, std::set< std::string > > out;
	std::string member;
	const std::string text = Run( "nm --defined-only " + strNmArgs );
	size_t start = 0;
	while ( start < text.size() )
	{
		size_t end = text.find( '\n', start );
		if ( end == std::string::npos )
			end = text.size();
		const std::string line = text.substr( start, end - start );
		start = end + 1;
		if ( !line.empty() && line.back() == ':' )
		{
			member = line.substr( 0, line.size() - 1 );
			continue;
		}
		const size_t a = line.find( ' ' );
		if ( a == std::string::npos || a + 3 > line.size() )
			continue;
		const char t = line[ a + 1 ];
		if ( strchr( pszTypes, t ) == NULL || line[ a + 2 ] != ' ' )
			continue;
		out[ member ].insert( line.substr( a + 3 ) );
	}
	return out;
}
} // namespace

TEST( ClientTier1Members, TheLeftMembersNamesAreOurs )
{
	const std::string strBuild = cmake_graph::BuildDir();
	const auto archive = Defined( "'" + strBuild + "/lib/libtier1.a'", "TDBR" );
	ASSERT_GT( archive.size(), 12u ) << "libtier1.a read as nearly empty";   // 17 members since tier1.cmake builds only the linked ones

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
	std::set< std::string > ours;
	for ( const auto &kv : Defined( objs, "TtDdBbRrVvWw" ) )
		ours.insert( kv.second.begin(), kv.second.end() );

	const auto so = Defined( "'" + strBuild + "/out/client.so'", "TtDdBbRrVvWw" );
	ASSERT_EQ( 1u, so.size() );
	const std::set< std::string > &inSo = so.begin()->second;
	ASSERT_GT( inSo.size(), 1000u ) << "client.so read as nearly empty";

	const struct { const char *pszMember; int nMinShared; const char *pszProof; } kMembers[] = {
		{ "KeyValues.cpp.o", 15, "_ZN9KeyValues6SetIntEPKci" },
		{ "ilocalize.cpp.o", 1, "_ZN9ILocalize20ConvertUnicodeToANSIEPKwPci" },
		{ "strtools_unicode.cpp.o", 0, NULL },
		{ "checksum_crc.cpp.o", 3, "_Z19CRC32_ProcessBufferPjPKvi" },
		{ "rangecheckedvar.cpp.o", 0, NULL },	// its reader is cut by --gc-sections; GC_SECTIONS=0 needs ours
		{ "interface.cpp.o", 3, "CreateInterface" },
		{ "mempool.cpp.o", 5, "_ZN14CUtlMemoryPool4FreeEPv" },
		{ "stringpool.cpp.o", 0, NULL },
		{ "generichash.cpp.o", 0, NULL },
		{ "utlstring.cpp.o", 0, NULL },
	};
	size_t nPresent = 0;
	for ( const auto &m : kMembers )
	{
		if ( m.pszProof )
		{
			EXPECT_TRUE( inSo.count( m.pszProof ) ) << m.pszProof;
			EXPECT_TRUE( ours.count( m.pszProof ) ) << m.pszProof;
		}
		auto it = archive.find( m.pszMember );
		if ( it == archive.end() )
			continue;   // not built (tier1.cmake), so not linked
		nPresent++;
		ASSERT_GT( it->second.size(), 3u ) << m.pszMember << " read as nearly empty";
		int nShared = 0;
		for ( const std::string &name : it->second )
		{
			if ( !inSo.count( name ) )
				continue;
			nShared++;
			EXPECT_TRUE( ours.count( name ) ) << name << " is in client.so and only " << m.pszMember << " defines it";
		}
		EXPECT_GE( nShared, m.nMinShared ) << m.pszMember;
	}
	// KeyValues.cpp.o, strtools_unicode, checksum_crc, generichash and
	// utlstring stay in the archive (libss_particles links them), so the
	// per-member read above still runs: none of them would leave it blind.
	EXPECT_GE( nPresent, 5u ) << "too few of the members are in libtier1.a for the read to mean anything";
}
