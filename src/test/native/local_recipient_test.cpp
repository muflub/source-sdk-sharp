//========= SourceSharp — which slot a client filter holds ===================//
//
// C_RecipientFilter reaches exactly one person, the viewer, and every add on it
// asked C_BasePlayer::GetLocalPlayer() to find out who that was. Under this mod
// the viewer is a C_StandalonePlayer, that answers NULL, and EVERY CLIENT
// FILTER WAS EMPTY.
//
// Which would be quiet, except that both client temp-entity dispatchers open
// with `if ( !filter.GetRecipientCount() ) return true;  // suppress it` — so
// every predicted effect the viewer's own actions make was thrown away before
// the effect function ran. The server's copy arrives a round trip later, so
// things appear, late, and no line anywhere says a filter came out empty. It is
// the same shape, and the same disguise, as the server-side outbreak that made
// explosions invisible while their damage worked.
//
// The rule is now three comparisons over slot numbers, and this is all of them.
// Worth having off the client because the interesting states — no viewer yet, a
// re-predicted frame, somebody else's slot — are ones a running game is between
// for a few milliseconds at a time.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../../game/client/local_recipient.h"

namespace
{

TEST( LocalRecipient, TheViewerIsHeld )
{
	EXPECT_TRUE( LocalRecipient_ShouldHold( 1, 1, false, true ) );
	EXPECT_TRUE( LocalRecipient_ShouldHold( 9, 9, false, true ) );
}

TEST( LocalRecipient, NobodyElseIs )
{
	// A client filter is not a broadcast list: it reaches the person at this
	// keyboard or nobody.
	EXPECT_FALSE( LocalRecipient_ShouldHold( 2, 1, false, true ) );
	EXPECT_FALSE( LocalRecipient_ShouldHold( 1, 9, false, true ) );
}

TEST( LocalRecipient, NoViewerYetHoldsNobody )
{
	// engine->GetLocalPlayer() before the viewer exists. Every filter built
	// during load is in this state, and holding slot -1 would be worse than
	// holding nothing.
	EXPECT_FALSE( LocalRecipient_ShouldHold( 1, -1, false, true ) );
	EXPECT_FALSE( LocalRecipient_ShouldHold( 1, 0, false, true ) );
}

TEST( LocalRecipient, SlotZeroIsTheWorldAndNeverAPlayer )
{
	EXPECT_FALSE( LocalRecipient_ShouldHold( 0, 0, false, true ) );
	EXPECT_FALSE( LocalRecipient_ShouldHold( -1, 1, false, true ) );
}

TEST( LocalRecipient, ARePredictedFrameSuppressesTheViewer )
{
	// Stock's rule: the local player is added the FIRST time an effect is
	// predicted and not on the re-simulations, which is what stops one shot
	// drawing its own tracer once per replayed command.
	EXPECT_TRUE ( LocalRecipient_ShouldHold( 1, 1, true, true ) );
	EXPECT_FALSE( LocalRecipient_ShouldHold( 1, 1, true, false ) );
}

TEST( LocalRecipient, SuppressionOnlyAppliesUnderPredictionRules )
{
	// A filter not using prediction rules ignores CanPredict entirely — the
	// same as stock, where the check sat inside `if ( m_bUsingPredictionRules )`.
	EXPECT_TRUE( LocalRecipient_ShouldHold( 1, 1, false, false ) );
}

// The bug, stated as the test that would have caught it: a viewer whose entity
// is not a C_BasePlayer still occupies a slot, and the filter must hold it.
// Stock's spelling could not — it read the slot OFF the C_BasePlayer it had
// just failed to find.
TEST( LocalRecipient, AViewerWhoIsNotACBasePlayerIsStillHeld )
{
	const int nEngineSaysLocalIs = 1;		// engine->GetLocalPlayer()
	const int nCBasePlayerWouldSay = -1;	// C_BasePlayer::GetLocalPlayer() is NULL

	EXPECT_TRUE ( LocalRecipient_ShouldHold( 1, nEngineSaysLocalIs, false, true ) );
	EXPECT_FALSE( LocalRecipient_ShouldHold( 1, nCBasePlayerWouldSay, false, true ) );
}

}  // namespace
