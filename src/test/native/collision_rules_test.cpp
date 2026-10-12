//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the collision matrix answers what stock answered =====//
//
// CGameRules::ShouldCollide was sixty-seven lines of ordered ifs over two group
// indices. It is a table now, declared in C# (SourceSharp.Abi.CollisionRules)
// and held natively as twenty words.
//
// THIS FILE IS THE ORACLE. It keeps stock's predicate VERBATIM -- copied from
// gamerules.cpp:915 with the HL2MP arms included, because this mod does not
// define HL2MP -- and checks the native table against it for all four hundred
// pairs. The domain is finite and small, so this is not a sample: it is a
// complete proof that the table says exactly what the function said.
//
// The chain it closes is:
//
//     stock C++  <-- this test -->  native default table
//     native default table  <-- CollisionRulesTests.cs -->  the C# rules
//
// so a change to the C# predicate that disagrees with stock fails on the far
// side, and a change to the native words fails here.
//
//============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <sstream>
#include <regex>

#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
// The engine's groups, positionally. Asserted against const.h below rather than
// trusted -- the enum there is unnumbered, so a member inserted in the middle
// renumbers everything after it and would silently reinterpret every row.
//-----------------------------------------------------------------------------
enum
{
	COLLISION_GROUP_NONE = 0,
	COLLISION_GROUP_DEBRIS,
	COLLISION_GROUP_DEBRIS_TRIGGER,
	COLLISION_GROUP_INTERACTIVE_DEBRIS,
	COLLISION_GROUP_INTERACTIVE,
	COLLISION_GROUP_PLAYER,
	COLLISION_GROUP_BREAKABLE_GLASS,
	COLLISION_GROUP_VEHICLE,
	COLLISION_GROUP_PLAYER_MOVEMENT,
	COLLISION_GROUP_NPC,
	COLLISION_GROUP_IN_VEHICLE,
	COLLISION_GROUP_WEAPON,
	COLLISION_GROUP_VEHICLE_CLIP,
	COLLISION_GROUP_PROJECTILE,
	COLLISION_GROUP_DOOR_BLOCKER,
	COLLISION_GROUP_PASSABLE_DOOR,
	COLLISION_GROUP_DISSOLVING,
	COLLISION_GROUP_PUSHAWAY,
	COLLISION_GROUP_NPC_ACTOR,
	COLLISION_GROUP_NPC_SCRIPTED,
	LAST_SHARED_COLLISION_GROUP
};

template < typename T >
void V_swap( T &a, T &b ) { T t = a; a = b; b = t; }

