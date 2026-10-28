//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// IShimLevel: the native steps of LevelInit, ServerActivate and LevelShutdown,
// one step per method. Managed owns their order and every condition.

#include "eiface.h"

#include "filesystem.h"
#include "datacache/imdlcache.h"
#include "ss_mapentities.h"
#include "ss_soundent.h"
#include "entity_shared_statics.h"

#include "ishim_level.h"
#include "shell_standalone.h"

#include "ss_world.h"
#include "shell_serversystem.h"

// Defined in shell_standalone_base.cpp.
void StandaloneEntities_LevelShutdown();

// Defined in physics.cpp, gamestringpool.cpp and shell_entityslots.cpp.
CServerSystem *PhysicsGameSystem();
CServerSystem *GameStringSystem();
void SourceSharp_ResetEntitySlots();

extern INetworkStringTable *g_pStringTableInfoPanel;

extern CGlobalVars *gpGlobals;
extern IVEngineServer *engine;
extern IFileSystem *filesystem;
extern IMDLCache *mdlcache;

// Defined in shell_entityapi.cpp.
uint32_t SourceSharp_CreateEntity( const char *pClassName );

// The managed map spawner's creation step: creates the entity and hands every
// key to KeyValueShellOnly, which keeps the ones the shell needs. pPackedKeys is
// key\0value\0..., nBytes long (map text holds no NUL).
uint32_t SourceSharp_MapSpawnCreate( const char *pClassName, const char *pPackedKeys, int32_t nBytes )
{
	const uint32_t hEntity = SourceSharp_CreateEntity( pClassName );

	if ( hEntity == INVALID_EHANDLE_INDEX )
		return INVALID_EHANDLE_INDEX;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone == NULL )
	{
		// The managed registry and the native factories disagree; say so.
		Warning( "[SourceSharp] map spawn: '%s' is not on the parallel hierarchy; "
				 "its map keys were not applied\n",
				 pClassName );
		return hEntity;
	}

	if ( pPackedKeys && nBytes > 0 )
	{
		const char *pAt = pPackedKeys;
		const char *pEnd = pPackedKeys + nBytes;

		while ( pAt < pEnd )
		{
			const char *pKey = pAt;
			pAt += strlen( pAt ) + 1;

			// A truncated payload: stop rather than read past pEnd.
			if ( pAt >= pEnd )
				break;

			const char *pValue = pAt;
			pAt += strlen( pAt ) + 1;

			ent.pStandalone->KeyValueShellOnly( pKey, pValue );
		}
	}

	return hEntity;
}

namespace
{

class CShimLevel : public IShimLevel
{
public:
	// No fields: the object exists only to carry a vtable.

	// Empty, but still called: its position in managed's LevelInit marks two
	// orderings (low violence before the template precache, templates after the
	// entities they read).
	virtual void PrecachePointTemplates( const char *pMapDataBlobs, const int32_t *pStringSizes,
										 int32_t nCount ) OVERRIDE
	{
		(void)pMapDataBlobs;
		(void)pStringSizes;
		(void)nCount;
	}

	// Empty, and still called for its position. Managed decides low violence.
	virtual void SetLowViolence( const char *pMapName ) OVERRIDE { (void)pMapName; }

	// Empty, and still called for its position.
	virtual void LoadMessageOfTheDay() OVERRIDE {}

	virtual void SetPrecacheAllowed( int32_t bAllowed ) OVERRIDE { EntityPrecache_SetAllowed( bAllowed != 0 ); }

	// The standalone hierarchy's teardown, which gEntList cannot see. Runs
	// pre-entity, while everything the hierarchy holds is still real.
	virtual void ShutdownStandaloneEntities() OVERRIDE { StandaloneEntities_LevelShutdown(); }

	// Stock's: "YWB: This entity pointer is going away now and is corrupting
	// memory on level transitions/restarts".
	virtual void ShutdownSoundEnt() OVERRIDE { ss::SoundEnt_Shutdown(); }

	// Releases the world through its networkable (CWorldNetworkable::Release
	// deletes the outer object). The world is not in the entity list, so nothing
	// else frees it, and the next map's CWorldFactory::Create needs it gone.
	virtual void ClearEntityList() OVERRIDE
	{
		// Must not be guarded: a world left alive faults the next map load.
		if ( CWorld *pWorld = GetWorldEntity() )
			pWorld->GetNetworkable()->Release();
	}

