//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimEntitiesServer: an entity's identity, names, keyvalues, spawn state, health,
// visibility and owner, a player's skybox area, and the searches by classname and sphere,
// as stock's CBaseEntity and CGlobalEntityList answer them. One object, the DLL tier's;
// the client answers IShimEntities' shared slots with its own bodies.

#pragma once

#include "ishim_areas.h"
#include "shell_shim_areas.h"

class CServerEntityResolver;

class CServerEntities final : public IShimEntitiesServer
{
public:
	explicit CServerEntities( CServerEntityResolver &entities )
		: m_Entities( entities )
	{
	}

	CServerEntities( const CServerEntities & ) = delete;
	CServerEntities &operator=( const CServerEntities & ) = delete;

	int32_t EntityGetIndex( uint32_t hEntity ) override;
	void EntityGetOrigin( uint32_t hEntity, float *pOutXYZ ) override;
	void EntityGetAngles( uint32_t hEntity, float *pOutPYR ) override;
	int32_t EntityIsValid( uint32_t hEntity ) override;
	int32_t EntityIsPlayer( uint32_t hEntity ) override;
	void EntityGetClassName( uint32_t hEntity, char *pOut, int32_t nOutSize ) override;
	void EntityGetName( uint32_t hEntity, char *pOut, int32_t nOutSize ) override;
	int32_t EntityGetState( uint32_t hEntity, int32_t *pOutMoveType ) override;
	uint32_t FindEntityByClassname( uint32_t hStartAfter, const char *pClassName ) override;
	int32_t EntitiesInSphere( const float *pCenterXYZ, float flRadius, uint32_t *pOutHandles, int32_t nMax ) override;
	int32_t EntitySetKeyValue( uint32_t hEntity, const char *pKey, const char *pValue ) override;
	void EntityGetSpawnOrigin( uint32_t hEntity, float *pOutXYZ ) override;
	void EntityGetSpawnAngles( uint32_t hEntity, float *pOutPYR ) override;
	int32_t EntityGetSpawnFlags( uint32_t hEntity ) override;
	void EntitySetSpawnFlags( uint32_t hEntity, int32_t nSpawnFlags ) override;
	int32_t EntityGetHealth( uint32_t hEntity ) override;
	void EntitySetHealth( uint32_t hEntity, int32_t nHealth ) override;
	void EntitySetVisible( uint32_t hEntity, int32_t bVisible ) override;
	void EntitySetOwner( uint32_t hEntity, uint32_t hOwner ) override;
	int32_t EntitiesStandalonePlayerSkyboxArea( int32_t iEdict, int32_t *pOutArea ) override;
	void EntitySetClassName( uint32_t hEntity, const char *pName ) override;

private:
	CServerEntityResolver &m_Entities;
};
