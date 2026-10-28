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
#include "server_state.h"
#include "shell_level.h"
#include "datacache/imdlcache.h"
#include "ss_mapentities.h"
#include "ss_soundent.h"
#include "entity_shared_statics.h"

#include "ishim_level.h"
#include "shell_standalone.h"

#include "ss_world.h"
#include "shell_serversystem.h"

// Defined in shell_standalone_base.cpp.
void StandaloneEntities_LevelShutdown( const CServerEntityContext &context );

// Defined in physics.cpp, gamestringpool.cpp and shell_entityslots.cpp.
CServerSystem *PhysicsGameSystem();
CServerSystem *GameStringSystem();
void SourceSharp_ResetEntitySlots();

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

void CServerLevel::PrecachePointTemplates( const char *pMapDataBlobs, const int32_t *pStringSizes, int32_t nCount )
{
	(void)pMapDataBlobs;
	(void)pStringSizes;
	(void)nCount;
}

void CServerLevel::SetLowViolence( const char *pMapName )
{
	(void)pMapName;
}

void CServerLevel::LoadMessageOfTheDay() {}

void CServerLevel::SetPrecacheAllowed( int32_t bAllowed )
{
	EntityPrecache_SetAllowed( bAllowed != 0 );
}

void CServerLevel::ShutdownStandaloneEntities()
{
	StandaloneEntities_LevelShutdown( m_Entities );
}

void CServerLevel::ShutdownSoundEnt()
{
	ss::SoundEnt_Shutdown();
}

void CServerLevel::ClearEntityList()
{
	// Must not be guarded: a world left alive faults the next map load.
	if ( CWorld *pWorld = GetWorldEntity() )
		pWorld->GetNetworkable()->Release();
}

int32_t CServerLevel::LoadSaveGameState( const char *pMapName, int32_t bCreatePlayers )
{
	if ( pMapName == NULL )
		return 0;

	return m_pEngine->LoadGameState( pMapName, bCreatePlayers != 0 ) ? 1 : 0;
}

// Open/Size/Close: Size( path ) answers 0 for a missing file.
int32_t CServerLevel::FileSizeOnPath( const char *pPath, const char *pPathId )
{
	if ( pPath == NULL || pPath[0] == '\0' || pPathId == NULL )
		return -1;

	FileHandle_t hFile = m_pFileSystem->Open( pPath, "rb", pPathId );
	if ( hFile == FILESYSTEM_INVALID_HANDLE )
		return -1;

	unsigned int nSize = m_pFileSystem->Size( hFile );
	m_pFileSystem->Close( hFile );
	return nSize > 0x7fffffffu ? -1 : (int32_t)nSize;
}

int32_t CServerLevel::ReadFileOnPath( const char *pPath, const char *pPathId, void *pOut, int32_t nOutSize )
{
	if ( pPath == NULL || pPath[0] == '\0' || pPathId == NULL || pOut == NULL || nOutSize < 0 )
		return -1;

	FileHandle_t hFile = m_pFileSystem->Open( pPath, "rb", pPathId );
	if ( hFile == FILESYSTEM_INVALID_HANDLE )
		return -1;

	unsigned int nSize = m_pFileSystem->Size( hFile );
	if ( nSize > (unsigned int)nOutSize )
	{
		m_pFileSystem->Close( hFile );
		return -1;
	}

	int nRead = m_pFileSystem->Read( pOut, (int)nSize, hFile );
	m_pFileSystem->Close( hFile );
	return nRead;
}

int32_t CServerLevel::SpawnWorld( const char *const *ppKeys, const char *const *ppValues, int32_t nKeys )
{
	if ( nKeys < 0 || ( nKeys > 0 && ( ppKeys == NULL || ppValues == NULL ) ) )
		return 0;

	return MapEntity_SpawnWorld( ppKeys, ppValues, nKeys ) ? 1 : 0;
}

int32_t CServerLevel::IsPrecacheAllowed()
{
	return EntityPrecache_IsAllowed() ? 1 : 0;
}

void CServerLevel::SetLoadType( int32_t eLoadType )
{
	m_pGlobals->eLoadType = (MapLoadType_t)eLoadType;
}

int32_t CServerLevel::PhysicsInit()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	return PhysicsGameSystem()->Init() ? 1 : 0;
}

void CServerLevel::PhysicsLevelInitPreEntity()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	PhysicsGameSystem()->LevelInitPreEntity();
}

void CServerLevel::PhysicsLevelInitPostEntity()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	PhysicsGameSystem()->LevelInitPostEntity();
}

void CServerLevel::PhysicsLevelShutdownPreEntity()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	PhysicsGameSystem()->LevelShutdownPreEntity();
}

void CServerLevel::PhysicsLevelShutdownPostEntity()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	PhysicsGameSystem()->LevelShutdownPostEntity();
}

void CServerLevel::StringPoolLevelShutdownPostEntity()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	GameStringSystem()->LevelShutdownPostEntity();
}

void CServerLevel::ResetEntitySlots()
{
	SourceSharp_ResetEntitySlots();
}
