//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_movement.cpp's diagnostic console command ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
//=============================================================================//

#include "shell_gamemovement.h"   // SourceSharp_MoveManagedReport
#include "native_command_body.h"

//-----------------------------------------------------------------------------
// The server's half of cs_{sv,cl}_move_managed_report: how many commands the
// managed movement loop ran, counted in the crossing, and how many it could not
// run. The body is shell_movement.cpp's, shared with the client's
// cs_cl_move_managed_report, so the two sides print the same measurement.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_move_managed_report )
{
	SourceSharp_MoveManagedReport();
}
