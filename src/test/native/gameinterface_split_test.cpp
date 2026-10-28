//========= SourceSharp — gameinterface.cpp's last four interfaces ============//
//
// plan_gamedll_after.md Phase A9. The file that held four of the census's seven
// game-DLL interfaces is taken apart and deleted. This harness holds the parts
// of that the linker cannot.
//
// WHY A LINK IS NOT ENOUGH, said once for the whole file:
//
//   * an EXPOSE_ that goes missing is not a link error. Nothing in this binary
//     calls CreateInterface; the ENGINE does, at load, by version string, and a
//     game DLL that has stopped registering IServerGameEnts links, starts, and
//     fails when the engine asks. tools/gamedll-interface-census reads the
//     built .so for exactly this and these facts read the source that feeds it;
//   * an EXPOSE_ that is DUPLICATED is worse than one that is missing. Two
//     InterfaceReg entries for one version string do not collide at link time.
//     Both link, and which one the engine gets is decided by static-init order
//     across translation units -- a silent, nondeterministic wrong answer;
//   * the state that came across with each class had to change shape. Stock
//     kept it in file-scope globals and tools/statics-audit gates that shape
//     out of src/sourcesharp, so it is class fields here. A field that is
//     declared and never assigned compiles perfectly and is a dead setting.
//
// Source-read, and forced: every one of these files includes cbase.h and the
// whole entity stack, and none can be linked into a gtest binary.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

bool Exists( const char *pszRel )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return true;   // cannot tell; do not report a deletion that did not happen
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + pszRel ).c_str(), &st ) == 0;
}

const char *kEnts    = "sourcesharp/game/server/shell_gameents.cpp";
const char *kShared  = "sourcesharp/game/server/shell_sharedappsystems.cpp";
const char *kClients = "sourcesharp/game/server/shell_gameclients.cpp";
const char *kTags    = "sourcesharp/game/server/shell_gametags.cpp";
const char *kHeader  = "game/server/gameinterface.h";
const char *kGlobals = "sourcesharp/game/server/shell_engineboundary.cpp";
// WHERE THE RATIONALE LIVES NOW. It used to be a comment beside the $File line
// in the .vpc; the scripts were reformatted with every comment stripped out to
// this document, verbatim and keyed the same way. The convention is unchanged --
// a removed file is NAMED somewhere greppable, so a $File line deleted with no
// explanation is still distinguishable from one deleted by accident -- only the
// place it is named moved.

} // namespace

//=============================================================================//
// STEP 1a — IServerGameEnts
//=============================================================================//

// THE FORCE-TRANSMIT FACT IS RETIRED WITH ITS CONVAR -- plan_shim.md 5 3a,
// item 8, lane 3b-enginebind. It read:
//
//     ForceTransmitEntsIsStillRegisteredAndStillRead
//       "sv_force_transmit_ents" is still in kEnts
//       m_svForceTransmitEnts.GetBool() is still in kEnts
//
// and its reason was that "a cheat convar that silently stops existing reads
// as a working default" and "nothing reads the convar, so setting it does
// nothing". BOTH ARE RIGHT, AND TOGETHER THEY ARE WHY THE CONVAR IS GONE.
//
// The read this fact pinned was `const bool bWouldForceTransmit = bInPVS ||
// m_svForceTransmitEnts.GetBool();` followed by `(void)bWouldForceTransmit;`
// on the next line. So the second assertion was satisfied by a read whose
// value was discarded, and the convar was already in exactly the state the
// first assertion was written to prevent: registered, settable, and with no
// effect on anything. The fact was keeping the LETTER of its own claim while
// the claim itself was false.
//
// It is not replaced by a "the convar is gone" fact. What replaces it is that
// the name is absent from the tree, which
// TheTransmitWalkStillReadsTheNetworkProperty below now states as part of a
// claim that is still about live code.
//
// A REPLACEMENT CONVAR WOULD BE CAUGHT. If sv_force_transmit_ents is ever
// wanted again it belongs beside the managed transmit code that would read it,
// as a [ConVar] attribute -- not as a native declaration, which is the rule
// this item enforces.

