// CServerLevel over generated fakes: the file reads, the load-game read and the load
// type managed hands it, and the world keys it refuses.

#include "gtest/gtest.h"

#include "shell_level.h"
#include "server_state.h"
#include "eiface.h"
#include "fakes/fake_engine_server.g.h"
#include "fakes/fake_base_file_system.g.h"
#include "fakes/fake_mdl_cache.g.h"
#include "igameentity_fwd.h"
#include "shell_resolve.h"
#include "shell_serversystem.h"
#include "shell_standalone.h"
#include "ss_world.h"
#include "ss_soundent.h"

#include "mathlib_test_env.h"

#include <cstring>
#include <string>

// The links this file's subject imports. The world spawn records whether it ran;
// nothing else is reached by these facts.
static int s_nWorldSpawns = 0;

bool MapEntity_SpawnWorld( const char *const *, const char *const *, int )
{
	s_nWorldSpawns++;
	return true;
}

CWorld *GetWorldEntity()
{
	return nullptr;
}

CServerSystem *GameStringSystem()
{
	return nullptr;
}

CServerSystem *PhysicsGameSystem()
{
	return nullptr;
}

uint32_t SourceSharp_CreateEntity( const char * )
{
	return INVALID_EHANDLE_INDEX;
}

SourceSharpEntity_t SourceSharp_ResolveEntity( uint32_t )
{
	return SourceSharpEntity_t();
}

void SourceSharp_ResetEntitySlots() {}

static const CServerEntityContext *s_pShutDownWith = nullptr;

void StandaloneEntities_LevelShutdown( const CServerEntityContext &context )
{
	s_pShutDownWith = &context;
}

bool CStandaloneEntity::KeyValueShellOnly( const char *, const char * )
{
	return false;
}

const model_t *CWorldCollideable::GetCollisionModel()
{
	return nullptr;
}

namespace ss
{
void SoundEnt_Shutdown() {}
}

namespace
{
// One file, "maps/a.txt" on "GAME", holding m_Contents; every open is closed.
class COneFileSystem : public CFakeBaseFileSystem
{
public:
	FileHandle_t Open( const char *pFileName, const char *pOptions, const char *pathID = 0 ) override
	{
		if ( std::string( pFileName ) != "maps/a.txt" || std::string( pathID ? pathID : "" ) != "GAME" )
			return FILESYSTEM_INVALID_HANDLE;
		m_nOpen++;
		return (FileHandle_t)this;
	}

	void Close( FileHandle_t file ) override { m_nOpen--; }

	unsigned int Size( FileHandle_t file ) override { return (unsigned int)m_Contents.size(); }

	int Read( void *pOutput, int size, FileHandle_t file ) override
	{
		memcpy( pOutput, m_Contents.data(), (size_t)size );
		return size;
	}

	std::string m_Contents = "hello";
	int m_nOpen = 0;
};

class CLoadGameEngine : public CFakeVEngineServer
{
public:
	bool LoadGameState( char const *pMapName, bool createPlayers ) override
	{
		m_LoadedMap = pMapName;
		m_bCreatePlayers = createPlayers;
		return true;
	}

	std::string m_LoadedMap;
	bool m_bCreatePlayers = false;
};

class ServerLevel : public ::testing::Test
{
protected:
	ServerLevel() { s_nWorldSpawns = 0; }

	CLoadGameEngine m_Engine;
	COneFileSystem m_Files;
	CFakeMDLCache m_MdlCache;
	CGlobalVars m_Globals{ false };
	CManagedHolders m_Holders{};
	CServerPlayersDll m_Players;
	unsigned short m_nWorldSerial = 0;
	CServerEntityContext m_Entities{ &m_Engine, nullptr, nullptr, m_Holders, m_Players, m_nWorldSerial };
	CServerLevel m_Level{ &m_Engine, &m_Files, &m_MdlCache, &m_Globals, m_Entities };
};
} // namespace

TEST_F( ServerLevel, AFilesSizeIsAnsweredAndItIsClosed )
{
	EXPECT_EQ( 5, m_Level.FileSizeOnPath( "maps/a.txt", "GAME" ) );
	EXPECT_EQ( 0, m_Files.m_nOpen );
}

TEST_F( ServerLevel, AMissingFileOrPathHasNoSize )
{
	EXPECT_EQ( -1, m_Level.FileSizeOnPath( "maps/b.txt", "GAME" ) );
	EXPECT_EQ( -1, m_Level.FileSizeOnPath( "maps/a.txt", "MOD" ) );
	EXPECT_EQ( -1, m_Level.FileSizeOnPath( "", "GAME" ) );
	EXPECT_EQ( -1, m_Level.FileSizeOnPath( "maps/a.txt", nullptr ) );
}

TEST_F( ServerLevel, AFileThatFitsIsReadWhole )
{
	char buf[8] = {};

	EXPECT_EQ( 5, m_Level.ReadFileOnPath( "maps/a.txt", "GAME", buf, sizeof( buf ) ) );
	EXPECT_EQ( std::string( "hello" ), std::string( buf, 5 ) );
	EXPECT_EQ( 0, m_Files.m_nOpen );
}

TEST_F( ServerLevel, AFileThatDoesNotFitIsNotReadAndIsClosed )
{
	char buf[4] = {};

	EXPECT_EQ( -1, m_Level.ReadFileOnPath( "maps/a.txt", "GAME", buf, sizeof( buf ) ) );
	EXPECT_EQ( 0, buf[0] );
	EXPECT_EQ( 0, m_Files.m_nOpen ) << "a refused read still closes the file";
}

TEST_F( ServerLevel, TheLoadGameReadIsTheEnginesAndANullMapIsRefused )
{
	EXPECT_EQ( 1, m_Level.LoadSaveGameState( "dm_lockdown", 1 ) );
	EXPECT_EQ( "dm_lockdown", m_Engine.m_LoadedMap );
	EXPECT_TRUE( m_Engine.m_bCreatePlayers );

	EXPECT_EQ( 0, m_Level.LoadSaveGameState( nullptr, 1 ) );
}

TEST_F( ServerLevel, TheLoadTypeIsWrittenToTheGlobals )
{
	m_Level.SetLoadType( MapLoad_Transition );

	EXPECT_EQ( MapLoad_Transition, m_Globals.eLoadType );
}

TEST_F( ServerLevel, WorldKeysWithoutTheirArraysAreRefusedBeforeTheSpawn )
{
	EXPECT_EQ( 0, m_Level.SpawnWorld( nullptr, nullptr, 2 ) );
	EXPECT_EQ( 0, m_Level.SpawnWorld( nullptr, nullptr, -1 ) );
	EXPECT_EQ( 0, s_nWorldSpawns );

	EXPECT_EQ( 1, m_Level.SpawnWorld( nullptr, nullptr, 0 ) );
	EXPECT_EQ( 1, s_nWorldSpawns );
}

TEST_F( ServerLevel, TheStandaloneTeardownIsHandedTheLevelsEntityContext )
{
	s_pShutDownWith = nullptr;

	m_Level.ShutdownStandaloneEntities();

	EXPECT_EQ( &m_Entities, s_pShutDownWith );
}
