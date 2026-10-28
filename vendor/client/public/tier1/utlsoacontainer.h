//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
// $NoKeywords: $
//
// A Fixed-allocation class for maintaining a 1d or 2d or 3d array of data in a structure-of-arrays
// (SOA) sse-friendly manner.
// =============================================================================//

#ifndef UTLSOACONTAINER_H
#define UTLSOACONTAINER_H

#ifdef _WIN32
#pragma once
#endif


#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier0/threadtools.h"
#include "tier1/utlmemory.h"
#include "tier1/utlblockmemory.h"
#include "mathlib/ssemath.h"


// strided pointers. gives you a class that acts like a pointer, but the ++ and += operators do the
// right thing
template<class T> class CStridedPtr
{
protected:
	T *m_pData = nullptr;
	size_t m_nStride = 0;
	
public:
	FORCEINLINE CStridedPtr( void *pData, size_t nByteStride )
	{
		m_pData = reinterpret_cast<T *>( pData );
		m_nStride = nByteStride / sizeof( T );
	}

	FORCEINLINE CStridedPtr( void ) {}
	T *operator->(void) const
	{
		return m_pData;
	}
	
	T & operator*(void) const
	{
		return *m_pData;
	}
	
	FORCEINLINE operator T *(void)
	{
		return m_pData;
	}

	FORCEINLINE CStridedPtr<T> & operator++(void)
	{
		m_pData += m_nStride;
		return *this;
	}

	FORCEINLINE void operator+=( size_t nNumElements )
	{
		m_pData += nNumElements * m_nStride;
	}

};

template<class T> class CStridedConstPtr
{
protected:
	const T *m_pData;
	size_t m_nStride;

public:
	FORCEINLINE CStridedConstPtr( void const *pData, size_t nByteStride )
	{
		m_pData = reinterpret_cast<T const *>( pData );
		m_nStride = nByteStride / sizeof( T );
	}

	FORCEINLINE CStridedConstPtr( void ) {}

	const T *operator->(void) const
	{
		return m_pData;
	}

	const T & operator*(void) const
	{
		return *m_pData;
	}

	FORCEINLINE operator const T *(void) const
	{
		return m_pData;
	}

	FORCEINLINE CStridedConstPtr<T> &operator++(void)
	{
		m_pData += m_nStride;
		return *this;
	}
	FORCEINLINE void operator+=( size_t nNumElements )
	{
		m_pData += nNumElements*m_nStride;
	}
};

// allowed field data types. if you change these values, you need to change the tables in the .cpp file
enum EAttributeDataType
{
	ATTRDATATYPE_FLOAT = 0,									// a float attribute
	ATTRDATATYPE_4V = 1,									// vector data type, stored as class FourVectors
	ATTRDATATYPE_INT = 2,									// integer. not especially sse-able on
															// all architectures.
	ATTRDATATYPE_POINTER = 3,								// a pointer.
	ATTRDATATYPE_NONE = -1,									// pad and varargs ender
};

#define MAX_SOA_FIELDS 32

class CSOAContainer
{

protected:
	int m_nColumns;											// # of rows and columns created with
	int m_nRows;
	int m_nSlices;

	int m_nPaddedColumns;									// # of columns rounded up for sse
	int m_nNumQuadsPerRow;									// # of groups of 4 elements per row

	uint8 *m_pDataMemory;									// the actual data memory
	uint8 *m_pAttributePtrs[MAX_SOA_FIELDS];

	EAttributeDataType m_nDataType[MAX_SOA_FIELDS];

	size_t m_nStrideInBytes[MAX_SOA_FIELDS];			  // stride from one field datum to another
	size_t m_nRowStrideInBytes[MAX_SOA_FIELDS];			  // stride from one row datum to another per field
	size_t m_nSliceStrideInBytes[MAX_SOA_FIELDS];         // stride from one slice datum to another per field



	uint32 m_nFieldPresentMask;

	FORCEINLINE void Init( void )
	{
		memset( m_nDataType, 0xff, sizeof( m_nDataType ) );
		m_pDataMemory = 0;
		m_nColumns = m_nPaddedColumns = m_nRows = m_nSlices = 0;
		m_nFieldPresentMask = 0;
	}
public:


	CSOAContainer( void )									// an empoty one with no attributes
	{
		Init();
	}

	void Purge( void );										// set back to un-initted state, freeing memory

	~CSOAContainer( void );

	// easy constructor for 2d using varargs. call like
	// #define ATTR_RED 0
	// #define ATTR_GREEN 1
	// #define ATTR_BLUE 2
	// CSOAContainer myimage( 256, 256, ATTR_RED, ATTRDATATYPE_FLOAT, ATTR_GREEN, ATTRDATATYPE_FLOAT,
	//                        ATTR_BLUE, ATTRDATATYPE_FLOAT, -1 );

	CSOAContainer( int nCols, int nRows, ... );

	size_t ElementSize( void ) const;					// total bytes per element. not super fast.


	FORCEINLINE int NumRows( void ) const
	{
		return m_nRows;
	}

	FORCEINLINE int NumCols( void ) const
	{
		return m_nColumns;
	}
	FORCEINLINE int NumSlices( void ) const
	{
		return m_nSlices;
	}


	


	FORCEINLINE int Count( void ) const						// for 1d data
	{
		return NumCols();
	}



	
	FORCEINLINE void *RowPtr( int nAttributeIdx, int nRowNumber, int nSliceNumber = 0 ) const
	{
		Assert( nRowNumber < m_nRows );
		Assert( nAttributeIdx < MAX_SOA_FIELDS );
		Assert( m_nDataType[nAttributeIdx] != ATTRDATATYPE_NONE );
		Assert( m_nFieldPresentMask & ( 1 << nAttributeIdx ) );
		return m_pAttributePtrs[nAttributeIdx] + 
			+ nRowNumber * m_nRowStrideInBytes[nAttributeIdx]
			+ nSliceNumber * m_nSliceStrideInBytes[nAttributeIdx];
	}

	FORCEINLINE void const *ConstRowPtr( int nAttributeIdx, int nRowNumber, int nSliceNumber = 0 ) const
	{
		Assert( nRowNumber < m_nRows );
		Assert( nAttributeIdx < MAX_SOA_FIELDS );
		Assert( m_nDataType[nAttributeIdx] != ATTRDATATYPE_NONE );
		return m_pAttributePtrs[nAttributeIdx] 
			+ nRowNumber * m_nRowStrideInBytes[nAttributeIdx]
			+ nSliceNumber * m_nSliceStrideInBytes[nAttributeIdx];
	}


		
	FORCEINLINE size_t ItemByteStride( int nAttributeIdx ) const
	{
		Assert( nAttributeIdx < MAX_SOA_FIELDS );
		Assert( m_nDataType[nAttributeIdx] != ATTRDATATYPE_NONE );
		return m_nStrideInBytes[ nAttributeIdx ];
	}



	// move all the data from one csoacontainer to another, leaving the source empty.
	// this is just a pointer copy.
	FORCEINLINE void MoveDataFrom( CSOAContainer other )
	{
		(*this) = other;
		other.Init();
	}




	// arithmetic and data filling functions. All SIMD and hopefully fast



};




#endif
