//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON b  an interface resolved by name at load
//
// CConVarsClient: IShimConVarsClient's two slots.
//
// Watch: tier1 calls every installed global change callback for every convar,
// so a watched name is forwarded to managed's ConVarChanged under the id the
// console answered.
//
// Values: floats managed decided, stored under names native code reads without
// crossing.

#include "icvar.h"

#include "shell_convars_client.h"
#include "shell_shim_console.h"

namespace
{
const int kNameBytes = 64;

// The decided values: a name and its float, set by managed.
struct DecidedValue_t
{
	char szName[kNameBytes];
	float flValue;
};

const int kMaxDecided = 8;
DecidedValue_t g_Decided[kMaxDecided];
int g_nDecided = 0;
} // namespace

int32_t CConVarsClient::ConVarsClientWatch(const char* pName)
{
	return m_Console.WatchConVar(pName);
}

int32_t CConVarsClient::ConVarsClientSetValue(const char* pName, float flValue)
{
	return SourceSharp_ClientSetDecidedValue(pName, flValue);
}

int32_t SourceSharp_ClientSetDecidedValue(const char* pName, float flValue)
{
	if (!pName || !pName[0] || V_strlen(pName) >= kNameBytes)
		return 0;

	for (int i = 0; i < g_nDecided; i++)
	{
		if (!V_stricmp(g_Decided[i].szName, pName))
		{
			g_Decided[i].flValue = flValue;
			return 1;
		}
	}

	if (g_nDecided >= kMaxDecided)
		return 0;

	V_strncpy(g_Decided[g_nDecided].szName, pName, kNameBytes);
	g_Decided[g_nDecided].flValue = flValue;
	g_nDecided++;
	return 1;
}

bool SourceSharp_ClientDecidedValue(const char* pName, float* pOut)
{
	for (int i = 0; i < g_nDecided; i++)
	{
		if (!V_stricmp(g_Decided[i].szName, pName))
		{
			*pOut = g_Decided[i].flValue;
			return true;
		}
	}
	return false;
}
