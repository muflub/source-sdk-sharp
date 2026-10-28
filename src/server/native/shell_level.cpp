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
#include "precache_model.h"
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

// Defined in gamestringpool.cpp and shell_entityslots.cpp.
void SourceSharp_ResetEntitySlots();

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
	m_Precache.SetAllowed( bAllowed != 0 );
}

void CServerLevel::ShutdownStandaloneEntities()
{
	StandaloneEntities_LevelShutdown( m_Entities );
}

void CServerLevel::ShutdownSoundEnt()
{
	ss::SoundEnt_Shutdown( m_Entities.m_Managed.m_Areas.m_pSoundEnt );
}

void CServerLevel::ClearEntityList()
{
	// Must not be guarded: a world left alive faults the next map load.
	if ( CWorld *pWorld = m_Entities.m_pLevel ? m_Entities.m_pLevel->m_pWorld : nullptr )
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

	return MapEntity_SpawnWorld( m_Factories, m_Entities, ppKeys, ppValues, nKeys ) ? 1 : 0;
}

int32_t CServerLevel::IsPrecacheAllowed()
{
	return m_Precache.IsAllowed() ? 1 : 0;
}

void CServerLevel::SetLoadType( int32_t eLoadType )
{
	m_pGlobals->eLoadType = (MapLoadType_t)eLoadType;
}

int32_t CServerLevel::PhysicsInit()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	return m_Physics.Init() ? 1 : 0;
}

void CServerLevel::PhysicsLevelInitPreEntity()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	m_Physics.LevelInitPreEntity();
}

void CServerLevel::PhysicsLevelInitPostEntity()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	m_Physics.LevelInitPostEntity();
}

void CServerLevel::PhysicsLevelShutdownPreEntity()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	m_Physics.LevelShutdownPreEntity();
}

void CServerLevel::PhysicsLevelShutdownPostEntity()
{
	MDLCACHE_CRITICAL_SECTION_( m_pMdlCache );
	m_Physics.LevelShutdownPostEntity();
}

// Nothing to free: a pooled string belongs to the pool of the entity that holds it, which
// the level's teardown frees (ss_gamestringpool.h).
void CServerLevel::StringPoolLevelShutdownPostEntity() {}

void CServerLevel::ResetEntitySlots()
{
	SourceSharp_ResetEntitySlots();
}
