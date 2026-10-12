// ss_physics_shared.h respells physics_shared.h's VPhysics game-flag bits in
// namespace ss, so translation units on the R5 include set can name them. A
// local copy of a number is a thing that drifts, so each one is read back out
// of the fork header's text and required to be the same value.
// (ss::PhysSetGameFlags and its NULL-guard fact left with the last native
// caller, 2026-10-12: EntityPhysicsMotion.AddGameFlags and its facts.)

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <string>

#include "gtest/gtest.h"

#include "../game/shared/ss_physics_shared.h"

namespace
{

const char *const kPhysicsSharedH = "game/shared/physics_shared.h";

std::string ReadFile( const char *pPath )
{
	FILE *fp = fopen( pPath, "rb" );
	if ( !fp )
		return std::string();

	std::string out;
	char buf[ 4096 ];
	size_t n;
	while ( ( n = fread( buf, 1, sizeof( buf ), fp ) ) > 0 )
		out.append( buf, n );
	fclose( fp );
	return out;
}

// `#define NAME 0x....` out of the header, as an int; -1 when it is not there
// in that shape (a failure, never a zero that could compare equal). The
// directive and the name may be separated by spaces or a tab (physics_shared.h
// uses both), and the name must be followed by whitespace, so a longer name
// that starts with this one is not taken for it.
int HexDefine( const std::string &header, const char *pszName )
{
	const std::string name( pszName );
	for ( size_t at = header.find( "#define" ); at != std::string::npos;
	      at = header.find( "#define", at + 1 ) )
	{
		size_t p = at + strlen( "#define" );
		if ( p >= header.size() || ( header[ p ] != ' ' && header[ p ] != '\t' ) )
			continue;
		while ( p < header.size() && ( header[ p ] == ' ' || header[ p ] == '\t' ) )
			p++;
		if ( header.compare( p, name.size(), name ) != 0 )
			continue;
		const size_t after = p + name.size();
		if ( after >= header.size() || ( header[ after ] != ' ' && header[ after ] != '\t' ) )
			continue;

		const size_t hex = header.find( "0x", after );
		const size_t eol = header.find( '\n', after );
		if ( hex == std::string::npos || eol == std::string::npos || hex > eol )
			return -1;
		return (int)strtol( header.c_str() + hex, NULL, 16 );
	}
	return -1;
}

struct FlagRow_t
{
	const char *pszValveName;
	int nOurs;
};

const FlagRow_t kFlags[] = {
	{ "FVPHYSICS_DMG_SLICE",          ss::kPhysFlagSliceDamage },
	{ "FVPHYSICS_CONSTRAINT_STATIC",  ss::kPhysFlagConstraintStatic },
	{ "FVPHYSICS_PLAYER_HELD",        ss::kPhysFlagPlayerHeld },
	{ "FVPHYSICS_PART_OF_RAGDOLL",    ss::kPhysFlagPartOfRagdoll },
	{ "FVPHYSICS_MULTIOBJECT_ENTITY", ss::kPhysFlagMultiObjectEntity },
	{ "FVPHYSICS_HEAVY_OBJECT",       ss::kPhysFlagHeavyObject },
	{ "FVPHYSICS_PENETRATING",        ss::kPhysFlagPenetrating },
	{ "FVPHYSICS_NO_PLAYER_PICKUP",   ss::kPhysFlagNoPlayerPickup },
	{ "FVPHYSICS_WAS_THROWN",         ss::kPhysFlagWasThrown },
	{ "FVPHYSICS_DMG_DISSOLVE",       ss::kPhysFlagDissolveDamage },
	{ "FVPHYSICS_NO_IMPACT_DMG",      ss::kPhysFlagNoImpactDamage },
	{ "FVPHYSICS_NO_NPC_IMPACT_DMG",  ss::kPhysFlagNoNpcImpactDamage },
	{ "FVPHYSICS_NO_SELF_COLLISIONS", ss::kPhysFlagNoSelfCollisions },
};

} // namespace

TEST( SsPhysicsShared, EveryFlagIsPhysicsSharedHsOwnValue )
{
	const std::string header = ReadFile( kPhysicsSharedH );
	ASSERT_FALSE( header.empty() ) << kPhysicsSharedH << " could not be read; "
		"every comparison below would pass for the wrong reason.";

	for ( size_t i = 0; i < sizeof( kFlags ) / sizeof( kFlags[ 0 ] ); i++ )
	{
		const int nValve = HexDefine( header, kFlags[ i ].pszValveName );
		ASSERT_NE( -1, nValve ) << kFlags[ i ].pszValveName
			<< " is not a `#define NAME 0x...` in " << kPhysicsSharedH;
		EXPECT_EQ( nValve, kFlags[ i ].nOurs ) << kFlags[ i ].pszValveName
			<< " is " << nValve << " in " << kPhysicsSharedH
			<< " and ss_physics_shared.h says " << kFlags[ i ].nOurs;
	}
}

// The two energy scales and FLUID_TIME_MAX are not hex flags: hold the fork's text of
// each (whitespace aside) and our value at the speeds physics_shared.cpp asks.
static std::string Squeezed( const std::string &s )
{
	std::string out;
	for ( char c : s )
		if ( c != ' ' && c != '\t' )
			out += c;
	return out;
}

TEST( SsPhysicsShared, TheEnergyScalesAndFluidTimeArePhysicsSharedHs )
{
	const std::string header = Squeezed( ReadFile( kPhysicsSharedH ) );
	ASSERT_FALSE( header.empty() ) << kPhysicsSharedH << " could not be read";

	EXPECT_NE( std::string::npos, header.find( "#defineMASS_SPEED2ENERGY(mass,speed)((speed)*(speed)*(mass))\n" ) );
	EXPECT_NE( std::string::npos, header.find( "#defineMASS10_SPEED2ENERGY(speed)MASS_SPEED2ENERGY(10,speed)\n" ) );
	EXPECT_NE( std::string::npos, header.find( "#defineENERGY_VOLUME_SCALE(1.0f/15500.0f)\n" ) );
	EXPECT_NE( std::string::npos, header.find( "#defineFLUID_TIME_MAX2.0f//" ) );

	EXPECT_EQ( 2250, SS_MASS10_SPEED2ENERGY( 15 ) );
	EXPECT_EQ( 7840, SS_MASS10_SPEED2ENERGY( 28 ) );
	EXPECT_EQ( 25000, SS_MASS10_SPEED2ENERGY( 50 ) );
	EXPECT_EQ( 1.0f / 15500.0f, ss::kEnergyVolumeScale );
	EXPECT_EQ( 2.0f, ss::kFluidTimeMax );
}
