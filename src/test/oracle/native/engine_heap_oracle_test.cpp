//========= SourceSharp — an engine-grown vector goes back to tier0 ==========//
//
// engine_heap.h frees a block an engine interface grew for its caller, through
// the g_pMemAlloc of the libtier0.so already in the process. This holds it to
// the two things it depends on:
//
//   - its three-slot IEngineHeap is tier0's IMemAlloc, slot for slot, per the
//     PRISTINE public/tier0/memalloc.h (Itanium: a pointer to a virtual member
//     holds its vtable offset plus one);
//   - THE GAME'S OWN libtier0.so (the Steam install's, not the SDK's link
//     library, which is a different build) grows a real CUtlVector the way
//     engine.so does, and the vector is read through the view and freed through
//     the lookup.
//
// This binary links no tier0. It defines the g_pMemAlloc the pristine template
// allocates through and points it at the game's allocator once it has loaded
// it -- which is what the engine's copy of the template does. Tests run in
// declaration order: the first ones see a process with no libtier0 at all.
//
//=============================================================================//

#include "tier0/memalloc.h"
#include "tier1/utlvector.h"

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"

#include "engine_heap.h"
#include "engine_utlvector.h"
#include "death_test_no_core.h"

#include <dlfcn.h>
#include <link.h>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <string>

// What the pristine template's malloc/realloc/free resolve to (memdbgon.h).
IMemAlloc *g_pMemAlloc = NULL;

namespace
{
template < class PMF >
long SlotOf( PMF pmf )
{
	static_assert( sizeof( PMF ) == 2 * sizeof( uintptr_t ), "Itanium member pointer" );
	uintptr_t raw[2];
	memcpy( raw, &pmf, sizeof( raw ) );
	EXPECT_EQ( 1u, raw[0] & 1u ) << "not a virtual member";
	return (long)( ( raw[0] - 1 ) / sizeof( void * ) );
}

// Any object in the process whose file is libtier0.so, however it was opened.
bool Tier0Loaded()
{
	bool bFound = false;
	dl_iterate_phdr( []( struct dl_phdr_info *pInfo, size_t, void *pv ) -> int {
		const char *n = pInfo->dlpi_name;
		const char *b = n ? strrchr( n, '/' ) : NULL;
		b = b ? b + 1 : n;
		if ( b && strcmp( b, "libtier0.so" ) == 0 )
			*(bool *)pv = true;
		return 0;
	}, &bFound );
	return bFound;
}

void *LoadGamesTier0()
{
	const char *pszPath = getenv( "SOURCESHARP_ENGINE_TIER0" );
	if ( !pszPath || !*pszPath )
	{
		ADD_FAILURE() << "SOURCESHARP_ENGINE_TIER0 is empty: CMake found no Source SDK Base "
		                 "2013 Multiplayer install in Steam's libraryfolders.vdf. Install it "
		                 "(app 243750) or set SOURCESHARP_ENGINE_TIER0 and re-run `make setup`.";
		return NULL;
	}
	void *h = dlopen( pszPath, RTLD_NOW | RTLD_GLOBAL );
	if ( !h )
		ADD_FAILURE() << "dlopen( " << pszPath << " ): " << dlerror();
	return h;
}

// Stands in the game's exported g_pMemAlloc for the lookup to find, forwarding
// to the real allocator and counting what reaches Free. Only the three slots
// the lookup's IEngineHeap declares are ever called through it.
struct CountingHeap : ss::IEngineHeap
{
	IMemAlloc *m_pReal = NULL;
	int   m_nFrees = 0;
	void *m_pLastFreed = NULL;

	void *Alloc( size_t n ) override              { return m_pReal->Alloc( n ); }
	void *Realloc( void *p, size_t n ) override   { return m_pReal->Realloc( p, n ); }
	void  Free( void *p ) override                { m_nFrees++; m_pLastFreed = p; m_pReal->Free( p ); }
};
}

