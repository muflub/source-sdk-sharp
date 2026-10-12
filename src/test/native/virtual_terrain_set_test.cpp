//========= SourceSharp ========================================================//
//
// virtual_terrain_set_test.cpp -- lane g-vdisp.
//
// WHAT THIS PINS.
//
// SourceSharpReportObjectsOutOfBounds (server/physics.cpp) asks vphysics'
// CollideGetAABB for every physics object's hull, and vphysics SEGFAULTS there
// on a virtual-mesh collide. Every map whose
// displacements are virtual meshes crashed the game at level init. The report
// now collects the virtual-terrain collides, sorts them, and skips the AABB for
// any object whose collide is one of them.
//
// That membership test is the whole fix, and a wrong answer in either direction
// is silent: a false "no" is the crash back, and a false "yes" hides a real
// object's hull from the report. So these facts pin both directions over sets
// the test controls.
//
// ENGINE-FREE: physics_shell.h above its ENGINE guard is arithmetic over PODs,
// and this file does not define SOURCESHARP_PHYSICS_SHELL_ENGINE.
//
//=============================================================================//
#include <gtest/gtest.h>

#include "physics_shell.h"

namespace
{
	// Distinct, stable addresses to stand in for CPhysCollide pointers.
	char g_Collides[ 16 ];

	const void *At( int i ) { return &g_Collides[ i ]; }
}

TEST( VirtualTerrainSet, AMemberIsFound )
{
	const void *set[] = { At( 5 ), At( 1 ), At( 9 ), At( 3 ) };
	SourceSharpSortPointerSet( set, 4 );

	EXPECT_TRUE( SourceSharpPointerSetContains( set, 4, At( 1 ) ) );
	EXPECT_TRUE( SourceSharpPointerSetContains( set, 4, At( 3 ) ) );
	EXPECT_TRUE( SourceSharpPointerSetContains( set, 4, At( 5 ) ) );
	EXPECT_TRUE( SourceSharpPointerSetContains( set, 4, At( 9 ) ) );
}

TEST( VirtualTerrainSet, ANonMemberIsNotFound )
{
	const void *set[] = { At( 5 ), At( 1 ), At( 9 ), At( 3 ) };
	SourceSharpSortPointerSet( set, 4 );

	EXPECT_FALSE( SourceSharpPointerSetContains( set, 4, At( 0 ) ) );   // below all
	EXPECT_FALSE( SourceSharpPointerSetContains( set, 4, At( 4 ) ) );   // between
	EXPECT_FALSE( SourceSharpPointerSetContains( set, 4, At( 12 ) ) );  // above all
	EXPECT_FALSE( SourceSharpPointerSetContains( set, 4, NULL ) );
}

TEST( VirtualTerrainSet, AnEmptySetHoldsNothing )
{
	// A map with no displacements: the report must ask for every hull.
	EXPECT_FALSE( SourceSharpPointerSetContains( NULL, 0, At( 1 ) ) );

	const void *set[ 1 ] = { At( 1 ) };
	SourceSharpSortPointerSet( set, 0 );
	EXPECT_FALSE( SourceSharpPointerSetContains( set, 0, At( 1 ) ) );
}

TEST( VirtualTerrainSet, DuplicatesAreStillFoundAndExcludeNothingElse )
{
	const void *set[] = { At( 7 ), At( 2 ), At( 7 ), At( 2 ), At( 7 ) };
	SourceSharpSortPointerSet( set, 5 );

	EXPECT_TRUE( SourceSharpPointerSetContains( set, 5, At( 2 ) ) );
	EXPECT_TRUE( SourceSharpPointerSetContains( set, 5, At( 7 ) ) );
	EXPECT_FALSE( SourceSharpPointerSetContains( set, 5, At( 3 ) ) );
	EXPECT_FALSE( SourceSharpPointerSetContains( set, 5, At( 8 ) ) );
}

TEST( VirtualTerrainSet, SortingLeavesTheSetAscending )
{
	const void *set[] = { At( 9 ), At( 8 ), At( 7 ), At( 1 ), At( 4 ), At( 4 ) };
	SourceSharpSortPointerSet( set, 6 );

	for ( int i = 1; i < 6; i++ )
		EXPECT_LE( (uintptr_t)set[ i - 1 ], (uintptr_t)set[ i ] ) << "at " << i;
}

TEST( VirtualTerrainSet, EveryMemberOfALargeReversedSetIsFound )
{
	// MAX_MAP_DISPINFO-sized, handed over in the worst order for the sort.
	static char s_Block[ 2048 * 2 ];
	static const void *s_Set[ 2048 ];
	for ( int i = 0; i < 2048; i++ )
		s_Set[ i ] = &s_Block[ ( 2047 - i ) * 2 ];
	SourceSharpSortPointerSet( s_Set, 2048 );

	for ( int i = 0; i < 2048; i++ )
	{
		ASSERT_TRUE( SourceSharpPointerSetContains( s_Set, 2048, &s_Block[ i * 2 ] ) ) << i;
		ASSERT_FALSE( SourceSharpPointerSetContains( s_Set, 2048, &s_Block[ i * 2 + 1 ] ) ) << i;
	}
}
