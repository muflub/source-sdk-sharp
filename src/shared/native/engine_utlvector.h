//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — a read-only view of an engine-built CUtlVector =====//
//
// Some of what server.so reads was built by another module: the model cache's
// virtualmodel_t, and any CUtlVector an engine interface fills for its caller.
// Reading one needs its LAYOUT, not tier1's template -- whose growth and purge
// call tier0's allocator (utlmemory.h under memdbgon.h), which is what keeps
// g_pMemAlloc in server.so's imports.
//
// The layout, from public/tier1/utlmemory.h and utlvector.h (the PC arm):
//   CUtlMemory<T>   T *m_pMemory; int m_nAllocationCount; int m_nGrowSize;
//   CUtlVector<T>   CUtlMemory<T> m_Memory; int m_Size; T *m_pElements;
// (CUtlVector's one base, base_vector_t, is empty.) The elements are
// m_pMemory[0 .. m_Size); m_pElements is a debugger copy of m_pMemory.
//
// EngineUtlVector is a VIEW: never constructed, grown or freed here -- the block
// belongs to the module that built it. EngineFilledUtlVector is the one this
// module owns the HANDLE of: an out-parameter the engine grows, released to the
// engine's allocator. engine_utlvector_layout_oracle_test holds the layout to
// the pristine CUtlVector, member by member, for several T.
//
//=============================================================================//

#pragma once

#include "engine_heap.h"

#include <string.h>
#include <type_traits>

namespace ss
{

// The bytes, in CUtlVector<T>'s order. All zero is a default-constructed
// CUtlVector: no block, no elements, the default growth policy.
template < class T >
struct EngineUtlVectorLayout
{
	// CUtlMemory<T>
	T  *m_pMemory;
	int m_nAllocationCount;
	int m_nGrowSize;
	// CUtlVector<T>
	int m_Size;
	T  *m_pElements;
};

template < class T >
struct EngineUtlVector : EngineUtlVectorLayout< T >
{
	using EngineUtlVectorLayout< T >::m_pMemory;
	using EngineUtlVectorLayout< T >::m_nAllocationCount;
	using EngineUtlVectorLayout< T >::m_nGrowSize;
	using EngineUtlVectorLayout< T >::m_Size;
	using EngineUtlVectorLayout< T >::m_pElements;

	EngineUtlVector() = delete;
	EngineUtlVector( const EngineUtlVector & ) = delete;
	EngineUtlVector &operator=( const EngineUtlVector & ) = delete;

	int  Count() const                { return m_Size; }
	bool IsValidIndex( int i ) const  { return i >= 0 && i < m_Size; }
	const T &operator[]( int i ) const { return m_pMemory[i]; }
	const T *begin() const            { return m_pMemory; }
	const T *end() const              { return m_pMemory + m_Size; }
};

//-----------------------------------------------------------------------------
// An out-parameter an engine interface fills. It starts as a default-constructed
// CUtlVector<T>, is handed to the engine as one (ForEngine), is read through the
// view, and on destruction gives the block back to tier0's allocator, which is
// the allocator the engine's copy of the template grew it with (engine_heap.h).
//-----------------------------------------------------------------------------
template < class T >
class EngineFilledUtlVector
{
public:
	EngineFilledUtlVector() : m_Vec() {}
	~EngineFilledUtlVector() { EngineHeapFree( m_Vec.m_pMemory ); }

	EngineFilledUtlVector( const EngineFilledUtlVector & ) = delete;
	EngineFilledUtlVector &operator=( const EngineFilledUtlVector & ) = delete;

	// V is the interface's CUtlVector<T>, named by the caller, which has the
	// engine header; this one does not.
	template < class V >
	V *ForEngine()
	{
		static_assert( sizeof( V ) == sizeof( EngineUtlVectorLayout< T > ),
		               "not the layout of a CUtlVector of this T" );
		return reinterpret_cast< V * >( &m_Vec );
	}

