//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "materialsystem/imaterialsystem.h"

// forward declaraions

class IMaterialVar;
class ITexture;
class IMaterialProxy;
class Vector;

// Flags for GetVertexFormat

// Indicates we're using wrinkle

// Indicates we're using bone indices

// Indicates this is a vertex shader

// Indicates this format shouldn't be bloated to cache align it
// (only used for VertexUsage)

// Indicates that compressed vertex elements are to be used (see also VertexCompressionType_t)
#define VERTEX_FORMAT_COMPRESSED 0x400

// Update this if you add or remove bits...

// If everything is off, it's an unknown vertex format

// Macros for construction..

inline VertexCompressionType_t CompressionType(VertexFormat_t vertexFormat)
{
	// This is trivial now, but we may add multiple flavours of compressed vertex later on
	if (vertexFormat & VERTEX_FORMAT_COMPRESSED)
		return VERTEX_COMPRESSION_ON;
	else
		return VERTEX_COMPRESSION_NONE;
}

// VertexElement_t (enumerates all usable vertex elements)
// FIXME: unify this with VertexFormat_t (i.e. construct the lower bits of VertexFormat_t with "1 << (VertexElement_t)element")
enum VertexElement_t
{
	VERTEX_ELEMENT_NONE = -1,

	// Deliberately explicitly numbered so it's a pain to change, so you read this:
	// #!#!#NOTE#!#!# update GetVertexElementSize, VertexElementToDeclType and
	//                CVBAllocTracker (elementTable) when you update this!
	VERTEX_ELEMENT_POSITION = 0,
	VERTEX_ELEMENT_NORMAL = 1,
	VERTEX_ELEMENT_COLOR = 2,
	VERTEX_ELEMENT_SPECULAR = 3,
	VERTEX_ELEMENT_TANGENT_S = 4,
	VERTEX_ELEMENT_TANGENT_T = 5,
	VERTEX_ELEMENT_WRINKLE = 6,
	VERTEX_ELEMENT_BONEINDEX = 7,
	VERTEX_ELEMENT_BONEWEIGHTS1 = 8,
	VERTEX_ELEMENT_BONEWEIGHTS2 = 9,
	VERTEX_ELEMENT_BONEWEIGHTS3 = 10,
	VERTEX_ELEMENT_BONEWEIGHTS4 = 11,
	VERTEX_ELEMENT_USERDATA1 = 12,
	VERTEX_ELEMENT_USERDATA2 = 13,
	VERTEX_ELEMENT_USERDATA3 = 14,
	VERTEX_ELEMENT_USERDATA4 = 15,
	VERTEX_ELEMENT_TEXCOORD1D_0 = 16,
	VERTEX_ELEMENT_TEXCOORD1D_1 = 17,
	VERTEX_ELEMENT_TEXCOORD1D_2 = 18,
	VERTEX_ELEMENT_TEXCOORD1D_3 = 19,
	VERTEX_ELEMENT_TEXCOORD1D_4 = 20,
	VERTEX_ELEMENT_TEXCOORD1D_5 = 21,
	VERTEX_ELEMENT_TEXCOORD1D_6 = 22,
	VERTEX_ELEMENT_TEXCOORD1D_7 = 23,
	VERTEX_ELEMENT_TEXCOORD2D_0 = 24,
	VERTEX_ELEMENT_TEXCOORD2D_1 = 25,
	VERTEX_ELEMENT_TEXCOORD2D_2 = 26,
	VERTEX_ELEMENT_TEXCOORD2D_3 = 27,
	VERTEX_ELEMENT_TEXCOORD2D_4 = 28,
	VERTEX_ELEMENT_TEXCOORD2D_5 = 29,
	VERTEX_ELEMENT_TEXCOORD2D_6 = 30,
	VERTEX_ELEMENT_TEXCOORD2D_7 = 31,
	VERTEX_ELEMENT_TEXCOORD3D_0 = 32,
	VERTEX_ELEMENT_TEXCOORD3D_1 = 33,
	VERTEX_ELEMENT_TEXCOORD3D_2 = 34,
	VERTEX_ELEMENT_TEXCOORD3D_3 = 35,
	VERTEX_ELEMENT_TEXCOORD3D_4 = 36,
	VERTEX_ELEMENT_TEXCOORD3D_5 = 37,
	VERTEX_ELEMENT_TEXCOORD3D_6 = 38,
	VERTEX_ELEMENT_TEXCOORD3D_7 = 39,
	VERTEX_ELEMENT_TEXCOORD4D_0 = 40,
	VERTEX_ELEMENT_TEXCOORD4D_1 = 41,
	VERTEX_ELEMENT_TEXCOORD4D_2 = 42,
	VERTEX_ELEMENT_TEXCOORD4D_3 = 43,
	VERTEX_ELEMENT_TEXCOORD4D_4 = 44,
	VERTEX_ELEMENT_TEXCOORD4D_5 = 45,
	VERTEX_ELEMENT_TEXCOORD4D_6 = 46,
	VERTEX_ELEMENT_TEXCOORD4D_7 = 47,

