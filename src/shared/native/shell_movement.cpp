//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — how movement feels ================================//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// Phase 6 of the migration plan is the one about movement, and it opens by
// saying not to port it wholesale. The reason is prediction: the movement loop
// runs on the client for every unacknowledged command and again on the server,
// so it executes many times per frame per player, and every managed call on
// that path is paid every one of those times.
//
// So this WAS step 2 of that phase and not step 3: the parameters came over and
// the loop stayed. C# said how high a jump reaches and how much friction the
// ground has, and the native loop read it out of a struct.
//
// docs/plan-audit.md E1 then asked who OWNS this file and answered "managed
// code does", so the second half of the file is the arithmetic itself. What
// changed the cost argument above is not a new measurement -- 7ns a crossing is
// still 7ns -- but a second reason that outweighs it: stock compiles this
// arithmetic into client.so AND server.so, the two must agree bit-for-bit or
// the player rubber-bands, and one managed copy called by both is that whole
// class of bug removed rather than tested for.
//
// What is NOT here is as deliberate. Duck timing stays native: GAMEMOVEMENT_DUCK_TIME
// is used ten times as a compile-time constant, and m_flDucktime is PREDICTED
// state initialised from it, so a value that changed mid-map would disagree
// with the copy the client was already counting down.
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
#endif
#ifdef CLIENT_DLL
#include "gamerules.h"
#else
#include "igamerules.h"
#endif
#ifdef CLIENT_DLL
#include "baseplayer_shared.h"
#else
// CPlayerState and the local data MvLocal() answers, from their own headers:
// baseplayer_shared.h would bring the whole of player.h to the server, which
// builds no CBasePlayer (P5).
#include "PlayerState.h"
#include "ss_playerlocaldata.h"
#endif
#include "imovementplayer.h"
// The IGameMovement shim and its move helper are the client's: the server's
// movement and its helper's work are managed (plan_server_native_v2 T4g).
#ifdef CLIENT_DLL
#include "igamemovement.h"
#endif
#ifdef CLIENT_DLL
#include "imovehelper.h"
#define SS_MOVE_HELPER() MoveHelper()
#define SS_PLAYER_ANIM( n ) ( (PLAYER_ANIM)( n ) )
#endif
#ifdef CLIENT_DLL
#include "movevars_shared.h"
#endif
#ifndef CLIENT_DLL
#include "managed_convar_ref.h"
#endif
#include "movement_ground.h"
#include "movement_ground_writeback.h"
#include "engine/IEngineTrace.h"
#include "coordsize.h"
#ifdef CLIENT_DLL
#include "rumble_shared.h"
#include "shareddefs.h"
#endif
#include "sourcesharp.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E17: the server's IManagedPlayerCommands
#ifdef CLIENT_DLL
#include "imanaged_areas_client.h"   // plan_client_vtables CE8: IManagedPredictionClient
#endif
#include "shell_gamemovement.h"

#ifndef CLIENT_DLL
// THE SERVER HALF ON THE R5 SET (plan_server_novalve.md P5): what cbase.h,
// movevars_shared.h, rumble_shared.h and shareddefs.h gave it, from server.so's
// own headers, the stock macro names aliased to their ss:: spellings so the
// shared body reads the same on both sides.
#include "vphysics_interface.h"   // IPhysicsSurfaceProps, surfacedata_t
#include "ss_util.h"               // engine, enginetrace
#include "ss_shareddefs.h"
#include "world_subject.h"        // SourceSharp_IsTheWorld, each side's own
extern CGlobalVars *gpGlobals;
#define RUMBLE_FLAGS_NONE        ss::kRumbleFlagsNone
#define VEC_VIEW_SCALED( player )          ( g_pGameRules->GetViewVectors()->m_vView * player->GetModelScale() )
#define VEC_HULL_MIN_SCALED( player )      ( g_pGameRules->GetViewVectors()->m_vHullMin * player->GetModelScale() )
#define VEC_HULL_MAX_SCALED( player )      ( g_pGameRules->GetViewVectors()->m_vHullMax * player->GetModelScale() )
#define VEC_DUCK_HULL_MIN_SCALED( player ) ( g_pGameRules->GetViewVectors()->m_vDuckHullMin * player->GetModelScale() )
#define VEC_DUCK_HULL_MAX_SCALED( player ) ( g_pGameRules->GetViewVectors()->m_vDuckHullMax * player->GetModelScale() )
#define VEC_DUCK_VIEW_SCALED( player )     ( g_pGameRules->GetViewVectors()->m_vDuckView * player->GetModelScale() )
// A handle's entity: CBaseHandle::Get()'s SOURCESHARP body (ehandle.h), called
// directly rather than given a second inline definition here.
IHandleEntity *SourceSharp_EntityFromHandle( const CBaseHandle &handle );
#define SS_HANDLE_ENTITY( h ) SourceSharp_EntityFromHandle( h )
#include "trace_hit.h"   // ss::TraceHit: the IHandleEntity the engine stored in trace_t::m_pEnt
#endif
#ifdef CLIENT_DLL
#define SS_HANDLE_ENTITY( h ) ( h ).Get()
#endif

#ifdef CLIENT_DLL
	#define SOURCESHARP_MOVE_SIDE_TAG "client"
#else
	#define SOURCESHARP_MOVE_SIDE_TAG "server"
#endif

unsigned int SourceSharp_MovementRefusalCount( int nReason );

// Defined in shell_gamerules.cpp, beside the CViewVectors table it writes.
// plan_shim.md §5 3e: the hulls are declared in Shared/Movement.cs and arrive
// in the movement parameter block, but they are READ through
// g_pGameRules->GetViewVectors(), so the rules object is where they have to
// land. See shell_gamerules.h for why this is a prototype and not a shared
// declaration.
void SourceSharp_PublishViewVectors( const SourceSharpMovementParams *pParams );


//-----------------------------------------------------------------------------
// Stock's numbers, so a build where C# never registers anything moves exactly
// like Half-Life rather than not at all.
//
// That matters more than it sounds: this struct is read by the movement loop
// from the first tick, which is before any managed code has run on a listen
// server still loading. Zero jump height there is a player who cannot jump and
// a bug that looks like the input system.
//-----------------------------------------------------------------------------
static SourceSharpMovementParams s_Movement =
{
	21.0f,      // flJumpHeight — GAMEMOVEMENT_JUMP_HEIGHT
	600.0f,     // flGravity
	4.0f,       // flFriction
	10.0f,      // flAccelerate
	10.0f,      // flAirAccelerate
	75.0f,      // flStopSpeed
	3500.0f,    // flMaxVelocity
	18.0f,      // flStepSize
	30.0f,      // flAirSpeedCap — CGameMovement::GetAirSpeedCap
	0.33333333f,// flDuckSpeedScale — HandleDuckingSpeedCrop
	320.0f,     // flMaxSpeed
};

const SourceSharpMovementParams *SourceSharp_MovementParams()
{
	return &s_Movement;
}

// SetEngineConVar and SourceSharp_AuditMovementConVar stood here, with the
// eight-slot record of what the tuning wrote. Both are C# now
// (plan_server_novalve.md P2.3): SourceSharp.Server/MovementTuning.cs writes the
// mod's tuning into the eight ConVars, which it declares, and audits their
// changes from each ConVar's own OnChanged.

//-----------------------------------------------------------------------------
// Called once per side during managed startup.
//
// The convar-backed values are applied HERE and never again, which makes them
// defaults rather than overrides: a server operator who sets sv_friction in
// their config keeps it, because their config is read after this runs.
//-----------------------------------------------------------------------------
void SourceSharp_RegisterMovementParams( const SourceSharpMovementParams *pParams )
{
	if ( !pParams )
		return;

	s_Movement = *pParams;

	// THE HULLS, HANDED TO THE ONE OBJECT THAT ANSWERS FOR THEM (plan_shim.md
	// §5 3e). shareddefs.h's VEC_HULL_MIN and its eight neighbours all expand to
	// `g_pGameRules->GetViewVectors()->m_v...`, so the rules object is where a
	// hull has to arrive to be readable at all. BOTH SIDES: the client predicts
	// with the same hulls or it predicts a player standing somewhere else.
	//
	// Before this call the rules object answers its own static, which holds the
	// same nine numbers Shared/Movement.cs declares -- a pre-registration read
	// gets stock's hulls rather than zeroes, which would be a player with no
	// collision box at all.
	SourceSharp_PublishViewVectors( &s_Movement );

#ifndef CLIENT_DLL
	// Server only: the eight ConVars the tuning is written into, and the line
	// that names the numbers, are MovementTuning.Apply's now (C#,
	// plan_server_novalve.md P2.3), which runs right after this registration.
#else
	// The client's own confirmation, one line, because this side's registration
	// was the masked hazard: the slot was null here until Phase 9 and the
	// managed side logged success anyway. The line names the numbers the
	// PREDICTED loop now reads, so a client whose replay uses different
	// parameters than the server's tick is visible in two logs that disagree.
	Msg( "[SourceSharp] movement(client): predicted params registered — jump %.0fu, "
	     "air cap %.0f, duck %.2fx\n",
		s_Movement.flJumpHeight, s_Movement.flAirSpeedCap, s_Movement.flDuckSpeedScale );
#endif
}

