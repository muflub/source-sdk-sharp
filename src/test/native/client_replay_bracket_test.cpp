//========= SourceSharp — the client replay bracket's NULL player ============//
//
// plan_entity.md step 6f. The client's prediction is managed: the ring, the
// basis, the compare and the loop are C#, and `CGameMovement::player` is NULL
// for the whole of a client command because there is no client IMovementPlayer
// any more — C_StandalonePlayer was deleted with the native replay it existed
// for.
//
// WHAT THIS PINS, AND WHY IT IS A SOURCE SCAN. The managed loop asks native
// three questions and reports one kind of effect, through the four ABI slots
// step 3e added. Every one of those is serviced by a CGameMovement method, and
// four of those methods dereference `player`. On the SERVER they are all safe.
// On the CLIENT each one is a segfault, and the whole class of bug is invisible
// to every other test in this tree: nothing but a running game reaches those
// methods with a NULL player, and a unit test cannot construct a CGameMovement.
//
// IT IS NOT HYPOTHETICAL. The first live run of step 6f died on it:
//
//     [SourceSharp/Client] C# received its first user command (tick 87)
//     [SourceSharp] ASSERT #1: gamemovement.cpp (4526) : Assertion Failed: player
//     hl2.sh: line 113: 1095759 Segmentation fault (core dumped)
//
// — GetPointContentsCached, reached from the loop's very first CheckWater, on
// the first command the client ever replayed. That is the red path for every
// guard below, and it is a real defect rather than a poke.
//
// A SCAN AND NOT A BEHAVIOUR TEST, deliberately. What can go wrong is a guard
// being DELETED — by a merge, by a tidy-up, by somebody restoring "the
// unconditional version" — and the symptom is a crash on the first user command
// of every session. A test that reads the guard is the only thing that fails
// before the game does.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <sys/stat.h>

#include <fstream>
#include <sstream>
#include <string>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string s = std::string( kCandidates[ i ] ) + "/sourcesharp/host/sourcesharp.h";
		if ( stat( s.c_str(), &st ) == 0 )
			return kCandidates[ i ];
	}
	return NULL;
}

// COMMENTS STRIPPED. Every guard below is discussed at length in a comment
// beside it, and a scan that matched comment text would pass on a file whose
// code had lost the guard entirely — which is the one thing this test is for.
std::string CodeOf( const char *pRelative )
{
	const char *pRoot = SrcRoot();
	if ( !pRoot )
		return std::string();

	std::ifstream in( ( std::string( pRoot ) + "/" + pRelative ).c_str() );
	if ( !in )
		return std::string();

	std::stringstream ss;
	ss << in.rdbuf();
	const std::string raw = ss.str();

	std::string out;
	out.reserve( raw.size() );

	bool bLine = false, bBlock = false;

	for ( size_t i = 0; i < raw.size(); i++ )
	{
		if ( bLine )
		{
			if ( raw[ i ] == '\n' ) { bLine = false; out.push_back( '\n' ); }
			continue;
		}
		if ( bBlock )
		{
			if ( raw[ i ] == '*' && i + 1 < raw.size() && raw[ i + 1 ] == '/' ) { bBlock = false; i++; }
			continue;
		}
		if ( raw[ i ] == '/' && i + 1 < raw.size() && raw[ i + 1 ] == '/' ) { bLine = true; continue; }
		if ( raw[ i ] == '/' && i + 1 < raw.size() && raw[ i + 1 ] == '*' ) { bBlock = true; i++; continue; }
		out.push_back( raw[ i ] );
	}

	// Whitespace squashed so a reformat does not move a needle.
	std::string squashed;
	squashed.reserve( out.size() );
	for ( size_t i = 0; i < out.size(); i++ )
	{
		if ( !isspace( (unsigned char)out[ i ] ) )
			squashed.push_back( out[ i ] );
	}
	return squashed;
}

// REPOINTED AT STEP 8f-igamemovement, NOT RELAXED.
//
// The four guarded sites were in game/shared/gamemovement.cpp until that step
// moved the whole servicing half -- the CMoveData copy, the four slots and this
// bracket -- onto a standalone CShellGameMovement in the shim, so the file could
// leave both vpcs. The guards came with the code and every needle below is the
// same needle; what changed is which file it is read out of.
//
// AND THE OLD ADDRESS IS ASSERTED EMPTY, which the repoint would otherwise have
// lost. A copy left behind in gamemovement.cpp is not a compile error and not a
// link error while that file is still compiled -- it is a second, unreachable
// implementation, and the next reader would have two answers to choose between.
const std::string &Movement()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_movement.cpp" );
	return s;
}

bool Has( const std::string &haystack, const char *needle )
{
	std::string n;
	for ( const char *p = needle; *p; ++p )
	{
		if ( !isspace( (unsigned char)*p ) )
			n.push_back( *p );
	}
	return haystack.find( n ) != std::string::npos;
}

} // namespace

//-----------------------------------------------------------------------------
// The sites, one fact each. One fact per site rather than one fact listing
// four needles, so a failure names the method that will crash rather than "one
// of these four".
//-----------------------------------------------------------------------------

