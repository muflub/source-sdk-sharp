//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The client shell: the client mode, the third-person camera, the mode manager and the
// VGUI root panel.

#include "predictable_entity.h"
#include "cdll_util.h"

#include "vgui/ILocalize.h"
#include "hud.h"
#include "vgui/ISurface.h"
#include "iclientmode.h"
#include "GameEventListener.h"
#include "cam_thirdperson.h"
#include "particles/particles.h"
#include "c_rumble.h"

#include "c_recipientfilter.h"

#include "ivmodemanager.h"
#include "vgui_int.h"
#include "ienginevgui.h"

#include "vgui_controls/Controls.h"
#include "ifpspanel.h"
#include "inetgraphpanel.h"
#include "filesystem.h"

#include "cdll_client_int.h"

#include "client_entity.h"

#include "util_shared.h"

#include "client_sound.h"
#include "shell_viewport_client.h"
#include "view_facts.h"
#include "shell_diagnostics.h"
#include "shell_standalone_client.h"

#include "shell_view_targets.h"
#include "shell_particle_systems.h"

// The third-person camera. Its last fraction is published because whether the player's
// model and viewmodel draw depend on it: a camera pulled onto the eye by a wall is a
// first-person camera whatever mode was typed.
static bool s_bThirdPersonSeesOwner = false;

bool SourceSharp_ThirdPersonSeesOwner()
{
	return s_bThirdPersonSeesOwner;
}

// A second camera that shows the viewer: for its draw the flag answers as a third-person
// camera's would, and the body moves into the leaf system and back out.
bool ShellClient_ViewerSeenBegin()
{
	const bool bWas = s_bThirdPersonSeesOwner;
	s_bThirdPersonSeesOwner = true;
	if (C_StandaloneEntity* pPlayer = SourceSharp_LocalPlayerShell())
		pPlayer->UpdateVisibility();
	return bWas;
}

void ShellClient_ViewerSeenEnd(bool bWasSeen)
{
	s_bThirdPersonSeesOwner = bWasSeen;
	if (C_StandaloneEntity* pPlayer = SourceSharp_LocalPlayerShell())
		pPlayer->UpdateVisibility();
}

static void SourceSharp_ApplyThirdPersonCamera(C_StandaloneEntity* pPlayer, CViewSetup* pSetup)
{
	s_bThirdPersonSeesOwner = false;

	if (!::input || !::input->CAM_IsThirdPerson())
	{
		// Still tell the player: leaving third person is when its model must leave the leaf system.
		if (pPlayer)
			pPlayer->UpdateVisibility();

		return;
	}

	// Stock's convar names, through ConVarRef: in_camera.cpp declares them static.
	static ConVarRef s_IdealDist("cam_idealdist");
	static ConVarRef s_IdealDistRight("cam_idealdistright");
	static ConVarRef s_IdealDistUp("cam_idealdistup");
	static ConVarRef s_IdealPitch("cam_idealpitch");
	static ConVarRef s_IdealYaw("cam_idealyaw");
	static ConVarRef s_Collision("cam_collision");

	if (!s_IdealDist.IsValid())
		return;

	IManagedPredictionClient* pPrediction = ManagedPredictionClient();

	if (!pPrediction)
	{
		// The runtime is not up; there is deliberately no native fallback placement.
		if (pPlayer)
			pPlayer->UpdateVisibility();

		return;
	}

	const Vector vecEye = pSetup->origin;

	// Phase one: where the camera wants to be. Managed does the arithmetic.
	SourceSharpThirdPersonCamera io;
	V_memset(&io, 0, sizeof(io));
	io.struct_size = sizeof(io);
	io.nPhase = SOURCESHARP_THIRDPERSON_PHASE_PLACE;
	io.vecViewAngles[0] = pSetup->angles[PITCH];
	io.vecViewAngles[1] = pSetup->angles[YAW];
	io.vecViewAngles[2] = pSetup->angles[ROLL];
	io.flIdealPitch = s_IdealPitch.GetFloat();
	io.flIdealYaw = s_IdealYaw.GetFloat();
	io.flIdealDist = s_IdealDist.GetFloat();
	io.flIdealDistRight = s_IdealDistRight.IsValid() ? s_IdealDistRight.GetFloat() : 0.0f;
	io.flIdealDistUp = s_IdealDistUp.IsValid() ? s_IdealDistUp.GetFloat() : 0.0f;
	io.nCollision = s_Collision.IsValid() ? (s_Collision.GetBool() ? 1 : 0) : -1;

	if (!pPrediction->ThirdPersonCamera(&io))
		return;

	const QAngle angCamera(io.vecCameraAngles[0], io.vecCameraAngles[1], io.vecCameraAngles[2]);
	const Vector vecWish(io.vecWish[0], io.vecWish[1], io.vecWish[2]);
	const float flWishLength = io.flWishLength;

	// The hull sweep toward that place; none at 0 (cam_collision off).
	float flTraceFraction = 1.0f;

	if (io.flHullHalfExtent > 0.0f)
	{
		const float flHull = io.flHullHalfExtent;
		trace_t trace;

		UTIL_TraceHull(vecEye, vecEye + vecWish, Vector(-flHull, -flHull, -flHull), Vector(flHull, flHull, flHull),
					   io.nTraceMask, pPlayer, COLLISION_GROUP_NONE, &trace);

		flTraceFraction = trace.fraction;
	}

	// Phase two: how much of the wish survives the sweep, and whether the camera can see the
	// player at all.
	io.nPhase = SOURCESHARP_THIRDPERSON_PHASE_RESOLVE;
	io.flTraceFraction = flTraceFraction;

	if (!pPrediction->ThirdPersonCamera(&io))
		return;

	const float flSurviving = io.flSurvivingFraction;

	pSetup->origin = vecEye + vecWish * flSurviving;
	pSetup->angles = angCamera;

	s_bThirdPersonSeesOwner = (io.bSeesOwner != 0);

	// The leaf system never re-asks ShouldDraw for a world entity, so the player is moved in
	// or out here when the camera changes, as stock's UpdateVisibility does.
	if (pPlayer)
		pPlayer->UpdateVisibility();
}

