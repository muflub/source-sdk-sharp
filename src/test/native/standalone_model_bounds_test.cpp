//========= SourceSharp — tests for model-bounds adoption ====================//
//
// The other half of the lift that rose through its passenger.
//
// CStandaloneEntity::SetModel takes an entity's collision size from its model,
// which is right for the brush entities the hierarchy was built around and
// wrong for exactly one thing on it. CStandalonePlayer::Spawn installs the
// movement hull and then calls managed OnPlayerSpawn, whose first line is
// SetModel(PlayerModel) -- so the hull was overwritten by the citizen mesh's
// box on every spawn.
//
// Measured on ss_sandbox at 821aea34, a player standing on belts_lift:
//
//     cand 'player' originZ=26 mins=-13,-13,-5.93788 maxs=13,13,72
//
// Feet reported at 20.06 for a player whose feet are at 26. Riders.Carry asks
// whether a candidate's feet are within Riders.Sink (four units) of the
// mover's top face; six units of phantom shin put them outside it every tick
// of the rise.
//
// This is worth pinning rather than eyeballing for the same reason the movetype
// was: nothing errors. A player with the wrong box still walks, still shoots,
// still collides -- it is merely measured wrong, by every caller, forever. And
// the direction that would have shown it up is the one direction the carry is
// not responsible for.
//
// See game/server/standalone_model_bounds.h.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/standalone_model_bounds.h"

namespace
{

//-----------------------------------------------------------------------------
// THE REGRESSION. A player has a model -- a citizen with a perfectly good
// bounding box -- and must keep the movement hull anyway. This is the case that
// was wrong, and it was wrong in the direction that looks most reasonable: the
// model DID have bounds, so the old condition said yes.
//-----------------------------------------------------------------------------
TEST( StandaloneModelBounds, PlayerKeepsItsHullDespiteAModelWithBounds )
{
	EXPECT_FALSE( StandaloneModel_AdoptsBounds( true, true ) );
}

//-----------------------------------------------------------------------------
// And a player with a boundless model is refused for the SAME reason rather
// than by accident of the second argument -- so that a future model change
// cannot make a player start adopting again.
//-----------------------------------------------------------------------------
TEST( StandaloneModelBounds, PlayerRefusalDoesNotDependOnTheModel )
{
	EXPECT_FALSE( StandaloneModel_AdoptsBounds( true, false ) );
	EXPECT_EQ( StandaloneModel_AdoptsBounds( true, true ), StandaloneModel_AdoptsBounds( true, false ) );
}

//-----------------------------------------------------------------------------
// What the adoption is FOR, kept working. A brush entity is a slab somewhere in
// the map whose origin is usually the world's, so its size can only come from
// its model -- a door that stopped adopting would collapse to a point at the
// map origin, which is a far louder failure than the one being fixed and must
// not be traded for it.
//-----------------------------------------------------------------------------
TEST( StandaloneModelBounds, BrushEntityStillTakesItsSizeFromItsModel )
{
	EXPECT_TRUE( StandaloneModel_AdoptsBounds( false, true ) );
}

//-----------------------------------------------------------------------------
// An empty box is what modelinfo answers for a model carrying no size, and
// adopting it would collapse the entity. Refused before this change and after.
//-----------------------------------------------------------------------------
TEST( StandaloneModelBounds, ABoundlessModelSetsNothing )
{
	EXPECT_FALSE( StandaloneModel_AdoptsBounds( false, false ) );
}

} // namespace
