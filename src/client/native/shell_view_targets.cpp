//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- a second camera, drawn into a render target ========//
// SS_REASON b  an interface resolved by name at load
//
// Lane camproto: the prototype of plan_client_novalve C7's monitor pass. Stock
// draws a point_camera into `_rt_Camera` from CViewRender::DrawMonitors, which
// runs inside RenderView just before the main scene; this file is the same
// thing with every decision moved to C#:
//
//   1. THE TARGETS. IClientRenderTargets (ClientRenderTargets001), which the
//      engine asks the client for by name when it builds its well-known render
//      targets, at startup and again whenever the material system rebuilds
//      them. A client that exports it takes over the three the engine would
//      otherwise make itself (`_rt_WaterReflection`, `_rt_WaterRefraction`,
//      `_rt_Camera`, as CBaseClientRenderTargets makes them), and adds its own:
//      `_rt_SSView<n>`, kViewTargets of them, kViewTargetSize square.
//
//   2. THE REQUEST. IShimViewClient::RenderViewToTarget, called by managed
//      code at FRAME_RENDER_START with a SourceSharpViewTargetSetup: one view,
//      queued for this frame. Nothing is drawn by the call itself.
//
//   3. THE DRAW. ShellViewTargets_RenderQueued, from the client mode's
//      PreRender (shell_client.cpp): inside View_Render, before the main view,
//      which is the point DrawMonitors draws at. The render target is pushed,
//      the client's own CViewRender::RenderView draws the queued view with no
//      HUD, no viewmodel and no monitors, and the target is popped -- the same
//      shape as stock's WriteSaveGameScreenshotOfSize, which draws a whole
//      RenderView into a pushed target. It cannot recurse: RenderView does not
//      call the client mode's PreRender, and a draw already under way refuses
//      a second.
//
//   4. THE PANEL'S TEXTURE. IShimViewClient::ViewTargetInfo answers a target's
//      size, the vgui texture id whose material samples it, and how many
//      views have been drawn into it. The UI draws that texture id with the
//      draw slots it already has.
//
// Native decides nothing here: where the camera is, what it looks at, whether
// it is orthographic, how big, how often and whether the viewer's body draws
// are all the setup's fields, and the setup is managed's.
//
//=============================================================================//
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/itexture.h"
#include "materialsystem/imaterial.h"
#include "iviewrender.h"
#include "view_shared.h"
#include "vgui/ISurface.h"
#include "vgui_controls/Controls.h"
#include "VGuiMatSurface/IMatSystemSurface.h"
#include "tier3/tier3.h"
#include "tier1/KeyValues.h"
#include "bitmap/imageformat.h"
#include "mathlib/vector.h"
#include "tier0/platform.h"
#include "tier1/convar.h"
#include "tier1/interface.h"
#include "tier1/refcount.h"
#include "tier1/strtools.h"
#include "tier2/tier2.h"
#include "vstdlib/IKeyValuesSystem.h"
#include "vtf/vtf.h"
#include "cdll_int.h"
#include "sourcesharp.h"
#include "sourcesharp_entrypoints.h"
#include "shell_view_targets.h"

// memdbgon must be the last include file in a .cpp file!!!

// The layout ViewTargetSetup.cs mirrors; ViewTargetSetupLayoutTests pins the
// same numbers from the managed side.
COMPILE_TIME_ASSERT( sizeof( SourceSharpViewTargetSetup ) == 64 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpViewTargetSetup, Fov ) == 24 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpViewTargetSetup, OrthoLeft ) == 36 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpViewTargetSetup, Width ) == 52 );
COMPILE_TIME_ASSERT( offsetof( SourceSharpViewTargetSetup, Flags ) == 60 );

namespace
{
	// The prototype's one target. A fixed size, square: the view is drawn into
	// its top left at whatever width and height managed asks for, up to this.
	const int kViewTargets = 1;
	const int kViewTargetSize = 512;

	// Stock's sizes for the engine's three (CBaseClientRenderTargets'
	// default arguments).
	const int kWaterTextureSize = 1024;
	const int kCameraTextureSize = 256;

	struct ViewTarget
	{
		ITexture *pTexture;
		IMaterial *pMaterial;   // UnlitGeneric over the texture, for vgui
		int nVguiTexture;       // 0 until the UI first asks
		bool bPending;
		SourceSharpViewTargetSetup Setup;
		int nRenders;
	};

