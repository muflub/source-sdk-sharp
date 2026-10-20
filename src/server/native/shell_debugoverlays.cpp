//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the frame's debug overlays ==========================//
// SS_REASON c  static-init registration
//
// plan_entity.md Phase 4, step 4c: damage and particles, and the overlays that
// came with them. This file was carried across in plan_gamedll_after.md A4 as
// DrawAllDebugOverlays and nothing else; it is now the SERVER's whole overlay
// shim — the frame step, the two entries the rest of the build reaches by name,
// and the two ABI slots that let managed code draw an overlay FOR A HANDLE.
//
// The drawing itself is not here. It is sourcesharp/game/shared/overlay_library,
// which is entity-free so that step 7's client half can call the same code; this
// file is the half that knows what an entity is, and every one of its entity
// answers goes through a CBaseHandle and the slot table.
//
// WHAT 4c DELETED FROM THIS FILE, AND WHY IT IS A DELETION AND NOT A STUB
//
// 1. THE AI NODE EDITOR BLOCK, which was `if (engine->IsInEditMode())` and 60
//    lines under it. It held plan_entity.md §3.16's FAMILY 6 — the hint node's
//    owner, turned into an entity through a void pointer — and it was the only
//    site of that family on this side outside baseentity_statics.cpp.
//
//    It was also a NULL DEREFERENCE. shell_ai_absent.cpp defines
//    `CAI_NetworkManager *g_pAINetworkManager = NULL` and `CAI_Network
//    *g_pBigAINet = NULL`, because Phase 7 decided this mod designs its own AI
//    in C# and none of ai_*.cpp is in the build. The block opened with
//    `g_pAINetworkManager->GetEditOps()->m_bLinkEditMode` with no test, so
//    `map <name> edit` did not draw the wrong thing here — it crashed the
//    server. The end of the function has the test (`if ( g_pAINetworkManager )`)
//    and the top of the block never did.
//
//    So it is deleted rather than corrected, on the same grounds as the two
//    gEntList walks the note further down records: the feature is the AI node
//    editor, the AI node editor needs an AI network, and there is no AI network
//    to edit. Recorded as a refusal in ContractBaseline (AiNodeEditorOverlays)
//    rather than left as an empty `if`.
//
// 2. THE PICKER OVERLAY'S SDK ARM, which asked FindPickerEntity for a
//    CBaseEntity and called three of its members. shell_entity_orphans.cpp
//    already answers NULL there and says why. What replaces it is NOT another
//    NULL: SourceSharp_DrawEntityOverlay below draws the same three things —
//    the name, the bounding box, the origin marker — for an entity named by
//    HANDLE, which is the form both hierarchies have. Managed code picks the
//    subject, because managed code is what owns the player's aim.
//
// A HANDLE, AND NOT AN INDEX, AND NOT A DEFAULT ONE. The invalid handle is
// INVALID_EHANDLE_INDEX. A zeroed handle is entry 0 serial 0, which is a REAL
// SLOT — worldspawn — so a caller that forgot to fill one in would draw an
// overlay on the world and see something, which is indistinguishable from
// working. SourceSharp_DrawEntityOverlay refuses the invalid sentinel, counts
// the refusal, and answers 0; it does NOT refuse entry 0, because drawing an
// overlay on the world is a legitimate thing to ask for.
//
// THE READOUT. Overlay_Counts() is four numbers and they reach `debug-api status`
// through OverlayCounts below. An overlay is the check that cannot fail if all
// anybody asserts is "it did not crash" — a build that draws nothing passes
// that — so `drawn` is the work and `noViewer` / `clipped` / `refused` are the
// reasons a zero would be honest. Same device as step 1g's `status.think`.
//
//=============================================================================//

#include "ss_util.h"            // engine, ss::UTIL_TraceLine

// The engine's globals (connected in shell_gamedll.cpp); cbase.h supplied them.
extern CGlobalVars *gpGlobals;
#include "overlay_library.h"
#include "slot_table.h"
#include "shell_standalone.h"
// SourceSharp_PlayerStateForEdict / SourceSharp_ClientViewOrigin: defined in
// shell_gameclients.cpp now (plan_server_native_min P1); they were declared by
// the deleted shell_playerinfo.h.
CPlayerState *SourceSharp_PlayerStateForEdict( edict_t *pEdict );
bool SourceSharp_ClientViewOrigin( edict_t *pClient, Vector *pOut );
#include "shell_diagnostics.h"
#include "PlayerState.h"


