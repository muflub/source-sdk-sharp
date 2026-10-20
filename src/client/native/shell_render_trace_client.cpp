//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the render call recorder ============================//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve.md C7 step 1 (§5.2's "recorded stock call sequence"):
// the render plan's oracle and its live A/B instrument. Everything the client
// asks the engine to draw goes through four engine-owned objects -- `render`
// (IVRenderView), the material system's render context (IMatRenderContext),
// `modelrender` (IVModelRender) and `staticpropmgr` (IStaticPropMgrClient) --
// plus four render members of `engine` (IVEngineClient). Armed for one frame,
// this records each call client.so makes on them, in order, as one text line:
// the method and the arguments that say what was asked (a view's whole
// CViewSetup, a render target's name, a clip plane, a draw's entity index and
// model). Stock's viewrender and the plan's executor both draw through the same
// four pointers, so the two paths are recorded by the same instrument.
//
// HOW. Each hooked method's slot in its object's vtable is overwritten in
// place for the frame (the page's protection is lifted for the write and put
// back) with a hook that logs and forwards to the original. The vtables are the
// ENGINE'S, so the engine's own calls on those objects reach the hooks too;
// a hook records only when the call came from client.so (its return address is
// in this module), so what is recorded is the client's side of the contract
// and nothing the engine does with it.
//
// WHAT IS NOT A LINE. Queries whose answer only feeds a decision
// (GetMatrix, GetViewport,
// GetFogColor, GetRenderTarget ...) are not hooked: the plan path asks its
// inputs up front and stock asks them mid-frame, and neither is a thing drawn.
// Three queries are recorded as `q` lines because they say which branch stock
// took (GetVisibleFogVolume's answer, IsSkyboxVisibleFromPoint's, and
// LeafContainsTranslucentSurfaces', which the view-draw facts read back); a comparison
// of two sequences skips `q` lines. Pointer identities (a world list, a
// texture) print as names or as the order they first appeared in, so two
// frames' traces compare as text.
//
//=============================================================================//

#include "cbase.h"
#include "shell_render_trace_client.h"
#include "vtable_slot_patch.h"

#include "ivrenderview.h"
#include "engine/ivmodelrender.h"
#include "engine/ivmodelinfo.h"
#include "engine/IStaticPropMgr.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/itexture.h"
#include "view_shared.h"
#include "iclientshadowmgr.h"

#include "ss_platform.h"
#include <stdarg.h>
#include <stdio.h>
#include <string.h>

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

namespace
{

//-----------------------------------------------------------------------------
// The trace
//-----------------------------------------------------------------------------
bool              g_bActive = false;
CUtlVector<char>  g_Text;
CUtlVector<const void *> g_WorldLists;	// a world list's ordinal is its index here
const void       *g_pClientBase = NULL;

// Inside the shadow manager's own frame work (its render-to-texture refresh,
// which redraws only the shadows that went dirty, so it differs frame to
// frame), lines are marked `~`: the call into the shadow manager is the plan's,
// what it does inside is the shadow manager's.
int g_nShadowDepth = 0;

void Append( const char *pszFormat, va_list args )
{
	if ( g_nShadowDepth > 0 )
		g_Text.AddMultipleToTail( 2, "~\t" );
	char szLine[ 1024 ];
	int nLength = V_vsnprintf( szLine, sizeof( szLine ), pszFormat, args );
	if ( nLength < 0 )
		return;
	if ( nLength >= (int)sizeof( szLine ) )
		nLength = sizeof( szLine ) - 1;
	g_Text.AddMultipleToTail( nLength, szLine );
	g_Text.AddToTail( '\n' );
}

void Line( const char *pszFormat, ... )
{
	va_list args;
	va_start( args, pszFormat );
	Append( pszFormat, args );
	va_end( args );
}

// True when the call came from client.so. dladdr names the module that holds
// the return address; the engine's own calls on the hooked objects come from
// engine.so or materialsystem.so and are not the client's.
bool FromClient( const void *pReturn )
{
	if ( !g_bActive )
		return false;
	SSModuleInfo info;
	return SS_ModuleOfAddress( pReturn, &info ) && info.pBase == g_pClientBase;
}

#define SS_FROM_CLIENT() FromClient( SS_RETURN_ADDRESS() )

const char *TextureName( ITexture *pTexture )
{
	return pTexture ? pTexture->GetName() : "-";
}

const char *MaterialName( IMaterial *pMaterial )
{
	return pMaterial ? pMaterial->GetName() : "-";
}

int WorldList( const void *pList )
{
	if ( !pList )
		return -1;
	int i = g_WorldLists.Find( pList );
	if ( i < 0 )
		i = g_WorldLists.AddToTail( pList );
	return i;
}

// A CViewSetup as one field run. %.9g prints a float back to the same float.
void ViewText( const CViewSetup &v, char *pOut, int nOut )
{
	V_snprintf( pOut, nOut,
		"xywh=%d,%d,%d,%d unscaled=%d,%d,%d,%d eye=%d ortho=%d:%.9g,%.9g,%.9g,%.9g "
		"fov=%.9g fovvm=%.9g origin=%.9g,%.9g,%.9g angles=%.9g,%.9g,%.9g "
		"z=%.9g,%.9g zvm=%.9g,%.9g subrect=%d aspect=%.9g offcenter=%d:%.9g,%.9g,%.9g,%.9g "
		"bloom=%d cachefull=%d projoverride=%d",
		v.x, v.y, v.width, v.height, v.m_nUnscaledX, v.m_nUnscaledY, v.m_nUnscaledWidth, v.m_nUnscaledHeight,
		(int)v.m_eStereoEye, v.m_bOrtho ? 1 : 0, v.m_OrthoLeft, v.m_OrthoTop, v.m_OrthoRight, v.m_OrthoBottom,
		v.fov, v.fovViewmodel, v.origin.x, v.origin.y, v.origin.z, v.angles.x, v.angles.y, v.angles.z,
		v.zNear, v.zFar, v.zNearViewmodel, v.zFarViewmodel, v.m_bRenderToSubrectOfLargerScreen ? 1 : 0,
		v.m_flAspectRatio, v.m_bOffCenter ? 1 : 0, v.m_flOffCenterTop, v.m_flOffCenterBottom,
		v.m_flOffCenterLeft, v.m_flOffCenterRight, v.m_bDoBloomAndToneMapping ? 1 : 0,
		v.m_bCacheFullSceneState ? 1 : 0, v.m_bViewToProjectionOverride ? 1 : 0 );
}

//-----------------------------------------------------------------------------
// Slot patching (vtable_slot_patch.h): every slot Begin overwrote, in order.
//-----------------------------------------------------------------------------
CUtlVector<VTableSlotPatch::Patch> g_Patches;

template <typename PMF>
bool Install( void *pObject, PMF pmf, void *pHook, void **ppOriginal )
{
	VTableSlotPatch::Patch patch;
	if ( !VTableSlotPatch::Install( pObject, pmf, pHook, &patch ) )
		return false;
	*ppOriginal = patch.pOriginal;
	g_Patches.AddToTail( patch );
	return true;
}

void RemoveAll()
{
	for ( int i = g_Patches.Count() - 1; i >= 0; --i )
		VTableSlotPatch::Remove( g_Patches[i] );
	g_Patches.RemoveAll();
}

//-----------------------------------------------------------------------------
// The hooks. Each logs when the call is the client's and forwards. The
// original pointer is the slot's value when Begin ran.
//-----------------------------------------------------------------------------
enum HookIndex_t
{
	kHook_DrawBrushModel,
	kHook_DrawIdentityBrushModel,
	kHook_Draw3DDebugOverlays,
	kHook_SetBlend,
	kHook_SetColorModulation,
	kHook_SceneBegin,
	kHook_SceneEnd,
	kHook_GetVisibleFogVolume,
	kHook_CreateWorldList,
	kHook_BuildWorldLists,
	kHook_DrawWorldLists,
	kHook_DrawLights,
	kHook_DrawMaskEntities,
	kHook_DrawTranslucentSurfaces,
	kHook_LeafContainsTranslucentSurfaces,
	kHook_ViewSetupVis,
	kHook_ViewSetupVisEx,
	kHook_VguiPaint,
	kHook_ViewDrawFade,
	kHook_SetFogVolumeState,
	kHook_VGui_Paint,
	kHook_Push3DView,
	kHook_Push3DViewDepth,
	kHook_Push2DView,
	kHook_PopView,
	kHook_SetMainView,
	kHook_OverrideViewFrustum,
	kHook_UpdateBrushModelLightmap,
	kHook_BeginUpdateLightmaps,
	kHook_EndUpdateLightmaps,
	kHook_DrawBrushModelEx,
	kHook_DrawBrushModelShadowDepth,
	kHook_Flush,
	kHook_SetRenderTarget,
	kHook_DepthRange,
	kHook_ClearBuffers,
	kHook_MatrixMode,
	kHook_PushMatrix,
	kHook_PopMatrix,
	kHook_Viewport,
	kHook_SetHeightClipMode,
	kHook_SetHeightClipZ,
	kHook_FogMode,
	kHook_FogStart,
	kHook_FogEnd,
	kHook_SetFogZ,
	kHook_FogColor3fv,
	kHook_ClearColor3ub,
	kHook_ClearColor4ub,
	kHook_OverrideDepthEnable,
	kHook_DrawScreenSpaceQuad,
	kHook_DrawScreenSpaceRectangle,
	kHook_PushRTVP0,
	kHook_PushRTVP1,
	kHook_PushRTVP5,
	kHook_PushRTVP6,
	kHook_PopRenderTargetAndViewport,
	kHook_CopyRenderTargetToTextureEx,
	kHook_SetIntRenderingParameter,
	kHook_PushCustomClipPlane,
	kHook_PopCustomClipPlane,
	kHook_ClearBuffersObeyStencil,
	kHook_ResetToneMappingScale,
	kHook_SetGoalToneMappingScale,
	kHook_TurnOnToneMapping,
	kHook_SetToneMappingScaleLinear,
	kHook_FogMaxDensity,
	kHook_SetFullScreenDepthTextureValidityFlag,
	kHook_OverrideAlphaWriteEnable,
	kHook_OverrideColorWriteEnable,
	kHook_FogRadial,
	kHook_SetFrameBufferCopyTexture,
	kHook_ForcedMaterialOverride,
	kHook_DrawModelEx,
	kHook_DrawModelExStaticProp,
	kHook_DrawModelExecute,
	kHook_DrawStaticPropArrayFast,
	kHook_ComputePropOpacity,
	kHook_DrawStaticProps,
	kHook_Sound_ExtraUpdate,
	kHook_DrawPortals,
	kHook_IsSkyboxVisibleFromPoint,
	kHook_GrabPreColorCorrectedFrame,
	kHook_PreRender,
	kHook_AdvanceFrame,
	kHook_ComputeShadowTextures,
	kHook_ComputeShadowDepthTextures,
	kHook_UnlockAllShadowDepthTextures,
	// The mesh and occlusion-query hooks (SOURCESHARP_RENDER_TRACE_MESHES=1).
	kHook_Bind,
	kHook_GetDynamicMesh,
	kHook_EnableClipping,
	kHook_CreateOcclusionQueryObject,
	kHook_DestroyOcclusionQueryObject,
	kHook_ResetOcclusionQueryObject,
	kHook_BeginOcclusionQueryDrawing,
	kHook_EndOcclusionQueryDrawing,
	kHook_OcclusionQuery_GetNumPixelsRendered,
	kHook_ComputePixelDiameterOfSphere,
	kHookCount
};

// Each hooked slot's original, by hook: one table rather than one static per hook.
void *g_Originals[ kHookCount ];

#define SS_CALL( name, type ) ( (type)g_Originals[ kHook_##name ] )

// IVRenderView ---------------------------------------------------------------
void H_DrawBrushModel( IVRenderView *self, IClientEntity *pEnt, model_t *pModel, const Vector &origin, const QAngle &angles, bool bSort )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawBrushModel\tmodel=%s sort=%d", modelinfo->GetModelName( pModel ), bSort ? 1 : 0 );
	SS_CALL( DrawBrushModel, void (*)( IVRenderView *, IClientEntity *, model_t *, const Vector &, const QAngle &, bool ) )( self, pEnt, pModel, origin, angles, bSort );
}

