//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
//=============================================================================

#ifndef DMXLOADER_H
#define DMXLOADER_H

#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Forward declarations
//-----------------------------------------------------------------------------
class CUtlBuffer;
class CDmxElement;




//-----------------------------------------------------------------------------
// DMX elements/attributes can only be accessed inside a dmx context
//-----------------------------------------------------------------------------
void BeginDMXContext( );
void EndDMXContext( bool bDecommitMemory );


//-----------------------------------------------------------------------------
// Helper macro
//-----------------------------------------------------------------------------
class CDMXContextHelper
{
public:
	CDMXContextHelper( bool bDecommitMemory ) { m_bDecommitMemory = bDecommitMemory; BeginDMXContext(); }
	~CDMXContextHelper() { EndDMXContext( m_bDecommitMemory ); }

private:
	bool m_bDecommitMemory;
};






//-----------------------------------------------------------------------------
// Used for allocation. All will be freed when we leave the DMX context
//-----------------------------------------------------------------------------
void* DMXAlloc( size_t size );


//-----------------------------------------------------------------------------
// Helper macro
//-----------------------------------------------------------------------------
#define DECLARE_DMX_ALLOCATOR( )												\
	public:																		\
		inline void* operator new( size_t size ) { MEM_ALLOC_CREDIT_( "DMXAlloc" ); return DMXAlloc(size); }   \
		inline void* operator new( size_t size, int nBlockUse, const char *pFileName, int nLine ) { MEM_ALLOC_CREDIT_( "DMXAlloc" ); return DMXAlloc(size); }   \
		inline void  operator delete( void* p ) { }		\
		inline void  operator delete( void* p, int nBlockUse, const char *pFileName, int nLine ) { }   \

#endif // DMXLOADER_H
