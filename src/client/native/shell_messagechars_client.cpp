//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// IMessageChars with nothing to draw: nothing in this client queues a string,
// so the object is only the interface vgui_int.cpp and the shell call through.

#include "imessagechars.h"

class CSourceSharpMessageChars : public IMessageChars
{
public:
	virtual void Create( vgui::VPANEL parent ) OVERRIDE {}

	virtual void Destroy() OVERRIDE {}

	virtual int DrawString( vgui::HFont, int, int, int, int, int, int, const char *, int, ... ) OVERRIDE { return 0; }

	virtual int DrawString( vgui::HFont, int, int, const char *, int, ... ) OVERRIDE { return 0; }

	virtual int DrawStringForTime( float, vgui::HFont, int, int, int, int, int, int, const char *, int, ... ) OVERRIDE
	{
		return 0;
	}

	virtual int DrawStringForTime( float, vgui::HFont, int, int, const char *, int, ... ) OVERRIDE { return 0; }

	virtual void RemoveStringsByID( int ) OVERRIDE {}

	virtual void GetStringLength( vgui::HFont, int *width, int *height, const char *, ... ) OVERRIDE
	{
		if ( width )
			*width = 0;
		if ( height )
			*height = 0;
	}

	virtual void Clear() OVERRIDE {}
};

static CSourceSharpMessageChars g_MessageChars;
IMessageChars *messagechars = &g_MessageChars;
