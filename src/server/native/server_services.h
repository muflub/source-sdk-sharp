// The DLL tier's subsystems: built in DLLInit once the engine is bound, each handed
// the engine interfaces it calls, and gone in DLLShutdown with the tier.

#pragma once

#include "shell_usermessage_send.h"

class CServerDllServices
{
public:
	CServerDllServices( IVEngineServer *pEngine, const CGlobalVars *pGlobals )
		: m_UserMessages( pEngine, pGlobals )
	{
	}

	CServerDllServices( const CServerDllServices & ) = delete;
	CServerDllServices &operator=( const CServerDllServices & ) = delete;

	CServerUserMessages m_UserMessages;
};
