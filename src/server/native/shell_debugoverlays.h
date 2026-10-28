//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The server's debug overlays: the slots that let managed code draw an overlay for an
// entity handle, and the frame step that draws the pooled lines.

#pragma once

class CServerEntityResolver;

#include "ishim_areas.h"

class COverlayLibrary;

class CServerDebugOverlays final : public IShimDebugOverlay
{
public:
	CServerDebugOverlays( COverlayLibrary &library, CServerEntityResolver &entities )
		: m_Library( library )
		, m_Entities( entities )
	{
	}

	CServerDebugOverlays( const CServerDebugOverlays & ) = delete;
	CServerDebugOverlays &operator=( const CServerDebugOverlays & ) = delete;

	// Draws an entity's collision box and its text at its origin; nLine stacks text
	// downward. Answers 1 when drawn, 0 for INVALID_EHANDLE_INDEX or an unresolved
	// handle. Entry 0 is the world, a real slot, and is not refused.
	int32_t DrawEntityOverlay( uint32_t hEntity, int32_t nLine, const char *pText, float flDuration, int32_t r,
							   int32_t g, int32_t b, int32_t a ) override;

	// One line in a colour managed chose. flDuration <= 0 puts it in the pool redrawn
	// every frame until clear_debug_overlays; > 0 is the engine's own timed overlay.
	void AddLineOverlay( const float *pStartXYZ, const float *pEndXYZ, int32_t r, int32_t g, int32_t b,
						 int32_t bNoDepthTest, float flDuration ) override;

	// The overlay counters, cumulative since the level began, for `debug-api status`.
	void OverlayCounts( uint32_t *pOutDrawn, uint32_t *pOutClipped, uint32_t *pOutNoViewer, uint32_t *pOutNoOverlay,
						int32_t *pOutViewerSet ) override;

	// Stock's DrawAllDebugOverlays without the AI node editor and the gEntList walks.
	// Not behind _DEBUG, as stock's.
	void DrawAll();

private:
	COverlayLibrary &m_Library;

	// What an entity overlay's handle names.
	CServerEntityResolver &m_Entities;
};
