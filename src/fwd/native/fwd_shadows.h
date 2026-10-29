//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client shadow manager's native table ============//
//
// C7 cluster 12 S2 (docs/plans/c7_shadow_manager.md). The shadow manager is
// managed's (SourceSharp.Client's Render/Shadows); what it asks of the engine
// and tells it goes through ONE function of this library's HAND table
// ShadowNative (tools/engine-forwarders, fwd_client.g.inc lays it out): Run,
// which carries out a buffer of records in order. A record that answers
// nothing is an engine call made; a question writes its answer after the
// answers before it. The managed binding queues the calls that answer nothing
// and sends the queue with the question that needs an answer, so a buffer
// costs one crossing however many calls it carries.
//
// A RECORD is two int32 words, its kind and the count of payload words after
// them, then the payload: an int a word, a float its bits, a pointer two
// words (low, high), a string its byte length and then its bytes with a NUL,
// padded to a word. SS_SHADOW_* below are the kinds, and
// SourceSharp.Client.Interop.ShadowNativeOp is the same list
// (ShadowNativeAbiTests holds the two equal).
//
// IT DECIDES NOTHING. Its `if`s are the bounds of a record, the null guards
// and the switch over the kind.
//=============================================================================//
#pragma once

#include <stdint.h>

class IShadowMgr;
class IStaticPropMgrClient;
class IMaterialSystem;
class IMaterialSystemHardwareConfig;
class IVModelRender;
class IVRenderView;
class IVEngineClient;
class IVModelInfoClient;
class IVDebugOverlay;
class IClientRenderable;
class IMaterial;
class ITexture;
class CViewSetup;
class CGlobalVarsBase;

// What the table reaches in client.so, handed over by address: the library
// has no globals of client.so's and no heap, so the engine's interfaces are
// client.so's pointers to them, and what needs an allocation or a client.so
// object (the material and texture references, the view, the entity list) is
// a function of client.so's. shell_shadows_client.cpp builds it; managed
// asks client.so's own factory for it (SS_SHADOW_ENV_VERSION) and hands it to
// every Run.
struct SSShadowEnv
{
	IShadowMgr* const* ppShadowMgr;
	IStaticPropMgrClient* const* ppStaticPropMgr;
	IMaterialSystem* const* ppMaterials;
	IMaterialSystemHardwareConfig* const* ppHardwareConfig;
	IVModelRender* const* ppModelRender;
	IVRenderView* const* ppRender;
	IVEngineClient* const* ppEngine;
	IVModelInfoClient* const* ppModelInfo;
	IVDebugOverlay* const* ppDebugOverlay;
	CGlobalVarsBase* const* ppGlobals;

	// The references client.so holds for the engine (CMaterialReference,
	// CTextureReference): an id each, 0 for none.
	int32_t (*pfnInitMaterial)(const char* pszName, const char* pszGroup);
	void (*pfnShutdownMaterial)(int32_t nMaterial);
	IMaterial* (*pfnMaterial)(int32_t nMaterial);
	int32_t (*pfnInitRenderTarget)(const char* pszName, int32_t nWidth, int32_t nHeight, int32_t nSizeMode,
								   int32_t nFormat, int32_t nDepth, int32_t bHdr);
	void (*pfnShutdownTexture)(int32_t nTexture);
	ITexture* (*pfnTexture)(int32_t nTexture);
	// The flashlight's spotlight textures, by the index FlashlightTexture answered.
	ITexture* (*pfnFlashlightTexture)(int32_t nIndex);
	// materials->AddRestoreFunc / RemoveRestoreFunc of client.so's ShadowRestoreFunc.
	void (*pfnAddRestoreFunc)();
	void (*pfnRemoveRestoreFunc)();
	// CommandLine()->CheckParm( "-tools" ).
	int32_t (*pfnToolsMode)();
	// The renderable an entity handle names now (the client entity list).
	IClientRenderable* (*pfnRenderableOf)(uint32_t hEntity);
	// view->UpdateShadowDepthTexture.
	void (*pfnUpdateShadowDepthTexture)(ITexture* pRenderTarget, ITexture* pDepthTexture, const CViewSetup& view);
	// RenderShadowTexture's quad, through a dynamic mesh (CMeshBuilder is client.so's).
	void (*pfnDrawTextureQuad)(IMaterial* pMaterial, float flWidth, float flHeight);
};

