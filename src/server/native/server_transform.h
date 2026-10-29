//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimTransform: an entity's origin, angles, velocities, gravity and bounce, as stock's CBaseEntity setters and getters keep them, and a player's eye. One object, the DLL tier's.

#pragma once

#include "ishim_areas.h"

class CServerEntityResolver;

class CServerTransform final : public IShimTransform
{
public:
	explicit CServerTransform(CServerEntityResolver& entities)
		: m_Entities(entities)
	{
	}

	CServerTransform(const CServerTransform&) = delete;
	CServerTransform& operator=(const CServerTransform&) = delete;

	void EntitySetOrigin(uint32_t hEntity, const float* pXYZ) override;
	void EntitySetAngles(uint32_t hEntity, const float* pPYR) override;
	void EntityTeleport(uint32_t hEntity, const float* pOrigin, const float* pAngles, int32_t bZeroVelocity) override;
	void EntityGetVelocity(uint32_t hEntity, float* pOutXYZ) override;
	void EntitySetVelocity(uint32_t hEntity, const float* pXYZ) override;
	void EntityGetAngularVelocity(uint32_t hEntity, float* pOutPYR) override;
	void EntitySetAngularVelocity(uint32_t hEntity, const float* pPYR) override;
	void EntityGetEyePosition(uint32_t hEntity, float* pOutXYZ) override;
	void EntityGetEyeAngles(uint32_t hEntity, float* pOutPYR) override;
	float EntityGetGravity(uint32_t hEntity) override;
	void EntitySetGravity(uint32_t hEntity, float flGravity) override;
	void EntitySetBounce(uint32_t hEntity, int32_t nBounces) override;

private:
	CServerEntityResolver& m_Entities;
};
