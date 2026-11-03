//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// A second camera drawn into a render target, as stock's monitors draw a
// point_camera. The client exports IClientRenderTargets, so it makes the
// engine's three well-known targets and adds its own _rt_SSView<n>. Managed
// code queues a view with RenderViewToTarget at FRAME_RENDER_START; the client
// mode's PreRender draws it inside View_Render before the main view, into the
// pushed target, with no HUD, viewmodel or monitors. It cannot recurse:
// RenderView does not call PreRender, and a draw under way refuses a second.

#include "materialsystem/itexture.h"
#include "materialsystem/imaterial.h"
#include "iviewrender.h"
#include "view_shared.h"

#include "vgui_controls/Controls.h"
#include "VGuiMatSurface/IMatSystemSurface.h"

#include "tier1/KeyValues.h"

#include "cdll_int.h"

#include "sourcesharp_entrypoints.h"
#include "shell_view_targets.h"
#include "shell_client_mode.h"
#include "shell_client_engine_interfaces.h"

// The layout ViewTargetSetup.cs mirrors.
COMPILE_TIME_ASSERT(sizeof(SourceSharpViewTargetSetup) == 64);
COMPILE_TIME_ASSERT(offsetof(SourceSharpViewTargetSetup, Fov) == 24);
COMPILE_TIME_ASSERT(offsetof(SourceSharpViewTargetSetup, OrthoLeft) == 36);
COMPILE_TIME_ASSERT(offsetof(SourceSharpViewTargetSetup, Width) == 52);
COMPILE_TIME_ASSERT(offsetof(SourceSharpViewTargetSetup, Flags) == 60);

namespace
{
// Square; the view is drawn into its top left at the width and height asked
// for, up to this.
const int kViewTargetSize = 512;

// Stock's sizes for the engine's three (CBaseClientRenderTargets' defaults).
const int kWaterTextureSize = 1024;
const int kCameraTextureSize = 256;

void Release(ITexture*& pTexture)
{
	if (pTexture)
		pTexture->DecrementReferenceCount();
	pTexture = NULL;
}

ITexture* Hold(ITexture* pTexture)
{
	if (pTexture)
		pTexture->IncrementReferenceCount();
	return pTexture;
}

void TargetName(int nTarget, char* pOut, int nOut)
{
	V_snprintf(pOut, nOut, "_rt_SSView%d", nTarget);
}
}

void CViewTargetsClient::InitClientRenderTargets(IMaterialSystem* pMaterialSystem,
												 IMaterialSystemHardwareConfig* pHardwareConfig)
{
	const int iWaterTextureSize = kWaterTextureSize;
	const int iCameraTextureSize = kCameraTextureSize;

	// Exporting this interface stops the engine making these three, and the water
	// and point_camera monitors read them by name.
	m_pWaterReflection = Hold(pMaterialSystem->CreateNamedRenderTargetTextureEx2(
		"_rt_WaterReflection", iWaterTextureSize, iWaterTextureSize, RT_SIZE_PICMIP,
		pMaterialSystem->GetBackBufferFormat(), MATERIAL_RT_DEPTH_SHARED, TEXTUREFLAGS_CLAMPS | TEXTUREFLAGS_CLAMPT,
		CREATERENDERTARGETFLAGS_HDR));
	m_pWaterRefraction = Hold(pMaterialSystem->CreateNamedRenderTargetTextureEx2(
		"_rt_WaterRefraction", iWaterTextureSize, iWaterTextureSize, RT_SIZE_PICMIP, IMAGE_FORMAT_RGBA8888,
		MATERIAL_RT_DEPTH_SHARED, TEXTUREFLAGS_CLAMPS | TEXTUREFLAGS_CLAMPT, CREATERENDERTARGETFLAGS_HDR));
	m_pCamera = Hold(pMaterialSystem->CreateNamedRenderTargetTextureEx2(
		"_rt_Camera", iCameraTextureSize, iCameraTextureSize, RT_SIZE_DEFAULT, pMaterialSystem->GetBackBufferFormat(),
		MATERIAL_RT_DEPTH_SHARED, 0, CREATERENDERTARGETFLAGS_HDR));

	// A depth buffer of its own: a shared one is the back buffer's, and a window
	// smaller than the target would leave it none.
	for (int i = 0; i < kViewTargets; i++)
	{
		char szName[32];
		TargetName(i, szName, sizeof(szName));
		m_Targets[i].pTexture = Hold(pMaterialSystem->CreateNamedRenderTargetTextureEx2(
			szName, kViewTargetSize, kViewTargetSize, RT_SIZE_LITERAL, pMaterialSystem->GetBackBufferFormat(),
			MATERIAL_RT_DEPTH_SEPARATE, TEXTUREFLAGS_CLAMPS | TEXTUREFLAGS_CLAMPT, CREATERENDERTARGETFLAGS_HDR));
	}
}

