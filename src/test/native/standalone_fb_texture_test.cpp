//========= SourceSharp — the shell's frame-buffer-sampler answer ============//
//
// The translucent passes refresh the framebuffer texture before they draw a
// renderable that samples it, and the refresh reached the shell's census only
// as the constant false (shell_standalone_client.h had both answers false), so
// a cloaked spy's VertexLitGeneric cloak pass refracted nothing and drew a
// solid body. The decision is the model's own materials, asked through the
// engine's modelinfo (C_BaseAnimating::UsesPowerOfTwoFrameBufferTexture,
// c_baseanimating.cpp:829-832) — these facts hold that ask over a fake
// modelinfo whose recorder sits at the compiler's slot.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <string.h>

#include "client/native/standalone_fb_texture.h"
#include "shared/native/vtable_hook.h"

namespace
{

//-----------------------------------------------------------------------------
// A fake object: word 0 is the vtable, the recorder at the compiler's slot.
//-----------------------------------------------------------------------------
struct FakeTable
{
	void* slots[256];

	FakeTable() { memset(slots, 0, sizeof(slots)); }

	template <typename Fn>
	void Put(Fn pmf, void* pRecorder)
	{
		const int slot = VTableHook::MemberOf(pmf).slot;
		ASSERT_GE(slot, 0);
		ASSERT_LT(slot, 256);
		slots[slot] = pRecorder;
	}
};

const model_t* const kSamplerModel = reinterpret_cast<const model_t*>(0x1000);
const model_t* const kPlainModel = reinterpret_cast<const model_t*>(0x2000);

// The ask the fake modelinfo saw, and what it answers next.
struct Ask
{
	const model_t* model;
	int skin;
	int body;
	void* renderable;
	bool answers;
	bool asked;
};

Ask g_ask;

bool Rec_IsUsingFBTexture(const void*, const model_t* model, int nSkin, int nBody, void* pClientRenderable)
{
	g_ask.asked = true;
	g_ask.model = model;
	g_ask.skin = nSkin;
	g_ask.body = nBody;
	g_ask.renderable = pClientRenderable;
	return g_ask.answers;
}

struct ModelInfoTable : FakeTable
{
	ModelInfoTable() { Put(&IVModelInfoClient::IsUsingFBTexture, (void*)&Rec_IsUsingFBTexture); }
};

ModelInfoTable g_modelInfoTable;

// Word 0 is the vtable pointer: it names the recorder table.
struct FakeObject
{
	void** vtable;
};

FakeObject g_modelInfoObject = {g_modelInfoTable.slots};
IVModelInfoClient* g_pModelInfo = reinterpret_cast<IVModelInfoClient*>(&g_modelInfoObject);

} // namespace

// The engine's interface pointer the decision reads (shell_clientdll.cpp's).
// A test links no client shell, so it owns the definition.
IVModelInfoClient* modelinfo = nullptr;

namespace
{

struct Tag
{
	int unused;
};

} // namespace

TEST(StandaloneFBTexture, AMaterialThatSamplesTheFrameBufferAnswersTrue)
{
	modelinfo = g_pModelInfo;
	g_ask = Ask{};
	g_ask.answers = true;

	Tag renderable;
	EXPECT_TRUE(SourceSharp_ShellUsesFBTexture(kSamplerModel, 2, 7, &renderable));

	// The ask carried the whole decision: the model, the skin, the body and the
	// renderable (c_baseanimating.cpp:831's argument list).
	EXPECT_TRUE(g_ask.asked);
	EXPECT_EQ(kSamplerModel, g_ask.model);
	EXPECT_EQ(2, g_ask.skin);
	EXPECT_EQ(7, g_ask.body);
	EXPECT_EQ(&renderable, g_ask.renderable);
}

TEST(StandaloneFBTexture, AMaterialThatDoesNotSampleItAnswersFalse)
{
	modelinfo = g_pModelInfo;
	g_ask = Ask{};
	g_ask.answers = false;

	Tag renderable;
	EXPECT_FALSE(SourceSharp_ShellUsesFBTexture(kPlainModel, 0, 0, &renderable));
	EXPECT_TRUE(g_ask.asked);
}

TEST(StandaloneFBTexture, AShellWithNoModelDoesNotSampleIt)
{
	modelinfo = g_pModelInfo;
	g_ask = Ask{};
	g_ask.answers = true;

	// GetModel answers NULL before an entity has one: nothing is asked and the
	// frame buffer is not refreshed for a model-less renderable.
	EXPECT_FALSE(SourceSharp_ShellUsesFBTexture(NULL, 0, 0, nullptr));
	EXPECT_FALSE(g_ask.asked);
}

TEST(StandaloneFBTexture, AnUnconnectedModelInfoAnswersFalse)
{
	// Before the client's Connect ran (modelinfo still NULL), nothing to ask.
	modelinfo = nullptr;
	g_ask = Ask{};
	g_ask.answers = true;

	EXPECT_FALSE(SourceSharp_ShellUsesFBTexture(kSamplerModel, 0, 0, nullptr));
	EXPECT_FALSE(g_ask.asked);
}
