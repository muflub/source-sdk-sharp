//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — telling vphysics its cached answer has changed ======//
//
// ONE FUNCTION, AND STOCK'S OWN COMMENT IS THE WHOLE JUSTIFICATION.
//
//     src/game/shared/baseentity_shared.cpp:2714, CBaseEntity::CollisionRulesChanged
//
//     // ivp maintains state based on recent return values from the collision
//     // filter, so anything that can change the state that a collision filter
//     // will return (like m_Solid) needs to call RecheckCollisionFilter.
//
// IPhysicsCollisionSolver::ShouldCollide is NOT asked per step. vphysics asks
// it once, when a pair of objects comes into proximity, and then keeps the
// answer: IVP creates — or refuses to create — the pair from that one
// answer. The only thing that makes it ask
// again is `IPhysicsObject::RecheckCollisionFilter`.
//
// So a pair refused ONCE stays refused for the object's whole life, while every
// later reading of the rule table says the two should collide. That is a prop
// falling through a floor every other prop is standing on, with nothing wrong
// anywhere a report can look. vphysics has a debug check for exactly this
// mistake and it names it in those words: "Changed collision rules for %s vs.
// %s without calling recheck!".
//
// THE PARALLEL HIERARCHY HAD NO EQUIVALENT. CBaseEntity calls
// CollisionRulesChanged from SetSolid, SetSolidFlags, AddSolidFlags,
// RemoveSolidFlags and SetCollisionGroup. CStandaloneEntity::SetSolidity and
// ::SetCollisionGroup wrote the block, marked the field changed, updated the
// spatial partition — and told vphysics nothing. The engine-facing half of the
// rule moved; this half was left behind.
//
// WHY IT IS A HEADER AND A TEMPLATE. The rule is "if there is an object, ask it
// again", which is two lines and one decision — and the decision is worth a
// test, which means it must be reachable without an engine. The template
// parameter is any type with `RecheckCollisionFilter()`, which is
// IPhysicsObject in the build and a counting stub in the test.
//
//=============================================================================//
#ifndef SOURCESHARP_STANDALONE_COLLISION_RULES_H
#define SOURCESHARP_STANDALONE_COLLISION_RULES_H

//-----------------------------------------------------------------------------
// Tell vphysics that what it last answered about this object may now be wrong.
//
// Returns whether anything was asked, so a caller — or a test — can tell "there
// was no object" from "the object was told". An entity with no physics object
// has nothing cached about it and there is nothing to invalidate; that is not
// an error and it is the common case during spawn.
//-----------------------------------------------------------------------------
template < typename TPhysicsObject >
inline bool SourceSharpCollisionRulesChanged( TPhysicsObject *pObject )
{
	if ( !pObject )
		return false;

	pObject->RecheckCollisionFilter();
	return true;
}

//-----------------------------------------------------------------------------
// THE SAME RULE WITH AN OFF SWITCH, so the A/B is ONE PROCESS.
//
// `bArmed` is a convar's value at the call site (see shell_standalone_base.cpp,
// sourcesharp_collision_rules_recheck). With it 0 this is the build before the
// fix, exactly; with it 1 it is the build after. Two builds on two boxes at two
// times measure the box as much as the change, and this failure is a coin flip
// -- which is precisely the shape where that matters.
//
// INSTRUMENTATION DEBT, and the removal owner is whoever lands this fix for
// good: once the rate is measured the switch has no reason to exist and the
// call sites should go back to the two-argument form above.
//-----------------------------------------------------------------------------
template < typename TPhysicsObject >
inline bool SourceSharpCollisionRulesChanged( TPhysicsObject *pObject, bool bArmed )
{
	if ( !bArmed )
		return false;

	return SourceSharpCollisionRulesChanged( pObject );
}

#endif // SOURCESHARP_STANDALONE_COLLISION_RULES_H
