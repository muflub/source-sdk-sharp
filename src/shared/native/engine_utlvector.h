//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Views of CUtlVectors built by another module, by layout rather than tier1's template,
// whose growth would pull in tier0's allocator. The blocks belong to their builder.

#pragma once

#include "engine_heap.h"

#include <type_traits>

namespace ss
{

// CUtlVector<T>'s bytes (PC layout). All zero is a default-constructed vector.
template <class T>
struct EngineUtlVectorLayout
{
	// CUtlMemory<T>
	T *m_pMemory;
	int m_nAllocationCount;
	int m_nGrowSize;
	// CUtlVector<T>
	int m_Size;
	T *m_pElements;
};

template <class T>
struct EngineUtlVector : EngineUtlVectorLayout<T>
{
	using EngineUtlVectorLayout<T>::m_pMemory;
	using EngineUtlVectorLayout<T>::m_nAllocationCount;
	using EngineUtlVectorLayout<T>::m_nGrowSize;
	using EngineUtlVectorLayout<T>::m_Size;
	using EngineUtlVectorLayout<T>::m_pElements;

	EngineUtlVector() = delete;
	EngineUtlVector( const EngineUtlVector & ) = delete;
	EngineUtlVector &operator=( const EngineUtlVector & ) = delete;

	int Count() const { return m_Size; }

	bool IsValidIndex( int i ) const { return i >= 0 && i < m_Size; }

	const T &operator[]( int i ) const { return m_pMemory[i]; }

	const T *begin() const { return m_pMemory; }

	const T *end() const { return m_pMemory + m_Size; }
};

// An out-parameter an engine interface fills. Its block is freed with tier0's allocator,
// the one the engine's copy of the template grew it with.
template <class T>
class EngineFilledUtlVector
{
public:
	EngineFilledUtlVector()
		: m_Vec()
	{
	}

	~EngineFilledUtlVector() { EngineHeapFree( m_Vec.m_pMemory ); }

	EngineFilledUtlVector( const EngineFilledUtlVector & ) = delete;
	EngineFilledUtlVector &operator=( const EngineFilledUtlVector & ) = delete;

	// V is the interface's CUtlVector<T>, named by a caller that has the engine header.
	template <class V>
	V *ForEngine()
	{
		static_assert( sizeof( V ) == sizeof( EngineUtlVectorLayout<T> ), "not the layout of a CUtlVector of this T" );
		return reinterpret_cast<V *>( &m_Vec );
	}

	const EngineUtlVector<T> &View() const { return *reinterpret_cast<const EngineUtlVector<T> *>( &m_Vec ); }

private:
	EngineUtlVectorLayout<T> m_Vec;
};

// Appends to a vector another module owns and frees, e.g. AutoCompleteSuggest's
// CUtlVector<CUtlString>: growth goes through that module's heap. T is copied bytewise.
// False, with nothing changed, for external memory (m_nGrowSize < 0), no heap, or a refusal.
template <class T>
bool EngineUtlVectorAppend( IEngineHeap *pHeap, EngineUtlVectorLayout<T> &vec, const T &value )
{
	static_assert( std::is_trivially_copyable<T>::value, "an element is copied bytewise" );

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

	memcpy( (void *)&vec.m_pMemory[vec.m_Size], (const void *)&value, sizeof( T ) );
	vec.m_Size++;
	vec.m_pElements = vec.m_pMemory;
	return true;
}

// The layout of a vector of type V (the interface's CUtlVector<...>, named by
// the caller, which has the header) as elements T.
template <class T, class V>
EngineUtlVectorLayout<T> &EngineUtlVectorLayoutOf( V &vec )
{
	static_assert( sizeof( V ) == sizeof( EngineUtlVectorLayout<T> ), "not the layout of a CUtlVector of this T" );
	return *reinterpret_cast<EngineUtlVectorLayout<T> *>( &vec );
}

// CUtlString's layout: one char *, freed by the owner's CUtlString::Purge.
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
inline bool EngineUtlStringVectorAppend( IEngineHeap *pHeap, EngineUtlVectorLayout<EngineUtlString> &vec,
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
