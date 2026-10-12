//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//=============================================================================//
// SS_REASON b  an interface resolved by name at load
// c_baseentity_statics.cpp's TENANTS, RULED ONE BY ONE — CLIENT_FLIP lane 2
// (`c8-entitystatics`). See shell_entitystatics.h for what this file is for.
//
// THE MEASUREMENTS EVERY RULING BELOW RESTS ON, each from a grep over the whole
// compiled tree rather than from reading one file:
//
//   * g_AimEntsList, g_InterpolationList and g_TeleportList are WRITTEN FROM
//     ONE TRANSLATION UNIT — game/client/c_baseentity.cpp, at :2176/:2196-2206
//     (aim-ents), :6099/:6107 (interpolation) and :6116/:6124 (teleport) — and
//     that file is the flip's removal set. After the flip all three are
//     permanently empty, so every loop over them runs zero times. That is not
//     an inference from the guards: the guards were WRITTEN from it, and
//     c_baseentity_statics.cpp says so in its own words at the head of its
//     frame-loop section.
//
//   * recordinglist has exactly two writers, c_baseentity.cpp:6006 and :6010,
//     both C_BaseEntity::SetToolRecording. Same removal set, same conclusion.
//
//   * predictables (g_Predictables) is added to only from C_BaseEntity's four
//     member call sites. Same.
//
//   * the managed hierarchy owns NO CInterpolatedVar. shell_standalone_client.h
//     :1383-1392 and .cpp:289-294 both record that the two per-entity histories
//     became managed rings (SourceSharp.Client/Interpolation.cs) at step 6a, and
//     the managed pass runs from CSourceSharpStandaloneInterpSystem::PreRender
//     (shell_standalone_client.cpp:3355) — not from anything in this file.
//
// SO THE FRAME-LOOP FUNCTIONS DO NOT "STOP WORKING" WHEN THEY RETIRE. They walk
// registries that hold nothing and drive histories that no longer exist. What
// WOULD be lost, and therefore what each retired row has to name, is the
// BEHAVIOUR — and every row below names what performs it now.
//
// THE ONE PLACE THAT IS A REAL GAP is the frame gate, and it is refused out
// loud rather than absorbed. See EntityInterp_UpdateFrameGate.
//=============================================================================//

#include "cbase.h"

#include "shell_entitystatics.h"
#include "entity_abs_query_state.h"
#include "entity_shared_statics.h"
#include "toolframework_client.h"
#include "inetchannelinfo.h"
// For the evicted vcollide_wireframe change callback below: the standalone
// registry walk (SourceSharp_StandaloneClientCount / At) and C_StandaloneEntity.
#include "shell_standalone_client.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//=============================================================================//
//
// THE RULING TABLE.
//
// FIFTEEN ROWS AND NOT THIRTEEN. The lane's brief names thirteen functions; two
// more — EntityAbs_PushEnableRecomputations and EntityAbs_PopEnableRecomputations
// — share the eight-deep save stack with EntityAbs_SetRecomputationsEnabled,
// whose Assert reads it. A ruling that moved four of a six-function group and
// left two behind would split one piece of state across the flip boundary, so
// the group is ruled together and the table says so.
//
//=============================================================================//

//-----------------------------------------------------------------------------
// The one instance. A function-local static rather than a file-scope one, which
// is the form the project's rule allows and the form entity_abs_query_state.h's
//=============================================================================//
//
// THE ALLOCATOR. MOVED, bodies unchanged.
//
// TWO THINGS ABOUT THIS MOVE, BOTH OF WHICH c_baseentity_statics.h WARNED ABOUT
// AND ONE OF WHICH IS A REAL COST THIS LANE PAYS:
//
//   * MEM_ALLOC_CREDIT() credits __FILE__. That string is now this file rather
//     than game/client/c_baseentity.cpp, so every entity allocation is
//     re-credited in a memory report. That is a RECORDED COST, not a defect —
//     the alternative is leaving the allocator in a translation unit the flip
//     deletes, and the credit string was already going to change when it did.
//
//   * the definitions must sit inside a memdbgoff/memdbgon bracket, because
//     memdbgon.h redefines new and delete and these bodies call MemAlloc_Alloc
//     directly. The bracket is reproduced here for that reason and not by
//     copying.
//
//=============================================================================//

