//========= SourceSharp — the leaf cluster's de-entitying, and why the 85-TU
//          cycle does not move for it =======================================//
//
// plan_entity.md wave 2b, lane `c7-leaf`: clientleafsystem, clientshadowmgr,
// detailobjectsystem, c_pixel_visibility, client_thinklist, studio_stats and
// toolframework_client. Two halves, and the second is the one nobody had
// written down.
//
// HALF 1 — THE RELOCATION IS A MOVE AND NOT A DELETION.
//
// plan_entity.md 3.9: the leaf system and the shadow manager "stay,
// de-entitied: they address renderables through IClientRenderable and handles
// resolved by the slot table, never through C_BaseEntity or
// ClientEntityList()." clientleafsystem.cpp's last three C_BaseEntity reaches
// were the bone-merge render-bounds special case -- Tracker 37433, a stunstick
// posed onto a combine soldier's hand -- and those moved WHOLE into
// shell_renderable_facts.cpp's two seam functions.
//
// A grep for `C_BaseEntity` in clientleafsystem.cpp goes to zero just as
// happily when the special case is DELETED as when it is ported, and deleting
// it is a real regression that renders normally: a bone-merged prop keeps its
// stale own-origin AABB, so it is frustum-culled at the edge of the screen and
// vanishes for a frame or two at a time. Nothing asserts, nothing crashes.
// So every scan below is paired with a fact that the behaviour still EXISTS at
// the far end and that the caller still consults it.
//
// HALF 2 — WHY THE SCC DOES NOT MOVE, MEASURED.
//
// CLIENT_CLUSTERS.md's one rule is p7-exit's: "de-entitying a TU off the entity
// list does NOT take it out of the cycle while it still references
// c_baseentity.o", 85 before and 85 after. This lane found a THIRD answer that
// is neither of the two the rule offers:
//
//   `client_base.vpc` compiled this project with `-include cbase.h`, and
//   cbase.h -> baseentity_shared.h -> c_baseentity.h -> particle_property.h
//   -> particles_new.h. CNewParticleEffect declares ~20 virtuals inline there,
//   so at -O0 EVERY TU that gets the PCH emits its own weak copy of the vtable
//   and leaves 21 out-of-line virtuals undefined -- symbols whose only definer
//   is particles_new.o, which is inside the cycle.
//
// `tools/c7-leaf-cycle` TOOK the numeric reading (70 of 70 PCH'd stock client
// TUs carry that edge; 0 do not). It is DELETED -- one of seven removal-set
// instruments retired in `the-close 11/N` -- and it needed a built tree in any
// case, so the facts here pin the CAUSE and are now the whole of the guard:
// the four include links, and the build setting (a PCH) that would turn them
// into an edge.
// Break any one of them and this file goes red, which is what stops the
// finding rotting into a stale paragraph in a handover. The numbers quoted
// throughout this file are that tool's last readings, and they are HISTORY --
// they are what the facts were calibrated against, not something re-measured
// on every run.
//
//=============================================================================//
#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <sys/stat.h>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// The file with its comments taken out. 7b's recorded shape: a raw needle reads
// non-zero for a file whose CODE reads zero, because the prose explaining what
// went is allowed to spell the name. Every count below is of code.
//
// String literals are preserved so a "//" inside one is not read as a comment.
//-----------------------------------------------------------------------------
bool Contains( const std::string &strHaystack, const std::string &strNeedle )
{
	return strHaystack.find( strNeedle ) != std::string::npos;
}

//-----------------------------------------------------------------------------
// The body of a function, by brace balance from its signature line.
//
// Needed rather than a whole-file scan because the two facts that matter about
// the leaf system's seam are about WHERE the call is: a call to
// SourceSharp_BoneMergeBoundsFromFollowedEntity that is not inside
// DefaultRenderBoundsWorldspace, or one whose result nothing tests, is a file
// that greps clean and has lost the behaviour.
//-----------------------------------------------------------------------------
} // namespace

//=============================================================================
// PART 1 — THE LEAF SYSTEM IS DE-ENTITIED, AND THE BEHAVIOUR IS AT THE FAR END
//=============================================================================