TEST( EngineHeap, ItsThreeSlotsAreTier0sAllocReallocAndFree )
{
	EXPECT_EQ( SlotOf( static_cast< void *( IMemAlloc::* )( size_t ) >( &IMemAlloc::Alloc ) ),
	           SlotOf( &ss::IEngineHeap::Alloc ) );
	EXPECT_EQ( SlotOf( static_cast< void *( IMemAlloc::* )( void *, size_t ) >( &IMemAlloc::Realloc ) ),
	           SlotOf( &ss::IEngineHeap::Realloc ) );
	EXPECT_EQ( SlotOf( static_cast< void ( IMemAlloc::* )( void * ) >( &IMemAlloc::Free ) ),
	           SlotOf( &ss::IEngineHeap::Free ) );
	EXPECT_EQ( 2, SlotOf( &ss::IEngineHeap::Free ) );
}

TEST( EngineHeap, WithNoTier0InTheProcessTheLookupFindsNothing )
{
	ASSERT_FALSE( Tier0Loaded() ) << "a tier0 is already loaded: this binary must link none";
	EXPECT_EQ( nullptr, ss::FindEngineHeap() );
}

TEST( EngineHeapDeathTest, FreeingWithNoTier0InTheProcessStopsTheProcess )
{
	ASSERT_FALSE( Tier0Loaded() );
	EXPECT_DEATH( { SS_DEATH_NO_CORE(); ss::EngineHeapFree( (void *)0x1000 ); }, "no libtier0.so g_pMemAlloc" );
}

TEST( EngineHeap, FreeingNullIsNothingEvenWithNoTier0 )
{
	ss::EngineHeapFree( NULL );
	SUCCEED();
}

TEST( EngineHeap, TheGamesTier0IsFoundOnceLoaded )
{
	SS_SKIP_ON_CI( "needs the game's own libraries from a Source SDK Base 2013 Multiplayer install, which a runner has not" );
	void *h = LoadGamesTier0();
	ASSERT_NE( nullptr, h );
	ASSERT_TRUE( Tier0Loaded() );

	IMemAlloc **ppExported = (IMemAlloc **)dlsym( h, "g_pMemAlloc" );
	ASSERT_NE( nullptr, ppExported ) << "the game's libtier0.so no longer exports g_pMemAlloc";
	ASSERT_NE( nullptr, *ppExported );
	EXPECT_EQ( (void *)*ppExported, (void *)ss::FindEngineHeap() );
}

TEST( EngineHeap, AVectorTheGamesTier0GrewIsReadThroughTheViewAndFreedThroughTheLookup )
{
	SS_SKIP_ON_CI( "needs the game's own libraries from a Source SDK Base 2013 Multiplayer install, which a runner has not" );
	void *h = LoadGamesTier0();
	ASSERT_NE( nullptr, h );
	IMemAlloc **ppExported = (IMemAlloc **)dlsym( h, "g_pMemAlloc" );
	ASSERT_NE( nullptr, ppExported );
	IMemAlloc *pReal = *ppExported;
	ASSERT_NE( nullptr, pReal );

	// The template in this TU grows through the game's allocator, as engine.so's
	// copy does; the lookup, at release, finds the counting stand-in.
	g_pMemAlloc = pReal;
	CountingHeap counting;
	counting.m_pReal = pReal;
	*ppExported = (IMemAlloc *)(ss::IEngineHeap *)&counting;

	void *pBlock = NULL;
	{
		ss::EngineFilledUtlVector< int > out;
		EXPECT_EQ( 0, out.View().Count() );
		EXPECT_EQ( nullptr, out.View().m_pMemory ) << "not a default-constructed CUtlVector";

		// What IEngineTrace::GetBrushesInAABB does to its out-parameter.
		CUtlVector< int > *pEngineSide = out.ForEngine< CUtlVector< int > >();
		for ( int i = 0; i < 100; i++ )
			pEngineSide->AddToTail( i * 7 );

		const ss::EngineUtlVector< int > &view = out.View();
		ASSERT_EQ( 100, view.Count() );
		for ( int i = 0; i < 100; i++ )
			EXPECT_EQ( i * 7, view[i] );

		// The block is the game allocator's: it knows the block's size.
		pBlock = view.m_pMemory;
		ASSERT_NE( nullptr, pBlock );
		EXPECT_GE( pReal->GetSize( pBlock ), 100 * sizeof( int ) );
		EXPECT_EQ( 0, counting.m_nFrees ) << "released before the holder went";
	}

	*ppExported = pReal;
	g_pMemAlloc = NULL;

	EXPECT_EQ( 1, counting.m_nFrees ) << "the block did not go back through tier0's Free";
	EXPECT_EQ( pBlock, counting.m_pLastFreed );
}
