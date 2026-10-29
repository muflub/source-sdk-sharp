//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Relays for SDK game code that holds an opaque IGameEntity * and needs more than
// the engine boundary carries. The cast to the concrete class is made once, in
// standalone_relays.cpp.
#pragma once

#include "igameentity.h"

namespace ss
{
class CTakeDamageInfo;
}
class IHandleEntity;
class IPhysicsObject;

// The name a mapper gave it; empty for an unnamed entity.
const char* StandaloneEntity_Name(const IGameEntity* pEnt);

// Returns the damage actually taken, as CBaseEntity::TakeDamage does.
int StandaloneEntity_TakeDamage(IGameEntity* pEnt, const ss::CTakeDamageInfo& info);

// A game concept the damage rules branch on; the engine never asks.
bool StandaloneEntity_IsPlayer(const IGameEntity* pEnt);

// The MOVETYPE_* the solver's callbacks see: a player's own; for an entity with a
// physics object, the movetype managed wrote into its block; NONE otherwise.
// Stock's collision record and ShouldCollide read GetMoveType() here.
int StandaloneEntity_MoveType(const IGameEntity* pEnt);

// The solver's object, or NULL. Not on IGameEntity: the engine asks an object for
// its entity, never the reverse, so only game code wants this direction.
IPhysicsObject* StandaloneEntity_PhysicsObject(const IGameEntity* pEnt);

// One parsed key. Returns whether anything wanted it, which is what lets a
// caller warn about a key nothing claimed.
bool StandaloneEntity_ApplyMapKey(IGameEntity* pEnt, const char* szKeyName, const char* szValue);

// Place positions an entity; PlaceAndSpawn also spawns it, which must come after
// placement. A NULL origin or angles leaves the mapper's value alone.
void StandaloneEntity_Place(IGameEntity* pEnt, const Vector* pOrigin, const QAngle* pAngles);

void StandaloneEntity_PlaceAndSpawn(IGameEntity* pEnt, const Vector* pOrigin, const QAngle* pAngles);

// The entity's world-space velocity, as managed holds it: its physics object's
// velocity when it has one, else its own member.
Vector StandaloneEntity_Velocity(const IGameEntity* pEnt);

// The velocity of the entity's own physics object, held for StandaloneEntity_Velocity.
void StandaloneEntity_HoldMoveVelocity(IGameEntity* pEnt, const Vector& vecVelocity);
