//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The managed client's convar watch and decided values.
#pragma once

// Watched convars' ids start here, clear of the managed registry's own ids.
#define SOURCESHARP_CONVAR_WATCH_ID_BASE 0x10000

#include "ishim_areas_client.h"

class CShimConsole;

// IShimConVarsClient: the watch is the console's, which owns the change callback.
class CConVarsClient final : public IShimConVarsClient
{
public:
	explicit CConVarsClient(CShimConsole& console)
		: m_Console(console)
	{
	}

	int32_t ConVarsClientWatch(const char* pName) override;
	int32_t ConVarsClientSetValue(const char* pName, float flValue) override;

private:
	CShimConsole& m_Console;
};

// Stores the value managed decided under pName; 0 when the table has no room for it.
int32_t SourceSharp_ClientSetDecidedValue(const char* pName, float flValue);

// The value managed decided under pName; false when it has decided none yet.
bool SourceSharp_ClientDecidedValue(const char* pName, float* pOut);