void H_DrawIdentityBrushModel( IVRenderView *self, IWorldRenderList *pList, model_t *pModel )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawIdentityBrushModel\tlist=%d model=%s", WorldList( pList ), modelinfo->GetModelName( pModel ) );
	SS_CALL( DrawIdentityBrushModel, void (*)( IVRenderView *, IWorldRenderList *, model_t * ) )( self, pList, pModel );
}

void H_Draw3DDebugOverlays( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.Draw3DDebugOverlays" );
	SS_CALL( Draw3DDebugOverlays, void (*)( IVRenderView * ) )( self );
}

void H_SetBlend( IVRenderView *self, float flBlend )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SetBlend\t%.9g", flBlend );
	SS_CALL( SetBlend, void (*)( IVRenderView *, float ) )( self, flBlend );
}

void H_SetColorModulation( IVRenderView *self, const float *pBlend )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SetColorModulation\t%.9g,%.9g,%.9g", pBlend[0], pBlend[1], pBlend[2] );
	SS_CALL( SetColorModulation, void (*)( IVRenderView *, const float * ) )( self, pBlend );
}

void H_SceneBegin( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SceneBegin" );
	SS_CALL( SceneBegin, void (*)( IVRenderView * ) )( self );
}

void H_SceneEnd( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SceneEnd" );
	SS_CALL( SceneEnd, void (*)( IVRenderView * ) )( self );
}

void H_GetVisibleFogVolume( IVRenderView *self, const Vector &eye, VisibleFogVolumeInfo_t *pInfo )
{
	bool bClient = SS_FROM_CLIENT();
	SS_CALL( GetVisibleFogVolume, void (*)( IVRenderView *, const Vector &, VisibleFogVolumeInfo_t * ) )( self, eye, pInfo );
	if ( bClient )
		Line( "q\trender.GetVisibleFogVolume\teye=%.9g,%.9g,%.9g volume=%d leaf=%d infog=%d dist=%.9g height=%.9g material=%s",
		      eye.x, eye.y, eye.z, pInfo->m_nVisibleFogVolume, pInfo->m_nVisibleFogVolumeLeaf,
		      pInfo->m_bEyeInFogVolume ? 1 : 0, pInfo->m_flDistanceToWater, pInfo->m_flWaterHeight,
		      MaterialName( pInfo->m_pFogVolumeMaterial ) );
}

IWorldRenderList *H_CreateWorldList( IVRenderView *self )
{
	bool bClient = SS_FROM_CLIENT();
	IWorldRenderList *pList = SS_CALL( CreateWorldList, IWorldRenderList *(*)( IVRenderView * ) )( self );
	if ( bClient )
		Line( "render.CreateWorldList\tlist=%d", WorldList( pList ) );
	return pList;
}

void H_BuildWorldLists( IVRenderView *self, IWorldRenderList *pList, WorldListInfo_t *pInfo, int iForceViewLeaf,
                        const VisOverrideData_t *pVisData, bool bShadowDepth, float *pReflectionWaterHeight )
{
	bool bClient = SS_FROM_CLIENT();
	SS_CALL( BuildWorldLists, void (*)( IVRenderView *, IWorldRenderList *, WorldListInfo_t *, int, const VisOverrideData_t *, bool, float * ) )(
		self, pList, pInfo, iForceViewLeaf, pVisData, bShadowDepth, pReflectionWaterHeight );
	if ( bClient )
	{
		char szHeight[ 32 ] = "-";
		if ( pReflectionWaterHeight )
			V_snprintf( szHeight, sizeof( szHeight ), "%.9g", *pReflectionWaterHeight );
		Line( "render.BuildWorldLists\tlist=%d forceleaf=%d visdata=%d shadowdepth=%d reflheight=%s => fogvolume=%d leaves=%d",
		      WorldList( pList ), iForceViewLeaf, pVisData ? 1 : 0, bShadowDepth ? 1 : 0, szHeight,
		      pInfo ? pInfo->m_ViewFogVolume : 0, pInfo ? pInfo->m_LeafCount : 0 );
	}
}

void H_DrawWorldLists( IVRenderView *self, IWorldRenderList *pList, unsigned long flags, float waterZAdjust )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawWorldLists\tlist=%d flags=0x%lx waterz=%.9g", WorldList( pList ), flags, waterZAdjust );
	SS_CALL( DrawWorldLists, void (*)( IVRenderView *, IWorldRenderList *, unsigned long, float ) )( self, pList, flags, waterZAdjust );
}

void H_DrawLights( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawLights" );
	SS_CALL( DrawLights, void (*)( IVRenderView * ) )( self );
}

void H_DrawMaskEntities( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawMaskEntities" );
	SS_CALL( DrawMaskEntities, void (*)( IVRenderView * ) )( self );
}

