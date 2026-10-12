//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the centre print's native half =====================//
// SS_REASON b  an interface resolved by name at load
//
// plan_client_novalve C3. Stock's CCenterPrint (game/client/
// vgui_centerstringpanel.cpp) drew a vgui Label; the engine asks for it by
// name (VCENTERPRINT002: engine.so's "Could not get centerprint interface
// from library client") and the client's own level code clears it. The label
// is gone: each call is carried out by forwarding it to the managed UI
// (IManagedUiClient::CenterPrint / CenterClear), whose CentrePrint keeps the
// text, the colour and the time (scr_centertime is managed now). Nothing is
// decided here.
//
// Stock's Print always prints white (it calls ColorPrint 255,255,255,255), so
// SetTextColor on its own has no visible effect and carries nothing here.
//=============================================================================//

#include "cbase.h"
#include "vguicenterprint.h"
#include "imanaged_areas_client.h"

CCenterPrint::CCenterPrint( void )
{
	vguiCenterString = NULL;
}

// The label's parent is the UI's now: nothing to make or unmake.
void CCenterPrint::Create( vgui::VPANEL parent )
{
}

void CCenterPrint::Destroy( void )
{
}

void CCenterPrint::SetTextColor( int r, int g, int b, int a )
{
}

void CCenterPrint::Print( char *text )
{
	ColorPrint( 255, 255, 255, 255, text );
}

void CCenterPrint::Print( wchar_t *text )
{
	ColorPrint( 255, 255, 255, 255, text );
}

void CCenterPrint::ColorPrint( int r, int g, int b, int a, char *text )
{
	if ( IManagedUiClient *pUi = ManagedUiClient() )
		pUi->CenterPrint( text ? text : "", r, g, b, a );
}

// The wide print as UTF-8 for the slot. By hand rather than tier1's
// V_UnicodeToUTF8, which would be a new tier1 import for this one call
// (client-novalve-ratchet imports:tier1).
void CCenterPrint::ColorPrint( int r, int g, int b, int a, wchar_t *text )
{
	char utf8[ 1024 ];
	int n = 0;
	for ( const wchar_t *p = text; p && *p && n < (int)sizeof( utf8 ) - 4; p++ )
	{
		unsigned int c = (unsigned int)*p;
		if ( c < 0x80 ) { utf8[ n++ ] = (char)c; continue; }
		if ( c < 0x800 ) { utf8[ n++ ] = (char)( 0xC0 | ( c >> 6 ) ); }
		else
		{
			if ( c < 0x10000 ) { utf8[ n++ ] = (char)( 0xE0 | ( c >> 12 ) ); }
			else { utf8[ n++ ] = (char)( 0xF0 | ( c >> 18 ) ); utf8[ n++ ] = (char)( 0x80 | ( ( c >> 12 ) & 0x3F ) ); }
			utf8[ n++ ] = (char)( 0x80 | ( ( c >> 6 ) & 0x3F ) );
		}
		utf8[ n++ ] = (char)( 0x80 | ( c & 0x3F ) );
	}
	utf8[ n ] = 0;
	ColorPrint( r, g, b, a, utf8 );
}

void CCenterPrint::Clear( void )
{
	if ( IManagedUiClient *pUi = ManagedUiClient() )
		pUi->CenterClear();
}

static CCenterPrint g_CenterString;
CCenterPrint *internalCenterPrint = &g_CenterString;

EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CCenterPrint, ICenterPrint, VCENTERPRINT_INTERFACE_VERSION, g_CenterString );
