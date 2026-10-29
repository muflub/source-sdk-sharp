//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CShellLog (shell_log.h) and the free logging calls that forward to the DLL's log.

#include "shell_log.h"
#include "shell_diagnostics.h"
#include "ss_string.h"
#include "tier0/dbg.h"

#include <cstdarg>
#include <cstdio>

#ifdef CLIENT_DLL
#define SOURCESHARP_SIDE_TAG "client"
#else
#define SOURCESHARP_SIDE_TAG "server"
#endif

// 0 = errors only, 1 = startup tracing, 2 = verbose (per-call). Named per side
// because Source rejects one ConVar registered from both DLLs.
#ifdef CLIENT_DLL
#define SOURCESHARP_VERBOSE_CVAR "cl_sourcesharp_verbose"
#else
#define SOURCESHARP_VERBOSE_CVAR "sv_sourcesharp_verbose"
#endif

// The DLL's log, which the free calls below forward to. Set by its constructor; NULL
// during static initialisation before the root is built, when the calls answer the
// defaults (verbosity 1, lines to stdout).
static CShellLog *s_pLog = NULL;

CShellLog::CShellLog()
#ifdef CLIENT_DLL
	: m_Verbose( SOURCESHARP_VERBOSE_CVAR, "1", FCVAR_NONE,
				 "SourceSharp log verbosity: 0=errors, 1=startup, 2=verbose" )
#else
	// Declared in managed code. Until it registers, the answer is the default of 1 rather
	// than the unresolved ref's 0, so the earliest lines are not silenced.
	: m_Verbose( SOURCESHARP_VERBOSE_CVAR )
#endif
{
	s_pLog = this;
}

CShellLog::~CShellLog()
{
	if ( s_pLog == this )
		s_pLog = NULL;
}

int CShellLog::Verbosity() const
{
#ifdef CLIENT_DLL
	return m_bReady ? m_Verbose.GetInt() : 1;
#else
	return ( m_bReady && m_Verbose.IsValid() ) ? m_Verbose.GetInt() : 1;
#endif
}

void CShellLog::Print( const char *pFmt, ... )
{
	char szBuf[512];
	va_list args;
	va_start( args, pFmt );
	ss::FormatV( szBuf, sizeof( szBuf ), pFmt, args );
	va_end( args );

	// Msg() is not safe during static init, so fall back to stdout until the engine is up.
	if ( m_bReady )
		Msg( "%s", szBuf );
	else
		fputs( szBuf, stdout );
}

void CShellLog::Stub( const char *pWhat, bool *pbAlreadyLogged )
{
	const int nVerbosity = Verbosity();
	if ( nVerbosity < 1 )
		return;

	if ( *pbAlreadyLogged && nVerbosity < 2 )
		return;

	*pbAlreadyLogged = true;
	Print( "[SourceSharp] STUB %s (%s) — engine called it; C# does not implement it yet\n", pWhat,
		   SOURCESHARP_SIDE_TAG );
}

void CShellLog::Trace( const char *pWhat, const char *pDetail )
{
	if ( Verbosity() < 1 )
		return;

	if ( pDetail )
		Print( "[SourceSharp] > %s (%s): %s\n", pWhat, SOURCESHARP_SIDE_TAG, pDetail );
	else
		Print( "[SourceSharp] > %s (%s)\n", pWhat, SOURCESHARP_SIDE_TAG );
}

void CShellLog::EntityCreate( const char *pClassName )
{
	if ( Verbosity() < 1 )
		return;

	Print( "[SourceSharp] entity factory: creating '%s'\n", pClassName ? pClassName : "(null)" );
}

void CShellLog::EntityCreateFailed( const char *pClassName )
{
	Print( "[SourceSharp] entity factory MISS: no factory registered for '%s'\n", pClassName ? pClassName : "(null)" );
}

void CShellLog::EntityDestroy( const char *pClassName )
{
	if ( Verbosity() < 2 )
		return;

	Print( "[SourceSharp] entity factory: destroying '%s'\n", pClassName ? pClassName : "(null)" );
}

// The free calls the macros and stock translation units make.
int SourceSharp_GetVerbosity()
{
	return s_pLog ? s_pLog->Verbosity() : 1;
}

