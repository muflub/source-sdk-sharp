//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
// CSoundEmitterSystemRow: each stage forwards to managed code.

#include "tier0/dbg.h"

#include "shell_soundemitter_client.h"
#include "imanaged_areas_client.h"

CSoundEmitterSystemRow::CSoundEmitterSystemRow(const CManagedAreasClient& areas)
	: m_Areas(areas)
{
}

char const* CSoundEmitterSystemRow::Name()
{
	return "CSoundEmitterSystem";
}

bool CSoundEmitterSystemRow::Init()
{
	IManagedSoundEmitterClient* pApi = m_Areas.m_pSoundEmitterClient;
	if (!pApi)
	{
		Warning("[SourceSharp] CSoundEmitterSystem: no IManagedSoundEmitterClient, so the sound "
				"scripts cannot load\n");
		return false;
	}

	return pApi->ClientSoundSystemInit();
}

void CSoundEmitterSystemRow::Shutdown()
{
	if (IManagedSoundEmitterClient* pApi = m_Areas.m_pSoundEmitterClient)
		pApi->ClientSoundSystemShutdown();
}

void CSoundEmitterSystemRow::LevelInitPreEntity()
{
	if (IManagedSoundEmitterClient* pApi = m_Areas.m_pSoundEmitterClient)
		pApi->ClientSoundLevelInitPreEntity();
}

void CSoundEmitterSystemRow::LevelShutdownPostEntity()
{
	if (IManagedSoundEmitterClient* pApi = m_Areas.m_pSoundEmitterClient)
		pApi->ClientSoundLevelShutdownPostEntity();
}
