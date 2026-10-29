//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON e  a synchronous mid-step answer

// Physics object ownership: the shell holds the object managed made, and the
// owner lookups physicsowner.h asks mid-callback turn a handle into the shell.

#include "shell_standalone_class.h"

#include "slot_table.h"
#include "server_physics.h"
#include "server_entity_resolver.h"

// Both take the handle, never the object. A stale handle answers NULL, so a
// callback about a dead entity never reaches whatever reused its slot.

IServerEntity* PhysicsStandaloneOwner(CServerEntityResolver& entities, const CBaseHandle& handle)
{
	return entities.ShellFromHandle(handle);
}

// The shell takes the object managed made, or lets go of one (NULL). As stock's
// VPhysicsSwapObject, the previous object is returned, never destroyed. 0 for a
// handle that names no shell.
int32_t CServerPhysics::EntitySwapPhysicsObject(uint32_t hEntity, void* pObject, void** ppPrevious)
{
	if (ppPrevious)
		*ppPrevious = NULL;

	CStandaloneEntity* pShell = m_Resolver.Standalone(hEntity);
	if (!pShell)
		return 0;

	IPhysicsObject* pPrevious = pShell->SwapPhysics(static_cast<IPhysicsObject*>(pObject));
	if (ppPrevious)
		*ppPrevious = pPrevious;

	return 1;
}