#include "tier0/memdbgoff.h"

void *EntityMem_Alloc( size_t stAllocateBlock )
{
	Assert( stAllocateBlock != 0 );
	MEM_ALLOC_CREDIT();
	void *pMem = MemAlloc_Alloc( stAllocateBlock );
	memset( pMem, 0, stAllocateBlock );
	return pMem;
}

void *EntityMem_AllocDbg( size_t stAllocateBlock, const char *pFileName, int nLine )
{
	Assert( stAllocateBlock != 0 );
	void *pMem = MemAlloc_Alloc( stAllocateBlock, pFileName, nLine );
	memset( pMem, 0, stAllocateBlock );
	return pMem;
}

void EntityMem_Free( void *pMem )
{
	// get the engine to free the memory
	MemAlloc_Free( pMem );
}

#include "tier0/memdbgon.h"

//=============================================================================//
//
// THE SIX ABS-QUERY ACCESSORS. MOVED, bodies unchanged.
//
// Two of the three storages are inline function-local statics shared with the
// server in game/shared/entity_abs_query_state.h, which the flip does not
// delete. The third is the eight-deep save stack Push/Pop use, which was a
// file-static in the dying translation unit and comes here with them: it is read
// and written by these functions and by nothing else in the build.
//
// A FUNCTION-LOCAL STATIC RATHER THAN A FILE-SCOPE ONE, which is the project's
// rule and also the idiom the shared header already uses for the other two.
//
// @MULTICORE: all six are no-ops or answer `true` off the main thread. That rule
// stays in the bodies rather than in the storage, because the storage is shared
// with the server and the rule is not — the server's own SetAbsQueriesValid and
// IsAbsQueriesValid are inline in baseentity.h and have no ThreadInMainThread()
// arm, because the server never had one.
//
// WHAT THE CLIENT-SIDE CONSUMER OF THESE FLAGS WAS, AND WHY ITS LOSS IS NOT A
// REFUSAL. C_BaseEntity::CalcAbsolutePosition early-returned on
// !EntityAbs_RecomputationEnabledRef() (c_baseentity.cpp:4177) and three
// c_baseentity.h Asserts read the query flag. All four die with the class, so on
// the client these flags now gate nothing directly. They are NOT refused,
// because the invariant they express is enforced STRUCTURALLY for managed
// entities rather than by a flag: the managed transform work runs from
// CSourceSharpStandaloneInterpSystem::PreRender, which the engine raises at
// FRAME_RENDER_START — strictly after FRAME_NET_UPDATE_END, which is where the
// suppression was lifted. There is no managed path that can recompute a
// transform during a net update for the flag to have to stop.
//
//=============================================================================//

static bool *AbsRecomputationStack()
{
	static bool s_bStack[ 8 ];
	return s_bStack;
}

static unsigned short &AbsRecomputationStackPos()
{
	static unsigned short s_iPos = 0;
	return s_iPos;
}

void EntityAbs_SetQueriesValid( bool bValid )
{
	// @MULTICORE: Always allow in worker threads, assume higher level code is handling correctly
	if ( !ThreadInMainThread() )
		return;

	if ( !bValid )
	{
		EntityAbs_QueriesValidRef() = false;
	}
	else
	{
		EntityAbs_QueriesValidRef() = true;
	}
}

bool EntityAbs_AreQueriesValid()
{
	if ( !ThreadInMainThread() )
		return true;
	return EntityAbs_QueriesValidRef();
}

void EntityAbs_PushEnableRecomputations( bool bEnable )
{
	if ( !ThreadInMainThread() )
		return;
	if ( AbsRecomputationStackPos() < 8 )
	{
		AbsRecomputationStack()[ AbsRecomputationStackPos() ] = EntityAbs_RecomputationEnabledRef();
		++AbsRecomputationStackPos();
		EntityAbs_RecomputationEnabledRef() = bEnable;
	}
	else
	{
		Assert( false );
	}
}