void H_DrawTranslucentSurfaces( IVRenderView *self, IWorldRenderList *pList, int sortIndex, unsigned long flags, bool bShadowDepth )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawTranslucentSurfaces\tlist=%d leaf=%d flags=0x%lx shadowdepth=%d", WorldList( pList ), sortIndex, flags, bShadowDepth ? 1 : 0 );
	SS_CALL( DrawTranslucentSurfaces, void (*)( IVRenderView *, IWorldRenderList *, int, unsigned long, bool ) )( self, pList, sortIndex, flags, bShadowDepth );
}

bool H_LeafContainsTranslucentSurfaces( IVRenderView *self, IWorldRenderList *pList, int sortIndex, unsigned long flags )
{
	bool bClient = SS_FROM_CLIENT();
	bool bAnswer = SS_CALL( LeafContainsTranslucentSurfaces, bool (*)( IVRenderView *, IWorldRenderList *, int, unsigned long ) )( self, pList, sortIndex, flags );
	if ( bClient )
		Line( "q\trender.LeafContainsTranslucentSurfaces\tlist=%d leaf=%d flags=0x%lx => %d", WorldList( pList ), sortIndex, flags, bAnswer ? 1 : 0 );
	return bAnswer;
}

void H_ViewSetupVis( IVRenderView *self, bool bNoVis, int nOrigins, const Vector origin[] )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.ViewSetupVis\tnovis=%d n=%d origin=%.9g,%.9g,%.9g", bNoVis ? 1 : 0, nOrigins,
		      nOrigins > 0 ? origin[0].x : 0.0f, nOrigins > 0 ? origin[0].y : 0.0f, nOrigins > 0 ? origin[0].z : 0.0f );
	SS_CALL( ViewSetupVis, void (*)( IVRenderView *, bool, int, const Vector * ) )( self, bNoVis, nOrigins, origin );
}

void H_ViewSetupVisEx( IVRenderView *self, bool bNoVis, int nOrigins, const Vector origin[], unsigned int &returnFlags )
{
	bool bClient = SS_FROM_CLIENT();
	SS_CALL( ViewSetupVisEx, void (*)( IVRenderView *, bool, int, const Vector *, unsigned int & ) )( self, bNoVis, nOrigins, origin, returnFlags );
	if ( bClient )
		Line( "render.ViewSetupVisEx\tnovis=%d n=%d origin=%.9g,%.9g,%.9g => flags=0x%x", bNoVis ? 1 : 0, nOrigins,
		      nOrigins > 0 ? origin[0].x : 0.0f, nOrigins > 0 ? origin[0].y : 0.0f, nOrigins > 0 ? origin[0].z : 0.0f, returnFlags );
}

void H_VguiPaint( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.VguiPaint" );
	SS_CALL( VguiPaint, void (*)( IVRenderView * ) )( self );
}

void H_ViewDrawFade( IVRenderView *self, byte *pColor, IMaterial *pMaterial )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.ViewDrawFade\tcolor=%d,%d,%d,%d material=%s", pColor[0], pColor[1], pColor[2], pColor[3], MaterialName( pMaterial ) );
	SS_CALL( ViewDrawFade, void (*)( IVRenderView *, byte *, IMaterial * ) )( self, pColor, pMaterial );
}

void H_SetFogVolumeState( IVRenderView *self, int nVisibleFogVolume, bool bUseHeightFog )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SetFogVolumeState\tvolume=%d heightfog=%d", nVisibleFogVolume, bUseHeightFog ? 1 : 0 );
	SS_CALL( SetFogVolumeState, void (*)( IVRenderView *, int, bool ) )( self, nVisibleFogVolume, bUseHeightFog );
}

void H_VGui_Paint( IVRenderView *self, int mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.VGui_Paint\tmode=0x%x", mode );
	SS_CALL( VGui_Paint, void (*)( IVRenderView *, int ) )( self, mode );
}

void H_Push3DView( IVRenderView *self, const CViewSetup &view, int nFlags, ITexture *pRenderTarget, Frustum frustumPlanes )
{
	if ( SS_FROM_CLIENT() )
	{
		char szView[ 768 ];
		ViewText( view, szView, sizeof( szView ) );
		Line( "render.Push3DView\tclear=0x%x rt=%s depth=- %s", nFlags, TextureName( pRenderTarget ), szView );
	}
	SS_CALL( Push3DView, void (*)( IVRenderView *, const CViewSetup &, int, ITexture *, VPlane * ) )( self, view, nFlags, pRenderTarget, frustumPlanes );
}

void H_Push3DViewDepth( IVRenderView *self, const CViewSetup &view, int nFlags, ITexture *pRenderTarget, Frustum frustumPlanes, ITexture *pDepthTexture )
{
	if ( SS_FROM_CLIENT() )
	{
		char szView[ 768 ];
		ViewText( view, szView, sizeof( szView ) );
		Line( "render.Push3DView\tclear=0x%x rt=%s depth=%s %s", nFlags, TextureName( pRenderTarget ), TextureName( pDepthTexture ), szView );
	}
	SS_CALL( Push3DViewDepth, void (*)( IVRenderView *, const CViewSetup &, int, ITexture *, VPlane *, ITexture * ) )( self, view, nFlags, pRenderTarget, frustumPlanes, pDepthTexture );
}

void H_Push2DView( IVRenderView *self, const CViewSetup &view, int nFlags, ITexture *pRenderTarget, Frustum frustumPlanes )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.Push2DView\tclear=0x%x rt=%s xywh=%d,%d,%d,%d", nFlags, TextureName( pRenderTarget ), view.x, view.y, view.width, view.height );
	SS_CALL( Push2DView, void (*)( IVRenderView *, const CViewSetup &, int, ITexture *, VPlane * ) )( self, view, nFlags, pRenderTarget, frustumPlanes );
}

void H_PopView( IVRenderView *self, Frustum frustumPlanes )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.PopView" );
	SS_CALL( PopView, void (*)( IVRenderView *, VPlane * ) )( self, frustumPlanes );
}

void H_SetMainView( IVRenderView *self, const Vector &origin, const QAngle &angles )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SetMainView\torigin=%.9g,%.9g,%.9g angles=%.9g,%.9g,%.9g", origin.x, origin.y, origin.z, angles.x, angles.y, angles.z );
	SS_CALL( SetMainView, void (*)( IVRenderView *, const Vector &, const QAngle & ) )( self, origin, angles );
}

void H_OverrideViewFrustum( IVRenderView *self, Frustum custom )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.OverrideViewFrustum" );
	SS_CALL( OverrideViewFrustum, void (*)( IVRenderView *, VPlane * ) )( self, custom );
}

void H_UpdateBrushModelLightmap( IVRenderView *self, model_t *pModel, IClientRenderable *pRenderable )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.UpdateBrushModelLightmap\tmodel=%s", modelinfo->GetModelName( pModel ) );
	SS_CALL( UpdateBrushModelLightmap, void (*)( IVRenderView *, model_t *, IClientRenderable * ) )( self, pModel, pRenderable );
}

void H_BeginUpdateLightmaps( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.BeginUpdateLightmaps" );
	SS_CALL( BeginUpdateLightmaps, void (*)( IVRenderView * ) )( self );
}

void H_EndUpdateLightmaps( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.EndUpdateLightmaps" );
	SS_CALL( EndUpdateLightmaps, void (*)( IVRenderView * ) )( self );
}

void H_DrawBrushModelEx( IVRenderView *self, IClientEntity *pEnt, model_t *pModel, const Vector &origin, const QAngle &angles, DrawBrushModelMode_t mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawBrushModelEx\tmodel=%s mode=%d", modelinfo->GetModelName( pModel ), (int)mode );
	SS_CALL( DrawBrushModelEx, void (*)( IVRenderView *, IClientEntity *, model_t *, const Vector &, const QAngle &, DrawBrushModelMode_t ) )( self, pEnt, pModel, origin, angles, mode );
}

void H_DrawBrushModelShadowDepth( IVRenderView *self, IClientEntity *pEnt, model_t *pModel, const Vector &origin, const QAngle &angles, ERenderDepthMode mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawBrushModelShadowDepth\tmodel=%s mode=%d", modelinfo->GetModelName( pModel ), (int)mode );
	SS_CALL( DrawBrushModelShadowDepth, void (*)( IVRenderView *, IClientEntity *, model_t *, const Vector &, const QAngle &, ERenderDepthMode ) )( self, pEnt, pModel, origin, angles, mode );
}

// IMatRenderContext ----------------------------------------------------------
void H_Flush( IMatRenderContext *self, bool bHardware )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.Flush\thardware=%d", bHardware ? 1 : 0 );
	SS_CALL( Flush, void (*)( IMatRenderContext *, bool ) )( self, bHardware );
}

