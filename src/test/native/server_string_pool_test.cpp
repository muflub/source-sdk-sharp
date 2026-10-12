//========= SourceSharp — the server's game string pool storage =============//
//
// plan_server_novalve.md P3: CServerStringPool replaced tier1's CStringPool
// under the server's CGameStringPool. These pin what the pool's users rely on:
// one copy per string, case-insensitive, stable until FreeAll, and a copy.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "server_string_pool.h"

TEST( ServerStringPool, TheSameStringTwiceIsOneCopy )
{
	CServerStringPool pool;
	const char *a = pool.Allocate( "info_player_start" );
	const char *b = pool.Allocate( "info_player_start" );
	EXPECT_EQ( a, b );
	EXPECT_EQ( 1, pool.Count() );
}

TEST( ServerStringPool, CaseDoesNotMakeANewString )
{
	CServerStringPool pool;
	const char *a = pool.Allocate( "Prop_Physics" );
	const char *b = pool.Allocate( "prop_physics" );
	EXPECT_EQ( a, b );
	EXPECT_STREQ( "Prop_Physics", b );
	EXPECT_EQ( 1, pool.Count() );
}

TEST( ServerStringPool, DifferentStringsAreDifferentCopies )
{
	CServerStringPool pool;
	const char *a = pool.Allocate( "alpha" );
	const char *b = pool.Allocate( "beta" );
	EXPECT_NE( a, b );
	EXPECT_EQ( 2, pool.Count() );
}

TEST( ServerStringPool, ThePooledStringIsACopyNotTheCallersBuffer )
{
	CServerStringPool pool;
	char buf[] = "func_door";
	const char *a = pool.Allocate( buf );
	EXPECT_NE( (const char *)buf, a );
	buf[0] = 'x';
	EXPECT_STREQ( "func_door", a );
}

TEST( ServerStringPool, AnAddressStaysPutAsTheSetGrows )
{
	CServerStringPool pool;
	const char *first = pool.Allocate( "m" );
	char name[16];
	for ( int i = 0; i < 500; i++ )
	{
		snprintf( name, sizeof( name ), "s%03d", i );
		pool.Allocate( name );
	}
	EXPECT_EQ( first, pool.Allocate( "M" ) );
	EXPECT_EQ( 501, pool.Count() );
}

TEST( ServerStringPool, FreeAllEmptiesThePool )
{
	CServerStringPool pool;
	pool.Allocate( "a" );
	pool.Allocate( "b" );
	pool.FreeAll();
	EXPECT_EQ( 0, pool.Count() );
	pool.Allocate( "a" );
	EXPECT_EQ( 1, pool.Count() );
}
