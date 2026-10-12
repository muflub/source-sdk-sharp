// plan_map_load P1: the spawn's material reload state machine
// (map_material_reload.h), driven as the hooked engine calls drive it.

#include "gtest/gtest.h"
#include "../game/client/map_material_reload.h"

#include <string>
#include <vector>

namespace
{
struct Spawn
{
	CMapMaterialReload core;
	std::vector< std::string > reloads;     // "(null)" for the full reload
	std::vector< std::string > asked;       // "next|previous"
	int answer = -1;
	std::string names;
	char buffer[ 256 ];

	int Reload( const char *pSub )
	{
		auto reload = [ this ]( const char *p ) { reloads.push_back( p ? p : "(null)" ); };
		auto ask = [ this ]( const char *pNext, const char *pPrev, char *pOut, int nOut ) -> int
		{
			asked.push_back( std::string( pNext ) + "|" + pPrev );
			if ( answer < 0 )
				return answer;
			if ( (int)names.size() >= nOut )
				return (int)names.size();
			memcpy( pOut, names.c_str(), names.size() + 1 );
			return (int)names.size();
		};
		return core.OnReloadMaterials( pSub, reload, ask, buffer, sizeof( buffer ) );
	}

	void Mount( const char *pPath ) { core.OnAddSearchPath( pPath, "GAME", true ); core.OnBeginMapAccess(); }
};
}

TEST( MapMaterialReload, OutsideASpawnTheFullReloadIsUntouched )
{
	Spawn s;
	EXPECT_EQ( -1, s.Reload( NULL ) );
	EXPECT_EQ( ( std::vector< std::string >{ "(null)" } ), s.reloads );
	EXPECT_TRUE( s.asked.empty() );
}

TEST( MapMaterialReload, ASubstringReloadIsAlwaysPassedThrough )
{
	Spawn s;
	s.Mount( "/game/maps/a.bsp" );
	EXPECT_EQ( 1, s.Reload( "tile/floor" ) );
	EXPECT_EQ( ( std::vector< std::string >{ "tile/floor" } ), s.reloads );
	EXPECT_TRUE( s.asked.empty() );
	EXPECT_TRUE( s.core.IsArmed() );
}

TEST( MapMaterialReload, InsideASpawnOnlyManagedsNamesAreReloaded )
{
	Spawn s;
	s.answer = 0;
	s.names = "tile/floor\nmetal/grate";
	s.Mount( "/game/maps/a.bsp" );
	EXPECT_EQ( 2, s.Reload( NULL ) );
	EXPECT_EQ( ( std::vector< std::string >{ "tile/floor", "metal/grate" } ), s.reloads );
	EXPECT_EQ( ( std::vector< std::string >{ "/game/maps/a.bsp|" } ), s.asked );
	EXPECT_FALSE( s.core.IsArmed() );
}

TEST( MapMaterialReload, AnEmptyAnswerReloadsNothing )
{
	Spawn s;
	s.answer = 0;
	s.Mount( "/game/maps/a.bsp" );
	EXPECT_EQ( 0, s.Reload( NULL ) );
	EXPECT_TRUE( s.reloads.empty() );
}

TEST( MapMaterialReload, NoAnswerIsTheFullReload )
{
	Spawn s;
	s.answer = -1;
	s.Mount( "/game/maps/a.bsp" );
	EXPECT_EQ( -1, s.Reload( NULL ) );
	EXPECT_EQ( ( std::vector< std::string >{ "(null)" } ), s.reloads );
}

TEST( MapMaterialReload, AnAnswerThatDoesNotFitIsTheFullReload )
{
	Spawn s;
	s.answer = 0;
	s.names = std::string( 300, 'x' );
	s.Mount( "/game/maps/a.bsp" );
	EXPECT_EQ( -1, s.Reload( NULL ) );
	EXPECT_EQ( ( std::vector< std::string >{ "(null)" } ), s.reloads );
}

TEST( MapMaterialReload, OnlyAMapPakAtTheHeadOfGameArmsIt )
{
	Spawn s;
	s.core.OnAddSearchPath( "/game/maps/a.bsp", "MOD", true );
	s.core.OnBeginMapAccess();
	EXPECT_FALSE( s.core.IsArmed() );
	s.core.OnAddSearchPath( "/game/maps/a.bsp", "GAME", false );
	s.core.OnBeginMapAccess();
	EXPECT_FALSE( s.core.IsArmed() );
	s.core.OnAddSearchPath( "/game/custom/pack.vpk", "GAME", true );
	s.core.OnBeginMapAccess();
	EXPECT_FALSE( s.core.IsArmed() );
	s.core.OnAddSearchPath( "/game/maps/a.bsp", "GAME", true );
	s.core.OnBeginMapAccess();
	EXPECT_TRUE( s.core.IsArmed() );   // the positive arm: the same calls do arm it
}

TEST( MapMaterialReload, EndMapAccessClosesTheWindow )
{
	Spawn s;
	s.answer = 0;
	s.Mount( "/game/maps/a.bsp" );
	s.core.OnEndMapAccess();
	EXPECT_EQ( -1, s.Reload( NULL ) );   // a later mat_reloadallmaterials stays full
	EXPECT_EQ( ( std::vector< std::string >{ "(null)" } ), s.reloads );
}

TEST( MapMaterialReload, TheNextSpawnIsToldThePreviousMap )
{
	Spawn s;
	s.answer = 0;
	s.Mount( "/game/maps/a.bsp" );
	s.Reload( NULL );
	s.core.OnEndMapAccess();
	s.Mount( "/game/maps/b.bsp" );
	s.Reload( NULL );
	EXPECT_EQ( ( std::vector< std::string >{ "/game/maps/a.bsp|", "/game/maps/b.bsp|/game/maps/a.bsp" } ), s.asked );
	EXPECT_STREQ( "/game/maps/b.bsp", s.core.Previous() );
}
