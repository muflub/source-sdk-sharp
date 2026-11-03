//========= SourceSharp — client.so's tier0 and vstdlib reach the engine's ===//
//
// tier_bindings_client.cpp defines the tier0 and vstdlib names client.so's
// objects import, each reaching the definition in the libtier0.so or
// libvstdlib.so the engine loaded. This binary links the fakes in
// fake_engine_tier/ (named as the game's are) and those bindings, and calls
// the names the way the client's objects do, through tier0's headers: every
// call must arrive in the fake, with the object it was made on, and the two
// pointers must be the fakes' own.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "tier0/threadtools.h"
#include "tier0/icommandline.h"

#include "death_test_no_core.h"
#include "fake_engine_tier/fake_engine_tier.h"
#include "loaded_symbol.h"

#include <string>

extern "C" void* g_pMemAlloc;

namespace
{
// The calls the fake recorded since Reset, as "name@object".
std::string Recorded()
{
	std::string str;
	for (int i = 0; i < FakeTier_CallCount(); ++i)
	{
		const FakeTierCall* pCall = FakeTier_Call(i);
		str += pCall->pszName;
		str += pCall->pThis ? "@obj" : "";
		str += ";";
	}
	return str;
}

bool RecordedOn(const char* pszName, const void* pThis)
{
	for (int i = 0; i < FakeTier_CallCount(); ++i)
	{
		const FakeTierCall* pCall = FakeTier_Call(i);
		if (std::string(pCall->pszName) == pszName && pCall->pThis == pThis)
			return true;
	}
	return false;
}

class TierBindingsClient : public ::testing::Test
{
protected:
	void SetUp() override { FakeTier_Reset(); }
};
} // namespace

TEST_F(TierBindingsClient, TheFakesAreTheLoadedLibrariesTheBindingsLookIn)
{
	// Proof of setup: the fake is loaded under the game's file name.
	EXPECT_NE(nullptr, ss::FindLoadedSymbol("libtier0.so", "CommandLine_Tier0"));
}

TEST_F(TierBindingsClient, CommandLineIsTheEngineTier0s)
{
	ICommandLine* pCommandLine = CommandLine();
	EXPECT_EQ(FakeTier_CommandLineObject(), (void*)pCommandLine);
	EXPECT_TRUE(RecordedOn("CommandLine_Tier0", nullptr)) << Recorded();
}

TEST_F(TierBindingsClient, TheAllocatorIsTier0sAndWasCopiedAtLoad)
{
	ASSERT_NE(nullptr, FakeTier_HeapObject());
	EXPECT_EQ(FakeTier_HeapObject(), g_pMemAlloc);
}

TEST(TierBindingsClientDeathTest, AMissingLibraryStopsTheProcessNamingIt)
{
	SS_DEATH_NO_CORE();
	// The positive arm first: the same call finds a name that is there.
	ASSERT_NE(nullptr, ss::RequireLoadedSymbol("libtier0.so", "CommandLine_Tier0"));
	EXPECT_DEATH(ss::RequireLoadedSymbol("libss_never_loaded.so", "CommandLine_Tier0"),
				 "CommandLine_Tier0: no loaded libss_never_loaded.so defines it");
}

TEST(TierBindingsClientDeathTest, AMissingNameStopsTheProcessNamingIt)
{
	SS_DEATH_NO_CORE();
	EXPECT_DEATH(ss::RequireLoadedSymbol("libtier0.so", "SS_NoSuchTier0Name"),
				 "SS_NoSuchTier0Name: no loaded libtier0.so defines it");
}
