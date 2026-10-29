//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Stock's blast-blocking rule from CGameRules::RadiusDamage, over handles so it
// works for entities of either hierarchy. A blocker in the way that is neither
// the victim nor owned by it keeps mass/350 of the damage if it has physics, else
// a flat quarter. No engine headers, so it can be tested.
#pragma once

class IHandleEntity;

// Stock's MASS_ABSORB_ALL_DAMAGE, in kilogrammes.
const float BLAST_MASS_ABSORB_ALL = 350.0f;

// Stock's flat share for a blocker with no physics object.
const float BLAST_BLOCKED_NO_PHYSICS = 0.25f;

// Whether what the trace stopped on is in the way. pBlocker is NULL for the world
// and static props; something owned by the victim does not shield it.
inline bool BlastBlockerCounts(const IHandleEntity* pVictim, const IHandleEntity* pBlocker,
							   const IHandleEntity* pBlockerOwner)
{
	if (!pBlocker)
		return false;

	if (pBlocker == pVictim)
		return false;

	if (pBlockerOwner && pBlockerOwner == pVictim)
		return false;

	return true;
}

// The fraction of the blast a blocker keeps; 1.0 or more means the victim takes
// nothing, and the caller tests for that.
inline float BlastBlockedFraction(bool bHasPhysics, float flMass)
{
	if (!bHasPhysics)
		return BLAST_BLOCKED_NO_PHYSICS;

	return flMass / BLAST_MASS_ABSORB_ALL;
}