void EntityAbs_PopEnableRecomputations()
{
	if ( !ThreadInMainThread() )
		return;
	if ( AbsRecomputationStackPos() > 0 )
	{
		--AbsRecomputationStackPos();
		EntityAbs_RecomputationEnabledRef() = AbsRecomputationStack()[ AbsRecomputationStackPos() ];
	}
	else
	{
		Assert( false );
	}
}

void EntityAbs_SetRecomputationsEnabled( bool bEnable )
{
	if ( !ThreadInMainThread() )
		return;
	// This should only be called at the frame level. Use PushEnableAbsRecomputations
	// if you're blocking out a section of code.
	Assert( AbsRecomputationStackPos() == 0 );

	EntityAbs_RecomputationEnabledRef() = bEnable;
}

bool EntityAbs_AreRecomputationsEnabled()
{
	if ( !ThreadInMainThread() )
		return true;
	return EntityAbs_RecomputationEnabledRef();
}

//=============================================================================//
//
// THE TWO ConVars THAT ARE PROTOCOL NAMES, AND THE ONE THAT IS NOT.
//
// cl_interpolate and cl_interp_npcs are FCVAR_USERINFO. plan_entity.md §6's
// ConVar row says the ones that legitimately stay native are "engine-protocol
// names the engine itself transmits or resolves", and these are transmitted:
// game/server/player.cpp:3509 reads the client's cl_interpolate BACK off the
// wire — `Q_atoi( QUICKGETCVARVALUE("cl_interpolate") )` — to decide whether to
// lag-compensate against an interpolating client. If the flip deleted the
// declaration, that read would find an empty string, Q_atoi would answer 0, and
// the server would silently change how it lag-compensates every player. So they
// move HERE rather than dying with the translation unit that happened to
// declare them.
//
// cl_extrapolate DOES NOT MOVE, and that is a deletion this file states rather
// than performs quietly. It fed CInterpolationContext::EnableExtrapolation,
// which only stock CInterpolatedVars read. The managed pass hard-codes
// `allowExtrapolation: false` (Interpolation.cs:1301) and has since step 6a, so
// the ConVar has ALREADY been inert for the whole of this port's life —
// Interpolation.cs:507-520 measured the flag false for all 10,380 Interpolate
// calls in the reference recording. Retiring it removes nothing that worked.
// Re-wiring it to the managed arm is a one-line change to a caller in
// Interpolation.cs and is written into
// .claude/entity-port/requests/c8-entitystatics.md as an available future
// change rather than taken here.
//
// cl_interp_npcs WENT TO MANAGED CODE IN PHASE 1c, and the distinction above
// is exactly why it could while cl_interpolate could not. The server reads
// cl_interpolate back off the wire by name (player.cpp:3509,
// QUICKGETCVARVALUE) and changes how it lag-compensates on the answer, which
// makes it protocol. NOTHING reads cl_interp_npcs in this build at all: its one
// reader was c_baseentity.cpp:5730, and that translation unit is not on the
// client link line. FCVAR_USERINFO makes the VALUE transmissible; it does not
// make anything read it. So the declaration -- default, flags and help text --
// is SourceSharp.Client/ConVars.cs's, per §6's ConVar row, and the transmitted
// name is unchanged.
//
//=============================================================================//

static ConVar cl_interpolate( "cl_interpolate", "1.0f", FCVAR_USERINFO | FCVAR_DEVELOPMENTONLY );

