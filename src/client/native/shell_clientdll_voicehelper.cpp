//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "shell_clientdll_voicehelper.h"

void CHLVoiceStatusHelper::GetPlayerTextColor(int entindex, int color[3])
{
	color[0] = color[1] = color[2] = 128;
}

void CHLVoiceStatusHelper::UpdateCursorState() {}

bool CHLVoiceStatusHelper::CanShowSpeakerLabels()
{
	return true;
}
