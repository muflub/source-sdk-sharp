//========= SourceSharp — gamemovement.cpp leaves the build ==================//
//
// plan_entity.md step 8f-remove, the last of the OWNER RULING's three parts.
// 8f-movetypes gave the three declared refusals managed bodies; 8f-igamemovement
// moved the IGameMovement publication onto a standalone CShellGameMovement and
// deleted the "managed has not started" fallback; this step takes the file out
// of the build.
//
// IT IS TWO `$File` ROWS AND THAT IS THE POINT. The two lanes before it were
// arranged so that the removal needs no other edit: nothing in the shim names a
// symbol gamemovement.cpp defines, so the file's going leaves nothing undefined.
// The rows were in the SERVER vpc and in the CLIENT vpc (now server_sourcesharp
// and client_sourcesharp, src/{server,client}/native/CMakeLists.txt), and a ONE-SIDED removal
// is silently wrong — the side that kept the row still links a complete
// CGameMovement, sourcesharp_movement_managed still selects it there, and the
// two sides then run different movement code under prediction. That is a
// divergence the game does not announce.
//
// WHY THESE ARE SOURCE SCANS. Everything this step can get wrong is invisible to
// a game that plays correctly:
//
//   * a one-sided removal        — both sides start, both move, they disagree
//   * a `$File` row that no longer resolves — the build breaks LATER, in a
//                                  worktree that is not the one the edit was
//                                  made in
//   * the loan machinery left behind — `if ( g_pStockMover )` reads as a live
//                                  fallback to the next maintainer, and it can
//                                  only ever be NULL
//   * a retired gate arm left armed — a check that fails forever, or one
//                                  everybody learns to ignore
//   * a probe pointed at an uncompiled file — the only live evidence for the
//                                  three move-type arms silently stops existing
//
// The last one is not hypothetical. tools/8f-probe-movetypes was ALREADY dead at
// the entity-port tip this step branched from: 8f-igamemovement moved its three
// anchors into the shim, and the probe went on naming gamemovement.cpp. It
// asserted "anchor not unique (0)" and applied nothing, and nothing in the tree
// knew. TheProbePatchesAFileThatIsStillCompiled and
// TheProbeAnchorsResolveInTheFileItNames are that failure, written down.
//
//=============================================================================//
#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <sys/stat.h>

#include <algorithm>
#include <cctype>
#include <fstream>
#include <sstream>
#include <set>
#include <string>
#include <vector>

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

// The SRC root, which is what a vpc script's $SRCDIR expands to. tools/ is a
// SIBLING of it, so ReadTool below reaches through "../tools" rather than
// carrying a second root of its own that could go stale independently.
std::string Under( const std::string &rel )
{
	const char *pRoot = SrcRoot();
	return pRoot ? std::string( pRoot ) + "/" + rel : rel;
}

bool Exists( const std::string &path )
{
	struct stat st;
	return stat( path.c_str(), &st ) == 0 && S_ISREG( st.st_mode );
}

