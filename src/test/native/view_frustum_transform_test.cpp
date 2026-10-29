// view_frustum_transform.h: view_scene.cpp's FrustumTransform, which
// ScreenTransform (c_pixel_visibility.cpp's projection) runs on (C7 step 2).

#include "gtest/gtest.h"

#include "../game/client/view_frustum_transform.h"

namespace
{
// x, y pass through; w is z + 1, so a point at z = 1 halves.
const float kMatrix[16] = {
	1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1,
};
} // namespace

TEST(ViewFrustumTransform, APointInFrontIsDividedByW)
{
	const float p[3] = {4, 6, 1};
	float s[3] = {9, 9, 9};
	EXPECT_EQ(0, ViewFrustumTransform(kMatrix, p, s));
	EXPECT_FLOAT_EQ(2.0f, s[0]);
	EXPECT_FLOAT_EQ(3.0f, s[1]);
	EXPECT_FLOAT_EQ(0.0f, s[2]);
}

TEST(ViewFrustumTransform, APointBehindIsPushedFarOffScreen)
{
	const float p[3] = {1, -2, -3};
	float s[3];
	EXPECT_EQ(1, ViewFrustumTransform(kMatrix, p, s));
	EXPECT_FLOAT_EQ(100000.0f, s[0]);
	EXPECT_FLOAT_EQ(-200000.0f, s[1]);
}

TEST(ViewFrustumTransform, WJustUnderTheEpsilonIsBehind)
{
	const float p[3] = {1, 1, 0.0009f - 1.0f};
	float s[3];
	EXPECT_EQ(1, ViewFrustumTransform(kMatrix, p, s));
}
