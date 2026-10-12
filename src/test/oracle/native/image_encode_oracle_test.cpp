// Pristine bitmap.a's TGAWriter::WriteToBuffer and vtf.a's CVTFTexture
// Serialize (the prebuilt archives the SDK ships, src/lib/public/linux64:
// it ships no source for either) over seeded pixel buffers, recorded as the
// golden src/test/client/Fixtures/image-encode-stock.tsv that the managed
// encoders (ScreenshotEncoding) replay byte for byte (plan_client_novalve C9:
// bitmap.a and vtf.a off client.so). The cases are the save-game screenshot's
// two files -- a TGA (RGB888 in, RGB888 out) and a VTF (one RGB888 frame,
// TEXTUREFLAGS_NOMIP | NOLOD | SRGB, no mips) -- and the post dump's TGA
// (RGBA8888), at sizes around and at the screenshot's own. One line a case:
// kind, width, height, the input pixels as hex, the file as hex.
// Regenerate only when the cases change (touch image_encode_golden.regen
// beside this file, run, delete the marker, say why in the commit).

#include "gtest/gtest.h"

#include "bitmap/tgawriter.h"
#include "bitmap/imageformat.h"
#include "vtf/vtf.h"
#include "tier1/utlbuffer.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

// tgawriter.o names the filesystem global for its file-writing entry points;
// the buffer form this test calls never touches it.
class IFileSystem;
IFileSystem *g_pFullFileSystem = nullptr;

namespace
{
std::string RepoPath( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

struct Rng
{
	uint32_t s = 0x5EED1234u;
	uint32_t Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
};

std::string Hex( const unsigned char *p, int n )
{
	static const char kDigits[] = "0123456789abcdef";
	std::string out;
	out.reserve( n * 2 );
	for ( int i = 0; i < n; i++ )
	{
		out += kDigits[ p[i] >> 4 ];
		out += kDigits[ p[i] & 15 ];
	}
	return out;
}

std::string RunCases( int &nRefused )
{
	nRefused = 0;
	Rng r;
	std::string out = "# kind | width | height | input pixels (hex) | the file stock wrote (hex)\n";
	struct Case_t { const char *pszKind; int w, h; };
	const Case_t kCases[] = {
		{ "tga-rgb", 1, 1 }, { "tga-rgb", 3, 2 }, { "tga-rgb", 5, 7 }, { "tga-rgb", 16, 16 }, { "tga-rgb", 180, 100 },
		{ "tga-rgb", 256, 128 },
		{ "tga-rgba", 1, 1 }, { "tga-rgba", 4, 3 }, { "tga-rgba", 7, 5 },
		{ "vtf-rgb", 1, 1 }, { "vtf-rgb", 3, 2 }, { "vtf-rgb", 2, 2 }, { "vtf-rgb", 4, 6 }, { "vtf-rgb", 12, 8 }, { "vtf-rgb", 8, 4 }, { "vtf-rgb", 16, 16 }, { "vtf-rgb", 180, 100 },
		{ "vtf-rgb", 256, 128 },
	};
	for ( const Case_t &c : kCases )
	{
		const bool bRgba = std::string( c.pszKind ) == "tga-rgba";
		const int nBpp = bRgba ? 4 : 3;
		std::vector<unsigned char> pixels( c.w * c.h * nBpp );
		for ( unsigned char &b : pixels )
			b = (unsigned char)r.Next();

		CUtlBuffer buffer;
		bool bOk;
		if ( std::string( c.pszKind ) == "vtf-rgb" )
		{
			IVTFTexture *pVTF = CreateVTFTexture();
			const int nFlags = TEXTUREFLAGS_NOMIP | TEXTUREFLAGS_NOLOD | TEXTUREFLAGS_SRGB;
			bOk = pVTF->Init( c.w, c.h, 1, IMAGE_FORMAT_RGB888, nFlags, 1, 1 );
			if ( bOk )
			{
				memcpy( pVTF->ImageData(), pixels.data(), pixels.size() );
				bOk = pVTF->Serialize( buffer );
			}
			DestroyVTFTexture( pVTF );
		}
		else
		{
			const ImageFormat fmt = bRgba ? IMAGE_FORMAT_RGBA8888 : IMAGE_FORMAT_RGB888;
			bOk = TGAWriter::WriteToBuffer( pixels.data(), buffer, c.w, c.h, fmt, fmt );
		}
		// A refusal is part of the record: CVTFTexture::Init takes a side of 1,
		// 2 or a multiple of 4 (180x100 included) and refuses 3x2 and 4x6;
		// the managed writer must refuse what stock does.
		nRefused += !bOk;

		char head[64];
		snprintf( head, sizeof( head ), "%s\t%d\t%d\t", c.pszKind, c.w, c.h );
		out += head;
		out += Hex( pixels.data(), (int)pixels.size() );
		out += "\t";
		out += bOk ? Hex( (const unsigned char *)buffer.Base(), buffer.TellPut() ) : std::string( "REFUSED" );
		out += "\n";
	}
	return out;
}
} // namespace

TEST( ImageEncodeOracle, StockProducesTheGolden )
{
	int nRefused = 0;
	const std::string run = RunCases( nRefused );
	EXPECT_EQ( 2, nRefused ) << "stock refuses the 3x2 and 4x6 VTFs and nothing else here";
	const std::string golden = RepoPath( "src/test/client/Fixtures/image-encode-stock.tsv" );
	if ( std::ifstream( RepoPath( "src/test/oracle/native/image_encode_golden.regen" ) ).good() )
		std::ofstream( golden ) << run;

	std::ifstream in( golden );
	ASSERT_TRUE( in.good() ) << golden << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	ASSERT_GT( ss.str().size(), 100000u ) << "the golden is empty or truncated";
	ASSERT_TRUE( ss.str() == run ) << "pristine bitmap.a / vtf.a no longer write the golden; do NOT regenerate it to pass";
}
