//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The map-entity parse's entry points that need no game/ type, for server.so
// TUs on the R5 include set (plan_server_novalve.md P5). All of them are
// defined in shell_mapparse.cpp, which includes this header so the compiler
// holds these declarations to its definitions.
//
// The parse itself is ss::MapEntity_ParseEntity over an ss::IMapEntityFilter
// (P5 lane E): the filter is server.so's own interface, so it is not a second
// body under the fork's IMapEntityFilter name, and the entry point that takes
// one is ss:: too, so a TU that still sees mapentities.h's declaration of
// ::MapEntity_ParseEntity is never offered two overloads for a NULL filter.
// The plain entry points below agree with mapentities.h word for word, so a
// TU may include both.
//=============================================================================//

#ifndef SS_MAPENTITIES_H
#define SS_MAPENTITIES_H
#ifdef _WIN32
#pragma once
#endif

#include <stddef.h>

class IGameEntity;
class CWorld;


// Builds the world from its parsed key/value pairs (block order) and spawns it:
// the level-init step that must have the world before any other entity exists.
// Managed parsed the map and picked the block (IShimLevel::SpawnWorld). False
// when the factory gave nothing or the object did not claim the singleton.
bool MapEntity_SpawnWorld( const char *const *ppKeys, const char *const *ppValues, int nKeys );

// A standalone entity from its parsed key/value pairs (block order, classname
// included), keyed and QUEUED for MapEntity_SpawnPendingNonBaseEntities -- the
// same work the text parse's non-base arm does, without the text. NULL when no
// registered class answers pClassName.
IGameEntity *MapEntity_CreateFromKeys( const char *pClassName, const char *const *ppKeys,
                                       const char *const *ppValues, int nKeys );

// Spawns the entities the parse queued because they are not SDK entities.
void MapEntity_SpawnPendingNonBaseEntities();

// Takes pEntity off that queue; false when it was not on it.
bool MapEntity_ClaimPendingNonBaseEntity( IGameEntity *pEntity );

// The engine-side point_template capture (SourceSharp_TemplateCapture's
// callee). It answers 0, nothing captured: the capture window is MapSpawner's
// in C#. See the definition.
int MapEntity_TemplateCapture( const char *pszSourceName, bool bRemoveSources,
                               int *pOutIndices, float *pOutOriginsXYZ,
                               float *pOutAnglesPYR, int nMax );

#endif // SS_MAPENTITIES_H