//-----------------------------------------------------------------------------
// STOCK, VERBATIM. gamerules.cpp:915. Do not tidy this: its value is that it is
// character-for-character what the engine used to run, including the order,
// which is load-bearing -- several of the tests overlap and the first match
// wins.
//-----------------------------------------------------------------------------
bool StockShouldCollide( int collisionGroup0, int collisionGroup1 )
{
	if ( collisionGroup0 > collisionGroup1 )
	{
		V_swap(collisionGroup0,collisionGroup1);
	}

	if ( (collisionGroup0 == COLLISION_GROUP_PLAYER || collisionGroup0 == COLLISION_GROUP_PLAYER_MOVEMENT) &&
		collisionGroup1 == COLLISION_GROUP_PUSHAWAY )
	{
		return false;
	}

	if ( collisionGroup0 == COLLISION_GROUP_DEBRIS && collisionGroup1 == COLLISION_GROUP_PUSHAWAY )
	{
		return true;
	}

	if (( collisionGroup0 == COLLISION_GROUP_IN_VEHICLE ) || ( collisionGroup1 == COLLISION_GROUP_IN_VEHICLE ))
		return false;

	if ( ( collisionGroup1 == COLLISION_GROUP_DOOR_BLOCKER ) && ( collisionGroup0 != COLLISION_GROUP_NPC ) )
		return false;

	if ( ( collisionGroup0 == COLLISION_GROUP_PLAYER ) && ( collisionGroup1 == COLLISION_GROUP_PASSABLE_DOOR ) )
		return false;

	if ( collisionGroup0 == COLLISION_GROUP_DEBRIS || collisionGroup0 == COLLISION_GROUP_DEBRIS_TRIGGER )
	{
		return false;
	}

	if ( (collisionGroup0 == COLLISION_GROUP_DISSOLVING) || (collisionGroup1 == COLLISION_GROUP_DISSOLVING) )
	{
		if ( collisionGroup0 != COLLISION_GROUP_NONE )
			return false;
	}

	if ( collisionGroup0 == COLLISION_GROUP_INTERACTIVE_DEBRIS && collisionGroup1 == COLLISION_GROUP_INTERACTIVE_DEBRIS )
		return false;

	if ( collisionGroup0 == COLLISION_GROUP_INTERACTIVE_DEBRIS && ( collisionGroup1 == COLLISION_GROUP_PLAYER_MOVEMENT || collisionGroup1 == COLLISION_GROUP_PLAYER ) )
		 return false;

	if ( collisionGroup0 == COLLISION_GROUP_BREAKABLE_GLASS && collisionGroup1 == COLLISION_GROUP_BREAKABLE_GLASS )
		return false;

	if ( collisionGroup1 == COLLISION_GROUP_INTERACTIVE && collisionGroup0 != COLLISION_GROUP_NONE )
		return false;

	if ( collisionGroup1 == COLLISION_GROUP_PROJECTILE )
	{
		if ( collisionGroup0 == COLLISION_GROUP_DEBRIS ||
			collisionGroup0 == COLLISION_GROUP_WEAPON ||
			collisionGroup0 == COLLISION_GROUP_PROJECTILE )
		{
			return false;
		}
	}

	if ( collisionGroup1 == COLLISION_GROUP_WEAPON )
	{
		if ( collisionGroup0 == COLLISION_GROUP_VEHICLE ||
			collisionGroup0 == COLLISION_GROUP_PLAYER ||
			collisionGroup0 == COLLISION_GROUP_NPC )
		{
			return false;
		}
	}

	if ( collisionGroup0 == COLLISION_GROUP_VEHICLE_CLIP || collisionGroup1 == COLLISION_GROUP_VEHICLE_CLIP )
	{
		if ( collisionGroup0 == COLLISION_GROUP_VEHICLE )
			return true;
		return false;
	}

	return true;
}

std::string Read( const char *pszRel )
{
	std::string prefix;
	for ( int i = 0; i < 6; i++ )
	{
		std::ifstream f( prefix + "src/" + pszRel );
		if ( f.good() )
		{
			std::stringstream ss;
			ss << f.rdbuf();
			return ss.str();
		}
		prefix += "../";
	}
	return "";
}

//-----------------------------------------------------------------------------
// The twenty words out of the shell's default table.
//-----------------------------------------------------------------------------
std::vector< unsigned int > NativeRows()
{
	const std::string cpp = Read( "sourcesharp/game/shared/shell_gamerules.cpp" );
	std::vector< unsigned int > rows;

	const size_t at = cpp.find( "static SourceSharpCollisionRules s_CollisionRules" );
	if ( at == std::string::npos )
		return rows;

	const size_t end = cpp.find( "};", at );
	const std::string body = cpp.substr( at, end - at );

	const std::regex word( R"(0x([0-9A-Fa-f]{8})u)" );
	for ( std::sregex_iterator it( body.begin(), body.end(), word ), last; it != last; ++it )
		rows.push_back( (unsigned int)strtoul( ( *it )[1].str().c_str(), NULL, 16 ) );

	return rows;
}

} // namespace

