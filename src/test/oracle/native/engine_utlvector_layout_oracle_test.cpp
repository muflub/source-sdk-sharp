//========= SourceSharp — EngineUtlVector against the pristine CUtlVector ====//
//
// ss::EngineUtlVector<T> (src/shared/native/engine_utlvector.h) reads vectors
// another module built. It is right only if its members sit where the pristine
// CUtlVector<T>'s do. This builds real CUtlVectors from the upstream clone's
// public/tier1/utlvector.h (first on the include path) and reads each through
// the view: size, every member, every element, for element types of several
// sizes and alignments, empty, grown, and after a purge.
//
//=============================================================================//

#include "tier1/utlvector.h"
#include "tier1/utlstring.h"
#include "tier0/memalloc.h"

#include "gtest/gtest.h"

#include "engine_utlvector.h"

#include <cstdint>

namespace
{
struct Vec3   { float x, y, z; };                // 12 bytes, 4-aligned
struct Wide   { double a; int b; };              // 16 bytes, 8-aligned
struct Odd    { char c[7]; };                    // 7 bytes, 1-aligned

// The members CUtlVector keeps protected, by derivation, for the comparison.
template < class T >
struct Open : CUtlVector< T >
{
	T  *Memory() const     { return const_cast< T * >( this->m_Memory.Base() ); }
	int Allocated() const  { return this->m_Memory.NumAllocated(); }
	int Size() const       { return this->m_Size; }
	T  *Elements() const   { return this->m_pElements; }
};

template < class T >
const ss::EngineUtlVector< T > &View( const CUtlVector< T > &v )
{
	return *reinterpret_cast< const ss::EngineUtlVector< T > * >( &v );
}

template < class T >
void ExpectSameReading( const Open< T > &v )
{
	const ss::EngineUtlVector< T > &view = View< T >( v );
	EXPECT_EQ( v.Memory(), view.m_pMemory );
	EXPECT_EQ( v.Allocated(), view.m_nAllocationCount );
	EXPECT_EQ( v.Size(), view.m_Size );
	EXPECT_EQ( v.Count(), view.Count() );
	EXPECT_EQ( v.Elements(), view.m_pElements );
	EXPECT_EQ( v.Base(), view.begin() );
	EXPECT_EQ( v.Base() + v.Count(), view.end() );
	for ( int i = 0; i < v.Count(); i++ )
		EXPECT_EQ( &v[i], &view[i] ) << "element " << i;
}

template < class T, class Make >
void Exercise( Make make )
{
	Open< T > v;
	ExpectSameReading( v );                      // empty: no block yet
	for ( int i = 0; i < 37; i++ )               // several growths
		v.AddToTail( make( i ) );
	ASSERT_EQ( 37, v.Count() );
	ASSERT_GE( v.Allocated(), 37 );
	ExpectSameReading( v );
	v.Remove( 3 );
	ExpectSameReading( v );
	v.Purge();
	ExpectSameReading( v );
}
}

TEST( EngineUtlVectorLayout, TheViewIsTheSizeOfTheVectorItReads )
{
	static_assert( sizeof( ss::EngineUtlVector< int > )           == sizeof( CUtlVector< int > ), "int" );
	static_assert( sizeof( ss::EngineUtlVector< unsigned short > ) == sizeof( CUtlVector< unsigned short > ), "ushort" );
	static_assert( sizeof( ss::EngineUtlVector< Vec3 > )          == sizeof( CUtlVector< Vec3 > ), "Vec3" );
	static_assert( sizeof( ss::EngineUtlVector< Wide > )          == sizeof( CUtlVector< Wide > ), "Wide" );
	static_assert( sizeof( ss::EngineUtlVector< Odd > )           == sizeof( CUtlVector< Odd > ), "Odd" );
	static_assert( alignof( ss::EngineUtlVector< int > )          == alignof( CUtlVector< int > ), "align" );
	SUCCEED();
}

TEST( EngineUtlVectorLayout, IntsReadTheSameThroughTheView )
{
	Exercise< int >( []( int i ) { return i * 3 + 1; } );
}

TEST( EngineUtlVectorLayout, UnsignedShortsReadTheSameThroughTheView )
{
	// virtualmodel_t::m_autoplaySequences is a CUtlVector< unsigned short >.
	Exercise< unsigned short >( []( int i ) { return (unsigned short)( 60000 + i ); } );
}

