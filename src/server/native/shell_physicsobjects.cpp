//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — turning things into physics objects ================//
// SS_REASON e  a synchronous mid-step answer
//
// Making an entity physical is managed's (2026-10-12): EntityPhysicsCreation
// parses the model's solid, makes the object through the vphysics vtables and
// decides everything about it; PhysicsConversion is stock's phys_convert. What
// is left here is what the engine forces: the shell HOLDS the object
// (SourceSharp_EntitySwapPhysicsObject, stock's VPhysicsSwapObject) because
// the engine and the shell read that pointer every frame, and the two owner
// lookups physicsowner.h asks mid-callback turn a handle into the shell.
//
//=============================================================================//

#include "shell_standalone_class.h"
#include "sourcesharp.h"
#include "shell_diagnostics.h"
#include "model_types.h"
#include "shell_model_name.h"

#include "precache_model.h"

// The slot table of plan_entity.md §3.3, for the two lookups below. One
// include, and it brings the whole of §3.3 with it -- see slot_table.h.
#include "slot_table.h"
#include "physicslink.h"
#include "physics_bridge_refusal.h"


//=============================================================================
// WHO OWNS A PHYSICS OBJECT (plan_entity.md step 1h, §3.16 family 4)
//
// physicsowner.h declares these two and reads the game data for them; both take
// the HANDLE and never the object, so neither of these bodies calls
// GetGameData and neither of them casts anything. That split is what closes
// cast family 4: the decode is arithmetic in a header, and the only thing that
// turns a handle into an entity is the slot table's serial-checked resolve --
// the one downcast plan_entity.md permits, in shell_entityslots.cpp, allowlisted
// once.
//
// THEY REPLACE A REGISTRY. Until step 1h these were two linear scans over a
// file-scope CUtlVector< CStandaloneEntity * > in shell_standalone_base.cpp,
// which plan.md §1a forbids and which cost a walk per physics callback. The
// object now carries its owner's handle in its own game data, so the answer is
// an array read.
//
// A STALE HANDLE ANSWERS NULL. That is the whole point of routing through
// Resolve rather than an index: the entity died, its entry was reused, and the
// serial says so. Answering with the entry's new occupant would hand a
// VPhysics callback about a dead crate to whatever spawned in its place.
//=============================================================================

IServerEntity *PhysicsStandaloneOwner( const CBaseHandle &handle )
{
	return SourceSharp_ShellFromHandle( handle );
}

//-----------------------------------------------------------------------------
// The same owner, as the thing that walked into something.
//
// Built by the concrete type rather than by physicsowner.h, and that has not
// changed with the registry: a toucher carries the handle AS WELL AS the
// interface, and only CStandaloneEntity has both to give.
//-----------------------------------------------------------------------------
Toucher_t PhysicsStandaloneToucher( const CBaseHandle &handle )
{
	CStandaloneEntity *pShell = SourceSharp_ShellFromHandle( handle );

	if ( pShell )
		return pShell->AsToucher();

	Toucher_t none = { NULL, NULL, NULL };
	return none;
}

//-----------------------------------------------------------------------------
// The shell holds the object managed made (EntityPhysicsCreation), or lets go
// of one (NULL). Stock's VPhysicsSwapObject: the previous object is answered,
// never destroyed -- that is the caller's to decide. 0 for a handle that names
// no shell, which then holds nothing.
//-----------------------------------------------------------------------------
int32_t SourceSharp_EntitySwapPhysicsObject( uint32_t hEntity, void *pObject, void **ppPrevious )
{
	if ( ppPrevious )
		*ppPrevious = NULL;

	CStandaloneEntity *pShell = SourceSharp_ResolveStandalone( hEntity );
	if ( !pShell )
		return 0;

	IPhysicsObject *pPrevious = pShell->SwapPhysics( static_cast< IPhysicsObject * >( pObject ) );
	if ( ppPrevious )
		*ppPrevious = pPrevious;

	return 1;
}

//=============================================================================//
// CONVERSION IS MANAGED'S (2026-10-12). phys_convert turning scenery into a
// simple physics entity is stock's CPhysConvert / CreateSimplePhysicsObject /
// TransferPhysicsObject, ported in SourceSharp.Server's PhysicsConversion; the
// object it moves between shells crosses through
// SourceSharp_EntitySwapPhysicsObject above. The native stub that stood here
// answered 0 for every entity (its SDK arm was always NULL).
//=============================================================================//

//=============================================================================//
// THE TWO LINK-LIST TEARDOWNS, ARMED. REFUSED, AND NOTHING IS LOST BY THE FIRST.
//
// Both are declared in game/shared/physicslink.h and both are DEFINED in
// game/shared/physics_main_shared.cpp, which the armed build does not compile.
// That file is also the only producer of the two pools they drain -- the touch
// links and the ground links -- and the only place their allocator, their free
// helpers and their cleanup flag exist. Swept across the whole tree, nothing
// else names either pool. So armed both lists are empty by construction: not
// "probably empty", but incapable of having an element put in them.
//
// They are still CALLED. The map loader's edict teardown calls both, and so
// does the client-disconnect path, and both of those the engine drives -- so a
// definition has to exist. What it must not do is pretend to have drained
// something.
//
// GUARDED TO ARMED ONLY, because unarmed the real bodies are compiled.
//=============================================================================//