std::string Read( const std::string &rel )
{
	std::ifstream in( Under( rel ).c_str() );
	if ( !in )
		return std::string();

	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

// A SHELL SCRIPT WITH ITS FULL-LINE COMMENTS DROPPED.
//
// The tools in this tree argue for themselves at length, so a rule about what a
// script DOES cannot be checked against its raw text: this file's own
// ArmFDoesNotAskKillZeroWhetherTheGameDied failed on first run because
// tools/movemath-gate's comment EXPLAINING why `kill -0 "$GAMEPID"` was removed
// contains the string `kill -0 "$GAMEPID"`. That is the "needle in a comment"
// this tree has caught four times, arriving from the other direction: an
// EXPECT_EQ( 0 ) that the argument for the change defeats.
//
// Only whole-line comments are dropped. A trailing `#` cannot be found by
// scanning, because `#` inside a quoted string is data, and a stripper that
// guessed wrong would silently change what every needle below is asked about.
// Every rule here is written against a line of code, so whole-line is enough.
std::string ShellCode( const std::string &raw )
{
	std::string out;
	out.reserve( raw.size() );

	size_t at = 0;
	while ( at <= raw.size() )
	{
		size_t eol = raw.find( '\n', at );
		if ( eol == std::string::npos )
			eol = raw.size();

		size_t first = at;
		while ( first < eol && isspace( (unsigned char)raw[ first ] ) )
			first++;

		if ( first >= eol || raw[ first ] != '#' )
			out.append( raw, at, eol - at );
		out.push_back( '\n' );

		if ( eol == raw.size() )
			break;
		at = eol + 1;
	}
	return out;
}

// tools/ is a sibling of src/, not a child of it.
std::string ReadTool( const char *pName )
{
	std::ifstream in( Under( std::string( "../tools/" ) + pName ).c_str() );
	if ( !in )
		return std::string();

	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

// COMMENTS STRIPPED, whitespace squashed. Every rule below is argued at length
// in a comment beside the code it is about, so a raw scan would be satisfied by
// the ARGUMENT for the code rather than by the code — the "needle in a comment"
// this tree has now caught four separate times (6d's hitbox rule, 7b's
// C_BaseTempEntity:: count, 8c's vpc `$File` rows, and this step's own
// g_pStockMover, which survives in shell_movement.cpp only as the paragraph
// explaining why it was deleted).
std::string CodeOf( const char *pRelative )
{
	const std::string raw = Read( pRelative );

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

	std::string squashed;
	squashed.reserve( out.size() );
	for ( size_t i = 0; i < out.size(); i++ )
	{
		if ( !isspace( (unsigned char)out[ i ] ) )
			squashed.push_back( out[ i ] );
	}
	return squashed;
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

int CountRaw( const std::string &haystack, const std::string &needle )
{
	if ( needle.empty() )
		return 0;

	int n = 0;
	for ( size_t pos = haystack.find( needle ); pos != std::string::npos;
	      pos = haystack.find( needle, pos + needle.size() ) )
	{
		n++;
	}
	return n;
}

//-----------------------------------------------------------------------------
// EVERY TRANSLATION UNIT ONE SIDE COMPILES, farm-relative, read through
// cmake_graph.h (compile_commands.json cross-checked against the target's
// CMakeLists source list; it throws on a missing, empty or stale input).
// Until Phase 6b this walked the `$File` rows of server_csharp.vpc and
// client_csharp.vpc.
//-----------------------------------------------------------------------------
std::vector<std::string> CompiledRows( const char *pszTarget )
{
	const std::set<std::string> s = cmake_graph::CompiledSources( pszTarget );
	return std::vector<std::string>( s.begin(), s.end() );
}

// The proof-of-read for each side: the shim this whole step moved the movement
// publication into, compiled on both sides. A reader that saw nothing cannot
// then answer "gamemovement.cpp is not compiled" for free.
const char *const kShimProofOfRead = "sourcesharp/game/shared/shell_movement.cpp";

bool NamesBasename( const std::vector<std::string> &rows, const std::string &base )
{
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		const size_t slash = rows[ i ].rfind( '/' );
		if ( rows[ i ].substr( slash == std::string::npos ? 0 : slash + 1 ) == base )
			return true;
	}
	return false;
}

const std::string &Shim()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_movement.cpp" );
	return s;
}

const std::string &ShimHeader()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_gamemovement.h" );
	return s;
}

//-----------------------------------------------------------------------------
// THE SCAN REALLY SCANNED. Under VPC this was a second, independent reading of
// each script (a loose `$File` line count) that the quote-walking scan had to
// meet. RETIRED IN 6b: cmake_graph::CompiledSources already reads each list
// twice (compile_commands.json against the CMakeLists source list) and throws
// when they disagree or either is empty. The proof-of-read stays, per side.
//-----------------------------------------------------------------------------
void AssertRowsAreAFileList( const char *pszTarget, const std::vector<std::string> &rows )
{
	ASSERT_TRUE( std::find( rows.begin(), rows.end(), std::string( kShimProofOfRead ) ) != rows.end() )
		<< pszTarget << " does not compile " << kShimProofOfRead
		<< ", so what was read is not this side's source list.";
}

} // namespace

//=============================================================================
// PART 1 — the two rows.
//=============================================================================

