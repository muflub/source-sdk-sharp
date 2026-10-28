//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The client's entity allocator, abs-query flags, the cl_interpolate convar and
// the visibility sweep.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "tier0/dbg.h"
#include "tier0/memalloc.h"
#include "tier0/threadtools.h"
#include "tier1/convar.h"
#include "tier1/iconvar.h"
#include "client_entity.h"

#include "shell_entitystatics.h"
#include "entity_abs_query_state.h"
#include "entity_shared_statics.h"
#include "shell_standalone_client.h"

// The entity allocator. MEM_ALLOC_CREDIT() credits this file.
void *EntityMem_Alloc( size_t stAllocateBlock )
{
	Assert( stAllocateBlock != 0 );
	MEM_ALLOC_CREDIT();
	void *pMem = MemAlloc_Alloc( stAllocateBlock );
	memset( pMem, 0, stAllocateBlock );
	return pMem;
}

void EntityMem_Free( void *pMem )
{
	// get the engine to free the memory
	MemAlloc_Free( pMem );
}

// The abs-query flags. Their storage is shared with the server
// (entity_abs_query_state.h); off the main thread the setters do nothing and
// the getters answer true.
void EntityAbs_SetQueriesValid( bool bValid )
{
	// @MULTICORE: Always allow in worker threads, assume higher level code is handling correctly
	if ( !ThreadInMainThread() )
		return;

	if ( !bValid )
	{
		EntityAbs_QueriesValidRef() = false;
	}
	else
	{
		EntityAbs_QueriesValidRef() = true;
	}
}

bool EntityAbs_AreQueriesValid()
{
	if ( !ThreadInMainThread() )
		return true;
	return EntityAbs_QueriesValidRef();
}

void EntityAbs_SetRecomputationsEnabled( bool bEnable )
{
	if ( !ThreadInMainThread() )
		return;
	EntityAbs_RecomputationEnabledRef() = bEnable;
}

bool EntityAbs_AreRecomputationsEnabled()
{
	if ( !ThreadInMainThread() )
		return true;
	return EntityAbs_RecomputationEnabledRef();
}

// cl_interpolate is FCVAR_USERINFO: the server reads it back by name to decide
// whether to lag-compensate against an interpolating client, so it stays native.
static ConVar cl_interpolate( "cl_interpolate", "1.0f", FCVAR_USERINFO | FCVAR_DEVELOPMENTONLY );

// Calls UpdateVisibility() on every standalone client shell; answers how many
// it swept, since the effect is only visible next frame.
int32_t SourceSharp_ClientEntitiesUpdateVisibilityAll( void )
{
	const int nCount = SourceSharp_StandaloneClientCount();
	int32_t nSwept = 0;

	for ( int i = 0; i < nCount; i++ )
	{
		if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneClientAt( i ) )
		{
			pEntity->UpdateVisibility();
			nSwept++;
		}
	}

	return nSwept;
}