// Defined elsewhere in the server and reached by name, exactly as
// gameinterface.cpp reached them. There is no header that declares this one.

// DebugDrawLine WENT (plan_server_native_v2 T5): no server object named it any
// more (nm U over the link edge), and --gc-sections had dropped it. Its body,
// Overlay_LinePadded, went with it.

// clear_debug_overlays, which empties the engine's overlay list at once, is a
// diagnostic: it lives in shell_debugoverlays_debug.cpp, which only a debug
// build compiles.

//=============================================================================//
// THE VIEWER
//=============================================================================//

//-----------------------------------------------------------------------------
// Where the overlays are being looked at from, once a frame: NOWHERE, and that
// is what this build has always answered.
//
// The viewer used to be established from the player the debug picker was on
// (EntityDebug_SelectingPlayer), gated on the picker, r_visualizetraces or
// `developer` being on. Nothing in either DLL ever set the picker: stock's
// ent_* picker commands were never ported, so the slot was -1 for the process
// and every arm past the gate cleared the viewer. plan_server_native_v2 T6
// deleted the picker's two globals, the two ConVarRefs the gate read and the
// unreachable arm; what is left is the answer every frame already got, so a
// clipped overlay counts as "no viewer" exactly as before.
//-----------------------------------------------------------------------------
static void SourceSharp_EstablishOverlayViewer()
{
	Overlay_ClearViewer();
}

//=============================================================================//
// AN OVERLAY FOR A HANDLE — the two ABI slots
//=============================================================================//

//-----------------------------------------------------------------------------
// Draw an entity's overlay: its text at its origin, and its collision box.
//
// THE SUBJECT IS A HANDLE. Every entity in this build has one -- it is what the
// slot table is keyed on and what an entity property puts on the wire -- and it
// is the ONLY identity the two hierarchies share. An entity POINTER would have
// been a decision about which hierarchy the subject is on, made by the caller,
// which is the decision plan_entity.md §3.16 exists to remove.
//
// nLine offsets the text downward so several can stack at one spot, exactly as
// DrawWorldText's does.
//
// ANSWERS 1 when something was drawn and 0 when the call was refused, and the
// caller is expected to read it: an overlay that draws nothing and says nothing
// is the failure mode this whole readout exists to make visible. The three
// refusals, in order:
//
//   * the invalid sentinel. INVALID_EHANDLE_INDEX and nothing else -- a ZEROED
//     handle is entry 0 serial 0, which is worldspawn, a real slot. Drawing on
//     the world when the caller meant "nothing" is precisely the failure that
//     looks like success, so the sentinel is refused explicitly and `default`
//     is not treated as one.
//   * a handle the slot table does not resolve: the entity is gone, or the
//     entry was reused and the serial says so.
//   * no overlay interface at all -- a dedicated server. Counted by the library
//     rather than here, because every draw shares that answer.
//-----------------------------------------------------------------------------
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

	// The text: what the caller asked for, or the entity's own name when it
	// asked for nothing. Falling back to the classname when the entity is
	// unnamed is what every ent_text overlay in stock did.
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

