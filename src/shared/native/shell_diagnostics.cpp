//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell diagnostics ===================================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
// The client's cbase.h brings engine (IVEngineClient) and the DPT_ prop types
// this file's client half uses; the server half needs nothing from it (P5).
#ifdef CLIENT_DLL
#include "cbase.h"
#endif
#include "ss_command_line.h"
#include "shell_diagnostics.h"
#include "shell_diagnostics_commands.h"
#include "fatal_report.h"
#include "shell_notshipped.h"
#include "tier1/interface.h"
#ifdef CLIENT_DLL
#else
	#include "server_class.h"
	#include "shell_diagnostics.h"
#endif
#include "assert_stack.h"
#include "sourcesharp.h"
#include "crossings_fetch.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E14: the server's IManagedDiagnostics
#include "imanaged_areas_client.h"   // plan_client_vtables CE9a: the client's IManagedDiagnosticsClient
#include "managed_convar_ref.h"
#include <stdarg.h>
#include <stdio.h>
#include <execinfo.h>
#include <unistd.h>
#include <string.h>
#include <dlfcn.h>
#include "ss_string.h"
#ifndef CLIENT_DLL
#include "shell_factory.h"
#endif


#ifdef CLIENT_DLL
	#define SOURCESHARP_SIDE_TAG "client"
#else
	#define SOURCESHARP_SIDE_TAG "server"
#endif

#if defined( SS_COVERAGE )
// libgcov's dump entry point, declared rather than #included: <gcov.h> exists
// only on a coverage build, and a linkage specification is not allowed at block
// scope, so this cannot live next to its caller -- the one above the _exit(3)
// in SourceSharp_LoggingSpewFunc, which would otherwise take the counters to
// the grave with it.
//
// __gcov_reset WAS declared here too, for cs_{cl,sv}_gcov's `dump` verb. That
// command is gone: `make coverage` and the golden-lap scoring it fed were
// removed in b6c429e1 (the Makefile says so in its own comment), so nothing in
// the tree ever set -DSS_COVERAGE or ran the command.
extern "C" void __gcov_dump( void );
#endif

// 0 = errors only, 1 = startup tracing, 2 = verbose (per-call).
//
// Named per side: this file compiles into BOTH DLLs, and Source rejects the
// same ConVar registered from client and server
// ("Parent cvar in server.dll not allowed").
#ifdef CLIENT_DLL
	#define SOURCESHARP_VERBOSE_CVAR "cl_sourcesharp_verbose"
#else
	#define SOURCESHARP_VERBOSE_CVAR "sv_sourcesharp_verbose"
#endif

//
// Plain int, constant-initialised, so it is valid from program start.
//
// This matters: UserMessageHelper's STATIC CONSTRUCTOR
// (shell_usermessage_registry.cpp) runs during dlopen, before ConVars are
// registered. Reading a ConVar there segfaults inside ld.so's call_init.
// Anything these shells call must therefore be static-init safe.
//
// It used to run the whole user message registration from there, which is why
// the list could not be managed. It now allocates the singleton and nothing
// else -- but the constraint is unchanged, because the constructor still runs
// in that window.
//
static int  g_nVerbosity  = 1;
static bool g_bTier0Ready = false;

#ifdef CLIENT_DLL
static void OnVerbosityChanged( IConVar *pVar, const char *pOld, float flOld );

static ConVar sourcesharp_verbose( SOURCESHARP_VERBOSE_CVAR, "1", FCVAR_NONE,
	"SourceSharp log verbosity: 0=errors, 1=startup, 2=verbose", OnVerbosityChanged );

static void OnVerbosityChanged( IConVar *pVar, const char *pOld, float flOld )
{
	g_nVerbosity = ( (ConVar *)pVar )->GetInt();
}

int SourceSharp_GetVerbosity()
{
	return g_nVerbosity;
}

// Called once the engine is up, after which Msg() is safe.
void SourceSharp_MarkDiagnosticsReady()
{
	g_bTier0Ready = true;
	g_nVerbosity  = sourcesharp_verbose.GetInt();
}
#else
// THE SERVER'S IS DECLARED IN C# (SourceSharp.Server.SvSourceSharpVerboseConVar),
// plan_server_novalve.md P2.3b. Until the managed registry has registered it
// the answer is g_nVerbosity's 1, the declared default; a ref that is still
// unresolved would answer 0 and silence the earliest lines, which are the ones
// worth seeing. No change callback: the value is read where it is used.
static CManagedConVarRef sourcesharp_verbose( SOURCESHARP_VERBOSE_CVAR );

int SourceSharp_GetVerbosity()
{
	if ( g_bTier0Ready && sourcesharp_verbose.IsValid() )
		return sourcesharp_verbose.GetInt();
	return g_nVerbosity;
}

// Called once the engine is up, after which Msg() is safe.
void SourceSharp_MarkDiagnosticsReady()
{
	g_bTier0Ready = true;
}
#endif

//-----------------------------------------------------------------------------
// Msg() is not safe during static init either, so fall back to stdout until the
// engine is up. Dropping these lines would hide exactly the earliest calls,
// which are the ones worth seeing.
//-----------------------------------------------------------------------------
static void SourceSharp_DiagPrint( const char *pFmt, ... )
{
	char szBuf[512];
	va_list args;
	va_start( args, pFmt );
	ss::FormatV( szBuf, sizeof( szBuf ), pFmt, args );
	va_end( args );

	if ( g_bTier0Ready )
		Msg( "%s", szBuf );
	else
		fputs( szBuf, stdout );
}

//-----------------------------------------------------------------------------
void SourceSharp_LogStub( const char *pWhat, bool *pbAlreadyLogged )
{
	const int nVerbosity = SourceSharp_GetVerbosity();
	if ( nVerbosity < 1 )
		return;

	// Verbosity 1: once per call site. Verbosity 2: every call.
	if ( *pbAlreadyLogged && nVerbosity < 2 )
		return;

	*pbAlreadyLogged = true;
	SourceSharp_DiagPrint( "[SourceSharp] STUB %s (%s) — engine called it; C# does not implement it yet\n",
		pWhat, SOURCESHARP_SIDE_TAG );
}

//-----------------------------------------------------------------------------
// Unconditional call trace (verbosity >= 1). This is how the exact order the
// engine drives the shells becomes visible — the last line before a crash is
// the last shell function that ran.
//-----------------------------------------------------------------------------
void SourceSharp_Trace( const char *pWhat, const char *pDetail )
{
	if ( SourceSharp_GetVerbosity() < 1 )
		return;

	if ( pDetail )
		SourceSharp_DiagPrint( "[SourceSharp] > %s (%s): %s\n", pWhat, SOURCESHARP_SIDE_TAG, pDetail );
	else
		SourceSharp_DiagPrint( "[SourceSharp] > %s (%s)\n", pWhat, SOURCESHARP_SIDE_TAG );
}

