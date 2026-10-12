//========= SourceSharp — tests for what a failed managed init says ==========//
//
// The subject is managed_init_failure.h, which is the message a user sees when
// the .NET runtime does not come up, plus the one bit that decides whether
// CSourceSharpSystem::Init() runs at all.
//
// Both halves had a failure mode that LOOKED like success:
//
//   the message   there was none. Three failure paths each printed a Warning
//                 and returned true, and what the user actually saw was seven
//                 `Assertion Failed: 0` in usermessages.cpp — a file that was
//                 not wrong — followed by a segfault inside engine.so with no
//                 game-DLL frame. The cause line sat four thousand lines
//                 earlier in a log a default launch does not even write.
//
//   the gate      it was a bool set on ENTRY to Init(). A failed attempt still
//                 marked the system initialised, so the client's second call
//                 through IGameSystem::InitAllSystems() — the one place that
//                 could have returned false and stopped startup — silently did
//                 nothing. The gate reads as closed either way, which is why
//                 nothing noticed for as long as it did.
//
// Engine-free on purpose: this is snprintf and a bool, and it is checked here
// rather than by staring at a crash.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/managed_init_failure.h"

#include <cstring>
#include <string>

namespace
{

//-----------------------------------------------------------------------------
// A destination buffer whose SIZE the optimiser cannot work out.
//
// The truncation test below formats a five-hundred-character message into
// thirty-two bytes ON PURPOSE: truncating safely is the behaviour under test.
// GCC inlines the formatter, sees a constant array and constant strings, does
// the arithmetic and says so --
//
//   managed_init_failure.h:226: warning: '[SourceSharp] FATAL: the man...'
//   directive output truncated writing 38 bytes into a region of size 32
//
// -- and it is right about the arithmetic and wrong about it being a defect.
// The answer is not -Wno-format-truncation: the same diagnostic found a real
// bug in assert_stack.h's emitter, and a suppression wide enough to cover this
// covers that too. Same technique as assert_stack_test.cpp, for the same
// reason: a heap block whose length has been through a volatile. Every byte
// and every truncation is identical, at run time where the assertions can see
// it; only the deduction becomes impossible.
//
// The sentinel byte past the end is not padding for the warning's benefit — it
// is how the test checks nothing was written past the size it gave.
//-----------------------------------------------------------------------------
class OpaqueBuffer
{
public:
	explicit OpaqueBuffer( size_t cch )
		: m_cch( Hide( cch ) )
		, m_pch( new char[ Hide( cch ) + 1 ] )
	{
		memset( m_pch, 'X', m_cch + 1 );
	}

	~OpaqueBuffer() { delete [] m_pch; }

	char       *Data() const { return m_pch; }
	size_t      Size() const { return m_cch; }
	std::string Str()  const { return std::string( m_pch ); }

	bool SentinelIntact() const { return m_pch[ m_cch ] == 'X'; }

private:
	static size_t Hide( size_t cch ) { volatile size_t v = cch; return v; }

	OpaqueBuffer( const OpaqueBuffer & );
	OpaqueBuffer &operator=( const OpaqueBuffer & );