	VERTEX_ELEMENT_NUMELEMENTS = 48
};

inline void Detect_VertexElement_t_Changes(VertexElement_t element) // GREPs for VertexElement_t will hit this
{
	// Make it harder for someone to change VertexElement_t without noticing that dependent code
	// (GetVertexElementSize, VertexElementToDeclType, CVBAllocTracker) needs updating
	Assert(VERTEX_ELEMENT_NUMELEMENTS == 48);
	switch (element)
	{
		case VERTEX_ELEMENT_POSITION:
			Assert(VERTEX_ELEMENT_POSITION == 0);
			break;
		case VERTEX_ELEMENT_NORMAL:
			Assert(VERTEX_ELEMENT_NORMAL == 1);
			break;
		case VERTEX_ELEMENT_COLOR:
			Assert(VERTEX_ELEMENT_COLOR == 2);
			break;
		case VERTEX_ELEMENT_SPECULAR:
			Assert(VERTEX_ELEMENT_SPECULAR == 3);
			break;
		case VERTEX_ELEMENT_TANGENT_S:
			Assert(VERTEX_ELEMENT_TANGENT_S == 4);
			break;
		case VERTEX_ELEMENT_TANGENT_T:
			Assert(VERTEX_ELEMENT_TANGENT_T == 5);
			break;
		case VERTEX_ELEMENT_WRINKLE:
			Assert(VERTEX_ELEMENT_WRINKLE == 6);
			break;
		case VERTEX_ELEMENT_BONEINDEX:
			Assert(VERTEX_ELEMENT_BONEINDEX == 7);
			break;
		case VERTEX_ELEMENT_BONEWEIGHTS1:
			Assert(VERTEX_ELEMENT_BONEWEIGHTS1 == 8);
			break;
		case VERTEX_ELEMENT_BONEWEIGHTS2:
			Assert(VERTEX_ELEMENT_BONEWEIGHTS2 == 9);
			break;
		case VERTEX_ELEMENT_BONEWEIGHTS3:
			Assert(VERTEX_ELEMENT_BONEWEIGHTS3 == 10);
			break;
		case VERTEX_ELEMENT_BONEWEIGHTS4:
			Assert(VERTEX_ELEMENT_BONEWEIGHTS4 == 11);
			break;
		case VERTEX_ELEMENT_USERDATA1:
			Assert(VERTEX_ELEMENT_USERDATA1 == 12);
			break;
		case VERTEX_ELEMENT_USERDATA2:
			Assert(VERTEX_ELEMENT_USERDATA2 == 13);
			break;
		case VERTEX_ELEMENT_USERDATA3:
			Assert(VERTEX_ELEMENT_USERDATA3 == 14);
			break;
		case VERTEX_ELEMENT_USERDATA4:
			Assert(VERTEX_ELEMENT_USERDATA4 == 15);
			break;
		case VERTEX_ELEMENT_TEXCOORD1D_0:
			Assert(VERTEX_ELEMENT_TEXCOORD1D_0 == 16);
			break;
		case VERTEX_ELEMENT_TEXCOORD1D_1:
			Assert(VERTEX_ELEMENT_TEXCOORD1D_1 == 17);
			break;
		case VERTEX_ELEMENT_TEXCOORD1D_2:
			Assert(VERTEX_ELEMENT_TEXCOORD1D_2 == 18);
			break;
		case VERTEX_ELEMENT_TEXCOORD1D_3:
			Assert(VERTEX_ELEMENT_TEXCOORD1D_3 == 19);
			break;
		case VERTEX_ELEMENT_TEXCOORD1D_4:
			Assert(VERTEX_ELEMENT_TEXCOORD1D_4 == 20);
			break;
		case VERTEX_ELEMENT_TEXCOORD1D_5:
			Assert(VERTEX_ELEMENT_TEXCOORD1D_5 == 21);
			break;
		case VERTEX_ELEMENT_TEXCOORD1D_6:
			Assert(VERTEX_ELEMENT_TEXCOORD1D_6 == 22);
			break;
		case VERTEX_ELEMENT_TEXCOORD1D_7:
			Assert(VERTEX_ELEMENT_TEXCOORD1D_7 == 23);
			break;
		case VERTEX_ELEMENT_TEXCOORD2D_0:
			Assert(VERTEX_ELEMENT_TEXCOORD2D_0 == 24);
			break;
		case VERTEX_ELEMENT_TEXCOORD2D_1:
			Assert(VERTEX_ELEMENT_TEXCOORD2D_1 == 25);
			break;
		case VERTEX_ELEMENT_TEXCOORD2D_2:
			Assert(VERTEX_ELEMENT_TEXCOORD2D_2 == 26);
			break;
		case VERTEX_ELEMENT_TEXCOORD2D_3:
			Assert(VERTEX_ELEMENT_TEXCOORD2D_3 == 27);
			break;
		case VERTEX_ELEMENT_TEXCOORD2D_4:
			Assert(VERTEX_ELEMENT_TEXCOORD2D_4 == 28);
			break;
		case VERTEX_ELEMENT_TEXCOORD2D_5:
			Assert(VERTEX_ELEMENT_TEXCOORD2D_5 == 29);
			break;
		case VERTEX_ELEMENT_TEXCOORD2D_6:
			Assert(VERTEX_ELEMENT_TEXCOORD2D_6 == 30);
			break;
		case VERTEX_ELEMENT_TEXCOORD2D_7:
			Assert(VERTEX_ELEMENT_TEXCOORD2D_7 == 31);
			break;
		case VERTEX_ELEMENT_TEXCOORD3D_0:
			Assert(VERTEX_ELEMENT_TEXCOORD3D_0 == 32);
			break;
		case VERTEX_ELEMENT_TEXCOORD3D_1:
			Assert(VERTEX_ELEMENT_TEXCOORD3D_1 == 33);
			break;
		case VERTEX_ELEMENT_TEXCOORD3D_2:
			Assert(VERTEX_ELEMENT_TEXCOORD3D_2 == 34);
			break;
		case VERTEX_ELEMENT_TEXCOORD3D_3:
			Assert(VERTEX_ELEMENT_TEXCOORD3D_3 == 35);
			break;
		case VERTEX_ELEMENT_TEXCOORD3D_4:
			Assert(VERTEX_ELEMENT_TEXCOORD3D_4 == 36);
			break;
		case VERTEX_ELEMENT_TEXCOORD3D_5:
			Assert(VERTEX_ELEMENT_TEXCOORD3D_5 == 37);
			break;
		case VERTEX_ELEMENT_TEXCOORD3D_6:
			Assert(VERTEX_ELEMENT_TEXCOORD3D_6 == 38);
			break;
		case VERTEX_ELEMENT_TEXCOORD3D_7:
			Assert(VERTEX_ELEMENT_TEXCOORD3D_7 == 39);
			break;
		case VERTEX_ELEMENT_TEXCOORD4D_0:
			Assert(VERTEX_ELEMENT_TEXCOORD4D_0 == 40);
			break;
		case VERTEX_ELEMENT_TEXCOORD4D_1:
			Assert(VERTEX_ELEMENT_TEXCOORD4D_1 == 41);
			break;
		case VERTEX_ELEMENT_TEXCOORD4D_2:
			Assert(VERTEX_ELEMENT_TEXCOORD4D_2 == 42);
			break;
		case VERTEX_ELEMENT_TEXCOORD4D_3:
			Assert(VERTEX_ELEMENT_TEXCOORD4D_3 == 43);
			break;
		case VERTEX_ELEMENT_TEXCOORD4D_4:
			Assert(VERTEX_ELEMENT_TEXCOORD4D_4 == 44);
			break;
		case VERTEX_ELEMENT_TEXCOORD4D_5:
			Assert(VERTEX_ELEMENT_TEXCOORD4D_5 == 45);
			break;
		case VERTEX_ELEMENT_TEXCOORD4D_6:
			Assert(VERTEX_ELEMENT_TEXCOORD4D_6 == 46);
			break;
		case VERTEX_ELEMENT_TEXCOORD4D_7:
			Assert(VERTEX_ELEMENT_TEXCOORD4D_7 == 47);
			break;
		default:
			Assert(0); // Invalid input or VertexElement_t has definitely changed
			break;
	}
}

