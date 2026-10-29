//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

//#include "vgui_bitmap.h"
#include <vgui_controls/Button.h>
#include <vgui_controls/Image.h>
#include "voice_common.h"
#include "voice_banmgr.h"
#include "hudelement.h"

class CVoiceStatus;
class IMaterial;

// This is provided by each mod to access data that may not be the same across mods.
abstract_class IVoiceStatusHelper
{
public:
	virtual ~IVoiceStatusHelper() {}

	// Get RGB color for voice status text about this player.
	virtual void GetPlayerTextColor( int entindex, int color[3] ) = 0;

	// Force it to update the cursor state.
	virtual void UpdateCursorState() = 0;

	// Return true if the voice manager is allowed to show speaker labels
	// (mods usually return false when the scoreboard is up).
	virtual bool CanShowSpeakerLabels() = 0;
};

class CVoiceStatus /*: public vgui::CDefaultInputSignal*/
{
public:
	CVoiceStatus();
	virtual ~CVoiceStatus();

	// CHudBase overrides.
public:
	// Initialize the cl_dll's voice manager.
	virtual int Init( IVoiceStatusHelper *m_pHelper, vgui::VPANEL pParentPanel );

	// ackPosition is the bottom position of where CVoiceStatus will draw the voice acknowledgement labels.
	virtual void VidInit();

public:
	// Call from HUD_Frame each frame.
	void Frame( double frametime );

	// Called when a player starts or stops talking.
	// entindex is -1 to represent the local client talking (before the data comes back from the server).
	// When the server acknowledges that the local client is talking, then entindex will be gEngfuncs.GetLocalPlayer().
	// entindex is -2 to represent the local client's voice being acked by the server.
	void UpdateSpeakerStatus( int entindex, bool bTalking );

	// Call from the HUD_CreateEntities function so it can add sprites above player heads.
	void DrawHeadLabels();

	// Squelch mode functions.
public:
	void StopSquelchMode();

	// returns true if the target client has been banned
	// playerIndex is of range 1..maxplayers
	bool IsPlayerBlocked( int iPlayerIndex );

	// blocks the target client from being heard
	void SetPlayerBlockedState( int iPlayerIndex, bool blocked );

private:
private:
	alignas( 8 ) unsigned char unused_m_LastUpdateServerState[8];

	vgui::VPANEL m_pParentPanel;
	CPlayerBitVec m_VoicePlayers; // Who is currently talking. Indexed by client index.

	// This is the gamerules-defined list of players that you can hear. It is based on what teams people are on
	// and is totally separate from the ban list. Indexed by client index.
	CPlayerBitVec m_AudiblePlayers;

	// Players who have spoken at least once in the game so far
	CPlayerBitVec m_VoiceEnabledPlayers;

	// This is who the server THINKS we have banned (it can become incorrect when a new player arrives on the server).
	// It is checked periodically, and the server is told to squelch or unsquelch the appropriate players.
	CPlayerBitVec m_ServerBannedPlayers;

	IVoiceStatusHelper *m_pHelper; // Each mod provides an implementation of this.

	// Squelch mode stuff.
	bool m_bInSquelchMode;

	bool m_bTalking; // Set to true when the client thinks it's talking.
	bool m_bServerAcked; // Set to true when the server knows the client is talking.

public:
	CVoiceBanMgr m_BanMgr; // Tracks which users we have squelched and don't want to hear.

private:
	IMaterial *m_pHeadLabelMaterial; // For labels above players' heads.

	bool m_bBanMgrInitialized;

	int m_nControlSize;

	bool m_bHeadLabelsDisabled;
};

// Get the (global) voice manager.
CVoiceStatus *GetClientVoiceMgr();
void ClientVoiceMgr_Init();
void ClientVoiceMgr_Shutdown();
