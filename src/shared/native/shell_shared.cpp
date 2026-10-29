//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Symbols the game libraries expect every mod to supply, for both sides.

#ifdef CLIENT_DLL

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#else
#include "tier1/convar.h"

#endif
#ifdef CLIENT_DLL
#include "igamemovement.h"
#endif

#include "ss_string.h"
#include "ss_command_line.h"

// Default field of view. Native because view.cpp reads it through `extern ConVar`.
#ifdef CLIENT_DLL
ConVar default_fov("default_fov", "90", FCVAR_REPLICATED | FCVAR_CHEAT, "Default field of view.");
#endif

// The client's prediction bracket lends it to the movement servicing.
#ifdef CLIENT_DLL
static CMoveData g_MoveData;
CMoveData* g_pMoveData = &g_MoveData;
#endif
