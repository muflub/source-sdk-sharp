//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the render plan's executor ==========================//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve.md §3.3 / C7. The engine calls IBaseClientDLL::View_Render
// once a frame; CShellClientDLL hands the call here. Managed decides, through
// IManagedRenderClient::RenderFrame, which path draws the frame (stock's
// viewrender, or the render plan) and whether the frame is recorded by
// shell_render_trace_client.cpp; a recorded frame's text goes back to managed
// through RenderTraceDone.
//
// THE SEAM IS RenderView. Stock's CViewRender::Render (view.cpp) still builds
// each eye's CViewSetup, clear flags and draw flags and paints the main menu
// after; it hands them to the virtual RenderView, whose slot in CViewRender's
// vtable this file overwrites (vtable_slot_patch.h) once, at the first frame.
// On a plan frame the hook gathers RenderView's inputs (GatherInputs: the view,
// the hardware answers, the convars, the fog volume and water material, the
// viewer's fog and sky), asks managed for the plan (RenderPlanBuild), and runs
// the ops; on a stock frame, or when managed declines, it calls stock's
// RenderView. GetViewSetup's slot is overwritten too, so the setup the plan
// draws is the one shell_glow_client.cpp reads, as stock's RenderView made it, and
// FreezeFrame's, so the plan knows when stock's freeze frame is running.
//
// THE EXECUTOR DECIDES NOTHING. Each op is one engine call or one stock
// primitive with the arguments managed chose (RenderOpKind in C# says what each
// carries). Two ops act on an answer the engine gives mid-frame, and managed
// says how: ViewSetupVisEx's radial-vis bit (ClearColorToFogIfRadialVis,
// ClearForView) and the fog colour the render context holds after a fog volume
// is set (ClearColorFromFogColor).
//
// WHAT IS STILL STOCK'S, CALLED FROM HERE (C7 step 2 moves each as the plan
// owns it): a view's lists and draws are CRendering3dView's own primitives --
// CPlanView derives from it and calls BuildWorldRenderLists (on a list-cache
// miss), PruneWorldListInfo, BuildRenderableRenderLists, DrawWorld,
// DrawOpaqueRenderables, DrawTranslucentRenderables,
// DrawNoZBufferTranslucentRenderables and DrawTranslucentWorldInLeaves -- and
// the post-processing, overlay, glow, debug and HUD helpers are the free
// functions stock's RenderView calls. The world-list cache is this file's own
// (stock's g_WorldListCache is a viewrender.cpp class).
//
//=============================================================================//

#include <stdio.h>
#include <stdlib.h>
#include "vstdlib/random.h"
#include "utlvector.h"
#include "string_t.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "view.h"
#include "iviewrender.h"
#include "viewrender.h"
#include "view_shared.h"
#include "view_scene.h"
#include "iclientmode.h"
#include "model_types.h"
#include "vgui_controls/Controls.h"
#include "clientsideeffects.h"
#include "smoke_fog_overlay.h"
#include "c_effects.h"
#include "clientleafsystem.h"
#include "particles/particles.h"
#include "engine/IStaticPropMgr.h"
#include "detailobjectsystem.h"
#include "particles_ez.h"
#include "c_rope.h"
#include "studio_stats.h"
#include "c_func_reflective_glass.h"
#include "vgui_int.h"
#include "ienginevgui.h"
#include "engine/IEngineTrace.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"
#include "materialsystem/materialsystem_config.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/itexture.h"
#include "tier1/refcount.h"
#include "vgui/IPanel.h"
#include "materialsystem/imesh.h"
#include "client_world.h"
#include "filesystem.h"
#include "engine/ishadowmgr.h"
#include "voice_status.h"
#include "bitmap/imageformat.h"
#include "datacache/imdlcache.h"
#include "engine/IClientLeafSystem.h"
#include "engine/ivmodelinfo.h"
#include "engine/ivmodelrender.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "materialsystem/imaterialsystem.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib/vmatrix.h"
#include "mathlib/vplane.h"
#include "tier0/basetypes.h"
#include "tier0/commonmacros.h"
#include "tier0/dbg.h"
#include "tier0/platform.h"
#include "tier1/KeyValues.h"
#include "tier1/convar.h"
#include "tier1/strtools.h"
#include "tier1/tier1.h"
#include "tier1/utlmemory.h"
#include "tier1/utlvector.h"
#include "tier2/tier2.h"
#include "tier3/tier3.h"
#include "toolframework/itoolentity.h"
#include "vgui_controls/MessageMap.h"
#include "bspflags.h"
#include "bsptreedata.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "const.h"
#include "globalvars_base.h"
#include "hud.h"
#include "icliententitylist.h"
#include "iclientnetworkable.h"
#include "iclientrenderable.h"
#include "iclientunknown.h"
#include "icvar.h"
#include "igameevents.h"
#include "istudiorender.h"
#include "ivrenderview.h"
#include "networkvar.h"
#include "playernet_vars.h"
#include "texture_group_names.h"
#include "shell_render_targets.h"   // the render targets managed declares (C7)
#include "shell_glow_client.h"
#include "shell_pixelvis_client.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"
#include "shell_clientsystem.h"
#include "view_facts.h"
#include "vtable_slot_patch.h"
#include "shell_render_trace_client.h"
#include "shell_shadows_client.h"
#include "shell_standalone_client.h"
#include "shell_view_targets.h"
#include "ss_image_encode.h"

bool SourceSharp_ColorCorrectionActive();	// shell_worldrender.cpp (C9: colorcorrectionmgr.cpp left)

// shell_effects_list_client.cpp's material ids (IFxRender), as materials.
IMaterial *SourceSharp_FxMaterialOf( int32_t nMaterial );
#include "shell_view_executor_client.h"
#include "ss_clock.h"
#include "shell_particle_systems.h"

// memdbgon must be the last include file in a .cpp file!!!

extern void UpdateClientRenderableInPVSStatus();
extern IClientMode *GetClientModeNormal();
// viewrender.cpp's current-view setters, which stock declares at each use.
void SetupCurrentView( const Vector &vecOrigin, const QAngle &angles, view_id_t viewID );
void FinishCurrentView();

// The map's tonemap overrides (viewpostprocess.cpp's globals), noted with a traced frame.
extern bool  g_bUseCustomAutoExposureMin;
extern bool  g_bUseCustomAutoExposureMax;
extern bool  g_bUseCustomBloomScale;
extern float g_flCustomAutoExposureMin;
extern float g_flCustomAutoExposureMax;
extern float g_flCustomBloomScale;
extern float g_flCustomBloomScaleMinimum;


namespace
{

// RenderOpKind (C#), same names and numbers; RenderPlanAbiParityTests holds
// the two lists equal.
enum RenderOpKind_t
{
	RENDER_OP_None = 0,
	RENDER_OP_SceneBegin = 1,
	RENDER_OP_SceneEnd = 2,
	RENDER_OP_TurnOnToneMapping = 3,
	RENDER_OP_ShadowAdvanceFrame = 4,
	RENDER_OP_Push3DView = 5,
	RENDER_OP_Push2DView = 6,
	RENDER_OP_PopView = 7,
	RENDER_OP_SetupCurrentView = 8,
	RENDER_OP_FinishCurrentView = 9,
	RENDER_OP_AllowCurrentViewAccess = 10,
	RENDER_OP_ShadowPreRender = 11,
	RENDER_OP_ShadowComputeDepthTextures = 12,
	RENDER_OP_ShadowUnlockDepthTextures = 13,
	RENDER_OP_ClientSystemsPreRender = 14,
	// 15 was ClientSystemsPostRender (serialised ABI pass, 2026-10-20); not reused.
	RENDER_OP_ViewSetupVisEx = 16,
	RENDER_OP_ClearColorToFogIfRadialVis = 17,
	RENDER_OP_SetClearColorToFogColor = 18,
	RENDER_OP_ClearColor4ub = 19,
	RENDER_OP_ParticleIncrementFrameCode = 20,
	RENDER_OP_WorldViewBegin = 21,
	RENDER_OP_WorldViewEnd = 22,
	RENDER_OP_BeginUpdateLightmaps = 23,
	RENDER_OP_EndUpdateLightmaps = 24,
	RENDER_OP_BuildWorldLists = 25,
	RENDER_OP_PruneWorldList = 26,
	RENDER_OP_BuildRenderables = 27,
	RENDER_OP_FogLinear = 28,
	RENDER_OP_FogNone = 29,
	RENDER_OP_FogVolumeState = 30,
	RENDER_OP_ClearColorFromFogColor = 31,
	RENDER_OP_WorldViewShadowTextures = 32,
	RENDER_OP_SoundExtraUpdate = 33,
	RENDER_OP_SetFogZ = 34,
	RENDER_OP_SetHeightClipZ = 35,
	RENDER_OP_SetHeightClipMode = 36,
	RENDER_OP_ClearForView = 37,
	RENDER_OP_WaterLightmapScale = 38,
	RENDER_OP_RestoreWaterLightmapScale = 39,
	RENDER_OP_FrameBufferCopyPush = 40,
	RENDER_OP_FrameBufferCopyPop = 41,
	RENDER_OP_DrawWorld = 42,
	RENDER_OP_DrawOpaque = 43,
	RENDER_OP_DrawTranslucent = 44,
	RENDER_OP_DrawNoZTranslucent = 45,
	RENDER_OP_DrawTranslucentWorldInLeaves = 46,
	RENDER_OP_PixelVisEndCurrentView = 47,
	RENDER_OP_PixelVisEndScene = 48,
	RENDER_OP_Flush = 49,
	RENDER_OP_CopyRefractionTarget = 50,
	RENDER_OP_PushCustomClipPlane = 51,
	RENDER_OP_PopCustomClipPlane = 52,
	RENDER_OP_GlowDrawOverlays = 53,
	RENDER_OP_GlowUpdateSkyOverlays = 54,
	RENDER_OP_DrawPrecipitation = 55,
	RENDER_OP_ClientEffectsDraw = 59,
	RENDER_OP_SetFXCreationAllowed = 60,
	RENDER_OP_DrawPortals = 61,
	RENDER_OP_DrawLights = 62,
	RENDER_OP_RenderPlayerSprites = 63,
	RENDER_OP_MotionBlur = 64,
	RENDER_OP_PostScreenSpaceEffects = 65,
	RENDER_OP_ViewModels = 66,
	RENDER_OP_UnderwaterOverlay = 67,
	RENDER_OP_SmokeFogOverlay = 68,
	RENDER_OP_ViewDrawFade = 69,
	RENDER_OP_ScreenOverlay = 70,
	RENDER_OP_EnginePostProcessing = 71,
	RENDER_OP_GrabPreColorCorrectedFrame = 72,
	RENDER_OP_ToneMapScaleLinear = 74,
	RENDER_OP_SaveRenderTarget = 75,
	RENDER_OP_SetRenderTargetSaved = 76,
	RENDER_OP_HudPaint = 77,
	RENDER_OP_WorldListCacheFlush = 78,
	RENDER_OP_Skybox3DAreaBits = 79,
	RENDER_OP_Skybox3DAreaBitsRestore = 80,
	RENDER_OP_ViewSetupVis = 81,
	RENDER_OP_SetUnderwaterOverlay = 82,
	RENDER_OP_ClearUnderwaterOverlay = 83,
	RENDER_OP_Upscale = 84,
	RENDER_OP_SetCurrentViewSetup = 85,
	RENDER_OP_ViewScene = 86,
	RENDER_OP_SectionEnd = 87,
	RENDER_OP_SetBlend = 88,
	RENDER_OP_SetColorModulation = 89,
	RENDER_OP_DrawEntity = 90,
	RENDER_OP_StaticProp = 91,
	RENDER_OP_DrawStaticPropsBatch = 92,
	RENDER_OP_RopeResetCache = 93,
	RENDER_OP_ParticleSystemResetCache = 94,
	RENDER_OP_RopeDrawCache = 95,
	RENDER_OP_ParticleSystemDrawCache = 96,
	RENDER_OP_ParticleSingletons = 97,
	RENDER_OP_UpdateFullScreenDepthTexture = 98,
	RENDER_OP_FullScreenDepthTextureValidity = 99,
	RENDER_OP_DetailBeginTranslucent = 100,
	RENDER_OP_DetailLeaf = 101,
	RENDER_OP_DetailRenderTranslucent = 102,
	RENDER_OP_DetailRenderInLeaf = 103,
	RENDER_OP_TranslucentSurfaces = 104,
	RENDER_OP_FrameBufferForEntity = 105,
	RENDER_OP_UpdateRefractTexture = 106,
	RENDER_OP_UpdateScreenEffectTexture = 107,
	RENDER_OP_ViewerSeenBegin = 108,
	RENDER_OP_ViewerSeenEnd = 109,
	RENDER_OP_ViewTargetDrawn = 110,
	RENDER_OP_DebugQuad = 111,
	RENDER_OP_DebugScreenMaterial = 112,
	RENDER_OP_DebugLightmapPage = 113,
	RENDER_OP_DebugShadowTexture = 114,
	RENDER_OP_DebugShowTexture = 115,
	RENDER_OP_DebugFlashlightDepth = 116,
	RENDER_OP_Draw3DDebugOverlays = 117,
	RENDER_OP_DrawLineFile = 118,
	RENDER_OP_SmokeFogOverlayAlpha = 119,
	RENDER_OP_PushDefaultTarget = 120,
	RENDER_OP_PopRenderTargetAndViewport = 121,
	RENDER_OP_PostCopyScreenEffect = 122,
	RENDER_OP_PostSetRenderTarget = 123,
	RENDER_OP_PostSetRenderTargetAndViewport = 124,
	RENDER_OP_PostPushRenderTargetAndViewport = 125,
	RENDER_OP_PostDrawRect = 126,
	RENDER_OP_PostMaterialVarFloat = 127,
	RENDER_OP_PostQueryBegin = 128,
	RENDER_OP_PostQueryEnd = 129,
	RENDER_OP_PostStencil = 130,
	RENDER_OP_PostGoalToneScale = 131,
	RENDER_OP_PostResetToneScale = 132,
	RENDER_OP_PostEnableColorCorrection = 133,
	RENDER_OP_PostScissor = 134,
	RENDER_OP_PostClearColor3ub = 135,
	RENDER_OP_PostViewport = 136,
	RENDER_OP_PostClearBuffers = 137,
	RENDER_OP_PostDumpTga = 138,
	RENDER_OP_PostReadbackNext = 139,
	RENDER_OP_PostPushRenderTarget = 140,
	RENDER_OP_PostMeshBegin = 141,
	RENDER_OP_PostMeshVertex = 142,
	RENDER_OP_PostMeshFrame = 143,
	RENDER_OP_PostMeshEnd = 144,
	RENDER_OP_PostClearColor4ub = 145,
	RENDER_OP_FreezeFrameDraw = 146,
	RENDER_OP_FreezeFrameCapture = 147,
	RENDER_OP_ClearColor3ub = 148,
	RENDER_OP_ForcedMaterialOverride = 149,
};

// RenderTarget (C#).
enum RenderTarget_t
{
	RENDER_TARGET_None = 0,
	RENDER_TARGET_FullFrameFB0 = 1,
	RENDER_TARGET_WaterReflection = 2,
	RENDER_TARGET_WaterRefraction = 3,
	RENDER_TARGET_Saved = 4,
	RENDER_TARGET_ViewTarget0 = 5,
	RENDER_TARGET_ShadowColor = 6,
	RENDER_TARGET_ShadowDepth = 7,
};

// The convars RenderView's decisions read, in RenderCvar's (C#) order. The
// fog colours are strings; their three numbers take three slots each, after.
// C7 post-processing: the convars PostProcessPlanner reads (managed's PostCvar, in order).
const char *const kPostCvars[] =
{
	"mat_bloomscale", "mat_hdr_level", "mat_bloomamount_rate", "mat_debug_postprocessing_effects",
	"mat_debug_process_halfscreen", "mat_postprocessing_combine", "mat_dynamic_tonemapping", "mat_show_ab_hdr",
	"mat_tonemapping_occlusion_use_stencil", "mat_debug_autoexposure", "mat_autoexposure_max",
	"mat_autoexposure_min", "mat_show_histogram", "mat_hdr_tonemapscale", "mat_hdr_uncapexposure",
	"mat_force_bloom", "mat_disable_bloom", "mat_debug_bloom", "mat_accelerate_adjust_exposure_down",
	"mat_hdr_manual_tonemap_rate", "mat_non_hdr_bloom_scalefactor", "mat_bloom_scalefactor_scalar",
	"mat_exposure_center_region_x", "mat_exposure_center_region_y", "mat_exposure_center_region_x_flashlight",
	"mat_exposure_center_region_y_flashlight", "mat_tonemap_algorithm", "mat_tonemap_percent_target",
	"mat_tonemap_percent_bright_pixels", "mat_tonemap_min_avglum", "mat_fullbright", "mat_force_tonemap_scale",
	"mat_software_aa_strength", "mat_software_aa_quality", "mat_software_aa_edge_threshold",
	"mat_software_aa_blur_one_pixel_lines", "mat_software_aa_tap_offset", "mat_software_aa_debug",
	"mat_software_aa_strength_vgui", "mat_colorcorrection", "mat_postprocess_x", "mat_postprocess_y",
	"mat_dump_rts", "mat_queue_mode", "building_cubemaps", "localplayer_visionflags", "mat_motion_blur_enabled",
	"mat_motion_blur_forward_enabled", "mat_motion_blur_falling_min", "mat_motion_blur_falling_max",
	"mat_motion_blur_falling_intensity", "mat_motion_blur_rotation_intensity", "mat_motion_blur_strength",
};
const int kPostCvarCount = (int)ARRAYSIZE( kPostCvars );

const char *const kRenderCvars[] =
{
	"r_skybox", "r_3dsky", "mat_motion_blur_enabled", "building_cubemaps", "r_flashlightdepthtexture",
	"mat_viewportscale", "mat_viewportupscale", "cl_leveloverview", "fog_override", "fog_start",
	"fog_end", "fog_enable", "fog_maxdensity", "fog_radial", "fog_startskybox",
	"fog_endskybox", "fog_maxdensityskybox", "fog_enableskybox", "fog_radialskybox", "mat_drawwater",
	"r_waterforceexpensive", "r_waterforcereflectentities", "r_WaterDrawReflection", "r_WaterDrawRefraction", "r_ForceWaterLeaf",
	"mat_clipz", "r_eyewaterepsilon", "r_drawviewmodel", "r_drawentities",
	// C7 step 2: the per-view draws' switches.
	"r_drawopaquerenderables", "r_drawtranslucentrenderables", "r_drawtranslucentworld", "r_entityclips", "vcollide_wireframe",
	"r_drawopaqueworld", "r_DrawDetailProps", "cl_maxrenderable_dist", "r_worldlistcache",
	// C7 step 2: the 2D debug overlays' switches (viewdebug.cpp's Draw2DDebuggingInfo).
	"mat_yuv", "mat_hsv", "mat_showlightmappage", "cl_drawshadowtexture", "cl_shadowtextureoverlaysize", "mat_showwatertextures", "mat_wateroverlaysize", "mat_showcamerarendertarget", "mat_showframebuffertexture", "mat_framebuffercopyoverlaysize", "mat_drawTextureScale", "r_flashlightdrawdepth", "cl_overdraw_test",
};
const int kRenderCvarCount = ARRAYSIZE( kRenderCvars );
const int kRenderCvarFogColor = kRenderCvarCount;
const int kRenderCvarFogColorSkybox = kRenderCvarCount + 3;

const int kMaxRenderOps = 512;

//-----------------------------------------------------------------------------
// The world list cache, keyed as stock's CWorldListCache is: the CViewSetup
// fields that change what is visible (origin, angles, fov, near/far, aspect,
// ortho and off-centre extents), compared bitwise.
//-----------------------------------------------------------------------------
struct WorldListKey_t
{
	float m_flValues[ 18 ];
	int   m_nOrtho;
	int   m_nOffCenter;

