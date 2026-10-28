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

} // namespace

//=============================================================================
// PART 1 — the interface, published by the shim.
//=============================================================================

//=============================================================================
// PART 2 — the deleted fallback, which is the ruling.
//=============================================================================

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
			<< "reason " << r
			<< " has no remedy. The remedy line is the most "
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
	EXPECT_EQ( '\0', szTiny[sizeof( szTiny ) - 1] );
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
