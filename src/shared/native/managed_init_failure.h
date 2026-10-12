//========= SourceSharp — what a failed managed init says, and when it runs ===//
//
// THE BUG THIS EXISTS FOR. A user hit `Assert(0)` at usermessages.cpp:132 —
// CUserMessages::HookMessage, which fires when the client hooks a message
// nothing registered. Seven of them, pointing at a file that was not wrong.
//
// The client hooks exactly eight user messages and every one of them is
// declared in C#, registered by CSourceSharpSystem::Init(), which the client
// calls through SourceSharp_ClientEarlyInit() BEFORE gHUD.Init() hooks them.
// In a healthy launch the assert is unreachable. The one path that reaches it
// is a managed init that failed and RETURNED TRUE ANYWAY:
//
//     [SourceSharp] runtime acquire failed (-3): ...
//     CUserMessages::HookMessage:  no such message VGUIMenu    <- gated on -dev
//     usermessages.cpp (132) : Assertion Failed: 0             <- x7
//     ...
//     hl2.sh: line 113: Segmentation fault (core dumped)
//
// So the diagnosis a user is handed is seven asserts in unrelated code and a
// segfault inside engine.so with no game-DLL frame, and the actual cause — "the
// .NET runtime did not load" — is one line four thousand lines earlier that a
// default launch does not even print. That has cost this project four builds
// and five launches on a single occasion.
//
// The answer is that a failed managed init is FATAL. There is nothing to
// degrade into: this mod's entities, its player class, its game rules and its
// user-message list are all C#, so a game that got past this line has no
// content and dies later somewhere that does not name the cause.
//
// WHAT LIVES HERE. The parts that are string and integer work, engine-free, so
// they can be tested without a running Source (managed_init_failure_test.cpp):
//
//   StageName    which of the three calls failed
//   CauseText    what a SourceSharpResult code actually means, in English
//   Remedy       what the reader should DO about it — the most valuable line,
//                because two of these are missing directories that are
//                gitignored and therefore absent from every fresh worktree
//   Format       the one message, assembled and truncation-safe
//   CSourceSharpInitGate
//                whether Init() should run at all, which is where the second
//                half of the bug was: the old flag was set on ENTRY, so an
//                attempt that failed still marked the system initialised and
//                the client's later call through IGameSystem::InitAllSystems()
//                — the documented "retry" — silently did nothing.
//
// The part that needs the process (calling Error(), which does not return)
// stays in sourcesharp_gamesystem.cpp.
//
//=============================================================================//

#ifndef SOURCESHARP_MANAGED_INIT_FAILURE_H
#define SOURCESHARP_MANAGED_INIT_FAILURE_H
#ifdef _WIN32
#pragma once
#endif

#include <stddef.h>
#include <stdio.h>
#include <string.h>

// The result codes are NOT restated here. They are the host's, and a private
// copy would drift the day one was added — which would print a confident,
// wrong cause for a code this file no longer recognises.
#include "sourcesharp.h"

//-----------------------------------------------------------------------------
// The three calls in CSourceSharpSystem::Init() that can fail.
//
// In the order they run. Each one is a different thing to go and look at, which
// is why the message names the stage rather than only the code: -3 from Acquire
// and -3 from LoadSide would not be the same problem.
//-----------------------------------------------------------------------------
enum SourceSharpInitStage_t
{
	kSourceSharpInitStage_ModBinDir = 0,	// SourceSharp_GetModBinDir
	kSourceSharpInitStage_Acquire,			// SourceSharp_Acquire
	kSourceSharpInitStage_LoadSide,			// SourceSharp_LoadSide

	kSourceSharpInitStage_Count
};

//-----------------------------------------------------------------------------
// The name of the call that failed. Never NULL: an out-of-range stage answers
// a marker rather than reading off the end of the table, because the one thing
// a fatal-error path must not do is fault while reporting.
//-----------------------------------------------------------------------------
inline const char *SourceSharpInit_StageName( int nStage )
{
	switch ( nStage )
	{
	case kSourceSharpInitStage_ModBinDir:	return "GetModBinDir";
	case kSourceSharpInitStage_Acquire:		return "Acquire";
	case kSourceSharpInitStage_LoadSide:	return "LoadSide";
	default:								return "unknown stage";
	}
}

