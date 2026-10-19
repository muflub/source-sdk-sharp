//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — tier0's spew and assert entry points, in server.so ==//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// plan_server_novalve.md P4: server.so stops linking libtier0. Every server
// object that logs or asserts calls tier0 through public/tier0/dbg.h (the
// Assert macro alone expands to five tier0 calls, in 102 objects), and those
// headers stay until P5. So server.so DEFINES the tier0 functions it calls,
// here, with its own behaviour; -fvisibility=hidden plus the version script
// keep them inside server.so, so its own calls bind to these and the
// libtier0.so the engine loaded is untouched.
//
// THE SPEW SLOT IS server.so's OWN. SpewOutputFunc / GetSpewOutputFunc set and
// read a function local to this DLL, and everything below formats its text
// and hands it to that function. Its default hands the line on to whatever
// spew function is current in the libtier0.so the engine loaded, found at
// runtime (loaded_symbol.h): that function is the engine's, and it is what
// writes the console AND console.log. ICvar::ConsolePrintf reaches the console
// only; a gate run with it lost every native line from console.log. With no
// libtier0 loaded (the tests), the default falls back to ICvar.
//
// A line is handed on as a message or a warning, never as SPEW_ASSERT or
// SPEW_ERROR: tier0's slot answers those with SPEW_DEBUGGER or SPEW_ABORT,
// and what an assert or an error does is decided here and in shell_diagnostics,
// not by the engine's answer.
// shell_diagnostics installs its hook into this slot exactly as it installed
// it into tier0's, so the assert policy (fatal by default, -assertlog demotes,
// the self-test's deliberate assert survives) is unchanged, and a fatal one
// ends in SourceSharp_FatalReport.
//
// Error() ends there too, as the user designed for a failed assert: the
// report and the stack to stderr and assert-<pid>.txt, exit -3.
//
// Without libtier0 and before the engine's ICvar is connected there is nowhere
// to write, so early lines are held and written out, in order, by the first
// line after it.
//=============================================================================//

#include "tier0/dbg.h"
#include "tier0/mem.h"         // MemAllocOOMError
#include "icvar.h"
#include "Color.h"
#include "tier1/convar.h"

#include "fatal_report.h"
#include "loaded_symbol.h"
#include "tier0_spew_port.h"
#include "ss_platform.h"	// SS_BIND_IMPORT
#ifdef _MSC_VER
// This file defines functions the engine DLLs' headers declare dllimport
// (C4273); SS_BIND_IMPORT at the end binds this module's calls to them.
#pragma warning( disable : 4273 )
#endif

#include <atomic>
#include <mutex>
#include <stdarg.h>
#include <stdio.h>
#include <string.h>
#include <string>

extern ICvar *g_pCVar;

