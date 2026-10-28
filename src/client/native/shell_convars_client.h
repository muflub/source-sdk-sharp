//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the managed client's convar watch and decided values
//
// shell_convars_client.cpp says what these are (plan_client_novalve C8).
//=============================================================================//
#pragma once

#include <stdint.h>

// Watched convars' ids start here, clear of the managed registry's own ids.
#define SOURCESHARP_CONVAR_WATCH_ID_BASE 0x10000

// The id a watched name answers, or -1.
int32_t SourceSharp_ClientWatchedConVarId( const char *pName );

// The value managed decided under pName; false when it has decided none yet.
bool SourceSharp_ClientDecidedValue( const char *pName, float *pOut );

// Installs the client's global change callback if no registration has yet.
void SourceSharp_EnsureConVarChangeCallback();
