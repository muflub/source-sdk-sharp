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

#include "edict.h"
#include "eiface.h"

extern IVEngineServer *engine;
extern CGlobalVars *gpGlobals;
#include "eiface.h"
#include "edict.h"

#include "shell_standalone_player.h"

// The standalone player's 3D skybox area. 0 if the edict is not one.
int32_t SourceSharp_EntitiesStandalonePlayerSkyboxArea( int32_t iEdict, int32_t *pOutArea )
{
	if ( pOutArea == NULL )
		return 0;

	edict_t *pEdict = engine->PEntityOfEntIndex( iEdict );
	if ( pEdict == NULL )
		return 0;

	CStandalonePlayer *pStandalone = CStandalonePlayer::FromEdict( pEdict );
	if ( pStandalone == NULL )
		return 0;

	*pOutArea = pStandalone->MvLocal().m_skybox3d.area;
	return 1;
}
