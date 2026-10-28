//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON h  the CoreCLR host and bootstrap dispatch
#ifdef CLIENT_DLL
#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
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
#include "imanaged_areas.h"
#include "imanaged_areas_client.h"
#include "managed_convar_ref.h"
#include <stdarg.h>
#include <stdio.h>
#include <string.h>
#include "ss_platform.h"
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
// Declared rather than included: <gcov.h> exists only on a coverage build, and a
// linkage specification is not allowed at block scope.
extern "C" void __gcov_dump( void );
#endif

// 0 = errors only, 1 = startup tracing, 2 = verbose (per-call). Named per side
// because Source rejects one ConVar registered from both DLLs.
#ifdef CLIENT_DLL
	#define SOURCESHARP_VERBOSE_CVAR "cl_sourcesharp_verbose"
#else
	#define SOURCESHARP_VERBOSE_CVAR "sv_sourcesharp_verbose"
#endif

// Constant-initialised, so it is valid from program start: static constructors that
// run during dlopen reach this before ConVars are registered.
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
// Declared in managed code. Until it registers, the answer is g_nVerbosity's default
// of 1 rather than the unresolved ref's 0, so the earliest lines are not silenced.
static CManagedConVarRef sourcesharp_verbose( SOURCESHARP_VERBOSE_CVAR );

int SourceSharp_GetVerbosity()
{
	if ( g_bTier0Ready && sourcesharp_verbose.IsValid() )
		return sourcesharp_verbose.GetInt();
	return g_nVerbosity;
}

void SourceSharp_MarkDiagnosticsReady()
{
	g_bTier0Ready = true;
}
#endif

// Msg() is not safe during static init, so fall back to stdout until the engine is up.
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

void SourceSharp_LogStub( const char *pWhat, bool *pbAlreadyLogged )
{
	const int nVerbosity = SourceSharp_GetVerbosity();
	if ( nVerbosity < 1 )
		return;

	if ( *pbAlreadyLogged && nVerbosity < 2 )
		return;

	*pbAlreadyLogged = true;
	SourceSharp_DiagPrint( "[SourceSharp] STUB %s (%s) — engine called it; C# does not implement it yet\n",
		pWhat, SOURCESHARP_SIDE_TAG );
}

void SourceSharp_Trace( const char *pWhat, const char *pDetail )
{
	if ( SourceSharp_GetVerbosity() < 1 )
		return;

	if ( pDetail )
		SourceSharp_DiagPrint( "[SourceSharp] > %s (%s): %s\n", pWhat, SOURCESHARP_SIDE_TAG, pDetail );
	else
		SourceSharp_DiagPrint( "[SourceSharp] > %s (%s)\n", pWhat, SOURCESHARP_SIDE_TAG );
}

void SourceSharp_LogEntityCreate( const char *pClassName )
{
	if ( SourceSharp_GetVerbosity() < 1 )
		return;

	SourceSharp_DiagPrint( "[SourceSharp] entity factory: creating '%s'\n",
		pClassName ? pClassName : "(null)" );
}

// One ledger per DLL. This file has no owning object, and its reachers are stock
// translation units.
static CShellNotShipped g_NotShipped;

#ifdef CLIENT_DLL
void ShellNotShipped_Warning( const char *pName, const char *pReason, int nTotal )
{
	SourceSharp_DiagPrint( "[SourceSharp] NOT SHIPPED (%s): %s -- %s [total %d]\n",
		SOURCESHARP_SIDE_TAG,
		pName   ? pName   : "(null)",
		pReason ? pReason : "(no reason given)",
		nTotal );
}
#endif

#ifdef CLIENT_DLL
void SourceSharp_NotShipped( const char *pCapability, const char *pReason )
{
	g_NotShipped.Reached( pCapability, pReason );
}
#endif

static const CShellNotShipped &SourceSharp_NotShippedLedger()
{
	return g_NotShipped;
}

#ifdef CLIENT_DLL
// The not-shipped ledger for the dev API: one capability per index, with its reason
// and reach count. Returns the number of names, and the total through pOutTotal.
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
			ss::StrCopy( pOutName, ledger.NameAt( iIndex ) ? ledger.NameAt( iIndex ) : "", nNameBytes );
		if ( pOutReason && nReasonBytes > 0 )
			ss::StrCopy( pOutReason, ledger.ReasonAt( iIndex ) ? ledger.ReasonAt( iIndex ) : "", nReasonBytes );
		if ( pOutCount )
			*pOutCount = ledger.CountAt( iIndex );
	}

	return ledger.NameCount();
}
#endif