// The client mode: IClientMode implemented directly over the raw viewport. Its decisions
// are managed's; native keeps the view setup, the sound DSP reset, the particle manager's
// post-render, the rumble update and the team-255 broadcast sound.

static ConVar cl_show_num_particle_systems("cl_show_num_particle_systems", "0", FCVAR_CLIENTDLL,
										   "Display the number of active particle systems.");

CON_COMMAND(cl_reload_localization_files, "Reloads all localization files")
{
	g_pVGuiLocalize->ReloadLocalizationFiles();
}

// The view managed asked OverrideView to draw this frame.
struct ViewOverride_t
{
	bool bActive = false;
	bool bSetViewAngles = false;
	Vector origin;
	QAngle angles;
};

static ViewOverride_t s_ViewOverride;

// Managed's screen shake: this frame's offset and roll, added at factor 1.
static void ApplyManagedShake(Vector& origin, QAngle& angles)
{
	float out[4] = {0, 0, 0, 0};
	if (IManagedRenderClient* p = ManagedRenderClient())
		p->ViewEffectsCalcShake(gpGlobals->curtime, gpGlobals->frametime, out);
	VectorMA(origin, 1.0f, Vector(out[0], out[1], out[2]), origin);
	angles.z += out[3] * 1.0f;
}

// Stock's CalcObserverView, decided by managed and carried out here: the view, the engine's
// view angles when in-eye asks, and the shake. False when there is no override this frame.
static bool ApplyViewOverride(CViewSetup* pSetup)
{
	if (!s_ViewOverride.bActive)
		return false;
	pSetup->origin = s_ViewOverride.origin;
	pSetup->angles = s_ViewOverride.angles;
	if (s_ViewOverride.bSetViewAngles)
		engine->SetViewAngles(pSetup->angles);
	ApplyManagedShake(pSetup->origin, pSetup->angles);
	return true;
}

void SourceSharp_ViewOverrideSet(int32_t nActive, const float* pOriginXYZ, const float* pAngles, int32_t nSetViewAngles)
{
	s_ViewOverride.bActive = nActive != 0 && pOriginXYZ && pAngles;
	if (!s_ViewOverride.bActive)
		return;
	s_ViewOverride.origin.Init(pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2]);
	s_ViewOverride.angles.Init(pAngles[0], pAngles[1], pAngles[2]);
	s_ViewOverride.bSetViewAngles = nSetViewAngles != 0;
}

class CSourceSharpClientMode : public IClientMode, public CGameEventListener
{
public:
	CSourceSharpClientMode()
		: m_pViewport(NULL)
	{
		SOURCESHARP_TRACE();
	}

	virtual ~CSourceSharpClientMode()
	{
		SOURCESHARP_TRACE();
		delete m_pViewport;
		m_pViewport = NULL;
	}

	virtual void InitViewport() OVERRIDE
	{
		SOURCESHARP_TRACE();
		m_pViewport = new CSourceSharpViewport();
		m_pViewport->Start(gameeventmanager);
	}

