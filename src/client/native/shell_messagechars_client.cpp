//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — IMessageChars with nothing to draw ================//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve C3. Stock's CMessageChars (game/client/
// vgui_messagechars.cpp) was a vgui Panel that drew the strings callers queued
// through DrawString. Nothing in this client queues one: vgui_int.cpp makes
// and destroys it and the shell clears it at a level change, and no other
// compiled code names it. So the object stays, as the interface those three
// calls go through, and the Panel (and its vgui_controls vtable) goes.
//=============================================================================//

#include "cbase.h"
#include "imessagechars.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

class CSourceSharpMessageChars : public IMessageChars
{
public:
	virtual void Create( vgui::VPANEL parent ) OVERRIDE {}
	virtual void Destroy( void ) OVERRIDE {}

	virtual int DrawString( vgui::HFont, int, int, int, int, int, int, const char *, int, ... ) OVERRIDE { return 0; }
	virtual int DrawString( vgui::HFont, int, int, const char *, int, ... ) OVERRIDE { return 0; }
	virtual int DrawStringForTime( float, vgui::HFont, int, int, int, int, int, int, const char *, int, ... ) OVERRIDE { return 0; }
	virtual int DrawStringForTime( float, vgui::HFont, int, int, const char *, int, ... ) OVERRIDE { return 0; }
	virtual void RemoveStringsByID( int ) OVERRIDE {}

	virtual void GetStringLength( vgui::HFont, int *width, int *height, const char *, ... ) OVERRIDE
	{
		if ( width )
			*width = 0;
		if ( height )
			*height = 0;
	}

	virtual void Clear( void ) OVERRIDE {}
};

static CSourceSharpMessageChars g_MessageChars;
IMessageChars *messagechars = &g_MessageChars;
