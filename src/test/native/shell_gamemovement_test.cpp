//========= SourceSharp — the shim's IGameMovement, and the deleted fallback ==//
//
// plan_entity.md step 8f-igamemovement, an OWNER RULING with two halves, and
// this file pins both.
//
// PART 1 — THE SHIM PUBLISHES THE INTERFACE. `shell_shared.cpp` used to hold a
// `CGameMovement` singleton and EXPOSE_SINGLE_INTERFACE_GLOBALVAR over it, so
// IGameMovement could not outlive the 6,040-line stock file that defined the
// class. A standalone CShellGameMovement in the shim does it instead.
//
// PART 2 — THE "MANAGED SIDE HAS NOT STARTED" FALLBACK IS DELETED. It is not
// reimplemented and it is not moved: that state is a LOUD FAILURE now.
//
// WHY EVERY FACT BELOW IS A SOURCE SCAN OR A HEADER CALL, and neither a
// behaviour test nor a live one. What can go wrong here is a DELETION -- a
// merge, a tidy-up, or somebody restoring "the version that degraded
// gracefully" -- and the two symptoms are, respectively, a game that runs stock
// movement while reporting nothing wrong, and a link that fails only once
// somebody removes two vpc rows. Neither is visible to a test that plays the
// game correctly. A test that reads the code is the only thing that fails
// before the tree does.
//
// The formatter is called for real rather than scanned, because it is
// engine-free by construction for exactly that purpose (managed_init_failure.h's
// split: the TEXT is testable, the Error() that does not return is not).
//
//=============================================================================//
#include "gtest/gtest.h"

#include <sys/stat.h>

#include <cctype>
#include <cstring>
#include <fstream>
#include <sstream>
#include <string>

#include "shell_gamemovement.h"

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

std::string Read( const char *pRelative )
{
	const char *pRoot = SrcRoot();
	if ( !pRoot )
		return std::string();

	std::ifstream in( ( std::string( pRoot ) + "/" + pRelative ).c_str() );
	if ( !in )
		return std::string();

	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

// COMMENTS STRIPPED. Every rule below is argued at length in a comment beside
// it, so a scan over the raw text would be satisfied by the argument for the
// code rather than by the code — which is the "needle in a comment" this tree
// has now caught three separate times (6d's hitbox rule, 7b's
// C_BaseTempEntity:: count, 8c's vpc `$File` rows).
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

	// Whitespace squashed so a reformat cannot move a needle.
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

int CountOf( const std::string &haystack, const char *needle )
{
	std::string n;
	for ( const char *p = needle; *p; ++p )
	{
		if ( !isspace( (unsigned char)*p ) )
			n.push_back( *p );
	}
	if ( n.empty() )
		return 0;

	int nFound = 0;
	for ( size_t pos = haystack.find( n ); pos != std::string::npos;
	      pos = haystack.find( n, pos + n.size() ) )
	{
		nFound++;
	}
	return nFound;
}

const std::string &Shim()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_movement.cpp" );
	return s;
}

const std::string &Shared()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_shared.cpp" );
	return s;
}

const std::string &Iface()
{
	static const std::string s = CodeOf( "game/shared/igamemovement.h" );
	return s;
}

const std::string &ManagedAreas()
{
	static const std::string s = CodeOf( "sourcesharp/game/shared/shell_managed_areas.inc" );
	return s;
}

} // namespace

//=============================================================================
// PART 1 — the interface, published by the shim.
//=============================================================================

TEST( ShellGameMovement, TheShimPublishesTheInterfaceOverItsOwnClass )
{
	ASSERT_FALSE( Shim().empty() ) << "shell_movement.cpp did not open";

	EXPECT_TRUE( Has( Shim(),
		"EXPOSE_SINGLE_INTERFACE_GLOBALVAR(CShellGameMovement,IGameMovement,"
		"INTERFACENAME_GAMEMOVEMENT,g_ShellGameMovement);" ) )
		<< "the shim no longer exports IGameMovement over its own class. The name "
		   "the macro takes is the class the interface is TIED TO, so exporting it "
		   "over a CGameMovement is what made gamemovement.cpp unremovable.";

	EXPECT_TRUE( Has( Shim(), "IGameMovement*g_pGameMovement=(IGameMovement*)&g_ShellGameMovement;" ) )
		<< "g_pGameMovement no longer names the shim's mover. That global is what "
		   "this build's own callers use -- the export is parity -- so a build "
		   "whose global still named a CGameMovement would run stock movement with "
		   "the shim sitting beside it doing nothing.";
}