//=============================================================================//
//
// TWO RENDERING TOGGLES EVICTED FROM THE FLIP'S TWO HIERARCHY-DEFINERS — lane
// `f-block`, client family 9, plan_entity.md §6's ConVar row read BACKWARDS.
//
// §6's row is about ConVars that must not be DECLARED natively. These two are
// the opposite failure and the row covers them just as squarely: they were
// declared natively, inside translation units the flip DELETES, and READ from
// translation units that STAY. Nothing would have reported it. A ConVar whose
// defining TU leaves the link line does not fail to build and does not fail at
// runtime — the console variable is simply not there, the reader's `extern`
// finds nothing to bind, and a rendering toggle stops existing in silence.
//
// MEASURED, not inferred, with `nm --defined-only` over the 286 compiled client
// objects and `nm -u` over the readers:
//
//   vcollide_wireframe   defined  c_baseanimating.o   (in the removal set)
//                        read by  viewrender.o        (STAYS, §6 view render)
//                                 ragdoll.o           (STAYS, §6 ragdoll builder)
//   r_drawmodeldecals    defined  c_baseentity.o      (in the removal set)
//                        read by  shell_te_backends_client.o  (the PORT's own)
//
// ONE OF THE TWO IS NOW GONE AGAIN AND THE OTHER STAYS -- PHASE 1c, and the
// asymmetry is the finding rather than an inconsistency:
//
//   r_drawmodeldecals IS THE ENGINE'S. engine.so registers it itself (`strings
//   -a` finds the name in the shipped binary) and reads it for static props.
//   The declaration below was therefore a
//   DUPLICATE registration of an engine-protocol name; the engine's constructor
//   runs first, so the engine's object is the one the console and every
//   ConVarRef bind to, and this one's FCVAR_ALLOWED_IN_COMPETITIVE never
//   applied to anything. It is deleted, and the port's one reader
//   (shell_te_backends_client.cpp) resolves the ENGINE's by name.
//
//   vcollide_wireframe STAYS NATIVE, and it is the one ConVar in this file
//   Phase 1c did not move. Two reasons, either of which alone decides it:
//     * its CHANGE CALLBACK does native work -- it sweeps the standalone
//       registry calling UpdateVisibility on every client shell -- and a
//       managed [ConVar] carries a managed OnChanged. There is no ABI slot for
//       "re-evaluate every client shell's visibility", and appending one is the
//       thing this phase serialises into a single pass.
//     * viewrender.cpp, which §6 keeps native, reads it twice through
//       `extern ConVar vcollide_wireframe;`. An extern binds to a DEFINITION,
//       and a managed declaration is not one; each reader would have to become
//       a name-resolved ref first.
//
// THIS FILE AND NOT THE READERS, for the reason its own banner gives: it is
// already the port's home for tenants evicted from exactly these two TUs, and
// `cl_interp_npcs` two lines above is the same shape — a non-static ConVar
// whose defining TU left and whose old home keeps an `extern` until it goes.
// Its remit is widened from c_baseentity_statics.cpp's tenants to the flip's
// evicted tenants generally, and that widening is stated here rather than left
// to be discovered. Family 9 is ONE decision, so both live under one ruling.
//
// FILE SCOPE, NOT FUNCTION-LOCAL, and that is the registration hazard rather
// than a preference: ConVar's constructor puts itself on the pending list and
// CShellClientDLL::Init calls ConVar_Register( FCVAR_CLIENTDLL ) exactly once
// (shell_clientdll.cpp:973). A ConVar constructed lazily is constructed AFTER
// that call and never registers at all.
//
// NEITHER IS static: both still have a reader in the TU they came from, which
// keeps its `extern` until the flip removes it.
//
// THE CHANGE CALLBACK MOVED TOO, and it had to: it is the ConVar constructor's
// fifth argument, so leaving it behind would have replaced a defined symbol
// with an undefined one rather than fixing anything. Its stock body walked
// `ClientEntityList().FirstBaseEntity()` / `NextBaseEntity()` — client family
// 3's entity list, naming the leaving hierarchy twice — and `UpdateVisibility`
// is NOT on IClientRenderable, so there is no interface spelling that covers
// both hierarchies. It sweeps the standalone registry instead, through the
// index pair shell_prediction.cpp already uses. What that changes is stated
// plainly: on the SDK hierarchy the toggle forced every C_BaseEntity to
// re-evaluate visibility, and on this one it forces every C_StandaloneEntity
// to. After the flip those are the same set; before it, an SDK entity still
// compiled into this build no longer re-evaluates on a toggle of a cheat
// ConVar, and re-evaluates on its next model or PVS change as it always did.
//
//=============================================================================//

