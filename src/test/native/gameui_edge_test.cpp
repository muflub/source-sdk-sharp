//========= SourceSharp — the GameUI visibility edge ==========================//
//
// The pause menu's drive: the engine hands its GameUI activation to the client
// only when -game names tf exactly, so the client watches the engine's own
// visibility and fires gameui_activated / gameui_hidden on the edge
// (gameui_edge.h). The machine is engine-free; these facts are its whole
// contract -- one edge per change, none while the answer holds, and none
// before the first reading, which only primes the state.
//
//=============================================================================//

#include "gameui_edge.h"
#include "gtest/gtest.h"

namespace
{

TEST(GameUIVisibility, AHiddenFirstReadingOnlyPrimesTheState)
{
	GameUIVisibility visible;
	EXPECT_EQ(GameUIEdge::None, visible.Update(false));
	EXPECT_EQ(GameUIEdge::None, visible.Update(false));
}

// At launch the GameUI is already up before the first client frame: stock's
// main-menu override is visible from the start, so the first reading that
// finds it up is the activation the menu opens on.
TEST(GameUIVisibility, TheGameUIUpAtTheFirstReadingIsTheActivation)
{
	GameUIVisibility visible;
	EXPECT_EQ(GameUIEdge::Activated, visible.Update(true));
	EXPECT_EQ(GameUIEdge::None, visible.Update(true));
}

TEST(GameUIVisibility, AnUnchangedAnswerIsNoEdge)
{
	GameUIVisibility visible;
	EXPECT_EQ(GameUIEdge::None, visible.Update(false));
	EXPECT_EQ(GameUIEdge::None, visible.Update(false));
	EXPECT_EQ(GameUIEdge::None, visible.Update(false));
}

TEST(GameUIVisibility, TheGameUIShowingIsTheActivatedEdge)
{
	GameUIVisibility visible;
	EXPECT_EQ(GameUIEdge::None, visible.Update(false));
	EXPECT_EQ(GameUIEdge::Activated, visible.Update(true));
}

TEST(GameUIVisibility, TheGameUIHidingIsTheHiddenEdge)
{
	GameUIVisibility visible;
	EXPECT_EQ(GameUIEdge::Activated, visible.Update(true));
	EXPECT_EQ(GameUIEdge::Hidden, visible.Update(false));
}

TEST(GameUIVisibility, OneEdgePerChangeAcrossAlternatingFrames)
{
	GameUIVisibility visible;
	EXPECT_EQ(GameUIEdge::None, visible.Update(false));
	for (int i = 0; i < 3; i++)
	{
		EXPECT_EQ(GameUIEdge::Activated, visible.Update(true));
		EXPECT_EQ(GameUIEdge::None, visible.Update(true));
		EXPECT_EQ(GameUIEdge::Hidden, visible.Update(false));
		EXPECT_EQ(GameUIEdge::None, visible.Update(false));
	}
}

// The override panel goes to the gameui while it is up, once: a panel already
// under the gameui's base panel stays, and a hidden GameUI takes nothing.
TEST(MenuOverrideHand, TheGameUIUpTakesAPanelNotUnderIt)
{
	EXPECT_TRUE(MenuOverrideHand(true, false));
}

TEST(MenuOverrideHand, APanelAlreadyUnderTheGameUIIsNotHandedAgain)
{
	EXPECT_FALSE(MenuOverrideHand(true, true));
}

TEST(MenuOverrideHand, AHiddenGameUITakesNothing)
{
	EXPECT_FALSE(MenuOverrideHand(false, false));
	EXPECT_FALSE(MenuOverrideHand(false, true));
}

TEST(GameUIOverrideHold, NothingHandedIsNothingToTakeBack)
{
	GameUIOverrideHold hold{};
	EXPECT_FALSE(hold.TakeBack());
}

TEST(GameUIOverrideHold, AHandedRootIsTakenBackOnce)
{
	GameUIOverrideHold hold{};
	hold.Handed();
	hold.Handed();
	EXPECT_TRUE(hold.TakeBack());
	EXPECT_FALSE(hold.TakeBack());
}

} // namespace