//-----------------------------------------------------------------------------
// HOW MANY TIMES THE MANAGED MOVEMENT LOOP ACTUALLY RAN.
//
// This exists because of the check that cannot fail. The gate for E1 is "the
// prediction meter still reports zero mispredictions" -- and a build where the
// crossing never happened at all, because managed never bound the slot, reports
// zero mispredictions too. It would be a green gate over a port that is not
// running. So tools/movemath-gate asks for this number and requires it to have
// GROWN, which is the same shape prediction-jump-gate already uses for its own
// liveness check.
//
// THERE USED TO BE ELEVEN OF THESE and there is one, which is plan_shim.md §5
// 3e. The other ten counted the piecewise forwarders of docs/plan-audit.md E1 --
// SourceSharp_MoveClipVelocity and its nine neighbours, C# arithmetic slotted
// into stock's C++ PlayerMove chain one method at a time. Every call site was in
// game/shared/gamemovement.cpp, which left both vpcs at plan_entity.md step
// 8f-remove, so all ten counters could only ever read zero and the report
// printed ten zeroes under a total that was entirely the eleventh's.
//
// FUNCTION-LOCAL rather than file-scope, deliberately: this is diagnostics, not
// a reason to move the shim's file-scope statics count.
//
// Counted in the CROSSING and not in C#, which is the only place that can tell
// "the managed body ran" from "the managed body was asked for": MoveMath is
// static, side-free and holds no fields on purpose, and a counter inside it
// would be the one piece of state two sides could disagree about.
//
// unsigned, and allowed to wrap. A movement counter on a long-running server
// will pass four billion; what the gate reads is that it MOVED across a window,
// and "moved" survives a wrap in every case but the astronomically unlucky one.
//-----------------------------------------------------------------------------
enum SourceSharpMoveCounter_t
{
	// plan_entity.md step 3e. What keeps the movemath gate honest now the whole
	// loop is managed: ONE crossing per command, counted on both sides through
	// the same enum, so arm 1 and arm 2 are the same measurement.
	kMoveProcessMovement = 0,

	kMoveCounterCount
};

static unsigned int *MoveCounters()
{
	static unsigned int s_nCounts[ kMoveCounterCount ] = { 0 };
	return s_nCounts;
}

//=============================================================================
// docs/plan-audit.md E1 -- THE MOVEMENT ARITHMETIC, IN C#.
//
// Everything above this line is Phase 6 step 2 and 3: parameters and
// decisions. Everything below is the decision E1 actually asked for -- the
// movement CODE, for the part of gamemovement.cpp that can be moved safely.
//
// THE SELECTION RULE, and it is the whole design: a method comes over if its
// body reads and writes NOTHING but its arguments. No trace, no entity, no
// convar lookup, no gpGlobals. Ten of CGameMovement's methods pass that test
// and the rest do not; what is left native is listed in docs/divergences.md
// with the reason for each, and the reason is always the same shape -- moving
// it would mean callbacks BACK into the engine from inside the movement frame,
// which is a worse boundary than the one being removed.
//
// WHY IT IS WORTH DOING AT ALL. Stock compiles this arithmetic TWICE: once into
// client.so, once into server.so. The client replays it over every command the
// server has not acknowledged and the server runs it once for real, and a
// per-field comparison reports any disagreement as a misprediction. Two
// compiled copies that must agree bit-for-bit is the shape the jump
// misprediction bug had. There is now ONE copy, in SourceSharp.Abi -- which is
// the assembly GameLoadContext resolves to the DEFAULT load context, so it is
// one copy for both sides and across every hot reload, where a class in
// SourceSharp.Shared would have been loaded once per side.
//
// THE PIECEWISE HALF OF E1 IS GONE (plan_shim.md §5 3e). Ten forwarders stood
// here, each answering false when managed was not there so that the stock body
// in gamemovement.cpp could run instead. That file left both vpcs at
// plan_entity.md step 8f-remove and it held every one of their call sites, so
// the ten had no caller on any arm. What is left below is the WHOLE-LOOP
// crossing, which is the shape E1 was working towards: one call per command,
// the state in, the state out, no stock body behind it.
//=============================================================================

//-----------------------------------------------------------------------------
// plan_entity.md step 3e -- THE WHOLE LOOP, one crossing per command.
//
// The state struct is filled and read back in
// sourcesharp/game/shared/shell_gamemovement.cpp, where the CMoveData and the
// IMovementPlayer are; this is only the boundary.
//
// STEP 8f-igamemovement DELETED ONE OF THE TWO THINGS `false` USED TO MEAN.
//
// It used to read "answers false when managed has not started OR when the loop
// declined the move type, and the caller runs its stock body -- the same shape
// the ten leaves use." The ten leaves are arithmetic and a stock copy of each
// is right there; THIS one is the whole player movement loop, and reverting to
// stock for it is not a degraded answer, it is a DIFFERENT GAME running under a
// build that reports nothing wrong. That state is indistinguishable from a
// working one right up until the two sides disagree under prediction.
//
// So a managed side that has not started is now FATAL, here, at the boundary
// that is the only place able to tell it from a decline. `false` means exactly
// one thing afterwards: the managed loop declined this command's move type.
//
// THE ANSWER IS THE SAME ONE THIS CODEBASE ALREADY GIVES. A failed managed init
// and a failed hot reload are both Error() -- and the reload's own comment names
// this function's old shape as the reason it used to be safe to Warning:
// "every other caller of SourceSharp_GetManagedApi() in this tree tests it for
// NULL and degrades to stock". This caller no longer does.
//
// AND IT IS GUARDED A WHOLE BOOT EARLIER TOO, on the server. Since
// plan_engineapi_vtables E17 movement is the managed-built
// IManagedPlayerCommands, and the load refuses without it (ManagedAreas_Acquire),
// naming the area, before a map is ever loaded. The two are not redundant: the
// acquire sees an area that was not built, and this sees one that is not there
// at all -- a side that never started, or one a failed reload released.
//-----------------------------------------------------------------------------
// THE LAYOUT, CHECKED ON THIS SIDE.
//
// SourceSharpMoveState and SourceSharp.Abi's MoveStateBlob are both printed
// from one field list, and this is what catches an edit that skipped the
// generator: MoveStateBlob.WordCount is 147 and MovementTests asserts it
// against Marshal.SizeOf on the other side. 147 words is 588 bytes, and every
// field is a float or an int32_t, so there is no padding to argue about.
COMPILE_TIME_ASSERT( sizeof( SourceSharpMoveState ) == 147 * 4 );

// THE CLIENT'S ALONE since plan_server_native_v2 T4g: the server's commands
// run in managed (ServerPlayerCommands), which calls the loop directly.
#ifdef CLIENT_DLL
bool SourceSharp_MoveProcessMovement( SourceSharpMoveState *pState )
{
	// The side's managed-built area: plan_client_vtables CE8.
	IManagedPredictionClient *pCommands = ManagedPredictionClient();

	// THE DELETED FALLBACK. Both of these used to `return false` and the caller
	// ran stock movement. NEITHER RETURNS NOW.
	//
	// The null pState is in the same arm rather than a separate one because it is
	// the same class of fault from the reader's point of view -- there is nothing
	// to move this player with -- and splitting it would add a reason nobody can
	// reach: every call site passes the address of a stack object.
	if ( !pCommands || !pState )
		SourceSharp_MovementFatal( kMovementFatal_NoManagedSide, 0 );

	// THE ONE REMAINING MEANING OF false: the managed loop declined this move
	// type. Not fatal, and the reason is that the ABI cannot separate it from a
	// caught exception -- Entry.cs's thunk answers 0 for a declined move type, a
	// null state and an exception alike, and the exception is already reported by
	// Fail(). Killing a live server over one exceptional command would be a worse
	// answer than skipping it. The caller counts and reports it; step
	// 8f-movetypes removes the decline.
	if ( !pCommands->MoveProcessMovement( pState ) )
		return false;

	MoveCounters()[ kMoveProcessMovement ]++;
	return true;
}
#endif // CLIENT_DLL

//-----------------------------------------------------------------------------
// plan_entity.md step 6f -- THE SAME COUNT, FROM THE CLIENT'S BRACKET.
//
// The client's replay is managed since 6f and calls the movement loop as
// managed code, so nothing goes through the forwarder above on that side. This
// is the one line of it the client still needs: the count that lets
// cs_cl_move_managed_report tell "the managed body RAN" from "the managed body
// was asked for". It is called from SourceSharp_MoveEndClientCommand, once per
// command, only when managed reported that it ran.
//
// A SECOND COUNTER WOULD BE A SECOND ANSWER. Both sides read the same report
// through the same enum, and the whole value of the number to
// tools/movemath-gate is that arm 1 and arm 2 are the same measurement.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL   // server gc cull: server.so never reaches this
void SourceSharp_MoveNoteManagedCommand()
{
	MoveCounters()[ kMoveProcessMovement ]++;
}
#endif

#ifndef CLIENT_DLL
//-----------------------------------------------------------------------------
// The same count from the SERVER'S managed command loop (plan_server_native_v2
// T4g), which calls the movement loop as managed code like the client's
// replay: PlayerCommandFinish bumps it once per command the loop ran.
//-----------------------------------------------------------------------------
void SourceSharp_MoveNoteServerCommand()
{
	MoveCounters()[ kMoveProcessMovement ]++;
}
#endif

//-----------------------------------------------------------------------------
// THE REPORT, for cs_cl_move_managed_report below and for the server's
// cs_sv_move_managed_report, which is a diagnostic and lives in
// shell_movement_debug.cpp (compiled only into a debug build of server.so).
// One body, so the two sides print the same lines from the same counters.
//-----------------------------------------------------------------------------
void SourceSharp_MoveManagedReport()
{
	// Local rather than a file-scope table, for the statics-audit reason given
	// above MoveCounters(). A console command is not a hot path.
	const char *const pszNames[ kMoveCounterCount ] =
	{
		"ProcessMovement",
	};

	const unsigned int *pCounts = MoveCounters();
	unsigned int nTotal = 0;

	for ( int i = 0; i < kMoveCounterCount; i++ )
		nTotal += pCounts[ i ];

	Msg( "[SourceSharp] movemath(%s): %u managed call(s) total\n",
#ifdef CLIENT_DLL
		"client",
#else
		"server",
#endif
		nTotal );

	for ( int i = 0; i < kMoveCounterCount; i++ )
		Msg( "  %-16s %u\n", pszNames[ i ], pCounts[ i ] );

	// COMMANDS THAT COULD NOT BE RUN, step 8f-igamemovement.
	//
	// Two of the three reasons do not return, so a nonzero count for either is
	// only ever readable in a log this process already died in; the third --
	// the managed loop declining a move type -- warns once and then counts, and
	// this line is the only place the total is visible. A refusal nobody can
	// count is the shape of silence this step exists to remove, so it is
	// printed unconditionally rather than only when it is nonzero.
	static const char *const pszRefusals[ kMovementFatal_Count ] =
	{
		"no-managed-side", "no-stock-body", "declined-movetype",
	};

	for ( int i = 0; i < kMovementFatal_Count; i++ )
		Msg( "  refused:%-8s %u\n", pszRefusals[ i ],
			SourceSharp_MovementRefusalCount( i ) );
}

