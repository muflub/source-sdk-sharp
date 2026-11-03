//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON c  static-init registration
// CManagedClientSystemRow: each stage goes to the managed system of the row's name.

#include "tier0/dbg.h"

#include "shell_managed_clientsystems_client.h"
#include "imanaged_areas_client.h"

// Must match ClientSystemStage in C#.
enum ManagedClientSystemStage_t
{
	SS_CLIENT_SYSTEM_STAGE_Init = 0,
	SS_CLIENT_SYSTEM_STAGE_PostInit = 1,
	SS_CLIENT_SYSTEM_STAGE_Shutdown = 2,
	SS_CLIENT_SYSTEM_STAGE_LevelInitPreEntity = 3,
	SS_CLIENT_SYSTEM_STAGE_LevelInitPostEntity = 4,
	SS_CLIENT_SYSTEM_STAGE_LevelShutdownPreEntity = 5,
	SS_CLIENT_SYSTEM_STAGE_LevelShutdownPostEntity = 6,
};

bool CManagedClientSystemRow::Init()
{
	return Stage(SS_CLIENT_SYSTEM_STAGE_Init) != 0;
}

void CManagedClientSystemRow::PostInit()
{
	Stage(SS_CLIENT_SYSTEM_STAGE_PostInit);
}

void CManagedClientSystemRow::Shutdown()
{
	Stage(SS_CLIENT_SYSTEM_STAGE_Shutdown);
}

void CManagedClientSystemRow::LevelInitPreEntity()
{
	Stage(SS_CLIENT_SYSTEM_STAGE_LevelInitPreEntity);
}

void CManagedClientSystemRow::LevelInitPostEntity()
{
	Stage(SS_CLIENT_SYSTEM_STAGE_LevelInitPostEntity);
}

void CManagedClientSystemRow::LevelShutdownPreEntity()
{
	Stage(SS_CLIENT_SYSTEM_STAGE_LevelShutdownPreEntity);
}

void CManagedClientSystemRow::LevelShutdownPostEntity()
{
	Stage(SS_CLIENT_SYSTEM_STAGE_LevelShutdownPostEntity);
}

int32_t CManagedClientSystemRow::Stage(int32_t nStage)
{
	IManagedClientSystemsClient* pManaged = m_Areas.m_pClientSystemsClient;
	if (!pManaged)
		return -1;

	const int32_t nAnswer = pManaged->Stage(m_pszRow, nStage);
	if (nAnswer < 0 && !m_bWarned)
	{
		m_bWarned = true;
		Warning("[SourceSharp] client system row %s: managed has no system of that name\n", m_pszRow);
	}
	return nAnswer;
}
