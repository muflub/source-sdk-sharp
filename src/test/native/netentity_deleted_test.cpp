//========= SourceSharp — the netentity shell is gone, and stays gone ========//
//
// shim_underive_verdict_test.cpp recorded three shim TUs that still derived
// from CBaseEntity. Its third verdict — shell_netentity — was a "when", not a
// "whether": step 6 makes CBaseEntity an alias for IGameEntity, and a class
// deriving from the alias is ABSTRACT. Route (c), deleting the shell, was the
// only route that removed more C++ than it added. It is taken, so the verdict's
// assertions were deleted as they themselves instructed, and this replaced
// them: A DELETION NEEDS A GUARD AT LEAST AS MUCH AS A REFUSAL DOES. Three
// things could undo it and none would fail to compile:
//
//   1. THE CLASS COMES BACK. Abstract under the armed alias, and a class that
//      silently becomes abstract emits no diagnostic of its own — the error
//      lands wherever a member is asked for. That is the c_rope.h shape.
//
//   2. THE RETIRED ABI VALUE IS REUSED. SS_SHELL_NETENTITY = 0 is reserved, not
//      removed, because NetShellKind SENDS THESE NUMBERS.
//
//   3. THE FOUR DEAD ABI SLOTS ARE WIRED UP AGAIN, or reaped without pricing
//      the renumber of the thirty live slots after the first of them.
//
// THE STRIPPER IS LOAD-BEARING: the prose above spells the very strings the
// assertions look for, and an unstripped scan would read the explanation as the
// fact. ANCHORS: every file is identified before anything is asserted absent
// about it, so a renamed file is a red test and not a vacuous pass.
//
//=============================================================================//

#include <string>
#include <vector>
#include <utility>
#include <fstream>
#include <dirent.h>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

bool Exists( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;

	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + strRelative ).c_str(), &st ) == 0;
}

struct File_t { const char *pszRel; const char *pszAnchor; };

} // namespace

//=============================================================================//
// 1. THE SHELL IS GONE, and nothing names it.
//
// Both halves are asserted, and neither alone is a check. A missing FILE with a
// live reference somewhere is a build that does not link; a present file that
// nothing references is a class quietly waiting to be constructed again.
//=============================================================================//
TEST( NetEntityDeleted, NeitherHalfOfTheShellExists )
{
	// The directory is real, so "the file is not there" cannot be "the tree is
	// not there".
	ASSERT_TRUE( Exists( "sourcesharp/game/shared" ) )
		<< "the shared shim directory is missing; this test would pass vacuously.";
	ASSERT_TRUE( Exists( "sourcesharp/game/shared/generic_payload.cpp" ) )
		<< "a file that IS expected in that directory is missing too — the path "
		   "this test looks under has moved, and its absence checks mean nothing.";

	EXPECT_FALSE( Exists( "sourcesharp/game/shared/shell_netentity.h" ) )
		<< "shell_netentity.h is back. It declares CSourceSharpNetEntity, which "
		   "derives from CBaseEntity — an alias for IGameEntity once step 6 is "
		   "armed, which makes the class ABSTRACT and unconstructable. A class "
		   "that silently becomes abstract emits NO diagnostic of its own; the "
		   "error lands wherever a member is asked for. If the shell is genuinely "
		   "wanted again, route (c) has been reversed and this whole file is "
		   "obsolete — say so and delete it.";

	EXPECT_FALSE( Exists( "sourcesharp/game/shared/shell_netentity.cpp" ) )
		<< "shell_netentity.cpp is back. See the header assertion above.";

	EXPECT_FALSE( Exists( "sourcesharp/test/netentity_table_pairing_test.cpp" ) )
		<< "the netentity pairing test is back. It diffed two hand-copied prop "
		   "lists that no longer exist; if something needs diffing again, the "
		   "shell has returned and this file is obsolete.";
}

//=============================================================================//
// 2. THE RETIRED ABI VALUE IS STILL RESERVED, and still refused at both
//    publish paths.
//
// The values are the ABI: NetShellKind in NetSchema.cs sends these numbers.
// Reusing 0 changes what one side means by a number the other reads.
//=============================================================================//
//=============================================================================//
// 3. THE TIER LIST COLLAPSED TO ONE NAME PAIR, which is the C++ the deletion
//    actually removed from the shared header.
//=============================================================================//
//=============================================================================//
// 4. THE FOUR DEAD SLOTS ARE REAPED, AND STAY REAPED.
//
// This section used to guard the opposite decision: the four callbacks were
// LEFT in the ABI struct because removing them renumbered 141 live slots in a
// struct several lanes were appending to concurrently.
//
// They are gone as of SOURCESHARP_ABI_VERSION 240. What closed it was the live
// half of step 8a's differential, which 8a itself could not take because the
// box had no GPU: row 2 of .claude/entity-port/DEFERRED_LIVE_GATES.md armed
// all four slots on BOTH sides with an [UnmanagedCallersOnly] probe, and
// wrapped the LIVE FramePreThink slot in the same probe as a CONTROL. Two
// passes of tools/live-gate over 20 steps, 0 skipped, exit 0: the CONTROL
// printed in three separate game processes and no probe on any of the four
// ever fired. The control arm is why that is evidence -- without it, a build
// that failed to deploy and a log written elsewhere both look identical to it.
//
// So these two tests now assert the REMOVAL rather than the presence, which is
// what stops a later lane re-adding a slot with no subject.
//=============================================================================//
