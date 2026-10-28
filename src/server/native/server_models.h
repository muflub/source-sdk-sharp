//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimModelsServer: the model an entity is given, applied now as stock's CBaseEntity::SetModel applies it. One object, the DLL tier's; IShimModels' shared slots are forwarded by the generated CShimModelsServerBase.

#pragma once

#include "ishim_areas.h"
#include "shell_shim_areas.h"

class CServerEntityResolver;

class CServerModels final : public CShimModelsServerBase
{
public:
	explicit CServerModels( CServerEntityResolver &entities )
		: m_Entities( entities )
	{
	}

	CServerModels( const CServerModels & ) = delete;
	CServerModels &operator=( const CServerModels & ) = delete;

	void EntitySetModel( uint32_t hEntity, const char *pModel ) override;

private:
	CServerEntityResolver &m_Entities;
};
