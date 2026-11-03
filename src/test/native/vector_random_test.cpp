// CVectorRandom: what the muzzle flashes' scatter relies on.

#include "gtest/gtest.h"

#include "shell_vector_random.h"

TEST(VectorRandom, EveryComponentIsInTheRangeAndTheRangeIsUsed)
{
	CVectorRandom random;
	float flMin = 1.0f, flMax = -1.0f;
	for (int i = 0; i < 1000; i++)
	{
		float v[3];
		random.Fill(-0.5f, 0.5f, v);
		for (float f : v)
		{
			ASSERT_GE(f, -0.5f);
			ASSERT_LE(f, 0.5f);
			flMin = f < flMin ? f : flMin;
			flMax = f > flMax ? f : flMax;
		}
	}
	EXPECT_LT(flMin, -0.4f);
	EXPECT_GT(flMax, 0.4f);
}

TEST(VectorRandom, TheThreeComponentsAreSeparateDraws)
{
	CVectorRandom random;
	float v[3];
	random.Fill(0.0f, 1.0f, v);
	EXPECT_NE(v[0], v[1]);
	EXPECT_NE(v[1], v[2]);
}

TEST(VectorRandom, AnEmptyRangeAnswersItsOneValue)
{
	CVectorRandom random;
	float v[3];
	random.Fill(2.5f, 2.5f, v);
	EXPECT_EQ(2.5f, v[0]);
	EXPECT_EQ(2.5f, v[1]);
	EXPECT_EQ(2.5f, v[2]);
}

TEST(VectorRandom, TwoGeneratorsDrawTheSameSequence)
{
	CVectorRandom a, b;
	for (int i = 0; i < 16; i++)
	{
		float va[3], vb[3];
		a.Fill(-1.0f, 1.0f, va);
		b.Fill(-1.0f, 1.0f, vb);
		ASSERT_EQ(va[0], vb[0]);
		ASSERT_EQ(va[2], vb[2]);
	}
}
