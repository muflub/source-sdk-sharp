//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The server's shim root: a by-value member of the game DLL object, handed to managed at
// load. Acquire serves this side's shim interfaces and forwarder tables by name.

#pragma once

#include "ishim_root.h"
#include "shell_shim_console.h"
#include "shell_shim_areas.h"
#include "ishim_frame.h"
#include "ishim_level.h"
#include "ss_init_factory.h"

class CGlobalVars;

class CShimRootServer final : public IShimRoot
{
public:
	// NULL for a name this side does not serve. Defined in shell_internalfactory.cpp.
	void *Acquire( const char *pszName ) override;

	// The engine's interfaces by factory kind; this side's own (INTERNAL) from Acquire.
	void *AcquireInterface( int32_t nType, const char *pszVersion, char *pError, int32_t nErrorBytes ) override;

	// The root lives as long as the DLL; this does nothing.
	void Release() override {}

	// The native-side shim areas. What native holds from managed is the game DLL's m_Managed.
	CShimConsole m_Console;
	CShimAreas m_Areas;

	// The frame area, the DLL tier's (CServerFrame); NULL outside DLLInit..DLLShutdown.
	IShimFrame *m_pFrame = nullptr;

	// The level area, the DLL tier's (CServerLevel); NULL outside DLLInit..DLLShutdown.
	IShimLevel *m_pLevel = nullptr;

	// The engine's factories, the DLL tier's; NULL outside DLLInit..DLLShutdown.
	const ss::factorylist_t *m_pFactories = nullptr;

	// The engine's globals, from DLLInit to DLLShutdown.
	CGlobalVars *m_pGlobals = nullptr;
};

// The game DLL's shim root. Defined beside the root in shell_gamedll.cpp.
CShimRootServer &ShellGameDll_ShimRoot();
