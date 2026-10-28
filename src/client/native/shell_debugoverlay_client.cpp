//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the debug overlay's text, native half ==============//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve C3. Stock's CDebugOverlay (game/client/
// vgui_debugoverlaypanel.cpp) was a vgui Panel that walked the engine's
// debug-overlay text list each paint and drew it. The panel is gone; the
// managed UI draws the text, reading the list through two IShimUiClient
// slots: one entry at a time, placed on the screen by the engine's own
// ScreenPosition (as stock placed it), and the dead ones cleared after the
// paint, as stock's Paint ended. vgui_int.cpp still creates and destroys the
// panel object: those calls have nothing to do. DebugDrawLine, stock's helper
// beside it, stays.
//=============================================================================//

#include <stdio.h>
#include <stdlib.h>
#include "tier0/dbg.h"
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
#include "idebugoverlaypanel.h"
#include "engine/ivdebugoverlay.h"
#include "overlaytext.h"
#include "mathlib/vector.h"
#include "tier0/platform.h"
#include "tier1/strtools.h"
#include "vgui/VGUI.h"
#include "cdll_client_int.h"
#include "sourcesharp_entrypoints.h"

class CSourceSharpDebugOverlayPanel : public IDebugOverlayPanel
{
public:
	virtual void Create( vgui::VPANEL parent ) OVERRIDE
	{
	}

	virtual void Destroy( void ) OVERRIDE
	{
	}
};

static CSourceSharpDebugOverlayPanel g_DebugOverlay;
IDebugOverlayPanel *debugoverlaypanel = &g_DebugOverlay;

void DebugDrawLine( const Vector& vecAbsStart, const Vector& vecAbsEnd, int r, int g, int b, bool test, float duration )
{
	if ( debugoverlay )
		debugoverlay->AddLineOverlay( vecAbsStart + Vector( 0, 0, 0.1 ), vecAbsEnd + Vector( 0, 0, 0.1 ), r, g, b, test, duration );
}

//-----------------------------------------------------------------------------
// The text overlay at position nIndex of the engine's list: -1 past its end,
// 0 when it has no text or is off the screen (stock skipped it), 1 with its
// screen position, its line offset, its colour (0xRRGGBBAA) and its text.
//-----------------------------------------------------------------------------
int32_t SourceSharp_UiDebugOverlayText( int32_t nIndex, float *pOutX, float *pOutY, int32_t *pOutLine,
                                        uint32_t *pOutRgba, uint8_t *pText, int32_t nTextBytes )
{
	if ( !debugoverlay )
		return -1;
	OverlayText_t *p = debugoverlay->GetFirst();
	for ( int32_t i = 0; p && i < nIndex; i++ )
		p = debugoverlay->GetNext( p );
	if ( !p )
		return -1;
	if ( p->text[ 0 ] == 0 )
		return 0;

	Vector screen;
	bool bOff = p->bUseOrigin ? debugoverlay->ScreenPosition( p->origin, screen )
	                          : debugoverlay->ScreenPosition( p->flXPos, p->flYPos, screen );
	if ( bOff )
		return 0;

	*pOutX = screen[ 0 ];
	*pOutY = screen[ 1 ];
	*pOutLine = p->lineOffset;
	*pOutRgba = ( (uint32_t)( p->r & 0xFF ) << 24 ) | ( (uint32_t)( p->g & 0xFF ) << 16 ) | ( (uint32_t)( p->b & 0xFF ) << 8 ) | (uint32_t)( p->a & 0xFF );
	V_strncpy( (char *)pText, p->text, nTextBytes );
	return 1;
}

void SourceSharp_UiDebugOverlayClearDead( void )
{
	if ( debugoverlay )
		debugoverlay->ClearDeadOverlays();
}