#ifdef CLIENT_DLL
// Always false: the client-side SourceTV camera is not shipped. engine->IsHLTV() is
// still asked so a session that really was watching HLTV records the refusal.
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

// SourceSharp's own tables always dump their props: they must match across the
// boundary, and the client dump runs before command-line ConVars are applied.
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

// Walks the DLL's own interface registry, which is what the engine's CreateInterface sees.
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

// Asserts that log instead of stopping the game. Whether an assert breaks is decided by
// the spew handler's return value, so this handler answers SPEW_CONTINUE for asserts.
// Opt-in through -assertlog.

static SpewOutputFunc_t g_pPreviousSpewFunc = NULL;

// Deep enough for the frame loop plus a render path plus the leaf.
enum { kAssertStackDepth = 48 };

// Set by the assertstack self-test around its own deliberate assert.
#define SOURCESHARP_ASSERTSTACK_FORCE    "SOURCESHARP_ASSERTSTACK_FORCE"

static bool g_bDemoteAsserts = false;

static int g_nAssertsLogged = 0;

// Set when this DLL claimed the echo of our own lines to the launching terminal.
static bool g_bEchoOurs = false;

// Set while an -assertstack report is written, so its lines are not echoed twice.
// In the environment because the two hooks may be in different DLLs.
#define SOURCESHARP_ASSERTSTACK_WRITING "SOURCESHARP_ASSERTSTACK_WRITING"

static bool SourceSharp_AssertStackWriting()
{
	return getenv( SOURCESHARP_ASSERTSTACK_WRITING ) != NULL;
}

static void SourceSharp_AssertStackSetWriting( bool bWriting )
{
	if ( bWriting )
		SS_SetEnv( SOURCESHARP_ASSERTSTACK_WRITING, "1", 1 );
	else
		SS_UnsetEnv( SOURCESHARP_ASSERTSTACK_WRITING );
}

// One echo per process: both DLLs install the hook and chain, so every message reaches
// both. A claim in the environment crosses the DLL boundary.
static bool SourceSharp_ClaimEcho()
{
	if ( getenv( "SOURCESHARP_ECHO_CLAIMED" ) != NULL )
		return false;

	SS_SetEnv( "SOURCESHARP_ECHO_CLAIMED", "1", 0 );
	return true;
}

// A stack under a fatal spew line, written once per DLL. The engine answers SPEW_ERROR
// with Sys_Error and does not return, so this is the last chance to say it.
static bool g_bFatalSpewStackWritten = false;
static void SourceSharp_WriteFatalSpewStack( const char *pMsg );