//-----------------------------------------------------------------------------
void SourceSharp_LogEntityCreate( const char *pClassName )
{
	if ( SourceSharp_GetVerbosity() < 1 )
		return;

	SourceSharp_DiagPrint( "[SourceSharp] entity factory: creating '%s'\n",
		pClassName ? pClassName : "(null)" );
}

//-----------------------------------------------------------------------------
// THE NOT-SHIPPED LEDGER -- plan_entity.md step 7g.
//
// One instance per DLL, and a file-scope static rather than a member for the
// reason the surrounding file already is one: this file has no owning object.
// g_nVerbosity and g_bTier0Ready above it are the same shape, for the same
// reason -- the shells that DO own their state (CShellClientDLL::m_Refusals,
// CShellGameDll's) keep the project's no-globals rule where there is an owner
// to keep it in, and this table's reachers are stock translation units, which
// have none. (tier1's CreateInterface miss hook was the other reacher; it went
// in Phase 6b, when the build moved to Valve's prebuilt tier1.a.)
//
// The Warning half of shell_notshipped.h is defined here because it is the one
// part that needs the engine -- the gtest links its own recorder against the
// same declaration, which is what lets it check the shipped class rather than
// a copy of it.
//-----------------------------------------------------------------------------
static CShellNotShipped g_NotShipped;

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void ShellNotShipped_Warning( const char *pName, const char *pReason, int nTotal )
{
	SourceSharp_DiagPrint( "[SourceSharp] NOT SHIPPED (%s): %s -- %s [total %d]\n",
		SOURCESHARP_SIDE_TAG,
		pName   ? pName   : "(null)",
		pReason ? pReason : "(no reason given)",
		nTotal );
}
#endif

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void SourceSharp_NotShipped( const char *pCapability, const char *pReason )
{
	g_NotShipped.Reached( pCapability, pReason );
}
#endif

// FILE-LOCAL since plan_shim.md §5 3e. It was declared in shell_diagnostics.h
// as an entry point and its one caller is SourceSharp_PrintNotShipped, eleven
// hundred lines below in this same file. A header declaration is a promise to
// every translation unit that includes it; this one was a promise nobody took
// up, and it exported the CShellNotShipped type's whole interface with it.
static const CShellNotShipped &SourceSharp_NotShippedLedger()
{
	return g_NotShipped;
}

#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
//-----------------------------------------------------------------------------
// THE NOT-SHIPPED LEDGER, for the dev API's /client/{slot}/absences
// (plan_client_novalve §4.4): one capability per index, with its reason and
// how often it was reached; the answer is how many names, and the total
// through pOutTotal. A readout of what cs_cl_notshipped prints.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientNotShipped( int32_t iIndex, char *pOutName, int32_t nNameBytes,
                                      char *pOutReason, int32_t nReasonBytes,
                                      int32_t *pOutCount, int32_t *pOutTotal )
{
	const CShellNotShipped &ledger = SourceSharp_NotShippedLedger();
	if ( pOutTotal )
		*pOutTotal = ledger.Total();

	if ( iIndex >= 0 && iIndex < ledger.NameCount() )
	{
		if ( pOutName && nNameBytes > 0 )
			V_strncpy( pOutName, ledger.NameAt( iIndex ) ? ledger.NameAt( iIndex ) : "", nNameBytes );
		if ( pOutReason && nReasonBytes > 0 )
			V_strncpy( pOutReason, ledger.ReasonAt( iIndex ) ? ledger.ReasonAt( iIndex ) : "", nReasonBytes );
		if ( pOutCount )
			*pOutCount = ledger.CountAt( iIndex );
	}

	return ledger.NameCount();
}
#endif

#ifdef CLIENT_DLL
//-----------------------------------------------------------------------------
// THE HLTV CAMERA'S ONE ANSWER -- plan_entity.md step 7g.
//
// game/client/hltvcamera.cpp is deleted: this mod does not ship the client-side
// SourceTV camera. Nineteen call sites in four still-compiled translation units
// reached C_HLTVCamera, and all but three of them stood inside
// `if ( engine->IsHLTV() ) { ...camera... } else { ...the ordinary path... }`.
//
// SO THE EDIT AT THOSE SITES IS THIS FUNCTION IN PLACE OF `engine->IsHLTV()`,
// one token each, and every else-arm is left exactly as stock wrote it. It
// always answers false, so the ordinary arm is always the one taken -- and when
// the engine really does say this client is watching a broadcast, the refusal
// is RECORDED BY NAME on the way past instead of the camera silently doing
// nothing. That is §7's rule, and it is also the only way anyone would ever
// find out: a spectator client with no camera looks like a spectator client.
//
// engine->IsHLTV() IS STILL ASKED, deliberately. Answering a flat false without
// asking would make the ledger read zero for a session that really was watching
// HLTV, which is exactly the reading that has to be trustworthy.
//-----------------------------------------------------------------------------
bool SourceSharp_HltvCameraAvailable()
{
	if ( engine != NULL && engine->IsHLTV() )
	{
		SourceSharp_NotShipped( "HLTV spectator camera",
			"game/client/hltvcamera.cpp is not shipped (plan_entity.md 7g); "
			"this client is watching a SourceTV broadcast and has no camera for it" );
	}

	return false;
}
#endif // CLIENT_DLL

//-----------------------------------------------------------------------------
// Entity factory miss — the classname has no registered factory, so the entity
// cannot be created. The engine prints its own terse warning; this adds the
// side and a consistent prefix so it greps alongside the rest.
//-----------------------------------------------------------------------------
void SourceSharp_LogEntityCreateFailed( const char *pClassName )
{
	SourceSharp_DiagPrint( "[SourceSharp] entity factory MISS: no factory registered for '%s'\n",
		pClassName ? pClassName : "(null)" );
}

#ifndef CLIENT_DLL
void SourceSharp_LogEntityDestroy( const char *pClassName )
{
	if ( SourceSharp_GetVerbosity() < 2 )
		return;

	SourceSharp_DiagPrint( "[SourceSharp] entity factory: destroying '%s'\n",
		pClassName ? pClassName : "(null)" );
}
#endif // !CLIENT_DLL