namespace
{
// Where an assert's text came from: _SpewInfo sets it, _SpewMessage reads it,
// on the same thread, as tier0's did.
thread_local SpewType_t t_Type = SPEW_MESSAGE;
thread_local const tchar *t_pFile = nullptr;
thread_local int t_nLine = 0;

std::mutex s_EarlyLock;
std::string s_Early;   // lines spewed before the console existed

AssertFailedNotifyFunc_t s_pAssertNotify = nullptr;

void WriteToConsole( SpewType_t type, const char *pText )
{
	switch ( type )
	{
	case SPEW_WARNING:
		g_pCVar->ConsoleColorPrintf( Color( 255, 255, 0, 255 ), "%s", pText );
		break;
	case SPEW_ASSERT:
	case SPEW_ERROR:
		g_pCVar->ConsoleColorPrintf( Color( 255, 64, 64, 255 ), "%s", pText );
		break;
	default:
		g_pCVar->ConsolePrintf( "%s", pText );
		break;
	}
}

SpewRetval_t ConsoleSpew( SpewType_t type, const tchar *pText );

// tier0's GetSpewOutputFunc in the libtier0.so the engine loaded, or NULL.
// Kept once found: engine.so needs that libtier0, so it outlives server.so. A
// miss is not kept, so a libtier0 that arrives later is still found.
using GetSpewOutputFunc_t = SpewOutputFunc_t ( * )();
std::atomic< GetSpewOutputFunc_t > s_pEngineGetSpew{ nullptr };

SpewOutputFunc_t EngineSpew()
{
	GetSpewOutputFunc_t pGet = s_pEngineGetSpew.load( std::memory_order_acquire );
	if ( !pGet )
	{
		pGet = (GetSpewOutputFunc_t)ss::FindLoadedSymbol( "libtier0.so", "GetSpewOutputFunc" );
		if ( !pGet )
			return nullptr;
		s_pEngineGetSpew.store( pGet, std::memory_order_release );
	}

	// Read on every line: the engine may change its slot at any time.
	SpewOutputFunc_t pSpew = pGet();
	return pSpew == ConsoleSpew ? nullptr : pSpew;
}

SpewType_t HandedOnAs( SpewType_t type )
{
	switch ( type )
	{
	case SPEW_ASSERT:
	case SPEW_ERROR:
	case SPEW_WARNING:
		return SPEW_WARNING;
	default:
		return SPEW_MESSAGE;
	}
}

// Set while a line is in the engine's slot. A slot that leads back here
// (someone put one of ours into tier0's) would recurse without end; the second
// pass writes to ICvar instead.
thread_local bool t_bInEngine = false;

void HandToEngine( SpewOutputFunc_t pEngine, SpewType_t type, const char *pText )
{
	t_bInEngine = true;
	pEngine( type, pText );
	t_bInEngine = false;
}

// The default spew function: the engine's tier0 slot, else the engine console,
// else held until there is one.
SpewRetval_t ConsoleSpew( SpewType_t type, const tchar *pText )
{
	if ( !pText )
		return SPEW_CONTINUE;

	SpewOutputFunc_t pEngine = t_bInEngine ? nullptr : EngineSpew();
	if ( !pEngine && !g_pCVar )
	{
		std::lock_guard< std::mutex > hold( s_EarlyLock );
		s_Early += pText;
		return SPEW_CONTINUE;
	}

	{
		std::lock_guard< std::mutex > hold( s_EarlyLock );
		if ( !s_Early.empty() )
		{
			if ( pEngine )
				HandToEngine( pEngine, SPEW_MESSAGE, s_Early.c_str() );
			else
				g_pCVar->ConsolePrintf( "%s", s_Early.c_str() );
			s_Early.clear();
		}
	}

	if ( pEngine )
		HandToEngine( pEngine, HandedOnAs( type ), pText );
	else
		WriteToConsole( type, pText );
	return SPEW_CONTINUE;
}

SpewOutputFunc_t s_pSpew = ConsoleSpew;

SpewRetval_t SpewV( SpewType_t type, const tchar *pFormat, va_list args )
{
	char szText[ 4096 ];
	vsnprintf( szText, sizeof( szText ), pFormat, args );
	return s_pSpew( type, szText );
}

bool DeveloperAt( int level )
{
	if ( !g_pCVar )
		return false;
	const ConVar *pDeveloper = g_pCVar->FindVar( "developer" );
	return pDeveloper && pDeveloper->GetInt() >= level;
}
}

//-----------------------------------------------------------------------------
// The spew slot.
//-----------------------------------------------------------------------------
void SpewOutputFunc( SpewOutputFunc_t func )
{
	s_pSpew = func ? func : ConsoleSpew;
}

SpewOutputFunc_t GetSpewOutputFunc( void )
{
	return s_pSpew;
}

//-----------------------------------------------------------------------------
// Assert. The macro calls _SpewInfo then _SpewMessage, then the notify
// function, then asks about a dialog only if the spew answered SPEW_DEBUGGER,
// which nothing here does.
//-----------------------------------------------------------------------------
void _SpewInfo( SpewType_t type, const tchar *pFile, int line )
{
	t_Type = type;
	t_pFile = pFile;
	t_nLine = line;
}

SpewRetval_t _SpewMessage( const tchar *pMsg, ... )
{
	char szText[ 4096 ];
	int n = 0;

	// tier0's assert line: "file (line) : message\n".
	if ( t_Type == SPEW_ASSERT && t_pFile )
		n = snprintf( szText, sizeof( szText ), "%s (%d) : ", t_pFile, t_nLine );

	if ( n < 0 || n >= (int)sizeof( szText ) )
		n = 0;

	va_list args;
	va_start( args, pMsg );
	vsnprintf( szText + n, sizeof( szText ) - n, pMsg, args );
	va_end( args );

	if ( t_Type == SPEW_ASSERT )
	{
		const size_t len = strlen( szText );
		if ( len + 1 < sizeof( szText ) && ( len == 0 || szText[ len - 1 ] != '\n' ) )
		{
			szText[ len ] = '\n';
			szText[ len + 1 ] = 0;
		}
	}

	const SpewType_t type = t_Type;
	t_Type = SPEW_MESSAGE;
	return s_pSpew( type, szText );
}