	explicit WorldListKey_t( const CViewSetup &v )
	{
		memset( this, 0, sizeof( *this ) );
		float *f = m_flValues;
		*f++ = v.m_OrthoLeft; *f++ = v.m_OrthoTop; *f++ = v.m_OrthoRight; *f++ = v.m_OrthoBottom;
		*f++ = v.fov;
		*f++ = v.origin.x; *f++ = v.origin.y; *f++ = v.origin.z;
		*f++ = v.angles.x; *f++ = v.angles.y; *f++ = v.angles.z;
		*f++ = v.zNear; *f++ = v.zFar; *f++ = v.m_flAspectRatio;
		*f++ = v.m_flOffCenterTop; *f++ = v.m_flOffCenterBottom; *f++ = v.m_flOffCenterLeft; *f++ = v.m_flOffCenterRight;
		m_nOrtho = v.m_bOrtho ? 1 : 0;
		m_nOffCenter = v.m_bOffCenter ? 1 : 0;
	}
};

// A view's world list info: the engine's WorldListInfo_t, ref-counted, with
// the remap a pruned list carries from its leaf order to the unpruned one's
// (stock's ClientWorldListInfo_t's shape). A pruned list owns its arrays; an
// unpruned one points at what the engine's BuildWorldLists filled.
struct PlanWorldListInfo_t : public CRefCounted1<WorldListInfo_t>
{
	PlanWorldListInfo_t()
	{
		memset( static_cast<WorldListInfo_t *>( this ), 0, sizeof( WorldListInfo_t ) );
		m_pActualLeafIndex = NULL;
	}

	LeafIndex_t      *m_pActualLeafIndex;
	CUtlMemory<byte>  m_Owned;
};

typedef CRefCounted1<WorldListInfo_t> ListInfoBase_t;

inline ListInfoBase_t *ListInfo( PlanWorldListInfo_t *p ) { return p; }

struct WorldListEntry_t
{
	WorldListKey_t         m_Key;
	IWorldRenderList      *m_pList;
	PlanWorldListInfo_t   *m_pInfo;
};

//-----------------------------------------------------------------------------
// One view of the plan (C7 step 3 phase 1: no longer stock's CRendering3dView).
// It holds the view's setup and lists and carries out the list builds the plan
// asks for, with the plan's decisions; every draw over the lists is managed's
// view program. While it is the innermost open view, view->GetDrawFlags()
// answers its flags (C7 step 3: CShellViewRender's).
//-----------------------------------------------------------------------------
class CPlanView : public CRefCounted<>, protected CViewSetup
{
public:
	CPlanView() {}
	~CPlanView() { ReleaseLists(); }

	void Begin( const CViewSetup &setup, int nDrawFlags, int nClearFlags )
	{
		memcpy( static_cast<CViewSetup *>( this ), &setup, sizeof( setup ) );
		ReleaseLists();
		m_pRenderablesList = new CClientRenderablesList;
		m_DrawFlags = nDrawFlags;
		m_ClearFlags = nClearFlags;
	}

	int GetDrawFlags() const { return m_DrawFlags; }

	// BuildWorldRenderLists, with this file's list cache, and
	// UpdateRenderablesOpacity at the plan's prop-fade factor.
	void BuildWorld( CUtlVector<WorldListEntry_t> &cache, bool bDrawEntities, int iForceViewLeaf, bool bUseCache,
	                 bool bShadowDepth, float *pReflectionWaterHeight, float flOpacityFactor );

	// PruneWorldListInfo: only the leaves on the water side the draw flags ask for.
	void Prune();

	// BuildRenderableRenderLists, with SetupRenderablesList's decisions the plan made.
	void Renderables( int nViewID, bool bEntities, bool bDetail, bool bTranslucent, float flRenderDistSq );

	// C7 step 2: the view's lists, read by the scene gather and the program's ops.
	IWorldRenderList *WorldList() const { return m_pWorldRenderList; }
	WorldListInfo_t *WorldInfo() const { return m_pWorldListInfo; }
	CClientRenderablesList *RenderList() const { return m_bBuiltRenderables ? m_pRenderablesList : NULL; }

	// The view's draw program (managed's, from the scene) and where it is: the
	// next section a list-dependent draw op runs, the entries' renderables in
	// scene order, and the static props and detail leaves queued by its ops.
	bool                           m_bBuiltRenderables = false;
	bool                           m_bHasProgram = false;
	int                            m_nSectionStart = 0;
	CUtlVector<SourceSharpRenderOp_t> m_Program;
	CUtlVector<IClientRenderable *> m_Entries;
	CUtlVector<IClientRenderable *> m_Statics;
	CUtlVector<LeafIndex_t>         m_DetailLeaves;

	IClientRenderable *Entry( int e ) const { return ( e >= 0 && e < m_Entries.Count() ) ? m_Entries[e] : NULL; }

	// The next section's ops: [start, end) of m_Program; false when there is none.
	bool NextSection( int &nStart, int &nEnd )
	{
		if ( !m_bHasProgram || m_nSectionStart >= m_Program.Count() )
			return false;
		nStart = m_nSectionStart;
		nEnd = nStart;
		while ( nEnd < m_Program.Count() && m_Program[nEnd].Kind != RENDER_OP_SectionEnd )
			++nEnd;
		m_nSectionStart = nEnd + 1;
		return true;
	}

	void ShadowTextures()
	{
		if ( !m_pWorldListInfo )
			return;
		ShellShadows_ComputeShadowTextures( m_pWorldListInfo->m_LeafCount, m_pWorldListInfo->m_pLeafList );
	}

	void ReleaseLists()
	{
		SafeRelease( m_pWorldRenderList );
		SafeRelease( m_pRenderablesList );
		SafeRelease( m_pWorldListInfo );
	}

private:
	IWorldRenderList       *m_pWorldRenderList = NULL;
	CClientRenderablesList *m_pRenderablesList = NULL;
	PlanWorldListInfo_t    *m_pWorldListInfo = NULL;
	int                     m_DrawFlags = 0;
	int                     m_ClearFlags = 0;
};

void CPlanView::BuildWorld( CUtlVector<WorldListEntry_t> &cache, bool bDrawEntities, int iForceViewLeaf, bool bUseCache,
                            bool bShadowDepth, float *pReflectionWaterHeight, float flOpacityFactor )
{
	ConVarRef r_worldlistcache( "r_worldlistcache" );
	const bool bCache = bUseCache && r_worldlistcache.GetBool();
	bool bFound = false;
	if ( bCache )
	{
		WorldListKey_t key( *this );
		for ( int i = 0; i < cache.Count() && !bFound; ++i )
		{
			if ( memcmp( &cache[i].m_Key, &key, sizeof( key ) ) != 0 )
				continue;
			UpdateClientRenderableInPVSStatus();
			ShellViewRender_IncWorldListsNumber();
			m_pWorldRenderList = cache[i].m_pList;
			m_pWorldRenderList->AddRef();
			m_pWorldListInfo = cache[i].m_pInfo;
			m_pWorldListInfo->AddRef();
			bFound = true;
		}
	}

	if ( !bFound )
	{
		UpdateClientRenderableInPVSStatus();
		ShellViewRender_IncWorldListsNumber();
		m_pWorldRenderList = render->CreateWorldList();
		m_pWorldListInfo = new PlanWorldListInfo_t;
		render->BuildWorldLists( m_pWorldRenderList, m_pWorldListInfo, iForceViewLeaf, NULL, bShadowDepth, pReflectionWaterHeight );
		if ( bCache )
		{
			WorldListEntry_t entry = { WorldListKey_t( *this ), m_pWorldRenderList, m_pWorldListInfo };
			m_pWorldRenderList->AddRef();
			m_pWorldListInfo->AddRef();
			cache.AddToTail( entry );
		}
	}

	if ( bDrawEntities )
	{
		staticpropmgr->ComputePropOpacity( origin, flOpacityFactor );
		DetailObjectSystem()->BuildDetailObjectRenderLists( origin );
	}
}

void CPlanView::Prune()
{
	const int nWaterDrawFlags = m_DrawFlags & ( DF_RENDER_UNDERWATER | DF_RENDER_ABOVEWATER );
	if ( nWaterDrawFlags == ( DF_RENDER_UNDERWATER | DF_RENDER_ABOVEWATER ) || !m_pWorldListInfo )
		return;

	PlanWorldListInfo_t *pNew = new PlanWorldListInfo_t;
	pNew->m_ViewFogVolume = m_pWorldListInfo->m_ViewFogVolume;
	pNew->m_LeafCount = 0;
	const int nLeaves = m_pWorldListInfo->m_LeafCount;
	if ( nLeaves > 0 && nWaterDrawFlags )
	{
		pNew->m_Owned.EnsureCapacity( nLeaves * ( 2 * sizeof( LeafIndex_t ) + sizeof( LeafFogVolume_t ) ) );
		byte *pMemory = pNew->m_Owned.Base();
		pNew->m_pLeafList = (LeafIndex_t *)pMemory;
		pNew->m_pLeafFogVolume = (LeafFogVolume_t *)( pMemory + nLeaves * sizeof( LeafIndex_t ) );
		pNew->m_pActualLeafIndex = (LeafIndex_t *)( (byte *)pNew->m_pLeafFogVolume + nLeaves * sizeof( LeafFogVolume_t ) );

		const bool bUnderwater = ( nWaterDrawFlags & DF_RENDER_UNDERWATER ) != 0;
		for ( int i = 0; i < nLeaves; ++i )
		{
			const bool bLeafUnderwater = m_pWorldListInfo->m_pLeafFogVolume[i] != -1;
			if ( bUnderwater != bLeafUnderwater )
				continue;
			pNew->m_pLeafList[ pNew->m_LeafCount ] = m_pWorldListInfo->m_pLeafList[i];
			pNew->m_pLeafFogVolume[ pNew->m_LeafCount ] = m_pWorldListInfo->m_pLeafFogVolume[i];
			pNew->m_pActualLeafIndex[ pNew->m_LeafCount ] = i;
			++pNew->m_LeafCount;
		}
	}
	m_pWorldListInfo->Release();
	m_pWorldListInfo = pNew;
}

void CPlanView::Renderables( int nViewID, bool bEntities, bool bDetail, bool bTranslucent, float flRenderDistSq )
{
	MDLCACHE_CRITICAL_SECTION();
	const bool bShadowDepth = nViewID == VIEW_SHADOW_DEPTH_TEXTURE;
	if ( !bShadowDepth )
		render->BeginUpdateLightmaps();

	ShellViewRender_IncRenderablesListsNumber();
	WorldListInfo_t &info = *m_pWorldListInfo;

	for ( int i = 0; i < RENDER_GROUP_COUNT; ++i )
		m_pRenderablesList->m_RenderGroupCounts[i] = 0;
	if ( bEntities )
	{
		SetupRenderInfo_t setupInfo;
		setupInfo.m_pWorldListInfo = m_pWorldListInfo;
		setupInfo.m_nRenderFrame = ShellViewRender_RenderablesListsNumber();
		setupInfo.m_nDetailBuildFrame = view->BuildWorldListsNumber();
		setupInfo.m_pRenderList = m_pRenderablesList;
		setupInfo.m_bDrawDetailObjects = bDetail;
		setupInfo.m_bDrawTranslucentObjects = bTranslucent;
		setupInfo.m_vecRenderOrigin = origin;
		setupInfo.m_vecRenderForward = CurrentViewForward();
		setupInfo.m_flRenderDistSq = flRenderDistSq;
		// The leaf system is managed's (C7 cluster 12 L2): both calls are the
		// forwarder's, and each answers from the managed registry.
		if ( !bShadowDepth )
			ClientLeafSystem()->ComputeTranslucentRenderLeaf( info.m_LeafCount, info.m_pLeafList, info.m_pLeafFogVolume,
			                                                  ShellViewRender_RenderablesListsNumber(), nViewID );
		ClientLeafSystem()->BuildRenderablesList( setupInfo );
	}

	if ( nViewID == VIEW_MAIN )
		StudioStats_FindClosestEntity( m_pRenderablesList );

	if ( !bShadowDepth )
	{
		// The brush models' lightmaps, opaque and translucent.
		CClientRenderablesList::CEntry *pEntities = m_pRenderablesList->m_RenderGroups[RENDER_GROUP_OPAQUE_BRUSH];
		const int nOpaque = m_pRenderablesList->m_RenderGroupCounts[RENDER_GROUP_OPAQUE_BRUSH];
		for ( int i = 0; i < nOpaque; ++i )
			render->UpdateBrushModelLightmap( (model_t *)pEntities[i].m_pRenderable->GetModel(), pEntities[i].m_pRenderable );

		pEntities = m_pRenderablesList->m_RenderGroups[RENDER_GROUP_TRANSLUCENT_ENTITY];
		const int nTranslucent = m_pRenderablesList->m_RenderGroupCounts[RENDER_GROUP_TRANSLUCENT_ENTITY];
		for ( int i = 0; i < nTranslucent; ++i )
		{
			const model_t *pModel = pEntities[i].m_pRenderable->GetModel();
			if ( pModel && modelinfo->GetModelType( pModel ) == mod_brush )
				render->UpdateBrushModelLightmap( (model_t *)pModel, pEntities[i].m_pRenderable );
		}
		render->EndUpdateLightmaps();
	}
	m_bBuiltRenderables = true;
}

//-----------------------------------------------------------------------------
// The executor's state, one object made at the first frame.
//-----------------------------------------------------------------------------
struct ExecutorState_t
{
	bool                         m_bHooked = false;
	bool                         m_bPlanFrame = false;
	VTableSlotPatch::Patch       m_BuildRenderablesPatch = {};

	// The two textures UpdateShadowDepthTexture was handed, while its plan runs,
	// and that plan's ops (it runs inside the frame's plan, whose ops are m_Ops).
	ITexture                    *m_pShadowColor = NULL;
	ITexture                    *m_pShadowDepth = NULL;
	SourceSharpRenderOp_t        m_ShadowOps[ 256 ];

