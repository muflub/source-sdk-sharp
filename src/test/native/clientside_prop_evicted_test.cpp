//========= SourceSharp — the clientside prop simulation is out of the build ===//
//
// C_PhysPropClientside and C_FuncPhysicsRespawnZone (game/client/
// physpropclientside.cpp) are out of client_sourcesharp. The .cpp and
// .h stay ON DISK -- props.cpp's do too -- so nothing about that exclusion is
// visible to a compiler. Every fact it rests on is therefore a fact only a
// source scan can defend, and each of them is revocable by a plausible edit
// that produces no diagnostic anywhere:
//
//   * un-exclude the .cpp and the whole subsystem is back in the DLL, with a
//     duplicate prop for any map that has a prop_physics_multiplayer;
//   * put a C_PhysPropClientside::CreateNew() back into CTempEnts::PhysicsProp
//     and the temp-entity door reopens the moment anything sends the TE;
//   * un-exclude props.cpp and something DOES send it: props.cpp:1775/:1784
//     are the only two te->PhysicsProp() call sites in the tree;
//   * add a second client caller of PropBreakableCreateAll and the break loop
//     stops being closed, so --gc-sections stops dropping it.
//
// THAT LAST ONE USED TO END "and BreakModelCreateSingle becomes undefined at
// link". IT NO LONGER DOES, and the change was deliberate rather than
// accidental, so it is recorded here rather than quietly left stale.
//
// sourcesharp/game/shared/shell_props_absent.cpp now DEFINES
// BreakModelCreateSingle for both DLLs. It had to: the same closed-loop
// argument held on the server after props.cpp was evicted, and it meant
// `make debug GC_SECTIONS=0` -- the diagnostic Makefile:30 recommends as the
// first thing to try when a symbol goes missing -- failed the server link with
// 8 undefined references and, once this lane landed, the client link with 4
// more. A broken diagnostic is a specially bad thing to have, because it is
// only ever reached for while something ELSE is already wrong.
//
// So the symbol resolves now, and a second client caller no longer fails the
// link. What replaces that warning is EARLIER and more specific, and both
// halves run in `make test` rather than at link time:
//
//   * NoOtherClientTranslationUnitEntersTheBreakPath, below, covers the case
//     the link used to: a caller of PropBreakableCreateAll or
//     CreateGibsFromList in some OTHER client-compiled file.
//
// (TheBreakPathHasNoClientEntranceOfItsOwn, which read props_shared.cpp, and
// LevelInitDoesNotRecreateClientsideProps, which read cdll_client_int.cpp, were
// culled when client.so stopped compiling those two files and they left the
// source tree.)
//
// What is genuinely given up is that a route nobody has thought of would now
// reach a stub that asserts at runtime instead of failing to link. That is the
// same trade shell_ai_absent.cpp makes about a hundred times, and it is taken
// here for a reason it does not have: the alternative was leaving a documented
// debugging tool broken for everyone, permanently, to preserve a link error for
// one hypothetical edit that two tests now catch sooner.
//
// WHY A SOURCE SCAN AND NOT A LINK TEST. The link already agrees today: the
// tree's own ./evict_probe answers exit 0 / 0 undefined refs for
// physpropclientside.o in both configurations. What the link CANNOT tell you
// is why -- it is silent about the difference between "nothing calls this"
// and "the caller is itself dead", and it is the second one here. A link test
// would keep passing through every edit above except the last.
//
// The stripper is load-bearing rather than tidy: this file's own prose spells
// `C_PhysPropClientside::CreateNew()` and `te->PhysicsProp` in the paragraphs
// explaining that no such spelling may remain, and an unstripped scan would
// read the explanation as the violation. Same rule as header_alias_test.cpp.
//
// ANCHORS, NOT LINE COUNTS. Every file below is asserted to still contain a
// string that identifies it. A source-scanning test whose file has been
// renamed or rewritten reads an empty string and reports every absence
// invariant as satisfied -- the check that cannot fail.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sys/stat.h>
#include <dirent.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it.
//-----------------------------------------------------------------------------
struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// WHAT EACH TARGET COMPILES is its source list, read through cmake_graph.h
// (compile_commands.json cross-checked against the target's CMakeLists). The
// proof-of-read for each absence is the PORT'S OWN shim that supplies what the
// eviction took away, which no eviction can take: a reader that sees nothing
// cannot then answer "not compiled" for free.
//
// (The vpc-era anchors moved twice -- off the `-$File` row itself when
// client_csharp.vpc was flattened, and off shell_props_absent.cpp when
// plan_shared.md Phase 5 deleted it. The client anchor was the file that
// replaced props_shared.cpp until C9 deleted that too (its CPropData fed
// nothing); it is now the client-system table that listed it. The server's is
// shell_shared.cpp, as in the rest of this family.)
const char *const kClientProofOfRead = "sourcesharp/game/client/shell_clientsystems.cpp";
const char *const kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";
} // namespace

//-----------------------------------------------------------------------------
// The exclusion itself.
//-----------------------------------------------------------------------------
TEST( ClientsidePropEvicted, TheClientExcludesPhysPropClientside )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kClientProofOfRead ) )
		<< "client_sourcesharp does not compile " << kClientProofOfRead
		<< " -- the reader is not reading the client's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/physpropclientside.cpp" ) )
		<< "physpropclientside.cpp is back in the client build; the whole "
		   "clientside prop simulation returns with it, and with it a second "
		   "unnetworked copy of every prop_physics_multiplayer the map places";
}

//-----------------------------------------------------------------------------
// DOOR 2's far side, and the reason it is dead rather than merely quiet.
//
// CTempEnts::PhysicsProp is still a vtable slot -- ITempEnts declares it pure
// virtual -- so it exists and can be called. What makes it safe is that the
// message that would call it has no sender, and the sender lives in a file
// that is itself excluded. Both halves are asserted, because either one coming
// back is enough.
//-----------------------------------------------------------------------------
TEST( ClientsidePropEvicted, TheServerStillExcludesPropsCpp )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "server_sourcesharp does not compile " << kServerProofOfRead
		<< " -- the reader is not reading the server's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "game/server/props.cpp" ) )
		<< "props.cpp is back in the server build. It holds the ONLY two "
		   "te->PhysicsProp() call sites in the tree, so the physicsprop temp "
		   "entity acquires a sender and CTempEnts::PhysicsProp starts being "
		   "reached again";
}

//-----------------------------------------------------------------------------
// The other half of the same invariant, and the half the LINKER used to cover.
//
// props_shared.cpp is allowed to call the break path; it is the break path. The
// claim that keeps the loop closed on the client is that NOTHING ELSE the
// client compiles calls PropBreakableCreateAll or CreateGibsFromList. Until
// shell_props_absent.cpp landed, breaking that claim produced an undefined
// BreakModelCreateSingle at link. Now it produces nothing at all at build time,
// so it is checked here instead -- earlier, and by name.
//
// The set scanned is exactly what client_sourcesharp compiles, read from the
// CMake graph -- not a list of directories. A directory list went stale when
// the move left game/client/hl2mp and game/shared/hl2mp with no client source
// in the farm: a fresh farm has no such directories at all, while an older farm
// kept them as empty leftovers, so the same tree failed in one checkout and
// passed in another. physpropclientside.cpp is not compiled (that is
// TheClientExcludesPhysPropClientside), so it is never scanned here.
//-----------------------------------------------------------------------------
