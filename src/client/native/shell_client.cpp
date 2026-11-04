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

#include "vgui_int.h"
#include "ienginevgui.h"

#include "vgui_controls/Controls.h"
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
#include "shell_vgui_object.h"
#include "shell_managed_particles.h"
#include "shell_standalone_clients.h"
#include "shell_client_engine_interfaces.h"

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
	// The edge, not the write: leaving third person re-asks every shell, the carried
	// weapon with the body; on a settled frame the flag already agrees and nothing sweeps.
	m_Standalone.SetRestingThirdPersonSeen(false);

	if (!m_Engines.m_pInput || !m_Engines.m_pInput->CAM_IsThirdPerson())
	{
		// Still tell the player: leaving third person is when its model must leave the leaf system.
		if (pPlayer)
			pPlayer->UpdateVisibility();

		return;
	}

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

		UTIL_TraceHull(m_Engines, vecEye, vecEye + vecWish, Vector(-flHull, -flHull, -flHull),
					   Vector(flHull, flHull, flHull), io.nTraceMask, pPlayer, COLLISION_GROUP_NONE, &trace);

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

	m_Standalone.SetRestingThirdPersonSeen(io.bSeesOwner != 0);

	// The leaf system never re-asks ShouldDraw for a world entity, so the player is moved in
	// or out here when the camera changes, as stock's UpdateVisibility does.
	if (pPlayer)
		pPlayer->UpdateVisibility();
}

// CSourceSharpClientMode (shell_client_mode.h).

// Managed's screen shake: this frame's offset and roll, added at factor 1.
void CSourceSharpClientMode::ApplyManagedShake(Vector& origin, QAngle& angles)
{
	float out[4] = {0, 0, 0, 0};
	if (IManagedRenderClient* p = m_Areas.m_pRenderClient)
		p->ViewEffectsCalcShake(m_Engines.m_pGlobals->curtime, m_Engines.m_pGlobals->frametime, out);
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
		p->ViewEffectsViewModelBob(m_Engines.m_pGlobals->curtime, m_Engines.m_pGlobals->frametime, inOrigin, inAngles,
								   outOrigin, outAngles);
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
		m_Engines.m_pEngine->SetViewAngles(pSetup->angles);
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

CSourceSharpClientMode::CSourceSharpClientMode(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
											   CHud& hud, CStandaloneClients& standalone,
											   CManagedParticlesClient& particles, CViewTargetsClient& viewTargets,
											   const CVguiClient& vgui)
	: CGameEventListener(engines)
	, m_Areas(areas)
	, m_Engines(engines)
	, m_Hud(hud)
	, m_Standalone(standalone)
	, m_Particles(particles)
	, m_ViewTargets(viewTargets)
	, m_Vgui(vgui)
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
	m_pViewport = new CSourceSharpViewport(m_Areas, m_Engines);
	m_pViewport->Start(m_Engines.m_pEvents);
}

IViewPort* CSourceSharpClientMode::ViewPort() const
{
	return m_pViewport;
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
	if (vgui::VPANEL root = m_Vgui.RootPanel())
		m_pViewport->SetParent(root);
	m_pViewport->SetCursor(vgui::dc_none);
	m_Engines.m_pVGuiSurface->SetCursor(vgui::dc_none);
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
	vgui::VPANEL root = m_Vgui.RootPanel();
	if (!m_pViewport || !root)
		return;
	int nWide, nTall;
	m_Engines.m_pVGuiPanel->GetSize(root, nWide, nTall);
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
	m_Hud.ProcessInput(bActive);
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
	if (IGameEvent* event = m_Engines.m_pEvents->CreateEvent("game_newmap"))
	{
		event->SetString("mapname", newmap);
		m_Engines.m_pEvents->FireEventClientSide(event);
	}
	CLocalPlayerFilter filter(m_Engines.m_pEngine);
	m_Engines.m_pSound->SetPlayerDSP(filter, 0, true);
}

void CSourceSharpClientMode::LevelShutdown()
{
	CLocalPlayerFilter filter(m_Engines.m_pEngine);
	m_Engines.m_pSound->SetPlayerDSP(filter, 0, true);
}

void CSourceSharpClientMode::PreRender(CViewSetup* pSetup)
{
	m_ViewTargets.RenderQueued(*this);
}

void CSourceSharpClientMode::PostRender()
{
	m_Particles.PostRender(); // the old-style effects, managed's
}

float CSourceSharpClientMode::GetViewModelFOV()
{
	return 54.0f;
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
	if (m_cl_show_num_particle_systems.GetBool())
	{
		int nCount = 0;
		for (int i = 0; i < m_Engines.m_pParticleMgr->GetParticleSystemCount(); i++)
		{
			CParticleSystemDefinition* pDef = m_Engines.m_pParticleMgr->FindParticleSystem(
				m_Engines.m_pParticleMgr->GetParticleSystemNameFromIndex(i));
			if (!pDef)
				continue;
			for (CParticleCollection* p = pDef->FirstCollection(); p; p = p->GetNextCollectionUsingSameDef())
				++nCount;
		}
		m_Engines.m_pEngine->Con_NPrintf(0, "# Active particle systems: %i", nCount);
	}
}

bool CSourceSharpClientMode::ShouldBlackoutAroundHUD()
{
	return m_Engines.m_pVGui->IsGameUIVisible();
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
	CLocalPlayerFilter filter(m_Engines.m_pEngine);
	ClientSound::Emit(m_Areas.m_pSoundEmitterClient, filter, SOUND_FROM_LOCAL_PLAYER, et);
}

int CSourceSharpClientMode::KeyInput(int down, ButtonCode_t keynum, const char* pszCurrentBinding)
{
	IManagedUiClient* pUi = m_Areas.m_pUiClient;
	if (!pUi)
		return 1;
	const int nDecision = pUi->KeyInput(down, keynum, pszCurrentBinding, m_Engines.m_pEngine->Con_IsVisible() ? 1 : 0,
										m_Engines.m_pGlobals->maxClients);
	return nDecision >= 0 ? nDecision : 1;
}

void CSourceSharpClientMode::OverrideView(CViewSetup* pSetup)
{
	if (C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer())
	{
		if (ApplyViewOverride(pSetup))
			return;

		pSetup->origin = pPlayer->EyePosition();
		m_Engines.m_pEngine->GetViewAngles(pSetup->angles);

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
	if (m_Standalone.LocalPlayer() && !m_Engines.m_pInput->CAM_IsThirdPerson() &&
		m_Engines.m_pInput->CAM_IsOrthographic())
	{
		pSetup->m_bOrtho = true;
		float w, h;
		m_Engines.m_pInput->CAM_OrthographicSize(w, h);
		pSetup->m_OrthoLeft = -w * 0.5f;
		pSetup->m_OrthoTop = -h * 0.5f;
		pSetup->m_OrthoRight = w * 0.5f;
		pSetup->m_OrthoBottom = h * 0.5f;
	}
}

// Managed calls this once per ammo type; the client keeps no ammo table.
void SourceSharp_RegisterAmmoType(const char* pName, int32_t nDamageType, int32_t nMaxCarry, int32_t nTracerType,
								  float flDamageForce, float flDamage)
{
}

void CSourceSharpClientMode::ReloadLocalizationCommand(const CCommand& args)
{
	m_Engines.m_pVGuiLocalize->ReloadLocalizationFiles();
}