//-----------------------------------------------------------------------------
// Human-readable names for the datatable prop types, so the dump is readable
// without cross-referencing the enum.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// True for tables SourceSharp declares. Those always dump their props, because
// they are the ones that must match across the client/server boundary and the
// client dump runs before command-line ConVars are applied.
//-----------------------------------------------------------------------------
static bool IsSourceSharpTable( const char *pTableName )
{
	return pTableName && strncmp( pTableName, "DT_SourceSharp", 14 ) == 0;
}

static const char *DTTypeName( int nType )
{
	switch ( nType )
	{
	case DPT_Int:        return "int";
	case DPT_Float:      return "float";
	case DPT_Vector:     return "vector";
	case DPT_VectorXY:   return "vectorxy";
	case DPT_String:     return "string";
	case DPT_Array:      return "array";
	case DPT_DataTable:  return "datatable";
	default:             return "?";
	}
}

//-----------------------------------------------------------------------------
// SourceSharp_DumpDataMap WAS HERE, and it walked a prediction/save datamap
// including its base maps. It had NO CALLER, in this file or any other, on
// either side -- measured over the sources of every object on both link lines,
// and shim-census agrees: the symbol is imported by nothing.
//
// It is not a diagnostic somebody might want back, either, and that is worth
// saying because "a debug printer costs nothing" is how one survives. This
// port's entities do not go through a datamap at all -- they are
// SourceSharp.Abi's SavedEntityCodec -- so the only datamaps left in the
// process are the ENGINE's own, and shell_savegame.h's codec is what reads
// those. A walker for a structure this build does not produce prints nothing
// about the thing its comment describes.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Every networked class this DLL registered, with its datatable name.
//
// Client and server must agree on this list by name for a connect to succeed,
// so dumping both sides makes a mismatch visible instead of fatal.
//-----------------------------------------------------------------------------
void SourceSharp_DumpNetworkClasses()
{
	int nCount = 0;

#ifdef CLIENT_DLL
	for ( ClientClass *pCur = g_pClientClassHead; pCur; pCur = pCur->m_pNext )
		nCount++;

	SourceSharp_DiagPrint( "[SourceSharp] client.so registered %d ClientClass(es)\n", nCount );

	{
		for ( ClientClass *pCur = g_pClientClassHead; pCur; pCur = pCur->m_pNext )
		{
			RecvTable *pTable = pCur->m_pRecvTable;
			SourceSharp_DiagPrint( "[SourceSharp]     %s -> %s (%d prop(s))\n",
				pCur->GetName(),
				pTable ? pTable->GetName() : "(no recv table)",
				pTable ? pTable->GetNumProps() : 0 );

			if ( !pTable )
				continue;
			if ( SourceSharp_GetVerbosity() < 2 && !IsSourceSharpTable( pTable->GetName() ) )
				continue;

			for ( int i = 0; i < pTable->GetNumProps(); i++ )
			{
				RecvProp *pProp = pTable->GetProp( i );
				if ( !pProp )
					continue;

				SourceSharp_DiagPrint( "[SourceSharp]         %-28s %s\n",
					pProp->GetName(), DTTypeName( (int)pProp->GetType() ) );
			}
		}
	}
#else
	for ( ServerClass *pCur = g_pServerClassHead; pCur; pCur = pCur->m_pNext )
		nCount++;

	SourceSharp_DiagPrint( "[SourceSharp] server.so registered %d ServerClass(es)\n", nCount );

	{
		for ( ServerClass *pCur = g_pServerClassHead; pCur; pCur = pCur->m_pNext )
		{
			SendTable *pTable = pCur->m_pTable;
			SourceSharp_DiagPrint( "[SourceSharp]     %s -> %s (%d prop(s))\n",
				pCur->GetName(),
				pTable ? pTable->GetName() : "(no send table)",
				pTable ? pTable->GetNumProps() : 0 );

			if ( !pTable )
				continue;
			if ( SourceSharp_GetVerbosity() < 2 && !IsSourceSharpTable( pTable->GetName() ) )
				continue;

			for ( int i = 0; i < pTable->GetNumProps(); i++ )
			{
				SendProp *pProp = pTable->GetProp( i );
				if ( !pProp )
					continue;

				SourceSharp_DiagPrint( "[SourceSharp]         %-28s %s offset=%d\n",
					pProp->GetName(), DTTypeName( (int)pProp->GetType() ), pProp->GetOffset() );
			}
		}
	}
#endif
}

//-----------------------------------------------------------------------------
// Walks the DLL's own interface registry. This is exactly what the engine sees
// when it calls CreateInterface, so it answers "are we exporting everything the
// game expects?" directly rather than by inspection of the source.
//-----------------------------------------------------------------------------
void SourceSharp_DumpExportedInterfaces()
{
#ifdef CLIENT_DLL
	int nCount = 0;
	for ( InterfaceReg *pReg = InterfaceReg::s_pInterfaceRegs; pReg; pReg = pReg->m_pNext )
		nCount++;

	SourceSharp_DiagPrint( "[SourceSharp] %s.so exports %d interface(s):\n",
		SOURCESHARP_SIDE_TAG, nCount );

	for ( InterfaceReg *pReg = InterfaceReg::s_pInterfaceRegs; pReg; pReg = pReg->m_pNext )
		SourceSharp_DiagPrint( "[SourceSharp]     %s\n", pReg->m_pName );
#else
	// server.so's factory is a table (shell_factory.cpp), not a registry.
	const int nCount = SourceSharp_ExposedCount();
	SourceSharp_DiagPrint( "[SourceSharp] %s.so exports %d interface(s):\n",
		SOURCESHARP_SIDE_TAG, nCount );

	for ( int i = 0; i < nCount; i++ )
		SourceSharp_DiagPrint( "[SourceSharp]     %s\n", SourceSharp_ExposedAt( i ).m_pszVersion );
#endif
}

//=============================================================================//
// Asserts that log instead of stopping the game.
//
// A debug build halts on the first failed assert: DebuggerBreak raises SIGTRAP
// and the process is gone. That is right when you are sitting at the game and
// want to know immediately, and useless when you are trying to find out how
// often something happens, or driving the game from outside, or bisecting an
// intermittent fault that takes twenty launches to appear.
//
// Whether an assert breaks is decided by the SPEW HANDLER, not by the assert:
// the macro only breaks if _SpewMessage returned SPEW_DEBUGGER. So this installs
// a handler that reports asserts and answers SPEW_CONTINUE, and passes
// everything else to whatever was there before.
//
// Opt-in through -assertlog, and deliberately so. An assert that no longer stops
// anything is an assert nobody reads, and this exists to make a specific hunt
// possible rather than to make the game quieter.
//=============================================================================//

static SpewOutputFunc_t g_pPreviousSpewFunc = NULL;

