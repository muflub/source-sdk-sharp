//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the console, as a shim interface ===================//
//
// plan_engineapi_vtables.md step A4, the first EngineApi area to become a
// vtable: printing, ConCommand/ConVar registration and access, and the console
// snapshot. Acquired from IShimRoot as SOURCESHARP_SHIM_CONSOLE_VERSION. Both
// sides fill these ten slots, so this is a SHARED base: the client implements
// it too, and serves it once its own wave moves it off the struct.
//
// Rules as ishim_frame.h: no virtual destructor, blittable arguments,
// APPEND-ONLY. The count is taken from the LAST member, ConsoleSnapshotEntry.
//
//=============================================================================//

#ifndef ISHIM_CONSOLE_H
#define ISHIM_CONSOLE_H
#pragma once

#include <stdint.h>
#include "tier0/platform.h"
#include "sourcesharp.h"

#define SOURCESHARP_SHIM_CONSOLE_VERSION "SourceSharpConsole001"

abstract_class IShimConsole
{
public:
	virtual void Msg( const char *pUtf8 ) = 0;
	virtual void Warning( const char *pUtf8 ) = 0;
	virtual int32_t RegisterConCommand( const char *pName, const char *pHelp, int32_t nFlags ) = 0;
	virtual int32_t RegisterConVar( const char *pName, const char *pDefaultValue, const char *pHelp,
	                                int32_t nFlags, int32_t bHasMin, float flMin,
	                                int32_t bHasMax, float flMax ) = 0;
	virtual void SetConVarValue( int32_t nId, const char *pValue ) = 0;
	virtual void GetConVarValue( int32_t nId, char *pOut, int32_t nOutSize ) = 0;
	virtual int32_t FindConVarValue( const char *pName, char *pOut, int32_t nOutSize ) = 0;
	virtual void SetNamedConVarValue( const char *pName, const char *pValue ) = 0;
	virtual void RevertNamedConVar( const char *pName ) = 0;
	virtual int32_t ConsoleSnapshot() = 0;
	virtual int32_t ConsoleSnapshotEntry( int32_t nIndex, SourceSharpConsoleEntry *pOut ) = 0;
};

#endif // ISHIM_CONSOLE_H
