//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- the client's IViewRender ===========================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// plan_client_novalve C7 step 3 (docs/plans/c7_step3_eviction.md): view.cpp,
// viewrender.cpp and viewdebug.cpp left client.so, and this is what they gave
// it. CShellViewRender is `view`: the frame's view is managed's decision
// (ViewFrame: SetUpViews' planes and FOV, Render's eye), every view is the
// render plan's (shell_view_executor_client.cpp), and this carries both out --
// the client mode's override and hooks, the engine's main view and audio, the
// 2D panels, the save-game screenshot. The view state those TUs kept in
// statics (the main and current views) is this object's, and the names the
// fork's TUs still call -- MainViewOrigin, CurrentViewForward,
// SetupCurrentView, R_CullSphere, ... -- answer from it. The convars the fork's
// TUs link against by object stay native here; the rest are managed
// declarations (RenderViewConVars.cs).
//=============================================================================//

#include "cbase.h"
#include "iviewrender.h"
#include "viewrender.h"
#include "shell_view_executor_client.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"
#include "view.h"
#include "iviewrender_beams.h"
#include "c_te_legacytempents.h"
#include "ScreenSpaceEffects.h"
#include "toolframework_client.h"
#include "view_facts.h"
#include "iinput.h"
#include "c_baseplayer.h"
#include "clienteffectprecachesystem.h"
#include "vgui/ISurface.h"
#include "vgui_controls/Controls.h"
#include "tier1/convar.h"
#include "shell_diagnostics.h"
#include "iclientmode.h"
#include "cl_mat_stub.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "ienginevgui.h"
#include "ss_image_encode.h"
#include "filesystem.h"
#include "tier1/utlbuffer.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

namespace
{
class CShellViewRender final : public IViewRender, public IReplayScreenshotSystem
{
public:

	// CViewRender::Init: the beams and the temp entities.
	void Init( void ) override
	{
		beams->InitBeams();
		tempents->Init();
	}

	// CViewRender::LevelInit: a level starts with no beams, temp entities,
	// list stamps, freeze frame or screen overlay, and its screen-space effects.
	void LevelInit( void ) override
	{
		beams->ClearBeams();
		tempents->Clear();
		m_nBuildWorldListsNumber = 0;
		m_nBuildRenderablesListsNumber = 0;
		SourceSharp_FreezeFrame( 0.0f );
		ReleaseScreenOverlay();
		g_pScreenSpaceEffects->InitScreenSpaceEffects();
	}

	// CViewRender::LevelShutdown.
	void LevelShutdown( void ) override
	{
		g_pScreenSpaceEffects->ShutdownScreenSpaceEffects();
		m_nCurrentViewID = VIEW_NONE;
	}

	// CViewRender::Shutdown.
	void Shutdown( void ) override
	{
		ReleaseScreenOverlay();
		beams->ShutdownBeams();
		tempents->Shutdown();
	}

	// CViewRender::OnRenderStart: the frame's main view, before the frame's
	// entities simulate (they read the viewmodel's attachments from it).
	void OnRenderStart() override { SetUpViews(); }
	void SetUpViews();
	void Render( vrect_t *rect ) override;
	// CViewRender::RenderView is the render plan's: managed plans the view and
	// the executor carries it out. A view the planner declines -- reflective
	// glass, which this build never has in view -- or one with no managed
	// client to plan it draws nothing, and says so once.
	void RenderView( const CViewSetup &setup, int nClearFlags, int whatToDraw ) override
	{
		if ( SourceSharp_RenderView( setup, nClearFlags, whatToDraw ) || m_bSaidUnplanned )
			return;
		m_bSaidUnplanned = true;
		Warning( "[SourceSharp] view: a view with no plan to draw it (declined, or no managed client) draws nothing\n" );
	}

	// The draw flags of the view the executor is drawing (stock: the active renderer's).
	int GetDrawFlags() override { return SourceSharp_ActiveViewDrawFlags(); }
	// PITCH DRIFT IS NOT SHIPPED (view.cpp's ruling, kept): DriftPitch is the
	// only reader of the state these two kept, and it has never run on this
	// client -- the view angle is the engine's, with no second author. So
	// `centerview` and managed input's stop have nothing to set.
	void StartPitchDrift( void ) override {}
	void StopPitchDrift( void ) override {}
	// The frustum the engine's Push3DView fills and the culls read.
	VPlane *GetFrustum() override { return m_Frustum; }
	// r_drawbrushmodels, read by name (the engine's convar); drawn when it is missing, as stock.
	bool ShouldDrawBrushModels( void ) override
	{
		ConVarRef drawBrushModels( "r_drawbrushmodels", true );
		return !drawBrushModels.IsValid() || drawBrushModels.GetInt() != 0;
	}
	// The mono eye SetUpViews made and Render widened (CViewRender's m_View).
	const CViewSetup *GetPlayerViewSetup( void ) const override { return &m_View; }
	// The view the plan is drawing, and the last one it drew between frames.
	const CViewSetup *GetViewSetup( void ) const override { return &m_CurrentView; }

