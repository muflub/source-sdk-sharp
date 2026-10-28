//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

class IPhysicsObject;
class IPhysicsConstraint;
class IPhysicsConstraintGroup;
class IPhysicsCollision;
class IPhysicsEnvironment;
class IPhysicsSurfaceProps;
struct matrix3x4_t;

struct vcollide_t;
struct studiohdr_t;
class CStudioHdr;
class CBoneAccessor;

#include "mathlib/vector.h"
#include "bone_accessor.h"

// UNDONE: Remove and make dynamic?
#define RAGDOLL_MAX_ELEMENTS	24
#define RAGDOLL_INDEX_BITS		5			// NOTE 1<<RAGDOLL_INDEX_BITS >= RAGDOLL_MAX_ELEMENTS

struct ragdollelement_t
{
	Vector				originParentSpace;
	IPhysicsObject		*pObject;		// all valid elements have an object
	IPhysicsConstraint	*pConstraint;	// all valid elements have a constraint (except the root)
	int					parentIndex;
};

struct ragdollanimatedfriction_t
{
	float					flFrictionTimeIn;
	float					flFrictionTimeOut;
	float					flFrictionTimeHold;
	int						iMinAnimatedFriction;
	int						iMaxAnimatedFriction;
};

struct ragdoll_t
{
	int					listCount;
	bool					allowStretch;
	bool					unused;
	IPhysicsConstraintGroup *pGroup;
	// store these in separate arrays for save/load
	ragdollelement_t 	list[RAGDOLL_MAX_ELEMENTS];
	int					boneIndex[RAGDOLL_MAX_ELEMENTS];
	ragdollanimatedfriction_t animfriction;
};
