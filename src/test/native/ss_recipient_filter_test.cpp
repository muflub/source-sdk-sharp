//========= SourceSharp — the server's recipient filters keep a list of slots ==//
//
// ss_recipient_filter.cpp: the list logic of server.so's own recipient filters
// (plan_server_novalve.md P5), held to the behaviour of the game/server
// CRecipientFilter it replaces. Links only that file and a CGlobalVars with
// four client slots; the engine-facing fills are the live gate's.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_recipient_filter.h"
#include "edict.h"

#include <vector>

CGlobalVars g_TestGlobals( false );
CGlobalVars *gpGlobals = &g_TestGlobals;

namespace
{
std::vector< int > Slots( const ss::CRecipientFilter &f )
{
	std::vector< int > v;
	for ( int i = 0; i < f.GetRecipientCount(); ++i )
		v.push_back( f.GetRecipientIndex( i ) );
	return v;
}

class SsRecipientFilter : public ::testing::Test
{
protected:
	void SetUp() override { g_TestGlobals.maxClients = 4; }
};
}

TEST_F( SsRecipientFilter, ANewFilterIsEmptyUnreliableAndNotAnInitMessage )
{
	ss::CRecipientFilter f;
	EXPECT_EQ( 0, f.GetRecipientCount() );
	EXPECT_FALSE( f.IsReliable() );
	EXPECT_FALSE( f.IsInitMessage() );
	EXPECT_FALSE( f.IsUsingPredictionRules() );
	EXPECT_FALSE( f.IgnorePredictionCull() );
}

TEST_F( SsRecipientFilter, SlotsAreAddedInOrderOnceEach )
{
	ss::CRecipientFilter f;
	f.AddRecipientByPlayerIndex( 3 );
	f.AddRecipientByPlayerIndex( 1 );
	f.AddRecipientByPlayerIndex( 3 );
	EXPECT_EQ( ( std::vector< int >{ 3, 1 } ), Slots( f ) );
}

TEST_F( SsRecipientFilter, OnlySlotsOneToMaxClientsAreAccepted )
{
	ss::CRecipientFilter f;
	f.AddRecipientByPlayerIndex( 0 );
	f.AddRecipientByPlayerIndex( -2 );
	f.AddRecipientByPlayerIndex( 5 );
	f.AddRecipientByPlayerIndex( 4 );
	EXPECT_EQ( ( std::vector< int >{ 4 } ), Slots( f ) );
}

TEST_F( SsRecipientFilter, RemovingASlotKeepsTheRestInOrder )
{
	ss::CRecipientFilter f;
	for ( int i : { 1, 2, 3 } )
		f.AddRecipientByPlayerIndex( i );
	f.RemoveRecipientByPlayerIndex( 2 );
	f.RemoveRecipientByPlayerIndex( 4 );
	EXPECT_EQ( ( std::vector< int >{ 1, 3 } ), Slots( f ) );
}

TEST_F( SsRecipientFilter, AnIndexOutsideTheListAnswersMinusOne )
{
	ss::CRecipientFilter f;
	f.AddRecipientByPlayerIndex( 2 );
	EXPECT_EQ( -1, f.GetRecipientIndex( -1 ) );
	EXPECT_EQ( -1, f.GetRecipientIndex( 1 ) );
}

TEST_F( SsRecipientFilter, CopyFromAppendsTheSlotsAndTakesTheFlags )
{
	ss::CRecipientFilter src;
	src.AddRecipientByPlayerIndex( 2 );
	src.MakeReliable();
	src.UsePredictionRules();

	ss::CRecipientFilter dst;
	dst.AddRecipientByPlayerIndex( 1 );
	dst.CopyFrom( src );

	// Appended, not replaced: stock's CopyFrom never cleared the list first.
	EXPECT_EQ( ( std::vector< int >{ 1, 2 } ), Slots( dst ) );
	EXPECT_TRUE( dst.IsReliable() );
	EXPECT_TRUE( dst.IsUsingPredictionRules() );
	EXPECT_FALSE( dst.IsInitMessage() );
}

TEST_F( SsRecipientFilter, ASingleUserFilterHoldsThatSlot )
{
	ss::CSingleUserRecipientFilter f( 2 );
	EXPECT_EQ( ( std::vector< int >{ 2 } ), Slots( f ) );
	ss::CSingleUserRecipientFilter none( 0 );
	EXPECT_EQ( 0, none.GetRecipientCount() );
}