// Whether -assertlog was given. Separate from whether the hook is installed,
// because the hook is now always installed and only this half is optional.
// Deep enough for the frame loop plus a render path plus the leaf; anything
// past this is main() and the loader.
enum { kAssertStackDepth = 48 };

// Set by the assertstack self-test around its own deliberate assert, and by
// nothing else. Defined here rather than beside its three siblings below
// because the fatal-assert path reads it, and that path runs earlier in the
// file than they are declared.
#define SOURCESHARP_ASSERTSTACK_FORCE    "SOURCESHARP_ASSERTSTACK_FORCE"

static bool g_bDemoteAsserts = false;

static int g_nAssertsLogged = 0;

//-----------------------------------------------------------------------------
// Everything this mod says, to the terminal that launched the game.
//
// Msg() goes wherever tier0's spew handler sends it, and the engine's handler
// writes to the in-game console and console.log -- never to stdout for a
// windowed client. So the shim's own diagnostics were invisible in the terminal
// while the host's (which uses printf, having no tier0) were not, and managed
// logging only appeared because it echoed itself, one call at a time.
//
// Done here instead, because a spew hook catches every caller: the shells, the
// entity factory, the HUD, and anything added later, without a single call site
// knowing about it. Filtered on our own prefix so the terminal does not become
// a second copy of the entire engine console -- console.log already is that.
//-----------------------------------------------------------------------------
// Whether this DLL is the one that echoes. See SourceSharp_ClaimEcho.
static bool g_bEchoOurs = false;

//-----------------------------------------------------------------------------
// Set while an -assertstack report is being written.
//
// That hook puts every line on stderr itself and then sends the same line down
// the spew chain to reach console.log; without this the chain arrives at the
// echo below and each line appears on stderr twice.
//
// In the ENVIRONMENT rather than in a static, because the two halves are in
// different DLLs whenever one side won the assert-stack claim and the other won
// the echo claim. A static was tried first and left the client's verdict
// printed twice while the server's printed once — which is what a per-DLL flag
// looks like when only one DLL is echoing.
//
// Set once per report rather than once per line: this runs on whatever thread
// the assert fired on, and setenv is not something to do nine times in a row
// there when once will do.
//-----------------------------------------------------------------------------
#define SOURCESHARP_ASSERTSTACK_WRITING "SOURCESHARP_ASSERTSTACK_WRITING"

static bool SourceSharp_AssertStackWriting()
{
	return getenv( SOURCESHARP_ASSERTSTACK_WRITING ) != NULL;
}

static void SourceSharp_AssertStackSetWriting( bool bWriting )
{
	if ( bWriting )
		setenv( SOURCESHARP_ASSERTSTACK_WRITING, "1", 1 );
	else
		unsetenv( SOURCESHARP_ASSERTSTACK_WRITING );
}

//-----------------------------------------------------------------------------
// One echo per process, not one per DLL.
//
// client.so and server.so both install this hook, and each chains to whatever
// was there before — so a single message passes through both handlers and was
// printed to the terminal twice, adjacent and identical. The statics above are
// per-DLL, so neither side can see that the other already installed one; a
// claim that outlives the DLL boundary can.
//
// Either side may win. It does not matter which: the chain means every message
// reaches both handlers regardless of which DLL spewed it, so whichever one
// echoes sees all of them.
//-----------------------------------------------------------------------------
static bool SourceSharp_ClaimEcho()
{
	if ( getenv( "SOURCESHARP_ECHO_CLAIMED" ) != NULL )
		return false;

	setenv( "SOURCESHARP_ECHO_CLAIMED", "1", 0 );
	return true;
}

//-----------------------------------------------------------------------------
// A STACK UNDER THE FATAL LINE — lane 8e-physics, plan_entity.md step 8e.
//
// Defined below, beside the frame walk it shares with -assertstack. Declared
// here because the echo is the ONLY hook that runs while the stack that caused
// a fatal spew is still standing: the engine answers SPEW_ERROR with Sys_Error
// and does not return, so anything not said here is not said.
//
// The flag is per DLL and that is correct: only the DLL that claimed the echo
// reaches this at all, and a second DLL re-spewing the same fatal on its way
// down would otherwise print the same thirty lines again.
//-----------------------------------------------------------------------------
static bool g_bFatalSpewStackWritten = false;
static void SourceSharp_WriteFatalSpewStack( const char *pMsg );

static void SourceSharp_EchoOurs( SpewType_t spewType, const tchar *pMsg )
{
	if ( !pMsg )
		return;

	const bool bOurs  = ( strncmp( pMsg, "[SourceSharp", 12 ) == 0 );

	// SPEW_ERROR IS FATAL AND SO IT IS NOT FILTERED ON OUR PREFIX. The engine
	// answers SPEW_ERROR with Sys_Error(), which ends in a deliberate write to
	// address zero -- so a message on this path has no later chance to be read,
	// and this hook is the last thing standing between it and the operator.
	// Three save/load crashes were reported as "*** stack smashing detected ***"
	// because vphysics's "IVP Failed at ../ivp_utility/ivu_vhash.cxx 157" went
	// through here and was dropped for not starting with "[SourceSharp". The
	// whole predicate, and that story, are in assert_stack.h.
	const bool bFatal = ( spewType == SPEW_ERROR );

	if ( !SourceSharp_SpewShouldEcho( g_bEchoOurs, SourceSharp_AssertStackWriting(),
	                                  bOurs, bFatal ) )
		return;

	FILE *pStream = ( spewType == SPEW_WARNING || spewType == SPEW_ERROR
	                  || spewType == SPEW_ASSERT ) ? stderr : stdout;

	// NAMED when it is not ours, because an unadorned engine line arriving on
	// stderr in the middle of the shell's own output reads as the shell's.
	if ( !bOurs )
		fputs( "[SourceSharp] FATAL SPEW from outside this DLL follows; the "
		       "engine answers SPEW_ERROR with Sys_Error and does not return:\n",
		       pStream );

	// Flushed: unflushed output interleaves wrongly with the engine's own
	// stderr and, when the game dies, loses exactly the lines that said why.
	fputs( pMsg, pStream );
	fflush( pStream );

	// AND WHO CALLED IT. See SourceSharp_FatalSpewShouldWriteStack in
	// assert_stack.h: the line alone names the assert and never the caller, and
	// the last three occurrences of this one had to be walked by hand out of
	// core dumps whose binaries no longer existed.
	if ( SourceSharp_FatalSpewShouldWriteStack( bFatal, g_bFatalSpewStackWritten,
	                                            SourceSharp_AssertStackWriting() ) )
	{
		g_bFatalSpewStackWritten = true;
		SourceSharp_WriteFatalSpewStack( pMsg );
	}
}

