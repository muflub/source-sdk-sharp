//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Purpose: 
//
// $Header: $
// $NoKeywords: $
//===========================================================================//

#ifndef IMATERIALSYSTEMHARDWARECONFIG_H
#define IMATERIALSYSTEMHARDWARECONFIG_H

#ifdef _WIN32
#pragma once
#endif

#include "tier0/platform.h"
#include "tier1/interface.h"
#include "imaterialsystem.h"


//-----------------------------------------------------------------------------
// Material system interface version
//-----------------------------------------------------------------------------
#define MATERIALSYSTEM_HARDWARECONFIG_INTERFACE_VERSION		"MaterialSystemHardwareConfig012"

// For now, vertex compression is simply "on or off" (for the sake of simplicity
// and MeshBuilder perf.), but later we may support multiple flavours.
enum VertexCompressionType_t
{
	// This indicates an uninitialized VertexCompressionType_t value
	VERTEX_COMPRESSION_INVALID = 0xFFFFFFFF,

	// 'VERTEX_COMPRESSION_NONE' means that no elements of a vertex are compressed
	VERTEX_COMPRESSION_NONE = 0,

	// Currently (more stuff may be added as needed), 'VERTEX_COMPRESSION_ON' means:
	//  - if a vertex contains VERTEX_ELEMENT_NORMAL, this is compressed
	//    (see CVertexBuilder::CompressedNormal3f)
	//  - if a vertex contains VERTEX_ELEMENT_USERDATA4 (and a normal - together defining a tangent
	//    frame, with the binormal reconstructed in the vertex shader), this is compressed
	//    (see CVertexBuilder::CompressedUserData)
	//  - if a vertex contains VERTEX_ELEMENT_BONEWEIGHTSx, this is compressed
	//    (see CVertexBuilder::CompressedBoneWeight3fv)
	VERTEX_COMPRESSION_ON = 1
};


// use DEFCONFIGMETHOD to define time-critical methods that we want to make just return constants
// on the 360, so that the checks will happen at compile time. Not all methods are defined this way
// - just the ones that I perceive as being called often in the frame interval.
#ifdef _X360
#define DEFCONFIGMETHOD( ret_type, method, xbox_return_value )		\
FORCEINLINE ret_type method const 									\
{																	\
	return xbox_return_value;										\
}


#else
#define DEFCONFIGMETHOD( ret_type, method, xbox_return_value )	\
virtual ret_type method const = 0;
#endif



//-----------------------------------------------------------------------------
// Material system configuration
//-----------------------------------------------------------------------------
class IMaterialSystemHardwareConfig
{
public:
	// on xbox, some methods are inlined to return constants

	virtual void unused_HasDestAlphaBuffer() = 0;
	virtual void unused_HasStencilBuffer() = 0;
	virtual void unused_GetFrameBufferColorDepth() = 0;
	virtual void unused_GetSamplerCount() = 0;
	virtual void unused_HasSetDeviceGammaRamp() = 0;
	virtual void unused_SupportsCompressedTextures() = 0;
	virtual void unused_SupportsCompressedVertices() = 0;
	virtual void unused_SupportsNormalMapCompression() = 0;
	virtual void unused_SupportsVertexAndPixelShaders() = 0;
	virtual void unused_SupportsPixelShaders_1_4() = 0;
	virtual void unused_SupportsStaticControlFlow() = 0;
	DEFCONFIGMETHOD( bool, SupportsPixelShaders_2_0(), true );
	virtual void unused_SupportsVertexShaders_2_0() = 0;
	virtual void unused_MaximumAnisotropicLevel() = 0;	// 0 means no anisotropic filtering
	virtual void unused_MaxTextureWidth() = 0;
	virtual void unused_MaxTextureHeight() = 0;
	virtual void unused_TextureMemorySize() = 0;
	virtual void unused_SupportsOverbright() = 0;
	virtual void unused_SupportsCubeMaps() = 0;
	virtual void unused_SupportsMipmappedCubemaps() = 0;
	virtual void unused_SupportsNonPow2Textures() = 0;