	// Zero-initialised PODs: no static initialiser.
	ViewTarget g_ViewTargets[ kViewTargets ];
	ITexture *g_pWaterReflection;
	ITexture *g_pWaterRefraction;
	ITexture *g_pCamera;
	bool g_bRenderingViewTarget;

	void Release( ITexture *&pTexture )
	{
		if ( pTexture )
			pTexture->DecrementReferenceCount();
		pTexture = NULL;
	}

	ITexture *Hold( ITexture *pTexture )
	{
		if ( pTexture )
			pTexture->IncrementReferenceCount();
		return pTexture;
	}

	void TargetName( int nTarget, char *pOut, int nOut )
	{
		V_snprintf( pOut, nOut, "_rt_SSView%d", nTarget );
	}
}

//-----------------------------------------------------------------------------
// The engine's well-known render targets, from the client (stock:
// CBaseClientRenderTargets, which this build does not compile).
//
// THE INTERFACE IS RESTATED HERE, as the engine calls it: two slots, no base,
// no destructor (public/game/client/iclientrendertargets.h, which no client
// TU includes, so the farm does not hold it). The engine passes no sizes:
// stock's water and camera sizes were default arguments of
// CBaseClientRenderTargets' override, and are this file's constants.
//-----------------------------------------------------------------------------
abstract_class IClientRenderTargets
{
public:
	virtual void InitClientRenderTargets( IMaterialSystem *pMaterialSystem, IMaterialSystemHardwareConfig *pHardwareConfig ) = 0;
	virtual void ShutdownClientRenderTargets( void ) = 0;
};

#define CLIENTRENDERTARGETS_INTERFACE_VERSION "ClientRenderTargets001"

class CSourceSharpRenderTargets : public IClientRenderTargets
{
public:
	virtual void InitClientRenderTargets( IMaterialSystem *pMaterialSystem, IMaterialSystemHardwareConfig *pHardwareConfig ) OVERRIDE
	{
		const int iWaterTextureSize = kWaterTextureSize;
		const int iCameraTextureSize = kCameraTextureSize;

		// The three the engine makes itself when a client exports no
		// IClientRenderTargets, made as CBaseClientRenderTargets makes them,
		// because exporting this interface is what stops the engine making
		// them and the water and the point_camera monitors read them by name.
		g_pWaterReflection = Hold( pMaterialSystem->CreateNamedRenderTargetTextureEx2(
			"_rt_WaterReflection", iWaterTextureSize, iWaterTextureSize, RT_SIZE_PICMIP,
			pMaterialSystem->GetBackBufferFormat(), MATERIAL_RT_DEPTH_SHARED,
			TEXTUREFLAGS_CLAMPS | TEXTUREFLAGS_CLAMPT, CREATERENDERTARGETFLAGS_HDR ) );
		g_pWaterRefraction = Hold( pMaterialSystem->CreateNamedRenderTargetTextureEx2(
			"_rt_WaterRefraction", iWaterTextureSize, iWaterTextureSize, RT_SIZE_PICMIP,
			IMAGE_FORMAT_RGBA8888, MATERIAL_RT_DEPTH_SHARED,
			TEXTUREFLAGS_CLAMPS | TEXTUREFLAGS_CLAMPT, CREATERENDERTARGETFLAGS_HDR ) );
		g_pCamera = Hold( pMaterialSystem->CreateNamedRenderTargetTextureEx2(
			"_rt_Camera", iCameraTextureSize, iCameraTextureSize, RT_SIZE_DEFAULT,
			pMaterialSystem->GetBackBufferFormat(), MATERIAL_RT_DEPTH_SHARED,
			0, CREATERENDERTARGETFLAGS_HDR ) );

		// Ours. A depth buffer of its own: a shared one is the back buffer's,
		// and a window smaller than the target would leave it none.
		for ( int i = 0; i < kViewTargets; i++ )
		{
			char szName[ 32 ];
			TargetName( i, szName, sizeof( szName ) );
			g_ViewTargets[ i ].pTexture = Hold( pMaterialSystem->CreateNamedRenderTargetTextureEx2(
				szName, kViewTargetSize, kViewTargetSize, RT_SIZE_LITERAL,
				pMaterialSystem->GetBackBufferFormat(), MATERIAL_RT_DEPTH_SEPARATE,
				TEXTUREFLAGS_CLAMPS | TEXTUREFLAGS_CLAMPT, CREATERENDERTARGETFLAGS_HDR ) );
		}
	}