//-----------------------------------------------------------------------------
TEST( CollisionRules, TheGroupNumberingMatchesTheEngine )
{
	const std::string h = Read( "public/const.h" );
	ASSERT_FALSE( h.empty() ) << "cannot read public/const.h";

	const size_t at = h.find( "COLLISION_GROUP_NONE  = 0" );
	ASSERT_NE( at, std::string::npos ) << "the collision group enum has moved.";

	const size_t end = h.find( "LAST_SHARED_COLLISION_GROUP", at );
	ASSERT_NE( end, std::string::npos );

	// The names in order, comments and blank lines removed.
	const std::string body = h.substr( at, end - at );
	const std::regex name( R"(COLLISION_GROUP_(\w+))" );

	std::vector< std::string > got;
	for ( std::sregex_iterator it( body.begin(), body.end(), name ), last; it != last; ++it )
		got.push_back( ( *it )[1].str() );

	static const char *kExpect[] = {
		"NONE", "DEBRIS", "DEBRIS_TRIGGER", "INTERACTIVE_DEBRIS", "INTERACTIVE",
		"PLAYER", "BREAKABLE_GLASS", "VEHICLE", "PLAYER_MOVEMENT", "NPC",
		"IN_VEHICLE", "WEAPON", "VEHICLE_CLIP", "PROJECTILE", "DOOR_BLOCKER",
		"PASSABLE_DOOR", "DISSOLVING", "PUSHAWAY", "NPC_ACTOR", "NPC_SCRIPTED",
	};
	const size_t kCount = sizeof( kExpect ) / sizeof( kExpect[0] );

	ASSERT_EQ( got.size(), kCount )
		<< "public/const.h now lists " << got.size() << " collision groups, not "
		<< kCount << ". The enum is UNNUMBERED, so every index after the change "
		   "has shifted and every row of the matrix now means something else.";

	for ( size_t i = 0; i < kCount; i++ )
	{
		EXPECT_EQ( got[i], kExpect[i] )
			<< "collision group " << i << " is COLLISION_GROUP_" << got[i]
			<< " in const.h and COLLISION_GROUP_" << kExpect[i] << " here. The "
			   "matrix is indexed by these numbers on both sides.";
	}
}

//-----------------------------------------------------------------------------
TEST( CollisionRules, TheNativeTableHasOneRowPerGroup )
{
	const std::vector< unsigned int > rows = NativeRows();

	ASSERT_EQ( rows.size(), (size_t)LAST_SHARED_COLLISION_GROUP )
		<< "the shell's default collision table has " << rows.size()
		<< " row(s), want " << LAST_SHARED_COLLISION_GROUP << ". A short table "
		   "is read past its end; a long one has rows nothing indexes.";
}

//-----------------------------------------------------------------------------
// The whole point: all four hundred pairs.
//-----------------------------------------------------------------------------
TEST( CollisionRules, TheTableAnswersExactlyWhatStockAnswered )
{
	const std::vector< unsigned int > rows = NativeRows();
	ASSERT_EQ( rows.size(), (size_t)LAST_SHARED_COLLISION_GROUP );

	int disagreements = 0;

	for ( int a = 0; a < LAST_SHARED_COLLISION_GROUP; a++ )
	{
		for ( int b = 0; b < LAST_SHARED_COLLISION_GROUP; b++ )
		{
			const bool stock = StockShouldCollide( a, b );
			const bool table = ( rows[a] & ( 1u << b ) ) != 0;

			if ( stock != table )
			{
				disagreements++;
				EXPECT_EQ( stock, table )
					<< "groups " << a << " and " << b << ": stock says "
					<< ( stock ? "collide" : "pass through" ) << ", the table says "
					<< ( table ? "collide" : "pass through" ) << ".";
			}
		}
	}

	EXPECT_EQ( disagreements, 0 )
		<< disagreements << " of " << ( LAST_SHARED_COLLISION_GROUP * LAST_SHARED_COLLISION_GROUP )
		<< " pairs disagree with stock.";
}

//-----------------------------------------------------------------------------
// The relation is symmetric, because stock sorts its arguments before deciding.
// The lookup does NOT sort, so an asymmetric table would answer differently
// depending on argument order -- which is the one way a table can be wrong that
// the pair-by-pair check above would still pass.
//-----------------------------------------------------------------------------
TEST( CollisionRules, TheTableIsSymmetric )
{
	const std::vector< unsigned int > rows = NativeRows();
	ASSERT_EQ( rows.size(), (size_t)LAST_SHARED_COLLISION_GROUP );

	for ( int a = 0; a < LAST_SHARED_COLLISION_GROUP; a++ )
	{
		for ( int b = a + 1; b < LAST_SHARED_COLLISION_GROUP; b++ )
		{
			const bool ab = ( rows[a] & ( 1u << b ) ) != 0;
			const bool ba = ( rows[b] & ( 1u << a ) ) != 0;

			EXPECT_EQ( ab, ba )
				<< "row " << a << " bit " << b << " is " << ab << " but row " << b
				<< " bit " << a << " is " << ba << ". Stock sorted its arguments "
				   "before deciding, so the relation is symmetric; the lookup "
				   "does not sort, so an asymmetric table answers differently "
				   "depending on which argument came first.";
		}
	}
}

