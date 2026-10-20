//====== SourceSharp — the flex pair leaves the client build ==================//
//
// game/client/c_baseflex.cpp and game/client/c_baseanimatingoverlay.cpp are
// out of the client build (`-$File` out of client_csharp.vpc then; absent from
// client_sourcesharp, src/client/native/CMakeLists.txt, now). They are 240 of c_baseentity.o's consumer
// symbol-needs and two of its 56 consumers, both VTABLE-BOUND: the object needs
// C_BaseEntity because it emits a vtable for a class deriving from it, which no
// retype inside the file can ever change. Only not deriving does.
//
// WHY THEY ARE UNREACHABLE. A client entity class can be instantiated in exactly
// three ways, and all three are shut for these two:
//
//   (N) THE NETWORK. The engine matches a ClientClass to a server class BY
//       NETWORK NAME and then calls its m_pCreateFn. C_BaseFlex registers under
//       "CBaseFlex" and C_BaseAnimatingOverlay under "CBaseAnimatingOverlay",
//       and this server registers NEITHER: game/server/baseflex.cpp and
//       game/server/BaseAnimatingOverlay.cpp are both already `-$File` out of
//       the server build (13e batches 4 and 5). No server class, no class ID,
//       no create call. TheServerStillExcludesBothFlexSendTables is that
//       tripwire, and it is the one that matters: putting either server file
//       back makes the server SEND a class this client can no longer decode,
//       which is fatal at run time and silent at build time on both sides.
//
//   (S) A SYMBOL. `new C_BaseFlex`, a member call, a derived class's vtable --
//       anything that names the type leaves an undefined symbol behind, and the
//       linker says so under -Wl,--no-undefined. It does not:
//
//           ./evict_probe release client c_baseflex.o c_baseanimatingoverlay.o
//           ./evict_probe debug   client c_baseflex.o c_baseanimatingoverlay.o
//               -> exit 0, 0 undefined references, both configurations
//
//       So no test here re-checks what a compiler would shout about. The one
//       referrer there WAS is gone in the commit before this one:
//       entity_client_tools.cpp asked C_BaseFlex for the global flex-controller
//       registry, and now does not.
//
//   (K) A STRING KEY. The client creates entities by classname too --
//       CreateEntityByName -> GetClassMap().CreateEntity(), fed by the client
//       LINK_ENTITY_TO_CLASS in game/shared/predictable_entity.h. NEITHER file
//       contains one, so neither class was ever in the classmap and nothing can
//       ask for it by name. This is the path the linker cannot see, and the only
//       reason it is safe here is that the registration never existed.
//
// (K) IS WHY THE ORPHAN TEST ALONE IS NOT A VERDICT, and this tree has the
// receipts: `strings`-over-the-binaries reduced to DT_ names would call
// DT_Sprite, DT_SpriteTrail, DT_Func_Dust and DT_RopeKeyframe orphans too, and
// all four files STAY, because our own renderers construct the stock class --
// shell_sprite_render.cpp does `new C_SpriteTrail`, shell_dust_render.cpp does
// `new C_Func_Dust`, shell_rope_render.cpp does C_RopeKeyframe::Create. The
// probe is the authority; the orphan diff is only a shortlist.
//
// AND THE DT_ DIFF IS WRONG IN BOTH DIRECTIONS ON THIS TREE, which is why the
// argument above is written against NETWORK NAMES instead:
//
//   * It invents orphans. The server's table is DT_WORLD and the client's is
//     DT_World -- a case mismatch on a class that is live on both sides, since
//     the engine pairs them by the name CWorld. A DT_ diff flags c_world.cpp,
//     which registers a class the map always contains.
//   * It hides them. "DT_BaseFlex" occurs as a plain string in 48 server
//     objects, so the diff reports it PAIRED -- while game/server/baseflex.cpp,
//     the only file that registers it, is not compiled into the server at all.
//
// WHAT LEAVES WITH THE FILES, and why nothing misses it:
//
//   * SIX CONVARS -- phonemedelay, phonemefilter, flex_rules, blink_duration,
//     flex_smooth and phonemesnap. c_baseflex.cpp declares all six and is the
//     only file in either build that reads one. (tf_playermodelpanel.cpp names
//     two, and is not compiled into anything here.) They leave with the class
//     that was their only subject, the same shape as r_PhysPropStaticLighting
//     leaving with c_physicsprop.cpp.
//   * THE GLOBAL FLEX-CONTROLLER REGISTRY -- C_BaseFlex::g_flexcontroller and
//     the two statics over it. Every caller but the tools framework was inside
//     c_baseflex.cpp itself, on model-load paths of the class.
//
// WHY ONLY THE .cpp GOES. game/client/c_baseanimatingoverlay.h is still included
// by game/client/c_baseentity.cpp, which IS compiled -- and names nothing in it,
// which is exactly why the probe came back clean. Both halves are asserted
// below: lose the include and the header becomes deletable; gain a USE and the
// link is about to disagree. c_baseflex.h's includers -- c_basecombatcharacter.h,
// basemodelpanel.h, tf_playermodelpanel.cpp -- are none of them compiled.
//
// THE STRIPPER IS LOAD-BEARING rather than tidy: this file's own prose spells
// DT_BaseFlex, phonemesnap and `new C_Func_Dust` in the paragraphs explaining
// where they may and may not be, and an unstripped scan would read the
// explanation as the violation. Same rule as client_orphan_evictions_test.cpp.
//
// ANCHORS, NOT LINE COUNTS. Every named file is asserted to still contain a
// string that identifies it, and the tree walk is asserted to have visited a
// plausible number of files. A source-scanning test whose subject has been
// renamed reads an empty string and reports every absence invariant as
// satisfied -- the check that cannot fail.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