// The name client.so's own factory serves the environment under.
#define SS_SHADOW_ENV_VERSION "SourceSharpShadowEnv001"

// The record kinds. 1-63 answer nothing; 64 and up are questions.
enum SSShadowOp_t
{
	// IShadowMgr and the static prop manager.
	SS_SHADOW_DestroyShadow = 1, // shadow
	SS_SHADOW_SetShadowMaterial = 2, // shadow, material, model material, proxy (2)
	SS_SHADOW_ProjectShadow =
		3, // shadow, origin (3), dir (3), matrix (16), size (2), maxHeight, falloffOffset, falloffAmount, caster (3), leaves
	SS_SHADOW_ProjectFlashlight = 4, // shadow, matrix (16), leaves
	SS_SHADOW_AddShadowToBrushModel = 5, // shadow, model (2), origin (3), angles (3)
	SS_SHADOW_RemoveAllShadowsFromBrushModel = 6, // model (2)
	SS_SHADOW_SetShadowTexCoord = 7, // shadow, x, y, w, h
	SS_SHADOW_AddShadowToModel = 8, // shadow, instance
	SS_SHADOW_RemoveAllShadowsFromModel = 9, // instance
	SS_SHADOW_ClearExtraClipPlanes = 10, // shadow
	SS_SHADOW_AddExtraClipPlane = 11, // shadow, normal (3), dist
	SS_SHADOW_EnableShadow = 12, // shadow, enable
	SS_SHADOW_SetFalloffBias = 13, // shadow, bias
	SS_SHADOW_UpdateFlashlightState = 14, // shadow, SourceSharpFlashlightState (25)
	SS_SHADOW_AddFlashlightRenderable = 15, // shadow, renderable (2)
	SS_SHADOW_SetFlashlightDepthTexture = 16, // shadow, texture, stencil bit
	SS_SHADOW_SetFlashlightRenderState = 17, // shadow
	SS_SHADOW_AddShadowToStaticProp = 18, // shadow, renderable (2)
	SS_SHADOW_RemoveAllShadowsFromStaticProp = 19, // renderable (2)

	// The material system.
	SS_SHADOW_ShutdownMaterial = 20, // material
	SS_SHADOW_ColorModulate = 21, // material, r, g, b
	SS_SHADOW_ShutdownTexture = 22, // texture
	SS_SHADOW_BeginRenderTargetAllocation = 23,
	SS_SHADOW_EndRenderTargetAllocation = 24,
	SS_SHADOW_UncacheUnusedMaterials = 25,
	SS_SHADOW_AddRestoreFunc = 26,
	SS_SHADOW_RemoveRestoreFunc = 27,

	// The render context.
	SS_SHADOW_FogMode = 28, // mode
	SS_SHADOW_SetHeightClipMode = 29, // mode
	SS_SHADOW_ClearColor4ub = 30, // r, g, b, a
	SS_SHADOW_ClearColor3ub = 31, // r, g, b
	SS_SHADOW_MatrixMode = 32, // mode
	SS_SHADOW_PushMatrix = 33,
	SS_SHADOW_PopMatrix = 34,
	SS_SHADOW_LoadIdentity = 35,
	SS_SHADOW_LoadMatrix = 36, // matrix (16)
	SS_SHADOW_Scale = 37, // x, y, z
	SS_SHADOW_Ortho = 38, // left, top, right, bottom, near, far: doubles (2 words each)
	SS_SHADOW_PushRenderTargetAndViewport = 39, // texture
	SS_SHADOW_PopRenderTargetAndViewport = 40,
	SS_SHADOW_ClearBuffers = 41, // color, depth, stencil
	SS_SHADOW_Viewport = 42, // x, y, w, h
	SS_SHADOW_SetShadowDepthBiasFactors = 43, // slope scale, bias
	SS_SHADOW_DrawTextureQuad = 44, // material, w, h
	SS_SHADOW_DrawBrushModelShadow = 45, // renderable (2)
	SS_SHADOW_UpdateShadowDepthTexture = 46, // render target, depth texture, ShadowDepthView (19)
	SS_SHADOW_CreateModelInstance = 47, // renderable (2)
	SS_SHADOW_AddLineOverlay = 48, // origin (3), dest (3), r, g, b, noDepthTest, duration
	SS_SHADOW_AddTextOverlay =
		49, // origin (3), duration, string (stock's AddTextOverlay( origin, 0, fmt ) is the duration overload)