	// view.cpp's decisions, made by managed each frame (C7 step 3 phase 2) and,
	// until the eviction, held to what stock's CViewRender::Render handed
	// RenderView: the frame's decision, whether the frame's first RenderView is
	// still to be compared, and the mismatches since managed last heard.
	SourceSharpRenderFrameOut_t  m_FrameDecision = {};
	bool                         m_bDecisionPending = false;

	CMaterialReference           m_FreezeFrameMaterial;
	int32_t                      m_nLastPath = 0;
	float                        m_flLastRenderMs = 0.0f;

	ITexture                    *m_pSavedRenderTarget = NULL;
	unsigned int                 m_nLastVisFlags = 0;
	CUtlVector<CPlanView *>      m_Views;
	CUtlVector<ITexture *>       m_FrameBufferCopies;
	CUtlVector<bool>             m_ViewerSeen;
	CUtlVector<WorldListEntry_t> m_ListCache;
	Vector                       m_vSavedLightmapScale = Vector( -1, -1, -1 );
	unsigned char               *m_pSavedAreaBits = NULL;
	unsigned char                m_AreaBits[ 32 ] = {};
	CMaterialReference           m_UnderWaterOverlay;
	CMaterialReference           m_TranslucentSingleColor;
	CMaterialReference           m_ModulateSingleColor;
	char                         m_szUnderwaterOverlay[ MAX_PATH ] = {};

	// C7 post-processing: the histogram buckets' occlusion queries (created on
	// first use, kept for the process, as stock's), and the buckets the next
	// post crossing reads back.
	OcclusionQueryObjectHandle_t m_PostQueries[ 32 ] = {};
	int32_t                      m_nPostReadbackMask = 0;
	// Draws over a view's lists that found no program section (C7 step 3 phase 1).
	int32_t                      m_nProgramMisses = 0;
	// A post mesh being built (PostMeshBegin .. PostMeshEnd).
	IMesh                       *m_pPostMesh = NULL;
	CMeshBuilder                 m_PostMeshBuilder;
	bool                         m_bPostMeshIdentity = false;
	SourceSharpRenderOp_t        m_PostOps[ 512 ];

	SourceSharpRenderOp_t        m_Ops[ kMaxRenderOps ];

};

ExecutorState_t *g_pExecutor = NULL;

ExecutorState_t &State()
{
	if ( !g_pExecutor )
		g_pExecutor = new ExecutorState_t();
	return *g_pExecutor;
}

void ToViewSetup( const SourceSharpViewSetup_t &in, CViewSetup &out )
{
	out.x = in.X; out.y = in.Y; out.width = in.Width; out.height = in.Height;
	out.m_nUnscaledX = in.UnscaledX; out.m_nUnscaledY = in.UnscaledY;
	out.m_nUnscaledWidth = in.UnscaledWidth; out.m_nUnscaledHeight = in.UnscaledHeight;
	out.m_eStereoEye = (StereoEye_t)in.StereoEye;
	out.m_bOrtho = in.Ortho != 0;
	out.m_OrthoLeft = in.OrthoLeft; out.m_OrthoTop = in.OrthoTop; out.m_OrthoRight = in.OrthoRight; out.m_OrthoBottom = in.OrthoBottom;
	out.fov = in.Fov; out.fovViewmodel = in.FovViewmodel;
	out.origin.Init( in.OriginXYZ[0], in.OriginXYZ[1], in.OriginXYZ[2] );
	out.angles.Init( in.AnglesXYZ[0], in.AnglesXYZ[1], in.AnglesXYZ[2] );
	out.zNear = in.ZNear; out.zFar = in.ZFar; out.zNearViewmodel = in.ZNearViewmodel; out.zFarViewmodel = in.ZFarViewmodel;
	out.m_bRenderToSubrectOfLargerScreen = in.RenderToSubrect != 0;
	out.m_flAspectRatio = in.AspectRatio;
	out.m_bOffCenter = in.OffCenter != 0;
	out.m_flOffCenterTop = in.OffCenterTop; out.m_flOffCenterBottom = in.OffCenterBottom;
	out.m_flOffCenterLeft = in.OffCenterLeft; out.m_flOffCenterRight = in.OffCenterRight;
	out.m_bDoBloomAndToneMapping = in.DoBloomAndToneMapping != 0;
	out.m_bCacheFullSceneState = in.CacheFullSceneState != 0;
	out.m_bViewToProjectionOverride = in.ViewToProjectionOverride != 0;
	memcpy( out.m_ViewToProjection.Base(), in.ViewToProjection, sizeof( in.ViewToProjection ) );
}

void FromViewSetup( const CViewSetup &in, SourceSharpViewSetup_t &out )
{
	memset( &out, 0, sizeof( out ) );
	out.X = in.x; out.Y = in.y; out.Width = in.width; out.Height = in.height;
	out.UnscaledX = in.m_nUnscaledX; out.UnscaledY = in.m_nUnscaledY;
	out.UnscaledWidth = in.m_nUnscaledWidth; out.UnscaledHeight = in.m_nUnscaledHeight;
	out.StereoEye = (int32_t)in.m_eStereoEye;
	out.Ortho = in.m_bOrtho ? 1 : 0;
	out.OrthoLeft = in.m_OrthoLeft; out.OrthoTop = in.m_OrthoTop; out.OrthoRight = in.m_OrthoRight; out.OrthoBottom = in.m_OrthoBottom;
	out.Fov = in.fov; out.FovViewmodel = in.fovViewmodel;
	out.OriginXYZ[0] = in.origin.x; out.OriginXYZ[1] = in.origin.y; out.OriginXYZ[2] = in.origin.z;
	out.AnglesXYZ[0] = in.angles.x; out.AnglesXYZ[1] = in.angles.y; out.AnglesXYZ[2] = in.angles.z;
	out.ZNear = in.zNear; out.ZFar = in.zFar; out.ZNearViewmodel = in.zNearViewmodel; out.ZFarViewmodel = in.zFarViewmodel;
	out.RenderToSubrect = in.m_bRenderToSubrectOfLargerScreen ? 1 : 0;
	out.AspectRatio = in.m_flAspectRatio;
	out.OffCenter = in.m_bOffCenter ? 1 : 0;
	out.OffCenterTop = in.m_flOffCenterTop; out.OffCenterBottom = in.m_flOffCenterBottom;
	out.OffCenterLeft = in.m_flOffCenterLeft; out.OffCenterRight = in.m_flOffCenterRight;
	out.DoBloomAndToneMapping = in.m_bDoBloomAndToneMapping ? 1 : 0;
	out.CacheFullSceneState = in.m_bCacheFullSceneState ? 1 : 0;
	out.ViewToProjectionOverride = in.m_bViewToProjectionOverride ? 1 : 0;
	memcpy( out.ViewToProjection, in.m_ViewToProjection.Base(), sizeof( out.ViewToProjection ) );
}

void FromFog( const fogparams_t *pFog, SourceSharpFogParams_t &out )
{
	memset( &out, 0, sizeof( out ) );
	if ( !pFog )
		return;
	out.Present = 1;
	Vector dir = pFog->dirPrimary;
	out.DirPrimaryXYZ[0] = dir.x; out.DirPrimaryXYZ[1] = dir.y; out.DirPrimaryXYZ[2] = dir.z;
	color32 c;
	c = pFog->colorPrimary;         out.ColorPrimary[0] = c.r; out.ColorPrimary[1] = c.g; out.ColorPrimary[2] = c.b;
	c = pFog->colorSecondary;       out.ColorSecondary[0] = c.r; out.ColorSecondary[1] = c.g; out.ColorSecondary[2] = c.b;
	c = pFog->colorPrimaryLerpTo;   out.ColorPrimaryLerpTo[0] = c.r; out.ColorPrimaryLerpTo[1] = c.g; out.ColorPrimaryLerpTo[2] = c.b;
	c = pFog->colorSecondaryLerpTo; out.ColorSecondaryLerpTo[0] = c.r; out.ColorSecondaryLerpTo[1] = c.g; out.ColorSecondaryLerpTo[2] = c.b;
	out.Start = pFog->start;
	out.End = pFog->end;
	out.MaxDensity = pFog->maxdensity;
	out.StartLerpTo = pFog->startLerpTo;
	out.EndLerpTo = pFog->endLerpTo;
	out.LerpTime = pFog->lerptime;
	out.Duration = pFog->duration;
	out.Enable = pFog->enable ? 1 : 0;
	out.Blend = pFog->blend ? 1 : 0;
	out.Radial = pFog->radial ? 1 : 0;
}

void ParseColor( const char *pszValue, float *pOut )
{
	pOut[0] = pOut[1] = pOut[2] = -1.0f;
	if ( pszValue )
		sscanf( pszValue, "%f%f%f", pOut, pOut + 1, pOut + 2 );
}

// The water material's switch named pszVar: defined (1/0) and its int value.
void WaterVar( IMaterial *pMaterial, const char *pszVar, int32_t *pDefined, int32_t *pValue )
{
	IMaterialVar *pVar = pMaterial->FindVar( pszVar, NULL, false );
	*pDefined = ( pVar && pVar->IsDefined() ) ? 1 : 0;
	*pValue = *pDefined ? pVar->GetIntValueFast() : 0;
}

int32_t WaterTextureVar( IMaterial *pMaterial, const char *pszVar )
{
	IMaterialVar *pVar = pMaterial->FindVar( pszVar, NULL, false );
	return ( pVar && pVar->GetType() == MATERIAL_VAR_TYPE_TEXTURE ) ? 1 : 0;
}

// DoesViewPlaneIntersectWater's answer for the view, from the matrices the
// view's Push3DView sets (GetMatricesForView), asked before anything is pushed.
int32_t ViewPlaneIntersectsWater( const CViewSetup &setup, float waterZ, int leafWaterDataID )
{
	if ( leafWaterDataID == -1 )
		return 0;

	VMatrix worldToView, viewToProjection, worldToProjection, worldToPixels;
	render->GetMatricesForView( setup, &worldToView, &viewToProjection, &worldToProjection, &worldToPixels );
	VMatrix viewProjection, inverse;
	MatrixMultiply( viewToProjection, worldToView, viewProjection );
	MatrixInverseGeneral( viewProjection, inverse );

	Vector mins, maxs;
	ClearBounds( mins, maxs );
	Vector points[4];
	points[0].Init( -1.0f, -1.0f, 0.0f );
	points[1].Init( -1.0f, 1.0f, 0.0f );
	points[2].Init( 1.0f, -1.0f, 0.0f );
	points[3].Init( 1.0f, 1.0f, 0.0f );
	bool bAbove = false, bBelow = false;
	const float fudge = 7.0f;
	for ( int i = 0; i < 4; ++i )
	{
		Vector world;
		Vector3DMultiplyPositionProjective( inverse, points[i], world );
		AddPointToBounds( world, mins, maxs );
		if ( world.z + fudge > waterZ )
			bAbove = true;
		if ( world.z - fudge < waterZ )
			bBelow = true;
	}
	if ( !( bAbove && bBelow ) )
		return 0;
	Vector vecFudge( fudge, fudge, fudge );
	mins -= vecFudge;
	maxs += vecFudge;
	return render->DoesBoxIntersectWaterVolume( mins, maxs, leafWaterDataID ) ? 1 : 0;
}

//-----------------------------------------------------------------------------
// RenderView's inputs, gathered at its entry. Nothing here is decided.
//-----------------------------------------------------------------------------
void GatherInputs( const CViewSetup &setup, int nClearFlags, int whatToDraw, SourceSharpRenderInputs_t &in )
{
	ExecutorState_t &state = State();
	memset( &in, 0, sizeof( in ) );

	FromViewSetup( setup, in.View );
	in.ClearFlags = nClearFlags;
	in.WhatToDraw = whatToDraw;
	in.CurTime = gpGlobals->curtime;
	in.FrameTime = gpGlobals->frametime;
	in.FrameCount = gpGlobals->framecount;
	in.MaxClients = gpGlobals->maxClients;
	in.HdrType = g_pMaterialSystemHardwareConfig->GetHDRType();
	in.DxLevel = engine->GetDXSupportLevel();
	in.UseFastClipping = g_pMaterialSystemHardwareConfig->UseFastClipping() ? 1 : 0;
	in.IsHammerRunning = engine->IsHammerRunning() ? 1 : 0;
	in.ScreenAspect = engine->GetScreenAspectRatio();
	in.ClientModeDrawFog = g_pClientMode->ShouldDrawFog() ? 1 : 0;
	in.ClientModeDrawDetail = g_pClientMode->ShouldDrawDetailObjects() ? 1 : 0;
	// The freeze frame's state is managed's (RenderPlanHost fills these three).
	in.FreezeActive = 0;
	in.FreezeTake = 0;
	in.FreezeFlash = 0.0f;
	in.TakingScreenshot = engine->IsTakingScreenshot() ? 1 : 0;
	in.ForceNoVis = ShellViewRender_ForceNoVis() ? 1 : 0;
	in.PointContentsSolid = enginetrace->GetPointContents( setup.origin ) == CONTENTS_SOLID ? 1 : 0;

	in.SkyboxVisible = (int32_t)engine->IsSkyboxVisibleFromPoint( setup.origin );
	if ( sky3dparams_t *pSky = SourceSharp_ViewerSkyboxParams() )
	{
		in.SkyPresent = 1;
		in.SkyScale = pSky->scale;
		Vector origin = pSky->origin;
		in.SkyOriginXYZ[0] = origin.x; in.SkyOriginXYZ[1] = origin.y; in.SkyOriginXYZ[2] = origin.z;
		in.SkyArea = pSky->area;
		FromFog( &pSky->fog, in.SkyFog );
	}
	FromFog( SourceSharp_ViewerFogParams(), in.Fog );

	VisibleFogVolumeInfo_t fog;
	render->GetVisibleFogVolume( setup.origin, &fog );
	in.FogVolume = fog.m_nVisibleFogVolume;
	in.FogVolumeLeaf = fog.m_nVisibleFogVolumeLeaf;
	in.EyeInFogVolume = fog.m_bEyeInFogVolume ? 1 : 0;
	in.DistanceToWater = fog.m_flDistanceToWater;
	in.WaterHeight = fog.m_flWaterHeight;
	state.m_szUnderwaterOverlay[0] = 0;
	if ( IMaterial *pWater = fog.m_pFogVolumeMaterial )
	{
		in.WaterMaterial = 1;
		in.WaterTranslucent = pWater->IsTranslucent() ? 1 : 0;
		WaterVar( pWater, "$forcecheap", &in.WaterForceCheapDefined, &in.WaterForceCheap );
		WaterVar( pWater, "$forceexpensive", &in.WaterForceExpensiveDefined, &in.WaterForceExpensive );
		in.WaterReflectTexture = WaterTextureVar( pWater, "$reflecttexture" );
		in.WaterRefractTexture = WaterTextureVar( pWater, "$refracttexture" );
		IMaterialVar *pReflectEntities = pWater->FindVar( "$reflectentities", NULL, false );
		in.WaterReflectEntities = ( pReflectEntities && pReflectEntities->GetIntValueFast() != 0 ) ? 1 : 0;
		IMaterialVar *pOverlay = pWater->FindVar( "$underwateroverlay", NULL, false );
		if ( pOverlay && pOverlay->IsDefined() )
		{
			const char *pszOverlay = pOverlay->GetStringValue();
			in.WaterUnderwaterOverlay = ( pszOverlay[0] != '0' ) ? 2 : 1;
			V_strncpy( state.m_szUnderwaterOverlay, pszOverlay, sizeof( state.m_szUnderwaterOverlay ) );
		}
	}
	if ( fog.m_nVisibleFogVolume != -1 )
	{
		in.ViewPlaneIntersectsWater = ViewPlaneIntersectsWater( setup, fog.m_flWaterHeight, fog.m_nVisibleFogVolume );
		ITexture *pReflection = ShellRenderTarget( SS_RT_WaterReflection );
		ITexture *pRefraction = ShellRenderTarget( SS_RT_WaterRefraction );
		in.ReflectionWidth = pReflection ? pReflection->GetActualWidth() : 0;
		in.ReflectionHeight = pReflection ? pReflection->GetActualHeight() : 0;
		in.RefractionWidth = pRefraction ? pRefraction->GetActualWidth() : 0;
		in.RefractionHeight = pRefraction ? pRefraction->GetActualHeight() : 0;
	}
	view->GetWaterLODParams( in.CheapWaterStart, in.CheapWaterEnd );

	cplane_t glass;
	in.GlassInView = IsReflectiveGlassInView( setup, glass ) ? 1 : 0;
	if ( in.GlassInView )
	{
		in.GlassPlane[0] = glass.normal.x; in.GlassPlane[1] = glass.normal.y; in.GlassPlane[2] = glass.normal.z;
		in.GlassPlane[3] = glass.dist;
	}

	in.ThirdPersonSeesOwner = SourceSharp_ThirdPersonSeesOwner() ? 1 : 0;
	in.ViewEntity = render->GetViewEntity();

	// The screen fade, managed's (ViewEffects): stock's GetFadeParams.
	int32_t fade[5] = { 0, 0, 0, 0, 0 };
	if ( IManagedRenderClient *p = ManagedRenderClient() )
		p->ViewEffectsFadeParams( gpGlobals->curtime, fade );
	in.FadeColor[0] = (byte)fade[0]; in.FadeColor[1] = (byte)fade[1]; in.FadeColor[2] = (byte)fade[2]; in.FadeColor[3] = (byte)fade[3];
	in.FadeBlend = fade[4] != 0 ? 1 : 0;

	if ( IMaterial *pOverlay = view->GetScreenOverlayMaterial() )
	{
		in.ScreenOverlay = 1;
		in.ScreenOverlayFullFrame = pOverlay->NeedsFullFrameBufferTexture() ? 1 : 0;
		in.ScreenOverlayPowerOfTwo = pOverlay->NeedsPowerOfTwoFrameBufferTexture() ? 1 : 0;
	}
	in.FlashlightOn = SourceSharp_ViewerEffectActive( EF_DIMLIGHT ) ? 1 : 0;

	for ( int i = 0; i < kRenderCvarCount; ++i )
	{
		ConVarRef var( kRenderCvars[i], true );
		in.Cvars[i] = var.IsValid() ? var.GetFloat() : 0.0f;
	}
	ConVarRef fogColor( "fog_color", true ), fogColorSkybox( "fog_colorskybox", true );
	ParseColor( fogColor.IsValid() ? fogColor.GetString() : NULL, &in.Cvars[ kRenderCvarFogColor ] );
	ParseColor( fogColorSkybox.IsValid() ? fogColorSkybox.GetString() : NULL, &in.Cvars[ kRenderCvarFogColorSkybox ] );
}

// A struct as recorded lines: its bytes in hex, so managed reads back exactly
// what this frame had (RenderPlanFixtureTests). `in`/`inbytes` are the frame's
// RenderInputs; `scene`/`scenebytes` one view's scene.
void NoteBytes( const char *pszHead, const char *pszBytesPrefix, const void *pData, int n )
{
	static const char kHex[] = "0123456789abcdef";
	const unsigned char *p = (const unsigned char *)pData;
	CUtlVector<char> hex;
	hex.SetCount( n * 2 + 1 );
	for ( int i = 0; i < n; ++i )
	{
		hex[ i * 2 ] = kHex[ p[i] >> 4 ];
		hex[ i * 2 + 1 ] = kHex[ p[i] & 15 ];
	}
	hex[ n * 2 ] = 0;
	RenderTrace::Note( "%s\tsize=%d", pszHead, n );
	RenderTrace::NoteLong( pszBytesPrefix, hex.Base() );
}

void NoteInputs( const SourceSharpRenderInputs_t &in )
{
	NoteBytes( "in", "inbytes\t", &in, (int)sizeof( in ) );
}

// A traced frame's surroundings by name (C7 step 3's frozen recordings): every
// convar's value (`cvar`) and the engine's answers post-processing reads
// (`env`). Keyed by name rather than laid out as a struct, so a recording made
// before the stock path left still answers for an input a later port adds.
void NoteFrameEnvironment()
{
	CMatRenderContextPtr pRenderContext( materials );
	const Vector vScale = pRenderContext->GetToneMappingScaleLinear();
	int nVx = 0, nVy = 0, nVw = 0, nVh = 0;
	pRenderContext->GetViewport( nVx, nVy, nVw, nVh );
	RenderTrace::Note( "env\trealtime\t%.9g", gpGlobals->realtime );
	RenderTrace::Note( "env\tcurtime\t%.9g", gpGlobals->curtime );
	RenderTrace::Note( "env\tframetime\t%.9g", gpGlobals->frametime );
	RenderTrace::Note( "env\thdrtype\t%d", (int)g_pMaterialSystemHardwareConfig->GetHDRType() );
	RenderTrace::Note( "env\tdxlevel\t%d", engine->GetDXSupportLevel() );
	RenderTrace::Note( "env\thwdxlevel\t%d", g_pMaterialSystemHardwareConfig->GetDXSupportLevel() );
	RenderTrace::Note( "env\tmaphashdrlighting\t%d", engine->MapHasHDRLighting() ? 1 : 0 );
	RenderTrace::Note( "env\tcandosrgbreadfromrts\t%d", g_pMaterialSystemHardwareConfig->CanDoSRGBReadFromRTs() ? 1 : 0 );
	RenderTrace::Note( "env\tfakesrgbwrite\t%d", g_pMaterialSystemHardwareConfig->FakeSRGBWrite() ? 1 : 0 );
	RenderTrace::Note( "env\tsupportsps20\t%d", g_pMaterialSystemHardwareConfig->SupportsPixelShaders_2_0() ? 1 : 0 );
	RenderTrace::Note( "env\ttonemapscalelinear\t%.9g,%.9g,%.9g", vScale.x, vScale.y, vScale.z );
	RenderTrace::Note( "env\tviewport\t%d,%d,%d,%d", nVx, nVy, nVw, nVh );
	// Session history the frame starts from: a frame's restore of its copy
	// texture names it (RenderTrace.FrameStartCopyName).
	ITexture *pCopy = pRenderContext->GetFrameBufferCopyTexture( 0 );
	RenderTrace::Note( "env\tfbcopy0\t%s", pCopy ? pCopy->GetName() : "-" );
	RenderTrace::Note( "env\tvideomodeheight\t%d", g_pMaterialSystem->GetCurrentConfigForVideoCard().m_VideoMode.m_Height );
	RenderTrace::Note( "env\tgamedirectory\t%s", engine->GetGameDirectory() );
	RenderTrace::Note( "env\tcustomtonemap\t%d,%d,%d,%.9g,%.9g,%.9g,%.9g", g_bUseCustomAutoExposureMin ? 1 : 0,
	                   g_bUseCustomAutoExposureMax ? 1 : 0, g_bUseCustomBloomScale ? 1 : 0, g_flCustomAutoExposureMin,
	                   g_flCustomAutoExposureMax, g_flCustomBloomScale, g_flCustomBloomScaleMinimum );

	ICvar::Iterator iter( g_pCVar );
	for ( iter.SetFirst(); iter.IsValid(); iter.Next() )
	{
		ConCommandBase *pBase = iter.Get();
		if ( !pBase || pBase->IsCommand() )
			continue;
		const ConVar *pVar = static_cast<const ConVar *>( pBase );
		RenderTrace::Note( "cvar\t%s\t%s", pVar->GetName(), pVar->GetString() );
	}
}

//-----------------------------------------------------------------------------
// A view's scene (C7 step 2): what its lists hold, in SourceSharpViewScene_t's
// shape, one buffer: the header, then the leaves, then the entries.
//-----------------------------------------------------------------------------


struct ViewSceneBuffer_t
{
	CUtlVector<unsigned char> m_Bytes;

