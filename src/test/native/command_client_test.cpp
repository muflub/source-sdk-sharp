// CServerEntityContext::CommandIssuedByServerAdmin over the generated engine fake: who may
// run an admin command, from the console client slot the engine names.

#include "gtest/gtest.h"

#include "server_entity_context.h"
#include "server_state.h"
#include "shell_managed_areas.h"
#include "precache_model.h"
#include "fakes/fake_engine_server.g.h"

#include "mathlib_test_env.h"

// The links this file's subject imports.
void SourceSharp_Trace( const char *, const char * ) {}

int SourceSharp_GetVerbosity()
{
	return 0;
}

namespace
{
class CDedicatedEngine : public CFakeVEngineServer
{
public:
	bool IsDedicatedServer() override { return m_bDedicated; }

	bool m_bDedicated = false;
};

class CommandClient : public ::testing::Test
{
protected:
	bool Admin( int nSlot, bool bDedicated )
	{
		m_Engine.m_bDedicated = bDedicated;
		m_Players.m_nCommandClientSlot = nSlot;
		return m_Entities.CommandIssuedByServerAdmin();
	}

	CDedicatedEngine m_Engine;
	CManagedHolders m_Holders{};
	CServerPlayersDll m_Players;
	unsigned short m_nWorldSerial = 0;
	bool m_bManagedLoaded = false;
	unsigned int m_nManagedGeneration = 1;
	CServerModelPrecache m_Precache{ &m_Engine, nullptr, m_Holders.m_Areas };
	CServerNetTables m_NetTables;
	CSourceSharpSlotTable m_SlotTableForContext;
	CSourceSharpEntitySlots m_EntitySlotsForContext;
	CServerEntityContext m_Entities{ &m_Engine,
									 nullptr,
									 nullptr,
									 nullptr,
									 nullptr,
									 nullptr,
									 nullptr,
									 m_Holders,
									 m_bManagedLoaded,
									 m_nManagedGeneration,
									 m_Players,
									 m_nWorldSerial,
									 m_Precache,
									 m_NetTables,
									 m_SlotTableForContext,
									 m_EntitySlotsForContext };
};
} // namespace

TEST_F( CommandClient, TheDedicatedConsoleIsAnAdmin )
{
	EXPECT_TRUE( Admin( -1, true ) );
}

TEST_F( CommandClient, TheListenServersHostIsAnAdmin )
{
	EXPECT_TRUE( Admin( 0, false ) );
}

TEST_F( CommandClient, NoPlayerOnADedicatedServerIsAnAdmin )
{
	EXPECT_FALSE( Admin( 0, true ) );
}

TEST_F( CommandClient, ASecondPlayerIsNotAnAdmin )
{
	EXPECT_FALSE( Admin( 1, false ) );
}

TEST_F( CommandClient, StocksInitialSlotIsPlayerOne )
{
	EXPECT_EQ( 0, CServerPlayersDll().m_nCommandClientSlot );
}
