//========= SourceSharp — game/server/effects.cpp is out of the build =========//
//
// effects.cpp held sixteen entity classes in 2,545 lines and is out of the
// server build (it was `-$File` out of server_csharp.vpc; it is absent from
// server_sourcesharp's list in src/server/native/CMakeLists.txt now). The .cpp stays ON DISK — props.cpp's and
// physpropclientside.cpp's do too — so nothing about that exclusion is visible
// to a compiler, and every fact it rests on is a fact only a source scan can
// defend.
//
// WHAT THE LINK ALREADY AGREES ABOUT, and therefore what this does NOT need to
// re-check: `./evict_probe release server effects.o` answered exit 1 with
// exactly ONE undefined reference, UTIL_BloodSpray, and after the lift to
// shell_entity_orphans.cpp the server links clean in BOTH configurations,
// including `make debug GC_SECTIONS=0` — the diagnostic Makefile:30 recommends,
// and the one that catches an eviction whose symbols a RETAINED file still
// names. What the link cannot tell you is WHY, and it is silent about the
// difference between "nothing calls this" and "the caller is itself dead".
// Every fact below is of the second kind.
//
// THE REVOCABLE EDITS, each of which produces no diagnostic anywhere:
//
//   * un-exclude the .cpp and sixteen dead classes are back in server.so, three
//     of them registering classnames C# already answers to (env_bubbles,
//     env_blood, gibshooter and ten more) — a duplicate factory entry for every
//     one, of which the dictionary keeps ONE and silently drops the other;
//   * put UTIL_BloodSpray or EffectsPrecache back into effects.cpp and the
//     first is a duplicate symbol (loud) while the second is a SECOND
//     PRECACHE_REGISTER_FN of the same function (silent — it would simply
//     precache the same six sounds twice);
//   * delete either from shell_entity_orphans.cpp: UTIL_BloodSpray fails the
//     link, and EffectsPrecache does NOT, because nothing names it. The splash,
//     the underwater bullet impact, the ricochet and BaseExplosionEffect.Sound
//     would go silent on their first play with nothing reporting it. That is
//     the one this file exists for.
//
// AND ONE FIXTURE-AGAINST-NATIVE CHECK. BloodSprayCommand.Everything in
// SourceSharp.Server/DevCommandsLive.cs stands in for stock's
// FX_BLOODSPRAY_ALL, which is 0xFF, on the argument that the client reads
// exactly three of those bits. C# cannot see the client. This asserts the
// agreement, so a fourth bit added to fx_blood.cpp is a red test rather than an
// effect the managed command can no longer ask for. Same rule as TierParityTests
// and AbiParityTests.
//
// THE STRIPPER IS LOAD-BEARING rather than tidy: this file's own prose spells
// `LINK_ENTITY_TO_CLASS( env_bubbles`, `UTIL_BloodSpray` and
// `PRECACHE_REGISTER_FN` in the paragraphs explaining where they may and may not
// be, and an unstripped scan would read the explanation as the violation. Same
// rule as clientside_prop_evicted_test.cpp and header_alias_test.cpp.
//
// ANCHORS, NOT LINE COUNTS. Every file below is asserted to still contain a
// string that identifies it. A source-scanning test whose file has been renamed
// or rewritten reads an empty string and reports every absence invariant as
// satisfied — the check that cannot fail.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <set>
#include <vector>
#include <cctype>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as
// clientside_prop_evicted_test.cpp.
//-----------------------------------------------------------------------------
struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// The anchor is a string the file must still contain for this test to be
// reasoning about the file it thinks it is.
//
// WHAT THE SERVER COMPILES is server_sourcesharp's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/server/native/CMakeLists.txt). Its proof-of-read is the PORT'S OWN shim,
// which no flip can take. (It used to be
// `$SRCDIR\game\shared\baseentity_shared.cpp`, and THE FLIP REMOVED THAT ROW along with the rest of
// the entity hierarchy: a stock TU is not an anchor for this port.)
const char *const kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

// Is the server compiling ANY source whose path contains the needle? The
// read is proved first, so an unread list cannot answer "no" for free.
bool ServerCompilesAnyNaming( const std::string &strNeedle )
{
	const std::set<std::string> compiled = cmake_graph::CompiledSources( cmake_graph::kServer );
	EXPECT_TRUE( compiled.count( kServerProofOfRead ) ) << "server_sourcesharp does not compile " << kServerProofOfRead
														<< " -- the reader is not reading the server's source list";
	for ( std::set<std::string>::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
		if ( it->find( strNeedle ) != std::string::npos )
			return true;
	return false;
}

} // namespace

