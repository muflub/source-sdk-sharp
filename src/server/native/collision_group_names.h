// Maps SourceSharp's collision group names to the engine's Collision_Group_t and
// back, through one table. The engine's numbers come from the .cpp that includes
// const.h, so this header stays engine-free.

#pragma once

// Must match the CollisionGroup enum in SourceSharp.Abi/ScriptedEntity.cs.
enum SourceSharpCollisionGroup_t
{
	SS_COLLISION_GROUP_NONE         = 0,
	SS_COLLISION_GROUP_VEHICLE_CLIP = 1,
	SS_COLLISION_GROUP_WEAPON       = 2,

	// A breaking prop in the neutral group hands its pieces INTERACTIVE, as stock's
	// PropBreakableCreateAll does, so they don't collide with the ejected debris.
	SS_COLLISION_GROUP_DEBRIS       = 3,
	SS_COLLISION_GROUP_INTERACTIVE  = 4,
	SS_COLLISION_GROUP_PROJECTILE   = 5,
	SS_COLLISION_GROUP_BREAKABLE_GLASS = 6,
};

// The first row of a table is the neutral one, and every lookup falls back to it.
struct CollisionGroupName_t
{
	int nSourceSharp;
	int nEngine;
};

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

// Many-to-one: an engine group with no name here reads as the neutral one.
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
