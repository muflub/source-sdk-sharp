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
#include "particles/particles.h"

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
#include "shell_client_mode.h"
#include "shell_managed_particles.h"
#include "shell_standalone_clients.h"

// The free answers for the code not yet handed the client mode.
bool SourceSharp_ThirdPersonSeesOwner()
{
	return ShellClientDll_Standalone().ThirdPersonSeesOwner();
}

bool ShellClient_ViewerSeenBegin()
{
	return ShellClientDll_ClientMode().ViewerSeenBegin();
}

void ShellClient_ViewerSeenEnd(bool bWasSeen)
{
	ShellClientDll_ClientMode().ViewerSeenEnd(bWasSeen);
}

bool CSourceSharpClientMode::ViewerSeenBegin()
{
	const bool bWas = m_Standalone.ThirdPersonSeesOwner();
	m_Standalone.SetThirdPersonSeesOwner(true);
	if (C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer())
		pPlayer->UpdateVisibility();
	return bWas;
}

void CSourceSharpClientMode::ViewerSeenEnd(bool bWasSeen)
{
	m_Standalone.SetThirdPersonSeesOwner(bWasSeen);
	if (C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer())
		pPlayer->UpdateVisibility();
}

void CSourceSharpClientMode::ApplyThirdPersonCamera(C_StandaloneEntity* pPlayer, CViewSetup* pSetup)
{
	m_Standalone.SetThirdPersonSeesOwner(false);

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

	IManagedPredictionClient* pPrediction = m_Areas.m_pPredictionClient;

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

	m_Standalone.SetThirdPersonSeesOwner(io.bSeesOwner != 0);

	// The leaf system never re-asks ShouldDraw for a world entity, so the player is moved in
	// or out here when the camera changes, as stock's UpdateVisibility does.
	if (pPlayer)
		pPlayer->UpdateVisibility();
}

// CSourceSharpClientMode (shell_client_mode.h).

static ConVar cl_show_num_particle_systems("cl_show_num_particle_systems", "0", FCVAR_CLIENTDLL,
										   "Display the number of active particle systems.");

CON_COMMAND(cl_reload_localization_files, "Reloads all localization files")
{
	g_pVGuiLocalize->ReloadLocalizationFiles();
}

// Managed's screen shake: this frame's offset and roll, added at factor 1.
void CSourceSharpClientMode::ApplyManagedShake(Vector& origin, QAngle& angles)
{
	float out[4] = {0, 0, 0, 0};
	if (IManagedRenderClient* p = m_Areas.m_pRenderClient)
		p->ViewEffectsCalcShake(gpGlobals->curtime, gpGlobals->frametime, out);
	VectorMA(origin, 1.0f, Vector(out[0], out[1], out[2]), origin);
	angles.z += out[3] * 1.0f;
}

// The viewmodel bob is managed's whole (stock's CalcViewModelBobHelper and
// AddViewModelBobHelper): the eye in, the bobbed viewmodel origin and angles
// out -- the camera's are left as the frame set them. Native only marshals.
void CSourceSharpClientMode::ApplyManagedViewModelBob(Vector& origin, QAngle& angles)
{
	float inOrigin[3] = {origin.x, origin.y, origin.z};
	float inAngles[3] = {angles.x, angles.y, angles.z};
	float outOrigin[3], outAngles[3];
	if (IManagedRenderClient* p = m_Areas.m_pRenderClient)
		p->ViewEffectsViewModelBob(gpGlobals->curtime, gpGlobals->frametime, inOrigin, inAngles, outOrigin, outAngles);
	else
		return;
	origin.Init(outOrigin[0], outOrigin[1], outOrigin[2]);
	angles.Init(outAngles[0], outAngles[1], outAngles[2]);
}

// Stock's CalcObserverView, decided by managed and carried out here: the view, the engine's
// view angles when in-eye asks, and the shake. False when there is no override this frame.
bool CSourceSharpClientMode::ApplyViewOverride(CViewSetup* pSetup)
{
	if (!m_ViewOverride.bActive)
		return false;
	pSetup->origin = m_ViewOverride.origin;
	pSetup->angles = m_ViewOverride.angles;
	if (m_ViewOverride.bSetViewAngles)
		engine->SetViewAngles(pSetup->angles);
	ApplyManagedShake(pSetup->origin, pSetup->angles);
	return true;
}