	virtual void ShutdownClientRenderTargets() OVERRIDE
	{
		Release( g_pWaterReflection );
		Release( g_pWaterRefraction );
		Release( g_pCamera );
		for ( int i = 0; i < kViewTargets; i++ )
		{
			Release( g_ViewTargets[ i ].pTexture );
			g_ViewTargets[ i ].bPending = false;
		}
	}
};

static CSourceSharpRenderTargets s_RenderTargets;
EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CSourceSharpRenderTargets, IClientRenderTargets,
                                   CLIENTRENDERTARGETS_INTERFACE_VERSION, s_RenderTargets );

//-----------------------------------------------------------------------------
// IShimViewClient::RenderViewToTarget: queue one view for this frame's render.
// 1 queued, 0 refused (no such target, the target was never made, or a size
// outside it). A second request for the same target in a frame replaces the
// first: the target holds one picture.
//-----------------------------------------------------------------------------
int32_t SourceSharp_RenderViewToTarget( int32_t nTarget, const SourceSharpViewTargetSetup *pSetup )
{
	if ( !pSetup || nTarget < 0 || nTarget >= kViewTargets )
		return 0;

	ViewTarget &t = g_ViewTargets[ nTarget ];
	if ( !t.pTexture || t.pTexture->IsError() )
		return 0;

	if ( pSetup->Width <= 0 || pSetup->Height <= 0
	     || pSetup->Width > t.pTexture->GetActualWidth() || pSetup->Height > t.pTexture->GetActualHeight() )
		return 0;

	t.Setup = *pSetup;
	t.bPending = true;
	return 1;
}

//-----------------------------------------------------------------------------
// IShimViewClient::ViewTargetInfo: width, height, the vgui texture id over the
// target, and the views drawn into it since load. 0 for no such target. The
// texture id is made on the first ask, which the UI makes from its paint.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ViewTargetInfo( int32_t nTarget, int32_t *pOut4 )
{
	if ( !pOut4 || nTarget < 0 || nTarget >= kViewTargets )
		return 0;

	pOut4[ 0 ] = pOut4[ 1 ] = pOut4[ 2 ] = pOut4[ 3 ] = 0;

	ViewTarget &t = g_ViewTargets[ nTarget ];
	if ( !t.pTexture || t.pTexture->IsError() )
		return 0;

	if ( t.nVguiTexture == 0 && g_pMatSystemSurface && materials )
	{
		char szTarget[ 32 ], szMaterial[ 48 ];
		TargetName( nTarget, szTarget, sizeof( szTarget ) );
		V_snprintf( szMaterial, sizeof( szMaterial ), "__ss_view_target%d", nTarget );

		// vgui's own texture materials' keys (matsystemsurface), over the
		// target, OPAQUE: no $vertexalpha. The scene shaders write depth into
		// the target's alpha (fog and depth-to-dest-alpha), so a material that
		// blended by texture alpha drew an orthographic view almost wholly
		// transparent and a perspective one fading with distance.
		KeyValues *pKeys = new KeyValues( "UnlitGeneric" );
		pKeys->SetString( "$basetexture", szTarget );
		pKeys->SetInt( "$vertexcolor", 1 );
		pKeys->SetInt( "$ignorez", 1 );
		pKeys->SetInt( "$no_fullbright", 1 );
		pKeys->SetInt( "$nolod", 1 );
		t.pMaterial = materials->CreateMaterial( szMaterial, pKeys );
		if ( t.pMaterial )
		{
			t.pMaterial->IncrementReferenceCount();
			t.nVguiTexture = vgui::surface()->CreateNewTextureID( true );
			g_pMatSystemSurface->DrawSetTextureMaterial( t.nVguiTexture, t.pMaterial );
		}
	}

	pOut4[ 0 ] = t.pTexture->GetActualWidth();
	pOut4[ 1 ] = t.pTexture->GetActualHeight();
	pOut4[ 2 ] = t.nVguiTexture;
	pOut4[ 3 ] = t.nRenders;
	return 1;
}

