//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "tier0/platform.h"
#include "bitmap/imageformat.h" // ImageFormat defn.

class IVTFTexture;
class ITexture;
struct Rect_t;

// This will get called on procedural textures to re-fill the textures
// with the appropriate bit pattern. Calling Download() will also
// cause this interface to be called. It will also be called upon
// mode switch, or on other occasions where the bits are discarded.
abstract_class ITextureRegenerator
{
public:
	// This will be called when the texture bits need to be regenerated.
	// Use the VTFTexture interface, which has been set up with the
	// appropriate texture size + format
	// The rect specifies which part of the texture needs to be updated
	// You can choose to update all of the bits if you prefer
	virtual void unused_RegenerateTextureBits() = 0;

	// This will be called when the regenerator needs to be deleted
	// which will happen when the texture is destroyed
	virtual void Release() = 0;

	// (erics): This should have a virtual destructor, but would be ABI breaking (non-versioned interface implemented
	//          by the game)
//	virtual ~ITextureRegenerator(){}
};

abstract_class ITexture
{
public:
	// Various texture polling methods
	virtual const char *GetName( void ) const = 0;
	virtual int GetMappingWidth() const = 0;
	virtual int GetMappingHeight() const = 0;
	virtual int GetActualWidth() const = 0;
	virtual int GetActualHeight() const = 0;
	virtual int GetNumAnimationFrames() const = 0;
	virtual bool IsTranslucent() const = 0;
	virtual void unused_IsMipmapped() = 0;

	virtual void unused_GetLowResColorSample() = 0;

	// Gets texture resource data of the specified type.
	// Params:
	//		eDataType		type of resource to retrieve.
	//		pnumBytes		on return is the number of bytes available in the read-only data buffer or is undefined
	// Returns:
	//		pointer to the resource data, or NULL
	virtual void *GetResourceData( uint32 eDataType, size_t *pNumBytes ) const = 0;

	// Methods associated with reference count
	virtual void IncrementReferenceCount( void ) = 0;
	virtual void DecrementReferenceCount( void ) = 0;

	inline void AddRef() { IncrementReferenceCount(); }
	inline void Release() { DecrementReferenceCount(); }

	// Used to modify the texture bits (procedural textures only)
	virtual void unused_SetTextureRegenerator() = 0;

	// Reconstruct the texture bits in HW memory

	// If rect is not specified, reconstruct all bits, otherwise just
	// reconstruct a subrect.
	virtual void unused_Download() = 0;

	// Uses for stats. . .get the approximate size of the texture in it's current format.
	virtual void unused_GetApproximateVidMemBytes() = 0;

	// Returns true if the texture data couldn't be loaded.
	virtual bool IsError() const = 0;

	// NOTE: Stuff after this is added after shipping HL2.

	// For volume textures
	virtual void unused_IsVolumeTexture() = 0;
	virtual void unused_GetMappingDepth() = 0;
	virtual void unused_GetActualDepth() = 0;

	virtual void unused_GetImageFormat() = 0;
	virtual void unused_GetNormalDecodeMode() = 0;

	// Various information about the texture
	virtual void unused_IsRenderTarget() = 0;
	virtual void unused_IsCubeMap() = 0;
	virtual void unused_IsNormalMap() = 0;
	virtual void unused_IsProcedural() = 0;

	virtual void DeleteIfUnreferenced() = 0;

	// swap everything except the name with another texture
	virtual void unused_SwapContents() = 0;

	// Retrieve the vtf flags mask
	virtual unsigned int GetFlags( void ) const = 0;

	// Force LOD override (automatically downloads the texture)
	virtual void unused_ForceLODOverride() = 0;

	// Save texture to a file.
	virtual void unused_SaveToFile() = 0;

	// Copy this texture, which must be a render target or a renderable texture, to the destination texture, 
	// which must have been created with the STAGING bit.
	virtual void unused_CopyToStagingTexture() = 0;

	// Set that this texture should return true for the call "IsError"
	virtual void unused_SetErrorTexture() = 0;

	// Download textures, except return true if we were successful and false if we were not.
	virtual void unused_BDownload() = 0;
};

inline bool IsErrorTexture( ITexture *pTex )
{
	return !pTex || pTex->IsError();
}
