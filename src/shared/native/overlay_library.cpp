//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the debug overlay leaf library ======================//
// SS_REASON g  leaf math over mdl/bsp data
//
// plan_entity.md Phase 4, step 4c. See overlay_library.h for what this is, what
// it deliberately is not, and why the viewer exists.
//
// THE BODIES ARE STOCK'S, and where one differs the difference is stated at the
// line rather than in this block. The two files they came from are
// game/server/ndebugoverlay.cpp (the pool, and the padded line) and
// game/shared/debugoverlay_shared.cpp (everything else).
//
// NO cbase.h, and that is what lets a gtest link this against mathlib and a
// stub IVDebugOverlay rather than against a game DLL.
//
//=============================================================================//

#include "overlay_library.h"

#include "mathlib/mathlib.h"
#include "engine/ivdebugoverlay.h"
#include "tier1/strtools.h"

// The engine's overlay channel. Defined per side — the server's in
// sourcesharp/game/server/shell_engineboundary.cpp, the client's in
// sourcesharp/game/client/shell_clientdll.cpp — and declared here rather than
// reached through enginecallback.h, which would pull the whole server game
// header set into an entity-free file. game/client/c_func_breakablesurf.cpp
// declares it exactly this way for exactly this reason.
extern IVDebugOverlay *debugoverlay;


// debugoverlay_shared.cpp's MAX_OVERLAY_DIST_SQR, unchanged: 9,486 units, which
// is a little over the diagonal of the largest map this mod ships.
#define OVERLAY_MAX_DIST_SQR 90000000.0f

//=============================================================================//
// STATE, VIA ACCESSORS.
//
// plan.md §1a: nothing is a static or a global. The idiom is the one
// game/shared/entity_debug_globals.h established and states the reasoning for —
// a function-local static is one object across the program, and putting the
// accessor between it and its readers makes "who writes this" answerable by
// grepping one name.
//=============================================================================//

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

// One pool slot. ndebugoverlay.h's OverlayLine_t, restated here rather than
// included, because that header is one of the two this library exists to let
// the build stop compiling.
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

// Every draw funnels through here so that "there is no overlay interface" is
// counted once, in one place, rather than being a silent `if ( debugoverlay )`
// at twenty call sites the way stock had it.

bool HaveOverlay()
{
	if ( debugoverlay )
		return true;

	CountsRef().nNoOverlay++;
	return false;
}

} // anonymous namespace

//=============================================================================//
// THE READOUT
//=============================================================================//

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

//=============================================================================//
// THE VIEWER AND THE CLIP
//=============================================================================//

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

//-----------------------------------------------------------------------------
// NDebugOverlay::Line's clip, and it is NOT a point clip applied twice.
// Stock keeps a line when EITHER end is within range, and keeps it when EITHER
// end is in front of the eye — a line whose near end is behind you and whose
// far end is in front of you is a line you can see part of.
//-----------------------------------------------------------------------------
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

//=============================================================================//
// UNCLIPPED DRAWING
//=============================================================================//

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

//=============================================================================//
// CLIPPED DRAWING — the entries that were `return;`
//=============================================================================//

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

//=============================================================================//
// THE PADDED LINE
//=============================================================================//

void Overlay_LinePadded( const Vector &vecAbsStart, const Vector &vecAbsEnd,
                         int r, int g, int b, bool bNoDepthTest, float flDuration )
{
	Overlay_Line( vecAbsStart + Vector( 0, 0, 0.1f ), vecAbsEnd + Vector( 0, 0, 0.1f ),
	              r, g, b, bNoDepthTest, flDuration );
}

//=============================================================================//
// THE POOL
//=============================================================================//

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

//-----------------------------------------------------------------------------
// Stock walked all twenty slots every frame and tested `draw` on each. The walk
// is the same; what it is NOT is guarded by stock's `m_nDebugOverlayIndex != -1`
// sentinel, because the pool here is initialised on first use rather than
// lazily allocated with `new` — twenty structs by value, which also removes
// twenty allocations that were never freed.
//-----------------------------------------------------------------------------
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
