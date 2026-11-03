//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client mode: IClientMode implemented directly over the raw viewport, a member of the
// client DLL object handed the managed areas and the standalone clients it calls. Its
// decisions are managed's; native keeps the view setup, the sound DSP reset, the particle
// manager's post-render and the team-255 broadcast sound.

#pragma once

#include "inputsystem/ButtonCode.h"
#include "iclientmode.h"
#include "GameEventListener.h"

#include "shell_managed_areas_client.h"
#include "ishim_areas_client.h"

class CClientEngineInterfaces;
class CSourceSharpViewport;
class CStandaloneClients;
class CViewTargetsClient;
class CManagedParticlesClient;
class C_StandaloneEntity;

class CSourceSharpClientMode final : public IClientMode, public IShimUserMessagesClient, public CGameEventListener
{
public:
	// IShimUserMessagesClient: managed's VGUIMenu handler, over the viewport and this mode.
	int32_t ClientViewportQuery(const char* pszPanel) override;
	void ClientViewportShow(const char* pszPanel, int32_t bShow, const unsigned char* pPairs, int32_t nPairBytes,
							int32_t nFlags) override;

	CSourceSharpClientMode(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
						   CStandaloneClients& standalone, CManagedParticlesClient& particles,
						   CViewTargetsClient& viewTargets);

	virtual ~CSourceSharpClientMode();

	virtual void InitViewport() OVERRIDE;

	virtual void Init() OVERRIDE;

	virtual void VGui_Shutdown() OVERRIDE;

	virtual void Shutdown() OVERRIDE {}

	// Under the client root, shown, laid out.
	virtual void Enable() OVERRIDE;

	virtual void Disable() OVERRIDE;

	// The viewport fills the client root.
	virtual void Layout() OVERRIDE;

	// The HUD and menu roots ask for the viewport's VPANEL instead.
	virtual vgui::Panel* GetViewport() OVERRIDE;

	virtual vgui::AnimationController* GetViewportAnimationController() OVERRIDE;

	vgui::VPANEL GetViewportVPanel();

	virtual void ProcessInput(bool bActive) OVERRIDE;

	virtual bool ShouldDrawDetailObjects() OVERRIDE;

	virtual bool ShouldDrawEntity(C_BaseEntity* pEnt) OVERRIDE;

	virtual bool ShouldDrawParticles() OVERRIDE;

	virtual bool ShouldDrawFog() OVERRIDE;

	virtual bool ShouldDrawViewModel() OVERRIDE;

	virtual bool ShouldDrawCrosshair() OVERRIDE;

	// The body draws when the camera got far enough behind the eye to see it.
	virtual bool ShouldDrawLocalPlayer(C_BasePlayer* pPlayer) OVERRIDE;

	virtual void StartMessageMode(int iMessageModeType) OVERRIDE {}

	virtual vgui::Panel* GetMessagePanel() OVERRIDE;

	virtual void OverrideMouseInput(float* x, float* y) OVERRIDE {}

	virtual bool CreateMove(float flInputSampleTime, CUserCmd* cmd) OVERRIDE;

	// game_newmap, faked client side as stock did (clients connect after the server fired it),
	// and the player's sound DSP reset.
	virtual void LevelInit(const char* newmap) OVERRIDE;

	virtual void LevelShutdown() OVERRIDE;

	virtual void AdjustEngineViewport(int& x, int& y, int& width, int& height) OVERRIDE {}

	// Before the main view, where stock's DrawMonitors draws point_cameras.
	virtual void PreRender(CViewSetup* pSetup) OVERRIDE;

	virtual void PostRender() OVERRIDE;

	virtual void PostRenderVGui() OVERRIDE {}

	// Nothing activates an in-game vgui context in this build.
	virtual void ActivateInGameVGuiContext(vgui::Panel* pPanel) OVERRIDE {}

	virtual void DeactivateInGameVGuiContext() OVERRIDE {}

	virtual float GetViewModelFOV() OVERRIDE;

	virtual bool CanRecordDemo(char* errorMsg, int length) const OVERRIDE;

	virtual void ComputeVguiResConditions(KeyValues* pkvConditions) OVERRIDE {}

	virtual wchar_t* GetServerName() OVERRIDE;

	virtual void SetServerName(wchar_t* name) OVERRIDE {}

	virtual wchar_t* GetMapName() OVERRIDE;

	virtual void SetMapName(wchar_t* name) OVERRIDE {}

	virtual bool DoPostScreenSpaceEffects(const CViewSetup* pSetup) OVERRIDE;

	virtual void DisplayReplayMessage(const char* pLocalizeName, float flDuration, bool bUrgent, const char* pSound,
									  bool bDlg) OVERRIDE
	{
	}

	// Stock's particle-system count when asked; the managed client frame steps the rumble mixer.
	virtual void Update() OVERRIDE;

	virtual bool ShouldBlackoutAroundHUD() OVERRIDE;

	virtual HeadtrackMovementMode_t ShouldOverrideHeadtrackControl() OVERRIDE;

	virtual bool IsInfoPanelAllowed() OVERRIDE;

	virtual void InfoPanelDisplayed() OVERRIDE {}

	virtual bool IsHTMLInfoPanelAllowed() OVERRIDE;

	virtual void OnDemoRecordStart(char const* pDemoBaseName) OVERRIDE {}

	virtual void OnDemoRecordStop() OVERRIDE {}

	// A broadcast sound to every team (255) plays on the local player, as stock's.
	virtual void FireGameEvent(IGameEvent* event) OVERRIDE;

	// Managed decides: 1 the engine runs the binding, 0 the key was used, -1 no decision.
	// With no decision or no managed side, the engine runs it.
	virtual int KeyInput(int down, ButtonCode_t keynum, const char* pszCurrentBinding) OVERRIDE;

	// SetUpViews calls this after zeroing the origin and before deriving the camera, audio
	// state and g_vecRenderOrigin from it, so filling it here makes the view follow the player.
	virtual void OverrideView(CViewSetup* pSetup) OVERRIDE;

	// A second camera that shows the viewer: for its draw the flag answers as a third-person
	// camera's would, and the body moves into the leaf system and back out.
	bool ViewerSeenBegin();
	void ViewerSeenEnd(bool bWasSeen);

	// IShimViewClient's ViewOverrideSet: the view managed asks OverrideView to draw.
	void ViewOverrideSet(int32_t nActive, const float* pOriginXYZ, const float* pAngles, int32_t nSetViewAngles);

private:
	void ApplyThirdPersonCamera(C_StandaloneEntity* pPlayer, CViewSetup* pSetup);
	void ApplyManagedShake(Vector& origin, QAngle& angles);
	void ApplyManagedViewModelBob(Vector& origin, QAngle& angles);
	bool ApplyViewOverride(CViewSetup* pSetup);

	// The view managed asked OverrideView to draw this frame.
	struct ViewOverride_t
	{
		bool bActive = false;
		bool bSetViewAngles = false;
		Vector origin;
		QAngle angles;
	};

	const CManagedAreasClient& m_Areas;
	const CClientEngineInterfaces& m_Engines;
	CStandaloneClients& m_Standalone;
	CManagedParticlesClient& m_Particles;
	CViewTargetsClient& m_ViewTargets;
	CSourceSharpViewport* m_pViewport;
	ViewOverride_t m_ViewOverride;
};
