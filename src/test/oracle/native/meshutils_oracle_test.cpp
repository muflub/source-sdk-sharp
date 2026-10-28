// The five index-buffer generators (public/tier2/meshutils.h) against tier2.a's
// own meshutils.o, linked into the same binary (plan_client_novalve C9). The
// SDK ships no source for them; the archive object is the reference. Ours are
// in ss::meshutils (src/client/native/mesh_index_client.cpp); the archive
// supplies the Valve names. Every count from 0 to 64 at three first vertices,
// each output array compared whole, and the bytes past the count untouched.

#include "gtest/gtest.h"

#include "mesh_index_client.h"
#include "tier2/meshutils.h"

#include <cstring>

namespace
{
typedef void ( *Generator_t )( unsigned short *, int, int );

struct Pair_t
{
	const char *pszName;
	Generator_t ours;
	Generator_t stock;
	int nStep; // counts the stock function is defined for are multiples of this
};

const Pair_t kPairs[] = {
	{ "Sequential", ss::meshutils::GenerateSequential, GenerateSequentialIndexBuffer, 1 },
	{ "Quad", ss::meshutils::GenerateQuad, GenerateQuadIndexBuffer, 6 },
	{ "Polygon", ss::meshutils::GeneratePolygon, GeneratePolygonIndexBuffer, 3 },
	{ "LineStrip", ss::meshutils::GenerateLineStrip, GenerateLineStripIndexBuffer, 2 },
	{ "LineLoop", ss::meshutils::GenerateLineLoop, GenerateLineLoopIndexBuffer, 2 },
};
} // namespace

TEST( MeshUtilsOracle, EveryGeneratorWritesTheArchivesIndices )
{
	int nCompared = 0;
	for ( const Pair_t &pair : kPairs )
	{
		for ( int nFirst : { 0, 7, 65000 } )
		{
			for ( int nCount = 0; nCount <= 64; nCount += pair.nStep )
			{
				unsigned short ours[80], stock[80];
				memset( ours, 0xCD, sizeof( ours ) );
				memset( stock, 0xCD, sizeof( stock ) );
				pair.ours( ours, nCount, nFirst );
				pair.stock( stock, nCount, nFirst );
				ASSERT_EQ( 0, memcmp( ours, stock, sizeof( ours ) ) )
					<< pair.pszName << " count " << nCount << " first " << nFirst;
				nCompared++;
			}
		}
	}
	// The stimulus reached every generator: 65 + 11 + 22 + 33 + 33 counts, x3.
	EXPECT_EQ( ( 65 + 11 + 22 + 33 + 33 ) * 3, nCompared );
}

TEST( MeshUtilsOracle, TheArchiveWritesSomethingForEachGenerator )
{
	// The control: a comparison of two untouched arrays proves nothing.
	for ( const Pair_t &pair : kPairs )
	{
		unsigned short stock[12];
		memset( stock, 0xCD, sizeof( stock ) );
		pair.stock( stock, 6, 3 );
		EXPECT_NE( 0xCDCD, stock[0] ) << pair.pszName;
	}
}
