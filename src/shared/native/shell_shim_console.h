//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — IShimConsole's implementation ======================//
//
// plan_engineapi_vtables.md step A4. The methods are the bodies the ten
// console slots of SourceSharpEngineApi pointed at, renamed in place in
// sourcesharp_gamesystem.cpp; the struct still reaches them (through
// ShimConsole()) for the client, which reads the struct until its own wave.
//
// NOT A GLOBAL OF ITS OWN. The one object is a member of the shim root
// (shell_internalfactory.cpp), so each area that moves off the struct adds a
// member to one static rather than a static per area.
//
//=============================================================================//

#ifndef SHELL_SHIM_CONSOLE_H
#define SHELL_SHIM_CONSOLE_H
#pragma once

#include "ishim_console.h"

class CShimConsole final : public IShimConsole
{
public:
	void Msg( const char *pUtf8 ) override;
	void Warning( const char *pUtf8 ) override;
	int32_t RegisterConCommand( const char *pName, const char *pHelp, int32_t nFlags ) override;
	int32_t RegisterConVar( const char *pName, const char *pDefaultValue, const char *pHelp,
	                        int32_t nFlags, int32_t bHasMin, float flMin,
	                        int32_t bHasMax, float flMax ) override;
	void SetConVarValue( int32_t nId, const char *pValue ) override;
	void GetConVarValue( int32_t nId, char *pOut, int32_t nOutSize ) override;
	int32_t FindConVarValue( const char *pName, char *pOut, int32_t nOutSize ) override;
	void SetNamedConVarValue( const char *pName, const char *pValue ) override;
	void RevertNamedConVar( const char *pName ) override;
	int32_t ConsoleSnapshot() override;
	int32_t ConsoleSnapshotEntry( int32_t nIndex, SourceSharpConsoleEntry *pOut ) override;
};

#ifdef CLIENT_DLL
// The client's console, owned by its shim root. Defined in shell_internalfactory.cpp;
// the server reaches it only through the root's vtable (server gc cull).
CShimConsole &ShimConsole();
#endif

// The internal factory's row for SOURCESHARP_SHIM_CONSOLE_VERSION.
void *ShimConsole_Acquire( int32_t *pnEntries );

#endif // SHELL_SHIM_CONSOLE_H
