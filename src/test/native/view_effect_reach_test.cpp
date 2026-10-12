//========= SourceSharp — the view-effect reach rules ========================//
//
// Screen shake, screen fade and view punch reached NOBODY for the whole of this
// port: the three loops in util.cpp resolved each slot as a CBasePlayer, got
// NULL, and skipped it. Fixing the subject makes every rule below run for the
// first time, on every explosion, in one commit — so they are pinned here
// first.
//
// The shake rules that stood here went with view_effect_reach.h's (lane HC,
// 2026-10-04): C#'s ScreenEffects decides the shake, and ScreenEffectsTests
// carries every arm they had. What is left is the view punch.
//
// These are engine-free by construction. Nothing in view_effect_reach.h takes
// an entity, and that is what lets a rule that only ever runs inside a live
// server be checked without one.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <fstream>
#include <sstream>
#include <string>

#include "../game/shared/view_effect_reach.h"

using namespace ViewEffectReach;

//-----------------------------------------------------------------------------
// The punch falloff. It scales an angle.
//-----------------------------------------------------------------------------
TEST( PunchScale, NoRadiusPunchesEveryoneUnscaled )
{
	EXPECT_FLOAT_EQ( 1.0f, PunchScale( 0.0f, 0.0f ) );
	EXPECT_FLOAT_EQ( 1.0f, PunchScale( 99999.0f, 0.0f ) );
}

TEST( PunchScale, FallsLinearlyToZeroAtTheEdge )
{
	EXPECT_FLOAT_EQ( 1.00f, PunchScale(   0.0f, 256.0f ) );
	EXPECT_FLOAT_EQ( 0.75f, PunchScale(  64.0f, 256.0f ) );
	EXPECT_FLOAT_EQ( 0.50f, PunchScale( 128.0f, 256.0f ) );
	EXPECT_FLOAT_EQ( 0.00f, PunchScale( 256.0f, 256.0f ) );
}

TEST( PunchScale, PastTheEdgeIsOutOfRange )
{
	EXPECT_TRUE ( InRange( PunchScale( 256.0f, 256.0f ) ) );
	EXPECT_FALSE( InRange( PunchScale( 256.5f, 256.0f ) ) );
}

//-----------------------------------------------------------------------------
// The on-ground gate: a punch checks the ground every time.
//-----------------------------------------------------------------------------
TEST( GroundGate, AGroundedPlayerIsAlwaysPunched )
{
	EXPECT_TRUE( PunchReachesPlayer( true, false ) );
}

TEST( GroundGate, AirborneNeedsTheAirFlagForAPunch )
{
	EXPECT_FALSE( PunchReachesPlayer( false, false ) );
	EXPECT_TRUE ( PunchReachesPlayer( false, true ) );
}

//=============================================================================//
// WHERE THE WALK LIVES
//
// plan_entity.md step 4e (Utilities). The rules above are engine-free, which is
// what lets them be tested; the WALK that feeds them -- which slot holds a
// player, whether they are on the ground, where their centre is, and the user
// message that carries the result -- is the other half, and step 4e moved that
// half out of game/server/util.cpp into
// sourcesharp/game/server/shell_scripted.cpp (shell_vieweffects.cpp until the
// shake and the fade left for C#).
//
// Pinned here because nothing else in the tree names these four, so the family
// could be relocated, duplicated or dropped with no test going red. The failure
// that matters for now is the reach rules migrating into the walk beside it,
// which would end the split this suite depends on. (A body typed back into
// util.cpp was the other; util.cpp is no longer compiled and has left the
// source tree, so that check was culled.)
//
// AND ONE DELETION IS PINNED AS A DELETION. UTIL_ScreenShakeObject is the one
// member of the family that read a parent chain off an entity pointer, and it
// has no caller this mod compiles. It is gone. Its DECLARATION stayed in
// game/server/util.h -- step 4a's treatment of the ten sentence functions --
// and was asserted too, until util.h itself became compiled by nothing in
// plan_server_novalve.md P5 and left the farm in P6; that half is retired.
//=============================================================================//