	void SetCurrentView( const CViewSetup &setup ) { m_CurrentView = setup; }
	// No vis for the next frame's views; Render clears it.
	void DisableVis( void ) override { m_bForceNoVis = true; }

	bool ForceNoVis() const { return m_bForceNoVis; }

	// The view state (phase 4): the main view and the last one, the view being drawn.
	ShellCamera_t m_Main;
	Vector m_vecPrevMainOrigin = vec3_origin;
	QAngle m_angPrevMainAngles = vec3_angle;
	ShellCamera_t m_Current;
	int m_nCurrentViewID = VIEW_NONE;
	bool m_bCanAccessCurrentView = false;

private:
	bool m_bForceNoVis = false;
	bool m_bSaidNoDecision = false;
	bool m_bSaidUnplanned = false;
	CViewSetup m_View;
	// CViewRender's constructor's distances.
	float m_flCheapWaterStartDistance = 0.0f;
	float m_flCheapWaterEndDistance = 0.1f;
	IMaterial *m_pScreenOverlay = NULL;
	IClientUnknown *m_pCurrentlyDrawingEntity = NULL;
	int m_nBuildWorldListsNumber = 0;
	int m_nBuildRenderablesListsNumber = 0;
	CViewSetup m_CurrentView;
	Frustum m_Frustum;

public:
	// The world and renderable list build numbers the executor's views count
	// up (the leaf system's and the detail objects' frame stamps).
	int BuildWorldListsNumber() const override { return m_nBuildWorldListsNumber; }
	void IncWorldListsNumber() { ++m_nBuildWorldListsNumber; }
	void IncRenderablesListsNumber() { ++m_nBuildRenderablesListsNumber; }
	int RenderablesListsNumber() const { return m_nBuildRenderablesListsNumber; }
	// The cheap-water distances the map's water LOD control sets
	// (shell_worldrender.cpp) and the plan and the water proxy read.
	void SetCheapWaterStartDistance( float flDistance ) override
	{
		m_flCheapWaterStartDistance = flDistance;
	}
	void SetCheapWaterEndDistance( float flDistance ) override
	{
		m_flCheapWaterEndDistance = flDistance;
	}
	void GetWaterLODParams( float &flStart, float &flEnd ) override
	{
		flStart = m_flCheapWaterStartDistance;
		flEnd = m_flCheapWaterEndDistance;
	}
	void DriftPitch( void ) override
	{
		SourceSharp_NotShipped( "pitch drift (v_centermove)",
			"the drift is written into the local player entity's own local angles, "
			"and on this client the view angle is the engine's -- there is no second "
			"author for it" );
	}
	// The full-screen overlay material (r_screenoverlay), held by reference as
	// stock's CMaterialReference held it.
	void SetScreenOverlayMaterial( IMaterial *pMaterial ) override
	{
		if ( pMaterial )
			pMaterial->IncrementReferenceCount();
		ReleaseScreenOverlay();
		m_pScreenOverlay = pMaterial;
	}
	IMaterial *GetScreenOverlayMaterial() override { return m_pScreenOverlay; }