void SourceSharp_MarkDiagnosticsReady()
{
	if ( s_pLog )
		s_pLog->MarkReady();
}

void SourceSharp_DiagPrint( const char *pFmt, ... )
{
	char szBuf[512];
	va_list args;
	va_start( args, pFmt );
	ss::FormatV( szBuf, sizeof( szBuf ), pFmt, args );
	va_end( args );

	if ( s_pLog )
		s_pLog->Print( "%s", szBuf );
	else
		fputs( szBuf, stdout );
}

void SourceSharp_LogStub( const char *pWhat, bool *pbAlreadyLogged )
{
	if ( s_pLog )
		s_pLog->Stub( pWhat, pbAlreadyLogged );
	else if ( !*pbAlreadyLogged )
	{
		*pbAlreadyLogged = true;
		SourceSharp_DiagPrint( "[SourceSharp] STUB %s (%s) — engine called it; C# does not implement it yet\n", pWhat,
							   SOURCESHARP_SIDE_TAG );
	}
}

void SourceSharp_Trace( const char *pWhat, const char *pDetail )
{
	if ( s_pLog )
		s_pLog->Trace( pWhat, pDetail );
	else if ( pDetail )
		SourceSharp_DiagPrint( "[SourceSharp] > %s (%s): %s\n", pWhat, SOURCESHARP_SIDE_TAG, pDetail );
	else
		SourceSharp_DiagPrint( "[SourceSharp] > %s (%s)\n", pWhat, SOURCESHARP_SIDE_TAG );
}

void SourceSharp_LogEntityCreate( const char *pClassName )
{
	if ( s_pLog )
		s_pLog->EntityCreate( pClassName );
}

void SourceSharp_LogEntityCreateFailed( const char *pClassName )
{
	if ( s_pLog )
		s_pLog->EntityCreateFailed( pClassName );
	else
		SourceSharp_DiagPrint( "[SourceSharp] entity factory MISS: no factory registered for '%s'\n",
							   pClassName ? pClassName : "(null)" );
}

#ifndef CLIENT_DLL
void SourceSharp_LogEntityDestroy( const char *pClassName )
{
	if ( s_pLog )
		s_pLog->EntityDestroy( pClassName );
}
#endif // !CLIENT_DLL

#ifdef CLIENT_DLL
void ShellNotShipped_Warning( const char *pName, const char *pReason, int nTotal )
{
	SourceSharp_DiagPrint( "[SourceSharp] NOT SHIPPED (%s): %s -- %s [total %d]\n", SOURCESHARP_SIDE_TAG,
						   pName ? pName : "(null)", pReason ? pReason : "(no reason given)", nTotal );
}

void SourceSharp_NotShipped( const char *pCapability, const char *pReason )
{
	if ( s_pLog )
		s_pLog->NotShipped().Reached( pCapability, pReason );
}

// The not-shipped ledger for the dev API: one capability per index, with its reason
// and reach count. Returns the number of names, and the total through pOutTotal.
int32_t SourceSharp_ClientNotShipped( int32_t iIndex, char *pOutName, int32_t nNameBytes, char *pOutReason,
									  int32_t nReasonBytes, int32_t *pOutCount, int32_t *pOutTotal )
{
	if ( pOutTotal )
		*pOutTotal = 0;
	if ( !s_pLog )
		return 0;

	const CShellNotShipped &ledger = s_pLog->NotShipped();
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
#endif // CLIENT_DLL

// The not-shipped ledger readout. Zero is the expected reading. The ledger is per DLL,
// so the client and server commands answer about their own DLL.

void SourceSharp_PrintNotShipped()
{
	if ( s_pLog )
		s_pLog->PrintNotShipped();
}

void CShellLog::PrintNotShipped() const
{
	const CShellNotShipped &led = m_NotShipped;

	Msg( "[SourceSharp] not-shipped ledger (%s): %d reach(es) across %d name(s)\n", SOURCESHARP_SIDE_TAG, led.Total(),
		 led.NameCount() );

	for ( int i = 0; i < led.NameCount(); i++ )
	{
		Msg( "[SourceSharp]   %-40s %6d  %s\n", led.NameAt( i ), led.CountAt( i ),
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
		Msg( "[SourceSharp]     %s\n", pRows[i].pszName );
}
