//========= Copyright Valve Corporation, All rights reserved. ============//
//  cdll_int.h
// 4-23-98
// JOHN:  client dll interface declarations

#pragma once

class Vector;

#define VDEBUG_OVERLAY_INTERFACE_VERSION "VDebugOverlay003"

// When used as a duration by a server-side NDebugOverlay:: call,
// causes the overlay to persist until the next server update.

class OverlayText_t;

abstract_class IVDebugOverlay
{
public:
	virtual void unused_AddEntityTextOverlay() = 0;
	virtual void AddBoxOverlay( const Vector &origin, const Vector &mins, const Vector &max, QAngle const &orientation,
								int r, int g, int b, int a, float duration ) = 0;
	virtual void unused_AddTriangleOverlay() = 0;
	virtual void AddLineOverlay( const Vector &origin, const Vector &dest, int r, int g, int b, bool noDepthTest,
								 float duration ) = 0;
	virtual void AddTextOverlay( const Vector &origin, float duration, PRINTF_FORMAT_STRING const char *format,
								 ... ) = 0;
	virtual void AddTextOverlay( const Vector &origin, int line_offset, float duration,
								 PRINTF_FORMAT_STRING const char *format, ... ) = 0;
	virtual void unused_AddScreenTextOverlay() = 0;
	virtual void unused_AddSweptBoxOverlay() = 0;
	virtual void unused_AddGridOverlay() = 0;
	virtual int ScreenPosition( const Vector &point, Vector &screen ) = 0;
	virtual int ScreenPosition( float flXPos, float flYPos, Vector &screen ) = 0;

	virtual OverlayText_t *GetFirst() = 0;
	virtual OverlayText_t *GetNext( OverlayText_t * current ) = 0;
	virtual void ClearDeadOverlays() = 0;
	virtual void ClearAllOverlays() = 0;

	virtual void AddTextOverlayRGB( const Vector &origin, int line_offset, float duration, float r, float g, float b,
									float alpha, PRINTF_FORMAT_STRING const char *format, ... ) = 0;
	virtual void AddTextOverlayRGB( const Vector &origin, int line_offset, float duration, int r, int g, int b, int a,
									PRINTF_FORMAT_STRING const char *format, ... ) = 0;

	virtual void unused_AddLineOverlayAlpha() = 0;
	virtual void unused_AddBoxOverlay2() = 0;

	virtual void unused_AddScreenTextOverlay2() = 0;

private:
	/* catch improper use of bad interface. Needed because '0' duration can be resolved by compiler to NULL format string (i.e., compiles but calls wrong function) */
};
