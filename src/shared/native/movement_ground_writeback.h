//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// What the movement crossing writes back for the ground: the loop knows whether there is
// ground, native knows which entity. Write whenever it is not already right, as stock's
// unconditional SetGroundEntity does, so a rider carried onto new ground is updated.
// Includes nothing, so its gtest needs only this header.
#pragma once

enum MovementGroundWrite_t
{
	// Already right: a player standing still on one floor.
	MOVEMENT_GROUND_LEAVE = 0,

	// The loop let go of the ground: a jump, or walking off an edge.
	MOVEMENT_GROUND_CLEAR = 1,

	// The player holds no ground, or different ground from the one found.
	MOVEMENT_GROUND_SET = 2,
};

inline MovementGroundWrite_t MovementGround_WriteBack( bool bLoopHasGround, bool bPlayerHasGround,
													   bool bPlayerHoldsTheOneFound )
{
	if ( !bLoopHasGround )
		return bPlayerHasGround ? MOVEMENT_GROUND_CLEAR : MOVEMENT_GROUND_LEAVE;

	return bPlayerHoldsTheOneFound ? MOVEMENT_GROUND_LEAVE : MOVEMENT_GROUND_SET;
}