//-----------------------------------------------------------------------------
// What a SourceSharpResult code means, in a phrase a reader can act on.
//
// The host's own message (SourceSharp_LastError) is the DETAIL — a path, a
// hostfxr hresult — and it is precise and unreadable. This is the sentence that
// goes above it.
//
// Never NULL, including for a code this build does not know: an unrecognised
// code is itself worth saying, and saying it beats formatting a null pointer.
//-----------------------------------------------------------------------------
inline const char *SourceSharpInit_CauseText( int rc )
{
	switch ( rc )
	{
	case SOURCESHARP_OK:
		return "no error reported (which is itself wrong — this path is only "
		       "reached on failure)";
	case SOURCESHARP_ERR_HOSTFXR_LOAD:
		return "the bundled .NET runtime's libhostfxr could not be loaded";
	case SOURCESHARP_ERR_HOSTFXR_SYM:
		return "the bundled libhostfxr is missing exports this host needs";
	case SOURCESHARP_ERR_RUNTIME_INIT:
		return "the .NET runtime would not initialise from the bootstrap's "
		       "runtimeconfig";
	case SOURCESHARP_ERR_DELEGATE:
		return "the .NET runtime would not hand out its assembly-loading "
		       "delegate";
	case SOURCESHARP_ERR_BOOTSTRAP:
		return "the managed bootstrap could not be resolved or refused to load "
		       "this side";
	case SOURCESHARP_ERR_ABI_MISMATCH:
		return "the managed side was built against a different ABI version than "
		       "this DLL";
	case SOURCESHARP_ERR_NOT_INITIALIZED:
		return "the host was asked to load a side before the runtime was "
		       "acquired";
	default:
		return "the host returned a code this DLL does not recognise";
	}
}

//-----------------------------------------------------------------------------
// What to DO about it.
//
// The line worth the most, and the reason this is a table rather than one
// generic sentence. TWO of the causes below are a missing directory that git
// does not carry:
//
//   bin/dotnet    80MB of bundled runtime, gitignored. Absent from every fresh
//                 clone and from every `git worktree add`. Its absence used to
//                 surface as a SIGSEGV inside engine.so with no game-DLL frame,
//                 which reads exactly like a datatable bug.
//   bin/managed   the managed assemblies, written by the build. Absent until
//                 something has built them.
//
// Neither is a code change and neither is guessable from a hostfxr hresult.
//-----------------------------------------------------------------------------
inline const char *SourceSharpInit_Remedy( int nStage, int rc )
{
	if ( nStage == kSourceSharpInitStage_ModBinDir )
	{
		return "the engine gave this DLL no game directory to look in — launch "
		       "with -game <moddir>";
	}

	switch ( rc )
	{
	case SOURCESHARP_ERR_HOSTFXR_LOAD:
	case SOURCESHARP_ERR_HOSTFXR_SYM:
		return "<mod>/bin/dotnet holds the bundled .NET runtime and is "
		       "gitignored, so a fresh clone or `git worktree add` does not "
		       "have it — run tools/worktree-setup";
	case SOURCESHARP_ERR_RUNTIME_INIT:
	case SOURCESHARP_ERR_DELEGATE:
		return "<mod>/bin/managed holds the managed assemblies and the "
		       "runtimeconfig this reads — build them with `make debug`";
	case SOURCESHARP_ERR_ABI_MISMATCH:
		return "native and managed were built from different trees — rebuild "
		       "both with `make debug`";
	case SOURCESHARP_ERR_BOOTSTRAP:
		return "check SourceSharp.Server.dll and SourceSharp.Client.dll are in "
		       "<mod>/bin/managed, then rebuild with `make debug`";
	default:
		return "rebuild with `make debug` and check <mod>/bin/dotnet and "
		       "<mod>/bin/managed are both present";
	}
}

