//========= SourceSharp — which game events a side listens for ===============//
//
// game_event_subscriptions.h is SourceSharp_ListenForGameEvent's decision
// (shell_gameevents.cpp). The engine's AddListener is the `add` passed in; the
// fake here counts what it was asked.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "game_event_subscriptions.h"

#include <string>
#include <vector>

namespace
{

struct FakeAdd
{
	std::vector< std::string > *pAsked;
	bool bAccept;
	bool operator()( const char *pName ) const { pAsked->push_back( pName ); return bAccept; }
};

TEST( GameEventSubscriptions, OneNameInTwoCasesIsOneSubscription )
{
	CGameEventSubscriptions subs;
	std::vector< std::string > asked;

	EXPECT_EQ( CGameEventSubscriptions::kSubscribed, subs.Subscribe( "player_death", FakeAdd{ &asked, true } ) );
	EXPECT_EQ( CGameEventSubscriptions::kAlreadyListening, subs.Subscribe( "Player_Death", FakeAdd{ &asked, true } ) );

	EXPECT_EQ( 1u, asked.size() ) << "the engine was asked twice, so every event would arrive twice";
	EXPECT_EQ( 1, subs.Count() );
}

TEST( GameEventSubscriptions, DifferentNamesAreEachSubscribed )
{
	CGameEventSubscriptions subs;
	std::vector< std::string > asked;

	subs.Subscribe( "player_death", FakeAdd{ &asked, true } );
	subs.Subscribe( "round_start", FakeAdd{ &asked, true } );

	EXPECT_EQ( 2u, asked.size() );
	EXPECT_EQ( 2, subs.Count() );
}

TEST( GameEventSubscriptions, ARefusedNameIsNotRecordedAndCanBeAskedAgain )
{
	CGameEventSubscriptions subs;
	std::vector< std::string > asked;

	EXPECT_EQ( CGameEventSubscriptions::kRefused, subs.Subscribe( "no_such_event", FakeAdd{ &asked, false } ) );
	EXPECT_EQ( 0, subs.Count() );
	EXPECT_EQ( CGameEventSubscriptions::kSubscribed, subs.Subscribe( "no_such_event", FakeAdd{ &asked, true } ) );
	EXPECT_EQ( 2u, asked.size() );
}

TEST( GameEventSubscriptions, ClearLetsTheSideSubscribeAgain )
{
	CGameEventSubscriptions subs;
	std::vector< std::string > asked;

	subs.Subscribe( "player_death", FakeAdd{ &asked, true } );
	subs.Clear();
	EXPECT_EQ( 0, subs.Count() );
	EXPECT_EQ( CGameEventSubscriptions::kSubscribed, subs.Subscribe( "player_death", FakeAdd{ &asked, true } ) );
	EXPECT_EQ( 2u, asked.size() );
}

} // namespace
