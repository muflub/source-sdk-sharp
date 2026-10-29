//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load

// Server-side symbols server_base.vpc expects a mod to supply; game decisions
// route into C#.

#include "shell_gamerules.h"
#include "shell_standalone_player.h"
#include "shell_diagnostics.h"

#include "shell_gameclients.h"

#include "shell_server.h"

#include "ss_shareddefs.h"
