// CServerTempEnts: the temp entity tables built from managed plans, the one ServerClass
// each registers, what a managed reload is answered, and the window the root closes.

#include "gtest/gtest.h"
#include "mathlib_test_env.h"

#include "tempent_registry.h"
#include "tempent_block.h"
#include "server_class.h"

// The list the ServerClass constructor links into; the engine's walk reads it.
ServerClass* g_pServerClassHead = nullptr;

using tempent_test::Block;
using tempent_test::Row;

namespace
{
std::vector<unsigned char> Sparks()
{
	return Block("CTESparks", "DT_TESparks", {Row(DPT_Vector, SOURCESHARP_SENDPROXY_VECTOR_TO_VECTOR)}, {"Direction"});
}

std::vector<unsigned char> Smoke()
{
	return Block("CTESmoke", "DT_TESmoke", {Row(DPT_Float, SOURCESHARP_SENDPROXY_FLOAT_TO_FLOAT)}, {"Scale"});
}

int32_t Build(CServerTempEnts& te, const std::vector<unsigned char>& block, SourceSharpTeBuiltTable_t& out)
{
	out = SourceSharpTeBuiltTable_t();
	return te.TempEntBuild(block.data(), (int32_t)block.size(), &out);
}
} // namespace

TEST(TempEntRegistry, ABlockBuildsATableAndTheServerClassIsKnownAsOurs)
{
	CServerTempEnts te;
	SourceSharpTeBuiltTable_t out;

	ASSERT_EQ(0, Build(te, Sparks(), out));
	EXPECT_EQ(0, out.bExisted);
	ASSERT_NE(nullptr, out.pSendTable);
	EXPECT_STREQ("DT_TESparks", te.BuiltTable(0)->GetName());

	ServerClass* pClass = te.BuiltClass(0);
	ASSERT_NE(nullptr, pClass);
	EXPECT_STREQ("CTESparks", pClass->GetName());
	EXPECT_TRUE(te.IsTempEntServerClass(pClass));

	ServerClass foreign("CStockThing", te.BuiltTable(0));
	EXPECT_FALSE(te.IsTempEntServerClass(&foreign));
	EXPECT_FALSE(te.IsTempEntServerClass(nullptr));
}

TEST(TempEntRegistry, AReloadAskingForABuiltClassIsAnsweredItsFirstHandle)
{
	CServerTempEnts te;
	SourceSharpTeBuiltTable_t first, again;

	ASSERT_EQ(0, Build(te, Sparks(), first));
	ASSERT_EQ(1, Build(te, Smoke(), again));
	ASSERT_EQ(0, Build(te, Sparks(), again));

	EXPECT_EQ(1, again.bExisted);
	EXPECT_EQ(first.pSendTable, again.pSendTable);
	EXPECT_EQ(2, te.BuiltCount());
}

TEST(TempEntRegistry, AfterTheWindowClosesANewClassIsRefusedAndABuiltOneStillAnswers)
{
	CServerTempEnts te;
	SourceSharpTeBuiltTable_t out;
	ASSERT_EQ(0, Build(te, Sparks(), out));

	EXPECT_FALSE(te.RegistrationClosed());
	te.CloseRegistration();
	EXPECT_TRUE(te.RegistrationClosed());

	EXPECT_EQ(-1, Build(te, Smoke(), out));
	EXPECT_EQ(0, Build(te, Sparks(), out));
	EXPECT_EQ(1, te.BuiltCount());
}

TEST(TempEntRegistry, AMalformedBlockOrNoAnswerIsRefused)
{
	CServerTempEnts te;
	const std::vector<unsigned char> block = Sparks();

	EXPECT_EQ(-1, te.TempEntBuild(block.data(), (int32_t)block.size(), nullptr));
	EXPECT_EQ(-1, te.TempEntBuild(block.data(), 3, nullptr));

	SourceSharpTeBuiltTable_t out;
	EXPECT_EQ(-1, te.TempEntBuild(block.data(), 3, &out));
	EXPECT_EQ(0, te.BuiltCount());
}
