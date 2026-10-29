//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The centre print the engine requests by name (VCENTERPRINT002), forwarded to the managed UI.
// SS_REASON b  an interface resolved by name at load

#include "vguicenterprint.h"

#include "imanaged_areas_client.h"

CCenterPrint::CCenterPrint()
{
	vguiCenterString = NULL;
}

void CCenterPrint::Create( vgui::VPANEL parent ) {}

void CCenterPrint::Destroy() {}

void CCenterPrint::SetTextColor( int r, int g, int b, int a ) {}

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

// The wide text as UTF-8, by hand to avoid a tier1 import.
void CCenterPrint::ColorPrint( int r, int g, int b, int a, wchar_t *text )
{
	char utf8[1024];
	int n = 0;
	for ( const wchar_t *p = text; p && *p && n < (int)sizeof( utf8 ) - 4; p++ )
	{
		unsigned int c = (unsigned int)*p;
		if ( c < 0x80 )
		{
			utf8[n++] = (char)c;
			continue;
		}
		if ( c < 0x800 )
		{
			utf8[n++] = (char)( 0xC0 | ( c >> 6 ) );
		}
		else
		{
			if ( c < 0x10000 )
			{
				utf8[n++] = (char)( 0xE0 | ( c >> 12 ) );
			}
			else
			{
				utf8[n++] = (char)( 0xF0 | ( c >> 18 ) );
				utf8[n++] = (char)( 0x80 | ( ( c >> 12 ) & 0x3F ) );
			}
			utf8[n++] = (char)( 0x80 | ( ( c >> 6 ) & 0x3F ) );
		}
		utf8[n++] = (char)( 0x80 | ( c & 0x3F ) );
	}
	utf8[n] = 0;
	ColorPrint( r, g, b, a, utf8 );
}

void CCenterPrint::Clear()
{
	if ( IManagedUiClient *pUi = ManagedUiClient() )
		pUi->CenterClear();
}

static CCenterPrint g_CenterString;
CCenterPrint *internalCenterPrint = &g_CenterString;

EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CCenterPrint, ICenterPrint, VCENTERPRINT_INTERFACE_VERSION, g_CenterString );
