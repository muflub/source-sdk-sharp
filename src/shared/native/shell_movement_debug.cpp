//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds only.

#include "shell_gamemovement.h"
#include "native_command_body.h"

// Prints how many commands the managed movement loop ran and how many it could not.
SS_NATIVE_COMMAND( cs_sv_move_managed_report )
{
	SourceSharp_MoveManagedReport();
}