	SourceSharpViewScene_t *Header() { return (SourceSharpViewScene_t *)m_Bytes.Base(); }
	SourceSharpViewSceneLeaf_t *Leaves() { return (SourceSharpViewSceneLeaf_t *)( m_Bytes.Base() + sizeof( SourceSharpViewScene_t ) ); }
	SourceSharpViewSceneEntry_t *Entries()
	{
		return (SourceSharpViewSceneEntry_t *)( m_Bytes.Base() + sizeof( SourceSharpViewScene_t ) +
		                                        Header()->LeafCount * sizeof( SourceSharpViewSceneLeaf_t ) );
	}
};

COMPILE_TIME_ASSERT( RENDER_GROUP_COUNT == 14 );

// Fills the buffer from a view's world list info and renderables list. A null
// world list leaves every leaf's translucent answers 0 (the recorder's case:
// stock's own asks are its `q` lines).
void BuildViewScene( ViewSceneBuffer_t &out, WorldListInfo_t *pInfo, CClientRenderablesList *pList, int nDetailFrame,
                     IWorldRenderList *pWorld, const int nTranslucentFlags[2], int nOpIndex )
{
	const int nLeaves = pInfo ? pInfo->m_LeafCount : 0;
	int nEntries = 0;
	if ( pList )
		for ( int g = 0; g < RENDER_GROUP_COUNT; ++g )
			nEntries += pList->m_RenderGroupCounts[g];

	out.m_Bytes.SetCount( sizeof( SourceSharpViewScene_t ) + nLeaves * sizeof( SourceSharpViewSceneLeaf_t ) +
	                      nEntries * sizeof( SourceSharpViewSceneEntry_t ) );
	memset( out.m_Bytes.Base(), 0, out.m_Bytes.Count() );
	SourceSharpViewScene_t *pHeader = out.Header();
	pHeader->LeafCount = nLeaves;
	pHeader->EntryCount = nEntries;
	pHeader->TranslucentFlags[0] = nTranslucentFlags[0];
	pHeader->TranslucentFlags[1] = nTranslucentFlags[1];
	pHeader->HasRenderables = pList ? 1 : 0;
	pHeader->OpIndex = nOpIndex;

	// A pruned list's remap; stock's ClientWorldListInfo_t (the stock path's
	// lists) has the same shape after its base.
	const LeafIndex_t *pActual = pInfo ? static_cast<PlanWorldListInfo_t *>( static_cast<CRefCounted1<WorldListInfo_t> *>( pInfo ) )->m_pActualLeafIndex : NULL;
	SourceSharpViewSceneLeaf_t *pLeaves = out.Leaves();
	// Decision 3 for every leaf of the view in one crossing (the leaf system
	// is managed's, C7 cluster 12 L2); none drawn with no managed answer.
	CUtlVector< int32_t > detail;
	detail.SetCount( nLeaves );
	for ( int i = 0; i < nLeaves; ++i )
		detail[i] = 0;
	if ( IManagedLeafSystemClient *pLeafSystem = nLeaves > 0 ? ManagedLeafSystemClient() : NULL )
		pLeafSystem->DetailLeaves( nDetailFrame, pInfo->m_pLeafList, nLeaves, detail.Base() );
	for ( int i = 0; i < nLeaves; ++i )
	{
		SourceSharpViewSceneLeaf_t &leaf = pLeaves[i];
		leaf.Leaf = pInfo->m_pLeafList[i];
		leaf.ActualLeaf = pActual ? pActual[i] : i;
		leaf.Detail = detail[i] ? 1 : 0;
		if ( pWorld )
		{
			leaf.Translucent[0] = render->LeafContainsTranslucentSurfaces( pWorld, leaf.ActualLeaf, nTranslucentFlags[0] ) ? 1 : 0;
			leaf.Translucent[1] = render->LeafContainsTranslucentSurfaces( pWorld, leaf.ActualLeaf, nTranslucentFlags[1] ) ? 1 : 0;
		}
	}

	if ( !pList )
		return;
	SourceSharpViewSceneEntry_t *pEntry = out.Entries();
	for ( int g = 0; g < RENDER_GROUP_COUNT; ++g )
	{
		pHeader->GroupCounts[g] = pList->m_RenderGroupCounts[g];
		for ( int i = 0; i < pList->m_RenderGroupCounts[g]; ++i, ++pEntry )
		{
			const CClientRenderablesList::CEntry &e = pList->m_RenderGroups[g][i];
			pEntry->Group = g;
			pEntry->Leaf = e.m_iWorldListInfoLeaf;
			pEntry->EntIndex = -1;
			IClientRenderable *pRenderable = e.m_pRenderable;
			if ( !pRenderable )
				continue;
			int flags = 1;
			if ( e.m_TwoPass ) flags |= 2;
			if ( pRenderable->IgnoresZBuffer() ) flags |= 4;
			if ( pRenderable->UsesPowerOfTwoFrameBufferTexture() ) flags |= 8;
			if ( pRenderable->UsesFullFrameBufferTexture() ) flags |= 16;
			const model_t *pModel = pRenderable->GetModel();
			if ( pModel && modelinfo->GetModelType( pModel ) == mod_brush ) flags |= 32;
			if ( pRenderable == g_pStudioStatsEntity ) flags |= 64;
			pEntry->Flags = flags;
			IClientUnknown *pUnknown = pRenderable->GetIClientUnknown();
			IClientEntity *pEntity = pUnknown ? pUnknown->GetIClientEntity() : NULL;
			pEntry->EntIndex = pEntity ? pEntity->entindex() : -1;
			pEntry->FxBlend = pRenderable->GetFxBlend();
			pRenderable->GetColorModulation( pEntry->Color );
		}
	}
}

//-----------------------------------------------------------------------------
// The ops
//-----------------------------------------------------------------------------
ITexture *Target( int nTarget )
{
	switch ( nTarget )
	{
	case RENDER_TARGET_FullFrameFB0: return ShellRenderTarget( SS_RT_FullFrameFrameBuffer0 );
	case RENDER_TARGET_WaterReflection: return ShellRenderTarget( SS_RT_WaterReflection );
	case RENDER_TARGET_WaterRefraction: return ShellRenderTarget( SS_RT_WaterRefraction );
	case RENDER_TARGET_Saved: return State().m_pSavedRenderTarget;
	case RENDER_TARGET_ViewTarget0: return ShellViewTargets_Texture( 0 );
	case RENDER_TARGET_ShadowColor: return State().m_pShadowColor;
	case RENDER_TARGET_ShadowDepth: return State().m_pShadowDepth;
	default: return NULL;
	}
}

CPlanView *CurrentView()
{
	ExecutorState_t &state = State();
	return state.m_Views.Count() ? state.m_Views.Tail() : NULL;
}

void SetClearColorToFogColor()
{
	unsigned char ucFogColor[3];
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->GetFogColor( ucFogColor );
	if ( g_pMaterialSystemHardwareConfig->GetHDRType() == HDR_TYPE_INTEGER )
	{
		float scale = LinearToGammaFullRange( pRenderContext->GetToneMappingScaleLinear().x );
		ucFogColor[0] *= scale;
		ucFogColor[1] *= scale;
		ucFogColor[2] *= scale;
	}
	pRenderContext->ClearColor4ub( ucFogColor[0], ucFogColor[1], ucFogColor[2], 255 );
}

bool UpdateRefractIfNeededByList( CUtlVector< IClientRenderable * > &list )
{
	for ( int i = 0; i < list.Count(); ++i )
	{
		IClientRenderable *pRenderable = list[i]->GetIClientUnknown()->GetClientRenderable();
		if ( pRenderable->UsesPowerOfTwoFrameBufferTexture() )
		{
			UpdateRefractTexture();
			return true;
		}
	}
	return false;
}

void DrawRenderablesInList( CUtlVector< IClientRenderable * > &list, int flags )
{
	for ( int i = 0; i < list.Count(); ++i )
	{
		IClientRenderable *pRenderable = list[i]->GetIClientUnknown()->GetClientRenderable();
		if ( pRenderable->ShouldDraw() )
			pRenderable->DrawModel( STUDIO_RENDER | flags );
	}
}

// The viewmodels' pass: managed chose the view and whether the player's
// viewmodel draws. (Stock also drew the tools' viewmodels and filtered one kind
// out when ToolsEnabled(); that arm is deleted: SFM is dead, user ruling
// 2026-10-19.)
void ViewModels( const CViewSetup &setup, bool bDrawPlayer )
{
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->MatrixMode( MATERIAL_PROJECTION );
	pRenderContext->PushMatrix();

	float one[4] = { 1.0f, 1.0f, 1.0f, 1.0f };
	render->SetColorModulation( one );
	render->SetBlend( 1.0f );
	render->Push3DView( setup, 0, NULL, view->GetFrustum(), NULL );
	pRenderContext->DepthRange( 0.0f, 0.1f );

	if ( bDrawPlayer )
	{
		CUtlVector< IClientRenderable * > opaque( 32 );
		CUtlVector< IClientRenderable * > translucent( 32 );
		ClientLeafSystem()->CollateViewModelRenderables( opaque, translucent );
		if ( !UpdateRefractIfNeededByList( opaque ) )
			UpdateRefractIfNeededByList( translucent );
		DrawRenderablesInList( opaque, 0 );
		DrawRenderablesInList( translucent, STUDIO_TRANSPARENCY );
	}

	pRenderContext->DepthRange( 0.0f, 1.0f );
	render->PopView( view->GetFrustum() );
	pRenderContext->MatrixMode( MATERIAL_PROJECTION );
	pRenderContext->PopMatrix();
}

// A full-screen material, the three ways stock draws one (ScreenOverlayBranch).
void DrawOverlayMaterial( IMaterial *pMaterial, int nBranch, int x, int y, int w, int h )
{
	if ( nBranch == 1 )
	{
		DrawScreenEffectMaterial( pMaterial, x, y, w, h );
	}
	else if ( nBranch == 2 )
	{
		UpdateRefractTexture( x, y, w, h, true );
		CMatRenderContextPtr pRenderContext( materials );
		ITexture *pTexture = ShellRenderTarget( SS_RT_PowerOfTwoFrameBuffer );
		int sw = pTexture->GetActualWidth();
		int sh = pTexture->GetActualHeight();
		pRenderContext->DrawScreenSpaceRectangle( pMaterial, 0, 0, w, h, 0, 0, sw - 1, sh - 1, sw, sh );
	}
	else if ( nBranch == 3 )
	{
		byte color[4] = { 255, 255, 255, 255 };
		render->ViewDrawFade( color, pMaterial );
	}
}

void UnderwaterOverlay()
{
	IMaterial *pOverlay = State().m_UnderWaterOverlay;
	if ( !pOverlay )
		return;
	CMatRenderContextPtr pRenderContext( materials );
	int x, y, w, h;
	pRenderContext->GetViewport( x, y, w, h );
	if ( pOverlay->NeedsFullFrameBufferTexture() )
		DrawScreenEffectMaterial( pOverlay, x, y, w, h );
	else if ( pOverlay->NeedsPowerOfTwoFrameBufferTexture() )
		DrawOverlayMaterial( pOverlay, 2, x, y, w, h );
	else
		pRenderContext->DrawScreenSpaceRectangle( pOverlay, 0, 0, w, h, 0, 0, 1, 1, 1, 1 );
}

void HudPaint( const CViewSetup &setup )
{
	const int viewWidth = setup.m_nUnscaledWidth;
	const int viewHeight = setup.m_nUnscaledHeight;

	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->PushRenderTargetAndViewport( NULL, NULL, setup.m_nUnscaledX, setup.m_nUnscaledY, viewWidth, viewHeight );
	pRenderContext.SafeRelease();

	VGui_PreRender();
	vgui::VPANEL root = enginevgui->GetPanel( PANEL_CLIENTDLL );
	if ( root != 0 )
		vgui::ipanel()->SetSize( root, viewWidth, viewHeight );
	root = enginevgui->GetPanel( PANEL_CLIENTDLL_TOOLS );
	if ( root != 0 )
		vgui::ipanel()->SetSize( root, viewWidth, viewHeight );

	AllowCurrentViewAccess( true );
	render->VGui_Paint( PAINT_INGAMEPANELS );
	AllowCurrentViewAccess( false );
	VGui_PostRender();
	g_pClientMode->PostRenderVGui();

	pRenderContext.GetFrom( materials );
	pRenderContext->PopRenderTargetAndViewport();
	pRenderContext->Flush();
	pRenderContext.SafeRelease();
}

void Upscale( const CViewSetup &setup )
{
	CMatRenderContextPtr pRenderContext( materials );
	ITexture *pFullFrameFB1 = materials->FindTexture( "_rt_FullFrameFB1", TEXTURE_GROUP_RENDER_TARGET );
	IMaterial *pCopyMaterial = materials->FindMaterial( "dev/upscale", TEXTURE_GROUP_OTHER );
	pCopyMaterial->IncrementReferenceCount();
	Rect_t down, up;
	down.x = setup.x; down.y = setup.y; down.width = setup.width; down.height = setup.height;
	up.x = setup.m_nUnscaledX; up.y = setup.m_nUnscaledY; up.width = setup.m_nUnscaledWidth; up.height = setup.m_nUnscaledHeight;
	pRenderContext->CopyRenderTargetToTextureEx( pFullFrameFB1, 0, &down, &down );
	pRenderContext->DrawScreenSpaceRectangle( pCopyMaterial, up.x, up.y, up.width, up.height,
		down.x, down.y, down.x + down.width - 1, down.y + down.height - 1,
		pFullFrameFB1->GetActualWidth(), pFullFrameFB1->GetActualHeight() );
	pCopyMaterial->DecrementReferenceCount();
}

// CFreezeFrameView::Setup's material and Draw's two rectangles (mono): the
// captured frame, then the flash at the alpha managed worked out.
void FreezeFrameDraw( int x, int y, int w, int h, int nFlashAlpha )
{
	ExecutorState_t &state = State();
	if ( !state.m_FreezeFrameMaterial.IsValid() )
	{
		KeyValues *pVMTKeyValues = new KeyValues( "UnlitGeneric" );
		pVMTKeyValues->SetString( "$basetexture", "_rt_FullScreen" );
		pVMTKeyValues->SetInt( "$nocull", 1 );
		pVMTKeyValues->SetInt( "$nofog", 1 );
		pVMTKeyValues->SetInt( "$ignorez", 1 );
		state.m_FreezeFrameMaterial.Init( "FreezeFrame_FullScreen", TEXTURE_GROUP_OTHER, pVMTKeyValues );
		state.m_FreezeFrameMaterial->Refresh();
	}
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->DrawScreenSpaceRectangle( state.m_FreezeFrameMaterial, x, y, w, h, 0, 0, w - 1, h - 1, w, h );
	if ( nFlashAlpha < 0 )
		return;
	if ( !state.m_TranslucentSingleColor.IsValid() )
		state.m_TranslucentSingleColor.Init( "debug/debugtranslucentsinglecolor", TEXTURE_GROUP_OTHER );
	IMaterial *pMaterial = state.m_TranslucentSingleColor;
	pMaterial->AlphaModulate( nFlashAlpha * ( 1.0f / 255.0f ) );
	pMaterial->ColorModulate( 1.0f, 1.0f, 1.0f );
	pMaterial->SetMaterialVarFlag( MATERIAL_VAR_IGNOREZ, true );
	pRenderContext->DrawScreenSpaceRectangle( pMaterial, x, y, w, h, 0, 0, w - 1, h - 1, w, h );
}

void FlushListCache()
{
	ExecutorState_t &state = State();
	for ( int c = 0; c < state.m_ListCache.Count(); ++c )
	{
		state.m_ListCache[c].m_pList->Release();
		ListInfo( state.m_ListCache[c].m_pInfo )->Release();
	}
	state.m_ListCache.RemoveAll();
}

void ViewScene( CPlanView *pView, const SourceSharpRenderOp_t &op, int nOpIndex );
void DebugQuad( const SourceSharpRenderOp_t &op );
void PostOp( const SourceSharpRenderOp_t &op );
bool RunPostProgram( int nKind, const CViewSetup &setup, int x, int y, int w, int h, bool bFlashlight, bool bPostVGui );
void DebugShowTexture( float flScale );
void DrawProgramEntity( CPlanView *pView, const SourceSharpRenderOp_t &op );
void FrameBufferForEntity( const SourceSharpRenderOp_t &op );
bool RunSection( CPlanView *pView );

void ExecuteOps( const SourceSharpRenderOp_t *pOps, int nOps, int nIndexBase )
{
	ExecutorState_t &state = State();
	for ( int i = 0; i < nOps; ++i )
	{
		const SourceSharpRenderOp_t &op = pOps[i];
		CViewSetup setup;
		ToViewSetup( op.View, setup );
		CPlanView *pView = CurrentView();

		switch ( op.Kind )
		{
		case RENDER_OP_SceneBegin: render->SceneBegin(); break;
		case RENDER_OP_SceneEnd: render->SceneEnd(); break;
		case RENDER_OP_TurnOnToneMapping: { CMatRenderContextPtr ctx( materials ); ctx->TurnOnToneMapping(); break; }
		case RENDER_OP_ShadowAdvanceFrame: ShellShadows_AdvanceFrame(); break;
		case RENDER_OP_Push3DView:
			if ( op.I[3] )
				render->Push3DView( setup, op.I[0], Target( op.I[1] ), view->GetFrustum(), Target( op.I[2] ) );
			else
				render->Push3DView( setup, op.I[0], Target( op.I[1] ), view->GetFrustum() );
			break;
		case RENDER_OP_Push2DView: render->Push2DView( setup, op.I[0], Target( op.I[1] ), view->GetFrustum() ); break;
		case RENDER_OP_PopView: render->PopView( view->GetFrustum() ); break;
		case RENDER_OP_SetupCurrentView: ShellView_SetupCurrentView( setup.origin, setup.angles, op.I[0], true ); break;
		case RENDER_OP_FinishCurrentView: ShellView_FinishCurrentView(); break;
		case RENDER_OP_AllowCurrentViewAccess: ShellView_AllowCurrentViewAccess( op.I[0] != 0 ); break;
		case RENDER_OP_ShadowPreRender: ShellShadows_PreRender(); break;
		case RENDER_OP_ShadowComputeDepthTextures: ShellShadows_ComputeShadowDepthTextures( setup ); break;
		case RENDER_OP_ShadowUnlockDepthTextures: ShellShadows_UnlockAllShadowDepthTextures(); break;
		case RENDER_OP_ClientSystemsPreRender: SourceSharp_ClientSystemsPreRender(); break;
		case RENDER_OP_ViewSetupVisEx:
		{
			Vector origin( op.F[0], op.F[1], op.F[2] );
			unsigned int flags = 0;
			render->ViewSetupVisEx( op.I[0] != 0, 1, &origin, flags );
			state.m_nLastVisFlags = flags;
			break;
		}
		case RENDER_OP_ClearColorToFogIfRadialVis:
			if ( state.m_nLastVisFlags & IVRenderView::VIEW_SETUP_VIS_EX_RETURN_FLAGS_USES_RADIAL_VIS )
				SetClearColorToFogColor();
			break;
		case RENDER_OP_SetClearColorToFogColor: SetClearColorToFogColor(); break;
		case RENDER_OP_ClearColor4ub: { CMatRenderContextPtr ctx( materials ); ctx->ClearColor4ub( op.I[0], op.I[1], op.I[2], op.I[3] ); break; }
		case RENDER_OP_ClearColor3ub: { CMatRenderContextPtr ctx( materials ); ctx->ClearColor3ub( op.I[0], op.I[1], op.I[2] ); break; }
		case RENDER_OP_ForcedMaterialOverride: modelrender->ForcedMaterialOverride( NULL, (OverrideType_t)op.I[0] ); break;
		case RENDER_OP_ParticleIncrementFrameCode:
			ShellParticles_FrameCode();	// the old-style effects', managed's
			break;
		case RENDER_OP_WorldViewBegin:
		{
			CPlanView *pNew = new CPlanView();
			pNew->Begin( setup, op.I[0], op.I[1] );
			state.m_Views.AddToTail( pNew );
			break;
		}
		case RENDER_OP_WorldViewEnd:
			if ( pView )
			{
				state.m_Views.RemoveMultipleFromTail( 1 );
				pView->Release();
			}
			break;
		case RENDER_OP_BeginUpdateLightmaps: render->BeginUpdateLightmaps(); break;
		case RENDER_OP_EndUpdateLightmaps: render->EndUpdateLightmaps(); break;
		case RENDER_OP_BuildWorldLists:
			if ( pView )
			{
				float flHeight = op.F[0];
				pView->BuildWorld( state.m_ListCache, op.I[0] != 0, op.I[1], op.I[2] != 0, op.I[3] != 0, op.I[4] ? &flHeight : NULL, op.F[1] );
			}
			break;
		case RENDER_OP_PruneWorldList: if ( pView ) pView->Prune(); break;
		case RENDER_OP_BuildRenderables: if ( pView ) pView->Renderables( op.I[0], op.I[1] != 0, op.I[2] != 0, op.I[3] != 0, op.F[0] ); break;
		case RENDER_OP_FogLinear:
		{
			CMatRenderContextPtr ctx( materials );
			float color[3] = { op.F[0], op.F[1], op.F[2] };
			ctx->FogMode( MATERIAL_FOG_LINEAR );
			ctx->FogColor3fv( color );
			ctx->FogStart( op.F[3] );
			ctx->FogEnd( op.F[4] );
			ctx->FogMaxDensity( op.F[5] );
			ctx->FogRadial( op.I[0] != 0 );
			break;
		}
		case RENDER_OP_FogNone: { CMatRenderContextPtr ctx( materials ); ctx->FogMode( MATERIAL_FOG_NONE ); break; }
		case RENDER_OP_FogVolumeState: render->SetFogVolumeState( op.I[0], op.I[1] != 0 ); break;
		case RENDER_OP_ClearColorFromFogColor:
		{
			CMatRenderContextPtr ctx( materials );
			unsigned char ucFogColor[3];
			ctx->GetFogColor( ucFogColor );
			ctx->ClearColor4ub( ucFogColor[0], ucFogColor[1], ucFogColor[2], op.I[0] );
			break;
		}
		case RENDER_OP_WorldViewShadowTextures: if ( pView ) pView->ShadowTextures(); break;
		case RENDER_OP_SoundExtraUpdate: engine->Sound_ExtraUpdate(); break;
		case RENDER_OP_SetFogZ: { CMatRenderContextPtr ctx( materials ); ctx->SetFogZ( op.F[0] ); break; }
		case RENDER_OP_SetHeightClipZ: { CMatRenderContextPtr ctx( materials ); ctx->SetHeightClipZ( op.F[0] ); break; }
		case RENDER_OP_SetHeightClipMode: { CMatRenderContextPtr ctx( materials ); ctx->SetHeightClipMode( (MaterialHeightClipMode_t)op.I[0] ); break; }
		case RENDER_OP_ClearForView:
		{
			int clear = op.I[0];
			if ( op.I[1] && ( state.m_nLastVisFlags & IVRenderView::VIEW_SETUP_VIS_EX_RETURN_FLAGS_USES_RADIAL_VIS ) )
				clear |= VIEW_CLEAR_COLOR;
			if ( clear & ( VIEW_CLEAR_DEPTH | VIEW_CLEAR_COLOR | VIEW_CLEAR_STENCIL ) )
			{
				CMatRenderContextPtr ctx( materials );
				if ( clear & VIEW_CLEAR_OBEY_STENCIL )
					ctx->ClearBuffersObeyStencil( ( clear & VIEW_CLEAR_COLOR ) != 0, ( clear & VIEW_CLEAR_DEPTH ) != 0 );
				else
					ctx->ClearBuffers( ( clear & VIEW_CLEAR_COLOR ) != 0, ( clear & VIEW_CLEAR_DEPTH ) != 0, ( clear & VIEW_CLEAR_STENCIL ) != 0 );
			}
			break;
		}
		case RENDER_OP_WaterLightmapScale:
		{
			CMatRenderContextPtr ctx( materials );
			state.m_vSavedLightmapScale = ctx->GetToneMappingScaleLinear();
			Vector scaled = state.m_vSavedLightmapScale;
			scaled *= 0.25;
			ctx->SetToneMappingScaleLinear( scaled );
			break;
		}
		case RENDER_OP_RestoreWaterLightmapScale:
			if ( state.m_vSavedLightmapScale.x >= 0 )
			{
				CMatRenderContextPtr ctx( materials );
				ctx->SetToneMappingScaleLinear( state.m_vSavedLightmapScale );
				state.m_vSavedLightmapScale.x = -1;
			}
			break;
		case RENDER_OP_FrameBufferCopyPush:
		{
			CMatRenderContextPtr ctx( materials );
			state.m_FrameBufferCopies.AddToTail( ctx->GetFrameBufferCopyTexture( 0 ) );
			if ( op.I[0] )
				ctx->SetFrameBufferCopyTexture( ShellRenderTarget( SS_RT_PowerOfTwoFrameBuffer ) );
			break;
		}
		case RENDER_OP_FrameBufferCopyPop:
			if ( state.m_FrameBufferCopies.Count() )
			{
				CMatRenderContextPtr ctx( materials );
				ctx->SetFrameBufferCopyTexture( state.m_FrameBufferCopies.Tail() );
				state.m_FrameBufferCopies.RemoveMultipleFromTail( 1 );
			}
			break;
		case RENDER_OP_DrawWorld:
			if ( pView && pView->WorldList() )
				render->DrawWorldLists( pView->WorldList(), (unsigned long)(unsigned int)op.I[0], op.F[0] );
			break;
		// C7 step 3 phase 1: the view's program is the only draw over its lists.
		// A view without one draws nothing there; the first miss is said, and
		// managed counts the declines it made (/render's viewProgramsDeclined).
		case RENDER_OP_DrawOpaque:
		case RENDER_OP_DrawTranslucent:
		case RENDER_OP_DrawNoZTranslucent:
		case RENDER_OP_DrawTranslucentWorldInLeaves:
			if ( pView && !RunSection( pView ) && state.m_nProgramMisses++ == 0 )
				Warning( "[SourceSharp] render plan: a world view drew over its lists with no program section (managed declined it); it draws nothing there\n" );
			break;

		// C7 step 2: the view's scene, and its program's ops.
		case RENDER_OP_ViewScene: if ( pView && nIndexBase >= 0 ) ViewScene( pView, op, nIndexBase + i ); break;
		case RENDER_OP_SectionEnd: break;
		case RENDER_OP_SetBlend: render->SetBlend( op.F[0] ); break;
		case RENDER_OP_SetColorModulation: render->SetColorModulation( op.F ); break;
		case RENDER_OP_DrawEntity: if ( pView ) DrawProgramEntity( pView, op ); break;
		case RENDER_OP_StaticProp:
			if ( pView && pView->Entry( op.I[0] ) )
				pView->m_Statics.AddToTail( pView->Entry( op.I[0] ) );
			break;
		case RENDER_OP_DrawStaticPropsBatch:
			if ( pView && pView->m_Statics.Count() )
			{
				staticpropmgr->DrawStaticProps( pView->m_Statics.Base(), pView->m_Statics.Count(), op.I[0] != 0, op.I[1] != 0 );
				pView->m_Statics.RemoveAll();
			}
			break;
		case RENDER_OP_RopeResetCache: RopeManager()->ResetRenderCache(); break;
		case RENDER_OP_ParticleSystemResetCache: g_pParticleSystemMgr->ResetRenderCache(); break;
		case RENDER_OP_RopeDrawCache: RopeManager()->DrawRenderCache( op.I[0] != 0 ); break;
		case RENDER_OP_ParticleSystemDrawCache: g_pParticleSystemMgr->DrawRenderCache( op.I[0] != 0 ); break;
		case RENDER_OP_ParticleSingletons: DrawParticleSingletons( op.I[0] != 0 ); break;
		case RENDER_OP_UpdateFullScreenDepthTexture: UpdateFullScreenDepthTexture(); break;
		case RENDER_OP_FullScreenDepthTextureValidity:
		{
			CMatRenderContextPtr ctx( materials );
			ctx->SetFullScreenDepthTextureValidityFlag( op.I[0] != 0 );
			break;
		}
		case RENDER_OP_DetailBeginTranslucent: DetailObjectSystem()->BeginTranslucentDetailRendering(); break;
		case RENDER_OP_DetailLeaf: if ( pView ) pView->m_DetailLeaves.AddToTail( (LeafIndex_t)op.I[0] ); break;
		case RENDER_OP_DetailRenderTranslucent:
			if ( pView )
			{
				DetailObjectSystem()->RenderTranslucentDetailObjects( CurrentViewOrigin(), CurrentViewForward(), CurrentViewRight(), CurrentViewUp(),
				                                                      pView->m_DetailLeaves.Count(), pView->m_DetailLeaves.Base() );
				pView->m_DetailLeaves.RemoveAll();
			}
			break;
		case RENDER_OP_DetailRenderInLeaf:
		{
			IClientRenderable *pEnt = pView ? pView->Entry( op.I[1] ) : NULL;
			Vector vecOrigin = pEnt ? pEnt->GetRenderOrigin() : vec3_origin;
			DetailObjectSystem()->RenderTranslucentDetailObjectsInLeaf( CurrentViewOrigin(), CurrentViewForward(), CurrentViewRight(), CurrentViewUp(),
			                                                            op.I[0], pEnt ? &vecOrigin : NULL );
			break;
		}
		case RENDER_OP_TranslucentSurfaces:
			if ( pView && pView->WorldList() )
				render->DrawTranslucentSurfaces( pView->WorldList(), op.I[0], (unsigned long)(unsigned int)op.I[1], op.I[2] != 0 );
			break;
		case RENDER_OP_FrameBufferForEntity: FrameBufferForEntity( op ); break;
		case RENDER_OP_UpdateRefractTexture: UpdateRefractTexture(); break;
		case RENDER_OP_UpdateScreenEffectTexture: UpdateScreenEffectTexture(); break;

		// C7 item 6: the second camera's pass.
		case RENDER_OP_ViewerSeenBegin: state.m_ViewerSeen.AddToTail( ShellClient_ViewerSeenBegin() ); break;
		case RENDER_OP_ViewerSeenEnd:
			if ( state.m_ViewerSeen.Count() )
			{
				ShellClient_ViewerSeenEnd( state.m_ViewerSeen.Tail() );
				state.m_ViewerSeen.RemoveMultipleFromTail( 1 );
			}
			break;
		case RENDER_OP_ViewTargetDrawn: ShellViewTargets_NoteDrawn( op.I[0] ); break;

		// C7 step 2: viewdebug.cpp's overlays, as managed placed them.
		case RENDER_OP_DebugQuad: DebugQuad( op ); break;
		case RENDER_OP_DebugScreenMaterial:
			if ( IMaterial *pMaterial = SourceSharp_FxMaterialOf( op.I[0] ) )
			{
				if ( !IsErrorMaterial( pMaterial ) )
					DrawScreenEffectMaterial( pMaterial, setup.x, setup.y, setup.width, setup.height );
			}
			break;
		case RENDER_OP_DebugLightmapPage:
		{
			CPlanView *pDebug = new CPlanView();
			pDebug->Begin( setup, 0, 0 );
			Frustum frustum;
			AllowCurrentViewAccess( true );
			render->Push3DView( setup, 0, NULL, frustum );
			pDebug->BuildWorld( state.m_ListCache, true, 1, true, false, NULL, op.F[0] );
			render->PopView( frustum );
			AllowCurrentViewAccess( false );
			if ( pDebug->WorldList() )
				render->DrawLightmaps( pDebug->WorldList(), op.I[0] );
			pDebug->Release();
			break;
		}
		case RENDER_OP_DebugShadowTexture: ShellShadows_RenderShadowTexture( op.I[0], op.I[0] ); break;
		case RENDER_OP_DebugShowTexture: DebugShowTexture( op.F[0] ); break;
		case RENDER_OP_DebugFlashlightDepth: shadowmgr->DrawFlashlightDepthTexture(); break;
		case RENDER_OP_Draw3DDebugOverlays: render->Draw3DDebugOverlays(); break;
		case RENDER_OP_DrawLineFile: render->DrawLineFile(); break;
		case RENDER_OP_SmokeFogOverlayAlpha: g_SmokeFogOverlayAlpha = op.F[0]; break;
		case RENDER_OP_PushDefaultTarget: { CMatRenderContextPtr ctx( materials ); ctx->PushRenderTargetAndViewport( NULL ); break; }
		case RENDER_OP_PopRenderTargetAndViewport: { CMatRenderContextPtr ctx( materials ); ctx->PopRenderTargetAndViewport(); break; }
		case RENDER_OP_PostCopyScreenEffect:
		case RENDER_OP_PostSetRenderTarget:
		case RENDER_OP_PostSetRenderTargetAndViewport:
		case RENDER_OP_PostPushRenderTargetAndViewport:
		case RENDER_OP_PostDrawRect:
		case RENDER_OP_PostMaterialVarFloat:
		case RENDER_OP_PostQueryBegin:
		case RENDER_OP_PostQueryEnd:
		case RENDER_OP_PostStencil:
		case RENDER_OP_PostGoalToneScale:
		case RENDER_OP_PostResetToneScale:
		case RENDER_OP_PostEnableColorCorrection:
		case RENDER_OP_PostScissor:
		case RENDER_OP_PostClearColor3ub:
		case RENDER_OP_PostViewport:
		case RENDER_OP_PostClearBuffers:
		case RENDER_OP_PostDumpTga:
		case RENDER_OP_PostReadbackNext:
		case RENDER_OP_PostPushRenderTarget:
		case RENDER_OP_PostMeshBegin:
		case RENDER_OP_PostMeshVertex:
		case RENDER_OP_PostMeshFrame:
		case RENDER_OP_PostMeshEnd:
		case RENDER_OP_PostClearColor4ub:
			PostOp( op );
			break;
		case RENDER_OP_PixelVisEndCurrentView: ShellPixelVisEndView(); break;
		case RENDER_OP_PixelVisEndScene: ShellPixelVisEndScene(); break;
		case RENDER_OP_Flush: { CMatRenderContextPtr ctx( materials ); ctx->Flush( op.I[0] != 0 ); break; }
		case RENDER_OP_CopyRefractionTarget:
		{
			Rect_t src;
			src.x = op.I[0]; src.y = op.I[1]; src.width = op.I[2]; src.height = op.I[3];
			CMatRenderContextPtr ctx( materials );
			ctx->CopyRenderTargetToTextureEx( ShellRenderTarget( SS_RT_WaterRefraction ), 0, &src, NULL );
			break;
		}
		case RENDER_OP_PushCustomClipPlane: { CMatRenderContextPtr ctx( materials ); ctx->PushCustomClipPlane( op.F ); break; }
		case RENDER_OP_PopCustomClipPlane: { CMatRenderContextPtr ctx( materials ); ctx->PopCustomClipPlane(); break; }
		case RENDER_OP_GlowDrawOverlays: ShellGlowDrawOverlays( op.I[0] != 0 ); break;
		case RENDER_OP_GlowUpdateSkyOverlays: ShellGlowUpdateSkyOverlays( op.F[0], op.I[0] != 0 ); break;
		case RENDER_OP_DrawPrecipitation: DrawPrecipitation(); break;
		case RENDER_OP_ClientEffectsDraw: clienteffects->DrawEffects( gpGlobals->frametime ); break;
		case RENDER_OP_SetFXCreationAllowed: SetFXCreationAllowed( op.I[0] != 0 ); break;
		case RENDER_OP_DrawPortals: engine->DrawPortals(); break;
		case RENDER_OP_DrawLights: render->DrawLights(); break;
		// CViewRender::RenderPlayerSprites: the voice icons over players' heads.
		case RENDER_OP_RenderPlayerSprites: GetClientVoiceMgr()->DrawHeadLabels(); break;
		case RENDER_OP_MotionBlur: RunPostProgram( 1, setup, setup.x, setup.y, setup.width, setup.height, false, false ); break;
		case RENDER_OP_PostScreenSpaceEffects: GetClientModeNormal()->DoPostScreenSpaceEffects( &setup ); break;
		case RENDER_OP_ViewModels: ViewModels( setup, op.I[0] != 0 ); break;
		case RENDER_OP_UnderwaterOverlay: UnderwaterOverlay(); break;
		case RENDER_OP_SmokeFogOverlay: DrawSmokeFogOverlay(); break;
		case RENDER_OP_ViewDrawFade:
		{
			byte color[4] = { (byte)op.I[0], (byte)op.I[1], (byte)op.I[2], (byte)op.I[3] };
			if ( !state.m_ModulateSingleColor.IsValid() )
				state.m_ModulateSingleColor.Init( "engine/modulatesinglecolor", TEXTURE_GROUP_OTHER );
			if ( !state.m_TranslucentSingleColor.IsValid() )
				state.m_TranslucentSingleColor.Init( "debug/debugtranslucentsinglecolor", TEXTURE_GROUP_OTHER );
			render->ViewDrawFade( color, op.I[4] ? (IMaterial *)state.m_ModulateSingleColor : (IMaterial *)state.m_TranslucentSingleColor );
			break;
		}
		case RENDER_OP_ScreenOverlay:
			if ( IMaterial *pOverlay = view->GetScreenOverlayMaterial() )
				DrawOverlayMaterial( pOverlay, op.I[4], op.I[0], op.I[1], op.I[2], op.I[3] );
			break;
		case RENDER_OP_EnginePostProcessing: RunPostProgram( 0, setup, op.I[0], op.I[1], op.I[2], op.I[3], op.I[4] != 0, false ); break;
		case RENDER_OP_GrabPreColorCorrectedFrame: engine->GrabPreColorCorrectedFrame( op.I[0], op.I[1], op.I[2], op.I[3] ); break;
		case RENDER_OP_ToneMapScaleLinear: { CMatRenderContextPtr ctx( materials ); ctx->SetToneMappingScaleLinear( Vector( op.F[0], op.F[1], op.F[2] ) ); break; }
		case RENDER_OP_SaveRenderTarget: { CMatRenderContextPtr ctx( materials ); state.m_pSavedRenderTarget = ctx->GetRenderTarget(); break; }
		case RENDER_OP_SetRenderTargetSaved: { CMatRenderContextPtr ctx( materials ); ctx->SetRenderTarget( state.m_pSavedRenderTarget ); break; }
		case RENDER_OP_HudPaint: HudPaint( setup ); break;
		case RENDER_OP_WorldListCacheFlush: FlushListCache(); break;
		case RENDER_OP_Skybox3DAreaBits:
		{
			unsigned char **ppAreaBits = render->GetAreaBits();
			state.m_pSavedAreaBits = *ppAreaBits;
			memset( state.m_AreaBits, 0, sizeof( state.m_AreaBits ) );
			state.m_AreaBits[ op.I[0] >> 3 ] |= 1 << ( op.I[0] & 7 );
			*ppAreaBits = state.m_AreaBits;
			break;
		}
		case RENDER_OP_Skybox3DAreaBitsRestore:
			*render->GetAreaBits() = state.m_pSavedAreaBits;
			break;
		case RENDER_OP_ViewSetupVis:
		{
			Vector origin( op.F[0], op.F[1], op.F[2] );
			render->ViewSetupVis( op.I[0] != 0, 1, &origin );
			break;
		}
		case RENDER_OP_SetUnderwaterOverlay:
			if ( state.m_szUnderwaterOverlay[0] )
				state.m_UnderWaterOverlay.Init( materials->FindMaterial( state.m_szUnderwaterOverlay, TEXTURE_GROUP_OTHER ) );
			break;
		case RENDER_OP_ClearUnderwaterOverlay: state.m_UnderWaterOverlay.Shutdown(); break;
		case RENDER_OP_Upscale: Upscale( setup ); break;
		case RENDER_OP_FreezeFrameDraw: FreezeFrameDraw( op.I[0], op.I[1], op.I[2], op.I[3], op.I[4] ); break;
		case RENDER_OP_FreezeFrameCapture:
		{
			Rect_t rect;
			rect.x = op.I[0]; rect.y = op.I[1]; rect.width = op.I[2]; rect.height = op.I[3];
			CMatRenderContextPtr ctx( materials );
			ctx->CopyRenderTargetToTextureEx( ShellRenderTarget( SS_RT_Fullscreen ), 0, &rect, &rect );
			break;
		}
		case RENDER_OP_SetCurrentViewSetup:
		{
			// Stock's RenderView kept the view in CViewRender's m_CurrentView,
			// which GetViewSetup answers between frames too; CShellViewRender
			// keeps the plan's (the plan frames' stale SetMainView, 2026-10-11).
			ShellViewRender_SetCurrentView( setup );
			break;
		}
		default:
			Warning( "[SourceSharp] render plan: op %d (kind %d) is not one this executor knows; skipped\n", i, op.Kind );
			break;
		}
	}

}

// The view's scene, gathered once its lists are built, and managed's program
// for it. A view managed gives no program draws with stock's primitives.
void ViewScene( CPlanView *pView, const SourceSharpRenderOp_t &op, int nOpIndex )
{
	pView->m_bHasProgram = false;
	pView->m_nSectionStart = 0;
	IManagedRenderClient *pManaged = ManagedRenderClient();
	if ( !pManaged )
		return;

	const int nFlags[2] = { op.I[0], op.I[1] };
	CClientRenderablesList *pList = pView->RenderList();
	ViewSceneBuffer_t scene;
	BuildViewScene( scene, pView->WorldInfo(), pList, view->BuildWorldListsNumber(), pView->WorldList(), nFlags, nOpIndex );

	pView->m_Entries.RemoveAll();
	if ( pList )
		for ( int g = 0; g < RENDER_GROUP_COUNT; ++g )
			for ( int i = 0; i < pList->m_RenderGroupCounts[g]; ++i )
				pView->m_Entries.AddToTail( pList->m_RenderGroups[g][i].m_pRenderable );

	const SourceSharpViewScene_t *pHeader = scene.Header();
	const int nMax = 64 + 4 * pHeader->LeafCount + 12 * pHeader->EntryCount;
	pView->m_Program.SetCount( nMax );
	const int nOps = pManaged->RenderViewProgram( pHeader, scene.m_Bytes.Count(), pView->m_Program.Base(), nMax );
	if ( nOps < 0 || nOps > nMax )
	{
		pView->m_Program.RemoveAll();
		return;
	}
	pView->m_Program.SetCountNonDestructively( nOps );
	pView->m_bHasProgram = true;
}

// Stock's DrawOpaqueRenderable/DrawTranslucentRenderable after the colour and
// blend managed set: the draw, inside the renderable's clip plane when it has
// one and managed honours them (r_entityclips).
void DrawProgramEntity( CPlanView *pView, const SourceSharpRenderOp_t &op )
{
	IClientRenderable *pEnt = pView->Entry( op.I[0] );
	if ( !pEnt )
		return;
	float *pClip = op.I[2] ? pEnt->GetRenderClipPlane() : NULL;
	const bool bPushClip = pClip && !materials->UsingFastClipping();
	if ( bPushClip )
	{
		CMatRenderContextPtr ctx( materials );
		ctx->PushCustomClipPlane( pClip );
	}
	// Stock draws a clipped depth box first under fast clipping
	// (viewrender.cpp's DrawClippedDepthBox); not carried yet (docs/todo.md).
	pEnt->DrawModel( op.I[1] );
	if ( bPushClip )
	{
		CMatRenderContextPtr ctx( materials );
		ctx->PopCustomClipPlane();
	}
}

// The frame-buffer refresh before a translucent entity that samples it, at the
// size of the render target in force (stock's two arms; I2 says which).
void FrameBufferForEntity( const SourceSharpRenderOp_t &op )
{
	const bool bPow2 = op.I[0] != 0, bFull = op.I[1] != 0;
	ITexture *rt;
	{
		CMatRenderContextPtr ctx( materials );
		rt = ctx->GetRenderTarget();
	}
	if ( op.I[2] == 0 )
	{
		if ( rt && bFull )
			UpdateScreenEffectTexture( 0, 0, 0, rt->GetActualWidth(), rt->GetActualHeight(), true );
		else if ( bPow2 )
			UpdateRefractTexture();
		return;
	}
	if ( rt )
	{
		if ( bFull )
			UpdateScreenEffectTexture( 0, 0, 0, rt->GetActualWidth(), rt->GetActualHeight(), true );
		else if ( bPow2 )
			UpdateRefractTexture( 0, 0, rt->GetActualWidth(), rt->GetActualHeight() );
	}
	else if ( bPow2 )
	{
		UpdateRefractTexture();
	}
}

// viewdebug.cpp's overlay quad, every one of its helpers' shape: the material
// managed found, at the rectangle and texture coordinates managed chose, white;
// sized by the material's mapping when asked (RenderMaterial). An error
// material draws nothing, as in stock.
void DebugQuad( const SourceSharpRenderOp_t &op )
{
	IMaterial *pMaterial = SourceSharp_FxMaterialOf( op.I[0] );
	if ( !pMaterial || IsErrorMaterial( pMaterial ) )
		return;
	const float x = op.F[0], y = op.F[1];
	const float w = op.I[1] ? (float)pMaterial->GetMappingWidth() : op.F[2];
	const float h = op.I[1] ? (float)pMaterial->GetMappingHeight() : op.F[3];
	const float s0 = op.F[4], t0 = op.F[5], s1 = op.F[6], t1 = op.F[7];

	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->Bind( pMaterial );
	IMesh *pMesh = pRenderContext->GetDynamicMesh( true );
	CMeshBuilder meshBuilder;
	meshBuilder.Begin( pMesh, MATERIAL_QUADS, 1 );
	meshBuilder.Position3f( x, y, 0.0f );
	meshBuilder.TexCoord2f( 0, s0, t0 );
	meshBuilder.Color4ub( 255, 255, 255, 255 );
	meshBuilder.AdvanceVertex();
	meshBuilder.Position3f( x + w, y, 0.0f );
	meshBuilder.TexCoord2f( 0, s1, t0 );
	meshBuilder.Color4ub( 255, 255, 255, 255 );
	meshBuilder.AdvanceVertex();
	meshBuilder.Position3f( x + w, y + h, 0.0f );
	meshBuilder.TexCoord2f( 0, s1, t1 );
	meshBuilder.Color4ub( 255, 255, 255, 255 );
	meshBuilder.AdvanceVertex();
	meshBuilder.Position3f( x, y + h, 0.0f );
	meshBuilder.TexCoord2f( 0, s0, t1 );
	meshBuilder.Color4ub( 255, 255, 255, 255 );
	meshBuilder.AdvanceVertex();
	meshBuilder.End();
	pMesh->Draw();
}

// C7 post-processing: viewpostprocess.cpp's render-context calls, as managed's
// PostProcessPlanner asked for them.
ITexture *PostTexture( int nTexture )
{
	switch ( nTexture )
	{
	case 1: return ShellRenderTarget( SS_RT_FullFrameFrameBuffer0 );
	case 2: return materials->FindTexture( "_rt_SmallFB0", TEXTURE_GROUP_RENDER_TARGET );
	case 3: return materials->FindTexture( "_rt_SmallFB1", TEXTURE_GROUP_RENDER_TARGET );
	case 4: return materials->FindTexture( "_rt_ResolvedFullFrameDepth", TEXTURE_GROUP_RENDER_TARGET );
	default: return NULL;
	}
}

const char *const kPostVarNames[] = { NULL, "$C0_X", "$C0_Y", "$C0_Z", "$bloomamount" };
const char *const kPostDumpNames[] = { "BackBuffer", "QuarterSizeFB", "BlurX", "BlurYAndBloom", "EnginePost", "MotionBlur" };

// A render target's pixels to //MOD/<index>_<name>_PC.tga.
void PostDumpTga( int nWidth, int nHeight, const char *pszName, int nIndex )
{
	if ( nWidth <= 0 || nHeight <= 0 )
		return;
	CMatRenderContextPtr pRenderContext( materials );
	const int nPixelBytes = nWidth * nHeight * 4;
	uint8_t *pBlock = ss::ImageEncodeBlock( nPixelBytes );
	if ( !pBlock )
		return;
	pRenderContext->ReadPixels( 0, 0, nWidth, nHeight, ss::ImageEncodePixels( pBlock ), IMAGE_FORMAT_RGBA8888 );

	// The TGA is encoded in C# (C9: ScreenshotEncoding, held to the pristine bitmap.a).
	int nFileBytes = 0;
	unsigned char *pFile = ss::ImageEncode( ss::kImageTgaRgba, nWidth, nHeight, false, pBlock, nPixelBytes, &nFileBytes );
	free( pBlock );
	if ( !pFile )
	{
		Warning( "[SourceSharp] post: could not write %s as a TGA\n", pszName );
		return;
	}
	char szPath[ MAX_PATH ];
	V_snprintf( szPath, sizeof( szPath ), "//MOD/%d_%s_PC.tga", nIndex, pszName );
	FileHandle_t hFile = g_pFullFileSystem->Open( szPath, "wb" );
	if ( hFile )
	{
		g_pFullFileSystem->Write( pFile, nFileBytes, hFile );
		g_pFullFileSystem->Close( hFile );
	}
	free( pFile );
}

// C7 post-processing: what PostProcessPlanner reads here, then its program,
// carried out. False when managed declined (stock's function draws then).
bool RunPostProgram( int nKind, const CViewSetup &setup, int x, int y, int w, int h, bool bFlashlight, bool bPostVGui )
{
	IManagedRenderClient *pManaged = ManagedRenderClient();
	if ( !pManaged )
		return false;
	ExecutorState_t &state = State();
	CMatRenderContextPtr ctx( materials );

	SourceSharpPostInputs_t in;
	V_memset( &in, 0, sizeof( in ) );
	in.Kind = nKind;
	in.X = x; in.Y = y; in.Width = w; in.Height = h;
	in.FlashlightOn = bFlashlight ? 1 : 0;
	in.PostVGui = bPostVGui ? 1 : 0;
	in.HdrType = g_pMaterialSystemHardwareConfig->GetHDRType();
	in.EngineDxLevel = engine->GetDXSupportLevel();
	in.HardwareDxLevel = g_pMaterialSystemHardwareConfig->GetDXSupportLevel();
	in.MapHasHdrLighting = engine->MapHasHDRLighting() ? 1 : 0;
	in.CanDoSrgbReadFromRts = g_pMaterialSystemHardwareConfig->CanDoSRGBReadFromRTs() ? 1 : 0;
	in.FakeSrgbWrite = g_pMaterialSystemHardwareConfig->FakeSRGBWrite() ? 1 : 0;
	in.SupportsPixelShaders20 = g_pMaterialSystemHardwareConfig->SupportsPixelShaders_2_0() ? 1 : 0;
	in.ColorCorrectionActive = SourceSharp_ColorCorrectionActive() ? 1 : 0;
	if ( ITexture *pFull = ShellRenderTarget( SS_RT_FullFrameFrameBuffer0 ) )
	{
		in.FullFrameWidth = pFull->GetActualWidth();
		in.FullFrameHeight = pFull->GetActualHeight();
	}
	if ( ITexture *pSmall = PostTexture( 3 ) )
	{
		in.SmallFB1Width = pSmall->GetActualWidth();
		in.SmallFB1Height = pSmall->GetActualHeight();
	}
	ctx->GetViewport( in.ViewportX, in.ViewportY, in.ViewportWidth, in.ViewportHeight );
	for ( int t = 1; t <= 4; ++t )
	{
		if ( ITexture *pTexture = PostTexture( t ) )
		{
			in.TextureSize[ 2 * t ] = pTexture->GetActualWidth();
			in.TextureSize[ 2 * t + 1 ] = pTexture->GetActualHeight();
		}
	}
	materials->GetBackBufferDimensions( in.BackBufferWidth, in.BackBufferHeight );
	in.UseCustomAutoExposureMin = g_bUseCustomAutoExposureMin ? 1 : 0;
	in.UseCustomAutoExposureMax = g_bUseCustomAutoExposureMax ? 1 : 0;
	in.UseCustomBloomScale = g_bUseCustomBloomScale ? 1 : 0;
	in.CustomAutoExposureMin = g_flCustomAutoExposureMin;
	in.CustomAutoExposureMax = g_flCustomAutoExposureMax;
	in.CustomBloomScale = g_flCustomBloomScale;
	in.RealTime = gpGlobals->realtime;
	in.CurTime = gpGlobals->curtime;
	in.FrameTime = gpGlobals->frametime;
	in.ToneScaleLinear = ctx->GetToneMappingScaleLinear().x;
	for ( int i = 0; i < 3; ++i )
	{
		in.ViewOriginXYZ[i] = setup.origin[i];
		in.ViewAnglesXYZ[i] = setup.angles[i];
	}
	in.Fov = setup.fov;
	in.AspectRatio = setup.m_flAspectRatio;

	// The histogram's read-backs the last program asked for (DoEnginePostProcessing only).
	for ( int b = 0; b < (int)ARRAYSIZE( in.Readback ); ++b )
	{
		in.Readback[b] = -1;
		if ( nKind == 0 && ( state.m_nPostReadbackMask & ( 1 << b ) ) && state.m_PostQueries[b] )
			in.Readback[b] = ctx->OcclusionQuery_GetNumPixelsRendered( state.m_PostQueries[b] );
	}
	if ( nKind == 0 )
		state.m_nPostReadbackMask = 0;

	for ( int i = 0; i < kPostCvarCount && i < (int)ARRAYSIZE( in.Cvars ); ++i )
	{
		ConVarRef var( kPostCvars[i], true );
		in.Cvars[i] = var.IsValid() ? var.GetFloat() : 0.0f;
	}

	const int nOps = pManaged->RenderPostProgram( &in, state.m_PostOps, (int32_t)ARRAYSIZE( state.m_PostOps ) );
	if ( nOps < 0 )
		return false;
	for ( int i = 0; i < nOps; ++i )
	{
		const SourceSharpRenderOp_t &op = state.m_PostOps[i];
		if ( op.Kind == RENDER_OP_PopRenderTargetAndViewport )
			ctx->PopRenderTargetAndViewport();
		else
			PostOp( op );
	}
	return true;
}

void PostOp( const SourceSharpRenderOp_t &op )
{
	ExecutorState_t &state = State();
	CMatRenderContextPtr ctx( materials );
	switch ( op.Kind )
	{
	case RENDER_OP_PostCopyScreenEffect: UpdateScreenEffectTexture( 0, op.I[0], op.I[1], op.I[2], op.I[3], op.I[4] != 0 ); break;
	case RENDER_OP_PostSetRenderTarget: ctx->SetRenderTarget( PostTexture( op.I[0] ) ); break;
	case RENDER_OP_PostSetRenderTargetAndViewport:
	{
		ITexture *pTarget = PostTexture( op.I[0] );
		ctx->SetRenderTarget( pTarget );
		if ( pTarget )
			ctx->Viewport( 0, 0, pTarget->GetActualWidth(), pTarget->GetActualHeight() );
		break;
	}
	case RENDER_OP_PostPushRenderTargetAndViewport: ctx->PushRenderTargetAndViewport(); break;
	case RENDER_OP_PostDrawRect:
	{
		IMaterial *pMaterial = SourceSharp_FxMaterialOf( op.I[0] );
		if ( pMaterial )
			ctx->DrawScreenSpaceRectangle( pMaterial, op.I[1], op.I[2], op.I[3], op.I[4], op.F[0], op.F[1], op.F[2], op.F[3],
			                               op.I[5], op.I[6], op.F[6] != 0.0f ? GetClientWorldRenderable() : NULL, (int)op.F[4], (int)op.F[5] );
		break;
	}
	case RENDER_OP_PostMaterialVarFloat:
	{
		IMaterial *pMaterial = SourceSharp_FxMaterialOf( op.I[0] );
		if ( pMaterial && op.I[1] > 0 && op.I[1] < (int)ARRAYSIZE( kPostVarNames ) )
		{
			bool bFound = false;
			IMaterialVar *pVar = pMaterial->FindVar( kPostVarNames[ op.I[1] ], &bFound, false );
			if ( bFound && pVar )
				pVar->SetFloatValue( op.F[0] );
		}
		break;
	}
	case RENDER_OP_PostQueryBegin:
	case RENDER_OP_PostQueryEnd:
	{
		if ( op.I[0] < 0 || op.I[0] >= (int)ARRAYSIZE( state.m_PostQueries ) )
			break;
		OcclusionQueryObjectHandle_t &hQuery = state.m_PostQueries[ op.I[0] ];
		if ( !hQuery )
			hQuery = ctx->CreateOcclusionQueryObject();
		if ( op.Kind == RENDER_OP_PostQueryBegin )
			ctx->BeginOcclusionQueryDrawing( hQuery );
		else
			ctx->EndOcclusionQueryDrawing( hQuery );
		break;
	}
	case RENDER_OP_PostStencil:
		if ( op.I[0] == 0 )
		{
			ctx->SetStencilEnable( false );
			break;
		}
		ctx->SetStencilEnable( true );
		ctx->SetStencilFailOperation( STENCILOPERATION_KEEP );
		ctx->SetStencilZFailOperation( STENCILOPERATION_KEEP );
		ctx->SetStencilReferenceValue( 1 );
		if ( op.I[0] == 1 )
		{
			ctx->SetStencilWriteMask( 1 );
			ctx->SetStencilPassOperation( STENCILOPERATION_REPLACE );
			ctx->SetStencilCompareFunction( STENCILCOMPARISONFUNCTION_ALWAYS );
		}
		else
		{
			ctx->SetStencilTestMask( 1 );
			ctx->SetStencilPassOperation( STENCILOPERATION_KEEP );
			ctx->SetStencilCompareFunction( STENCILCOMPARISONFUNCTION_EQUAL );
		}
		break;
	case RENDER_OP_PostGoalToneScale: ctx->SetGoalToneMappingScale( op.F[0] ); break;
	case RENDER_OP_PostResetToneScale: ctx->ResetToneMappingScale( op.F[0] ); break;
	case RENDER_OP_PostEnableColorCorrection: ctx->EnableColorCorrection( op.I[0] != 0 ); break;
	case RENDER_OP_PostScissor: ctx->SetScissorRect( op.I[0], op.I[1], op.I[2], op.I[3], op.I[4] != 0 ); break;
	case RENDER_OP_PostClearColor3ub: ctx->ClearColor3ub( (unsigned char)op.I[0], (unsigned char)op.I[1], (unsigned char)op.I[2] ); break;
	case RENDER_OP_PostViewport: ctx->Viewport( op.I[0], op.I[1], op.I[2], op.I[3] ); break;
	case RENDER_OP_PostClearBuffers: ctx->ClearBuffers( op.I[0] != 0, op.I[1] != 0 ); break;
	case RENDER_OP_PostDumpTga:
		if ( op.I[2] >= 0 && op.I[2] < (int)ARRAYSIZE( kPostDumpNames ) )
			PostDumpTga( op.I[0], op.I[1], kPostDumpNames[ op.I[2] ], op.I[3] );
		break;
	case RENDER_OP_PostReadbackNext: state.m_nPostReadbackMask = op.I[0]; break;
	case RENDER_OP_PostClearColor4ub: ctx->ClearColor4ub( (unsigned char)op.I[0], (unsigned char)op.I[1], (unsigned char)op.I[2], (unsigned char)op.I[3] ); break;
	case RENDER_OP_PostPushRenderTarget: ctx->PushRenderTargetAndViewport( PostTexture( op.I[0] ) ); break;
	case RENDER_OP_PostMeshBegin:
	{
		state.m_pPostMesh = NULL;
		state.m_bPostMeshIdentity = false;
		IMaterial *pMaterial = SourceSharp_FxMaterialOf( op.I[0] );
		if ( !pMaterial )
			break;
		if ( op.I[3] & 4 )
		{
			// The material's own switch.
			bool bFound = false;
			IMaterialVar *pVar = pMaterial->FindVar( "$disabled", &bFound, false );
			if ( bFound && pVar && pVar->GetIntValue() )
				break;
		}
		if ( op.I[3] & 1 )
		{
			ctx->MatrixMode( MATERIAL_VIEW );
			ctx->PushMatrix();
			ctx->LoadIdentity();
			ctx->MatrixMode( MATERIAL_PROJECTION );
			ctx->PushMatrix();
			ctx->LoadIdentity();
			state.m_bPostMeshIdentity = true;
		}
		ctx->Bind( pMaterial, ( op.I[3] & 2 ) ? GetClientWorldRenderable() : NULL );
		state.m_pPostMesh = ctx->GetDynamicMesh( true );
		state.m_PostMeshBuilder.Begin( state.m_pPostMesh, (MaterialPrimitiveType_t)op.I[1], op.I[2] );
		break;
	}
	case RENDER_OP_PostMeshVertex:
	{
		if ( !state.m_pPostMesh )
			break;
		CMeshBuilder &mb = state.m_PostMeshBuilder;
		mb.Position3f( op.F[0], op.F[1], op.F[2] );
		if ( op.I[0] & 1 )
		{
			float flAlpha;
			V_memcpy( &flAlpha, &op.I[1], sizeof( flAlpha ) );
			mb.Color4f( op.F[5], op.F[6], op.F[7], flAlpha );
		}
		mb.TexCoord2f( 0, op.F[3], op.F[4] );
		if ( !( op.I[0] & 2 ) )
			mb.AdvanceVertex();
		break;
	}
	case RENDER_OP_PostMeshFrame:
	{
		if ( !state.m_pPostMesh )
			break;
		CMeshBuilder &mb = state.m_PostMeshBuilder;
		float flNormalZ;
		V_memcpy( &flNormalZ, &op.I[0], sizeof( flNormalZ ) );
		mb.TangentS3f( op.F[0], op.F[1], op.F[2] );
		mb.TangentT3f( op.F[3], op.F[4], op.F[5] );
		mb.Normal3f( op.F[6], op.F[7], flNormalZ );
		mb.AdvanceVertex();
		break;
	}
	case RENDER_OP_PostMeshEnd:
		if ( state.m_pPostMesh )
		{
			state.m_PostMeshBuilder.End();
			state.m_pPostMesh->Draw();
			state.m_pPostMesh = NULL;
		}
		if ( state.m_bPostMeshIdentity )
		{
			ctx->MatrixMode( MATERIAL_VIEW );
			ctx->PopMatrix();
			ctx->MatrixMode( MATERIAL_PROJECTION );
			ctx->PopMatrix();
			state.m_bPostMeshIdentity = false;
		}
		break;
	}
}

// viewdebug.cpp's OverlayShowTexture: mat_drawTexture's texture on ___debug's
// $basetexture, at 32, 32, the texture's size times the scale managed read.
void DebugShowTexture( float flScale )
{
	ConVarRef drawTexture( "mat_drawTexture", true );
	const char *pszTexture = drawTexture.IsValid() ? drawTexture.GetString() : NULL;
	bool bFound = false;
	IMaterial *pMaterial = materials->FindMaterial( "___debug", TEXTURE_GROUP_OTHER, true );
	IMaterialVar *pBase = pMaterial->FindVar( "$basetexture", &bFound, false );
	if ( !bFound )
		return;
	float w = 64.0f * flScale, h = 64.0f * flScale;
	if ( pszTexture && pszTexture[0] )
	{
		ITexture *pTex = materials->FindTexture( pszTexture, TEXTURE_GROUP_OTHER, false );
		pBase->SetTextureValue( pTex );
		w = pTex->GetActualWidth() * flScale;
		h = pTex->GetActualHeight() * flScale;
	}
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->Bind( pMaterial );
	IMesh *pMesh = pRenderContext->GetDynamicMesh( true );
	CMeshBuilder meshBuilder;
	meshBuilder.Begin( pMesh, MATERIAL_QUADS, 1 );
	const float x = 32, y = 32;
	meshBuilder.Position3f( x, y, 0.0f ); meshBuilder.TexCoord2f( 0, 0, 0 ); meshBuilder.AdvanceVertex();
	meshBuilder.Position3f( x + w, y, 0.0f ); meshBuilder.TexCoord2f( 0, 1, 0 ); meshBuilder.AdvanceVertex();
	meshBuilder.Position3f( x + w, y + h, 0.0f ); meshBuilder.TexCoord2f( 0, 1, 1 ); meshBuilder.AdvanceVertex();
	meshBuilder.Position3f( x, y + h, 0.0f ); meshBuilder.TexCoord2f( 0, 0, 1 ); meshBuilder.AdvanceVertex();
	meshBuilder.End();
	pMesh->Draw();
}

// A list-dependent draw op: its section of the view's program, or stock's
// primitive when managed gave the view none.
bool RunSection( CPlanView *pView )
{
	int nStart = 0, nEnd = 0;
	if ( !pView || !pView->NextSection( nStart, nEnd ) )
		return false;
	// The section may not open or close views; it runs against this one.
	CUtlVector<SourceSharpRenderOp_t> section;
	section.CopyArray( pView->m_Program.Base() + nStart, nEnd - nStart );
	ExecuteOps( section.Base(), section.Count(), -1 );
	return true;
}

void Execute( const SourceSharpRenderOp_t *pOps, int nOps )
{
	ExecutorState_t &state = State();
	ExecuteOps( pOps, nOps, 0 );

	// A plan that left a view open would leave a dangling active renderer.
	while ( CPlanView *pOpen = CurrentView() )
	{
		state.m_Views.RemoveMultipleFromTail( 1 );
		pOpen->Release();
	}
}

//-----------------------------------------------------------------------------
// The CViewRender hooks
//-----------------------------------------------------------------------------
// RenderView through the plan: the inputs, managed's plan, carried out. False when there is no plan to
// draw it with (no managed client, the hooks not in, or the planner declined).
bool PlanRenderView( const CViewSetup &setup, int nClearFlags, int whatToDraw )
{
	ExecutorState_t &state = State();
	state.m_bDecisionPending = false;
	IManagedRenderClient *pManaged = ManagedRenderClient();
	const bool bPlan = state.m_bPlanFrame && pManaged != NULL;
	if ( !bPlan && !RenderTrace::Active() )
		return false;

	SourceSharpRenderInputs_t in;
	GatherInputs( setup, nClearFlags, whatToDraw, in );
	if ( RenderTrace::Active() )
		NoteInputs( in );

	if ( bPlan )
	{
		int nOps = pManaged->RenderPlanBuild( &in, state.m_Ops, kMaxRenderOps );
		if ( nOps >= 0 )
		{
			RenderTrace::Note( "plan\tops=%d", nOps );
			Execute( state.m_Ops, nOps );
			return true;
		}
		RenderTrace::Note( "plan\tdeclined" );
	}

	return false;
}



// The leaf system's list for a view, captured as it is built: its custom-draw
// entities are drawn from one batch (shell_standalone_client.cpp, C7 item 7).
void AfterRenderablesList( const SetupRenderInfo_t &info )
{
	if ( info.m_pRenderList )
		SourceSharp_StandaloneDrawBatchCapture( *info.m_pRenderList );

	// The oracle (C7 step 2): on a recorded frame, either path, the scene the
	// view's draws were decided over.
	if ( RenderTrace::Active() )
	{
		static const int kNoFlags[2] = { 0, 0 };
		ViewSceneBuffer_t scene;
		BuildViewScene( scene, info.m_pWorldListInfo, info.m_pRenderList, info.m_nDetailBuildFrame, NULL, kNoFlags, -1 );
		NoteBytes( "scene", "scenebytes\t", scene.m_Bytes.Base(), scene.m_Bytes.Count() );
	}
}

void H_BuildRenderablesList( IClientLeafSystem *self, const SetupRenderInfo_t &info )
{
	typedef void ( *BuildRenderablesList_t )( IClientLeafSystem *, const SetupRenderInfo_t & );
	( (BuildRenderablesList_t)State().m_BuildRenderablesPatch.pOriginal )( self, info );
	AfterRenderablesList( info );
}

// UpdateShadowDepthTexture, asked by the shadow manager inside a plan frame's
// ShadowComputeDepthTextures op: managed plans CShadowDepthView's draw and it
// runs here, inside the frame's plan. False with no plan to draw it in.
bool PlanShadowDepthView( ITexture *pRenderTarget, ITexture *pDepthTexture, const CViewSetup &shadowView )
{
	ExecutorState_t &state = State();
	IManagedRenderClient *pManaged = ManagedRenderClient();
	if ( state.m_bPlanFrame && pManaged )
	{
		SourceSharpViewSetup_t view;
		FromViewSetup( shadowView, view );
		int32_t nIndexBase = 0;
		int nOps = pManaged->RenderShadowDepthPlan( &view, state.m_ShadowOps, ARRAYSIZE( state.m_ShadowOps ), &nIndexBase );
		if ( nOps >= 0 )
		{
			state.m_pShadowColor = pRenderTarget;
			state.m_pShadowDepth = pDepthTexture;
			ExecuteOps( state.m_ShadowOps, nOps, nIndexBase );
			state.m_pShadowColor = NULL;
			state.m_pShadowDepth = NULL;
			return true;
		}
	}
	return false;
}

bool InstallHooks()
{
	ExecutorState_t &state = State();
	if ( state.m_bHooked )
		return true;
	if ( !VTableSlotPatch::Install( ClientLeafSystem(), &IClientLeafSystem::BuildRenderablesList, (void *)&H_BuildRenderablesList,
	                                &state.m_BuildRenderablesPatch ) )
	{
		return false;
	}
	state.m_bHooked = true;
	return true;
}

} // namespace

