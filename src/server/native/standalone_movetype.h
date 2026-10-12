//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Which MOVETYPE_* a standalone entity reports across the ABI.
// The parallel hierarchy answers a SUBSET of what a CBaseEntity answers, and
// the movetype used to be the emptiest part of that subset: the standalone arm
// of SourceSharp_EntityGetState wrote a movetype only when the entity had a
// VPhysics object, so everything else on the hierarchy -- including the
// player, which has no physics object at all -- came back MOVETYPE_NONE.
// That is not a cosmetic gap. Riders.Carry filters its candidates by movetype
// before it looks at where they are:
//     if (moveType != MoveType.Walk && moveType != MoveType.Step) continue;
// so the one entity a lift exists to carry was discarded before the geometry
// test ever ran. Measured live on ss_sandbox at b5920b61: belts_lift sent GoUp
// with a player aboard rose 16 -> 112 while the player stayed at 26 and then
// fell to the floor. The brush went straight through them. The DESCENDING case
// hid this, because a rider going down is carried by gravity onto a floor that
// keeps arriving under them -- Carry contributes nothing there, and a lift that
// works one way is easy to read as a lift that works.
// The rule, in precedence order:
//   a player answers its OWN movetype. CStandalonePlayer keeps a real one
//     (MvGetMoveType, MOVETYPE_WALK from Spawn, MOVETYPE_NOCLIP while
//     noclipping) and it is the authority -- a noclipping player must not be
//     carried, and reporting WALK for them would carry them.
//   anything else holding a physics object answers the movetype MANAGED wrote
//     into its engine block (Head.MoveType) when the object was made or handed
//     over: VPHYSICS for a moveable object (stock's VPhysicsInitNormal), NONE
//     beside a static one (VPhysicsInitStatic leaves the entity's own, so a
//     solid prop_dynamic is MOVETYPE_NONE as stock's is). The rule is
//     EntityPhysicsCreation's and PhysicsConversion's (2026-10-12,
//     plan_physics_bones_perf C2/C6); before it, any object read as VPHYSICS
//     and a static prop looked simulated to the collision rules.
//   everything else is NONE, which is the honest answer. A managed brush mover
//     is stepped by CStandaloneEntity::UpdateMovement rather than by any
//     movetype, and claiming PUSH for it would invite the physics solver to
//     treat it as one.
// Header-only and engine-free so the precedence is checked by gtest rather
// than by standing on a lift (src/sourcesharp/test/standalone_movetype_test.cpp).

#ifndef SOURCESHARP_STANDALONE_MOVETYPE_H
#define SOURCESHARP_STANDALONE_MOVETYPE_H

#include "sourcesharp.h"

//-----------------------------------------------------------------------------
// nPlayerMoveType is only read when bIsPlayer, and is the raw MOVETYPE_* the
// player keeps. nHeldMoveType is the block's word, read only with an object. The MOVETYPE_* numbers and the SOURCESHARP_MOVETYPE_* numbers
// are the same numbers by construction (sourcesharp.h), so a player's movetype
// crosses as itself rather than through a mapping that could drift.
//-----------------------------------------------------------------------------
inline int32_t StandaloneMoveType_Report( bool bIsPlayer, int nPlayerMoveType, bool bHasPhysics,
                                          int nHeldMoveType )
{
	if ( bIsPlayer )
		return (int32_t)nPlayerMoveType;

	if ( bHasPhysics )
		return (int32_t)nHeldMoveType;

	return SOURCESHARP_MOVETYPE_NONE;
}

#endif // SOURCESHARP_STANDALONE_MOVETYPE_H
