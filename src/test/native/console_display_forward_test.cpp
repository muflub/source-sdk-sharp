//========= SourceSharp — the engine's console prints reach the managed side //
//
// console_display_forward.cpp installs an IConsoleDisplayFunc on the side's
// ICvar so the engine's ColorPrint/Print/DPrint fan-out reaches this load's
// managed console-output area (docs/todo.md's console capture item): since
// console-batch, by appending to the forwarder's line buffer, handed to the
// area once per load, which the managed side drains. The binary links no
// engine: CRecordingCvar records the install, and the fixture answers
// ManagedConsoleOutput() with its own sink, which drains the way
// NativeConsoleBuffer does.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "icvar.h"
#include "imanaged_areas.h"

#include "console_display_forward.h"
#include "console_line_buffer_drain.h"
#include "recording_cvar.h"

#include <string>
#include <vector>

namespace
{

struct ForwardedLine
{
	std::string text;
	int nWarning;
	uint32_t seq;
};

std::vector<ForwardedLine> g_SinkLines;

/// The area the accessor answers with while a "load" is open: it records the
/// buffer it was handed, and Drain() reads it as the managed reader does.
/// Managed builds the real one as a block whose first word is its table
/// (plan_forwarders_everywhere.md phase 4); here the table is static functions
/// that call the members below.
class CSinkArea final : public IManagedConsoleOutput
{
public:
	CSinkArea()
		: IManagedConsoleOutput( &kTable )
	{
	}

	CConsoleLineBuffer *m_pBuffer = nullptr;
	int m_nAttaches = 0;

	void Attach( void *pBuffer );

	/// The lines buffered since the last drain, onto g_SinkLines.
	void Drain()
	{
		if ( !m_pBuffer )
			return;
		for ( const ConsoleLineDrained &line : ConsoleLineParse( ConsoleLineSwap( *m_pBuffer ) ) )
			g_SinkLines.push_back( ForwardedLine{ line.text, (int)line.warning, line.seq } );
	}

private:
	static CSinkArea *Of( void *self ) { return static_cast<CSinkArea *>( self ); }

	static void Released( IManagedObject * ) {}

	// The forwarder never validates (acceptance is ManagedAccept's job at
	// acquire); -1 is the agree answer managed_accept_test's fake gives.
	static int32_t Validated( IManagedObject *, const uint32_t *, int32_t ) { return -1; }

	static void Attached( IManagedConsoleOutput *self, void *pBuffer ) { Of( self )->Attach( pBuffer ); }

	static const SSManagedConsoleOutputTable kTable;
};

const SSManagedConsoleOutputTable CSinkArea::kTable = { &Released, &Validated, &Attached };

/// The forwarder's buffer, as the last attach handed it: one per DLL, so
/// every test sees the same memory and its sequence runs on across tests.
CConsoleLineBuffer *g_pBuffer = nullptr;

void CSinkArea::Attach( void *pBuffer )
{
	++m_nAttaches;
	m_pBuffer = static_cast<CConsoleLineBuffer *>( pBuffer );
	g_pBuffer = m_pBuffer;
}

CSinkArea g_AreaA;
CSinkArea g_AreaB;

// Outside a load the accessor answers NULL; the fixture opens one by pointing
// it at a sink.
IManagedConsoleOutput *g_pArea = nullptr;

} // namespace

IManagedConsoleOutput *ManagedConsoleOutput()
{
	return g_pArea;
}

namespace
{

class ConsoleDisplayForward : public ::testing::Test
{
protected:
	CRecordingCvar m_Cvar;
	// Fixture-owned like m_Cvar: a second ICvar installed in a test body must
	// outlive it, since TearDown's ConsoleDisplay_Remove still names it.
	CRecordingCvar m_Second;

	void SetUp() override
	{
		ConsoleDisplay_Remove(); // idempotent guard across fixtures
		// The buffer is the forwarder's and outlives a test: empty it.
		if ( g_pBuffer )
		{
			ConsoleLineSwap( *g_pBuffer );
			ConsoleLineSwap( *g_pBuffer );
		}
		g_AreaA = CSinkArea();
		g_AreaB = CSinkArea();
		g_SinkLines.clear();
		g_pArea = &g_AreaA;
	}

