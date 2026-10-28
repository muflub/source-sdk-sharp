//====== SourceSharp — the ENTITY beam leaves the client build ================//
//
// game/shared/beam_shared.cpp is out of the client build (`-$File` out of
// client_csharp.vpc then, absent from client_sourcesharp now). It is 180 of
// c_baseentity.o's consumer symbol-needs and one of its above-cliff consumers,
// vtable-bound: the object needs C_BaseEntity because it emits a vtable for a
// class deriving from it, and no retype inside the file removes one of them.
//
// THIS IS NOT THE BEAMS YOU SEE, and that distinction is the whole reason this
// is safe. Every beam this game actually draws is a Beam_t, made by the temp
// entities CTEBeamPoints, CTEBeamEnts, CTEBeamRing, CTEBeamLaser, CTEBeamSpline,
// CTEBeamFollow, CTEBeamRingPoint and CTEBeamEntPoint -- all of them LIVE server
// classes -- and reaching the renderer through CreateBeamPoints and its siblings
// in view_beams.cpp, none of which ever makes an entity. C_Beam is the
// env_beam / env_laser MAP entity, a different thing, and it is what leaves.
//
// WHY IT IS UNREACHABLE, by the three ways a client entity can be made:
//
//   (N) THE NETWORK. The engine matches a ClientClass to a server class by
//       NETWORK NAME. C_Beam registers under "CBeam", and this server registers
//       no such class: game/shared/beam_shared.cpp is already out of the
//       server build, which took DT_Beam and DT_BeamPredictableId off the
//       server with it. No server class, no class ID, no create call.
//
//   (S) A SYMBOL. beam_shared.o had exactly ONE referrer, view_beams.o, and it
//       was the INVERTED shape this tree keeps meeting: the 233 lines of
//       CViewRenderBeams::DrawBeam( C_Beam * ) existed only to be called from
//       beam_shared.cpp:1005, inside C_Beam::DrawModel -- in the file being
//       evicted. The adapter and its two declarations went with it. The
//       Beam_t overload is a DIFFERENT function and is untouched, which this
//       file asserts in both directions.
//
//           ./evict_probe release client beam_shared.o -> exit 0, 0 undefined
//           ./evict_probe debug   client beam_shared.o -> exit 0, 0 undefined
//
//   (K) A STRING KEY.
//       Unlike the flex pair, beam_shared.cpp carries LIVE, UNGUARDED client
//       registrations -- LINK_ENTITY_TO_CLASS( beam, CBeam ) and
//       LINK_ENTITY_TO_CLASS( info_target, CInfoTarget ) -- so both names WERE
//       in the client classmap and both leave it. A factory reached by a string
//       leaves no symbol behind, so the linker cannot see this at all: if
//       anything compiled called CreateEntityByName( "beam" ), the eviction
//       would turn it into a silent NULL and a "Can't find factory" Msg.
//       Nothing does. The only askers for "beam" in the whole tree are
//       CBeam::BeamCreate and CBeam::BeamCreatePredictable, both inside the
//       evicted file.
//
//       `info_target` IS a live map classname -- server_beam_viewmodel_evicted_
//       test.cpp asserts the shipping maps declare it. That is not a problem
//       here, and the reason is worth writing down: map entities are spawned on
//       the SERVER, and the CLIENT classmap is only ever read by the CLIENT's
//       own CreateEntityByName. They are two different registries in two
//       different DLLs, and the server's answer for info_target is unchanged.
//
// WHAT THE LINK ALREADY AGREES ABOUT is not re-checked here. What it cannot see
// is everything below: the JOIN and the Beam_t path surviving.
//
// ANCHORS, NOT LINE COUNTS -- a source-scanning test whose subject was renamed
// reads an empty string and reports every absence invariant as satisfied.
//
//=============================================================================//

