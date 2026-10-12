//========= SourceSharp — IGameMovement, published by the shim ==============//
//
// plan_entity.md step 8f-igamemovement. `gamemovement.cpp` is leaving the
// build, and IGameMovement is one of the four things `phase4-exit` measured as
// holding it there: the engine's own interface macro named CGameMovement, so a
// replacement had to be a concrete class rather than a forward. This header is
// the seam that lets the file go.
//
// It declares TWO things and nothing else:
//
//   1. the reasons a movement command can be fatal; and
//   2. the formatter for those, which is engine-free so it can be tested.
//
// IT DECLARED A THIRD UNTIL STEP 8f-remove, and what happened to it is worth
// keeping. `SourceSharp_RegisterStockGameMovement` was the registration hook
// `gamemovement.cpp` used to LEND the shim its stock CGameMovement while that
// file was still compiled, and it pointed the only way it could: the stock file
// called INTO the shim, and the shim named nothing the stock file defined. That
// direction is what made 8f-remove exactly two `$File` rows — nothing in the
// shim was left undefined by the file's going. A shim that had reached the
// other way — `extern CGameMovement g_GameMovement;` — would have read
// identically and would have pinned the file in the build forever.
//
// The file has now gone, so nobody registers, and a pointer that can only ever
// be NULL is worse than no pointer: the tests beside it read as a live
// fallback. The hook, the pointer and the three forwards to it were deleted in
// the same commit as the two vpc rows. kMovementFatal_NoStock below is what the
// A/B switch's 0 arm answers instead.
//
// WHY THE MESSAGES ARE FORMATTED HERE AND NOT AT THE Error() CALL.
//
// Error() is NORETURN, so the function that calls it cannot be unit tested: a
// test that reached it would take the test binary down with it.
// `managed_init_failure.h` solved this once already for the init failure
// (SourceSharpInit_Format) and this is the same split for the same reason — the
// TEXT is testable, the dying is not.
//
// The text matters more here than usual. This project has shipped EIGHT silent
// losses and the two most recent are exactly the shape this file removes: a
// path that quietly did nothing while everything around it reported success.
// A build silently running stock movement instead of the managed loop is
// indistinguishable from a working one right up until the two sides disagree
// under prediction, so each reason below says what was asked for, what was
// missing, and what the reader should do about it.
//
//=============================================================================//

#ifndef SHELL_GAMEMOVEMENT_H
#define SHELL_GAMEMOVEMENT_H
#ifdef _WIN32
#pragma once
#endif

#include <stddef.h>
#include <stdio.h>

//-----------------------------------------------------------------------------
// Formats one of the reasons below and hands it to Error(). DOES NOT RETURN.
//
// Lives in shell_gamemovement.cpp because Error() is tier0's and this header is
// deliberately engine-free; the text it prints is SourceSharpMovement_Format's
// and is testable on its own.
//-----------------------------------------------------------------------------
void SourceSharp_MovementFatal( int nReason, int nDetail );

//-----------------------------------------------------------------------------
// HOW MUCH THE LANDING THAT IS ABOUT TO BE APPLIED DOES.
//
// SOURCESHARP_MOVE_EFFECT_FALLING_DAMAGE is the one move effect with an answer,
// and IMoveHelper::PlayerFallingDamage() -- an SDK virtual this port does not
// own -- takes no arguments. So the amount, which is managed's since the
// policy-player lane, travels beside the call rather than through it.
//
// WHY IT IS NOT RECOMPUTED HERE, which is what the shim did before: the helper
// only has the player's m_flFallVelocity MEMBER, and that is the value from
// before the move that is landing them -- the loop's own fall velocity is still
// in the managed state and is not written back until FinishMove. So the
// threshold that armed the call and the number that scaled the damage read two
// different inputs, and a fall could arm the call and then do nothing.
// MoveMath.FallDamage is the one formula now and it is handed the velocity the
// arming test used.
//
// A FUNCTION-LOCAL STATIC AND NOT A GLOBAL: one per DLL, which is exactly the
// scope wanted -- the client's move helper ignores it and the server's reads it
// -- and no .cpp has to own a definition for a float that is written and read
// two lines apart on one thread.
//
// It is set immediately before the call and read immediately inside it. The
// value left behind afterwards is meaningless and no caller may read it.
//-----------------------------------------------------------------------------
inline float &SourceSharpMovement_PendingFallDamage()
{
	static float s_flPendingFallDamage = 0.0f;
	return s_flPendingFallDamage;
}

//-----------------------------------------------------------------------------
// The three ways one movement command can have nothing to run it.
//
// NOTE WHAT IS NOT HERE: "the managed side has not started, so run the stock
// body." That was a real arm of `SourceSharp_MoveProcessMovement` until this
// step, and deleting it is the step's whole point. It is now
// kMovementFatal_NoManagedSide.
//-----------------------------------------------------------------------------
enum
{
	// The managed side has not started, or started without binding
	// MoveProcessMovement. There is no stock body behind this any more.
	kMovementFatal_NoManagedSide = 0,

