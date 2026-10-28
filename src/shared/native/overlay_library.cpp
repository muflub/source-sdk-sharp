//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data

#include "overlay_library.h"

#include "mathlib/mathlib.h"
#include "engine/ivdebugoverlay.h"
#include "tier1/strtools.h"

// The engine's overlay channel, defined per side; declared here so this file needs no cbase.h.
extern IVDebugOverlay *debugoverlay;

// Stock's MAX_OVERLAY_DIST_SQR.
#define OVERLAY_MAX_DIST_SQR 90000000.0f

namespace
{

struct OverlayViewer_t
{
	Vector vecOrigin;
	Vector vecForward;
	bool   bSet;
};

OverlayViewer_t &ViewerRef()
{
	static OverlayViewer_t s_Viewer = { Vector( 0, 0, 0 ), Vector( 0, 0, 0 ), false };
	return s_Viewer;
}

OverlayCounts_t &CountsRef()
{
	static OverlayCounts_t s_Counts = { 0, 0, 0, 0 };
	return s_Counts;
}

// ndebugoverlay.h's OverlayLine_t, restated so that header is not needed.
struct OverlayPoolLine_t
{
	Vector vecOrigin;
	Vector vecDest;
	int    r;
	int    g;
	int    b;
	bool   bNoDepthTest;
	bool   bDraw;
};

struct OverlayPool_t
{
	OverlayPoolLine_t lines[ OVERLAY_POOL_LINES ];
	int               nNext;      // the slot the next AddLine writes
};

OverlayPool_t &PoolRef()
{
	static OverlayPool_t s_Pool;
	static bool s_bInitialised = false;
	if ( !s_bInitialised )
	{
		for ( int i = 0; i < OVERLAY_POOL_LINES; i++ )
		{
			s_Pool.lines[i].vecOrigin.Init();
			s_Pool.lines[i].vecDest.Init();
			s_Pool.lines[i].r = 255;
			s_Pool.lines[i].g = 255;
			s_Pool.lines[i].b = 255;
			s_Pool.lines[i].bNoDepthTest = true;
			s_Pool.lines[i].bDraw = false;
		}
		s_Pool.nNext  = 0;
		s_bInitialised = true;
	}
	return s_Pool;
}

// Counts calls made with no overlay interface.
bool HaveOverlay()
{
	if ( debugoverlay )
		return true;

	CountsRef().nNoOverlay++;
	return false;
}

} // anonymous namespace

const OverlayCounts_t &Overlay_Counts()
{
	return CountsRef();
}

void Overlay_ResetCounts()
{
	OverlayCounts_t &c = CountsRef();
	c.nDrawn = 0;
	c.nClipped = 0;
	c.nNoViewer = 0;
	c.nNoOverlay = 0;
}

void Overlay_SetViewer( const Vector &vecEyeOrigin, const Vector &vecEyeForward )
{
	OverlayViewer_t &v = ViewerRef();
	v.vecOrigin  = vecEyeOrigin;
	v.vecForward = vecEyeForward;
	v.bSet       = true;
}

void Overlay_ClearViewer()
{
	ViewerRef().bSet = false;
}

bool Overlay_ViewerIsSet()
{
	return ViewerRef().bSet;
}

// Stock keeps a line when either end is in range and either end is in front of the eye.
bool Overlay_SegmentIsVisible( const Vector &vecStart, const Vector &vecEnd )
{
	const OverlayViewer_t &v = ViewerRef();
	if ( !v.bSet )
		return false;

	const float flStartSqr = ( v.vecOrigin - vecStart ).LengthSqr();
	const float flEndSqr   = ( v.vecOrigin - vecEnd ).LengthSqr();
	if ( flStartSqr > OVERLAY_MAX_DIST_SQR && flEndSqr > OVERLAY_MAX_DIST_SQR )
		return false;

	const float flDotStart = DotProduct( v.vecForward, vecStart - v.vecOrigin );
	const float flDotEnd   = DotProduct( v.vecForward, vecEnd   - v.vecOrigin );
	if ( flDotStart < 0.0f && flDotEnd < 0.0f )
		return false;

	return true;
}

void Overlay_BoxAngles( const Vector &origin, const Vector &mins, const Vector &maxs,
                        const QAngle &angles, int r, int g, int b, int a, float flDuration )
{
	if ( !HaveOverlay() )
		return;

	debugoverlay->AddBoxOverlay( origin, mins, maxs, angles, r, g, b, a, flDuration );
	CountsRef().nDrawn++;
}

void Overlay_Box( const Vector &origin, const Vector &mins, const Vector &maxs,
                  int r, int g, int b, int a, float flDuration )
{
	Overlay_BoxAngles( origin, mins, maxs, vec3_angle, r, g, b, a, flDuration );
}

void Overlay_TextAtPosition( const Vector &origin, int nLineOffset, const char *pText,
                             float flDuration, int r, int g, int b, int a )
{
	if ( !pText )
		return;

	if ( !HaveOverlay() )
		return;

	debugoverlay->AddTextOverlayRGB( origin, nLineOffset, flDuration, r, g, b, a, "%s", pText );
	CountsRef().nDrawn++;
}

void Overlay_ClearAll()
{
	if ( !HaveOverlay() )
		return;

	debugoverlay->ClearAllOverlays();
}

void Overlay_Line( const Vector &origin, const Vector &target,
                   int r, int g, int b, bool bNoDepthTest, float flDuration )
{
	if ( !Overlay_ViewerIsSet() )
	{
		CountsRef().nNoViewer++;
		return;
	}

	if ( !Overlay_SegmentIsVisible( origin, target ) )
	{
		CountsRef().nClipped++;
		return;
	}

	if ( !HaveOverlay() )
		return;

	debugoverlay->AddLineOverlay( origin, target, r, g, b, bNoDepthTest, flDuration );
	CountsRef().nDrawn++;
}

void Overlay_AddLine( const Vector &vecStart, const Vector &vecEnd, bool bNoDepthTest,
                      int r, int g, int b )
{
	OverlayPool_t &pool = PoolRef();

	OverlayPoolLine_t &line = pool.lines[ pool.nNext ];
	line.vecOrigin    = vecStart;
	line.vecDest      = vecEnd;
	line.bNoDepthTest = bNoDepthTest;
	line.r            = r;
	line.g            = g;
	line.b            = b;
	line.bDraw        = true;

	pool.nNext = ( pool.nNext + 1 ) % OVERLAY_POOL_LINES;
}

void Overlay_DrawPooledLines()
{
	OverlayPool_t &pool = PoolRef();

	for ( int i = 0; i < OVERLAY_POOL_LINES; i++ )
	{
		if ( !pool.lines[i].bDraw )
			continue;

		Overlay_Line( pool.lines[i].vecOrigin, pool.lines[i].vecDest,
		              pool.lines[i].r, pool.lines[i].g, pool.lines[i].b,
		              pool.lines[i].bNoDepthTest, 0 );
	}
}