	void ReleaseScreenOverlay()
	{
		if ( m_pScreenOverlay )
			m_pScreenOverlay->DecrementReferenceCount();
		m_pScreenOverlay = NULL;
	}
	// SAVEGAME_SCREENSHOT_WIDTH by SAVEGAME_SCREENSHOT_HEIGHT (view.cpp, PC).
	void WriteSaveGameScreenshot( const char *pFilename ) override { WriteSaveGameScreenshotOfSize( pFilename, 180, 100, false, false ); }
	void WriteSaveGameScreenshotOfSize( const char *pFilename, int width, int height, bool bCreatePowerOf2Padded, bool bWriteVTF ) override;
	// REPLAY IS NOT SHIPPED (step 7g): there is no screenshot taker to hand a
	// frame to. Both are IReplayScreenshotSystem's too, which this object is,
	// as stock's was.
	void WriteReplayScreenshot( WriteReplayScreenshotParams_t &params ) override
	{
		SourceSharp_NotShipped( "replay screenshot",
			"replay is not shipped: replay/replay_screenshot.cpp is not in this "
			"build and there is no screenshot taker to hand the frame to" );
	}
	void UpdateReplayScreenshotCache() override {}
	// NOT SHIPPED: stock kept this view and drew it inside its own RenderView,
	// after the 3D scene; the render plan has no overlay-view arm, and nothing
	// in this build calls it (docs/todo.md).
	void QueueOverlayRenderView( const CViewSetup &setup, int nClearFlags, int whatToDraw ) override
	{
		SourceSharp_NotShipped( "overlay render view (QueueOverlayRenderView)",
			"the render plan has no overlay-view arm, and nothing in this build queues one" );
	}
	// SetUpViews' planes, as managed decides them (ViewFrame: VIEW_NEARZ, and
	// r_farz or the map's diagonal pulled in by fog). 0 with no managed client.
	float GetZNear() override
	{
		float flNear = 0.0f, flFar = 0.0f;
		SourceSharp_AskViewPlanes( flNear, flFar );
		return flNear;
	}
	float GetZFar() override
	{
		float flNear = 0.0f, flFar = 0.0f;
		SourceSharp_AskViewPlanes( flNear, flFar );
		return flFar;
	}

