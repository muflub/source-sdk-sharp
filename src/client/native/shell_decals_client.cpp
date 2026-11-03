//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through

// CShellDecalSystemClient: the client's IDecalEmitterSystem over managed decals.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "shell_decals_client.h"
#include "imanaged_areas_client.h"

CShellDecalSystemClient::CShellDecalSystemClient(const CManagedAreasClient& areas)
	: CClientSystem("CDecalEmitterSystem")
	, m_Areas(areas)
{
	m_szName[0] = '\0';
	m_szTranslated[0] = '\0';
}

bool CShellDecalSystemClient::Init()
{
	if (IManagedDecalsClient* pArea = m_Areas.m_pDecalsClient)
		pArea->Init();
	return true;
}

void CShellDecalSystemClient::Shutdown()
{
	if (IManagedDecalsClient* pArea = m_Areas.m_pDecalsClient)
		pArea->Shutdown();
}

void CShellDecalSystemClient::LevelInitPreEntity()
{
	if (IManagedDecalsClient* pArea = m_Areas.m_pDecalsClient)
		pArea->LevelInitPreEntity();
}

int CShellDecalSystemClient::GetDecalIndexForName(char const* decalname)
{
	IManagedDecalsClient* pArea = m_Areas.m_pDecalsClient;
	if (!pArea || !decalname || !decalname[0])
		return -1;
	return pArea->IndexForName(decalname);
}

const char* CShellDecalSystemClient::GetDecalNameForIndex(int nIndex)
{
	m_szName[0] = '\0';
	IManagedDecalsClient* pArea = m_Areas.m_pDecalsClient;
	if (pArea && pArea->NameForIndex(nIndex, m_szName, sizeof(m_szName)) < 0)
		m_szName[0] = '\0';
	return m_szName;
}

char const* CShellDecalSystemClient::TranslateDecalForGameMaterial(char const* decalName, unsigned char gamematerial)
{
	IManagedDecalsClient* pArea = m_Areas.m_pDecalsClient;
	if (!pArea || !decalName)
		return decalName;
	if (pArea->TranslateForGameMaterial(decalName, (int32_t)gamematerial, m_szTranslated, sizeof(m_szTranslated)) < 0)
		return decalName;
	return m_szTranslated;
}

// The client DLL object's member; set from its Init.
