//========= SourceSharp — server.so's spew reaches the engine's tier0 slot ====//
//
// tier0_spew_port.cpp's default spew hands each line to the spew function
// current in the libtier0.so the engine loaded: that function writes the
// console and console.log, and ICvar::ConsolePrintf reaches only the console
// (a live gate lost every native line from console.log that way). This binary
// links no tier0; it loads fake_tier0/libtier0.so, whose slot the facts fill.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "tier0_spew_port.h"

#include "tier0/dbg.h"
#include "tier1/convar.h"

#include "recording_cvar.h"

#include <dlfcn.h>
#include <string>
#include <vector>

namespace
{
struct SpewRecord
{
	SpewType_t type;
	std::string text;
};

std::vector<SpewRecord> g_EngineA;
std::vector<SpewRecord> g_EngineB;

SpewRetval_t EngineA( SpewType_t type, const tchar *pText )
{
	g_EngineA.push_back( SpewRecord{ type, pText } );
	return SPEW_CONTINUE;
}

SpewRetval_t EngineB( SpewType_t type, const tchar *pText )
{
	g_EngineB.push_back( SpewRecord{ type, pText } );
	return SPEW_CONTINUE;
}

// tier0's answer to an assert or an error, which the port must not act on.
SpewRetval_t EngineThatAborts( SpewType_t type, const tchar *pText )
{
	g_EngineA.push_back( SpewRecord{ type, pText } );
	return type == SPEW_MESSAGE ? SPEW_CONTINUE : SPEW_ABORT;
}

// A slot that leads back into server.so's own.
SpewRetval_t EngineThatCallsBack( SpewType_t type, const tchar *pText )
{
	g_EngineA.push_back( SpewRecord{ type, pText } );
	return GetSpewOutputFunc()( type, pText );
}

using SetSpew_t = void ( * )( SpewOutputFunc_t );

class Tier0SpewForward : public ::testing::Test
{
protected:
	static SetSpew_t s_pSetEngineSpew;
	CRecordingCvar m_Cvar;

	// Loaded once and never closed: the port keeps the slot getter it found.
	static void SetUpTestSuite()
	{
		void *h = dlopen( SS_FAKE_TIER0, RTLD_NOW | RTLD_LOCAL );
		ASSERT_NE( nullptr, h ) << dlerror();
		s_pSetEngineSpew = (SetSpew_t)dlsym( h, "SpewOutputFunc" );
		ASSERT_NE( nullptr, s_pSetEngineSpew );
	}

	void SetUp() override
	{
		ASSERT_NE( nullptr, s_pSetEngineSpew );
		SpewOutputFunc( NULL ); // server.so's slot: the default
		s_pSetEngineSpew( EngineA );
		Tier0_ConnectConsole( &m_Cvar );
		g_EngineA.clear();
		g_EngineB.clear();
	}

	void TearDown() override
	{
		s_pSetEngineSpew( nullptr );
		Tier0_ConnectConsole( nullptr );
	}
};

SetSpew_t Tier0SpewForward::s_pSetEngineSpew = nullptr;
}

TEST_F( Tier0SpewForward, MsgGoesToTheEngineSlotAndNotToICvar )
{
	Msg( "hello %d\n", 7 );

	ASSERT_EQ( 1u, g_EngineA.size() );
	EXPECT_EQ( SPEW_MESSAGE, g_EngineA[0].type );
	EXPECT_EQ( "hello 7\n", g_EngineA[0].text );
	EXPECT_TRUE( m_Cvar.m_Printed.empty() );
}

TEST_F( Tier0SpewForward, AWarningIsHandedOnAsAWarning )
{
	Warning( "careful\n" );

	ASSERT_EQ( 1u, g_EngineA.size() );
	EXPECT_EQ( SPEW_WARNING, g_EngineA[0].type );
	EXPECT_EQ( "careful\n", g_EngineA[0].text );
}

TEST_F( Tier0SpewForward, AnAssertIsHandedOnAsAWarningSoTheEngineCannotDecideIt )
{
	s_pSetEngineSpew( EngineThatAborts );

	_SpewInfo( SPEW_ASSERT, "file.cpp", 12 );
	const SpewRetval_t ret = _SpewMessage( "%s", "x > 0" );

	ASSERT_EQ( 1u, g_EngineA.size() );
	EXPECT_EQ( SPEW_WARNING, g_EngineA[0].type );
	EXPECT_EQ( "file.cpp (12) : x > 0\n", g_EngineA[0].text );
	EXPECT_EQ( SPEW_CONTINUE, ret ) << "the engine's SPEW_ABORT came back to the assert";
}

TEST_F( Tier0SpewForward, TheEngineSlotIsReadOnEveryLine )
{
	Msg( "one\n" );
	s_pSetEngineSpew( EngineB );
	Msg( "two\n" );

	ASSERT_EQ( 1u, g_EngineA.size() );
	EXPECT_EQ( "one\n", g_EngineA[0].text );
	ASSERT_EQ( 1u, g_EngineB.size() );
	EXPECT_EQ( "two\n", g_EngineB[0].text );
}

TEST_F( Tier0SpewForward, AnEmptyEngineSlotFallsBackToICvar )
{
	s_pSetEngineSpew( nullptr );
	Msg( "to the console\n" );

	ASSERT_EQ( 1u, m_Cvar.m_Printed.size() );
	EXPECT_EQ( "to the console\n", m_Cvar.m_Printed[0].text );
}

TEST_F( Tier0SpewForward, LinesBeforeICvarExistsStillReachTheEngineAtOnce )
{
	Tier0_ConnectConsole( nullptr );
	Msg( "early\n" );

	ASSERT_EQ( 1u, g_EngineA.size() );
	EXPECT_EQ( "early\n", g_EngineA[0].text );
}

TEST_F( Tier0SpewForward, AnEngineSlotThatLeadsBackHereWritesOnceToICvarInstead )
{
	s_pSetEngineSpew( EngineThatCallsBack );
	Msg( "loop\n" );

	ASSERT_EQ( 1u, g_EngineA.size() );
	ASSERT_EQ( 1u, m_Cvar.m_Printed.size() );
	EXPECT_EQ( "loop\n", m_Cvar.m_Printed[0].text );
}

TEST_F( Tier0SpewForward, ServerSlotHooksStillSeeEveryLineFirst )
{
	// shell_diagnostics installs into server.so's slot and chains to what was
	// there: the default, which hands on to the engine.
	static SpewOutputFunc_t s_pPrevious;
	s_pPrevious = GetSpewOutputFunc();
	static int s_nSeen;
	s_nSeen = 0;
	SpewOutputFunc(
		[]( SpewType_t type, const tchar *pText )
		{
			++s_nSeen;
			return s_pPrevious( type, pText );
		} );

	Msg( "through the hook\n" );

	EXPECT_EQ( 1, s_nSeen );
	ASSERT_EQ( 1u, g_EngineA.size() );
	EXPECT_EQ( "through the hook\n", g_EngineA[0].text );
}
