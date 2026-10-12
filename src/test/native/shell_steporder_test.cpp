//=============================================================================//
// GATE 5's TRANSMIT-SET RECORDER, RUN -- plan_server.md Gate 5,
// docs/plan-audit.md C2.
//
// Two halves, and they need different instruments -- the same split
// shell_gamedll_refusals_test.cpp makes and for the same reason:
//
//   1. THE BODIES. That the transmit capture is one-shot, that a capture that
//      never happened is distinguishable from an empty one, that out-of-range
//      marks are dropped rather than corrupting. Those are properties of
//      CShellTransmitSet's body, so this file compiles the SHIPPED header
//      (sourcesharp/game/server/shell_steporder.h) and runs it. Nothing here is
//      a transcription.
//
//   2. THE ARRANGEMENT in the .cpp files that record. Those include cbase.h's
//      whole world and can never be linked here, so they are source-read -- the
//      way shell_gamedll_registration_test.cpp reads shell_gamedll.cpp for the
//      same reason. Comment-stripped, because every comment in those files
//      NAMES the symbols it explains and a raw search would pass on prose.
//
// WHAT THIS FILE NO LONGER CLAIMS. It held 17 CShellStepOrder facts, three
// rows inside TheSeamReachesTheShippedBodies, and two
// call-site censuses (EveryLevelStepRecords / EveryFrameStepRecords), all over the
// step-order ring. The ring had no reader -- the `tools/gate5` meant to diff
// its output was never written -- so the ring, its 41 call sites and
// `cs_sv_steporder` are gone, and those assertions are no longer claims
// about anything in the build. The transmit-set half stays because it HAS a
// reader: tools/p4-exit-probe-skybox.
//=============================================================================//

#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <sys/stat.h>

#include "gtest/gtest.h"

#include "shell_steporder.h"

//-----------------------------------------------------------------------------
// The seam the header declares, defined here against a recorder of this file's
// own. In the game these three reach CShellGameDll's field; nothing in this
// binary has a CShellGameDll, and the point of the header being engine-free is
// that it does not need one.
//
// They are defined at all because shell_steporder.h DECLARES them: a header
// that declared three functions nobody defined would link in the game and fail
// only here, which is backwards.
//-----------------------------------------------------------------------------
namespace
{

CShellTransmitSet g_TestSet;

} // namespace

bool ShellGameDll_TransmitCaptureBegin( int nRecipientEdict )
{
	return g_TestSet.BeginCapture( nRecipientEdict );
}

void ShellGameDll_TransmitCaptureMark( int iEdict )
{
	g_TestSet.Mark( iEdict );
}

//=============================================================================
// HALF ONE -- THE BODIES.
//=============================================================================












//-----------------------------------------------------------------------------
// The trigger. Without it the level load cannot be recorded at all -- see
// shell_steporder.h for the measurement that produced it.
//-----------------------------------------------------------------------------







//-----------------------------------------------------------------------------
// The transmit set.
//-----------------------------------------------------------------------------

TEST( ShellTransmitSet, UnarmedDeclinesTheCapture )
{
	CShellTransmitSet set;

	EXPECT_FALSE( set.Armed() );
	EXPECT_FALSE( set.HaveCapture() );

	// The call site's whole cost every frame but one, and the answer that keeps
	// CheckTransmit from walking 2048 edicts for nothing.
	EXPECT_FALSE( set.BeginCapture( 1 ) );
	EXPECT_FALSE( set.HaveCapture() );
}

TEST( ShellTransmitSet, ArmedCapturesExactlyOnce )
{
	CShellTransmitSet set;
	set.Arm();

	EXPECT_TRUE( set.BeginCapture( 1 ) );

	// ONE SHOT. Armed for a whole second, CheckTransmit runs 66 times and the
	// set read back would be the LAST frame before the dump rather than a frame
	// anybody chose -- and two runs of the gate would compare two different
	// frames and call the difference a regression.
	EXPECT_FALSE( set.BeginCapture( 2 ) );
	EXPECT_EQ( 1, set.Recipient() );
}

TEST( ShellTransmitSet, ArmedForOneRecipientWaitsForThatRecipient )
{
	// With two clients CheckTransmit runs for each every snapshot, client 1
	// first; an arm that took whoever came next could never see client 2.
	CShellTransmitSet set;
	set.Arm( 2 );

	EXPECT_FALSE( set.BeginCapture( 1 ) );
	EXPECT_FALSE( set.HaveCapture() );
	EXPECT_TRUE( set.Armed() );
	EXPECT_TRUE( set.BeginCapture( 2 ) );
	EXPECT_EQ( 2, set.Recipient() );
	EXPECT_FALSE( set.BeginCapture( 2 ) );
}

TEST( ShellTransmitSet, TheRecipientIsPartOfTheAnswer )
{
	CShellTransmitSet set;
	EXPECT_EQ( -1, set.Recipient() );

	set.Arm();
	set.BeginCapture( 7 );
	EXPECT_EQ( 7, set.Recipient() );
}

