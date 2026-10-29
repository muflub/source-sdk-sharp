//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The entity that arrived in a touch, on whichever hierarchy holds it. A value
// built per report and dropped; only the handle survives being stored.
#pragma once

#include "basehandle.h"

#include "igameentity.h"
#include "shell_resolve.h"

struct Toucher_t
{
	IHandleEntity *pHandle;

	// The SDK entity; NULL for the other kind.
	IGameEntity *pBase;

	// The entity outside the SDK. NULL for a CBaseEntity.
	IGameEntity *pGame;

	bool IsValid() const { return pBase != NULL || pGame != NULL; }

	// Both hierarchies share one handle table, so this one value names either. Not an
	// EHANDLE, whose Get() would cast an entity of the other kind to the wrong type.
	CBaseHandle GetRefEHandle() const { return pHandle ? pHandle->GetRefEHandle() : CBaseHandle(); }
};

// The toucher behind an edict, partition element or trace result.
inline Toucher_t ToucherFromEntityHandle( CServerEntityResolver &entities, IHandleEntity *pHandleEntity )
{
	Toucher_t toucher = { pHandleEntity, NULL, NULL };

	if ( !pHandleEntity )
		return toucher;

	// Every GetBaseEntity() in server.so returns NULL.
	toucher.pBase = NULL;

	toucher.pGame = SourceSharp_GameEntityFromHandleEntity( entities, pHandleEntity );

	return toucher;
}
