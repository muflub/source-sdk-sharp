//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — what the movement crossing writes back ============//
//
// WHAT THE MOVEMENT CROSSING WRITES BACK for the ground, given what the loop
// decided and what the player already holds.
//
// The managed loop (plan_entity.md step 3e) carries "IS there ground" as a
// BOOL, because an entity pointer cannot cross the boundary; native carries
// WHICH entity, remembered from the last trace that found standable ground.
// Putting the two back together is a decision, and the first spelling of it was
// wrong in a way nothing could reach until step 2g:
//
//     if      ( !bHasGround && player->MvGetGroundEntity() )  clear
//     else if (  bHasGround && !player->MvGetGroundEntity() ) set
//
// That writes on the two EDGES -- nothing to something, something to nothing --
// and never when the ground CHANGES FROM ONE ENTITY TO ANOTHER without a frame
// of air in between. Before 2g nothing in this build could do that: a managed
// mover carried nobody, so the only way to arrive on a new floor was to fall
// onto it, and falling clears the ground first. A carried rider does not fall.
//
// MEASURED, on dm_lockdown, with the live gate's own lift: the player stands on
// a prop_physics on the floor (index 65), a func_movelinear rises into them and
// carries them 96 units to the top of the shaft, and their GroundEntity reads
// the CRATE the whole way up -- 90 units below them, and stale for as long as
// they stand there. FL_ONGROUND is set, the surface friction is the crate's,
// and anything asking what they are standing on is told a lie.
//
// So the rule is "write it whenever it is not already right", which is also
// what stock does: CGameMovement::SetGroundEntity ends in an unconditional
// player->SetGroundEntity( newGround ). The edge test was never an
// optimisation over that -- it was an attempt to avoid naming an entity the
// loop could not name, and the entity IS named, by the same trace the loop's
// decision came from.
//
// Header-only and engine-free so the rule is checked by gtest
// (src/sourcesharp/test/movement_ground_writeback_test.cpp) rather than by
// riding a lift. NOTHING IS INCLUDED, deliberately: movement_ground.h next
// door needs mathlib and trace_t, which is why this is its own file rather
// than a paragraph in it -- the gtest binary is then this header and gtest.
//
//=============================================================================//
#ifndef SOURCESHARP_MOVEMENT_GROUND_WRITEBACK_H
#define SOURCESHARP_MOVEMENT_GROUND_WRITEBACK_H
#ifdef _WIN32
#pragma once
#endif

enum MovementGroundWrite_t
{
	// It is already right. The commonest answer by far: a player standing
	// still on one floor, every tick.
	MOVEMENT_GROUND_LEAVE = 0,

	// The loop let go of the ground. Somebody jumped, or walked off an edge.
	MOVEMENT_GROUND_CLEAR = 1,

	// The loop has ground and the player is not holding THAT ground -- either
	// they had none (they landed) or they had a different one (they were
	// carried onto this one, or stepped from one brush to another).
	MOVEMENT_GROUND_SET = 2,
};

inline MovementGroundWrite_t MovementGround_WriteBack( bool bLoopHasGround,
                                                       bool bPlayerHasGround,
                                                       bool bPlayerHoldsTheOneFound )
{
	if ( !bLoopHasGround )
		return bPlayerHasGround ? MOVEMENT_GROUND_CLEAR : MOVEMENT_GROUND_LEAVE;

	return bPlayerHoldsTheOneFound ? MOVEMENT_GROUND_LEAVE : MOVEMENT_GROUND_SET;
}

#endif // SOURCESHARP_MOVEMENT_GROUND_WRITEBACK_H
