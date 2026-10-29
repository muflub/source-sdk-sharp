//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// vphysics asks ShouldCollide once, when a pair comes into proximity, and keeps
// the answer; only RecheckCollisionFilter makes it ask again. So anything that
// changes what the collision filter would return must call it (stock's
// CBaseEntity::CollisionRulesChanged). A template so a test can use a stub.
#pragma once

// Returns whether an object was told; an entity with no physics object has
// nothing cached.
template <typename TPhysicsObject>
inline bool SourceSharpCollisionRulesChanged(TPhysicsObject* pObject)
{
	if (!pObject)
		return false;

	pObject->RecheckCollisionFilter();
	return true;
}

// The same, behind a switch (a convar at the call site) so both behaviours can be
// compared in one process.
template <typename TPhysicsObject>
inline bool SourceSharpCollisionRulesChanged(TPhysicsObject* pObject, bool bArmed)
{
	if (!bArmed)
		return false;

	return SourceSharpCollisionRulesChanged(pObject);
}