// cs_cl_move_managed_report, the client's command over it, is client-only and
// lives in src/client/native/shell_prediction.cpp since plan_server_native_v2 T2.

//========= SourceSharp — IGameMovement, published by the shim ==============//
//
// plan_entity.md step 8f-igamemovement, and it is an OWNER RULING: START THE
// MANAGED SIDE, AND REMOVE THE FILE.
//
// WHAT THIS FILE IS. `gamemovement.cpp` is 6,040 lines of stock movement that
// this port replaced with C# at step 3e, and it stayed in both vpcs afterwards
// because four things still needed it. `phase4-exit` measured all four; two of
// them are this file's job:
//
//   * THE ENGINE ASKS FOR `IGameMovement` BY NAME. `shell_shared.cpp` published
//     it with EXPOSE_SINGLE_INTERFACE_GLOBALVAR over a `CGameMovement`
//     singleton, so the interface could not outlive the class. It does now:
//     CShellGameMovement below is a STANDALONE implementation of IGameMovement's
//     seven pure virtuals that answers from the managed loop, and it is what the
//     macro names.
//
//   * SIX SYMBOLS WERE OWED TO `gamemovement.cpp` BY THE SHIM — the two
//     CGameMovement constructors `shell_shared.cpp` needed, and the four
//     `SourceSharp_Move*` ABI slot bodies `sourcesharp_gamesystem.cpp` needed.
//     All six are here now. `tools/4a-owes gamemovement` reads 0 symbols owed
//     after this step, which is what makes 8f-remove two vpc rows and nothing
//     else.
//
// AND WHAT IS DELETED, WHICH IS THE POINT OF THE STEP.
//
// `SourceSharp_MoveProcessMovement` used to answer `false` when the managed side
// had not started, and `CGameMovement::ProcessMovement` then ran its stock body.
// THAT FALLBACK IS GONE. A managed side that has not started is now a LOUD
// FAILURE — SourceSharp_MovementFatal below, which formats a message naming the
// cause and the remedy and hands it to Error(), the same NORETURN treatment
// `sourcesharp_gamesystem.cpp` already gives a failed init and a failed reload.
//
// The reason is this project's own history. It has shipped EIGHT silent losses,
// and the two most recent are exactly this shape: the physics event ring was
// drained into nothing for six steps while its high-water mark climbed to 263,
// and `UpdateAllClientData` walked a helper that cannot answer so its body never
// ran once. Nothing anywhere reported an error in either case. A build silently
// running stock movement instead of the managed loop is indistinguishable from a
// working one right up until the two sides disagree under prediction, and that
// is the worst possible way for it to become visible.
//
// THE TWO SIDES START DIFFERENTLY, AND THE ANSWER IS DIFFERENT.
//
//   SERVER  CShellGameMovement::ProcessMovement is called once per command per
//           player from `shell_standalone_player.cpp` through the global
//           `g_pGameMovement`. That is the window a not-yet-started managed side
//           could be called in, and it is where the Error() fires. It is ALSO
//           guarded a whole boot earlier: the load refuses without the
//           managed-built IManagedPlayerCommands (E17), so a managed side
//           that builds no movement never gets as far as a map.
//
//   CLIENT  there is no such window. Since step 6f the client's replay is
//           MANAGED — the ring, the basis, the compare and the loop are all C#
//           — and native's whole part is the bracket at the bottom of this file,
//           which managed code CALLS. A client whose managed side has not
//           started makes no movement call at all, so there is nothing here to
//           revert to stock. The Error() path is compiled on this side too and
//           says "client" if it ever fires, because a side that cannot reach a
//           state and a side that has no code for it are not the same thing.
//
//=============================================================================//

// MOVED HERE FROM gamemovement.cpp, both of them, and the direction is the
// point: that file now declares them `extern` and this one defines them, so the
// file can leave without taking a definition with it. Two lines of stock text
// and one convar, which is the whole of what had to change over there.
//
// g_bMovementOptimizations is [MD]'s A/B for the point-contents cache and is
// stock's, unchanged. sourcesharp_movement_managed is step 3e's own switch and
// its comment is step 3e's, kept verbatim below.
//-----------------------------------------------------------------------------

// [MD] I'll remove this eventually. For now, I want the ability to A/B the optimizations.
// The client's fill reads it; the server's constant is ServerMoveConstants'
// (plan_server_native_v2 T4g).
#ifdef CLIENT_DLL
bool g_bMovementOptimizations = true;
#endif

//-----------------------------------------------------------------------------
// The A/B switch, and it is the gate for this whole item rather than a comfort.
//
// REPLICATED, so the two sides cannot be running different halves of the
// movement code -- which is the one way this switch could itself cause the
// misprediction it exists to measure. With it the before/after is taken in ONE
// process against ONE map: `sourcesharp_movement_managed 0`, jump, read the
// meter, `1`, jump, read it again. Comparing two builds cannot separate this
// change from everything else that landed between them, and the prediction
// meter is delta-compressed enough (docs/divergences.md) that a low count is
// not proof on its own.
//
// NOT a cheat, because turning the port off has to be possible on a live server
// whose players are mispredicting; NOTIFY, because it changes how the game
// moves and everyone connected deserves to be told.
//
// STEP 8f-remove: THE 0 ARM NO LONGER SELECTS ANYTHING, AND SAYS SO OUT LOUD.
// `gamemovement.cpp` left both vpcs, so there is no second implementation of the
// movement loop in this build and this convar at 0 is kMovementFatal_NoStock --
// a named, fatal, readable refusal rather than a player who silently stops
// moving. tools/movemath-gate's arm 1 WAS this convar and was retired in the
// same commit; the gate now measures the one loop directly.
//
// THE CONVAR IS KEPT ANYWAY, and not out of sentiment. It is the seam a reader
// reaches for when movement looks wrong, and what it now does is print the
// reason the seam is closed -- which is strictly more useful than a name that
// no longer resolves to anything. Deleting it would leave a server operator
// typing it, getting "unknown command", and learning nothing.
//-----------------------------------------------------------------------------
//
// THE SERVER'S HALF IS DECLARED IN C# (SourceSharp.Server.MovementManagedConVar),
// plan_server_novalve.md P2.3b, and read here by name. Unregistered reads as 1:
// an unresolved ref's 0 would be the fatal arm.
#ifdef CLIENT_DLL
ConVar sourcesharp_movement_managed( "sourcesharp_movement_managed", "1",
	FCVAR_REPLICATED | FCVAR_NOTIFY,
	"Run the ten ported CGameMovement methods in C# (1) or in the stock C++ they forward from (0)." );

static bool MovementManaged() { return sourcesharp_movement_managed.GetBool(); }
#else
static CManagedConVarRef sourcesharp_movement_managed( "sourcesharp_movement_managed" );

static bool MovementManaged()
{
	return !sourcesharp_movement_managed.IsValid() || sourcesharp_movement_managed.GetBool();
}

// The managed command loop's question before each command (plan_server_native_v2
// T4g): the 0 arm selects a movement this build does not have, so it is fatal,
// as ProcessMovement made it.
void SourceSharp_MovementRequireManaged()
{
	if ( !MovementManaged() )
		SourceSharp_MovementFatal( kMovementFatal_NoStock, 0 );
}
#endif

//-----------------------------------------------------------------------------
// THE SHIM VTABLE §3.10 ASKS FOR.
//
// STANDALONE, and that is deliberate rather than convenient: plan_entity.md's
// §3.1 wants shells that are not subclasses of the thing being counted, and a
// CShellGameMovement deriving from CGameMovement would be a CGameMovement to
// `nm`, to the census and to the linker alike -- which is precisely the state
// this step exists to leave.
//
// It holds the four things the servicing methods need and nothing else: the
// player and the move data of the command being run, the two per-command
// latches stock kept as members, the last standable ground a serviced trace
// found, and the point-contents cache.
//-----------------------------------------------------------------------------
// THE CLIENT'S ALONE since plan_server_native_v2 T4g (Decision 4's second
// half: the server's export went in T2, its object here, now that the server's
// movement state and command path are managed).
#ifdef CLIENT_DLL
class CShellGameMovement : public IGameMovement
{
public:
	CShellGameMovement( void );
	virtual ~CShellGameMovement( void );

	// ---- IGameMovement, all seven --------------------------------------
	virtual void	ProcessMovement( IMovementPlayer *pPlayer, CMoveData *pMove ) OVERRIDE;
	virtual void	StartTrackPredictionErrors( IMovementPlayer *pPlayer ) OVERRIDE;
	virtual void	FinishTrackPredictionErrors( IMovementPlayer *pPlayer ) OVERRIDE;
#ifdef CLIENT_DLL
	// Stock's igamemovement.h, which the client's GameMovement001 export
	// implements, declares these four; server.so's ss::IGameMovement dropped
	// them in plan_server_native_v2 T2 (no caller in either DLL).
	virtual void	DiffPrint( PRINTF_FORMAT_STRING char const *fmt, ... ) OVERRIDE;
	virtual Vector	GetPlayerMins( bool ducked ) const OVERRIDE;
	virtual Vector	GetPlayerMaxs( bool ducked ) const OVERRIDE;
	virtual Vector	GetPlayerViewOffset( bool ducked ) const OVERRIDE;
#endif

