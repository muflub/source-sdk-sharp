// CServerGameEnts' transmit walk over generated fakes: which edicts one client is sent,
// and what the armed transmit capture records.

#include "gtest/gtest.h"

#include "shell_gameents.h"
#include "late_bound_game_ents.g.h"
#include "shell_managed_areas.h"
#include "imanaged_areas.h"
#include "shell_steporder.h"
#include "basehandle.h"
#include "fakes/fake_engine_server.g.h"
#include "fakes/fake_mdl_cache.g.h"
#include "fakes/fake_collideable.g.h"
#include "fakes/fake_server_entity.g.h"

#include "mathlib_test_env.h"

#include <memory>

// The links this file's subject imports. Every recipient's skybox area is 3 unless a
// fact declines it.
static bool s_bDeclineRecipient = false;

int SourceSharp_SkyboxAreaForClient( int, int *pOutArea )
{
	*pOutArea = 3;
	return s_bDeclineRecipient ? 0 : 1;
}

IGameEntity *SourceSharp_GameEntityFromHandleEntity( IHandleEntity * )
{
	return nullptr;
}

void UTIL_ClearTrace( trace_t & ) {}

namespace
{
constexpr int kEdicts = 8;

class CPvsEngine : public CFakeVEngineServer
{
public:
	edict_t *PEntityOfEntIndex( int iEntIndex ) override { return &m_Edicts[iEntIndex]; }

	int GetEntityCount() override { return kEdicts; }

	bool CheckBoxInPVS( const Vector &mins, const Vector &maxs, const byte *, int ) override { return mins.x >= 0.0f; }

	edict_t m_Edicts[kEdicts]{};
};

// An entity whose collision box sits on the +X side (in the PVS) or the -X side.
class CBoxCollideable : public CFakeCollideable
{
public:
	explicit CBoxCollideable( float flX )
		: m_flX( flX )
	{
	}

	void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) override
	{
		pMins->Init( m_flX, 0, 0 );
		pMaxs->Init( m_flX + 1, 1, 1 );
	}

	float m_flX;
};

class CBoxEntity : public CFakeServerEntity
{
public:
	explicit CBoxEntity( float flX )
		: m_Box( flX )
	{
	}

	ICollideable *GetCollideable() override { return &m_Box; }

	CBoxCollideable m_Box;
};

int32_t AlwaysEdictFive( IManagedClients *, int32_t )
{
	return 5;
}

class ServerGameEnts : public ::testing::Test
{
protected:
	ServerGameEnts()
	{
		s_bDeclineRecipient = false;
		m_Info.m_pClientEnt = &m_Engine.m_Edicts[1];
		m_Info.m_nPVSSize = 0;
		m_Info.m_pTransmitEdict = &m_Sent;
	}

	void Flag( int iEdict, int nFlags ) { m_Engine.m_Edicts[iEdict].m_fStateFlags = nFlags; }

	void Box( int iEdict, float flX )
	{
		m_Entities.push_back( std::make_unique<CBoxEntity>( flX ) );
		m_Engine.m_Edicts[iEdict].SetEdict( m_Entities.back().get(), true );
		Flag( iEdict, FL_EDICT_FULL | FL_EDICT_PVSCHECK );
	}

	void Walk()
	{
		const unsigned short indices[] = { 1, 2, 3, 4, 5, 6, 7 };
		m_Ents.CheckTransmit( &m_Info, indices, (int)( sizeof( indices ) / sizeof( indices[0] ) ) );
	}

	CPvsEngine m_Engine;
	CFakeMDLCache m_MdlCache;
	CManagedAreas m_Managed{};
	CShellTransmitSet m_TransmitSet;
	CServerGameEnts m_Ents{ &m_Engine, &m_MdlCache, m_Managed, m_TransmitSet };
	std::vector<std::unique_ptr<CBoxEntity>> m_Entities;
	CCheckTransmitInfo m_Info{};
	CBitVec<MAX_EDICTS> m_Sent;
};
} // namespace

TEST_F( ServerGameEnts, AlwaysIsSentDontSendIsNotAndAnUnflaggedEdictIsNot )
{
	Flag( 2, FL_EDICT_ALWAYS );
	Flag( 3, FL_EDICT_DONTSEND | FL_EDICT_ALWAYS );

	Walk();

	EXPECT_TRUE( m_Sent.Get( 2 ) );
	EXPECT_FALSE( m_Sent.Get( 3 ) ) << "DONTSEND wins over ALWAYS";
	EXPECT_FALSE( m_Sent.Get( 4 ) );
}

TEST_F( ServerGameEnts, APvsCheckedEdictIsSentOnlyWhenItsBoxIsInThePvs )
{
	Box( 4, 10.0f );
	Box( 5, -10.0f );

	Walk();

	EXPECT_TRUE( m_Sent.Get( 4 ) );
	EXPECT_FALSE( m_Sent.Get( 5 ) );
}

TEST_F( ServerGameEnts, TheEdictManagedSaysIsAlwaysSentToThisClientIsSentOutsideThePvs )
{
	SSManagedClientsTable table{};
	table.ServerTransmitAlwaysForClient = AlwaysEdictFive;
	IManagedClients managed( &table );
	m_Managed.m_pClients = &managed;
	Box( 5, -10.0f );

	Walk();

	EXPECT_TRUE( m_Sent.Get( 5 ) );
}

TEST_F( ServerGameEnts, ARecipientManagedDeclinesIsSentNothing )
{
	s_bDeclineRecipient = true;
	Flag( 2, FL_EDICT_ALWAYS );

	Walk();

	EXPECT_FALSE( m_Sent.Get( 2 ) );
}

TEST_F( ServerGameEnts, AnArmedCaptureRecordsTheSentSetAndTheSkyboxArea )
{
	Flag( 2, FL_EDICT_ALWAYS );
	m_TransmitSet.Arm();

	Walk();

	ASSERT_TRUE( m_TransmitSet.HaveCapture() );
	EXPECT_EQ( 1, m_TransmitSet.Recipient() );
	EXPECT_EQ( 3, m_TransmitSet.SkyboxArea() );
	EXPECT_TRUE( m_TransmitSet.IsSet( 2 ) );
	EXPECT_FALSE( m_TransmitSet.IsSet( 3 ) );
}

TEST( LateBoundServerGameEnts, ForwardsToTheImplementationItIsBoundTo )
{
	CPvsEngine engine;
	CFakeMDLCache mdlCache;
	CManagedAreas managed{};
	CShellTransmitSet transmitSet;
	CServerGameEnts impl( &engine, &mdlCache, managed, transmitSet );
	CLateBoundServerGameEnts forwarder;
	forwarder.Bind( &impl );

	CCheckTransmitInfo info{};
	CBitVec<MAX_EDICTS> sent;
	info.m_pClientEnt = &engine.m_Edicts[1];
	info.m_pTransmitEdict = &sent;
	engine.m_Edicts[2].m_fStateFlags = FL_EDICT_ALWAYS;
	const unsigned short indices[] = { 2 };

	forwarder.CheckTransmit( &info, indices, 1 );

	EXPECT_TRUE( sent.Get( 2 ) );
}
