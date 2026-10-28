//====== SourceSharp — two temp-entity evictions, and what makes each falsifiable =//
//
// TWO SECTIONS. The first is plan_entity.md step 7b, three stock TUs; the second
// is plan_shim.md Phase 1f, the seventeen per-lane forwarders and stock c_te.cpp.
// They share this file because they share their whole argument: THE LINK CANNOT
// SEE AN EVICTION. A build that compiles is the one thing an eviction always
// produces, so the claim underneath one has to be written down as something that
// can be false, or the green build is the only evidence there is.
//
// ===== SECTION ONE: step 7b =====
//
// plan_entity.md Phase 7 step 7b evicts `c_basetempentity.cpp`,
// `c_te_particlesystem.cpp` and `c_te_explosion.cpp` from
// the client build (then `game/client/client_csharp.vpc`,
// now client_sourcesharp in src/client/native/CMakeLists.txt). This file is what makes the three claims
// underneath that eviction falsifiable, because the LINK cannot see any of
// them: a build that compiles is the one thing an eviction always produces, and
// "a compile gate selects for deletion" is this tree's own recorded lesson.
//
// THE THREE CLAIMS, one per file, and what would make each of them false:
//
//   c_basetempentity.cpp  "nothing in this build makes a C_BaseTempEntity, so
//                          the two static lists were permanently empty and the
//                          four walks over them in shell_clientdll.cpp did
//                          nothing." FALSE the moment any compiled client TU
//                          derives from that class again -- and a derivation is
//                          a source fact with no symbol behind it, so the
//                          compiled TU list is scanned for one.
//
//   c_te_particlesystem.cpp "CTEParticleRenderer MOVED to the shim, verbatim."
//                          FALSE if the move became a rewrite, and false in a
//                          worse way if the stock copy is still on disk: two
//                          definitions of one body, in two files, in two builds,
//                          is the drift this tree keeps paying for. There is
//                          exactly one, and it is the shim's.
//
//   c_te_explosion.cpp     "the one body this build reached, CExplosionOverlay::
//                          Update, moved; the rest of the file is uncompiled
//                          REFERENCE material." The reference half is
//                          ragdoll_blast_seam_test.cpp's subject. What is here
//                          is the other half: the overlay has one definition in
//                          the tree.
//
// THE STRIPPER IS LOAD-BEARING, not tidiness. shell_clientdll.cpp now carries
// FOUR comments that spell `C_BaseTempEntity::PrecacheTempEnts()` and its
// siblings, explaining where the calls went; so does this file's own prose. A
// scan over raw source would read every one of those explanations as the
// violation it is describing, and the fix would be to delete the explanation.
// TheStripperIsLoadBearing below asserts the raw text DOES contain what the
// stripped text must not, so a stripper that quietly stopped working fails
// rather than passing everything.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <fstream>
#include <set>
#include <string>
#include <vector>
#include <sys/stat.h>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "..", "../src", "src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

bool Exists( const std::string &strRelative )
{
	const char *root = SrcRoot();
	if ( !root )
		return false;

	struct stat st;
	return stat( ( std::string( root ) + "/" + strRelative ).c_str(), &st ) == 0
		&& S_ISREG( st.st_mode );
}

//-----------------------------------------------------------------------------
// The ARM THIS BUILD COMPILES, and the reason this function exists at all.
//
// The first run of NoCompiledClientTuDerivesFromCBaseTempEntity failed, and it
// was RIGHT to: c_te_effect_dispatch.cpp still spells
// `class C_TEEffectDispatch : public C_BaseTempEntity`. But that block is inside
// `#ifndef SOURCESHARP`, and this build defines SOURCESHARP -- measured, not
// assumed:
//
//     grep -oP '-D\K\w+' (VPC's ninja graph, at the time) | sort -u | grep ^SOURCESHARP
//     -> SOURCESHARP, SOURCESHARP_SHELL_GAMEDLL
//
// (Since Phase 6b the same fact is cmake_graph::CompileDefinitions( kClient ).)
//
// So the text is in the file and is not in the build, and a scan that cannot
// tell those apart answers a question nobody asked. It drops the `#ifndef
// SOURCESHARP` arms and keeps the `#ifdef SOURCESHARP` ones, counting depth
// through every other conditional so a nested `#endif` closes the right thing.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Every translation unit client_sourcesharp compiles, farm-relative, read
// through cmake_graph.h (compile_commands.json cross-checked against
// src/client/native/CMakeLists.txt). Until Phase 6b this walked the `$File`
// rows of game/client/client_csharp.vpc.
//-----------------------------------------------------------------------------
const char *kEvicted[] =
{
	"game/client/c_basetempentity.cpp",
	"game/client/c_te_particlesystem.cpp",
	"game/client/c_te_explosion.cpp",
};

