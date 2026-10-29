//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// CServerDamage's bodies (server_damage.h), moved from shell_entityapi.cpp and
// shell_worldquery.cpp.

#include "server_damage.h"

#include "ss_util.h"
#include "shell_standalone_player.h"
#include "server_entity_resolver.h"
#include "ss_takedamageinfo.h"
#include "shell_diagnostics.h"

void CServerDamage::ApplyDamage(uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType)
{
	const SourceSharpEntity_t victim = m_Entities.Resolve(hVictim);
	IGameEntity* pAttacker = m_Entities.Resolve(hAttacker).GameEntity();

	if (victim.pStandalone)
	{
		ss::CTakeDamageInfo standaloneInfo(pAttacker, pAttacker, flDamage, nDamageType);

		victim.pStandalone->TakeDamage(standaloneInfo);
		return;
	}
}

void CServerDamage::EntityTakeDamage(uint32_t hEntity, uint32_t hAttacker, float flDamage, int32_t nDamageType,
									 const float* pDirXYZ, const float* pForceOriginXYZ, int32_t bPhysicsOnly)
{
	SOURCESHARP_TRACE();

	const SourceSharpEntity_t ent = m_Entities.Resolve(hEntity);
	IGameEntity* pEntity = ent.pBase;
	CStandaloneEntity* pStandalone = ent.pStandalone;

	if (!pEntity && !pStandalone)
		return;

	IGameEntity* pAttacker = m_Entities.Resolve(hAttacker).GameEntity();

	if (!pAttacker)
		pAttacker = pEntity;

	ss::CTakeDamageInfo info(pAttacker, pAttacker, flDamage, nDamageType);

	if (pDirXYZ && pForceOriginXYZ)
	{
		ss::CalculateExplosiveDamageForce(m_pRandom, &info, Vector(pDirXYZ[0], pDirXYZ[1], pDirXYZ[2]),
										  Vector(pForceOriginXYZ[0], pForceOriginXYZ[1], pForceOriginXYZ[2]));
	}

	if (pStandalone)
	{
		// Physics-only means no damage; the shove is managed's.
		if (!bPhysicsOnly)
			pStandalone->TakeDamage(info);

		return;
	}
}

void CServerDamage::ApplyDamageForce(uint32_t hVictim, uint32_t hAttacker, float flDamage, int32_t nDamageType,
									 const float* pForceXYZ, const float* pPositionXYZ)
{
	const SourceSharpEntity_t victim = m_Entities.Resolve(hVictim);
	if (!victim.pStandalone)
		return;

	IGameEntity* pAttacker = m_Entities.Resolve(hAttacker).GameEntity();

	ss::CTakeDamageInfo info(pAttacker, pAttacker, flDamage, nDamageType);
	if (pForceXYZ)
		info.SetDamageForce(Vector(pForceXYZ[0], pForceXYZ[1], pForceXYZ[2]));
	if (pPositionXYZ)
		info.SetDamagePosition(Vector(pPositionXYZ[0], pPositionXYZ[1], pPositionXYZ[2]));

	victim.pStandalone->TakeDamage(info);
}
