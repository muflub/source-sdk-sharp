//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IShimMovementServer: a player's physics shadow, move type, the bracket around one
// usercmd's moves, the move trace, fall damage and the engine-side sync, as stock's
// CBasePlayer and CPlayerMove keep them. One object, the DLL tier's; IShimMovement's
// shared slots are forwarded by the generated CShimMovementServerBase.

#pragma once

#include "ishim_areas.h"
#include "shell_shim_areas.h"

class CServerEntityResolver;

class CServerMovement final : public CShimMovementServerBase
{
public:
	explicit CServerMovement( CServerEntityResolver &entities )
		: m_Entities( entities )
	{
	}

	CServerMovement( const CServerMovement & ) = delete;
	CServerMovement &operator=( const CServerMovement & ) = delete;

	int32_t PlayerShadowFacts( uint32_t hPlayer, SourceSharpPlayerShadowFacts *pOut ) override;
	int32_t PlayerShadowAdopt( uint32_t hPlayer, void *pStand, void *pCrouch, void *pController ) override;
	void PlayerShadowRelease( uint32_t hPlayer ) override;
	void PlayerShadowCommit( uint32_t hPlayer, int32_t nState, int32_t bAfterCommand ) override;
	int32_t PlayerSetMoveType( uint32_t hPlayer, int32_t nMoveType ) override;
	int32_t PlayerCommandBegin( uint32_t hPlayer, SourceSharpPlayerCommandFacts *pFacts ) override;
	void PlayerCommandView( uint32_t hPlayer, const float *pAnglePYR, int32_t nFixAngle ) override;
	void PlayerCommandFinish( uint32_t hPlayer, const SourceSharpPlayerCommandResult *pResult ) override;
	void MoveTraceServer( const float *pStartXYZ, const float *pEndXYZ, const float *pMinsXYZ, const float *pMaxsXYZ,
						  int32_t nMask, int32_t nCollisionGroup, SourceSharpMoveTraceResult *pOut, void *pFilter,
						  uint32_t *pOutHandles ) override;
	int32_t PlayerFallDamage( uint32_t hPlayer, float flDamage ) override;
	void PlayerSyncEngineSide( uint32_t hPlayer ) override;

	// Commands the managed loop ran, for cs_sv_move_managed_report.
	unsigned int CommandsRan() const { return m_nCommandsRan; }

private:
	CServerEntityResolver &m_Entities;

	unsigned int m_nCommandsRan = 0;
};