TEST( ShellGameMovement, TheOldPublicationLeftShellShared )
{
	ASSERT_FALSE( Shared().empty() ) << "shell_shared.cpp did not open";

	EXPECT_FALSE( Has( Shared(), "staticCGameMovementg_GameMovement;" ) )
		<< "shell_shared.cpp still holds a CGameMovement singleton. That object is "
		   "two of the six symbols `tools/4a-owes gamemovement` used to name, and "
		   "while it exists the file cannot leave either vpc.";

	EXPECT_FALSE( Has( Shared(), "EXPOSE_SINGLE_INTERFACE_GLOBALVAR(CGameMovement," ) )
		<< "shell_shared.cpp still exports IGameMovement over CGameMovement. Two "
		   "exports of one interface name is not a link error -- it is two "
		   "InterfaceRegs, and which one answers is registration order.";

	EXPECT_FALSE( Has( Shared(), "#include\"gamemovement.h\"" ) )
		<< "shell_shared.cpp includes gamemovement.h again; it needs CMoveData, "
		   "which is igamemovement.h's, and naming the stock class invites the "
		   "next reader to use it.";
}

TEST( ShellGameMovement, EveryPureVirtualOfTheInterfaceIsAnswered )
{
	ASSERT_FALSE( Iface().empty() ) << "igamemovement.h did not open";
	ASSERT_FALSE( Shim().empty() );

	// COUNTED FROM THE INTERFACE rather than written down, so an engine header
	// that grows an eighth pure virtual fails here instead of at a vtable slot
	// nobody notices. `= 0;` after a virtual is the whole definition of one.
	// Both spellings: four are `... ) = 0;` and three are `... ) const = 0;`.
	// Counting only the first read 4 and would have called an interface that had
	// LOST three slots correct.
	const int nPure = CountOf( Iface(), ")=0;" ) + CountOf( Iface(), ")const=0;" );
	EXPECT_EQ( 7, nPure )
		<< "IGameMovement's pure-virtual count moved. Every one of them has to be "
		   "answered by CShellGameMovement or the class is abstract and the shim "
		   "does not compile -- but a NEW one silently inheriting a default is the "
		   "case worth failing on.";

	static const char *const kSlots[] =
	{
		"virtualvoidProcessMovement(IMovementPlayer*pPlayer,CMoveData*pMove)OVERRIDE;",
		"virtualvoidStartTrackPredictionErrors(IMovementPlayer*pPlayer)OVERRIDE;",
		"virtualvoidFinishTrackPredictionErrors(IMovementPlayer*pPlayer)OVERRIDE;",
		"virtualvoidDiffPrint(PRINTF_FORMAT_STRINGcharconst*fmt,...)OVERRIDE;",
		"virtualVectorGetPlayerMins(boolducked)constOVERRIDE;",
		"virtualVectorGetPlayerMaxs(boolducked)constOVERRIDE;",
		"virtualVectorGetPlayerViewOffset(boolducked)constOVERRIDE;",
	};

	for ( size_t i = 0; i < sizeof( kSlots ) / sizeof( kSlots[ 0 ] ); i++ )
	{
		EXPECT_TRUE( Has( Shim(), kSlots[ i ] ) )
			<< "CShellGameMovement does not declare " << kSlots[ i ]
			<< " with OVERRIDE. Without the keyword a signature that drifts from "
			   "the interface's compiles as a NEW virtual and the engine's slot "
			   "keeps the abstract one.";
	}
}