void H_SetRenderTarget( IMatRenderContext *self, ITexture *pTexture )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetRenderTarget\trt=%s", TextureName( pTexture ) );
	SS_CALL( SetRenderTarget, void (*)( IMatRenderContext *, ITexture * ) )( self, pTexture );
}

void H_DepthRange( IMatRenderContext *self, float flNear, float flFar )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.DepthRange\t%.9g,%.9g", flNear, flFar );
	SS_CALL( DepthRange, void (*)( IMatRenderContext *, float, float ) )( self, flNear, flFar );
}

void H_ClearBuffers( IMatRenderContext *self, bool bColor, bool bDepth, bool bStencil )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ClearBuffers\tcolor=%d depth=%d stencil=%d", bColor ? 1 : 0, bDepth ? 1 : 0, bStencil ? 1 : 0 );
	SS_CALL( ClearBuffers, void (*)( IMatRenderContext *, bool, bool, bool ) )( self, bColor, bDepth, bStencil );
}

void H_MatrixMode( IMatRenderContext *self, MaterialMatrixMode_t mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.MatrixMode\t%d", (int)mode );
	SS_CALL( MatrixMode, void (*)( IMatRenderContext *, MaterialMatrixMode_t ) )( self, mode );
}

void H_PushMatrix( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushMatrix" );
	SS_CALL( PushMatrix, void (*)( IMatRenderContext * ) )( self );
}

void H_PopMatrix( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PopMatrix" );
	SS_CALL( PopMatrix, void (*)( IMatRenderContext * ) )( self );
}

void H_Viewport( IMatRenderContext *self, int x, int y, int w, int h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.Viewport\t%d,%d,%d,%d", x, y, w, h );
	SS_CALL( Viewport, void (*)( IMatRenderContext *, int, int, int, int ) )( self, x, y, w, h );
}

void H_SetHeightClipMode( IMatRenderContext *self, MaterialHeightClipMode_t mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetHeightClipMode\t%d", (int)mode );
	SS_CALL( SetHeightClipMode, void (*)( IMatRenderContext *, MaterialHeightClipMode_t ) )( self, mode );
}

void H_SetHeightClipZ( IMatRenderContext *self, float z )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetHeightClipZ\t%.9g", z );
	SS_CALL( SetHeightClipZ, void (*)( IMatRenderContext *, float ) )( self, z );
}

void H_FogMode( IMatRenderContext *self, MaterialFogMode_t mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogMode\t%d", (int)mode );
	SS_CALL( FogMode, void (*)( IMatRenderContext *, MaterialFogMode_t ) )( self, mode );
}

void H_FogStart( IMatRenderContext *self, float f )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogStart\t%.9g", f );
	SS_CALL( FogStart, void (*)( IMatRenderContext *, float ) )( self, f );
}

void H_FogEnd( IMatRenderContext *self, float f )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogEnd\t%.9g", f );
	SS_CALL( FogEnd, void (*)( IMatRenderContext *, float ) )( self, f );
}

void H_SetFogZ( IMatRenderContext *self, float f )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetFogZ\t%.9g", f );
	SS_CALL( SetFogZ, void (*)( IMatRenderContext *, float ) )( self, f );
}

void H_FogColor3fv( IMatRenderContext *self, const float *pRgb )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogColor3fv\t%.9g,%.9g,%.9g", pRgb[0], pRgb[1], pRgb[2] );
	SS_CALL( FogColor3fv, void (*)( IMatRenderContext *, const float * ) )( self, pRgb );
}

void H_ClearColor3ub( IMatRenderContext *self, unsigned char r, unsigned char g, unsigned char b )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ClearColor3ub\t%d,%d,%d", r, g, b );
	SS_CALL( ClearColor3ub, void (*)( IMatRenderContext *, unsigned char, unsigned char, unsigned char ) )( self, r, g, b );
}

void H_ClearColor4ub( IMatRenderContext *self, unsigned char r, unsigned char g, unsigned char b, unsigned char a )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ClearColor4ub\t%d,%d,%d,%d", r, g, b, a );
	SS_CALL( ClearColor4ub, void (*)( IMatRenderContext *, unsigned char, unsigned char, unsigned char, unsigned char ) )( self, r, g, b, a );
}

void H_OverrideDepthEnable( IMatRenderContext *self, bool bEnable, bool bDepthEnable )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.OverrideDepthEnable\t%d,%d", bEnable ? 1 : 0, bDepthEnable ? 1 : 0 );
	SS_CALL( OverrideDepthEnable, void (*)( IMatRenderContext *, bool, bool ) )( self, bEnable, bDepthEnable );
}

// The values a screen-space draw's material had once its proxies bound (C7
// step 3's frozen recordings: the engine_post, bloom, tonemap and motion blur
// materials' vars, which the managed proxies must reproduce): one q line per
// shader parameter, after the draw.
void MaterialVarLines( IMaterial *pMaterial )
{
	if ( !pMaterial )
		return;
	IMaterialVar **ppVars = pMaterial->GetShaderParams();
	const int nCount = pMaterial->ShaderParamCount();
	for ( int i = 0; ppVars && i < nCount; ++i )
	{
		IMaterialVar *pVar = ppVars[i];
		if ( !pVar )
			continue;
		// Numbers at full precision (GetStringValue prints six places); the rest as text.
		switch ( pVar->GetType() )
		{
		case MATERIAL_VAR_TYPE_FLOAT:
			Line( "q\tmaterial.var\t%s\t%s\tf %.9g", MaterialName( pMaterial ), pVar->GetName(), pVar->GetFloatValue() );
			break;
		case MATERIAL_VAR_TYPE_INT:
			Line( "q\tmaterial.var\t%s\t%s\ti %d", MaterialName( pMaterial ), pVar->GetName(), pVar->GetIntValue() );
			break;
		case MATERIAL_VAR_TYPE_VECTOR:
		{
			float v[4] = { 0, 0, 0, 0 };
			const int n = MIN( MAX( pVar->VectorSize(), 0 ), 4 );
			pVar->GetVecValue( v, n );
			Line( "q\tmaterial.var\t%s\t%s\tv%d %.9g,%.9g,%.9g,%.9g", MaterialName( pMaterial ), pVar->GetName(), n, v[0], v[1], v[2], v[3] );
			break;
		}
		default:
			Line( "q\tmaterial.var\t%s\t%s\ts %s", MaterialName( pMaterial ), pVar->GetName(), pVar->GetStringValue() );
			break;
		}
	}
}

void H_DrawScreenSpaceQuad( IMatRenderContext *self, IMaterial *pMaterial )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.DrawScreenSpaceQuad\tmaterial=%s", MaterialName( pMaterial ) );
	SS_CALL( DrawScreenSpaceQuad, void (*)( IMatRenderContext *, IMaterial * ) )( self, pMaterial );
	if ( SS_FROM_CLIENT() )
		MaterialVarLines( pMaterial );
}

void H_DrawScreenSpaceRectangle( IMatRenderContext *self, IMaterial *pMaterial, int destx, int desty, int width, int height,
                                 float srcx0, float srcy0, float srcx1, float srcy1, int srcw, int srch,
                                 void *pClientRenderable, int nXDice, int nYDice )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.DrawScreenSpaceRectangle\tmaterial=%s dest=%d,%d,%d,%d src=%.9g,%.9g,%.9g,%.9g srcsize=%d,%d dice=%d,%d",
		      MaterialName( pMaterial ), destx, desty, width, height, srcx0, srcy0, srcx1, srcy1, srcw, srch, nXDice, nYDice );
	SS_CALL( DrawScreenSpaceRectangle, void (*)( IMatRenderContext *, IMaterial *, int, int, int, int, float, float, float, float, int, int, void *, int, int ) )(
		self, pMaterial, destx, desty, width, height, srcx0, srcy0, srcx1, srcy1, srcw, srch, pClientRenderable, nXDice, nYDice );
	if ( SS_FROM_CLIENT() )
		MaterialVarLines( pMaterial );
}

void H_PushRTVP0( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushRenderTargetAndViewport" );
	SS_CALL( PushRTVP0, void (*)( IMatRenderContext * ) )( self );
}

void H_PushRTVP1( IMatRenderContext *self, ITexture *pTexture )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushRenderTargetAndViewport\trt=%s", TextureName( pTexture ) );
	SS_CALL( PushRTVP1, void (*)( IMatRenderContext *, ITexture * ) )( self, pTexture );
}

