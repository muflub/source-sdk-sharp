//========= SourceSharp — tests for the trace filter's shape clause ==========//
//
// "Is this the sort of thing this trace stops on at all", out of
// StandardFilterRules. Stock states it over a CBaseEntity, which is why every
// entity on the parallel hierarchy skipped it entirely and was waved through by
// every trace as though it were a static prop; the header exists so both
// hierarchies read ONE rule rather than two copies that can drift.
//
// What is pinned is the rule, over the three facts it is made of, rather than
// how either hierarchy resolves them — the resolution is an ICollideable and a
// model index on one side and a CBaseEntity on the other, and neither exists in
// a binary with no engine in it. The rule does not care, which is the point.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/standard_filter_shape.h"

namespace
{

//-----------------------------------------------------------------------------
// The world-like arm: a brush model that is solid as a brush. A func_wall, a
// door, a func_brush. Every trace stops on one, including a trace that asked
// only for CONTENTS_SOLID — which is what "is the world in the way" means.
//-----------------------------------------------------------------------------
TEST( StandardFilterShape, ABrushThatIsSolidAsOneStopsEveryTrace )
{
	EXPECT_TRUE( StandardFilterShapePasses( true, true, false ) )
		<< "a solid brush model stops a trace that asked only for the world";

	EXPECT_TRUE( StandardFilterShapePasses( true, true, true ) )
		<< "and it still stops one that asked for entities too";
}

//-----------------------------------------------------------------------------
// A studio prop. The case the whole thing was about: the blast's world retrace
// asks CONTENTS_SOLID alone and a crate is not the world, so it must NOT stop
// there. A managed prop was being waved through this and the retrace stopped on
// the victim itself, which zeroed the victim's damage outright.
//-----------------------------------------------------------------------------
TEST( StandardFilterShape, APropIsNotTheWorld )
{
	EXPECT_FALSE( StandardFilterShapePasses( false, true, false ) )
		<< "a crate does not stop a trace that asked only for the world";
}

//-----------------------------------------------------------------------------
// ...and is not invisible either. The same crate, asked for by a trace that
// wants entities — a bullet, a hull sweep, the blast's own visibility trace —
// is hit. Without this half the fix would make every managed prop non-solid to
// everything, which is a far worse bug than the one being fixed.
//-----------------------------------------------------------------------------
TEST( StandardFilterShape, APropStopsATraceThatAskedForEntities )
{
	EXPECT_TRUE( StandardFilterShapePasses( false, true, true ) )
		<< "a crate stops a bullet";

	EXPECT_TRUE( StandardFilterShapePasses( false, false, true ) )
		<< "whatever its solid type, once entities were asked for";
}

//-----------------------------------------------------------------------------
// Stock's condition is a DISJUNCTION — not a brush model, OR not solid as a
// brush — and both halves have to reach the same arm. A brush model that is
// SOLID_BBOX or SOLID_NONE is an entity, not world, however brushy its geometry.
// A rule that tested only the model type would pass this and every test above.
//-----------------------------------------------------------------------------
TEST( StandardFilterShape, ABrushModelThatIsNotSolidAsOneIsStillAnEntity )
{
	EXPECT_FALSE( StandardFilterShapePasses( true, false, false ) )
		<< "a brush model that is not solid as a brush is not the world";

	EXPECT_TRUE( StandardFilterShapePasses( true, false, true ) )
		<< "...but is still an entity to a trace that wanted one";
}

//-----------------------------------------------------------------------------
// The other half of the same trap: a rule that tested only the SOLID type would
// pass every case above except this one. Neither fact alone decides it.
//-----------------------------------------------------------------------------
TEST( StandardFilterShape, NeitherFactAloneDecidesIt )
{
	// Same solid type, different model type -> different answer.
	EXPECT_NE( StandardFilterShapePasses( true,  true, false ),
	           StandardFilterShapePasses( false, true, false ) )
		<< "the model type matters when the solid type is held still";

	// Same model type, different solid type -> different answer.
	EXPECT_NE( StandardFilterShapePasses( true, true,  false ),
	           StandardFilterShapePasses( true, false, false ) )
		<< "the solid type matters when the model type is held still";
}

//-----------------------------------------------------------------------------
// Nothing at all — no model, not solid, and a world-only mask. The arm a
// managed point entity takes, and the answer that keeps it out of the way.
//-----------------------------------------------------------------------------
TEST( StandardFilterShape, NothingSolidStopsNothing )
{
	EXPECT_FALSE( StandardFilterShapePasses( false, false, false ) )
		<< "a point entity is not in anybody's way";
}

} // namespace
