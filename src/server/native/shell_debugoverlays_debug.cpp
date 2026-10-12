//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_debugoverlays.cpp's diagnostic console command ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
//=============================================================================//

#include "ss_util.h"            // UTIL_IsCommandIssuedByServerAdmin
#include "overlay_library.h"
#include "native_command_body.h"

//-----------------------------------------------------------------------------
// Purpose: Allow all debug overlays to be cleared at once
//
// ndebugoverlay.cpp's, with its per-entity half already gone before 4c: the walk
// that zeroed every entity's m_debugOverlays went with the subsystem that read
// them — igameentity.h names shell_debugoverlays.cpp for that member and says the whole thing
// is CBaseEntity's own end to end. What is left is the engine's own overlay
// list, which is not about entities at all and still clears.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( clear_debug_overlays )
{
	if ( !UTIL_IsCommandIssuedByServerAdmin() )
		return;

	Overlay_ClearAll();
}