static SpewRetval_t SourceSharp_LoggingSpewFunc( SpewType_t spewType, const tchar *pMsg )
{
	SourceSharp_EchoOurs( spewType, pMsg );

	// THE SELF-TEST'S ASSERT IS NOT A DEFECT AND MUST NOT END THE PROCESS.
	//
	// cs_{cl,sv}_assertstack_selftest trips a deliberate assert to prove the
	// stack hook is alive, and it already sets this variable around exactly that
	// one assert and clears it immediately afterwards -- the same scoping the
	// dedup exemption uses. Reusing that signal rather than inventing a second
	// one keeps ONE definition of "this assert is on purpose"; a second flag
	// would drift from the first and the drift would show up as a self-test
	// that kills the game.
	const bool bDeliberate = ( getenv( SOURCESHARP_ASSERTSTACK_FORCE ) != NULL );

	// FATAL IS THE DEFAULT AND HAS NO VARIABLE, because -assertlog is already
	// the off switch and a second one could only disagree with it. Passing the
	// literal keeps the predicate's four inputs -- which is what the six facts
	// in assert_stack_test.cpp cover, including the fatal-off rows that no
	// production path can now reach.
	//
	// tier0 reaches DoNewAssertDialog only when this function returns
	// SPEW_DEBUGGER (dbg.h:259), so the dialog is prevented by never returning
	// it for an assert, not by anything in tier0.
	if ( SourceSharp_AssertShouldBeFatal( spewType == SPEW_ASSERT, /*bFatal=*/true,
	                                     g_bDemoteAsserts, bDeliberate ) )
	{
		++g_nAssertsLogged;

		// Through the previous handler first, so it lands in console.log in
		// ORDER, next to what the game was doing. That ordering is the whole
		// value of the log; an assert printed after the flush would arrive
		// detached from its cause.
		// SPEW_WARNING, NOT SPEW_ERROR, and the difference is the whole
		// behaviour of this branch. The engine's own spew handler answers
		// SPEW_ERROR with Sys_Error(), which is
		// its fatal path: it unwinds the engine, re-enters managed code, and
		// takes the .NET runtime down with a stack overflow and SIGABRT --
		// BEFORE the _exit(3) below is ever reached.
		//
		// Found by the C1 coverage lap, which is the only reason it was
		// visible: the process still died, and it still died right after an
		// assert, so from the outside it looked like this branch working. What
		// it actually did was skip the flush and leave zero .gcda -- which the
		// reach intersector would have read as "these regions never executed",
		// the claim that gets code deleted.
		//
		// Severity is already carried by the FATAL ASSERT line below. All this
		// call is for is landing the message in console.log IN ORDER, next to
		// what the game was doing, which is what makes it worth reading at all.
		if ( g_pPreviousSpewFunc )
			g_pPreviousSpewFunc( SPEW_WARNING, pMsg );

		SourceSharp_FatalReport( "ASSERT", pMsg );
	}

	if ( spewType == SPEW_ASSERT && g_bDemoteAsserts )
	{
		++g_nAssertsLogged;

		// Printed through the previous handler so it lands in console.log with
		// everything else, in order — an assert is only useful next to what the
		// game was doing when it fired.
		if ( g_pPreviousSpewFunc )
			g_pPreviousSpewFunc( SPEW_WARNING, pMsg );

		// And said again in this mod's own voice, so it reaches the TERMINAL.
		//
		// The echo above filters on the "[SourceSharp" prefix, which an engine
		// assert does not carry, so a demoted assert used to exist only in
		// console.log. Anyone reading a run's stdout — every scripted check
		// this project has — saw a clean log and concluded the assert had not
		// fired. That happened on 2026-08-19 with gamemovement.cpp(1168),
		// which was firing four times a run while the captured output looked
		// green. A demotion that also hides the assert is not a demotion.
		//
		// Printed DIRECTLY rather than through SourceSharp_EchoOurs, which is
		// gated on the one-echo-per-process claim: only one of the two DLLs
		// wins that claim, and the assert usually fires in the other one. That
		// gate is right for ordinary chatter, which both DLLs would otherwise
		// print twice, and wrong for an assert, which is rare and must never
		// be dropped because of which side happened to install first.
		fprintf( stderr, "[SourceSharp] ASSERT #%d (demoted by -assertlog): %s",
		         g_nAssertsLogged, pMsg ? pMsg : "(no message)\n" );
		fflush( stderr );

		// Continue rather than break. This is the whole point of the switch.
		return SPEW_CONTINUE;
	}

	return g_pPreviousSpewFunc ? g_pPreviousSpewFunc( spewType, pMsg ) : SPEW_CONTINUE;
}

//=============================================================================//
// -assertstack: the CALLER of every failed assert.
//
// tier0 calls CallAssertFailedNotifyFunc with the file, the line and the
// expression before it decides whether to break, so a notify hook sees every
// failed assert in the PROCESS — engine, materialsystem, both game DLLs —
// whether or not -assertlog demoted it. It is the only hook that runs while the
// stack that caused the assert is still standing.
//
// One slot, no chain: tier0 has a setter and no getter, so this installs once
// per process (the same env claim the echo uses) rather than letting client.so
// and server.so overwrite each other. It does not matter which side wins,
// because tier0 is one library shared by both and the hook catches asserts from
// either.
//
// Off unless asked for. A backtrace per assert is 30 lines, and the runs that
// want it are hunts, not ordinary play.
//=============================================================================//

static bool g_bAssertStacks = false;
static SourceSharpAssertStackSeen_t g_AssertStackSeen;


//-----------------------------------------------------------------------------
// The names the hook publishes its results under.
//
// Environment, because the hook is claimed once per PROCESS and the self-test
// is registered once per DLL, so the two halves are routinely on opposite sides
// of a module boundary with no shared static between them. The claim above
// already travels this way; this is the same channel carrying an answer back.
//-----------------------------------------------------------------------------
#define SOURCESHARP_ASSERTSTACK_REPORTS  "SOURCESHARP_ASSERTSTACK_REPORTS"
#define SOURCESHARP_ASSERTSTACK_FRAMES   "SOURCESHARP_ASSERTSTACK_FRAMES"
#define SOURCESHARP_ASSERTSTACK_RESOLVED "SOURCESHARP_ASSERTSTACK_RESOLVED"

static void SourceSharp_AssertStackPublish( const char *pchName, long long llValue )
{
	char szValue[ 32 ];
	snprintf( szValue, sizeof( szValue ), "%lld", llValue );
	setenv( pchName, szValue, 1 );
}