	// r_screenfademinsize and r_screenfademaxsize, read by name.
	void GetScreenFadeDistances( float *pMin, float *pMax ) override
	{
		if ( pMin )
		{
			ConVarRef minSize( "r_screenfademinsize", true );
			*pMin = minSize.IsValid() ? minSize.GetFloat() : 0.0f;
		}
		if ( pMax )
		{
			ConVarRef maxSize( "r_screenfademaxsize", true );
			*pMax = maxSize.IsValid() ? maxSize.GetFloat() : 0.0f;
		}
	}
	// Who is being drawn right now: the tool framework's material-proxy
	// recording asks it, the executor's entity draws set it.
	IClientUnknown *GetCurrentlyDrawingEntity() override { return m_pCurrentlyDrawingEntity; }
	void SetCurrentlyDrawingEntity( IClientUnknown *pEnt ) override { m_pCurrentlyDrawingEntity = pEnt; }
	bool UpdateShadowDepthTexture( ITexture *pRenderTarget, ITexture *pDepthTexture, const CViewSetup &shadowView ) override
	{
		// The shadow-depth view is the plan's (inside the frame's
		// ShadowComputeDepthTextures op).
		return SourceSharp_ShadowDepthView( pRenderTarget, pDepthTexture, shadowView );
	}
	// The freeze frame's state is managed's (its rule, CViewRender::FreezeFrame's;
	// the plan draws and captures it).
	void FreezeFrame( float flFreezeTime ) override { SourceSharp_FreezeFrame( flFreezeTime ); }
	IReplayScreenshotSystem *GetReplayScreenshotSystem() override { return this; }
};

//-----------------------------------------------------------------------------
// CViewRender::Render (view.cpp), mono: managed decided the eye -- its FOVs,
// viewport, aspect, clear and draw flags, the level overview (ViewFrame,
// asked at View_Render) -- and this carries it out: the main view, the client
// mode's hooks, the debug clears, the 3D view, the full-screen 2D panels.
// The eye goes back into the player view (GetPlayerViewSetup's answer), as
// stock's Render left it there.
//-----------------------------------------------------------------------------
void CShellViewRender::Render( vrect_t *rect )
{
	CViewSetup eye;
	int nClearFlags = 0, nDrawFlags = 0;
	int randomClear[4] = { 0, 0, 0, 0 };
	bool bOverview = false;
	if ( !SourceSharp_FrameViewDecision( eye, nClearFlags, nDrawFlags, randomClear, bOverview ) )
	{
		if ( !m_bSaidNoDecision )
		{
			m_bSaidNoDecision = true;
			Warning( "[SourceSharp] view: no view decision from managed this frame; nothing is drawn\n" );
		}
		return;
	}

	CMatStubHandler matStub;
	engine->EngineStats_BeginFrame();
	m_bForceNoVis = false;

	// The main view is the eye SetUpViews made, before the overview moves it.
	CViewSetup &player = m_View;
	render->SetMainView( player.origin, player.angles );

	g_pClientMode->PreRender( &eye );

	if ( randomClear[0] )
	{
		CMatRenderContextPtr pRenderContext( materials );
		pRenderContext->ClearColor3ub( (unsigned char)randomClear[1], (unsigned char)randomClear[2], (unsigned char)randomClear[3] );
		pRenderContext->ClearBuffers( true, false, false );
	}
	if ( bOverview )
	{
		CMatRenderContextPtr pRenderContext( materials );
		pRenderContext->ClearColor4ub( 0, 255, 0, 255 );
	}

	player = eye;
	RenderView( eye, nClearFlags, nDrawFlags );

	g_pClientMode->PostRender();
	engine->EngineStats_EndFrame();
	matStub.End();

	// The UI drawn full screen (the escape menu and the cursor).
	CViewSetup view2d;
	view2d.x = rect->x;
	view2d.y = rect->y;
	view2d.width = rect->width;
	view2d.height = rect->height;
	render->Push2DView( view2d, 0, NULL, GetFrustum() );
	render->VGui_Paint( PAINT_UIPANELS | PAINT_CURSOR );
	render->PopView( GetFrustum() );
}

//-----------------------------------------------------------------------------
// CViewRender::WriteSaveGameScreenshotOfSize (view.cpp): the player's eye
// drawn through the plan into the top left of the back buffer, read back, and
// written as a TGA or VTF (padded to powers of two when asked) to //MOD/.
// Managed decides the view (ViewFrame.Screenshot: the FOV widened to the
// rectangle); this carries out the draw and the file.
//-----------------------------------------------------------------------------
void CShellViewRender::WriteSaveGameScreenshotOfSize( const char *pFilename, int width, int height, bool bCreatePowerOf2Padded,
                                                     bool bWriteVTF )
{
	CViewSetup viewSetup;
	if ( width <= 0 || height <= 0 || !SourceSharp_ScreenshotView( *GetPlayerViewSetup(), width, height, viewSetup ) )
	{
		Warning( "[SourceSharp] view: no save-game screenshot of %s (%dx%d): no view from managed\n", pFilename, width, height );
		return;
	}

	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->MatrixMode( MATERIAL_PROJECTION );
	pRenderContext->PushMatrix();
	pRenderContext->MatrixMode( MATERIAL_VIEW );
	pRenderContext->PushMatrix();

	// The back buffer, with a viewport the screenshot's size.
	pRenderContext->PushRenderTargetAndViewport( NULL, 0, 0, width, height );

	// No HUD, no viewmodel.
	RenderView( viewSetup, VIEW_CLEAR_DEPTH | VIEW_CLEAR_COLOR, 0 );

	// The pixels cross to managed once, which pads them and encodes the file
	// (C9: ScreenshotEncoding, held to the pristine bitmap.a / vtf.a).
	const int nPixelBytes = width * height * 3;
	uint8_t *pBlock = ss::ImageEncodeBlock( nPixelBytes );
	if ( !pBlock )
		Error( "Couldn't write bitmap data snapshot.\n" );
	pRenderContext->ReadPixels( 0, 0, width, height, ss::ImageEncodePixels( pBlock ), IMAGE_FORMAT_RGB888 );

	int nFileBytes = 0;
	unsigned char *pFile = ss::ImageEncode( bWriteVTF ? ss::kImageVtfRgb : ss::kImageTgaRgb, width, height,
	                                        bCreatePowerOf2Padded, pBlock, nPixelBytes, &nFileBytes, true );
	free( pBlock );
	if ( !pFile )
		Error( "Couldn't write bitmap data snapshot.\n" );

	// The async write takes the file's memory (on the engine heap: the
	// filesystem frees it with tier0's allocator).
	char szPathedFileName[ MAX_PATH ];
	Q_snprintf( szPathedFileName, sizeof( szPathedFileName ), "//MOD/%s", pFilename );
	filesystem->AsyncWrite( szPathedFileName, pFile, nFileBytes, true );

	pRenderContext->PopRenderTargetAndViewport();
	pRenderContext->MatrixMode( MATERIAL_PROJECTION );
	pRenderContext->PopMatrix();
	pRenderContext->MatrixMode( MATERIAL_VIEW );
	pRenderContext->PopMatrix();
}

//-----------------------------------------------------------------------------
// The demo free camera (view.cpp's CalcDemoViewOverride): managed's
// DemoViewCamera, around the engine's view angles and the input's extra
// mouse sample it needs between its two steps.
//-----------------------------------------------------------------------------
void DemoCamera( Vector &origin, QAngle &angles )
{
	IManagedRenderClient *pManaged = ManagedRenderClient();
	if ( !pManaged )
		return;
	float in[ 6 ] = { origin.x, origin.y, origin.z, angles.x, angles.y, angles.z };
	float out[ 6 ] = { 0, 0, 0, 0, 0, 0 };
	if ( !pManaged->ViewDemoCamera( 0, in, out ) )
		return;
	QAngle demoAngles( out[ 0 ], out[ 1 ], out[ 2 ] );
	engine->SetViewAngles( demoAngles );
	input->ExtraMouseSample( gpGlobals->absoluteframetime, true );
	engine->GetViewAngles( demoAngles );
	float move[ 6 ] = { demoAngles.x, demoAngles.y, demoAngles.z, gpGlobals->absoluteframetime, 0, 0 };
	pManaged->ViewDemoCamera( 1, move, out );
	origin.Init( out[ 0 ], out[ 1 ], out[ 2 ] );
	angles.Init( out[ 3 ], out[ 4 ], out[ 5 ] );
}

//-----------------------------------------------------------------------------
// CViewRender::SetUpViews (view.cpp), mono: managed decides the planes and
// the FOV (ViewFrame, one ViewSetUp crossing); the client mode's override
// puts the viewer's eye in (CSourceSharpClientMode::OverrideView), a view
// entity overrides it, the tools and a playing demo may move it; then the
// viewmodel's FOV follows the override's zoom, the bones open for the frame,
// the main view and the engine's audio state take the eye.
//-----------------------------------------------------------------------------
void CShellViewRender::SetUpViews()
{
	float flViewModelFovOffset = 0.0f;
	CViewSetup &eye = m_View;
	if ( !SourceSharp_SetUpView( eye.zFar, eye.zFarViewmodel, eye.zNear, eye.zNearViewmodel, eye.fov, flViewModelFovOffset ) )
		return;
	eye.m_bOrtho = false;
	eye.m_bViewToProjectionOverride = false;
	eye.m_eStereoEye = STEREO_EYE_MONO;

	// Spatial partition access to the client's edicts, for the override.
	::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, false );

