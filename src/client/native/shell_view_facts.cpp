//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
// CViewerFacts, and view_facts.h's free answers through it.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"

#include "view_subject.h"
#include "shell_standalone_client.h"
#include "shell_diagnostics.h"
#include "shell_cliententitylist.h"
#include "shell_standalone_clients.h"
#include "client_entity_list.h"
#include "shell_viewer_facts.h"

// Checks the value view_subject.h restates against the enum it came from.

COMPILE_TIME_ASSERT(ViewSubject::WATER_LEVEL_EYES == WL_Eyes);

struct CViewerFacts::Pushed_t
{
	// NULL until pushed (no fog controller, or no player yet), as stock answers for a player
	// with no fog controller; a zeroed struct would read as a real "no fog".
	fogparams_t m_Fog;
	bool m_bHaveFog = false;

	// NULL means no sky camera has been heard from. Area 255 is a camera outside the sealed
	// world; a zeroed struct is never returned, since area 0 is a real area.
	sky3dparams_t m_Skybox;
	bool m_bHaveSkybox = false;
};

CViewerFacts::CViewerFacts(CStandaloneClients& standalone, CClientEntityList& entityList)
	: m_Standalone(standalone)
	, m_EntityList(entityList)
	, m_pPushed(NULL)
{
}

CViewerFacts::~CViewerFacts()
{
	if (m_pPushed)
	{
		m_pPushed->~Pushed_t();
		free(m_pPushed);
	}
}

// Zeroed before its constructors run, as the statics it replaces were: the pushes do
// not write every field.
CViewerFacts::Pushed_t* CViewerFacts::NewPushed()
{
	return new (calloc(1, sizeof(Pushed_t))) Pushed_t;
}

#include "sourcesharp.h"

bool CViewerFacts::HasLocalViewer() const
{
	return m_Standalone.LocalPlayer() != NULL;
}

bool CViewerFacts::ViewerAbsPosition(Vector* pOrigin, QAngle* pAngles) const
{
	C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer();

	if (!pPlayer)
		return false;

	if (pOrigin)
		*pOrigin = pPlayer->GetAbsOrigin();

	if (pAngles)
		*pAngles = pPlayer->GetAbsAngles();

	return true;
}

// The engine's view entity, when it is not this client's own player.
// GetClientEntity rather than GetEnt: a managed entity has no C_BaseEntity, and
// IClientEntity is what both hierarchies implement.
bool CViewerFacts::ViewEntityPose(Vector* pOrigin, QAngle* pAngles) const
{
	const int nViewEntity = render->GetViewEntity();

	if (!ViewSubject::ViewEntityOverridesTheEye(nViewEntity, engine->GetLocalPlayer()))
		return false;

	IClientUnknown* pViewUnknown = m_EntityList.UnknownFromIndex(nViewEntity);
	IClientEntity* pViewEntity = pViewUnknown ? pViewUnknown->GetIClientEntity() : NULL;

	if (!pViewEntity)
		return false;

	if (pOrigin)
		*pOrigin = pViewEntity->GetAbsOrigin();

	if (pAngles)
		*pAngles = pViewEntity->GetAbsAngles();

	return true;
}

// Read from the engine block, the one copy of an entity's EF_* bits.
bool CViewerFacts::ViewerEffectActive(int nEffectBits) const
{
	C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer();

	if (!pPlayer)
		return false;

	return ViewSubject::EffectActive(pPlayer->Block().Head.Effects, nEffectBits);
}

// The sound listener is underwater when the viewer's eyes are (stock:
// GetWaterLevel() >= WL_Eyes). vecMainViewOrigin, stock's spectator input, is unused.
bool CViewerFacts::ViewerAudioIsUnderwater(const Vector& vecMainViewOrigin) const
{
	NOTE_UNUSED(vecMainViewOrigin);

	C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer();

	if (!pPlayer)
		return false;

	return ViewSubject::AudioIsUnderwater(pPlayer->Block().Head.WaterLevel);
}

// CNetworkColor32 wants its four components set, not an address written through.
template <typename T>
static void SourceSharp_UnpackViewerColor32(uint32_t packed, T& out)
{
	out.SetR((unsigned char)(packed & 0xFF));
	out.SetG((unsigned char)((packed >> 8) & 0xFF));
	out.SetB((unsigned char)((packed >> 16) & 0xFF));
	out.SetA((unsigned char)((packed >> 24) & 0xFF));
}