	/// What this load's managed side would see on its next drain.
	const std::vector<ForwardedLine> &Drained( CSinkArea &area = g_AreaA )
	{
		area.Drain();
		return g_SinkLines;
	}

	void TearDown() override
	{
		ConsoleDisplay_Remove();
		g_pArea = nullptr;
	}

	IConsoleDisplayFunc *Installed()
	{
		EXPECT_EQ( 1u, m_Cvar.m_Installed.size() ) << "install must reach the ICvar exactly once";
		return m_Cvar.m_Installed.empty() ? nullptr : m_Cvar.m_Installed[0];
	}
};

TEST_F( ConsoleDisplayForward, InstallPutsOneFuncOnTheCvar )
{
	ASSERT_TRUE( ConsoleDisplay_Install( &m_Cvar ) );
	IConsoleDisplayFunc *pFunc = Installed();
	ASSERT_NE( nullptr, pFunc );
	EXPECT_TRUE( m_Cvar.m_Removed.empty() );
}

TEST_F( ConsoleDisplayForward, InstallHandsThisLoadTheBufferOnce )
{
	ASSERT_TRUE( ConsoleDisplay_Install( &m_Cvar ) );
	EXPECT_EQ( 1, g_AreaA.m_nAttaches ) << "the load must hold the buffer before its first print";
	ASSERT_NE( nullptr, g_AreaA.m_pBuffer );
	Installed()->Print( "one\n" );
	Installed()->Print( "two\n" );
	EXPECT_EQ( 1, g_AreaA.m_nAttaches ) << "a print must not hand the buffer over again";
}

TEST_F( ConsoleDisplayForward, TheBufferHandedOverCarriesTheHalfSizeTheReaderReads )
{
	// The buffer is all zero until Attach publishes it (console_line_buffer.h):
	// the reader takes each half's size from the header.
	ASSERT_TRUE( ConsoleDisplay_Install( &m_Cvar ) );
	ASSERT_NE( nullptr, g_AreaA.m_pBuffer );
	EXPECT_EQ( kConsoleLineHalfBytes, g_AreaA.m_pBuffer->m_Header.nHalfBytes );
}

// The point of the buffer: a print crosses nothing. The area HAS no per-line
// slot since the serialised ABI pass (2026-10-20): Release, Validate and the
// buffer hand-over are the whole table, so a line slot that came back would
// change its size here.
static_assert( sizeof( SSManagedConsoleOutputTable ) == 3 * sizeof( void * ),
			   "IManagedConsoleOutput's table is Release, Validate and ConsoleBufferAttach" );

TEST_F( ConsoleDisplayForward, APrintCrossesNothing )
{
	ConsoleDisplay_Install( &m_Cvar );
	for ( int i = 0; i < 100; ++i )
		Installed()->Print( "a line\n" );
	EXPECT_EQ( 100u, Drained().size() ) << "every line is in the buffer";
}

TEST_F( ConsoleDisplayForward, PrintForwardsTheExactTextAsNormal )
{
	ConsoleDisplay_Install( &m_Cvar );
	Installed()->Print( "a plain line\n" );

	ASSERT_EQ( 1u, Drained().size() );
	EXPECT_EQ( "a plain line\n", g_SinkLines[0].text );
	EXPECT_EQ( 0, g_SinkLines[0].nWarning );
}

// The engine prints EVERY spew through ColorPrint (the engine's
// spew handler), a plain Msg included, in the spew
// output colour; only the colour says which spew it was. A warning is
// SPEW_WARNING's (255, 90, 90), an assert's (255, 20, 20), an error's
// (20, 70, 255).
TEST_F( ConsoleDisplayForward, AMsgThroughColorPrintIsNotAWarning )
{
	ConsoleDisplay_Install( &m_Cvar );
	Installed()->ColorPrint( Color( 255, 255, 255, 255 ), "a plain msg\n" ); // the default spew colour
	Installed()->ColorPrint( Color( 0, 255, 0, 255 ), "a coloured msg\n" ); // a ConColorMsg

	ASSERT_EQ( 2u, Drained().size() );
	EXPECT_EQ( "a plain msg\n", g_SinkLines[0].text );
	EXPECT_EQ( 0, g_SinkLines[0].nWarning );
	EXPECT_EQ( 0, g_SinkLines[1].nWarning );
}

TEST_F( ConsoleDisplayForward, AWarningAssertOrErrorThroughColorPrintIsAWarning )
{
	ConsoleDisplay_Install( &m_Cvar );
	Installed()->ColorPrint( Color( 255, 90, 90, 255 ), "a warning line\n" );
	Installed()->ColorPrint( Color( 255, 20, 20, 255 ), "an assert line\n" );
	Installed()->ColorPrint( Color( 20, 70, 255, 255 ), "an error line\n" );

	ASSERT_EQ( 3u, Drained().size() );
	EXPECT_EQ( "a warning line\n", g_SinkLines[0].text );
	EXPECT_EQ( 1, g_SinkLines[0].nWarning );
	EXPECT_EQ( 1, g_SinkLines[1].nWarning );
	EXPECT_EQ( 1, g_SinkLines[2].nWarning );
}

TEST_F( ConsoleDisplayForward, DPrintForwardsTheExactTextAsNormal )
{
	ConsoleDisplay_Install( &m_Cvar );
	Installed()->DPrint( "a developer line\n" );

	ASSERT_EQ( 1u, Drained().size() );
	EXPECT_EQ( "a developer line\n", g_SinkLines[0].text );
	EXPECT_EQ( 0, g_SinkLines[0].nWarning );
}

TEST_F( ConsoleDisplayForward, LinesKeepPrintOrderAndConsecutiveNumbers )
{
	ConsoleDisplay_Install( &m_Cvar );
	Installed()->Print( "first\n" );
	Installed()->DPrint( "second\n" );
	Installed()->ColorPrint( Color( 255, 90, 90, 255 ), "third\n" );

	ASSERT_EQ( 3u, Drained().size() );
	EXPECT_EQ( "first\n", g_SinkLines[0].text );
	EXPECT_EQ( "second\n", g_SinkLines[1].text );
	EXPECT_EQ( "third\n", g_SinkLines[2].text );
	EXPECT_EQ( g_SinkLines[0].seq + 1, g_SinkLines[1].seq );
	EXPECT_EQ( g_SinkLines[1].seq + 1, g_SinkLines[2].seq );
}

TEST_F( ConsoleDisplayForward, ANullTextIsDropped )
{
	ConsoleDisplay_Install( &m_Cvar );
	const uint32_t before = ConsoleLineSequence( *g_pBuffer );
	Installed()->Print( nullptr );

	EXPECT_TRUE( Drained().empty() );
	EXPECT_EQ( before, ConsoleLineSequence( *g_pBuffer ) ) << "a NULL print takes no number";
}

TEST_F( ConsoleDisplayForward, OutsideALoadEveryPrintIsDropped )
{
	g_pArea = nullptr;
	ConsoleDisplay_Install( &m_Cvar );
	Installed()->Print( "into nothing\n" );

	// A load acquired later is handed the buffer, and the line is not in it.
	g_pArea = &g_AreaA;
	Installed()->Print( "into the load\n" );
	ASSERT_EQ( 1u, Drained().size() );
	EXPECT_EQ( "into the load\n", g_SinkLines[0].text );
	EXPECT_EQ( 1, g_AreaA.m_nAttaches ) << "the first print of a late load hands it the buffer";
}

TEST_F( ConsoleDisplayForward, RemoveStopsTheFlow )
{
	// Remove detaches the func from the ICvar, so the engine's fan-out stops
	// reaching it; a stale pointer kept calling is answered nothing too
	// (Forward checks its own registration, the one state it owns).
	ConsoleDisplay_Install( &m_Cvar );
	IConsoleDisplayFunc *pFunc = Installed();
	ConsoleDisplay_Remove();

	ASSERT_EQ( 1u, m_Cvar.m_Removed.size() );
	EXPECT_EQ( pFunc, m_Cvar.m_Removed[0] );

	pFunc->Print( "after teardown\n" );
	EXPECT_TRUE( Drained().empty() );
}

TEST_F( ConsoleDisplayForward, AReinstallAfterRemoveInstallsAgain )
{
	ConsoleDisplay_Install( &m_Cvar );
	ConsoleDisplay_Remove();
	ASSERT_TRUE( ConsoleDisplay_Install( &m_Second ) );

	ASSERT_EQ( 1u, m_Second.m_Installed.size() );
	m_Second.m_Installed[0]->Print( "fresh cvar\n" );
	ASSERT_EQ( 1u, Drained().size() );
	EXPECT_EQ( "fresh cvar\n", g_SinkLines[0].text );
	EXPECT_EQ( 2, g_AreaA.m_nAttaches ) << "each install hands the buffer over";
}

TEST_F( ConsoleDisplayForward, ASecondInstallIsIgnored )
{
	ASSERT_TRUE( ConsoleDisplay_Install( &m_Cvar ) );
	EXPECT_FALSE( ConsoleDisplay_Install( &m_Second ) );

	EXPECT_EQ( 1u, m_Cvar.m_Installed.size() );
	EXPECT_TRUE( m_Second.m_Installed.empty() );
}

TEST_F( ConsoleDisplayForward, InstallOfNullDoesNothing )
{
	EXPECT_FALSE( ConsoleDisplay_Install( nullptr ) );
	EXPECT_TRUE( m_Cvar.m_Installed.empty() );
}

// ---------------------------------------------------------------------------
// A managed reload's gap (plan_managed_vtable_reload.md L4). Between the
// unload's Detach and the next build's patch the area block stays where native
// holds it, every slot on the trap table, so ManagedConsoleOutput() still
// answers it. The trap reports through the side's log -- a Warning, which the
// engine prints through every installed display func -- before it fails the
// process: with the forwarder still installed and calling the area, that
// report re-entered the trap until the stack ran out (the nightly's
// hot-reload step died of SIGABRT on 10-10 and 10-11). A print no longer
// calls the area, but handing the buffer over still does.
// ---------------------------------------------------------------------------

/// The engine's console fan-out over CRecordingCvar: the func installed and
/// not since removed gets the print, as Con_ColorPrintf's loop would.
void EnginePrint( CRecordingCvar &cvar, const char *pText )
{
	if ( cvar.m_Installed.size() > cvar.m_Removed.size() )
		cvar.m_Installed.back()->Print( pText );
}

/// A detached area: every call is a trap hit, and the trap's report prints
/// through the engine (as ManagedVtables.FailFast's log.Warning does). The
/// depth cap stands in for the stack the real recursion ran out of.
class CTrapArea final : public IManagedConsoleOutput
{
public:
	CTrapArea()
		: IManagedConsoleOutput( &kTable )
	{
	}

