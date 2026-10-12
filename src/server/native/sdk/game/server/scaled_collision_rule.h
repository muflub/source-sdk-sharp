//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — whether a trace may use the solver's scaled hull ===//
//
// One rule, asked once per trace against a CBaseAnimating.
//
// CBaseAnimating::TestCollision has a special case for a model whose scale is
// not 1: the compiled hull in the .phy is the WRONG SIZE for a scaled model,
// and the solver holds a copy that has been scaled to match, so the trace is
// done against the solver's copy instead of against the hitboxes. Stock spells
// the whole decision as `if ( GetModelScale() != 1.0f )` and then dereferences
// two pointers it has not looked at:
//
//     IPhysicsObject *pPhysObject = VPhysicsGetObject();
//     pPhysObject->GetPosition( &vecPosition, &vecAngles );
//     const CPhysCollide *pScaledCollide = pPhysObject->GetCollide();
//
// Both can be NULL. VPhysicsGetObject() is NULL for anything the solver is not
// simulating -- and scale is set on things that have no physics: this port's
// SourceSharp_ShootGib calls SetModelScale on a gib before it knows whether the
// gib got an object at all. GetCollide() is NULL for an object the solver holds
// as a sphere rather than as a hull, which is why every other CollideGetAABB
// caller in the mod tests it.
//
// This is the hot one. TestCollision runs for every trace that reaches the
// entity -- every shot, every player move, every line of sight -- so a NULL
// here is not a rare crash, it is a crash on the next bullet.
//
// The rule is out here rather than inline because the answer to "no solver
// copy" is a DECISION with two plausible readings and only one right one, and a
// decision that lives inside an if-condition is a decision nobody can test.
// See sourcesharp/test/scaled_collision_rule_test.cpp.
//
// No engine headers. It takes three facts and answers one question.
//
//=============================================================================//
#ifndef SCALED_COLLISION_RULE_H
#define SCALED_COLLISION_RULE_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Whether TestCollision should trace against the solver's scaled hull.
//
//   bScaled        the model's scale is not exactly 1
//   bHasObject     VPhysicsGetObject() answered something
//   bHasCollide    that object's GetCollide() answered something
//
// FALSE MEANS FALL THROUGH TO THE UNSCALED PATH, not "nothing was hit". That
// distinction is the whole reason this is a named rule:
//
//   Returning "no hit" for a scaled entity with no solver copy would make a
//   visible, solid thing silently untraceable -- shots pass through it, it
//   blocks no line of sight, it cannot be shot at. That is a wrong answer
//   wearing the shape of a right one, and far harder to find than the crash it
//   replaced. It is also not what the entity deserves: the hitbox and
//   bounding-box tests below the special case are what EVERY unscaled animating
//   entity gets, and they are still there, still correct, merely the wrong size
//   by the scale factor. Wrong by a factor is a bad trace; absent is a ghost.
//
// The three inputs are asked in that order and each is necessary. Scale alone
// was stock's whole test and is the bug. Object without collide still crashes,
// one call later, inside physcollision.
//-----------------------------------------------------------------------------
inline bool ScaledCollisionUsesSolverHull( bool bScaled, bool bHasObject, bool bHasCollide )
{
	return bScaled && bHasObject && bHasCollide;
}

#endif // SCALED_COLLISION_RULE_H
