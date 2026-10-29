// CGameEvents over generated fakes: what it subscribes the engine to, what reaches the
// managed events area, what it unhooks, and the field accessors' defaults.

#include "gtest/gtest.h"

#include "game_events.h"
#include "imanaged_areas.h"
#include "fakes/fake_game_event_manager.g.h"
#include "fakes/fake_game_event.g.h"

#include "mathlib_test_env.h"

#include <string>
#include <vector>

// The links this file's subject imports.
int SourceSharp_GetVerbosity()
{
	return 0;
}

namespace
{
class CRecordingManager : public CFakeGameEventManager2
{
public:
	bool AddListener( IGameEventListener2 *listener, const char *name, bool bServerSide ) override
	{
		m_Added.push_back( name );
		m_pListener = listener;
		m_bServerSide = bServerSide;
		return m_bAccept;
	}

	void RemoveListener( IGameEventListener2 *listener ) override { m_Removed.push_back( listener ); }

	std::vector<std::string> m_Added;
	std::vector<IGameEventListener2 *> m_Removed;
	IGameEventListener2 *m_pListener = nullptr;
	bool m_bServerSide = false;
	bool m_bAccept = true;
};

class CNamedEvent : public CFakeGameEvent
{
public:
	const char *GetName() const override { return "player_death"; }

	int GetInt( const char *keyName, int defaultValue ) const override { return 42; }

	float GetFloat( const char *keyName, float defaultValue ) const override { return 2.5f; }

	bool GetBool( const char *keyName, bool defaultValue ) const override { return true; }

	const char *GetString( const char *keyName, const char *defaultValue ) const override { return "weapon"; }

	void SetInt( const char *keyName, int value ) override { m_nSet = value; }

	void SetBool( const char *keyName, bool value ) override { m_bSet = value; }

	void SetString( const char *keyName, const char *value ) override { m_Set = value; }

	int m_nSet = 0;
	bool m_bSet = false;
	std::string m_Set;
};

// What the managed events area was handed.
std::vector<std::pair<std::string, void *>> g_Fired;

void RecordEvent( IManagedEvents *, const char *pEventName, void *hEvent )
{
	g_Fired.push_back( { pEventName, hEvent } );
}

class GameEvents : public ::testing::Test
{
protected:
	GameEvents()
	{
		g_Fired.clear();
		m_Table.OnGameEvent = RecordEvent;
	}

	CRecordingManager m_Manager;
	IGameEventManager2 *m_pManager = &m_Manager;
	SSManagedEventsTable m_Table = {};
	IManagedEvents m_Area{ &m_Table };
	IManagedEvents *m_pArea = &m_Area;
	CGameEvents m_Events{ m_pManager, m_pArea, true };
};
} // namespace

TEST_F( GameEvents, ASubscriptionAddsTheListenerServerSide )
{
	m_Events.ListenForGameEvent( "player_death" );

	ASSERT_EQ( 1u, m_Manager.m_Added.size() );
	EXPECT_EQ( "player_death", m_Manager.m_Added[0] );
	EXPECT_TRUE( m_Manager.m_bServerSide );
	EXPECT_EQ( 1, m_Events.SubscribedCount() );
}

TEST_F( GameEvents, ARepeatedNameInAnyCaseIsKeptNotAddedAgain )
{
	m_Events.ListenForGameEvent( "player_death" );
	m_Events.ListenForGameEvent( "PLAYER_DEATH" );

	EXPECT_EQ( 1u, m_Manager.m_Added.size() );
	EXPECT_EQ( 1, m_Events.SubscribedCount() );
}

TEST_F( GameEvents, ANameTheEngineRefusesIsNotRecorded )
{
	m_Manager.m_bAccept = false;
	m_Events.ListenForGameEvent( "no_such_event" );
	m_Manager.m_bAccept = true;
	m_Events.ListenForGameEvent( "no_such_event" );

	EXPECT_EQ( 2u, m_Manager.m_Added.size() );
	EXPECT_EQ( 1, m_Events.SubscribedCount() );
}

