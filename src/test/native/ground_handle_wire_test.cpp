//========= SourceSharp — tests for ground_handle_wire.h ====================//
//
// The word the player's blob carries for "what am I standing on". Every way it
// can be wrong is a MISPREDICTION WITH NO ERROR ANYWHERE, which is the same
// class of failure the field was added to remove:
//
//   a sentinel that collides   "no ground" reads as "standing on entity N"
//   a zero sentinel            an unwritten blob reads as "standing on the
//                              world", the most plausible-looking wrong answer
//                              this file can produce
//   a lax range check          a garbage word (an unresolved field, a wrong
//                              offset) names a real but arbitrary entity
//
// WHAT EACH TEST WOULD CATCH, because "green" is not evidence:
//
//   TheWorldEncodesToItself         a bias or an offset in the encode
//   AnIndexSurvivesTheRoundTrip     an encode and a decode that are not
//                                   inverses
//   NothingIsNotZero                THE ONE THE SENTINEL EXISTS FOR: a
//                                   "nothing" word of 0 makes every blob that
//                                   has never been written say the player is
//                                   standing on the world
//   NoGroundIsItsOwnAnswer          the sentinel decoded as an entity
//   NoGroundWritesTheOutputAnyway   a caller reading its variable back on the
//                                   false path and standing on entity 0
//   AnIndexPastTheTableIsNothing    an out-of-range index wrapping into a
//                                   plausible neighbour
//   TheLastRealIndexStillEncodes    the range check one off the other way, so
//                                   the test above cannot pass on a function
//                                   that refuses everything
//   AGarbageWordIsNothing           a word above the table decoding to a real
//                                   entity
//   EveryIndexInTheTableRoundTrips  a mask applied where none belongs
//
//=============================================================================//
#include "gtest/gtest.h"

#include "../game/shared/ground_handle_wire.h"

namespace
{

int IndexOf( int nEncoded )
{
	int nIndex = -99;
	SourceSharpGroundWire_Decode( nEncoded, &nIndex );
	return nIndex;
}

TEST( GroundHandleWire, TheWorldEncodesToItself )
{
	// Entity 0 is worldspawn, which is what a player standing on the map itself
	// is standing on -- by far the commonest value this field ever carries, and
	// the one a bias in the encode would move.
	EXPECT_EQ( 0, SourceSharpGroundWire_Encode( 0 ) );

	int nIndex = -1;
	EXPECT_TRUE( SourceSharpGroundWire_Decode( 0, &nIndex ) );
	EXPECT_EQ( 0, nIndex );
}

TEST( GroundHandleWire, AnIndexSurvivesTheRoundTrip )
{
	int nIndex = -1;
	ASSERT_TRUE( SourceSharpGroundWire_Decode( SourceSharpGroundWire_Encode( 1337 ), &nIndex ) );
	EXPECT_EQ( 1337, nIndex );
}

//-----------------------------------------------------------------------------
// The sentinel is not zero, and that is load-bearing rather than tidy.
//
// A blob starts zeroed and stays zeroed until the server first writes it. If
// "nothing" were 0, every such blob would say the player is standing on entity
// 0 -- the world -- which is a state the movement loop accepts happily and a
// human reading the meter would never question.
//-----------------------------------------------------------------------------
TEST( GroundHandleWire, NothingIsNotZero )
{
	EXPECT_NE( 0, GROUNDWIRE_NONE );

	int nIndex = -1;
	EXPECT_TRUE( SourceSharpGroundWire_Decode( 0, &nIndex ) )
		<< "zero must stay a real entity index, or worldspawn cannot be named";
}

TEST( GroundHandleWire, NoGroundIsItsOwnAnswer )
{
	int nIndex = 7;
	EXPECT_FALSE( SourceSharpGroundWire_Decode( GROUNDWIRE_NONE, &nIndex ) );
}

TEST( GroundHandleWire, NoGroundWritesTheOutputAnyway )
{
	// A caller that ignores the return value must not end up standing on
	// entity 0.
	int nIndex = 7;
	SourceSharpGroundWire_Decode( GROUNDWIRE_NONE, &nIndex );
	EXPECT_EQ( -1, nIndex );
}

TEST( GroundHandleWire, AnIndexPastTheTableIsNothing )
{
	EXPECT_EQ( GROUNDWIRE_NONE, SourceSharpGroundWire_Encode( -1 ) );
	EXPECT_EQ( GROUNDWIRE_NONE, SourceSharpGroundWire_Encode( GROUNDWIRE_INDEX_MASK + 1 ) );
	EXPECT_EQ( GROUNDWIRE_NONE, SourceSharpGroundWire_Encode( 0x7FFFFFFF ) );
}

TEST( GroundHandleWire, TheLastRealIndexStillEncodes )
{
	// The other side of the boundary, so AnIndexPastTheTableIsNothing cannot
	// pass on an encode that refuses everything.
	EXPECT_EQ( GROUNDWIRE_INDEX_MASK, SourceSharpGroundWire_Encode( GROUNDWIRE_INDEX_MASK ) );
	EXPECT_EQ( GROUNDWIRE_INDEX_MASK, IndexOf( GROUNDWIRE_INDEX_MASK ) );
}

TEST( GroundHandleWire, AGarbageWordIsNothing )
{
	// A field that never resolved, or one read at the wrong offset, produces an
	// arbitrary word. Anything that is not an index is "no ground" rather than
	// whichever entity the low bits happen to name.
	EXPECT_FALSE( SourceSharpGroundWire_Decode( GROUNDWIRE_INDEX_MASK + 1, NULL ) );
	EXPECT_FALSE( SourceSharpGroundWire_Decode( -1, NULL ) );
	EXPECT_FALSE( SourceSharpGroundWire_Decode( 0x7FFFFFFF, NULL ) );
}

TEST( GroundHandleWire, EveryIndexInTheTableRoundTrips )
{
	for ( int nIndex = 0; nIndex <= GROUNDWIRE_INDEX_MASK; ++nIndex )
	{
		const int nEncoded = SourceSharpGroundWire_Encode( nIndex );

		ASSERT_NE( GROUNDWIRE_NONE, nEncoded ) << "index " << nIndex;
		EXPECT_EQ( nIndex, IndexOf( nEncoded ) ) << "index " << nIndex;
	}
}

}  // namespace
