//=============================================================================//
// GATE 5's TRANSMIT-SET RECORDER, RUN -- plan_server.md Gate 5,
// docs/plan-audit.md C2.
//
// Two halves, and they need different instruments -- the same split
// shell_gamedll_refusals_test.cpp makes and for the same reason:
//
//   1. THE BODIES. That the transmit capture is one-shot, that a capture that
//      never happened is distinguishable from an empty one, that out-of-range
//      marks are dropped rather than corrupting. Those are properties of
//      CShellTransmitSet's body, so this file compiles the SHIPPED header
//      (sourcesharp/game/server/shell_steporder.h) and runs it. Nothing here is
//      a transcription.
//
//   2. THE ARRANGEMENT in the .cpp files that record. Those include cbase.h's
//      whole world and can never be linked here, so they are source-read -- the
//      way shell_gamedll_registration_test.cpp reads shell_gamedll.cpp for the
//      same reason. Comment-stripped, because every comment in those files
//      NAMES the symbols it explains and a raw search would pass on prose.
//
// WHAT THIS FILE NO LONGER CLAIMS. It held 17 CShellStepOrder facts, three
// rows inside TheSeamReachesTheShippedBodies, and two
// call-site censuses (EveryLevelStepRecords / EveryFrameStepRecords), all over the
// step-order ring. The ring had no reader -- the `tools/gate5` meant to diff
// its output was never written -- so the ring, its 41 call sites and
// `cs_sv_steporder` are gone, and those assertions are no longer claims
// about anything in the build. The transmit-set half stays because it HAS a
// reader: tools/p4-exit-probe-skybox.
//=============================================================================//

#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <sys/stat.h>

#include "gtest/gtest.h"

#include "shell_steporder.h"

//=============================================================================
// HALF ONE -- THE BODIES.
//=============================================================================

//-----------------------------------------------------------------------------
// The trigger. Without it the level load cannot be recorded at all -- see
// shell_steporder.h for the measurement that produced it.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The transmit set.
//-----------------------------------------------------------------------------

TEST(ShellTransmitSet, UnarmedDeclinesTheCapture)
{
	CShellTransmitSet set;

	EXPECT_FALSE(set.Armed());
	EXPECT_FALSE(set.HaveCapture());

	// The call site's whole cost every frame but one, and the answer that keeps
	// CheckTransmit from walking 2048 edicts for nothing.
	EXPECT_FALSE(set.BeginCapture(1));
	EXPECT_FALSE(set.HaveCapture());
}

TEST(ShellTransmitSet, ArmedCapturesExactlyOnce)
{
	CShellTransmitSet set;
	set.Arm();

	EXPECT_TRUE(set.BeginCapture(1));

	// ONE SHOT. Armed for a whole second, CheckTransmit runs 66 times and the
	// set read back would be the LAST frame before the dump rather than a frame
	// anybody chose -- and two runs of the gate would compare two different
	// frames and call the difference a regression.
	EXPECT_FALSE(set.BeginCapture(2));
	EXPECT_EQ(1, set.Recipient());
}

TEST(ShellTransmitSet, ArmedForOneRecipientWaitsForThatRecipient)
{
	// With two clients CheckTransmit runs for each every snapshot, client 1
	// first; an arm that took whoever came next could never see client 2.
	CShellTransmitSet set;
	set.Arm(2);

	EXPECT_FALSE(set.BeginCapture(1));
	EXPECT_FALSE(set.HaveCapture());
	EXPECT_TRUE(set.Armed());
	EXPECT_TRUE(set.BeginCapture(2));
	EXPECT_EQ(2, set.Recipient());
	EXPECT_FALSE(set.BeginCapture(2));
}

TEST(ShellTransmitSet, TheRecipientIsPartOfTheAnswer)
{
	CShellTransmitSet set;
	EXPECT_EQ(-1, set.Recipient());

	set.Arm();
	set.BeginCapture(7);
	EXPECT_EQ(7, set.Recipient());
}

TEST(ShellTransmitSet, MarksAreASet)
{
	CShellTransmitSet set;
	set.Arm();
	ASSERT_TRUE(set.BeginCapture(1));

	set.Mark(0);
	set.Mark(33);
	set.Mark(33);
	set.Mark(2047);

	// A SET, so the second Mark( 33 ) is not a second member. The source is a
	// CBitVec and this has to answer the way one does.
	EXPECT_EQ(3, set.Count());
	EXPECT_TRUE(set.IsSet(0));
	EXPECT_TRUE(set.IsSet(33));
	EXPECT_TRUE(set.IsSet(2047));
	EXPECT_FALSE(set.IsSet(1));
	EXPECT_FALSE(set.IsSet(32));
	EXPECT_FALSE(set.IsSet(34));
}

TEST(ShellTransmitSet, OutOfRangeMarksAreDroppedRatherThanCorrupting)
{
	CShellTransmitSet set;
	set.Arm();
	ASSERT_TRUE(set.BeginCapture(1));

	set.Mark(-1);
	set.Mark(CShellTransmitSet::kMaxEdicts);
	set.Mark(999999);

	EXPECT_EQ(0, set.Count());
	EXPECT_FALSE(set.IsSet(-1));
	EXPECT_FALSE(set.IsSet(CShellTransmitSet::kMaxEdicts));
}

TEST(ShellTransmitSet, ASecondCaptureClearsTheFirst)
{
	CShellTransmitSet set;

	set.Arm();
	ASSERT_TRUE(set.BeginCapture(1));
	set.Mark(5);
	ASSERT_EQ(1, set.Count());

	set.Arm();
	ASSERT_TRUE(set.BeginCapture(2));

	// Not a union with the previous frame's. Two runs of the gate on one game
	// must not accumulate.
	EXPECT_EQ(0, set.Count());
	EXPECT_FALSE(set.IsSet(5));
}

TEST(ShellTransmitSet, NoCaptureIsDistinguishableFromAnEmptyOne)
{
	// A client that received nothing and a capture that never happened are
	// opposite findings, and the dump has to be able to tell them apart -- see
	// the `nocapture` line in cs_sv_transmitset.
	CShellTransmitSet never;
	EXPECT_FALSE(never.HaveCapture());

	CShellTransmitSet empty;
	empty.Arm();
	ASSERT_TRUE(empty.BeginCapture(1));
	EXPECT_TRUE(empty.HaveCapture());
	EXPECT_EQ(0, empty.Count());
}

TEST(ShellTransmitSet, TwoSetsShareNothing)
{
	CShellTransmitSet a, b;

	a.Arm();
	ASSERT_TRUE(a.BeginCapture(1));
	a.Mark(11);

	EXPECT_FALSE(b.HaveCapture());
	EXPECT_FALSE(b.IsSet(11));
	EXPECT_EQ(0, b.Count());
}

//=============================================================================
// HALF TWO -- THE ARRANGEMENT, source-read.
//=============================================================================

namespace
{

} // namespace
