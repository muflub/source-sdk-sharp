//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The debug overlay drawing library: positions, colours and durations handed to
// IVDebugOverlay. Entity-free: it includes no cbase.h. Clipped draws need a viewer
// set by the caller; with none, nothing is drawn and the refusal is counted.
#pragma once

#include "mathlib/vector.h"

// Per-outcome call counts, so "nothing drawn" can be told from "nothing asked".
//
//   nDrawn      calls that reached IVDebugOverlay
//   nClipped    calls the clip rejected: too far, or behind the eye
//   nNoViewer   calls that needed a viewer and had none
//   nNoOverlay  calls made with no IVDebugOverlay (a dedicated server, or before connect)
struct OverlayCounts_t
{
	unsigned int nDrawn;
	unsigned int nClipped;
	unsigned int nNoViewer;
	unsigned int nNoOverlay;
};

const OverlayCounts_t &Overlay_Counts();
void Overlay_ResetCounts();

// Set once per frame from the local player's eye; cleared when there is none.
void Overlay_SetViewer( const Vector &vecEyeOrigin, const Vector &vecEyeForward );
void Overlay_ClearViewer();
bool Overlay_ViewerIsSet();

// Stock's clip: keeps a line when either end is in range and either end is in front of the eye.
bool Overlay_SegmentIsVisible( const Vector &vecStart, const Vector &vecEnd );

// Unclipped: straight to IVDebugOverlay, as in stock.
void Overlay_BoxAngles( const Vector &origin, const Vector &mins, const Vector &maxs, const QAngle &angles, int r,
						int g, int b, int a, float flDuration );
void Overlay_Box( const Vector &origin, const Vector &mins, const Vector &maxs, int r, int g, int b, int a,
				  float flDuration );
void Overlay_TextAtPosition( const Vector &origin, int nLineOffset, const char *pText, float flDuration, int r, int g,
							 int b, int a );
void Overlay_ClearAll();

// Clipped against the viewer; counts the branch it took.
void Overlay_Line( const Vector &origin, const Vector &target, int r, int g, int b, bool bNoDepthTest,
				   float flDuration );

// Twenty slots, oldest reused, drawn once a frame. The caller decides the colour.
enum
{
	OVERLAY_POOL_LINES = 20
};

void Overlay_AddLine( const Vector &vecStart, const Vector &vecEnd, bool bNoDepthTest, int r, int g, int b );
void Overlay_DrawPooledLines();
