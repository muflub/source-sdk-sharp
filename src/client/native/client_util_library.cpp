//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Client utility functions: screen size, the engine's text messages and VarArgs.
// SS_REASON h  the CoreCLR host and bootstrap dispatch

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "hud.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"

#include "client_entity.h"

#include "managed_convar_ref.h"

int ScreenHeight(const CClientEngineInterfaces& engines)
{
	int w, h;
	GetHudSize(engines, w, h);
	return h;
}

int ScreenWidth(const CClientEngineInterfaces& engines)
{
	int w, h;
	GetHudSize(engines, w, h);
	return w;
}