	// ---- the crossing and the four things it is serviced with -----------
	bool			RunManagedCommand( void );
#ifdef CLIENT_DLL
	void			FillConstants( struct SourceSharpMoveState *pState );
#endif
	void			WorldTrace( const float *pStart, const float *pEnd,
	                            const float *pMins, const float *pMaxs,
	                            int nMask, int nCollisionGroup,
	                            struct SourceSharpMoveTraceResult *pOut );
	int				WorldEffect( int nKind, const float *pXYZ, float flValue, int nValue );

	// Step 6f's client bracket needs the move data the servicing methods read --
	// above all m_nPlayerHandle, the trace filter's skip entity. ProcessMovement
	// assigns mv on the way in; a client replay never calls it, so it says so
	// here instead of reaching into a member from a free function.
	void			SetMoveData( CMoveData *pMove ) { mv = pMove; }
	IHandleEntity  *LastGround( void ) const { return m_pLastGround; }

	IMovementPlayer *player;

private:
	void			ResetDuckLatched( void );

	CMoveData		*mv;

	int				m_nOldWaterLevel;
	int				m_nOnLadder;

	// THE LAST STANDABLE GROUND A SERVICED TRACE FOUND, and the one thing the
	// managed loop cannot carry: it moves a flat state across, and an entity is
	// not flat. Every SetGroundEntity the loop makes is fed by a trace this
	// side ran, so the last such trace's entity is the one it meant.
	//
	// NULL until the first trace of the first crossing, which is why the
	// write-back only uses it when the loop says it FOUND ground.
	IHandleEntity  *m_pLastGround;

	// The whole of the last trace this side serviced for the managed loop, so
	// ADD_TO_TOUCHED can be replayed with it -- a trace_t cannot cross and the
	// touch list is replayed against the entities in one.
	trace_t         m_LastTrace;
};

//-----------------------------------------------------------------------------
// THE SINGLETON, AND THE NAME THE ENGINE ASKS FOR.
//
// Both used to be in shell_shared.cpp over a CGameMovement. What has NOT
// changed is that the global and the interface export are BOTH needed:
// `g_pGameMovement` is what this build's own callers use (the server's command
// runner, and until step 3e the client's replay), and the export is what a
// CreateInterface("GameMovement001") would resolve.
//
// MEASURED, and it corrects the comment that used to stand here. That comment
// said "the engine asks the game DLLs for this BY NAME ... without it the
// interface is silently absent". The engine does NOT ask: the string
// "GameMovement001" does not appear in ANY binary of Source SDK Base 2013
// Multiplayer, 32- or 64-bit, and in the public SDK IGameMovement is used only
// by game code. The export is kept anyway -- it costs
// one InterfaceReg, it is what HL2MP and TF export, and step 8f-remove's gate
// is written as "IGameMovement still answered by name" -- but it is parity, not
// a requirement, and the difference is worth writing down rather than inheriting.
//-----------------------------------------------------------------------------
static CShellGameMovement g_ShellGameMovement;
IGameMovement *g_pGameMovement = ( IGameMovement * )&g_ShellGameMovement;

EXPOSE_SINGLE_INTERFACE_GLOBALVAR( CShellGameMovement, IGameMovement,
	INTERFACENAME_GAMEMOVEMENT, g_ShellGameMovement );
#endif // CLIENT_DLL
// The server's export went in plan_server_native_v2 T2 (ruling 4, 2026-10-11):
// no engine binary asks for the name, and server.so's callers use the global.

//-----------------------------------------------------------------------------
// THE STOCK MOVER ON LOAN IS GONE, AND SO IS THE THING THAT LENT IT.
//
// Step 8f-igamemovement left `g_pStockMover`, a NULL-until-registered pointer
// that `gamemovement.cpp` filled at static init, so the A/B switch still had a
// second body to select while that file was still compiled. Step 8f-remove took
// the file out of both vpcs, so nothing registers, and a pointer that can only
// ever be NULL is worse than no pointer: every `if ( g_pStockMover )` beside it
// reads as a live fallback to anyone maintaining this file, which is exactly
// the shape of the eight silent losses this port has already found.
//
// So the pointer, the registration hook and the three forwards to it were
// deleted with the file. What is left in their place is the enumerated,
// printed refusal shell_gamemovement.h declares -- kMovementFatal_NoStock for
// the operator who asked for a body that is not there, and
// kMovementFatal_Declined for a move type the managed loop would not take
// (unreachable since 8f-movetypes, and counted rather than assumed).
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// A command that could not be run, counted and named.
//
// FUNCTION-LOCAL, for tools/statics-audit's reason and for shell_movement.cpp's
// MoveCounters()' -- the shim is held to a file-scope statics budget.
//-----------------------------------------------------------------------------
static unsigned int *MovementRefusals( void )
{
	static unsigned int s_nRefusals[ kMovementFatal_Count ] = { 0, 0, 0 };
	return s_nRefusals;
}

//-----------------------------------------------------------------------------
// FATAL, AND IT DOES NOT RETURN.
//
// The same treatment sourcesharp_gamesystem.cpp gives a failed init
// (SourceSharp_FatalInitFailure) and a failed reload: Error() is NORETURN
// (tier0/dbg.h) and goes through the engine's spew handler, so the text reaches
// the console, console.log under -condebug and a dialog where there is one, and
// then the process stops. Not a silent exit, and not a fault twenty frames later
// in code that does not name the cause.
//
// "%s", not the message as the format: the text carries a move type and a
// remedy, and one stray %s in it would read the stack.
//-----------------------------------------------------------------------------
void SourceSharp_MovementFatal( int nReason, int nDetail )
{
	if ( nReason >= 0 && nReason < kMovementFatal_Count )
		MovementRefusals()[ nReason ]++;

	char szMessage[1536];
	SourceSharpMovement_Format( szMessage, sizeof( szMessage ),
		SOURCESHARP_MOVE_SIDE_TAG, nReason, nDetail );

	Error( "%s", szMessage );
}

//-----------------------------------------------------------------------------
// LOUD, BUT NOT FATAL, and the difference is argued rather than assumed.
//
// The one reason that gets this instead of Error() is kMovementFatal_Declined,
// because the managed side answers 0 for THREE different things and the ABI
// cannot tell them apart (Entry.cs:2596 -- a declined move type, a NULL state,
// and a caught exception all return 0, and the exception is already reported by
// Fail()). Killing a live server over a managed exception in one command would
// be a worse answer than skipping the command, and step 8f-movetypes removes the
// decline entirely.
//
// PRINTED ONCE PER REASON AND COUNTED FOREVER. A per-command Warning() at 66
// commands a second per player is a log nobody reads, which is its own kind of
// silence; the count is what cs_{sv,cl}_move_managed_report prints.
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL   // the server's commands are managed since plan_server_native_v2 T4g
static void SourceSharp_MovementRefused( int nReason, int nDetail )
{
	if ( nReason < 0 || nReason >= kMovementFatal_Count )
		return;

	unsigned int *pCounts = MovementRefusals();
	const bool bFirst = ( pCounts[ nReason ] == 0 );
	pCounts[ nReason ]++;

	if ( !bFirst )
		return;

	char szMessage[1536];
	SourceSharpMovement_Format( szMessage, sizeof( szMessage ),
		SOURCESHARP_MOVE_SIDE_TAG, nReason, nDetail );

	Warning( "%s", szMessage );
}
#endif // CLIENT_DLL


unsigned int SourceSharp_MovementRefusalCount( int nReason )
{
	if ( nReason < 0 || nReason >= kMovementFatal_Count )
		return 0;

	return MovementRefusals()[ nReason ];
}

//=============================================================================
// plan_entity.md step 3e -- THE SHIM THAT COPIES CMoveData ACROSS.
//
// §3.10: "IGameMovement remains a shim vtable the engine resolves by name;
// ProcessMovement copies CMoveData in and out once per command." This is that
// copy, and it is the whole of what stays native for the movement loop.
//
// MOVED HERE VERBATIM AT STEP 8f-igamemovement. It was in gamemovement.cpp,
// under a banner arguing it had to be, because every field below is read off
// the mover's own members and the four servicing methods need the player handle
// and the point-contents cache that live on the mover object. That argument was
// right about the ACCESS and wrong about the CLASS: the members are four, they
// are listed in CShellGameMovement above, and none of them is stock's. What
// moved is the code; what changed is which object holds the four fields.
//=============================================================================

#ifdef CLIENT_DLL   // the server's commands are managed since plan_server_native_v2 T4g
// The mover whose command is being run, for the ABI slots. Set for the duration
// of ONE crossing and cleared after it, so a slot called from anywhere else
// answers nothing rather than answering about whoever moved last.
//
// FUNCTION-LOCAL, for tools/statics-audit's reason -- src/game is not held to
// the shim's 250 file-scope statics, but this is the same shape as
// shell_movement.cpp's MoveCounters() and there is no object for it to be a
// field of: the ABI slot functions are free functions by necessity.
static CShellGameMovement **ActiveMover()
{
	static CShellGameMovement *s_pActive = NULL;
	return &s_pActive;
}
#endif // CLIENT_DLL


static void SS3E_FillVec( float *pOut, const Vector &v )
{
	pOut[0] = v.x; pOut[1] = v.y; pOut[2] = v.z;
}

#ifdef CLIENT_DLL   // the server's commands are managed since plan_server_native_v2 T4g
// QAngle is not a Vector and the compiler is right to say so. Three floats
// either way, and the crossing carries three floats -- but the overload keeps
// the DISTINCTION at every call site, which is the whole reason QAngle exists.
static void SS3E_FillVec( float *pOut, const QAngle &a )
{
	pOut[0] = a.x; pOut[1] = a.y; pOut[2] = a.z;
}

static QAngle SS3E_ReadAng( const float *p )
{
	return QAngle( p[0], p[1], p[2] );
}
#endif // CLIENT_DLL


static Vector SS3E_ReadVec( const float *p )
{
	return Vector( p[0], p[1], p[2] );
}