bool SourceSharp_RenderView( const CViewSetup &setup, int nClearFlags, int whatToDraw )
{
	return PlanRenderView( setup, nClearFlags, whatToDraw );
}

bool SourceSharp_ShadowDepthView( ITexture *pRenderTarget, ITexture *pDepthTexture, const CViewSetup &shadowView )
{
	return PlanShadowDepthView( pRenderTarget, pDepthTexture, shadowView );
}

bool SourceSharp_ScreenshotView( const CViewSetup &eye, int nWidth, int nHeight, CViewSetup &out )
{
	IManagedRenderClient *pManaged = ManagedRenderClient();
	if ( !pManaged )
		return false;
	SourceSharpViewSetup_t in, decided;
	FromViewSetup( eye, in );
	decided = in;
	pManaged->ViewScreenshot( &in, nWidth, nHeight, &decided );
	ToViewSetup( decided, out );
	return true;
}

void SourceSharp_FreezeFrame( float flFreezeTime )
{
	if ( IManagedRenderClient *pManaged = ManagedRenderClient() )
		pManaged->ViewFreezeFrame( flFreezeTime, gpGlobals->curtime );
}

int SourceSharp_ActiveViewDrawFlags()
{
	CPlanView *pView = CurrentView();
	return pView ? pView->GetDrawFlags() : 0;
}