static long long SourceSharp_AssertStackRead( const char *pchName )
{
	return SourceSharpAssertStack_ParseCounter( getenv( pchName ) );
}

//-----------------------------------------------------------------------------
// One line of a report, to BOTH places a reader might be.
//
// This is the defect that made the instrument useless without making it look
// broken. Every line of an -assertstack report went to raw stderr and nowhere
// else — and stderr is the one channel a headless, script-driven run does not
// keep. `-condebug` writes console.log, `./debug-api console` reads console.log,
// and the engine's console is where every other line of a run is read from;
// none of them ever saw a single frame. What they DID see was the demoted
// assert, printed by the spew hook one screen up, so the log said the assert
// had been caught and reported and showed no stack under it. That reads exactly
// like a working switch on a quiet frame walk, and it was read that way twice.
//
// So a report goes out through the previous spew handler as well, which is what
// puts it in console.log — the same route, and for the same reason, that the
// demoted-assert branch above already uses. SPEW_WARNING, not SPEW_ASSERT: an
// assert-typed spew from inside tier0's assert path is the recursion the
// original comment here was rightly worried about.
//
// Still on stderr too, unconditionally, because a run without -condebug has no
// console.log and an assert during load happens before there is one.
//-----------------------------------------------------------------------------
static void SourceSharp_AssertStackEmit( const char *pchLine )
{
	// stderr first: if the spew chain is what is broken, this is still said.
	fputs( pchLine, stderr );
	fflush( stderr );

	if ( !g_pPreviousSpewFunc )
		return;

	// The chain reaches our own echo, which would put this on stderr a second
	// time. It is already there. The flag is set by the caller for the whole
	// report; see SourceSharp_AssertStackSetWriting.
	g_pPreviousSpewFunc( SPEW_WARNING, pchLine );
}

//-----------------------------------------------------------------------------
// THE FRAME WALK, ONCE — lane 8e-physics.
//
// Lifted out of SourceSharp_AssertStackNotify unchanged so that the fatal-spew
// report below prints frames in the SAME form, resolved the same way. Two
// copies of a dladdr walk is two formats for addr2line to be handed, and the
// second one is always the one nobody tested.
//
// nEmitted counts the lines written and nResolved the ones carrying a module
// and an offset -- the only form addr2line can take. Both are counted rather
// than assumed, because "a report was printed" is true whether or not a single
// frame follows it.
//-----------------------------------------------------------------------------
static void SourceSharp_EmitFrames( void *const *pFrames, int nFrames,
                                    int *pnEmitted, int *pnResolved )
{
	for ( int i = 0; i < nFrames; ++i )
	{
		Dl_info info;

		// FormatFrameLine, not FormatFrame plus a second snprintf into a
		// buffer of the same size. That pair is what used to drop the newline
		// off a frame long enough to fill the buffer, running two frames into
		// one line; the helper reserves the byte for it up front. See
		// assert_stack.h, and AssertStackFormatLine in assert_stack_test.cpp
		// for the cases that pin it.
		char szFrame[ 1024 ];

		if ( dladdr( pFrames[ i ], &info ) && info.dli_fname && info.dli_fname[ 0 ]
		     && (unsigned long long)(size_t)pFrames[ i ]
		        >= (unsigned long long)(size_t)info.dli_fbase )
		{
			SourceSharpAssertStack_FormatFrameLine( szFrame, sizeof( szFrame ), i,
				info.dli_fname,
				(unsigned long long)(size_t)info.dli_fbase,
				(unsigned long long)(size_t)pFrames[ i ],
				info.dli_sname );

			if ( pnResolved )
				++(*pnResolved);
		}
		else
		{
			SourceSharpAssertStack_FormatFrameLine( szFrame, sizeof( szFrame ), i,
				NULL, 0,
				(unsigned long long)(size_t)pFrames[ i ],
				NULL );
		}

		SourceSharp_AssertStackEmit( szFrame );

		if ( pnEmitted )
			++(*pnEmitted);
	}
}

//-----------------------------------------------------------------------------
// A STACK UNDER A FATAL SPEW — lane 8e-physics, plan_entity.md step 8e.
//
// `IVP Failed at ../ivp_utility/ivu_vhash.cxx 157` is a vphysics fatal that the
// engine answers with Sys_Error. Lane diag-savesmash made the LINE visible;
// this makes the CALLERS visible, which is the half that names the defect.
// Reconstructing them last time took three core dumps, a hand walk from a saved
// rbp, and binaries that no longer existed.
//
// Unconditional -- no -assertstack, no convar. The process is about to die, so
// there is no later message to bury and no next frame to cost, and a switch
// that has to be set in ADVANCE is exactly why the last three occurrences had
// no stack.
//-----------------------------------------------------------------------------
static void SourceSharp_WriteFatalSpewStack( const char *pMsg )
{
	void *pFrames[ kAssertStackDepth ];
	const int nFrames = backtrace( pFrames, kAssertStackDepth );

	// The whole report is one write: the echo must not repeat any of it on
	// stderr, where Emit has already put it line by line.
	SourceSharp_AssertStackSetWriting( true );

	char szLine[ 1024 ];
	snprintf( szLine, sizeof( szLine ),
	          "\n[SourceSharp] FATAL SPEW STACK -- the engine will not return "
	          "from this. The message was: %s",
	          pMsg ? pMsg : "(none)\n" );
	SourceSharp_AssertStackEmit( szLine );

	int nEmitted = 0;
	int nResolved = 0;
	SourceSharp_EmitFrames( pFrames, nFrames, &nEmitted, &nResolved );

	snprintf( szLine, sizeof( szLine ),
	          "[SourceSharp] FATAL SPEW STACK -- %d frames, %d resolved; "
	          "addr2line -e <module> -f -i -C <offset>\n", nEmitted, nResolved );
	SourceSharp_AssertStackEmit( szLine );

	SourceSharp_AssertStackSetWriting( false );
}