bool ShouldUseNewAssertDialog()
{
	return false;
}

// libmathlib.a asks before two of its asserts. Nothing hushes them: whether an
// assert stops the game is shell_diagnostics' policy (-assertlog), not this.
bool HushAsserts()
{
	return false;
}

bool DoNewAssertDialog( const tchar *pFile, int line, const tchar *pExpression )
{
	return false;
}

void SetAssertFailedNotifyFunc( AssertFailedNotifyFunc_t func )
{
	s_pAssertNotify = func;
}

void CallAssertFailedNotifyFunc( const char *pchFile, int nLine, const char *pchMessage )
{
	if ( s_pAssertNotify )
		s_pAssertNotify( pchFile, nLine, pchMessage );
}

//-----------------------------------------------------------------------------
// Logging.
//-----------------------------------------------------------------------------
void Msg( const tchar *pMsg, ... )
{
	va_list args;
	va_start( args, pMsg );
	SpewV( SPEW_MESSAGE, pMsg, args );
	va_end( args );
}

void Warning( const tchar *pMsg, ... )
{
	va_list args;
	va_start( args, pMsg );
	SpewV( SPEW_WARNING, pMsg, args );
	va_end( args );
}

void Tier0_DevSpewV( int level, SpewType_t type, const tchar *pMsg, va_list args )
{
	if ( DeveloperAt( level ) )
		SpewV( type, pMsg, args );
}

void DevMsg( int level, const tchar *pMsg, ... )
{
	va_list args;
	va_start( args, pMsg );
	Tier0_DevSpewV( level, SPEW_MESSAGE, pMsg, args );
	va_end( args );
}

// The C++-linkage overloads: their import names are mangled, so SS_BIND_IMPORT
// cannot bind them. Neither DLL links tier0.lib (client.dll since plan_client_
// novalve C9), so the linker binds each import of one to these (LNK4217).
void DevMsg( const tchar *pMsg, ... )
{
	if ( !DeveloperAt( 1 ) )
		return;
	va_list args;
	va_start( args, pMsg );
	SpewV( SPEW_MESSAGE, pMsg, args );
	va_end( args );
}

void DevWarning( const tchar *pMsg, ... )
{
	if ( !DeveloperAt( 1 ) )
		return;
	va_list args;
	va_start( args, pMsg );
	SpewV( SPEW_WARNING, pMsg, args );
	va_end( args );
}

//-----------------------------------------------------------------------------
// Error: said, then the process ends -- the user's fatal report.
//-----------------------------------------------------------------------------
void Error( const tchar *pMsg, ... )
{
	char szText[ 4096 ];
	va_list args;
	va_start( args, pMsg );
	vsnprintf( szText, sizeof( szText ), pMsg, args );
	va_end( args );

	s_pSpew( SPEW_ERROR, szText );
	SourceSharp_FatalReport( "ERROR", szText );
}

//-----------------------------------------------------------------------------
// MemAllocOOMError: tier0/memalloc.h's POSIX allocation inlines (the
// NO_MALLOC_OVERRIDE arm, plan_server_novalve.md P5 step 5) call it when malloc
// or memalign answers NULL. tier0's ends the process; so does this, through
// Error and the fatal report.
//-----------------------------------------------------------------------------
void MemAllocOOMError( size_t nSize )
{
	Error( "out of memory: an allocation of %zu bytes failed\n", nSize );
}

// Every call in this module of the functions above, made through the
// import pointer, reaches these definitions (ss_platform.h).
SS_BIND_IMPORT( Msg )
SS_BIND_IMPORT( Warning )
SS_BIND_IMPORT( Error )
SS_BIND_IMPORT( _SpewInfo )
SS_BIND_IMPORT( _SpewMessage )
SS_BIND_IMPORT( SpewOutputFunc )
SS_BIND_IMPORT( GetSpewOutputFunc )
SS_BIND_IMPORT( ShouldUseNewAssertDialog )
SS_BIND_IMPORT( HushAsserts )
SS_BIND_IMPORT( DoNewAssertDialog )
SS_BIND_IMPORT( SetAssertFailedNotifyFunc )
SS_BIND_IMPORT( CallAssertFailedNotifyFunc )
SS_BIND_IMPORT( MemAllocOOMError )
SS_BIND_IMPORT_AS( DevMsg, void ( * )( int, const tchar *, ... ) )
