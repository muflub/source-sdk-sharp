//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- IVoiceStatusHelper ================================//
// SS_REASON a  a vtable the engine calls through
//
// plan_entity.md Phase 5, the 5b split. The bodies behind
// shell_clientdll_voicehelper.h; see that header for why the instance is a
// member of CShellClientDLL rather than the file-scope static stock had.
//
//===========================================================================//
#include "cbase.h"
#include "shell_clientdll_voicehelper.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

void CHLVoiceStatusHelper::GetPlayerTextColor( int entindex, int color[3] )
{
	color[0] = color[1] = color[2] = 128;
}

void CHLVoiceStatusHelper::UpdateCursorState()
{
}

bool CHLVoiceStatusHelper::CanShowSpeakerLabels()
{
	return true;
}
