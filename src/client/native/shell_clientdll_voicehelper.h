//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The voice manager's game callbacks: a speaking player's name colour, whether
// speaker labels may show, and cursor updates. The instance is a member of
// CShellClientDLL, so the class is declared where that is compiled.
#pragma once

#include "voice_status.h"

class CHLVoiceStatusHelper : public IVoiceStatusHelper
{
public:
	virtual void			GetPlayerTextColor( int entindex, int color[3] );
	virtual void			UpdateCursorState();
	virtual bool			CanShowSpeakerLabels();
};