TEST( GameMovementEvicted, TheFileIsInNeitherBuild )
{
	const std::vector<std::string> server = CompiledRows( cmake_graph::kServer );
	const std::vector<std::string> client = CompiledRows( cmake_graph::kClient );

	// A GUARD BEFORE THE CLAIM. A scan that resolved nothing at all would report
	// "in neither build" about a file that is in both, and that exact shape -- a
	// perfectly clean, perfectly meaningless zero -- is why 8f-movetypes' own
	// scan carries one.
	ASSERT_NO_FATAL_FAILURE( AssertRowsAreAFileList( cmake_graph::kServer, server ) );
	ASSERT_NO_FATAL_FAILURE( AssertRowsAreAFileList( cmake_graph::kClient, client ) );

	// EACH SIDE NAMED SEPARATELY, because the dangerous failure is asymmetric:
	// a build with the row on ONE side links a second complete CGameMovement
	// there and nowhere else, so the two sides run different movement code and
	// the only symptom is prediction disagreeing. Matched by BASENAME, as the
	// vpc scan matched `\gamemovement.cpp`, so a copy moved elsewhere counts.
	EXPECT_FALSE( NamesBasename( server, "gamemovement.cpp" ) )
		<< "gamemovement.cpp is still compiled by server_sourcesharp "
		   "(src/server/native/CMakeLists.txt). If the client no longer has it, "
		   "the two sides now have different movement implementations available "
		   "to sourcesharp_movement_managed.";
	EXPECT_FALSE( NamesBasename( client, "gamemovement.cpp" ) )
		<< "gamemovement.cpp is still compiled by client_sourcesharp "
		   "(src/client/native/CMakeLists.txt). Same asymmetry, other way round.";
}

TEST( GameMovementEvicted, EveryCompiledSourceInBothBuildsResolvesOnDisk )
{
	// THE CHECK THAT HAS CAUGHT A WOULD-BE BUILD BREAK THREE TIMES IN THIS
	// PROJECT. Removing a row cannot break this one; MOVING a file, renaming
	// one, or landing a row for a file a peer lane has not merged yet all do.
	// The failure without it is a build that breaks in somebody else's worktree.
	//
	// The walk-read-the-whole-list relation this used to carry (resolved paths
	// >= loose `$File` lines) is RETIRED IN 6b: cmake_graph::CompiledSources
	// cross-checks its two readings itself and throws on a short or empty one.
	const char *kTargets[] = { cmake_graph::kServer, cmake_graph::kClient };

	for ( size_t v = 0; v < 2; v++ )
	{
		const std::vector<std::string> rows = CompiledRows( kTargets[ v ] );
		ASSERT_NO_FATAL_FAILURE( AssertRowsAreAFileList( kTargets[ v ], rows ) );

		for ( size_t i = 0; i < rows.size(); i++ )
		{
			EXPECT_TRUE( Exists( Under( rows[ i ] ) ) )
				<< kTargets[ v ] << " compiles '" << rows[ i ] << "', which is not on "
				<< "disk at " << Under( rows[ i ] );
		}
	}
}

//=============================================================================
// PART 2 — the loan, and its going.
//=============================================================================

TEST( GameMovementEvicted, TheShimNamesNothingTheEvictedFileDefines )
{
	ASSERT_FALSE( Shim().empty() ) << "shell_movement.cpp did not open";

	// This is the property that made the removal two rows, and it has to hold
	// AFTER the removal too — a shim that starts naming CGameMovement again does
	// not fail to build, because the header is still there. It fails to LINK, in
	// whichever worktree next runs a full build.
	EXPECT_FALSE( Has( Shim(), "externCGameMovement" ) )
		<< "the shim reaches into gamemovement.cpp for a class that is no longer "
		   "compiled. Nothing defines it now.";

	EXPECT_FALSE( Has( Shim(), "#include\"gamemovement.h\"" ) )
		<< "the shim includes gamemovement.h. It does not need the class, and "
		   "including it invites the next reader to name it.";

	EXPECT_FALSE( Has( Shim(), "g_pStockMover" ) )
		<< "the shim still carries the lent stock mover. Nothing registers one now "
		   "— gamemovement.cpp was the only caller and it is not compiled — so the "
		   "pointer can only ever be NULL, and every `if ( g_pStockMover )` beside "
		   "it reads to the next maintainer as a live fallback. That is the shape "
		   "of all eight silent losses this port has found.";

	EXPECT_FALSE( Has( Shim(), "SourceSharp_RegisterStockGameMovement" ) )
		<< "the shim still defines the registration hook. Its only caller left the "
		   "build with the file.";
}

