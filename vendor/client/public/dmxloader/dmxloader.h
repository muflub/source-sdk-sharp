//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

// Forward declarations
class CUtlBuffer;
class CDmxElement;

// Used for allocation. All will be freed when we leave the DMX context
void *DMXAlloc( size_t size );

// Helper macro
#define DECLARE_DMX_ALLOCATOR()                                                               \
public:                                                                                       \
	inline void *operator new( size_t size )                                                  \
	{                                                                                         \
		MEM_ALLOC_CREDIT_( "DMXAlloc" );                                                      \
		return DMXAlloc( size );                                                              \
	}                                                                                         \
	inline void *operator new( size_t size, int nBlockUse, const char *pFileName, int nLine ) \
	{                                                                                         \
		MEM_ALLOC_CREDIT_( "DMXAlloc" );                                                      \
		return DMXAlloc( size );                                                              \
	}                                                                                         \
	inline void operator delete( void *p ) {}                                                 \
	inline void operator delete( void *p, int nBlockUse, const char *pFileName, int nLine ) {}
\