TEST( ShellGameMovement, TheHullAccessorsAnswerTheViewVectorsRatherThanAZero )
{
	ASSERT_FALSE( Shim().empty() );

	// Three accessors with no caller in either DLL today. A refusal that
	// answered vec3_origin would be a plausible-looking zero -- the shape this
	// project has three recorded cases of -- and a zero hull is a player who
	// walks through walls rather than one who obviously does not work.
	EXPECT_EQ( 3, CountOf( Shim(), "constRulesViewVectors_t*vv=g_pGameRules->GetViewVectors();returnducked?" ) )
		<< "the three hull accessors no longer answer the unscaled view vectors "
		   "when there is no player. Whatever they answer instead is what an "
		   "engine that DOES call them would get, and a zero is worse than a "
		   "wrong-scaled hull.";
}

//=============================================================================
// PART 2 — the deleted fallback, which is the ruling.
//=============================================================================

TEST( ShellGameMovement, TheManagedSideHasNotStartedArmDoesNotReturn )
{
	ASSERT_FALSE( Shim().empty() );

	// THE EXACT LINE THE STEP DELETED was
	//     if ( !pApi || !pApi->MoveProcessMovement || !pState )
	//         return false;
	// and the caller then ran stock's PlayerMove()/FinishMove().
	// Since plan_client_vtables CE8 the client asks its
	// IManagedPredictionClient; the crossing is the client's alone since
	// plan_server_native_v2 T4g (the server's commands run in managed and
	// the server's 0 arm is SourceSharp_MovementRequireManaged's fatal).
	EXPECT_TRUE( Has( Shim(),
		"IManagedPredictionClient*pCommands=ManagedPredictionClient();"
		"if(!pCommands||!pState)"
		"SourceSharp_MovementFatal(kMovementFatal_NoManagedSide,0);" ) )
		<< "the movement boundary answers something for a managed side that has "
		   "not started. This is the ruling: it must not. A build silently running "
		   "stock movement instead of the managed loop is indistinguishable from a "
		   "working one right up until the two sides disagree under prediction.";

	// And the shape that would put it back, which is not the same string.
	EXPECT_FALSE( Has( Shim(),
		"if(!pApi||!pApi->MoveProcessMovement||!pState)returnfalse;" ) )
		<< "the deleted fallback is back.";
	EXPECT_FALSE( Has( Shim(), "if(!pCommands||!pState)returnfalse;" ) )
		<< "the deleted fallback is back, on the server's arm.";
}

TEST( ShellGameMovement, TheMissingAreaIsAlsoRefusedAWholeBootEarlier )
{
	// Since plan_engineapi_vtables E17 the server's movement is the
	// managed-built IManagedPlayerCommands, and the load refuses without it:
	// a managed side that builds no movement must not boot, load a map, admit
	// a client and die on their first user command.
	ASSERT_FALSE( ManagedAreas().empty() ) << "shell_managed_areas.inc did not open";

	EXPECT_TRUE( Has( ManagedAreas(),
		"pObject=pRoot->Acquire(\"IManagedPlayerCommands\");"
		"areas.m_pPlayerCommands=ManagedAccept<IManagedPlayerCommands>(pObject,kIManagedPlayerCommandsSlotHashes,"
		"kIManagedPlayerCommandsSlotCount,pWhy,nWhy);"
		"if(!areas.m_pPlayerCommands)returnfalse;" ) )
		<< "the server's load no longer refuses without IManagedPlayerCommands";
}

TEST( ShellGameMovement, TheDeclineIsCountedRatherThanFatal )
{
	ASSERT_FALSE( Shim().empty() );

	// NOT fatal, and the asymmetry is deliberate. Entry.cs's thunk answers 0 for
	// a declined move type, a null state AND a caught exception alike, and the
	// ABI cannot separate them, so a fatal here would kill a live server over one
	// exceptional command. Fail() already reports the exception.
	EXPECT_TRUE( Has( Shim(),
		"SourceSharp_MovementRefused(kMovementFatal_Declined,(int)pPlayer->MvGetMoveType());" ) )
		<< "a command the managed loop declined, on a build with no stock mover, "
		   "is neither run nor reported. That is a player who silently stops "
		   "moving, which is the failure shape this whole step exists to remove.";

	EXPECT_TRUE( Has( Shim(), "refused:%-8s" ) )
		<< "cs_{sv,cl}_move_managed_report no longer prints the refusal counts. A "
		   "refusal warned once and then counted is only honest if the count can "
		   "be read.";
}