void CSourceSharpClientMode::ViewOverrideSet(int32_t nActive, const float* pOriginXYZ, const float* pAngles,
											 int32_t nSetViewAngles)
{
	m_ViewOverride.bActive = nActive != 0 && pOriginXYZ && pAngles;
	if (!m_ViewOverride.bActive)
		return;
	m_ViewOverride.origin.Init(pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2]);
	m_ViewOverride.angles.Init(pAngles[0], pAngles[1], pAngles[2]);
	m_ViewOverride.bSetViewAngles = nSetViewAngles != 0;
}

CSourceSharpClientMode::CSourceSharpClientMode(const CManagedAreasClient& areas, CStandaloneClients& standalone,
											   CManagedParticlesClient& particles)
	: m_Areas(areas)
	, m_Standalone(standalone)
	, m_Particles(particles)
	, m_pViewport(NULL)
{
	SOURCESHARP_TRACE();
}

CSourceSharpClientMode::~CSourceSharpClientMode()
{
	SOURCESHARP_TRACE();
	delete m_pViewport;
	m_pViewport = NULL;
}

void CSourceSharpClientMode::InitViewport()
{
	SOURCESHARP_TRACE();
	m_pViewport = new CSourceSharpViewport();
	m_pViewport->Start(gameeventmanager);
}

void CSourceSharpClientMode::Init()
{
	ListenForGameEvent("teamplay_broadcast_audio");
}

void CSourceSharpClientMode::VGui_Shutdown()
{
	delete m_pViewport;
	m_pViewport = NULL;
}

void CSourceSharpClientMode::Enable()
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

void CSourceSharpClientMode::Disable()
{
	if (!m_pViewport)
		return;
	m_pViewport->SetParent((vgui::VPANEL)0);
	m_pViewport->SetVisible(false);
}

void CSourceSharpClientMode::Layout()
{
	vgui::VPANEL root = VGui_GetClientDLLRootPanel();
	if (!m_pViewport || !root)
		return;
	int nWide, nTall;
	vgui::ipanel()->GetSize(root, nWide, nTall);
	m_pViewport->SetBounds(0, 0, nWide, nTall);
}

vgui::Panel* CSourceSharpClientMode::GetViewport()
{
	return NULL;
}

vgui::AnimationController* CSourceSharpClientMode::GetViewportAnimationController()
{
	return NULL;
}

vgui::VPANEL CSourceSharpClientMode::GetViewportVPanel()
{
	return m_pViewport ? m_pViewport->GetVPanel() : 0;
}

void CSourceSharpClientMode::ProcessInput(bool bActive)
{
	gHUD.ProcessInput(bActive);
}

bool CSourceSharpClientMode::ShouldDrawDetailObjects()
{
	return true;
}

bool CSourceSharpClientMode::ShouldDrawEntity(C_BaseEntity* pEnt)
{
	return true;
}

bool CSourceSharpClientMode::ShouldDrawParticles()
{
	return true;
}

bool CSourceSharpClientMode::ShouldDrawFog()
{
	return true;
}

bool CSourceSharpClientMode::ShouldDrawViewModel()
{
	return true;
}

bool CSourceSharpClientMode::ShouldDrawCrosshair()
{
	return true;
}

bool CSourceSharpClientMode::ShouldDrawLocalPlayer(C_BasePlayer* pPlayer)
{
	return m_Standalone.ThirdPersonSeesOwner();
}

vgui::Panel* CSourceSharpClientMode::GetMessagePanel()
{
	return NULL;
}

bool CSourceSharpClientMode::CreateMove(float flInputSampleTime, CUserCmd* cmd)
{
	return true;
}

void CSourceSharpClientMode::LevelInit(const char* newmap)
{
	if (IGameEvent* event = gameeventmanager->CreateEvent("game_newmap"))
	{
		event->SetString("mapname", newmap);
		gameeventmanager->FireEventClientSide(event);
	}
	CLocalPlayerFilter filter;
	enginesound->SetPlayerDSP(filter, 0, true);
}

void CSourceSharpClientMode::LevelShutdown()
{
	CLocalPlayerFilter filter;
	enginesound->SetPlayerDSP(filter, 0, true);
}

