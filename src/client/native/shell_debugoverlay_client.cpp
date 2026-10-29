//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The engine's debug-overlay text list, read by the managed UI one entry at a
// time and placed with the engine's ScreenPosition, as stock's panel did.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "idebugoverlaypanel.h"
#include "engine/ivdebugoverlay.h"
#include "overlaytext.h"

class CSourceSharpDebugOverlayPanel : public IDebugOverlayPanel
{
public:
	virtual void Create(vgui::VPANEL parent) OVERRIDE {}

	virtual void Destroy() OVERRIDE {}
};

static CSourceSharpDebugOverlayPanel g_DebugOverlay;
IDebugOverlayPanel* debugoverlaypanel = &g_DebugOverlay;

void DebugDrawLine(const Vector& vecAbsStart, const Vector& vecAbsEnd, int r, int g, int b, bool test, float duration)
{
	if (debugoverlay)
		debugoverlay->AddLineOverlay(vecAbsStart + Vector(0, 0, 0.1), vecAbsEnd + Vector(0, 0, 0.1), r, g, b, test,
									 duration);
}

// The text overlay at position nIndex of the engine's list: -1 past its end,
// 0 when it has no text or is off the screen, 1 with its screen position, line
// offset, colour (0xRRGGBBAA) and text.
int32_t SourceSharp_UiDebugOverlayText(int32_t nIndex, float* pOutX, float* pOutY, int32_t* pOutLine,
									   uint32_t* pOutRgba, uint8_t* pText, int32_t nTextBytes)
{
	if (!debugoverlay)
		return -1;
	OverlayText_t* p = debugoverlay->GetFirst();
	for (int32_t i = 0; p && i < nIndex; i++)
		p = debugoverlay->GetNext(p);
	if (!p)
		return -1;
	if (p->text[0] == 0)
		return 0;

	Vector screen;
	bool bOff = p->bUseOrigin ? debugoverlay->ScreenPosition(p->origin, screen)
							  : debugoverlay->ScreenPosition(p->flXPos, p->flYPos, screen);
	if (bOff)
		return 0;

	*pOutX = screen[0];
	*pOutY = screen[1];
	*pOutLine = p->lineOffset;
	*pOutRgba = ((uint32_t)(p->r & 0xFF) << 24) | ((uint32_t)(p->g & 0xFF) << 16) | ((uint32_t)(p->b & 0xFF) << 8) |
		(uint32_t)(p->a & 0xFF);
	V_strncpy((char*)pText, p->text, nTextBytes);
	return 1;
}

void SourceSharp_UiDebugOverlayClearDead()
{
	if (debugoverlay)
		debugoverlay->ClearDeadOverlays();
}
