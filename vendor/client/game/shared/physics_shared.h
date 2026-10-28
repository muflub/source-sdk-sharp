//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

class IPhysics;
class IPhysicsEnvironment;
class IPhysicsSurfaceProps;
class IPhysicsCollision;
class IPhysicsObject;
class CSoundPatch;

extern IPhysicsObject		*g_PhysWorldObject;
extern IPhysics				*physics;
extern IPhysicsCollision	*physcollision;
extern IPhysicsEnvironment	*physenv;
#ifdef PORTAL
extern IPhysicsEnvironment	*physenv_main;
#endif
extern IPhysicsSurfaceProps *physprops;

// Compute enough energy of a reference mass travelling at speed
// makes numbers more intuitive
#define MASS_SPEED2ENERGY(mass, speed)	((speed)*(speed)*(mass))

// energy of a 10kg mass moving at speed
#define MASS10_SPEED2ENERGY(speed)	MASS_SPEED2ENERGY(10,speed)

#define ENERGY_VOLUME_SCALE		(1.0f / 15500.0f)

#define FLUID_TIME_MAX					2.0f // keep track of last time hitting fluid for up to 2 seconds 

// VPHYSICS object game-specific flags
#define FVPHYSICS_DMG_SLICE				0x0001		// does slice damage, not just blunt damage
#define FVPHYSICS_CONSTRAINT_STATIC		0x0002		// object is constrained to the world, so it should behave like a static
#define FVPHYSICS_PLAYER_HELD			0x0004		// object is held by the player, so have a very inelastic collision response
#define FVPHYSICS_PART_OF_RAGDOLL		0x0008		// object is part of a client or server ragdoll
#define FVPHYSICS_MULTIOBJECT_ENTITY	0x0010		// object is part of a multi-object entity
#define FVPHYSICS_HEAVY_OBJECT			0x0020		// HULK SMASH! (Do large damage even if the mass is small)
#define FVPHYSICS_PENETRATING			0x0040		// This object is currently stuck inside another object
#define FVPHYSICS_NO_PLAYER_PICKUP		0x0080		// Player can't pick this up for some game rule reason
#define	FVPHYSICS_WAS_THROWN			0x0100		// Player threw this object
#define FVPHYSICS_DMG_DISSOLVE			0x0200		// does dissolve damage, not just blunt damage
#define FVPHYSICS_NO_IMPACT_DMG			0x0400		// don't do impact damage to anything
#define FVPHYSICS_NO_NPC_IMPACT_DMG		0x0800		// Don't do impact damage to NPC's. This is temporary for NPC's shooting combine balls (sjb)
#define FVPHYSICS_NO_SELF_COLLISIONS	0x8000		// don't collide with other objects that are part of the same entity

// Purpose: A little cache of current objects making noises
struct friction_t
{
	CSoundPatch	*patch;
	CBaseEntity	*pObject;
	float		flLastUpdateTime;
	float		flLastEffectTime;
};

enum
{
	TOUCH_START=0,
	TOUCH_END,
};

struct touchevent_t
{
	CBaseEntity *pEntity0;
	CBaseEntity *pEntity1;
	int			touchType;
	Vector		endPoint;	//sv
	Vector		normal;		//sv
};

void PhysFrictionSound( CBaseEntity *pEntity, IPhysicsObject *pObject, const char *pSoundName, HSOUNDSCRIPTHANDLE& handle, float flVolume );

// Singleton access
IGameSystem* PhysicsGameSystem();