	virtual void Init() OVERRIDE { ListenForGameEvent("teamplay_broadcast_audio"); }

	virtual void VGui_Shutdown() OVERRIDE
	{
		delete m_pViewport;
		m_pViewport = NULL;
	}

	virtual void Shutdown() OVERRIDE {}

	// Under the client root, shown, laid out.
	virtual void Enable() OVERRIDE
	{
		if (!m_pViewport)
			return;
		if (vgui::VPANEL root = VGui_GetClientDLLRootPanel())
			m_pViewport->SetParent(root);
		m_pViewport->SetCursor(vgui::dc_none);
		vgui::surface()->SetCursor(vgui::dc_none);
		m_pViewport->SetVisible(true);
		Layout();
	}

	virtual void Disable() OVERRIDE
	{
		if (!m_pViewport)
			return;
		m_pViewport->SetParent((vgui::VPANEL)0);
		m_pViewport->SetVisible(false);
	}

	// The viewport fills the client root.
	virtual void Layout() OVERRIDE
	{
		vgui::VPANEL root = VGui_GetClientDLLRootPanel();
		if (!m_pViewport || !root)
			return;
		int nWide, nTall;
		vgui::ipanel()->GetSize(root, nWide, nTall);
		m_pViewport->SetBounds(0, 0, nWide, nTall);
	}

	// The HUD and menu roots ask for the viewport's VPANEL instead.
	virtual vgui::Panel* GetViewport() OVERRIDE { return NULL; }

	virtual vgui::AnimationController* GetViewportAnimationController() OVERRIDE { return NULL; }

	vgui::VPANEL GetViewportVPanel() { return m_pViewport ? m_pViewport->GetVPanel() : 0; }

	virtual void ProcessInput(bool bActive) OVERRIDE { gHUD.ProcessInput(bActive); }

	virtual bool ShouldDrawDetailObjects() OVERRIDE { return true; }

	virtual bool ShouldDrawEntity(C_BaseEntity* pEnt) OVERRIDE { return true; }

	virtual bool ShouldDrawParticles() OVERRIDE { return true; }

	virtual bool ShouldDrawFog() OVERRIDE { return true; }

	virtual bool ShouldDrawViewModel() OVERRIDE { return true; }

	virtual bool ShouldDrawCrosshair() OVERRIDE { return true; }

	// The body draws when the camera got far enough behind the eye to see it.
	virtual bool ShouldDrawLocalPlayer(C_BasePlayer* pPlayer) OVERRIDE { return SourceSharp_ThirdPersonSeesOwner(); }

	virtual void StartMessageMode(int iMessageModeType) OVERRIDE {}

	virtual vgui::Panel* GetMessagePanel() OVERRIDE { return NULL; }

	virtual void OverrideMouseInput(float* x, float* y) OVERRIDE {}

	virtual bool CreateMove(float flInputSampleTime, CUserCmd* cmd) OVERRIDE { return true; }

	// game_newmap, faked client side as stock did (clients connect after the server fired it),
	// and the player's sound DSP reset.
	virtual void LevelInit(const char* newmap) OVERRIDE
	{
		if (IGameEvent* event = gameeventmanager->CreateEvent("game_newmap"))
		{
			event->SetString("mapname", newmap);
			gameeventmanager->FireEventClientSide(event);
		}
		CLocalPlayerFilter filter;
		enginesound->SetPlayerDSP(filter, 0, true);
	}

	virtual void LevelShutdown() OVERRIDE
	{
		g_ThirdPersonManager.Init();
		CLocalPlayerFilter filter;
		enginesound->SetPlayerDSP(filter, 0, true);
	}

	virtual void AdjustEngineViewport(int& x, int& y, int& width, int& height) OVERRIDE {}

	// Before the main view, where stock's DrawMonitors draws point_cameras.
	virtual void PreRender(CViewSetup* pSetup) OVERRIDE { ShellViewTargets_RenderQueued(); }

	virtual void PostRender() OVERRIDE
	{
		ShellParticles_PostRender(); // the old-style effects, managed's
	}

	virtual void PostRenderVGui() OVERRIDE {}

	// Nothing activates an in-game vgui context in this build.
	virtual void ActivateInGameVGuiContext(vgui::Panel* pPanel) OVERRIDE {}

	virtual void DeactivateInGameVGuiContext() OVERRIDE {}

	virtual float GetViewModelFOV() OVERRIDE
	{
		ConVarRef viewmodelFov("viewmodel_fov", true);
		return viewmodelFov.IsValid() ? viewmodelFov.GetFloat() : 54.0f;
	}

