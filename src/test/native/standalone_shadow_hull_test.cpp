//========= SourceSharp — tests for the placing shadow's box =================//
//
// The shadow under a blueprint went map-sized. The sentry's placement model
// carries the box its art was authored in -- measured from
// models/buildables/sentry1_blueprint.mdl's hull, about 1510 a side -- and the
// client's answer to "what does this entity's shadow cover" was that box. The
// floor darkened half a screen away from the blueprint, under terrain that
// casts nothing.
//
// Nothing errors when this is wrong: the shadow projects, covers, and is
// merely enormous, which is why it survived until someone stood a blueprint on
// crete and looked.
//
// The fix is stock's own (C_ObjectSentrygun::GetShadowRenderBounds,
// c_obj_sentrygun.cpp:588): while the placing flag is up the shadow is the
// collision hull widened by (10, 10, 0), never the model's render bounds. See
// game/client/standalone_shadow_hull.h.
//
// The measured hull is the regression's number: a placed sentry on crete has a
// hull about 44 wide (sentry1's collision) and a model box about 3020, so the
// two answers differ by two orders of magnitude and either mistake is visible
// in the assertion.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/client/standalone_shadow_hull.h"

namespace
{

void WithObb(float obbMins[3], float obbMaxs[3], float x0, float y0, float z0, float x1, float y1, float z1)
{
	obbMins[0] = x0;
	obbMins[1] = y0;
	obbMins[2] = z0;
	obbMaxs[0] = x1;
	obbMaxs[1] = y1;
	obbMaxs[2] = z1;
}

//-----------------------------------------------------------------------------
// THE REGRESSION. A map-sized model box must not reach the shadow: the answer
// is the hull plus the bloat, on x and y, whatever the model's own box says.
//-----------------------------------------------------------------------------
TEST(StandaloneShadowHull, TheBoxIsTheHullWidenedNotTheModels)
{
	float obbMins[3], obbMaxs[3], mins[3], maxs[3];
	WithObb(obbMins, obbMaxs, -22.0f, -22.0f, 0.0f, 22.0f, 44.0f, 60.0f);

	StandaloneShadowHull_Bounds(obbMins, obbMaxs, mins, maxs);

	EXPECT_NEAR(-32.0f, mins[0], 0.01f);
	EXPECT_NEAR(-32.0f, mins[1], 0.01f);
	EXPECT_NEAR(32.0f, maxs[0], 0.01f);
	EXPECT_NEAR(54.0f, maxs[1], 0.01f);

	// Two orders of magnitude narrower than the model box the old path answered.
	EXPECT_LT(maxs[0] - mins[0], 100.0f);
}

//-----------------------------------------------------------------------------
// The bloat is horizontal only: z passes through untouched, so the shadow's
// height follows the hull the cast is aimed with (bbBloat is (10, 10, 0)).
//-----------------------------------------------------------------------------
TEST(StandaloneShadowHull, TheBloatNeverTouchesZ)
{
	float obbMins[3], obbMaxs[3], mins[3], maxs[3];
	WithObb(obbMins, obbMaxs, 0.0f, 0.0f, -3.5f, 10.0f, 10.0f, 72.0f);

	StandaloneShadowHull_Bounds(obbMins, obbMaxs, mins, maxs);

	EXPECT_NEAR(-3.5f, mins[2], 0.01f);
	EXPECT_NEAR(72.0f, maxs[2], 0.01f);
}

//-----------------------------------------------------------------------------
// A hull collapsed to a point still widens: the shadow of a placing object
// whose collision was never sized is a small box at the origin, not a point
// that projects nothing.
//-----------------------------------------------------------------------------
TEST(StandaloneShadowHull, APointHullStillWidens)
{
	float obbMins[3], obbMaxs[3], mins[3], maxs[3];
	WithObb(obbMins, obbMaxs, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f);

	StandaloneShadowHull_Bounds(obbMins, obbMaxs, mins, maxs);

	EXPECT_NEAR(-StandaloneShadowHull_BloatXY, mins[0], 0.01f);
	EXPECT_NEAR(StandaloneShadowHull_BloatXY, maxs[1], 0.01f);
}

} // namespace
