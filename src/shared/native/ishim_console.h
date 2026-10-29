//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The console as a shim interface: printing, ConCommand/ConVar registration and
// access, and the console snapshot. Implemented by both sides. No virtual
// destructor, blittable arguments, append-only: the slot order is an ABI.

#pragma once

#include <stdint.h>
#include "tier0/platform.h"
#include "sourcesharp.h"

#define SOURCESHARP_SHIM_CONSOLE_VERSION "SourceSharpConsole001"

abstract_class IShimConsole
{
public:
	virtual void Msg(const char* pUtf8) = 0;
	virtual void Warning(const char* pUtf8) = 0;
	virtual int32_t RegisterConCommand(const char* pName, const char* pHelp, int32_t nFlags) = 0;
	virtual int32_t RegisterConVar(const char* pName, const char* pDefaultValue, const char* pHelp, int32_t nFlags,
								   int32_t bHasMin, float flMin, int32_t bHasMax, float flMax) = 0;
	virtual void SetConVarValue(int32_t nId, const char* pValue) = 0;
	virtual void GetConVarValue(int32_t nId, char* pOut, int32_t nOutSize) = 0;
	virtual int32_t FindConVarValue(const char* pName, char* pOut, int32_t nOutSize) = 0;
	virtual void SetNamedConVarValue(const char* pName, const char* pValue) = 0;
	virtual void RevertNamedConVar(const char* pName) = 0;
	virtual int32_t ConsoleSnapshot() = 0;
	virtual int32_t ConsoleSnapshotEntry(int32_t nIndex, SourceSharpConsoleEntry* pOut) = 0;

	// The flags of the engine's console variable pName, or -1 when there is none.
	virtual int32_t FindConVarFlags(const char* pName) = 0;
};
