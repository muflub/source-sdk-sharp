//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimConsole's implementation. The one object is a member of the shim root.

#pragma once

#include "ishim_console.h"
#include "bound_callback.h"
#include "console_registry.h"

#include <vector>

class CServerDllServices;
class CSourceSharpCommand;
class CSourceSharpConVar;
class ConCommandBase;
class IConVar;

// The managed area C#-declared commands and convar changes are handed to, per side.
#ifdef CLIENT_DLL
class IManagedCommandsClient;
typedef IManagedCommandsClient ManagedCommandsArea_t;
#else
class IManagedCommands;
typedef IManagedCommands ManagedCommandsArea_t;
#endif

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

	// Where this side holds its managed commands area; the owner points it at its holders.
	// A NULL area means the managed side is not loaded.
	ManagedCommandsArea_t *const *m_ppCommands = nullptr;

	ManagedCommandsArea_t *Commands() const { return m_ppCommands ? *m_ppCommands : nullptr; }

	// Installs the global convar change callback once, when g_pCVar is live: on the first
	// registration, or a client watch before any.
	void EnsureChangeCallback();

	// Deletes the C#-declared commands and convars and takes the change callback out of the
	// engine. Only after ConVar_Unregister, or the engine's ICvar would point at freed memory.
	void ReleaseObjects();

private:
	// Installed once per DLL as a global callback, matching by name: tier1 makes a second
	// registration of a name a child of the first and moves or drops its per-convar callback.
	void OnConVarChanged( IConVar *pVar, const char *pOldValue, float flOldValue );

	typedef CBoundCallback<void( IConVar *, const char *, float )> ChangeCallback_t;

	CConsoleRegistry<CSourceSharpCommand> m_Commands;
	CConsoleRegistry<CSourceSharpConVar> m_ConVars;

	// A snapshot of the engine's console registry, copied out entry by entry in one frame.
	std::vector<ConCommandBase *> m_Snapshot;

	ChangeCallback_t m_ChangeCallback{ this,
									   &ChangeCallback_t::Trampoline<CShimConsole, &CShimConsole::OnConVarChanged> };
	bool m_bChangeCallbackInstalled = false;
};

#ifdef CLIENT_DLL
// The client's console, owned by its shim root (shell_internalfactory.cpp).
CShimConsole &ShimConsole();
#endif

// The internal factory's row for SOURCESHARP_SHIM_CONSOLE_VERSION.
void *ShimConsole_Acquire();
