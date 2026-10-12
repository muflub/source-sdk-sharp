//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — precaching a model, without an entity ===============//
// SS_REASON g  leaf math over mdl/bsp data
//
// The bodies behind precache_model.h. Moved verbatim out of baseentity.cpp,
// where they sat between CBaseEntity::SetSize and CBaseEntity::Remove without
// belonging to either: see the header for why a static that never touches an
// entity was worth a file of its own.
//
// What came with them, and had to: the modelsounds.cache machinery. The cache
// object, its three CUtlCachedFileData callbacks, its save-on-level-transition
// game system and the local symbol table that keeps PrecacheScriptSound from
// being called twice for the same name are all file-static state that exists
// only to serve PrecacheModelComponents. Leaving them in baseentity.cpp would
// have meant exporting four internal names so that this file could reach them,
// which is a worse seam than moving the whole subject.
//
// PrecacheSoundHelper came too, and stopped being a member on the way.
// CBaseEntity declared it as a public static; the only caller in the tree is
// PrecacheModelComponents, forty lines below, so it is a file-static function
// here and its declaration has left baseentity.h.
//
//=============================================================================//
#include "shell_serversystem.h"
#include "precache_model.h"
#include "model_types.h"
#include "eiface.h"
#include "engine/ivmodelinfo.h"

// The lifted precache/tick/prediction/tracer statics. They used to be
// reached as CBaseEntity::Foo() through one-line forwarders in
// game/shared/baseentity_shared_forwarders.cpp; that quarantine is retired
// and the calls below name the free functions directly.
#include "entity_shared_statics.h"

// What cbase.h supplied, declared (plan_server_novalve.md P5): the engine and
// its model info, both connected in shell_engineboundary.cpp. The animation,
// event-list, script-event, bone and game-sound headers that stood here had
// no reader left once the model sounds cache went to C# (P3).
extern IVEngineServer *engine;
extern IVModelInfo *modelinfo;

#include "managed_runtime_server.h"
#include "imanaged_areas.h"   // plan_engineapi_vtables E3: IManagedResources
#include "ss_string.h"


// THE MODEL SOUNDS CACHE WAS HERE -- plan_server_novalve.md P3. modelsounds.cache
// (CUtlCachedFileData< CModelSoundsCache >), its loader and its saver system held
// each model's animation-event sound list between runs. SourceSharp.Server's
// ModelComponents builds that list on every precache instead
// (Precache_ModelComponents below), so the cache, its file and the
// CModelSoundsCacheSaver row went with it.


//#define WATCHACCESS
#if defined( WATCHACCESS )

static bool g_bWatching = true;

void ModelLogFunc( const char *fileName, const char *accessType )
{
	if ( g_bWatching && !EntityPrecache_IsAllowed() )
	{
		if ( ss::StrIStr( fileName, ".vcd" ) )
		{
			Msg( "%s\n", fileName );
		}
	}
}

class CWatchForModelAccess: public CServerSystem
{
public:
	virtual bool Init()
	{
		filesystem->AddLoggingFunc(&ModelLogFunc);
		return true;
	}

	virtual void Shutdown()
	{
		filesystem->RemoveLoggingFunc(&ModelLogFunc);
	}

};

static CWatchForModelAccess g_WatchForModels;

#endif

//-----------------------------------------------------------------------------
// Precache model components
//-----------------------------------------------------------------------------
void Precache_ModelComponents( int nModelIndex )
{
	model_t *pModel = (model_t *)modelinfo->GetModel( nModelIndex );
	if ( !pModel || modelinfo->GetModelType( pModel ) != mod_studio )
	{
		return;
	}

	// THE COMPONENTS ARE C#'S -- plan_server_novalve.md P2.4/P3. The model's
	// animation-event sounds (what the model sounds cache held), its
	// $keyvalues "Particles" systems and its animation-event particle effects
	// are found and precached by SourceSharp.Server (ModelComponents), in that
	// order, which is stock's.
	// E3: the managed-built resources area, acquired again on every load.
	if ( IManagedResources *pArea = ManagedResources() )
		pArea->ModelPrecacheComponents( modelinfo->GetModelName( pModel ) );
}

int Precache_Model( const char *name, bool bPreload )
{
	if ( !name || !*name )
	{
		return -1;
	}

	// Warn on out of order precache
	if ( !EntityPrecache_IsAllowed() )
	{
		if ( !engine->IsModelPrecached( name ) )
		{
			DevMsg( "Late precache of %s -- not necessarily a bug now that we allow ~everything to be dynamically loaded.\n", name );
		}
	}
#if defined( WATCHACCESS )
	else
	{
		g_bWatching = false;
	}
#endif

	int idx = engine->PrecacheModel( name, bPreload );
	if ( idx != -1 )
	{
		Precache_ModelComponents( idx );
	}

#if defined( WATCHACCESS )
	g_bWatching = true;
#endif

	return idx;
}