// We're testing 2 normal compression methods
// One compressed normals+tangents into a SHORT2 each (8 bytes total)
// The other compresses them together, into a single UBYTE4 (4 bytes total)
// FIXME: pick one or the other, compare lighting quality in important cases
#define COMPRESSED_NORMALS_SEPARATETANGENTS_SHORT2 0
#define COMPRESSED_NORMALS_COMBINEDTANGENTS_UBYTE4 1
//#define COMPRESSED_NORMALS_TYPE						COMPRESSED_NORMALS_SEPARATETANGENTS_SHORT2
#define COMPRESSED_NORMALS_TYPE COMPRESSED_NORMALS_COMBINEDTANGENTS_UBYTE4

inline int GetVertexElementSize(VertexElement_t element, VertexCompressionType_t compressionType)
{
	Detect_VertexElement_t_Changes(element);

	if (compressionType == VERTEX_COMPRESSION_ON)
	{
		// Compressed-vertex element sizes
		switch (element)
		{
#if COMPRESSED_NORMALS_TYPE == COMPRESSED_NORMALS_SEPARATETANGENTS_SHORT2
			case VERTEX_ELEMENT_NORMAL:
				return (2 * sizeof(short));
			case VERTEX_ELEMENT_USERDATA4:
				return (2 * sizeof(short));
#else //( COMPRESSED_NORMALS_TYPE == COMPRESSED_NORMALS_COMBINEDTANGENTS_UBYTE4 )
				// Normals and tangents (userdata4) are combined into a single UBYTE4 vertex element
			case VERTEX_ELEMENT_NORMAL:
				return (4 * sizeof(unsigned char));
			case VERTEX_ELEMENT_USERDATA4:
				return (0);
#endif
			// Compressed bone weights use a SHORT2 vertex element:
			case VERTEX_ELEMENT_BONEWEIGHTS1:
			case VERTEX_ELEMENT_BONEWEIGHTS2:
				return (2 * sizeof(short));
			default:
				break;
		}
	}

	// Uncompressed-vertex element sizes
	switch (element)
	{
		case VERTEX_ELEMENT_POSITION:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_NORMAL:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_COLOR:
			return (4 * sizeof(unsigned char));
		case VERTEX_ELEMENT_SPECULAR:
			return (4 * sizeof(unsigned char));
		case VERTEX_ELEMENT_TANGENT_S:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_TANGENT_T:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_WRINKLE:
			return (1 * sizeof(float)); // Packed into Position.W
		case VERTEX_ELEMENT_BONEINDEX:
			return (4 * sizeof(unsigned char));
		case VERTEX_ELEMENT_BONEWEIGHTS1:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_BONEWEIGHTS2:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_BONEWEIGHTS3:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_BONEWEIGHTS4:
			return (4 * sizeof(float));
		case VERTEX_ELEMENT_USERDATA1:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_USERDATA2:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_USERDATA3:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_USERDATA4:
			return (4 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD1D_0:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD1D_1:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD1D_2:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD1D_3:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD1D_4:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD1D_5:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD1D_6:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD1D_7:
			return (1 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD2D_0:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD2D_1:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD2D_2:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD2D_3:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD2D_4:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD2D_5:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD2D_6:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD2D_7:
			return (2 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD3D_0:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD3D_1:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD3D_2:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD3D_3:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD3D_4:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD3D_5:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD3D_6:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD3D_7:
			return (3 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD4D_0:
			return (4 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD4D_1:
			return (4 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD4D_2:
			return (4 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD4D_3:
			return (4 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD4D_4:
			return (4 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD4D_5:
			return (4 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD4D_6:
			return (4 * sizeof(float));
		case VERTEX_ELEMENT_TEXCOORD4D_7:
			return (4 * sizeof(float));
		default:
			Assert(0);
			return 0;
	};
}

// Shader state flags can be read from the FLAGS materialvar
// Also can be read or written to with the Set/GetMaterialVarFlags() call
// Also make sure you add/remove a string associated with each flag below to CShaderSystem::ShaderStateString in ShaderSystem.cpp
enum MaterialVarFlags_t
{
	MATERIAL_VAR_DEBUG = (1 << 0),
	MATERIAL_VAR_NO_DEBUG_OVERRIDE = (1 << 1),
	MATERIAL_VAR_NO_DRAW = (1 << 2),
	MATERIAL_VAR_USE_IN_FILLRATE_MODE = (1 << 3),

	MATERIAL_VAR_VERTEXCOLOR = (1 << 4),
	MATERIAL_VAR_VERTEXALPHA = (1 << 5),
	MATERIAL_VAR_SELFILLUM = (1 << 6),
	MATERIAL_VAR_ADDITIVE = (1 << 7),
	MATERIAL_VAR_ALPHATEST = (1 << 8),
	MATERIAL_VAR_MULTIPASS = (1 << 9),
	MATERIAL_VAR_ZNEARER = (1 << 10),
	MATERIAL_VAR_MODEL = (1 << 11),
	MATERIAL_VAR_FLAT = (1 << 12),
	MATERIAL_VAR_NOCULL = (1 << 13),
	MATERIAL_VAR_NOFOG = (1 << 14),
	MATERIAL_VAR_IGNOREZ = (1 << 15),
	MATERIAL_VAR_DECAL = (1 << 16),
	MATERIAL_VAR_ENVMAPSPHERE = (1 << 17),
	MATERIAL_VAR_NOALPHAMOD = (1 << 18),
	MATERIAL_VAR_ENVMAPCAMERASPACE = (1 << 19),
	MATERIAL_VAR_BASEALPHAENVMAPMASK = (1 << 20),
	MATERIAL_VAR_TRANSLUCENT = (1 << 21),
	MATERIAL_VAR_NORMALMAPALPHAENVMAPMASK = (1 << 22),
	MATERIAL_VAR_NEEDS_SOFTWARE_SKINNING = (1 << 23),
	MATERIAL_VAR_OPAQUETEXTURE = (1 << 24),
	MATERIAL_VAR_ENVMAPMODE = (1 << 25),
	MATERIAL_VAR_SUPPRESS_DECALS = (1 << 26),
	MATERIAL_VAR_HALFLAMBERT = (1 << 27),
	MATERIAL_VAR_WIREFRAME = (1 << 28),
	MATERIAL_VAR_ALLOWALPHATOCOVERAGE = (1 << 29),
	MATERIAL_VAR_IGNORE_ALPHA_MODULATION = (1 << 30),

	// NOTE: Only add flags here that either should be read from
	// .vmts or can be set directly from client code. Other, internal
	// flags should to into the flag enum in imaterialinternal.h
};

// Preview image return values
enum PreviewImageRetVal_t
{
	MATERIAL_PREVIEW_IMAGE_BAD = 0,
	MATERIAL_PREVIEW_IMAGE_OK,
	MATERIAL_NO_PREVIEW_IMAGE,
};

// material interface
abstract_class IMaterial
{
public:
	// Get the name of the material.  This is a full path to
	// the vmt file starting from "hl2/materials" (or equivalent) without
	// a file extension.
	virtual const char* GetName() const = 0;
	virtual void unused_GetTextureGroupName() = 0;

	// Get the preferred size/bitDepth of a preview image of a material.
	// This is the sort of image that you would use for a thumbnail view
	// of a material, or in WorldCraft until it uses materials to render.
	// separate this for the tools maybe
	virtual void unused_GetPreviewImageProperties() = 0;

	// Get a preview image at the specified width/height and bitDepth.
	// Will do resampling if necessary.(not yet!!! :) )
	// Will do color format conversion. (works now.)
	virtual void unused_GetPreviewImage() = 0;
	virtual int GetMappingWidth() = 0;
	virtual int GetMappingHeight() = 0;

	virtual int GetNumAnimationFrames() = 0;

	// For material subrects (material pages).  Offset(u,v) and scale(u,v) are normalized to texture.
	virtual bool InMaterialPage() = 0;
	virtual void GetMaterialOffset(float* pOffset) = 0;
	virtual void GetMaterialScale(float* pScale) = 0;
	virtual IMaterial* GetMaterialPage() = 0;

	// find a vmt variable.
	// This is how game code affects how a material is rendered.
	// The game code must know about the params that are used by
	// the shader for the material that it is trying to affect.
	virtual IMaterialVar* FindVar(const char* varName, bool* found, bool complain = true) = 0;

	// The user never allocates or deallocates materials.  Reference counting is
	// used instead.  Garbage collection is done upon a call to
	// IMaterialSystem::UncacheUnusedMaterials.
	virtual void IncrementReferenceCount() = 0;
	virtual void DecrementReferenceCount() = 0;

	inline void AddRef()
	{
		IncrementReferenceCount();
	}
	inline void Release()
	{
		DecrementReferenceCount();
	}

	// Each material is assigned a number that groups it with like materials
	// for sorting in the application.
	virtual void unused_GetEnumerationID() = 0;

	virtual void unused_GetLowResColorSample() = 0;

	// This computes the state snapshots for this material
	virtual void unused_RecomputeStateSnapshots() = 0;

	// Are we translucent?
	virtual bool IsTranslucent() = 0;

	// Are we alphatested?
	virtual void unused_IsAlphaTested() = 0;

	// Are we vertex lit?
	virtual void unused_IsVertexLit() = 0;

	// Gets the vertex format
	virtual void unused_GetVertexFormat() = 0;

	// returns true if this material uses a material proxy
	virtual void unused_HasProxy() = 0;

	virtual void unused_UsesEnvCubemap() = 0;

	virtual void unused_NeedsTangentSpace() = 0;

	virtual bool NeedsPowerOfTwoFrameBufferTexture(bool bCheckSpecificToThisFrame = true) = 0;
	virtual bool NeedsFullFrameBufferTexture(bool bCheckSpecificToThisFrame = true) = 0;

	// returns true if the shader doesn't do skinning itself and requires
	// the data that is sent to it to be preskinned.
	virtual void unused_NeedsSoftwareSkinning() = 0;

	// Apply constant color or alpha modulation
	virtual void AlphaModulate(float alpha) = 0;
	virtual void ColorModulate(float r, float g, float b) = 0;

	// Material Var flags...
	virtual void SetMaterialVarFlag(MaterialVarFlags_t flag, bool on) = 0;
	virtual void unused_GetMaterialVarFlag() = 0;

	// Gets material reflectivity
	virtual void unused_GetReflectivity() = 0;

	// Gets material property flags
	virtual void unused_GetPropertyFlag() = 0;

	// Is the material visible from both sides?
	virtual void unused_IsTwoSided() = 0;

	// Sets the shader associated with the material
	virtual void unused_SetShader() = 0;

	// Can't be const because the material might have to precache itself.
	virtual void unused_GetNumPasses() = 0;

	// Can't be const because the material might have to precache itself.
	virtual void unused_GetTextureMemoryBytes() = 0;

	// Meant to be used with materials created using CreateMaterial
	// It updates the materials to reflect the current values stored in the material vars
	virtual void Refresh() = 0;

	// GR - returns true is material uses lightmap alpha for blending
	virtual void unused_NeedsLightmapBlendAlpha() = 0;

	// returns true if the shader doesn't do lighting itself and requires
	// the data that is sent to it to be prelighted
	virtual void unused_NeedsSoftwareLighting() = 0;

	// Gets at the shader parameters
	virtual int ShaderParamCount() const = 0;
	virtual IMaterialVar** GetShaderParams() = 0;

	// Returns true if this is the error material you get back from IMaterialSystem::FindMaterial if
	// the material can't be found.
	virtual bool IsErrorMaterial() const = 0;

	virtual void unused_SetUseFixedFunctionBakedLighting() = 0;

	// Gets the current alpha modulation
	virtual void unused_GetAlphaModulation() = 0;
	virtual void GetColorModulation(float* r, float* g, float* b) = 0;

	// Gets the morph format
	virtual void unused_GetMorphFormat() = 0;

	// fast find that stores the index of the found var in the string table in local cache
	virtual IMaterialVar* FindVarFast(char const* pVarName, unsigned int* pToken) = 0;

	// Sets new VMT shader parameters for the material
	virtual void unused_SetShaderAndParams() = 0;
	virtual void unused_GetShaderName() = 0;

	virtual void DeleteIfUnreferenced() = 0;

	virtual void unused_IsSpriteCard() = 0;

	virtual void unused_CallBindProxy() = 0;

	virtual void unused_CheckProxyReplacement() = 0;

	virtual void RefreshPreservingMaterialVars() = 0;

	virtual void unused_WasReloadedFromWhitelist() = 0;

	virtual void unused_IsPrecached() = 0;
};

inline bool IsErrorMaterial(IMaterial* pMat)
{
	return !pMat || pMat->IsErrorMaterial();
}
