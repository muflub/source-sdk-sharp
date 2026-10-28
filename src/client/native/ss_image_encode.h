// A screenshot's file, encoded in C#: native reads the pixels back and writes
// the file; managed decides its format (TGA or VTF, padded or not). The pixels
// cross once as a BitBlock filled by ImageEncodePixels.
#pragma once

#include "imanaged_areas_client.h"
#include "ss_bitblock.h"
#include "engine_heap.h"

namespace ss
{
enum ImageFileKind_t
{
	kImageTgaRgb = 0,
	kImageTgaRgba = 1,
	kImageVtfRgb = 2
};

// A block for nPixelBytes of pixels: free() it. NULL if out of memory.
inline uint8_t *ImageEncodeBlock( int nPixelBytes )
{
	uint8_t *pBlock = (uint8_t *)malloc( kBitBlockHeaderBytes + nPixelBytes );
	if ( pBlock )
	{
		const int32_t nBits = nPixelBytes * 8;
		memcpy( pBlock, &nBits, sizeof( nBits ) );
	}
	return pBlock;
}

inline unsigned char *ImageEncodePixels( uint8_t *pBlock )
{
	return pBlock + kBitBlockHeaderBytes;
}

// The file for the block's pixels, its size in *pnBytes; NULL when managed is
// not up or refused (the answer in *pnBytes). On the engine heap when
// bEngineHeap (for a filesystem AsyncWrite, which frees with tier0's
// allocator), else malloc()ed: free() it.
inline unsigned char *ImageEncode( ImageFileKind_t eKind, int nWidth, int nHeight, bool bPadPowerOfTwo,
								   const uint8_t *pBlock, int nPixelBytes, int *pnBytes, bool bEngineHeap = false )
{
	*pnBytes = -1;
	IManagedMaterialFilesClient *pFiles = ManagedMaterialFilesClient();
	if ( !pFiles )
		return NULL;
	const int nBlockBytes = kBitBlockHeaderBytes + nPixelBytes;
	const int nNeed =
		pFiles->EncodeImage( eKind, nWidth, nHeight, bPadPowerOfTwo ? 1 : 0, pBlock, nBlockBytes, NULL, 0 );
	if ( nNeed <= 0 )
	{
		*pnBytes = nNeed;
		return NULL;
	}
	IEngineHeap *pHeap = bEngineHeap ? FindEngineHeap() : NULL;
	if ( bEngineHeap && !pHeap )
		return NULL;
	unsigned char *pFile = (unsigned char *)( pHeap ? pHeap->Alloc( nNeed ) : malloc( nNeed ) );
	if ( !pFile )
		return NULL;
	*pnBytes = pFiles->EncodeImage( eKind, nWidth, nHeight, bPadPowerOfTwo ? 1 : 0, pBlock, nBlockBytes, pFile, nNeed );
	if ( *pnBytes != nNeed )
	{
		if ( pHeap )
			pHeap->Free( pFile );
		else
			free( pFile );
		return NULL;
	}
	return pFile;
}
} // namespace ss
