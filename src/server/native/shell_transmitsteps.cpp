//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec
//
// CheckTransmit's recipient question for the standalone hierarchy. The order in which
// hierarchies are asked is in C# (ServerSkyboxAreaForClient); the per-edict loop stays
// native.

#include "eiface.h"

#include "eiface.h"

#include "shell_standalone_player.h"
#include "server_entities.h"
#include "server_entity_resolver.h"
#include "server_entity_context.h"

// The standalone player's 3D skybox area. 0 if the edict is not one.
int32_t CServerEntities::EntitiesStandalonePlayerSkyboxArea(int32_t iEdict, int32_t* pOutArea)
{
	if (pOutArea == NULL)
		return 0;

	const CServerEntityContext& context = m_Entities.Context();
	edict_t* pEdict = context.m_pEngine->PEntityOfEntIndex(iEdict);
	if (pEdict == NULL)
		return 0;

	CStandalonePlayer* pStandalone = context.PlayerFromEdict(pEdict);
	if (pStandalone == NULL)
		return 0;

	*pOutArea = pStandalone->MvLocal().m_skybox3d.area;
	return 1;
}
