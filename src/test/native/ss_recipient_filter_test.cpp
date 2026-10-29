//========= SourceSharp — the server's recipient filters keep a list of slots ==//
//
// ss_recipient_filter.cpp: the list logic of server.so's own recipient filters
// (plan_server_novalve.md P5), held to the behaviour of the game/server
// CRecipientFilter it replaces. Links only that file; each filter is built for
// four client slots.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "ss_recipient_filter.h"

#include <vector>

namespace
{
std::vector<int> Slots(const ss::CRecipientFilter& f)
{
	std::vector<int> v;
	for (int i = 0; i < f.GetRecipientCount(); ++i)
		v.push_back(f.GetRecipientIndex(i));
	return v;
}

constexpr int kMaxClients = 4;
}

TEST(SsRecipientFilter, ANewFilterIsEmptyUnreliableAndNotAnInitMessage)
{
	ss::CRecipientFilter f(kMaxClients);
	EXPECT_EQ(0, f.GetRecipientCount());
	EXPECT_FALSE(f.IsReliable());
	EXPECT_FALSE(f.IsInitMessage());
	EXPECT_FALSE(f.IsUsingPredictionRules());
	EXPECT_FALSE(f.IgnorePredictionCull());
}

TEST(SsRecipientFilter, SlotsAreAddedInOrderOnceEach)
{
	ss::CRecipientFilter f(kMaxClients);
	f.AddRecipientByPlayerIndex(3);
	f.AddRecipientByPlayerIndex(1);
	f.AddRecipientByPlayerIndex(3);
	EXPECT_EQ((std::vector<int>{3, 1}), Slots(f));
}

TEST(SsRecipientFilter, OnlySlotsOneToMaxClientsAreAccepted)
{
	ss::CRecipientFilter f(kMaxClients);
	f.AddRecipientByPlayerIndex(0);
	f.AddRecipientByPlayerIndex(-2);
	f.AddRecipientByPlayerIndex(5);
	f.AddRecipientByPlayerIndex(4);
	EXPECT_EQ((std::vector<int>{4}), Slots(f));
}

// RemoveRecipientByPlayerIndex went (plan_server_native_v2 T5) with
// CPASAttenuationFilter::Filter, its one importer; RecipientFilter.cs holds the
// managed removal.

TEST(SsRecipientFilter, AnIndexOutsideTheListAnswersMinusOne)
{
	ss::CRecipientFilter f(kMaxClients);
	f.AddRecipientByPlayerIndex(2);
	EXPECT_EQ(-1, f.GetRecipientIndex(-1));
	EXPECT_EQ(-1, f.GetRecipientIndex(1));
}

// CopyFrom left with its one importer, sound_emitter.cpp's server caption
// (plan_server_native_v2 T4a); RecipientFilter.Copy (C#) is the managed
// emitter's, held by SoundRecipientsTests.
