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

class IVDebugOverlay;

// Per-outcome call counts, so "nothing drawn" can be told from "nothing asked".
//
//   nDrawn      calls that reached IVDebugOverlay
//   nNoOverlay  calls made with no IVDebugOverlay (a dedicated server, or before connect)
struct OverlayCounts_t
{
	unsigned int nDrawn;
	unsigned int nNoOverlay;
};

class COverlayLibrary
{
public:
	// Twenty pooled slots, oldest reused, drawn once a frame. The caller decides the colour.
	static constexpr int kPoolLines = 20;

	// pOverlay may be NULL (a dedicated server): draws are then counted, not made.
	explicit COverlayLibrary(IVDebugOverlay* pOverlay);

	COverlayLibrary(const COverlayLibrary&) = delete;
	COverlayLibrary& operator=(const COverlayLibrary&) = delete;

	const OverlayCounts_t& Counts() const { return m_Counts; }

	// Straight to IVDebugOverlay.
	void BoxAngles(const Vector& origin, const Vector& mins, const Vector& maxs, const QAngle& angles, int r, int g,
				   int b, int a, float flDuration);
	void Box(const Vector& origin, const Vector& mins, const Vector& maxs, int r, int g, int b, int a,
			 float flDuration);
	void TextAtPosition(const Vector& origin, int nLineOffset, const char* pText, float flDuration, int r, int g, int b,
						int a);
	void ClearAll();

	// Stock clipped a line against the local player's eye. Nothing here has a viewer to clip
	// against, so every line is drawn; the pool bounds how many.
	void Line(const Vector& origin, const Vector& target, int r, int g, int b, bool bNoDepthTest, float flDuration);

	void AddLine(const Vector& vecStart, const Vector& vecEnd, bool bNoDepthTest, int r, int g, int b);
	void DrawPooledLines();

private:
	// ndebugoverlay.h's OverlayLine_t, restated so that header is not needed.
	struct PoolLine_t
	{
		Vector vecOrigin;
		Vector vecDest;
		int r;
		int g;
		int b;
		bool bNoDepthTest;
		bool bDraw;
	};

	// Counts a call made with no overlay interface.
	bool HaveOverlay();

	IVDebugOverlay* m_pOverlay;
	OverlayCounts_t m_Counts;
	PoolLine_t m_Pool[kPoolLines];
	int m_nNextPoolLine;
};
