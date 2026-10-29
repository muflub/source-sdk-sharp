//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// Records, for one frame, every call client.so makes on `render`, the render
// context, `modelrender`, `staticpropmgr` and four render members of `engine`,
// one text line each. The mesh and occlusion-query hooks and the shadow hooks
// are armed only by SOURCESHARP_RENDER_TRACE_MESHES=1 and
// SOURCESHARP_RENDER_TRACE_SHADOWS=1, so a default trace keeps its usual lines.
//
// Each hooked slot in the engine's vtable is overwritten for the frame with a
// hook that logs and forwards. The engine's own calls reach the hooks too, so a
// hook records only when its return address is in client.so. Queries whose
// answer only feeds a decision are not hooked; the three that show which branch
// stock took are `q` lines, which comparisons skip. Pointers print as names or
// first-seen ordinals so two frames' traces compare as text.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "client_entity.h"

#include "materialsystem/imaterialvar.h"
#include "materialsystem/itexture.h"
#include "view_shared.h"

#include "materialsystem/imesh.h"

#include "vphysics_interface.h"
#include "shell_render_trace_client.h"
#include "vtable_slot_patch.h"

#include "ss_fwd.h"

namespace
{

bool g_bActive = false;
CUtlVector<char> g_Text;
CUtlVector<const void *> g_WorldLists; // a world list's ordinal is its index here
const void *g_pClientBase = NULL;
const void *g_pShadowsBase = NULL; // libss_fwd_client, whose ShadowNative carries out the shadow manager's calls

// Inside the shadow manager's frame work (its render-to-texture refresh, which
// differs frame to frame) lines are marked `~`.
int g_nShadowDepth = 0;

void Append( const char *pszFormat, va_list args )
{
	if ( g_nShadowDepth > 0 )
		g_Text.AddMultipleToTail( 2, "~\t" );
	char szLine[1024];
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

// True when the call came from client.so (dladdr on the return address), or, for
// the shadow manager's calls, from libss_fwd_client's shadow table, which carries
// them out. The shadow hooks take that library's calls anywhere, as the manager
// is their only caller.
bool FromClient( const void *pReturn, bool bShadowHook )
{
	if ( !g_bActive )
		return false;
	SSModuleInfo info;
	if ( !SS_ModuleOfAddress( pReturn, &info ) )
		return false;
	if ( info.pBase == g_pClientBase )
		return true;
	return g_pShadowsBase && info.pBase == g_pShadowsBase && ( bShadowHook || g_nShadowDepth > 0 );
}

#define SS_FROM_CLIENT() FromClient( SS_RETURN_ADDRESS(), false )
#define SS_FROM_SHADOWS() FromClient( SS_RETURN_ADDRESS(), true )

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
				v.fov, v.fovViewmodel, v.origin.x, v.origin.y, v.origin.z, v.angles.x, v.angles.y, v.angles.z, v.zNear,
				v.zFar, v.zNearViewmodel, v.zFarViewmodel, v.m_bRenderToSubrectOfLargerScreen ? 1 : 0,
				v.m_flAspectRatio, v.m_bOffCenter ? 1 : 0, v.m_flOffCenterTop, v.m_flOffCenterBottom,
				v.m_flOffCenterLeft, v.m_flOffCenterRight, v.m_bDoBloomAndToneMapping ? 1 : 0,
				v.m_bCacheFullSceneState ? 1 : 0, v.m_bViewToProjectionOverride ? 1 : 0 );
}

// Every slot Begin overwrote, in order.
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

// Each hook logs when the call is the client's and forwards to the slot's
// value when Begin ran.
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
	// The engine shadow manager's hooks (SOURCESHARP_RENDER_TRACE_SHADOWS=1).
	kHook_CreateShadowEx,
	kHook_DestroyShadow,
	kHook_SetShadowMaterial,
	kHook_ProjectShadow,
	kHook_ProjectFlashlight,
	kHook_GetInfo,
	kHook_GetFlashlightFrustum,
	kHook_AddShadowToBrushModel,
	kHook_RemoveAllShadowsFromBrushModel,
	kHook_SetShadowTexCoord,
	kHook_AddShadowToModel,
	kHook_RemoveAllShadowsFromModel,
	kHook_ClearExtraClipPlanes,
	kHook_AddExtraClipPlane,
	kHook_EnableShadow,
	kHook_SetFalloffBias,
	kHook_UpdateFlashlightState,
	kHook_AddFlashlightRenderable,
	kHook_SetFlashlightDepthTexture,
	kHook_GetFlashlightState,
	kHook_SetFlashlightRenderState,
	kHook_AddShadowToStaticProp,
	kHook_RemoveAllShadowsFromStaticProp,
	kHookCount
};

// Each hooked slot's original, by hook: one table rather than one static per hook.
void *g_Originals[kHookCount];

#define SS_CALL( name, type ) ( (type)g_Originals[kHook_##name] )

// IVRenderView ---------------------------------------------------------------
void H_DrawBrushModel( IVRenderView *self, IClientEntity *pEnt, model_t *pModel, const Vector &origin,
					   const QAngle &angles, bool bSort )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawBrushModel\tmodel=%s sort=%d", modelinfo->GetModelName( pModel ), bSort ? 1 : 0 );
	SS_CALL( DrawBrushModel,
			 void ( * )( IVRenderView *, IClientEntity *, model_t *, const Vector &, const QAngle &, bool ) )(
		self, pEnt, pModel, origin, angles, bSort );
}

void H_DrawIdentityBrushModel( IVRenderView *self, IWorldRenderList *pList, model_t *pModel )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawIdentityBrushModel\tlist=%d model=%s", WorldList( pList ),
			  modelinfo->GetModelName( pModel ) );
	SS_CALL( DrawIdentityBrushModel, void ( * )( IVRenderView *, IWorldRenderList *, model_t * ) )( self, pList,
																									pModel );
}

void H_Draw3DDebugOverlays( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.Draw3DDebugOverlays" );
	SS_CALL( Draw3DDebugOverlays, void ( * )( IVRenderView * ) )( self );
}

void H_SetBlend( IVRenderView *self, float flBlend )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SetBlend\t%.9g", flBlend );
	SS_CALL( SetBlend, void ( * )( IVRenderView *, float ) )( self, flBlend );
}

void H_SetColorModulation( IVRenderView *self, const float *pBlend )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SetColorModulation\t%.9g,%.9g,%.9g", pBlend[0], pBlend[1], pBlend[2] );
	SS_CALL( SetColorModulation, void ( * )( IVRenderView *, const float * ) )( self, pBlend );
}

void H_SceneBegin( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SceneBegin" );
	SS_CALL( SceneBegin, void ( * )( IVRenderView * ) )( self );
}

void H_SceneEnd( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SceneEnd" );
	SS_CALL( SceneEnd, void ( * )( IVRenderView * ) )( self );
}

void H_GetVisibleFogVolume( IVRenderView *self, const Vector &eye, VisibleFogVolumeInfo_t *pInfo )
{
	bool bClient = SS_FROM_CLIENT();
	SS_CALL( GetVisibleFogVolume, void ( * )( IVRenderView *, const Vector &, VisibleFogVolumeInfo_t * ) )( self, eye,
																											pInfo );
	if ( bClient )
		Line(
			"q\trender.GetVisibleFogVolume\teye=%.9g,%.9g,%.9g volume=%d leaf=%d infog=%d dist=%.9g height=%.9g material=%s",
			eye.x, eye.y, eye.z, pInfo->m_nVisibleFogVolume, pInfo->m_nVisibleFogVolumeLeaf,
			pInfo->m_bEyeInFogVolume ? 1 : 0, pInfo->m_flDistanceToWater, pInfo->m_flWaterHeight,
			MaterialName( pInfo->m_pFogVolumeMaterial ) );
}

IWorldRenderList *H_CreateWorldList( IVRenderView *self )
{
	bool bClient = SS_FROM_CLIENT();
	IWorldRenderList *pList = SS_CALL( CreateWorldList, IWorldRenderList * (*)(IVRenderView *))( self );
	if ( bClient )
		Line( "render.CreateWorldList\tlist=%d", WorldList( pList ) );
	return pList;
}

void H_BuildWorldLists( IVRenderView *self, IWorldRenderList *pList, WorldListInfo_t *pInfo, int iForceViewLeaf,
						const VisOverrideData_t *pVisData, bool bShadowDepth, float *pReflectionWaterHeight )
{
	bool bClient = SS_FROM_CLIENT();
	SS_CALL( BuildWorldLists,
			 void ( * )( IVRenderView *, IWorldRenderList *, WorldListInfo_t *, int, const VisOverrideData_t *, bool,
						 float * ) )( self, pList, pInfo, iForceViewLeaf, pVisData, bShadowDepth,
									  pReflectionWaterHeight );
	if ( bClient )
	{
		char szHeight[32] = "-";
		if ( pReflectionWaterHeight )
			V_snprintf( szHeight, sizeof( szHeight ), "%.9g", *pReflectionWaterHeight );
		Line(
			"render.BuildWorldLists\tlist=%d forceleaf=%d visdata=%d shadowdepth=%d reflheight=%s => fogvolume=%d leaves=%d",
			WorldList( pList ), iForceViewLeaf, pVisData ? 1 : 0, bShadowDepth ? 1 : 0, szHeight,
			pInfo ? pInfo->m_ViewFogVolume : 0, pInfo ? pInfo->m_LeafCount : 0 );
	}
}