void CViewTargetsClient::ShutdownClientRenderTargets()
{
	Release(m_pWaterReflection);
	Release(m_pWaterRefraction);
	Release(m_pCamera);
	for (int i = 0; i < kViewTargets; i++)
	{
		Release(m_Targets[i].pTexture);
		m_Targets[i].bPending = false;
	}
}

// Queues one view for this frame: 1 queued, 0 refused (no such target, never
// made, or a size outside it). A second request in a frame replaces the first.
int32_t CViewTargetsClient::RenderViewToTarget(int32_t nTarget, const SourceSharpViewTargetSetup* pSetup)
{
	if (!pSetup || nTarget < 0 || nTarget >= kViewTargets)
		return 0;

	ViewTarget& t = m_Targets[nTarget];
	if (!t.pTexture || t.pTexture->IsError())
		return 0;

	if (pSetup->Width <= 0 || pSetup->Height <= 0 || pSetup->Width > t.pTexture->GetActualWidth() ||
		pSetup->Height > t.pTexture->GetActualHeight())
		return 0;

	t.Setup = *pSetup;
	t.bPending = true;
	return 1;
}

// Width, height, the vgui texture id over the target (made on first ask), and
// the views drawn into it since load. 0 for no such target.
int32_t CViewTargetsClient::ViewTargetInfo(int32_t nTarget, int32_t* pOut4)
{
	if (!pOut4 || nTarget < 0 || nTarget >= kViewTargets)
		return 0;

	pOut4[0] = pOut4[1] = pOut4[2] = pOut4[3] = 0;

	ViewTarget& t = m_Targets[nTarget];
	if (!t.pTexture || t.pTexture->IsError())
		return 0;

	if (t.nVguiTexture == 0 && g_pMatSystemSurface && m_Engines.m_pMaterials)
	{
		char szTarget[32], szMaterial[48];
		TargetName(nTarget, szTarget, sizeof(szTarget));
		V_snprintf(szMaterial, sizeof(szMaterial), "__ss_view_target%d", nTarget);

		// Opaque, with no $vertexalpha: the scene shaders write depth into the target's
		// alpha, so blending by it would make the view mostly transparent.
		KeyValues* pKeys = new KeyValues("UnlitGeneric");
		pKeys->SetString("$basetexture", szTarget);
		pKeys->SetInt("$vertexcolor", 1);
		pKeys->SetInt("$ignorez", 1);
		pKeys->SetInt("$no_fullbright", 1);
		pKeys->SetInt("$nolod", 1);
		t.pMaterial = m_Engines.m_pMaterials->CreateMaterial(szMaterial, pKeys);
		if (t.pMaterial)
		{
			t.pMaterial->IncrementReferenceCount();
			t.nVguiTexture = vgui::surface()->CreateNewTextureID(true);
			g_pMatSystemSurface->DrawSetTextureMaterial(t.nVguiTexture, t.pMaterial);
		}
	}

	pOut4[0] = t.pTexture->GetActualWidth();
	pOut4[1] = t.pTexture->GetActualHeight();
	pOut4[2] = t.nVguiTexture;
	pOut4[3] = t.nRenders;
	return 1;
}