// REPOINTED AT STEP 8f-remove, and the half a repoint loses is stated first.
//
// This fact used to assert the LOAN: gamemovement.cpp called into the shim to
// lend it a stock CGameMovement, and the shim named nothing gamemovement.cpp
// defined. The direction of that edge was the whole of 8f-remove's cost -- it is
// why the removal was two `$File` rows and not two rows plus a second edit
// somewhere else.
//
// 8f-remove took the file out of both vpcs, so nobody registers, and the loan
// machinery was deleted in the same commit: a pointer that can only ever be NULL
// makes every test beside it read as a live fallback, which is the shape of all
// eight silent losses this port has found.
//
// SO WHAT IS ASSERTED NOW IS THE END STATE THE EDGE EXISTED TO REACH, and the
// stronger form of it: not "the shim does not reach into gamemovement.cpp for
// the mover" but "the shim does not name it AT ALL, and the arm that used to
// select it is a fatal with no condition left on it". The vpc rows themselves,
// and every other consequence of the file's going, are
// gamemovement_evicted_test.cpp's.
TEST( ShellGameMovement, TheLoanWentWithTheFileItCameFrom )
{
	ASSERT_FALSE( Shim().empty() );

	EXPECT_FALSE( Has( Shim(), "g_pStockMover" ) )
		<< "the shim still carries the lent stock mover. gamemovement.cpp was its "
		   "only registrar and it is in neither vpc, so the pointer is NULL for the "
		   "life of the process and the branches on it are dead code that reads "
		   "live.";

	EXPECT_FALSE( Has( Shim(), "SourceSharp_RegisterStockGameMovement" ) )
		<< "the shim still defines the registration hook, whose only caller left "
		   "the build with the file.";

	EXPECT_TRUE( Has( Shim(),
		"if(!MovementManaged()){SourceSharp_MovementFatal("
		"kMovementFatal_NoStock,0);return;}" ) )
		<< "the convar's 0 arm is no longer an unconditional fatal. There is "
		   "nothing left for it to select, so anything other than a fatal here is a "
		   "player who stops moving with nothing in the log -- the ninth silent "
		   "loss.";

	// The tripwire that re-read gamemovement.cpp for its registrar call was
	// culled when that file left the source tree (neither DLL compiles it).
}

//=============================================================================
// The message itself, called rather than scanned.
//=============================================================================

TEST( ShellGameMovementText, EveryReasonNamesItselfAndARemedy )
{
	for ( int r = 0; r < kMovementFatal_Count; r++ )
	{
		char szBuf[1536];
		const int n = SourceSharpMovement_Format( szBuf, sizeof( szBuf ), "server", r, 0 );

		EXPECT_GT( n, 0 ) << "reason " << r << " formatted nothing";
		EXPECT_LT( n, (int)sizeof( szBuf ) ) << "reason " << r << " was truncated";

		EXPECT_STRNE( "unknown", SourceSharpMovement_ReasonText( r ) )
			<< "reason " << r << " has no text. A fatal nobody can read is a crash.";
		EXPECT_STRNE( "none", SourceSharpMovement_Remedy( r ) )
			<< "reason " << r << " has no remedy. The remedy line is the most "
			   "valuable one: two of these are states a fresh worktree reaches by "
			   "accident.";

		EXPECT_NE( std::string( szBuf ).find( SourceSharpMovement_ReasonText( r ) ), std::string::npos );
		EXPECT_NE( std::string( szBuf ).find( SourceSharpMovement_Remedy( r ) ), std::string::npos );
	}
}

