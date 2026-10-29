//========= SourceSharp — the allocation checker's own tests =================//
// plan_server_novalve.md §3.6. This binary is linked exactly as server.so is in
// debug: alloc_check.cpp plus ss_alloc_check_wraps, so every malloc/free and
// operator new/delete below goes through the wraps. Each finding is planted
// and seen; the clean pairs are the negative control that shows a finding is
// not raised for everything.
//
// Explicit ::operator new / ::operator delete calls, not new-expressions: the
// compiler may elide a new-expression pair, and never an explicit call.
//=============================================================================//

#include "alloc_check.h"
#include "death_test_no_core.h"

#include <gtest/gtest.h>

#include <malloc.h>
#include <new>
#include <stdlib.h>
#include <string.h>
#include <string>
#include <vector>

extern "C" void *__real_malloc( size_t );

// Sized operator delete (C++14) is called by name: the call is _ZdlPvm, which
// the link hook wraps like every other form.

namespace
{

struct Delta
{
	SourceSharpAllocCheckCounts before = SourceSharp_AllocCheckCounts();

	uint64_t Mismatches() const { return SourceSharp_AllocCheckCounts().mismatches - before.mismatches; }

	uint64_t Foreign() const { return SourceSharp_AllocCheckCounts().foreignFrees - before.foreignFrees; }

	uint64_t Allocs() const { return SourceSharp_AllocCheckCounts().allocations - before.allocations; }

	int64_t Live() const { return (int64_t)SourceSharp_AllocCheckCounts().live - (int64_t)before.live; }
};

void *volatile g_sink;

} // namespace

TEST( AllocCheck, TheWrapsAreOnTheLinkLine )
{
	Delta d;
	void *p = malloc( 32 );
	g_sink = p;
	EXPECT_EQ( 1u, d.Allocs() ) << "malloc did not reach __wrap_malloc: the link hook is not in force";
	free( p );
}

TEST( AllocCheck, MatchingPairsRaiseNothing )
{
	Delta d;
	// Each block escapes through g_sink: an allocation nobody can observe is
	// one the optimiser is allowed to delete, and then the wrap never sees it.
	void *a = malloc( 16 );
	g_sink = a;
	free( a );
	void *b = calloc( 4, 8 );
	g_sink = b;
	free( b );
	void *c = ::operator new( 24 );
	g_sink = c;
	::operator delete( c );
	void *e = ::operator new[]( 40 );
	g_sink = e;
	::operator delete[]( e );
	void *f = ::operator new( 24 );
	g_sink = f;
	::operator delete( f, (size_t)24 );
	char *g = strdup( "novalve" );
	g_sink = g;
	free( g );
	void *h = malloc( 8 );
	g_sink = h;
	h = realloc( h, 4096 );
	g_sink = h;
	free( h );
	std::string s( 200, 'x' ); // libstdc++.so's own growth: unwrapped both ways
	std::vector<int> v( 1000 );
	g_sink = &v;
	EXPECT_EQ( 0u, d.Mismatches() );
	EXPECT_EQ( 0u, d.Foreign() );
	EXPECT_GE( d.Allocs(), 8u );
}

TEST( AllocCheck, AlignedBlocksReleasedByFreeRaiseNothing )
{
	// memalign is tier0/memalloc.h's aligned allocator on POSIX
	// (MemAlloc_AllocAligned, CTSQueue's nodes): a block from it that free
	// releases is this module's own, never a foreign free.
	Delta d;
	void *a = memalign( 64, 96 );
	g_sink = a;
	free( a );
	void *b = nullptr;
	const int rc = posix_memalign( &b, 64, 96 );
	g_sink = b;
	free( b );
	void *c = aligned_alloc( 64, 128 );
	g_sink = c;
	free( c );
	// Read before any assertion: gtest's own messages allocate.
	const uint64_t nForeign = d.Foreign(), nMismatches = d.Mismatches(), nAllocs = d.Allocs();
	EXPECT_EQ( 0, rc );
	EXPECT_EQ( 0u, nForeign );
	EXPECT_EQ( 0u, nMismatches );
	EXPECT_EQ( 3u, nAllocs ) << "an aligned allocator is not wrapped";
}

TEST( AllocCheck, MallocReleasedByDeleteIsAMismatch )
{
	Delta d;
	void *p = malloc( 16 );
	g_sink = p;
	::operator delete( p );
	EXPECT_EQ( 1u, d.Mismatches() );
}

TEST( AllocCheck, NewReleasedByFreeIsAMismatch )
{
	Delta d;
	void *p = ::operator new( 16 );
	g_sink = p;
	free( p );
	EXPECT_EQ( 1u, d.Mismatches() );
}

TEST( AllocCheck, ArrayNewReleasedByScalarDeleteIsAMismatch )
{
	Delta d;
	void *p = ::operator new[]( 16 );
	g_sink = p;
	::operator delete( p );
	EXPECT_EQ( 1u, d.Mismatches() );
}

TEST( AllocCheck, SizedDeleteWithTheWrongSizeIsAMismatch )
{
	Delta d;
	void *p = ::operator new( 48 );
	g_sink = p;
	::operator delete( p, (size_t)32 );
	EXPECT_EQ( 1u, d.Mismatches() );
}

TEST( AllocCheck, FreeingABlockThisModuleDidNotAllocateIsForeign )
{
	Delta d;
	void *p = __real_malloc( 64 ); // as the engine would: outside the wraps
	g_sink = p;
	free( p );
	EXPECT_EQ( 1u, d.Foreign() );
	EXPECT_EQ( 0u, d.Mismatches() );
}

TEST( AllocCheck, AnUnreleasedBlockIsLive )
{
	Delta d;
	void *p = malloc( 128 );
	g_sink = p;
	EXPECT_EQ( 1, d.Live() );
	free( p );
	EXPECT_EQ( 0, d.Live() );
}

TEST( AllocCheckDeathTest, AbortModeKillsOnAMismatch )
{
	EXPECT_DEATH(
		{
			SS_DEATH_NO_CORE();
			setenv( "SOURCESHARP_ALLOC_CHECK", "abort", 1 );
			SourceSharp_AllocCheckResetForTest();
			void *p = malloc( 16 );
			::operator delete( p );
		},
		"MISMATCH" );
}

TEST( AllocCheckDeathTest, AbortModeKillsOnAForeignFree )
{
	EXPECT_DEATH(
		{
			SS_DEATH_NO_CORE();
			setenv( "SOURCESHARP_ALLOC_CHECK", "abort", 1 );
			SourceSharp_AllocCheckResetForTest();
			free( __real_malloc( 8 ) );
		},
		"FOREIGN FREE" );
}
