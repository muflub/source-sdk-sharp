//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the debug overlay leaf library ======================//
//
// plan_entity.md Phase 4, step 4c. The drawing half of `ndebugoverlay.cpp` and
// `debugoverlay_shared.cpp`, pulled out from under the entity so that the two
// stock files can leave the server's vpc — and so that step 7's client half can
// call the same code rather than growing a second copy of it.
//
// WHAT THIS IS AND IS NOT. plan_entity.md §6 keeps mechanism in C++ and nothing
// else; an overlay call is a position, a colour and a duration handed to
// `IVDebugOverlay`, which is mechanism by that test — it decides nothing about
// what the game does. WHICH overlay is drawn, and for which entity, is policy
// and is NOT here: the entity-facing entries live in
// sourcesharp/game/server/shell_debugoverlays.cpp, they take a CBaseHandle, and
// they resolve it through the slot table.
//
// So this file is entity-free in the same sense bone_library.h is, and by the
// same checkable rule: it does not include cbase.h, and with comments stripped
// it names none of the entity classes, none of the entity interfaces and neither
// handle template. overlay_library_test.cpp holds the forbidden list and asserts
// it over both files.
//
// THE LIST IS NOT SPELLED IN THIS COMMENT, and that is deliberate rather than
// coy. tools/entity-census-patterns matches TEXT and does not strip comments:
// step 3g's own replacement comment put two of the three cast sites it had just
// deleted straight back into the census. A tombstone that names its subject
// literally is a tombstone that keeps the count it was written to lower.
//
// THE VIEWER, AND THE STUBS IT REPLACES.
//
// Six of debugoverlay_shared.cpp's entries had become `#ifdef SOURCESHARP
// return;` — Line, Triangle, Text, DrawTickMarkedLine, DrawGroundCrossHairOverlay
// and EntityBounds — because each clipped against a local player held as a
// pointer to a class this build no longer has. plan_entity.md §7: *"a behaviour that is refused is a
// ContractBaseline entry with a reason, never a silent no-op."* Six silent
// no-ops is the shape that section forbids.
//
// They are not refusals here. Stock's rule was "clip to the local player; with
// no local player, draw nothing", and the local player still exists — it is a
// managed entity, so what was missing was never the player but a way to ask it
// without a pointer to a class that is gone. Overlay_SetViewer takes the two
// vectors the clip actually needs and the frame step fills them in from a
// HANDLE. With no viewer set the answer is stock's own — nothing is drawn — and
// it is COUNTED as a refusal rather than being indistinguishable from a build
// that stopped drawing. Overlay_Counts() is that readout.
//
// WHAT STEP 6c REMOVED, AND THE RULE IT APPLIED.
//
// Seven entries left: BoxDirection, SweptBox, EntityTextByIndex, ScreenText,
// Grid, Cross3D and Cross3DSize. 4c ported the whole of stock's overlay
// namespace on the argument that step 7's client half would want it; measured
// at 76ce46be2, nothing in this build reached any of the seven by ANY route.
// Not a cross-TU import (nm over all 108 objects on the server link line), not
// an intra-TU call (objdump -r over this file's own object), not a name -- no
// ConVar, no ConCommand, no registration macro, no managed wrapper, nothing in
// tools/, docs/ or cfg/. All seven were already being dropped by
// -Wl,--gc-sections before the shipped server.so was written.
//
// THE RULE WAS NOT "IT HAS NO TEST". Three of the seven had tests and four did
// not, and deleting by that line is what step 5e refused to do -- the untested
// ones are the MORE dead, not the less. The rule is "nothing in this build can
// ask for it", applied to all twenty-eight entries at once, and what it keeps
// is not a matter of symmetry with stock: Overlay_Text had no caller of its own
// name either and it stayed then, because a recorded refusal cited its body as
// the witness for a behaviour this build declares missing.
//
// THE SERVER GC CULL TOOK SEVEN MORE (2026-10-01, user ruling): Overlay_Text,
// the clip and viewer accessors that existed for tests (Overlay_PointIsVisible,
// Overlay_MaxDistanceSqr, Overlay_ViewerOrigin/Forward) and the pool's test
// readouts (Overlay_PooledLineCount, Overlay_ResetPool). server.so's
// --print-gc-sections discarded each and the client never compiled this file.
// ContractBaseline's bViewCheck refusal now records NDebugOverlay::Text as
// absent as a whole, with no tree-local witness.
//
//=============================================================================//
#pragma once

