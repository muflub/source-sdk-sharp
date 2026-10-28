//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data

#include "model_types.h"
#include "eiface.h"

#include "entity_shared_statics.h"

extern IVEngineServer *engine;
extern IVModelInfo *modelinfo;

#include "imanaged_areas.h"
#include "ss_string.h"

void Precache_ModelComponents( int nModelIndex )
{
	model_t *pModel = (model_t *)modelinfo->GetModel( nModelIndex );
	if ( !pModel || modelinfo->GetModelType( pModel ) != mod_studio )
	{
		return;
	}

	// Precaches the model's animation-event sounds and particle systems, in stock's order.
	if ( IManagedResources *pArea = ManagedResources() )
		pArea->ModelPrecacheComponents( modelinfo->GetModelName( pModel ) );
}

int Precache_Model( const char *name, bool bPreload )
{
	if ( !name || !*name )
	{
		return -1;
	}

	if ( IManagedResources *pArea = ManagedResources() )
		pArea->ModelPrecacheCheck( name, EntityPrecache_IsAllowed() );

	int idx = engine->PrecacheModel( name, bPreload );
	if ( idx != -1 )
	{
		Precache_ModelComponents( idx );
	}

	return idx;
}
