//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The engine calls the installed IConsoleDisplayFunc on the printing thread.
// A print is appended to the line buffer, which the managed side drains itself.

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

namespace
{

// One per DLL: the engine's list holds a raw pointer, so a second install would double every line.
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
	void Detach() { m_bAttached.store( false, std::memory_order_release ); }

private:
	void Forward( const char *pText, int nWarning );

	std::atomic< bool > m_bAttached{ false };

	// Every spew reaches ColorPrint with its spew type's colour: warning (255,90,90),
	// assert (255,20,20), error (20,70,255).
	static bool IsEngineWarningColor( const Color &c )
	{
		return c.r() == 255 ? ( c.g() == 90 && c.b() == 90 ) || ( c.g() == 20 && c.b() == 20 )
		                    : c.r() == 20 && c.g() == 70 && c.b() == 255;
	}
};

CConsoleLineBuffer g_LineBuffer;  // not a member: the forwarder's vtable would put 1 MiB in .data
CConsoleDisplayForward g_Forwarder;
ICvar *g_pInstalledCvar = NULL;

} // namespace

void CConsoleDisplayForward::Forward( const char *pText, int nWarning )
{
	// A pointer the engine captured before a Remove must stop re-entering this DLL.
	if ( !g_pInstalledCvar || !pText )
	{
		return;
	}
	// Outside a load there is no reader and the line is dropped.
	if ( !Attach() )
	{
		return;
	}
	g_LineBuffer.Append( pText, nWarning );
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
	if ( !m_bAttached.exchange( true, std::memory_order_acq_rel ) )
	{
		g_LineBuffer.Publish();
#ifdef CLIENT_DLL
		pArea->ClientConsoleBufferAttach( &g_LineBuffer );
#else
		pArea->ConsoleBufferAttach( &g_LineBuffer );
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