TEST( ShellTransmitSet, MarksAreASet )
{
	CShellTransmitSet set;
	set.Arm();
	ASSERT_TRUE( set.BeginCapture( 1 ) );

	set.Mark( 0 );
	set.Mark( 33 );
	set.Mark( 33 );
	set.Mark( 2047 );

	// A SET, so the second Mark( 33 ) is not a second member. The source is a
	// CBitVec and this has to answer the way one does.
	EXPECT_EQ( 3, set.Count() );
	EXPECT_TRUE( set.IsSet( 0 ) );
	EXPECT_TRUE( set.IsSet( 33 ) );
	EXPECT_TRUE( set.IsSet( 2047 ) );
	EXPECT_FALSE( set.IsSet( 1 ) );
	EXPECT_FALSE( set.IsSet( 32 ) );
	EXPECT_FALSE( set.IsSet( 34 ) );
}

TEST( ShellTransmitSet, OutOfRangeMarksAreDroppedRatherThanCorrupting )
{
	CShellTransmitSet set;
	set.Arm();
	ASSERT_TRUE( set.BeginCapture( 1 ) );

	set.Mark( -1 );
	set.Mark( CShellTransmitSet::kMaxEdicts );
	set.Mark( 999999 );

	EXPECT_EQ( 0, set.Count() );
	EXPECT_FALSE( set.IsSet( -1 ) );
	EXPECT_FALSE( set.IsSet( CShellTransmitSet::kMaxEdicts ) );
}

TEST( ShellTransmitSet, ASecondCaptureClearsTheFirst )
{
	CShellTransmitSet set;

	set.Arm();
	ASSERT_TRUE( set.BeginCapture( 1 ) );
	set.Mark( 5 );
	ASSERT_EQ( 1, set.Count() );

	set.Arm();
	ASSERT_TRUE( set.BeginCapture( 2 ) );

	// Not a union with the previous frame's. Two runs of the gate on one game
	// must not accumulate.
	EXPECT_EQ( 0, set.Count() );
	EXPECT_FALSE( set.IsSet( 5 ) );
}

TEST( ShellTransmitSet, NoCaptureIsDistinguishableFromAnEmptyOne )
{
	// A client that received nothing and a capture that never happened are
	// opposite findings, and the dump has to be able to tell them apart -- see
	// the `nocapture` line in cs_sv_transmitset.
	CShellTransmitSet never;
	EXPECT_FALSE( never.HaveCapture() );

	CShellTransmitSet empty;
	empty.Arm();
	ASSERT_TRUE( empty.BeginCapture( 1 ) );
	EXPECT_TRUE( empty.HaveCapture() );
	EXPECT_EQ( 0, empty.Count() );
}

TEST( ShellTransmitSet, TwoSetsShareNothing )
{
	CShellTransmitSet a, b;

	a.Arm();
	ASSERT_TRUE( a.BeginCapture( 1 ) );
	a.Mark( 11 );

	EXPECT_FALSE( b.HaveCapture() );
	EXPECT_FALSE( b.IsSet( 11 ) );
	EXPECT_EQ( 0, b.Count() );
}

TEST( ShellTransmitSet, TheSeamReachesTheShippedBodies )
{
	// The three free functions shell_steporder.h declares, driven end to end.
	// In the game they land on CShellGameDll's field; the property being held
	// is that they land ANYWHERE -- a header that declares a seam nobody wires
	// is a recorder that records nothing and a gate that reports a green empty.
	//
	// THE RING'S THREE ROWS ARE GONE with ShellGameDll_RecordStep, which had no
	// reader; what is left is the transmit seam, which tools/p4-exit-probe-skybox
	// reads.
	g_TestSet.Arm();
	EXPECT_TRUE( ShellGameDll_TransmitCaptureBegin( 3 ) );
	ShellGameDll_TransmitCaptureMark( 12 );
	EXPECT_TRUE( g_TestSet.IsSet( 12 ) );
	EXPECT_EQ( 3, g_TestSet.Recipient() );
}

//=============================================================================
// HALF TWO -- THE ARRANGEMENT, source-read.
//=============================================================================

namespace
{

std::string SlurpStripped( const std::string &strPath )
{
	std::ifstream in( strPath.c_str() );
	EXPECT_TRUE( in.good() ) << "cannot read " << strPath;

	std::string strOut;
	std::string strLine;
	while ( std::getline( in, strLine ) )
	{
		// Comment-stripped. Every comment in these files NAMES the symbols it
		// explains -- this test's own subject appears in four of them -- so a
		// raw search would pass on prose and report call sites that are
		// paragraphs.
		const size_t at = strLine.find( "//" );
		if ( at != std::string::npos )
			strLine = strLine.substr( 0, at );
		strOut += strLine;
		strOut += "\n";
	}
	return strOut;
}

int CountOccurrences( const std::string &strHay, const std::string &strNeedle )
{
	int n = 0;
	size_t at = 0;
	while ( ( at = strHay.find( strNeedle, at ) ) != std::string::npos )
	{
		n++;
		at += strNeedle.size();
	}
	return n;
}

const char *kLevelSteps  = "sourcesharp/game/server/shell_level.cpp";
const char *kFrameSteps  = "sourcesharp/game/server/shell_frame.cpp";
const char *kGameEnts    = "sourcesharp/game/server/shell_gameents.cpp";
const char *kGameDll     = "sourcesharp/game/server/shell_gamedll.cpp";

} // namespace



