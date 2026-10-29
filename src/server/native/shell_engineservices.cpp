//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// Leaf steps that ask engine-owned globals what managed code cannot ask itself.

#include "standalone_list.h"
#include "eiface.h"

#include "shell_standalone.h"
#include "server_entity_resolver.h"
#include "server_models.h"

// Counts the standalone entities a model cache flush reached; the managed flush
// invalidates their skeletons and prints the count.
int32_t CServerModels::EntitiesInvalidateMdlCacheAll()
{
	int32_t nInvalidated = 0;

	const CStandaloneList standalones = StandaloneEntities(m_Entities.Context());

	nInvalidated = (int32_t)standalones.size();

	return nInvalidated;
}
