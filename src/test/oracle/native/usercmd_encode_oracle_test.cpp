// Pristine game/shared/usercmd.cpp's WriteUsercmd over seeded command pairs,
// recorded as the golden src/test/client/Fixtures/usercmd-encode-stock.tsv
// that the managed encoder (UserCmdCodec, plan_client_novalve C9) replays bit
// for bit. Compiled with pristine bitbuf.cpp, so the bits are stock's at both
// levels. This test asserts stock still produces exactly the golden; it
// changes only when the scenario does, and then on purpose (touch
// usercmd_golden.regen beside this file, run, delete the marker, and say why
// in the commit).

#include "gtest/gtest.h"

#include "cbase.h"
#include "usercmd.h"
#include "tier1/bitbuf.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>

void WriteUsercmd( bf_write *buf, const CUserCmd *to, const CUserCmd *from );

// usercmd.cpp's ReadUsercmd seeds through tier1's MD5_PseudoRandom, which this
// binary's tier1 does not build; the scenario only writes, so it is never called.
unsigned int MD5_PseudoRandom( unsigned int )
{
	ADD_FAILURE() << "the encode scenario reached ReadUsercmd";
	return 0;
}

namespace
{
struct Rng
{
	uint32_t s = 0x2545F491u;

	uint32_t Next()
	{
		s ^= s << 13;
		s ^= s >> 17;
		s ^= s << 5;
		return s;
	}

	int Pick( int lo, int hi ) { return lo + (int)( Next() % (uint32_t)( hi - lo + 1 ) ); }

	float Float()
	{
		switch ( Pick( 0, 3 ) )
		{
			case 0:
				return 0.0f;
			case 1:
				return (float)Pick( -450, 450 );
			// Any bits but a NaN or a subnormal. Whether a subnormal compares equal
			// to zero is the thread's MXCSR (DAZ): the first recording had this
			// binary treat one as zero where the managed test runner did not
			// (case 396). In the game both codecs run on one thread under one mode.
			case 2:
			{
				uint32_t u = Next();
				float f;
				memcpy( &f, &u, 4 );
				return ( f != f || ( u & 0x7f800000u ) == 0 ) ? 1.5f : f;
			}
			default:
				return (float)( (int)Next() % 100000 ) / 7.0f;
		}
	}
};

// A command, and a second one that differs from it in a random set of fields.
void Fill( CUserCmd &c, Rng &r )
{
	c.command_number = (int)r.Next();
	c.tick_count = (int)r.Next();
	c.viewangles.Init( r.Float(), r.Float(), r.Float() );
	c.forwardmove = r.Float();
	c.sidemove = r.Float();
	c.upmove = r.Float();
	c.buttons = (int)r.Next();
	c.impulse = (unsigned char)r.Next();
	c.weaponselect = r.Pick( 0, ( 1 << MAX_EDICT_BITS ) - 1 );
	c.weaponsubtype = r.Pick( 0, 63 );
	c.mousedx = (short)r.Next();
	c.mousedy = (short)r.Next();
}

void Vary( CUserCmd &to, const CUserCmd &from, Rng &r )
{
	to = from;
	const uint32_t mask = r.Next();
	if ( mask & 1 )
		to.command_number = from.command_number + 1;
	else if ( mask & 2 )
		to.command_number = (int)r.Next();
	if ( mask & 4 )
		to.tick_count = from.tick_count + 1;
	else if ( mask & 8 )
		to.tick_count = (int)r.Next();
	if ( mask & 16 )
		to.viewangles[0] = r.Float();
	if ( mask & 32 )
		to.viewangles[1] = r.Float();
	if ( mask & 64 )
		to.viewangles[2] = r.Float();
	if ( mask & 128 )
		to.forwardmove = r.Float();
	if ( mask & 256 )
		to.sidemove = r.Float();
	if ( mask & 512 )
		to.upmove = r.Float();
	if ( mask & 1024 )
		to.buttons = (int)r.Next();
	if ( mask & 2048 )
		to.impulse = (unsigned char)r.Next();
	if ( mask & 4096 )
		to.weaponselect = r.Pick( 0, ( 1 << MAX_EDICT_BITS ) - 1 );
	if ( mask & 8192 )
		to.weaponsubtype = r.Pick( 0, 63 );
	if ( mask & 16384 )
		to.mousedx = (short)r.Next();
	if ( mask & 32768 )
		to.mousedy = (short)r.Next();
}

std::string Fields( const CUserCmd &c )
{
	char b[256];
	uint32_t a[6];
	const float v[3] = { c.viewangles.x, c.viewangles.y, c.viewangles.z };
	memcpy( a, v, 12 );
	memcpy( &a[3], &c.forwardmove, 4 );
	memcpy( &a[4], &c.sidemove, 4 );
	memcpy( &a[5], &c.upmove, 4 );
	snprintf( b, sizeof( b ), "%d\t%d\t%08x\t%08x\t%08x\t%08x\t%08x\t%08x\t%d\t%d\t%d\t%d\t%d\t%d", c.command_number,
			  c.tick_count, a[0], a[1], a[2], a[3], a[4], a[5], c.buttons, (int)c.impulse, c.weaponselect,
			  c.weaponsubtype, (int)c.mousedx, (int)c.mousedy );
	return b;
}

std::string RunScenario()
{
	Rng r;
	std::string out =
		"# WriteUsercmd: case | from (14 fields) | to (14 fields) | start bit | bits written | the bits, LSB-first, as bytes\n"
		"# fields: command_number tick_count pitch yaw roll forward side up (float bits) buttons impulse weaponselect weaponsubtype mousedx mousedy\n";
	for ( int i = 0; i < 4000; i++ )
	{
		CUserCmd from, to;
		if ( i % 10 )
			Fill( from, r ); // one in ten against the all-zero command, as the first of a packet is
		Vary( to, from, r );
		if ( i % 7 == 0 )
			Fill( to, r );

		uint32_t words[64] = {};
		bf_write w( words, sizeof( words ) );
		const int start = r.Pick( 0, 31 );
		w.WriteUBitLong( 0, start );
		WriteUsercmd( &w, &to, &from );
		const int bits = w.GetNumBitsWritten() - start;

		char head[32];
		snprintf( head, sizeof( head ), "%d\t", i );
		out += head + Fields( from ) + "\t" + Fields( to );
		snprintf( head, sizeof( head ), "\t%d\t%d\t", start, bits );
		out += head;
		// The command's bits alone, re-packed from bit 0.
		bf_read rd( words, sizeof( words ) );
		rd.Seek( start );
		unsigned char packed[64] = {};
		rd.ReadBits( packed, bits );
		for ( int k = 0; k < ( bits + 7 ) / 8; k++ )
		{
			char h[4];
			snprintf( h, sizeof( h ), "%02x", packed[k] );
			out += h;
		}
		out += "\n";
	}
	return out;
}

std::string RepoPath( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}
} // namespace

TEST( UserCmdEncodeOracle, StockProducesTheGolden )
{
	const std::string run = RunScenario();
	const std::string golden = RepoPath( "src/test/client/Fixtures/usercmd-encode-stock.tsv" );
	if ( std::ifstream( RepoPath( "src/test/oracle/native/usercmd_golden.regen" ) ).good() )
		std::ofstream( golden ) << run;

	std::ifstream in( golden );
	ASSERT_TRUE( in.good() ) << golden << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	ASSERT_GT( ss.str().size(), 100000u ) << "the golden is empty or truncated";
	ASSERT_TRUE( ss.str() == run ) << "pristine WriteUsercmd no longer writes the golden; do NOT regenerate it to pass";
}
