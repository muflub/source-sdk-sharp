//========= SourceSharp — where a player's tick base is put, both arms ========//
//
// tickbase_rule.h is CBasePlayer::AdjustPlayerTimeBase. These pin the cases
// the port before it got wrong: a frame with no commands, and a base that has
// drifted from tickcount while the batch it runs still ends inside stock's
// window. Each of those re-based the clock and the client mispredicted
// TickBase and every timer read from it.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/server/tickbase_rule.h"

namespace
{

const int kCorrection = 4;   // TIME_TO_TICKS( 0.060 ) at a 15 ms tick

TEST( TickBaseRule, AFrameWithNoCommandsLeavesTheBaseAlone )
{
	// The port re-based here whenever tickcount had moved on: an idle frame
	// on a listen server moved the clock with nothing to run.
	EXPECT_EQ( 1000, TickBase_Adjust( 1000, 1010, 0, 1, kCorrection, false ) );
}

TEST( TickBaseRule, ABatchEndingInsideTheWindowKeepsTheBase )
{
	// Ideal final tick 1010 + 4 = 1014, window 1010..1018. A base of 1005 that
	// runs 7 commands ends at 1012: inside, so kept -- although the base sits 5
	// ticks from tickcount, which the port read as "drifted" and re-based.
	EXPECT_EQ( 1005, TickBase_Adjust( 1005, 1010, 7, 1, kCorrection, false ) );
}

TEST( TickBaseRule, ABatchEndingTooLateIsPulledBack )
{
	// 1020 + 1 = 1021 > 1018: corrected to 1014 - 1 + 1.
	EXPECT_EQ( 1014, TickBase_Adjust( 1020, 1010, 1, 1, kCorrection, false ) );
}

TEST( TickBaseRule, ABatchEndingTooEarlyIsPushedForward )
{
	// 900 + 2 = 902 < 1010: corrected to 1014 - 2 + 1.
	EXPECT_EQ( 1013, TickBase_Adjust( 900, 1010, 2, 1, kCorrection, false ) );
}

TEST( TickBaseRule, SinglePlayerAlwaysPinsTheBaseToTickcount )
{
	EXPECT_EQ( 1009, TickBase_Adjust( 1005, 1010, 2, 1, kCorrection, true ) );
}

}  // namespace