	// `sourcesharp_movement_managed 0` on a build with no stock CGameMovement
	// left in it. The operator asked for a body that is not there.
	kMovementFatal_NoStock = 1,

	// The managed loop DECLINED the command's move type and there is no stock
	// mover left to run it. UNREACHABLE as of step 8f-movetypes, which gave
	// MOVETYPE_OBSERVER, _FLY and _FLYGRAVITY managed bodies — the last three
	// declines step 3e had declared. Kept, counted and printed anyway: "the
	// loop declines nothing" is a claim about Movement.ProcessMovement, and a
	// counter is the only thing that can tell a reader the day it stops being
	// true.
	kMovementFatal_Declined = 2,

	kMovementFatal_Count = 3,
};

//-----------------------------------------------------------------------------
inline const char *SourceSharpMovement_ReasonText( int nReason )
{
	switch ( nReason )
	{
	case kMovementFatal_NoManagedSide:
		return "the managed (C#) side has not started, or started without "
		       "binding MoveProcessMovement";
	case kMovementFatal_NoStock:
		return "sourcesharp_movement_managed is 0 and this build has no stock "
		       "CGameMovement left in it";
	case kMovementFatal_Declined:
		return "the managed loop declined this move type and no stock mover is "
		       "registered to run it";
	default:
		return "unknown";
	}
}

//-----------------------------------------------------------------------------
inline const char *SourceSharpMovement_Remedy( int nReason )
{
	switch ( nReason )
	{
	case kMovementFatal_NoManagedSide:
		return "read the [SourceSharp] lines ABOVE this one: the managed side "
		       "reports its own failure first and that is the actual cause. A "
		       "fresh worktree missing game/mod_sharp/bin/dotnet is the common "
		       "one — run tools/worktree-setup";
	case kMovementFatal_NoStock:
		return "set sourcesharp_movement_managed back to 1. The A/B switch had "
		       "one arm removed with gamemovement.cpp (plan_entity.md step "
		       "8f-remove) and no longer selects anything";
	case kMovementFatal_Declined:
		return "this move type has no managed body. plan_entity.md step "
		       "8f-movetypes ports the three that had none; if it has landed, "
		       "the decline is a bug in Movement.ProcessMovement";
	default:
		return "none";
	}
}

//-----------------------------------------------------------------------------
// Formats one reason into pOut. `nDetail` is the move type for
// kMovementFatal_Declined and is ignored otherwise.
//
// Engine-free on purpose: snprintf and string literals, nothing that needs
// tier0 to be up. Always NUL-terminates when there is room for one byte.
// Returns what snprintf returned, so a caller can see truncation.
//-----------------------------------------------------------------------------
inline int SourceSharpMovement_Format( char *pOut, size_t cOut,
                                       const char *pSide, int nReason, int nDetail )
{
	if ( !pOut || cOut == 0 )
		return 0;

	if ( !pSide || !pSide[0] )
		pSide = "?";

	// Printed only for the reason it means something for. A move type of 0 under
	// the word FATAL on a reason that has nothing to do with move types reads as
	// data and sends the reader looking for a player who does not exist.
	char szDetail[128];
	szDetail[0] = '\0';
	if ( nReason == kMovementFatal_Declined )
		snprintf( szDetail, sizeof( szDetail ), "  movetype: %d\n", nDetail );

	return snprintf( pOut, cOut,
		"[SourceSharp] FATAL: the %s side was asked to move a player and has "
		"nothing to move them with.\n"
		"  reason  : %s\n"
		"%s"
		"  fix     : %s\n"
		"Movement is C# in this build (plan_entity.md 3.10) and the stock "
		"CGameMovement fallback was DELETED at step 8f-igamemovement, on purpose: "
		"a build that quietly runs stock movement instead of the managed loop is "
		"indistinguishable from a working one until the two sides disagree under "
		"prediction. Stopping here rather than shipping a ninth silent loss.\n",
		pSide,
		SourceSharpMovement_ReasonText( nReason ),
		szDetail,
		SourceSharpMovement_Remedy( nReason ) );
}

//-----------------------------------------------------------------------------
// Prints how many commands the managed movement loop ran on this side, and the
// refusal counts. Defined in shell_movement.cpp, beside the counters it reads;
// cs_cl_move_managed_report and the debug-only cs_sv_move_managed_report
// (shell_movement_debug.cpp) are its two callers.
//-----------------------------------------------------------------------------
void SourceSharp_MoveManagedReport();

#endif // SHELL_GAMEMOVEMENT_H
