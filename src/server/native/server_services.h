// The DLL tier's subsystems: built in DLLInit once the engine is bound, each handed
// what it calls, and gone in DLLShutdown with the tier.

#pragma once

#include "server_engine_interfaces.h"
#include "overlay_library.h"
#include "shell_debugoverlays.h"
#include "shell_frame.h"
#include "shell_gameclients.h"
#include "shell_gameents.h"
#include "shell_usermessage_send.h"

class CServerDllServices
{
public:
	CServerDllServices( const CServerEngineInterfaces &engine, CGlobalVars *pGlobals, const CManagedAreas &managed,
						CShellTransmitSet &transmitSet )
		: m_UserMessages( engine.m_pEngine, pGlobals )
		, m_OverlayLibrary( engine.m_pOverlay )
		, m_DebugOverlays( m_OverlayLibrary )
		, m_Frame( pGlobals, engine.m_pGameStats, engine.m_pMdlCache, m_DebugOverlays )
		, m_GameClients( engine.m_pEngine, pGlobals, engine.m_pMdlCache, managed )
		, m_GameEnts( engine.m_pEngine, engine.m_pMdlCache, managed, transmitSet )
	{
	}

	CServerDllServices( const CServerDllServices & ) = delete;
	CServerDllServices &operator=( const CServerDllServices & ) = delete;

	CServerUserMessages m_UserMessages;
	COverlayLibrary m_OverlayLibrary;
	CServerDebugOverlays m_DebugOverlays;
	CServerFrame m_Frame;
	CServerGameClients m_GameClients;
	CServerGameEnts m_GameEnts;
};