TEST( GameMovementEvicted, TheHeaderNoLongerDeclaresTheLoanHook )
{
	ASSERT_FALSE( ShimHeader().empty() ) << "shell_gamemovement.h did not open";

	EXPECT_FALSE( Has( ShimHeader(), "voidSourceSharp_RegisterStockGameMovement(" ) )
		<< "shell_gamemovement.h still declares the loan hook. A declaration with "
		   "no definition and no caller is an invitation to write one, and the one "
		   "somebody writes will be `extern CGameMovement`.";

	// The reasons stay. kMovementFatal_NoStock is what the convar's 0 arm
	// ANSWERS now, so deleting it with the loan would delete the replacement.
	EXPECT_TRUE( Has( ShimHeader(), "kMovementFatal_NoStock=1," ) )
		<< "the reason the 0 arm gives was deleted with the machinery it replaced.";
	EXPECT_TRUE( Has( ShimHeader(), "kMovementFatal_Declined=2," ) )
		<< "the declined reason was deleted. It is unreachable as of 8f-movetypes, "
		   "and it is COUNTED — the counter is the only thing that can report the "
		   "day 'the loop declines nothing' stops being true.";
}

TEST( GameMovementEvicted, TheZeroArmIsFatalWithNoConditionLeftOnIt )
{
	ASSERT_FALSE( Shim().empty() );

	EXPECT_TRUE( Has( Shim(),
		"if(!MovementManaged()){SourceSharp_MovementFatal("
		"kMovementFatal_NoStock,0);return;}" ) )
		<< "the convar's 0 arm is no longer an unconditional fatal. Either it "
		   "selects something (there is nothing left to select) or it falls "
		   "through into the managed preamble with no body behind it, which is a "
		   "player who stops moving and a log that says nothing.";
}

TEST( GameMovementEvicted, TheDeclineIsRefusedWithNoConditionLeftOnIt )
{
	ASSERT_FALSE( Shim().empty() );

	EXPECT_TRUE( Has( Shim(),
		"if(!RunManagedCommand()){SourceSharp_MovementRefused(kMovementFatal_Declined,"
		"(int)pPlayer->MvGetMoveType());}" ) )
		<< "a declined command is no longer counted and named. It cannot happen "
		   "today — 8f-movetypes gave the last three move types managed bodies — "
		   "and 'cannot happen' is exactly the claim a counter is for.";
}

TEST( GameMovementEvicted, ThePredictionErrorPairStoppedReadingTheConvar )
{
	ASSERT_FALSE( Shim().empty() );

	// StartTrackPredictionErrors is the only thing that sets `player` before
	// ProcessMovement reads it, so the assignment must survive; the convar test
	// beside it must not, because a test that chooses between one implementation
	// and none is the check that cannot fail.
	EXPECT_TRUE( Has( Shim(),
		"voidCShellGameMovement::StartTrackPredictionErrors(IMovementPlayer*pPlayer)"
		"{player=pPlayer;}" ) )
		<< "StartTrackPredictionErrors is not just the assignment any more. It is "
		   "the only thing that sets `player` before ProcessMovement asserts on it, "
		   "and anything else here is either a dead convar read or a forward to a "
		   "mover that no longer exists.";

	EXPECT_TRUE( Has( Shim(),
		"voidCShellGameMovement::FinishTrackPredictionErrors(IMovementPlayer*pPlayer)"
		"{}" ) )
		<< "FinishTrackPredictionErrors is not empty. Stock's is, at "
		   "PREDICTION_ERROR_CHECK_LEVEL 0 (shareddefs.h:914), which is this build.";
}

//=============================================================================
// PART 3 — the two tools that read the file, and the gate arm that read the
// convar. All three die or move with it, and a tool nobody notices dying is
// how this step found its own worst bug.
//=============================================================================

