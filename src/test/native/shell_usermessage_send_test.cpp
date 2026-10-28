// CServerUserMessages: which players a user message reaches and how, over a fake
// engine handed to its constructor.

#include "gtest/gtest.h"

#include "shell_usermessage_send.h"
#include "fakes/fake_engine_server.g.h"
#include "irecipientfilter.h"

#include <vector>

namespace
{
constexpr int kMaxClients = 4;

struct SentMessage_t
{
	int nMsgId;
	bool bReliable;
	std::vector<int> recipients;
};

class CRecordingEngine : public CFakeVEngineServer
{
public:
	edict_t *PEntityOfEntIndex( int iEntIndex ) override
	{
		return ( iEntIndex >= 0 && iEntIndex <= kMaxClients ) ? &m_Edicts[iEntIndex] : nullptr;
	}

	bf_write *UserMessageBegin( IRecipientFilter *filter, int msg_type ) override
	{
		SentMessage_t sent{ msg_type, filter->IsReliable(), {} };
		for ( int i = 0; i < filter->GetRecipientCount(); i++ )
			sent.recipients.push_back( filter->GetRecipientIndex( i ) );
		m_Sent.push_back( sent );
		return nullptr;
	}

	void MessageEnd() override { m_nEnds++; }

	// A connected player: a non-free edict with an entity behind it.
	void Connect( int nSlot ) { m_Edicts[nSlot].SetEdict( reinterpret_cast<IServerUnknown *>( this ), true ); }

	edict_t m_Edicts[kMaxClients + 1]{};
	std::vector<SentMessage_t> m_Sent;
	int m_nEnds = 0;
};

class ServerUserMessages : public ::testing::Test
{
protected:
	ServerUserMessages() { m_Globals.maxClients = kMaxClients; }

	CGlobalVars m_Globals{ false };
	CRecordingEngine m_Engine;
	CServerUserMessages m_Messages{ &m_Engine, &m_Globals };
};
} // namespace

TEST_F( ServerUserMessages, ABroadcastReachesEveryConnectedPlayerAndNoEmptySlot )
{
	m_Engine.Connect( 1 );
	m_Engine.Connect( 3 );

	EXPECT_EQ( 1, m_Messages.UserMessageSend( 7, nullptr, 0, 0, 0 ) );

	ASSERT_EQ( 1u, m_Engine.m_Sent.size() );
	EXPECT_EQ( 7, m_Engine.m_Sent[0].nMsgId );
	EXPECT_EQ( ( std::vector<int>{ 1, 3 } ), m_Engine.m_Sent[0].recipients );
	EXPECT_EQ( 1, m_Engine.m_nEnds ) << "every UserMessageBegin is closed";
}

TEST_F( ServerUserMessages, AMessageForOneConnectedPlayerReachesOnlyThem )
{
	m_Engine.Connect( 1 );
	m_Engine.Connect( 2 );

	EXPECT_EQ( 1, m_Messages.UserMessageSend( 7, nullptr, 0, 2, 0 ) );

	ASSERT_EQ( 1u, m_Engine.m_Sent.size() );
	EXPECT_EQ( ( std::vector<int>{ 2 } ), m_Engine.m_Sent[0].recipients );
}

TEST_F( ServerUserMessages, AMessageForAnEmptySlotIsNotSentAtAll )
{
	m_Engine.Connect( 1 );

	EXPECT_EQ( 0, m_Messages.UserMessageSend( 7, nullptr, 0, 2, 0 ) );
	EXPECT_EQ( 0, m_Messages.UserMessageSend( 7, nullptr, 0, kMaxClients + 1, 0 ) );

	EXPECT_TRUE( m_Engine.m_Sent.empty() ) << "a message for one player must never become a broadcast";
	EXPECT_EQ( 0, m_Engine.m_nEnds );
}

TEST_F( ServerUserMessages, AFreedEdictIsNotAPlayer )
{
	m_Engine.Connect( 1 );
	m_Engine.Connect( 2 );
	m_Engine.m_Edicts[2].m_fStateFlags |= FL_EDICT_FREE;

	m_Messages.UserMessageSend( 7, nullptr, 0, 0, 0 );

	ASSERT_EQ( 1u, m_Engine.m_Sent.size() );
	EXPECT_EQ( ( std::vector<int>{ 1 } ), m_Engine.m_Sent[0].recipients );
}

TEST_F( ServerUserMessages, ReliabilityIsCarriedToTheEngine )
{
	m_Engine.Connect( 1 );

	m_Messages.UserMessageSend( 7, nullptr, 0, 0, 1 );
	m_Messages.UserMessageSend( 7, nullptr, 0, 1, 1 );
	m_Messages.UserMessageSend( 7, nullptr, 0, 1, 0 );

	ASSERT_EQ( 3u, m_Engine.m_Sent.size() );
	EXPECT_TRUE( m_Engine.m_Sent[0].bReliable );
	EXPECT_TRUE( m_Engine.m_Sent[1].bReliable );
	EXPECT_FALSE( m_Engine.m_Sent[2].bReliable );
}
