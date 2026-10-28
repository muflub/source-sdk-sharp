//=============================================================================//
// THE CLIENT'S REFUSAL COUNTER, RUN -- plan_entity.md 5b.
//
// THE SERVER'S TEST, CLIENT SIDE. sourcesharp/test/shell_gamedll_refusals_test.cpp
// is this file's original, because the user's instruction for this step is that
// IBaseClientDLL be implemented the way IServerGameDLL is: the refusals
// mechanism is the half of that shape which is about BEHAVIOUR, so it gets the
// same evidence and not merely the same class name.
//
// The body's properties -- that a refusal counts, that it warns, that a second
// call to the same method opens no second row, that two counters share nothing
// -- can only be checked by RUNNING it, so this compiles the SHIPPED header
// (sourcesharp/game/client/shell_clientdll_refusals.h). Nothing here is a
// transcription of it.
//
// The ARRANGEMENT in shell_clientdll.cpp -- which methods refuse, and that
// nothing on a per-frame path does -- is source-read, because that file
// includes cbase.h's whole world and can never be linked into a gtest. That
// half lives in shell_clientdll_test.cpp, beside the rest of the shell's
// arrangement facts; what is here is the counter itself.
//
// "TWO COUNTERS SHARE NOTHING" IS THE FACT THAT IS EASY TO MISS. It is the only
// way, from inside a gtest, to tell a FIELD from the file-scope static this
// project's rules forbid: if the count lived in a static, two instances would
// see each other's calls and the second one's Total() would not be one.
//=============================================================================//

#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "shell_clientdll_refusals.h"

//-----------------------------------------------------------------------------
// The warning half, defined here rather than linked from tier0. This is the
// seam the header declares, and it is what lets the test assert that a refusal
// SAYS something as well as counting -- a counter nobody reads until they think
// to is not what the mechanism is for.
//-----------------------------------------------------------------------------
namespace
{

struct RecordedWarning
{
	std::string strMethod;
	int nTotal;
};

std::vector<RecordedWarning> g_Warnings;

} // namespace

void ShellClientDll_RefusalWarning( const char *pMethod, int nTotal )
{
	RecordedWarning rec;
	rec.strMethod = ( pMethod != NULL ) ? pMethod : "(unnamed)";
	rec.nTotal = nTotal;
	g_Warnings.push_back( rec );
}

namespace
{

class ClientDllRefusals : public ::testing::Test
{
protected:
	void SetUp() override { g_Warnings.clear(); }
};

// The two methods the shell actually refuses. Spelt as the shell spells them,
// which is ClientDllProbeName's spelling -- so a rename in the probe table that
// forgot this file would show up as a name mismatch rather than as nothing.
const char *const kChat = "IBaseClientDLL::BHaveChatSuspensionInCurrentMatch";
const char *const kVoice = "IBaseClientDLL::DisplayVoiceUnavailableMessage";

} // namespace

TEST_F( ClientDllRefusals, AFreshCounterIsZero )
{
	CShellClientDllRefusals refusals;

	EXPECT_EQ( 0, refusals.Total() );
	EXPECT_EQ( 0, refusals.MethodCount() );
}

TEST_F( ClientDllRefusals, AFreshCounterWarnsAboutNothing )
{
	CShellClientDllRefusals refusals;
	(void)refusals; // constructed for its constructor's (absent) warnings

	EXPECT_TRUE( g_Warnings.empty() );
}

TEST_F( ClientDllRefusals, OneRefusalCountsOne )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( kChat );

	EXPECT_EQ( 1, refusals.Total() );
}

TEST_F( ClientDllRefusals, OneRefusalOpensOneRow )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( kChat );

	ASSERT_EQ( 1, refusals.MethodCount() );
	EXPECT_STREQ( kChat, refusals.MethodAt( 0 ) );
	EXPECT_EQ( 1, refusals.CountAt( 0 ) );
}

// The gate's whole shape: "did any refusal fire" is Total(), and "WHICH one" is
// the row. A tally that could answer the first and not the second would name no
// method, and a finding that cannot name the method is not one anybody can act
// on.
TEST_F( ClientDllRefusals, TwoMethodsAreTwoRows )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( kChat );
	refusals.Unreachable( kVoice );

	EXPECT_EQ( 2, refusals.Total() );
	EXPECT_EQ( 2, refusals.MethodCount() );
	EXPECT_EQ( 1, refusals.CountOf( kChat ) );
	EXPECT_EQ( 1, refusals.CountOf( kVoice ) );
}

TEST_F( ClientDllRefusals, TheSameMethodTwiceIsOneRowAndTwoCalls )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( kChat );
	refusals.Unreachable( kChat );

	EXPECT_EQ( 2, refusals.Total() );
	EXPECT_EQ( 1, refusals.MethodCount() );
	EXPECT_EQ( 2, refusals.CountOf( kChat ) );
}