//=============================================================================//
// STEP 1b — IServerDLLSharedAppSystems
//=============================================================================//

//=============================================================================//
// STEP 2a — IServerGameClients, and all THREE of its version strings
//=============================================================================//

//=============================================================================//
// STEP 2b — IServerGameTags, and the header registration it came out of
//=============================================================================//

TEST( GameInterfaceSplit, TheHeaderRegistersNothingAtAll )
{
	// THE WHOLE POINT OF STEP 2b. An EXPOSE_ in a header is one registration
	// per INCLUDING translation unit -- six of them, measured with nm on the
	// built server.so, for "ServerGameTags001" alone. Duplicate InterfaceReg
	// entries for one version string do not collide at link time; they are
	// resolved by static-initialisation order.
	//
	// THE HEADER IS GONE, which is the strong form of "registers nothing".
	// game/server/gameinterface.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6, so the scan of its
	// text for EXPOSE_SINGLE_INTERFACE and a CServerGameTags body became an
	// assertion that it is not there at all -- anchored on a sibling in the
	// same directory that IS there, so a probe that cannot see game/server
	// does not pass it.
	EXPECT_TRUE( Exists( "game/shared/shareddefs.h" ) )
		<< "game/shared/shareddefs.h is not readable, so the absence below "
		<< "proves nothing about game/server.";
	EXPECT_FALSE( Exists( kHeader ) )
		<< kHeader << " is readable from the farm again. It once registered "
		<< "interfaces from a HEADER -- one singleton and one InterfaceReg per "
		<< "including translation unit -- and nothing compiles it now.";
}

//-----------------------------------------------------------------------------
// THE CLIENTS CLASS LEFT THE HEADER TOO, at plan_entity.md step 4g — and this
// is a MOVE, not a second copy, so both halves are asserted.
//
// A9 left the declaration in gameinterface.h on a named ground: "shell_server.cpp
// defines GetPlayerLimits against it, and four out-of-build mod files override
// that one method the same way." The four are base_gameinterface.cpp,
// hl2mp_gameinterface.cpp, sdk_gameinterface.cpp and tf_gameinterface.cpp, and
// none of them is compiled by server_sourcesharp — so what the header was sharing with
// stock, it was not sharing with anything compiled. shell_server.cpp still
// defines GetPlayerLimits and now includes the shim header to do it.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ...and the shim header does not register anything either, which is the SAME
// rule the Tags move was made under. Asserted separately because "the class
// moved" and "the move did not carry an EXPOSE_ into a header" are two facts,
// and a header with an EXPOSE_ in it is one singleton per including TU with
// nothing anywhere reporting it.
//-----------------------------------------------------------------------------
//=============================================================================//
// STEP 3 — stock's CServerGameDLL is deleted
//=============================================================================//

// TheRestoreTailSaysWhyItIsNotRePointedAtTheShell went with
// shell_restoreentities.cpp (plan_server_native_min P1): EndRestoreEntities
// returned on its first line forever and the whole path is deleted; there is
// no tail left to keep loud and nothing to re-point. The managed save/restore
// codec (P3c) writes its own refusal.

//=============================================================================//
// STEP 4 — the residue is rehomed and the file is deleted
//=============================================================================//

// TheRestoreTrioIsWholeAndItsStateIsFields went with the file it read
// (plan_server_native_min P1): the trio is deleted whole, not kept-whole.

//=============================================================================//
// The build graph: both files are compiled.
//=============================================================================//

TEST( GameInterfaceSplit, BothNewFilesAreInTheServerProject )
{
	// server_sourcesharp's source list, read through cmake_graph.h
	// (src/server/native/CMakeLists.txt). All six are presence checks, so a
	// reader that saw nothing fails every one.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kEnts ) )
		<< kEnts << " is not compiled by server_sourcesharp "
		<< "(src/server/native/CMakeLists.txt), so IServerGameEnts "
		<< "is not registered by anything.";
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kShared ) ) << kShared;
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kClients ) ) << kClients;
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kTags ) ) << kTags;
	// kRestore left the list with shell_restoreentities.cpp (P1).
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kGlobals ) ) << kGlobals;
}

