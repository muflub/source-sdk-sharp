// CServerEntityResolver over an empty slot table and no level: what a handle that names
// nothing resolves to, and which of those it counts as reaching from the engine-invisible
// range.

#include "gtest/gtest.h"
#include "mathlib_test_env.h"

#include "server_entity_resolver.h"
#include "server_entity_context.h"
#include "server_state.h"
#include "slot_table.h"
#include "basehandle.h"
#include "precache_model.h"
#include "ss_world.h"

// The links this file's subject imports. The wrappers are not reached here.
CServerDllState *ShellGameDll_Dll()
{
	return nullptr;
}

void SourceSharp_Trace( const char *, const char * ) {}

int SourceSharp_GetVerbosity()
{
	return 0;
}

class IStaticPropMgrServer;

IStaticPropMgrServer *ServerStaticProps()
{
	return nullptr;
}

const model_t *CWorldCollideable::GetCollisionModel()
{
	return nullptr;
}

namespace
{
class ServerEntityResolver : public ::testing::Test
{
protected:
	CManagedHolders m_Holders{};
	CServerPlayersDll m_Players;
	unsigned short m_nWorldSerial = 0;
	bool m_bManagedLoaded = false;
	unsigned int m_nManagedGeneration = 1;
	CServerModelPrecache m_Precache{ nullptr, nullptr, m_Holders.m_Areas };
	CServerEntityContext m_Entities{
		nullptr,   nullptr,        nullptr,   nullptr, m_Holders, m_bManagedLoaded, m_nManagedGeneration,
		m_Players, m_nWorldSerial, m_Precache };
	CSourceSharpSlotTable m_Slots;
	int32_t m_nReach = 0;
	CServerEntityResolver m_Resolver{ m_Slots, m_Entities, m_nReach };
};
} // namespace

TEST_F( ServerEntityResolver, TheInvalidHandleResolvesToNothingAndIsNotCounted )
{
	EXPECT_FALSE( m_Resolver.Resolve( INVALID_EHANDLE_INDEX ).IsValid() );
	EXPECT_EQ( 0, m_nReach );
}

TEST_F( ServerEntityResolver, AnUnboundHandleInTheInvisibleRangeIsCounted )
{
	const uint32_t hInvisible = (uint32_t)CBaseHandle( MAX_EDICTS + 5, 1 ).ToInt();

	EXPECT_FALSE( m_Resolver.Resolve( hInvisible ).IsValid() );
	EXPECT_EQ( nullptr, m_Resolver.Standalone( hInvisible ) );
	EXPECT_EQ( 2, m_nReach );
}

TEST_F( ServerEntityResolver, AnUnboundHandleTheEngineCanSeeIsNotCounted )
{
	const uint32_t hVisible = (uint32_t)CBaseHandle( 5, 1 ).ToInt();

	EXPECT_FALSE( m_Resolver.Resolve( hVisible ).IsValid() );
	EXPECT_EQ( nullptr, m_Resolver.Player( hVisible ) );
	EXPECT_EQ( 0, m_nReach );
}

TEST_F( ServerEntityResolver, WithNoLevelThereIsNoWorldToMatch )
{
	EXPECT_EQ( nullptr, m_Resolver.World() );
	EXPECT_EQ( nullptr, m_Resolver.WorldFromHandle( CBaseHandle( 0, 1 ) ) );
	EXPECT_EQ( nullptr, m_Resolver.EntityFromHandle( CBaseHandle( 0, 1 ) ) );
	EXPECT_EQ( nullptr, m_Resolver.ShellFromIndex( 3 ) );
}
