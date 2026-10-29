//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// CVoiceStatus as thunks to the managed voice manager: the mute questions
// IGameClientExports asks and the squelch mode in_main.cpp stops. The other
// calls have nothing to do.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "voice_status.h"

#include "imanaged_areas_client.h"

// The ban list is managed; this member of CVoiceStatus is never used.
CVoiceBanMgr::CVoiceBanMgr() {}

CVoiceBanMgr::~CVoiceBanMgr() {}

static CVoiceStatus* s_pVoiceStatus = NULL;

CVoiceStatus* GetClientVoiceMgr()
{
	if (!s_pVoiceStatus)
		ClientVoiceMgr_Init();
	return s_pVoiceStatus;
}

void ClientVoiceMgr_Init()
{
	if (!s_pVoiceStatus)
		s_pVoiceStatus = new CVoiceStatus();
}

void ClientVoiceMgr_Shutdown()
{
	delete s_pVoiceStatus;
	s_pVoiceStatus = NULL;
}

CVoiceStatus::CVoiceStatus()
	: m_pParentPanel(0)
	, m_pHelper(NULL)
	, m_bInSquelchMode(false)
	, m_pHeadLabelMaterial(NULL)
{
}

CVoiceStatus::~CVoiceStatus() {}

int CVoiceStatus::Init(IVoiceStatusHelper* pHelper, vgui::VPANEL pParentPanel)
{
	m_pHelper = pHelper;
	m_pParentPanel = pParentPanel;
	return 1;
}

void CVoiceStatus::VidInit() {}

// The managed voice manager hears the engine's VoiceStatus and ticks on the
// client's frame; these calls have nothing left to do.
void CVoiceStatus::Frame(double frametime) {}

void CVoiceStatus::UpdateSpeakerStatus(int entindex, bool bTalking) {}

// Stock's body drew nothing for any entity this build has.
void CVoiceStatus::DrawHeadLabels() {}

bool CVoiceStatus::IsPlayerBlocked(int iPlayerIndex)
{
	IManagedUiClient* pUi = ManagedUiClient();
	return pUi && pUi->VoiceIsBlocked(iPlayerIndex) != 0;
}

// Toggles the mute whatever state is asked for, as stock does.
void CVoiceStatus::SetPlayerBlockedState(int iPlayerIndex, bool blocked)
{
	if (IManagedUiClient* pUi = ManagedUiClient())
		pUi->VoiceToggleBlocked(iPlayerIndex);
}

void CVoiceStatus::StopSquelchMode()
{
	m_bInSquelchMode = false;
	if (m_pHelper)
		m_pHelper->UpdateCursorState();
}