// Matched by strcmp and not by pointer identity. The names are string literals
// in one translation unit today, so identity would work today and would stop
// working silently the moment one moved -- and the failure would be a SECOND
// ROW for the same method rather than an error.
TEST_F( ClientDllRefusals, ARowIsFoundByTextAndNotByPointer )
{
	CShellClientDllRefusals refusals;

	std::string copy( kChat );
	refusals.Unreachable( kChat );
	refusals.Unreachable( copy.c_str() );

	EXPECT_EQ( 1, refusals.MethodCount() );
	EXPECT_EQ( 2, refusals.CountOf( kChat ) );
}

TEST_F( ClientDllRefusals, ARefusalWarns )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( kChat );

	ASSERT_EQ( 1u, g_Warnings.size() );
	EXPECT_EQ( std::string( kChat ), g_Warnings[0].strMethod );
}

// The warning carries the RUNNING TOTAL, so the line a reader sees in the
// console carries the same number cs_cl_refusals will.
TEST_F( ClientDllRefusals, TheWarningCarriesTheRunningTotal )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( kChat );
	refusals.Unreachable( kVoice );

	ASSERT_EQ( 2u, g_Warnings.size() );
	EXPECT_EQ( 1, g_Warnings[0].nTotal );
	EXPECT_EQ( 2, g_Warnings[1].nTotal );
}

// NOT RATE LIMITED, and that is deliberate on both sides: the claim about a
// refused method is that it is unreachable, so the expected number of lines is
// zero, and a flood is that claim being wrong in the loudest available way.
TEST_F( ClientDllRefusals, EveryRefusalWarnsAndNotJustTheFirst )
{
	CShellClientDllRefusals refusals;
	for ( int i = 0; i < 5; ++i )
		refusals.Unreachable( kChat );

	EXPECT_EQ( 5u, g_Warnings.size() );
}

TEST_F( ClientDllRefusals, ANullMethodStillCountsTowardTheTotal )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( NULL );

	EXPECT_EQ( 1, refusals.Total() );
	EXPECT_EQ( 0, refusals.MethodCount() );
}

TEST_F( ClientDllRefusals, ANullMethodStillWarns )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( NULL );

	ASSERT_EQ( 1u, g_Warnings.size() );
	EXPECT_EQ( std::string( "(unnamed)" ), g_Warnings[0].strMethod );
}

// A table that overflowed still counts the total, which IS the gate. Losing the
// row is better than losing the number, and a tally that dropped the total once
// it ran out of rows would report zero refusals on the run with the most.
TEST_F( ClientDllRefusals, AnOverflowingTableKeepsTheTotal )
{
	CShellClientDllRefusals refusals;

	std::vector<std::string> names;
	for ( int i = 0; i < CShellClientDllRefusals::kMaxMethods + 5; ++i )
		names.push_back( "IBaseClientDLL::Method" + std::to_string( i ) );

	for ( size_t i = 0; i < names.size(); ++i )
		refusals.Unreachable( names[i].c_str() );

	EXPECT_EQ( (int)names.size(), refusals.Total() );
	EXPECT_EQ( CShellClientDllRefusals::kMaxMethods, refusals.MethodCount() );
}

TEST_F( ClientDllRefusals, ARowIndexOutsideTheTableAnswersRatherThanReadingPastIt )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( kChat );

	EXPECT_EQ( (const char *)NULL, refusals.MethodAt( -1 ) );
	EXPECT_EQ( (const char *)NULL, refusals.MethodAt( 1 ) );
	EXPECT_EQ( 0, refusals.CountAt( -1 ) );
	EXPECT_EQ( 0, refusals.CountAt( 1 ) );
}

TEST_F( ClientDllRefusals, CountOfAMethodThatNeverFiredIsZero )
{
	CShellClientDllRefusals refusals;
	refusals.Unreachable( kChat );

	EXPECT_EQ( 0, refusals.CountOf( kVoice ) );
	EXPECT_EQ( 0, refusals.CountOf( NULL ) );
}

// THE FIELD TEST. The only way, from inside a gtest, to tell a member from the
// file-scope static this project's rules forbid: if the count lived in a static
// the second instance would see the first one's call.
TEST_F( ClientDllRefusals, TwoCountersShareNothing )
{
	CShellClientDllRefusals first;
	CShellClientDllRefusals second;

	first.Unreachable( kChat );

	EXPECT_EQ( 1, first.Total() );
	EXPECT_EQ( 0, second.Total() );
	EXPECT_EQ( 0, second.MethodCount() );
}
