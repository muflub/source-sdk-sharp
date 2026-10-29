//========= SourceSharp — who asked the process to end =======================//
//
// exit_watch.cpp: the native half of the dev API's exit record. It records
// facts and changes nothing else: a signal's number and sender, then the
// handler that was there before, run as it would have been; SDL's quit and
// window-close events; a fatal stop's record file.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "exit_watch.h"
#include "fatal_report.h"

#include <fstream>
#include <signal.h>
#include <sstream>
#include <stdlib.h>
#include <string.h>
#include <string>
#include <sys/wait.h>
#include <unistd.h>

namespace
{

struct ExitWatchTest : ::testing::Test
{
	void SetUp() override { ExitWatch_ResetForTest(); }

	void TearDown() override { ExitWatch_ResetForTest(); }
};

int32_t Fact( int nFact )
{
	int32_t facts[kExitFactCount] = {};
	EXPECT_EQ( kExitFactCount, ExitWatch_Read( facts, kExitFactCount ) );
	return facts[nFact];
}

// SDL2's event layout as far as the watch reads it: type, then (for a window
// event) timestamp, windowID and the sub-event byte at offset 12.
struct FakeSdlEvent_t
{
	uint32_t nType;
	uint32_t nTimestamp;
	uint32_t nWindowId;
	uint8_t nSubEvent;
	uint8_t pad[43];
};

volatile sig_atomic_t g_nPlainCalls = 0;

void PlainHandler( int )
{
	g_nPlainCalls = g_nPlainCalls + 1;
}

volatile sig_atomic_t g_nInfoPid = 0;

void InfoHandler( int, siginfo_t *pInfo, void * )
{
	g_nInfoPid = pInfo ? pInfo->si_pid : -1;
}

void Install( int nSigno, void ( *pPlain )( int ) )
{
	struct sigaction sa;
	memset( &sa, 0, sizeof( sa ) );
	sa.sa_handler = pPlain;
	sigemptyset( &sa.sa_mask );
	sigaction( nSigno, &sa, NULL );
}

std::string ReadFile( const std::string &path )
{
	std::ifstream in( path );
	std::stringstream all;
	all << in.rdbuf();
	return all.str();
}

} // namespace

TEST_F( ExitWatchTest, ARecordedSignalKeepsItsNumberAndItsSender )
{
	ExitWatch_OnSignalForTest( SIGTERM, 4242, 1000 );
	EXPECT_EQ( 1, Fact( kExitFactSignalCount ) );
	EXPECT_EQ( SIGTERM, Fact( kExitFactFirstSigno ) );
	EXPECT_EQ( 4242, Fact( kExitFactFirstPid ) );
	EXPECT_EQ( 1000, Fact( kExitFactFirstUid ) );
}

TEST_F( ExitWatchTest, TheFirstAndTheLastSenderAreBothKept )
{
	ExitWatch_OnSignalForTest( SIGTERM, 11, 0 );
	ExitWatch_OnSignalForTest( SIGINT, 22, 0 );
	EXPECT_EQ( 2, Fact( kExitFactSignalCount ) );
	EXPECT_EQ( 11, Fact( kExitFactFirstPid ) );
	EXPECT_EQ( 22, Fact( kExitFactLastPid ) );
	EXPECT_EQ( SIGINT, Fact( kExitFactLastSigno ) );
}

TEST_F( ExitWatchTest, AnSdlQuitIsCountedAsAQuit )
{
	FakeSdlEvent_t ev = {};
	ev.nType = 0x100;
	ExitWatch_OnSdlEventForTest( &ev );
	EXPECT_EQ( 1, Fact( kExitFactSdlQuitCount ) );
	EXPECT_EQ( 0, Fact( kExitFactWindowCloseCount ) );
}

TEST_F( ExitWatchTest, AWindowCloseIsCountedAsAClose )
{
	FakeSdlEvent_t ev = {};
	ev.nType = 0x200;
	ev.nSubEvent = 14;
	ExitWatch_OnSdlEventForTest( &ev );
	EXPECT_EQ( 1, Fact( kExitFactWindowCloseCount ) );
	EXPECT_EQ( 0, Fact( kExitFactSdlQuitCount ) );
}

