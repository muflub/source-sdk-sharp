//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "shareddefs.h"
#include "tier1/utlstack.h"
#include "iviewrender.h"
#include "view_shared.h"
#include "replay/ireplayscreenshotsystem.h"

// Forward declarations
class ConVar;
class CClientRenderablesList;
class CViewRender;
struct ClientWorldListInfo_t;
class C_BaseEntity;
struct WriteReplayScreenshotParams_t;

// This identifies the view for certain systems that are unique per view (e.g. pixel visibility)
// NOTE: This is identifying which logical part of the scene an entity is being redered in
// This is not identifying a particular render target necessarily.  This is mostly needed for entities that
// can be rendered more than once per frame (pixel vis queries need to be identified per-render call)
enum view_id_t
{
	VIEW_ILLEGAL = -2,
	VIEW_NONE = -1,
	VIEW_MAIN = 0,
	VIEW_3DSKY = 1,
	VIEW_MONITOR = 2,
	VIEW_REFLECTION = 3,
	VIEW_REFRACTION = 4,
	VIEW_INTRO_PLAYER = 5,
	VIEW_INTRO_CAMERA = 6,
	VIEW_SHADOW_DEPTH_TEXTURE = 7,
	VIEW_SSAO = 8,
	VIEW_ID_COUNT
};

view_id_t CurrentViewID();

// Purpose: Stored pitch drifting variables
class CPitchDrift
{
public:
	float pitchvel;
	bool nodrift;
	float driftmove;
	double laststop;
};

struct ViewCustomVisibility_t
{
	ViewCustomVisibility_t()
	{
		m_nNumVisOrigins = 0;
		m_VisData.m_fDistToAreaPortalTolerance = FLT_MAX;
		m_iForceViewLeaf = -1;
	}

	// Set to true if you want to use multiple origins for doing client side map vis culling
	// NOTE:  In generaly, you won't want to do this, and by default the 3d origin of the camera, as above,
	//  will be used as the origin for vis, too.
	int m_nNumVisOrigins;
	// Array of origins
	Vector m_rgVisOrigins[MAX_VIS_LEAVES];

	// The view data overrides for visibility calculations with area portals
	VisOverrideData_t m_VisData;

	// The starting leaf to determing which area to start in when performing area portal culling on the engine
	// Default behavior is to use the leaf the camera position is in.
	int m_iForceViewLeaf;
};

class CBase3dView : public CRefCounted<>, protected CViewSetup
{
	DECLARE_CLASS_NOBASE(CBase3dView);

public:
	CBase3dView(CViewRender* pMainView);

	VPlane* GetFrustum();

	virtual int GetDrawFlags() { return 0; }

protected:
	// @MULTICORE (toml 8/11/2006): need to have per-view frustum. Change when move view stack to client
	VPlane* m_Frustum;
	CViewRender* m_pMainView;
};

// Base class for 3d views
class CRendering3dView : public CBase3dView
{
	DECLARE_CLASS(CRendering3dView, CBase3dView);

public:
	CRendering3dView(CViewRender* pMainView);

	virtual ~CRendering3dView() { ReleaseLists(); }

	void Setup(const CViewSetup& setup);

	// What are we currently rendering? Returns a combination of DF_ flags.
	virtual int GetDrawFlags();

	virtual void Draw() {};

protected:
	void ReleaseLists();

	// Combination of DF_ flags.
	int m_DrawFlags;
	int m_ClearFlags;

	IWorldRenderList* m_pWorldRenderList;
	CClientRenderablesList* m_pRenderablesList;
	ClientWorldListInfo_t* m_pWorldListInfo;
	ViewCustomVisibility_t* m_pCustomVisibility;
};

class CRenderExecutor
{
	DECLARE_CLASS_NOBASE(CRenderExecutor);

public:
	virtual void AddView(CRendering3dView* pView) = 0;
	virtual void Execute() = 0;

protected:
	CRenderExecutor(CViewRender* pMainView)
		: m_pMainView(pMainView)
	{
	}

	CViewRender* m_pMainView;
};

class CSimpleRenderExecutor : public CRenderExecutor
{
	DECLARE_CLASS(CSimpleRenderExecutor, CRenderExecutor);

public:
	CSimpleRenderExecutor(CViewRender* pMainView)
		: CRenderExecutor(pMainView)
	{
	}

