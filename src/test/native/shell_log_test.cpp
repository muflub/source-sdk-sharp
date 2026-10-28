// CShellLog, the server's: its verbosity before and after tier0 is up, the free calls the
// trace macros make forwarding to the DLL's log, and what they answer with none.

#include "gtest/gtest.h"

#include "shell_log.h"
#include "shell_diagnostics.h"

#include <string>

TEST( ShellLog, BeforeTier0IsUpTheVerbosityIsOne )
{
	CShellLog log;
	EXPECT_EQ( 1, log.Verbosity() );
	EXPECT_EQ( 1, SourceSharp_GetVerbosity() );
}

TEST( ShellLog, ReadyWithTheManagedConVarUnregisteredItStaysOne )
{
	// The convar is declared in managed code; until it is, the earliest lines still print.
	CShellLog log;
	log.MarkReady();
	EXPECT_EQ( 1, log.Verbosity() );
}

TEST( ShellLog, TheFreeCallsReachTheLiveLogAndItsDefaultsOnceItIsGone )
{
	{
		CShellLog log;
		bool bLogged = false;
		testing::internal::CaptureStdout();
		SourceSharp_LogStub( "Probe", &bLogged );
		const std::string out = testing::internal::GetCapturedStdout();

		EXPECT_TRUE( bLogged );
		EXPECT_NE( std::string::npos, out.find( "STUB Probe (server)" ) ) << out;
	}

	EXPECT_EQ( 1, SourceSharp_GetVerbosity() ) << "no log: the default, not a dangling read";
}

TEST( ShellLog, AStubIsPrintedOnceAtVerbosityOne )
{
	CShellLog log;
	bool bLogged = false;

	testing::internal::CaptureStdout();
	log.Stub( "Twice", &bLogged );
	log.Stub( "Twice", &bLogged );
	const std::string out = testing::internal::GetCapturedStdout();

	EXPECT_EQ( out.find( "STUB Twice" ), out.rfind( "STUB Twice" ) ) << out;
	EXPECT_NE( std::string::npos, out.find( "STUB Twice" ) );
}

TEST( ShellLog, TheNotShippedReadoutIsTheLogsOwnLedger )
{
	CShellLog log;
	EXPECT_EQ( 0, log.NotShipped().Total() );
}

TEST( ShellLog, OnceTheLiveLogIsReadyAFreePrintGoesToTier0NotStdout )
{
	CShellLog log;
	log.MarkReady();

	testing::internal::CaptureStdout();
	SourceSharp_DiagPrint( "[SourceSharp] through the log\n" );
	const std::string out = testing::internal::GetCapturedStdout();

	EXPECT_EQ( std::string::npos, out.find( "through the log" ) )
		<< "stdout is the fallback for before tier0 is up, or for no log at all";
}