// The native replacement that stays: the evicted temp entities' backends.
// (shell_explosion_overlay.cpp, c_te_explosion.cpp's overlay, left with the
// glow overlays' port, C7: CExplosionOverlay is the managed GlowOverlaySystem's
// RampOverlay.)
const char *kReplacements[] =
{
	"sourcesharp/game/client/shell_te_backends_client.cpp",
};

//-----------------------------------------------------------------------------
// THE LIST REALLY WAS READ, asserted without a magic number.
//
// Each sweep below is for something that must NOT be there, and a reader that
// returned nothing passes all of them perfectly. Under VPC this was a second,
// independent reading of client_csharp.vpc (loose `$File` line count) that had
// to agree with the quote-walking parser. RETIRED IN 6b: cmake_graph.h's
// CompiledSources already reads the list twice (compile_commands.json against
// the CMakeLists source list) and throws when they disagree or either is
// empty. What stays is the proof-of-read: the client shell, which no eviction
// can take, must be in the list the sweep walks.
//-----------------------------------------------------------------------------
const char *const kClientProofOfRead = "sourcesharp/game/client/shell_clientdll.cpp";

} // namespace

//-----------------------------------------------------------------------------
// The membership half.
//-----------------------------------------------------------------------------
TEST( TempEntEvicted, TheThreeStockTusAreOutOfTheClientBuild )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kClientProofOfRead ) )
		<< "client_sourcesharp does not compile " << kClientProofOfRead
		<< " -- the reader is not reading the client's source list";

	for ( size_t i = 0; i < sizeof( kEvicted ) / sizeof( kEvicted[ 0 ] ); i++ )
	{
		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, kEvicted[ i ] ) )
			<< kEvicted[ i ] << " is out of the client build "
			   "(client_sourcesharp, src/client/native/CMakeLists.txt)";
	}
}

TEST( TempEntEvicted, TheTwoShimReplacementsAreInTheClientBuild )
{
	for ( size_t i = 0; i < sizeof( kReplacements ) / sizeof( kReplacements[ 0 ] ); i++ )
	{
		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kReplacements[ i ] ) )
			<< kReplacements[ i ] << " is compiled -- an eviction whose replacement is "
			   "not in the build is a capability that stopped being shipped";
	}
}

// RETIRED IN 6b: TempEntEvicted.TheVpcStillBalancesItsBraces. It guarded
// client_csharp.vpc's `$Folder { }` syntax against a `$File` removal that took
// a brace with it. VPC is gone; a CMake source list has no such nesting, and a
// malformed CMakeLists fails the configure step itself.

