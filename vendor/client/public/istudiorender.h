//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "tier1/utlbuffer.h"

#include "materialsystem/imaterial.h"

#include "datacache/imdlcache.h"
#include "studio.h"

// forward declarations
struct studiohdr_t;
struct studiomeshdata_t;
class Vector;
struct LightDesc_t;
class IMaterial;
struct studiohwdata_t;
struct Ray_t;
class Vector4D;
class IMaterialSystem;
struct matrix3x4_t;
class IMesh;
struct vertexFileHeader_t;
struct FlashlightState_t;
class VMatrix;

namespace OptimizedModel
{
;
}
class IPooledVBAllocator;

// Studio render interface
DECLARE_POINTER_HANDLE(StudioDecalHandle_t);

enum
{
	ADDDECAL_TO_ALL_LODS = -1
};

// DrawModel flags
enum
{
	STUDIORENDER_DRAW_ENTIRE_MODEL = 0,
	STUDIORENDER_DRAW_OPAQUE_ONLY = 0x01,
	STUDIORENDER_DRAW_TRANSLUCENT_ONLY = 0x02,
	STUDIORENDER_DRAW_GROUP_MASK = 0x03,

	STUDIORENDER_DRAW_NO_FLEXES = 0x04,
	STUDIORENDER_DRAW_STATIC_LIGHTING = 0x08,

	STUDIORENDER_DRAW_ACCURATETIME = 0x10, // Use accurate timing when drawing the model.
	STUDIORENDER_DRAW_NO_SHADOWS = 0x20,
	STUDIORENDER_DRAW_GET_PERF_STATS = 0x40,

	STUDIORENDER_DRAW_WIREFRAME = 0x80,

	STUDIORENDER_DRAW_ITEM_BLINK = 0x100,

	STUDIORENDER_SHADOWDEPTHTEXTURE = 0x200,

	STUDIORENDER_SSAODEPTHTEXTURE = 0x1000,

	STUDIORENDER_GENERATE_STATS = 0x8000,
};

// Standard model vertex formats
// FIXME: remove these (materials/shaders should drive vertex format). Need to
//        list required forcedmaterialoverrides in models/bsps (rather than
//        all models supporting all possible overrides, as they do currently).

// What kind of material override is it?
enum OverrideType_t
{
	OVERRIDE_NORMAL = 0,
	OVERRIDE_BUILD_SHADOWS,
	OVERRIDE_DEPTH_WRITE,
	OVERRIDE_SSAO_DEPTH_WRITE,
};

// DrawModel info

// Special flag for studio models that have a compiled in shadow lod version
// It's negative 2 since positive numbers == use a regular slot and -1 means
//  have studiorender compute a value instead
enum
{
	USESHADOWLOD = -2,
};

// beyond this number of materials, you won't get info back from DrawModel
#define MAX_DRAW_MODEL_INFO_MATERIALS 8

struct DrawModelResults_t
{
	int m_ActualTriCount;
	int m_TextureMemoryBytes;
	int m_NumHardwareBones;
	int m_NumBatches;
	int m_NumMaterials;
	int m_nLODUsed;
	int m_flLODMetric;
	CFastTimer m_RenderTime;
	CUtlVectorFixed<IMaterial*, MAX_DRAW_MODEL_INFO_MATERIALS> m_Materials;
};

struct ColorTexelsInfo_t
{
	int m_nWidth;
	int m_nHeight;
	int m_nMipmapCount;
	ImageFormat m_ImageFormat;
	int m_nByteCount;
	byte* m_pTexelData;
};

struct ColorMeshInfo_t
{
	// A given color mesh can own a unique Mesh, or it can use a shared Mesh
	// (in which case it uses a sub-range defined by m_nVertOffset and m_nNumVerts)
	IMesh* m_pMesh;
	IPooledVBAllocator* m_pPooledVBAllocator;
	int m_nVertOffsetInBytes;
	int m_nNumVerts;
	ITexture* m_pLightmap;
	ColorTexelsInfo_t* m_pLightmapData;
};

struct DrawModelInfo_t
{
	alignas(8) unsigned char unused_m_pStudioHdr[24];
	int m_Skin;
	int m_Body;
	alignas(8) unsigned char unused_m_HitboxSet[16];
	int m_Lod;
	ColorMeshInfo_t* m_pColorMeshes;
	bool m_bStaticLighting;
	Vector m_vecAmbientCube[6]; // ambient, and lights that aren't in locallight[]
	int m_nLocalLightCount;
	LightDesc_t m_LocalLightDescs[4];
};

// Cache Callback Function
// implementation can either statically persist data (tools) or lru cache (engine) it.
// caller returns base pointer to resident data.
// code expectes data to be dynamic and invokes cache callback prior to iterative access.
// virtualModel is member passed in via studiohdr_t and passed back for model identification.

// Studio render interface

