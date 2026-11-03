//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A client systems row whose system is managed: each stage goes to the managed system of
// the same name. Init's 0 refuses client start-up, as a native row's false does; with no
// managed side or an unknown row nothing refuses. A member of the client systems table.

#pragma once

#include <stdint.h>

#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class CManagedClientSystemRow final : public CClientSystem
{
public:
	CManagedClientSystemRow(const CManagedAreasClient& areas, const char* pszRow)
		: CClientSystem(pszRow)
		, m_Areas(areas)
		, m_pszRow(pszRow)
	{
	}

	bool Init() override;
	void PostInit() override;
	void Shutdown() override;
	void LevelInitPreEntity() override;
	void LevelInitPostEntity() override;
	void LevelShutdownPreEntity() override;
	void LevelShutdownPostEntity() override;

private:
	// Managed's answer; -1 when there is no managed load or no system of the name.
	int32_t Stage(int32_t nStage);

	const CManagedAreasClient& m_Areas;
	const char* m_pszRow;
	bool m_bWarned = false;
};
