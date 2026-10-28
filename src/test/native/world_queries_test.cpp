// CServerWorldQueries: the sky camera's parameters as each player's client data reads
// them, and the invisible-range count it answers from the DLL tier.

#include "gtest/gtest.h"

#include "world_queries.h"
#include "ss_util.h"
#include "igameentity.h"
#include "shell_resolve.h"
#include "ss_physics_shared.h"
#include "ss_playerlocaldata.h"

#include "mathlib_test_env.h"

// The links this file's subject imports. EntityModelSurfaceProp is not reached here.
void SourceSharp_Trace( const char *, const char * ) {}

int SourceSharp_GetVerbosity()
{
	return 0;
}

SourceSharpEntity_t SourceSharp_ResolveEntity( uint32_t )
{
	return SourceSharpEntity_t();
}

IHandleEntity *SourceSharpEntity_t::ToHandleEntity() const
{
	return nullptr;
}

namespace
{
IPhysicsSurfaceProps *const kNoSurfaceProps = nullptr;

SourceSharpSkyboxParams SkyAt( float x, int32_t nArea )
{
	SourceSharpSkyboxParams params{};
	params.OriginXYZ[0] = x;
	params.Scale = 16;
	params.Area = nArea;
	params.FogEnable = 1;
	params.FogColorPrimary = 0x04030201u;
	params.FogEnd = 2000.0f;
	return params;
}
} // namespace

TEST( WorldQueries, NothingPushedFillsNothing )
{
	int32_t nReach = 0;
	CServerWorldQueries world( nullptr, nReach, kNoSurfaceProps );

	ss::sky3dparams_t out;
	EXPECT_FALSE( world.FillSkyboxParams( out ) );
}

TEST( WorldQueries, ThePushedSkyIsWhatEveryPlayerIsGiven )
{
	int32_t nReach = 0;
	CServerWorldQueries world( nullptr, nReach, kNoSurfaceProps );
	const SourceSharpSkyboxParams params = SkyAt( 128.0f, 3 );
	world.SetSkyboxParams( &params );

	ss::sky3dparams_t out;
	ASSERT_TRUE( world.FillSkyboxParams( out ) );
	EXPECT_EQ( 128.0f, out.origin.Get().x );
	EXPECT_EQ( 16, out.scale.Get() );
	EXPECT_EQ( 3, out.area.Get() );
	EXPECT_TRUE( out.fog.enable.Get() );
	EXPECT_EQ( 2000.0f, out.fog.end.Get() );
	EXPECT_EQ( 1, out.fog.colorPrimary.Get().r );
	EXPECT_EQ( 4, out.fog.colorPrimary.Get().a );
}

TEST( WorldQueries, ANullPushForgetsTheSky )
{
	int32_t nReach = 0;
	CServerWorldQueries world( nullptr, nReach, kNoSurfaceProps );
	const SourceSharpSkyboxParams params = SkyAt( 1.0f, 1 );
	world.SetSkyboxParams( &params );
	world.SetSkyboxParams( nullptr );

	ss::sky3dparams_t out;
	EXPECT_FALSE( world.FillSkyboxParams( out ) );
}

TEST( WorldQueries, TheInvisibleRangeCountIsTheTiersAsItStandsNow )
{
	int32_t nReach = 0;
	CServerWorldQueries world( nullptr, nReach, kNoSurfaceProps );
	EXPECT_EQ( 0, world.InvisibleRangeReach() );

	nReach = 5;
	EXPECT_EQ( 5, world.InvisibleRangeReach() );
}