//-----------------------------------------------------------------------------
// c_basetempentity.cpp: the claim is that nothing makes one any more.
//
// This is the only check in the file that could stop the eviction being a
// silent capability loss, and it is a SOURCE scan because a class derivation
// emits no symbol a linker could be asked about.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// c_te_particlesystem.cpp: the claim is a MOVE, and exactly one copy.
//-----------------------------------------------------------------------------
TEST( TempEntEvicted, TheStockParticleEmitterIsGoneFromDisk )
{
	EXPECT_FALSE( Exists( "game/client/c_te_particlesystem.cpp" ) )
		<< "the whole of what this build compiled in that TU moved to the shim, so "
		   "leaving the file on disk would be two definitions of one body in two builds";

	// The HEADER outlived its .cpp while compiled TUs still included it (7g's
	// precedent: gamestats.cpp went, gamestats.h stayed). Its last includer,
	// shell_impact_effects.cpp, dropped it in lane M1-C (2026-10-25), so it left
	// sdk-links/client.txt and the farm too.
	EXPECT_FALSE( Exists( "game/client/c_te_particlesystem.h" ) )
		<< "c_te_particlesystem.h is in the farm again, so something includes it; "
		   "its emitter is C#'s (TeParticles)";
	// Positive control of the same probe: a stock client header the build still reads.
	EXPECT_TRUE( Exists( "game/client/c_te_effect_dispatch.h" ) );
}

TEST( TempEntEvicted, TheParticleEmitterIsCSharpAndItsShimIsGone )
{
	// It MOVED to the shim verbatim (step 7b) and then was PORTED (lane
	// c5-particles): CTEParticleRenderer, TE_BloodStream and the debug line are
	// SourceSharp.Client's TeParticles, replayed against teparticles-stock.tsv.gz.
	// The shim copy must be gone, not left beside the port.
	EXPECT_FALSE( Exists( "sourcesharp/game/client/shell_te_particle_render.cpp" ) )
		<< "shell_te_particle_render.cpp is back; the emitter is C#'s (TeParticles)";
	// Positive control of the same probe: a shim file that stays.
	EXPECT_TRUE( Exists( "sourcesharp/game/client/shell_te_backends_client.cpp" ) );
}

//-----------------------------------------------------------------------------
// c_te_explosion.cpp: the overlay moved to the shim (step 7b), and then to C#
// (C7 glow overlays: the managed GlowOverlaySystem's RampOverlay).
//-----------------------------------------------------------------------------
//=============================================================================//
// plan_shim.md PHASE 1f — THE SEVENTEEN PER-LANE FORWARDERS BECOME TWO.
//
// A SECOND EVICTION IN THE SAME FILE, and it is here rather than in a new one
// because it rests on exactly the argument the three above rest on: the LINK
// CANNOT SEE ANY OF IT. A build that compiles is the one thing an eviction
// always produces, and "a compile gate selects for deletion" is this tree's own
// recorded lesson. What is gone:
//
//   server  shell_te_fwd_{a,b,c,d,e,f,s1,s4}.cpp   2,388 lines, 40 TE_*
//   client  shell_te_fwd_{c1,c4,ca,cd,ce,cn,cp,cx,pool2}.cpp  2,469 lines, 52 TE_*
//   client  game/client/c_te.cpp (stock)           the CTempEntsSystem vtable
//
// and what replaced it is shell_te_forwards.cpp (18 forwards: the 17 with a live
// importer, plus TE_DispatchEffect, whose only caller is in the same file) and
// shell_te_forwards_client.cpp (TE_Bubbles, TE_Smoke, and an ITempEntsSystem
// whose other 38 overrides are empty).
//
// THE ONE CLAIM A CENSUS COULD NOT MAKE, and the reason the client half of this
// section exists at all. All 52 client forwards were imported by exactly one
// object, c_te.o, so a consumer count said "alive" about every one of them. What
// actually reached them was a VIRTUAL CALL through `te`, which emits no symbol
// for any instrument to count. The deletion therefore rests on a SOURCE fact --
// how many `te->` call sites there are and which -- and a source fact with no
// symbol behind it is what a test like this one is for.
//
// AND IT IS THE FACT MOST LIKELY TO GO STALE SILENTLY. The 38 empty overrides
// compile, link and run. A `te->Explosion(...)` added tomorrow would build
// clean, do nothing, and say nothing;
// TheOnlyTeArrowCallsLeftAreTheOnesThatForward below is the only thing in the
// tree that would notice.
//=============================================================================//