	size_t  m_cch;
	char   *m_pch;
};

std::string Format( const char *pSide, int nStage, int rc, const char *pDetail )
{
	char szBuf[2048];
	SourceSharpInit_Format( szBuf, sizeof( szBuf ), pSide, nStage, rc, pDetail );
	return std::string( szBuf );
}

bool Contains( const std::string &haystack, const char *needle )
{
	return haystack.find( needle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// The stage. Named, because -3 out of Acquire and -3 out of LoadSide are not
// the same problem and do not send the reader to the same place.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, EachStageHasItsOwnName )
{
	EXPECT_STREQ( "GetModBinDir", SourceSharpInit_StageName( kSourceSharpInitStage_ModBinDir ) );
	EXPECT_STREQ( "Acquire",      SourceSharpInit_StageName( kSourceSharpInitStage_Acquire ) );
	EXPECT_STREQ( "LoadSide",     SourceSharpInit_StageName( kSourceSharpInitStage_LoadSide ) );

	// All three distinct: a table that answered the same string twice would
	// name a stage and mean another.
	EXPECT_STRNE( SourceSharpInit_StageName( kSourceSharpInitStage_ModBinDir ),
	              SourceSharpInit_StageName( kSourceSharpInitStage_Acquire ) );
	EXPECT_STRNE( SourceSharpInit_StageName( kSourceSharpInitStage_Acquire ),
	              SourceSharpInit_StageName( kSourceSharpInitStage_LoadSide ) );
}

//-----------------------------------------------------------------------------
// A stage this build does not know must still answer a string. This runs on the
// way to Error(), and faulting while reporting a fatal error loses the report.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, AnUnknownStageStillAnswersAString )
{
	const char *pName = SourceSharpInit_StageName( kSourceSharpInitStage_Count );
	ASSERT_TRUE( pName != NULL );
	EXPECT_GT( strlen( pName ), 0u );

	pName = SourceSharpInit_StageName( -12345 );
	ASSERT_TRUE( pName != NULL );
	EXPECT_GT( strlen( pName ), 0u );
}

//-----------------------------------------------------------------------------
// Every result code the HOST can return has a cause of its own.
//
// Driven off the host's enum rather than a list restated here: a code added to
// sourcesharp.h that this table does not know would otherwise print the
// catch-all confidently, and nothing would say so.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, EveryHostResultCodeHasItsOwnCause )
{
	const int rgCodes[] = {
		SOURCESHARP_ERR_HOSTFXR_LOAD,
		SOURCESHARP_ERR_HOSTFXR_SYM,
		SOURCESHARP_ERR_RUNTIME_INIT,
		SOURCESHARP_ERR_DELEGATE,
		SOURCESHARP_ERR_BOOTSTRAP,
		SOURCESHARP_ERR_ABI_MISMATCH,
		SOURCESHARP_ERR_NOT_INITIALIZED,
	};
	const int cCodes = (int)( sizeof( rgCodes ) / sizeof( rgCodes[0] ) );

	// The codes run -1..-7 with no gaps, so this list being the whole set is a
	// fact the test can check rather than one it has to be trusted about.
	EXPECT_EQ( -cCodes, SOURCESHARP_ERR_NOT_INITIALIZED );

	const char *pUnknown = SourceSharpInit_CauseText( -9999 );

	for ( int i = 0; i < cCodes; i++ )
	{
		const char *pCause = SourceSharpInit_CauseText( rgCodes[i] );
		ASSERT_TRUE( pCause != NULL ) << "code " << rgCodes[i];
		EXPECT_GT( strlen( pCause ), 0u ) << "code " << rgCodes[i];
		EXPECT_STRNE( pUnknown, pCause )
			<< "code " << rgCodes[i] << " falls through to the catch-all";

		for ( int j = 0; j < i; j++ )
			EXPECT_STRNE( SourceSharpInit_CauseText( rgCodes[j] ), pCause )
				<< "codes " << rgCodes[j] << " and " << rgCodes[i] << " share a cause";
	}
}

//-----------------------------------------------------------------------------
// The two remedies that are worth the whole file: a missing directory that git
// does not carry. Neither is guessable from a hostfxr hresult, and one of them
// used to present as a SIGSEGV inside engine.so.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, AMissingRuntimeDirectorySendsTheReaderToWorktreeSetup )
{
	std::string s = Format( "client", kSourceSharpInitStage_Acquire,
		SOURCESHARP_ERR_HOSTFXR_LOAD,
		"no bundled libhostfxr under /x/game/mod_sharp/bin/dotnet/host/fxr" );

	EXPECT_TRUE( Contains( s, "bin/dotnet" ) ) << s;
	EXPECT_TRUE( Contains( s, "worktree-setup" ) ) << s;
}

TEST( ManagedInitFailure, MissingAssembliesSendTheReaderToTheBuild )
{
	std::string s = Format( "server", kSourceSharpInitStage_Acquire,
		SOURCESHARP_ERR_RUNTIME_INIT,
		"hostfxr_initialize_for_runtime_config failed (0x80008093)" );

	EXPECT_TRUE( Contains( s, "bin/managed" ) ) << s;
	EXPECT_TRUE( Contains( s, "make debug" ) ) << s;
}

//-----------------------------------------------------------------------------
// The ModBinDir stage has no host call behind it, so its remedy must not be
// decided by the code — which is always zero there, and zero is OK.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, TheModBinDirStageHasItsOwnRemedyWhateverTheCode )
{
	const char *pExpected = SourceSharpInit_Remedy( kSourceSharpInitStage_ModBinDir, 0 );
	EXPECT_TRUE( strstr( pExpected, "-game" ) != NULL ) << pExpected;

	// Same answer for every code, because the code means nothing at this stage.
	for ( int rc = 0; rc >= -8; rc-- )
		EXPECT_STREQ( pExpected, SourceSharpInit_Remedy( kSourceSharpInitStage_ModBinDir, rc ) );

	// And it is NOT the answer the later stages give for the same code, which
	// is what would happen if the stage were ignored.
	EXPECT_STRNE( pExpected, SourceSharpInit_Remedy( kSourceSharpInitStage_Acquire, 0 ) );
}

//-----------------------------------------------------------------------------
// The whole message. Four facts have to survive to the reader: which side, which
// call, which code, and what the host said.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, TheMessageCarriesSideStageCodeAndDetail )
{
	// The detail deliberately does NOT contain the word "LoadSide", though the
	// real one does ("managed LoadSide returned -3"). With that detail the
	// stage check finds its needle in the DETAIL and passes with the stage line
	// deleted — measured, not hypothesised.
	std::string s = Format( "server", kSourceSharpInitStage_LoadSide,
		SOURCESHARP_ERR_BOOTSTRAP, "the bootstrap refused" );

	EXPECT_TRUE( Contains( s, "server" ) ) << s;
	EXPECT_TRUE( Contains( s, "LoadSide" ) ) << s;
	EXPECT_TRUE( Contains( s, "-5" ) ) << s;
	EXPECT_TRUE( Contains( s, "the bootstrap refused" ) ) << s;
	EXPECT_TRUE( Contains( s, "[SourceSharp]" ) ) << s;
}

//-----------------------------------------------------------------------------
// A stage with no host call behind it must not print a code.
//
// GetModBinDir is a local check, not a call into the host, so its code is
// always zero — and "code : 0 — no error reported" printed under the word FATAL
// reads as a contradiction and sends the reader hunting a code that does not
// exist. The stages that DO have one must still print it.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, AStageWithNoCodePrintsNoCodeLine )
{
	std::string none = Format( "client", kSourceSharpInitStage_ModBinDir, 0, NULL );
	EXPECT_FALSE( Contains( none, "code  :" ) ) << none;

	// The remedy, which is the point of the message, survives.
	EXPECT_TRUE( Contains( none, "-game" ) ) << none;

	std::string some = Format( "client", kSourceSharpInitStage_Acquire,
		SOURCESHARP_ERR_HOSTFXR_LOAD, "no bundled libhostfxr" );
	EXPECT_TRUE( Contains( some, "code  :" ) ) << some;
	EXPECT_TRUE( Contains( some, "-1" ) ) << some;
}

TEST( ManagedInitFailure, TheTwoSidesAreDistinguishable )
{
	const char *pDetail = "hostfxr_initialize_for_runtime_config failed";

	std::string sv = Format( "server", kSourceSharpInitStage_Acquire,
		SOURCESHARP_ERR_RUNTIME_INIT, pDetail );
	std::string cl = Format( "client", kSourceSharpInitStage_Acquire,
		SOURCESHARP_ERR_RUNTIME_INIT, pDetail );

	EXPECT_NE( sv, cl );
	EXPECT_TRUE( Contains( sv, "server" ) ) << sv;
	EXPECT_TRUE( Contains( cl, "client" ) ) << cl;
}

//-----------------------------------------------------------------------------
// A NULL detail is normal, not exceptional: SourceSharp_LastError() is empty
// when nothing set it, and the ModBinDir stage never calls the host at all.
// Formatting a null through %s is undefined behaviour on the way to a fatal
// error report, which is the worst possible place for it.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, ANullOrEmptyDetailIsSubstituted )
{
	std::string sNull = Format( "client", kSourceSharpInitStage_ModBinDir, 0, NULL );
	std::string sEmpty = Format( "client", kSourceSharpInitStage_ModBinDir, 0, "" );

	EXPECT_EQ( sNull, sEmpty );
	EXPECT_FALSE( Contains( sNull, "(null)" ) ) << sNull;
	EXPECT_TRUE( Contains( sNull, "none reported" ) ) << sNull;
}

TEST( ManagedInitFailure, ANullSideIsSubstituted )
{
	std::string s = Format( NULL, kSourceSharpInitStage_Acquire, -1, "x" );
	EXPECT_FALSE( Contains( s, "(null)" ) ) << s;
	EXPECT_GT( s.size(), 0u );
}

//-----------------------------------------------------------------------------
// The buffer the caller uses must fit a REAL failure — the detail is an
// absolute path, and a truncated message would cut the remedy off the end,
// which is the line worth the most.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, TwoKilobytesFitsARealFailure )
{
	const char *pRealDetail =
		"hostfxr_initialize_for_runtime_config failed (0x80008093) for "
		"/srv/checkouts/source-sdk-2013/.claude/worktrees/agent-000000000000000/"
		"base/game/mod_sharp/bin/managed/SourceSharp.Bootstrap.runtimeconfig.json";

	char szBuf[2048];
	int nWanted = SourceSharpInit_Format( szBuf, sizeof( szBuf ), "server",
		kSourceSharpInitStage_Acquire, SOURCESHARP_ERR_RUNTIME_INIT, pRealDetail );

	EXPECT_GT( nWanted, 0 );
	EXPECT_LT( (size_t)nWanted, sizeof( szBuf ) )
		<< "the message is longer than the buffer sourcesharp_gamesystem.cpp gives it";

	// The remedy is the tail; if anything is lost, it is this.
	EXPECT_TRUE( Contains( std::string( szBuf ), "make debug" ) ) << szBuf;
}

//-----------------------------------------------------------------------------
// A small buffer truncates instead of overrunning, and says how much it wanted.
//-----------------------------------------------------------------------------
TEST( ManagedInitFailure, ASmallBufferTruncatesSafely )
{
	OpaqueBuffer buf( 32 );

	int nWanted = SourceSharpInit_Format( buf.Data(), buf.Size(), "server",
		kSourceSharpInitStage_Acquire, SOURCESHARP_ERR_RUNTIME_INIT, "detail" );

	// The WANTED length, not the written one: a caller cannot tell a complete
	// message from a cut-off one otherwise.
	EXPECT_GT( (size_t)nWanted, buf.Size() );
	EXPECT_EQ( buf.Size() - 1, buf.Str().size() );
	EXPECT_TRUE( buf.SentinelIntact() ) << "wrote past the size it was given";
}

TEST( ManagedInitFailure, AZeroSizedBufferIsRefused )
{
	EXPECT_EQ( 0, SourceSharpInit_Format( NULL, 100, "server", 0, -1, "d" ) );

	OpaqueBuffer buf( 0 );
	EXPECT_EQ( 0, SourceSharpInit_Format( buf.Data(), buf.Size(), "server", 0, -1, "d" ) );
	EXPECT_TRUE( buf.SentinelIntact() );   // untouched
}

//=============================================================================//
// The gate.
//=============================================================================//

TEST( ManagedInitGate, TheFirstCallRuns )
{
	CSourceSharpInitGate gate;
	EXPECT_TRUE( gate.ShouldRun() );
	EXPECT_FALSE( gate.HasSucceeded() );
}

TEST( ManagedInitGate, ASecondCallAfterSuccessIsANoOp )
{
	CSourceSharpInitGate gate;
	ASSERT_TRUE( gate.ShouldRun() );

	gate.MarkSucceeded();

	// This is the client's InitAllSystems() call, a few lines after
	// SourceSharp_ClientEarlyInit(). It must do NOTHING: running the body twice
	// would Acquire a second runtime, LoadSide into a second load context and
	// double-register every C# console command.
	EXPECT_FALSE( gate.ShouldRun() );
	EXPECT_TRUE( gate.HasSucceeded() );

	// And a third, and a fourth.
	EXPECT_FALSE( gate.ShouldRun() );
	EXPECT_FALSE( gate.ShouldRun() );
}

//-----------------------------------------------------------------------------
// ONLY MarkSucceeded closes it.
//
// This is the half of the bug that lives in the class: nothing an attempt does
// short of finishing may close the gate. Reading it does not close it, asking
// twice does not close it, and a gate that has never been marked is open no
// matter how many times Init() has been through the body.
//
// The other half lives at the call site — that MarkSucceeded is written past
// every failure point in CSourceSharpSystem::Init() and not at the top — and no
// assertion in here can see that. It is pinned by
// ManagedInitFatalTests.cs, which reads the function.
//-----------------------------------------------------------------------------
TEST( ManagedInitGate, NothingButMarkSucceededClosesIt )
{
	CSourceSharpInitGate gate;

	for ( int i = 0; i < 5; i++ )
	{
		EXPECT_TRUE( gate.ShouldRun() )
			<< "asking closed the gate on pass " << i;
		EXPECT_FALSE( gate.HasSucceeded() );
	}

	gate.MarkSucceeded();
	EXPECT_FALSE( gate.ShouldRun() );
}

TEST( ManagedInitGate, ShutdownReopensIt )
{
	CSourceSharpInitGate gate;
	gate.MarkSucceeded();
	ASSERT_FALSE( gate.ShouldRun() );

	gate.Reset();

	EXPECT_TRUE( gate.ShouldRun() );
	EXPECT_FALSE( gate.HasSucceeded() );
}

TEST( ManagedInitGate, TwoGatesAreIndependent )
{
	// client.so and server.so each compile their own copy of the system, and
	// each has its own gate. One side coming up must not close the other's.
	CSourceSharpInitGate server, client;

	server.MarkSucceeded();

	EXPECT_FALSE( server.ShouldRun() );
	EXPECT_TRUE( client.ShouldRun() );
}

} // namespace