TEST( ClientReplayBracket, ThePointContentsCacheIsNoLongerNative )
{
	ASSERT_FALSE( Movement().empty() ) << "shell_movement.cpp did not open";
	ASSERT_TRUE( Has( Movement(), "classCShellGameMovement" ) ) << "the movement shell was read";

	// The cache was indexed by the player, and the client's replay has no
	// player object: the first live run of 6f died on that index. Since
	// 2026-10-19 the cache is managed (MovePointContentsCache) and the world
	// asks IEngineTrace itself, so there is no native index left to crash on.
	EXPECT_FALSE( Has( Movement(), "GetPointContentsCached(" ) )
		<< "a native point-contents cache is back; it must index the player by "
		   "mv->m_nPlayerHandle, never player->MvEntIndex(), or the client's "
		   "replay segfaults on its first command.";
}

TEST( ClientReplayBracket, TheStepSoundEffectsAreGuarded )
{
	ASSERT_FALSE( Movement().empty() );

	// SOURCESHARP_MOVE_EFFECT_PLAY_STEP_SOUND and _UPDATE_STEP_SOUND, reached
	// from the managed loop through the MoveEffect slot on every footstep. Both
	// were EMPTY BODIES on the client shell they used to reach, so the guard
	// costs nothing and its absence costs the process.
	EXPECT_TRUE( Has( Movement(),
		"if(player)player->MvPlayStepSound(" ) )
		<< "the PLAY_STEP_SOUND arm dereferences player unguarded again.";

	EXPECT_TRUE( Has( Movement(),
		"if(player)player->MvUpdateStepSound(" ) )
		<< "the UPDATE_STEP_SOUND arm dereferences player unguarded again.";
}

TEST( ClientReplayBracket, TheDuckLatchResetIsGuarded )
{
	ASSERT_FALSE( Movement().empty() );

	// SOURCESHARP_MOVE_EFFECT_RESET_DUCK_LATCHED, reached on every duck. The
	// shell this used to reach answered MvInFirstPersonView() true
	// unconditionally, so the reset never ran on this side anyway.
	EXPECT_TRUE( Has( Movement(),
		"if(player&&!player->MvInFirstPersonView())" ) )
		<< "ResetDuckLatched dereferences player unguarded again. It is client-only "
		   "code and the client has no player object.";
}

TEST( ClientReplayBracket, TheBracketIsClientOnlyAndBorrowsTheMoverRatherThanBuildingOne )
{
	ASSERT_FALSE( Movement().empty() );

	// The bracket itself. It must stay inside CLIENT_DLL -- the server has its
	// own driver and a second one would be two things setting *ActiveMover().
	EXPECT_TRUE( Has( Movement(),
		"int32_tSourceSharp_MoveBeginClientCommand(uint32_thPlayer,SourceSharpMoveState*pOut)" ) )
		<< "the client replay bracket is gone or renamed; the four trace slots have "
		   "nothing to point at and every trace refuses.";

	EXPECT_TRUE( Has( Movement(), "voidSourceSharp_MoveEndClientCommand(int32_tbRan,int32_t*pOutGroundIndex)" ) )
		<< "the bracket's other half is gone. Outside a bracket the trace slots "
		   "REFUSE, so a bracket that is never closed is one that is never reopened.";

	// And the whole of it is client-only.
	EXPECT_TRUE( Has( Movement(), "#ifdefCLIENT_DLL" ) );
}

TEST( ClientReplayBracket, TheConstantsAreFilledInExactlyOnePlace )
{
	ASSERT_FALSE( Movement().empty() );

	// Two callers need exactly the constant words -- the server's own crossing
	// and the client's bracket -- and a second copy is a drift class: the two
	// sides would run the same arithmetic under different convars and the
	// misprediction would name a field neither side is wrong about.
	EXPECT_TRUE( Has( Movement(), "voidCShellGameMovement::FillConstants(SourceSharpMoveState*pState)" ) )
		<< "the shared constants fill is gone.";

	EXPECT_TRUE( Has( Movement(), "FillConstants(&state);" ) )
		<< "the server's crossing no longer calls the shared fill, so it has a "
		   "second copy of the constants.";

	EXPECT_TRUE( Has( Movement(), "pMover->FillConstants(pOut);" ) )
		<< "the client's bracket no longer calls the shared fill.";
}

//-----------------------------------------------------------------------------
// THE OTHER HALF OF THE REPOINT, and it is the half a repoint usually loses.
//
// Each of the five facts above moved from gamemovement.cpp to
// shell_movement.cpp. If the move had been a COPY, every one of them would still
// be green and the tree would hold two implementations of the servicing half --
// with gamemovement.cpp's the one that answers, because it is still compiled and
// its definitions would win the link for anything that named them. 7f's
// BloodEffectPortabilityTests repoint made exactly this assertion for exactly
// this reason.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// AND THE EDGE THAT MADE THE FILE REMOVABLE, NOW THAT IT HAS BEEN REMOVED.
//
// UNTIL STEP 8f-remove this asserted a LOAN: gamemovement.cpp called INTO the
// shim to lend it a stock CGameMovement, and the shim named nothing
// gamemovement.cpp defined. Reversing that -- an `extern CGameMovement
// g_GameMovement;` in the shim, which reads identically -- would have pinned the
// file in the build forever.
//
// gamemovement.cpp is in neither vpc now, so the loan and its registrar were
// deleted in the same commit. What survives here is the half that still bites:
// the shim must go on naming nothing that file defines. Nothing STOPS it -- the
// header is still on disk and still parses -- and a shim that starts naming
// CGameMovement again does not fail to compile. It fails to LINK, in whichever
// worktree next runs a full build, with an error naming a class the reader will
// find in a file they can see and will assume is compiled.
//-----------------------------------------------------------------------------

