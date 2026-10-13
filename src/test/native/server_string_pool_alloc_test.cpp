//========= SourceSharp — the string pool frees what it allocated ============//
//
// gamestringpool.cpp includes tier0/memdbgon.h ahead of server_string_pool.h.
// memdbgon rewrites strdup to MemAlloc_StrDup (tier0's allocator) and leaves
// free alone, so a pool built on strdup/free allocated with one allocator and
// released with another. server.so's memoverride hid it by routing free to
// tier0 too; the allocation checker counted it as foreign (two sites, at every
// level shutdown), and removing memoverride would have made it real.
//
// This TU includes memdbgon.h first, exactly as that caller does, and runs under
// the allocation checker's wraps: every block the pool releases must be one the
// checker saw it allocate.
//
//=============================================================================//

#include "tier0/memdbgon.h"

#include "gtest/gtest.h"
#include "ss_ci_skip.h"

#include "alloc_check.h"
#include "server_string_pool.h"

namespace
{
struct Delta
{
	SourceSharpAllocCheckCounts before = SourceSharp_AllocCheckCounts();
	uint64_t Foreign() const    { return SourceSharp_AllocCheckCounts().foreignFrees - before.foreignFrees; }
	uint64_t Mismatches() const { return SourceSharp_AllocCheckCounts().mismatches - before.mismatches; }
	uint64_t Allocs() const     { return SourceSharp_AllocCheckCounts().allocations - before.allocations; }
};

// Longer than any small-string buffer, so each one is a heap block.
const char *const kNames[] = {
	"info_player_deathmatch_long_enough_to_be_a_heap_block",
	"prop_physics_multiplayer_long_enough_to_be_a_heap_block",
	"func_breakable_surf_long_enough_to_be_a_heap_block",
};
}

TEST( ServerStringPoolAlloc, UnderMemdbgonEveryReleaseIsOfABlockThePoolAllocated )
{
	SS_SKIP_ON_CI( "built against the fork's tier0 allocator routing (memdbgon/MemAlloc_StrDup), which the public patch series does not reproduce" );
	Delta d;
	{
		CServerStringPool pool;
		for ( const char *pszName : kNames )
			pool.Allocate( pszName );
		ASSERT_EQ( 3, pool.Count() );
		pool.FreeAll();
	}
	EXPECT_GE( d.Allocs(), 3u ) << "the pool's allocations never reached the checker";
	EXPECT_EQ( 0u, d.Foreign() ) << "the pool released memory it did not allocate "
	                                "through the wrapped allocator";
	EXPECT_EQ( 0u, d.Mismatches() );
}

TEST( ServerStringPoolAlloc, TheDestructorReleasesWhatFreeAllWouldHave )
{
	SS_SKIP_ON_CI( "built against the fork's tier0 allocator routing (memdbgon/MemAlloc_StrDup), which the public patch series does not reproduce" );
	Delta d;
	{
		CServerStringPool pool;
		for ( const char *pszName : kNames )
			pool.Allocate( pszName );
	}
	EXPECT_GE( d.Allocs(), 3u );
	EXPECT_EQ( 0u, d.Foreign() );
	EXPECT_EQ( 0u, d.Mismatches() );
}
