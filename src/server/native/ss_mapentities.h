//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Map-entity entry points that need no game/ type, defined in shell_mapparse.cpp.

#pragma once

#include <stddef.h>

#include "igameentity_fwd.h"
class CWorld;
class CServerEntityContext;
class IServerEntity;
class IServerNetworkable;

namespace ss
{
class IEntityFactoryDictionary;
}

// Builds and spawns the world from its parsed key/value pairs, before any other
// entity exists. False when the factory gave nothing or the object did not claim
// the singleton.
bool MapEntity_SpawnWorld(ss::IEntityFactoryDictionary& factories, const CServerEntityContext& entities,
						  const char* const* ppKeys, const char* const* ppValues, int nKeys);

// Creates and keys a standalone entity from its parsed key/value pairs (classname
// included); the caller places and spawns it. NULL when no class answers pClassName.
IGameEntity* MapEntity_CreateFromKeys(ss::IEntityFactoryDictionary& factories, const char* pClassName,
									  const char* const* ppKeys, const char* const* ppValues, int nKeys);

// Creates a classname on either hierarchy, asking the factory once. The caller owns the result.
IGameEntity* CreateEntityByNameEither(ss::IEntityFactoryDictionary& factories, const char* className,
									  IGameEntity** ppOutBaseEntity, IServerEntity** ppOutServerEntity);
IServerNetworkable* CreateNetworkableEntityByName(ss::IEntityFactoryDictionary& factories, const char* className);
