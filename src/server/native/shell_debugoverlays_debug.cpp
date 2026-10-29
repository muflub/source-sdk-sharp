//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
//
// shell_debugoverlays.cpp's diagnostic console command. Debug builds only; managed
// NativeCommands declares the matching row under #if DEBUG.

#include "ss_util.h"
#include "server_services.h"
#include "native_command_body.h"

// Allow all debug overlays to be cleared at once (the engine's overlay list).
SS_NATIVE_COMMAND( clear_debug_overlays )
{
	if ( !services.m_EntityContext.CommandIssuedByServerAdmin() )
		return;

	services.m_OverlayLibrary.ClearAll();
}
