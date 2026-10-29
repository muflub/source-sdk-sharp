// Engine-free pieces of the fatal message for a failed managed init: stage name,
// cause, remedy and the formatted text, plus the gate that decides whether Init()
// runs. A failed managed init is fatal: the mod's entities, player, rules and user
// messages are all C#, so there is nothing to degrade into.

#pragma once

#include <stddef.h>
#include <stdio.h>

// The result codes are the host's; a private copy would drift.
#include "sourcesharp.h"

// The three calls in CSourceSharpSystem::Init() that can fail, in order.
enum SourceSharpInitStage_t
{
	kSourceSharpInitStage_ModBinDir = 0, // SourceSharp_GetModBinDir
	kSourceSharpInitStage_Acquire, // SourceSharp_Acquire
	kSourceSharpInitStage_LoadSide, // SourceSharp_LoadSide

	kSourceSharpInitStage_Count
};

// The name of the call that failed. Never NULL.
inline const char *SourceSharpInit_StageName( int nStage )
{
	switch ( nStage )
	{
		case kSourceSharpInitStage_ModBinDir:
			return "GetModBinDir";
		case kSourceSharpInitStage_Acquire:
			return "Acquire";
		case kSourceSharpInitStage_LoadSide:
			return "LoadSide";
		default:
			return "unknown stage";
	}
}

// What a SourceSharpResult code means, in a phrase a reader can act on. Never
// NULL; the host's SourceSharp_LastError is the detail printed under it.
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

// What to do about it. bin/dotnet and bin/managed are gitignored, so a fresh
// clone or worktree lacks them until setup or a build.
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

// The whole message. Returns what snprintf wanted to write, so truncation shows.
// pDetail may be NULL.
inline int SourceSharpInit_Format( char *pOut, size_t cOut, const char *pSide, int nStage, int rc, const char *pDetail )
{
	if ( !pOut || cOut == 0 )
		return 0;

	if ( !pSide || !pSide[0] )
		pSide = "?";

	if ( !pDetail || !pDetail[0] )
		pDetail = "(none reported)";

	// The code lines only when there is a code; GetModBinDir has none.
	char szCode[256];
	szCode[0] = '\0';
	if ( rc != SOURCESHARP_OK )
	{
		snprintf( szCode, sizeof( szCode ), "  code  : %d — %s\n", rc, SourceSharpInit_CauseText( rc ) );
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
					 pSide, SourceSharpInit_StageName( nStage ), SourceSharpInit_StageName( nStage ), szCode, pDetail,
					 SourceSharpInit_Remedy( nStage, rc ) );
}

// Whether CSourceSharpSystem::Init() should run. The client calls Init() twice by
// design (early, before gHUD.Init() hooks user messages, then from
// InitAllSystems); the second call must do nothing. Marked only on success, so a
// failed attempt is not mistaken for a finished one.
class CSourceSharpInitGate
{
public:
	CSourceSharpInitGate()
		: m_bSucceeded( false )
	{
	}

	bool ShouldRun() const { return !m_bSucceeded; }

	// Called at the end of Init(), past every failure point.
	void MarkSucceeded() { m_bSucceeded = true; }

	// Shutdown: the next Init() starts over.
	void Reset() { m_bSucceeded = false; }

	bool HasSucceeded() const { return m_bSucceeded; }

private:
	bool m_bSucceeded;
};