bool SourceSharp_FrameViewDecision( CViewSetup &eye, int &nClearFlags, int &nDrawFlags, int *pRandomClear, bool &bOverview )
{
	ExecutorState_t &state = State();
	if ( !state.m_bDecisionPending )
		return false;
	const SourceSharpRenderFrameOut_t &d = state.m_FrameDecision;
	ToViewSetup( d.Eye, eye );
	nClearFlags = d.ClearFlags;
	nDrawFlags = d.DrawFlags;
	pRandomClear[0] = d.RandomClear;
	pRandomClear[1] = d.ClearR;
	pRandomClear[2] = d.ClearG;
	pRandomClear[3] = d.ClearB;
	bOverview = d.Overview != 0;
	return true;
}

float CvarFloat( const char *pszName )
{
	ConVarRef var( pszName, true );
	return var.IsValid() ? var.GetFloat() : 0.0f;
}

// SetUpViews' inputs before the override (view.cpp).
void GatherViewSetUp( SourceSharpViewSetUpIn_t &in )
{
	in.FarZ = CvarFloat( "r_farz" );
	in.MapExtents = CvarFloat( "r_mapextents" );
	const fogparams_t *pFog = SourceSharp_ViewerFogParams();
	in.FogFarZ = pFog ? pFog->farz : 0.0f;
	in.DefaultFov = CvarFloat( "default_fov" );
	in.ViewModelFov = g_pClientMode->GetViewModelFOV();
}

