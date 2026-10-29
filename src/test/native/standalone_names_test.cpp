//========= SourceSharp — tests for classname matching =======================//
//
// See standalone_names.h for why this is worth a test of its own: it exists to
// AGREE with CGlobalEntityList::FindEntityByClassname, and every way it can
// disagree produces silence rather than an error. A query that answered for the
// CBaseEntity entities and not the standalone ones under-reports by exactly the
// classes this migration has moved, which reads as "those entities are gone".
//
// WHAT THIS FILE USED TO ALSO COVER (lane 3c-entityshell). Seven facts about
// StandaloneOutputIndex and one about StandaloneNameEquals went with those two
// functions, in the commit that removed them, because neither had a caller left
// in the build:
//
//   StandaloneOutputIndex   0 callers anywhere; the arm that asked it went when
//                           shell_standalone_base.cpp:2292 recorded that
//                           routing an output through it "was asking C# a
//                           question C# had answered".
//   StandaloneNameEquals    last caller was the native cs_sv_setlocal, now
//                           SourceSharp.Server/HierarchySetLocalCommand, which
//                           asks the entity directory by name.
//
// The claims themselves are not lost so much as no longer claims about this
// build: the case-insensitive prefix rule the seven pinned is still pinned
// here, by MatchesAnExactNameRegardlessOfCase and SupportsATrailingStarAsAPrefix,
// over the one function that still has a caller.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/standalone_names.h"

namespace
{

//-----------------------------------------------------------------------------
// The classname matcher has to agree with the SDK's, because one iteration
// walks both hierarchies and applies this to one half and NamesMatch to the
// other. A rule that differed would under-report by exactly the classes that
// have moved, which reads as those entities having disappeared.
//-----------------------------------------------------------------------------
TEST(StandaloneNameMatches, MatchesAnExactNameRegardlessOfCase)
{
	EXPECT_TRUE(StandaloneNameMatches("logic_relay", "logic_relay"));
	EXPECT_TRUE(StandaloneNameMatches("LOGIC_RELAY", "logic_relay"));
	EXPECT_FALSE(StandaloneNameMatches("logic_timer", "logic_relay"));
}

TEST(StandaloneNameMatches, TreatsABareStarAsEverything)
{
	EXPECT_TRUE(StandaloneNameMatches("*", "logic_relay"));
	EXPECT_TRUE(StandaloneNameMatches("*", "info_target"));
	EXPECT_TRUE(StandaloneNameMatches("*", ""));
}

TEST(StandaloneNameMatches, SupportsATrailingStarAsAPrefix)
{
	EXPECT_TRUE(StandaloneNameMatches("logic_*", "logic_relay"));
	EXPECT_TRUE(StandaloneNameMatches("filter_*", "filter_activator_name"));
	EXPECT_FALSE(StandaloneNameMatches("logic_*", "info_target"));
}

//-----------------------------------------------------------------------------
// A query longer than the name is not a match. "logic_relay_two" against
// "logic_relay" runs the name out first, and answering true there would make
// every prefix query match its own shorter classnames.
//-----------------------------------------------------------------------------
TEST(StandaloneNameMatches, DoesNotMatchAQueryLongerThanTheName)
{
	EXPECT_FALSE(StandaloneNameMatches("logic_relay_two", "logic_relay"));
	EXPECT_FALSE(StandaloneNameMatches("logic_relay", "logic_"));
}

TEST(StandaloneNameMatches, TreatsAnUnnamedThingTheWayTheSdkDoes)
{
	EXPECT_TRUE(StandaloneNameMatches("", ""));
	EXPECT_FALSE(StandaloneNameMatches("logic_relay", ""));
	EXPECT_FALSE(StandaloneNameMatches(NULL, "logic_relay"));
}

} // namespace
