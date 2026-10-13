//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
//
// The engine holds the installed IConsoleDisplayFunc and calls its three
// methods through that vtable, on the printing thread, from inside the
// engine's own console writes. Install/remove are the only decisions here;
// forwarding is pure carry (docs/todo.md's console capture item).
//
// console-batch (2026-10-11): a print is appended to this DLL's console line
// buffer (console_line_buffer.h) and crosses nothing; the managed side drains
// the buffer itself, through the pointer handed to its area once per load
// (ConsoleBufferAttach). One crossing per line was 13 per server frame across
// a map change, the whole of the nightly gate's map-change overrun.
//=============================================================================//


#include "basetypes.h"
#include "icvar.h"
#include "Color.h"
#ifdef CLIENT_DLL
#include "imanaged_areas_client.h"
#else
#include "imanaged_areas.h"
#endif
#include "console_display_forward.h"
#include "console_line_buffer.h"

// The attach flag's two operations, spelled per compiler as
// console_line_buffer.h spells its lock word's (no __atomic under MSVC).
static inline bool AttachFlagExchange( bool *pFlag, bool bValue )
{
#ifdef _MSC_VER
	return _InterlockedExchange8( (volatile char *)pFlag, (char)bValue ) != 0;
#else
	return __atomic_exchange_n( pFlag, bValue, __ATOMIC_ACQ_REL );
#endif
}

static inline void AttachFlagStoreRelease( bool *pFlag, bool bValue )
{
#ifdef _MSC_VER
	_ReadWriteBarrier();
	*(volatile bool *)pFlag = bValue;
#else
	__atomic_store_n( pFlag, bValue, __ATOMIC_RELEASE );
#endif
}

namespace
{

/// The one forwarder per DLL: the engine's display-func list holds a raw
/// pointer, so a second installation would double every line.
class CConsoleDisplayForward final : public IConsoleDisplayFunc
{
public:
	void ColorPrint( const Color &color, const char *pMessage ) override { Forward( pMessage, IsEngineWarningColor( color ) ? 1 : 0 ); }
	void Print( const char *pMessage ) override { Forward( pMessage, 0 ); }
	void DPrint( const char *pMessage ) override { Forward( pMessage, 0 ); }

	CConsoleDisplayForward() = default;
	CConsoleDisplayForward( const CConsoleDisplayForward & ) = delete;
	CConsoleDisplayForward &operator=( const CConsoleDisplayForward & ) = delete;

	/// Hands the buffer to this load's area, once per install; false outside a load.
	bool Attach();
	void Detach() { AttachFlagStoreRelease( &m_bAttached, false ); }

private:
	void Forward( const char *pText, int nWarning );

	// The lines, and their one sequence, outlive a managed reload: the
	// forwarder is this DLL's, not the load's. 1 MiB (2 x 512 KiB halves).
	CConsoleLineBuffer m_Buffer;
	bool m_bAttached = false;

	// Every spew reaches ColorPrint, a Msg too (the engine's spew handler); its colour is the
	// spew type's: warning (255,90,90), assert (255,20,20), error (20,70,255). Read back, not decided.
	static bool IsEngineWarningColor( const Color &c )
	{
		return c.r() == 255 ? ( c.g() == 90 && c.b() == 90 ) || ( c.g() == 20 && c.b() == 20 )
		                    : c.r() == 20 && c.g() == 70 && c.b() == 255;
	}
};

CConsoleDisplayForward g_Forwarder;
ICvar *g_pInstalledCvar = NULL;

} // namespace

void CConsoleDisplayForward::Forward( const char *pText, int nWarning )
{
	// Installed-or-not is the ONE piece of state forwarding consults: it is
	// the lifetime decision install/remove already made, and a pointer the
	// engine captured before a Remove must stop re-entering this DLL.
	if ( !g_pInstalledCvar || !pText )
	{
		return;
	}
	// Outside a load (before the first acquire, after the last release,
	// during shutdown) there is no reader, and the line is dropped as it was
	// before the buffer. A load not yet handed the buffer (an install made
	// before its area was acquired) is handed it here, once.
	if ( !Attach() )
	{
		return;
	}
	m_Buffer.Append( pText, nWarning );
}

bool CConsoleDisplayForward::Attach()
{
#ifdef CLIENT_DLL
	IManagedConsoleOutputClient *pArea = ManagedConsoleOutputClient();
#else
	IManagedConsoleOutput *pArea = ManagedConsoleOutput();
#endif
	if ( !pArea )
	{
		return false;
	}
	if ( !AttachFlagExchange( &m_bAttached, true ) )
	{
#ifdef CLIENT_DLL
		pArea->ClientConsoleBufferAttach( &m_Buffer );
#else
		pArea->ConsoleBufferAttach( &m_Buffer );
#endif
	}
	return true;
}

bool ConsoleDisplay_Install( ICvar *pCVar )
{
	if ( !pCVar )
	{
		return false;
	}
	if ( g_pInstalledCvar )
	{
		return g_pInstalledCvar == pCVar;
	}
	pCVar->InstallConsoleDisplayFunc( &g_Forwarder );
	g_pInstalledCvar = pCVar;
	// This load gets the buffer now, so its reader exists before any print.
	g_Forwarder.Attach();
	return true;
}

void ConsoleDisplay_Remove()
{
	if ( !g_pInstalledCvar )
	{
		return;
	}
	g_pInstalledCvar->RemoveConsoleDisplayFunc( &g_Forwarder );
	g_pInstalledCvar = NULL;
	// The next install is the next load's: it is handed the buffer again.
	g_Forwarder.Detach();
}

CConsoleDisplayReloadGap::CConsoleDisplayReloadGap()
	: m_pCVar( g_pInstalledCvar )
{
	ConsoleDisplay_Remove();
}

void CConsoleDisplayReloadGap::End()
{
	if ( m_pCVar )
	{
		ConsoleDisplay_Install( m_pCVar );
		m_pCVar = NULL;
	}
}
