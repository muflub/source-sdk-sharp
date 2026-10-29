// CDiagnosticsHooks over the tier0 port: the spew hook it installs through a thunk bound
// to it, the handler every message still reaches, and what its destruction leaves on
// tier0 -- the previous handler when it is on top, the other DLL's hook when that one
// chained after it.

#include "gtest/gtest.h"

#include "tier0/dbg.h"

#include "diagnostics_hooks.h"

#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>

#include "ss_platform.h" // SS_BIND_IMPORT

#ifdef _MSC_VER
// As in tier0_spew_port_test: MSVC keeps the dead fatal arm of dbg.h's Assert,
// and with it a reference to _ExitOnFatalAssert, which the port does not define
// and this binary links no tier0 for. Defined here as tier0's is.
#pragma warning(disable : 4273) // defining what dbg.h declares dllimport

void _ExitOnFatalAssert(const tchar* pFile, int line)
{
	fprintf(stderr, "Fatal assert failed: %s, line %d.  Application exiting.\n", pFile, line);
	exit(EXIT_FAILURE);
}
SS_BIND_IMPORT(_ExitOnFatalAssert)
#endif

namespace
{
std::vector<std::string> g_Reached;

SpewRetval_t Previous(SpewType_t, const tchar* pText)
{
	g_Reached.push_back(pText ? pText : "");
	return SPEW_CONTINUE;
}

SpewRetval_t OtherDllsHook(SpewType_t, const tchar*)
{
	return SPEW_CONTINUE;
}

class DiagnosticsHooks : public ::testing::Test
{
protected:
	DiagnosticsHooks()
	{
		g_Reached.clear();
		SpewOutputFunc(Previous);
	}

	~DiagnosticsHooks() override { SpewOutputFunc(Previous); }
};
} // namespace

TEST_F(DiagnosticsHooks, InstallingPutsItsHookOnTopAndMessagesStillReachThePrevious)
{
	CDiagnosticsHooks hooks;
	hooks.Install();

	EXPECT_NE((SpewOutputFunc_t)Previous, GetSpewOutputFunc());

	// Install says how asserts behave, through the hook it just installed.
	ASSERT_FALSE(g_Reached.empty());
	g_Reached.clear();

	Msg("hello\n");
	ASSERT_EQ(1u, g_Reached.size());
	EXPECT_EQ("hello\n", g_Reached[0]);
}

TEST_F(DiagnosticsHooks, ASecondInstallKeepsTheFirst)
{
	CDiagnosticsHooks hooks;
	hooks.Install();
	const SpewOutputFunc_t pInstalled = GetSpewOutputFunc();

	hooks.Install();
	EXPECT_EQ(pInstalled, GetSpewOutputFunc());

	g_Reached.clear();
	Msg("once\n");
	EXPECT_EQ(1u, g_Reached.size()) << "a hook chained to itself would deliver twice or recurse";
}

TEST_F(DiagnosticsHooks, DestroyedOnTopItPutsThePreviousHandlerBack)
{
	{
		CDiagnosticsHooks hooks;
		hooks.Install();
	}

	EXPECT_EQ((SpewOutputFunc_t)Previous, GetSpewOutputFunc());
}

TEST_F(DiagnosticsHooks, DestroyedUnderAnotherHookItLeavesThatHookInstalled)
{
	{
		CDiagnosticsHooks hooks;
		hooks.Install();
		SpewOutputFunc(OtherDllsHook);
	}

	EXPECT_EQ((SpewOutputFunc_t)OtherDllsHook, GetSpewOutputFunc());
}

TEST_F(DiagnosticsHooks, NeverInstalledItLeavesTier0Alone)
{
	{
		CDiagnosticsHooks hooks;
	}

	EXPECT_EQ((SpewOutputFunc_t)Previous, GetSpewOutputFunc());
}