	// Names the refusal when the engine says this client watches a broadcast
	// (the HLTV camera is not shipped).
	SourceSharp_HltvCameraAvailable();

	// The view starts at nothing; the client mode's override fills it.
	eye.origin.Init();
	eye.angles.Init();
	g_pClientMode->OverrideView( &eye );

	// Looking out of something else's eyes (point_viewcontrol, a camera): the
	// last word, after the override (view_facts.h).
	if ( !g_nKillCamMode )
	{
		Vector vecViewEntityOrigin;
		QAngle angViewEntityAngles;
		if ( SourceSharp_ViewEntityPose( &vecViewEntityOrigin, &angViewEntityAngles ) )
		{
			eye.origin = vecViewEntityOrigin;
			eye.angles = angViewEntityAngles;
		}
	}

	ToolFramework_SetupEngineView( eye.origin, eye.angles, eye.fov );

	if ( engine->IsPlayingDemo() )
		DemoCamera( eye.origin, eye.angles );

	// The viewmodel's FOV moves with the override's zoom: the offset is
	// managed's (ViewSetUpOut), the zoomed fov the one operand only this has.
	eye.fovViewmodel = eye.fov + flViewModelFovOffset;

	::partition->SuppressLists( PARTITION_ALL_CLIENT_EDICTS, true );

	// The hearing origin, and the underwater DSP from the viewer's water level.
	AudioState_t audioState;
	audioState.m_Origin = eye.origin;
	audioState.m_Angles = eye.angles;
	audioState.m_bIsUnderwater = SourceSharp_ViewerAudioIsUnderwater( eye.origin );
	ToolFramework_SetupAudioState( audioState );
	eye.origin = audioState.m_Origin;
	eye.angles = audioState.m_Angles;
	engine->SetAudioState( audioState );

	ShellView_SetMainView( eye.origin, eye.angles );
}

CShellViewRender g_ShellViewRender;
} // namespace

