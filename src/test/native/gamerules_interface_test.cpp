//========= SourceSharp — the rules class answers an interface ================//
//
// CSourceSharpGameRules derives from IGameRules and nothing else. The chain was
// CMultiplayRules -> CGameRules -> IGameRules, and each step was taken for the
// same reason: the slots the base supplied were either answered here or reached
// by nothing.
//
// This file is a SOURCE test, not a runtime one, because what it checks is a
// shape: which base a class names, which members it declares, and what a build
// file lists. None of that can be observed from a running server, and all of it
// can regress silently -- re-adding a base compiles perfectly.
//
//============================================================================//

#include <string>
#include <vector>
#include <set>
#include <fstream>
#include <sstream>
#include <regex>

#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

const char *kSource = "sourcesharp/game/shared/shell_gamerules.cpp";
// WHERE THE RATIONALE LIVES NOW. It used to be a comment beside the $File line
// in the .vpc; the scripts were reformatted with every comment stripped out to
// this document, verbatim and keyed the same way. The convention is unchanged --
// a removed file is NAMED somewhere greppable, so a $File line deleted with no
// explanation is still distinguishable from one deleted by accident -- only the
// place it is named moved.

} // namespace

//-----------------------------------------------------------------------------
// The base.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Every interface member is answered.
//
// The compiler already enforces this -- an unimplemented pure virtual makes the
// class abstract and REGISTER_GAMERULES_CLASS's `new` fails. What it does NOT
// enforce is that the answer is the class's own rather than inherited, which is
// the property that disappeared with the base and the reason to state it.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The five symbols that came with it.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ...and they are server-only, which is the part that is easy to get wrong.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The registry is gone, and the wire contract it carried is not.
//-----------------------------------------------------------------------------
TEST( GameRulesInterface, GameRulesRegisterCppIsInNeitherBuild )
{
	// Both sides' source lists, read through cmake_graph.h: server_sourcesharp
	// (src/server/native/CMakeLists.txt) and client_sourcesharp
	// (src/client/native/CMakeLists.txt).
	//
	// Positive control first: an absence assertion over a list that could not
	// be read passes for the wrong reason.
	//
	// THE CONTROL MOVED, AND THE ABSENCE IT GUARDS DID NOT. It used to be
	// `$SRCDIR\game\shared\gamerules.cpp`, and THE FLIP TOOK THAT ROW OUT OF
	// client_csharp.vpc -- at which point the control was reporting exactly what
	// it warns about, an unreadable file list, for a file list that reads fine.
	// A stock TU is not a control for this port; removing stock TUs is what the
	// port does. The control is now the PORT'S OWN replacement for that very
	// file, sourcesharp/game/shared/shell_gamerules.cpp, which is the file whose
	// direct construction the absence below protects -- so it cannot leave the
	// build without this test having no subject at all.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kSource ) )
		<< "server_sourcesharp does not compile the port's own " << kSource
		<< ", so it is not the source list this test thinks it is.";
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kSource ) )
		<< "client_sourcesharp does not compile the port's own " << kSource
		<< ", so it is not the source list this test thinks it is.";

	// By the path's TAIL, as the old client needle was: a copy under any
	// directory counts.
	const std::string strTail = "/gamerules_register.cpp";
	const char *kTargets[] = { cmake_graph::kServer, cmake_graph::kClient };
	for ( size_t t = 0; t < 2; t++ )
	{
		const std::set<std::string> src = cmake_graph::CompiledSources( kTargets[t] );
		for ( std::set<std::string>::const_iterator it = src.begin(); it != src.end(); ++it )
		{
			EXPECT_FALSE( it->size() >= strTail.size() &&
						  it->compare( it->size() - strTail.size(), strTail.size(), strTail ) == 0 )
				<< kTargets[t] << " compiles " << *it
				<< " again, and its half "
				   "collides with the direct construction in shell_gamerules.cpp.";
		}
	}
}

//-----------------------------------------------------------------------------
// THE EIGHTEEN SLOTS NO COMPILED TRANSLATION UNIT ASKS FOR — plan_shim.md §5 3e.
//
// IGameRules' own header says what it is: "every question the build asks the
// game rules, and nothing else". Eighteen of its forty-seven members were not
// questions the build asked. Each was inherited from stock's CGameRules when
// that base was dropped, and the call sites that would have reached them are in
// files that are in neither build -- game/server/client.cpp for the chat members,
// game/shared/baseentity_shared.cpp for GetAmmoDamage, gamerules.cpp:1244 for
// SaveConvar.
//
// HOW THE LIST WAS CHOSEN, because a virtual call emits no symbol and
// tools/shim-census therefore cannot answer this one. Two scans over the
// SOURCES of every object on the ninja link line: the first required the
// receiver to be spelled one of the known rules-pointer names, the second
// dropped that entirely and matched any `Slot(` at all, over-counting on
// purpose so that it could not MISS. The second found five sites the first had
// not, and three of them were real (CreateCustomNetworkStringTables,
// Damage_GetNoPhysicsForce, and an unrelated engine->ClientCommand) -- which is
// why the list is eighteen and not the twenty the first scan alone produced.
//
// WHY A SOURCE SCAN AND NOT JUST THE COMPILER. The compiler proved the removal:
// both DLLs link. What it cannot prove is that they STAY removed. Re-adding a
// pure virtual to IGameRules and an empty body to the class compiles, links and
// runs, and leaves a member that reads live and is asked by nothing -- which is
// exactly what these eighteen were.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE TAGGED-CONVAR MECHANISM WENT AS A WHOLE, which is the part of the removal
// most likely to be half-done.
//
// Three methods and a vector. SaveConvar was the only WRITER, and the only
// caller it ever had was gamerules.cpp:1244. Removing the writer and keeping
// HasSavedConvar, RevertSavedConvars and m_SavedConvars would leave a mechanism
// that a reader would take for live: the destructor still calling
// RevertSavedConvars(), the vector still declared, and every convar the rules
// "changed" still restored -- over a list that can no longer be added to.
//-----------------------------------------------------------------------------
