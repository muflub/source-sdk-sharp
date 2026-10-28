//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// Purpose: SourceSharp — the two DispatchParticleEffect overloads that outlive
//          the entity component, and the §6 ruling that sent them to the wrong
//          place.
// plan_entity.md §6's "17 that LEAVE" table rules `particle_parse.cpp` out and
// names «`SourceSharp_ParticleSystemStart` / `Stop` / `FollowPoints`, already in
// the shim» as what does the thing afterwards. Lane `c8-flip` carried that
// forward as its client family 6 and called it "RE-POINT, cheapest family".
// IT IS NOT A RE-POINT, and this file is the measurement that says so rather
// than a paragraph asserting it. The shim trio takes a MANAGED ENTITY HANDLE and
// attaches ONE long-lived, followed particle system to that entity. The three
// §6-STAYS emitters that owe these two overloads want a ONE-SHOT effect at a
// world point, and two of the three pass no entity at all. So the ruling is
// RE-HOME: both bodies moved verbatim into
// sourcesharp/game/shared/particle_manifest.cpp, which already holds
// particle_parse.cpp's staying half and is already in BOTH targets.
// WHAT EACH TEST BELOW IS ACTUALLY ABOUT, since "the file compiles" is not it:
//     (the flip is a link question, and a duplicate is as fatal as a miss);
//     blood, slime splashes and tracers passes every link gate there is;
//     the `else` arm inside them that CLEARS the entity field -- losing that arm
//     is invisible unarmed and wrong armed;
//     reading §6 finds the refutation next to the code instead of repeating it.

#include <gtest/gtest.h>
#include "cmake_graph.h"

#include <dirent.h>

#include <algorithm>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// buildcpptests runs each binary with src/ as the working directory. A list
// rather than one path so that running the binary by hand from the repo root
// works too.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// A file's lines with whole-line comments dropped. Every subject of this file
// is named repeatedly in the PROSE of the files it scans -- this test's own
// banner does it, and so do both moved banners -- and a check a comment can
// satisfy is a check about comments.
//-----------------------------------------------------------------------------
// Every line of code in a file that contains pszNeedle, with its 1-based number.
struct Hit_t
{
	int         nLine;
	std::string strText;
};

//-----------------------------------------------------------------------------
// The body of a function, from the line that opens its definition to the brace
// that closes it, with whitespace squeezed out so the assertions below are
// about statements rather than about indentation.
//-----------------------------------------------------------------------------
const char *kShim      = "sourcesharp/game/shared/particle_manifest.cpp";
//=============================================================================//
// THE CHECK THAT WOULD OTHERWISE NOT BE ABLE TO FAIL.
//
// Four of the tests below assert that something is ABSENT, and a file that could
// not be read is absent in exactly the same way. A rename, a move, or an
// unexpected working directory would turn the lot green and mean nothing.
//=============================================================================//
//=============================================================================//
// PART ONE — the re-home itself. A link question, so a duplicate is as fatal as
// a miss and both directions are asserted.
//=============================================================================//

//-----------------------------------------------------------------------------
// THE CAPABILITY, not the link. COMMON.md: "a compile gate selects for
// deletion" -- `#ifndef SOURCESHARP`-ing these three call sites out would clear
// the same three owed pairs and would silently stop the game drawing blood,
// slime splashes and tracers.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The bodies still do the thing. A moved body that was hollowed out on the way
// satisfies every test above.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. There is no guard left to be outside of.
//
// This fact used to pin WHERE the index overload closed its
// `#ifndef SS_CBASEENTITY_ALIAS` -- on the `else`, so that the entity-clearing
// arm stayed outside it. That guard was one of the 453 arms the compiler never
// saw (CBaseEntity WAS IGameEntity, so SS_CBASEENTITY_ALIAS was always
// defined), and it has been deleted along with the entity arm it wrapped.
//
// THE CLAIM IS UNCHANGED AND IS NOW UNCONDITIONAL: the entity field is CLEARED
// before the dispatch, on BOTH sides, and is never left at whatever CEffectData
// was constructed with. What used to make that true was "the guard closes on
// the `else`"; what makes it true now is "there is no guard and the clear is
// the only arm". Both halves are asserted -- the absence of the guard, and the
// clear-then-dispatch that the guard existed to protect -- because either one
// alone passes over a body that lost the other.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ON THE CLIENT'S LINK LINE ONLY. When this was written the server called the
// name-and-origin overload too (physics.cpp, item_world.cpp). Neither does now:
// item_world.cpp is not built and physics.cpp names neither overload, so nm over
// every server_sourcesharp object finds no reference to DispatchParticleEffect,
// and particle_manifest.cpp left the server's list (P5 lane SP). Re-adding it
// would put a Valve-header TU back into server.so for two dead functions.
//-----------------------------------------------------------------------------
TEST( ParticleDispatchRehome, TheShimFileIsOnTheClientLinkLine )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kShim ) )
		<< "particle_manifest.cpp is not compiled into client_sourcesharp "
		   "(src/client/native/CMakeLists.txt)";
}

TEST( ParticleDispatchRehome, TheShimFileIsNotOnTheServerLinkLine )
{
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kShim ) )
		<< "particle_manifest.cpp is compiled into server_sourcesharp again "
		   "(src/server/native/CMakeLists.txt); no server object calls "
		   "either DispatchParticleEffect overload";
}

//=============================================================================//
// PART TWO — why §6's ruling could not be carried out, pinned next to the code
// so the next lane reading that table finds the refutation rather than
// repeating the attempt.
//=============================================================================//

//-----------------------------------------------------------------------------
// The shim entry point §6 named takes a HANDLE AND A NAME. It has no origin
// parameter, no angle parameter and no start parameter, so there is no spelling
// of a re-point that preserves what the three emitters pass.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// And the other half: two of the three call sites have no entity to hand it.
// `DispatchParticleEffect( name, origin, angles )` -- three arguments, and the
// fourth defaults to NULL in particle_parse.h.
//-----------------------------------------------------------------------------
} // namespace
