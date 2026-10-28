//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimNetworkServer: an entity's networked blob and engine block, binding the blob's slot, marking it changed, the payload agreement and transmit-always. One object, the DLL tier's; IShimNetwork's shared slots are forwarded by the generated CShimNetworkServerBase.

#pragma once

#include "ishim_areas.h"
#include "shell_shim_areas.h"

class CServerEntityResolver;
class CSourceSharpEntitySlots;

class CServerNetwork final : public CShimNetworkServerBase
{
public:
	CServerNetwork( CServerEntityResolver &entities, CSourceSharpEntitySlots &slots )
		: m_Entities( entities )
		, m_Slots( slots )
	{
	}

	CServerNetwork( const CServerNetwork & ) = delete;
	CServerNetwork &operator=( const CServerNetwork & ) = delete;

	void *EntityGetDataBlob( uint32_t hEntity, int32_t *pOutSize ) override;
	void *EntityBindBlob( uint32_t hEntity, int32_t *pOutSize, SourceSharpEntitySlot_t **pOutSlot,
						  uint32_t *pOutSerial ) override;
	void EntityMarkBlockChanged( uint32_t hEntity, int32_t nBlockOffset ) override;
	int32_t EntityPayloadAgreement( uint32_t hEntity, int32_t *pOutWords, int32_t *pOutFirstBadWord ) override;
	void *EntityGetEngineBlock( uint32_t hEntity, int32_t *pOutSize ) override;
	void EntitySetTransmitAlways( uint32_t hEntity, int32_t bAlways ) override;

private:
	CServerEntityResolver &m_Entities;

	// The DLL tier's blob slots; managed holds pointers into them across a reload.
	CSourceSharpEntitySlots &m_Slots;
};
