//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimConsoleServer: a command sent to one client's console, and the player whose
// command is running, as stock's engine->ClientCommand and UTIL_GetCommandClient answer
// them. One object, the DLL tier's; IShimConsole, its base, is the hand-written
// CShimConsole.

#pragma once

#include "ishim_areas.h"
#include "shell_shim_areas.h"

class CServerEntityResolver;
class IVEngineServer;

class CServerConsoleCommands final : public IShimConsoleServer
{
public:
	CServerConsoleCommands(CServerEntityResolver& entities, IVEngineServer* pEngine)
		: m_Entities(entities)
		, m_pEngine(pEngine)
	{
	}

	CServerConsoleCommands(const CServerConsoleCommands&) = delete;
	CServerConsoleCommands& operator=(const CServerConsoleCommands&) = delete;

	void SendClientCommand(uint32_t hPlayer, const char* pCommand) override;
	uint32_t GetCommandClientHandle() override;

private:
	CServerEntityResolver& m_Entities;
	IVEngineServer* m_pEngine;
};
