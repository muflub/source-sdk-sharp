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
extern "C" void* g_pThreadPool;
extern "C" void DestroyThreadPool(void* pPool);

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
	// Proof of setup: both fakes are loaded under the game's file names.
	EXPECT_NE(nullptr, ss::FindLoadedSymbol("libtier0.so", "CommandLine_Tier0"));
	EXPECT_NE(nullptr, ss::FindLoadedSymbol("libvstdlib.so", "DestroyThreadPool"));
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

TEST_F(TierBindingsClient, TheThreadPoolIsVstdlibsAndWasCopiedAtLoad)
{
	ASSERT_NE(nullptr, FakeVstdlib_PoolObject());
	EXPECT_EQ(FakeVstdlib_PoolObject(), g_pThreadPool);
}

TEST_F(TierBindingsClient, DestroyThreadPoolReachesVstdlibWithThePool)
{
	char pool;
	const int nBefore = FakeVstdlib_DestroyCount();
	DestroyThreadPool(&pool);
	EXPECT_EQ(nBefore + 1, FakeVstdlib_DestroyCount());
	EXPECT_EQ((void*)&pool, FakeVstdlib_LastDestroyed());
}

TEST_F(TierBindingsClient, AnEventIsBuiltSetAndDestroyedByTier0)
{
	const void* pEvent = nullptr;
	{
		CThreadEvent event(true);
		pEvent = &event;
		EXPECT_TRUE(event.Set());
		EXPECT_TRUE(RecordedOn("CThreadEvent::CThreadEvent", &event)) << Recorded();
		EXPECT_TRUE(RecordedOn("CThreadEvent::Set", &event)) << Recorded();
		ASSERT_GE(FakeTier_CallCount(), 1);
		EXPECT_EQ(1u, FakeTier_Call(0)->nArg) << "the manual-reset flag did not arrive";
		FakeTier_Reset();
	}
	// The destructor, at the end of the scope, on the same object.
	EXPECT_TRUE(RecordedOn("CThreadSyncObject::~CThreadSyncObject", pEvent)) << Recorded();
}

TEST_F(TierBindingsClient, TheReadWriteLockSlowPathsAreTier0s)
{
	CThreadRWLock lock;
	lock.LockForWrite();
	lock.UnlockWrite();
	EXPECT_TRUE(RecordedOn("CThreadRWLock::LockForWrite", &lock)) << Recorded();
	EXPECT_TRUE(RecordedOn("CThreadRWLock::UnlockWrite", &lock)) << Recorded();
}

TEST_F(TierBindingsClient, TheSpinLockIsTier0s)
{
	CThreadSpinRWLock lock;
	lock.LockForRead();
	lock.UnlockRead();
	lock.UnlockWrite();
	EXPECT_TRUE(RecordedOn("CThreadSpinRWLock::LockForRead", &lock)) << Recorded();
	EXPECT_TRUE(RecordedOn("CThreadSpinRWLock::UnlockRead", &lock)) << Recorded();
	EXPECT_TRUE(RecordedOn("CThreadSpinRWLock::UnlockWrite", &lock)) << Recorded();
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
