// CServerModelPrecache over generated fakes: what it hands the managed precache check,
// the engine's index it answers, and the names it refuses before the engine sees them.

#include "gtest/gtest.h"

#include "precache_model.h"
#include "shell_managed_areas.h"
#include "imanaged_areas.h"
#include "eiface.h"
#include "fakes/fake_engine_server.g.h"
#include "fakes/fake_model_info.g.h"

#include "mathlib_test_env.h"

#include <string>
#include <utility>
#include <vector>

// The links this file's subject imports.
void SourceSharp_Trace( const char *, const char * ) {}

int SourceSharp_GetVerbosity()
{
	return 0;
}

namespace
{
class CPrecachingEngine : public CFakeVEngineServer
{
public:
	int PrecacheModel( const char *s, bool preload ) override
	{
		m_Precached.push_back( s );
		return m_nIndex;
	}

	int PrecacheSentenceFile( const char *s, bool preload ) override
	{
		m_SentenceFiles.push_back( s );
		return 0;
	}

	std::vector<std::string> m_Precached;
	std::vector<std::string> m_SentenceFiles;
	int m_nIndex = 7;
};

// The managed check each model is put to, with the window as it stood.
std::vector<std::pair<std::string, bool>> g_Checked;

void RecordCheck( IManagedResources *, const char *pszModelName, bool bAllowed )
{
	g_Checked.push_back( { pszModelName, bAllowed } );
}

class ServerModelPrecache : public ::testing::Test
{
protected:
	ServerModelPrecache()
	{
		g_Checked.clear();
		m_Table.ModelPrecacheCheck = RecordCheck;
		m_Holders.m_Areas.m_pResources = &m_Resources;
	}

	CPrecachingEngine m_Engine;
	CFakeVModelInfo m_ModelInfo;
	SSManagedResourcesTable m_Table{};
	IManagedResources m_Resources{ &m_Table };
	CManagedHolders m_Holders{};
	CServerModelPrecache m_Precache{ &m_Engine, &m_ModelInfo, m_Holders.m_Areas };
};
} // namespace

TEST_F( ServerModelPrecache, TheManagedCheckIsToldWhetherTheWindowIsOpen )
{
	m_Precache.Model( "models/a.mdl" );
	m_Precache.SetAllowed( true );
	m_Precache.Model( "models/b.mdl" );

	ASSERT_EQ( 2u, g_Checked.size() );
	EXPECT_EQ( "models/a.mdl", g_Checked[0].first );
	EXPECT_FALSE( g_Checked[0].second );
	EXPECT_EQ( "models/b.mdl", g_Checked[1].first );
	EXPECT_TRUE( g_Checked[1].second );
}

TEST_F( ServerModelPrecache, TheSlotAnswersTheEnginesIndex )
{
	EXPECT_EQ( 7, m_Precache.PrecacheModel( "models/a.mdl" ) );

	m_Engine.m_nIndex = -1;
	EXPECT_EQ( -1, m_Precache.PrecacheModel( "models/missing.mdl" ) );

	EXPECT_EQ( ( std::vector<std::string>{ "models/a.mdl", "models/missing.mdl" } ), m_Engine.m_Precached );
}

TEST_F( ServerModelPrecache, ANullOrEmptyNameNeverReachesTheEngine )
{
	EXPECT_EQ( -1, m_Precache.PrecacheModel( nullptr ) );
	EXPECT_EQ( -1, m_Precache.PrecacheModel( "" ) );
	EXPECT_EQ( -1, m_Precache.Model( "" ) );

	EXPECT_TRUE( m_Engine.m_Precached.empty() );
	EXPECT_TRUE( g_Checked.empty() );
}

TEST_F( ServerModelPrecache, WithNoManagedSideTheEngineStillPrecaches )
{
	m_Holders.m_Areas.m_pResources = nullptr;

	EXPECT_EQ( 7, m_Precache.Model( "models/a.mdl" ) );
	EXPECT_TRUE( g_Checked.empty() );
}

// Every world spawn asks; the DLL precaches the sentence file once.
TEST_F( ServerModelPrecache, TheSentenceFileIsPrecachedOncePerDll )
{
	m_Precache.SentenceFile();
	m_Precache.SentenceFile();

	EXPECT_EQ( ( std::vector<std::string>{ "scripts/sentences.txt" } ), m_Engine.m_SentenceFiles );
}