ITexture *ShellViewTargets_Texture( int nTarget )
{
	if ( nTarget < 0 || nTarget >= kViewTargets )
		return NULL;
	ITexture *pTexture = g_ViewTargets[ nTarget ].pTexture;
	return ( pTexture && !pTexture->IsError() ) ? pTexture : NULL;
}

void ShellViewTargets_NoteDrawn( int nTarget )
{
	if ( nTarget >= 0 && nTarget < kViewTargets )
		g_ViewTargets[ nTarget ].nRenders++;
}

//-----------------------------------------------------------------------------
// The draw, from the client mode's PreRender: every queued view, into its
// target, through the client's own RenderView.
//-----------------------------------------------------------------------------
void ShellViewTargets_RenderQueued()
{
	if ( g_bRenderingViewTarget || !view || !materials )
		return;

	g_bRenderingViewTarget = true;

	// NO PIXEL-VISIBILITY QUERIES FROM A SECOND VIEW. RenderView draws every
	// view as VIEW_MAIN, and the pixel-visibility system keeps one query per
	// view id per frame: a second main view in a frame re-issues each glow's
	// query (c_pixel_visibility.cpp's "issued twice" assert, every frame) and
	// answers the main view's glows with this camera's occlusion. Stock's
	// monitors draw as VIEW_MONITOR and issue none; switched off here, the
	// system answers by its trace fallback for this view, as stock's monitors
	// are answered, and the main view's queries are untouched.
	static ConVarRef s_PixelVisibility( "r_dopixelvisibility" );
	const bool bPixelVisibility = s_PixelVisibility.IsValid() && s_PixelVisibility.GetBool();
	if ( bPixelVisibility )
		s_PixelVisibility.SetValue( 0 );

	for ( int i = 0; i < kViewTargets; i++ )
	{
		ViewTarget &t = g_ViewTargets[ i ];
		if ( !t.bPending )
			continue;
		t.bPending = false;

		if ( !t.pTexture || t.pTexture->IsError() )
			continue;

		const SourceSharpViewTargetSetup &s = t.Setup;

		CViewSetup v;
		v.x = v.m_nUnscaledX = 0;
		v.y = v.m_nUnscaledY = 0;
		v.width = v.m_nUnscaledWidth = s.Width;
		v.height = v.m_nUnscaledHeight = s.Height;
		v.origin.Init( s.OriginXYZ[ 0 ], s.OriginXYZ[ 1 ], s.OriginXYZ[ 2 ] );
		v.angles.Init( s.Angles[ 0 ], s.Angles[ 1 ], s.Angles[ 2 ] );
		v.fov = v.fovViewmodel = s.Fov;
		v.zNear = v.zNearViewmodel = s.ZNear;
		v.zFar = v.zFarViewmodel = s.ZFar;
		v.m_bOrtho = ( s.Flags & SOURCESHARP_VIEWTARGET_ORTHO ) != 0;
		v.m_OrthoLeft = s.OrthoLeft;
		v.m_OrthoTop = s.OrthoTop;
		v.m_OrthoRight = s.OrthoRight;
		v.m_OrthoBottom = s.OrthoBottom;
		v.m_flAspectRatio = (float)s.Width / (float)s.Height;
		v.m_bRenderToSubrectOfLargerScreen = true;
		v.m_bDoBloomAndToneMapping = false;
		v.m_bCacheFullSceneState = false;

		const bool bViewer = ( s.Flags & SOURCESHARP_VIEWTARGET_DRAW_VIEWER ) != 0;
		const bool bWasSeen = bViewer ? ShellClient_ViewerSeenBegin() : false;

		CMatRenderContextPtr pRenderContext( materials );
		pRenderContext->PushRenderTargetAndViewport( t.pTexture, 0, 0, s.Width, s.Height );
		pRenderContext.SafeRelease();

		view->RenderView( v, VIEW_CLEAR_COLOR | VIEW_CLEAR_DEPTH, RENDERVIEW_SUPPRESSMONITORRENDERING );

		pRenderContext.GetFrom( materials );
		pRenderContext->PopRenderTargetAndViewport();
		pRenderContext.SafeRelease();

		if ( bViewer )
			ShellClient_ViewerSeenEnd( bWasSeen );

		t.nRenders++;
	}

	if ( bPixelVisibility )
		s_PixelVisibility.SetValue( 1 );

	g_bRenderingViewTarget = false;
}