void H_DrawWorldLists( IVRenderView *self, IWorldRenderList *pList, unsigned long flags, float waterZAdjust )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawWorldLists\tlist=%d flags=0x%lx waterz=%.9g", WorldList( pList ), flags, waterZAdjust );
	SS_CALL( DrawWorldLists, void ( * )( IVRenderView *, IWorldRenderList *, unsigned long, float ) )(
		self, pList, flags, waterZAdjust );
}

void H_DrawLights( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawLights" );
	SS_CALL( DrawLights, void ( * )( IVRenderView * ) )( self );
}

void H_DrawMaskEntities( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawMaskEntities" );
	SS_CALL( DrawMaskEntities, void ( * )( IVRenderView * ) )( self );
}

void H_DrawTranslucentSurfaces( IVRenderView *self, IWorldRenderList *pList, int sortIndex, unsigned long flags,
								bool bShadowDepth )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawTranslucentSurfaces\tlist=%d leaf=%d flags=0x%lx shadowdepth=%d", WorldList( pList ),
			  sortIndex, flags, bShadowDepth ? 1 : 0 );
	SS_CALL( DrawTranslucentSurfaces, void ( * )( IVRenderView *, IWorldRenderList *, int, unsigned long, bool ) )(
		self, pList, sortIndex, flags, bShadowDepth );
}

bool H_LeafContainsTranslucentSurfaces( IVRenderView *self, IWorldRenderList *pList, int sortIndex,
										unsigned long flags )
{
	bool bClient = SS_FROM_CLIENT();
	bool bAnswer = SS_CALL( LeafContainsTranslucentSurfaces,
							bool ( * )( IVRenderView *, IWorldRenderList *, int, unsigned long ) )( self, pList,
																									sortIndex, flags );
	if ( bClient )
		Line( "q\trender.LeafContainsTranslucentSurfaces\tlist=%d leaf=%d flags=0x%lx => %d", WorldList( pList ),
			  sortIndex, flags, bAnswer ? 1 : 0 );
	return bAnswer;
}

void H_ViewSetupVis( IVRenderView *self, bool bNoVis, int nOrigins, const Vector origin[] )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.ViewSetupVis\tnovis=%d n=%d origin=%.9g,%.9g,%.9g", bNoVis ? 1 : 0, nOrigins,
			  nOrigins > 0 ? origin[0].x : 0.0f, nOrigins > 0 ? origin[0].y : 0.0f, nOrigins > 0 ? origin[0].z : 0.0f );
	SS_CALL( ViewSetupVis, void ( * )( IVRenderView *, bool, int, const Vector * ) )( self, bNoVis, nOrigins, origin );
}

void H_ViewSetupVisEx( IVRenderView *self, bool bNoVis, int nOrigins, const Vector origin[], unsigned int &returnFlags )
{
	bool bClient = SS_FROM_CLIENT();
	SS_CALL( ViewSetupVisEx, void ( * )( IVRenderView *, bool, int, const Vector *, unsigned int & ) )(
		self, bNoVis, nOrigins, origin, returnFlags );
	if ( bClient )
		Line( "render.ViewSetupVisEx\tnovis=%d n=%d origin=%.9g,%.9g,%.9g => flags=0x%x", bNoVis ? 1 : 0, nOrigins,
			  nOrigins > 0 ? origin[0].x : 0.0f, nOrigins > 0 ? origin[0].y : 0.0f, nOrigins > 0 ? origin[0].z : 0.0f,
			  returnFlags );
}

void H_VguiPaint( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.VguiPaint" );
	SS_CALL( VguiPaint, void ( * )( IVRenderView * ) )( self );
}

void H_ViewDrawFade( IVRenderView *self, byte *pColor, IMaterial *pMaterial )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.ViewDrawFade\tcolor=%d,%d,%d,%d material=%s", pColor[0], pColor[1], pColor[2], pColor[3],
			  MaterialName( pMaterial ) );
	SS_CALL( ViewDrawFade, void ( * )( IVRenderView *, byte *, IMaterial * ) )( self, pColor, pMaterial );
}

void H_SetFogVolumeState( IVRenderView *self, int nVisibleFogVolume, bool bUseHeightFog )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SetFogVolumeState\tvolume=%d heightfog=%d", nVisibleFogVolume, bUseHeightFog ? 1 : 0 );
	SS_CALL( SetFogVolumeState, void ( * )( IVRenderView *, int, bool ) )( self, nVisibleFogVolume, bUseHeightFog );
}

void H_VGui_Paint( IVRenderView *self, int mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.VGui_Paint\tmode=0x%x", mode );
	SS_CALL( VGui_Paint, void ( * )( IVRenderView *, int ) )( self, mode );
}

void H_Push3DView( IVRenderView *self, const CViewSetup &view, int nFlags, ITexture *pRenderTarget,
				   Frustum frustumPlanes )
{
	if ( SS_FROM_CLIENT() )
	{
		char szView[768];
		ViewText( view, szView, sizeof( szView ) );
		Line( "render.Push3DView\tclear=0x%x rt=%s depth=- %s", nFlags, TextureName( pRenderTarget ), szView );
	}
	SS_CALL( Push3DView, void ( * )( IVRenderView *, const CViewSetup &, int, ITexture *, VPlane * ) )(
		self, view, nFlags, pRenderTarget, frustumPlanes );
}

void H_Push3DViewDepth( IVRenderView *self, const CViewSetup &view, int nFlags, ITexture *pRenderTarget,
						Frustum frustumPlanes, ITexture *pDepthTexture )
{
	if ( SS_FROM_CLIENT() )
	{
		char szView[768];
		ViewText( view, szView, sizeof( szView ) );
		Line( "render.Push3DView\tclear=0x%x rt=%s depth=%s %s", nFlags, TextureName( pRenderTarget ),
			  TextureName( pDepthTexture ), szView );
	}
	SS_CALL( Push3DViewDepth, void ( * )( IVRenderView *, const CViewSetup &, int, ITexture *, VPlane *, ITexture * ) )(
		self, view, nFlags, pRenderTarget, frustumPlanes, pDepthTexture );
}

void H_Push2DView( IVRenderView *self, const CViewSetup &view, int nFlags, ITexture *pRenderTarget,
				   Frustum frustumPlanes )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.Push2DView\tclear=0x%x rt=%s xywh=%d,%d,%d,%d", nFlags, TextureName( pRenderTarget ), view.x,
			  view.y, view.width, view.height );
	SS_CALL( Push2DView, void ( * )( IVRenderView *, const CViewSetup &, int, ITexture *, VPlane * ) )(
		self, view, nFlags, pRenderTarget, frustumPlanes );
}

void H_PopView( IVRenderView *self, Frustum frustumPlanes )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.PopView" );
	SS_CALL( PopView, void ( * )( IVRenderView *, VPlane * ) )( self, frustumPlanes );
}

void H_SetMainView( IVRenderView *self, const Vector &origin, const QAngle &angles )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.SetMainView\torigin=%.9g,%.9g,%.9g angles=%.9g,%.9g,%.9g", origin.x, origin.y, origin.z, angles.x,
			  angles.y, angles.z );
	SS_CALL( SetMainView, void ( * )( IVRenderView *, const Vector &, const QAngle & ) )( self, origin, angles );
}

void H_OverrideViewFrustum( IVRenderView *self, Frustum custom )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.OverrideViewFrustum" );
	SS_CALL( OverrideViewFrustum, void ( * )( IVRenderView *, VPlane * ) )( self, custom );
}

void H_UpdateBrushModelLightmap( IVRenderView *self, model_t *pModel, IClientRenderable *pRenderable )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.UpdateBrushModelLightmap\tmodel=%s", modelinfo->GetModelName( pModel ) );
	SS_CALL( UpdateBrushModelLightmap, void ( * )( IVRenderView *, model_t *, IClientRenderable * ) )( self, pModel,
																									   pRenderable );
}

void H_BeginUpdateLightmaps( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.BeginUpdateLightmaps" );
	SS_CALL( BeginUpdateLightmaps, void ( * )( IVRenderView * ) )( self );
}

void H_EndUpdateLightmaps( IVRenderView *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.EndUpdateLightmaps" );
	SS_CALL( EndUpdateLightmaps, void ( * )( IVRenderView * ) )( self );
}