//-----------------------------------------------------------------------------
// The view state (C7 step 3 phase 4)
//-----------------------------------------------------------------------------
void ShellView_ComputeCamera( const Vector &vecOrigin, const QAngle &angles, ShellCamera_t &out )
{
	out.origin = vecOrigin;
	out.angles = angles;
	AngleVectors( angles, &out.forward, &out.right, &out.up );
	for ( int i = 0; i < 3; ++i )
	{
		out.worldToView[ 0 ][ i ] = out.right[ i ];
		out.worldToView[ 1 ][ i ] = out.up[ i ];
		out.worldToView[ 2 ][ i ] = -out.forward[ i ];
		out.worldToView[ 3 ][ i ] = 0.0F;
	}
	out.worldToView[ 0 ][ 3 ] = -DotProduct( out.right, vecOrigin );
	out.worldToView[ 1 ][ 3 ] = -DotProduct( out.up, vecOrigin );
	out.worldToView[ 2 ][ 3 ] = DotProduct( out.forward, vecOrigin );
	out.worldToView[ 3 ][ 3 ] = 1.0F;
}

void ShellView_SetMainView( const Vector &vecOrigin, const QAngle &angles )
{
	g_ShellViewRender.m_vecPrevMainOrigin = g_ShellViewRender.m_Main.origin;
	g_ShellViewRender.m_angPrevMainAngles = g_ShellViewRender.m_Main.angles;
	ShellView_ComputeCamera( vecOrigin, angles, g_ShellViewRender.m_Main );
}

const ShellCamera_t &ShellView_Main()
{
	return g_ShellViewRender.m_Main;
}

const Vector &ShellView_PrevMainOrigin()
{
	return g_ShellViewRender.m_vecPrevMainOrigin;
}

const QAngle &ShellView_PrevMainAngles()
{
	return g_ShellViewRender.m_angPrevMainAngles;
}

void ShellView_SetupCurrentView( const Vector &vecOrigin, const QAngle &angles, int nViewID, bool bEffects )
{
	ShellView_ComputeCamera( vecOrigin, angles, g_ShellViewRender.m_Current );
	g_ShellViewRender.m_nCurrentViewID = nViewID;
	g_ShellViewRender.m_bCanAccessCurrentView = true;
	if ( !bEffects )
		return;

	// The fade distances, cached for the models drawn in this view.
	float flScreenFadeMinSize, flScreenFadeMaxSize;
	g_ShellViewRender.GetScreenFadeDistances( &flScreenFadeMinSize, &flScreenFadeMaxSize );
	modelinfo->SetViewScreenFadeRange( flScreenFadeMinSize, flScreenFadeMaxSize );

	// INT_RENDERPARM_WRITE_DEPTH_TO_DESTALPHA is 10 (public/renderparm.h,
	// which client.so does not otherwise include); the frozen frames record
	// it as `ctx.SetIntRenderingParameter 10=...` at every view.
	const int kWriteDepthToDestAlpha = 10;
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->SetIntRenderingParameter( kWriteDepthToDestAlpha, ( nViewID == VIEW_MAIN || nViewID == VIEW_3DSKY ) ? 1 : 0 );
}

void ShellView_FinishCurrentView()
{
	g_ShellViewRender.m_bCanAccessCurrentView = false;
}

void ShellView_AllowCurrentViewAccess( bool bAllow )
{
	g_ShellViewRender.m_bCanAccessCurrentView = bAllow;
}

bool ShellView_CurrentViewAccessAllowed()
{
	return g_ShellViewRender.m_bCanAccessCurrentView;
}

const ShellCamera_t &ShellView_Current()
{
	return g_ShellViewRender.m_Current;
}

int ShellView_CurrentViewID()
{
	return g_ShellViewRender.m_nCurrentViewID;
}

bool ShellViewRender_ForceNoVis()
{
	return g_ShellViewRender.ForceNoVis();
}

void ShellViewRender_SetCurrentView( const CViewSetup &setup )
{
	g_ShellViewRender.SetCurrentView( setup );
}

void ShellViewRender_IncWorldListsNumber()
{
	g_ShellViewRender.IncWorldListsNumber();
}

void ShellViewRender_IncRenderablesListsNumber()
{
	g_ShellViewRender.IncRenderablesListsNumber();
}

int ShellViewRender_RenderablesListsNumber()
{
	return g_ShellViewRender.RenderablesListsNumber();
}

IViewRender *ShellViewRender()
{
	return &g_ShellViewRender;
}