#include "mathlib/vector.h"

//-----------------------------------------------------------------------------
// THE READOUT.
//
// An overlay is the check that cannot fail if all anybody asserts is "it did
// not crash": a build that draws nothing passes that. These four counters are
// what distinguishes a zero that means "nothing asked" from a zero that means
// "the drawing stopped working", and they are the same device 1g's
// `status.think` and 6a's `cs_cl_interp` are.
//
//   nDrawn      calls that reached IVDebugOverlay
//   nClipped    calls a viewer was set for and that the clip rejected —
//               too far, or behind the eye. Stock's own early-outs.
//   nNoViewer   calls that needed a viewer and had none. Stock drew nothing
//               here too; the difference is that this build says how often.
//   nNoOverlay  calls made with no IVDebugOverlay at all — a dedicated server,
//               or before the interface is connected.
//-----------------------------------------------------------------------------
struct OverlayCounts_t
{
	unsigned int nDrawn;
	unsigned int nClipped;
	unsigned int nNoViewer;
	unsigned int nNoOverlay;
};

const OverlayCounts_t &Overlay_Counts();
void Overlay_ResetCounts();

//-----------------------------------------------------------------------------
// THE VIEWER. Set once per frame by whoever knows where the eyes are; cleared
// when there is nobody. Overlay_ViewerIsSet answers what the clip will do
// before a draw is attempted, which is what a test asserts against.
//-----------------------------------------------------------------------------
void Overlay_SetViewer( const Vector &vecEyeOrigin, const Vector &vecEyeForward );
void Overlay_ClearViewer();
bool Overlay_ViewerIsSet();

// Both ends of a line: stock keeps a line whose FAR end is in range, and keeps
// a line if EITHER end is in front of the eye. Two rules, not one applied twice.
bool Overlay_SegmentIsVisible( const Vector &vecStart, const Vector &vecEnd );

//-----------------------------------------------------------------------------
// UNCLIPPED DRAWING. Everything here goes straight to IVDebugOverlay: stock
// clips none of these either.
//-----------------------------------------------------------------------------
void Overlay_BoxAngles( const Vector &origin, const Vector &mins, const Vector &maxs,
                        const QAngle &angles, int r, int g, int b, int a, float flDuration );
void Overlay_Box( const Vector &origin, const Vector &mins, const Vector &maxs,
                  int r, int g, int b, int a, float flDuration );
void Overlay_TextAtPosition( const Vector &origin, int nLineOffset, const char *pText,
                             float flDuration, int r, int g, int b, int a );
void Overlay_ClearAll();

//-----------------------------------------------------------------------------
// CLIPPED DRAWING. Keeps stock's rule and counts the branch it took.
// (Overlay_Text, NDebugOverlay::Text's port, went in the server gc cull: no
// caller in either DLL. ContractBaseline records it as absent.)
//-----------------------------------------------------------------------------
void Overlay_Line( const Vector &origin, const Vector &target,
                   int r, int g, int b, bool bNoDepthTest, float flDuration );

//-----------------------------------------------------------------------------
// THE OVERLAY LINE POOL. Twenty slots, oldest reused, drawn once a frame.
// ndebugoverlay.cpp's `m_debugOverlayLine` / `m_nDebugOverlayIndex` pair and
// its three functions, with the file-scope globals replaced by the accessor
// idiom entity_debug_globals.h established (plan.md §1a: nothing is a global).
//
// The LOS test that decided the colour is NOT here: it is a trace, the filter
// it wants is entity-aware, and the caller already has one. Overlay_AddLine
// takes the colour the caller decided on.
//-----------------------------------------------------------------------------
enum { OVERLAY_POOL_LINES = 20 };

void Overlay_AddLine( const Vector &vecStart, const Vector &vecEnd, bool bNoDepthTest,
                      int r, int g, int b );
void Overlay_DrawPooledLines();
