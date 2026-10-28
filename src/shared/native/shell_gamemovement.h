// The movement command fatal reasons, and an engine-free formatter for them so the
// text is testable apart from the noreturn Error() call.

#pragma once

#include <stddef.h>
#include <stdio.h>

// Formats one of the reasons below and hands it to Error(). Does not return.
void SourceSharp_MovementFatal( int nReason, int nDetail );

#ifndef CLIENT_DLL
// The server's managed command loop: the fatal check asked before each command, and
// the per-command counter.
void SourceSharp_MovementRequireManaged();
void SourceSharp_MoveNoteServerCommand();
#endif

// The fall damage of the landing about to be applied. IMoveHelper::PlayerFallingDamage()
// takes no arguments, so the amount travels beside the call. Set immediately before the
// call and read inside it; meaningless afterwards.
inline float &SourceSharpMovement_PendingFallDamage()
{
	static float s_flPendingFallDamage = 0.0f;
	return s_flPendingFallDamage;
}

// The ways one movement command can have nothing to run it.
enum
{
	// The managed side has not started, or started without binding MoveProcessMovement.
	kMovementFatal_NoManagedSide = 0,

	// `sourcesharp_movement_managed 0`, with no stock CGameMovement in the build.
	kMovementFatal_NoStock = 1,

	// The managed loop declined the command's move type. Unreachable today; still
	// counted and printed.
	kMovementFatal_Declined = 2,

	kMovementFatal_Count = 3,
};

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

// Formats one reason into pOut. `nDetail` is the move type for kMovementFatal_Declined.
// Always NUL-terminates when cOut > 0; returns snprintf's result so truncation shows.
inline int SourceSharpMovement_Format( char *pOut, size_t cOut, const char *pSide, int nReason, int nDetail )
{
	if ( !pOut || cOut == 0 )
		return 0;

	if ( !pSide || !pSide[0] )
		pSide = "?";

	// The move type is printed only for the reason it applies to.
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
					 pSide, SourceSharpMovement_ReasonText( nReason ), szDetail,
					 SourceSharpMovement_Remedy( nReason ) );
}

// Prints how many commands the managed movement loop ran on this side, and the refusal counts.
void SourceSharp_MoveManagedReport();
