#include "gtest/gtest.h"

#include "../game/client/mat_stub_swap.h"

#include <vector>

namespace
{

struct FakeSystem
{
	bool bStubMode = false;
	int nCleared = 0;

	void SetInStubMode(bool bOn) { bStubMode = bOn; }

	void ClearBuffers(bool, bool) { nCleared++; }
};

struct FakeStub : FakeSystem
{
	FakeSystem* pReal = nullptr;

	void SetRealMaterialSystem(FakeSystem* p) { pReal = p; }
};

struct FakeEngine
{
	std::vector<FakeSystem*> stubbed;

	void Mat_Stub(FakeSystem* p) { stubbed.push_back(p); }
};

typedef CMatStubSwap<FakeSystem, FakeStub, FakeEngine> Swap;

struct Rig
{
	FakeSystem real;
	FakeStub stub;
	FakeEngine engine;
	FakeSystem* pFirst = &real;
	FakeSystem* pSecond = &real;
	FakeSystem** ppSlots[2] = {&pFirst, &pSecond};
	Swap swap;

	void Begin() { swap.Begin(ppSlots, 2, &stub, &engine); }
};

TEST(MatStubSwap, EverySlotDrawsThroughTheStubWhileItIsOn)
{
	Rig r;
	r.Begin();
	EXPECT_EQ(r.pFirst, &r.stub);
	EXPECT_EQ(r.pSecond, &r.stub);
}

TEST(MatStubSwap, TheStubIsToldTheRealSystemAndTheEngineTheStub)
{
	Rig r;
	r.Begin();
	EXPECT_EQ(r.stub.pReal, &r.real);
	EXPECT_TRUE(r.real.bStubMode);
	ASSERT_EQ(r.engine.stubbed.size(), 1u);
	EXPECT_EQ(r.engine.stubbed[0], &r.stub);
}

TEST(MatStubSwap, EndPutsEverySlotBackAndClearsTheRealBuffers)
{
	Rig r;
	r.Begin();
	r.swap.End();
	EXPECT_EQ(r.pFirst, &r.real);
	EXPECT_EQ(r.pSecond, &r.real);
	EXPECT_FALSE(r.real.bStubMode);
	EXPECT_EQ(r.real.nCleared, 1);
	ASSERT_EQ(r.engine.stubbed.size(), 2u);
	EXPECT_EQ(r.engine.stubbed[1], &r.real);
}

TEST(MatStubSwap, EndWithoutBeginAndASecondEndDoNothing)
{
	Rig r;
	r.swap.End();
	EXPECT_EQ(r.real.nCleared, 0);
	r.Begin();
	r.swap.End();
	r.swap.End();
	EXPECT_EQ(r.real.nCleared, 1);
	EXPECT_EQ(r.engine.stubbed.size(), 2u);
}

} // namespace
