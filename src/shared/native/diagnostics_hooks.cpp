//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CDiagnosticsHooks (diagnostics_hooks.h): the spew and assert hooks tier0 calls through
// thunks bound to the side's object. Asserts that log instead of stopping the game:
// whether an assert breaks is decided by the spew handler's return value, so this
// handler answers SPEW_CONTINUE for asserts under -assertlog.

#include "diagnostics_hooks.h"

#include "fatal_report.h"
#include "ss_command_line.h"
#include "ss_platform.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>

CDiagnosticsHooks::CDiagnosticsHooks()
	: m_SpewHook(this, &SpewHook_t::Trampoline<CDiagnosticsHooks, &CDiagnosticsHooks::Spew>)
	, m_AssertHook(this, &AssertHook_t::Trampoline<CDiagnosticsHooks, &CDiagnosticsHooks::AssertNotify>)
{
}

CDiagnosticsHooks::~CDiagnosticsHooks()
{
	if (m_bInstalledAssertHook)
		SetAssertFailedNotifyFunc(NULL);

	if (!m_pPreviousSpew)
		return;

	if (GetSpewOutputFunc() == m_SpewHook.Get())
		SpewOutputFunc(m_pPreviousSpew);
	else
		m_SpewHook.Abandon();
}

// Deep enough for the frame loop plus a render path plus the leaf.
enum
{
	kAssertStackDepth = 48
};

// Set by the assertstack self-test around its own deliberate assert.
#define SOURCESHARP_ASSERTSTACK_FORCE "SOURCESHARP_ASSERTSTACK_FORCE"

// Set when this DLL claimed the echo of our own lines to the launching terminal.

// Set while an -assertstack report is written, so its lines are not echoed twice.
// In the environment because the two hooks may be in different DLLs.
#define SOURCESHARP_ASSERTSTACK_WRITING "SOURCESHARP_ASSERTSTACK_WRITING"

static bool SourceSharp_AssertStackWriting()
{
	return getenv(SOURCESHARP_ASSERTSTACK_WRITING) != NULL;
}

static void SourceSharp_AssertStackSetWriting(bool bWriting)
{
	if (bWriting)
		SS_SetEnv(SOURCESHARP_ASSERTSTACK_WRITING, "1", 1);
	else
		SS_UnsetEnv(SOURCESHARP_ASSERTSTACK_WRITING);
}

// One echo per process: both DLLs install the hook and chain, so every message reaches
// both. A claim in the environment crosses the DLL boundary.
static bool SourceSharp_ClaimEcho()
{
	if (getenv("SOURCESHARP_ECHO_CLAIMED") != NULL)
		return false;

	SS_SetEnv("SOURCESHARP_ECHO_CLAIMED", "1", 0);
	return true;
}

// A stack under a fatal spew line, written once per DLL. The engine answers SPEW_ERROR
// with Sys_Error and does not return, so this is the last chance to say it.

void CDiagnosticsHooks::EchoOurs(SpewType_t spewType, const tchar* pMsg)
{
	if (!pMsg)
		return;

	const bool bOurs = (strncmp(pMsg, "[SourceSharp", 12) == 0);

	// SPEW_ERROR is fatal, so it is echoed whatever its prefix.
	const bool bFatal = (spewType == SPEW_ERROR);

	if (!SourceSharp_SpewShouldEcho(m_bEchoOurs, SourceSharp_AssertStackWriting(), bOurs, bFatal))
		return;

	FILE* pStream = (spewType == SPEW_WARNING || spewType == SPEW_ERROR || spewType == SPEW_ASSERT) ? stderr : stdout;

	if (!bOurs)
		fputs("[SourceSharp] FATAL SPEW from outside this DLL follows; the "
			  "engine answers SPEW_ERROR with Sys_Error and does not return:\n",
			  pStream);

	fputs(pMsg, pStream);
	fflush(pStream);

	// And who called it: the line alone names the assert, never the caller.
	if (SourceSharp_FatalSpewShouldWriteStack(bFatal, m_bFatalSpewStackWritten, SourceSharp_AssertStackWriting()))
	{
		m_bFatalSpewStackWritten = true;
		WriteFatalSpewStack(pMsg);
	}
}

