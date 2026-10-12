//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — CVoiceStatus as thunks to the managed voice manager //
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve C3 step 4, plan cluster 15. Stock's CVoiceStatus
// (game/shared/voice_status.cpp) and its ban list (voice_banmgr.cpp) are
// SourceSharp.Client's VoiceStatus and VoiceBanList now (the ban list held to
// an oracle of stock's file format). The engine's VoiceStatus, the VoiceMask
// and RequestState messages and the server commands are managed end to end.
// What native code still asks of the object is answered here: the mute
// questions IGameClientExports puts (IManagedUiClient::VoiceIsBlocked and
// VoiceToggleBlocked), the squelch mode in_main.cpp stops (the helper's hook),
// and the calls the shell and viewrender.cpp make that have nothing left to do.
// The dead vgui free functions of stock's file (a bitmap image and a Panel's
// bounds) went with it.
//=============================================================================//

#include "cbase.h"
#include "voice_status.h"
#include "imanaged_areas_client.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// The ban list is managed; the member CVoiceStatus's header still carries is never used.
CVoiceBanMgr::CVoiceBanMgr() {}
CVoiceBanMgr::~CVoiceBanMgr() {}

static CVoiceStatus *s_pVoiceStatus = NULL;

CVoiceStatus *GetClientVoiceMgr()
{
	if ( !s_pVoiceStatus )
		ClientVoiceMgr_Init();
	return s_pVoiceStatus;
}

void ClientVoiceMgr_Init()
{
	if ( !s_pVoiceStatus )
		s_pVoiceStatus = new CVoiceStatus();
}

void ClientVoiceMgr_Shutdown()
{
	delete s_pVoiceStatus;
	s_pVoiceStatus = NULL;
}

CVoiceStatus::CVoiceStatus()
	: m_pParentPanel( 0 ), m_pHelper( NULL ), m_bInSquelchMode( false ), m_pHeadLabelMaterial( NULL )
{
}

CVoiceStatus::~CVoiceStatus()
{
}

int CVoiceStatus::Init( IVoiceStatusHelper *pHelper, vgui::VPANEL pParentPanel )
{
	m_pHelper = pHelper;
	m_pParentPanel = pParentPanel;
	return 1;
}

void CVoiceStatus::VidInit() {}

// The managed voice manager hears the engine's VoiceStatus and ticks on the
// client's frame; these calls have nothing left to do.
void CVoiceStatus::Frame( double frametime ) {}
void CVoiceStatus::UpdateSpeakerStatus( int entindex, bool bTalking ) {}

// Stock's body visited each talking player and drew nothing (its draw arm was
// an entity this build does not have).
void CVoiceStatus::DrawHeadLabels() {}

bool CVoiceStatus::IsPlayerBlocked( int iPlayerIndex )
{
	IManagedUiClient *pUi = ManagedUiClient();
	return pUi && pUi->VoiceIsBlocked( iPlayerIndex ) != 0;
}

// Stock's flips the mute whatever it is asked; so does the managed one.
void CVoiceStatus::SetPlayerBlockedState( int iPlayerIndex, bool blocked )
{
	if ( IManagedUiClient *pUi = ManagedUiClient() )
		pUi->VoiceToggleBlocked( iPlayerIndex );
}

void CVoiceStatus::StopSquelchMode()
{
	m_bInSquelchMode = false;
	if ( m_pHelper )
		m_pHelper->UpdateCursorState();
}