	CRecordingCvar *m_pCvar = nullptr;
	int m_nHits = 0;
	int m_nDepth = 0;

	void Hit()
	{
		++m_nHits;
		if ( m_pCvar && m_nDepth < 8 )
		{
			++m_nDepth;
			EnginePrint(
				*m_pCvar,
				"[SourceSharp] IManagedConsoleOutput slot was called while its managed vtable was detached\n" );
			--m_nDepth;
		}
	}

private:
	static CTrapArea *Of( void *self ) { return static_cast<CTrapArea *>( self ); }

	static void Released( IManagedObject * ) {}

	static int32_t Validated( IManagedObject *, const uint32_t *, int32_t ) { return -1; }

	static void Attached( IManagedConsoleOutput *self, void * ) { Of( self )->Hit(); }

	static const SSManagedConsoleOutputTable kTable;
};

const SSManagedConsoleOutputTable CTrapArea::kTable = { &Released, &Validated, &Attached };

TEST_F( ConsoleDisplayForward, WithoutTheGapAReinstallOverADetachedAreaHitsTheTrap )
{
	// The stimulus reaches the subject: an install while the area is on the
	// trap table hands the buffer to the trap. The gap below is what keeps
	// the reload from doing exactly this.
	CTrapArea trap;
	trap.m_pCvar = &m_Cvar;
	g_pArea = &trap;
	ConsoleDisplay_Install( &m_Cvar );

	EXPECT_GE( trap.m_nHits, 1 );
}

TEST_F( ConsoleDisplayForward, InAReloadGapNoPrintReachesTheDetachedArea )
{
	CTrapArea trap;
	trap.m_pCvar = &m_Cvar;
	ASSERT_TRUE( ConsoleDisplay_Install( &m_Cvar ) );
	EnginePrint( m_Cvar, "before the reload\n" );
	{
		CConsoleDisplayReloadGap gap;
		g_pArea = &trap; // UnloadSide's Detach
		EnginePrint( m_Cvar, "[SourceSharp] server: unloading\n" );
		g_pArea = &g_AreaB; // the next build's patch
	}
	EXPECT_EQ( 0, trap.m_nHits ) << "a print between the unload and the load reached the trap table";
}

TEST_F( ConsoleDisplayForward, AfterAReloadGapTheNewLoadGetsThePrintsOnTheSameCvar )
{
	ASSERT_TRUE( ConsoleDisplay_Install( &m_Cvar ) );
	{
		CConsoleDisplayReloadGap gap;
		g_pArea = &g_AreaB;
	}
	EnginePrint( m_Cvar, "after the reload\n" );

	EXPECT_EQ( 1, g_AreaB.m_nAttaches ) << "the next load must be handed the buffer";
	ASSERT_EQ( 1u, Drained( g_AreaB ).size() ) << "the forwarder must be back on the engine's list after the gap";
	EXPECT_EQ( "after the reload\n", g_SinkLines[0].text );
	EXPECT_EQ( 2u, m_Cvar.m_Installed.size() ) << "re-installed on the ICvar it was removed from";
	EXPECT_EQ( 1u, m_Cvar.m_Removed.size() );
}

TEST_F( ConsoleDisplayForward, TheBufferAndItsNumbersSurviveAReloadGap )
{
	// Lines the old load never drained reach the next one, in order, from the
	// same memory, and the numbers run on: monotonic across cs_sv_reload.
	ASSERT_TRUE( ConsoleDisplay_Install( &m_Cvar ) );
	EnginePrint( m_Cvar, "just before the gap\n" );
	EnginePrint( m_Cvar, "the last line before it\n" );
	{
		CConsoleDisplayReloadGap gap;
		g_pArea = &g_AreaB;
	}
	EnginePrint( m_Cvar, "the first line after it\n" );

	EXPECT_EQ( g_AreaA.m_pBuffer, g_AreaB.m_pBuffer ) << "the next load is handed the same buffer";
	ASSERT_EQ( 3u, Drained( g_AreaB ).size() );
	EXPECT_EQ( "just before the gap\n", g_SinkLines[0].text );
	EXPECT_EQ( "the last line before it\n", g_SinkLines[1].text );
	EXPECT_EQ( "the first line after it\n", g_SinkLines[2].text );
	EXPECT_EQ( g_SinkLines[0].seq + 1, g_SinkLines[1].seq );
	EXPECT_EQ( g_SinkLines[1].seq + 1, g_SinkLines[2].seq ) << "the numbers run on across the gap";
}

TEST_F( ConsoleDisplayForward, EndingAReloadGapEarlyReinstallsOnce )
{
	ASSERT_TRUE( ConsoleDisplay_Install( &m_Cvar ) );
	{
		CConsoleDisplayReloadGap gap;
		g_pArea = &g_AreaB;
		gap.End();
		EnginePrint( m_Cvar, "after the load, before the scope ends\n" );
	}
	ASSERT_EQ( 1u, Drained( g_AreaB ).size() ) << "End() must put the forwarder back at once";
	EXPECT_EQ( 2u, m_Cvar.m_Installed.size() ) << "and the scope's end must not install it a second time";
	EXPECT_EQ( 1, g_AreaB.m_nAttaches );
}

TEST_F( ConsoleDisplayForward, AReloadGapWithNothingInstalledInstallsNothing )
{
	{
		CConsoleDisplayReloadGap gap;
	}
	EXPECT_TRUE( m_Cvar.m_Installed.empty() );
	EXPECT_TRUE( m_Cvar.m_Removed.empty() );
}

TEST_F( ConsoleDisplayForward, RemoveWithoutInstallIsHarmless )
{
	ConsoleDisplay_Remove();
	EXPECT_TRUE( m_Cvar.m_Removed.empty() );
}

} // namespace