//-----------------------------------------------------------------------------
// The exclusion itself.
//-----------------------------------------------------------------------------
TEST( EffectsEvicted, TheServerExcludesEffectsCpp )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "server_sourcesharp does not compile " << kServerProofOfRead
		<< " -- the reader is not reading the server's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "game/server/effects.cpp" ) )
		<< "effects.cpp is back in the server build. Sixteen entity classes come "
		   "with it, thirteen of which register classnames a managed class "
		   "already answers to — and the factory dictionary keeps ONE "
		   "registration per name and drops the other in silence";
}

//-----------------------------------------------------------------------------
// The two tenants, and where each of them may live.
//
// ONE definition each, in the shell and nowhere else. A second definition in
// effects.cpp would be a duplicate symbol IF the file came back, which is
// exactly the state the first test forbids and this one makes survivable to
// diagnose.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The classname that came with it.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE FIXTURE-AGAINST-NATIVE CHECK.
//
// The managed command passes Drops|Gore|Cloud where stock passed
// FX_BLOODSPRAY_ALL (0xFF), on the argument that the client reads exactly those
// three bits. That argument is about a file C# cannot see.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The three classes that left with no managed replacement, and the evidence
// that each was already unreachable. All three are LINK_ENTITY_TO_CLASS
// registrations that were guarded away before this lane — the point is that the
// guard is what made them unreachable, so un-guarding one is the regression.
//-----------------------------------------------------------------------------
TEST( EffectsEvicted, TheHelicopterThatWantedTheRotorShooterIsStillNotBuilt )
{
	// GetRotorWashShooter's ONLY caller is hl2/cbasehelicopter.cpp, which lived
	// in Valve's server_hl2.vpc and has never been on this link line — asserted
	// here by the absence of any helicopter file from what the server compiles,
	// under any directory.
	EXPECT_FALSE( ServerCompilesAnyNaming( "cbasehelicopter" ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) now compiles "
		   "a cbasehelicopter file. env_rotorshooter's "
		   "CRotorWashShooter left with effects.cpp on the strength of "
		   "GetRotorWashShooter having no compiled caller, and cbasehelicopter.cpp "
		   "is that caller";
}

//-----------------------------------------------------------------------------
// game/shared/Sprite.cpp, and THE MEASUREMENT THIS TEST ASKED FOR HAS BEEN
// TAKEN.
//
// This section used to assert that shell_sprite_render.cpp still did
// `new C_SpriteTrail`, with the note: "If nothing builds one, SpriteTrail.o
// stops needing C_Sprite and BOTH files become evictable from the client --
// which is a measurement worth taking rather than a regression."
//
// Taken. Measured with nm over the built debug client objects -- the
// defined-symbols of one against the undefined-symbols of every other:
//
//   BEFORE   Sprite.o        2 referrers: SpriteTrail.o (12 symbols) and
//                            shell_sprite_render.o (SetBrightness, SetScale)
//            SpriteTrail.o   1 referrer:  shell_sprite_render.o, seven setters
//                            and the constructor
//   AFTER    SpriteTrail.o   ZERO referrers
//            Sprite.o        one referrer, SpriteTrail.o, which goes with it
//
// So the trail WAS the reason and it is not a reason any more. The ribbon lives
// on a CShellRenderable in shell_sprite_render.cpp -- the same de-entified base
// C_PointWorldText moved onto -- and both stock files are `-$File` out of the
// client build.
//
// SERVER. Unchanged, and not this lane's: what holds Sprite.o on the server link
// is gib.cpp's two dynamic_cast blocks, which are dead but belong to the
// prop-gibs lane. It was written down in server_csharp.vpc and is in Sprite.cpp.
// (gib.cpp and Sprite.cpp have since both left server_sourcesharp.)
//
// WHAT IS ASSERTED NOW is the trail's move, plus the one behaviour consequence
// below:
//
//   * the managed shooter attaches no sprite to its gibs.
//-----------------------------------------------------------------------------
//=============================================================================
// THE SPRITE SHELL'S DUPLICATION, FOLDED (lane 4c-render, plan_shim.md §6).
//
// Two things were written out more than once in shell_sprite_render.cpp: the
// render-mode bounds check (twice) and the "find the row for this owner" loop
// (three times, while sourcesharp/game/shared/shell_effect_registry.h already
// held it with a test that drives it).
//
// The render-mode fold has exactly ONE way to go wrong and it is invisible: if
// the two call sites share one warn-once flag, the second caller's first bad
// render mode is silent whenever the first caller has already warned. They are
// fed by two different managed call paths, so that is a real case, and the
// symptom is a sprite that draws nothing with nothing in the console.
//
// These live in this file rather than a new one because kSpriteShell is already
// here with its anchor, and a new binary is a buildcpptests edit.
//=============================================================================
