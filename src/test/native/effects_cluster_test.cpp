//========= SourceSharp — plan_entity.md step 4a's cluster, file by file ======//
//
// WHAT THIS FILE IS. Step 4a owns nine files: seven stock translation units and
// two shim ones. FOUR of the seven have left the server build and THREE have
// not, and the difference between "has not left yet" and "was quietly forgotten"
// is exactly the thing a status note cannot keep true. So it is a test.
//
// THE FOUR THAT LEFT, and what each cost:
//
//   game/server/gametrace_dll.cpp   MOVED WHOLE to
//                                   sourcesharp/game/server/world_subject_dll.cpp.
//                                   Two of its five definitions were written by
//                                   this port and the other three are
//                                   CGameTrace's own accessors; no entity, no
//                                   registration. -4 shim cross-edges.
//
//   game/server/sound.cpp           THREE functions moved to
//                                   sourcesharp/game/server/shell_sentences.cpp
//                                   and TEN were deleted, with their
//                                   declarations left in game/server/util.h so a
//                                   returning caller gets a link error naming the
//                                   symbol. -2 shim cross-edges, and -7 cast
//                                   sites that went with the deleted functions.
//
//   game/server/gib.cpp             PORTED. ss_gib and Gibs in
//                                   SourceSharp.Server; see
//                                   gib_evicted_test.cpp, which was the file
//                                   recording the refusal this overturned.
//                                   -5 shim cross-edges.
//
//   game/shared/effect_dispatch_data.cpp
//                                   Server side only. It contributed ONE thing
//                                   to the server -- the DT_EffectData send
//                                   table -- and the only server file that ever
//                                   referenced it, te_effect_dispatch.cpp, left
//                                   with plan_tmp.md Part 7. TE_DispatchEffect
//                                   marshals every field by hand to a managed
//                                   temp entity now (shell_te_fwd_s4.cpp) and
//                                   asks the table nothing.
//
// AND THE JOIN HAZARD plan_server_all.md §7 attaches to that file DOES NOT
// APPLY TO THIS BUILD, which is a measurement rather than an argument. §7 says
// DT_EffectData "has to leave both sides in ONE commit or the client join
// hangs", and the mechanism is a REGISTERED SERVER CLASS with no client
// counterpart. There is no such class on either side:
//
//     $ nm -C game/mod_sharp/bin/linux64/server.so | grep -c TEEffectDispatch
//     0
//     $ nm -C game/mod_sharp/bin/linux64/client.so | grep -c TEEffectDispatch
//     0
//
// c_te_effect_dispatch.cpp's IMPLEMENT_CLIENTCLASS_EVENT_DT -- the only thing
// that would reference REFERENCE_RECV_TABLE( DT_EffectData ) -- is inside
// `#ifndef SOURCESHARP` and this build does not compile it. What was removed on
// the server is a bare SendTable that no ServerClass pointed at, and a bare
// table is not in the class list the join matches.
//
// It STAYS in the client build (client_csharp.vpc then, client_sourcesharp in
// src/client/native/CMakeLists.txt now) because step 4a owns the server's project
// script for this cluster and not the client's. It is inert there too -- no
// client object holds an undefined reference to DT_EffectData -- so the client
// row is a candidate for whoever owns that side, with the same evidence.
//
// THE THREE THAT DID NOT, and this is the half worth having. Each has ZERO shim
// cross-edges -- nothing this port wrote needs any of them -- and each is held
// by one or two STOCK translation units that belong to a different step. Named,
// with the symbol and the owner, because "ready" is a claim somebody has to be
// able to check:
//
//   game/server/EntityDissolve.cpp  baseanimating.o (CEntityDissolve::Create x1)
//                                   ragdoll_shared.o (CEntityDissolve::Create,
//                                   typeinfo for CEntityDissolve).
//                                   Owner: 4f for baseanimating, 6e/4a's split
//                                   for ragdoll_shared. The SHIM's own dissolve
//                                   is a declared refusal (below).
//
//   game/server/RagdollBoogie.cpp   physics_prop_ragdoll.o
//                                   (CRagdollBoogie::Create). Owner: Phase 3's
//                                   exit, which takes physics_prop_ragdoll.cpp
//                                   out of the vpc.
//
//   game/server/damagemodifier.cpp  baseentity.o (GetModifier,
//                                   IsDamageDoneToMe). Owner: 4f, the base
//                                   itself. CDamageModifier is a member of
//                                   CBaseEntity and cannot outlive it.
//
// Measured with tools/4a-owes, which lists for one stock object every OTHER
// object in the server link that still references a symbol it defines. It is a
// FLOOR for the reason the census's section 3 is one: a virtual call, an inlined
// header body and a data-only dependency all emit no undefined symbol.
//
// AND TWO DECLARED REFUSALS, because plan_entity.md §7's whole warning about
// this phase is that the temptation on every batch is a stub: "the stub files
// must be empty at the phase exit, and a behaviour that is refused is a
// ContractBaseline entry with a reason, never a silent no-op". Two slots in this
// cluster are now empty bodies, and both of them SAY SO on the console with a
// Warning naming themselves. That is asserted here, because an empty body that
// returns 0 and an empty body that returns 0 loudly are indistinguishable to
// every other test in the tree.
//
// ANCHORS, NOT LINE COUNTS, and the stripper is load-bearing: this file's own
// prose names every symbol it asserts is absent.
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
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadRaw( const std::string &strAbsolute )
{
	std::ifstream file( strAbsolute.c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return all;
}

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string NoWhitespace( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

// Whole file, comments gone, with the anchor checked so a renamed or rewritten
// file is RED rather than vacuously satisfying every absence invariant below.
std::string Load( const char *pszRel, const char *pszAnchor )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	const std::string all = ReadRaw( std::string( pszRoot ) + "/" + pszRel );

	EXPECT_FALSE( all.empty() ) << "could not open " << pszRel
		<< " — every invariant about it would pass vacuously";

	const std::string stripped = StripComments( all );

	EXPECT_NE( NoWhitespace( stripped ).find( NoWhitespace( pszAnchor ) ), std::string::npos )
		<< pszRel << " does not contain its anchor \"" << pszAnchor
		<< "\" outside of comments";

	return stripped;
}

// WHAT EACH DLL COMPILES is its CMake target's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/{server,client}/native/CMakeLists.txt). Paths are farm-relative.
//
// THE PROOF-OF-READ is the PORT'S OWN shim, which no flip can take. (It used to
// be `$SRCDIR\game\shared\baseentity_shared.cpp`, and THE FLIP REMOVED THAT
// ROW along with the rest of the entity hierarchy: a stock TU is not an anchor
// for this port -- removing stock TUs is precisely what the port does, so an
// anchor on one is a proof-of-read the flip can evict.)
const char *kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

const char *kWorldQuery = "sourcesharp/game/server/shell_worldquery.cpp";
const char *kTeFwdS4 = "sourcesharp/game/server/shell_te_forwards.cpp";

} // namespace