//-----------------------------------------------------------------------------
// The message itself.
//
// Assembled here rather than at the call site so there is exactly one of it and
// a test can read what the user will read. Returns the number of characters it
// WANTED to write, the way snprintf does — so a caller that cares can tell a
// truncated message from a complete one, and a test can assert the buffer is
// big enough for a real path.
//
// pDetail may be NULL: SourceSharp_LastError() is empty when nothing set it,
// and the ModBinDir stage has no host call behind it at all.
//-----------------------------------------------------------------------------
inline int SourceSharpInit_Format( char *pOut, size_t cOut,
                                   const char *pSide, int nStage, int rc,
                                   const char *pDetail )
{
	if ( !pOut || cOut == 0 )
		return 0;

	if ( !pSide || !pSide[0] )
		pSide = "?";

	if ( !pDetail || !pDetail[0] )
		pDetail = "(none reported)";

	// The code and cause lines are printed only when there IS a code.
	// GetModBinDir is not a host call and has none, and rc 0 there would
	// otherwise print "0 — no error reported" under the word FATAL, which reads
	// as a contradiction and sends the reader looking for a code that does not
	// exist. Two lines held in one buffer rather than two calls, so a caller
	// still gets one message and one return value.
	char szCode[256];
	szCode[0] = '\0';
	if ( rc != SOURCESHARP_OK )
	{
		snprintf( szCode, sizeof( szCode ), "  code  : %d — %s\n",
			rc, SourceSharpInit_CauseText( rc ) );
	}

	return snprintf( pOut, cOut,
		"[SourceSharp] FATAL: the managed (C#) %s side did not initialise.\n"
		"  stage : %s (SourceSharp_%s)\n"
		"%s"
		"  detail: %s\n"
		"  fix   : %s\n"
		"This mod's entities, player class, game rules and user messages are all "
		"C#, so there is nothing to run without them. Stopping here rather than "
		"continuing into a degraded state, which used to surface as assertions in "
		"usermessages.cpp and a segfault with no game-DLL frame.\n",
		pSide,
		SourceSharpInit_StageName( nStage ), SourceSharpInit_StageName( nStage ),
		szCode,
		pDetail,
		SourceSharpInit_Remedy( nStage, rc ) );
}

//-----------------------------------------------------------------------------
// Whether CSourceSharpSystem::Init() should do anything.
//
// A one-bit state machine, and the bit is the second half of the bug at the top
// of this file. It is worth being precise about what the double call IS,
// because it has been read as a retry and it is not one.
//
// On the CLIENT, Init() is called twice by design:
//
//   1. SourceSharp_ClientEarlyInit(), from cdll_client_int.cpp, BEFORE
//      gHUD.Init() — because the HUD hooks user messages there and the list it
//      hooks against is what this system registers.
//   2. IGameSystem::InitAllSystems(), a few lines later, because the system is
//      a CAutoGameSystemPerFrame and that is how every game system starts.
//
// The second call must do NOTHING. Running the body twice would re-register the
// C# user messages, Acquire the runtime a second time and LoadSide into a
// second load context, and double-register every console command. So the flag
// is DEDUPLICATION, not recovery — and nothing between the two calls could make
// a failed attempt succeed anyway: gHUD.Init() and g_pClientMode->Init() do not
// create <mod>/bin/dotnet, do not build <mod>/bin/managed, and do not change
// what engine->GetGameDirectory() answers.
//
// The old shape set the flag on ENTRY, before anything that could fail. That
// made the second call a no-op after a FAILED attempt too — so the one place
// that could have noticed, and returned false out of InitAllSystems, never ran.
// Marking on success alone is what this class is: a run that did not reach the
// end leaves the gate open.
//-----------------------------------------------------------------------------
class CSourceSharpInitGate
{
public:
	CSourceSharpInitGate() : m_bSucceeded( false ) {}

	// True until an attempt has actually finished.
	bool ShouldRun() const { return !m_bSucceeded; }

	// Called from the END of Init(), past every failure point.
	void MarkSucceeded() { m_bSucceeded = true; }

	// Shutdown: the next Init() starts over.
	void Reset() { m_bSucceeded = false; }

	bool HasSucceeded() const { return m_bSucceeded; }

private:
	bool m_bSucceeded;
};

#endif // SOURCESHARP_MANAGED_INIT_FAILURE_H
