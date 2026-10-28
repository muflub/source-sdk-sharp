// This DLL's managed side: brings the runtime up, takes it down, reloads it in place, and
// says whether it is up. One per DLL, a member of the side's root, over the holders the
// root keeps the managed root and areas in.

#pragma once

#include <stddef.h>

#include "managed_init_failure.h"
#include "console_display_forward.h"

class CDiagnosticsHooks;
class CCVarRegistry;

#ifdef CLIENT_DLL
struct CManagedHoldersClient;
typedef CManagedHoldersClient ManagedHolders_t;
#else
struct CManagedHolders;
typedef CManagedHolders ManagedHolders_t;
#endif

class CManagedSide
{
public:
	// Defined in sourcesharp_gamesystem.cpp, where the holders are complete.
	CManagedSide( ManagedHolders_t &holders, CDiagnosticsHooks &diagnostics, CCVarRegistry &cvars );

	CManagedSide( const CManagedSide & ) = delete;
	CManagedSide &operator=( const CManagedSide & ) = delete;

	// Error() on failure, which does not return; a repeat call answers true.
	bool Load();

	// The counterpart of Load; the CLR is refcounted across both DLLs.
	void Unload();

	// False before the first load, after the last unload, and inside a reload. Handed by
	// reference to what must not call managed code while it is down.
	const bool &Loaded() const { return m_bLoaded; }

	// Bumped by every reload: native-held field offsets resolved under an older one belong
	// to the outgoing layout and re-ask before use. Handed by reference to what holds them.
	const unsigned int &Generation() const { return m_nGeneration; }

	// Reloads run at the top of the next frame: a collectible load context cannot unload
	// while any thread's stack, such as the requesting command's, holds its frames.
	void RequestReload();
	void ServiceReloadRequest();

private:
	int LoadMySide();
	void Reload();

	// The managed root and the areas it hands out (shell_internalfactory.cpp, per side).
	bool AcceptRoot( void *pObject, char *pWhy, size_t nWhy );
	bool AcquireAreas( char *pWhy, size_t nWhy );
	bool Reattached( void *pObject, char *pWhy, size_t nWhy );
	void ReleaseRoot();

	ManagedHolders_t &m_Holders;

	// The spew and assert hooks, installed at load before anything can assert.
	CDiagnosticsHooks &m_Diagnostics;

	// The console the managed side's display forwarder is installed on.
	CCVarRegistry &m_CVars;

	// The engine's console prints, forwarded to this side's managed console-output area.
	CConsoleDisplay m_ConsoleDisplay;

	// Whether Load has run to completion, as distinct from the side being up now.
	CSourceSharpInitGate m_InitGate;

	bool m_bLoaded = false;
	bool m_bReloadRequested = false;
	unsigned int m_nGeneration = 1;
};

#ifdef CLIENT_DLL
// The client DLL object's side (shell_clientdll.cpp).
CManagedSide &ShellClientDll_ManagedSide();
#endif