namespace
{

std::string SlurpForEffects( const char *pszPath )
{
	std::ifstream f( pszPath );
	EXPECT_TRUE( f.good() ) << "cannot open " << pszPath;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

size_t CountForEffects( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t i = hay.find( needle ); i != std::string::npos;
	      i = hay.find( needle, i + needle.size() ) )
	{
		n++;
	}
	return n;
}

const char *const kEffectsFile = "sourcesharp/game/server/shell_scripted.cpp";

const char *const kEffectDefinitions[] = {
	"void UTIL_ViewPunch( const Vector &center,",
	// UTIL_ScreenFade( IGameEntity *, ... ) WENT IN THE SERVER GC CULL
	// (2026-10-01): nothing called the one-entity fade, and server.so's
	// --print-gc-sections link dropped it. Asserted absent below, under both
	// spellings.
	"struct ViewEffectPlayer_t",
};

// THE SHAKE AND THE FADE LEFT FOR C# (user-message payload model,
// 2026-10-04): they are user messages, and SourceSharp.Server's ScreenEffects
// decides their reach and sends both payloads. The native walks had no caller
// (the IShimEffectsServer ScreenShake/ScreenFade slots were never called from
// managed code). A body typed back here would be a second sender of a message
// whose layout C# owns, so each is asserted absent, and so is any user
// message write.
const char *const kRetiredToManaged[] = {
	"void UTIL_ScreenShake(",
	"void UTIL_ScreenFadeAll(",
	"void UTIL_ScreenFadeSlot(",
	"UserMessageBegin(",
};
const size_t kNumEffects = sizeof( kEffectDefinitions ) / sizeof( kEffectDefinitions[ 0 ] );

// The spelling that went. Asserted absent below, because "the walk is in the
// shim" is satisfied by a tree that grew a SECOND overload under the old name:
// it would link, and the linker rather than the port would pick which fade
// runs. The name does not exist in this build, so a hit is a revert. (It was
// also asserted absent from game/server/util.cpp until that file left the
// source tree.)
const char *const kRetiredSpelling = "UTIL_ScreenFade( CBaseEntity *pEntity,";

} // anonymous namespace

TEST( ViewEffectWalk, EveryMemberIsDefinedInTheShimFile )
{
	const std::string eff = SlurpForEffects( kEffectsFile );
	ASSERT_FALSE( eff.empty() ) << kEffectsFile << " did not load";

	for ( size_t i = 0; i < kNumEffects; i++ )
	{
		EXPECT_EQ( 1u, CountForEffects( eff, kEffectDefinitions[ i ] ) )
			<< kEffectDefinitions[ i ] << " is not defined exactly once in "
			<< kEffectsFile;
	}

	for ( const char *pszGone : kRetiredToManaged )
	{
		EXPECT_EQ( 0u, CountForEffects( eff, pszGone ) )
			<< kEffectsFile << " has " << pszGone << " again; the shake and the "
			   "fade are C#'s ScreenEffects now.";
	}

	EXPECT_EQ( 0u, CountForEffects( eff, "void UTIL_ScreenFade( IGameEntity *pEntity," ) )
		<< kEffectsFile << " defines the one-entity fade again; nothing called it "
		   "when the cull deleted it.";
	EXPECT_EQ( 0u, CountForEffects( eff, kRetiredSpelling ) )
		<< kEffectsFile << " defines UTIL_ScreenFade under the retired spelling "
		   "as well. `CBaseEntity` is not a name in this build, so this is "
		   "either a revert of the alias retirement or a second overload -- and "
		   "a second overload of the fade is a walk the linker picks.";
}

TEST( ViewEffectWalk, TheWalkAsksTheReachRulesAndDoesNotRestateThem )
{
	const std::string eff = SlurpForEffects( kEffectsFile );
	ASSERT_FALSE( eff.empty() );

	EXPECT_LT( 0u, CountForEffects( eff, "ViewEffectReach::" ) )
		<< "the walk stopped asking the reach rules";

	EXPECT_EQ( 0u, CountForEffects( eff, "float PunchScale(" ) )
		<< "the falloff has been copied into the walk, so the engine-free half "
		   "this suite tests is no longer the half the game runs";
	EXPECT_EQ( 0u, CountForEffects( eff, "bool PunchReachesPlayer(" ) )
		<< "the same, for the ground gate.";
}

TEST( ViewEffectWalk, TheObjectShakeIsDeletedFromEveryDefinitionSite )
{
	// game/server/util.cpp was the other definition site; server.so no longer
	// compiles it and it has left the source tree, so that half was culled.
	EXPECT_EQ( 0u, CountForEffects( SlurpForEffects( kEffectsFile ),
	                                "void UTIL_ScreenShakeObject(" ) );
}

// TheObjectShakeKeepsItsDeclarationSoAReturningCallerFailsAtLink WAS HERE and
// read util.h for UTIL_ScreenShakeObject's declaration. game/server/util.h is
// compiled by nothing since plan_server_novalve.md P5 and left the farm in P6,
// so the fact has lost its subject.