TEST( ShellGameMovementText, TheSideIsNamedAndTheMoveTypeOnlyWhereItMeansSomething )
{
	char szServer[1536];
	SourceSharpMovement_Format( szServer, sizeof( szServer ), "server", kMovementFatal_NoManagedSide, 0 );
	EXPECT_NE( std::string( szServer ).find( "the server side was asked to move a player" ), std::string::npos );

	char szClient[1536];
	SourceSharpMovement_Format( szClient, sizeof( szClient ), "client", kMovementFatal_NoManagedSide, 0 );
	EXPECT_NE( std::string( szClient ).find( "the client side was asked to move a player" ), std::string::npos )
		<< "the two sides are not distinguished. They start differently -- the "
		   "server calls ProcessMovement per command, the client's replay is "
		   "managed and calls native -- so which one printed this is the first "
		   "thing a reader needs.";

	// A move type of 0 under the word FATAL on a reason that has nothing to do
	// with move types reads as data and sends the reader after a player who does
	// not exist.
	EXPECT_EQ( std::string( szServer ).find( "movetype" ), std::string::npos );

	char szDeclined[1536];
	SourceSharpMovement_Format( szDeclined, sizeof( szDeclined ), "server", kMovementFatal_Declined, 9 );
	EXPECT_NE( std::string( szDeclined ).find( "movetype: 9" ), std::string::npos )
		<< "the declined reason does not name the move type it declined, which is "
		   "the one number that says WHICH managed body is missing.";
}

TEST( ShellGameMovementText, ItSurvivesTheDegenerateArguments )
{
	// The earliest of these can fire before tier0 is up, so the formatter must
	// not be the thing that faults.
	EXPECT_EQ( 0, SourceSharpMovement_Format( NULL, 100, "server", kMovementFatal_NoStock, 0 ) );

	char szBuf[1536];
	EXPECT_EQ( 0, SourceSharpMovement_Format( szBuf, 0, "server", kMovementFatal_NoStock, 0 ) );

	SourceSharpMovement_Format( szBuf, sizeof( szBuf ), NULL, kMovementFatal_NoStock, 0 );
	EXPECT_NE( std::string( szBuf ).find( "the ? side" ), std::string::npos );

	// A tiny buffer truncates rather than overruns, and still terminates.
	//
	// THE SIZE IS READ THROUGH A `volatile` ON PURPOSE. With sizeof() written
	// directly, gcc folds the call, proves the first literal alone does not fit,
	// and emits -Wformat-truncation -- a warning ABOUT THE TEST rather than about
	// the code, on the one build (`make check` builds debug) whose warning count
	// this project gates on. The volatile read is what stops the fold; the call
	// is otherwise identical and the truncation still happens at runtime, which
	// is the thing being checked.
	char szTiny[16];
	memset( szTiny, 'x', sizeof( szTiny ) );
	volatile size_t nTiny = sizeof( szTiny );
	SourceSharpMovement_Format( szTiny, nTiny, "server", kMovementFatal_NoStock, 0 );
	EXPECT_EQ( '\0', szTiny[ sizeof( szTiny ) - 1 ] );
}

//=============================================================================
// PART 4 — THE PIECEWISE FORWARDERS ARE GONE (plan_shim.md §5 3e).
//
// docs/plan-audit.md E1 ported the movement arithmetic in two shapes at once:
// TEN forwarders, each standing in front of one stock CGameMovement method and
// asked from inside stock's own PlayerMove chain, and ONE whole-loop crossing
// that replaces the chain. Every call site of the ten was in
// game/shared/gamemovement.cpp, which left both vpcs at plan_entity.md step
// 8f-remove — so from that commit the ten had no caller on any arm, their
// counters could only read zero, and cs_{sv,cl}_move_managed_report printed ten
// zeroes under a total that was entirely the eleventh's.
//
// MEASURED rather than inferred, and the measurement is in this step's commit
// message: `tools/shim-census who-imports shell_movement.cpp` names six
// imported symbols per side and not one of the ten is among them.
//
// WHY A SOURCE SCAN. The failure this pins is a RESTORATION — somebody putting
// gamemovement.cpp's two `$File` rows back, or reaching for "the piecewise
// version that was easier to debug". Both compile, both link, and both run: the
// symptom is two implementations of one piece of arithmetic that must agree
// bit-for-bit, which is the misprediction class E1 existed to remove. Nothing
// that plays the game correctly can see it.
//=============================================================================

