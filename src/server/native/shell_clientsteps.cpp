//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// ClientEarPosition's standalone arm, as a step that answers yes or no; managed
// decides the order the hierarchies are asked in. The caller keeps the Assert
// and the vec3_origin fallback.

#include "eiface.h"

#include "shell_standalone_player.h"
#include "server_player_queries.h"
#include "server_entity_resolver.h"
#include "server_entity_context.h"

// 1 when this edict is a standalone player, with pOutXYZ written; 0 otherwise,
// with pOutXYZ untouched, so the caller can tell a miss from the world origin.
int32_t CServerPlayerQueries::ClientsStandalonePlayerEarPosition( int32_t iEdict, float *pOutXYZ )
{
	if ( pOutXYZ == NULL )
		return 0;

	const CServerEntityContext &context = m_Entities.Context();
	edict_t *pEdict = context.m_pEngine->PEntityOfEntIndex( iEdict );
	if ( pEdict == NULL )
		return 0;

	CStandalonePlayer *pStandalone = context.PlayerFromEdict( pEdict );
	if ( pStandalone == NULL )
		return 0;

	const Vector vecEars = pStandalone->EyePosition();
	pOutXYZ[0] = vecEars.x;
	pOutXYZ[1] = vecEars.y;
	pOutXYZ[2] = vecEars.z;
	return 1;
}