	virtual bool CanRecordDemo(char* errorMsg, int length) const OVERRIDE { return true; }

	virtual void ComputeVguiResConditions(KeyValues* pkvConditions) OVERRIDE {}

	virtual wchar_t* GetServerName() OVERRIDE { return NULL; }

	virtual void SetServerName(wchar_t* name) OVERRIDE {}

	virtual wchar_t* GetMapName() OVERRIDE { return NULL; }

	virtual void SetMapName(wchar_t* name) OVERRIDE {}

	virtual bool DoPostScreenSpaceEffects(const CViewSetup* pSetup) OVERRIDE { return true; }

	virtual void DisplayReplayMessage(const char* pLocalizeName, float flDuration, bool bUrgent, const char* pSound,
									  bool bDlg) OVERRIDE
	{
	}

	// The rumble update, and stock's particle-system count when asked.
	virtual void Update() OVERRIDE
	{
		UpdateRumbleEffects();

		if (cl_show_num_particle_systems.GetBool())
		{
			int nCount = 0;
			for (int i = 0; i < g_pParticleSystemMgr->GetParticleSystemCount(); i++)
			{
				CParticleSystemDefinition* pDef =
					g_pParticleSystemMgr->FindParticleSystem(g_pParticleSystemMgr->GetParticleSystemNameFromIndex(i));
				if (!pDef)
					continue;
				for (CParticleCollection* p = pDef->FirstCollection(); p; p = p->GetNextCollectionUsingSameDef())
					++nCount;
			}
			engine->Con_NPrintf(0, "# Active particle systems: %i", nCount);
		}
	}

	virtual bool ShouldBlackoutAroundHUD() OVERRIDE { return enginevgui->IsGameUIVisible(); }

	virtual HeadtrackMovementMode_t ShouldOverrideHeadtrackControl() OVERRIDE { return HMM_NOOVERRIDE; }

	virtual bool IsInfoPanelAllowed() OVERRIDE { return true; }

	virtual void InfoPanelDisplayed() OVERRIDE {}

	virtual bool IsHTMLInfoPanelAllowed() OVERRIDE { return true; }

	virtual void OnDemoRecordStart(char const* pDemoBaseName) OVERRIDE {}

	virtual void OnDemoRecordStop() OVERRIDE {}

	// A broadcast sound to every team (255) plays on the local player, as stock's.
	virtual void FireGameEvent(IGameEvent* event) OVERRIDE
	{
		if (V_strcmp(event->GetName(), "teamplay_broadcast_audio") || event->GetInt("team") != 255)
			return;
		EmitSound_t et;
		et.m_pSoundName = event->GetString("sound");
		et.m_nFlags = event->GetInt("additional_flags");
		CLocalPlayerFilter filter;
		ClientSound::Emit(filter, SOUND_FROM_LOCAL_PLAYER, et);
	}

	// Managed decides: 1 the engine runs the binding, 0 the key was used, -1 no decision.
	// With no decision or no managed side, the engine runs it.
	virtual int KeyInput(int down, ButtonCode_t keynum, const char* pszCurrentBinding) OVERRIDE
	{
		IManagedUiClient* pUi = ManagedUiClient();
		if (!pUi)
			return 1;
		const int nDecision =
			pUi->KeyInput(down, keynum, pszCurrentBinding, engine->Con_IsVisible() ? 1 : 0, gpGlobals->maxClients);
		return nDecision >= 0 ? nDecision : 1;
	}

	// SetUpViews calls this after zeroing the origin and before deriving the camera, audio
	// state and g_vecRenderOrigin from it, so filling it here makes the view follow the player.
	virtual void OverrideView(CViewSetup* pSetup) OVERRIDE
	{
		if (C_StandaloneEntity* pPlayer = SourceSharp_LocalPlayerShell())
		{
			if (ApplyViewOverride(pSetup))
				return;

			pSetup->origin = pPlayer->EyePosition();
			engine->GetViewAngles(pSetup->angles);

			// Punch then shake, before the viewmodel is pinned, in stock CalcPlayerView's order.
			VectorAdd(pSetup->angles, pPlayer->PredictedPunchAngle(), pSetup->angles);

			ApplyManagedShake(pSetup->origin, pSetup->angles);

			// The viewmodel is pinned to these eyes with the frame's actual view values.
			SourceSharp_StandaloneViewModelToEye(pSetup->origin, pSetup->angles);

			// Then the camera may leave the eye: the viewmodel stays at the eye, and only the camera
			// moves to third person.
			SourceSharp_ApplyThirdPersonCamera(pPlayer, pSetup);
			return;
		}

		// No viewer: stock's orthographic camera.
		if (SourceSharp_HasLocalViewer() && !::input->CAM_IsThirdPerson() && ::input->CAM_IsOrthographic())
		{
			pSetup->m_bOrtho = true;
			float w, h;
			::input->CAM_OrthographicSize(w, h);
			pSetup->m_OrthoLeft = -w * 0.5f;
			pSetup->m_OrthoTop = -h * 0.5f;
			pSetup->m_OrthoRight = w * 0.5f;
			pSetup->m_OrthoBottom = h * 0.5f;
		}
	}

private:
	CSourceSharpViewport* m_pViewport;
};