SpewRetval_t CDiagnosticsHooks::Spew(SpewType_t spewType, const tchar* pMsg)
{
	EchoOurs(spewType, pMsg);

	// The self-test's deliberate assert must not end the process.
	const bool bDeliberate = (getenv(SOURCESHARP_ASSERTSTACK_FORCE) != NULL);

	// tier0 shows the assert dialog only when this returns SPEW_DEBUGGER, so the dialog
	// is prevented by never returning it for an assert.
	if (SourceSharp_AssertShouldBeFatal(spewType == SPEW_ASSERT, /*bFatal=*/true, m_bDemoteAsserts, bDeliberate))
	{
		++m_nAssertsLogged;

		// SPEW_WARNING, not SPEW_ERROR: the engine answers SPEW_ERROR with Sys_Error,
		// which takes the .NET runtime down before the report below runs. Through the
		// previous handler so it lands in console.log in order.
		if (m_pPreviousSpew)
			m_pPreviousSpew(SPEW_WARNING, pMsg);

		SourceSharp_FatalReport("ASSERT", pMsg);
	}

	if (spewType == SPEW_ASSERT && m_bDemoteAsserts)
	{
		++m_nAssertsLogged;

		// Through the previous handler so it lands in console.log in order.
		if (m_pPreviousSpew)
			m_pPreviousSpew(SPEW_WARNING, pMsg);

		// And straight to stderr, not through the echo: an engine assert lacks our
		// prefix, and the echo is claimed by one DLL while the assert usually fires
		// in the other.
		fprintf(stderr, "[SourceSharp] ASSERT #%d (demoted by -assertlog): %s", m_nAssertsLogged,
				pMsg ? pMsg : "(no message)\n");
		fflush(stderr);

		return SPEW_CONTINUE;
	}

	return m_pPreviousSpew ? m_pPreviousSpew(spewType, pMsg) : SPEW_CONTINUE;
}

// -assertstack: a backtrace for every failed assert in the process. tier0's notify
// hook has a setter and no getter, so it is installed once per process (the same
// environment claim the echo uses).

// Results are published in the environment because the hook is claimed per process
// while the self-test is registered per DLL.
#define SOURCESHARP_ASSERTSTACK_REPORTS "SOURCESHARP_ASSERTSTACK_REPORTS"
#define SOURCESHARP_ASSERTSTACK_FRAMES "SOURCESHARP_ASSERTSTACK_FRAMES"
#define SOURCESHARP_ASSERTSTACK_RESOLVED "SOURCESHARP_ASSERTSTACK_RESOLVED"

static void SourceSharp_AssertStackPublish(const char* pchName, long long llValue)
{
	char szValue[32];
	snprintf(szValue, sizeof(szValue), "%lld", llValue);
	SS_SetEnv(pchName, szValue, 1);
}

static long long SourceSharp_AssertStackRead(const char* pchName)
{
	return SourceSharpAssertStack_ParseCounter(getenv(pchName));
}

// One line of a report, to stderr and through the previous spew handler so it also
// reaches console.log. SPEW_WARNING, because an assert-typed spew from inside tier0's
// assert path would recurse.
void CDiagnosticsHooks::Emit(const char* pchLine)
{
	fputs(pchLine, stderr);
	fflush(stderr);

	if (!m_pPreviousSpew)
		return;

	// The caller sets the writing flag, so our own echo skips this.
	m_pPreviousSpew(SPEW_WARNING, pchLine);
}

// Writes resolved frames in the one format addr2line takes. nEmitted counts lines
// written and nResolved those carrying a module and offset.
void CDiagnosticsHooks::EmitFrames(void* const* pFrames, int nFrames, int* pnEmitted, int* pnResolved)
{
	for (int i = 0; i < nFrames; ++i)
	{
		SSModuleInfo info;

		// FormatFrameLine reserves the newline, so a long frame cannot run into the next.
		char szFrame[1024];

		if (SS_ModuleOfAddress(pFrames[i], &info) && info.szPath[0] &&
			(unsigned long long)(size_t)pFrames[i] >= (unsigned long long)(size_t)info.pBase)
		{
			SourceSharpAssertStack_FormatFrameLine(szFrame, sizeof(szFrame), i, info.szPath,
												   (unsigned long long)(size_t)info.pBase,
												   (unsigned long long)(size_t)pFrames[i], info.pszSymbol);

			if (pnResolved)
				++(*pnResolved);
		}
		else
		{
			SourceSharpAssertStack_FormatFrameLine(szFrame, sizeof(szFrame), i, NULL, 0,
												   (unsigned long long)(size_t)pFrames[i], NULL);
		}

		Emit(szFrame);

		if (pnEmitted)
			++(*pnEmitted);
	}
}

// A stack under a fatal spew, unconditionally: the process is about to die, so there
// is nothing to bury and no switch could be set in advance.
void CDiagnosticsHooks::WriteFatalSpewStack(const char* pMsg)
{
	void* pFrames[kAssertStackDepth];
	const int nFrames = SS_Backtrace(pFrames, kAssertStackDepth);

	// One report: the echo must not repeat it on stderr.
	SourceSharp_AssertStackSetWriting(true);

	char szLine[1024];
	snprintf(szLine, sizeof(szLine),
			 "\n[SourceSharp] FATAL SPEW STACK -- the engine will not return "
			 "from this. The message was: %s",
			 pMsg ? pMsg : "(none)\n");
	Emit(szLine);

	int nEmitted = 0;
	int nResolved = 0;
	EmitFrames(pFrames, nFrames, &nEmitted, &nResolved);

	snprintf(szLine, sizeof(szLine),
			 "[SourceSharp] FATAL SPEW STACK -- %d frames, %d resolved; "
			 "addr2line -e <module> -f -i -C <offset>\n",
			 nEmitted, nResolved);
	Emit(szLine);

	SourceSharp_AssertStackSetWriting(false);
}

