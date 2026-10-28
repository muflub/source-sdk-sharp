// CServerGameClients over a fake engine, a fake net channel and a managed clients
// table: what it answers the engine about a client, and what it hands managed code.

#include "gtest/gtest.h"

#include "shell_gameclients.h"
#include "shell_managed_areas.h"
#include "server_state.h"
#include "imanaged_areas.h"
#include "fakes/fake_engine_server.g.h"
#include "fakes/fake_net_channel.g.h"
#include "fakes/fake_mdl_cache.g.h"
#include "shell_standalone_player.h"
#include "ss_shareddefs.h"

#include "mathlib_test_env.h"

#include <cstring>
#include <string>
#include <vector>

class IGameRules;

// The links this file's subject imports. No client here has a player, so the
// player-side ones are never reached; each answers as an empty server would.
IGameRules *g_pGameRules = nullptr;

void ClientActive( edict_t *, bool ) {}

void ClientPutInServer( CServerEntityContext &, edict_t *, const char * ) {}

void SourceSharp_Trace( const char *, const char * ) {}

CStandalonePlayer *StandalonePlayer_Create( CServerEntityContext &, edict_t *, const char * )
{
	return nullptr;
}

void UTIL_SetCommandClientIndex( int ) {}

void StandalonePlayer_ClientCommand( CStandalonePlayer *, const CCommand & ) {}

int SourceSharp_AreaPortalVisibility( const float *, float, unsigned char *, int )
{
	return -1;
}

int SourceSharp_EarPositionForClient( int, float * )
{
	return 0;
}

void SourceSharp_AnnounceStandaloneRemoved( CStandaloneEntity * ) {}

void CStandalonePlayer::SetPlayerName( const char * ) {}

void CStandalonePlayer::QueuedUsercmds( int ) {}

CStandalonePlayer *CStandalonePlayer::FromEdict( const edict_t * )
{
	return nullptr;
}

namespace ss
{
void SoundEnt_ResetClientSound( int ) {}
}

uint32_t CStandaloneEntity::ManagedHandle() const
{
	return 0;
}

int CStandaloneEntity::EntIndex() const
{
	return 0;
}
class IStaticPropMgrServer;

IStaticPropMgrServer *ServerStaticProps()
{
	return nullptr;
}

namespace
{
constexpr int kEdicts = 4;

class CRecordingEngine : public CFakeVEngineServer
{
public:
	int IndexOfEdict( const edict_t *pEdict ) override { return (int)( pEdict - m_Edicts ); }

	INetChannelInfo *GetPlayerNetInfo( int playerIndex ) override
	{
		return playerIndex == m_nChannelSlot ? m_pChannel : nullptr;
	}

	void ResetPVS( byte *pvs, int pvssize ) override { m_nResets++; }

	void AddOriginToPVS( const Vector &origin ) override { m_nOrigins++; }

	edict_t m_Edicts[kEdicts]{};
	int m_nChannelSlot = -1;
	INetChannel *m_pChannel = nullptr;
	int m_nResets = 0;
	int m_nOrigins = 0;
};

class CRecordingChannel : public CFakeNetChannel
{
public:
	bool RegisterMessage( INetMessage *msg ) override
	{
		m_Registered.push_back( msg );
		return true;
	}

	std::vector<INetMessage *> m_Registered;
};

// A managed clients object whose table records the one slot these facts reach.
struct ClientMessage_t
{
	int nEntIndex;
	std::string key;
};

std::vector<ClientMessage_t> g_Heard;

int32_t RecordNetMessage( IManagedClients *, int32_t nEntIndex, const char *pKey, const uint8_t *, int32_t )
{
	g_Heard.push_back( { nEntIndex, pKey } );
	return 1;
}

class ServerGameClients : public ::testing::Test
{
protected:
	ServerGameClients()
	{
		g_Heard.clear();
		m_Globals.maxClients = kEdicts - 1;
		m_Globals.interval_per_tick = 0.015f;
	}

	CGlobalVars m_Globals{ false };
	CRecordingEngine m_Engine;
	CManagedHolders m_Holders{};
	CManagedAreas &m_Managed = m_Holders.m_Areas;
	CFakeMDLCache m_MdlCache;
	CServerPlayersDll m_Players;
	unsigned short m_nWorldSerial = 0;
	CServerEntityContext m_EntityContext{ &m_Engine, nullptr, nullptr, m_Holders, m_Players, m_nWorldSerial };
	CServerGameClients m_Clients{ &m_Engine, &m_Globals, &m_MdlCache, m_Managed, m_EntityContext };
};
} // namespace