//-----------------------------------------------------------------------------
// plan_entity.md 3.9's sentence about the leaf system, as a check, now about
// its native end: neither file names C_BaseEntity. The stock file's one
// C_BaseEntity sat inside `#ifdef DUMP_RENDERABLE_LEAFS`, defined nowhere; it
// left with the file.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE ANTI-DELETION HALF, REWRITTEN BY LANE 4c-render.
//
// READ THE ASSERTIONS, NOT THE HISTORY. There is no bone-merge seam any more.
//
// What this half used to assert was that Tracker 37433's special case had been
// MOVED to the shim rather than deleted -- a grep for C_BaseEntity in
// clientleafsystem.cpp goes to zero just as happily either way, and a deletion
// renders: a bone-merged prop keeps its stale own-origin AABB and is culled at
// the screen edge for a frame at a time, with no assert and no crash.
//
// THE MOVE HAPPENED AND THEN THE MOVED BODY WAS MEASURED. Both arms were
// `return false;` and nothing else, because the resolve they depended on
// (SDKEntityFromRenderable) walks the partition before it asks GetBaseEntity()
// and this link line has no SDK C_BaseEntity for it to answer with. So the
// special case had not applied to anything in any build that ever ran, and the
// two calls were two branches never taken. Lane 4c-render deleted the seam and
// both call sites.
//
// THE CLAIM THAT SURVIVES IS THE ONE THAT CAN STILL FAIL: each caller must
// still produce a box for every renderable, by its own general path, and must
// reach no entity to do it. A deletion that took the general path with the
// seam would leave an uninitialised AABB, which is the SAME rendering failure
// the old facts were about -- so the successor is not a weaker check, it is the
// same failure asserted at the only place that still has a subject.
//-----------------------------------------------------------------------------
//=============================================================================
// PART 1b — THE SHADOW MANAGER: RETIRED WITH ITS SUBJECT
//
// 2026-10-19, C7 cluster 12 S2: clientshadowmgr.cpp, the client's last fork
// TU, left client.so. The shadow manager is SourceSharp.Client's
// (src/client/managed/Render/Shadows/), its native residue
// shell_shadows_client.cpp and the forwarder library's fwd_shadows.cpp.
//
// Four facts stood here, all read out of the fork's clientshadowmgr.cpp: that
// its code named no SDK entity type (paired with its FirstShadowChild /
// NextShadowPeer walks and docs/absence-client.md row 15), that the bone-setup
// queue was gone and the threaded setup branch was not, that
// ComputeExtraClipPlanes still added its three planes and no fourth, and that
// AddShadowToReceiver added to a static prop only on the flashlight path. The
// file is gone from the farm, so each had nothing left to read; they are
// retired with it, as the leaf system's stock-file facts were at L2. What the
// managed port does on those paths is held against stock by the shadow
// manager oracle (ShadowManagerOracleTests.cs), not by a source scan. What
// stays is the eviction itself, on the leaf system's model above.
//=============================================================================

namespace
{
}

//=============================================================================
// PART 2 — WHY THE CYCLE DOES NOT MOVE
//=============================================================================

