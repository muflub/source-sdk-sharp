//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimPlayers: a player by index, whether it lives, its spawn placement, observer mode, eye snap and ear position. One object, the DLL tier's.

#pragma once

#include "ishim_areas.h"

class CServerEntityResolver;

class CServerPlayerQueries final : public IShimPlayers
{
public:
	explicit CServerPlayerQueries( CServerEntityResolver &entities )
		: m_Entities( entities )
	{
	}

	CServerPlayerQueries( const CServerPlayerQueries & ) = delete;
	CServerPlayerQueries &operator=( const CServerPlayerQueries & ) = delete;

	uint32_t GetPlayerHandle( int32_t nPlayerIndex ) override;
	int32_t PlayerIsAlive( uint32_t hPlayer ) override;
	void PlayerPlaceAtSpawn( uint32_t hPlayer, const float *pOriginXYZ, const float *pAnglesPYR ) override;
	int32_t PlayerGetObserverMode( uint32_t hPlayer ) override;
	void PlayerSetObserverMode( uint32_t hPlayer, int32_t nMode ) override;
	void PlayerSnapEyeAngles( uint32_t hPlayer, const float *pPitchYawRoll ) override;
	int32_t ClientsStandalonePlayerEarPosition( int32_t iEdict, float *pOutXYZ ) override;

private:
	CServerEntityResolver &m_Entities;
};
