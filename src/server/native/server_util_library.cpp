//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//===== SourceSharp — the server utility mechanism that names no entity ======//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// plan_entity.md Phase 4 exit, lane p4-util. `game/server/util.cpp` is one of
// SCC 11's 31 translation units, and these families left it for the shim.
//
// WHAT MADE EACH ONE MOVABLE, since 4e measured that ten of util.cpp's
// nineteen cross-edges name a `CBaseEntity`, a `CBasePlayer` or the stock
// entity factory in their SIGNATURE and so cannot: every family below reaches
// the ENGINE and the C runtime and nothing else. None of them takes, returns,
// stores or dereferences an entity of either hierarchy.
//
// `UTIL_IsCommandIssuedByServerAdmin` is DELIBERATELY NOT HERE, though it fits
// every sentence above. Lane `phase4-exit` has already relocated it to
// `sourcesharp/game/server/shell_commandclient.cpp`, which is the better home:
// that file already defines `UTIL_GetCommandClientIndex`, the one symbol the
// function's body needs. Two lanes moving one body to two addresses is a
// duplicate definition, so this lane left stock's copy exactly where it is and
// says so rather than racing for it.
//
// TWO OF THEM MOVED WHOLE RATHER THAN AS THE ONE SYMBOL THE SHIM NAMED,
// because the part the shim names is not the part that owns the state:
//
//   * `g_NullSurface` is stock's file static and `UTIL_SetTrace` reads it too.
//     `UTIL_SetTrace` could NOT come: it calls `CBaseEntity::Instance`, which
//     is not a cross-edge today, so moving it would ADD one. So the object is
//     defined here with external linkage and util.cpp declares it `extern` —
//     one object, not two. A second file static of the same name would be
//     neither a compile error nor a link error; it would be a second surface,
//     and the two would drift.
//
//   * The CHECK-CLIENT SUBSYSTEM came as a unit: `CCheckClient` and its
//     `g_CheckClient`, the accessor `shell_serversystems.cpp` registers it by,
//     `UTIL_GetNewCheckClient`, `UTIL_GetCurrentCheckClient`,
//     `UTIL_SetClientVisibilityPVS`, `sv_strict_notarget`,
//     `UTIL_FindClientInPVSGuts` and `UTIL_FindClientInPVS`. Every one of the
//     last six reads `g_CheckClient`'s fields directly, and three of them are
//     file statics no other TU can name. The shim named exactly two of the
//     eight; taking those two would have exported the state instead.
//
// `GetContainingEntity` inside that subsystem is a weak inline emitted per TU
// (`nm` reads it `W` in nine objects), not a cross-edge, and the
// `#ifndef SS_CBASEENTITY_ALIAS` arms it sits inside came across verbatim —
// each one is stock's behaviour today, not a future-proofing.
//
// Every declaration stays in `game/server/util.h`, so not one caller changed.
// This TU is SERVER-ONLY, because util.cpp is: `client_csharp.vpc` has no row
// for either, and `ServerUtilLibrary.TheClientVpcDoesNotCompileIt` asserts that
// direction rather than assuming it.
//
//=============================================================================//

#include <stdarg.h>
#include "ss_util.h"   // the util.h helpers, and engine / enginetrace / CTraceFilter
#include "ss_shareddefs.h"   // ss::kLanguageGerman, ss::BLOOD_COLOR_RED (lane R)
// CServerSystem, the base CCheckClient derives from and the type its accessor
// returns. util.cpp includes this first for the same reason.
#include "shell_serversystem.h"
#include "bspfile.h"
#include "mathlib/mathlib.h"
#include "vstdlib/random.h"
// ss::Effects(), for UTIL_Smoke (lane SP's ss_effects.h).
#include "ss_effects.h"
// The emitter block below: recipient filters (CPVSFilter/CPASFilter), the temp
// entity forwarders, the world subject the impact arm asks first, and the world
// adapter that answers for it.
#include "ss_recipient_filter.h"
#include "shell_tempents.h"
#include "ss_world.h"
#include "world_subject.h"
#include "collisionutils.h"

#include "managed_convar_ref.h"
#include "ss_string.h"
#include "trace_hit.h"   // ss::TraceHit: the IHandleEntity the engine stored in trace_t::m_pEnt

extern CGlobalVars *gpGlobals;   // shell_engineboundary.cpp


//------------------------------------------------------------------------------
// DBG_AssertFunction
//------------------------------------------------------------------------------
#ifdef	DEBUG
#endif	// DEBUG

//------------------------------------------------------------------------------
// g_NullSurface
//------------------------------------------------------------------------------
// So we always return a valid surface
// EXTERNAL linkage, unlike stock's: util.cpp's UTIL_SetTrace still reads it
// and could not travel (it calls CBaseEntity::Instance, which is not a
// cross-edge today, so moving it would ADD one). One object, declared extern
// over there — never a second copy.
csurface_t	g_NullSurface = { "**empty**", 0 };