//-----------------------------------------------------------------------------
// The four that left.
//-----------------------------------------------------------------------------
TEST( EffectsCluster, TheFourEvictedTusAreOutOfTheServerBuild )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kProofOfRead ) )
		<< "server_sourcesharp does not compile " << kProofOfRead
		<< " -- the reader is not reading the server's source list";

	static const char *kGone[] = {
		"game/server/gametrace_dll.cpp",
		"game/server/sound.cpp",
		"game/server/gib.cpp",
		"game/shared/effect_dispatch_data.cpp",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kGone[ i ] ) )
			<< kGone[ i ] << " is back in the server build. Step 4a took it out; "
			<< "if it is needed again, the question to ask is which object "
			<< "started referencing it (tools/4a-owes).";
	}
}

/// The server needed exactly one thing from effect_dispatch_data.cpp and stopped
/// needing it before step 4a ran. Asserted at the place that replaced it, so
/// "the table is unused" cannot go stale silently.
TEST( EffectsCluster, TheServerDispatchesAnEffectWithoutTheSendTable )
{
	const std::string te = NoWhitespace( Load( kTeFwdS4, "TE_DispatchEffect" ) );

	ASSERT_FALSE( te.empty() );

	// Every field marshalled by hand into the managed temp-entity forward.
	EXPECT_NE( te.find( "SourceSharpTe_Forward(SOURCESHARP_TE_FWD_TE_DISPATCH_EFFECT," ),
	           std::string::npos )
		<< "TE_DispatchEffect no longer forwards to the managed temp entity. If "
		<< "it has gone back to a native CTEEffectDispatch, the server wants "
		<< "DT_EffectData again and effect_dispatch_data.cpp has to come back "
		<< "into server_sourcesharp (src/server/native/CMakeLists.txt) with it.";

	EXPECT_EQ( te.find( "DT_EffectData" ), std::string::npos )
		<< "shell_te_forwards.cpp names DT_EffectData in code again.";
}

