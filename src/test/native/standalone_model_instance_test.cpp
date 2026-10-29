//========= SourceSharp — standalone_model_instance.h tests ===================//
//
// The engine draws an instance's decal list through whatever model the
// renderable has now, so an instance made for one model must not survive a
// change to another: a TF2 player hit as a scout and respawned as a heavy
// crashed the client that drew him.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/client/standalone_model_instance.h"

#include <vector>

namespace
{

constexpr unsigned short kInvalid = 0xFFFF;

using Slot = CStandaloneModelInstance<unsigned short, kInvalid>;

struct FakeRenderable
{
};

struct FakeRender
{
	unsigned short nNext = 10;
	std::vector<unsigned short> created;
	std::vector<unsigned short> destroyed;

	unsigned short CreateInstance(FakeRenderable*)
	{
		created.push_back(nNext);
		return nNext++;
	}

	void DestroyInstance(unsigned short h) { destroyed.push_back(h); }
};

const int g_scout = 1;
const int g_heavy = 2;

TEST(StandaloneModelInstance, TheFirstEnsureCreatesOne)
{
	FakeRender render;
	FakeRenderable renderable;
	Slot slot;

	EXPECT_EQ(kInvalid, slot.Get());
	EXPECT_EQ(10, slot.Ensure(&render, &renderable, &g_scout));
	EXPECT_EQ(1u, render.created.size());
}

TEST(StandaloneModelInstance, TheSameModelKeepsItsInstance)
{
	FakeRender render;
	FakeRenderable renderable;
	Slot slot;

	slot.Ensure(&render, &renderable, &g_scout);
	EXPECT_EQ(10, slot.Ensure(&render, &renderable, &g_scout));
	EXPECT_EQ(1u, render.created.size());
	EXPECT_TRUE(render.destroyed.empty());
}

TEST(StandaloneModelInstance, ANewModelDestroysTheOldInstanceAndMakesAnother)
{
	FakeRender render;
	FakeRenderable renderable;
	Slot slot;

	slot.Ensure(&render, &renderable, &g_scout);
	EXPECT_EQ(11, slot.Ensure(&render, &renderable, &g_heavy));
	ASSERT_EQ(1u, render.destroyed.size());
	EXPECT_EQ(10, render.destroyed[0]);
	EXPECT_EQ(2u, render.created.size());
}

TEST(StandaloneModelInstance, DropIfNotForLeavesNoInstanceForAChangedModel)
{
	FakeRender render;
	FakeRenderable renderable;
	Slot slot;

	slot.Ensure(&render, &renderable, &g_scout);
	slot.DropIfNotFor(&render, &g_heavy);
	EXPECT_EQ(kInvalid, slot.Get());
	ASSERT_EQ(1u, render.destroyed.size());
	EXPECT_EQ(10, render.destroyed[0]);
}

TEST(StandaloneModelInstance, DropIfNotForKeepsTheInstanceOfTheSameModel)
{
	FakeRender render;
	FakeRenderable renderable;
	Slot slot;

	slot.Ensure(&render, &renderable, &g_scout);
	slot.DropIfNotFor(&render, &g_scout);
	EXPECT_EQ(10, slot.Get());
	EXPECT_TRUE(render.destroyed.empty());
}

TEST(StandaloneModelInstance, DestroyReleasesOnceAndForgetsTheModel)
{
	FakeRender render;
	FakeRenderable renderable;
	Slot slot;

	slot.Ensure(&render, &renderable, &g_scout);
	slot.Destroy(&render);
	slot.Destroy(&render);
	EXPECT_EQ(kInvalid, slot.Get());
	EXPECT_EQ(1u, render.destroyed.size());

	// A fresh instance after a destroy, even for the same model.
	EXPECT_EQ(11, slot.Ensure(&render, &renderable, &g_scout));
}

} // namespace