ITexture* CViewTargetsClient::Texture(int nTarget)
{
	if (nTarget < 0 || nTarget >= kViewTargets)
		return NULL;
	ITexture* pTexture = m_Targets[nTarget].pTexture;
	return (pTexture && !pTexture->IsError()) ? pTexture : NULL;
}

void CViewTargetsClient::NoteDrawn(int nTarget)
{
	if (nTarget >= 0 && nTarget < kViewTargets)
		m_Targets[nTarget].nRenders++;
}

// Draws every queued view into its target through the client's own RenderView.
void CViewTargetsClient::RenderQueued(CSourceSharpClientMode& clientMode)
{
	if (m_bRendering || !view || !m_Engines.m_pMaterials)
		return;

	m_bRendering = true;

	// Pixel visibility is off for a second view: RenderView draws as VIEW_MAIN, and
	// a second main view would re-issue each glow's query and answer the main view's
	// glows with this camera's occlusion. Stock's monitors issue none either.
	static ConVarRef s_PixelVisibility("r_dopixelvisibility");
	const bool bPixelVisibility = s_PixelVisibility.IsValid() && s_PixelVisibility.GetBool();
	if (bPixelVisibility)
		s_PixelVisibility.SetValue(0);

	for (int i = 0; i < kViewTargets; i++)
	{
		ViewTarget& t = m_Targets[i];
		if (!t.bPending)
			continue;
		t.bPending = false;

		if (!t.pTexture || t.pTexture->IsError())
			continue;

		const SourceSharpViewTargetSetup& s = t.Setup;

		CViewSetup v;
		v.x = v.m_nUnscaledX = 0;
		v.y = v.m_nUnscaledY = 0;
		v.width = v.m_nUnscaledWidth = s.Width;
		v.height = v.m_nUnscaledHeight = s.Height;
		v.origin.Init(s.OriginXYZ[0], s.OriginXYZ[1], s.OriginXYZ[2]);
		v.angles.Init(s.Angles[0], s.Angles[1], s.Angles[2]);
		v.fov = v.fovViewmodel = s.Fov;
		v.zNear = v.zNearViewmodel = s.ZNear;
		v.zFar = v.zFarViewmodel = s.ZFar;
		v.m_bOrtho = (s.Flags & SOURCESHARP_VIEWTARGET_ORTHO) != 0;
		v.m_OrthoLeft = s.OrthoLeft;
		v.m_OrthoTop = s.OrthoTop;
		v.m_OrthoRight = s.OrthoRight;
		v.m_OrthoBottom = s.OrthoBottom;
		v.m_flAspectRatio = (float)s.Width / (float)s.Height;
		v.m_bRenderToSubrectOfLargerScreen = true;
		v.m_bDoBloomAndToneMapping = false;
		v.m_bCacheFullSceneState = false;

		const bool bViewer = (s.Flags & SOURCESHARP_VIEWTARGET_DRAW_VIEWER) != 0;
		const bool bWasSeen = bViewer ? clientMode.ViewerSeenBegin() : false;

		CMatRenderContextPtr pRenderContext(m_Engines.m_pMaterials);
		pRenderContext->PushRenderTargetAndViewport(t.pTexture, 0, 0, s.Width, s.Height);
		pRenderContext.SafeRelease();

		view->RenderView(v, VIEW_CLEAR_COLOR | VIEW_CLEAR_DEPTH, RENDERVIEW_SUPPRESSMONITORRENDERING);

		pRenderContext.GetFrom(m_Engines.m_pMaterials);
		pRenderContext->PopRenderTargetAndViewport();
		pRenderContext.SafeRelease();

		if (bViewer)
			clientMode.ViewerSeenEnd(bWasSeen);

		t.nRenders++;
	}

	if (bPixelVisibility)
		s_PixelVisibility.SetValue(1);

	m_bRendering = false;
}