//-----------------------------------------------------------------------------
// THE SWEEP, WHICH IS NOW AN ABI SLOT AND NOT A CHANGE CALLBACK.
//
// It was `VCollideWireframe_ChangeCallback`, the fifth argument of a native
// `ConVar vcollide_wireframe` declared a few lines below where this now stands.
// Lane 1e-abi appended `EntitiesUpdateVisibilityAll` and the declaration moved
// to managed code (`VCollideWireframeConVar` in SourceSharp.Client/ConVars.cs),
// which is what §6's ConVar rule asks for: the name, the default, the flags and
// the help text are the tuning value, and while they sit in C++ managed code
// cannot see them.
//
// WHAT DID NOT CHANGE IS THE WALK. It is the standalone registry through the
// index pair shell_prediction.cpp already uses, calling `UpdateVisibility()` on
// each shell, exactly as the callback did. Stock's own walk was
// `ClientEntityList().FirstBaseEntity()` / `NextBaseEntity()` -- client family
// 3's entity list, naming the leaving hierarchy twice -- and `UpdateVisibility`
// is not on `IClientRenderable`, so there is no interface spelling that covers
// both hierarchies.
//
// A COUNT RATHER THAN void, and the ABI comment on the slot gives the argument:
// a sweep changes what the leaf system will do NEXT frame and is therefore
// invisible at the moment it runs, so a body that swept nothing would look
// exactly like one that swept everything. The count is what a caller and a gate
// can read. Shells that are gone from the registry mid-walk are skipped and not
// counted, which is the same NULL test the callback had.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientEntitiesUpdateVisibilityAll( void )
{
	const int nCount = SourceSharp_StandaloneClientCount();
	int32_t nSwept = 0;

	for ( int i = 0; i < nCount; i++ )
	{
		if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneClientAt( i ) )
		{
			pEntity->UpdateVisibility();
			nSwept++;
		}
	}

	return nSwept;
}

//=============================================================================//
//
// THE FRAME GATE — the live half of EntityInterp_InterpolateServerEntities, and
// THE ONE DECLARED GAP IN THIS LANE.
//
// WHAT IT COMPUTES is a frame-level policy and it is genuinely a policy, not a
// walk over a dead list: interpolation is off when the player has turned
// cl_interpolate off, when a timedemo is playing back, when the engine is
// paused, and — the one that matters in play — when the net channel has heard
// nothing for half a second, which is what stops a stalled client sliding every
// entity smoothly into a wall on a guess. It is preserved here, unchanged, and
// it writes EntityInterp_EnabledRef() in game/shared/entity_abs_query_state.h,
// which the flip does not delete.
//
// WHAT READS IT TODAY: nothing on the managed path. Stock's reader was
// C_BaseEntity::IsInterpolationEnabled (c_baseentity.h:2409), which dies with
// the class, and SourceSharp.Client's interpolation has no enable gate of any
// kind — ClientModule.cs:713,747 raise SamplePass and RenderPass on their two
// stages unconditionally.
//
// SO THIS IS A REFUSAL AND IT IS DECLARED AS ONE. It is not refused per call,
// because the gate being CLOSED is the normal case and warning once a frame for
// the whole of a healthy connection would be a flood that means nothing. It is
// refused when the gate actually CLOSES — the moment the behaviour would have
// differed — and never more than once per closing edge, because a stall lasts
// many frames and a stall is one event.
//
// THE FIX IS ONE LINE IN A FILE THIS LANE DOES NOT OWN.
// SourceSharp_ClientInterpBlocks (shell_standalone_client.cpp:3206) is the
// single call the managed pass makes to fetch the list, and it already answers
// NULL for "nothing to interpolate", which the managed pass already handles by
// doing nothing. Returning NULL when !EntityInterp_EnabledRef() would land this
// behaviour with no ABI slot and no managed change at all.
// shell_standalone_client.cpp is NOBODY's file in this wave, so the change is
// written into .claude/entity-port/requests/c8-entitystatics.md for the
// integrator instead of taken here — and refused, loudly, until it is.
//
//=============================================================================//

static bool &EntityInterpGateWasOpen()
{
	static bool s_bWasOpen = true;
	return s_bWasOpen;
}