	// Questions: each writes its answer words after the answers before it.
	SS_SHADOW_CreateShadowEx = 64, // material, model material, proxy (2), flags -> handle
	SS_SHADOW_GetInfo = 65, // shadow -> ShadowInfo_t (24: 93 bytes, padded)
	SS_SHADOW_GetFlashlightFrustum = 66, // shadow -> Frustum_t (48)
	SS_SHADOW_InitMaterial = 67, // name, group -> material
	SS_SHADOW_InitRenderTarget = 68, // name, w, h, size mode, format, depth, hdr -> texture
	SS_SHADOW_TextureSize = 69, // texture -> actual width, actual height
	SS_SHADOW_Hardware = 70, // -> shadow depth support, depth format, null format, DX level, -tools
	SS_SHADOW_ComputePixelDiameterOfSphere = 71, // center (3), radius -> float
	SS_SHADOW_GetFogMode = 72, // -> mode
	SS_SHADOW_GetHeightClipMode = 73, // -> mode
	SS_SHADOW_DrawModelShadow = 74, // renderable (2), body, skin -> drew bones
	SS_SHADOW_CullBox = 75, // mins (3), maxs (3) -> culled
	SS_SHADOW_AmbientLightColor = 76, // -> r, g, b
	SS_SHADOW_LeavesAlongRay = 77, // start (3), delta (3), extents (3) -> count, leaves
	SS_SHADOW_LeavesInBox = 78, // mins (3), maxs (3) -> count, leaves
	SS_SHADOW_FrameCount = 79, // -> gpGlobals->framecount
	SS_SHADOW_RenderableOf = 80, // entity -> renderable (2)
	SS_SHADOW_RenderOrigin = 81, // renderable (2) -> origin (3)
	SS_SHADOW_RenderAngles = 82, // renderable (2) -> angles (3)
	SS_SHADOW_RenderableToWorld = 83, // renderable (2) -> matrix3x4 (12)
	SS_SHADOW_Model = 84, // renderable (2) -> model (2)
	SS_SHADOW_ModelType = 85, // model (2) -> modtype_t
	SS_SHADOW_BodySkin = 86, // renderable (2) -> body, skin
	SS_SHADOW_RenderBoundsWorldspace = 87, // renderable (2) -> mins (3), maxs (3)
	SS_SHADOW_ShadowRenderBounds = 88, // renderable (2), shadow type -> mins (3), maxs (3)
	SS_SHADOW_ShouldReceiveProjectedTextures = 89, // renderable (2), flags -> 1 or 0
	SS_SHADOW_ShadowCastDistance = 90, // renderable (2), shadow type, distance -> answered, distance
	SS_SHADOW_ShadowCastDirection = 91, // renderable (2), shadow type, direction (3) -> answered, direction (3)
	SS_SHADOW_ShadowParent = 92, // renderable (2) -> renderable (2)
	SS_SHADOW_FirstShadowChild = 93, // renderable (2) -> renderable (2)
	SS_SHADOW_NextShadowPeer = 94, // renderable (2) -> renderable (2)
	SS_SHADOW_ModelInstance = 95, // renderable (2) -> instance
	SS_SHADOW_NetworkableEntIndex = 96, // renderable (2) -> entity index, or -1
};

// Carries out the nWords words of records at pOps in order over the
// environment, the questions' answers written to pAnswers one after another.
// The answer words written; when they did not fit, the negative of the words
// the answers needed (what fit is written, the rest is only counted). 0 with
// no environment or no records. A record whose payload runs past nWords, or
// is shorter than its kind reads, ends the run there.
int32_t SSFwd_Shadows_Run(const SSShadowEnv* pEnv, const int32_t* pOps, int32_t nWords, int32_t* pAnswers,
						  int32_t nAnswerWords);