//-----------------------------------------------------------------------------
// And the native side really is a lookup now, not the ifs.
//-----------------------------------------------------------------------------
TEST( CollisionRules, TheNativePredicateIsALookup )
{
	const std::string cpp = Read( "sourcesharp/game/shared/shell_gamerules.cpp" );
	ASSERT_FALSE( cpp.empty() );

	const size_t at = cpp.find( "bool CSourceSharpGameRules::ShouldCollide" );
	ASSERT_NE( at, std::string::npos ) << "ShouldCollide is gone entirely.";

	const size_t end = cpp.find( "\n}", at );
	const std::string body = cpp.substr( at, end - at );

	EXPECT_NE( body.find( "s_CollisionRules.nRows" ), std::string::npos )
		<< "ShouldCollide no longer reads the matrix.";

	// The ifs are what left. One group name in the body means a rule is being
	// decided here again rather than in C#.
	EXPECT_EQ( body.find( "COLLISION_GROUP_PUSHAWAY" ), std::string::npos )
		<< "ShouldCollide names a collision group again, so a rule has come back "
		   "into C++. The rules are declared in SourceSharp.Abi.CollisionRules.";
}

//-----------------------------------------------------------------------------
// THE CLIENT RECEIVES THE MATRIX TOO. ShouldCollide is asked during prediction,
// so ClientModule registers the same matrix the server does. Until lane
// side-audit (2026-10-02) the slot lived in the server-only IShimCollision, the
// client served no area holding it, and its registration was a silent no-op.
//
// Two links, both needed: the client's shim root serves the shared
// IShimCollision base with a forward to the one body, and that body stores the
// rows OUTSIDE the server guard, so the client's ShouldCollide reads them.
//-----------------------------------------------------------------------------
TEST( CollisionRules, TheClientServesTheRulesSlot )
{
	const std::string inc = Read( "sourcesharp/game/shared/shell_shim_areas_client.inc" );
	ASSERT_FALSE( inc.empty() ) << "shell_shim_areas_client.inc not found.";

	EXPECT_NE( inc.find( "void CShimCollisionClient::RegisterCollisionRules( const SourceSharpCollisionRules * pRules )\n"
	                     "\t{ return ::SourceSharp_RegisterCollisionRules( pRules ); }" ),
	           std::string::npos )
		<< "the client's collision area does not forward RegisterCollisionRules to the shared body.";

	EXPECT_NE( inc.find( "if ( !ss::StrICmp( pszName, SOURCESHARP_SHIM_COLLISION_VERSION ) )" ), std::string::npos )
		<< "the client's shim root does not serve SourceSharpCollision001.";
	EXPECT_NE( inc.find( "return static_cast< IShimCollision * >( &areas.m_Collision );" ), std::string::npos )
		<< "the client's shim root does not hand out its collision area.";
}

TEST( CollisionRules, TheBodyStoresTheRowsOnBothSides )
{
	const std::string cpp = Read( "sourcesharp/game/shared/shell_gamerules.cpp" );
	ASSERT_FALSE( cpp.empty() );

	const size_t at = cpp.find( "void SourceSharp_RegisterCollisionRules( const SourceSharpCollisionRules *pRules )\n{" );
	ASSERT_NE( at, std::string::npos ) << "the registration body is gone.";

	const size_t end = cpp.find( "\n}", at );
	const std::string body = cpp.substr( at, end - at );

	const size_t store = body.find( "\ts_CollisionRules = *pRules;" );
	const size_t guard = body.find( "#ifndef CLIENT_DLL" );
	ASSERT_NE( store, std::string::npos ) << "the body no longer stores the rows.";
	EXPECT_TRUE( guard == std::string::npos || store < guard )
		<< "the store is inside the server guard, so the client's ShouldCollide never sees the matrix.";
}