//-----------------------------------------------------------------------------
// THE PCH CHAIN. Four links, each asserted separately so a red says which one
// went. The numeric reading used to come off a built tree; the tool that took
// it is deleted, so this IS the guard rather than a companion to one.
//-----------------------------------------------------------------------------
TEST( LeafCyclePch, NoPrecompiledHeaderDirectiveExistsInThisProjectAnyMore )
{
	// This case used to assert that every TU in THIS project was compiled with
	// `-include cbase.h`, and that a per-file "Not Using Precompiled Headers"
	// block was what took one off it -- c_pixel_visibility.cpp being the worked
	// example, the one file of the seven with no edge to particles_new.o.
	//
	// plan_shim.md 0b deleted the mechanism from client_csharp.vpc, so the
	// reading this lane took is HISTORY rather than a live property, and what
	// is pinned now is that it cannot come back without saying so.
	//
	// RETIRED (Phase 6b): three assertions that Valve's stock client_base.vpc
	// still carried `$Create/UsePrecompiledHeader`, `$Create/UsePCHThroughFile`
	// and "cbase.h" -- kept as the record of the shape the reading was taken
	// against. The stock vpcs describe Valve's stock build, which this repo
	// never ran and which has left the tree; the shape is described above.
	//
	// The build this repo DOES run is client_sourcesharp
	// (src/client/native/CMakeLists.txt), read through cmake_graph.h. The
	// worked example itself left client.so (C7: c_pixel_visibility.cpp's
	// decisions are C#'s PixelVisibilitySystem), so the compile command read
	// is its executor's, a TU that starts with cbase.h as the example did.
	const char *const kPixelVis = "sourcesharp/game/client/shell_pixelvis_client.cpp";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/c_pixel_visibility.cpp" ) )
		<< "c_pixel_visibility.cpp is back in the build";
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kPixelVis ) )
		<< kPixelVis << " has left the build, so there is no compile command "
		<< "to read the reading's shape from";

	EXPECT_FALSE( Contains( cmake_graph::ListText( cmake_graph::kClient ), "target_precompile_headers" ) )
		<< cmake_graph::ListFile( cmake_graph::kClient ) << " declares a "
		<< "precompiled header again. Every TU in the project is then compiled "
		<< "with cbase.h whatever its own include list says, and the chain above "
		<< "becomes a link EDGE again for all of them at once.";

	// What the compiler is actually handed: CMake's PCH arrives as a forced
	// `-include .../cmake_pch.hxx`; a forced include of cbase.h is the stock
	// shape under another name.
	const std::string cmd = cmake_graph::CompileCommand( cmake_graph::kClient, kPixelVis );
	EXPECT_FALSE( Contains( cmd, "cmake_pch" ) )
		<< kPixelVis << " is compiled with a PCH:\n" << cmd;
	EXPECT_FALSE( Contains( cmd, " -include" ) )
		<< kPixelVis << " is compiled with a forced include, and there is "
		<< "nothing left for a file to opt out of:\n" << cmd;
}

//-----------------------------------------------------------------------------
// THE KEY FUNCTION. This is the change the fact above calls "the single
// cheapest change to the client's cycle anyone has found", and it is one
// function body moved between two files.
//
// GCC emits a class's vtable, typeinfo and thunks in the single object that
// defines the class's KEY FUNCTION -- the first non-pure virtual the class
// declares without a body. Give that function an inline body and the class has
// no key function: every TU that sees the declaration emits its own weak
// vtable, which references the class's out-of-line members and leaves them
// undefined. That is where the 21 CNewParticleEffect symbols, and
// clientleafsystem.o's DefaultRenderBoundsWorldspace behind
// CDefaultClientRenderable::GetRenderBoundsWorldspace, came from in 73 of the
// client's 190 stock objects -- none of which calls a particle function.
//
// Measured on the built tree by the now-retired c7-leaf-cycle, one change
// apart -- these two rows are the recorded reading, not a live one:
//     inline in the header      largest SCC 85 TUs, 104319 lines, 881 edges
//     out of line in the .cpp   largest SCC 78 TUs, 100702 lines, 746 edges
//
// A fact that only looked in the .cpp would pass with the body in BOTH places,
// which still leaves the class without a key function, so both halves are
// asserted -- on comment-stripped code, because the prose in both files now
// names the function repeatedly.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. THE INSTRUMENT IS GONE, AND THIS FACT IS
// WHAT STANDS IN ITS PLACE.
//
// tools/c7-leaf-cycle was deleted in `the-close 11/N` with six other
// removal-set instruments. It needed a BUILT TREE, so it was never run by any
// gate; what it produced was the two recorded readings quoted above, and the
// argument it carried is written out in this file's own banner.
//
// So the claim is inverted and kept: the tool is absent, and the property its
// correlation was about -- CNewParticleEffect having a key function, in exactly
// one object -- is pinned HERE, as text, on every run. That is strictly more
// often than a tool nobody could run without a build.
//-----------------------------------------------------------------------------