//------------------------------------------------------------------------------
// UTIL_ClearTrace
//------------------------------------------------------------------------------
void UTIL_ClearTrace( trace_t &trace )
{
	memset( &trace, 0, sizeof(trace));
	trace.fraction = 1.f;
	trace.fractionleftsolid = 0;
	trace.surface = g_NullSurface;
}

// UTIL_VarArgs went (plan_server_native_min P1): a static buffer shared by
// every caller; its one live caller formats into a local instead.

//------------------------------------------------------------------------------
// the check-client subsystem
//------------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The CCheckClient system went (plan_server_native_min P1): its two PVS
// bitsets had no reader (UTIL_FindClientInPVS is gone), and GetNewCheckClient
// `continue`d every candidate, so the walk it drove produced nothing.



//-----------------------------------------------------------------------------
// Gets the current check client....
//-----------------------------------------------------------------------------


// UTIL_ClientPVSIsExpanded, the box overload of UTIL_FindClientInPVS and the
// whole CCheckClient state they read went with plan_server_native_min P1: no
// reader of the bitsets was left, and GetNewCheckClient skipped every
// candidate. The edict-flavoured UTIL_FindClientInPVS below keeps its one
// live caller (ragdoll_shared.cpp's fade check) and reads none of it.

//-----------------------------------------------------------------------------
// Purpose: Returns a client (or object that has a client enemy) that would be a valid target.
//  If there are more than one valid options, they are cycled each frame
//  If (self.origin + self.viewofs) is not in the PVS of the current target, it is not returned at all.
// Input  : *pEdict - 
// Output : edict_t*
//-----------------------------------------------------------------------------
static CManagedConVarRef sv_strict_notarget( "sv_strict_notarget" );

//-----------------------------------------------------------------------------
// Purpose: Returns a client that could see the entity directly
//-----------------------------------------------------------------------------

//------------------------------------------------------------------------------
// UTIL_Smoke — moved for CYCLE MEMBERSHIP, not for the census.
//
// It was never a shim->stock cross-edge; it was game/shared/util_shared.cpp's
// ONE remaining sole-definer debt to game/server/util.cpp, and while that edge
// stood the two were locked together inside SCC 11. g_sModelIndexSmoke is
// combatweapon.cpp's and is reached here by the same extern declaration
// util.cpp used.
//------------------------------------------------------------------------------

extern short g_sModelIndexSmoke;

void UTIL_Smoke( const Vector &origin, const float scale, const float framerate )
{
	ss::Effects()->Smoke( origin, g_sModelIndexSmoke, scale, framerate );
}

//------------------------------------------------------------------------------
// THE THREE TEMP-ENTITY EMITTERS, and the two helpers they could not leave
// behind. Out of game/server/util.cpp.
//
// 4e listed these three as "temp-entity emitters; see the note under
// util_shared.cpp" and did not move them, because at that point moving one
// CONVERTED an edge instead of removing one. What changed is the two helpers:
// UTIL_BubbleTrail's water surface and UTIL_ImpactTrace's validity check are
// both in util.cpp, and taking the emitters WITHOUT them just moves the edge
// next door. Taking all five, plus UTIL_Bubbles which shares the bubble model
// index, leaves nothing next door to land on.
//
// EVERY REMAINING NAME IN THESE BODIES IS ALREADY THE SHIM'S, checked with nm
// over the object table rather than assumed:
//     UTIL_ShouldShowBlood        util_shared_library.o   (7f)
//     TE_BloodStream, TE_Bubbles  shell_te_fwd_d.o
//     SourceSharpTe_SuppressFilter shell_tempents.o
//     SourceSharp_IsTheWorld      world_subject_dll.o
//     GetWorldEntity, CWorld::ImpactTrace  world_adapter.o
//     TraceEntity                 a weak inline, emitted in ten objects
// and CBaseEntity::ImpactTrace is a VIRTUAL CALL, which emits no symbol at all.
//
// THE ONE COST, named rather than hidden: `g_Language` is defined in
// game/shared/gamerules.cpp, so UTIL_BloodStream's German-blood arm becomes a
// shim->stock edge into a file that had none. That arm is stock's behaviour and
// is LIVE, so it was not deleted -- deleting an unreachable-looking arm is the
// shape of all six recorded silent losses. gamerules.cpp is not in SCC 11, so
// the edge leaves the atomic batch even though it does not leave the census.
//------------------------------------------------------------------------------

// g_Language IS DECLARED IN C# (SourceSharp.Server.GLanguageConVar),
// plan_server_novalve.md P2.3b; this reads it by name.
static CManagedConVarRef s_Language( "g_Language" );

void UTIL_BloodStream( const Vector &origin, const Vector &direction, int color, int amount )
{
	if ( !UTIL_ShouldShowBlood( color ) )
		return;

	if ( s_Language.GetInt() == ss::kLanguageGerman && color == ss::BLOOD_COLOR_RED )
		color = 0;

	ss::CPVSFilter filter( origin );


	TE_BloodStream( filter, 0.0, &origin, &direction, 247, 63, 14, 255, MIN( amount, 255 ) );
}				

// CWaterTraceFilter went (plan_server_native_min P1): nm shows no vtable or
// typeinfo for it emitted or imported by any server object; nothing constructed
// one.