void H_DrawBrushModelEx( IVRenderView *self, IClientEntity *pEnt, model_t *pModel, const Vector &origin,
						 const QAngle &angles, DrawBrushModelMode_t mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawBrushModelEx\tmodel=%s mode=%d", modelinfo->GetModelName( pModel ), (int)mode );
	SS_CALL( DrawBrushModelEx,
			 void ( * )( IVRenderView *, IClientEntity *, model_t *, const Vector &, const QAngle &,
						 DrawBrushModelMode_t ) )( self, pEnt, pModel, origin, angles, mode );
}

void H_DrawBrushModelShadowDepth( IVRenderView *self, IClientEntity *pEnt, model_t *pModel, const Vector &origin,
								  const QAngle &angles, ERenderDepthMode mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "render.DrawBrushModelShadowDepth\tmodel=%s mode=%d", modelinfo->GetModelName( pModel ), (int)mode );
	SS_CALL( DrawBrushModelShadowDepth,
			 void ( * )( IVRenderView *, IClientEntity *, model_t *, const Vector &, const QAngle &,
						 ERenderDepthMode ) )( self, pEnt, pModel, origin, angles, mode );
}

// IMatRenderContext ----------------------------------------------------------
void H_Flush( IMatRenderContext *self, bool bHardware )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.Flush\thardware=%d", bHardware ? 1 : 0 );
	SS_CALL( Flush, void ( * )( IMatRenderContext *, bool ) )( self, bHardware );
}

void H_SetRenderTarget( IMatRenderContext *self, ITexture *pTexture )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetRenderTarget\trt=%s", TextureName( pTexture ) );
	SS_CALL( SetRenderTarget, void ( * )( IMatRenderContext *, ITexture * ) )( self, pTexture );
}

void H_DepthRange( IMatRenderContext *self, float flNear, float flFar )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.DepthRange\t%.9g,%.9g", flNear, flFar );
	SS_CALL( DepthRange, void ( * )( IMatRenderContext *, float, float ) )( self, flNear, flFar );
}

void H_ClearBuffers( IMatRenderContext *self, bool bColor, bool bDepth, bool bStencil )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ClearBuffers\tcolor=%d depth=%d stencil=%d", bColor ? 1 : 0, bDepth ? 1 : 0, bStencil ? 1 : 0 );
	SS_CALL( ClearBuffers, void ( * )( IMatRenderContext *, bool, bool, bool ) )( self, bColor, bDepth, bStencil );
}

void H_MatrixMode( IMatRenderContext *self, MaterialMatrixMode_t mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.MatrixMode\t%d", (int)mode );
	SS_CALL( MatrixMode, void ( * )( IMatRenderContext *, MaterialMatrixMode_t ) )( self, mode );
}

void H_PushMatrix( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushMatrix" );
	SS_CALL( PushMatrix, void ( * )( IMatRenderContext * ) )( self );
}

void H_PopMatrix( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PopMatrix" );
	SS_CALL( PopMatrix, void ( * )( IMatRenderContext * ) )( self );
}

void H_Viewport( IMatRenderContext *self, int x, int y, int w, int h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.Viewport\t%d,%d,%d,%d", x, y, w, h );
	SS_CALL( Viewport, void ( * )( IMatRenderContext *, int, int, int, int ) )( self, x, y, w, h );
}

void H_SetHeightClipMode( IMatRenderContext *self, MaterialHeightClipMode_t mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetHeightClipMode\t%d", (int)mode );
	SS_CALL( SetHeightClipMode, void ( * )( IMatRenderContext *, MaterialHeightClipMode_t ) )( self, mode );
}

void H_SetHeightClipZ( IMatRenderContext *self, float z )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetHeightClipZ\t%.9g", z );
	SS_CALL( SetHeightClipZ, void ( * )( IMatRenderContext *, float ) )( self, z );
}

void H_FogMode( IMatRenderContext *self, MaterialFogMode_t mode )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogMode\t%d", (int)mode );
	SS_CALL( FogMode, void ( * )( IMatRenderContext *, MaterialFogMode_t ) )( self, mode );
}

void H_FogStart( IMatRenderContext *self, float f )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogStart\t%.9g", f );
	SS_CALL( FogStart, void ( * )( IMatRenderContext *, float ) )( self, f );
}

void H_FogEnd( IMatRenderContext *self, float f )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogEnd\t%.9g", f );
	SS_CALL( FogEnd, void ( * )( IMatRenderContext *, float ) )( self, f );
}

void H_SetFogZ( IMatRenderContext *self, float f )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetFogZ\t%.9g", f );
	SS_CALL( SetFogZ, void ( * )( IMatRenderContext *, float ) )( self, f );
}

void H_FogColor3fv( IMatRenderContext *self, const float *pRgb )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogColor3fv\t%.9g,%.9g,%.9g", pRgb[0], pRgb[1], pRgb[2] );
	SS_CALL( FogColor3fv, void ( * )( IMatRenderContext *, const float * ) )( self, pRgb );
}

void H_ClearColor3ub( IMatRenderContext *self, unsigned char r, unsigned char g, unsigned char b )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ClearColor3ub\t%d,%d,%d", r, g, b );
	SS_CALL( ClearColor3ub, void ( * )( IMatRenderContext *, unsigned char, unsigned char, unsigned char ) )( self, r,
																											  g, b );
}

void H_ClearColor4ub( IMatRenderContext *self, unsigned char r, unsigned char g, unsigned char b, unsigned char a )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ClearColor4ub\t%d,%d,%d,%d", r, g, b, a );
	SS_CALL( ClearColor4ub,
			 void ( * )( IMatRenderContext *, unsigned char, unsigned char, unsigned char, unsigned char ) )( self, r,
																											  g, b, a );
}

void H_OverrideDepthEnable( IMatRenderContext *self, bool bEnable, bool bDepthEnable )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.OverrideDepthEnable\t%d,%d", bEnable ? 1 : 0, bDepthEnable ? 1 : 0 );
	SS_CALL( OverrideDepthEnable, void ( * )( IMatRenderContext *, bool, bool ) )( self, bEnable, bDepthEnable );
}

// A screen-space draw's material vars once its proxies bound: one q line per
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
				Line( "q\tmaterial.var\t%s\t%s\tf %.9g", MaterialName( pMaterial ), pVar->GetName(),
					  pVar->GetFloatValue() );
				break;
			case MATERIAL_VAR_TYPE_INT:
				Line( "q\tmaterial.var\t%s\t%s\ti %d", MaterialName( pMaterial ), pVar->GetName(),
					  pVar->GetIntValue() );
				break;
			case MATERIAL_VAR_TYPE_VECTOR:
			{
				float v[4] = { 0, 0, 0, 0 };
				const int n = MIN( MAX( pVar->VectorSize(), 0 ), 4 );
				pVar->GetVecValue( v, n );
				Line( "q\tmaterial.var\t%s\t%s\tv%d %.9g,%.9g,%.9g,%.9g", MaterialName( pMaterial ), pVar->GetName(), n,
					  v[0], v[1], v[2], v[3] );
				break;
			}
			default:
				Line( "q\tmaterial.var\t%s\t%s\ts %s", MaterialName( pMaterial ), pVar->GetName(),
					  pVar->GetStringValue() );
				break;
		}
	}
}

void H_DrawScreenSpaceQuad( IMatRenderContext *self, IMaterial *pMaterial )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.DrawScreenSpaceQuad\tmaterial=%s", MaterialName( pMaterial ) );
	SS_CALL( DrawScreenSpaceQuad, void ( * )( IMatRenderContext *, IMaterial * ) )( self, pMaterial );
	if ( SS_FROM_CLIENT() )
		MaterialVarLines( pMaterial );
}

void H_DrawScreenSpaceRectangle( IMatRenderContext *self, IMaterial *pMaterial, int destx, int desty, int width,
								 int height, float srcx0, float srcy0, float srcx1, float srcy1, int srcw, int srch,
								 void *pClientRenderable, int nXDice, int nYDice )
{
	if ( SS_FROM_CLIENT() )
		Line(
			"ctx.DrawScreenSpaceRectangle\tmaterial=%s dest=%d,%d,%d,%d src=%.9g,%.9g,%.9g,%.9g srcsize=%d,%d dice=%d,%d",
			MaterialName( pMaterial ), destx, desty, width, height, srcx0, srcy0, srcx1, srcy1, srcw, srch, nXDice,
			nYDice );
	SS_CALL( DrawScreenSpaceRectangle,
			 void ( * )( IMatRenderContext *, IMaterial *, int, int, int, int, float, float, float, float, int, int,
						 void *, int, int ) )( self, pMaterial, destx, desty, width, height, srcx0, srcy0, srcx1, srcy1,
											   srcw, srch, pClientRenderable, nXDice, nYDice );
	if ( SS_FROM_CLIENT() )
		MaterialVarLines( pMaterial );
}

