// ss_ci_skip.h: the CI skip takes CI's exact setting and nothing else, and
// a skipped test stops before its body.
#include "ss_ci_skip.h"

#include <cstdlib>
#include <string>

namespace
{
// The variable as it was, put back after each test that changes it.
struct SavedCi
{
	bool bHad;
	std::string value;

	SavedCi()
	{
		const char* p = std::getenv("SOURCESHARP_CI");
		bHad = p != nullptr;
		value = p ? p : "";
	}

	~SavedCi()
	{
		if (bHad)
			setenv("SOURCESHARP_CI", value.c_str(), 1);
		else
			unsetenv("SOURCESHARP_CI");
	}
};

bool g_bBodyRan = false;
}

TEST(SsCiSkip, OnlyTheExactValueOneIsCi)
{
	EXPECT_TRUE(ss_test::IsCiValue("1"));
	EXPECT_FALSE(ss_test::IsCiValue(nullptr));
	EXPECT_FALSE(ss_test::IsCiValue(""));
	EXPECT_FALSE(ss_test::IsCiValue("0"));
	EXPECT_FALSE(ss_test::IsCiValue("true"));
	EXPECT_FALSE(ss_test::IsCiValue("10"));
}

TEST(SsCiSkip, OnCiReadsTheEnvironment)
{
	SavedCi saved;
	setenv("SOURCESHARP_CI", "1", 1);
	EXPECT_TRUE(ss_test::OnCi());
	unsetenv("SOURCESHARP_CI");
	EXPECT_FALSE(ss_test::OnCi());
}

// The macro's two arms, through a helper so the skip ends the helper rather
// than this test: off CI the body runs; on CI it does not.
static void BodyBehindTheSkip()
{
	SS_SKIP_ON_CI("the self-test");
	g_bBodyRan = true;
}

TEST(SsCiSkip, OffCiTheBodyRuns)
{
	SavedCi saved;
	unsetenv("SOURCESHARP_CI");
	g_bBodyRan = false;
	BodyBehindTheSkip();
	EXPECT_TRUE(g_bBodyRan);
}

TEST(SsCiSkip, OnCiTheBodyDoesNotRun)
{
	SavedCi saved;
	setenv("SOURCESHARP_CI", "1", 1);
	g_bBodyRan = false;
	BodyBehindTheSkip();
	unsetenv("SOURCESHARP_CI");
	// GTEST_SKIP in the helper marked this test skipped; what is checked is
	// that the body behind it never ran.
	ASSERT_FALSE(g_bBodyRan);
}
