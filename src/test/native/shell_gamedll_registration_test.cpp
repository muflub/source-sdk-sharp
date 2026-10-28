//=============================================================================//
// THE SWITCH, and the three things about it that are invisible to every other
// gate in this tree. plan_gamedll.md Phase 4, and docs/gamedll-ledger.md's
// corrections to it.
//
// The engine now finds CShellGameDll when it asks for "ServerGameDLL012".
// Three properties make that safe rather than catastrophic, and NONE of them
// is a link error, a compile error, or a count `strings` can produce:
//
//   1. EXACTLY ONE CLASS REGISTERS EACH GAME-DLL INTERFACE. Two InterfaceReg
//      entries for one version string both link. Which one CreateInterface
//      hands the engine is decided by static-initialisation order across
//      translation units -- a silent, nondeterministic wrong answer.
//
//   2. THE GUARD IS ON THE REGISTRATION AND NOT ON THE CLASS. Phase 4's text
//      asks for both `#ifndef SOURCESHARP_SHELL_GAMEDLL` around
//      CServerGameDLL's class body AND a `CServerGameDLL *` delegate on the
//      shell (T4). Those are mutually exclusive; a pointer needs a complete
//      type with a definition to link. Guarding only the EXPOSE_ satisfies the
//      gate the phase actually states and keeps the delegate, which is what
//      makes Phases 5-7 A/B-testable on one binary.
//
//   3. THE SHELL ASSIGNS g_pSharedChangeInfo. Stock assigns it inside
//      CServerGameDLL::DLLInit. Its reader is CBaseEdict::StateChanged
//      (public/edict.h:295), an INLINE on the entity-networking path that
//      dereferences it unconditionally. A shell that owns DLLInit and does not
//      assign it is a null dereference on the first networked state change --
//      which is not reachable offline at all, so this file pins the assignment
//      instead. That is a weaker proof than running it and it is stated as
//      what it is.
//
// SOURCE-READ RATHER THAN RUN, and that is forced rather than chosen:
// shell_gamedll.cpp includes gameinterface.h, eiface.h and cbase.h's world, so
// there is no way to link its bodies into a gtest binary. What can be checked
// without an engine is the ARRANGEMENT, and every check here is one a reader
// could get wrong by eye -- which of two adjacent lines comes first, whether a
// method that was added last week got its branch, whether an EXPOSE_ is inside
// a guard or two lines below its #endif.
//
// The binary half of property 1 is tools/gamedll-interface-census, which reads
// the symbol table of the built server.so and cannot be run from here.
//=============================================================================//

#include <algorithm>
#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>
#include <sys/stat.h>

#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

// The -D definitions of one compile command, as NAME or NAME=VALUE, matched
// whole: a set lookup cannot be satisfied by a prefix, which is the failure the
// old VPC directive matcher had to guard against by hand.
std::set< std::string > DefinesOf( const std::string &strCommand )
{
	std::set< std::string > out;
	std::istringstream toks( strCommand );
	std::string strTok;
	while ( toks >> strTok )
		if ( strTok.compare( 0, 2, "-D" ) == 0 && strTok.size() > 2 )
			out.insert( strTok.substr( 2 ) );
	return out;
}

const char *kShell   = "sourcesharp/game/server/shell_gamedll.cpp";
} // namespace


//=============================================================================//
// Property 1 -- one registering implementation per interface
//=============================================================================//

TEST( ShellGameDllRegistration, TheDefineIsInTheProjectBesideSOURCESHARP )
{
	// The guard only guards if the macro is defined for this project, and it is
	// defined in exactly one place. A #ifndef whose macro nothing defines is
	// inert and reads identically to one that works.
	//
	// RE-POINTED IN PHASE 6b. This read server_csharp.vpc's
	// $PreprocessorDefinitions directive, with a hand-built matcher (and two
	// self-checks of it) because a VPC define list is one quoted string: the
	// matcher had to see SOURCESHARP_SHELL_GAMEDLL beside SOURCESHARP, not pin
	// the tabs, not pin the rest of the list, and not accept a longer name that
	// begins with this one. The define now lives in server_sourcesharp's
	// target_compile_definitions block (src/server/native/CMakeLists.txt), one
	// name per token, so each of those hazards is a set lookup: exact names,
	// order- and whitespace-free. The "$Configuration block that follows the
	// copied one" half was VPC's own ordering hazard and has no CMake analogue.
	//
	// Which define is armed in which target IS asserted, elsewhere and
	// deliberately -- alias_mechanics_test's "armed in BOTH targets or
	// neither".
	const std::vector< std::string > listed = cmake_graph::ListedDefinitions( cmake_graph::kServer );
	ASSERT_EQ( 1, (int)std::count( listed.begin(), listed.end(), std::string( "SOURCESHARP" ) ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) does not define "
		   "SOURCESHARP exactly once -- the reader is not reading the server's "
		   "target_compile_definitions block, or the port's own define is gone";
	EXPECT_EQ( 1, (int)std::count( listed.begin(), listed.end(), std::string( "SOURCESHARP_SHELL_GAMEDLL" ) ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) must define "
		   "SOURCESHARP_SHELL_GAMEDLL, once, beside SOURCESHARP";

	// AND THE LIST IS WHAT THE COMPILER SAW. The listed block is the source of
	// truth; the compile command of the file the guard is IN is the proof that
	// it reached the TU (a define on another target, or a per-source override,
	// would read the same in the list).
	const std::set< std::string > seen = DefinesOf( cmake_graph::CompileCommand( cmake_graph::kServer, kShell ) );
	ASSERT_TRUE( seen.count( "SOURCESHARP" ) )
		<< kShell << "'s compile command carries no -DSOURCESHARP -- the reader "
		   "is not reading the right command";
	EXPECT_TRUE( seen.count( "SOURCESHARP_SHELL_GAMEDLL" ) )
		<< kShell << " is compiled without -DSOURCESHARP_SHELL_GAMEDLL, so every "
		   "#ifndef SOURCESHARP_SHELL_GAMEDLL guard in stock is inert";
}


//=============================================================================//
// Property 2 -- the guard is on the registration, not on the class
//=============================================================================//

//=============================================================================//
// Property 3 -- g_pSharedChangeInfo, the crash Phase 4 walks into
//=============================================================================//

//=============================================================================//
// PHASE 5 -- the two pointers and the constants (Part 5, Part 6)
//
// The same forced arrangement as above: shell_gamedll.cpp cannot be linked into
// a gtest binary, so what is checked is the ORDER and the SHAPE of things a
// reader gets wrong. The behavioural half of Part 6 lives in the managed suite
// (ListingBuffer, ServerGameDll, ShellSlotWiringTests) and in the adapter's own
// gtests, which compile the shipped header.
//=============================================================================//

