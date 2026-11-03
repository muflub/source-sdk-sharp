//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's IViewRender (`view`), a member of the client DLL object handed the managed
// areas, the viewer facts and the client mode it calls. The frame's view is managed's
// decision and every view is the render plan's; this carries both out: the client mode's
// override and hooks, the engine's main view and audio, the 2D panels and the save-game
// screenshot. It holds the main and current view state, which the free functions fork TUs
// still call (MainViewOrigin, CurrentViewForward, R_CullSphere, ...) answer from.

#pragma once

#include "viewrender.h"

#include "shell_managed_areas_client.h"
#include "shell_view_executor_client.h"

class CViewerFacts;
class CSourceSharpClientMode;

class CShellViewRender final : public IViewRender, public IReplayScreenshotSystem
{
public:
	CShellViewRender(const CManagedAreasClient& areas, CViewerFacts& viewerFacts, CSourceSharpClientMode& clientMode,
					 CViewExecutor& executor);

	void Init() override;

	// CViewRender::LevelInit: a level starts with no beams, temp entities,
	// list stamps, freeze frame or screen overlay, and its screen-space effects.
	void LevelInit() override;

	void LevelShutdown() override;

	void Shutdown() override;

	// CViewRender::OnRenderStart: the frame's main view, before the frame's
	// entities simulate (they read the viewmodel's attachments from it).
	void OnRenderStart() override;

	void SetUpViews();
	void Render(vrect_t* rect) override;

	// RenderView is the render plan's. A view the planner declines, or one with
	// no managed client to plan it, draws nothing and says so once.
	void RenderView(const CViewSetup& setup, int nClearFlags, int whatToDraw) override;

	// The draw flags of the view the executor is drawing.
	int GetDrawFlags() override;

	// Pitch drift is not shipped: the view angle is the engine's alone.
	void StartPitchDrift() override {}

	void StopPitchDrift() override {}

	VPlane* GetFrustum() override;

	// r_drawbrushmodels, read by name (the engine's convar); drawn when it is missing, as stock.
	bool ShouldDrawBrushModels() override;

	const CViewSetup* GetPlayerViewSetup() const override;

	const CViewSetup* GetViewSetup() const override;

	void SetCurrentView(const CViewSetup& setup);

	// No vis for the next frame's views; Render clears it.
	void DisableVis() override;

	// SetUpViews' end: this frame's main view, the last one kept as the previous.
	void SetMainView(const Vector& vecOrigin, const QAngle& angles);

	// viewrender.cpp's SetupCurrentView, FinishCurrentView and AllowCurrentViewAccess.
	// bEffects also hands the engine the screen fade range and the depth-to-alpha
	// parameter, as stock's SetupCurrentView did.
	void SetupCurrentView(const Vector& vecOrigin, const QAngle& angles, int nViewID, bool bEffects);

	void FinishCurrentView() { m_bCanAccessCurrentView = false; }

	void AllowCurrentViewAccess(bool bAllow) { m_bCanAccessCurrentView = bAllow; }

	bool ForceNoVis() const;

	ShellCamera_t m_Main;
	Vector m_vecPrevMainOrigin = vec3_origin;
	QAngle m_angPrevMainAngles = vec3_angle;
	ShellCamera_t m_Current;
	int m_nCurrentViewID = VIEW_NONE;
	bool m_bCanAccessCurrentView = false;

private:
	// The demo free camera: managed's DemoViewCamera, around the engine's view
	// angles and the input's extra mouse sample it needs between its two steps.
	void DemoCamera(Vector& origin, QAngle& angles);

	const CManagedAreasClient& m_Areas;
	CViewerFacts& m_ViewerFacts;
	CSourceSharpClientMode& m_ClientMode;
	CViewExecutor& m_Executor;
	bool m_bForceNoVis = false;
	bool m_bSaidNoDecision = false;
	bool m_bSaidUnplanned = false;
	CViewSetup m_View;
	float m_flCheapWaterStartDistance = 0.0f;
	float m_flCheapWaterEndDistance = 0.1f;
	IMaterial* m_pScreenOverlay = NULL;
	int m_nBuildWorldListsNumber = 0;
	int m_nBuildRenderablesListsNumber = 0;
	CViewSetup m_CurrentView;
	Frustum m_Frustum;

public:
	// The world and renderable list build numbers the executor's views count
	// up (the leaf system's and the detail objects' frame stamps).
	int BuildWorldListsNumber() const override;

	void IncWorldListsNumber();

	void IncRenderablesListsNumber();

	int RenderablesListsNumber() const;

	// The cheap-water distances the map's water LOD control sets
	// (shell_worldrender.cpp) and the plan and the water proxy read.
	void SetCheapWaterStartDistance(float flDistance) override;

	void SetCheapWaterEndDistance(float flDistance) override;

	void GetWaterLODParams(float& flStart, float& flEnd) override;

	void DriftPitch() override;

	// The full-screen overlay material (r_screenoverlay), held by reference as
	// stock's CMaterialReference held it.
	void SetScreenOverlayMaterial(IMaterial* pMaterial) override;

	IMaterial* GetScreenOverlayMaterial() override;

	void ReleaseScreenOverlay();

	void WriteSaveGameScreenshot(const char* pFilename) override;

	void WriteSaveGameScreenshotOfSize(const char* pFilename, int width, int height, bool bCreatePowerOf2Padded,
									   bool bWriteVTF) override;

	// Replay is not shipped. This object is also the IReplayScreenshotSystem.
	void WriteReplayScreenshot(WriteReplayScreenshotParams_t& params) override;

	void UpdateReplayScreenshotCache() override {}

	// Not shipped: the render plan has no overlay-view arm and nothing calls this.
	void QueueOverlayRenderView(const CViewSetup& setup, int nClearFlags, int whatToDraw) override;

	// SetUpViews' planes, as managed decides them (ViewFrame: VIEW_NEARZ, and
	// r_farz or the map's diagonal pulled in by fog). 0 with no managed client.
	float GetZNear() override;

	float GetZFar() override;

	void GetScreenFadeDistances(float* pMin, float* pMax) override;

	// Only the tool framework asked who is being drawn.
	IGameEntity* GetCurrentlyDrawingEntity() override;

	void SetCurrentlyDrawingEntity(IGameEntity* pEntity) override {}

	bool UpdateShadowDepthTexture(ITexture* pRenderTarget, ITexture* pDepthTexture,
								  const CViewSetup& shadowView) override;

	// The freeze frame's state is managed's.
	void FreezeFrame(float flFreezeTime) override;

	IReplayScreenshotSystem* GetReplayScreenshotSystem() override;
};

// The client DLL object's view render, for the free answers the code not yet handed it
// still calls. Defined in shell_clientdll.cpp.
CShellViewRender& ShellClientDll_ViewRender();