TEST( GameMovementEvicted, MovemathGateHasNoArmThatCannotBeSatisfied )
{
	const std::string gate = ReadTool( "movemath-gate" );
	ASSERT_FALSE( gate.empty() ) << "tools/movemath-gate did not open";

	// Arm 1 SET the convar to 0 and then went on measuring. On this build that
	// command ends the process, so an arm that issues it and then reads a
	// counter is a gate that can only fail. There is exactly one place the
	// command may still appear — arm F, which issues it LAST and on purpose.
	// COUNTED OVER THE WHOLE FILE rather than matched against arm 1's exact
	// line. A needle shaped like the deleted line would go green the moment
	// somebody re-added the arm with a redirection on it, and "the arm is back
	// in a slightly different spelling" is the failure, not "this byte sequence
	// returned".
	EXPECT_EQ( 1, CountRaw( gate, "\"sourcesharp_movement_managed 0\"" ) )
		<< "tools/movemath-gate issues `sourcesharp_movement_managed 0` "
		<< CountRaw( gate, "\"sourcesharp_movement_managed 0\"" )
		<< " time(s). Exactly ONE is right, and it is arm F's, which is LAST and "
		   "expects the process to die. Any other is arm 1 back from the dead: that "
		   "command is an Error() that does not return, so every reading taken "
		   "after it comes from a game that is not running.";

	EXPECT_EQ( 0, CountRaw( gate, "== arm 1:" ) )
		<< "arm 1's banner is back. Whatever it now runs, the name is retired.";

	EXPECT_EQ( 1, CountRaw( gate, "== arm F: sourcesharp_movement_managed 0" ) )
		<< "arm F is gone. Retiring arm 1 leaves the claim that the 0 arm is a LOUD "
		   "refusal, and this project has eight silent losses that were each "
		   "believed to be loud. Arm F is the only thing that fires it.";

	// The structural half of what arm 1 used to prove.
	EXPECT_EQ( 1, CountRaw( gate, "targets_compiling_gamemovement()" ) )
		<< "the gate no longer checks that gamemovement.cpp is out of both game "
		   "targets (server_sourcesharp, client_sourcesharp). "
		   "That fact is WHY arm 1 was retired; a gate that drops both the arm and "
		   "the reason has quietly stopped covering a one-sided removal.";
}

TEST( GameMovementEvicted, ArmFDoesNotAskKillZeroWhetherTheGameDied )
{
	// THE CODE, WITHOUT THE ARGUMENT FOR IT -- see ShellCode above, and note
	// that the first version of this fact did not do this and failed on the
	// comment that explains the fix.
	const std::string gate = ShellCode( ReadTool( "movemath-gate" ) );
	ASSERT_FALSE( gate.empty() );

	// ARM F SHIPPED WITH THIS BUG AND ONE LIVE RUN FOUND IT. It asked
	// `kill -0 "$GAMEPID"` and reported the game ALIVE six seconds after a fatal
	// that had killed it. The game is the gate's own background child and
	// nothing ever `wait`s for it, so a dead one is a ZOMBIE -- and a zombie
	// answers kill -0 exactly like a live process. The signal for "dead" and the
	// signal for "alive" were the same signal.
	//
	// Measured, same fatal, game launched under setsid so init reaps it:
	//     t=1s kill-0=yes /proc-state=R      t=2s kill-0=no /proc-state=gone
	// so Error() does stop the process and the old check was reading its own
	// process table.
	EXPECT_EQ( 0, CountRaw( gate, "kill -0 \"$GAMEPID\"" ) )
		<< "arm F is back to asking kill -0 whether the game died. It cannot "
		   "answer: an unreaped child answers yes forever, and that is the state "
		   "the game is in when the gate kills it.";

	EXPECT_EQ( 1, CountRaw( gate, "game_is_running \"$GAMEPID\" || break" ) )
		<< "arm F no longer WAITS for the process to go. Error() writes a crash "
		   "dump on its way out, so the gap between the message landing and the "
		   "process leaving is real work -- a single sample after a fixed sleep "
		   "reports a healthy build as a failure on a loaded box.";

	EXPECT_EQ( 1, CountRaw( gate, "classify_state()" ) )
		<< "the state judgement is no longer separable from the /proc read, so "
		   "--self-test cannot drive it. The first attempt at that fixture made a "
		   "real zombie with `( exit 0 ) &`, which bash reaps before it can be "
		   "read -- the fixture tested job control, not the predicate.";
}