//-----------------------------------------------------------------------------
// The names view.cpp, viewrender.cpp and viewdebug.cpp gave client.so that the
// fork's TUs and ours still call, answered from CShellViewRender.
//-----------------------------------------------------------------------------
IViewRender *view = NULL;

const Vector &MainViewOrigin() { return ShellView_Main().origin; }
const QAngle &MainViewAngles() { return ShellView_Main().angles; }
const Vector &MainViewForward() { return ShellView_Main().forward; }
const Vector &MainViewRight() { return ShellView_Main().right; }
const Vector &MainViewUp() { return ShellView_Main().up; }
const VMatrix &MainWorldToViewMatrix() { return ShellView_Main().worldToView; }
const Vector &PrevMainViewOrigin() { return ShellView_PrevMainOrigin(); }
const QAngle &PrevMainViewAngles() { return ShellView_PrevMainAngles(); }

const Vector &CurrentViewOrigin() { Assert( ShellView_CurrentViewAccessAllowed() ); return ShellView_Current().origin; }
const QAngle &CurrentViewAngles() { Assert( ShellView_CurrentViewAccessAllowed() ); return ShellView_Current().angles; }
const Vector &CurrentViewForward() { Assert( ShellView_CurrentViewAccessAllowed() ); return ShellView_Current().forward; }
const Vector &CurrentViewRight() { Assert( ShellView_CurrentViewAccessAllowed() ); return ShellView_Current().right; }
const Vector &CurrentViewUp() { Assert( ShellView_CurrentViewAccessAllowed() ); return ShellView_Current().up; }
const VMatrix &CurrentWorldToViewMatrix() { Assert( ShellView_CurrentViewAccessAllowed() ); return ShellView_Current().worldToView; }
view_id_t CurrentViewID() { return (view_id_t)ShellView_CurrentViewID(); }
bool DrawingShadowDepthView( void ) { return ShellView_CurrentViewID() == VIEW_SHADOW_DEPTH_TEXTURE; }

void SetupCurrentView( const Vector &vecOrigin, const QAngle &angles, view_id_t viewID )
{
	ShellView_SetupCurrentView( vecOrigin, angles, viewID, true );
}
void FinishCurrentView() { ShellView_FinishCurrentView(); }
void AllowCurrentViewAccess( bool allow ) { ShellView_AllowCurrentViewAccess( allow ); }
bool IsCurrentViewAccessAllowed() { return ShellView_CurrentViewAccessAllowed(); }