void H_PushRTVP0( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushRenderTargetAndViewport" );
	SS_CALL( PushRTVP0, void ( * )( IMatRenderContext * ) )( self );
}

void H_PushRTVP1( IMatRenderContext *self, ITexture *pTexture )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushRenderTargetAndViewport\trt=%s", TextureName( pTexture ) );
	SS_CALL( PushRTVP1, void ( * )( IMatRenderContext *, ITexture * ) )( self, pTexture );
}

void H_PushRTVP5( IMatRenderContext *self, ITexture *pTexture, int x, int y, int w, int h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushRenderTargetAndViewport\trt=%s xywh=%d,%d,%d,%d", TextureName( pTexture ), x, y, w, h );
	SS_CALL( PushRTVP5, void ( * )( IMatRenderContext *, ITexture *, int, int, int, int ) )( self, pTexture, x, y, w,
																							 h );
}

void H_PushRTVP6( IMatRenderContext *self, ITexture *pTexture, ITexture *pDepth, int x, int y, int w, int h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushRenderTargetAndViewport\trt=%s depth=%s xywh=%d,%d,%d,%d", TextureName( pTexture ),
			  TextureName( pDepth ), x, y, w, h );
	SS_CALL( PushRTVP6, void ( * )( IMatRenderContext *, ITexture *, ITexture *, int, int, int, int ) )(
		self, pTexture, pDepth, x, y, w, h );
}

void H_PopRenderTargetAndViewport( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PopRenderTargetAndViewport" );
	SS_CALL( PopRenderTargetAndViewport, void ( * )( IMatRenderContext * ) )( self );
}

void H_CopyRenderTargetToTextureEx( IMatRenderContext *self, ITexture *pTexture, int nRenderTargetID, Rect_t *pSrc,
									Rect_t *pDst )
{
	if ( SS_FROM_CLIENT() )
	{
		char szSrc[64] = "-", szDst[64] = "-";
		if ( pSrc )
			V_snprintf( szSrc, sizeof( szSrc ), "%d,%d,%d,%d", pSrc->x, pSrc->y, pSrc->width, pSrc->height );
		if ( pDst )
			V_snprintf( szDst, sizeof( szDst ), "%d,%d,%d,%d", pDst->x, pDst->y, pDst->width, pDst->height );
		Line( "ctx.CopyRenderTargetToTextureEx\ttexture=%s id=%d src=%s dst=%s", TextureName( pTexture ),
			  nRenderTargetID, szSrc, szDst );
	}
	SS_CALL( CopyRenderTargetToTextureEx, void ( * )( IMatRenderContext *, ITexture *, int, Rect_t *, Rect_t * ) )(
		self, pTexture, nRenderTargetID, pSrc, pDst );
}

void H_SetIntRenderingParameter( IMatRenderContext *self, int parm, int value )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetIntRenderingParameter\t%d=%d", parm, value );
	SS_CALL( SetIntRenderingParameter, void ( * )( IMatRenderContext *, int, int ) )( self, parm, value );
}

void H_PushCustomClipPlane( IMatRenderContext *self, const float *pPlane )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PushCustomClipPlane\t%.9g,%.9g,%.9g,%.9g", pPlane[0], pPlane[1], pPlane[2], pPlane[3] );
	SS_CALL( PushCustomClipPlane, void ( * )( IMatRenderContext *, const float * ) )( self, pPlane );
}

void H_PopCustomClipPlane( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.PopCustomClipPlane" );
	SS_CALL( PopCustomClipPlane, void ( * )( IMatRenderContext * ) )( self );
}

void H_ClearBuffersObeyStencil( IMatRenderContext *self, bool bColor, bool bDepth )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ClearBuffersObeyStencil\tcolor=%d depth=%d", bColor ? 1 : 0, bDepth ? 1 : 0 );
	SS_CALL( ClearBuffersObeyStencil, void ( * )( IMatRenderContext *, bool, bool ) )( self, bColor, bDepth );
}

void H_ResetToneMappingScale( IMatRenderContext *self, float flScale )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ResetToneMappingScale\t%.9g", flScale );
	SS_CALL( ResetToneMappingScale, void ( * )( IMatRenderContext *, float ) )( self, flScale );
}

void H_SetGoalToneMappingScale( IMatRenderContext *self, float flScale )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetGoalToneMappingScale\t%.9g", flScale );
	SS_CALL( SetGoalToneMappingScale, void ( * )( IMatRenderContext *, float ) )( self, flScale );
}

void H_TurnOnToneMapping( IMatRenderContext *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.TurnOnToneMapping" );
	SS_CALL( TurnOnToneMapping, void ( * )( IMatRenderContext * ) )( self );
}

void H_SetToneMappingScaleLinear( IMatRenderContext *self, const Vector &scale )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetToneMappingScaleLinear\t%.9g,%.9g,%.9g", scale.x, scale.y, scale.z );
	SS_CALL( SetToneMappingScaleLinear, void ( * )( IMatRenderContext *, const Vector & ) )( self, scale );
}

void H_FogMaxDensity( IMatRenderContext *self, float f )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogMaxDensity\t%.9g", f );
	SS_CALL( FogMaxDensity, void ( * )( IMatRenderContext *, float ) )( self, f );
}

void H_SetFullScreenDepthTextureValidityFlag( IMatRenderContext *self, bool bValid )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetFullScreenDepthTextureValidityFlag\t%d", bValid ? 1 : 0 );
	SS_CALL( SetFullScreenDepthTextureValidityFlag, void ( * )( IMatRenderContext *, bool ) )( self, bValid );
}

void H_OverrideAlphaWriteEnable( IMatRenderContext *self, bool bEnable, bool bAlpha )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.OverrideAlphaWriteEnable\t%d,%d", bEnable ? 1 : 0, bAlpha ? 1 : 0 );
	SS_CALL( OverrideAlphaWriteEnable, void ( * )( IMatRenderContext *, bool, bool ) )( self, bEnable, bAlpha );
}

void H_OverrideColorWriteEnable( IMatRenderContext *self, bool bEnable, bool bColor )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.OverrideColorWriteEnable\t%d,%d", bEnable ? 1 : 0, bColor ? 1 : 0 );
	SS_CALL( OverrideColorWriteEnable, void ( * )( IMatRenderContext *, bool, bool ) )( self, bEnable, bColor );
}

void H_FogRadial( IMatRenderContext *self, bool bRadial )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.FogRadial\t%d", bRadial ? 1 : 0 );
	SS_CALL( FogRadial, void ( * )( IMatRenderContext *, bool ) )( self, bRadial );
}

void H_SetFrameBufferCopyTexture( IMatRenderContext *self, ITexture *pTexture, int nIndex )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.SetFrameBufferCopyTexture\ttexture=%s index=%d", TextureName( pTexture ), nIndex );
	SS_CALL( SetFrameBufferCopyTexture, void ( * )( IMatRenderContext *, ITexture *, int ) )( self, pTexture, nIndex );
}

// IVModelRender --------------------------------------------------------------
void H_ForcedMaterialOverride( IVModelRender *self, IMaterial *pMaterial, OverrideType_t type )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.ForcedMaterialOverride\tmaterial=%s type=%d", MaterialName( pMaterial ), (int)type );
	SS_CALL( ForcedMaterialOverride, void ( * )( IVModelRender *, IMaterial *, OverrideType_t ) )( self, pMaterial,
																								   type );
}

int H_DrawModelEx( IVModelRender *self, ModelRenderInfo_t &info )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.DrawModelEx\tent=%d model=%s flags=0x%x skin=%d body=%d", info.entity_index,
			  info.pModel ? modelinfo->GetModelName( info.pModel ) : "-", info.flags, info.skin, info.body );
	return SS_CALL( DrawModelEx, int ( * )( IVModelRender *, ModelRenderInfo_t & ) )( self, info );
}

int H_DrawModelExStaticProp( IVModelRender *self, ModelRenderInfo_t &info )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.DrawModelExStaticProp\tent=%d model=%s flags=0x%x", info.entity_index,
			  info.pModel ? modelinfo->GetModelName( info.pModel ) : "-", info.flags );
	return SS_CALL( DrawModelExStaticProp, int ( * )( IVModelRender *, ModelRenderInfo_t & ) )( self, info );
}

void H_DrawModelExecute( IVModelRender *self, const DrawModelState_t &state, const ModelRenderInfo_t &info,
						 matrix3x4_t *pCustomBoneToWorld )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.DrawModelExecute\tent=%d model=%s flags=0x%x", info.entity_index,
			  info.pModel ? modelinfo->GetModelName( info.pModel ) : "-", info.flags );
	SS_CALL( DrawModelExecute,
			 void ( * )( IVModelRender *, const DrawModelState_t &, const ModelRenderInfo_t &, matrix3x4_t * ) )(
		self, state, info, pCustomBoneToWorld );
}