	// Starts the engine's load-game read (stock's LevelInit loadGame arm).
	// Managed decides what a false answer means.
	virtual int32_t LoadSaveGameState( const char *pMapName, int32_t bCreatePlayers ) OVERRIDE
	{
		if ( pMapName == NULL )
			return 0;

		return engine->LoadGameState( pMapName, bCreatePlayers != 0 ) ? 1 : 0;
	}

	// Open/Size/Close: Size( path ) answers 0 for a missing file.
	virtual int32_t FileSizeOnPath( const char *pPath, const char *pPathId ) OVERRIDE
	{
		if ( pPath == NULL || pPath[0] == '\0' || pPathId == NULL || filesystem == NULL )
			return -1;

		FileHandle_t hFile = filesystem->Open( pPath, "rb", pPathId );
		if ( hFile == FILESYSTEM_INVALID_HANDLE )
			return -1;

		unsigned int nSize = filesystem->Size( hFile );
		filesystem->Close( hFile );
		return nSize > 0x7fffffffu ? -1 : (int32_t)nSize;
	}

	virtual int32_t ReadFileOnPath( const char *pPath, const char *pPathId, void *pOut, int32_t nOutSize ) OVERRIDE
	{
		if ( pPath == NULL || pPath[0] == '\0' || pPathId == NULL || pOut == NULL || nOutSize < 0 ||
			 filesystem == NULL )
			return -1;

		FileHandle_t hFile = filesystem->Open( pPath, "rb", pPathId );
		if ( hFile == FILESYSTEM_INVALID_HANDLE )
			return -1;

		unsigned int nSize = filesystem->Size( hFile );
		if ( nSize > (unsigned int)nOutSize )
		{
			filesystem->Close( hFile );
			return -1;
		}

		int nRead = filesystem->Read( pOut, (int)nSize, hFile );
		filesystem->Close( hFile );
		return nRead;
	}

	// Builds the world from keys managed parsed (MapEntity_SpawnWorld).
	virtual int32_t SpawnWorld( const char *const *ppKeys, const char *const *ppValues, int32_t nKeys ) OVERRIDE
	{
		if ( nKeys < 0 || ( nKeys > 0 && ( ppKeys == NULL || ppValues == NULL ) ) )
			return 0;

		return MapEntity_SpawnWorld( ppKeys, ppValues, nKeys ) ? 1 : 0;
	}

	virtual int32_t IsPrecacheAllowed() OVERRIDE { return EntityPrecache_IsAllowed() ? 1 : 0; }

	// The level's load type, chosen by managed.
	virtual void SetLoadType( int32_t eLoadType ) OVERRIDE { gpGlobals->eLoadType = (MapLoadType_t)eLoadType; }

	// The two native systems' stages, each under the model-cache lock stock's
	// game-system walk takes per system. Managed owns the order.
	virtual int32_t PhysicsInit() OVERRIDE
	{
		MDLCACHE_CRITICAL_SECTION();
		return PhysicsGameSystem()->Init() ? 1 : 0;
	}

	virtual void PhysicsLevelInitPreEntity() OVERRIDE
	{
		MDLCACHE_CRITICAL_SECTION();
		PhysicsGameSystem()->LevelInitPreEntity();
	}

	virtual void PhysicsLevelInitPostEntity() OVERRIDE
	{
		MDLCACHE_CRITICAL_SECTION();
		PhysicsGameSystem()->LevelInitPostEntity();
	}

	virtual void PhysicsLevelShutdownPreEntity() OVERRIDE
	{
		MDLCACHE_CRITICAL_SECTION();
		PhysicsGameSystem()->LevelShutdownPreEntity();
	}

	virtual void PhysicsLevelShutdownPostEntity() OVERRIDE
	{
		MDLCACHE_CRITICAL_SECTION();
		PhysicsGameSystem()->LevelShutdownPostEntity();
	}

	virtual void StringPoolLevelShutdownPostEntity() OVERRIDE
	{
		MDLCACHE_CRITICAL_SECTION();
		GameStringSystem()->LevelShutdownPostEntity();
	}

	// After the managed level's end, since a state object may write during its
	// entity's teardown.
	virtual void ResetEntitySlots() OVERRIDE { SourceSharp_ResetEntitySlots(); }
};

} // namespace

static CShimLevel g_ShimLevel;

// The internalFactory's row for this area.
void *ShimLevel_Acquire()
{
	return static_cast<IShimLevel *>( &g_ShimLevel );
}
