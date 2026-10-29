//========= SourceSharp — server.so's own tier0 spew and assert entry points ==//
//
// tier0_spew_port.cpp: the tier0 functions server.so calls through
// public/tier0/dbg.h, defined in server.so (plan_server_novalve.md P4). This
// binary links NO tier0: every Msg / Warning / Assert / Error below reaches
// the port, and the port reaches a recording ICvar.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "tier0/dbg.h"
#include "tier0/mem.h"
#include "tier1/convar.h"
#include "icvar.h"
#include "Color.h"

#include "fatal_report.h"
#include "tier0_spew_port.h"
#include "recording_cvar.h"

#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>

#include "ss_platform.h" // SS_BIND_IMPORT

#ifdef _MSC_VER
// MSVC at /Od keeps the dead `if ( _bFatal )` arm of every Assert (dbg.h's
// _AssertMsg), and with it a reference to _ExitOnFatalAssert; clang drops a
// constant-false branch even at -O0, and /O1 dropped it too. The port defines
// no _ExitOnFatalAssert -- nothing in the game DLLs asserts fatally -- and this
// binary links no tier0, so it is defined here as tier0's is: report, then exit.
#pragma warning( disable : 4273 ) // defining what dbg.h declares dllimport

void _ExitOnFatalAssert( const tchar *pFile, int line )
{
	fprintf( stderr, "Fatal assert failed: %s, line %d.  Application exiting.\n", pFile, line );
	exit( EXIT_FAILURE );
}
SS_BIND_IMPORT( _ExitOnFatalAssert )
#endif

namespace
{
struct SpewRecord
{
	SpewType_t type;
	std::string text;
};

std::vector<SpewRecord> g_Hooked;

SpewRetval_t Hook( SpewType_t type, const tchar *pText )
{
	g_Hooked.push_back( SpewRecord{ type, pText ? pText : "" } );
	return SPEW_CONTINUE;
}

std::vector<std::string> g_Notified;

void Notify( const char *pFile, int nLine, const char *pMsg )
{
	g_Notified.push_back( std::string( pMsg ? pMsg : "" ) );
}

class Tier0SpewPort : public ::testing::Test
{
protected:
	CRecordingCvar m_Cvar;

	void SetUp() override
	{
		SpewOutputFunc( NULL ); // the default: the console
		g_Hooked.clear();
		g_Notified.clear();
		Tier0_ConnectConsole( &m_Cvar );
		Msg( "%s", "" ); // drain anything an earlier fact left held
		m_Cvar.m_Printed.clear();
	}

	void TearDown() override
	{
		SpewOutputFunc( NULL );
		SetAssertFailedNotifyFunc( NULL );
		Tier0_ConnectConsole( nullptr );
	}
};
}

TEST_F( Tier0SpewPort, MsgReachesTheEngineConsole )
{
	Msg( "hello %d\n", 5 );

	ASSERT_EQ( 1u, m_Cvar.m_Printed.size() );
	EXPECT_EQ( "plain", m_Cvar.m_Printed[0].kind );
	EXPECT_EQ( "hello 5\n", m_Cvar.m_Printed[0].text );
}

TEST_F( Tier0SpewPort, AWarningIsPrintedInColour )
{
	Warning( "careful\n" );

	ASSERT_EQ( 1u, m_Cvar.m_Printed.size() );
	EXPECT_EQ( "color", m_Cvar.m_Printed[0].kind );
	EXPECT_EQ( "careful\n", m_Cvar.m_Printed[0].text );
}

TEST_F( Tier0SpewPort, LinesSpewedBeforeTheConsoleExistsComeOutFirstInOrder )
{
	Tier0_ConnectConsole( nullptr );
	Msg( "one\n" );
	Warning( "two\n" );
	Tier0_ConnectConsole( &m_Cvar );

	Msg( "three\n" );

	ASSERT_EQ( 2u, m_Cvar.m_Printed.size() );
	EXPECT_EQ( "one\ntwo\n", m_Cvar.m_Printed[0].text );
	EXPECT_EQ( "three\n", m_Cvar.m_Printed[1].text );
}

TEST_F( Tier0SpewPort, DevMsgIsSilentWithoutDeveloper )
{
	// The recording ICvar has no "developer" convar: developer is 0.
	DevMsg( "one\n" );
	DevMsg( 2, "two\n" );
	DevWarning( "three\n" );

	EXPECT_TRUE( m_Cvar.m_Printed.empty() );
}

TEST_F( Tier0SpewPort, TheSpewSlotIsThisDllsOwnAndNullRestoresTheConsole )
{
	EXPECT_NE( nullptr, GetSpewOutputFunc() );

	SpewOutputFunc( Hook );
	EXPECT_EQ( &Hook, GetSpewOutputFunc() );
	Msg( "hooked\n" );
	ASSERT_EQ( 1u, g_Hooked.size() );
	EXPECT_EQ( SPEW_MESSAGE, g_Hooked[0].type );
	EXPECT_TRUE( m_Cvar.m_Printed.empty() );

	SpewOutputFunc( NULL );
	Msg( "console\n" );
	ASSERT_EQ( 1u, m_Cvar.m_Printed.size() );
}

TEST_F( Tier0SpewPort, AnAssertReachesTheSlotAsAnAssertWithFileAndLine )
{
	SpewOutputFunc( Hook );
	SetAssertFailedNotifyFunc( Notify );

	const bool never = false;
	Assert( never );

	ASSERT_EQ( 1u, g_Hooked.size() );
	EXPECT_EQ( SPEW_ASSERT, g_Hooked[0].type );
	EXPECT_NE( std::string::npos, g_Hooked[0].text.find( "tier0_spew_port_test.cpp (" ) ) << g_Hooked[0].text;
	EXPECT_NE( std::string::npos, g_Hooked[0].text.find( "Assertion Failed: never" ) ) << g_Hooked[0].text;
	EXPECT_EQ( '\n', g_Hooked[0].text.back() );
	ASSERT_EQ( 1u, g_Notified.size() );

	// And the type does not stick: the next Msg is a message again.
	Msg( "after\n" );
	EXPECT_EQ( SPEW_MESSAGE, g_Hooked.back().type );
}

TEST_F( Tier0SpewPort, NoAssertAsksForADialog )
{
	EXPECT_FALSE( ShouldUseNewAssertDialog() );
	EXPECT_FALSE( DoNewAssertDialog( "f", 1, "e" ) );
}

TEST_F( Tier0SpewPort, AnErrorIsSaidThenEndsTheProcessWithTheFatalReport )
{
	EXPECT_EXIT( Error( "no %s\n", "engine" ), ::testing::ExitedWithCode( kSourceSharpFatalExitCode ),
				 "FATAL ERROR: no engine" );
}

TEST_F( Tier0SpewPort, AFailedAllocationEndsTheProcessWithTheFatalReport )
{
	EXPECT_EXIT( MemAllocOOMError( 4096 ), ::testing::ExitedWithCode( kSourceSharpFatalExitCode ),
				 "FATAL ERROR: out of memory: an allocation of 4096 bytes failed" );
}
