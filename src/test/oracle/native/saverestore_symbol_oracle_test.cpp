//-----------------------------------------------------------------------------
// The save block's token table: engineabi/saverestore.h against upstream's
// public/saverestoretypes.h (plan_server_novalve.md P5 wave 9).
//
// The engine and server.so insert tokens into ONE table and look them up by
// slot, so the slot a token lands in is part of the save format: the rewritten
// FindCreateSymbol (and the HashString under it) must place every token where
// upstream's does. Each fact runs the same sequence through both and compares
// every returned slot and the final table.
//-----------------------------------------------------------------------------
#include <gtest/gtest.h>

#include <algorithm>
#include <cstdio>
#include <string>
#include <vector>

void Upstream_InitSymbolTable( char **pTokens, int nTokens );
unsigned short Upstream_FindCreateSymbol( const char *pszToken );
const char *Upstream_StringFromSymbol( int token );
void Ours_InitSymbolTable( char **pTokens, int nTokens );
unsigned short Ours_FindCreateSymbol( const char *pszToken );
const char *Ours_StringFromSymbol( int token );

namespace
{

// The server's table size (shell_savegame.h, SOURCESHARP_SAVE_TOKEN_COUNT).
const int kServerTokens = 0xfff;

// Field and class names of the shape the save code tokenises, plus bytes the
// ASCII corpus would never reach: 0x80-0xff (sign extension in the hash), a
// one-character token, and an empty one.
std::vector< std::string > Corpus()
{
	std::vector< std::string > v;
	const char *kWords[] = { "m_iHealth", "m_vecOrigin", "m_angRotation", "m_hOwnerEntity",
	                         "classname", "targetname", "CBaseEntity", "prop_physics",
	                         "m_flNextThink", "m_iszModelName", "x", "" };
	for ( const char *w : kWords )
		v.push_back( w );
	for ( int i = 0; i < 1500; i++ )
		v.push_back( "field_" + std::to_string( i * 7919 ) );
	for ( int b = 0x80; b <= 0xff; b++ )
	{
		std::string s = "hi";
		s.push_back( static_cast< char >( b ) );
		s += "bit";
		v.push_back( s );
	}
	return v;
}

// A token as printable ASCII: the corpus holds bytes 0x80-0xff, and a raw one
// in a failure message is not UTF-8 (the test runner's log reader needs it to be).
std::string Printable( const std::string &s )
{
	std::string out;
	for ( unsigned char c : s )
	{
		if ( c >= 0x20 && c < 0x7f )
			out.push_back( static_cast< char >( c ) );
		else
		{
			char buf[ 8 ];
			snprintf( buf, sizeof( buf ), "\\x%02x", c );
			out += buf;
		}
	}
	return out;
}

struct Run_t
{
	std::vector< unsigned short > slots;
	std::vector< std::string > table;
};

// Every token in order (each twice: the second is a lookup), the slots
// returned, then the table as StringFromSymbol reads it.
template < class Init, class Find, class Str >
Run_t RunThrough( Init init, Find find, Str str, int nTokens, const std::vector< std::string > &corpus )
{
	std::vector< char * > table( nTokens, nullptr );
	init( table.data(), nTokens );
	Run_t r;
	for ( int pass = 0; pass < 2; pass++ )
		for ( const std::string &s : corpus )
			r.slots.push_back( find( s.c_str() ) );
	for ( int i = 0; i < nTokens; i++ )
	{
		const char *p = str( i );
		r.table.push_back( p ? p : "<null>" );
	}
	return r;
}

} // namespace

TEST( SaveRestoreSymbolOracle, EveryTokenLandsInUpstreamsSlotInTheServersTable )
{
	const std::vector< std::string > corpus = Corpus();
	const Run_t up = RunThrough( Upstream_InitSymbolTable, Upstream_FindCreateSymbol,
	                             Upstream_StringFromSymbol, kServerTokens, corpus );
	const Run_t us = RunThrough( Ours_InitSymbolTable, Ours_FindCreateSymbol,
	                             Ours_StringFromSymbol, kServerTokens, corpus );

	ASSERT_EQ( up.slots.size(), corpus.size() * 2 );
	ASSERT_EQ( us.slots.size(), up.slots.size() );
	for ( size_t i = 0; i < up.slots.size(); i++ )
		ASSERT_EQ( us.slots[ i ], up.slots[ i ] )
			<< "token #" << i << " ('" << Printable( corpus[ i % corpus.size() ] ) << "') lands in a "
			   "different slot than upstream's: a save written by one is misread by the other";
	for ( size_t i = 0; i < up.table.size(); i++ )
		ASSERT_EQ( Printable( us.table[ i ] ), Printable( up.table[ i ] ) ) << "slot " << i;

	// The stimulus reached the subject: distinct slots, many of them, and some
	// tokens had to probe past a taken slot (two hashed alike).
	std::vector< unsigned short > distinct( up.slots.begin(), up.slots.begin() + corpus.size() );
	std::sort( distinct.begin(), distinct.end() );
	distinct.erase( std::unique( distinct.begin(), distinct.end() ), distinct.end() );
	EXPECT_GT( distinct.size(), 1000u ) << "the corpus barely touched the table";
}

TEST( SaveRestoreSymbolOracle, AFullTableAndItsWraparoundAnswerAsUpstreamDoes )
{
	// Seven slots and more than seven tokens: the probe wraps past the end, and
	// once the table is full every new token answers 0, as upstream's does.
	std::vector< std::string > corpus;
	for ( int i = 0; i < 12; i++ )
		corpus.push_back( "t" + std::to_string( i ) );
	const Run_t up = RunThrough( Upstream_InitSymbolTable, Upstream_FindCreateSymbol,
	                             Upstream_StringFromSymbol, 7, corpus );
	const Run_t us = RunThrough( Ours_InitSymbolTable, Ours_FindCreateSymbol,
	                             Ours_StringFromSymbol, 7, corpus );
	EXPECT_EQ( us.slots, up.slots );
	EXPECT_EQ( us.table, up.table );
	EXPECT_EQ( up.slots[ 11 ], 0 ) << "upstream's full table should answer 0";
}

TEST( SaveRestoreSymbolOracle, AnOutOfRangeSlotReadsAsUpstreamsMarker )
{
	std::vector< char * > a( 3, nullptr ), b( 3, nullptr );
	Upstream_InitSymbolTable( a.data(), 3 );
	Ours_InitSymbolTable( b.data(), 3 );
	EXPECT_STREQ( Ours_StringFromSymbol( 3 ), Upstream_StringFromSymbol( 3 ) );
	EXPECT_STREQ( Ours_StringFromSymbol( -1 ), Upstream_StringFromSymbol( -1 ) );
}
