//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// The stand-in client world the PRISTINE game/client/vgui_game_viewport.cpp
// is compiled into for viewport_oracle_test.
//
// STOCK: the subject and game/shared/igamesystem.h (copied beside it).
//
// STAND-IN: cbase.h and clientmode.h are this header (viewport_stub/). The
// subject reaches one thing through them, g_pClientMode->Layout(); the client
// mode here is a class with that one method, which the test records.
//
//=============================================================================

#pragma once

#include "tier0/platform.h"
#include "igamesystem.h"

class IClientMode
{
public:
	virtual void Layout();
};

extern IClientMode *g_pClientMode;
