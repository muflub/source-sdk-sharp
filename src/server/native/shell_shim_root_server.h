//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the server's shim root, a member of the game DLL ===//
//
// plan_globals.md G2 ("IShimRoot::Acquire as the bootstrap pair"), as it lands
// in the forwarder-table world (plan_forwarders_everywhere.md,
// plan_server_native_v2 T6).
//
// THE BOOTSTRAP PAIR ALREADY EXISTED. Since plan_engineapi_vtables A3 and the
// forwarder tables, LoadSideV2 hands managed exactly one native object, the
// IShimRoot, beside its forwarder table (SSFwd_ShimRoot), and managed reaches
// every other area and table through IShimRoot::Acquire. What T6 changes is
// who the root is and where Acquire's answer comes from:
//
//   * the root was a file-scope static (g_ShimRoot, shell_internalfactory.cpp);
//     on the server it is now a by-value member of the game DLL object, the one
//     static (s_gameShellDll). It stays by value, not in m_pDll, because managed
//     is handed it at load and it is stateless beyond the areas it owns.
//   * Acquire was a forward to the free SourceSharp_InternalFactory, which found
//     the areas through ShimAreasOfRoot() -- a global accessor. On the server
//     the factory's body IS Acquire now, and it serves the console and the
//     generated areas from `this`. SourceSharp_InternalFactory is the client's
//     alone (its root is unchanged: the client keeps its own globals).
//   * AcquireInterface answers SOURCESHARP_FACTORY_INTERNAL from `this` too,
//     and hands only the ENGINE's factory kinds to SourceSharp_AcquireInterface.
//
// Managed sees no difference: the same object kind, the same forwarder table,
// the same names served.
//
//=============================================================================//

#pragma once

#include "ishim_root.h"
#include "shell_shim_console.h"
#include "shell_shim_areas.h"

class CShimRootServer final : public IShimRoot
{
public:
	// A shim interface or forwarder table this side serves, by name; NULL for
	// a name it does not. Defined in shell_internalfactory.cpp.
	void *Acquire( const char *pszName ) override;

	// The engine's interfaces by factory kind, and this side's own (the
	// INTERNAL kind) from Acquire.
	void *AcquireInterface( int32_t nType, const char *pszVersion,
	                        char *pError, int32_t nErrorBytes ) override;

	// The root lives as long as the DLL; this does nothing.
	void Release() override {}

	// The native-side shim areas. What native holds FROM managed (the managed
	// root, the runtime, the generated areas) is the game DLL object's:
	// ShellGameDll_Managed() (plan_managed_vtable_reload.md L2).
	CShimConsole m_Console;
	CShimAreas m_Areas;
};

// The game DLL's shim root. Defined beside the root in shell_gamedll.cpp.
CShimRootServer &ShellGameDll_ShimRoot();
