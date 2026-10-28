// The DLL tier's subsystems: built in DLLInit once the engine is bound, each handed
// what it calls, and gone in DLLShutdown with the tier.

#pragma once

#include "server_engine_interfaces.h"
#include "server_entity_context.h"
#include "shell_managed_areas.h"
#include "overlay_library.h"
#include "shell_debugoverlays.h"
#include "shell_frame.h"
#include "shell_gameclients.h"
#include "shell_gameents.h"
#include "shell_level.h"
#include "shell_usermessage_send.h"

class CServerDllServices
{
public:
	CServerDllServices( const CServerEngineInterfaces &engine, CGlobalVars *pGlobals, const CManagedHolders &managed,
						CShellTransmitSet &transmitSet, CServerPlayersDll &players, unsigned short &nWorldSerial )
		: m_EntityContext( engine.m_pEngine, engine.m_pPartition, engine.m_pModelInfo, managed, players, nWorldSerial )
		, m_UserMessages( engine.m_pEngine, pGlobals )
		, m_OverlayLibrary( engine.m_pOverlay )
		, m_DebugOverlays( m_OverlayLibrary )
		, m_Frame( pGlobals, engine.m_pGameStats, engine.m_pMdlCache, m_DebugOverlays, m_EntityContext )
		, m_GameClients( engine.m_pEngine, pGlobals, engine.m_pMdlCache, managed.m_Areas, m_EntityContext )
		, m_GameEnts( engine.m_pEngine, engine.m_pMdlCache, managed.m_Areas, transmitSet )
		, m_Level( engine.m_pEngine, engine.m_pBaseFileSystem, engine.m_pMdlCache, pGlobals, m_EntityContext )
	{
	}

	CServerDllServices( const CServerDllServices & ) = delete;
	CServerDllServices &operator=( const CServerDllServices & ) = delete;

	CServerEntityContext m_EntityContext;
	CServerUserMessages m_UserMessages;
	COverlayLibrary m_OverlayLibrary;
	CServerDebugOverlays m_DebugOverlays;
	CServerFrame m_Frame;
	CServerGameClients m_GameClients;
	CServerGameEnts m_GameEnts;
	CServerLevel m_Level;
};
