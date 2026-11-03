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
#include "shell_client_engine_interfaces.h"
#include "vgui/ISurface.h"
#include "vgui/ISurface.h"
#include "shell_shim_console.h"

namespace
{
const int kNameBytes = 64;

// The decided values: a name and its float, set by managed.
} // namespace

int32_t CConVarsClient::ConVarsClientWatch(const char* pName)
{
	return m_Console.WatchConVar(pName);
}

int32_t CConVarsClient::ConVarsClientSetValue(const char* pName, float flValue)
{
	if (!pName || !pName[0] || V_strlen(pName) >= kNameBytes)
		return 0;

	for (int i = 0; i < m_nDecided; i++)
	{
		if (!V_stricmp(m_Decided[i].szName, pName))
		{
			m_Decided[i].flValue = flValue;
			return 1;
		}
	}

	if (m_nDecided >= (int)ARRAYSIZE(m_Decided))
		return 0;

	V_strncpy(m_Decided[m_nDecided].szName, pName, kNameBytes);
	m_Decided[m_nDecided].flValue = flValue;
	m_nDecided++;
	return 1;
}

bool CConVarsClient::DecidedValue(const char* pName, float* pOut) const
{
	for (int i = 0; i < m_nDecided; i++)
	{
		if (!V_stricmp(m_Decided[i].szName, pName))
		{
			*pOut = m_Decided[i].flValue;
			return true;
		}
	}
	return false;
}

float CConVarsClient::ClientInterpAmount() const
{
	float flDecided;
	return DecidedValue("GetClientInterpAmount", &flDecided) ? flDecided : 0.1f;
}

CSoftwareCursorConVar::CSoftwareCursorConVar(const CClientEngineInterfaces& engines)
	: ConVar("cl_software_cursor", "0", FCVAR_ARCHIVE,
			 "Switches the game to use a larger software cursor instead of the normal OS cursor", Changed)
	, m_Engines(engines)
{
}

void CSoftwareCursorConVar::Changed(IConVar* pVar, const char* pOldValue, float flOldValue)
{
	const CSoftwareCursorConVar* pThis = static_cast<CSoftwareCursorConVar*>(static_cast<ConVar*>(pVar));
	pThis->m_Engines.m_pVGuiSurface->SetSoftwareCursor(pThis->GetBool());
}