// CViewRender::Render's inputs: the engine's, the client mode's and the
// convars, with the client mode's viewport adjustment made on a copy of the
// rectangle (stock's ToolFramework_AdjustEngineViewport after it is deleted:
// SFM is dead, user ruling 2026-10-19). Decides nothing.
void GatherFrame( const vrect_t &rect, const CViewSetup &eye, SourceSharpRenderFrameIn_t &in )
{
	FromViewSetup( eye, in.Eye );
	in.ScreenAspect = engine->GetScreenAspectRatio();
	in.Windowed = engine->IsWindowedMode() ? 1 : 0;
	in.MaxClients = gpGlobals->maxClients;
	in.RestrictAspectFov = (int32_t)CvarFloat( "sv_restrict_aspect_ratio_fov" );
	in.ViewportScale = CvarFloat( "mat_viewportscale" );
	in.GlClear = CvarFloat( "gl_clear" ) != 0.0f ? 1 : 0;
	in.GlClearRandomColor = CvarFloat( "gl_clear_randomcolor" ) != 0.0f ? 1 : 0;
	in.ClearColorForAntialiasing = 0;
	if ( IsPosix() )
	{
		MaterialAdapterInfo_t adapterInfo;
		materials->GetDisplayAdapterInfo( materials->GetCurrentAdapter(), adapterInfo );
		if ( adapterInfo.m_VendorID == 0x1002 && g_pMaterialSystem->GetCurrentConfigForVideoCard().m_nAASamples > 1 )
			in.ClearColorForAntialiasing = 1;
	}
	in.ClientModeDrawViewModel = g_pClientMode->ShouldDrawViewModel() ? 1 : 0;
	in.LevelOverview = CvarFloat( "cl_leveloverview" );
	int x = rect.x, y = rect.y, w = rect.width, h = rect.height;
	g_pClientMode->AdjustEngineViewport( x, y, w, h );
	in.ViewportX = x;
	in.ViewportY = y;
	in.ViewportWidth = w;
	in.ViewportHeight = h;
}

