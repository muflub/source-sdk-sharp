//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — what a pusher's trace refuses to hit ===============//
//
// CTraceFilterPushMove decides, for each thing the spatial partition offers it,
// whether the pusher's speculative trace should ignore it. Stock had two
// rejections and reached both of them through an unconditional downcast:
//
//     Assert( dynamic_cast<CBaseEntity*>(pHandleEntity) );
//     CBaseEntity *pTestEntity = static_cast<CBaseEntity*>(pHandleEntity);
//
// The assert compiles out of a release build, and this build puts entities in
// the partition that are not CBaseEntity — so a managed prop standing in an SDK
// func_rotating's way was two virtual calls through the wrong vtable. That is
// the same hazard igameentity.h was written for in CTraceFilterSimple, one
// level down and never noticed because the SDK's own movers are what usually
// meet it.
//
// Fixing the cast forces the question the cast was hiding: what do the two
// rejections MEAN for an entity on the other hierarchy? They are not the same
// answer, and the asymmetry is the thing worth pinning:
//
//   SHARES MY ROOT MOVE PARENT — SDK only, and not because the other hierarchy
//   was left out. That hierarchy is a handle-keyed registry resolved through a
//   virtual GetGameEntity() precisely so that no raw parent pointer exists to
//   hand out (shell_standalone_hierarchy.cpp), so nothing over there can share
//   an SDK root parent with the blocker. The clause is vacuous rather than
//   unimplemented.
//
//   THE SOLVER IS MOVING IT — both, spelled differently. The SDK asks the
//   entity for a MOVETYPE_VPHYSICS and then asks that entity for its object.
//   The other hierarchy has no move type to compare against: physicsowner.h's
//   rule is that an object is in the registry BECAUSE its entity simulates, so
//   "it has a moveable object" IS the whole condition, and asking for a move
//   type as well would be asking the same question twice and getting a neutral
//   for an answer. The object is reached through StandaloneEntity_PhysicsObject
//   because the boundary deliberately has no VPhysicsGetObject — see
//   standalone_relays.h.
//
// A third case exists and is neither: a static prop, which is in the partition,
// is not an entity, and answers neither lookup. The filter must not invent a
// rejection for it — the base filter already knows what to do with one — so it
// is named here rather than folded into either arm.
//
// Header-only and engine-free, so the rule is checked by gtest
// (src/sourcesharp/test/push_blocker_rule_test.cpp) rather than by pushing a
// crate with a rotating door.
//
//=============================================================================//
#ifndef SOURCESHARP_PUSH_BLOCKER_RULE_H
#define SOURCESHARP_PUSH_BLOCKER_RULE_H
#ifdef _WIN32
#pragma once
#endif

enum PushBlockerKind_t
{
	// Neither lookup answered: a static prop, or something else in the
	// partition that is not an entity at all.
	PUSH_BLOCKER_NOT_AN_ENTITY = 0,

	// A CBaseEntity, which is what this filter has always assumed.
	PUSH_BLOCKER_SDK = 1,

	// An entity on the parallel hierarchy.
	PUSH_BLOCKER_STANDALONE = 2,
};

//-----------------------------------------------------------------------------
// True when the filter should REFUSE to hit this — i.e. return false without
// consulting the base filter.
//
// bSharesRootParent and bMoveTypeIsVPhysics are SDK facts and are ignored for
// the other two kinds; a caller that cannot answer them passes false.
//-----------------------------------------------------------------------------
inline bool PushFilter_Rejects( PushBlockerKind_t kind,
                                bool bSharesRootParent,
                                bool bMoveTypeIsVPhysics,
                                bool bHasMoveablePhysicsObject )
{
	switch ( kind )
	{
	case PUSH_BLOCKER_SDK:
		if ( bSharesRootParent )
			return true;

		return bMoveTypeIsVPhysics && bHasMoveablePhysicsObject;

	case PUSH_BLOCKER_STANDALONE:
		// No SDK root parent to share, and no move type to compare: having a
		// moveable object is the whole of "the solver is moving it".
		return bHasMoveablePhysicsObject;

	default:
		// Not an entity. The base filter decides, as it did before.
		return false;
	}
}

#endif // SOURCESHARP_PUSH_BLOCKER_RULE_H
