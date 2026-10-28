//========= SourceSharp — client.so's random stream is vstdlib's, draw for draw //
//
// vstdlib_random_port.cpp replaced libvstdlib's RandomInt in server.so
// (plan_server_novalve.md P4; the P0 random probe ruled that callers need the
// algorithm, not the shared instance). The server stopped drawing at all and
// the file moved to client.so (plan_client_novalve C9, 2026-10-12), which also
// seeds the stream and draws floats (util_shared_library, viewpostprocess,
// particles.a): RandomSeed and RandomFloat are back, held here again.
// vstdlib's source is not in the SDK, so the oracle is THE GAME'S OWN
// libvstdlib.so, loaded here beside the port: both start unseeded and must
// answer the same values.
//
// The game's copy is found as engine_heap_oracle_test finds libtier0.so
// ($SOURCESHARP_ENGINE_VSTDLIB, from Steam's library folders); a missing one
// fails, naming the fix. This binary links no vstdlib: every Random* call it
// makes directly is the port.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../../native/ss_ci_skip.h"

#include "vstdlib/random.h"

#include <dlfcn.h>
#include <limits.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <vector>

namespace
{
struct Vstdlib_t
{
	void ( *Seed )( int );
	int ( *Int )( int, int );
	float ( *Float )( float, float );
};

uint32_t Bits( float f )
{
	uint32_t u;
	memcpy( &u, &f, sizeof( u ) );
	return u;
}

// Loaded once and kept: its global stream is the other half of every fact.
const Vstdlib_t *Game()
{
	static Vstdlib_t s_Fns = {};
	static bool s_bTried = false;
	if ( !s_bTried )
	{
		s_bTried = true;
		const char *pPath = getenv( "SOURCESHARP_ENGINE_VSTDLIB" );
		if ( !pPath || !*pPath )
			return nullptr;
		void *h = dlopen( pPath, RTLD_NOW | RTLD_LOCAL );
		if ( !h )
			return nullptr;
		s_Fns.Seed = (void ( * )( int ))dlsym( h, "RandomSeed" );
		s_Fns.Int = (int ( * )( int, int ))dlsym( h, "RandomInt" );
		s_Fns.Float = (float ( * )( float, float ))dlsym( h, "RandomFloat" );
	}
	return s_Fns.Seed && s_Fns.Int && s_Fns.Float ? &s_Fns : nullptr;
}

#define REQUIRE_GAME()                                                                          \
	const Vstdlib_t *pGame = Game();                                                            \
	ASSERT_NE( nullptr, pGame ) << "no game libvstdlib.so: set SOURCESHARP_ENGINE_VSTDLIB, or " \
								   "install Source SDK Base 2013 Multiplayer (dlerror: "        \
								<< ( dlerror() ? dlerror() : "none" ) << ")"
}

// FIRST, while neither stream has been seeded: vstdlib's global stream starts
// as if seeded with 0, and so must the port's.
TEST( VstdlibRandomOracle, AnUnseededStreamStartsWhereTheGamesDoes )
{
	SS_SKIP_ON_CI(
		"needs the game's own libraries from a Source SDK Base 2013 Multiplayer install, which a runner has not" );
	REQUIRE_GAME();
	for ( int i = 0; i < 200; ++i )
		ASSERT_EQ( pGame->Int( 0, 1000000 ), RandomInt( 0, 1000000 ) ) << "draw " << i;
}

