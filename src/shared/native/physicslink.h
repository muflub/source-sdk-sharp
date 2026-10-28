//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The physics touch and ground link functions stock wrote as CBaseEntity statics, as
// free functions. The bodies stay in physics_main_shared.cpp beside the link pools.

#pragma once
#define PHYSICSLINK_H



struct touchlink_t;
struct groundlink_t;

// trace_t is a typedef, so it is forward-declared the way icliententity.h does it.
class CGameTrace;
typedef CGameTrace trace_t;

// Not a forward declaration: `class CBaseEntity;` cannot coexist with the alias.


// Touch links. Each reaches its entities only through its arguments.
void PhysicsLink_RemoveTouchedList( IGameEntity *ent );
void PhysicsLink_NotifyOtherOfUntouch( IGameEntity *ent, IGameEntity *other );
void PhysicsLink_RemoveToucher( IGameEntity *other, touchlink_t *link );

// Ground links.
void PhysicsLink_NotifyOtherOfGroundRemoval( IGameEntity *ent, IGameEntity *other );
void PhysicsLink_RemoveGround( IGameEntity *other, groundlink_t *link );
void PhysicsLink_RemoveGroundList( IGameEntity *ent );

// Slides a velocity off an impacted plane. Returns the blocked flags
// (1 = floor, 2 = step / wall).
int Physics_ClipVelocity( const Vector &in, const Vector &normal, Vector &out, float overbounce );

// The last touch trace, stored in physics_main_shared.cpp.
const trace_t &Physics_GetTouchTrace();