int H_DrawStaticPropArrayFast( IVModelRender *self, StaticPropRenderInfo_t *pProps, int count, bool bShadowDepth )
{
	if ( SS_FROM_CLIENT() )
		Line( "model.DrawStaticPropArrayFast\tcount=%d shadowdepth=%d", count, bShadowDepth ? 1 : 0 );
	return SS_CALL( DrawStaticPropArrayFast, int ( * )( IVModelRender *, StaticPropRenderInfo_t *, int, bool ) )(
		self, pProps, count, bShadowDepth );
}

// IStaticPropMgrClient -------------------------------------------------------
void H_ComputePropOpacity( IStaticPropMgrClient *self, const Vector &origin, float factor )
{
	if ( SS_FROM_CLIENT() )
		Line( "props.ComputePropOpacity\torigin=%.9g,%.9g,%.9g factor=%.9g", origin.x, origin.y, origin.z, factor );
	SS_CALL( ComputePropOpacity, void ( * )( IStaticPropMgrClient *, const Vector &, float ) )( self, origin, factor );
}

void H_DrawStaticProps( IStaticPropMgrClient *self, IClientRenderable **pProps, int count, bool bShadowDepth,
						bool bWireframe )
{
	if ( SS_FROM_CLIENT() )
		Line( "props.DrawStaticProps\tcount=%d shadowdepth=%d wireframe=%d", count, bShadowDepth ? 1 : 0,
			  bWireframe ? 1 : 0 );
	SS_CALL( DrawStaticProps, void ( * )( IStaticPropMgrClient *, IClientRenderable **, int, bool, bool ) )(
		self, pProps, count, bShadowDepth, bWireframe );
}

// IVEngineClient -------------------------------------------------------------
void H_Sound_ExtraUpdate( IVEngineClient *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "engine.Sound_ExtraUpdate" );
	SS_CALL( Sound_ExtraUpdate, void ( * )( IVEngineClient * ) )( self );
}

void H_DrawPortals( IVEngineClient *self )
{
	if ( SS_FROM_CLIENT() )
		Line( "engine.DrawPortals" );
	SS_CALL( DrawPortals, void ( * )( IVEngineClient * ) )( self );
}

SkyboxVisibility_t H_IsSkyboxVisibleFromPoint( IVEngineClient *self, const Vector &point )
{
	bool bClient = SS_FROM_CLIENT();
	SkyboxVisibility_t result = SS_CALL( IsSkyboxVisibleFromPoint,
										 SkyboxVisibility_t ( * )( IVEngineClient *, const Vector & ) )( self, point );
	if ( bClient )
		Line( "q\tengine.IsSkyboxVisibleFromPoint\tpoint=%.9g,%.9g,%.9g => %d", point.x, point.y, point.z,
			  (int)result );
	return result;
}

void H_GrabPreColorCorrectedFrame( IVEngineClient *self, int x, int y, int w, int h )
{
	if ( SS_FROM_CLIENT() )
		Line( "engine.GrabPreColorCorrectedFrame\t%d,%d,%d,%d", x, y, w, h );
	SS_CALL( GrabPreColorCorrectedFrame, void ( * )( IVEngineClient *, int, int, int, int ) )( self, x, y, w, h );
}

// The mesh and occlusion-query hooks (SOURCESHARP_RENDER_TRACE_MESHES=1): the
// material bound, each dynamic mesh with its vertices as UnlockMesh gets them,
// the clipping switch, and each query object by first-seen order. Pixel counts
// and sphere diameters are `q` lines.
bool g_bMeshes = false;
CUtlVector<const void *> g_Queries; // a query object's ordinal is its index here

int Query( const void *h )
{
	if ( !h )
		return -1;
	int i = g_Queries.Find( h );
	if ( i < 0 )
		i = g_Queries.AddToTail( h );
	return i;
}

// By the vtable of the mesh it was installed on: dynamic meshes of more than one
// class may be handed out, each with its own original.
struct MeshHook_t
{
	void **pVtable;
	void *pOriginal;
};

MeshHook_t g_MeshHooks[8];
int g_nMeshHooks = 0;

void H_UnlockMesh( IMesh *self, int nVertexCount, int nIndexCount, MeshDesc_t &desc )
{
	void **pVtable = *(void ***)self;
	void *pOriginal = NULL;
	for ( int i = 0; i < g_nMeshHooks; i++ )
	{
		if ( g_MeshHooks[i].pVtable == pVtable )
			pOriginal = g_MeshHooks[i].pOriginal;
	}
	if ( SS_FROM_CLIENT() )
	{
		char szVerts[900];
		int n = 0;
		szVerts[0] = 0;
		for ( int v = 0; v < nVertexCount && v < 12 && desc.m_pPosition && n < (int)sizeof( szVerts ) - 80; v++ )
		{
			const float *p =
				(const float *)( (const unsigned char *)desc.m_pPosition + v * desc.m_VertexSize_Position );
			n += V_snprintf( szVerts + n, sizeof( szVerts ) - n, " %.9g,%.9g,%.9g", p[0], p[1], p[2] );
			if ( desc.m_pColor && desc.m_VertexSize_Color )
			{
				const unsigned char *c = desc.m_pColor + v * desc.m_VertexSize_Color;
				n += V_snprintf( szVerts + n, sizeof( szVerts ) - n, "/%02x%02x%02x%02x", c[0], c[1], c[2], c[3] );
			}
		}
		Line( "mesh.Unlock\tverts=%d indices=%d%s", nVertexCount, nIndexCount, szVerts );
	}
	if ( pOriginal )
		( (void ( * )( IMesh *, int, int, MeshDesc_t & ))pOriginal )( self, nVertexCount, nIndexCount, desc );
}

void HookMesh( IMesh *pMesh )
{
	if ( !pMesh || g_nMeshHooks >= (int)ARRAYSIZE( g_MeshHooks ) )
		return;
	void **pVtable = *(void ***)pMesh;
	for ( int i = 0; i < g_nMeshHooks; i++ )
	{
		if ( g_MeshHooks[i].pVtable == pVtable )
			return;
	}
	VTableSlotPatch::Patch patch;
	if ( !VTableSlotPatch::Install( pMesh, &IMesh::UnlockMesh, (void *)&H_UnlockMesh, &patch ) )
		return;
	g_MeshHooks[g_nMeshHooks].pVtable = pVtable;
	g_MeshHooks[g_nMeshHooks].pOriginal = patch.pOriginal;
	g_nMeshHooks++;
	g_Patches.AddToTail( patch );
}

void H_Bind( IMatRenderContext *self, IMaterial *pMaterial, void *pProxyData )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.Bind\tmaterial=%s", MaterialName( pMaterial ) );
	SS_CALL( Bind, void ( * )( IMatRenderContext *, IMaterial *, void * ) )( self, pMaterial, pProxyData );
}

IMesh *H_GetDynamicMesh( IMatRenderContext *self, bool bBuffered, IMesh *pVertexOverride, IMesh *pIndexOverride,
						 IMaterial *pAutoBind )
{
	const bool bClient = SS_FROM_CLIENT();
	IMesh *pMesh = SS_CALL( GetDynamicMesh, IMesh * (*)(IMatRenderContext *, bool, IMesh *, IMesh *, IMaterial *))(
		self, bBuffered, pVertexOverride, pIndexOverride, pAutoBind );
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
	const bool bWas = SS_CALL( EnableClipping, bool ( * )( IMatRenderContext *, bool ) )( self, bEnable );
	if ( bClient )
		Line( "ctx.EnableClipping\t%d was=%d", bEnable ? 1 : 0, bWas ? 1 : 0 );
	return bWas;
}

OcclusionQueryObjectHandle_t H_CreateOcclusionQueryObject( IMatRenderContext *self )
{
	const bool bClient = SS_FROM_CLIENT();
	OcclusionQueryObjectHandle_t h =
		SS_CALL( CreateOcclusionQueryObject, OcclusionQueryObjectHandle_t ( * )( IMatRenderContext * ) )( self );
	if ( bClient )
		Line( "ctx.CreateOcclusionQueryObject\t#%d", Query( h ) );
	return h;
}

