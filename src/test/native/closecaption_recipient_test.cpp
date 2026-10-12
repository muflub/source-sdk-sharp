//========= SourceSharp — the empty string is not a zero =====================//
//
// closecaption_recipient.h holds one recipient's worth of the rule inside
// RecipientFilter_RemoveIfNotCloseCaptioning. The whole reason it is a named
// function rather than four lines in a loop is one case:
//
//     atoi( "" ) == 0    and    atoi( "0" ) == 0
//
// and those two mean OPPOSITE things. "" is "this client has not reported the
// convar yet"; "0" is "this client has captions off". Collapsing them -- which
// is what anyone tidying the original loop would do, because the empty check
// looks redundant next to the atoi -- silently drops every client whose convar
// has not arrived, with no error anywhere and a symptom (some players miss some
// captions some of the time) that nobody would trace back to this line.
//
// plan_baseentity.md Phase 13: the containing function is a step 5 gate. It
// removes nobody today, so step 5 is the first day this rule runs at all, and
// these are the answers it must give when it does.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <stdlib.h>

#include "../game/shared/closecaption_recipient.h"

//-----------------------------------------------------------------------------
// THE CASE THE WHOLE HEADER EXISTS FOR. An unreported convar keeps the
// recipient; captions off removes them. Both strings parse to zero.
//-----------------------------------------------------------------------------
TEST( CloseCaptionRecipient, EmptyIsNotTheSameAsZero )
{
	EXPECT_FALSE( CloseCaption_ShouldRemoveRecipient( "" ) )
		<< "An unreported closecaption convar must KEEP the recipient. This is "
		   "the case that atoi() alone gets wrong.";
	EXPECT_TRUE( CloseCaption_ShouldRemoveRecipient( "0" ) )
		<< "closecaption 0 means captions off, so the recipient goes.";
}

//-----------------------------------------------------------------------------
// NULL is the same story as empty: nothing known, so nothing taken away. The
// engine's GetClientConVarValue is Assert'd non-NULL at the call site and this
// is the release-build answer behind that Assert.
//-----------------------------------------------------------------------------
TEST( CloseCaptionRecipient, NullKeepsTheRecipient )
{
	EXPECT_FALSE( CloseCaption_ShouldRemoveRecipient( NULL ) );
}

//-----------------------------------------------------------------------------
// Captions on, in each of the settings the convar actually takes: 1 is on, 2 is
// "subtitles only" in stock. Neither removes anybody.
//-----------------------------------------------------------------------------
TEST( CloseCaptionRecipient, AnyNonZeroSettingKeepsTheRecipient )
{
	EXPECT_FALSE( CloseCaption_ShouldRemoveRecipient( "1" ) );
	EXPECT_FALSE( CloseCaption_ShouldRemoveRecipient( "2" ) );
	EXPECT_FALSE( CloseCaption_ShouldRemoveRecipient( "-1" ) );
}

//-----------------------------------------------------------------------------
// The convar arrives as whatever the client typed, so the parse has to behave
// like the atoi the original loop used -- leading space and a trailing tail are
// both things a client can produce.
//-----------------------------------------------------------------------------
TEST( CloseCaptionRecipient, ParsesTheWayTheOriginalLoopDid )
{
	EXPECT_TRUE(  CloseCaption_ShouldRemoveRecipient( " 0" ) );
	EXPECT_FALSE( CloseCaption_ShouldRemoveRecipient( " 1" ) );

	// atoi stops at the first non-digit, so "0abc" is zero and "1abc" is one.
	EXPECT_TRUE(  CloseCaption_ShouldRemoveRecipient( "0abc" ) );
	EXPECT_FALSE( CloseCaption_ShouldRemoveRecipient( "1abc" ) );

	// ..and a string with no leading digits at all parses as zero, which is
	// what stock did with it too. Stated rather than discovered later.
	EXPECT_TRUE( CloseCaption_ShouldRemoveRecipient( "on" ) );
}

// TheFlagBitsAreSharedDefsOwn WAS HERE and pinned ss::CloseCaptionFlag against
// shareddefs.h. Nothing native sets those bits since C#'s CloseCaptions sends
// the message, so the enum went (lane HC, 2026-10-04) and the fact moved to
// CloseCaptionsTests.TheFlagBitsAreSharedDefsOwn, over CaptionFlags.
