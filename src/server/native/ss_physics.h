//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — physics.cpp's own records and entry points
//
// plan_server_novalve.md P5. Replaces game/server/physics.h, and the collision
// bookkeeping of game/shared/physics_shared.h, for server.so: physics.cpp is
// its compiled includer on the server (the collision event handler's header
// left with the handler, plan_physics_bones_native_zero 2.1).
//
// The records are server-only. VPhysics hands the game a vcollisionevent_t and
// calls back through the three solver interfaces; everything below is what
// physics.cpp keeps for itself between those calls, and no other object reads
// it by layout (nm: physics.cpp exports PhysicsGameSystem,
// PhysicsWorldOwner and the named PhysFrictionSound, none of which names a
// record here). So the records are ss:: types, per the plan's naming rule.
//
// Only what physics.cpp uses ahead of its definition, or that the collision
// event handler names, is declared.
//=============================================================================//

#ifndef SOURCESHARP_SS_PHYSICS_H
#define SOURCESHARP_SS_PHYSICS_H
#ifdef _WIN32
#pragma once
#endif

#include "vphysics_interface.h"
#include "basehandle.h"
#include "string_t.h"
#include "mathlib/vector.h"

#include "igameentity_fwd.h"
class IServerEntity;
class IServerNetworkable;
class CSoundPatch;
class IPhysicsFluidController;
struct PhysicsOwner_t;
struct vehicleparams_t;
struct vehiclesounds_t;

namespace ss
{

// An object this heavy or heavier counts as large in a damage exchange.
const float VPHYSICS_LARGE_OBJECT_MASS = 500.0f;

// VPhysics' collision event with the game's per-pair state appended.
struct gamevcollisionevent_t : public vcollisionevent_t
{
	Vector			preVelocity[ 2 ];
	Vector			postVelocity[ 2 ];
	AngularImpulse	preAngularVelocity[ 2 ];
	IGameEntity		*pEntities[ 2 ];

	// Takes VPhysics' half, clears the entities.
	void Init( vcollisionevent_t *pEvent )
	{
		*static_cast< vcollisionevent_t * >( this ) = *pEvent;
		pEntities[ 0 ] = nullptr;
		pEntities[ 1 ] = nullptr;
	}
};

// A map's mass-center override for one named entity.
struct masscenteroverride_t
{
	enum align_type
	{
		ALIGN_POINT = 0,
		ALIGN_AXIS = 1,
	};

	Vector		center;
	Vector		axis;
	int			alignType;
	string_t	entityName;
};

} // namespace ss

// VPhysics' collision solver, collision events and object events are managed's
// own tables (plan_physics_managed B2.1/B2.2, plan_physics_bones_native_zero
// 2.1): physics.cpp keeps no handler of its own.

// Defined in physics.cpp, used there ahead of their definitions.
void PhysCollisionScreenShake( ss::gamevcollisionevent_t *pEvent, int index );
void PhysCollisionDust( ss::gamevcollisionevent_t *pEvent, surfacedata_t *phit );
void PhysFrictionSound( IGameEntity *pEntity, IPhysicsObject *pObject, float energy, int surfaceProps,
                        int surfacePropsHit );

#endif // SOURCESHARP_SS_PHYSICS_H
