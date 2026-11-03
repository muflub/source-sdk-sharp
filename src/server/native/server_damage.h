//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimDamage: damage managed applies to an entity, with or without its force, as stock's
// CBaseEntity::TakeDamage is handed a CTakeDamageInfo. One object, the DLL tier's.

#pragma once

#include "ishim_areas.h"

class CServerEntityResolver;
class IUniformRandomStream;

class CServerDamage final : public IShimDamage
{
public:
	CServerDamage(CServerEntityResolver& entities, IUniformRandomStream* pRandom)
		: m_Entities(entities)
		, m_pRandom(pRandom)
	{
	}

	CServerDamage(const CServerDamage&) = delete;
	CServerDamage& operator=(const CServerDamage&) = delete;

	void ApplyDamage(uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType) override;

	// Damage from an emitter, and its force. CalculateExplosiveDamageForce scales, clamps
	// and jitters the force per victim; the jitter is server randomness and stays here so
	// managed is never a source of values demo playback must reproduce.
	void EntityTakeDamage(uint32_t hEntity, uint32_t hAttacker, float flDamage, int32_t nDamageType,
						  const float* pDirXYZ, const float* pForceOriginXYZ, int32_t bPhysicsOnly) override;

	void ApplyDamageForce(uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType,
						  const float* pForceXYZ, const float* pPositionXYZ) override;

	// The same damage, answered: how much the entity took. 0 when it took nothing (a
	// physics-only ask, an SDK entity, a refusal), so a caller whose follow-up belongs
	// inside OnTakeDamage_Alive (the blast's push) can gate on the hit.
	int32_t EntityTakeDamageAnswered(uint32_t hEntity, uint32_t hAttacker, float flDamage, int32_t nDamageType,
									 const float* pDirXYZ, const float* pForceOriginXYZ, int32_t bPhysicsOnly) override;

private:
	int32_t EntityTakeDamageLanded(uint32_t hEntity, uint32_t hAttacker, float flDamage, int32_t nDamageType,
								   const float* pDirXYZ, const float* pForceOriginXYZ, int32_t bPhysicsOnly);

	CServerEntityResolver& m_Entities;

	// The engine's: a blast's force is jittered per victim.
	IUniformRandomStream* m_pRandom;
};