// The movement ConVars the client's state fill reads: the client declares them
// in movevars_shared.cpp and reads its own objects. The SERVER's constants are
// managed's own (plan_server_native_v2 T4g, ServerMoveConstants), so this fill
// and the convar refs it read on that side are the client's alone.
#ifdef CLIENT_DLL
#define SS_MOVEVAR( name ) name
#endif

#ifdef CLIENT_DLL
//-----------------------------------------------------------------------------
// THE CONSTANT WORDS OF THE CROSSING, for the CLIENT'S MANAGED REPLAY (step 6f),
// which owns the player's state itself and asks native only for the numbers
// that are not the player's. The server's are managed's own since
// plan_server_native_v2 T4g (ServerMoveConstants).
//
// Read fresh rather than cached: every one is a convar an operator can move
// mid-round, and a cached copy is a staleness class the movement code cannot
// detect. 46 words a command.
//-----------------------------------------------------------------------------
void CShellGameMovement::FillConstants( SourceSharpMoveState *pState )
{
	const SourceSharpMovementParams *pParams = SourceSharp_MovementParams();

	// NOT A CONVAR AND HERE ANYWAY: sv_gravity times whatever gamerules scales
	// it by is a fact about the world and not about the player, and the client's
	// replay needs it as much as the server's tick does. It used to be filled
	// with the player's state a few lines above, which is where it read as the
	// player's.
	pState->flCurrentGravity      = GetCurrentGravity();

	pState->flCvFriction          = SS_MOVEVAR( sv_friction ).GetFloat();
	pState->flCvAccelerate        = SS_MOVEVAR( sv_accelerate ).GetFloat();
	pState->flCvAirAccelerate     = SS_MOVEVAR( sv_airaccelerate ).GetFloat();
	pState->flCvStopSpeed         = SS_MOVEVAR( sv_stopspeed ).GetFloat();
	pState->flCvMaxVelocity       = SS_MOVEVAR( sv_maxvelocity ).GetFloat();
	pState->flCvMaxSpeed          = SS_MOVEVAR( sv_maxspeed ).GetFloat();
	pState->flCvBounce            = SS_MOVEVAR( sv_bounce ).GetFloat();
	pState->flCvRollAngle         = SS_MOVEVAR( sv_rollangle ).GetFloat();
	pState->flCvRollSpeed         = SS_MOVEVAR( sv_rollspeed ).GetFloat();
	pState->flCvNoClipSpeed       = SS_MOVEVAR( sv_noclipspeed ).GetFloat();
	pState->flCvNoClipAccelerate  = SS_MOVEVAR( sv_noclipaccelerate ).GetFloat();
	pState->flCvSpecSpeed         = SS_MOVEVAR( sv_specspeed ).GetFloat();
	pState->flCvSpecAccelerate    = SS_MOVEVAR( sv_specaccelerate ).GetFloat();
	pState->flCvAirSpeedCap       = pParams->flAirSpeedCap;
	pState->flCvDuckSpeedScale    = pParams->flDuckSpeedScale;
	pState->bCvOptimizations      = g_bMovementOptimizations ? 1 : 0;
	pState->bCvSpecNoClip         = SS_MOVEVAR( sv_specnoclip ).GetBool() ? 1 : 0;
	pState->nCvMaxClients         = gpGlobals->maxClients;
	pState->flCvTickInterval      = TICK_INTERVAL;
	pState->flCvJumpHeight        = pParams->flJumpHeight;
	pState->flEngineTime          = engine->Time();

	const RulesViewVectors_t *vv = g_pGameRules->GetViewVectors();
	SS3E_FillVec( pState->vecCvView,           vv->m_vView );
	SS3E_FillVec( pState->vecCvHullMin,        vv->m_vHullMin );
	SS3E_FillVec( pState->vecCvHullMax,        vv->m_vHullMax );
	SS3E_FillVec( pState->vecCvDuckHullMin,    vv->m_vDuckHullMin );
	SS3E_FillVec( pState->vecCvDuckHullMax,    vv->m_vDuckHullMax );
	SS3E_FillVec( pState->vecCvDuckView,       vv->m_vDuckView );
	SS3E_FillVec( pState->vecCvObsHullMin,     vv->m_vObsHullMin );
	SS3E_FillVec( pState->vecCvObsHullMax,     vv->m_vObsHullMax );
	SS3E_FillVec( pState->vecCvDeadViewHeight, vv->m_vDeadViewHeight );
}
#endif // CLIENT_DLL

#ifdef CLIENT_DLL   // the server's commands are managed since plan_server_native_v2 T4g
bool CShellGameMovement::RunManagedCommand( void )
{
	SourceSharpMoveState state;
	memset( &state, 0, sizeof( state ) );

	// ---- the command and the player -------------------------------------
	state.nCommandNumber        = player->MvCurrentCommandNumber();
	state.flFrameTime           = gpGlobals->frametime;
	state.nMoveType             = (int)player->MvGetMoveType();
	state.nFlags                = player->MvGetFlags();
	state.nWaterLevel           = player->MvGetWaterLevel();
	state.nWaterType            = player->MvGetWaterType();
	state.bDeadFlag             = player->MvPlayerState().deadflag ? 1 : 0;
	SS3E_FillVec( state.vecOrigin, mv->GetAbsOrigin() );
	SS3E_FillVec( state.vecVelocity, mv->m_vecVelocity );
	SS3E_FillVec( state.vecBaseVelocity, player->MvGetBaseVelocity() );
	SS3E_FillVec( state.angViewAngles, mv->m_vecViewAngles );
	state.flForwardMove         = mv->m_flForwardMove;
	state.flSideMove            = mv->m_flSideMove;
	state.flUpMove              = mv->m_flUpMove;
	state.nButtons              = mv->m_nButtons;
	state.nOldButtons           = mv->m_nOldButtons;
	state.flMaxSpeed            = mv->m_flMaxSpeed;
	state.flClientMaxSpeed      = mv->m_flClientMaxSpeed;
	state.bDucked               = player->MvLocal().m_bDucked ? 1 : 0;
	state.bDucking              = player->MvLocal().m_bDucking ? 1 : 0;
	state.bInDuckJump           = player->MvLocal().m_bInDuckJump ? 1 : 0;
	state.flDuckTime            = player->MvLocal().m_flDucktime;
	state.flDuckJumpTime        = player->MvLocal().m_flDuckJumpTime;
	state.flJumpTime            = player->MvLocal().m_flJumpTime;
	state.flFallVelocity        = player->MvLocal().m_flFallVelocity;
	state.flStepSize            = player->MvLocal().m_flStepSize;
	SS3E_FillVec( state.vecViewOffset, player->MvGetViewOffset() );
	SS3E_FillVec( state.angPunchAngle, (const QAngle &)player->MvLocal().m_vecPunchAngle );
	SS3E_FillVec( state.angPunchAngleVel, (const QAngle &)player->MvLocal().m_vecPunchAngleVel );
	state.flEntityGravity       = player->MvGetGravity();
	state.flSurfaceFriction     = player->MvSurfaceFriction();

	IHandleEntity *pGround = player->MvGetGroundEntity();
	state.bHasGround            = pGround ? 1 : 0;
	SS3E_FillVec( state.vecGroundVelocity, MovementGroundVelocity( pGround ) );

	state.flWaterJumpTime       = player->MvWaterJumpTime();
	SS3E_FillVec( state.vecWaterJumpVel, player->MvWaterJumpVel() );
	state.flSwimSoundTime       = player->MvSwimSoundTime();
	state.flStepSoundTime       = player->MvStepSoundTime();
	state.nHealth               = player->MvHealth();
	state.bGameCodeMovedPlayer  = mv->m_bGameCodeMovedPlayer ? 1 : 0;
	state.flConstraintRadius    = mv->m_flConstraintRadius;
	state.flConstraintWidth     = mv->m_flConstraintWidth;
	state.flConstraintSpeedFactor = mv->m_flConstraintSpeedFactor;
	SS3E_FillVec( state.vecConstraintCenter, mv->m_vecConstraintCenter );
	state.flOldForwardMove      = mv->m_flOldForwardMove;
	state.nImpulseCommand       = mv->m_nImpulseCommand;
	state.flLaggedMovementValue = player->MvGetLaggedMovementValue();
	state.nSurfaceProps         = player->MvSurfaceProps();
	SS3E_FillVec( state.vecLadderNormal, player->MvLadderNormal() );
	state.flOutStepHeight       = mv->m_outStepHeight;
	SS3E_FillVec( state.vecOutWishVel, mv->m_outWishVel );
	SS3E_FillVec( state.vecOutJumpVel, mv->m_outJumpVel );
	state.nMoveCollide          = (int)player->MvGetMoveCollide();
	state.nOnLadder             = m_nOnLadder;
	state.nOldWaterLevel        = m_nOldWaterLevel;
	state.bIsAlive              = player->MvIsAlive() ? 1 : 0;
	state.nObserverMode         = player->MvGetObserverMode();
	state.bInAVehicle           = player->MvIsInAVehicle() ? 1 : 0;
	state.flModelScale          = player->GetModelScale();
	state.nEntIndex             = player->MvEntIndex();
	state.nStuckLast            = player->MvStuckLast();
	SS3E_FillVec( state.angAngles, mv->m_vecAngles );
	SS3E_FillVec( state.angOldAngles, mv->m_vecOldAngles );
	state.bFirstRunOfFunctions  = mv->m_bFirstRunOfFunctions ? 1 : 0;
	state.flCurTime             = gpGlobals->curtime;
	state.bSurfaceDataValid     = player->MvSurfaceData() ? 1 : 0;
	state.bAllowAutoMovement    = player->MvLocal().m_bAllowAutoMovement ? 1 : 0;
	state.bSlowMovement         = player->MvLocal().m_bSlowMovement ? 1 : 0;
	state.bIsServer             = player->MvIsServer() ? 1 : 0;

	// The constant words: the client's fill; the server's are managed's own
	// (plan_server_native_v2 T4g), so on that side they stay zero.
	FillConstants( &state );

	// ---- the crossing -----------------------------------------------------
	CShellGameMovement **ppActive = ActiveMover();
	CShellGameMovement *pWas = *ppActive;
	*ppActive = this;
	const bool bRan = SourceSharp_MoveProcessMovement( &state );
	*ppActive = pWas;

	if ( !bRan )
		return false;

	// ---- and back ---------------------------------------------------------
	//
	// ONLY what the loop writes. The inputs are not copied back, which is what
	// stock does too: CMoveData's inputs are filled before the call and only
	// the outputs are read after it.
	player->MvSetMoveType( (MoveType_t)state.nMoveType );
	player->MvSetMoveCollide( (MoveCollide_t)state.nMoveCollide );

	// The flags go through Add/Remove rather than being assigned, because they
	// are a NETWORKED field on the player and the change hooks are what make
	// the client see them.
	const int nAdd = state.nFlags & ~player->MvGetFlags();
	const int nRemove = player->MvGetFlags() & ~state.nFlags;
	if ( nAdd )
		player->MvAddFlag( nAdd );
	if ( nRemove )
		player->MvRemoveFlag( nRemove );

	player->MvSetWaterLevel( state.nWaterLevel );
	player->MvSetWaterType( state.nWaterType );
	mv->SetAbsOrigin( SS3E_ReadVec( state.vecOrigin ) );
	mv->m_vecVelocity = SS3E_ReadVec( state.vecVelocity );
	player->MvSetBaseVelocity( SS3E_ReadVec( state.vecBaseVelocity ) );
	mv->m_flForwardMove = state.flForwardMove;
	mv->m_flSideMove    = state.flSideMove;
	mv->m_flUpMove      = state.flUpMove;
	mv->m_nOldButtons   = state.nOldButtons;
	mv->m_flMaxSpeed    = state.flMaxSpeed;
	player->MvLocal().m_bDucked       = state.bDucked != 0;
	player->MvLocal().m_bDucking      = state.bDucking != 0;
	player->MvLocal().m_bInDuckJump   = state.bInDuckJump != 0;
	player->MvLocal().m_flDucktime    = state.flDuckTime;
	player->MvLocal().m_flDuckJumpTime= state.flDuckJumpTime;
	player->MvLocal().m_flJumpTime    = state.flJumpTime;
	player->MvLocal().m_flFallVelocity= state.flFallVelocity;
	player->MvSetViewOffset( SS3E_ReadVec( state.vecViewOffset ) );
	player->MvLocal().m_vecPunchAngle    = SS3E_ReadAng( state.angPunchAngle );
	player->MvLocal().m_vecPunchAngleVel = SS3E_ReadAng( state.angPunchAngleVel );
	player->MvSetGravity( state.flEntityGravity );
	player->MvSurfaceFriction() = state.flSurfaceFriction;

	// THE GROUND, and the one field that cannot round-trip as a value: the loop
	// carries "is there ground" and native carries WHICH entity it is, taken
	// from the trace this side serviced for the loop's own decision.
	//
	// THE RULE IS movement_ground_writeback.h's AND IT USED TO BE WRITTEN HERE,
	// on the two EDGES only -- nothing to something and something to nothing.
	// That misses the ground CHANGING from one entity to another with no frame
	// of air between, which nothing here could do until plan_entity.md step 2g
	// gave managed movers their riders. Measured on the live gate's own lift: a
	// player carried 96 units up out of a shaft went on reporting the crate they
	// had been standing on at the bottom. See the header for the whole story.
	switch ( MovementGround_WriteBack( state.bHasGround != 0,
	                                   player->MvGetGroundEntity() != NULL,
	                                   player->MvGetGroundEntity() == m_pLastGround ) )
	{
	case MOVEMENT_GROUND_CLEAR:
		player->MvSetGroundEntity( NULL );
		break;

	case MOVEMENT_GROUND_SET:
		player->MvSetGroundEntity( m_pLastGround );
		break;

	case MOVEMENT_GROUND_LEAVE:
		break;
	}

	player->MvWaterJumpTime() = state.flWaterJumpTime;
	player->MvWaterJumpVel()  = SS3E_ReadVec( state.vecWaterJumpVel );
	player->MvSwimSoundTime() = state.flSwimSoundTime;
	player->MvStepSoundTime() = state.flStepSoundTime;
	player->MvSurfaceProps()  = state.nSurfaceProps;
	player->MvLadderNormal()  = SS3E_ReadVec( state.vecLadderNormal );
	player->MvStuckLast()     = state.nStuckLast;

	if ( state.bSurfaceDataValid )
	{
		player->MvSurfaceData() = SS_MOVE_HELPER()->GetSurfaceProps()->GetSurfaceData( state.nSurfaceProps );
		if ( player->MvSurfaceData() )
			player->MvTextureType() = player->MvSurfaceData()->game.material;
	}

	mv->m_outStepHeight = state.flOutStepHeight;
	mv->m_outWishVel    = SS3E_ReadVec( state.vecOutWishVel );
	mv->m_outJumpVel    = SS3E_ReadVec( state.vecOutJumpVel );
	mv->m_vecAngles     = SS3E_ReadAng( state.angAngles );
	mv->m_flOldForwardMove = state.flOldForwardMove;

	m_nOnLadder      = state.nOnLadder;
	m_nOldWaterLevel = state.nOldWaterLevel;

	return true;
}

