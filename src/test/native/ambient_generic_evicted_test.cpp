//====== SourceSharp — CAmbientGeneric is DELETED; SENTENCEG_* is not =========//
//
// game/server/sound.cpp was two subsystems sharing a file:
//
//   CAmbientGeneric   the `ambient_generic` looping-sound ENTITY, ~1200 lines
//                     of it, whose registration had been inside
//                     `#ifndef SOURCESHARP` for batches. C# serves the
//                     classname (Entities/Env/AmbientGeneric.cs) and
//                     dm_lockdown places TWENTY-ONE of them.
//
//   SENTENCEG_*       the sentence-group free functions, plus the suit-speaker
//                     emits and TEXTURETYPE_Find. Nothing entity-shaped, and
//                     the only reason the file is on the link line at all.
//
// The entity half is DELETED rather than left compiled behind a guard, and the
// reason is Phase 13 rather than tidiness: once CBaseEntity is a typedef for
// IGameEntity — six pure virtuals and NO members — a class that goes on
// deriving from it cannot compile a single member access. "Leave it compiled"
// stopped being one of the options.
//
// UTIL_RestartAmbientSounds went with it. It walked gEntList for
// "ambient_generic" and called a method on what it found, so it could not
// outlive the type. Its only caller is game/server/player.cpp, which is
// EXCLUDED from the server build — and that exclusion is asserted here,
// because it is the whole of the safety argument and it lives in somebody
// else's file.
//
// WHAT THIS FILE IS FOR. Every fact above is a fact about a file somebody may
// change for their own reasons. Red here is not automatically a regression:
// read the message, re-measure, and either update the fact or take the
// consequence.
//
// SAME STRIPPER RULE as bmodels_verdict_test.cpp: the sentence file's own
// header comment names CAmbientGeneric, UTIL_RestartAmbientSounds and all ten
// of the functions 4a deleted, in the paragraphs explaining why they are gone,
// so a scan that does not remove comments reads the explanation as the
// violation. LoadStripped removes comments before any needle is looked for,
// and that is load-bearing rather than tidy.
//
// WHERE THE FILE IS NOW. plan_entity.md step 4a took game/server/sound.cpp out
// of server_csharp.vpc and off disk. Its three link-demanded functions --
// SENTENCEG_Lookup, SENTENCEG_Init, SENTENCEG_PlaySequentialSz -- are
// sourcesharp/game/server/shell_sentences.cpp, and the ten nothing referenced
// were DELETED rather than carried across. Their declarations stayed in
// game/server/util.h so a returning caller would get a link error naming the
// symbol; that header is compiled by nothing since plan_server_novalve.md P5
// and left the farm in P6, so only the deletion half is still asserted below.
// "Moved the file" and "moved the file and deleted the dead half" are
// different claims and neither implies the other.
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <cstring>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

// game/server/util.h, which DECLARED all thirteen, is compiled by nothing
// since plan_server_novalve.md P5 and left the farm in P6; the facts about
// its declarations retired with it.
// WHAT THE SERVER COMPILES is server_sourcesharp's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/server/native/CMakeLists.txt). CMake lists every compiled source by its
// farm-relative path, so "out of the build" is exactly "not in that set"; the
// vpc-era `-$File` subtractive arm has no CMake equivalent and went with VPC.
const char *const kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

} // namespace

//-----------------------------------------------------------------------------
// The entity half is gone.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ...and the classname it served has an owner, with instances to prove it.
//
// Asked for PRESENCE in C# and in the map, so that "C++ dropped it" and
// "nobody serves it" cannot look the same.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The half that STAYS, spelled the way its referrers spell it.
//
// A deleted symbol is a link error. A renamed one is a link error too — but a
// renamed one that something ELSE in the tree happens to define is not, and
// that is the shape this port has found forty-four times. So these are
// asserted where they live.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ...AND THE TEN NOTHING REFERENCED ARE GONE.
//
// The other half of step 4a's argument, and the half that could quietly not be
// true. "Moved the file" and "moved the file and deleted the dead half" look
// identical to the test above, because that one only asks about the three the
// linker demands.
//
// Each of these was measured, per symbol, with tools/4a-owes over the debug
// server objects: no object in the server link holds an undefined reference to
// any of them. The one call site in a COMPILED file -- TEXTURETYPE_Find at
// gamemovement.cpp:939 -- is inside stock's own block comment, which is why
// `nm -C -u gamemovement.o` names neither it nor any SENTENCEG_.
//
// The declaration half (each one still declared in game/server/util.h) is
// retired: game/server/util.h is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ...and they emit through the FREE function, not through the class static.
//
// CBaseEntity::EmitSentenceByIndex is an INLINE FORWARDER in baseentity.h onto
// GameSound_EmitSentenceByIndex, which is declared in baseentity_statics.h and
// survives Phase 13 by design. The forwarder does not: it is a member of a
// class that becomes IGameEntity, so a call through it is one of the four
// CBASEENTITY-STATIC sites the survey reported for this file, and naming the
// free function is what took sound.cpp to zero.
//
// This is exactly the shape somebody tidies BACK, because the forwarder is
// shorter -- it supplies six defaults that the direct call has to spell. So it
// is asserted in both directions.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE SAFETY ARGUMENT, asserted where it lives.
//
// UTIL_RestartAmbientSounds is DECLARED in game/server/util.h and no longer
// DEFINED anywhere. That is deliberate — a link error is the right answer for
// anyone who puts its caller back — but it is only safe while the caller is
// out of the build, and the caller is in a file this lane does not own.
//-----------------------------------------------------------------------------
TEST( AmbientGenericEvicted, TheOnlyCallerOfTheDeletedHelperIsStillOutOfTheBuild )
{
	// THE ANCHOR MOVED; NOTHING THIS FILE ASSERTS DID.
	//
	// It used to be `$SRCDIR\game\shared\baseentity_shared.cpp`, and THE FLIP
	// REMOVED THAT ROW from both vpcs along with the rest of the entity hierarchy.
	// A stock TU is not an anchor for this port -- removing stock TUs is precisely
	// what the port does, so an anchor on one is a proof-of-read the flip can evict.
	// Stale here is the worst case: this anchor is the only thing stopping every
	// absence invariant it guards from passing vacuously against a file it is not
	// really reading.
	//
	// So the proof-of-read is now the PORT'S OWN shim, which no flip can take, and
	// it is the anchor the client-side entries in this family already use.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "server_sourcesharp does not compile " << kServerProofOfRead
		<< " -- the reader is not reading the server's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "game/server/player.cpp" ) )
		<< "game/server/player.cpp is back in the server build.\n"
		<< "It calls UTIL_RestartAmbientSounds, which is declared in "
		<< "game/server/util.h and DEFINED NOWHERE — its body walked gEntList "
		<< "for `ambient_generic` and could not outlive CAmbientGeneric. The "
		<< "link will fail naming the symbol, which is the intended outcome "
		<< "rather than an accident: decide whether that call still means "
		<< "anything now that the classname is C#'s, and either drop it or "
		<< "give it a managed answer.";

	// The declaration half of this argument (UTIL_RestartAmbientSounds still
	// declared in util.h) is retired: game/server/util.h is compiled by nothing
	// since plan_server_novalve.md P5 and left the farm in P6.
}
