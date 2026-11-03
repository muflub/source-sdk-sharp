//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// C_SoundscapeSystem holds the stock system's slot in the client-systems walk so its
// stages run when stock's did; the system itself is managed. Scripts must load at
// LevelInitPreEntity: the server's first push arrives inside ClientActive, before managed
// level init. A member of the client systems table.

#pragma once

#include "shell_clientsystem.h"
#include "shell_managed_areas_client.h"

class C_SoundscapeSystem final : public CClientSystemPerFrame
{
public:
	explicit C_SoundscapeSystem(const CManagedAreasClient& areas)
		: CClientSystemPerFrame("C_SoundScapeSystem")
		, m_Areas(areas)
	{
	}

	void LevelInitPreEntity() override;
	void LevelShutdownPostEntity() override;
	void Update(float frametime) override;

private:
	const CManagedAreasClient& m_Areas;
};