void CShellGameMovement::WorldTrace( const float *pStart, const float *pEnd,
                                           const float *pMins, const float *pMaxs,
                                           int nMask, int nCollisionGroup,
                                           SourceSharpMoveTraceResult *pOut )
{
	trace_t pm;
	Ray_t ray;
	ray.Init( SS3E_ReadVec( pStart ), SS3E_ReadVec( pEnd ),
	          SS3E_ReadVec( pMins ), SS3E_ReadVec( pMaxs ) );
	UTIL_TraceRay( ray, (unsigned int)nMask, SS_HANDLE_ENTITY( mv->m_nPlayerHandle ), nCollisionGroup, &pm );

	IHandleEntity *pGround = MovementGroundFromTrace( pm );

	// THE WHOLE TRACE, KEPT, so ADD_TO_TOUCHED can be replayed with it.
	//
	// IMoveHelper::AddToTouched takes a trace_t and the touch list is replayed
	// by ProcessImpacts against the entities in it -- so the flattened answer
	// the managed loop holds cannot be turned back into one. It does not have
	// to be: every AddToTouched in the loop is made with the trace it JUST
	// asked for, in all four places (TryPlayerMove's blocking trace, the ground
	// trace SetGroundEntity takes, PushEntity's, and CheckStuck's position
	// test), so the last serviced trace is the one it means. Checked against
	// the loop rather than assumed -- TryPlayerMove's one intervening trace,
	// the unswept stuck re-test, only runs on the fraction == 1 path, which
	// breaks before AddToTouched.
	m_LastTrace = pm;

	// REMEMBERED, because the loop cannot carry an entity pointer across the
	// boundary and SetGroundEntity has to be able to name one. The last trace
	// that found standable ground is the one every SetGroundEntity in the loop
	// was fed, so this is the entity it meant.
	if ( pGround )
		m_pLastGround = pGround;

	pOut->flFraction = pm.fraction;
	SS3E_FillVec( pOut->vecEndPos, pm.endpos );
	SS3E_FillVec( pOut->vecPlaneNormal, pm.plane.normal );
	pOut->flPlaneDist    = pm.plane.dist;
	pOut->bStartSolid    = pm.startsolid ? 1 : 0;
	pOut->bAllSolid      = pm.allsolid ? 1 : 0;
	pOut->nSurfaceProps  = pm.surface.surfaceProps;
	pOut->nSurfaceFlags  = pm.surface.flags;
	pOut->nContents      = pm.contents;
	pOut->bHasGround     = pGround ? 1 : 0;
	SS3E_FillVec( pOut->vecGroundVelocity, MovementGroundVelocity( pGround ) );
	pOut->bHasEntity     = pm.m_pEnt ? 1 : 0;
	pOut->bHitWorld      = SourceSharp_IsTheWorld( ss::TraceHit( pm ) ) ? 1 : 0;
}

int CShellGameMovement::WorldEffect( int nKind, const float *pXYZ, float flValue, int nValue )
{
	const Vector where = pXYZ ? SS3E_ReadVec( pXYZ ) : vec3_origin;

	switch ( nKind )
	{
	case SOURCESHARP_MOVE_EFFECT_RESET_TOUCH_LIST:
		SS_MOVE_HELPER()->ResetTouchList();
		break;

	case SOURCESHARP_MOVE_EFFECT_ADD_TO_TOUCHED:
		// The LAST SERVICED TRACE, which is the one the loop means -- see
		// SourceSharpWorldTrace. Without this the movement loop stops touching
		// anything: no trigger_push, no trigger_teleport, no pickup by walking
		// into it, all silently.
		SS_MOVE_HELPER()->AddToTouched( m_LastTrace, mv->m_vecVelocity );
		break;

	case SOURCESHARP_MOVE_EFFECT_START_SOUND_SWIM:
		SS_MOVE_HELPER()->StartSound( where, "Player.Swim" );
		break;

	case SOURCESHARP_MOVE_EFFECT_SET_ANIMATION:
		SS_MOVE_HELPER()->PlayerSetAnimation( SS_PLAYER_ANIM( nValue ) );
		break;

	case SOURCESHARP_MOVE_EFFECT_FALLING_DAMAGE:
		// flValue IS THE DAMAGE, from MoveMath.FallDamage. See
		// SourceSharpMovement_PendingFallDamage for why the amount travels
		// beside an SDK virtual that takes no arguments rather than through it.
		SourceSharpMovement_PendingFallDamage() = flValue;
		return SS_MOVE_HELPER()->PlayerFallingDamage() ? 1 : 0;

	// NULL-GUARDED SINCE STEP 6f, and the guard is the honest shape rather
	// than a defensive one: the client's managed replay owns the player's
	// state in C# and there is no IMovementPlayer on that side any more, so
	// `player` is NULL for the whole of a client command. Both of these were
	// EMPTY BODIES on the client shell they used to reach
	// (C_StandalonePlayer::MvPlayStepSound and MvUpdateStepSound), so nothing
	// is lost by not calling them -- what would be lost is the process, to a
	// NULL dereference on the first footstep.
	case SOURCESHARP_MOVE_EFFECT_PLAY_STEP_SOUND:
		if ( player )
			player->MvPlayStepSound( const_cast< Vector & >( where ), player->MvSurfaceData(), flValue, true );
		break;

	case SOURCESHARP_MOVE_EFFECT_UPDATE_STEP_SOUND:
		if ( player )
			player->MvUpdateStepSound( player->MvSurfaceData(), where, mv->m_vecVelocity );
		break;

	case SOURCESHARP_MOVE_EFFECT_SPLASH:
		break;

	case SOURCESHARP_MOVE_EFFECT_RUMBLE:
		break;

	case SOURCESHARP_MOVE_EFFECT_RESET_DUCK_LATCHED:
		ResetDuckLatched();
		break;

	case SOURCESHARP_MOVE_EFFECT_SET_PLAYER_SURFACE:
		break;

	default:
		break;
	}

	return 1;
}
#endif // CLIENT_DLL