static void SourceSharp_EchoOurs( SpewType_t spewType, const tchar *pMsg )
{
	if ( !pMsg )
		return;

	const bool bOurs  = ( strncmp( pMsg, "[SourceSharp", 12 ) == 0 );

	// SPEW_ERROR is fatal, so it is echoed whatever its prefix.
	const bool bFatal = ( spewType == SPEW_ERROR );

	if ( !SourceSharp_SpewShouldEcho( g_bEchoOurs, SourceSharp_AssertStackWriting(),
	                                  bOurs, bFatal ) )
		return;

	FILE *pStream = ( spewType == SPEW_WARNING || spewType == SPEW_ERROR
	                  || spewType == SPEW_ASSERT ) ? stderr : stdout;

	if ( !bOurs )
		fputs( "[SourceSharp] FATAL SPEW from outside this DLL follows; the "
		       "engine answers SPEW_ERROR with Sys_Error and does not return:\n",
		       pStream );

	fputs( pMsg, pStream );
	fflush( pStream );

	// And who called it: the line alone names the assert, never the caller.
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

	// The self-test's deliberate assert must not end the process.
	const bool bDeliberate = ( getenv( SOURCESHARP_ASSERTSTACK_FORCE ) != NULL );

	// tier0 shows the assert dialog only when this returns SPEW_DEBUGGER, so the dialog
	// is prevented by never returning it for an assert.
	if ( SourceSharp_AssertShouldBeFatal( spewType == SPEW_ASSERT, /*bFatal=*/true,
	                                     g_bDemoteAsserts, bDeliberate ) )
	{
		++g_nAssertsLogged;

		// SPEW_WARNING, not SPEW_ERROR: the engine answers SPEW_ERROR with Sys_Error,
		// which takes the .NET runtime down before the report below runs. Through the
		// previous handler so it lands in console.log in order.
		if ( g_pPreviousSpewFunc )
			g_pPreviousSpewFunc( SPEW_WARNING, pMsg );

		SourceSharp_FatalReport( "ASSERT", pMsg );
	}

	if ( spewType == SPEW_ASSERT && g_bDemoteAsserts )
	{
		++g_nAssertsLogged;

		// Through the previous handler so it lands in console.log in order.
		if ( g_pPreviousSpewFunc )
			g_pPreviousSpewFunc( SPEW_WARNING, pMsg );

		// And straight to stderr, not through the echo: an engine assert lacks our
		// prefix, and the echo is claimed by one DLL while the assert usually fires
		// in the other.
		fprintf( stderr, "[SourceSharp] ASSERT #%d (demoted by -assertlog): %s",
		         g_nAssertsLogged, pMsg ? pMsg : "(no message)\n" );
		fflush( stderr );

		return SPEW_CONTINUE;
	}

	return g_pPreviousSpewFunc ? g_pPreviousSpewFunc( spewType, pMsg ) : SPEW_CONTINUE;
}

// -assertstack: a backtrace for every failed assert in the process. tier0's notify
// hook has a setter and no getter, so it is installed once per process (the same
// environment claim the echo uses).

static bool g_bAssertStacks = false;
static SourceSharpAssertStackSeen_t g_AssertStackSeen;

// Results are published in the environment because the hook is claimed per process
// while the self-test is registered per DLL.
#define SOURCESHARP_ASSERTSTACK_REPORTS  "SOURCESHARP_ASSERTSTACK_REPORTS"
#define SOURCESHARP_ASSERTSTACK_FRAMES   "SOURCESHARP_ASSERTSTACK_FRAMES"
#define SOURCESHARP_ASSERTSTACK_RESOLVED "SOURCESHARP_ASSERTSTACK_RESOLVED"

static void SourceSharp_AssertStackPublish( const char *pchName, long long llValue )
{
	char szValue[ 32 ];
	snprintf( szValue, sizeof( szValue ), "%lld", llValue );
	SS_SetEnv( pchName, szValue, 1 );
}

static long long SourceSharp_AssertStackRead( const char *pchName )
{
	return SourceSharpAssertStack_ParseCounter( getenv( pchName ) );
}

// One line of a report, to stderr and through the previous spew handler so it also
// reaches console.log. SPEW_WARNING, because an assert-typed spew from inside tier0's
// assert path would recurse.
static void SourceSharp_AssertStackEmit( const char *pchLine )
{
	fputs( pchLine, stderr );
	fflush( stderr );

	if ( !g_pPreviousSpewFunc )
		return;

	// The caller sets the writing flag, so our own echo skips this.
	g_pPreviousSpewFunc( SPEW_WARNING, pchLine );
}

