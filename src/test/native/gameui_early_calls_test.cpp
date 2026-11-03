//========= SourceSharp — gameui_early_calls.h tests ==========================//
//
// The engine initialises its GameUI before the client brings the CLR up
// (C0), so SSGameUI holds those calls and plays them to the managed GameUI at
// the first call after it exists (plan_gameui_managed.md G4): in their order,
// once, with their arguments, and past the capacity counted, not kept.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/client/gameui_early_calls.h"

#include <vector>

namespace
{

using GameUiEarly::Call;

struct Recorder
{
	std::vector<GameUiEarly::Entry> played;

	void operator()(const GameUiEarly::Entry& entry) { played.push_back(entry); }
};

TEST(GameUiEarlyCalls, C0sSequenceIsPlayedInItsOrderWithItsArguments)
{
	GameUiEarly::Queue queue{};
	queue.Push(Call::Initialize, 0x1234);
	queue.Push(Call::Start);
	queue.Push(Call::ConsoleInitialize);
	queue.Push(Call::ConsoleSetParent, 0x55);
	queue.Push(Call::OnGameUIActivated);
	Recorder sink;
	queue.Replay(sink);
	ASSERT_EQ(5u, sink.played.size());
	EXPECT_EQ(Call::Initialize, sink.played[0].call);
	EXPECT_EQ(0x1234u, sink.played[0].arg);
	EXPECT_EQ(Call::Start, sink.played[1].call);
	EXPECT_EQ(Call::ConsoleInitialize, sink.played[2].call);
	EXPECT_EQ(Call::ConsoleSetParent, sink.played[3].call);
	EXPECT_EQ(0x55u, sink.played[3].arg);
	EXPECT_EQ(Call::OnGameUIActivated, sink.played[4].call);
}

TEST(GameUiEarlyCalls, AReplayEmptiesTheQueueSoNothingPlaysTwice)
{
	GameUiEarly::Queue queue{};
	queue.Push(Call::Start);
	Recorder sink;
	queue.Replay(sink);
	queue.Replay(sink);
	EXPECT_EQ(1u, sink.played.size());
	EXPECT_EQ(0, queue.Count());
}

TEST(GameUiEarlyCalls, ACallTheSinkMakesBackIsHeldNotReplayedInTheSamePass)
{
	GameUiEarly::Queue queue{};
	queue.Push(Call::Start);
	int calls = 0;
	auto sink = [&](const GameUiEarly::Entry&)
	{
		calls++;
		queue.Push(Call::OnGameUIHidden);
	};
	queue.Replay(sink);
	EXPECT_EQ(1, calls);
	EXPECT_EQ(1, queue.Count());
}

TEST(GameUiEarlyCalls, CallsPastTheCapacityAreCountedAndDropped)
{
	GameUiEarly::Queue queue{};
	for (int i = 0; i < GameUiEarly::Queue::kCapacity + 3; i++)
		queue.Push(Call::Start);
	EXPECT_EQ(GameUiEarly::Queue::kCapacity, queue.Count());
	EXPECT_EQ(3, queue.Dropped());
}

} // namespace
