//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON c  static-init registration
// Entity creation through the lifecycle's factory dictionary, and the world and
// template entities built from keys the managed map spawner already parsed.

#include "ss_util.h"
#include "ss_entityhandle.h"
#include "standalone_relays.h"

#include "ss_entityfactory.h"
#include "ss_world.h"
#include "server_entity_context.h"
#include "server_state.h"

#include <vector>

IServerNetworkable *CreateNetworkableEntityByName( ss::IEntityFactoryDictionary &factories, const char *className )
{
	return factories.Create( className );
}

// Creates a classname on either hierarchy, asking the factory once. The entity is
// reached through its handle, since the networkable is a different subobject.
// The caller owns the result.
IGameEntity *CreateEntityByNameEither( ss::IEntityFactoryDictionary &factories, const char *className,
									   IGameEntity **ppOutBaseEntity, IServerEntity **ppOutServerEntity )
{
	if ( ppOutBaseEntity )
		*ppOutBaseEntity = NULL;
	if ( ppOutServerEntity )
		*ppOutServerEntity = NULL;

	IServerNetworkable *pNetworkable = CreateNetworkableEntityByName( factories, className );
	if ( !pNetworkable )
		return NULL;

	if ( IGameEntity *pGameEntity = ss::GameEntityFromEntityHandle( pNetworkable->GetEntityHandle() ) )
	{
		// On the server the handle is the server entity.
		if ( ppOutServerEntity )
			*ppOutServerEntity = (IServerEntity *)pNetworkable->GetEntityHandle();
		return pGameEntity;
	}

	if ( ppOutBaseEntity )
	{
		*ppOutBaseEntity = pNetworkable->GetBaseEntity();
		if ( *ppOutBaseEntity )
			return NULL;
	}

	// Neither a CBaseEntity nor an IGameEntity: nothing can configure it, so release it.
	Warning( "'%s' is neither a CBaseEntity nor an IGameEntity; removed\n", className );
	pNetworkable->Release();
	return NULL;
}

// The world is the one C++ entity the engine names by index, and
// CWorldFactory::Create its only construction. Its keys arrive parsed, in block
// order. Spawn() rather than DispatchSpawn(), which means nothing for worldspawn.
static CWorld *MapEntity_CreateWorld( ss::IEntityFactoryDictionary &factories, const CServerEntityContext &entities );

bool MapEntity_SpawnWorld( ss::IEntityFactoryDictionary &factories, const CServerEntityContext &entities,
						   const char *const *ppKeys, const char *const *ppValues, int nKeys )
{
	CWorld *pWorld = MapEntity_CreateWorld( factories, entities );
	if ( !pWorld )
		return false;

	for ( int i = 0; i < nKeys; i++ )
		pWorld->KeyValue( ppKeys[i], ppValues[i] );

	pWorld->Spawn();
	return true;
}

// A standalone entity from parsed key/value pairs, keyed in order. The caller
// places and spawns it.
IGameEntity *MapEntity_CreateFromKeys( ss::IEntityFactoryDictionary &factories, const char *pClassName,
									   const char *const *ppKeys, const char *const *ppValues, int nKeys )
{
	if ( !pClassName )
		return NULL;

	// The standalone hierarchy alone; anything else is created and released.
	IGameEntity *pGameEntity = CreateEntityByNameEither( factories, pClassName, NULL, NULL );
	if ( !pGameEntity )
		return NULL;

	for ( int i = 0; i < nKeys; i++ )
		StandaloneEntity_ApplyMapKey( pGameEntity, ppKeys[i], ppValues[i] );

	return pGameEntity;
}

// Asks the factory directly: CreateEntityByName would destroy anything that is
// not a CBaseEntity, worldspawn included.
static CWorld *MapEntity_CreateWorld( ss::IEntityFactoryDictionary &factories, const CServerEntityContext &entities )
{
	IServerNetworkable *pNetworkable = CreateNetworkableEntityByName( factories, "worldspawn" );
	if ( !pNetworkable )
		return NULL;

	// The singleton CWorld's constructor claimed.
	CWorld *pWorld = entities.m_pLevel ? entities.m_pLevel->m_pWorld : NULL;

	if ( !pWorld )
	{
		Warning( "[SourceSharp] worldspawn was created and did not claim the "
				 "world singleton. The level has no world.\n" );
		pNetworkable->Release();
		return NULL;
	}

	return pWorld;
}