//-----------------------------------------------------------------------------
// ONE LINE IN THE DEBUG OVERLAY, FOR A MANAGED CALLER -- the primitive this
// file's own note said the ABI did not have.
//
// WHAT IT IS FOR. `drawline` and `drawcross` are managed DevCommands again.
// They were native commands in game/server/client.cpp, came across whole at the
// Phase 4 exit, and Phase 1b retired them because the file they had come across
// into was deleted and there was nothing on the managed side of the boundary
// for a body to call: the ABI published DrawEntityOverlay (TEXT on an entity),
// OverlayCounts and OverlayResetCounts, and no line.
//
// NO LOS TRACE HERE, WHICH IS THE HALF THAT MOVED. UTIL_AddDebugLine above does
// two things -- it traces MASK_BLOCKLOS to pick white-or-red, and it puts the
// result in the pool -- and the split between them is the boundary. The trace
// is managed now (TraceLine with SOURCESHARP_TRACE_BLOCKLOS, whose native
// return is `tr.fraction < 1.0f || tr.startsolid`, which is stock's colour
// condition exactly), so this takes the colour already decided. That keeps the
// DECISION in C# and leaves the mechanism here, which is the rule the whole
// port is built on.
//
// THE DURATION DECIDES WHICH LIFETIME, and the two are genuinely different
// things rather than a parameter with a special case:
//
//   flDuration <= 0  ->  THE POOL. Overlay_AddLine puts the line in the
//                        twenty-slot ring the frame step redraws every frame,
//                        oldest reused, emptied by `clear_debug_overlays`.
//                        This is what UTIL_AddDebugLine did and therefore what
//                        `drawline` has always done: the line stays up.
//   flDuration >  0  ->  THE ENGINE'S OWN TIMED OVERLAY, through Overlay_Line,
//                        which clips against the viewer and counts itself like
//                        every other clipped draw in the library.
//
// A caller that named no duration and got the engine's `0` would get a line
// drawn for ONE FRAME, which from a console is indistinguishable from a command
// that did nothing -- the silent no-op plan_entity.md §7 forbids and the reason
// these two commands were retired out loud rather than left drawing nothing.
//
// Note that the pooled line reaches the engine through Overlay_Line as well,
// once a frame from Overlay_DrawPooledLines, so both arms are clipped and
// counted the same way. The difference is lifetime and nothing else.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// The overlay readout, four counters, for `debug-api status`.
//
// NATIVE COUNTS AND MANAGED REPORTS, because debug-api's log is managed-only --
// anything native prints goes to console.log where the gate cannot read it.
// The counters are cumulative since the last reset; managed code resets them
// when it wants a per-interval reading.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Zero the four counters, so a caller can read an interval rather than a total.
//-----------------------------------------------------------------------------
void SourceSharp_OverlayResetCounts( void )
{
	Overlay_ResetCounts();
}

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

//=============================================================================//
// THE FRAME STEP
//=============================================================================//

//-----------------------------------------------------------------------------
// Purpose: SourceSharp_FrameDrawAllDebugOverlays, step 12 of the frame
// (shell_frame.cpp). Stock's DrawAllDebugOverlays, with the AI node editor
// and the two entity-list walks gone and the viewer established first.
//
// NOT BEHIND _DEBUG, and that is stock's decision rather than an oversight --
// its own comment beside the call reads "allow this in release for now".
//
// THE TWO gEntList WALKS THAT USED TO BE HERE ARE GONE and the note is kept
// because it is load-bearing. The per-entity overlay driver (`developer 1`) and
// sv_massreport both opened by turning CEntInfo::m_pEntity -- which is an
// IHandleEntity, not an entity of any particular class -- into one with a
// C-style cast and no test of any kind. This build puts entities in that list
// which are not of that class at all, so every call those loops then made was a
// virtual dispatched through a slot past the end of the interface the two
// hierarchies share, landing on whatever the other class declared there.
//
// Measured, so as not to overstate it: with ss_sandbox loaded, `developer 2`
// and `sv_massreport 1` did not crash the server -- the reads land on live
// memory and the dispatch lands on a real function. It is undefined behaviour
// that happens to be quiet, which is the worst kind to leave in.
//
// They were DELETED rather than corrected because the feature they drive is one
// class's own from end to end and has no reader on the other hierarchy. What
// replaces them for the parallel hierarchy is SourceSharp_DrawEntityOverlay
// above: same three things drawn, named by handle, and driven by managed code
// which is what owns the aim that picks the subject.
//-----------------------------------------------------------------------------
void DrawAllDebugOverlays( void )
{
	// FIRST, because everything below that clips wants it and because clearing
	// it is how a frame with nobody looking is told apart from a frame that drew
	// nothing for a different reason.
	SourceSharp_EstablishOverlayViewer();

	// --------------------------------------------------------
	//  Draw debug overlay lines
	// --------------------------------------------------------
	Overlay_DrawPooledLines();

	// DrawMessageEntities went (plan_server_native_min P1): an empty body;
	// the managed point_message draws through the debug-overlay path from C#.
}