void H_PushRTVP5( IMatRenderContext *self, ITexture *pTexture, int x, int y, int w, int h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushRenderTargetAndViewport\trt=%s xywh=%d,%d,%d,%d", TextureName( pTexture ), x, y, w, h );
	SS_CALL( PushRTVP5, void (*)( IMatRenderContext *, ITexture *, int, int, int, int ) )( self, pTexture, x, y, w, h );
}

void H_PushRTVP6( IMatRenderContext *self, ITexture *pTexture, ITexture *pDepth, int x, int y, int w, int h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushRenderTargetAndViewport\trt=%s depth=%s xywh=%d,%d,%d,%d", TextureName( pTexture ), TextureName( pDepth ), x, y, w, h );
	SS_CALL( PushRTVP6, void (*)( IMatRenderContext *, ITexture *, ITexture *, int, int, int, int ) )( self, pTexture, pDepth, x, y, w, h );
}

void H_PopRenderTargetAndViewport( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PopRenderTargetAndViewport" );
	SS_CALL( PopRenderTargetAndViewport, void (*)( IMatRenderContext * ) )( self );
}

void H_CopyRenderTargetToTextureEx( IMatRenderContext *self, ITexture *pTexture, int nRenderTargetID, Rect_t *pSrc, Rect_t *pDst )
{
	if ( SS_FROM_CLIENT() )
	{
		char szSrc[ 64 ] = "-", szDst[ 64 ] = "-";
		if ( pSrc )
			V_snprintf( szSrc, sizeof( szSrc ), "%d,%d,%d,%d", pSrc->x, pSrc->y, pSrc->width, pSrc->height );
		if ( pDst )
			V_snprintf( szDst, sizeof( szDst ), "%d,%d,%d,%d", pDst->x, pDst->y, pDst->width, pDst->height );
		Line( "ctx.CopyRenderTargetToTextureEx\ttexture=%s id=%d src=%s dst=%s", TextureName( pTexture ), nRenderTargetID, szSrc, szDst );
	}
	SS_CALL( CopyRenderTargetToTextureEx, void (*)( IMatRenderContext *, ITexture *, int, Rect_t *, Rect_t * ) )( self, pTexture, nRenderTargetID, pSrc, pDst );
}

void H_SetIntRenderingParameter( IMatRenderContext *self, int parm, int value )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetIntRenderingParameter\t%d=%d", parm, value );
	SS_CALL( SetIntRenderingParameter, void (*)( IMatRenderContext *, int, int ) )( self, parm, value );
}

void H_PushCustomClipPlane( IMatRenderContext *self, const float *pPlane )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushCustomClipPlane\t%.9g,%.9g,%.9g,%.9g", pPlane[0], pPlane[1], pPlane[2], pPlane[3] );
	SS_CALL( PushCustomClipPlane, void (*)( IMatRenderContext *, const float * ) )( self, pPlane );
}

void H_PopCustomClipPlane( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PopCustomClipPlane" );
	SS_CALL( PopCustomClipPlane, void (*)( IMatRenderContext * ) )( self );
}

void H_ClearBuffersObeyStencil( IMatRenderContext *self, bool bColor, bool bDepth )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ClearBuffersObeyStencil\tcolor=%d depth=%d", bColor ? 1 : 0, bDepth ? 1 : 0 );
	SS_CALL( ClearBuffersObeyStencil, void (*)( IMatRenderContext *, bool, bool ) )( self, bColor, bDepth );
}

void H_ResetToneMappingScale( IMatRenderContext *self, float flScale )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ResetToneMappingScale\t%.9g", flScale );
	SS_CALL( ResetToneMappingScale, void (*)( IMatRenderContext *, float ) )( self, flScale );
}

void H_SetGoalToneMappingScale( IMatRenderContext *self, float flScale )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetGoalToneMappingScale\t%.9g", flScale );
	SS_CALL( SetGoalToneMappingScale, void (*)( IMatRenderContext *, float ) )( self, flScale );
}

void H_TurnOnToneMapping( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.TurnOnToneMapping" );
	SS_CALL( TurnOnToneMapping, void (*)( IMatRenderContext * ) )( self );
}

void H_SetToneMappingScaleLinear( IMatRenderContext *self, const Vector &scale )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetToneMappingScaleLinear\t%.9g,%.9g,%.9g", scale.x, scale.y, scale.z );
	SS_CALL( SetToneMappingScaleLinear, void (*)( IMatRenderContext *, const Vector & ) )( self, scale );
}

void H_FogMaxDensity( IMatRenderContext *self, float f )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogMaxDensity\t%.9g", f );
	SS_CALL( FogMaxDensity, void (*)( IMatRenderContext *, float ) )( self, f );
}

void H_SetFullScreenDepthTextureValidityFlag( IMatRenderContext *self, bool bValid )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetFullScreenDepthTextureValidityFlag\t%d", bValid ? 1 : 0 );
	SS_CALL( SetFullScreenDepthTextureValidityFlag, void (*)( IMatRenderContext *, bool ) )( self, bValid );
}

void H_OverrideAlphaWriteEnable( IMatRenderContext *self, bool bEnable, bool bAlpha )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.OverrideAlphaWriteEnable\t%d,%d", bEnable ? 1 : 0, bAlpha ? 1 : 0 );
	SS_CALL( OverrideAlphaWriteEnable, void (*)( IMatRenderContext *, bool, bool ) )( self, bEnable, bAlpha );
}

void H_OverrideColorWriteEnable( IMatRenderContext *self, bool bEnable, bool bColor )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.OverrideColorWriteEnable\t%d,%d", bEnable ? 1 : 0, bColor ? 1 : 0 );
	SS_CALL( OverrideColorWriteEnable, void (*)( IMatRenderContext *, bool, bool ) )( self, bEnable, bColor );
}

void H_FogRadial( IMatRenderContext *self, bool bRadial )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogRadial\t%d", bRadial ? 1 : 0 );
	SS_CALL( FogRadial, void (*)( IMatRenderContext *, bool ) )( self, bRadial );
}

void H_SetFrameBufferCopyTexture( IMatRenderContext *self, ITexture *pTexture, int nIndex )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetFrameBufferCopyTexture\ttexture=%s index=%d", TextureName( pTexture ), nIndex );
	SS_CALL( SetFrameBufferCopyTexture, void (*)( IMatRenderContext *, ITexture *, int ) )( self, pTexture, nIndex );
}

// IVModelRender --------------------------------------------------------------
void H_ForcedMaterialOverride( IVModelRender *self, IMaterial *pMaterial, OverrideType_t type )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.ForcedMaterialOverride\tmaterial=%s type=%d", MaterialName( pMaterial ), (int)type );
	SS_CALL( ForcedMaterialOverride, void (*)( IVModelRender *, IMaterial *, OverrideType_t ) )( self, pMaterial, type );
}

int H_DrawModelEx( IVModelRender *self, ModelRenderInfo_t &info )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.DrawModelEx\tent=%d model=%s flags=0x%x skin=%d body=%d", info.entity_index,
		      info.pModel ? modelinfo->GetModelName( info.pModel ) : "-", info.flags, info.skin, info.body );
	return SS_CALL( DrawModelEx, int (*)( IVModelRender *, ModelRenderInfo_t & ) )( self, info );
}

int H_DrawModelExStaticProp( IVModelRender *self, ModelRenderInfo_t &info )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.DrawModelExStaticProp\tent=%d model=%s flags=0x%x", info.entity_index,
		      info.pModel ? modelinfo->GetModelName( info.pModel ) : "-", info.flags );
	return SS_CALL( DrawModelExStaticProp, int (*)( IVModelRender *, ModelRenderInfo_t & ) )( self, info );
}

void H_DrawModelExecute( IVModelRender *self, const DrawModelState_t &state, const ModelRenderInfo_t &info, matrix3x4_t *pCustomBoneToWorld )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.DrawModelExecute\tent=%d model=%s flags=0x%x", info.entity_index,
		      info.pModel ? modelinfo->GetModelName( info.pModel ) : "-", info.flags );
	SS_CALL( DrawModelExecute, void (*)( IVModelRender *, const DrawModelState_t &, const ModelRenderInfo_t &, matrix3x4_t * ) )( self, state, info, pCustomBoneToWorld );
}

int H_DrawStaticPropArrayFast( IVModelRender *self, StaticPropRenderInfo_t *pProps, int count, bool bShadowDepth )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.DrawStaticPropArrayFast\tcount=%d shadowdepth=%d", count, bShadowDepth ? 1 : 0 );
	return SS_CALL( DrawStaticPropArrayFast, int (*)( IVModelRender *, StaticPropRenderInfo_t *, int, bool ) )( self, pProps, count, bShadowDepth );
}

