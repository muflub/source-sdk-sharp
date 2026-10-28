//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimConsole's implementation. The one object is a member of the shim root.

#pragma once

#include "ishim_console.h"

class CServerDllServices;

class CShimConsole final : public IShimConsole
{
public:
	void Msg( const char *pUtf8 ) override;
	void Warning( const char *pUtf8 ) override;
	int32_t RegisterConCommand( const char *pName, const char *pHelp, int32_t nFlags ) override;
	int32_t RegisterConVar( const char *pName, const char *pDefaultValue, const char *pHelp, int32_t nFlags,
							int32_t bHasMin, float flMin, int32_t bHasMax, float flMax ) override;
	void SetConVarValue( int32_t nId, const char *pValue ) override;
	void GetConVarValue( int32_t nId, char *pOut, int32_t nOutSize ) override;
	int32_t FindConVarValue( const char *pName, char *pOut, int32_t nOutSize ) override;
	void SetNamedConVarValue( const char *pName, const char *pValue ) override;
	void RevertNamedConVar( const char *pName ) override;
	int32_t ConsoleSnapshot() override;
	int32_t ConsoleSnapshotEntry( int32_t nIndex, SourceSharpConsoleEntry *pOut ) override;
	int32_t FindConVarFlags( const char *pName ) override;

	// The server's DLL tier services, handed to native command bodies; set by the game DLL
	// for DLLInit..DLLShutdown. NULL on the client, which defines no bodies.
	CServerDllServices *m_pServices = nullptr;
};

#ifdef CLIENT_DLL
// The client's console, owned by its shim root (shell_internalfactory.cpp).
CShimConsole &ShimConsole();
#endif

// The internal factory's row for SOURCESHARP_SHIM_CONSOLE_VERSION.
void *ShimConsole_Acquire();
