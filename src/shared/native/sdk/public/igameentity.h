//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// What game code needs from an entity that is not a CBaseEntity. The SDK's trace
// filter tries the CBaseEntity path first and asks this only when that answers NULL.
// Members are what the engine initiates; game-code questions go through relays.
#pragma once

#include "mathlib/vector.h"
#include "igameentity_fwd.h"
#include "ihandleentity.h"

class IPhysicsObject;

// Declared, not included: its definition is in a server header below this one.
struct Toucher_t;

// On the server, built on stock public headers, SS_GAME_ENTITY names this CBaseEntity,
// the name stock headers forward-declare. The client keeps IGameEntity.
#if defined( SS_GAME_ENTITY ) && defined( CLIENT_DLL )
// Stock headers convert C_BaseEntity to IHandleEntity. No client object implements
// this class, so the base costs nothing.
abstract_class SS_GAME_ENTITY : public IHandleEntity
#elif defined( SS_GAME_ENTITY )
abstract_class SS_GAME_ENTITY
#else
abstract_class IGameEntity
#endif
{
public:
	// What CTraceFilterSimple asks an entity for.
	virtual bool ShouldCollide( int collisionGroup, int contentsMask ) const = 0;
	virtual int  GetCollisionGroup() const = 0;

	// Deliberately absent: the engine never asks these, so other interfaces, relays or
	// the SDK class answer them. VPhysicsGetObject, GetMoveType, GetFlags, EyePosition,
	// GetAbsVelocity, IsEFlagSet, AddEFlags, GetTeamNumber, m_takedamage, m_debugOverlays,
	// SetTransmit, GetMoveParent.

	// Touch is an overlap report, renewed every tick the two overlap. StartTouch and
	// EndTouch are the edges VPhysics triggers report exactly once each.
	virtual void Touch( const Toucher_t &toucher ) = 0;
	virtual void StartTouch( const Toucher_t &toucher ) = 0;
	virtual void EndTouch( const Toucher_t &toucher ) = 0;

	// The solver's once-a-tick report of the object it moved.
	virtual void VPhysicsUpdate( IPhysicsObject *pObject ) = 0;

protected:
#ifdef SS_GAME_ENTITY
	virtual ~SS_GAME_ENTITY() {}
#else
	virtual ~IGameEntity() {}
#endif
};
