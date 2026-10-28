//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// Leaf steps that ask engine-owned globals what managed code cannot ask itself.

#include "eiface.h"

#include "shell_standalone.h"

#include "sourcesharp.h"

#include "sourcesharp_gamesystem.h"
#include "shell_server.h"

// Counts the standalone entities a model cache flush reached; the managed flush
// invalidates their skeletons and prints the count.
int32_t SourceSharp_EntitiesInvalidateMdlCacheAll()
{
	int32_t nInvalidated = 0;

	// Declared in the body: this file keeps nothing at file scope but its steps.
	extern std::vector< CStandaloneEntity * > &StandaloneEntities();

	const std::vector< CStandaloneEntity * > &standalones = StandaloneEntities();

	nInvalidated = (int32_t)standalones.size();

	return nInvalidated;
}

#include "imanaged_areas.h"