//-----------------------------------------------------------------------------
// The four ABI slots, forwarding to whichever mover is mid-crossing.
//
// They answer NOTHING outside a crossing rather than answering a default: these
// questions are about a player, and outside ProcessMovement there is no player
// to be about. A zero-filled trace with fraction 1 reads as "nothing in the
// way", which is the most dangerous possible wrong answer.
//-----------------------------------------------------------------------------
void SourceSharp_MoveTrace( const float *pStart, const float *pEnd,
                            const float *pMins, const float *pMaxs,
                            int32_t nMask, int32_t nCollisionGroup,
                            SourceSharpMoveTraceResult *pOut )
{
	if ( !pOut )
		return;

	memset( pOut, 0, sizeof( *pOut ) );

#ifdef CLIENT_DLL
	CShellGameMovement *pMover = *ActiveMover();
	if ( !pMover )
#endif
	// THE SERVER REFUSES ALWAYS: its hull traces through SourceSharp_MoveTraceServer
	// with managed's filter (plan_server_native_v2 T4f, T4g).
	{
		// Blocked at the start, which is the safe refusal: a caller outside a
		// crossing gets "you cannot go anywhere", never "the way is clear".
		pOut->bStartSolid = 1;
		pOut->bAllSolid = 1;
		SS3E_FillVec( pOut->vecEndPos, SS3E_ReadVec( pStart ) );
		return;
	}

#ifdef CLIENT_DLL
	pMover->WorldTrace( pStart, pEnd, pMins, pMaxs, nMask, nCollisionGroup, pOut );
#endif
}

#ifndef CLIENT_DLL
//-----------------------------------------------------------------------------
// THE SERVER'S MOVEMENT HULL TRACE, FOR THE MANAGED COMMAND LOOP
// (plan_server_native_v2 T4g). The same trace as WorldTrace, through the
// managed filter, with no mover behind it: what the loop does with the trace
// -- the touch it reports, the ground it remembers -- is managed's, so this
// answers the two entities those need as handles (pOutHandles[0] the entity
// the trace hit, [1] the standable ground MovementGroundFromTrace found; the
// invalid handle for none). Without a filter it refuses as the slots above do.
//-----------------------------------------------------------------------------
void SourceSharp_MoveTraceServer( const float *pStart, const float *pEnd,
                                  const float *pMins, const float *pMaxs,
                                  int32_t nMask, int32_t nCollisionGroup,
                                  SourceSharpMoveTraceResult *pOut, void *pFilter,
                                  uint32_t *pOutHandles )
{
	if ( !pOut || !pOutHandles )
		return;

	memset( pOut, 0, sizeof( *pOut ) );
	pOutHandles[0] = pOutHandles[1] = INVALID_EHANDLE_INDEX;

	if ( !pFilter )
	{
		pOut->bStartSolid = 1;
		pOut->bAllSolid = 1;
		SS3E_FillVec( pOut->vecEndPos, SS3E_ReadVec( pStart ) );
		return;
	}

	trace_t pm;
	Ray_t ray;
	ray.Init( SS3E_ReadVec( pStart ), SS3E_ReadVec( pEnd ),
	          SS3E_ReadVec( pMins ), SS3E_ReadVec( pMaxs ) );
	enginetrace->TraceRay( ray, (unsigned int)nMask, static_cast< ITraceFilter * >( pFilter ), &pm );

	IHandleEntity *pGround = MovementGroundFromTrace( pm );

	pOut->flFraction = pm.fraction;
	SS3E_FillVec( pOut->vecEndPos, pm.endpos );
	SS3E_FillVec( pOut->vecPlaneNormal, pm.plane.normal );
	pOut->flPlaneDist    = pm.plane.dist;
	pOut->bStartSolid    = pm.startsolid ? 1 : 0;
	pOut->bAllSolid      = pm.allsolid ? 1 : 0;
	pOut->nSurfaceProps  = pm.surface.surfaceProps;
	pOut->nSurfaceFlags  = pm.surface.flags;
	pOut->nContents      = pm.contents;
	pOut->bHasGround     = pGround ? 1 : 0;
	SS3E_FillVec( pOut->vecGroundVelocity, MovementGroundVelocity( pGround ) );
	pOut->bHasEntity     = pm.m_pEnt ? 1 : 0;
	pOut->bHitWorld      = SourceSharp_IsTheWorld( ss::TraceHit( pm ) ) ? 1 : 0;

	if ( pm.m_pEnt )
		pOutHandles[0] = (uint32_t)ss::TraceHit( pm )->GetRefEHandle().ToInt();
	if ( pGround )
		pOutHandles[1] = (uint32_t)pGround->GetRefEHandle().ToInt();
}
#endif

int32_t SourceSharp_MoveEffect( int32_t nKind, const float *pXYZ, float flValue, int32_t nValue )
{
#ifdef CLIENT_DLL
	CShellGameMovement *pMover = *ActiveMover();
	if ( !pMover )
		return 1;

	return pMover->WorldEffect( nKind, pXYZ, flValue, nValue );
#else
	// The server's movement answers its own effects (ServerMoveWorld, T4g).
	(void)nKind; (void)pXYZ; (void)flValue; (void)nValue;
	return 1;
#endif
}

#ifdef CLIENT_DLL
// The client's own CMoveData singleton (shell_shared.cpp). The bracket borrows
// it for the servicing methods to read; nothing else on this side uses it any
// more, because the replay that did is managed now.
extern CMoveData *g_pMoveData;

// shell_movement.cpp, beside the counter it bumps. See MoveEndClientCommand.
extern void SourceSharp_MoveNoteManagedCommand();

//-----------------------------------------------------------------------------
// plan_entity.md step 6f -- THE CLIENT'S REPLAY BRACKET.
//
// On this side the whole of the prediction frame is managed: the ring, the
// basis, the compare and the loop. That leaves native two jobs per replayed
// command, and this is both of them.
//
// 1. THE CONSTANTS. The managed side owns the player's state and nothing else;
//    the convars, the tick interval, the view vectors and the wall clock are
//    not the player's and are filled from the one place that fills them for the
//    server too (SourceSharpFillConstants). Only those words are written --
//    everything else in the struct is the caller's and is left alone.
//
// 2. THE SERVICER. The four slots above answer against *ActiveMover(), and
//    outside a bracket they REFUSE: MoveTrace answers "blocked at the start"
//    rather than "the way is clear". That refusal is the reason this is a
//    bracket and not a flag -- step 3e measured what a clear trace does to a
//    client whose loop was running without one, and the answer was a player who
//    falls through the floor.
//
// The mover borrowed is this file's own singleton, which holds the
// point-contents cache the loop's three queries a frame collapse into one
// through. It used to be reached by down-casting g_pGameMovement, which was
// correct while exactly one class implemented the interface and is a cast this
// file no longer has to make: the object is right here. `player` stays NULL:
// nothing the four slots do on this side reads it (the two that did are guarded
// above), and leaving it NULL is what makes "there is no client IMovementPlayer
// any more" a fact the code states rather than one it works around.
//-----------------------------------------------------------------------------
int32_t SourceSharp_MoveBeginClientCommand( uint32_t hPlayer, SourceSharpMoveState *pOut )
{
	if ( !pOut )
		return 0;

	CShellGameMovement *pMover = &g_ShellGameMovement;

	// The move data the servicing methods read. m_nPlayerHandle is the trace
	// filter's skip entity -- without it every trace starts solid inside the
	// player's own hull.
	CMoveData *pMove = g_pMoveData;

	if ( !pMove )
		return 0;

	// UnsafeFromIndex, and the name is the honest one: the word came from
	// CBaseHandle::ToInt() on this side's own valid handle (the managed player
	// holds it), which is exactly the provenance basehandle.h requires of it.
	pMove->m_nPlayerHandle = CBaseHandle::UnsafeFromIndex( (int)hPlayer );

	pMover->SetMoveData( pMove );
	pMover->FillConstants( pOut );

	*ActiveMover() = pMover;
	return 1;
}

