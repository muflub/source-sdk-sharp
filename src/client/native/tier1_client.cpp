//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — tier1's small members client.so links =============//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// plan_client_novalve C9, the small tier1 pieces: what client.so calls from
// tier1 objects too small to carry their own file, so each object leaves the
// client's link. The server links no tier1 and calls none of these.
//
//   checksum_crc.cpp  CRC32_Init / ProcessBuffer / Final: sentence.cpp's
//                     phoneme checksum, shell_input.cpp's command checksum,
//                     SharedRandom's seed (util_shared_library.cpp). Over
//                     ss_crc32.h, held to pristine checksum_crc.cpp by
//                     ss_crc32_oracle_test.
//   rangecheckedvar.cpp g_bDoRangeChecks, read by sequence_Transitioner.cpp;
//                     no CDisableRangeChecks is made on client.so.
//   mempool.cpp       CUtlMemoryPool (clientleafsystem.cpp, viewrender.cpp,
//                     the temp-entity pools, the view executor), held to
//                     pristine mempool.cpp by mempool_client_test. No leak
//                     report: nothing on client.so installs a report function.
//
// Exit: each name's last native caller leaving.
//=============================================================================//

#include "tier1/checksum_crc.h"
#include "tier1/mempool.h"
#include "ss_crc32.h"

#include <stdint.h>
#include <stdlib.h>
#include <string.h>

void CRC32_Init( CRC32_t *pulCRC )
{
	*pulCRC = ss::Crc32Start();
}

void CRC32_ProcessBuffer( CRC32_t *pulCRC, const void *p, int len )
{
	*pulCRC = ss::Crc32Add( *pulCRC, p, len );
}

void CRC32_Final( CRC32_t *pulCRC )
{
	*pulCRC = ss::Crc32Finish( *pulCRC );
}

// rangecheckedvar.cpp's switch, which sequence_Transitioner.cpp's
// CRangeCheckedVar reads; nothing on client.so turns it off.
bool g_bDoRangeChecks = true;

//-----------------------------------------------------------------------------
// CUtlMemoryPool: fixed-size blocks carved from blobs on a ring, handed out
// and taken back through a free list threaded through the blocks themselves
// (last freed, first reused). A block is at least a pointer, rounded up to
// the alignment. A new blob holds numElements blocks, times the blob count
// so far plus one when growing FAST, plain numElements when SLOW; a NONE pool
// has its first blob only.
//-----------------------------------------------------------------------------
CUtlMemoryPool::CUtlMemoryPool( int blockSize, int numElements, int growMode, const char *pszAllocOwner, int nAlignment )
{
	m_nAlignment = (unsigned short)( nAlignment ? nAlignment : 1 );
	const int nAtLeast = blockSize < (int)sizeof( void * ) ? (int)sizeof( void * ) : blockSize;
	m_BlockSize = ( nAtLeast + m_nAlignment - 1 ) / m_nAlignment * m_nAlignment;
	m_BlocksPerBlob = numElements;
	m_GrowMode = growMode;
	m_PeakAlloc = 0;
	m_pszAllocOwner = pszAllocOwner ? pszAllocOwner : __FILE__;
	Init();
	AddNewBlob();
}

CUtlMemoryPool::~CUtlMemoryPool()
{
	Clear();
}

void CUtlMemoryPool::Init()
{
	m_BlobHead.m_pNext = &m_BlobHead;
	m_BlobHead.m_pPrev = &m_BlobHead;
	m_pHeadOfFreeList = NULL;
	m_BlocksAllocated = 0;
	m_NumBlobs = 0;
}

void CUtlMemoryPool::Clear()
{
	CBlob *pBlob = m_BlobHead.m_pNext;
	while ( pBlob != &m_BlobHead )
	{
		CBlob *pAfter = pBlob->m_pNext;
		free( pBlob );
		pBlob = pAfter;
	}
	Init();
}

void CUtlMemoryPool::AddNewBlob()
{
	if ( m_GrowMode == UTLMEMORYPOOL_GROW_NONE && m_NumBlobs != 0 )
		return;
	const int nScale = ( m_GrowMode == UTLMEMORYPOOL_GROW_SLOW ) ? 1 : m_NumBlobs + 1;
	const int nBlocks = m_BlocksPerBlob * nScale;
	const int nBytes = m_BlockSize * nBlocks;

	// The header, the data, and room to align the first block.
	CBlob *pBlob = (CBlob *)malloc( sizeof( CBlob ) - 1 + nBytes + ( m_nAlignment - 1 ) );
	pBlob->m_NumBytes = nBytes;
	pBlob->m_pNext = &m_BlobHead;
	pBlob->m_pPrev = m_BlobHead.m_pPrev;
	m_BlobHead.m_pPrev->m_pNext = pBlob;
	m_BlobHead.m_pPrev = pBlob;

	uintptr_t first = (uintptr_t)pBlob->m_Data;
	first = ( first + m_nAlignment - 1 ) / m_nAlignment * m_nAlignment;
	char *pBlock = (char *)first;
	m_pHeadOfFreeList = pBlock;
	for ( int i = 0; i + 1 < nBlocks; i++, pBlock += m_BlockSize )
		*(void **)pBlock = pBlock + m_BlockSize;
	*(void **)pBlock = NULL;
	m_NumBlobs++;
}

void *CUtlMemoryPool::Alloc()
{
	return Alloc( m_BlockSize );
}

void *CUtlMemoryPool::AllocZero()
{
	return AllocZero( m_BlockSize );
}

void *CUtlMemoryPool::Alloc( size_t amount )
{
	if ( amount > (size_t)(unsigned int)m_BlockSize )
		return NULL;
	if ( !m_pHeadOfFreeList )
	{
		if ( m_GrowMode == UTLMEMORYPOOL_GROW_NONE )
			return NULL;
		AddNewBlob();
		if ( !m_pHeadOfFreeList )
			return NULL;
	}
	void *pBlock = m_pHeadOfFreeList;
	m_pHeadOfFreeList = *(void **)pBlock;
	m_BlocksAllocated++;
	if ( m_BlocksAllocated > m_PeakAlloc )
		m_PeakAlloc = m_BlocksAllocated;
	return pBlock;
}

void *CUtlMemoryPool::AllocZero( size_t amount )
{
	void *pBlock = Alloc( amount );
	if ( pBlock )
		memset( pBlock, 0, amount );
	return pBlock;
}

void CUtlMemoryPool::Free( void *memBlock )
{
	if ( !memBlock )
		return;
	*(void **)memBlock = m_pHeadOfFreeList;
	m_pHeadOfFreeList = memBlock;
	m_BlocksAllocated--;
}