static void SourceSharp_AssertStackNotify( const char *pchFile, int nLine,
                                           const char *pchMessage )
{
	if ( !g_bAssertStacks )
		return;

	void *pFrames[ kAssertStackDepth ];
	const int nFrames = backtrace( pFrames, kAssertStackDepth );

	// The identity of this report is the assert plus its immediate callers, so
	// twelve callers of one leaf are twelve reports and 3600 frames of one
	// caller are one. See assert_stack.h.
	//
	// The self-test is exempt. It asserts the same expression from the same line
	// through the same frames every time, so the dedup — correctly — recognises
	// the second run of it and prints nothing, and an instrument that answers
	// only the first time it is asked is not one anybody can lean on. The flag
	// is set by the self-test around its own assert and by nothing else.
	const bool bForced = ( getenv( SOURCESHARP_ASSERTSTACK_FORCE ) != NULL );

	bool bFirstOverflow = false;
	const SourceSharpAssertStackKey_t ulKey =
		SourceSharpAssertStack_Key( pchFile, nLine, pFrames, nFrames );

	if ( !bForced
	     && !SourceSharpAssertStack_ShouldReport( &g_AssertStackSeen, ulKey, &bFirstOverflow ) )
	{
		if ( bFirstOverflow )
		{
			char szFull[ 256 ];
			snprintf( szFull, sizeof( szFull ),
			          "[SourceSharp] -assertstack: %d distinct reports seen; "
			          "no more stacks will be printed\n",
			          kSourceSharpAssertStackMaxSeen );

			SourceSharp_AssertStackSetWriting( true );
			SourceSharp_AssertStackEmit( szFull );
			SourceSharp_AssertStackSetWriting( false );
		}
		return;
	}

	// Everything from here to the addr2line footer is one report, and the echo
	// must leave all of it alone: it is on stderr already, put there line by
	// line by Emit, and the trip down the spew chain exists only to reach
	// console.log.
	SourceSharp_AssertStackSetWriting( true );

	char szLine[ 1024 ];

	snprintf( szLine, sizeof( szLine ), "\n[SourceSharp] -assertstack: %s (%d) : %s\n",
	          pchFile ? pchFile : "??", nLine,
	          pchMessage ? pchMessage : "(no message)" );
	SourceSharp_AssertStackEmit( szLine );

	// Counted, not assumed.
	//
	// nEmitted is how many lines were actually written, and nResolved how many
	// of them carry a module and an offset — the only form addr2line can take.
	// The self-test reads both back, and the whole reason they exist is that
	// "an assert was reported" was the only thing it used to be able to check,
	// and that is true whether or not a single frame follows.
	int nEmitted = 0;
	int nResolved = 0;

	// Frame 0 is this function and frame 1 is tier0's notify caller; both are
	// bookkeeping. Printed anyway, and labelled, because a hook that silently
	// skips frames is a hook nobody can check.
	SourceSharp_EmitFrames( pFrames, nFrames, &nEmitted, &nResolved );

	SourceSharp_AssertStackEmit(
		"[SourceSharp] -assertstack: addr2line -e <module> -f -i -C <offset>\n" );

	SourceSharp_AssertStackSetWriting( false );

	// Published last, so a reader that sees the counter move knows the report
	// under it is complete.
	SourceSharp_AssertStackPublish( SOURCESHARP_ASSERTSTACK_FRAMES, nEmitted );
	SourceSharp_AssertStackPublish( SOURCESHARP_ASSERTSTACK_RESOLVED, nResolved );
	SourceSharp_AssertStackPublish( SOURCESHARP_ASSERTSTACK_REPORTS,
		SourceSharp_AssertStackRead( SOURCESHARP_ASSERTSTACK_REPORTS ) + 1 );
	fflush( stderr );
}

// FILE-LOCAL for SourceSharp_NotShippedLedger's reason: its one caller is
// SourceSharp_InstallAssertLogging, thirty lines below, and -assertstack is
// reached through THAT rather than directly. The header said "Installed from
// SourceSharp_InstallAssertLogging" and then declared it publicly anyway.
static void SourceSharp_InstallAssertStacks()
{
	if ( !ss::CommandLineHas( "-assertstack" ) )
		return;

	// One per process. tier0 is shared, so one hook sees both DLLs' asserts,
	// and there is no getter to chain a second one onto.
	if ( getenv( "SOURCESHARP_ASSERTSTACK_CLAIMED" ) != NULL )
		return;

	setenv( "SOURCESHARP_ASSERTSTACK_CLAIMED", "1", 0 );

	SourceSharpAssertStack_Reset( &g_AssertStackSeen );
	g_bAssertStacks = true;
	SetAssertFailedNotifyFunc( SourceSharp_AssertStackNotify );

	// Printed to the terminal DIRECTLY as well as through Msg().
	//
	// This runs as the first thing in SourceSharp_InstallAssertLogging, which is
	// BEFORE that function installs the spew hook that echoes this mod's output
	// to stdout — so through Msg() alone the line reaches console.log and
	// nowhere else, and anyone watching the terminal to check the switch took
	// effect sees nothing and concludes it did not. That happened on the first
	// run of this probe.
	Msg( "[SourceSharp] -assertstack: every failed assert will print a native backtrace\n" );
	fprintf( stderr, "[SourceSharp] -assertstack: every failed assert will print a native backtrace\n" );
	fflush( stderr );
}

void SourceSharp_InstallAssertLogging()
{
	SourceSharp_InstallAssertStacks();

	// Installed unconditionally now: the hook does two jobs and only one of
	// them is -assertlog. Echoing this mod's output to the terminal is wanted
	// on every run, and gating it on a switch meant the switch silently decided
	// whether logging was visible where anyone was looking.
	g_bDemoteAsserts = ss::CommandLineHas( "-assertlog" );

	// Once. Both DLLs initialise, and chaining this to itself would recurse.
	if ( g_pPreviousSpewFunc )
		return;

	g_bEchoOurs = SourceSharp_ClaimEcho();

	g_pPreviousSpewFunc = GetSpewOutputFunc();
	SpewOutputFunc( SourceSharp_LoggingSpewFunc );

	if ( g_bDemoteAsserts )
		Msg( "[SourceSharp] -assertlog: assertions will be logged and NOT stop the game\n" );
	else
		Msg( "[SourceSharp] assertions are FATAL: logged, flushed, then the process stops. "
		     "Pass -assertlog to survey them instead.\n" );
}

// SourceSharp_GetAssertsLogged WAS HERE: `return g_nAssertsLogged;`, the count
// of assertions -assertlog demoted to log lines. It had no caller on either
// side. The COUNTER stays -- SourceSharp_LoggingSpewFunc increments it and the
// summary line prints it, which is the reading anybody wanted; what went is the
// accessor nobody asked.

//=============================================================================//
// THE NOT-SHIPPED READOUT -- plan_entity.md step 7g.
//
// The console channel for the ledger above, and the shape 5b's
// `ss_cl_clientdll_refusals` established: the number is only evidence if a
// person can get it out of a running game. ZERO IS THE EXPECTED READING and the
// command says so out loud, because "the client did not crash" is not evidence
// that nothing reached a not-shipped path -- every one of these paths is a
// no-op whose absence nothing would notice.
//
// TWO NAMES FOR ONE COMMAND, for the reason the crossings pair has two: this
// file compiles into both DLLs and Source refuses the same ConCommand twice.
// Unlike the crossings, the two ledgers here really are SEPARATE -- one static
// per DLL -- so cs_cl_notshipped answers about client.so and cs_sv_notshipped
// about server.so.
//=============================================================================//

