// The render plan's executor: View_Render's body, carried out as managed code decides each frame.
#pragma once

struct vrect_t;
class CViewSetup;

#include "materialsystem/imaterialvar.h"
#include "bsptreedata.h"
#include "shell_view_cameras.h"

// view.cpp's ComputeCameraVariables: the view's bases and its world-to-view matrix.
void ShellView_ComputeCamera(const Vector& vecOrigin, const QAngle& angles, ShellCamera_t& out);
class ITexture;
struct CManagedAreasClient;
class CClientSystems;
class CViewerFacts;
class CStandaloneClients;
class CSourceSharpClientMode;
class CShellViewRender;
class CEffectsListClient;
class CManagedParticlesClient;
class CShellDetailProps;
class CClientEngineInterfaces;
class CViewTargetsClient;
class CViewCameras;
class CHudUiClient;
class CShadowsClient;
class CPrecipitationClient;
class CPixelVisClient;
class CGlowOverlaysClient;

namespace ShellViewExecutor
{
class CViewExecutorImpl;
}

// The render plan's executor, a member of the client DLL object handed what the plan's
// ops call. The executor itself (its frame state and the ops) is made on the first frame.
class CViewExecutor final
{
public:
	CViewExecutor(const CManagedAreasClient& areas, CClientSystems& systems, CViewerFacts& viewerFacts,
				  CStandaloneClients& standalone, CSourceSharpClientMode& clientMode, CShellViewRender& viewRender,
				  CEffectsListClient& effects, CManagedParticlesClient& particles, CShellDetailProps& detailProps,
				  CGlowOverlaysClient& glow, CPixelVisClient& pixelVis, CPrecipitationClient& precipitation,
				  CShadowsClient& shadows, CHudUiClient& hudUi, CViewCameras& cameras, CViewTargetsClient& viewTargets,
				  const CClientEngineInterfaces& engines)
		: m_Areas(areas)
		, m_Systems(systems)
		, m_ViewerFacts(viewerFacts)
		, m_Standalone(standalone)
		, m_ClientMode(clientMode)
		, m_ViewRender(viewRender)
		, m_Effects(effects)
		, m_Particles(particles)
		, m_DetailProps(detailProps)
		, m_Engines(engines)
		, m_ViewTargets(viewTargets)
		, m_Cameras(cameras)
		, m_HudUi(hudUi)
		, m_Shadows(shadows)
		, m_Precipitation(precipitation)
		, m_Glow(glow)
		, m_PixelVis(pixelVis)
	{
	}

	~CViewExecutor();

	CViewExecutor(const CViewExecutor&) = delete;
	CViewExecutor& operator=(const CViewExecutor&) = delete;

	// CShellClientDLL::View_Render's body, after its empty-rect guard.
	void ViewRender(vrect_t* rect);

	// RenderView through the plan (false when there is no plan), a shadow-depth view
	// likewise, and the draw flags of the view being drawn.
	bool RenderView(const CViewSetup& setup, int nClearFlags, int whatToDraw);
	bool ShadowDepthView(ITexture* pRenderTarget, ITexture* pDepthTexture, const CViewSetup& shadowView);
	int ActiveViewDrawFlags();

	void FreezeFrame(float flFreezeTime);

	// The save-game screenshot's view of the player's eye; false with no managed client.
	bool ScreenshotView(const CViewSetup& eye, int nWidth, int nHeight, CViewSetup& out);

	// SetUpViews' planes and FOV as managed code decides them; false with no managed client.
	bool SetUpView(float& flZFar, float& flZFarViewmodel, float& flZNear, float& flZNearViewmodel, float& flFov,
				   float& flViewModelFovOffset);

	// GetZNear and GetZFar's planes as managed code decides them. False with no managed client.
	bool AskViewPlanes(float& flZNear, float& flZFar);

	// This frame's view for CShellViewRender::Render: the eye RenderView draws, its clear
	// and draw flags, gl_clear_randomcolor's colour (pRandomClear: on, r, g, b) and the
	// level overview's green clear. False when managed code made none.
	bool FrameViewDecision(CViewSetup& eye, int& nClearFlags, int& nDrawFlags, int* pRandomClear, bool& bOverview);

private:
	ShellViewExecutor::CViewExecutorImpl& Impl();

	const CManagedAreasClient& m_Areas;
	CClientSystems& m_Systems;
	CViewerFacts& m_ViewerFacts;
	CStandaloneClients& m_Standalone;
	CSourceSharpClientMode& m_ClientMode;
	CShellViewRender& m_ViewRender;
	CEffectsListClient& m_Effects;
	CManagedParticlesClient& m_Particles;
	CShellDetailProps& m_DetailProps;
	const CClientEngineInterfaces& m_Engines;
	CViewTargetsClient& m_ViewTargets;
	CViewCameras& m_Cameras;
	CHudUiClient& m_HudUi;
	CShadowsClient& m_Shadows;
	CPrecipitationClient& m_Precipitation;
	CGlowOverlaysClient& m_Glow;
	CPixelVisClient& m_PixelVis;
	ShellViewExecutor::CViewExecutorImpl* m_pImpl = nullptr;
};
