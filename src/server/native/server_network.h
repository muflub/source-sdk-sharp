//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimNetworkServer: the managed types published for networking, the tier and world ServerClasses
// built from them, and an entity's networked blob and engine block: binding the blob's slot,
// marking it changed, the payload agreement and transmit-always. One object, the DLL tier's.
// The client answers IShimNetwork's shared slots with its own bodies.

#pragma once

#include "ishim_areas.h"
#include "shell_shim_areas.h"

class CServerEntityResolver;
class CSourceSharpEntitySlots;
class CServerNetTables;

class CServerNetwork final : public IShimNetworkServer
{
public:
	CServerNetwork( CServerEntityResolver &entities, CSourceSharpEntitySlots &slots, CServerNetTables &tables )
		: m_Entities( entities )
		, m_Slots( slots )
		, m_Tables( tables )
	{
	}

	CServerNetwork( const CServerNetwork & ) = delete;
	CServerNetwork &operator=( const CServerNetwork & ) = delete;

	// The hierarchy's largest declared blob size, checked against the largest tier; the payload
	// word count the greediest class will carry.
	int32_t NetHierarchyDeclare( int32_t nBlobBytes ) override;

	// C# publishing one managed type: a name, its layout hash and size. Answers the type's
	// tier. pOutTypeId hands back native's FNV-1a of the name so C# can check its own.
	int32_t NetPublishType( const char *pClassName, int32_t nShell, uint32_t nSchemaHash, int32_t nLayoutBytes,
							uint32_t *pOutTypeId ) override;

	// One tier as native registered it, with the props native actually built.
	int32_t NetTierInfo( int32_t nShell, int32_t iTier, const char **ppOutTable, int32_t *pOutBytes, int32_t *pOutWords,
						 int32_t *pOutProps, int32_t *pOutLive ) override;

	// worldspawn's blob size, declared once; builds its ServerClass.
	int32_t WorldNetDeclare( int32_t nBlobBytes ) override;

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

	CServerNetTables &m_Tables;
};
