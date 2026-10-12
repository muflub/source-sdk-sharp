// The KeyValues scenario both halves of the KeyValues oracle run: pristine
// tier1/KeyValues.cpp (keyvalues_members_oracle_test, which records it as
// src/test/client/Fixtures/keyvalues-members-stock.tsv) and client.so's
// members (src/client/native/keyvalues_client.cpp; keyvalues_client_test,
// which replays it). Only the members client.so defines are called: the two
// constructors, the setters, the getters (each over every type a setter can
// give), FindKey's paths through them, AddSubKey and deleteThis. Each step
// writes a line, and the trees are dumped after the setters and again after
// the getters have converted what they convert.

#ifndef KEYVALUES_SCENARIO_H
#define KEYVALUES_SCENARIO_H

#include "tier1/KeyValues.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace keyvalues_scenario
{
inline std::string Escape( const char *psz )
{
	if ( !psz )
		return "(null)";
	std::string out;
	for ( const unsigned char *p = (const unsigned char *)psz; *p; p++ )
	{
		if ( *p < 0x20 || *p >= 0x7f || *p == '\\' )
		{
			char b[8];
			snprintf( b, sizeof( b ), "\\x%02x", *p );
			out += b;
		}
		else
			out += (char)*p;
	}
	return out;
}

inline std::string FloatBits( float fl )
{
	uint32_t u;
	memcpy( &u, &fl, 4 );
	char b[16];
	snprintf( b, sizeof( b ), "%08x", u );
	return b;
}

// The value by type, read from the fields as tier1 stores them (the getters
// convert, so the dump does not call them).
inline void Dump( KeyValues *kv, int nDepth, std::string &out )
{
	char b[128];
	const int nType = kv->GetDataType( NULL );
	snprintf( b, sizeof( b ), "%*s%s\t%d\t", nDepth * 2, "", Escape( kv->GetName() ).c_str(), nType );
	out += b;
	switch ( nType )
	{
	case KeyValues::TYPE_INT: snprintf( b, sizeof( b ), "%d", kv->GetInt( (const char *)NULL ) ); out += b; break;
	case KeyValues::TYPE_FLOAT: out += FloatBits( kv->GetFloat( (const char *)NULL ) ); break;
	case KeyValues::TYPE_STRING: out += Escape( kv->GetString( (const char *)NULL ) ); break;
	default: break;
	}
	out += "\n";
	for ( KeyValues *sub = kv->GetFirstSubKey(); sub; sub = sub->GetNextKey() )
		Dump( sub, nDepth + 1, out );
}

inline std::string Run()
{
	std::string out;
	char b[512];
	auto line = [&]( const std::string &s ) { out += s; out += "\n"; };

	KeyValues *root = new KeyValues( "KvRoot" );
	root->SetInt( "int", 7 );
	root->SetInt( "neg", -2147483647 - 1 );
	root->SetFloat( "float", 2.5f );
	root->SetFloat( "negfloat", -2.75f );
	root->SetFloat( "big", 3.0e9f );
	root->SetString( "str", "hello" );
	root->SetString( "nullstr", NULL );
	root->SetString( "empty", "" );
	root->SetString( "numstr", "  -12abc" );
	root->SetString( "floatstr", "1.5e2x" );
	root->SetString( "hex", "0x1F" );
	root->SetPtr( "ptr", (void *)(uintptr_t)0x12345678ABCDull );
	root->SetUint64( "u64", 0xFEDCBA9876543210ull );
	root->SetUint64( "u64small", 42ull );
	root->SetInt( "path/to/leaf", 3 );
	root->SetString( "path/to/other", "x" );
	root->SetFloat( "path/also", 1.0f );
	root->SetInt( "PATH/TO/LEAF", 4 );			// the symbol table is case-insensitive
	root->SetInt( "trailing/", 5 );
	root->SetInt( "a//b", 6 );
	std::string longName( 300, 'n' );
	root->SetInt( ( longName + "/x" ).c_str(), 8 );	// a 300-character segment, cut at 255
	root->SetInt( "retype", 1 );
	root->SetString( "retype", "now a string" );
	root->SetFloat( "retype", 9.5f );
	root->SetUint64( "retype2", 5ull );
	root->SetString( "retype2", "back to string" );
	root->SetInt( "retype2", 11 );
	root->SetString( "block/child", "c" );
	root->SetInt( "block", 12 );				// a block given a value keeps its children
	root->SetString( "dup", "first" );

	KeyValues *self = new KeyValues( "Self" );
	self->SetInt( NULL, 99 );
	self->SetString( "", "self string" );
	self->SetInt( "kid", 1 );				// the parent becomes a block: TYPE_NONE

	KeyValues *pair = new KeyValues( "Pair", "first", 1, "second", 2 );
	KeyValues *same = new KeyValues( "Same", "k", 1, "k", 2 );

	KeyValues *adopted = new KeyValues( "Adopted" );
	adopted->SetInt( "v", 1 );
	root->AddSubKey( adopted );
	KeyValues *empty = new KeyValues( "EmptyParent" );
	empty->AddSubKey( new KeyValues( "OnlyChild" ) );
	root->AddSubKey( empty );

	line( "== after the setters" );
	for ( KeyValues *k : { root, self, pair, same } )
		Dump( k, 0, out );

	const char *kKeys[] = { "int", "neg", "float", "negfloat", "big", "str", "nullstr", "empty", "numstr", "floatstr", "hex",
	                        "ptr", "u64", "u64small", "path", "path/to/leaf", "path/to", "trailing", "block", "missing",
	                        "missing/deeper", "Adopted", "EmptyParent", NULL, "" };
	const char *kDefault = "DEFAULT";
	line( "== GetDataType, GetInt, GetFloat, then GetString (which converts)" );
	for ( const char *k : kKeys )
	{
		const int nType = root->GetDataType( k );
		const int n = root->GetInt( k, -77 );
		const float fl = root->GetFloat( k, -7.5f );
		snprintf( b, sizeof( b ), "%s\ttype %d\tint %d\tfloat %s", k ? k : "(null)", nType, n, FloatBits( fl ).c_str() );
		line( b );
	}
	for ( const char *k : kKeys )
	{
		const char *psz = root->GetString( k, kDefault );
		snprintf( b, sizeof( b ), "%s\tstring %s\ttype now %d", k ? k : "(null)",
		          psz == kDefault ? "<the default>" : Escape( psz ).c_str(), root->GetDataType( k ) );
		line( b );
	}

	// A string set from its own value is not reallocated.
	const char *pszOwn = root->GetString( "str" );
	root->SetString( "str", pszOwn );
	snprintf( b, sizeof( b ), "SetString from its own value keeps the pointer: %d", root->GetString( "str" ) == pszOwn ? 1 : 0 );
	line( b );

	snprintf( b, sizeof( b ), "names: %s %s %s", Escape( root->GetName() ).c_str(), Escape( pair->GetName() ).c_str(),
	          Escape( root->FindKey( "PATH" ) ? root->FindKey( "PATH" )->GetName() : "(none)" ).c_str() );
	line( b );

	line( "== after the getters" );
	for ( KeyValues *k : { root, self, pair, same } )
		Dump( k, 0, out );

	root->deleteThis();
	self->deleteThis();
	pair->deleteThis();
	same->deleteThis();
	line( "== deleted" );
	return out;
}
} // namespace keyvalues_scenario

#endif // KEYVALUES_SCENARIO_H