void H_DestroyOcclusionQueryObject( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.DestroyOcclusionQueryObject\t#%d", Query( h ) );
	SS_CALL( DestroyOcclusionQueryObject, void ( * )( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
}

void H_ResetOcclusionQueryObject( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.ResetOcclusionQueryObject\t#%d", Query( h ) );
	SS_CALL( ResetOcclusionQueryObject, void ( * )( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
}

void H_BeginOcclusionQueryDrawing( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.BeginOcclusionQueryDrawing\t#%d", Query( h ) );
	SS_CALL( BeginOcclusionQueryDrawing, void ( * )( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
}

void H_EndOcclusionQueryDrawing( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	if ( SS_FROM_CLIENT() )
		Line( "ctx.EndOcclusionQueryDrawing\t#%d", Query( h ) );
	SS_CALL( EndOcclusionQueryDrawing, void ( * )( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
}

int H_OcclusionQuery_GetNumPixelsRendered( IMatRenderContext *self, OcclusionQueryObjectHandle_t h )
{
	const bool bClient = SS_FROM_CLIENT();
	const int n = SS_CALL( OcclusionQuery_GetNumPixelsRendered,
						   int ( * )( IMatRenderContext *, OcclusionQueryObjectHandle_t ) )( self, h );
	if ( bClient )
		Line( "q\tOcclusionQuery_GetNumPixelsRendered\t#%d=%d", Query( h ), n );
	return n;
}

float H_ComputePixelDiameterOfSphere( IMatRenderContext *self, const Vector &origin, float flRadius )
{
	const bool bClient = SS_FROM_CLIENT();
	const float f = SS_CALL( ComputePixelDiameterOfSphere,
							 float ( * )( IMatRenderContext *, const Vector &, float ) )( self, origin, flRadius );
	if ( bClient )
		Line( "q\tComputePixelDiameterOfSphere\t%.9g,%.9g,%.9g r=%.9g=%.9g", origin.x, origin.y, origin.z, flRadius,
			  f );
	return f;
}

// The shadow hooks (SOURCESHARP_RENDER_TRACE_SHADOWS=1): what the client's shadow
// manager asks IShadowMgr and the static prop manager's two shadow methods.
// GetInfo, GetFlashlightFrustum and GetFlashlightState are `q` lines. Handles
// print as the engine's numbers; materials and textures by name; a renderable as
// `ent:<index>` or `prop:<model>@<origin>`; a leaf list as its count, an FNV-1a
// hash and, when short, the indices.
unsigned int HashInts( const int *pList, int nCount )
{
	unsigned int h = 2166136261u;
	const unsigned char *p = (const unsigned char *)pList;
	for ( int i = 0; pList && i < nCount * (int)sizeof( int ); i++ )
		h = ( h ^ p[i] ) * 16777619u;
	return h;
}

void LeafText( int nCount, const int *pList, char *pOut, int nOut )
{
	int n = V_snprintf( pOut, nOut, "%d:%08x", nCount, HashInts( pList, nCount ) );
	for ( int i = 0; pList && nCount <= 32 && i < nCount && n > 0 && n < nOut - 16; i++ )
		n += V_snprintf( pOut + n, nOut - n, "%c%d", i ? ',' : '=', pList[i] );
}

void MatrixText( const VMatrix &m, char *pOut, int nOut )
{
	V_snprintf( pOut, nOut, "%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g,%.9g", m[0][0],
				m[0][1], m[0][2], m[0][3], m[1][0], m[1][1], m[1][2], m[1][3], m[2][0], m[2][1], m[2][2], m[2][3],
				m[3][0], m[3][1], m[3][2], m[3][3] );
}

void RenderableText( IClientRenderable *pRenderable, char *pOut, int nOut )
{
	if ( !pRenderable )
	{
		V_snprintf( pOut, nOut, "-" );
		return;
	}
	IClientUnknown *pUnknown = pRenderable->GetIClientUnknown();
	IClientEntity *pEntity = pUnknown ? pUnknown->GetIClientEntity() : NULL;
	if ( pEntity )
	{
		V_snprintf( pOut, nOut, "ent:%d", pEntity->entindex() );
		return;
	}
	const model_t *pModel = pRenderable->GetModel();
	const Vector &origin = pRenderable->GetRenderOrigin();
	V_snprintf( pOut, nOut, "prop:%s@%.9g,%.9g,%.9g", pModel ? modelinfo->GetModelName( pModel ) : "-", origin.x,
				origin.y, origin.z );
}

void FlashlightText( const FlashlightState_t &state, char *pOut, int nOut )
{
	FlashlightState_t s = state; // the scissor getters are not const
	V_snprintf( pOut, nOut,
				"origin=%.9g,%.9g,%.9g quat=%.9g,%.9g,%.9g,%.9g z=%.9g,%.9g fov=%.9g,%.9g atten=%.9g,%.9g,%.9g "
				"color=%.9g,%.9g,%.9g,%.9g texture=%s frame=%d shadows=%d drawfrustum=%d res=%.9g filter=%.9g "
				"slopebias=%.9g bias=%.9g jitter=%.9g shadowatten=%.9g quality=%d scissor=%d:%d,%d,%d,%d",
				s.m_vecLightOrigin.x, s.m_vecLightOrigin.y, s.m_vecLightOrigin.z, s.m_quatOrientation.x,
				s.m_quatOrientation.y, s.m_quatOrientation.z, s.m_quatOrientation.w, s.m_NearZ, s.m_FarZ,
				s.m_fHorizontalFOVDegrees, s.m_fVerticalFOVDegrees, s.m_fQuadraticAtten, s.m_fLinearAtten,
				s.m_fConstantAtten, s.m_Color[0], s.m_Color[1], s.m_Color[2], s.m_Color[3],
				TextureName( s.m_pSpotlightTexture ), s.m_nSpotlightTextureFrame, s.m_bEnableShadows ? 1 : 0,
				s.m_bDrawShadowFrustum ? 1 : 0, s.m_flShadowMapResolution, s.m_flShadowFilterSize,
				s.m_flShadowSlopeScaleDepthBias, s.m_flShadowDepthBias, s.m_flShadowJitterSeed, s.m_flShadowAtten,
				s.m_nShadowQuality, s.DoScissor() ? 1 : 0, s.GetLeft(), s.GetTop(), s.GetRight(), s.GetBottom() );
}

// IShadowMgr -----------------------------------------------------------------
ShadowHandle_t H_CreateShadowEx( IShadowMgr *self, IMaterial *pMaterial, IMaterial *pModelMaterial, void *pBindProxy,
								 int nFlags )
{
	const bool bClient = SS_FROM_SHADOWS();
	const ShadowHandle_t h =
		SS_CALL( CreateShadowEx, ShadowHandle_t ( * )( IShadowMgr *, IMaterial *, IMaterial *, void *, int ) )(
			self, pMaterial, pModelMaterial, pBindProxy, nFlags );
	if ( bClient )
		Line( "shadowmgr.CreateShadowEx\tmaterial=%s model=%s proxy=%d flags=0x%x => %d", MaterialName( pMaterial ),
			  MaterialName( pModelMaterial ), (int)(intp)pBindProxy, nFlags, (int)h );
	return h;
}

void H_DestroyShadow( IShadowMgr *self, ShadowHandle_t h )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.DestroyShadow\t%d", (int)h );
	SS_CALL( DestroyShadow, void ( * )( IShadowMgr *, ShadowHandle_t ) )( self, h );
}

void H_SetShadowMaterial( IShadowMgr *self, ShadowHandle_t h, IMaterial *pMaterial, IMaterial *pModelMaterial,
						  void *pBindProxy )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.SetShadowMaterial\t%d material=%s model=%s proxy=%d", (int)h, MaterialName( pMaterial ),
			  MaterialName( pModelMaterial ), (int)(intp)pBindProxy );
	SS_CALL( SetShadowMaterial, void ( * )( IShadowMgr *, ShadowHandle_t, IMaterial *, IMaterial *, void * ) )(
		self, h, pMaterial, pModelMaterial, pBindProxy );
}

void H_ProjectShadow( IShadowMgr *self, ShadowHandle_t h, const Vector &origin, const Vector &dir,
					  const VMatrix &worldToShadow, const Vector2D &size, int nLeafCount, const int *pLeafList,
					  float flMaxHeight, float flFalloffOffset, float flFalloffAmount, const Vector &casterOrigin )
{
	if ( SS_FROM_SHADOWS() )
	{
		char szMatrix[320], szLeaves[448];
		MatrixText( worldToShadow, szMatrix, sizeof( szMatrix ) );
		LeafText( nLeafCount, pLeafList, szLeaves, sizeof( szLeaves ) );
		Line( "shadowmgr.ProjectShadow\t%d origin=%.9g,%.9g,%.9g dir=%.9g,%.9g,%.9g size=%.9g,%.9g maxheight=%.9g "
			  "falloff=%.9g,%.9g caster=%.9g,%.9g,%.9g matrix=%s leaves=%s",
			  (int)h, origin.x, origin.y, origin.z, dir.x, dir.y, dir.z, size.x, size.y, flMaxHeight, flFalloffOffset,
			  flFalloffAmount, casterOrigin.x, casterOrigin.y, casterOrigin.z, szMatrix, szLeaves );
	}
	SS_CALL( ProjectShadow,
			 void ( * )( IShadowMgr *, ShadowHandle_t, const Vector &, const Vector &, const VMatrix &,
						 const Vector2D &, int, const int *, float, float, float,
						 const Vector & ) )( self, h, origin, dir, worldToShadow, size, nLeafCount, pLeafList,
											 flMaxHeight, flFalloffOffset, flFalloffAmount, casterOrigin );
}

void H_ProjectFlashlight( IShadowMgr *self, ShadowHandle_t h, const VMatrix &worldToShadow, int nLeafCount,
						  const int *pLeafList )
{
	if ( SS_FROM_SHADOWS() )
	{
		char szMatrix[320], szLeaves[448];
		MatrixText( worldToShadow, szMatrix, sizeof( szMatrix ) );
		LeafText( nLeafCount, pLeafList, szLeaves, sizeof( szLeaves ) );
		Line( "shadowmgr.ProjectFlashlight\t%d matrix=%s leaves=%s", (int)h, szMatrix, szLeaves );
	}
	SS_CALL( ProjectFlashlight, void ( * )( IShadowMgr *, ShadowHandle_t, const VMatrix &, int, const int * ) )(
		self, h, worldToShadow, nLeafCount, pLeafList );
}

const ShadowInfo_t &H_GetInfo( IShadowMgr *self, ShadowHandle_t h )
{
	const bool bClient = SS_FROM_SHADOWS();
	const ShadowInfo_t &info = SS_CALL( GetInfo, const ShadowInfo_t &(*)( IShadowMgr *, ShadowHandle_t ) )( self, h );
	if ( bClient )
	{
		char szMatrix[320];
		MatrixText( info.m_WorldToShadow, szMatrix, sizeof( szMatrix ) );
		Line( "q\tshadowmgr.GetInfo\t%d => falloff=%.9g,%.9g,%.9g tex=%.9g,%.9g,%.9g,%.9g bias=%d matrix=%s", (int)h,
			  info.m_FalloffOffset, info.m_MaxDist, info.m_FalloffAmount, info.m_TexOrigin.x, info.m_TexOrigin.y,
			  info.m_TexSize.x, info.m_TexSize.y, (int)info.m_FalloffBias, szMatrix );
	}
	return info;
}

const Frustum_t &H_GetFlashlightFrustum( IShadowMgr *self, ShadowHandle_t h )
{
	const bool bClient = SS_FROM_SHADOWS();
	const Frustum_t &frustum =
		SS_CALL( GetFlashlightFrustum, const Frustum_t &(*)( IShadowMgr *, ShadowHandle_t ) )( self, h );
	if ( bClient )
	{
		char szPlanes[720];
		int n = 0;
		szPlanes[0] = 0;
		for ( int i = 0; i < FRUSTUM_NUMPLANES && n >= 0 && n < (int)sizeof( szPlanes ) - 120; i++ )
		{
			const cplane_t *p = frustum.GetPlane( i );
			n += V_snprintf( szPlanes + n, sizeof( szPlanes ) - n, " %.9g,%.9g,%.9g,%.9g/%d", p->normal.x, p->normal.y,
							 p->normal.z, p->dist, (int)p->type );
		}
		Line( "q\tshadowmgr.GetFlashlightFrustum\t%d =>%s", (int)h, szPlanes );
	}
	return frustum;
}

void H_AddShadowToBrushModel( IShadowMgr *self, ShadowHandle_t h, model_t *pModel, const Vector &origin,
							  const QAngle &angles )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.AddShadowToBrushModel\t%d model=%s origin=%.9g,%.9g,%.9g angles=%.9g,%.9g,%.9g", (int)h,
			  modelinfo->GetModelName( pModel ), origin.x, origin.y, origin.z, angles.x, angles.y, angles.z );
	SS_CALL( AddShadowToBrushModel,
			 void ( * )( IShadowMgr *, ShadowHandle_t, model_t *, const Vector &, const QAngle & ) )( self, h, pModel,
																									  origin, angles );
}

void H_RemoveAllShadowsFromBrushModel( IShadowMgr *self, model_t *pModel )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.RemoveAllShadowsFromBrushModel\tmodel=%s", modelinfo->GetModelName( pModel ) );
	SS_CALL( RemoveAllShadowsFromBrushModel, void ( * )( IShadowMgr *, model_t * ) )( self, pModel );
}

void H_SetShadowTexCoord( IShadowMgr *self, ShadowHandle_t h, float x, float y, float w, float hgt )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.SetShadowTexCoord\t%d %.9g,%.9g,%.9g,%.9g", (int)h, x, y, w, hgt );
	SS_CALL( SetShadowTexCoord, void ( * )( IShadowMgr *, ShadowHandle_t, float, float, float, float ) )( self, h, x, y,
																										  w, hgt );
}

void H_AddShadowToModel( IShadowMgr *self, ShadowHandle_t h, ModelInstanceHandle_t instance )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.AddShadowToModel\t%d instance=%d", (int)h, (int)instance );
	SS_CALL( AddShadowToModel, void ( * )( IShadowMgr *, ShadowHandle_t, ModelInstanceHandle_t ) )( self, h, instance );
}