//-----------------------------------------------------------------------------
// THE HELD TUs AND THEIR HOLDERS, AFTER THE FLIP.
//
// THIS FACT USED TO BE `TheThreeHeldTusAreStillBuiltAndTheirHoldersAreStillHere`,
// and it was a LEDGER OF PAIRS: each file step 4a could not take out, beside the
// stock TU that was the reason it could not. Its own rule said what a red meant:
//
//     "If a PAIR goes red together, the holder left and the held one is ready --
//      re-run tools/4a-owes and take it out. If only the held one goes red,
//      somebody removed it without checking, and the link will say so."
//
// THE FLIP IS THAT REMOVAL, DONE DELIBERATELY AND ALL AT ONCE, and the ledger
// reads exactly as its own rule predicted -- three pairs red together:
//
//     held  EntityDissolve.cpp        holder  baseanimating.cpp          both gone
//     held  RagdollBoogie.cpp         holder  physics_prop_ragdoll.cpp   both gone
//     held  damagemodifier.cpp        holder  baseentity.cpp             both gone
//
// The FOURTH pair is the one that matters and the one a careless rewrite would
// lose: `ragdoll_shared.cpp` is a §6 file, it did NOT leave, and it was the
// other reason EntityDissolve.cpp could not. It no longer holds it -- the armed
// tree LINKS, which is the only evidence that settles a holder question, and
// anim_cluster_ledger_test pins the source-level half (ragdoll_shared.cpp
// constructs no C_EntityDissolve).
//
// So the ledger is REWRITTEN AND STAYS TWO-SIDED. The seven rows the flip took
// are asserted ABSENT -- a re-appearance means somebody restored a TU the flip
// removed, and then the holder reasoning above is live again. The one row that
// did NOT go is asserted PRESENT, which is the original tripwire unchanged: §6
// keeps ragdoll_shared.cpp and no lane may take it out. Neither half can pass by
// the other being true.
//-----------------------------------------------------------------------------
TEST( EffectsCluster, TheFlipTookEveryHeldTuAndEveryHolder )
{
	// The three held TUs, and the three holders that went with them.
	static const char *const kGone[] = {
		"game/server/EntityDissolve.cpp",
		"game/server/RagdollBoogie.cpp",
		"game/server/damagemodifier.cpp",
		"game/server/baseanimating.cpp",
		"game/server/physics_prop_ragdoll.cpp",
		"game/server/baseentity.cpp",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, kGone[ i ] ) )
			<< kGone[ i ] << " is back in the server build. The flip took it out "
			<< "with the rest of the entity hierarchy; a TU of that hierarchy "
			<< "compiling again is not a small change, and the 4a holder "
			<< "reasoning in the header above becomes live with it.";
	}

	// AND THE HALF THAT IS NOT AN ABSENCE. ragdoll_shared.cpp is §6's
	// ragdoll-builder row: it STAYS -- on the CLIENT. P5 lane SP (2026-10-01)
	// took it out of the SERVER build: nm over every server_sourcesharp object
	// found no reference to any of its 62 defined symbols, and the linked
	// server.so kept nothing of it but TU-local constant initialisers. So the
	// server's proof that the file list read at all is now a file it does
	// compile, and the row itself is pinned where it lives.
	// (shell_serversystems.cpp was that file until it left with kServerSystems[],
	// plan_server_native_v2 T4e.)
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/server/shell_frame.cpp" ) )
		<< "the server's source list does not name shell_frame.cpp, so the "
		<< "six absences above are vacuous rather than measured.";
	// plan_physics_bones_native_zero Phase 3 retired the §6 pin: the file's
	// primitives and key handlers are SourceSharp.Client's RagdollSolver, and
	// the file left the client and the repository (RagdollEvictionTests).
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/shared/ragdoll_shared.cpp" ) )
		<< "sourcesharp/game/shared/ragdoll_shared.cpp is back in the client build; the "
		<< "client's ragdolls are SourceSharp.Client's.";
	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/shared/ragdoll_shared.cpp" ) )
		<< "sourcesharp/game/shared/ragdoll_shared.cpp is back in the server build, where "
		<< "no object calls any function it defines.";
}

//-----------------------------------------------------------------------------
// The two retired slots are GONE, refusals and all (plan_server_native_v2 T2).
//-----------------------------------------------------------------------------
TEST( EffectsCluster, TheGibAndDissolveSlotsAreGone )
{
	const std::string wq = NoWhitespace( Load( kWorldQuery, "SourceSharp_TraceLine" ) );

	ASSERT_FALSE( wq.empty() );

	// (a) The gib slot: no managed code called it. A gib is ss_gib, made by
	// SourceSharp.Server's Gibs (gib_evicted_test pins the managed maker).
	EXPECT_EQ( wq.find( "SourceSharp_ShootGib(" ), std::string::npos )
		<< "the gib slot is back; gibs are managed.";

	// (b) The dissolve slot: no managed code called it either. Its gap is
	// recorded in docs/todo.md (anim_cluster_ledger_test pins the record).
	EXPECT_EQ( wq.find( "SourceSharp_EntityDissolve(" ), std::string::npos )
		<< "the dissolve slot is back; a refusal or a port, not a silent 0.";

	// And the dissolve really is gone from the code rather than merely quiet:
	// this is the `Dissolve` edge step 4d's handover named as 4a's, and it was
	// one of baseanimating.o's eight.
	EXPECT_EQ( wq.find( "pAnimating->Dissolve(" ), std::string::npos )
		<< "shell_worldquery.cpp calls CBaseAnimating::Dissolve again; "
		<< "baseanimating.o has its Dissolve edge back.";
}