TEST( GameMovementEvicted, TheMoveTypeGatesControlArmWasRetiredToo )
{
	const std::string gate = ShellCode( ReadTool( "8f-movetype-gate" ) );
	ASSERT_FALSE( gate.empty() ) << "tools/8f-movetype-gate did not open";

	// THE SAME RETIREMENT AS movemath-gate's ARM 1, AND THIS ONE WAS WORSE.
	// Arm 7 set sourcesharp_movement_managed 0 and required the counter to go
	// FLAT, which is how it said the growth in arms 2-6 was the switch rather
	// than a free-running counter. With gamemovement.cpp out of both vpcs that
	// command is an Error() that does not return -- so the arm PASSED, on a dead
	// process, because the counter is read out of a log and the last logged
	// value answered. A check that fails forever gets noticed. One that passes
	// for the wrong reason does not.
	EXPECT_EQ( 0, CountRaw( gate, "the counter must go FLAT" ) )
		<< "tools/8f-movetype-gate still has arm 7. It cannot be satisfied and it "
		   "does not fail either: it reads a counter out of a log after killing "
		   "the process that writes it, and a dead writer's last value is FLAT.";

	EXPECT_EQ( 1, CountRaw( gate, "== F. sourcesharp_movement_managed 0" ) )
		<< "arm F is gone from the move-type gate. Retiring arm 7 without it "
		   "leaves the claim that the 0 arm is loud with nothing firing it.";

	EXPECT_EQ( 0, CountRaw( gate, "os.kill(" ) )
		<< "the move-type gate asks os.kill(pid, 0) whether the process died. It "
		   "cannot answer -- an unreaped child answers yes forever. Read /proc.";

	EXPECT_EQ( 1, CountRaw( gate, "def process_state( pid )" ) + CountRaw( gate, "def process_state(pid)" ) )
		<< "the /proc reader is gone, so arm F has nothing to judge the process "
		   "with.";
}

TEST( GameMovementEvicted, TheProbePatchesAFileThatIsStillCompiled )
{
	const std::string probe = ReadTool( "8f-probe-movetypes" );
	ASSERT_FALSE( probe.empty() ) << "tools/8f-probe-movetypes did not open";

	// THE BUG THIS FACT IS WRITTEN FROM. At the entity-port tip this step
	// branched from, the probe still named game/shared/gamemovement.cpp while
	// 8f-igamemovement had moved all three of its anchors into the shim. It
	// asserted "anchor not unique (0)" and patched nothing, so
	// tools/8f-movetype-gate — which 8f-movetypes names as the ONLY thing that
	// can re-prove the three move-type arms — could not be run by anybody, and
	// nothing in the tree said so.
	const std::string kTarget = "CPP = \"src/sourcesharp/game/shared/shell_movement.cpp\"";
	EXPECT_EQ( 1, CountRaw( probe, kTarget ) )
		<< "tools/8f-probe-movetypes does not patch shell_movement.cpp. If it names "
		   "an uncompiled file, applying it changes the tree and nothing else, and "
		   "the move-type gate reports on a build that was never probed.";

	EXPECT_EQ( 0, CountRaw( probe, "\"src/game/shared/gamemovement.cpp\"" ) )
		<< "the probe still targets gamemovement.cpp, which is in neither build.";
}

TEST( GameMovementEvicted, TheProbeAnchorsResolveInTheFileItNames )
{
	const std::string probe = ReadTool( "8f-probe-movetypes" );
	ASSERT_FALSE( probe.empty() );

	// RAW text, not CodeOf(): the probe matches the file byte for byte, so a
	// comment-stripped, whitespace-squashed copy would answer a different
	// question from the one the probe asks.
	const std::string shim = Read( "sourcesharp/game/shared/shell_movement.cpp" );
	ASSERT_FALSE( shim.empty() ) << "shell_movement.cpp did not open";

	// The three C++ anchors, as substrings both files must contain. Asserting
	// them in the PROBE as well as in the shim is what stops this fact drifting
	// away from the tool: a needle only this test knows about would go on
	// passing after the probe had been repointed somewhere else.
	const char *kAnchors[] = {
		"Run the ten ported CGameMovement methods in C# (1) or in the stock C++ ",
		"mv->m_flMaxSpeed = pPlayer->MvGetPlayerMaxSpeed();",
		"state.nObserverMode         = player->MvGetObserverMode();",
	};

	for ( size_t i = 0; i < sizeof( kAnchors ) / sizeof( kAnchors[ 0 ] ); i++ )
	{
		EXPECT_GE( CountRaw( probe, kAnchors[ i ] ), 1 )
			<< "the probe no longer contains anchor " << i << " ('" << kAnchors[ i ]
			<< "'), so this test is checking a needle nothing uses.";

		EXPECT_EQ( 1, CountRaw( shim, kAnchors[ i ] ) )
			<< "anchor " << i << " ('" << kAnchors[ i ] << "') occurs "
			<< CountRaw( shim, kAnchors[ i ] ) << " time(s) in shell_movement.cpp. "
			<< "The probe asserts each is unique and dies otherwise; zero means it "
			<< "patches nothing, and two means it would patch the wrong one.";
	}
}