// IStaticPropMgrClient -------------------------------------------------------
void H_ComputePropOpacity( IStaticPropMgrClient *self, const Vector &origin, float factor )
{
	if ( SS_FROM_CLIENT() )
		Line( "props.ComputePropOpacity\torigin=%.9g,%.9g,%.9g factor=%.9g", origin.x, origin.y, origin.z, factor );
	SS_CALL( ComputePropOpacity, void (*)( IStaticPropMgrClient *, const Vector &, float ) )( self, origin, factor );
}

void H_DrawStaticProps( IStaticPropMgrClient *self, IClientRenderable **pProps, int count, bool bShadowDepth, bool bWireframe )
{
	if ( SS_FROM_CLIENT() )
		Line( "props.DrawStaticProps\tcount=%d shadowdepth=%d wireframe=%d", count, bShadowDepth ? 1 : 0, bWireframe ? 1 : 0 );
	SS_CALL( DrawStaticProps, void (*)( IStaticPropMgrClient *, IClientRenderable **, int, bool, bool ) )( self, pProps, count, bShadowDepth, bWireframe );
}

// IVEngineClient -------------------------------------------------------------
void H_Sound_ExtraUpdate( IVEngineClient *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "engine.Sound_ExtraUpdate" );
	SS_CALL( Sound_ExtraUpdate, void (*)( IVEngineClient * ) )( self );
}

void H_DrawPortals( IVEngineClient *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "engine.DrawPortals" );
	SS_CALL( DrawPortals, void (*)( IVEngineClient * ) )( self );
}

SkyboxVisibility_t H_IsSkyboxVisibleFromPoint( IVEngineClient *self, const Vector &point )
{
	bool bClient = SS_FROM_CLIENT();
	SkyboxVisibility_t result = SS_CALL( IsSkyboxVisibleFromPoint, SkyboxVisibility_t (*)( IVEngineClient *, const Vector & ) )( self, point );
	if ( bClient )
		Line( "q\tengine.IsSkyboxVisibleFromPoint\tpoint=%.9g,%.9g,%.9g => %d", point.x, point.y, point.z, (int)result );
	return result;
}

void H_GrabPreColorCorrectedFrame( IVEngineClient *self, int x, int y, int w, int h )
{
	if ( SS_FROM_CLIENT() )
		Line( "engine.GrabPreColorCorrectedFrame\t%d,%d,%d,%d", x, y, w, h );
	SS_CALL( GrabPreColorCorrectedFrame, void (*)( IVEngineClient *, int, int, int, int ) )( self, x, y, w, h );
}

// IClientShadowMgr (client.so's own object) -----------------------------------
void H_PreRender( IClientShadowMgr *self )
{
	const bool bClient = SS_FROM_CLIENT();
	if ( bClient )
		Line( "shadow.PreRender" );
	++g_nShadowDepth;
	SS_CALL( PreRender, void (*)( IClientShadowMgr * ) )( self );
	--g_nShadowDepth;
}

void H_AdvanceFrame( IClientShadowMgr *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "shadow.AdvanceFrame" );
	++g_nShadowDepth;
	SS_CALL( AdvanceFrame, void (*)( IClientShadowMgr * ) )( self );
	--g_nShadowDepth;
}

void H_ComputeShadowTextures( IClientShadowMgr *self, const CViewSetup &view, int leafCount, LeafIndex_t *pLeafList )
{
	if ( SS_FROM_CLIENT() )
		Line( "shadow.ComputeShadowTextures	leaves=%d", leafCount );
	++g_nShadowDepth;
	SS_CALL( ComputeShadowTextures, void (*)( IClientShadowMgr *, const CViewSetup &, int, LeafIndex_t * ) )( self, view, leafCount, pLeafList );
	--g_nShadowDepth;
}

void H_ComputeShadowDepthTextures( IClientShadowMgr *self, const CViewSetup &view )
{
	if ( SS_FROM_CLIENT() )
		Line( "shadow.ComputeShadowDepthTextures" );
	++g_nShadowDepth;
	SS_CALL( ComputeShadowDepthTextures, void (*)( IClientShadowMgr *, const CViewSetup & ) )( self, view );
	--g_nShadowDepth;
}

void H_UnlockAllShadowDepthTextures( IClientShadowMgr *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "shadow.UnlockAllShadowDepthTextures" );
	SS_CALL( UnlockAllShadowDepthTextures, void (*)( IClientShadowMgr * ) )( self );
}

// THE MESH AND OCCLUSION-QUERY HOOKS, armed only when the game was started with
// SOURCESHARP_RENDER_TRACE_MESHES=1 (tools/probes/sessions/c7-render-small-ab.steps):
// the frozen recordings and the live compare were taken without them, so a
// default trace keeps their lines. They witness the draws that go through
// dynamic meshes and occlusion queries -- pixel visibility's proxies and the
// glow overlays' quads (C7) -- which a call trace otherwise cannot see: the
// material bound, each dynamic mesh with its vertices as UnlockMesh is handed
// them, the clipping switch, and every query object by the order it first
// appeared. The pixel counts and sphere diameters are `q` lines (answers).
bool g_bMeshes = false;
CUtlVector<const void *> g_Queries;	// a query object's ordinal is its index here

int Query( const void *h )
{
	if ( !h )
		return -1;
	int i = g_Queries.Find( h );
	if ( i < 0 )
		i = g_Queries.AddToTail( h );
	return i;
}

// IMesh::UnlockMesh, by the vtable of the mesh it was installed on: dynamic
// meshes of more than one class may be handed out, and each class has its own
// original.
struct MeshHook_t
{
	void **pVtable;
	void *pOriginal;
};
MeshHook_t g_MeshHooks[ 8 ];
int g_nMeshHooks = 0;

void H_UnlockMesh( IMesh *self, int nVertexCount, int nIndexCount, MeshDesc_t &desc )
{
	void **pVtable = *(void ***)self;
	void *pOriginal = NULL;
	for ( int i = 0; i < g_nMeshHooks; i++ )
	{
		if ( g_MeshHooks[ i ].pVtable == pVtable )
			pOriginal = g_MeshHooks[ i ].pOriginal;
	}
	if ( SS_FROM_CLIENT() )
	{
		char szVerts[ 900 ];
		int n = 0;
		szVerts[ 0 ] = 0;
		for ( int v = 0; v < nVertexCount && v < 12 && desc.m_pPosition && n < (int)sizeof( szVerts ) - 80; v++ )
		{
			const float *p = (const float *)( (const unsigned char *)desc.m_pPosition + v * desc.m_VertexSize_Position );
			n += V_snprintf( szVerts + n, sizeof( szVerts ) - n, " %.9g,%.9g,%.9g", p[ 0 ], p[ 1 ], p[ 2 ] );
			if ( desc.m_pColor && desc.m_VertexSize_Color )
			{
				const unsigned char *c = desc.m_pColor + v * desc.m_VertexSize_Color;
				n += V_snprintf( szVerts + n, sizeof( szVerts ) - n, "/%02x%02x%02x%02x", c[ 0 ], c[ 1 ], c[ 2 ], c[ 3 ] );
			}
		}
		Line( "mesh.Unlock\tverts=%d indices=%d%s", nVertexCount, nIndexCount, szVerts );
	}
	if ( pOriginal )
		( (void (*)( IMesh *, int, int, MeshDesc_t & ))pOriginal )( self, nVertexCount, nIndexCount, desc );
}

void HookMesh( IMesh *pMesh )
{
	if ( !pMesh || g_nMeshHooks >= (int)ARRAYSIZE( g_MeshHooks ) )
		return;
	void **pVtable = *(void ***)pMesh;
	for ( int i = 0; i < g_nMeshHooks; i++ )
	{
		if ( g_MeshHooks[ i ].pVtable == pVtable )
			return;
	}
	VTableSlotPatch::Patch patch;
	if ( !VTableSlotPatch::Install( pMesh, &IMesh::UnlockMesh, (void *)&H_UnlockMesh, &patch ) )
		return;
	g_MeshHooks[ g_nMeshHooks ].pVtable = pVtable;
	g_MeshHooks[ g_nMeshHooks ].pOriginal = patch.pOriginal;
	g_nMeshHooks++;
	g_Patches.AddToTail( patch );
}

void H_Bind( IMatRenderContext *self, IMaterial *pMaterial, void *pProxyData )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.Bind\tmaterial=%s", MaterialName( pMaterial ) );
	SS_CALL( Bind, void (*)( IMatRenderContext *, IMaterial *, void * ) )( self, pMaterial, pProxyData );
}