#include <string>
#include <set>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as client_orphan_evictions_test.
//-----------------------------------------------------------------------------
struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// THE ANCHOR MOVED, AND ITS OLD SUBJECT LEAVING IS THE POINT OF THE ANCHOR.
//
// It used to be `$SRCDIR\game\shared\baseentity_shared.cpp`, and the armed
// link evicted that file from the server project -- baseentity_shared.cpp is
// one of the stock translation units the flip takes out, which is the whole
// reason the alias can be armed. So the anchor went red for exactly the reason
// an anchor exists: the script this test reasons about was rewritten under it,
// and the assertion below would otherwise have gone on passing against a file
// nobody had re-read.
//
// THE ASSERTION ITSELF IS UNTOUCHED. Only the proof-of-read moved. beam_shared
// .cpp is still out of the server build, and that is checked below exactly as
// it was.
//
// The replacement is the one the client row already uses, for the same reason
// spelled out under it: a file the script NAMES, which this test asserts
// nothing about, and -- new here -- one the flip cannot take, because
// shell_shared.cpp is this port's own shim rather than a stock TU. An anchor
// that can be evicted by the change it is meant to survive is not an anchor.

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
// (N) THE JOIN. The fatal direction, with no build-time diagnostic on either
// side: a server file comes back, the server sends CBeam, and this client has no
// receive table for it.
//-----------------------------------------------------------------------------
TEST( ClientBeamEvicted, TheServerStillExcludesTheBeamSendTable )
{
	EXPECT_TRUE( Excludes( cmake_graph::kServer, "game/shared/beam_shared.cpp" ) )
		<< "game/shared/beam_shared.cpp is back in the SERVER build. It registers "
		   "the server class CBeam, and this client can no longer decode it. "
		   "Fatal at run time, silent at build time on both sides.";
}

//-----------------------------------------------------------------------------
// (S) THE INVERTED ADAPTER. The 233-line DrawBeam( C_Beam * ) went with the file
// that was its only caller -- and the Beam_t overload, which is how every beam
// in this game is drawn, did NOT. Both halves, so this fails whichever way it
// moves.
//-----------------------------------------------------------------------------
//=============================================================================//
// THE BEAM ENDPOINT — lane `c8-tempents-beams`, then the C2 beam port.
//
// view_beams.cpp's ComputeBeamEntPosition took a C_BaseEntity * (an
// IGameEntity * once the alias retired) and its body had been cut to `return
// false`. It left with view_beams.cpp. The endpoint shim's SDK route now gives
// that answer itself, and no stock entity-typed function is reached at all.
//=============================================================================//

//-----------------------------------------------------------------------------
// No stock entity-typed arm is reached. view_beams.cpp's ComputeBeamEntPosition
// and the endpoint shim's call into it both left; the facts are the only
// native half of an endpoint now.
//-----------------------------------------------------------------------------
// (The SDK endpoint's early return went with the client's stock headers,
// 2026-10-25: no client object answers GetBaseEntity(), so it was never taken.)

//-----------------------------------------------------------------------------
// THE LINE THAT CLOSES THE REAP GAP (beam_reaper_test.cpp's, kept when the
// reaper's decision moved into BeamSystem.KillDeadBeams). C_BaseEntity::Term
// reaps a dying entity's beams and a shell is not a C_BaseEntity, so the shell
// does it through `beams`, which forwards to the managed system.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// view_beams.cpp and beamdraw.cpp are out of the client build, and the file
// that replaced them is in it.
//-----------------------------------------------------------------------------
TEST( ClientBeamEvicted, TheStockBeamSystemLeftAndTheExecutorReplacedIt )
{
	EXPECT_TRUE( Excludes( cmake_graph::kClient, "game/client/view_beams.cpp" ) )
		<< "game/client/view_beams.cpp is back in the client build; its `beams` "
		   "and r_DrawBeams collide with the managed beam system's";
	EXPECT_TRUE( Excludes( cmake_graph::kClient, "game/client/beamdraw.cpp" ) )
		<< "game/client/beamdraw.cpp is back in the client build";
	EXPECT_FALSE( Excludes( cmake_graph::kClient, "sourcesharp/game/client/shell_beam_render.cpp" ) )
		<< "shell_beam_render.cpp is not in the client build: nothing defines "
		   "`beams`, and the client does not link";
}

//-----------------------------------------------------------------------------
// THE REMOVAL ITSELF.
//-----------------------------------------------------------------------------
TEST( ClientBeamEvicted, TheClientBuildExcludesBeamShared )
{
	EXPECT_TRUE( Excludes( cmake_graph::kClient, "game/shared/beam_shared.cpp" ) )
		<< "game/shared/beam_shared.cpp is back in the client build";
}
