//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — physics game data is the handle =====================//
//
// plan_entity.md §3.3: "Physics game data is the handle too. IPhysicsObject::
// SetGameData takes a void *; it is given the packed handle plus one (so a null
// still means 'no entity'), never a pointer. Every VPhysics callback then
// resolves through the slot table."  This file is that encoding, and nothing
// else: the arithmetic, the boundary, and the one question a reader of game
// data has to ask before it does anything with the value.
//
// It is a HEADER OF ITS OWN rather than a block inside physicsowner.h for one
// reason: physicsowner.h includes cbase.h, so nothing that includes it can be
// tested without an engine. The rules below are arithmetic on a CBaseHandle and
// they are exactly the part that has a boundary to get wrong, so they compile
// against basehandle.h alone and physics_gamedata_test.cpp compiles against
// them the same way slot_table_test.cpp compiles against the table.
//
//=============================================================================
// WHY PLUS ONE
//=============================================================================
//
// VPhysics game data of NULL means "no owner" — every reader in this tree tests
// for it, and IPhysicsObject::GetGameData answers NULL for an object nobody
// claimed. A handle of zero is a REAL handle: entry 0, serial 0, which is the
// world's on the client on every map load (see slot_table.h's note on serial 0
// and entitylist_slot0_test.cpp). Storing a raw packed handle would therefore
// give exactly one entity per map the same game data as "nobody", and the
// failure would be a silent skip rather than a crash.
//
// So the stored value is the packed handle PLUS ONE, and NULL is reserved. The
// pairing is one add and one subtract and it is the whole of the boundary:
//
//     handle (0,0)  ->  game data 1        (NOT NULL — this is the point)
//     game data 0   ->  invalid handle     ("no owner")
//
// physics_gamedata_test.cpp pins both directions, and removing either the +1 or
// the -1 turns it red.
//
//=============================================================================
// WHY A TAG BIT, AND WHEN IT GOES
//=============================================================================
//
// §3.3's sentence ends "never a pointer", and that is the END STATE: once
// CBaseEntity is gone (Phase 4) the only thing anybody stores in game data is a
// handle and there is nothing to tell apart. THAT IS NOT TRUE YET. In this tree
// the SDK still creates physics objects for the entity classes that survive —
// prop_ragdoll and physics_prop_ragdoll (physics_prop_ragdoll.cpp:57-58), gib
// and raggib (gib.cpp:720,768), the NPC solvers — and physics_shared.cpp still
// writes `solid.params.pGameData = static_cast<void *>(pEntity)` for every one
// of them. So during Phases 1-4 a game-data word holds EITHER a packed handle
// or a real CBaseEntity pointer, and PhysicsOwnerOf has to know which.
//
// A RANGE CHECK WOULD BE A GUESS. A packed handle plus one is at most
// (NUM_ENT_ENTRIES-1) | (serial << NUM_SERIAL_NUM_SHIFT_BITS) plus one, which
// is under 2^31 — a range a pointer can land in on any target whose allocator
// hands out low addresses (a non-PIE main binary's brk heap starts a few
// megabytes up). Guessing wrong there does not fail loudly: it casts a handle
// to CBaseEntity * and dereferences it.
//
// So the discriminator is EXACT and it is one bit: the TOP bit of a pointer-
// sized word, which no user-space allocator on either target can return.
// x86-64 user space is the canonical LOW half (bits 63..47 clear, so bit 63 is
// never set in a user pointer); 32-bit Linux user space is the low 3GB, so bit
// 31 is never set either. The bit is therefore free by the ABI rather than by
// observation, and IsHandle() is a mask instead of a comparison.
//
// WHAT DELETES IT. When Phase 4 removes the last CBaseEntity that owns a
// physics object, PhysicsOwnerOf's pBase arm goes and there is nothing left to
// discriminate against. At that point SOURCESHARP_PHYSICS_GAMEDATA_TAG becomes
// 0 and every function below still compiles and still round-trips — the tests
// in physics_gamedata_test.cpp are written against the round trip and the
// boundary, not against the bit, so the deletion is a one-line change with the
// coverage already in place.
//
//=============================================================================
#ifndef SOURCESHARP_PHYSICS_GAMEDATA_H
#define SOURCESHARP_PHYSICS_GAMEDATA_H
#ifdef _WIN32
#pragma once
#endif

#include <stddef.h>
#include <stdint.h>

#include "basehandle.h"

//-----------------------------------------------------------------------------
// The tag. The top bit of a pointer-sized word, derived from the word's own
// width rather than written as 63 so that a 32-bit build tags bit 31 and not a
// shift that is undefined there.
//-----------------------------------------------------------------------------
#define SOURCESHARP_PHYSICS_GAMEDATA_TAG \
	( (uintptr_t)1 << ( sizeof( uintptr_t ) * 8 - 1 ) )

// The bias. Named rather than spelled `1` at four sites, because the +1 and the
// -1 have to move together and a lone literal is how they stop.
#define SOURCESHARP_PHYSICS_GAMEDATA_BIAS ( (uintptr_t)1 )

//-----------------------------------------------------------------------------
// WHICH ARM A GAME-DATA WORD SELECTS.
//
// Three answers and not two, because "no owner" and "an owner this build cannot
// name" are different things and a caller that folds them together is the bug
// physicsowner.h's header block describes.
//
//   NONE     the word is NULL. Nobody owns this object -- an object the solver
//            made for itself, or one whose entity cleared its game data.
//   HANDLE   the word is one of ours. It is answered ONLY through the slot
//            table, and a stale handle answers NULL: never the entry's new
//            occupant, and never a pointer.
//   POINTER  the word is an SDK CBaseEntity *. Phase 4 deletes this arm.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// HANDLE -> GAME DATA.
//
// An INVALID handle stores NULL, and that is not a shortcut: CBaseHandle::
// ToInt() answers INVALID_EHANDLE_INDEX (0xFFFFFFFF) for one, which plus the
// bias is a perfectly ordinary tagged word that would decode back to a handle
// whose GetEntryIndex() is NUM_ENT_ENTRIES-1 by basehandle.h's own
// bug-submarining. Storing NULL says "no owner", which is what an entity with
// no handle is.
//-----------------------------------------------------------------------------
inline void *PhysicsGameDataFromHandle( const CBaseHandle &handle )
{
	if ( !handle.IsValid() )
		return NULL;

	const uintptr_t word = (uintptr_t)(uint32)handle.ToInt()
	                     + SOURCESHARP_PHYSICS_GAMEDATA_BIAS;

	return (void *)( word | SOURCESHARP_PHYSICS_GAMEDATA_TAG );
}

//-----------------------------------------------------------------------------
// GAME DATA -> HANDLE. The inverse, and it answers an INVALID handle for
// anything that is not one of ours -- NULL and an SDK pointer alike, because
// the only correct thing to do with a pointer here is to not decode it.
//
// The mask before the subtract is what keeps the tag out of the arithmetic; the
// (uint32) is what keeps a 64-bit word from carrying anything a CBaseHandle
// cannot hold.
//-----------------------------------------------------------------------------
inline CBaseHandle PhysicsHandleFromGameData( const void *pGameData )
{
	if ( !PhysicsGameDataIsHandle( pGameData ) )
		return CBaseHandle();

	const uintptr_t word = ( (uintptr_t)pGameData & ~SOURCESHARP_PHYSICS_GAMEDATA_TAG )
	                     - SOURCESHARP_PHYSICS_GAMEDATA_BIAS;

	return CBaseHandle::UnsafeFromIndex( (int)(uint32)word );
}

#endif // SOURCESHARP_PHYSICS_GAMEDATA_H
