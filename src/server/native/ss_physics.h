//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// physics.cpp's own records and entry points, as server.so's ss:: types. No other object
// reads these records by layout.

#pragma once

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

// Defined in physics.cpp, used there ahead of their definitions.
void PhysCollisionScreenShake( ss::gamevcollisionevent_t *pEvent, int index );
void PhysCollisionDust( ss::gamevcollisionevent_t *pEvent, surfacedata_t *phit );
void PhysFrictionSound( IGameEntity *pEntity, IPhysicsObject *pObject, float energy, int surfaceProps,
                        int surfacePropsHit );
