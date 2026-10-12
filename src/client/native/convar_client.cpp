//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the console carriers only client.so builds =========//
// SS_REASON a  a vtable the engine calls through
//
// convar_port.cpp (src/shared/native) holds tier1 convar.cpp's members both
// DLLs use. These are the constructors only client objects call -- the fork
// TUs' `static ConVar x( name, default, flags, help[, callback] )` and
// CON_COMMANDs, and particles.a's ConVars -- which the server's gc cull took
// out of the port (the server declares its console in C#). What the server
// does not need is client-only (user ruling 2026-10-12).
//=============================================================================//

#include "tier1/convar.h"

namespace
{
int NoCompletions( const char *, char[ COMMAND_COMPLETION_MAXITEMS ][ COMMAND_COMPLETION_ITEM_LENGTH ] )
{
	return 0;
}
}

ConCommand::ConCommand( const char *pName, FnCommandCallbackVoid_t callback, const char *pHelpString, int flags,
                        FnCommandCompletionCallback completionFunc )
{
	m_fnCommandCallbackV1 = callback;
	m_bUsingNewCommandCallback = false;
	m_bUsingCommandCallbackInterface = false;
	m_bHasCompletionCallback = completionFunc != NULL;
	m_fnCompletionCallback = completionFunc ? completionFunc : NoCompletions;
	BaseClass::CreateBase( pName, pHelpString, flags );
}

ConCommand::ConCommand( const char *pName, FnCommandCallback_t callback, const char *pHelpString, int flags,
                        FnCommandCompletionCallback completionFunc )
{
	m_fnCommandCallback = callback;
	m_bUsingNewCommandCallback = true;
	m_bUsingCommandCallbackInterface = false;
	m_bHasCompletionCallback = completionFunc != NULL;
	m_fnCompletionCallback = completionFunc ? completionFunc : NoCompletions;
	BaseClass::CreateBase( pName, pHelpString, flags );
}

ConVar::ConVar( const char *pName, const char *pDefaultValue, int flags, const char *pHelpString )
{
	Create( pName, pDefaultValue, flags, pHelpString );
}

ConVar::ConVar( const char *pName, const char *pDefaultValue, int flags, const char *pHelpString,
                FnChangeCallback_t callback )
{
	Create( pName, pDefaultValue, flags, pHelpString, false, 0.0f, false, 0.0f, false, 0.0f, false, 0.0f, callback );
}

ConVarRef::ConVarRef( const char *pName, bool bIgnoreMissing )
{
	Init( pName, bIgnoreMissing );
}
