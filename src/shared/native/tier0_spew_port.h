//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — tier0_spew_port.cpp's one shared helper ============//
//
// A line at a developer level, written through this DLL's spew slot when
// `developer` is at least that level. DevMsg( level ) is it with SPEW_MESSAGE;
// the client's DevWarning( level ) (tier0_spew_client.cpp) with SPEW_WARNING.
//=============================================================================//

#pragma once

#include "tier0/dbg.h"

#include <stdarg.h>

void Tier0_DevSpewV( int level, SpewType_t type, const tchar *pMsg, va_list args );
