//========= SourceSharp — the collision groups this mod has names for ========//
//
// Collision_Group_t is thirty positional entries deep and mostly names things
// this mod has none of, so C# does not learn its numbers: it has three names of
// its own and this is where the two vocabularies meet. The rule was already
// here, spelt as a switch inside SourceSharp_EntitySetCollisionGroup; it moved
// out when the question started being asked in BOTH directions.
//
// Both directions matter and neither is decoration:
//
//   to the engine    a managed brush saying "vehicle clip" and meaning it
//   from the engine  a projectile asking whether the trigger it just flew into
//                    is a dropped weapon, which stock's CMissile::MissileTouch
//                    exempts by name ("don't touch triggers, but DO hit
//                    weapons")
//
// ONE table read two ways, rather than two switches. A pair of switches is the
// bug waiting to happen: edit one and a group is written as one thing and read
// back as another, with nothing anywhere to say so. Read one table both ways
// and the disagreement cannot be written down.
//
// No engine header here, deliberately, and it is what lets the rules be tested:
// const.h drags in the whole tier0 chain, so the ENGINE's numbers are supplied
// by the one .cpp that has them and the lookup lives where a test binary can
// reach it. Same shape as collideable_center_math.h.
//
//=============================================================================//

#pragma once

//-----------------------------------------------------------------------------
// SourceSharp's numbering. MUST match the CollisionGroup enum in
// SourceSharp.Abi/ScriptedEntity.cs — CollisionGroupParityTests reads this file
// and asserts it does, rather than leaving the two to be kept in step by hand.
//-----------------------------------------------------------------------------
enum SourceSharpCollisionGroup_t
{
	SS_COLLISION_GROUP_NONE         = 0,
	SS_COLLISION_GROUP_VEHICLE_CLIP = 1,
	SS_COLLISION_GROUP_WEAPON       = 2,

	// The two a BREAKING PROP needs, and the reason this list grew.
	//
	// Debris is the one that is visible in play: it is what stops the pieces of
	// a smashed crate from colliding with the player who smashed it, and with
	// each other. Without a name for it every gib came out in the neutral group
	// and shoved the player about — which is not a cosmetic difference, it is a
	// prop that can wedge a door.
	//
	// Interactive comes with it rather than after it, because it is the OTHER
	// half of one rule. Stock's PropBreakableCreateAll defaults a piece to the
	// breaking prop's own group, "don't automatically make anything
	// COLLISION_GROUP_NONE or it will collide with debris being ejected by
	// breaking" — so a prop in the neutral group hands its pieces INTERACTIVE.
	// A build that knew only Debris would fall that case back to neutral and
	// reintroduce exactly the collision the comment is about.
	SS_COLLISION_GROUP_DEBRIS       = 3,
	SS_COLLISION_GROUP_INTERACTIVE  = 4,
	SS_COLLISION_GROUP_PROJECTILE   = 5,
	SS_COLLISION_GROUP_BREAKABLE_GLASS = 6,
};

//-----------------------------------------------------------------------------
// One name, in both vocabularies.
//
// The FIRST row of a table is the neutral one, and every lookup below falls
// back to it. That is not a convention to remember: it is how "the engine has
// twenty-seven groups this mod cannot name" gets an answer at all, and the
// fallback has to be a row of the table so both directions fall back to the
// same pair.
//-----------------------------------------------------------------------------
struct CollisionGroupName_t
{
	int nSourceSharp;
	int nEngine;
};

//-----------------------------------------------------------------------------
// A name this mod has, as the engine's number.
//-----------------------------------------------------------------------------
inline int CollisionGroup_ToEngine( int nGroup, const CollisionGroupName_t *pNames, int nCount )
{
	if ( !pNames || nCount <= 0 )
		return 0;

	for ( int i = 0; i < nCount; ++i )
	{
		if ( pNames[i].nSourceSharp == nGroup )
			return pNames[i].nEngine;
	}

	return pNames[0].nEngine;
}

//-----------------------------------------------------------------------------
// The same, read back. Many-to-one on purpose: an engine group this mod has no
// name for reads as the neutral one, which is what "it is not one of the ones
// we care about" honestly means.
//-----------------------------------------------------------------------------
inline int CollisionGroup_FromEngine( int nEngineGroup, const CollisionGroupName_t *pNames, int nCount )
{
	if ( !pNames || nCount <= 0 )
		return 0;

	for ( int i = 0; i < nCount; ++i )
	{
		if ( pNames[i].nEngine == nEngineGroup )
			return pNames[i].nSourceSharp;
	}

	return pNames[0].nSourceSharp;
}