// Writes resolved frames in the one format addr2line takes. nEmitted counts lines
// written and nResolved those carrying a module and offset.
static void SourceSharp_EmitFrames( void *const *pFrames, int nFrames,
                                    int *pnEmitted, int *pnResolved )
{
	for ( int i = 0; i < nFrames; ++i )
	{
		SSModuleInfo info;

		// FormatFrameLine reserves the newline, so a long frame cannot run into the next.
		char szFrame[ 1024 ];

		if ( SS_ModuleOfAddress( pFrames[ i ], &info ) && info.szPath[ 0 ]
		     && (unsigned long long)(size_t)pFrames[ i ]
		        >= (unsigned long long)(size_t)info.pBase )
		{
			SourceSharpAssertStack_FormatFrameLine( szFrame, sizeof( szFrame ), i,
				info.szPath,
				(unsigned long long)(size_t)info.pBase,
				(unsigned long long)(size_t)pFrames[ i ],
				info.pszSymbol );

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

// A stack under a fatal spew, unconditionally: the process is about to die, so there
// is nothing to bury and no switch could be set in advance.
static void SourceSharp_WriteFatalSpewStack( const char *pMsg )
{
	void *pFrames[ kAssertStackDepth ];
	const int nFrames = SS_Backtrace( pFrames, kAssertStackDepth );

	// One report: the echo must not repeat it on stderr.
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
	const int nFrames = SS_Backtrace( pFrames, kAssertStackDepth );

	// A report's identity is the assert plus its immediate callers (assert_stack.h).
	// The self-test is exempt from the dedup so it answers every time.
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

	// One report: the echo must not repeat it on stderr.
	SourceSharp_AssertStackSetWriting( true );

	char szLine[ 1024 ];

	snprintf( szLine, sizeof( szLine ), "\n[SourceSharp] -assertstack: %s (%d) : %s\n",
	          pchFile ? pchFile : "??", nLine,
	          pchMessage ? pchMessage : "(no message)" );
	SourceSharp_AssertStackEmit( szLine );

	int nEmitted = 0;
	int nResolved = 0;

	SourceSharp_EmitFrames( pFrames, nFrames, &nEmitted, &nResolved );

	SourceSharp_AssertStackEmit(
		"[SourceSharp] -assertstack: addr2line -e <module> -f -i -C <offset>\n" );

	SourceSharp_AssertStackSetWriting( false );

	// Published last, so a reader that sees the counter move knows the report is complete.
	SourceSharp_AssertStackPublish( SOURCESHARP_ASSERTSTACK_FRAMES, nEmitted );
	SourceSharp_AssertStackPublish( SOURCESHARP_ASSERTSTACK_RESOLVED, nResolved );
	SourceSharp_AssertStackPublish( SOURCESHARP_ASSERTSTACK_REPORTS,
		SourceSharp_AssertStackRead( SOURCESHARP_ASSERTSTACK_REPORTS ) + 1 );
	fflush( stderr );
}

static void SourceSharp_InstallAssertStacks()
{
	if ( !ss::CommandLineHas( "-assertstack" ) )
		return;

	// One per process: tier0 is shared and has no getter to chain onto.
	if ( getenv( "SOURCESHARP_ASSERTSTACK_CLAIMED" ) != NULL )
		return;

	SS_SetEnv( "SOURCESHARP_ASSERTSTACK_CLAIMED", "1", 0 );

	SourceSharpAssertStack_Reset( &g_AssertStackSeen );
	g_bAssertStacks = true;
	SetAssertFailedNotifyFunc( SourceSharp_AssertStackNotify );

	// Also straight to stderr: the echo hook is not installed yet.
	Msg( "[SourceSharp] -assertstack: every failed assert will print a native backtrace\n" );
	fprintf( stderr, "[SourceSharp] -assertstack: every failed assert will print a native backtrace\n" );
	fflush( stderr );
}

void SourceSharp_InstallAssertLogging()
{
	SourceSharp_InstallAssertStacks();

	// The hook is always installed: it also echoes our output to the terminal.
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

// The not-shipped ledger readout. Zero is the expected reading. The ledger is per DLL,
// so the client and server commands answer about their own DLL.

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

// Managed<->native crossing counters: one process-wide table, so the client and server
// commands print the same counters.
#ifndef CLIENT_DLL
// The fetch loop takes a plain function; the area is asked for on every call.
static int SourceSharp_ServerCrossingsReport( char *pBuffer, int nBytes, int bReset )
{
	IManagedDiagnostics *pDiagnostics = ManagedDiagnostics();
	return pDiagnostics ? pDiagnostics->CrossingsReport( pBuffer, nBytes, bReset ) : -1;
}
#else
static int SourceSharp_ClientCrossingsReport( char *pBuffer, int nBytes, int bReset )
{
	IManagedDiagnosticsClient *pDiagnostics = ManagedDiagnosticsClient();
	return pDiagnostics ? pDiagnostics->CrossingsReport( pBuffer, nBytes, bReset ) : -1;
}
#endif

void SourceSharp_PrintCrossings( bool bReset )
{
	// Read through the accessor, never cached: a hot reload replaces the table.
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

	// One Msg for the whole report, so another thread's spew cannot interleave.
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
