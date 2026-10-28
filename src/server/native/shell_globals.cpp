//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets
//
// Small native support definitions the engine boundary needs: the ServerClass list head
// and g_bUseNetworkVars.

#include "server_class.h"

// The list every ServerClass adds itself to from its static constructor, walked by the
// engine. Constant-initialised, so it is NULL before any of those constructors run.
ServerClass *g_pServerClassHead = 0;

// Read by the _DEBUG network-var assertions.

bool g_bUseNetworkVars = true;