// The server draws only RandomInt, from the unseeded stream: RandomSeed,
// RandomFloat and RandomFloatExp left vstdlib_random_port.cpp in the server gc
// cull (2026-10-01; no server object called them). The two streams stay in step
// from the fact above, so every kind of integer draw is compared without
// seeding.
TEST( VstdlibRandomOracle, EveryKindOfIntDrawIsTheGames )
{
	SS_SKIP_ON_CI(
		"needs the game's own libraries from a Source SDK Base 2013 Multiplayer install, which a runner has not" );
	REQUIRE_GAME();
	long nCompared = 0;
	for ( int i = 0; i < 8000; ++i )
	{
		switch ( i % 4 )
		{
			// Just over 2^30 values: about half the draws are rejected and redrawn,
			// so this is the case that sees the rejection loop.
			case 3:
				ASSERT_EQ( pGame->Int( 0, 0x40000000 ), RandomInt( 0, 0x40000000 ) ) << i;
				break;
			case 0:
				ASSERT_EQ( pGame->Int( 0, 0x7ffffffe ), RandomInt( 0, 0x7ffffffe ) ) << i;
				break;
			case 1:
				ASSERT_EQ( pGame->Int( -50, 50 ), RandomInt( -50, 50 ) ) << i;
				break;
			case 2:
				ASSERT_EQ( pGame->Int( 0, 2 ), RandomInt( 0, 2 ) ) << i;
				break;
		}
		++nCompared;
	}
	EXPECT_EQ( 8000, nCompared );
}

TEST( VstdlibRandomOracle, DegenerateRangesAnswerAsTheGamesDo )
{
	SS_SKIP_ON_CI(
		"needs the game's own libraries from a Source SDK Base 2013 Multiplayer install, which a runner has not" );
	REQUIRE_GAME();
	EXPECT_EQ( pGame->Int( 5, 5 ), RandomInt( 5, 5 ) );
	EXPECT_EQ( pGame->Int( 5, 4 ), RandomInt( 5, 4 ) );
	EXPECT_EQ( pGame->Int( INT_MIN, INT_MAX ), RandomInt( INT_MIN, INT_MAX ) );
	// And the streams are still in step after them.
	EXPECT_EQ( pGame->Int( 0, 1000 ), RandomInt( 0, 1000 ) );
}

// After the unseeded facts: seeding both streams alike, every kind of draw the
// client makes stays in step, floats compared bit for bit.
TEST( VstdlibRandomOracle, EverySeedGivesTheGamesSequenceOfIntsAndFloats )
{
	SS_SKIP_ON_CI(
		"needs the game's own libraries from a Source SDK Base 2013 Multiplayer install, which a runner has not" );
	REQUIRE_GAME();
	const int seeds[] = { 0, 1, 2, 42, -7, 12345, 0x1234567, INT_MAX, INT_MIN + 1, -1 };
	long nCompared = 0;
	for ( int seed : seeds )
	{
		pGame->Seed( seed );
		RandomSeed( seed );
		for ( int i = 0; i < 2000; ++i )
		{
			switch ( i % 6 )
			{
				// Just over 2^30 values: about half the draws are rejected and redrawn.
				case 5:
					ASSERT_EQ( pGame->Int( 0, 0x40000000 ), RandomInt( 0, 0x40000000 ) ) << seed << "/" << i;
					break;
				case 0:
					ASSERT_EQ( pGame->Int( 0, 0x7ffffffe ), RandomInt( 0, 0x7ffffffe ) ) << seed << "/" << i;
					break;
				case 1:
					ASSERT_EQ( pGame->Int( -50, 50 ), RandomInt( -50, 50 ) ) << seed << "/" << i;
					break;
				case 2:
					ASSERT_EQ( pGame->Int( 0, 2 ), RandomInt( 0, 2 ) ) << seed << "/" << i;
					break;
				case 3:
					ASSERT_EQ( Bits( pGame->Float( 0.0f, 1.0f ) ), Bits( RandomFloat( 0.0f, 1.0f ) ) )
						<< seed << "/" << i;
					break;
				case 4:
					ASSERT_EQ( Bits( pGame->Float( -3.5f, 9.25f ) ), Bits( RandomFloat( -3.5f, 9.25f ) ) )
						<< seed << "/" << i;
					break;
			}
			++nCompared;
		}
	}
	EXPECT_EQ( 20000, nCompared );
}