void EntityInterp_UpdateFrameGate()
{

	EntityInterp_EnabledRef() = cl_interpolate.GetBool();

	// Don't interpolate during timedemo playback
	if ( engine->IsPlayingTimeDemo() || engine->IsPaused() )
	{
		EntityInterp_EnabledRef() = false;
	}

	if ( !engine->IsPlayingDemo() )
	{
		// Don't interpolate, either, if we are timing out
		INetChannelInfo *nci = engine->GetNetChannelInfo();
		if ( nci && nci->GetTimeSinceLastReceived() > 0.5f )
		{
			EntityInterp_EnabledRef() = false;
		}
	}

	// THE CLOSING EDGE, and only the edge. See the block above for why this is
	// not a per-call refusal.
	const bool bOpen = EntityInterp_EnabledRef();

	if ( EntityInterpGateWasOpen() && !bOpen )
	{
		// §7's refusal, with its reason at the site that refuses rather than in
		// a table indexed from another file. Phase 1c deleted the table; the
		// strings below are its row, verbatim.
		//
		// NOT RATE LIMITED, and deliberately: the claim a retired row makes is
		// that nothing can reach it in this build, so the expected number of
		// these lines is zero and a flood is that claim being wrong in the
		// loudest available way. This one is on the CLOSING EDGE of the gate,
		// not on a per-call path.
		Warning( "[SourceSharp] REFUSED: EntityInterp_InterpolateServerEntities "
		         "was reached. plan_entity.md §7 -- this capability retired with "
		         "the C_BaseEntity hierarchy. Reason: SPLIT, and the split is the "
		         "finding. Its two passes walked g_TeleportList and "
		         "g_InterpolationList, and its third arm iterated "
		         "C_BaseEntityIterator to push a changed interp amount into "
		         "per-entity CInterpolatedVar histories -- three registries the "
		         "flip empties. Its CInterpolationContext is read only by stock "
		         "CInterpolatedVars, of which the managed hierarchy owns none. "
		         "Now: SourceSharp.Client/Interpolation.cs -- SamplePass at "
		         "NetUpdatePostDataUpdateEnd and RenderPass at RenderStart. The "
		         "LIVE half of this function did not retire: it is "
		         "EntityInterp_UpdateFrameGate in this file.\n" );
	}

	EntityInterpGateWasOpen() = bOpen;
}

//=============================================================================//
//
// THE TOOL-RECORDING PASS — kept so the SFM path ANSWERS BY NAME.
//
// The walk is gone with its list, and the refusal below says which list and
// why. What is left is the ONE condition under which the missing capability
// could ever have mattered — a tool actually in recording mode — and under
// that condition this refuses by name instead of returning quietly from an
// empty loop.
//
// THE GUARD IS STOCK'S OWN, unchanged and in stock's order: ToolsEnabled() is
// false in every build that has not been started with -tools, so the ordinary
// game pays one bool read per frame for this and nothing else.
//
//=============================================================================//

void EntityTool_RecordEntities()
{

	if ( !ToolsEnabled() || !clienttools->IsInRecordingMode() )
		return;

	// §7's refusal, with its reason at the site that refuses. Phase 1c deleted
	// the ruling table; this is its row, verbatim. The guard above is stock's
	// own, so an ordinary game never reaches this line at all.
	Warning( "[SourceSharp] REFUSED: EntityTool_RecordEntities was reached. "
	         "plan_entity.md §7 -- this capability retired with the C_BaseEntity "
	         "hierarchy. Reason: it is the only body of the six that names no "
	         "entity type and carries no guard -- it walks IClientRenderable. It "
	         "retires anyway, because its LIST does not survive: recordinglist is "
	         "written only by C_BaseEntity::SetToolRecording, and "
	         "C_StandaloneEntity::RecordToolMessage is an empty override, so a "
	         "managed entity would record nothing even if something put it on the "
	         "list. Now: NOTHING DOES. This is a real capability gap and it is "
	         "REFUSED BY NAME. Entity recording for SFM is not implemented for "
	         "the managed hierarchy.\n" );
}