vgui::VPANEL SourceSharp_ClientViewportPanel();

static CSourceSharpClientMode g_ClientMode;
IClientMode* g_pClientMode = &g_ClientMode;

vgui::VPANEL SourceSharp_ClientViewportPanel()
{
	return g_ClientMode.GetViewportVPanel();
}

// The client mode's Layout, when managed asks.
void SourceSharp_ClientModeLayout()
{
	g_pClientMode->Layout();
}

IClientMode* GetClientModeNormal()
{
	SOURCESHARP_TRACE_HOT();

	return &g_ClientMode;
}

class CSourceSharpModeManager : public IVModeManager
{
public:
	virtual void Init() OVERRIDE { SOURCESHARP_STUB(); }

	virtual void SwitchMode(bool commander, bool force) OVERRIDE { SOURCESHARP_STUB(); }

	virtual void LevelInit(const char* newmap) OVERRIDE
	{
		SOURCESHARP_TRACE_MSG(newmap);
		g_pClientMode->LevelInit(newmap);
	}

	virtual void LevelShutdown() OVERRIDE
	{
		SOURCESHARP_TRACE();
		g_pClientMode->LevelShutdown();
	}
};

static CSourceSharpModeManager g_ModeManager;
IVModeManager* modemanager = &g_ModeManager;

// The engine parents all client VGUI under this.
static CSourceSharpRawPanel* g_pRootPanel = NULL;

// VGUI clips a child to its parent, so the root must fill the screen, from creation and
// again whenever the screen size changes.
void VGUI_CreateClientDLLRootPanel()
{
	SOURCESHARP_TRACE();
	g_pRootPanel = new CSourceSharpRawPanel("SourceSharp Root Panel");
	g_pRootPanel->SetParent(enginevgui->GetPanel(PANEL_CLIENTDLL));
	g_pRootPanel->SetPaintEnabled(false);
	g_pRootPanel->SetCursor(vgui::dc_none);
	g_pRootPanel->SetZPos(0);
	g_pRootPanel->SetVisible(true);
	g_pRootPanel->SetKeyBoardInputEnabled(false);
	g_pRootPanel->SetMouseInputEnabled(false);

	g_pRootPanel->FillScreen();
	int nScreenW, nScreenH;
	g_pRootPanel->GetSize(nScreenW, nScreenH);

	Msg("[SourceSharp] VGUI root panel sized %dx%d\n", nScreenW, nScreenH);
}

void VGUI_DestroyClientDLLRootPanel()
{
	SOURCESHARP_TRACE();
	delete g_pRootPanel;
	g_pRootPanel = NULL;
}

vgui::VPANEL VGui_GetClientDLLRootPanel()
{
	SOURCESHARP_TRACE_HOT();

	return g_pRootPanel ? g_pRootPanel->GetVPanel() : 0;
}

// The FPS, net-graph and studio-stats hooks the engine still calls; they do nothing.
namespace
{
class CAbsentFPSPanel final : public IFPSPanel
{
public:
	void Create(vgui::VPANEL) override {}

	void Destroy() override {}
};

CAbsentFPSPanel s_AbsentFPSPanel;
}

IFPSPanel* fps = &s_AbsentFPSPanel;

namespace
{
class CAbsentNetGraphPanel final : public INetGraphPanel
{
public:
	void Create(vgui::VPANEL) override {}

	void Destroy() override {}
};

CAbsentNetGraphPanel s_AbsentNetGraphPanel;
}

INetGraphPanel* netgraphpanel = &s_AbsentNetGraphPanel;

IClientRenderable* g_pStudioStatsEntity = NULL;

void StudioStats_FindClosestEntity(CClientRenderablesList*) {}

// Managed calls this once per ammo type; the client keeps no ammo table.
void SourceSharp_RegisterAmmoType(const char* pName, int32_t nDamageType, int32_t nMaxCarry, int32_t nTracerType,
								  float flDamageForce, float flDamage)
{
}
