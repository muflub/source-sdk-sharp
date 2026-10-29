//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimCollisionServer: an entity's bounds, solidity, collision group, trigger bloat and
// overlap report, as stock's CCollisionProperty keeps them. One object, the DLL tier's.

#pragma once

#include "ishim_areas.h"
#include "shell_shim_areas.h"

class CServerEntityResolver;
class CGlobalVars;

class CServerCollision final : public IShimCollisionServer
{
public:
	CServerCollision(CServerEntityResolver& entities, const CGlobalVars* pGlobals)
		: m_Entities(entities)
		, m_pGlobals(pGlobals)
	{
	}

	CServerCollision(const CServerCollision&) = delete;
	CServerCollision& operator=(const CServerCollision&) = delete;

	void EntityGetBounds(uint32_t hEntity, float* pOutMinsXYZ, float* pOutMaxsXYZ) override;
	void EntityGetBoundsSize(uint32_t hEntity, float* pOutXYZ) override;
	void EntitySetBounds(uint32_t hEntity, const float* pMinsXYZ, const float* pMaxsXYZ) override;
	void EntitySetSolidity(uint32_t hEntity, int32_t nSolidType, int32_t nSolidFlags) override;
	int32_t EntityGetCollisionGroup(uint32_t hEntity) override;
	void EntitySetCollisionGroup(uint32_t hEntity, int32_t nGroup) override;
	int32_t EntityOverlapReport(uint32_t* pOutPairs, int32_t nMaxPairs) override;
	void EntitySetTriggerBloat(uint32_t hEntity, int32_t nBloat, int32_t bUniform) override;

private:
	CServerEntityResolver& m_Entities;

	// The overlap report is drained once per frame, by its frame count.
	const CGlobalVars* m_pGlobals;
};