	// The number of texture stages represents the number of computations
	// we can do in the fixed-function pipeline, it is *not* related to the
	// simultaneous number of textures we can use
	virtual void unused_GetTextureStageCount() = 0;
	virtual void unused_NumVertexShaderConstants() = 0;
	virtual void unused_NumPixelShaderConstants() = 0;
	virtual void unused_MaxNumLights() = 0;
	virtual void unused_SupportsHardwareLighting() = 0;
	virtual void unused_MaxBlendMatrices() = 0;
	virtual void unused_MaxBlendMatrixIndices() = 0;
	virtual void unused_MaxTextureAspectRatio() = 0;
	virtual void unused_MaxVertexShaderBlendMatrices() = 0;
	virtual void unused_MaxUserClipPlanes() = 0;
	virtual bool UseFastClipping() const = 0;

	// This here should be the major item looked at when checking for compat
	// from anywhere other than the material system	shaders
	DEFCONFIGMETHOD( int, GetDXSupportLevel(), 98 );
	virtual void unused_GetShaderDLLName() = 0;

	virtual void unused_ReadPixelsFromFrontBuffer() = 0;

	// Are dx dynamic textures preferred?
	virtual void unused_PreferDynamicTextures() = 0;

	virtual void unused_SupportsHDR() = 0;

	virtual void unused_HasProjectedBumpEnv() = 0;
	virtual void unused_SupportsSpheremapping() = 0;
	virtual void unused_NeedsAAClamp() = 0;
	virtual void unused_NeedsATICentroidHack() = 0;

	virtual void unused_SupportsColorOnSecondStream() = 0;
	virtual void unused_SupportsStaticPlusDynamicLighting() = 0;

	// Does our card have a hard time with fillrate 
	// relative to other cards w/ the same dx level?
	virtual void unused_PreferReducedFillrate() = 0;

	// This is the max dx support level supported by the card
	virtual void unused_GetMaxDXSupportLevel() = 0;

	// Does the card specify fog color in linear space when sRGBWrites are enabled?
	virtual void unused_SpecifiesFogColorInLinearSpace() = 0;

	// Does the card support sRGB reads/writes?
	virtual void unused_SupportsSRGB() = 0;
	DEFCONFIGMETHOD( bool, FakeSRGBWrite(), false );
	DEFCONFIGMETHOD( bool, CanDoSRGBReadFromRTs(), true );

	virtual void unused_SupportsGLMixedSizeTargets() = 0;

	virtual bool IsAAEnabled() const = 0;	// Is antialiasing being used?

	// NOTE: Anything after this was added after shipping HL2.
	virtual void unused_GetVertexTextureCount() = 0;
	virtual void unused_GetMaxVertexTextureDimension() = 0;

	virtual void unused_MaxTextureDepth() = 0;

	virtual HDRType_t GetHDRType() const = 0;
	virtual void unused_GetHardwareHDRType() = 0;

	DEFCONFIGMETHOD( bool, SupportsPixelShaders_2_b(), true );
	virtual void unused_SupportsStreamOffset() = 0;

	virtual void unused_StencilBufferBits() = 0;
	virtual void unused_MaxViewports() = 0;

	virtual void unused_OverrideStreamOffsetSupport() = 0;

	virtual void unused_GetShadowFilterMode() = 0;

	virtual void unused_NeedsShaderSRGBConversion() = 0;

	virtual void unused_UsesSRGBCorrectBlending() = 0;

	virtual void unused_SupportsShaderModel_3_0() = 0;
	virtual void unused_HasFastVertexTextures() = 0;
	virtual void unused_MaxHWMorphBatchCount() = 0;

	// Does the board actually support this?
	virtual void unused_ActuallySupportsPixelShaders_2_b() = 0;

	virtual void unused_SupportsHDRMode() = 0;

	virtual void unused_GetHDREnabled() = 0;
	virtual void unused_SetHDREnabled() = 0;

	virtual bool SupportsBorderColor( void ) const = 0;
	virtual void unused_SupportsFetch4() = 0;
	virtual void unused_CanStretchRectFromTextures() = 0;

};

#endif // IMATERIALSYSTEMHARDWARECONFIG_H