TEST( ShellGameMovement, ThePiecewiseForwardersHaveNoDefinitionLeft )
{
	ASSERT_FALSE( Shim().empty() );

	static const char *const kGone[] =
	{
		"SourceSharp_MoveClipVelocity(",
		"SourceSharp_MoveDecayPunchAngle(",
		"SourceSharp_MoveCheckVelocity(",
		"SourceSharp_MoveGravity(",
		"SourceSharp_MoveAccelerate(",
		"SourceSharp_MoveAirAccelerate(",
		"SourceSharp_MoveFriction(",
		"SourceSharp_MoveSplineFraction(",
		"SourceSharp_MoveCalcRoll(",
		"SourceSharp_MoveReduceTimers(",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_FALSE( Has( Shim(), kGone[ i ] ) )
			<< kGone[ i ] << " is back in shell_movement.cpp. Its only call site "
			   "was in game/shared/gamemovement.cpp, which is in neither vpc, so a "
			   "definition here is either dead weight or -- worse -- the first half "
			   "of somebody restoring a second copy of the movement arithmetic.";
	}
}

TEST( ShellGameMovement, TheThreeManagedPolicyHooksAreAskedFromCSharpOnly )
{
	ASSERT_FALSE( Shim().empty() );

	// The mod's three gameplay rules -- how high a jump goes, how slippery the
	// ground is, how fast a player may move. They were native forwarders asked
	// from gamemovement.cpp; the managed loop asks the same three rules for
	// itself through GameMovement.Rules (MovementRules), which each side's
	// movement binding hands its loop for the run. Two routes to one rule is a divergence waiting to
	// happen, and only one of them has a caller.
	EXPECT_FALSE( Has( Shim(), "SourceSharp_JumpScale(" ) );
	EXPECT_FALSE( Has( Shim(), "SourceSharp_FrictionScale(" ) );
	EXPECT_FALSE( Has( Shim(), "SourceSharp_MaxSpeedScale(" ) );
	EXPECT_FALSE( Has( Shim(), "pApi->OnPlayerJump" ) )
		<< "the shim reaches the managed jump rule through the ABI again. Since "
		   "the loop itself is managed, GameMovement.Rules is the one route.";
	EXPECT_FALSE( Has( Shim(), "pApi->OnPlayerFriction" ) );
	EXPECT_FALSE( Has( Shim(), "pApi->OnPlayerMaxSpeed" ) );
}

TEST( ShellGameMovement, TheReportCountsTheOneCrossingThatHasACaller )
{
	ASSERT_FALSE( Shim().empty() );

	// tools/movemath-gate reads the TOTAL line and requires it to have grown on
	// BOTH sides, which is its liveness check -- "zero mispredictions" is
	// otherwise satisfiable by a port that never ran. The total is now exactly
	// the whole-loop crossing's count, so the gate is measuring the thing it
	// names rather than that plus ten permanent zeroes.
	EXPECT_TRUE( Has( Shim(), "kMoveProcessMovement=0," ) )
		<< "the one surviving counter is no longer the first (and only) member of "
		   "SourceSharpMoveCounter_t.";

	// EVERY indexing of the counter array, counted, so a restored forwarder that
	// carries its own counter is caught by this fact as well as by the one
	// above. Three sites, and they are the arms of one measurement: the
	// client's native crossing (the client's alone since plan_server_native_v2
	// T4g) bumps it, the client's bracket bumps it for the replay that called
	// managed code directly, and the server's managed command loop bumps it
	// through SourceSharp_MoveNoteServerCommand (PlayerCommandFinish).
	EXPECT_EQ( 3, CountOf( Shim(), "MoveCounters()[" ) )
		<< "the number of places that bump a movement counter has changed.";
	EXPECT_EQ( 3, CountOf( Shim(), "MoveCounters()[kMoveProcessMovement]++;" ) )
		<< "something other than the whole-loop crossing bumps a movement counter.";

	// And the refusal half, which is the other thing the report prints and is
	// pinned above in TheDeclineIsCountedRatherThanFatal.
	EXPECT_TRUE( Has( Shim(), "refused:%-8s" ) );
}
