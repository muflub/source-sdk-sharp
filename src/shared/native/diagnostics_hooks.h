// This DLL's spew and assert hooks: the -assertlog demotion, the echo of our lines to the
// launching terminal, the stack under a fatal spew and the -assertstack backtraces. One
// per DLL, a member of the side's root; tier0 calls it through thunks bound to it
// (bound_callback.h), installed at managed load.

#pragma once

#include "tier0/dbg.h"
#include "assert_stack.h"
#include "bound_callback.h"

class CDiagnosticsHooks
{
public:
	CDiagnosticsHooks();

	// Takes the hook off tier0 when it is still on top. One the other DLL chained after
	// cannot be unlinked, so its page is left mapped rather than pulled from under it.
	~CDiagnosticsHooks();

	CDiagnosticsHooks( const CDiagnosticsHooks & ) = delete;
	CDiagnosticsHooks &operator=( const CDiagnosticsHooks & ) = delete;

	// Before anything can assert. A second call keeps the first install.
	void Install();

	// Asserts this DLL demoted to warnings under -assertlog.
	int AssertsLogged() const { return m_nAssertsLogged; }

private:
	SpewRetval_t Spew( SpewType_t spewType, const tchar *pMsg );
	void AssertNotify( const char *pchFile, int nLine, const char *pchMessage );

	void EchoOurs( SpewType_t spewType, const tchar *pMsg );
	void Emit( const char *pchLine );
	void EmitFrames( void *const *pFrames, int nFrames, int *pnEmitted, int *pnResolved );
	void WriteFatalSpewStack( const char *pMsg );
	void InstallAssertStacks();

	typedef CBoundCallback<SpewRetval_t( SpewType_t, const tchar * )> SpewHook_t;
	typedef CBoundCallback<void( const char *, int, const char * )> AssertHook_t;

	SpewHook_t m_SpewHook;
	AssertHook_t m_AssertHook;

	// The handler that was installed before this one, which every message reaches next.
	SpewOutputFunc_t m_pPreviousSpew = nullptr;
	bool m_bInstalledAssertHook = false;

	bool m_bDemoteAsserts = false;
	int m_nAssertsLogged = 0;

	// Whether this DLL claimed the echo of our own lines to the launching terminal.
	bool m_bEchoOurs = false;

	// A stack under a fatal spew line, written once.
	bool m_bFatalSpewStackWritten = false;

	bool m_bAssertStacks = false;
	SourceSharpAssertStackSeen_t m_AssertStackSeen;
};
