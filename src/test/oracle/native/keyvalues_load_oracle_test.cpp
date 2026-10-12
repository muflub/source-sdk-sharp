//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// THE KEYVALUES TEXT LOAD ORACLE (plan_client_novalve C9, KeyValues K4).
//
// Pristine tier1/KeyValues.cpp's LoadFromBuffer over every case in
// src/test/client/Fixtures/keyvalues-load-cases.tsv, each tree dumped as one
// line a key, held to the golden keyvalues-load-stock.tsv that the managed
// loader (SourceSharp.Client.KeyValuesLoad) replays. The cases are written to
// reach what a load DECIDES: the type each value is given (int, float, uint64
// or string, by strtol and strtod), conditionals on values, blocks and roots,
// comments, the parse's error exits, EOF in every position, and the token cap.
//
// LoadFromBuffer is given no filesystem, as the cases have no files to
// include: #include and #base are read and ignored, which is stock's own
// behaviour then. Conditionals are evaluated (the default); escape sequences
// are not (the default), which is how CEngineSprite's LoadFromFile reads a
// .vmt. SteamDeck and SteamTenFoot are unset first, so [$DECK] is false.
//
// One line a key, in preorder: case, depth, name, type, value. The name is
// GetName(), which comes from KeyValuesSystem()'s symbol table: the table is
// case-insensitive and process-wide, so a name's casing is that of its first
// use in this run. The managed facts compare names case-insensitively for
// that reason, and only for that.
//=============================================================================//

#include "gtest/gtest.h"

#include "tier1/KeyValues.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{
std::string RepoPath( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

int Hex( char c )
{
	if ( c >= '0' && c <= '9' ) return c - '0';
	if ( c >= 'a' && c <= 'f' ) return c - 'a' + 10;
	if ( c >= 'A' && c <= 'F' ) return c - 'A' + 10;
	return -1;
}

// The corpus' escapes: \n \r \t \v \f \\ and \xHH.
bool Unescape( const std::string &in, std::string &out )
{
	out.clear();
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( in[i] != '\\' ) { out += in[i]; continue; }
		if ( ++i >= in.size() ) return false;
		switch ( in[i] )
		{
		case 'n': out += '\n'; break;
		case 'r': out += '\r'; break;
		case 't': out += '\t'; break;
		case 'v': out += '\v'; break;
		case 'f': out += '\f'; break;
		case '\\': out += '\\'; break;
		case 'x':
		{
			if ( i + 2 >= in.size() ) return false;
			const int hi = Hex( in[i + 1] ), lo = Hex( in[i + 2] );
			if ( hi < 0 || lo < 0 ) return false;
			out += (char)( hi * 16 + lo );
			i += 2;
			break;
		}
		default: return false;
		}
	}
	return true;
}

// The golden's escapes, the same set, so a value or name never breaks a line.
std::string Escape( const char *psz )
{
	std::string out;
	for ( const unsigned char *p = (const unsigned char *)psz; *p; p++ )
	{
		switch ( *p )
		{
		case '\n': out += "\\n"; break;
		case '\r': out += "\\r"; break;
		case '\t': out += "\\t"; break;
		case '\v': out += "\\v"; break;
		case '\f': out += "\\f"; break;
		case '\\': out += "\\\\"; break;
		default:
			if ( *p < 0x20 || *p >= 0x7f )
			{
				char b[8];
				snprintf( b, sizeof( b ), "\\x%02x", *p );
				out += b;
			}
			else
				out += (char)*p;
		}
	}
	return out;
}

void Dump( KeyValues *kv, int nDepth, const std::string &strCase, std::string &out )
{
	char b[64];
	out += strCase;
	snprintf( b, sizeof( b ), "\t%d\t", nDepth );
	out += b;
	out += Escape( kv->GetName() );
	const int nType = kv->GetDataType();
	snprintf( b, sizeof( b ), "\t%d\t", nType );
	out += b;
	switch ( nType )
	{
	case KeyValues::TYPE_STRING: out += Escape( kv->GetString() ); break;
	case KeyValues::TYPE_INT: snprintf( b, sizeof( b ), "%d", kv->GetInt() ); out += b; break;
	case KeyValues::TYPE_FLOAT:
	{
		const float fl = kv->GetFloat();
		uint32_t u;
		memcpy( &u, &fl, 4 );
		snprintf( b, sizeof( b ), "%08x", u );
		out += b;
		break;
	}
	case KeyValues::TYPE_UINT64: snprintf( b, sizeof( b ), "%016llx", (unsigned long long)kv->GetUint64() ); out += b; break;
	default: break;
	}
	out += "\n";
	for ( KeyValues *sub = kv->GetFirstSubKey(); sub; sub = sub->GetNextKey() )
		Dump( sub, nDepth + 1, strCase, out );
}

std::string RunCorpus( int &nCases )
{
	nCases = 0;
	std::ifstream in( RepoPath( "src/test/client/Fixtures/keyvalues-load-cases.tsv" ) );
	EXPECT_TRUE( in.good() ) << "the corpus is missing";
	std::string out = "# pristine KeyValues::LoadFromBuffer: case | depth | name | type (0 none, 1 string, 2 int, 3 float, 7 uint64) | value\n"
	                  "# (float as its bits; names and strings escaped as the corpus is)\n";
	std::string line;
	while ( std::getline( in, line ) )
	{
		if ( line.empty() || line[0] == '#' )
			continue;
		const size_t tab = line.find( '\t' );
		EXPECT_NE( tab, std::string::npos ) << "a corpus line with no tab: " << line;
		if ( tab == std::string::npos )
			continue;
		const std::string strCase = line.substr( 0, tab );
		std::string text;
		EXPECT_TRUE( Unescape( line.substr( tab + 1 ), text ) ) << "case " << strCase << " does not unescape";

		KeyValues *root = new KeyValues( "vmt" );
		root->LoadFromBuffer( "case.vmt", text.c_str(), NULL, NULL );
		for ( KeyValues *k = root; k; k = k->GetNextKey() )
			Dump( k, 0, strCase, out );
		root->deleteThis();
		nCases++;
	}
	return out;
}
} // namespace

TEST( KeyValuesLoadOracle, StockProducesTheGolden )
{
	unsetenv( "SteamDeck" );
	unsetenv( "SteamTenFoot" );

	int nCases = 0;
	const std::string run = RunCorpus( nCases );
	ASSERT_GE( nCases, 100 ) << "the corpus did not load";

	const std::string golden = RepoPath( "src/test/client/Fixtures/keyvalues-load-stock.tsv" );
	if ( std::ifstream( RepoPath( "src/test/oracle/native/keyvalues_golden.regen" ) ).good() )
		std::ofstream( golden ) << run;

	std::ifstream in( golden );
	ASSERT_TRUE( in.good() ) << golden << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	ASSERT_GT( ss.str().size(), 4000u ) << "the golden is empty or truncated";
	ASSERT_TRUE( ss.str() == run ) << "pristine LoadFromBuffer no longer writes the golden; do NOT regenerate it to pass";
}
