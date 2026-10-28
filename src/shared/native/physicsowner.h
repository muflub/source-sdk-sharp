//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Who owns a physics object, and what can be asked of them. The game data is NULL,
// a tagged entity handle (physics_gamedata.h), or an SDK entity pointer; a stale
// handle resolves to no owner, never to the entry's new occupant.
#pragma once

#ifdef CLIENT_DLL
#include "client_entity.h"
#endif

#ifndef CLIENT_DLL
#include "igameentity.h"
#include "standalone_relays.h"
#include "const.h"
#include "mathlib/mathlib.h"
#include "bspflags.h"
#include "iservernetworkable.h"
#include "iserverentity.h"
#include "engine/ICollideable.h"
#else
// The client has no parallel hierarchy; these exist so shared code compiles.
class IGameEntity;
class IServerEntity;
#endif

#include "vphysics_interface.h"

#include "vphysics/constraints.h"

#include "physics_gamedata.h"

class CWorld;

// Built per lookup and dropped; never store one across a frame.
struct PhysicsOwner_t
{
	// The SDK entity, for the machinery only it has.
	IGameEntity *pBase;

	// The entity outside the SDK. Always NULL on the client.
	IGameEntity *pGame;

	// worldspawn's own object only; always NULL on the client.
	CWorld *pWorld;

	// The same entity's engine-facing interface, set with pGame.
	IServerEntity *pServer;

	// The object this owner was looked up from; IGameEntity has no VPhysicsGetObject.
	IPhysicsObject *pObject;

	bool IsValid() const { return pBase != NULL || pGame != NULL || pWorld != NULL; }

	bool IsWorld() const { return pWorld != NULL; }

	// A stable per-owner identity for keyed uses such as the collision-disable pair hash.
	void *Key() const
	{
		if ( pBase )
			return static_cast<void *>( pBase );
#ifndef CLIENT_DLL
		if ( pWorld )
			return static_cast<void *>( pWorld );
#endif
#ifndef CLIENT_DLL
		return static_cast<void *>( pGame );
#else
		return NULL;
#endif
	}

	// The owner as the engine's collision code takes it, for a trace filter or a handle key.
	IHandleEntity *ToHandleEntity() const
	{
#ifndef CLIENT_DLL
		if ( pServer )
			return pServer;
#endif
		return NULL;
	}

	const char *GetClassname() const
	{
#ifndef CLIENT_DLL
		if ( pServer )
			return pServer->GetNetworkable()->GetClassName();
#endif
		return "";
	}

	// The mapper's name, or the classname when it has none, as CBaseEntity::GetDebugName.
	const char *GetDebugName() const
	{
#ifndef CLIENT_DLL
		if ( pGame )
		{
			const char *pName = StandaloneEntity_Name( pGame );
			return ( pName && pName[0] ) ? pName : pServer->GetNetworkable()->GetClassName();
		}
#endif
		return "";
	}

	int GetCollisionGroup() const
	{
		if ( pBase )
			return pBase->GetCollisionGroup();
#ifndef CLIENT_DLL
		if ( pGame )
			return pGame->GetCollisionGroup();
#endif
		return COLLISION_GROUP_NONE;
	}

	const Vector &GetAbsOrigin() const
	{
#ifndef CLIENT_DLL
		if ( pServer )
			return pServer->GetCollideable()->GetCollisionOrigin();
#endif
		return vec3_origin;
	}

	bool IsPlayer() const
	{
#ifndef CLIENT_DLL
		if ( pGame )
			return StandaloneEntity_IsPlayer( pGame );
#endif
		return false;
	}

	int entindex() const
	{
#ifndef CLIENT_DLL
		if ( pServer )
		{
			// -1 for an entity with no edict; otherwise the entry index is the edict index.
			if ( !pServer->GetNetworkable()->GetEdict() )
				return -1;
			return pServer->GetRefEHandle().GetEntryIndex();
		}
#endif
		return -1;
	}

	MoveType_t GetMoveType() const
	{
#ifndef CLIENT_DLL
		if ( pGame )
			return (MoveType_t)StandaloneEntity_MoveType( pGame );
#endif
		return MOVETYPE_NONE;
	}

	bool IsNPC() const { return false; }

	bool CanTakeDamage() const { return true; }

	// Always plain water: entities outside the SDK track no water level.
	int GetWaterType() const { return 0; }

	// The solver is the only thing that moves an entity on the parallel hierarchy.
	bool IsVPhysicsSimulated() const { return ( pGame != NULL ); }

	// The object this owner was looked up from; a ragdoll's first object, as stock.
	bool IsVPhysicsMoveable() const { return pObject && pObject->IsMoveable(); }

	// Read from the entity's collideable, as stock's ShouldCollide does.
	SolidType_t GetSolid() const
	{
#ifndef CLIENT_DLL
		if ( pServer )
			return (SolidType_t)pServer->GetCollideable()->GetSolid();
#endif
		return SOLID_VPHYSICS;
	}

	int GetSolidFlags() const
	{
#ifndef CLIENT_DLL
		if ( pServer )
			return pServer->GetCollideable()->GetSolidFlags();
#endif
		return 0;
	}

