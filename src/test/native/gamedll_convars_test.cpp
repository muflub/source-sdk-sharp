//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// plan_shim.md Phase 1d, lane 1d. game/server/game.cpp left server_csharp.vpc (the
// server's source list, now server_sourcesharp's in src/server/native/CMakeLists.txt);
// its seventeen ConVar declarations did not.
// THREE FAILURES THIS PINS, all of them silent:
//   1. A DECLARATION DROPPED WITH THE TRANSLATION UNIT. Seven of the
//      seventeen have a consumer that reaches them by NAME -- managed code
//      through FindConVarValue, the engine through ConVarRef, and the
//      dedicated server's settings dialog through cfg/settings.scr. None of
//      those is a symbol import, so the link-line census that authorised this
//      deletion reads 0 importers for all seventeen and is right about the
//      symbols and wrong about the ConVars. A name that is not registered is
//      not a link error and not a warning: FindConVarValue answers nothing and
//      the caller reads its own default, which for displaysoundlist is
//      indistinguishable from "the display is switched off".
//   2. TWO REGISTRATIONS, OR NONE. Registration is ConVar_Register, tier1
//      guards it with s_bRegistered, and whichever call runs first supplies
//      the accessor that applies command-line values. Before this lane there
//      were two -- stock's in game.cpp and the shell's transliteration of it --
//      and stock's ran first, which made the shell's override dead code. With
//      game.cpp gone the shell's is the only one left. A second one added back
//      anywhere in the compiled server set would make the shell's dead again,
//      and nothing about that is visible at compile or link time.
//   3. THE HANDLE LEFT UNASSIGNED. g_pDeveloper is dereferenced by
//      physics.cpp and shell_soundscape.cpp. It is assigned in exactly one
//      place, and that place is called from one call site.

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <algorithm>
#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{

const char *const kShimGameDll = "sourcesharp/game/server/shell_gamedll.cpp";
const char *const kStockCpp  = "game/server/game.cpp";
// WHAT THE SERVER COMPILES is server_sourcesharp's source list, read through
// cmake_graph.h: compile_commands.json cross-checked against
// src/server/native/CMakeLists.txt, farm-relative paths. (This used to be a
// hand-written $File walker over server_csharp.vpc; the cross-check is what
// now proves the whole list was read.)

// The seventeen, as game.h declares them, paired with the ConVar NAME each
// one registers -- the two differ for eleven of them, and the name is the half
// that is reached from outside this DLL.
//
// The kSeventeen symbol table went with the native file it counted
// (plan_server_native_min P1); the managed count below pins the number.

} // namespace

//=============================================================================//
// 1. THE STOCK ROW IS GONE AND THE SHIM ROW IS THERE
//=============================================================================//

TEST( GameDllConVars, TheStockTranslationUnitHasLeftTheServer )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kShimGameDll ) )
		<< "server_sourcesharp does not compile " << kShimGameDll
		<< " -- the reader is not reading the server's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kStockCpp ) )
		<< "game.cpp is back in server_sourcesharp (src/server/native/CMakeLists.txt). It defines the same "
		   "seventeen ConVars GameDllConVars.cs declares, so both rows together "
		   "are seventeen duplicate registrations -- which the ConVar system "
		   "reports at runtime and no compile or link step sees.";
}

// TheShimFileIsStillCompiledForTheDeveloperHandle went with the file it
// required present (plan_server_native_min P1): g_pDeveloper's two native
// readers left with physics's and the soundscape's ports, so the pointer and
// the one call that assigned it are deleted, not re-pinned.

//=============================================================================//
// 2. ALL SEVENTEEN SURVIVED, BY SYMBOL AND BY NAME
//=============================================================================//

// NoCompiledNativeFileDeclaresAnyOfTheSeventeen went with the file it read
// (plan_server_native_min P1). What it guarded against -- a second native
// ConVar under a managed name -- is the tier1 child-registration hazard, and
// the whole-tree form of the guard is ExactlyOneCompiledServerFileCalls-
// ConVarRegister above, which walks every compiled server source.

//=============================================================================//
// 3. THE BY-NAME CONSUMERS THE CENSUS CANNOT SEE, each still asking
//=============================================================================//

//=============================================================================//
// 4. ONE REGISTRATION IN THE WHOLE COMPILED SERVER SET
//=============================================================================//

//=============================================================================//
// 5. THE ENGINE HANDLE, assigned once and called once
//=============================================================================//

// InitializeCvarsAssignsTheDeveloperHandleAndIsStillCalled went with its
// subject (plan_server_native_min P1): InitializeCvars is deleted from
// shell_engineboundary.cpp with the pointer it assigned, whose last two
// dereferences left with their files' ports.

//=============================================================================//
// 6. THE CHECK ON THE CHECKS
//=============================================================================//

// NoNeedleIsSatisfiedByProseAlone read kShimCvars's banner; the file is
// deleted (plan_server_native_min P1) and with it the banner it proved
// non-empty. NoManagedNeedleIsSatisfiedByProseAlone keeps the form over the
// managed file.