void CDiagnosticsHooks::AssertNotify(const char* pchFile, int nLine, const char* pchMessage)
{
	if (!m_bAssertStacks)
		return;

	void* pFrames[kAssertStackDepth];
	const int nFrames = SS_Backtrace(pFrames, kAssertStackDepth);

	// A report's identity is the assert plus its immediate callers (assert_stack.h).
	// The self-test is exempt from the dedup so it answers every time.
	const bool bForced = (getenv(SOURCESHARP_ASSERTSTACK_FORCE) != NULL);

	bool bFirstOverflow = false;
	const SourceSharpAssertStackKey_t ulKey = SourceSharpAssertStack_Key(pchFile, nLine, pFrames, nFrames);

	if (!bForced && !SourceSharpAssertStack_ShouldReport(&m_AssertStackSeen, ulKey, &bFirstOverflow))
	{
		if (bFirstOverflow)
		{
			char szFull[256];
			snprintf(szFull, sizeof(szFull),
					 "[SourceSharp] -assertstack: %d distinct reports seen; "
					 "no more stacks will be printed\n",
					 kSourceSharpAssertStackMaxSeen);

			SourceSharp_AssertStackSetWriting(true);
			Emit(szFull);
			SourceSharp_AssertStackSetWriting(false);
		}
		return;
	}

	// One report: the echo must not repeat it on stderr.
	SourceSharp_AssertStackSetWriting(true);

	char szLine[1024];

	snprintf(szLine, sizeof(szLine), "\n[SourceSharp] -assertstack: %s (%d) : %s\n", pchFile ? pchFile : "??", nLine,
			 pchMessage ? pchMessage : "(no message)");
	Emit(szLine);

	int nEmitted = 0;
	int nResolved = 0;

	EmitFrames(pFrames, nFrames, &nEmitted, &nResolved);

	Emit("[SourceSharp] -assertstack: addr2line -e <module> -f -i -C <offset>\n");

	SourceSharp_AssertStackSetWriting(false);

	// Published last, so a reader that sees the counter move knows the report is complete.
	SourceSharp_AssertStackPublish(SOURCESHARP_ASSERTSTACK_FRAMES, nEmitted);
	SourceSharp_AssertStackPublish(SOURCESHARP_ASSERTSTACK_RESOLVED, nResolved);
	SourceSharp_AssertStackPublish(SOURCESHARP_ASSERTSTACK_REPORTS,
								   SourceSharp_AssertStackRead(SOURCESHARP_ASSERTSTACK_REPORTS) + 1);
	fflush(stderr);
}

void CDiagnosticsHooks::InstallAssertStacks()
{
	if (!ss::CommandLineHas("-assertstack"))
		return;

	// One per process: tier0 is shared and has no getter to chain onto.
	if (getenv("SOURCESHARP_ASSERTSTACK_CLAIMED") != NULL)
		return;

	SS_SetEnv("SOURCESHARP_ASSERTSTACK_CLAIMED", "1", 0);

	SourceSharpAssertStack_Reset(&m_AssertStackSeen);
	m_bAssertStacks = true;
	SetAssertFailedNotifyFunc(m_AssertHook.Get());
	m_bInstalledAssertHook = true;

	// Also straight to stderr: the echo hook is not installed yet.
	Msg("[SourceSharp] -assertstack: every failed assert will print a native backtrace\n");
	fprintf(stderr, "[SourceSharp] -assertstack: every failed assert will print a native backtrace\n");
	fflush(stderr);
}

void CDiagnosticsHooks::Install()
{
	InstallAssertStacks();

	// The hook is always installed: it also echoes our output to the terminal.
	m_bDemoteAsserts = ss::CommandLineHas("-assertlog");

	// Once. Both DLLs initialise, and chaining this to itself would recurse.
	if (m_pPreviousSpew)
		return;

	m_bEchoOurs = SourceSharp_ClaimEcho();

	m_pPreviousSpew = GetSpewOutputFunc();
	SpewOutputFunc(m_SpewHook.Get());

	if (m_bDemoteAsserts)
		Msg("[SourceSharp] -assertlog: assertions will be logged and NOT stop the game\n");
	else
		Msg("[SourceSharp] assertions are FATAL: logged, flushed, then the process stops. "
			"Pass -assertlog to survey them instead.\n");
}
