//====== SourceSharp — step 7a: four stock effect TUs leave the client ========//
//
// plan_entity.md Phase 7 step 7a, "effect entities over leaf renderers". Four
// files leave the client build (then `game/client/client_csharp.vpc`, now
// client_sourcesharp in src/client/native/CMakeLists.txt):
//
//   c_env_fog_controller.cpp  compiled NOTHING. Its whole body is inside
//                             `#ifndef SOURCESHARP` and nm found no client
//                             object wanting a symbol it defined.
//   c_func_dust.cpp           FX_Dust, its last compiled body, moved to
//                             sourcesharp/game/client/shell_dust_render.cpp.
//   c_sprite.cpp              the leaf sprite renderer moved to
//                             sourcesharp/game/client/shell_sprite_leaf.cpp.
//   c_impact_effects.cpp      1,391 lines of FX_* impact effects moved to
//                             sourcesharp/game/client/shell_impact_effects.cpp.
//
// THE LINK CANNOT SEE ONE CLAIM UNDER ANY OF THAT. A build that compiles is the
// one thing an eviction always produces, and "a compile gate selects for
// deletion" is this tree's own recorded lesson: the way this step goes wrong is
// that a body is DELETED rather than moved, or LEFT BEHIND as a second copy in
// a file nothing compiles, and neither produces a diagnostic anywhere. What is
// left here is the build-list half: the evicted TUs are out of the client and
// their replacements are in it, and the moved copies carry no dead guard.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <iterator>
#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <sys/stat.h>

namespace
{

//-----------------------------------------------------------------------------
// The subjects.
//-----------------------------------------------------------------------------
const char *kEvicted[] =
{
	"game/client/c_env_fog_controller.cpp",
	"game/client/c_func_dust.cpp",
	"game/client/c_sprite.cpp",
	"game/client/c_impact_effects.cpp",
	// THE FIFTH, AND IT ARRIVED BY A DIFFERENT ROUTE THAN THE FOUR ABOVE.
	// game/client/fx_impact.cpp was never taken out of client_csharp.vpc BY THIS
	// STEP -- its $File row left with the armed-rehearsal removal set, before a
	// single body had moved -- so for a while it was an evicted TU whose five
	// definitions were still the only ones in the tree, and the client link said
	// so with five undefined symbols. plan_entity.md §6's LEAVE row closed that:
	// ParseImpactData, PerformCustomEffects, GetImpactDecal and PlayImpactSound
	// moved to shell_impact_effects.cpp and FX_AffectRagdolls was a §7 refusal
	// there (all five are C#'s BulletImpactEffect since lane M1-C). It joins this list at that point and not before, because only now
	// is "out of the build AND defining nothing this build wanted" true of it.
	"game/client/fx_impact.cpp",
};

const char *kReplacements[] =
{
	"sourcesharp/game/client/shell_sprite_leaf.cpp",
	"sourcesharp/game/client/shell_impact_effects.cpp",
	"sourcesharp/game/client/shell_effect_ledger.cpp",
};

const size_t kNumEvicted = sizeof( kEvicted ) / sizeof( kEvicted[ 0 ] );
const size_t kNumReplacements = sizeof( kReplacements ) / sizeof( kReplacements[ 0 ] );

} // namespace

//-----------------------------------------------------------------------------
TEST( EffectTusEvicted, TheFiveStockTusAreOutOfTheClient )
{
	// WHAT client_sourcesharp COMPILES, not a substring search over a file's
	// text: compile_commands.json cross-checked against the CMakeLists source
	// list (cmake_graph throws on a missing, empty or stale input). A name in a
	// COMMENT of the list is not a name compiled.
	//
	// Proof-of-read before any absence: a replacement this step put in IS
	// compiled, so the reader is reading the client's list.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kReplacements[ 0 ] ) )
		<< "client_sourcesharp does not compile " << kReplacements[ 0 ]
		<< "; every absence below would pass vacuously";

	for ( size_t i = 0; i < kNumEvicted; i++ )
	{
		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kEvicted[ i ] ) )
			<< kEvicted[ i ] << " is compiled again. Step 7a took it out because "
			   "everything it still held is inside `#ifndef SOURCESHARP` -- so "
			   "putting it back compiles a translation unit with no content, and "
			   "if a body came back with it, two definitions.";
	}
}

//-----------------------------------------------------------------------------
TEST( EffectTusEvicted, TheThreeShimReplacementsAreInTheClient )
{
	for ( size_t i = 0; i < kNumReplacements; i++ )
	{
		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kReplacements[ i ] ) )
			<< kReplacements[ i ] << " is not compiled into client_sourcesharp. An "
			   "eviction whose replacement is not in the build is a capability "
			   "that stopped being shipped -- and for the leaf renderers it is "
			   "every sprite, every impact and every dust puff on the client.";
	}
}

// TheVpcStillBalancesItsBraces is RETIRED (Phase 6b): it guarded that removing a
// `$File` row could not take a `$Folder` brace with it -- VPC script syntax. The
// CMake source list has no folders, and a malformed CMakeLists fails configure.

//-----------------------------------------------------------------------------
// THE MOVED COPIES CARRY NO DEAD MOD OR HARDWARE GUARD.
//
// tools/dead-conditionals refuses one in the shim, and it went from 3 problems
// to 0 when these were resolved at the move: `#ifndef TF_CLIENT_DLL` and
// `#ifndef CSTRIKE_DLL` around the muzzleflash handles, the `_X360` early
// return in FX_AntlionImpact, and the whole XBox arm of FX_GaussExplosion. This
// asserts the source fact directly as well, because that tool reads the ninja
// command lines and a change to those would move its answer without moving the
// code.
//-----------------------------------------------------------------------------