	const EngineUtlVector< T > &View() const
	{
		return *reinterpret_cast< const EngineUtlVector< T > * >( &m_Vec );
	}

private:
	EngineUtlVectorLayout< T > m_Vec;
};


//-----------------------------------------------------------------------------
// APPENDING TO A VECTOR ANOTHER MODULE OWNS -- an in/out parameter the engine
// passes in and destroys itself, with its own copy of tier1's template: e.g.
// ConCommand::AutoCompleteSuggest's CUtlVector<CUtlString>. Its block, and
// every block an element owns, must come from THAT module's allocator, so the
// growth here goes through the heap given (FindEngineHeap()), never through
// this module's malloc or tier1's CUtlMemory.
//
// The layout is CUtlVector<T>'s (above). Growth doubles (from 4), rounded up
// to m_nGrowSize when one is set; any capacity >= the count is a valid state
// for the owner's template to carry on from. m_pElements follows m_pMemory,
// as CUtlVector::ResetDbgInfo keeps it. T is copied bytewise, so it must be
// trivially copyable: a layout, not a class with a destructor.
//
// False, and nothing changed, for a vector over external memory (m_nGrowSize
// < 0: CUtlMemory never grows those), no heap, or a heap that refuses.
//-----------------------------------------------------------------------------
template < class T >
bool EngineUtlVectorAppend( IEngineHeap *pHeap, EngineUtlVectorLayout< T > &vec, const T &value )
{
	static_assert( std::is_trivially_copyable< T >::value, "an element is copied bytewise" );

	if ( !pHeap || vec.m_nGrowSize < 0 || vec.m_Size < 0 )
		return false;

	if ( vec.m_Size >= vec.m_nAllocationCount )
	{
		int nNew = ( vec.m_nAllocationCount > 0 ) ? vec.m_nAllocationCount * 2 : 4;
		if ( vec.m_nGrowSize > 0 )
			nNew = ( ( vec.m_Size + 1 + vec.m_nGrowSize - 1 ) / vec.m_nGrowSize ) * vec.m_nGrowSize;
		if ( nNew <= vec.m_Size )
			return false;

		const size_t nBytes = (size_t)nNew * sizeof( T );
		void *pNew = vec.m_pMemory ? pHeap->Realloc( vec.m_pMemory, nBytes ) : pHeap->Alloc( nBytes );
		if ( !pNew )
			return false;

		vec.m_pMemory = (T *)pNew;
		vec.m_nAllocationCount = nNew;
	}

	memcpy( (void *)&vec.m_pMemory[ vec.m_Size ], (const void *)&value, sizeof( T ) );
	vec.m_Size++;
	vec.m_pElements = vec.m_pMemory;
	return true;
}

// The layout of a vector of type V (the interface's CUtlVector<...>, named by
// the caller, which has the header) as elements T.
template < class T, class V >
EngineUtlVectorLayout< T > &EngineUtlVectorLayoutOf( V &vec )
{
	static_assert( sizeof( V ) == sizeof( EngineUtlVectorLayout< T > ),
	               "not the layout of a CUtlVector of this T" );
	return *reinterpret_cast< EngineUtlVectorLayout< T > * >( &vec );
}

//-----------------------------------------------------------------------------
// CUtlString's layout (public/tier1/utlstring.h: one member, char *m_pString,
// freed by the owner's CUtlString::Purge through its free()), and a copy of a
// C string in the owner's heap to put in one.
//-----------------------------------------------------------------------------
struct EngineUtlString
{
	char *m_pString;
};

inline char *EngineHeapStrDup( IEngineHeap *pHeap, const char *psz )
{
	if ( !pHeap || !psz )
		return NULL;

	const size_t nBytes = strlen( psz ) + 1;
	char *pCopy = (char *)pHeap->Alloc( nBytes );
	if ( pCopy )
		memcpy( pCopy, psz, nBytes );
	return pCopy;
}

// Appends a copy of psz, as a CUtlString, to an owner's CUtlVector<CUtlString>.
// The copy goes back to the heap if the vector cannot take it.
inline bool EngineUtlStringVectorAppend( IEngineHeap *pHeap, EngineUtlVectorLayout< EngineUtlString > &vec,
                                         const char *psz )
{
	EngineUtlString str = { EngineHeapStrDup( pHeap, psz ) };
	if ( !str.m_pString )
		return false;

	if ( EngineUtlVectorAppend( pHeap, vec, str ) )
		return true;

	pHeap->Free( str.m_pString );
	return false;
}

} // namespace ss