	void AddView(CRendering3dView* pView);

	void Execute() {}
};

// Purpose: Implements the interface to view rendering for the client .dll

class CViewRender : public IViewRender, public IReplayScreenshotSystem
{
	DECLARE_CLASS_NOBASE(CViewRender);

public:
	virtual void Init();
	virtual void Shutdown();

	const CViewSetup* GetPlayerViewSetup() const;

	virtual void StartPitchDrift();
	virtual void StopPitchDrift();

	virtual float GetZNear();
	virtual float GetZFar();

	virtual void OnRenderStart();
	void DriftPitch();

protected:
	// generates a low-res screenshot for save games
	virtual void WriteSaveGameScreenshotOfSize(const char* pFilename, int width, int height,
											   bool bCreatePowerOf2Padded = false, bool bWriteVTF = false);
	void WriteSaveGameScreenshot(const char* filename);

	virtual IReplayScreenshotSystem* GetReplayScreenshotSystem() { return this; }

	// IReplayScreenshot implementation
	virtual void WriteReplayScreenshot(WriteReplayScreenshotParams_t& params);
	virtual void UpdateReplayScreenshotCache();

	// This stores all of the view setup parameters that the engine needs to know about.
	// Best way to pick the right one is with ::GetView(), rather than directly.
	CViewSetup m_View; // mono <- in stereo mode, this will be between the two eyes and is the "main" view.
	CViewSetup m_ViewLeft; // left (unused for mono)
	CViewSetup m_ViewRight; // right (unused for mono)

	// Pitch drifting data
	CPitchDrift m_PitchDrift;

public:
	CViewRender();

	virtual ~CViewRender() {}

	// Implementation of IViewRender interface
public:
	// Render functions
	virtual void Render(vrect_t* rect);
	virtual void RenderView(const CViewSetup& view, int nClearFlags, int whatToDraw);
	virtual void unused_RenderPlayerSprites() = 0;
	virtual void unused_Render2DEffectsPreHUD() = 0;
	virtual void unused_Render2DEffectsPostHUD() = 0;

	// Called once per level change
	void LevelInit();
	void LevelShutdown();

	// Add entity to transparent entity queue

	bool ShouldDrawBrushModels();

	const CViewSetup* GetViewSetup() const;

	void DisableVis();

	void SetCheapWaterStartDistance(float flCheapWaterStartDistance);
	void SetCheapWaterEndDistance(float flCheapWaterEndDistance);

	void GetWaterLODParams(float& flCheapWaterStartDistance, float& flCheapWaterEndDistance);

	virtual void QueueOverlayRenderView(const CViewSetup& view, int nClearFlags, int whatToDraw);

	virtual void GetScreenFadeDistances(float* min, float* max);

	virtual C_BaseEntity* GetCurrentlyDrawingEntity();
	virtual void SetCurrentlyDrawingEntity(C_BaseEntity* pEnt);

	virtual bool UpdateShadowDepthTexture(ITexture* pRenderTarget, ITexture* pDepthTexture,
										  const CViewSetup& shadowView);

	virtual void unused_ShouldForceNoVis() = 0;

	int BuildWorldListsNumber() const;

	virtual VPlane* GetFrustum() { return (m_pActiveRenderer) ? m_pActiveRenderer->GetFrustum() : m_Frustum; }

	// What are we currently rendering? Returns a combination of DF_ flags.
	virtual int GetDrawFlags() { return (m_pActiveRenderer) ? m_pActiveRenderer->GetDrawFlags() : 0; }

	void FreezeFrame(float flFreezeTime);

private:
	int m_BuildWorldListsNumber;

	// Overlays
	void SetScreenOverlayMaterial(IMaterial* pMaterial);
	IMaterial* GetScreenOverlayMaterial();

	virtual void unused_ViewDrawScene_Intro() = 0;

	// This stores the current view
	alignas(4) unsigned char unused_m_CurrentView[768];

	friend class CBase3dView;

	Frustum m_Frustum;

	CBase3dView* m_pActiveRenderer;
	CSimpleRenderExecutor m_SimpleExecutor;

	bool m_rbTakeFreezeFrame[STEREO_EYE_MAX];
	float m_flFreezeFrameUntil;
};