void SourceSharp_PrintNotShipped()
{
	const CShellNotShipped &led = SourceSharp_NotShippedLedger();

	Msg( "[SourceSharp] not-shipped ledger (%s): %d reach(es) across %d name(s)\n",
	     SOURCESHARP_SIDE_TAG, led.Total(), led.NameCount() );

	for ( int i = 0; i < led.NameCount(); i++ )
	{
		Msg( "[SourceSharp]   %-40s %6d  %s\n",
		     led.NameAt( i ), led.CountAt( i ),
		     led.ReasonAt( i ) ? led.ReasonAt( i ) : "" );
	}

	if ( led.Total() == 0 )
	{
		Msg( "[SourceSharp]   none -- nothing reached a capability or interface "
		     "this build declares it does not ship\n" );
	}

	// The declared list itself, so a reader can tell an empty ledger from an
	// empty DECLARATION. A list that lost its rows would print zero reaches
	// too, and the two are opposite findings.
	int nRows = 0;
	const ShellNotShippedRow_t *pRows = ShellNotShipped_Interfaces( &nRows );

	Msg( "[SourceSharp]   %d interface name(s) declared not shipped:\n", nRows );

	for ( int i = 0; i < nRows; i++ )
		Msg( "[SourceSharp]     %s\n", pRows[ i ].pszName );
}

#ifdef CLIENT_DLL
CON_COMMAND( cs_cl_notshipped,
	"SourceSharp: what reached a path this client build does not ship." )
{
	SourceSharp_PrintNotShipped();
}
#endif

// cs_sv_notshipped, the server's name for this readout, is a diagnostic: it
// lives in shell_diagnostics_debug.cpp, which only a debug build of server.so
// compiles. It reaches SourceSharp_PrintNotShipped through
// shell_diagnostics_commands.h.

//=============================================================================//
// THE CROSSING COUNTERS -- plan_entity.md 0c and 3.15.
//
// A "crossing" is one managed<->native transition through an ABI thunk, and the
// entity port is gated on driving the number of them down: one RunFrame per
// server frame rather than one think per entity, one ProcessUsercmds per
// command batch. Managed counts its own thunk entries with one interlocked
// increment each; this is how a console asks what it has counted.
//
// TWO NAMES FOR ONE TABLE. This file compiles into BOTH DLLs and Source refuses
// the same command registered twice, so there is a cs_sv_ and a cs_cl_ -- but
// SourceSharp.Abi lives in the DEFAULT load context, shared by both sides'
// collectible ALCs, so the counters behind them are ONE process-wide table.
// Either command prints all of it. That is what makes a client number and a
// server number comparable rather than two unrelated measurements.
//
// THE REPORT IS COMPOSED MANAGED-SIDE. The counters are keyed by op name, the
// names live beside the thunks they count, and formatting here would mean
// keeping a copy of that table in C++ -- which is the "two enumerations of the
// same bits that agreed at first and drifted" failure sourcesharp.h names
// elsewhere. This side sizes a buffer, prints it, and knows nothing else.
//=============================================================================//

//-----------------------------------------------------------------------------
// The shared body. CON_COMMAND stringifies the name it is given, so the two
// registrations below cannot share one macro -- but they can share this.
//
// bReset closes the interval: the report printed is the one just ended, and the
// counters start again at zero. That is how a measurement gets scoped to a
// stage rather than to "everything since the game started", which is the shape
// the live gate needs (reset at the top of a scenario step, read at the bottom).
//-----------------------------------------------------------------------------
#ifndef CLIENT_DLL
// E14: the server's report comes from its managed-built area. The fetch loop
// takes a plain function, so this adapts it, asking for the area on every call
// (NULL between loads, which reads as "no slot").
static int SourceSharp_ServerCrossingsReport( char *pBuffer, int nBytes, int bReset )
{
	IManagedDiagnostics *pDiagnostics = ManagedDiagnostics();
	return pDiagnostics ? pDiagnostics->CrossingsReport( pBuffer, nBytes, bReset ) : -1;
}
#else // plan_client_vtables CE9a: the client's, the same way
static int SourceSharp_ClientCrossingsReport( char *pBuffer, int nBytes, int bReset )
{
	IManagedDiagnosticsClient *pDiagnostics = ManagedDiagnosticsClient();
	return pDiagnostics ? pDiagnostics->CrossingsReport( pBuffer, nBytes, bReset ) : -1;
}
#endif

void SourceSharp_PrintCrossings( bool bReset )
{
	// Read through the accessor at the point of use, never cached in a static:
	// a hot reload replaces the whole table and a stale copy would call into
	// unloaded code. managed_runtime_server_test.cpp exists for that bug.
#ifndef CLIENT_DLL
	SourceSharpCrossingsReportFn pFn = ManagedDiagnostics() ? &SourceSharp_ServerCrossingsReport : NULL;
#else
	SourceSharpCrossingsReportFn pFn = ManagedDiagnosticsClient() ? &SourceSharp_ClientCrossingsReport : NULL;
#endif

	char *pReport = NULL;
	const SourceSharpCrossingsStatus status =
		SourceSharp_FetchCrossingsReport( pFn, bReset, &pReport );

	if ( status != SOURCESHARP_CROSSINGS_OK )
	{
		Msg( "[SourceSharp] crossings: %s\n", SourceSharp_CrossingsStatusText( status ) );
		return;
	}

	// One Msg for the whole thing rather than a line at a time: the report is
	// already newline-separated and the console's own line buffer is what would
	// interleave a per-line print with another thread's spew.
	Msg( "%s", pReport );

	if ( bReset )
		Msg( "[SourceSharp] crossings: counters reset\n" );

	free( pReport );
}

#ifdef CLIENT_DLL
CON_COMMAND( cs_cl_crossings,
             "Print the managed<->native ABI crossing counts. "
             "Pass 'reset' to print the interval and start a new one." )
{
	SourceSharp_PrintCrossings( args.ArgC() > 1 && ss::StrICmp( args.Arg( 1 ), "reset" ) == 0 );
}
#endif

// cs_sv_crossings, the server's name for this table, is a diagnostic: it lives
// in shell_diagnostics_debug.cpp, which only a debug build of server.so
// compiles. It reaches SourceSharp_PrintCrossings through
// shell_diagnostics_commands.h.