abstract_class IStudioRender : public IAppSystem
{
public:
	virtual void unused_BeginFrame() = 0;
	virtual void unused_EndFrame() = 0;

	// Used for the mat_stub console command.
	virtual void Mat_Stub(IMaterialSystem * pMatSys) = 0;

	// Updates the rendering configuration
	virtual void unused_UpdateConfig() = 0;
	virtual void unused_GetCurrentConfig() = 0;

	// Load, unload model data
	virtual bool LoadModel(studiohdr_t * pStudioHdr, void* pVtxData, studiohwdata_t* pHardwareData) = 0;
	virtual void unused_UnloadModel() = 0;

	// Refresh the studiohdr since it was lost...
	virtual void unused_RefreshStudioHdr() = 0;

	// This is needed to do eyeglint and calculate the correct texcoords for the eyes.
	virtual void unused_SetEyeViewTarget() = 0;

	// Methods related to lighting state
	// NOTE: SetAmbientLightColors assumes that the arraysize is the same as
	// returned from GetNumAmbientLightSamples
	virtual void unused_GetNumAmbientLightSamples() = 0;
	virtual void unused_GetAmbientLightDirections() = 0;
	virtual void SetAmbientLightColors(const Vector4D* pAmbientOnlyColors) = 0;
	virtual void SetAmbientLightColors(const Vector* pAmbientOnlyColors) = 0;
	virtual void unused_SetLocalLights() = 0;

	// Sets information about the camera location + orientation
	virtual void unused_SetViewState() = 0;

	// Allocates flex weights for use in rendering
	// NOTE: Pass in a non-null second parameter to lock delayed flex weights
	virtual void unused_LockFlexWeights() = 0;
	virtual void unused_UnlockFlexWeights() = 0;

	// Used to allocate bone matrices to be used to pass into DrawModel
	virtual void unused_LockBoneMatrices() = 0;
	virtual void unused_UnlockBoneMatrices() = 0;

	// LOD stuff
	virtual void unused_GetNumLODs() = 0;
	virtual void unused_GetLODSwitchValue() = 0;
	virtual void unused_SetLODSwitchValue() = 0;

	// Sets the color/alpha modulation
	virtual void SetColorModulation(float const* pColor) = 0;
	virtual void unused_SetAlphaModulation() = 0;

	// Draws the model
	virtual void DrawModel(DrawModelResults_t * pResults, const DrawModelInfo_t& info, matrix3x4_t* pBoneToWorld,
						   float* pFlexWeights, float* pFlexDelayedWeights, const Vector& modelOrigin,
						   int flags = STUDIORENDER_DRAW_ENTIRE_MODEL) = 0;

	// Methods related to static prop rendering
	virtual void unused_DrawModelStaticProp() = 0;
	virtual void unused_DrawStaticPropDecals() = 0;
	virtual void unused_DrawStaticPropShadows() = 0;

	// Causes a material to be used instead of the materials the model was compiled with
	virtual void ForcedMaterialOverride(IMaterial * newMaterial, OverrideType_t nOverrideType = OVERRIDE_NORMAL) = 0;

	// Create, destroy list of decals for a particular model
	virtual void unused_CreateDecalList() = 0;
	virtual void unused_DestroyDecalList() = 0;

	// Add decals to a decal list by doing a planar projection along the ray
	// The BoneToWorld matrices must be set before this is called
	virtual void AddDecal(StudioDecalHandle_t handle, studiohdr_t * pStudioHdr, matrix3x4_t * pBoneToWorld,
						  const Ray_t& ray, const Vector& decalUp, IMaterial* pDecalMaterial, float radius, int body,
						  bool noPokethru = false, int maxLODToDecal = ADDDECAL_TO_ALL_LODS) = 0;

	// Compute the lighting at a point and normal
	virtual void ComputeLighting(const Vector* pAmbient, int lightCount, LightDesc_t* pLights, const Vector& pt,
								 const Vector& normal, Vector& lighting) = 0;

	// Compute the lighting at a point, constant directional component is passed
	// as flDirectionalAmount
	virtual void unused_ComputeLightingConstDirectional() = 0;

	// Shadow state (affects the models as they are rendered)
	virtual void unused_AddShadow() = 0;
	virtual void unused_ClearAllShadows() = 0;

	// Gets the model LOD; pass in the screen size in pixels of a sphere
	// of radius 1 that has the same origin as the model to get the LOD out...
	virtual void unused_ComputeModelLod() = 0;

	// Return a number that is usable for budgets, etc.
	// Things that we care about:
	// 1) effective triangle count (factors in batch sizes, state changes, etc)
	// 2) texture memory usage
	// Get Triangles returns the LOD used
	virtual void unused_GetPerfStats() = 0;
	virtual void unused_GetTriangles() = 0;

	// Returns materials used by a particular model
	virtual void unused_GetMaterialList() = 0;
	virtual void unused_GetMaterialListFromBodyAndSkin() = 0;
	// draw an array of models with the same state
	virtual void unused_DrawModelArray() = 0;

	virtual void unused_GetMaterialOverride() = 0;
};