	unsigned int PhysicsSolidMaskForEntity() const
	{
		// CBasePlayer's override: a player also treats player clips as solid.
		if ( IsPlayer() )
			return (unsigned int)MASK_PLAYERSOLID;

		return (unsigned int)MASK_SOLID;
	}

#ifndef CLIENT_DLL
	// Whether this entity forces a collision the ordinary rules would skip
	// (func_clip_vphysics). Nothing in this build does.
	bool ForcesCollisionWith( const PhysicsOwner_t &other ) const { return false; }
#endif

	void VPhysicsUpdate( IPhysicsObject *pObject ) const
	{
		if ( pBase )
		{
			pBase->VPhysicsUpdate( pObject );
			return;
		}
#ifndef CLIENT_DLL
		if ( pGame )
			pGame->VPhysicsUpdate( pObject );
#endif
	}

#ifdef CLIENT_DLL
	// Toucher_t is server-only, so the client has no touch path.
	void StartTouch( const PhysicsOwner_t &other ) const { (void)other; }

	void EndTouch( const PhysicsOwner_t &other ) const { (void)other; }
#else
	// Defined at the end of this file: toucher.h includes this header.
	void StartTouch( const PhysicsOwner_t &other ) const;
	void EndTouch( const PhysicsOwner_t &other ) const;

	// This owner seen as the thing that walked in.
	Toucher_t AsToucher() const;
#endif
};

#if !defined( CLIENT_DLL )
// The entity a game-data handle names, or NULL when the handle is invalid, unbound,
// or stale (the entry has been reused).
IServerEntity *PhysicsStandaloneOwner( const CBaseHandle &handle );

// The owner as a toucher, built by the shell because only the concrete type has both
// the handle and the interface.
struct Toucher_t;
Toucher_t PhysicsStandaloneToucher( const CBaseHandle &handle );
#endif

#if !defined( CLIENT_DLL )
// The owner a handle names, with no object, for queues that outlive the solver
// callback. A stale handle answers no owner.
inline PhysicsOwner_t PhysicsOwnerOfHandle( const CBaseHandle &handle, IPhysicsObject *pObject = NULL )
{
	PhysicsOwner_t owner = { NULL, NULL, NULL, NULL, pObject };

	owner.pServer = PhysicsStandaloneOwner( handle );

	if ( owner.pServer )
		owner.pGame = owner.pServer->GetBaseEntity();

	return owner;
}
#endif

inline PhysicsOwner_t PhysicsOwnerOfGameData( void *pGameData, IPhysicsObject *pObject )
{
	PhysicsOwner_t owner = { NULL, NULL, NULL, NULL, pObject };

#if !defined( CLIENT_DLL )
	if ( PhysicsGameDataIsHandle( pGameData ) )
	{
		owner = PhysicsOwnerOfHandle( PhysicsHandleFromGameData( pGameData ), pObject );
		return owner;
	}
#else
	// A client ragdoll a managed entity owns carries a tagged handle; managed answers
	// for it, so it has no SDK owner here. Never cast the word to IGameEntity.
	if ( PhysicsGameDataIsHandle( pGameData ) )
		return owner;
#endif

	owner.pBase = static_cast<IGameEntity *>( pGameData );
	return owner;
}

inline PhysicsOwner_t PhysicsOwnerOf( IPhysicsObject *pObject )
{
	if ( !pObject )
	{
		PhysicsOwner_t none = { NULL, NULL, NULL, NULL, NULL };
		return none;
	}

	return PhysicsOwnerOfGameData( pObject->GetGameData(), pObject );
}

// A constraint's own game data, read like an object's. Nothing sets it today, so
// every constraint answers no owner.
inline PhysicsOwner_t PhysicsConstraintOwner( IPhysicsConstraint *pConstraint )
{
	if ( !pConstraint )
	{
		PhysicsOwner_t none = { NULL, NULL, NULL, NULL, NULL };
		return none;
	}

	return PhysicsOwnerOfGameData( pConstraint->GetGameData(), NULL );
}

#ifndef CLIENT_DLL
#include "toucher.h"

// An owner of neither kind is a toucher of neither.
inline Toucher_t PhysicsOwner_t::AsToucher() const
{
	// pBase and pGame have the same type: the entity goes third, or no touches arrive.
	if ( pBase )
	{
		Toucher_t toucher = { NULL, NULL, pBase };
		return toucher;
	}

	// Decoded again from the object rather than stored on this aggregate.
	if ( pGame && pObject )
		return PhysicsStandaloneToucher( PhysicsHandleFromGameData( pObject->GetGameData() ) );

	// A buffered contact touch has no object; the handle names the same entity.
	if ( pGame && pServer )
		return PhysicsStandaloneToucher( pServer->GetRefEHandle() );

	Toucher_t none = { NULL, NULL, NULL };
	return none;
}

inline void PhysicsOwner_t::StartTouch( const PhysicsOwner_t &other ) const
{
	const Toucher_t toucher = other.AsToucher();

	if ( pGame )
		pGame->StartTouch( toucher );
}

inline void PhysicsOwner_t::EndTouch( const PhysicsOwner_t &other ) const
{
	const Toucher_t toucher = other.AsToucher();

	if ( pGame )
		pGame->EndTouch( toucher );
}
#endif