void H_RemoveAllShadowsFromModel( IShadowMgr *self, ModelInstanceHandle_t instance )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.RemoveAllShadowsFromModel\tinstance=%d", (int)instance );
	SS_CALL( RemoveAllShadowsFromModel, void ( * )( IShadowMgr *, ModelInstanceHandle_t ) )( self, instance );
}

void H_ClearExtraClipPlanes( IShadowMgr *self, ShadowHandle_t h )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.ClearExtraClipPlanes\t%d", (int)h );
	SS_CALL( ClearExtraClipPlanes, void ( * )( IShadowMgr *, ShadowHandle_t ) )( self, h );
}

void H_AddExtraClipPlane( IShadowMgr *self, ShadowHandle_t h, const Vector &normal, float flDist )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.AddExtraClipPlane\t%d normal=%.9g,%.9g,%.9g dist=%.9g", (int)h, normal.x, normal.y, normal.z,
			  flDist );
	SS_CALL( AddExtraClipPlane, void ( * )( IShadowMgr *, ShadowHandle_t, const Vector &, float ) )( self, h, normal,
																									 flDist );
}

void H_EnableShadow( IShadowMgr *self, ShadowHandle_t h, bool bEnable )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.EnableShadow\t%d %d", (int)h, bEnable ? 1 : 0 );
	SS_CALL( EnableShadow, void ( * )( IShadowMgr *, ShadowHandle_t, bool ) )( self, h, bEnable );
}

void H_SetFalloffBias( IShadowMgr *self, ShadowHandle_t h, unsigned char ucBias )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.SetFalloffBias\t%d %d", (int)h, (int)ucBias );
	SS_CALL( SetFalloffBias, void ( * )( IShadowMgr *, ShadowHandle_t, unsigned char ) )( self, h, ucBias );
}

void H_UpdateFlashlightState( IShadowMgr *self, ShadowHandle_t h, const FlashlightState_t &state )
{
	if ( SS_FROM_SHADOWS() )
	{
		char szState[900];
		FlashlightText( state, szState, sizeof( szState ) );
		Line( "shadowmgr.UpdateFlashlightState\t%d %s", (int)h, szState );
	}
	SS_CALL( UpdateFlashlightState, void ( * )( IShadowMgr *, ShadowHandle_t, const FlashlightState_t & ) )( self, h,
																											 state );
}

void H_AddFlashlightRenderable( IShadowMgr *self, ShadowHandle_t h, IClientRenderable *pRenderable )
{
	if ( SS_FROM_SHADOWS() )
	{
		char szRenderable[320];
		RenderableText( pRenderable, szRenderable, sizeof( szRenderable ) );
		Line( "shadowmgr.AddFlashlightRenderable\t%d renderable=%s", (int)h, szRenderable );
	}
	SS_CALL( AddFlashlightRenderable, void ( * )( IShadowMgr *, ShadowHandle_t, IClientRenderable * ) )( self, h,
																										 pRenderable );
}

void H_SetFlashlightDepthTexture( IShadowMgr *self, ShadowHandle_t h, ITexture *pTexture, unsigned char ucStencilBit )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.SetFlashlightDepthTexture\t%d texture=%s stencil=%d", (int)h, TextureName( pTexture ),
			  (int)ucStencilBit );
	SS_CALL( SetFlashlightDepthTexture,
			 void ( * )( IShadowMgr *, ShadowHandle_t, ITexture *, unsigned char ) )( self, h, pTexture, ucStencilBit );
}

const FlashlightState_t &H_GetFlashlightState( IShadowMgr *self, ShadowHandle_t h )
{
	const bool bClient = SS_FROM_SHADOWS();
	const FlashlightState_t &state =
		SS_CALL( GetFlashlightState, const FlashlightState_t &(*)( IShadowMgr *, ShadowHandle_t ) )( self, h );
	if ( bClient )
	{
		char szState[900];
		FlashlightText( state, szState, sizeof( szState ) );
		Line( "q\tshadowmgr.GetFlashlightState\t%d => %s", (int)h, szState );
	}
	return state;
}