TEST_F( ExitWatchTest, AnyOtherWindowEventIsNotAClose )
{
	FakeSdlEvent_t ev = {};
	ev.nType = 0x200;
	ev.nSubEvent = 12; // SDL_WINDOWEVENT_FOCUS_GAINED
	ExitWatch_OnSdlEventForTest( &ev );
	EXPECT_EQ( 0, Fact( kExitFactWindowCloseCount ) );
}

TEST_F( ExitWatchTest, ArmingRecordsTheSenderAndRunsThePlainHandlerThatWasThere )
{
	Install( SIGHUP, PlainHandler );
	g_nPlainCalls = 0;
	EXPECT_NE( 0, ExitWatch_Arm( NULL ) & kExitWatchSignals );

	raise( SIGHUP );

	EXPECT_EQ( 1, g_nPlainCalls );
	EXPECT_EQ( SIGHUP, Fact( kExitFactFirstSigno ) );
	EXPECT_EQ( int( getpid() ), Fact( kExitFactFirstPid ) );
	EXPECT_EQ( 2, Fact( kExitFactPrevHup ) );
}

TEST_F( ExitWatchTest, AnSaSiginfoHandlerThatWasThereStillGetsTheSiginfo )
{
	struct sigaction sa;
	memset( &sa, 0, sizeof( sa ) );
	sa.sa_sigaction = InfoHandler;
	sa.sa_flags = SA_SIGINFO;
	sigemptyset( &sa.sa_mask );
	sigaction( SIGINT, &sa, NULL );
	g_nInfoPid = 0;

	ExitWatch_Arm( NULL );
	raise( SIGINT );

	EXPECT_EQ( int( getpid() ), int( g_nInfoPid ) );
	EXPECT_EQ( 3, Fact( kExitFactPrevInt ) );
}

TEST_F( ExitWatchTest, AnIgnoredSignalIsStillIgnoredAndStillRecorded )
{
	Install( SIGINT, SIG_IGN );
	ExitWatch_Arm( NULL );
	raise( SIGINT ); // the test is that this process is still here
	EXPECT_EQ( 1, Fact( kExitFactSignalCount ) );
	EXPECT_EQ( 1, Fact( kExitFactPrevInt ) );
}

TEST_F( ExitWatchTest, ADefaultDispositionStillEndsTheProcessBySignal )
{
	const pid_t child = fork();
	ASSERT_GE( child, 0 );
	if ( child == 0 )
	{
		Install( SIGTERM, SIG_DFL );
		ExitWatch_Arm( NULL );
		raise( SIGTERM );
		_exit( 0 ); // reached only if the default action was lost
	}

	int status = 0;
	ASSERT_EQ( child, waitpid( child, &status, 0 ) );
	EXPECT_TRUE( WIFSIGNALED( status ) ) << "the child exited " << WEXITSTATUS( status );
	EXPECT_EQ( SIGTERM, WTERMSIG( status ) );
}

TEST_F( ExitWatchTest, ArmingTwiceInstallsOnceSoASignalIsCountedOnce )
{
	Install( SIGHUP, PlainHandler );
	g_nPlainCalls = 0;
	ExitWatch_Arm( NULL );
	ExitWatch_Arm( NULL );

	raise( SIGHUP );

	EXPECT_EQ( 1, Fact( kExitFactSignalCount ) );
	EXPECT_EQ( 1, g_nPlainCalls );
}

