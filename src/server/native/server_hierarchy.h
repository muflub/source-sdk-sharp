//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimHierarchy: parenting a standalone entity and its local transform, as stock's CBaseEntity::SetParent and SetLocalOrigin/Angles keep them. One object, the DLL tier's (shell_standalone_hierarchy.cpp).

#pragma once

#include "ishim_areas.h"

class CServerEntityResolver;

class CServerHierarchy final : public IShimHierarchy
{
public:
	explicit CServerHierarchy(CServerEntityResolver& entities)
		: m_Entities(entities)
	{
	}

	CServerHierarchy(const CServerHierarchy&) = delete;
	CServerHierarchy& operator=(const CServerHierarchy&) = delete;

	int32_t EntitySetParent(uint32_t hChild, uint32_t hParent, int32_t nParentAttachment) override;
	uint32_t EntityGetParent(uint32_t hChild) override;
	int32_t EntityGetParentAttachment(uint32_t hChild) override;
	void EntitySetLocalTransform(uint32_t hChild, const float* pOrigin, const float* pAngles) override;
	void EntityGetLocalTransform(uint32_t hChild, float* pOutOrigin, float* pOutAngles) override;
	int32_t EntityParentProbe(uint32_t hEntity) override;
	int32_t EntityGetAttachmentFrame(uint32_t hEntity, int32_t nAttachment, float* pOutOriginXYZ,
									 float* pOutAnglesPYR) override;

private:
	CServerEntityResolver& m_Entities;
};