IMesh *H_GetDynamicMesh( IMatRenderContext *self, bool bBuffered, IMesh *pVertexOverride, IMesh *pIndexOverride, IMaterial *pAutoBind )
{
	const bool bClient = SS_FROM_CLIENT();
	IMesh *pMesh = SS_CALL( GetDynamicMesh, IMesh *(*)( IMatRenderContext *, bool, IMesh *, IMesh *, IMaterial * ) )( self, bBuffered, pVertexOverride, pIndexOverride, pAutoBind );
	if ( bClient )
	{
		Line( "ctx.GetDynamicMesh\tbuffered=%d override=%d,%d material=%s", bBuffered ? 1 : 0, pVertexOverride ? 1 : 0,
		      pIndexOverride ? 1 : 0, MaterialName( pAutoBind ) );
		HookMesh( pMesh );
	}
	return pMesh;
}

bool H_EnableClipping( IMatRenderContext *self, bool bEnable )
{
	const bool bClient = SS_FROM_CLIENT();
	const bool bWas = SS_CALL( EnableClipping, bool (*)( IMatRenderContext *, bool ) )( self, bEnable );
	if ( bClient )
		Line( "ctx.EnableClipping\t%d was=%d", bEnable ? 1 : 0, bWas ? 1 : 0 );
	return bWas;
}

OcclusionQueryObjectHandle_t H_CreateOcclusionQueryObject( IMatRenderContext *self )
{
	const bool bClient = SS_FROM_CLIENT();
	OcclusionQueryObjectHandle_t h = SS_CALL( CreateOcclusionQueryObject, OcclusionQueryObjectHandle_t (*)( IMatRenderContext * ) )( self );
	if ( bClient )
		Line( "ctx.CreateOcclusionQueryObject\t#%d", Query( h ) );
	return h;
}

