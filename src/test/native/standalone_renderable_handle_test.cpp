//========= SourceSharp — standalone_renderable_handle.h tests ================//
//
// The draw batch asks a renderable for its entity handle only when it is an
// entity: a non-entity renderable's GetRefEHandle is a null dereference
// (CDefaultClientRenderable), which ss_sandbox's first frame hit.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/client/standalone_renderable_handle.h"

namespace
{

struct FakeHandle
{
	int nValue;

	int ToInt() const { return nValue; }
};

struct FakeUnknown
{
	void* pEntity;
	int nHandleAsks;
	FakeHandle handle;

	void* GetIClientEntity() { return pEntity; }

	const FakeHandle& GetRefEHandle()
	{
		++nHandleAsks;
		return handle;
	}
};

TEST(StandaloneRenderableHandle, ANonEntityIsNeverAskedForAHandle)
{
	FakeUnknown unknown = {nullptr, 0, {77}};
	uint32_t nWord = 5;
	EXPECT_FALSE(RenderableEntityHandleWord(&unknown, nWord));
	EXPECT_EQ(0, unknown.nHandleAsks);
	EXPECT_EQ(5u, nWord);
}

TEST(StandaloneRenderableHandle, AnEntityAnswersItsHandleWord)
{
	int entity = 0;
	FakeUnknown unknown = {&entity, 0, {0x12345}};
	uint32_t nWord = 0;
	EXPECT_TRUE(RenderableEntityHandleWord(&unknown, nWord));
	EXPECT_EQ(1, unknown.nHandleAsks);
	EXPECT_EQ(0x12345u, nWord);
}

TEST(StandaloneRenderableHandle, NoUnknownIsNoHandle)
{
	uint32_t nWord = 9;
	EXPECT_FALSE(RenderableEntityHandleWord((FakeUnknown*)nullptr, nWord));
	EXPECT_EQ(9u, nWord);
}

} // namespace
