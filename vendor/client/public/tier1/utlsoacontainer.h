//========= Copyright Valve Corporation, All rights reserved. ============//
// A Fixed-allocation class for maintaining a 1d or 2d or 3d array of data in a structure-of-arrays
// (SOA) sse-friendly manner.

#pragma once

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

	FORCEINLINE CStridedPtr() {}
	
	FORCEINLINE operator T *()
	{
		return m_pData;
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

	FORCEINLINE CStridedConstPtr() {}

	FORCEINLINE operator const T *() const
	{
		return m_pData;
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

	FORCEINLINE void Init()
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

	void Purge();										// set back to un-initted state, freeing memory

	~CSOAContainer();

	// easy constructor for 2d using varargs. call like
	// #define ATTR_RED 0
	// #define ATTR_GREEN 1
	// #define ATTR_BLUE 2
	// CSOAContainer myimage( 256, 256, ATTR_RED, ATTRDATATYPE_FLOAT, ATTR_GREEN, ATTRDATATYPE_FLOAT,
	//                        ATTR_BLUE, ATTRDATATYPE_FLOAT, -1 );

	CSOAContainer( int nCols, int nRows, ... );

	size_t ElementSize() const;					// total bytes per element. not super fast.

	FORCEINLINE int NumCols() const
	{
		return m_nColumns;
	}

	FORCEINLINE int Count() const						// for 1d data
	{
		return NumCols();
	}

	// arithmetic and data filling functions. All SIMD and hopefully fast

};