void H_DestroyOcclusionQueryObject( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.DestroyOcclusionQueryObject\t#%d", Query( h ) );
	SS_CALL( DestroyOcclusionQueryObject, void (*)( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
}

void H_ResetOcclusionQueryObject( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ResetOcclusionQueryObject\t#%d", Query( h ) );
	SS_CALL( ResetOcclusionQueryObject, void (*)( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
}

void H_BeginOcclusionQueryDrawing( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.BeginOcclusionQueryDrawing\t#%d", Query( h ) );
	SS_CALL( BeginOcclusionQueryDrawing, void (*)( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
}

void H_EndOcclusionQueryDrawing( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.EndOcclusionQueryDrawing\t#%d", Query( h ) );
	SS_CALL( EndOcclusionQueryDrawing, void (*)( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
}

int H_OcclusionQuery_GetNumPixelsRendered( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	const bool bClient = SS_FROM_CLIENT();
	const int n = SS_CALL( OcclusionQuery_GetNumPixelsRendered, int (*)( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
	if ( bClient )
		Line( "q\tOcclusionQuery_GetNumPixelsRendered\t#%d=%d", Query( h ), n );
	return n;
}

float H_ComputePixelDiameterOfSphere( IMatRenderContext *self, const Vector &origin, float flRadius )
{
	const bool bClient = SS_FROM_CLIENT();
	const float f = SS_CALL( ComputePixelDiameterOfSphere, float (*)( IMatRenderContext *, const Vector &, float ) )( self, origin, flRadius );
	if ( bClient )
		Line( "q\tComputePixelDiameterOfSphere\t%.9g,%.9g,%.9g r=%.9g=%.9g", origin.x, origin.y, origin.z, flRadius, f );
	return f;
}

#define SS_INSTALL( object, pmf, name ) \
	bOk = bOk && Install( object, pmf, (void *)&H_##name, &g_Originals[ kHook_##name ] )

bool InstallAll( IMatRenderContext *pContext )
{
	bool bOk = true;

	SS_INSTALL( render, &IVRenderView::DrawBrushModel, DrawBrushModel );
	SS_INSTALL( render, &IVRenderView::DrawIdentityBrushModel, DrawIdentityBrushModel );
	SS_INSTALL( render, &IVRenderView::Draw3DDebugOverlays, Draw3DDebugOverlays );
	SS_INSTALL( render, &IVRenderView::SetBlend, SetBlend );
	SS_INSTALL( render, &IVRenderView::SetColorModulation, SetColorModulation );
	SS_INSTALL( render, &IVRenderView::SceneBegin, SceneBegin );
	SS_INSTALL( render, &IVRenderView::SceneEnd, SceneEnd );
	SS_INSTALL( render, &IVRenderView::GetVisibleFogVolume, GetVisibleFogVolume );
	SS_INSTALL( render, &IVRenderView::CreateWorldList, CreateWorldList );
	SS_INSTALL( render, &IVRenderView::BuildWorldLists, BuildWorldLists );
	SS_INSTALL( render, &IVRenderView::DrawWorldLists, DrawWorldLists );
	SS_INSTALL( render, &IVRenderView::DrawLights, DrawLights );
	SS_INSTALL( render, &IVRenderView::DrawMaskEntities, DrawMaskEntities );
	SS_INSTALL( render, &IVRenderView::DrawTranslucentSurfaces, DrawTranslucentSurfaces );
	SS_INSTALL( render, &IVRenderView::LeafContainsTranslucentSurfaces, LeafContainsTranslucentSurfaces );
	SS_INSTALL( render, &IVRenderView::ViewSetupVis, ViewSetupVis );
	SS_INSTALL( render, &IVRenderView::ViewSetupVisEx, ViewSetupVisEx );
	SS_INSTALL( render, &IVRenderView::VguiPaint, VguiPaint );
	SS_INSTALL( render, &IVRenderView::ViewDrawFade, ViewDrawFade );
	SS_INSTALL( render, &IVRenderView::SetFogVolumeState, SetFogVolumeState );
	SS_INSTALL( render, &IVRenderView::VGui_Paint, VGui_Paint );
	SS_INSTALL( render, static_cast<void ( IVRenderView::* )( const CViewSetup &, int, ITexture *, Frustum )>( &IVRenderView::Push3DView ), Push3DView );
	SS_INSTALL( render, static_cast<void ( IVRenderView::* )( const CViewSetup &, int, ITexture *, Frustum, ITexture * )>( &IVRenderView::Push3DView ), Push3DViewDepth );
	SS_INSTALL( render, &IVRenderView::Push2DView, Push2DView );
	SS_INSTALL( render, &IVRenderView::PopView, PopView );
	SS_INSTALL( render, &IVRenderView::SetMainView, SetMainView );
	SS_INSTALL( render, &IVRenderView::OverrideViewFrustum, OverrideViewFrustum );
	SS_INSTALL( render, &IVRenderView::UpdateBrushModelLightmap, UpdateBrushModelLightmap );
	SS_INSTALL( render, &IVRenderView::BeginUpdateLightmaps, BeginUpdateLightmaps );
	SS_INSTALL( render, &IVRenderView::EndUpdateLightmaps, EndUpdateLightmaps );
	SS_INSTALL( render, &IVRenderView::DrawBrushModelEx, DrawBrushModelEx );
	SS_INSTALL( render, &IVRenderView::DrawBrushModelShadowDepth, DrawBrushModelShadowDepth );

	SS_INSTALL( pContext, &IMatRenderContext::Flush, Flush );
	SS_INSTALL( pContext, &IMatRenderContext::SetRenderTarget, SetRenderTarget );
	SS_INSTALL( pContext, &IMatRenderContext::DepthRange, DepthRange );
	SS_INSTALL( pContext, &IMatRenderContext::ClearBuffers, ClearBuffers );
	SS_INSTALL( pContext, &IMatRenderContext::MatrixMode, MatrixMode );
	SS_INSTALL( pContext, &IMatRenderContext::PushMatrix, PushMatrix );
	SS_INSTALL( pContext, &IMatRenderContext::PopMatrix, PopMatrix );
	SS_INSTALL( pContext, &IMatRenderContext::Viewport, Viewport );
	SS_INSTALL( pContext, &IMatRenderContext::SetHeightClipMode, SetHeightClipMode );
	SS_INSTALL( pContext, &IMatRenderContext::SetHeightClipZ, SetHeightClipZ );
	SS_INSTALL( pContext, &IMatRenderContext::FogMode, FogMode );
	SS_INSTALL( pContext, &IMatRenderContext::FogStart, FogStart );
	SS_INSTALL( pContext, &IMatRenderContext::FogEnd, FogEnd );
	SS_INSTALL( pContext, &IMatRenderContext::SetFogZ, SetFogZ );
	SS_INSTALL( pContext, &IMatRenderContext::FogColor3fv, FogColor3fv );
	SS_INSTALL( pContext, &IMatRenderContext::ClearColor3ub, ClearColor3ub );
	SS_INSTALL( pContext, &IMatRenderContext::ClearColor4ub, ClearColor4ub );
	SS_INSTALL( pContext, &IMatRenderContext::OverrideDepthEnable, OverrideDepthEnable );
	SS_INSTALL( pContext, &IMatRenderContext::DrawScreenSpaceQuad, DrawScreenSpaceQuad );
	SS_INSTALL( pContext, &IMatRenderContext::DrawScreenSpaceRectangle, DrawScreenSpaceRectangle );
	SS_INSTALL( pContext, static_cast<void ( IMatRenderContext::* )()>( &IMatRenderContext::PushRenderTargetAndViewport ), PushRTVP0 );
	SS_INSTALL( pContext, static_cast<void ( IMatRenderContext::* )( ITexture * )>( &IMatRenderContext::PushRenderTargetAndViewport ), PushRTVP1 );
	SS_INSTALL( pContext, static_cast<void ( IMatRenderContext::* )( ITexture *, int, int, int, int )>( &IMatRenderContext::PushRenderTargetAndViewport ), PushRTVP5 );
	SS_INSTALL( pContext, static_cast<void ( IMatRenderContext::* )( ITexture *, ITexture *, int, int, int, int )>( &IMatRenderContext::PushRenderTargetAndViewport ), PushRTVP6 );
	SS_INSTALL( pContext, &IMatRenderContext::PopRenderTargetAndViewport, PopRenderTargetAndViewport );
	SS_INSTALL( pContext, &IMatRenderContext::CopyRenderTargetToTextureEx, CopyRenderTargetToTextureEx );
	SS_INSTALL( pContext, &IMatRenderContext::SetIntRenderingParameter, SetIntRenderingParameter );
	SS_INSTALL( pContext, &IMatRenderContext::PushCustomClipPlane, PushCustomClipPlane );
	SS_INSTALL( pContext, &IMatRenderContext::PopCustomClipPlane, PopCustomClipPlane );
	SS_INSTALL( pContext, &IMatRenderContext::ClearBuffersObeyStencil, ClearBuffersObeyStencil );
	SS_INSTALL( pContext, &IMatRenderContext::ResetToneMappingScale, ResetToneMappingScale );
	SS_INSTALL( pContext, &IMatRenderContext::SetGoalToneMappingScale, SetGoalToneMappingScale );
	SS_INSTALL( pContext, &IMatRenderContext::TurnOnToneMapping, TurnOnToneMapping );
	SS_INSTALL( pContext, &IMatRenderContext::SetToneMappingScaleLinear, SetToneMappingScaleLinear );
	SS_INSTALL( pContext, &IMatRenderContext::FogMaxDensity, FogMaxDensity );
	SS_INSTALL( pContext, &IMatRenderContext::SetFullScreenDepthTextureValidityFlag, SetFullScreenDepthTextureValidityFlag );
	SS_INSTALL( pContext, &IMatRenderContext::OverrideAlphaWriteEnable, OverrideAlphaWriteEnable );
	SS_INSTALL( pContext, &IMatRenderContext::OverrideColorWriteEnable, OverrideColorWriteEnable );
	SS_INSTALL( pContext, &IMatRenderContext::FogRadial, FogRadial );
	SS_INSTALL( pContext, &IMatRenderContext::SetFrameBufferCopyTexture, SetFrameBufferCopyTexture );

	SS_INSTALL( modelrender, &IVModelRender::ForcedMaterialOverride, ForcedMaterialOverride );
	SS_INSTALL( modelrender, &IVModelRender::DrawModelEx, DrawModelEx );
	SS_INSTALL( modelrender, &IVModelRender::DrawModelExStaticProp, DrawModelExStaticProp );
	SS_INSTALL( modelrender, &IVModelRender::DrawModelExecute, DrawModelExecute );
	SS_INSTALL( modelrender, &IVModelRender::DrawStaticPropArrayFast, DrawStaticPropArrayFast );

	SS_INSTALL( staticpropmgr, &IStaticPropMgrClient::ComputePropOpacity, ComputePropOpacity );
	SS_INSTALL( staticpropmgr, &IStaticPropMgrClient::DrawStaticProps, DrawStaticProps );

	SS_INSTALL( engine, &IVEngineClient::Sound_ExtraUpdate, Sound_ExtraUpdate );
	SS_INSTALL( engine, &IVEngineClient::DrawPortals, DrawPortals );
	SS_INSTALL( engine, &IVEngineClient::IsSkyboxVisibleFromPoint, IsSkyboxVisibleFromPoint );
	SS_INSTALL( engine, &IVEngineClient::GrabPreColorCorrectedFrame, GrabPreColorCorrectedFrame );

	SS_INSTALL( g_pClientShadowMgr, &IClientShadowMgr::PreRender, PreRender );
	SS_INSTALL( g_pClientShadowMgr, &IClientShadowMgr::AdvanceFrame, AdvanceFrame );
	SS_INSTALL( g_pClientShadowMgr, &IClientShadowMgr::ComputeShadowTextures, ComputeShadowTextures );
	SS_INSTALL( g_pClientShadowMgr, &IClientShadowMgr::ComputeShadowDepthTextures, ComputeShadowDepthTextures );
	SS_INSTALL( g_pClientShadowMgr, &IClientShadowMgr::UnlockAllShadowDepthTextures, UnlockAllShadowDepthTextures );

	if ( g_bMeshes )
	{
		SS_INSTALL( pContext, &IMatRenderContext::Bind, Bind );
		SS_INSTALL( pContext, &IMatRenderContext::GetDynamicMesh, GetDynamicMesh );
		SS_INSTALL( pContext, &IMatRenderContext::EnableClipping, EnableClipping );
		SS_INSTALL( pContext, &IMatRenderContext::CreateOcclusionQueryObject, CreateOcclusionQueryObject );
		SS_INSTALL( pContext, &IMatRenderContext::DestroyOcclusionQueryObject, DestroyOcclusionQueryObject );
		SS_INSTALL( pContext, &IMatRenderContext::ResetOcclusionQueryObject, ResetOcclusionQueryObject );
		SS_INSTALL( pContext, &IMatRenderContext::BeginOcclusionQueryDrawing, BeginOcclusionQueryDrawing );
		SS_INSTALL( pContext, &IMatRenderContext::EndOcclusionQueryDrawing, EndOcclusionQueryDrawing );
		SS_INSTALL( pContext, &IMatRenderContext::OcclusionQuery_GetNumPixelsRendered, OcclusionQuery_GetNumPixelsRendered );
		SS_INSTALL( pContext, &IMatRenderContext::ComputePixelDiameterOfSphere, ComputePixelDiameterOfSphere );
	}

	return bOk;
}

} // namespace

bool RenderTrace::Begin()
{
	if ( g_bActive || !render || !modelrender || !staticpropmgr || !engine || !materials || !g_pClientShadowMgr )
		return false;

	if ( !g_pClientBase )
	{
		SSModuleInfo info;
		if ( !SS_ModuleOfAddress( (void *)&RenderTrace::Begin, &info ) )
			return false;
		g_pClientBase = info.pBase;
	}

	g_Text.RemoveAll();
	g_WorldLists.RemoveAll();
	g_nShadowDepth = 0;
	g_Queries.RemoveAll();
	g_nMeshHooks = 0;
	const char *pszMeshes = getenv( "SOURCESHARP_RENDER_TRACE_MESHES" );
	g_bMeshes = pszMeshes && pszMeshes[ 0 ] == '1';

	CMatRenderContextPtr pContext( materials );
	if ( !InstallAll( pContext ) )
	{
		RemoveAll();
		Warning( "[SourceSharp] render trace: a hook could not be installed; no trace\n" );
		return false;
	}

	g_bActive = true;
	return true;
}

void RenderTrace::End()
{
	g_bActive = false;
	RemoveAll();
}

bool RenderTrace::Active()
{
	return g_bActive;
}

const char *RenderTrace::Text( int32_t *pLength )
{
	if ( pLength )
		*pLength = g_Text.Count();
	return g_Text.Count() ? g_Text.Base() : "";
}

void RenderTrace::Note( const char *pszFormat, ... )
{
	if ( !g_bActive )
		return;
	va_list args;
	va_start( args, pszFormat );
	Append( pszFormat, args );
	va_end( args );
}

void RenderTrace::NoteLong( const char *pszPrefix, const char *pszText )
{
	if ( !g_bActive )
		return;
	g_Text.AddMultipleToTail( V_strlen( pszPrefix ), pszPrefix );
	g_Text.AddMultipleToTail( V_strlen( pszText ), pszText );
	g_Text.AddToTail( '\n' );
}
