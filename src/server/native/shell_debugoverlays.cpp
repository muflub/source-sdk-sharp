//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// The server's debug overlays: the frame step and the slots that let managed
// code draw an overlay for an entity handle. The drawing is overlay_library's.

#include "ss_util.h"

extern CGlobalVars *gpGlobals;
#include "overlay_library.h"
#include "slot_table.h"
#include "shell_standalone.h"
// Defined in shell_gameclients.cpp.
CPlayerState *SourceSharp_PlayerStateForEdict( edict_t *pEdict );
bool SourceSharp_ClientViewOrigin( edict_t *pClient, Vector *pOut );
#include "shell_diagnostics.h"
#include "PlayerState.h"

// No viewer: nothing in this build sets the debug picker, so clipped overlays
// count as "no viewer".
static void SourceSharp_EstablishOverlayViewer()
{
	Overlay_ClearViewer();
}

// Draws an entity's collision box and its text at its origin; nLine stacks text
// downward. Answers 1 when drawn, 0 for INVALID_EHANDLE_INDEX or an unresolved
// handle. Entry 0 is the world, a real slot, and is not refused.
int32_t SourceSharp_DrawEntityOverlay( uint32_t hEntity, int32_t nLine, const char *pText,
                                       float flDuration, int32_t r, int32_t g, int32_t b, int32_t a )
{
	SOURCESHARP_TRACE();

	if ( hEntity == (uint32_t)INVALID_EHANDLE_INDEX )
		return 0;

	CStandaloneEntity *pShell =
		SourceSharp_ShellFromHandle( CBaseHandle::UnsafeFromIndex( (int)hEntity ) );

	if ( pShell == NULL )
		return 0;

	const Vector &vecOrigin = pShell->GetAbsOrigin();

	// The box first, so the text lands on top of it in the overlay list.
	Overlay_Box( vecOrigin, pShell->OBBMins(), pShell->OBBMaxs(), r, g, b, a, flDuration );

	// The caller's text, else the entity's name, else its classname, as stock's ent_text.
	const char *pDrawn = pText;
	if ( pDrawn == NULL || pDrawn[0] == '\0' )
	{
		pDrawn = pShell->GetEntityName();
		if ( pDrawn == NULL || pDrawn[0] == '\0' )
			pDrawn = pShell->GetClassname();
	}

	Overlay_TextAtPosition( vecOrigin, nLine, pDrawn, flDuration, r, g, b, a );

	return 1;
}

// One debug overlay line in a colour managed already chose from its LOS trace.
// flDuration <= 0 puts it in the pool redrawn every frame until
// clear_debug_overlays; > 0 is the engine's own timed overlay.
void SourceSharp_AddLineOverlay( const float *pStartXYZ, const float *pEndXYZ,
                                 int32_t r, int32_t g, int32_t b,
                                 int32_t bNoDepthTest, float flDuration )
{
	SOURCESHARP_TRACE();

	if ( !pStartXYZ || !pEndXYZ )
		return;

	const Vector vecStart( pStartXYZ[0], pStartXYZ[1], pStartXYZ[2] );
	const Vector vecEnd  ( pEndXYZ[0],   pEndXYZ[1],   pEndXYZ[2]   );

	if ( flDuration > 0.0f )
		Overlay_Line( vecStart, vecEnd, r, g, b, bNoDepthTest != 0, flDuration );
	else
		Overlay_AddLine( vecStart, vecEnd, bNoDepthTest != 0, r, g, b );
}

// The overlay counters, cumulative since the level began, for `debug-api status`.
void SourceSharp_OverlayCounts( uint32_t *pOutDrawn, uint32_t *pOutClipped,
                                uint32_t *pOutNoViewer, uint32_t *pOutNoOverlay,
                                int32_t *pOutViewerSet )
{
	const OverlayCounts_t &c = Overlay_Counts();

	if ( pOutDrawn )      *pOutDrawn      = c.nDrawn;
	if ( pOutClipped )    *pOutClipped    = c.nClipped;
	if ( pOutNoViewer )   *pOutNoViewer   = c.nNoViewer;
	if ( pOutNoOverlay )  *pOutNoOverlay  = c.nNoOverlay;
	if ( pOutViewerSet )  *pOutViewerSet  = Overlay_ViewerIsSet() ? 1 : 0;
}

// Stock's DrawAllDebugOverlays without the AI node editor and the gEntList walks.
// Not behind _DEBUG, as stock's ("allow this in release for now").
void DrawAllDebugOverlays( void )
{
	// First, so a frame with nobody looking is told apart from one that drew nothing.
	SourceSharp_EstablishOverlayViewer();

	Overlay_DrawPooledLines();
}
