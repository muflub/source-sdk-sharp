//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The engine calls the installed IConsoleDisplayFunc on the printing thread.
// A print is appended to the line buffer, which the managed side drains itself.

#include "icvar.h"
#include "Color.h"
#ifdef CLIENT_DLL
#include "imanaged_areas_client.h"
#else
#include "imanaged_areas.h"
#endif
#include "console_display_forward.h"

// Every spew reaches ColorPrint with its spew type's colour: warning (255,90,90),
// assert (255,20,20), error (20,70,255).
static bool IsEngineWarningColor( const Color &c )
{
	return c.r() == 255 ? ( c.g() == 90 && c.b() == 90 ) || ( c.g() == 20 && c.b() == 20 )
						: c.r() == 20 && c.g() == 70 && c.b() == 255;
}

void CConsoleDisplay::CForwarder::ColorPrint( const Color &color, const char *pMessage )
{
	m_Owner.Forward( pMessage, IsEngineWarningColor( color ) ? 1 : 0 );
}

void CConsoleDisplay::CForwarder::Print( const char *pMessage )
{
	m_Owner.Forward( pMessage, 0 );
}

void CConsoleDisplay::CForwarder::DPrint( const char *pMessage )
{
	m_Owner.Forward( pMessage, 0 );
}

CConsoleDisplay::CConsoleDisplay( ManagedConsoleOutputArea_t *const &ppArea )
	: m_ppArea( ppArea )
	, m_pBuffer( std::make_unique<CConsoleLineBuffer>() )
{
}

void CConsoleDisplay::Forward( const char *pText, int nWarning )
{
	// A pointer the engine captured before a Remove must stop re-entering this DLL.
	if ( !m_pInstalledCvar || !pText )
	{
		return;
	}
	// Outside a load there is no reader and the line is dropped.
	if ( !Attach() )
	{
		return;
	}
	m_pBuffer->Append( pText, nWarning );
}

bool CConsoleDisplay::Attach()
{
	ManagedConsoleOutputArea_t *pArea = m_ppArea;
	if ( !pArea )
	{
		return false;
	}
	if ( !m_bAttached.exchange( true, std::memory_order_acq_rel ) )
	{
		m_pBuffer->Publish();
#ifdef CLIENT_DLL
		pArea->ClientConsoleBufferAttach( m_pBuffer.get() );
#else
		pArea->ConsoleBufferAttach( m_pBuffer.get() );
#endif
	}
	return true;
}

bool CConsoleDisplay::Install( ICvar *pCVar )
{
	if ( !pCVar )
	{
		return false;
	}
	if ( m_pInstalledCvar )
	{
		return m_pInstalledCvar == pCVar;
	}
	pCVar->InstallConsoleDisplayFunc( &m_Forwarder );
	m_pInstalledCvar = pCVar;
	// This load gets the buffer now, so its reader exists before any print.
	Attach();
	return true;
}

void CConsoleDisplay::Remove()
{
	if ( !m_pInstalledCvar )
	{
		return;
	}
	m_pInstalledCvar->RemoveConsoleDisplayFunc( &m_Forwarder );
	m_pInstalledCvar = NULL;
	m_bAttached.store( false, std::memory_order_release );
}

CConsoleDisplayReloadGap::CConsoleDisplayReloadGap( CConsoleDisplay &display )
	: m_Display( display )
	, m_pCVar( display.Installed() )
{
	m_Display.Remove();
}

void CConsoleDisplayReloadGap::End()
{
	if ( m_pCVar )
	{
		m_Display.Install( m_pCVar );
		m_pCVar = NULL;
	}
}
