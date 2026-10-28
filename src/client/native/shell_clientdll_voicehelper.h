//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- IVoiceStatusHelper ================================//
//
// plan_entity.md Phase 5, the 5b split. ONE CLASS, ONE FILE.
//
// The voice manager asks the game which colour to draw a speaking player's
// name in, whether speaker labels may be shown, and to re-evaluate the cursor
// when the voice UI changes. Three questions, three answers, and none of them
// is about IBaseClientDLL.
//
// THE INSTANCE MOVED, WHICH IS THE POINT OF THE HEADER. Stock kept a
// `static CHLVoiceStatusHelper g_VoiceStatusHelper;` at file scope and handed
// its address to GetClientVoiceMgr()->Init(). plan.md 1a: nothing is a static
// or a global. It is a MEMBER of CShellClientDLL now
// (CShellClientDLL::m_VoiceStatusHelper) and Init passes &m_VoiceStatusHelper,
// so there is exactly one, it is owned, and its lifetime is the client DLL's
// -- which is what the file-scope static was approximating. The class has to
// be declared where CShellClientDLL is compiled, hence this header.
//
//===========================================================================//
#pragma once

#include "voice_status.h"

//-----------------------------------------------------------------------------
// Helper interface for voice.
//-----------------------------------------------------------------------------
class CHLVoiceStatusHelper : public IVoiceStatusHelper
{
public:
	virtual void			GetPlayerTextColor( int entindex, int color[3] );
	virtual void			UpdateCursorState();
	virtual bool			CanShowSpeakerLabels();
};