TEST( EngineUtlVectorLayout, TwelveByteStructsReadTheSameThroughTheView )
{
	Exercise< Vec3 >( []( int i ) { return Vec3{ (float)i, 0.5f, -1.0f }; } );
}

TEST( EngineUtlVectorLayout, EightAlignedStructsReadTheSameThroughTheView )
{
	Exercise< Wide >( []( int i ) { return Wide{ i * 0.25, i }; } );
}

TEST( EngineUtlVectorLayout, OneAlignedStructsReadTheSameThroughTheView )
{
	Exercise< Odd >( []( int i ) { Odd o{}; o.c[0] = (char)i; return o; } );
}

TEST( EngineUtlVectorLayout, AVectorInsideAStructIsReadWhereTheStructPutsIt )
{
	// How the model cache's vectors are reached: members of a larger object.
	struct Holder { int before; CUtlVector< int > v; char after; };
	Holder h;
	h.before = 7;
	h.after = 9;
	for ( int i = 0; i < 5; i++ )
		h.v.AddToTail( i * i );
	const ss::EngineUtlVector< int > &view = View< int >( h.v );
	ASSERT_EQ( 5, view.Count() );
	for ( int i = 0; i < 5; i++ )
		EXPECT_EQ( i * i, view[i] );
	EXPECT_EQ( 7, h.before );
	EXPECT_EQ( 9, h.after );
}

//-----------------------------------------------------------------------------
// APPENDING TO THE OWNER'S VECTOR (ss::EngineUtlStringVectorAppend), handed to
// the pristine template. ConCommand::AutoCompleteSuggest fills the engine's
// CUtlVector<CUtlString> this way; the engine then reads it and destroys it
// with its own template and allocator. Here the owner is this binary: the heap
// is its own tier0 (g_pMemAlloc), the reading is CUtlString::Get and the
// destruction is CUtlVector's and CUtlString's own destructors -- which free
// every block through that same tier0. A block from anywhere else is a heap
// error there.
//-----------------------------------------------------------------------------
namespace
{
struct Tier0Heap : ss::IEngineHeap
{
	int m_nCalls = 0;
	void *Alloc( size_t n ) override { m_nCalls++; return g_pMemAlloc->Alloc( n ); }
	void *Realloc( void *p, size_t n ) override { m_nCalls++; return g_pMemAlloc->Realloc( p, n ); }
	void  Free( void *p ) override { m_nCalls++; g_pMemAlloc->Free( p ); }
};
}

TEST( EngineUtlVectorLayout, TheStringLayoutIsCUtlStrings )
{
	static_assert( sizeof( ss::EngineUtlString ) == sizeof( CUtlString ), "CUtlString is one char *" );
	static_assert( sizeof( ss::EngineUtlVectorLayout< ss::EngineUtlString > ) == sizeof( CUtlVector< CUtlString > ),
	               "CUtlVector<CUtlString>" );
	SUCCEED();
}

TEST( EngineUtlVectorLayout, StringsAppendedWithTheOwnersHeapAreReadAndDestroyedByItsTemplate )
{
	Tier0Heap heap;
	{
		CUtlVector< CUtlString > owned;   // the engine's, as AutoCompleteSuggest receives it
		owned.AddToTail( CUtlString( "already_there" ) );

		ss::EngineUtlVectorLayout< ss::EngineUtlString > &vec =
			ss::EngineUtlVectorLayoutOf< ss::EngineUtlString >( owned );
		char szName[ 32 ];
		for ( int i = 0; i < 40; i++ )
		{
			snprintf( szName, sizeof( szName ), "suggestion_%02d", i );
			ASSERT_TRUE( ss::EngineUtlStringVectorAppend( &heap, vec, szName ) );
		}

		ASSERT_EQ( 41, owned.Count() );
		EXPECT_STREQ( "already_there", owned[0].Get() );
		for ( int i = 0; i < 40; i++ )
		{
			snprintf( szName, sizeof( szName ), "suggestion_%02d", i );
			EXPECT_STREQ( szName, owned[ i + 1 ].Get() );
		}

		// And the owner can keep using its own template on it.
		owned.AddToTail( CUtlString( "after" ) );
		EXPECT_STREQ( "after", owned[41].Get() );
	}   // destroyed by CUtlVector<CUtlString>: every string and the block, through tier0

	EXPECT_GT( heap.m_nCalls, 40 ) << "the appends did not go through the owner's heap";
}
