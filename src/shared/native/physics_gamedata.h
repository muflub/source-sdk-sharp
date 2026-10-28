//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// VPhysics game data holds an entity's packed handle plus one, so NULL still means
// "no owner" (handle 0 is the world's). The top bit tells a handle from an SDK
// CBaseEntity pointer: no user-space pointer sets it on either target.
#pragma once

#include <stddef.h>
#include <stdint.h>

#include "basehandle.h"

// The top bit of a pointer-sized word, so a 32-bit build tags bit 31.
#define SOURCESHARP_PHYSICS_GAMEDATA_TAG \
	( (uintptr_t)1 << ( sizeof( uintptr_t ) * 8 - 1 ) )

// The +1 and the -1 must move together.
#define SOURCESHARP_PHYSICS_GAMEDATA_BIAS ( (uintptr_t)1 )

// NONE: no owner. HANDLE: resolved only through the slot table, and a stale handle
// answers NULL. POINTER: an SDK CBaseEntity *.
enum PhysicsGameDataKind_t
{
	PHYSICS_GAMEDATA_NONE = 0,
	PHYSICS_GAMEDATA_HANDLE,
	PHYSICS_GAMEDATA_POINTER,
};

inline PhysicsGameDataKind_t PhysicsGameDataKindOf( const void *pGameData )
{
	const uintptr_t word = (uintptr_t)pGameData;

	if ( word == 0 )
		return PHYSICS_GAMEDATA_NONE;

	if ( word & SOURCESHARP_PHYSICS_GAMEDATA_TAG )
		return PHYSICS_GAMEDATA_HANDLE;

	return PHYSICS_GAMEDATA_POINTER;
}

inline bool PhysicsGameDataIsHandle( const void *pGameData )
{
	return PhysicsGameDataKindOf( pGameData ) == PHYSICS_GAMEDATA_HANDLE;
}

// An invalid handle stores NULL: encoded, 0xFFFFFFFF would decode to a real entry.
inline void *PhysicsGameDataFromHandle( const CBaseHandle &handle )
{
	if ( !handle.IsValid() )
		return NULL;

	const uintptr_t word = (uintptr_t)(uint32)handle.ToInt()
	                     + SOURCESHARP_PHYSICS_GAMEDATA_BIAS;

	return (void *)( word | SOURCESHARP_PHYSICS_GAMEDATA_TAG );
}

// The inverse. NULL and SDK pointers alike answer an invalid handle.
inline CBaseHandle PhysicsHandleFromGameData( const void *pGameData )
{
	if ( !PhysicsGameDataIsHandle( pGameData ) )
		return CBaseHandle();

	const uintptr_t word = ( (uintptr_t)pGameData & ~SOURCESHARP_PHYSICS_GAMEDATA_TAG )
	                     - SOURCESHARP_PHYSICS_GAMEDATA_BIAS;

	return CBaseHandle::UnsafeFromIndex( (int)(uint32)word );
}