void CSourceSharpClientMode::PreRender(CViewSetup* pSetup)
{
	ShellViewTargets_RenderQueued();
}

void CSourceSharpClientMode::PostRender()
{
	m_Particles.PostRender(); // the old-style effects, managed's
}

float CSourceSharpClientMode::GetViewModelFOV()
{
	ConVarRef viewmodelFov("viewmodel_fov", true);
	return viewmodelFov.IsValid() ? viewmodelFov.GetFloat() : 54.0f;
}

bool CSourceSharpClientMode::CanRecordDemo(char* errorMsg, int length) const
{
	return true;
}

wchar_t* CSourceSharpClientMode::GetServerName()
{
	return NULL;
}

wchar_t* CSourceSharpClientMode::GetMapName()
{
	return NULL;
}

bool CSourceSharpClientMode::DoPostScreenSpaceEffects(const CViewSetup* pSetup)
{
	return true;
}

void CSourceSharpClientMode::Update()
{
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

bool CSourceSharpClientMode::ShouldBlackoutAroundHUD()
{
	return enginevgui->IsGameUIVisible();
}

HeadtrackMovementMode_t CSourceSharpClientMode::ShouldOverrideHeadtrackControl()
{
	return HMM_NOOVERRIDE;
}

bool CSourceSharpClientMode::IsInfoPanelAllowed()
{
	return true;
}

bool CSourceSharpClientMode::IsHTMLInfoPanelAllowed()
{
	return true;
}

void CSourceSharpClientMode::FireGameEvent(IGameEvent* event)
{
	if (V_strcmp(event->GetName(), "teamplay_broadcast_audio") || event->GetInt("team") != 255)
		return;
	EmitSound_t et;
	et.m_pSoundName = event->GetString("sound");
	et.m_nFlags = event->GetInt("additional_flags");
	CLocalPlayerFilter filter;
	ClientSound::Emit(filter, SOUND_FROM_LOCAL_PLAYER, et);
}

int CSourceSharpClientMode::KeyInput(int down, ButtonCode_t keynum, const char* pszCurrentBinding)
{
	IManagedUiClient* pUi = m_Areas.m_pUiClient;
	if (!pUi)
		return 1;
	const int nDecision =
		pUi->KeyInput(down, keynum, pszCurrentBinding, engine->Con_IsVisible() ? 1 : 0, gpGlobals->maxClients);
	return nDecision >= 0 ? nDecision : 1;
}

void CSourceSharpClientMode::OverrideView(CViewSetup* pSetup)
{
	if (C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer())
	{
		if (ApplyViewOverride(pSetup))
			return;

		pSetup->origin = pPlayer->EyePosition();
		engine->GetViewAngles(pSetup->angles);

		// Punch then shake, before the viewmodel is pinned, in stock CalcPlayerView's order.
		VectorAdd(pSetup->angles, pPlayer->PredictedPunchAngle(), pSetup->angles);

		ApplyManagedShake(pSetup->origin, pSetup->angles);

		// The viewmodel is pinned to these eyes with the frame's actual view values,
		// bobbed: stock's AddViewModelBob runs on the viewmodel's copy, not the camera's.
		Vector vecBobbed = pSetup->origin;
		QAngle angBobbed = pSetup->angles;
		ApplyManagedViewModelBob(vecBobbed, angBobbed);
		m_Standalone.ViewModelToEye(vecBobbed, angBobbed);

		// Then the camera may leave the eye: the viewmodel stays at the eye, and only the camera
		// moves to third person.
		ApplyThirdPersonCamera(pPlayer, pSetup);
		return;
	}

	// No viewer: stock's orthographic camera.
	if (m_Standalone.LocalPlayer() && !::input->CAM_IsThirdPerson() && ::input->CAM_IsOrthographic())
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

// The client DLL object's member; set from its Init.
IClientMode* g_pClientMode = NULL;

// The client mode's Layout, when managed asks.
void SourceSharp_ClientModeLayout()
{
	g_pClientMode->Layout();
}

IClientMode* GetClientModeNormal()
{
	SOURCESHARP_TRACE_HOT();

	return &ShellClientDll_ClientMode();
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
