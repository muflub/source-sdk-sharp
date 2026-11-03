//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The sound emitter system's client systems row: each stage forwards to managed code. It
// stays second, before anything asks the table for a script. Init fails when the scripts
// will not load or managed code is absent. A member of the client systems table.

#pragma once

#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class CSoundEmitterSystemRow final : public CClientSystem
{
public:
	explicit CSoundEmitterSystemRow(const CManagedAreasClient& areas);

	char const* Name() override;
	bool Init() override;
	void Shutdown() override;
	void LevelInitPreEntity() override;
	void LevelShutdownPostEntity() override;

private:
	const CManagedAreasClient& m_Areas;
};