TEST_F( ExitWatchTest, AFatalStopWritesItsRecordToTheArmedPath )
{
	char szTemplate[] = "/tmp/exit_watch_test.XXXXXX";
	ASSERT_NE( nullptr, mkdtemp( szTemplate ) );
	const std::string path = std::string( szTemplate ) + "/123.exit.json";

	const pid_t child = fork();
	ASSERT_GE( child, 0 );
	if ( child == 0 )
	{
		unsetenv( "SOURCESHARP_ASSERT_DIR" );
		ExitWatch_Arm( path.c_str() );
		SourceSharp_FatalReport( "ERROR", "the \"thing\" broke\n" );
	}

	int status = 0;
	ASSERT_EQ( child, waitpid( child, &status, 0 ) );
	EXPECT_EQ( int( kSourceSharpFatalExitCode ), WEXITSTATUS( status ) );

	const std::string record = ReadFile( path );
	EXPECT_NE( std::string::npos, record.find( "\"reason\":\"fatal\"" ) ) << record;
	EXPECT_NE( std::string::npos, record.find( "\"kind\":\"ERROR\"" ) ) << record;
	EXPECT_NE( std::string::npos, record.find( "the \\\"thing\\\" broke\\u000a" ) ) << record;

	unlink( path.c_str() );
	rmdir( szTemplate );
}

TEST_F( ExitWatchTest, AFatalStopWithNoArmedPathWritesNothing )
{
	char szTemplate[] = "/tmp/exit_watch_test.XXXXXX";
	ASSERT_NE( nullptr, mkdtemp( szTemplate ) );
	const std::string path = std::string( szTemplate ) + "/123.exit.json";

	const pid_t child = fork();
	ASSERT_GE( child, 0 );
	if ( child == 0 )
	{
		unsetenv( "SOURCESHARP_ASSERT_DIR" );
		SourceSharp_FatalReport( "ERROR", "unarmed" );
	}

	int status = 0;
	ASSERT_EQ( child, waitpid( child, &status, 0 ) );
	EXPECT_EQ( int( kSourceSharpFatalExitCode ), WEXITSTATUS( status ) );
	EXPECT_EQ( "", ReadFile( path ) );
	rmdir( szTemplate );
}

// The shutdown half: managed code disarms before the module can be unmapped,
// so a late signal or SDL event never reaches a recorder whose code is gone
// (the SIGTERM-exits-139 fault: SDL's watch list kept a freed OnSdlEvent).
TEST_F( ExitWatchTest, DisarmRestoresTheHandlerThatWasThere )
{
	Install( SIGHUP, PlainHandler );
	g_nPlainCalls = 0;
	ExitWatch_Arm( NULL );

	const int32_t nRemoved = ExitWatch_Disarm();
	EXPECT_TRUE( nRemoved & kExitWatchSignals );

	raise( SIGHUP ); // the handler that predates the arm is back on top
	EXPECT_EQ( 1, g_nPlainCalls );
	EXPECT_EQ( 0, Fact( kExitFactSignalCount ) ); // our recorder is off
}

TEST_F( ExitWatchTest, DisarmKeepsTheFactsAlreadyRecorded )
{
	ExitWatch_OnSignalForTest( SIGTERM, 4242, 1000 );
	ExitWatch_Disarm();

	// The managed record is written after the disarm; it still tells the truth.
	EXPECT_EQ( 1, Fact( kExitFactSignalCount ) );
	EXPECT_EQ( SIGTERM, Fact( kExitFactFirstSigno ) );
	EXPECT_EQ( 4242, Fact( kExitFactFirstPid ) );
}

TEST_F( ExitWatchTest, DisarmingTwiceRemovesOnce )
{
	ExitWatch_Arm( NULL );
	const int32_t nRemoved = ExitWatch_Disarm();
	EXPECT_NE( 0, nRemoved & kExitWatchSignals );
	EXPECT_EQ( 0, ExitWatch_Disarm() );
}

TEST_F( ExitWatchTest, ArmingAfterADisarmInstallsAgain )
{
	Install( SIGHUP, PlainHandler );
	ExitWatch_Arm( NULL );
	ExitWatch_Disarm();

	// A reload re-arms over the restored handler, not over the old snapshot.
	Install( SIGHUP, PlainHandler );
	g_nPlainCalls = 0;
	EXPECT_NE( 0, ExitWatch_Arm( NULL ) & kExitWatchSignals );

	raise( SIGHUP );
	EXPECT_EQ( 1, Fact( kExitFactSignalCount ) );
	EXPECT_EQ( 1, g_nPlainCalls );
}
