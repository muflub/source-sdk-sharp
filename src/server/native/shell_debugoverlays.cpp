//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration

#include "shell_debugoverlays.h"

#include "overlay_library.h"
#include "slot_table.h"
#include "shell_standalone.h"
#include "shell_diagnostics.h"

int32_t CServerDebugOverlays::DrawEntityOverlay( uint32_t hEntity, int32_t nLine, const char *pText, float flDuration,
												 int32_t r, int32_t g, int32_t b, int32_t a )
{
	SOURCESHARP_TRACE();

	if ( hEntity == (uint32_t)INVALID_EHANDLE_INDEX )
		return 0;

	CStandaloneEntity *pShell = SourceSharp_ShellFromHandle( CBaseHandle::UnsafeFromIndex( (int)hEntity ) );

	if ( pShell == NULL )
		return 0;

	const Vector &vecOrigin = pShell->GetAbsOrigin();

	// The box first, so the text lands on top of it in the overlay list.
	m_Library.Box( vecOrigin, pShell->OBBMins(), pShell->OBBMaxs(), r, g, b, a, flDuration );

	// The caller's text, else the entity's name, else its classname, as stock's ent_text.
	const char *pDrawn = pText;
	if ( pDrawn == NULL || pDrawn[0] == '\0' )
	{
		pDrawn = pShell->GetEntityName();
		if ( pDrawn == NULL || pDrawn[0] == '\0' )
			pDrawn = pShell->GetClassname();
	}

	m_Library.TextAtPosition( vecOrigin, nLine, pDrawn, flDuration, r, g, b, a );

	return 1;
}

void CServerDebugOverlays::AddLineOverlay( const float *pStartXYZ, const float *pEndXYZ, int32_t r, int32_t g,
										   int32_t b, int32_t bNoDepthTest, float flDuration )
{
	SOURCESHARP_TRACE();

	if ( !pStartXYZ || !pEndXYZ )
		return;

	const Vector vecStart( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );
	const Vector vecEnd( pEndXYZ[0], pEndXYZ[1], pEndXYZ[2] );

	if ( flDuration > 0.0f )
		m_Library.Line( vecStart, vecEnd, r, g, b, bNoDepthTest != 0, flDuration );
	else
		m_Library.AddLine( vecStart, vecEnd, bNoDepthTest != 0, r, g, b );
}

void CServerDebugOverlays::OverlayCounts( uint32_t *pOutDrawn, uint32_t *pOutClipped, uint32_t *pOutNoViewer,
										  uint32_t *pOutNoOverlay, int32_t *pOutViewerSet )
{
	const OverlayCounts_t &c = m_Library.Counts();

	if ( pOutDrawn )
		*pOutDrawn = c.nDrawn;
	if ( pOutClipped )
		*pOutClipped = c.nClipped;
	if ( pOutNoViewer )
		*pOutNoViewer = c.nNoViewer;
	if ( pOutNoOverlay )
		*pOutNoOverlay = c.nNoOverlay;
	if ( pOutViewerSet )
		*pOutViewerSet = m_Library.ViewerIsSet() ? 1 : 0;
}

void CServerDebugOverlays::DrawAll()
{
	// No viewer: nothing in this build sets the debug picker, so clipped overlays count
	// as "no viewer". First, so a frame with nobody looking is told apart from one that
	// drew nothing.
	m_Library.ClearViewer();

	m_Library.DrawPooledLines();
}