TEST_F( ServerGameClients, AnEdictWithNoPlayerHasItsOwnPlayerStateEveryTime )
{
	CPlayerState *pOne = m_Clients.GetPlayerState( &m_Engine.m_Edicts[1] );
	CPlayerState *pTwo = m_Clients.GetPlayerState( &m_Engine.m_Edicts[2] );

	ASSERT_NE( nullptr, pOne ) << "the engine writes through this unchecked";
	EXPECT_NE( pOne, pTwo ) << "the HLTV and Replay masters must not share one";
	EXPECT_EQ( pOne, m_Clients.GetPlayerState( &m_Engine.m_Edicts[1] ) );
	EXPECT_EQ( m_Clients.GetPlayerState( nullptr ), m_Clients.GetPlayerState( &m_Engine.m_Edicts[0] ) )
		<< "no edict answers slot 0's state";
}

TEST_F( ServerGameClients, ThePlayerLimitsAreStocks )
{
	int nMin = 0, nMax = 0, nDefault = 0;
	m_Clients.GetPlayerLimits( nMin, nMax, nDefault );

	EXPECT_EQ( 1, nMin );
	EXPECT_EQ( ss::kMaxPlayers, nMax );
	EXPECT_EQ( 16, nDefault );
}

TEST_F( ServerGameClients, UsercmdsWithNoManagedDecoderRefuseThePacket )
{
	unsigned char data[8] = {};
	bf_read buf( data, sizeof( data ) );

	EXPECT_EQ( 0.0f, m_Clients.ProcessUsercmds( &m_Engine.m_Edicts[1], &buf, 1, 1, 0, false, false ) );
	EXPECT_TRUE( buf.IsOverflowed() );
}

TEST_F( ServerGameClients, AClientWithNoPlayerSeesOnlyTheResetPvs )
{
	unsigned char pvs[32] = {};

	m_Clients.ClientSetupVisibility( nullptr, &m_Engine.m_Edicts[1], pvs, sizeof( pvs ) );

	EXPECT_EQ( 1, m_Engine.m_nResets );
	EXPECT_EQ( 0, m_Engine.m_nOrigins );
}

TEST_F( ServerGameClients, AnActiveClientsMessageReachesManagedWithItsOwner )
{
	CRecordingChannel channel;
	m_Engine.m_nChannelSlot = 2;
	m_Engine.m_pChannel = &channel;

	SSManagedClientsTable table{};
	table.OnClientNetMessage = RecordNetMessage;
	IManagedClients managed( &table );
	m_Managed.m_pClients = &managed;

	m_Clients.ClientActive( &m_Engine.m_Edicts[2], false );

	ASSERT_EQ( 1u, channel.m_Registered.size() ) << "the message is registered on the client's channel";
	CSourceSharpClientMessage *pMsg = static_cast<CSourceSharpClientMessage *>( channel.m_Registered[0] );
	ASSERT_TRUE( pMsg->Set( "ping", nullptr, 0 ) );
	EXPECT_TRUE( pMsg->Process() );

	ASSERT_EQ( 1u, g_Heard.size() );
	EXPECT_EQ( 2, g_Heard[0].nEntIndex );
	EXPECT_EQ( "ping", g_Heard[0].key );
	delete pMsg;
}

TEST_F( ServerGameClients, AMessageWithNoManagedSideIsKeptNotDropped )
{
	CRecordingChannel channel;
	m_Engine.m_nChannelSlot = 1;
	m_Engine.m_pChannel = &channel;

	m_Clients.ClientActive( &m_Engine.m_Edicts[1], false );

	ASSERT_EQ( 1u, channel.m_Registered.size() );
	CSourceSharpClientMessage *pMsg = static_cast<CSourceSharpClientMessage *>( channel.m_Registered[0] );
	EXPECT_TRUE( pMsg->Process() ) << "false would make the engine drop the client";
	delete pMsg;
}

TEST( LateBoundServerGameClients, ForwardsToTheImplementationItIsBoundTo )
{
	CGlobalVars globals( false );
	CRecordingEngine engine;
	CManagedHolders holders{};
	CServerPlayersDll players;
	unsigned short nWorldSerial = 0;
	CServerEntityContext entities( &engine, nullptr, nullptr, holders, players, nWorldSerial );
	CServerGameClients impl( &engine, &globals, nullptr, holders.m_Areas, entities );
	CRootServerGameClients forwarder;

	forwarder.Bind( &impl );

	EXPECT_EQ( impl.GetPlayerState( &engine.m_Edicts[1] ), forwarder.GetPlayerState( &engine.m_Edicts[1] ) );
	int nMin = 0, nMax = 0, nDefault = 0;
	forwarder.GetPlayerLimits( nMin, nMax, nDefault );
	EXPECT_EQ( 16, nDefault );
}

TEST( RootServerGameClients, AnswersThePlayerLimitsBeforeAnythingIsBound )
{
	// The engine asks while loading the DLL, before DLLInit binds the DLL tier.
	CRootServerGameClients root;

	int nMin = 0, nMax = 0, nDefault = 0;
	root.GetPlayerLimits( nMin, nMax, nDefault );

	EXPECT_EQ( 1, nMin );
	EXPECT_EQ( ss::kMaxPlayers, nMax );
	EXPECT_EQ( 16, nDefault );
}