void H_SetFlashlightRenderState( IShadowMgr *self, ShadowHandle_t h )
{
	if ( SS_FROM_SHADOWS() )
		Line( "shadowmgr.SetFlashlightRenderState\t%d", (int)h );
	SS_CALL( SetFlashlightRenderState, void ( * )( IShadowMgr *, ShadowHandle_t ) )( self, h );
}

// IStaticPropMgrClient's shadow methods --------------------------------------
void H_AddShadowToStaticProp( IStaticPropMgrClient *self, unsigned short h, IClientRenderable *pRenderable )
{
	if ( SS_FROM_SHADOWS() )
	{
		char szRenderable[320];
		RenderableText( pRenderable, szRenderable, sizeof( szRenderable ) );
		Line( "props.AddShadowToStaticProp\t%d renderable=%s", (int)h, szRenderable );
	}
	SS_CALL( AddShadowToStaticProp,
			 void ( * )( IStaticPropMgrClient *, unsigned short, IClientRenderable * ) )( self, h, pRenderable );
}

void H_RemoveAllShadowsFromStaticProp( IStaticPropMgrClient *self, IClientRenderable *pRenderable )
{
	if ( SS_FROM_SHADOWS() )
	{
		char szRenderable[320];
		RenderableText( pRenderable, szRenderable, sizeof( szRenderable ) );
		Line( "props.RemoveAllShadowsFromStaticProp\trenderable=%s", szRenderable );
	}
	SS_CALL( RemoveAllShadowsFromStaticProp, void ( * )( IStaticPropMgrClient *, IClientRenderable * ) )( self,
																										  pRenderable );
}

#define SS_INSTALL( object, pmf, name ) \
	bOk = bOk && Install( object, pmf, (void *)&H_##name, &g_Originals[kHook_##name] )

bool InstallAll( IMatRenderContext *pContext, bool bShadows )
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
	SS_INSTALL( render,
				static_cast<void ( IVRenderView::* )( const CViewSetup &, int, ITexture *, Frustum )>(
					&IVRenderView::Push3DView ),
				Push3DView );
	SS_INSTALL( render,
				static_cast<void ( IVRenderView::* )( const CViewSetup &, int, ITexture *, Frustum, ITexture * )>(
					&IVRenderView::Push3DView ),
				Push3DViewDepth );
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
	SS_INSTALL( pContext,
				static_cast<void ( IMatRenderContext::* )()>( &IMatRenderContext::PushRenderTargetAndViewport ),
				PushRTVP0 );
	SS_INSTALL(
		pContext,
		static_cast<void ( IMatRenderContext::* )( ITexture * )>( &IMatRenderContext::PushRenderTargetAndViewport ),
		PushRTVP1 );
	SS_INSTALL( pContext,
				static_cast<void ( IMatRenderContext::* )( ITexture *, int, int, int, int )>(
					&IMatRenderContext::PushRenderTargetAndViewport ),
				PushRTVP5 );
	SS_INSTALL( pContext,
				static_cast<void ( IMatRenderContext::* )( ITexture *, ITexture *, int, int, int, int )>(
					&IMatRenderContext::PushRenderTargetAndViewport ),
				PushRTVP6 );
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
	SS_INSTALL( pContext, &IMatRenderContext::SetFullScreenDepthTextureValidityFlag,
				SetFullScreenDepthTextureValidityFlag );
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
		SS_INSTALL( pContext, &IMatRenderContext::OcclusionQuery_GetNumPixelsRendered,
					OcclusionQuery_GetNumPixelsRendered );
		SS_INSTALL( pContext, &IMatRenderContext::ComputePixelDiameterOfSphere, ComputePixelDiameterOfSphere );
	}

	if ( bShadows )
	{
		SS_INSTALL( shadowmgr, &IShadowMgr::CreateShadowEx, CreateShadowEx );
		SS_INSTALL( shadowmgr, &IShadowMgr::DestroyShadow, DestroyShadow );
		SS_INSTALL( shadowmgr, &IShadowMgr::SetShadowMaterial, SetShadowMaterial );
		SS_INSTALL( shadowmgr, &IShadowMgr::ProjectShadow, ProjectShadow );
		SS_INSTALL( shadowmgr, &IShadowMgr::ProjectFlashlight, ProjectFlashlight );
		SS_INSTALL( shadowmgr, &IShadowMgr::GetInfo, GetInfo );
		SS_INSTALL( shadowmgr, &IShadowMgr::GetFlashlightFrustum, GetFlashlightFrustum );
		SS_INSTALL( shadowmgr, &IShadowMgr::AddShadowToBrushModel, AddShadowToBrushModel );
		SS_INSTALL( shadowmgr, &IShadowMgr::RemoveAllShadowsFromBrushModel, RemoveAllShadowsFromBrushModel );
		SS_INSTALL( shadowmgr, &IShadowMgr::SetShadowTexCoord, SetShadowTexCoord );
		SS_INSTALL( shadowmgr, &IShadowMgr::AddShadowToModel, AddShadowToModel );
		SS_INSTALL( shadowmgr, &IShadowMgr::RemoveAllShadowsFromModel, RemoveAllShadowsFromModel );
		SS_INSTALL( shadowmgr, &IShadowMgr::ClearExtraClipPlanes, ClearExtraClipPlanes );
		SS_INSTALL( shadowmgr, &IShadowMgr::AddExtraClipPlane, AddExtraClipPlane );
		SS_INSTALL( shadowmgr, &IShadowMgr::EnableShadow, EnableShadow );
		SS_INSTALL( shadowmgr, &IShadowMgr::SetFalloffBias, SetFalloffBias );
		SS_INSTALL( shadowmgr, &IShadowMgr::UpdateFlashlightState, UpdateFlashlightState );
		SS_INSTALL( shadowmgr, &IShadowMgr::AddFlashlightRenderable, AddFlashlightRenderable );
		SS_INSTALL( shadowmgr, &IShadowMgr::SetFlashlightDepthTexture, SetFlashlightDepthTexture );
		SS_INSTALL( shadowmgr, &IShadowMgr::GetFlashlightState, GetFlashlightState );
		SS_INSTALL( shadowmgr, &IShadowMgr::SetFlashlightRenderState, SetFlashlightRenderState );
		SS_INSTALL( staticpropmgr, &IStaticPropMgrClient::AddShadowToStaticProp, AddShadowToStaticProp );
		SS_INSTALL( staticpropmgr, &IStaticPropMgrClient::RemoveAllShadowsFromStaticProp,
					RemoveAllShadowsFromStaticProp );
	}

	return bOk;
}

} // namespace

bool RenderTrace::Begin()
{
	if ( g_bActive || !render || !modelrender || !staticpropmgr || !engine || !materials )
		return false;

	if ( !g_pClientBase )
	{
		SSModuleInfo info;
		if ( !SS_ModuleOfAddress( (void *)&RenderTrace::Begin, &info ) )
			return false;
		g_pClientBase = info.pBase;
	}
	if ( !g_pShadowsBase )
	{
		const SSForwarderTable *pTable = SSFwdClient_Table( "SSFwd_Shadows" );
		SSModuleInfo info;
		if ( pTable && pTable->nSlots > 0 && SS_ModuleOfAddress( pTable->ppSlots[0], &info ) )
			g_pShadowsBase = info.pBase;
	}

	g_Text.RemoveAll();
	g_WorldLists.RemoveAll();
	g_nShadowDepth = 0;
	g_Queries.RemoveAll();
	g_nMeshHooks = 0;
	const char *pszMeshes = getenv( "SOURCESHARP_RENDER_TRACE_MESHES" );
	g_bMeshes = pszMeshes && pszMeshes[0] == '1';
	const char *pszShadows = getenv( "SOURCESHARP_RENDER_TRACE_SHADOWS" );
	const bool bShadows = pszShadows && pszShadows[0] == '1';
	if ( bShadows && !shadowmgr )
		return false;

	CMatRenderContextPtr pContext( materials );
	if ( !InstallAll( pContext, bShadows ) )
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

void RenderTrace::ShadowEnter( const char *pszLine )
{
	if ( g_bActive )
		Line( "%s", pszLine );
	++g_nShadowDepth;
}

void RenderTrace::ShadowLeave()
{
	if ( g_nShadowDepth > 0 )
		--g_nShadowDepth;
}

void RenderTrace::ShadowNote( const char *pszLine )
{
	if ( g_bActive )
		Line( "%s", pszLine );
}

void RenderTrace::NoteLong( const char *pszPrefix, const char *pszText )
{
	if ( !g_bActive )
		return;
	g_Text.AddMultipleToTail( V_strlen( pszPrefix ), pszPrefix );
	g_Text.AddMultipleToTail( V_strlen( pszText ), pszText );
	g_Text.AddToTail( '\n' );
}
