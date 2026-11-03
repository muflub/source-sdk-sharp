//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The temp entity diagnostics: cs_cl_te_backend and the pool's commands. A member of the
// client DLL object; each body is in the file of what it exercises.

#pragma once

#include "shell_member_command.h"

class CClientEngineInterfaces;
class CStandaloneClients;
class CTempEntBackendsClient;
class CTempEnts;
class CTempEntsSystemClient;
class CViewCameras;

class CTempEntCommandsClient final
{
public:
	CTempEntCommandsClient(const CClientEngineInterfaces& engines, CTempEntBackendsClient& backends,
						   const CViewCameras& cameras, CTempEnts& pool, CTempEntsSystemClient& system,
						   CStandaloneClients& standalone)
		: m_Engines(engines)
		, m_Backends(backends)
		, m_Cameras(cameras)
		, m_Pool(pool)
		, m_System(system)
		, m_Standalone(standalone)
	{
	}

private:
	void Backend(const CCommand& args);
	void PoolShell(const CCommand& args);
	void PoolAttach(const CCommand& args);
	void PoolLive(const CCommand& args);

	const CClientEngineInterfaces& m_Engines;
	CTempEntBackendsClient& m_Backends;
	const CViewCameras& m_Cameras;
	CTempEnts& m_Pool;
	CTempEntsSystemClient& m_System;
	CStandaloneClients& m_Standalone;

	CMemberCommand<CTempEntCommandsClient, &CTempEntCommandsClient::Backend> m_BackendCommand{
		*this, "cs_cl_te_backend", "TEMPORARY (C-Phase 2): fire one temp entity backend in front of the camera.",
		FCVAR_CLIENTDLL};
	CMemberCommand<CTempEntCommandsClient, &CTempEntCommandsClient::PoolShell> m_PoolShellCommand{
		*this, "cs_cl_te_pool_shell",
		"Eject brass through CTempEnts::EjectBrass -- the legacy temp entity pool's own entry "
		"point, for verifying the pool end to end. cs_cl_te_pool_shell [count] [type 0|1|2]",
		FCVAR_CHEAT};
	CMemberCommand<CTempEntCommandsClient, &CTempEntCommandsClient::PoolAttach> m_PoolAttachCommand{
		*this, "cs_cl_te_pool_attach",
		"Attach high-priority, FTENT_PLYRATTACHMENT tempents to a player through "
		"CTempEnts::AttachTentToPlayer -- what CTEKillPlayerAttachments kills. "
		"cs_cl_te_pool_attach [count] [player]",
		FCVAR_CHEAT};
	CMemberCommand<CTempEntCommandsClient, &CTempEntCommandsClient::PoolLive> m_PoolLiveCommand{
		*this, "cs_cl_te_live",
		"Every live tempent's POSITION, its attachment client, its owner index and whether "
		"that owner still resolves -- the geometry a pool counter cannot show. "
		"cs_cl_te_live [entity], where the entity prints what an owner index resolves to "
		"through each of the two lookups. cs_cl_te_live bones counts the bone matrices the "
		"live studio tempents leave unwritten when asked as the engine's draw asks (0 is right).",
		FCVAR_CHEAT};
};