// Whether a sphere is wholly outside any of the planes (a frustum cull).
bool R_CullSphere( VPlane const *pPlanes, int nPlanes, Vector const *pCenter, float radius )
{
	for ( int i = 0; i < nPlanes; i++ )
	{
		if ( pPlanes[ i ].DistTo( *pCenter ) < -radius )
			return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// The convars fork TUs link against by object (`extern ConVar ...`):
// detailobjectsystem.cpp, glow_overlay.cpp, particlemgr.cpp, clientshadowmgr.cpp
// and view_scene.h's wireframe test. The rest of these TUs' convars are
// managed declarations (RenderViewConVars.cs).
//-----------------------------------------------------------------------------
ConVar mat_wireframe( "mat_wireframe", "0", FCVAR_CHEAT );
const ConVar *sv_cheats = NULL;
ConVar r_DrawDetailProps( "r_DrawDetailProps", "1", FCVAR_NONE, "0=Off, 1=Normal, 2=Wireframe" );
ConVar cl_drawshadowtexture( "cl_drawshadowtexture", "0", FCVAR_CHEAT );

// cl_software_cursor: its change hands vgui the larger software cursor (VR,
// which also asked for it, is not shipped).
static void SoftwareCursorChanged( IConVar *pVar, const char *pOldValue, float flOldValue )
{
	vgui::surface()->SetSoftwareCursor( ( (ConVar *)pVar )->GetBool() );
}
static ConVar cl_software_cursor( "cl_software_cursor", "0", FCVAR_ARCHIVE,
	"Switches the game to use a larger software cursor instead of the normal OS cursor", SoftwareCursorChanged );

//-----------------------------------------------------------------------------
// The console commands that work on the view itself.
//-----------------------------------------------------------------------------
// getpos and spec_pos: the main view, or ("2") the player's own position.
static void ViewPosition( const CCommand &args, Vector &vecOrigin, QAngle &angles, bool &bPlayer )
{
	vecOrigin = MainViewOrigin();
	angles = MainViewAngles();
	bPlayer = args.ArgC() == 2 && atoi( args[ 1 ] ) == 2;
	if ( bPlayer )
		SourceSharp_ViewerAbsPosition( &vecOrigin, &angles );
}

CON_COMMAND( spec_pos, "dump position and angles to the console" )
{
	Vector o;
	QAngle a;
	bool bPlayer;
	ViewPosition( args, o, a, bPlayer );
	Warning( "spec_goto %.1f %.1f %.1f %.1f %.1f\n", o.x, o.y, o.z, a.x, a.y );
}

CON_COMMAND( getpos, "dump position and angles to the console" )
{
	Vector o;
	QAngle a;
	bool bPlayer;
	ViewPosition( args, o, a, bPlayer );
	Warning( "%s %f %f %f;", bPlayer ? "setpos_exact" : "setpos", o.x, o.y, o.z );
	Warning( "%s %f %f %f\n", bPlayer ? "setang_exact" : "setang", a.x, a.y, a.z );
}

// centerview: pitch drift, which is not shipped (CShellViewRender::StartPitchDrift).
static void CenterView()
{
	view->StartPitchDrift();
}
static ConCommand centerview( "centerview", CenterView );

// r_cheapwaterstart / r_cheapwaterend: set one cheap-water distance, or print it.
static void CheapWaterCommand( const CCommand &args, bool bStart )
{
	if ( args.ArgC() == 2 )
	{
		const float flDistance = atof( args[ 1 ] );
		if ( bStart )
			view->SetCheapWaterStartDistance( flDistance );
		else
			view->SetCheapWaterEndDistance( flDistance );
		return;
	}
	float flStart, flEnd;
	view->GetWaterLODParams( flStart, flEnd );
	Warning( "%s: %f\n", bStart ? "r_cheapwaterstart" : "r_cheapwaterend", bStart ? flStart : flEnd );
}
CON_COMMAND( r_cheapwaterstart, "" ) { CheapWaterCommand( args, true ); }
CON_COMMAND( r_cheapwaterend, "" ) { CheapWaterCommand( args, false ); }

// r_screenoverlay: a material drawn over the frame; "off" or "0" (or a
// material that does not load) takes it away; no argument prints it.
CON_COMMAND_F( r_screenoverlay, "Draw specified material as an overlay", FCVAR_CHEAT | FCVAR_SERVER_CAN_EXECUTE )
{
	if ( args.ArgC() != 2 )
	{
		IMaterial *pShown = view->GetScreenOverlayMaterial();
		Warning( "r_screenoverlay: %s\n", pShown ? pShown->GetName() : "off" );
		return;
	}
	IMaterial *pMaterial = NULL;
	if ( Q_stricmp( "off", args[ 1 ] ) != 0 && Q_stricmp( "0", args[ 1 ] ) != 0 )
	{
		pMaterial = materials->FindMaterial( args[ 1 ], TEXTURE_GROUP_OTHER, false );
		if ( IsErrorMaterial( pMaterial ) )
			pMaterial = NULL;
	}
	view->SetScreenOverlayMaterial( pMaterial );
}

//-----------------------------------------------------------------------------
// The post-processing materials viewrender.cpp precached for DX9 and up
// (CLIENTEFFECT_REGISTER PrecachePostProcessingEffects): the client effect
// precache registry holds them, and ClientEffectPrecache.cs decides when.
//-----------------------------------------------------------------------------
namespace
{
const char *const kPostProcessingMaterials[] =
{
	"dev/blurfiltery_and_add_nohdr", "dev/blurfilterx", "dev/blurfilterx_nohdr", "dev/blurfiltery", "dev/blurfiltery_nohdr",
	"dev/bloomadd", "dev/downsample", "dev/downsample_non_hdr", "dev/no_pixel_write", "dev/lumcompare",
	"dev/floattoscreen_combine", "dev/copyfullframefb_vanilla", "dev/copyfullframefb", "dev/engine_post",
	"dev/motion_blur", "dev/upscale",
};

class CPostProcessingPrecache : public CClientEffect
{
public:
	void Cache( bool bPrecache ) override
	{
		if ( engine->GetDXSupportLevel() < 90 )
			return;
		for ( int i = 0; i < (int)ARRAYSIZE( kPostProcessingMaterials ); i++ )
			ReferenceMaterial( kPostProcessingMaterials[ i ], bPrecache );
	}
};
CPostProcessingPrecache g_PostProcessingPrecache;
} // namespace