TEST_F( GameEvents, AnEmptyNameOrNoManagerSubscribesNothing )
{
	m_Events.ListenForGameEvent( "" );
	m_Events.ListenForGameEvent( nullptr );
	m_pManager = nullptr;
	m_Events.ListenForGameEvent( "player_death" );

	EXPECT_TRUE( m_Manager.m_Added.empty() );
	EXPECT_EQ( 0, m_Events.SubscribedCount() );
}

TEST_F( GameEvents, AFiredEventReachesTheManagedAreaByNameAndHandle )
{
	m_Events.ListenForGameEvent( "player_death" );
	ASSERT_NE( nullptr, m_Manager.m_pListener );

	CNamedEvent event;
	m_Manager.m_pListener->FireGameEvent( &event );

	ASSERT_EQ( 1u, g_Fired.size() );
	EXPECT_EQ( "player_death", g_Fired[0].first );
	EXPECT_EQ( (void *)&event, g_Fired[0].second );
}

TEST_F( GameEvents, AnEventOutsideALoadReachesNothing )
{
	m_Events.ListenForGameEvent( "player_death" );
	m_pArea = nullptr;

	CNamedEvent event;
	m_Manager.m_pListener->FireGameEvent( &event );
	m_Manager.m_pListener->FireGameEvent( nullptr );

	EXPECT_TRUE( g_Fired.empty() );
}

TEST_F( GameEvents, StoppingRemovesTheListenerAndForgetsTheNames )
{
	m_Events.ListenForGameEvent( "player_death" );
	IGameEventListener2 *pListener = m_Manager.m_pListener;

	m_Events.StopListening();

	ASSERT_EQ( 1u, m_Manager.m_Removed.size() );
	EXPECT_EQ( pListener, m_Manager.m_Removed[0] );
	EXPECT_EQ( 0, m_Events.SubscribedCount() );

	m_Events.ListenForGameEvent( "player_death" );
	EXPECT_EQ( 2u, m_Manager.m_Added.size() );
}

TEST_F( GameEvents, StoppingWithNoManagerIsSafe )
{
	m_pManager = nullptr;
	m_Events.StopListening();
	m_Events.StopListening();

	EXPECT_TRUE( m_Manager.m_Removed.empty() );
}

TEST_F( GameEvents, TheAccessorsReadTheEventInProgress )
{
	CNamedEvent event;

	EXPECT_EQ( 42, m_Events.EventGetInt( &event, "userid" ) );
	EXPECT_FLOAT_EQ( 2.5f, m_Events.EventGetFloat( &event, "x" ) );
	EXPECT_EQ( 1, m_Events.EventGetBool( &event, "headshot" ) );
	EXPECT_STREQ( "weapon", m_Events.EventGetString( &event, "weapon" ) );
}

TEST_F( GameEvents, ANullHandleOrKeyAnswersTheDefault )
{
	CNamedEvent event;

	EXPECT_EQ( 0, m_Events.EventGetInt( nullptr, "userid" ) );
	EXPECT_EQ( 0, m_Events.EventGetInt( &event, nullptr ) );
	EXPECT_FLOAT_EQ( 0.0f, m_Events.EventGetFloat( nullptr, "x" ) );
	EXPECT_EQ( 0, m_Events.EventGetBool( &event, nullptr ) );
	EXPECT_STREQ( "", m_Events.EventGetString( nullptr, "weapon" ) );
}

TEST_F( GameEvents, TheServerSettersWriteTheEvent )
{
	CNamedEvent event;

	m_Events.EventSetInt( &event, "userid", 7 );
	m_Events.EventSetBool( &event, "headshot", 1 );
	m_Events.EventSetString( &event, "weapon", nullptr );

	EXPECT_EQ( 7, event.m_nSet );
	EXPECT_TRUE( event.m_bSet );
	EXPECT_EQ( "", event.m_Set );
}
