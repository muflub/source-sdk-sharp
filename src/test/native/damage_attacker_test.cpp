// plan_physics_managed A40: a damage entry names its attacker on either hierarchy.
//
// Every damage entry point resolved the attacker as SourceSharp_ResolveEntity(
// hAttacker ).pBase, which is NULL for every managed entity, so a grenade, a
// rocket, a crossbow bolt or a bullet from a player named no attacker; and the
// player's kill relay turned that NULL into handle 0, which is entity 0, the
// world, and managed scoring credited the world with the kill. The resolver's
// GameEntity() answers on both hierarchies, and no attacker is the invalid
// handle (SourceSharp_HandleFromEntity), never 0.
//
// A source-text pin because these are engine-facing functions no gtest binary
// can drive; the live check is a player killed by a grenade crediting the
// thrower. Comment-stripped, whole constructs, and a positive count per file so
// an emptied or renamed file cannot pass.
//=============================================================================//
#include "gtest/gtest.h"

#include <dirent.h>

#include <fstream>
#include <iterator>
#include <string>

namespace
{

const char *SourceRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		const std::string probe = std::string( kCandidates[ i ] ) + "/sourcesharp/game/server";
		DIR *pDir = opendir( probe.c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadFile( const std::string &strRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

//-----------------------------------------------------------------------------
// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and here it is load-bearing rather
// than tidiness: every arm this file speaks for carries a comment NAMING the
// member it stopped calling. "pEntity->VPhysicsGetObject()" appears in
// physics.cpp today inside the sentence explaining that it is gone, and
// "GetMoveType() != MOVETYPE_VPHYSICS -> true" appears inside the sentence
// explaining that it would be wrong. A scanner matching raw text would find
// both and report a correct fix as broken.
//
// header_alias_test.cpp and cpp_alias_lane1_test.cpp were each caught by
// exactly this, which is why the stripping is copied rather than reinvented.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' )
				i++;
			if ( i < strIn.size() )
				out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

int CountOf( const std::string &strHay, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t at = strHay.find( strNeedle ); at != std::string::npos; at = strHay.find( strNeedle, at + 1 ) )
		n++;
	return n;
}

std::string Code( const char *pszRelative )
{
	return StripComments( ReadFile( pszRelative ) );
}

} // namespace

TEST( DamageAttacker, EveryEntryResolvesTheAttackerOnEitherHierarchy )
{
	const std::string api   = Code( "sourcesharp/game/server/shell_entityapi.cpp" );
	const std::string world = Code( "sourcesharp/game/server/shell_worldquery.cpp" );
	ASSERT_FALSE( api.empty() );
	ASSERT_FALSE( world.empty() );

	EXPECT_EQ( 0, CountOf( api, "SourceSharp_ResolveEntity( hAttacker ).pBase" ) );
	EXPECT_EQ( 0, CountOf( world, "SourceSharp_ResolveEntity( hAttacker ).pBase" ) );

	// ApplyDamage and ApplyDamageForce; EntityTakeDamage (RadiusDamage and
	// ExplosionDamage went in plan_server_native_v2 T2: no managed caller).
	EXPECT_EQ( 2, CountOf( api, "IGameEntity *pAttacker = SourceSharp_ResolveEntity( hAttacker ).GameEntity();" ) );
	EXPECT_EQ( 1, CountOf( world, "IGameEntity *pAttacker = SourceSharp_ResolveEntity( hAttacker ).GameEntity();" ) );
}

TEST( DamageAttacker, AKillWithNoAttackerIsTheInvalidHandleNotTheWorld )
{
	const std::string player = Code( "sourcesharp/game/server/shell_standalone_player.cpp" );
	ASSERT_FALSE( player.empty() );

	EXPECT_EQ( 0, CountOf( player, "uint32_t hAttacker = 0;" ) );
	EXPECT_EQ( 1, CountOf( player, "const uint32_t hAttacker = SourceSharp_HandleFromEntity( pAttacker );" ) );
}