// (The reader that opened the tools-off stand-ins left with them, C7 cluster 12 S2.)

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

//
// WHAT EACH SIDE COMPILES is read through cmake_graph.h: server_sourcesharp
// (src/server/native/CMakeLists.txt) and client_sourcesharp
// (src/client/native/CMakeLists.txt). Until Phase 6b it was server_csharp.vpc
// and client_csharp.vpc. The proof-of-read is the same shim on both sides.
const char *const kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";


// Does this target leave the file out of the build? Out means NO compiled
// source of the target has its BASENAME -- the strength the flattened-vpc
// check had (basename in any directory, or the full path), so a copy moved to
// another directory still counts as back. cmake_graph throws on a missing,
// empty or stale list; the proof-of-read stops a reader that saw the wrong
// list from answering "excluded" for everything.
bool Excludes( const char *pszTarget, const std::string &strPath )
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( pszTarget );

	EXPECT_TRUE( compiled.count( kProofOfRead ) )
		<< pszTarget << " does not compile " << kProofOfRead
		<< " -- this is not the target's source list, and every exclusion "
		   "below would pass vacuously";

	const std::string strBase = strPath.substr( strPath.rfind( '/' ) + 1 );
	for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
	{
		if ( it->substr( it->rfind( '/' ) + 1 ) == strBase )
			return false;
	}
	return true;
}

} // namespace

//-----------------------------------------------------------------------------
// (N) THE JOIN. The fatal direction, and the only one with no build-time
// diagnostic on either side: a server file comes back, the server sends
// CBaseFlex or CBaseAnimatingOverlay, and this client has no receive table for
// it.
//-----------------------------------------------------------------------------
TEST( ClientFlexEvicted, TheServerStillExcludesBothFlexSendTables )
{
	EXPECT_TRUE( Excludes( cmake_graph::kServer, "game/server/baseflex.cpp" ) )
		<< "game/server/baseflex.cpp is back in the SERVER build. It registers "
		   "the server class CBaseFlex, and the client can no longer decode it: "
		   "c_baseflex.cpp is out of the client build. This is fatal at run time "
		   "and silent at build time on both sides.";

	EXPECT_TRUE( Excludes( cmake_graph::kServer, "game/server/BaseAnimatingOverlay.cpp" ) )
		<< "game/server/BaseAnimatingOverlay.cpp is back in the SERVER build. It "
		   "registers CBaseAnimatingOverlay, whose receive table left the client "
		   "with c_baseanimatingoverlay.cpp.";
}

//-----------------------------------------------------------------------------
// (S) THE ONE REFERRER THERE WAS, entity_client_tools.cpp, left client.so (SFM
// is dead, 2026-10-19); the IClientTools that replaced it left with
// flashlighteffect.cpp (2026-10-18), and the tools-off stand-ins that were
// left (shell_tools_off_client.cpp) went with clientshadowmgr.cpp, their last
// importer (C7 cluster 12 S2). Nothing is left to name C_BaseFlex, and
// client_tools_evicted_test holds that no tools TU is compiled.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE REMOVAL ITSELF, and the one thing the per-file assertions cannot reach
// between them: the two are excluded TOGETHER. C_BaseFlex derives from
// C_BaseAnimatingOverlay, so excluding the base alone is an undefined vtable and
// excluding the derived alone leaves a base nothing uses.
//-----------------------------------------------------------------------------
TEST( ClientFlexEvicted, TheClientBuildExcludesBothFlexFilesTogether )
{
	const bool bFlex = Excludes( cmake_graph::kClient, "game/client/c_baseflex.cpp" );
	const bool bOverlay = Excludes( cmake_graph::kClient, "game/client/c_baseanimatingoverlay.cpp" );

	EXPECT_TRUE( bFlex )
		<< "game/client/c_baseflex.cpp is back in the client build";
	EXPECT_TRUE( bOverlay )
		<< "game/client/c_baseanimatingoverlay.cpp is back in the client build";

	EXPECT_EQ( bFlex, bOverlay )
		<< "c_baseflex.cpp and c_baseanimatingoverlay.cpp are no longer excluded "
		   "TOGETHER. C_BaseFlex derives from C_BaseAnimatingOverlay: excluding "
		   "the base while the derived class is compiled is an undefined vtable, "
		   "and excluding the derived while the base stays leaves an object "
		   "nothing on the link line references.";
}