void CViewerFacts::SetViewerFogParams(const SourceSharpFogParams* pParams)
{
	if (!m_pPushed)
		m_pPushed = NewPushed();

	if (!pParams)
	{
		// Cleared, which is not "fog off".
		m_pPushed->m_bHaveFog = false;
		return;
	}

	m_pPushed->m_Fog.enable = pParams->Enable != 0;
	m_pPushed->m_Fog.blend = pParams->Blend != 0;
	m_pPushed->m_Fog.radial = pParams->Radial != 0;

	m_pPushed->m_Fog.dirPrimary =
		Vector(pParams->DirPrimaryXYZ[0], pParams->DirPrimaryXYZ[1], pParams->DirPrimaryXYZ[2]);

	SourceSharp_UnpackViewerColor32(pParams->ColorPrimary, m_pPushed->m_Fog.colorPrimary);
	SourceSharp_UnpackViewerColor32(pParams->ColorSecondary, m_pPushed->m_Fog.colorSecondary);

	// The lerp targets are the current values, not zero: GetFogColor blends towards
	// them whenever lerptime is in the future. The server already ran the lerp.
	m_pPushed->m_Fog.colorPrimaryLerpTo = m_pPushed->m_Fog.colorPrimary;
	m_pPushed->m_Fog.colorSecondaryLerpTo = m_pPushed->m_Fog.colorSecondary;
	m_pPushed->m_Fog.startLerpTo = pParams->Start;
	m_pPushed->m_Fog.endLerpTo = pParams->End;
	m_pPushed->m_Fog.lerptime = 0.0f;
	m_pPushed->m_Fog.duration = 0.0f;

	m_pPushed->m_Fog.start = pParams->Start;
	m_pPushed->m_Fog.end = pParams->End;
	m_pPushed->m_Fog.farz = pParams->FarZ;
	m_pPushed->m_Fog.maxdensity = pParams->MaxDensity;

	m_pPushed->m_bHaveFog = true;
}

fogparams_t* CViewerFacts::ViewerFogParams()
{
	if (!m_pPushed || !m_pPushed->m_bHaveFog)
	{
		return NULL;
	}

	return &m_pPushed->m_Fog;
}

void CViewerFacts::SetViewerSkyboxParams(const SourceSharpSkyboxParams* pParams)
{
	if (!m_pPushed)
		m_pPushed = NewPushed();

	if (!pParams)
	{
		// Cleared, which is not area 255.
		m_pPushed->m_bHaveSkybox = false;
		return;
	}

	m_pPushed->m_Skybox.origin = Vector(pParams->OriginXYZ[0], pParams->OriginXYZ[1], pParams->OriginXYZ[2]);
	m_pPushed->m_Skybox.scale = pParams->Scale;
	m_pPushed->m_Skybox.area = pParams->Area;

	m_pPushed->m_Skybox.fog.enable = pParams->FogEnable != 0;
	m_pPushed->m_Skybox.fog.blend = pParams->FogBlend != 0;
	m_pPushed->m_Skybox.fog.radial = pParams->FogRadial != 0;

	m_pPushed->m_Skybox.fog.dirPrimary =
		Vector(pParams->FogDirPrimaryXYZ[0], pParams->FogDirPrimaryXYZ[1], pParams->FogDirPrimaryXYZ[2]);

	SourceSharp_UnpackViewerColor32(pParams->FogColorPrimary, m_pPushed->m_Skybox.fog.colorPrimary);
	SourceSharp_UnpackViewerColor32(pParams->FogColorSecondary, m_pPushed->m_Skybox.fog.colorSecondary);

	// The lerp targets are the current values, as for the world fog above.
	m_pPushed->m_Skybox.fog.colorPrimaryLerpTo = m_pPushed->m_Skybox.fog.colorPrimary;
	m_pPushed->m_Skybox.fog.colorSecondaryLerpTo = m_pPushed->m_Skybox.fog.colorSecondary;
	m_pPushed->m_Skybox.fog.startLerpTo = pParams->FogStart;
	m_pPushed->m_Skybox.fog.endLerpTo = pParams->FogEnd;
	m_pPushed->m_Skybox.fog.lerptime = 0.0f;
	m_pPushed->m_Skybox.fog.duration = 0.0f;

	m_pPushed->m_Skybox.fog.start = pParams->FogStart;
	m_pPushed->m_Skybox.fog.end = pParams->FogEnd;
	m_pPushed->m_Skybox.fog.maxdensity = pParams->FogMaxDensity;

	// farz is not sent: sky_camera has no such key and CSkyboxView sets zFar itself.

	m_pPushed->m_bHaveSkybox = true;
}

sky3dparams_t* CViewerFacts::ViewerSkyboxParams()
{
	if (!m_pPushed || !m_pPushed->m_bHaveSkybox)
		return NULL;

	return &m_pPushed->m_Skybox;
}

// Refreshes visibility on the camera mode change's edge, so the player's own
// model leaves or joins the leaf system that frame rather than the next.
void CViewerFacts::ViewerCameraModeChanged()
{
	if (C_StandaloneEntity* pPlayer = m_Standalone.LocalPlayer())
		pPlayer->UpdateVisibility();
}

// HL2 aim assist: neither end of m_bAutoAimTarget's wire is in this build.
bool SourceSharp_ViewerAimIsAssisted()
{
	SourceSharp_NotShipped("HL2 autoaim assist (client)",
						   "CHL2_PlayerLocalData::m_bAutoAimTarget is written on the server by "
						   "hl2_player.cpp and networked through DT_HL2Local; neither file is in "
						   "this build and the state is not in the managed player's networked "
						   "declaration, so the joystick never dampens onto a target");

	return false;
}
