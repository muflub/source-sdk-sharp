//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Resolving a managed entity handle. CServerEntityResolver::Resolve does one
// entity-list lookup (serial revalidated); everything else here is a projection
// of its result. GetGameEntity() and GetBaseEntity() partition the hierarchies,
// so at most one arm is set.
#pragma once

class CBasePlayer;
class CStandaloneEntity;
class CWorld;

class IHandleEntity;
class IPhysicsObject;
struct edict_t;

// Declared, not included: it lives in mathlib/mathlib.h, which this header must not pull in.
struct matrix3x4_t;

// A handle resolved to whichever hierarchy holds it. Methods cover only what both
// hierarchies can answer; a caller needing more reads pBase directly.
struct SourceSharpEntity_t
{
	IGameEntity* pBase;
	CStandaloneEntity* pStandalone;

	// worldspawn, which neither GetGameEntity() nor GetBaseEntity() answers.
	CWorld* pWorld;

	bool IsValid() const { return pBase != NULL || pStandalone != NULL || pWorld != NULL; }

	// Whether this handle names the world.
	bool IsWorld() const { return pWorld != NULL; }

	// The entity as an IGameEntity on either hierarchy, or NULL. What a
	// CTakeDamageInfo's attacker and inflictor take.
	IGameEntity* GameEntity() const;

	const Vector& Origin() const;

	// The collision box, as offsets from Origin(); a brush model's origin is
	// usually the world's, not the box's.
	void Bounds(Vector* pOutMins, Vector* pOutMaxs) const;

	Vector Center() const;

	// The index the engine stores things against (a decal, ...). 0 for an
	// entity the client has never heard of.
	int EntityIndex() const;

	// The frame the entity's model is expressed in. Not CollisionToWorldTransform
	// on the SDK arm: that drops the rotation for SOLID_NONE, SOLID_BBOX and
	// world-aligned entities.
	const matrix3x4_t& EntityToWorld() const;

	// For diagnostics only; the handle is the identity.
	const char* Classname() const;

	IPhysicsObject* Physics() const;

	// What the engine's collision code takes to skip an entity.
	IHandleEntity* ToHandleEntity() const;
};

// The other direction: the handle C# knows an entity by.

// INVALID_EHANDLE_INDEX for NULL.
uint32_t SourceSharp_HandleFromEntity(IGameEntity* pEntity);

// The same identity as a CBaseHandle (entry and serial). IGameEntity is not an
// IHandleEntity, so GetRefEHandle() is unavailable here.
CBaseHandle SourceSharp_RefHandleFromEntity(IGameEntity* pEntity);

// From an entity of either hierarchy.
uint32_t SourceSharp_HandleFromAnyEntity(IHandleEntity* pHandleEntity);

// The shell's IGameEntity face for an IHandleEntity, or NULL for anything not
// one of ours. Lets headers that must not include shell_standalone.h make the
// derived-to-base conversion.
class CServerEntityResolver;
IGameEntity* SourceSharp_GameEntityFromHandleEntity(CServerEntityResolver& entities, IHandleEntity* pHandleEntity);