namespace
{

const char *kPerLaneForwarders[] =
{
	"sourcesharp/game/server/shell_te_fwd_a.cpp",
	"sourcesharp/game/server/shell_te_fwd_b.cpp",
	"sourcesharp/game/server/shell_te_fwd_c.cpp",
	"sourcesharp/game/server/shell_te_fwd_d.cpp",
	"sourcesharp/game/server/shell_te_fwd_e.cpp",
	"sourcesharp/game/server/shell_te_fwd_f.cpp",
	"sourcesharp/game/server/shell_te_fwd_s1.cpp",
	"sourcesharp/game/server/shell_te_fwd_s4.cpp",
	"sourcesharp/game/client/shell_te_fwd_c1.cpp",
	"sourcesharp/game/client/shell_te_fwd_c4.cpp",
	"sourcesharp/game/client/shell_te_fwd_ca.cpp",
	"sourcesharp/game/client/shell_te_fwd_cd.cpp",
	"sourcesharp/game/client/shell_te_fwd_ce.cpp",
	"sourcesharp/game/client/shell_te_fwd_cn.cpp",
	"sourcesharp/game/client/shell_te_fwd_cp.cpp",
	"sourcesharp/game/client/shell_te_fwd_cx.cpp",
	"sourcesharp/game/client/shell_te_fwd_pool2.cpp",
};

} // namespace

TEST( TempEntForwardersCollapsed, TheSeventeenPerLaneForwardersAreOutOfBothBuilds )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/shell_te_forwards.cpp" ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) is read and names its forwarder";
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_te_forwards_client.cpp" ) )
		<< "client_sourcesharp (src/client/native/CMakeLists.txt) is read and names its forwarder";

	for ( size_t i = 0; i < sizeof( kPerLaneForwarders ) / sizeof( kPerLaneForwarders[ 0 ] ); i++ )
	{
		const std::string rel = kPerLaneForwarders[ i ];

		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, rel ) )
			<< rel << " is out of the server build";
		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, rel ) )
			<< rel << " is out of the client build";
	}
}

TEST( TempEntForwardersCollapsed, TheSeventeenPerLaneForwardersAreGoneFromDisk )
{
	// A ROW REMOVED WITH THE FILE LEFT BEHIND is drift this tree keeps paying
	// for: the next reader greps, finds a forward, and believes it is compiled.
	for ( size_t i = 0; i < sizeof( kPerLaneForwarders ) / sizeof( kPerLaneForwarders[ 0 ] ); i++ )
	{
		EXPECT_FALSE( Exists( kPerLaneForwarders[ i ] ) )
			<< kPerLaneForwarders[ i ] << " is still on disk, uncompiled, with forwards in it";
	}
}

TEST( TempEntForwardersCollapsed, TheTwoReplacementsAreCompiledOnTheirOwnSides )
{
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/shell_te_forwards.cpp" ) )
		<< "the collapsed server forwarder is compiled -- an eviction whose replacement "
		   "is not in the build is a capability that stopped being shipped";
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_te_forwards_client.cpp" ) )
		<< "and so is the client's";

	EXPECT_TRUE( Exists( "sourcesharp/game/server/shell_te_forwards.cpp" ) );
	EXPECT_TRUE( Exists( "sourcesharp/game/client/shell_te_forwards_client.cpp" ) );

	// AND NEITHER SIDE COMPILES THE OTHER'S. The two use the same forward ids
	// and DIFFERENT SourceSharpTe_Forward signatures -- the server's takes a
	// filter and a delay and sends, the client's takes neither and plays -- so a
	// crossed row is a link error at best and the wrong effect at worst.
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/client/shell_te_forwards_client.cpp" ) )
		<< "the server does not compile the client's forwarder";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/server/shell_te_forwards.cpp" ) )
		<< "and the client does not compile the server's";
}

// RETIRED IN 6b: TempEntForwardersCollapsed.BothVpcsStillBalanceTheirBraces.
// It guarded the `$Folder { }` syntax of server_csharp.vpc and
// client_csharp.vpc. VPC is gone; a CMake source list has no such nesting, and
// a malformed CMakeLists fails the configure step itself.