bool SourceSharp_SetUpView( float &flZFar, float &flZFarViewmodel, float &flZNear, float &flZNearViewmodel, float &flFov,
                            float &flViewModelFovOffset )
{
	IManagedRenderClient *pManaged = ManagedRenderClient();
	if ( !pManaged )
		return false;
	SourceSharpViewSetUpIn_t in;
	SourceSharpViewSetUpOut_t out;
	GatherViewSetUp( in );
	pManaged->ViewSetUp( &in, &out );
	flZFar = out.ZFar;
	flZFarViewmodel = out.ZFarViewmodel;
	flZNear = out.ZNear;
	flZNearViewmodel = out.ZNearViewmodel;
	flFov = out.Fov;
	flViewModelFovOffset = out.ViewModelFovOffset;
	return true;
}

bool SourceSharp_AskViewPlanes( float &flZNear, float &flZFar )
{
	IManagedRenderClient *pManaged = ManagedRenderClient();
	if ( !pManaged )
		return false;
	SourceSharpViewSetUpIn_t in;
	SourceSharpViewSetUpOut_t out;
	GatherViewSetUp( in );
	pManaged->ViewSetUp( &in, &out );
	flZNear = out.ZNear;
	flZFar = out.ZFar;
	return true;
}

void SourceSharp_ViewRender( vrect_t *rect )
{
	ExecutorState_t &state = State();
	if ( !state.m_bHooked && !InstallHooks() )
	{
		static bool s_bSaid = false;
		if ( !s_bSaid )
		{
			s_bSaid = true;
			Warning( "[SourceSharp] render plan: the leaf system's BuildRenderablesList could not be hooked; views draw their custom entities one by one\n" );
		}
	}

	SourceSharpRenderFrameIn_t in;
	in.X = rect->x;
	in.Y = rect->y;
	in.Width = rect->width;
	in.Height = rect->height;
	in.FrameCount = gpGlobals->framecount;
	in.CurTime = gpGlobals->curtime;
	in.LastPath = state.m_nLastPath;
	in.LastRenderMs = state.m_flLastRenderMs;
	GatherFrame( *rect, *view->GetPlayerViewSetup(), in );

	SourceSharpRenderFrameOut_t out;
	memset( &out, 0, sizeof( out ) );

	IManagedRenderClient *pManaged = ManagedRenderClient();
	if ( pManaged )
	{
		pManaged->RenderFrame( &in, &out );

		state.m_FrameDecision = out;
		state.m_bDecisionPending = true;
	}

	const bool bTrace = out.Trace != 0 && RenderTrace::Begin();
	if ( bTrace )
	{
		RenderTrace::Note( "frame\tpath=%d framecount=%d rect=%d,%d,%d,%d", out.Path, in.FrameCount, in.X, in.Y, in.Width, in.Height );
		NoteFrameEnvironment();
	}

	// Every frame is the plan's: stock's viewrender left client.so (C7 step 3).
	state.m_bPlanFrame = true;
	const double flStart = ss::SecondsSinceStart();
	view->Render( rect );
	state.m_flLastRenderMs = (float)( ( ss::SecondsSinceStart() - flStart ) * 1000.0 );
	state.m_nLastPath = state.m_bPlanFrame ? 1 : 0;
	state.m_bPlanFrame = false;
	state.m_bDecisionPending = false;

	if ( bTrace )
	{
		RenderTrace::End();
		int32_t nLength = 0;
		const char *pText = RenderTrace::Text( &nLength );
		if ( pManaged )
			pManaged->RenderTraceDone( out.TraceTag, pText, nLength );
	}
}
