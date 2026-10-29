//========= SourceSharp — appending to a vector another module owns ==========//
//
// ss::EngineUtlVectorAppend / EngineUtlStringVectorAppend (engine_utlvector.h)
// grow an engine-owned CUtlVector with the ENGINE's allocator, so the engine's
// own template can destroy it. ConCommand::AutoCompleteSuggest
// (convar_port.cpp) is the caller. This drives them against a heap that
// records every call; engine_utlvector_layout_oracle_test hands the result to
// the pristine CUtlVector<CUtlString> to read and destroy.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "engine_utlvector.h"

#include <cstdlib>
#include <cstring>
#include <set>

namespace
{

class RecordingHeap : public ss::IEngineHeap
{
public:
	int m_nAllocs = 0, m_nReallocs = 0, m_nFrees = 0;
	bool m_bRefuse = false;
	std::set<void *> m_Live;

	void *Alloc( size_t n ) override
	{
		m_nAllocs++;
		if ( m_bRefuse )
			return NULL;
		void *p = malloc( n );
		m_Live.insert( p );
		return p;
	}

	void *Realloc( void *p, size_t n ) override
	{
		m_nReallocs++;
		if ( m_bRefuse )
			return NULL;
		m_Live.erase( p );
		void *q = realloc( p, n );
		m_Live.insert( q );
		return q;
	}

	void Free( void *p ) override
	{
		m_nFrees++;
		m_Live.erase( p );
		free( p );
	}

	~RecordingHeap()
	{
		for ( void *p : m_Live )
			free( p );
	}
};

TEST( EngineUtlVectorAppend, AppendsInOrderAndGrowsThroughTheHeap )
{
	RecordingHeap heap;
	ss::EngineUtlVectorLayout<int> vec = {};

	for ( int i = 0; i < 100; i++ )
		ASSERT_TRUE( ss::EngineUtlVectorAppend( &heap, vec, i * 3 ) );

	ASSERT_EQ( 100, vec.m_Size );
	EXPECT_GE( vec.m_nAllocationCount, 100 );
	for ( int i = 0; i < 100; i++ )
		EXPECT_EQ( i * 3, vec.m_pMemory[i] );
	EXPECT_EQ( vec.m_pMemory, vec.m_pElements ) << "the debugger copy follows the block";

	EXPECT_EQ( 1, heap.m_nAllocs ) << "the first block comes from Alloc";
	EXPECT_GT( heap.m_nReallocs, 0 ) << "and it grows through Realloc";
	EXPECT_EQ( 0, heap.m_nFrees ) << "nothing is freed here: the owner frees";
	EXPECT_EQ( 1u, heap.m_Live.count( vec.m_pMemory ) ) << "the block is the heap's";
}

TEST( EngineUtlVectorAppend, AppendsAfterWhatTheOwnerAlreadyHas )
{
	RecordingHeap heap;
	ss::EngineUtlVectorLayout<int> vec = {};
	vec.m_pMemory = (int *)heap.Alloc( 3 * sizeof( int ) );
	vec.m_nAllocationCount = 3;
	vec.m_pMemory[0] = 10;
	vec.m_pMemory[1] = 11;
	vec.m_Size = 2;

	ASSERT_TRUE( ss::EngineUtlVectorAppend( &heap, vec, 12 ) );
	ASSERT_TRUE( ss::EngineUtlVectorAppend( &heap, vec, 13 ) );

	ASSERT_EQ( 4, vec.m_Size );
	EXPECT_EQ( 10, vec.m_pMemory[0] );
	EXPECT_EQ( 11, vec.m_pMemory[1] );
	EXPECT_EQ( 12, vec.m_pMemory[2] );
	EXPECT_EQ( 13, vec.m_pMemory[3] );
}

TEST( EngineUtlVectorAppend, AGrowSizeRoundsTheCapacityUp )
{
	RecordingHeap heap;
	ss::EngineUtlVectorLayout<int> vec = {};
	vec.m_nGrowSize = 16;

	ASSERT_TRUE( ss::EngineUtlVectorAppend( &heap, vec, 1 ) );
	EXPECT_EQ( 16, vec.m_nAllocationCount );
	for ( int i = 0; i < 16; i++ )
		ASSERT_TRUE( ss::EngineUtlVectorAppend( &heap, vec, i ) );
	EXPECT_EQ( 32, vec.m_nAllocationCount );
}

TEST( EngineUtlVectorAppend, ExternalMemoryOrARefusingHeapChangesNothing )
{
	RecordingHeap heap;
	int buffer[2] = { 7, 8 };
	ss::EngineUtlVectorLayout<int> ext = { buffer, 2, -1, 2, buffer };
	EXPECT_FALSE( ss::EngineUtlVectorAppend( &heap, ext, 9 ) );
	EXPECT_EQ( 2, ext.m_Size );
	EXPECT_EQ( buffer, ext.m_pMemory );

	heap.m_bRefuse = true;
	ss::EngineUtlVectorLayout<int> vec = {};
	EXPECT_FALSE( ss::EngineUtlVectorAppend( &heap, vec, 1 ) );
	EXPECT_EQ( 0, vec.m_Size );
	EXPECT_TRUE( vec.m_pMemory == NULL );

	EXPECT_FALSE( ss::EngineUtlVectorAppend( (ss::IEngineHeap *)NULL, vec, 1 ) );
}

TEST( EngineUtlStringVectorAppend, EachStringIsTheHeapsOwnCopy )
{
	RecordingHeap heap;
	ss::EngineUtlVectorLayout<ss::EngineUtlString> vec = {};
	char szSource[] = "sv_gravity";

	ASSERT_TRUE( ss::EngineUtlStringVectorAppend( &heap, vec, szSource ) );
	ASSERT_TRUE( ss::EngineUtlStringVectorAppend( &heap, vec, "sv_cheats" ) );

	ASSERT_EQ( 2, vec.m_Size );
	EXPECT_NE( szSource, vec.m_pMemory[0].m_pString );
	szSource[0] = 'X';
	EXPECT_STREQ( "sv_gravity", vec.m_pMemory[0].m_pString );
	EXPECT_STREQ( "sv_cheats", vec.m_pMemory[1].m_pString );
	EXPECT_EQ( 1u, heap.m_Live.count( vec.m_pMemory[0].m_pString ) ) << "the string is the heap's";
	EXPECT_EQ( 1u, heap.m_Live.count( vec.m_pMemory[1].m_pString ) );
}

TEST( EngineUtlStringVectorAppend, ACopyTheVectorCannotTakeGoesBackToTheHeap )
{
	RecordingHeap heap;
	int buffer[1];
	// External memory: the vector refuses, the string copy must not leak.
	ss::EngineUtlVectorLayout<ss::EngineUtlString> ext = { (ss::EngineUtlString *)buffer, 0, -1, 0,
														   (ss::EngineUtlString *)buffer };

	EXPECT_FALSE( ss::EngineUtlStringVectorAppend( &heap, ext, "sv_gravity" ) );
	EXPECT_EQ( 1, heap.m_nAllocs );
	EXPECT_EQ( 1, heap.m_nFrees );
	EXPECT_TRUE( heap.m_Live.empty() );
}

} // namespace