void SourceSharp_MoveEndClientCommand( int32_t bRan, int32_t *pOutGroundIndex )
{
	CShellGameMovement *pMover = *ActiveMover();

	// WHICH ENTITY THE LOOP MEANT, before the bracket closes. The loop carries
	// "is there ground" as a bool; the last trace that found standable ground
	// is the one every SetGroundEntity in it was fed, so that trace's entity is
	// the answer. -1 when no trace has ever found one, which the caller reads as
	// "keep what you had" -- MovementGround_WriteBack's LEAVE arm.
	if ( pOutGroundIndex )
	{
		IHandleEntity *pGround = pMover ? pMover->LastGround() : NULL;

		*pOutGroundIndex = pGround ? pGround->GetRefEHandle().GetEntryIndex() : -1;
	}

	*ActiveMover() = NULL;

	// THE COUNTER THAT TELLS THE TWO APART. On the server this line lives
	// inside SourceSharp_MoveProcessMovement, which is native and only bumps
	// once managed has answered "I ran it". The client's loop is called from
	// managed code directly since step 6f, so this bracket is the only native
	// place that can make the same distinction -- and cs_cl_move_managed_report
	// is worth nothing if it counts asks instead of runs.
	if ( bRan )
		SourceSharp_MoveNoteManagedCommand();
}
#endif // CLIENT_DLL

//=============================================================================
// IGameMovement — the seven pure virtuals, and what each one is for HERE.
//
// WHICH OF THEM THE ENGINE CALLS: none. Measured two ways rather than assumed,
// because a recorded lesson in this project says an interface's users cannot be
// established by grepping this repo:
//
//   * the string "GameMovement001" does not occur in any binary under
//     Source SDK Base 2013 Multiplayer's bin/ -- 32-bit or linux64 -- while
//     "ServerGameDLL" occurs twice in engine.so. An engine that does not carry
//     the name cannot CreateInterface() it.
//   * in the public SDK every caller of IGameMovement is a GAME DLL file:
//     game/server/player_command.cpp and game/client/prediction.cpp, both
//     through the GLOBAL g_pGameMovement and not through the interface.
//
// So the live callers are this build's own, and there are two:
//
//   SERVER  shell_standalone_player.cpp calls StartTrackPredictionErrors,
//           ProcessMovement and FinishTrackPredictionErrors, in that order,
//           once per queued user command.
//   CLIENT  nothing. Stock's prediction.cpp is not compiled here (there is no
//           prediction.o in the client object table; shell_prediction.cpp is),
//           and since step 6f the client's replay calls the managed loop
//           directly through the bracket at the bottom of this file.
//
// The remaining four -- DiffPrint, GetPlayerMins, GetPlayerMaxs and
// GetPlayerViewOffset -- have NO caller in either DLL, and are the client's
// alone since plan_server_native_v2 T2 (stock's interface, which the client
// exports, declares them). They are implemented rather than refused because they are three lines each and because a wrong
// answer from a hull accessor is a player who walks through walls: a refusal
// that returned vec3_origin would be a plausible-looking zero of exactly the
// kind `.claude/entity-port` has three recorded cases of.
//=============================================================================

#ifdef CLIENT_DLL   // the server's commands are managed since plan_server_native_v2 T4g
CShellGameMovement::CShellGameMovement( void )
{
	player           = NULL;
	mv               = NULL;
	m_nOldWaterLevel = 0;
	m_nOnLadder      = 0;
	m_pLastGround    = NULL;

	// trace_t has a user-declared constructor, so clang (-Wnontrivial-memcall)
	// wants the cast to say the byte-wise zeroing is meant. It is.
	memset( (void *)&m_LastTrace, 0, sizeof( m_LastTrace ) );

}

CShellGameMovement::~CShellGameMovement( void )
{
}

//-----------------------------------------------------------------------------
// ONE COMMAND. This is the whole of CGameMovement::ProcessMovement that
// survives, and what is missing from it is the point of the step.
//
// Stock ended with `PlayerMove(); FinishMove();` behind a test that read "if the
// managed loop did not run it". THERE IS NO SUCH BODY HERE. The three ways one
// command can have nothing to run it are enumerated in shell_gamemovement.h and
// every one of them says so out loud.
//-----------------------------------------------------------------------------
void CShellGameMovement::ProcessMovement( IMovementPlayer *pPlayer, CMoveData *pMove )
{
	Assert( pMove && pPlayer );

	if ( !pMove || !pPlayer )
		return;

	// THE A/B ARM, FIRST AND WHOLE. Delegated before any of the preamble below
	// runs, because the stock mover does its own: scaling gpGlobals->frametime
	// by the lagged-movement value twice, or resetting a point-contents cache
	// that is not the one about to be read, would make the two arms measure
	// different things -- which is the one way this switch could cause the
	// misprediction it exists to measure.
	if ( !MovementManaged() )
	{
		// NOTHING TO SELECT. Until step 8f-remove this delegated to a stock
		// CGameMovement lent by `gamemovement.cpp`; that file is in neither vpc
		// now, so the 0 arm is a named, printed, fatal refusal rather than a
		// player who silently stops moving. The `return` is unreachable --
		// SourceSharp_MovementFatal ends in tier0's Error(), which does not
		// return -- and is here because the declaration carries no NORETURN
		// attribute, so without it the managed preamble below would read as
		// reachable from this arm.
		SourceSharp_MovementFatal( kMovementFatal_NoStock, 0 );
		return;
	}

	const float flStoreFrametime = gpGlobals->frametime;

	//!!HACK HACK: Adrian - slow down all player movement by this factor.
	//!!Blame Yahn for this one.
	gpGlobals->frametime *= pPlayer->MvGetLaggedMovementValue();


	// StartTrackPredictionErrors should have set this
	Assert( player == pPlayer );
	player = pPlayer;

	mv = pMove;
	mv->m_flMaxSpeed = pPlayer->MvGetPlayerMaxSpeed();

	// THE CROSSING. RunManagedCommand answers false for ONE thing now -- the
	// managed loop declined this move type -- because SourceSharp_MoveProcessMovement
	// no longer returns for a managed side that has not started. It Error()s.
	if ( !RunManagedCommand() )
	{
		// UNREACHABLE, and counted rather than assumed. Step 8f-movetypes gave
		// MOVETYPE_OBSERVER, _FLY and _FLYGRAVITY managed bodies, so the loop
		// declines nothing; step 8f-remove took away the stock mover this used
		// to fall back to. The counter is what tells a reader which of those
		// two statements stopped being true -- cs_{sv,cl}_move_managed_report
		// prints it as refused:declined-movetype.
		SourceSharp_MovementRefused( kMovementFatal_Declined,
			(int)pPlayer->MvGetMoveType() );
	}

	//This is probably not needed, but just in case.
	gpGlobals->frametime = flStoreFrametime;
}

//-----------------------------------------------------------------------------
// PREDICTION_ERROR_CHECK_LEVEL is 0 in this build (shareddefs.h:914), so stock's
// pair is `player = pPlayer` and an empty body. The assignment is not decoration:
// ProcessMovement asserts on it, and the servicing methods read `player`.
//
// Step 8f-igamemovement forwarded both to the lent stock mover behind a convar
// test; step 8f-remove deleted the loan with `gamemovement.cpp`, so the pair is
// stock's own body again -- an assignment and nothing. The convar is not read
// here any more: reading it to decide between one implementation and none is
// the check that cannot fail.
//-----------------------------------------------------------------------------
void CShellGameMovement::StartTrackPredictionErrors( IMovementPlayer *pPlayer )
{
	player = pPlayer;
}

void CShellGameMovement::FinishTrackPredictionErrors( IMovementPlayer *pPlayer )
{
}

//-----------------------------------------------------------------------------
// Stock's body is empty at PREDICTION_ERROR_CHECK_LEVEL 0 and this one is too.
// The varargs are consumed rather than ignored so a caller with a bad format
// string is not left holding a half-walked list.
//-----------------------------------------------------------------------------
void CShellGameMovement::DiffPrint( char const *fmt, ... )
{
	va_list argptr;
	va_start( argptr, fmt );
	va_end( argptr );
}

//-----------------------------------------------------------------------------
// The hulls, stock's bodies unchanged. They read the gamerules view vectors and
// scale by the player's model scale -- which is why they need a player, and why
// they answer the UNSCALED vector when there is not one rather than a zero.
//-----------------------------------------------------------------------------
Vector CShellGameMovement::GetPlayerMins( bool ducked ) const
{
	if ( !player )
	{
		const RulesViewVectors_t *vv = g_pGameRules->GetViewVectors();
		return ducked ? vv->m_vDuckHullMin : vv->m_vHullMin;
	}

	return ducked ? VEC_DUCK_HULL_MIN_SCALED( player ) : VEC_HULL_MIN_SCALED( player );
}

Vector CShellGameMovement::GetPlayerMaxs( bool ducked ) const
{
	if ( !player )
	{
		const RulesViewVectors_t *vv = g_pGameRules->GetViewVectors();
		return ducked ? vv->m_vDuckHullMax : vv->m_vHullMax;
	}

	return ducked ? VEC_DUCK_HULL_MAX_SCALED( player ) : VEC_HULL_MAX_SCALED( player );
}

Vector CShellGameMovement::GetPlayerViewOffset( bool ducked ) const
{
	if ( !player )
	{
		const RulesViewVectors_t *vv = g_pGameRules->GetViewVectors();
		return ducked ? vv->m_vDuckView : vv->m_vView;
	}

	return ducked ? VEC_DUCK_VIEW_SCALED( player ) : VEC_VIEW_SCALED( player );
}

//=============================================================================
// The two CGameMovement members the servicing half reached through, moved with
// it. Stock's bodies, unchanged apart from the class name.
//=============================================================================

//-----------------------------------------------------------------------------
// Purpose: Reset interpolation when player duck state changes
//-----------------------------------------------------------------------------
void CShellGameMovement::ResetDuckLatched()
{
	// NULL-GUARDED SINCE STEP 6f, for WorldEffect's reason: there is no client
	// IMovementPlayer any more. Nothing is lost -- the shell this used to reach
	// answered MvInFirstPersonView() true unconditionally, so the latch reset
	// never ran on this side anyway. What the guard removes is the dereference,
	// which the managed loop reaches through
	// SOURCESHARP_MOVE_EFFECT_RESET_DUCK_LATCHED on every duck.
	if ( player && !player->MvInFirstPersonView() )
		player->MvResetLatched();
}
#endif // CLIENT_DLL