TEST( StepFileArrangement, TheStepFilesCannotSeeTheGameDllObject )
{
	// The rule outlives the recorder that motivated it: a step that can reach
	// CShellGameDll is one edit away from being a decision, which is exactly
	// what plan_gamedll.md Part 4 forbids. Naming the singleton in a step file
	// would work and would dissolve the rule silently.
	const std::string level = SlurpStripped( kLevelSteps );
	const std::string frame = SlurpStripped( kFrameSteps );

	EXPECT_EQ( 0, CountOccurrences( level, "CShellGameDll" ) );
	EXPECT_EQ( 0, CountOccurrences( frame, "CShellGameDll" ) );
	EXPECT_EQ( 0, CountOccurrences( level, "s_gameShellDll" ) );
	EXPECT_EQ( 0, CountOccurrences( frame, "s_gameShellDll" ) );
}

TEST( TransmitSetArrangement, TheSeamIsDefinedExactlyOnceInTheShell )
{
	// Three declarations in the header, three definitions in shell_gamedll.cpp.
	// A second definition would not link; NONE would link in the game and fail
	// only in this binary, which is the direction that goes unnoticed.
	//
	// THE FOURTH ROW IS GONE. It pinned ShellGameDll_RecordStep's definition and
	// the `ring.Arm( nBudget, pTrigger )` argument pass-through in
	// cs_sv_steporder. The ring had no reader and is deleted, so neither is a
	// claim about the build any more.
	const std::string dll = SlurpStripped( kGameDll );

	EXPECT_EQ( 1, CountOccurrences( dll, "bool ShellGameDll_TransmitCaptureBegin( int nRecipientEdict )" ) );
	EXPECT_EQ( 1, CountOccurrences( dll, "void ShellGameDll_TransmitCaptureMark( int iEdict )" ) );
	EXPECT_EQ( 1, CountOccurrences( dll, "void ShellGameDll_TransmitCaptureSkyboxArea( int nArea )" ) );
}

TEST( TransmitSetArrangement, TheRecorderIsAFieldAndNotFileScopeState )
{
	// The project rule tools/statics-audit gates, held here at the source level
	// too because the audit reads OBJECTS and an anonymous-namespace instance
	// is not a `static`.
	const std::string dll = SlurpStripped( kGameDll );

	EXPECT_EQ( 1, CountOccurrences( dll, "CShellTransmitSet m_TransmitSet;" ) );
	EXPECT_EQ( 0, CountOccurrences( dll, "static CShellTransmitSet" ) );
}

TEST( TransmitSetArrangement, TheCaptureIsArmedAfterTheEarlyReturnsAndCopiedAtTheEnd )
{
	const std::string ents = SlurpStripped( kGameEnts );

	// Exactly one arm and exactly one copy loop, in CheckTransmit.
	ASSERT_EQ( 1, CountOccurrences( ents, "ShellGameDll_TransmitCaptureBegin(" ) );
	ASSERT_EQ( 1, CountOccurrences( ents, "ShellGameDll_TransmitCaptureMark(" ) );

	// THE GUARD'S SPELLING CHANGED AT plan-audit C4, and this fact went red for
	// it -- correctly, and for the work being done right. It read
	// `if ( !pRecipientEntity && !bStandaloneRecipient )`; C4 moved that
	// disjunction into C# and the guard became one bool, so the anchor stopped
	// matching anything and the assertion started interrogating npos.
	//
	// THE PROPERTY IS UNCHANGED and is what the new anchor holds: the capture is
	// armed AFTER the guard that declines a recipient this function answers
	// nothing for. What moved is which expression that guard tests.
	const size_t atGuard  = ents.find( "if ( !bHaveRecipient )" );
	const size_t atArm    = ents.find( "ShellGameDll_TransmitCaptureBegin(" );
	const size_t atMark   = ents.find( "ShellGameDll_TransmitCaptureMark(" );

	ASSERT_NE( std::string::npos, atGuard );
	ASSERT_NE( std::string::npos, atArm );
	ASSERT_NE( std::string::npos, atMark );

	// AFTER the declining guard: a capture spent on a recipient this function
	// answers nothing for hands the gate an empty set, which is exactly the
	// finding Gate 5 exists to report and so is the worst possible artefact.
	EXPECT_LT( atGuard, atArm );

	// And the copy comes after the arm, at the end of the walk -- the bitvec is
	// only the answer once the walk has finished with it.
	EXPECT_LT( atArm, atMark );
}

TEST( TransmitSetArrangement, TheCopyReadsTheEngineBitvecRatherThanTheSetSites )
{
	// SetTransmit() marks an entity's dependents, which this function never
	// touches. An instrument that marked only the eleven visible ->Set( iEdict )
	// sites would record a SUBSET and call it the transmit set.
	const std::string ents = SlurpStripped( kGameEnts );
	EXPECT_EQ( 1, CountOccurrences( ents, "pInfo->m_pTransmitEdict->Get( i )" ) );
}
