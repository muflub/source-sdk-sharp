//========= SourceSharp — step p7-exit ========================================//
//
// THE STOCK CLIENT TUs THAT STAY COMPILED ASK THE SLOT TABLE, NOT THE LIST.
//
// plan_entity.md clause 2 of the definition of done: no compiled translation
// unit may name `cl_entitylist` / `ClientEntityList()`. §6 says which client
// files legitimately stay native when the port is finished -- the leaf system,
// the shadow manager, view render, client mode, input, the HUD canvas, the leaf
// renderers -- so for those files "leave the build" was never the answer and
// "stop naming the entity list" always was.
//
// Step 5a had already done this for the SHIM (shell_cliententitylist.h's banner
// records the 34 sites it collapsed). This step does it for the STOCK client
// TUs that stay: the beam list, the legacy tempent list, the think list, the
// particle systems, the impact effects, the move helper and the client's own
// utility TU. Each one's asks are one of seven questions -- an index or a handle
// in, one engine interface out -- and each is answered by
// SourceSharp_Client*From{Index,Handle}, which tries the slot table FIRST.
//
// WHAT IT CHANGES TODAY: NOTHING OBSERVABLE. An earlier draft of this banner
// claimed the migration was a WIDENING -- that cl_entitylist held stock client
// entities only, so these sites had been answering NULL for managed ones. That
// was WRONG. SourceSharp_ClientEntityBind registers every shell in the stock
// list as well as in the slot table (SourceSharp_LegacyListBind is
// cl_entitylist->AddNetworkableEntity), so both spellings resolve the same
// objects. The retraction is kept here rather than deleted, because a false
// "this is a fix" is precisely what a text gate cannot catch and this one was
// quoted twice before the bind path was read.
//
// WHAT IT DOES CHANGE is the direction: clause 2 is satisfied for these files,
// and the resolvers ask the TABLE FIRST, so when the seam goes they keep
// working with no further edit. TheResolversTryTheSlotTableBeforeTheSeam is
// what pins the order -- a body that asked the seam first would answer the
// stock object for an entry both hold, and would keep answering after the
// table stopped being consulted at all.
//
// WHAT IS DELIBERATELY NOT DELETED. Every migrated site keeps stock's own call
// in its `#ifndef SOURCESHARP` arm, because these files are in client_base.vpc
// as well as client_csharp.vpc and the port's rule is the smallest possible diff
// to stock. TheStockArmSurvivesEveryMigration is that clause, asserted rather
// than assumed -- a "no compiled TU names the list" gate is satisfied just as
// well by deleting the stock arm, and that is the cheapest wrong move here.
//
// THE WAIVER LIST IS RECOMPUTED, NOT LISTED. TheOnlyClientTusLeftNamingTheList
// walks every source client_sourcesharp compiles, strips comments, strips the
// preprocessor arms this build does not take, and requires the set of files
// whose LIVE text still names the stock list to be exactly the named set. A new
// stock TU reaching for `cl_entitylist` reddens it without anybody remembering
// to add a name here, and a waived file that stops needing the waiver reddens it
// too.
//
// PROSE IS NOT A SITE and GUARDS ARE NOT CODE. Both passes are the ones
// orphan_clientclass_test.cpp already needed and for the same reasons: this
// file's own banner names `cl_entitylist` four times, and c_te_legacytempents
// names it in a comment about tempents having no row in it.
//
//=============================================================================//

#include <algorithm>
#include <string>
#include <vector>
#include <set>
#include <map>
#include <fstream>
#include <sstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Comments out. The prose in this tree names the very macros the scans look
// for -- this file does it in its own banner -- so an unstripped scan reads the
// explanation as the thing explained.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The body of one function, braces counted rather than the first `}` taken.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// EVERY source the client compiles, as farm-relative paths: client_sourcesharp's
// list, read through cmake_graph.h (compile_commands.json cross-checked against
// src/client/native/CMakeLists.txt).
//
// This was a `$File` walker over client_csharp.vpc, which had to skip `-$File`
// subtractions and follow backslash-continued rows (one carried 16 files) --
// and which took only `$SRCDIR\...` spellings, so the vpc's bare-basename rows
// were never scanned. The CMake list has one spelling for every row, so every
// compiled source is scanned now.
//-----------------------------------------------------------------------------
// The -D definitions of one compile command, as NAME or NAME=VALUE, whole.
std::set<std::string> DefinesOf( const std::string &strCommand )
{
	std::set<std::string> out;
	std::istringstream toks( strCommand );
	std::string strTok;
	while ( toks >> strTok )
		if ( strTok.compare( 0, 2, "-D" ) == 0 && strTok.size() > 2 )
			out.insert( strTok.substr( 2 ) );
	return out;
}

//-----------------------------------------------------------------------------
// A MINIMAL PREPROCESSOR, and it is load-bearing rather than tidy.
//
// It knows exactly the four guard names this build decides:
//   SOURCESHARP          DEFINED on both targets (both target_compile_definitions)
//   CLIENT_DLL           defined on the client project only
//   GAME_DLL             defined on the server project only
//   SS_CBASEENTITY_ALIAS NOT defined anywhere -- the armed guard
// Any OTHER condition keeps BOTH arms, which can only ever over-report a class
// and so cannot hide one.
//
// Without this pass the scan reads c_te_effect_dispatch.cpp's
// `#ifndef SOURCESHARP` arm and invents a fifth client class, CTEEffectDispatch,
// that is in no binary.
//-----------------------------------------------------------------------------
struct GuardWorld
{
	bool bClient;

	bool Known( const std::string &name, bool &bValue ) const
	{
		if ( name == "SOURCESHARP" )
		{
			bValue = true;
			return true;
		}
		if ( name == "CLIENT_DLL" )
		{
			bValue = bClient;
			return true;
		}
		if ( name == "GAME_DLL" )
		{
			bValue = !bClient;
			return true;
		}
		if ( name == "SS_CBASEENTITY_ALIAS" )
		{
			bValue = false;
			return true;
		}
		return false;
	}
};

//-----------------------------------------------------------------------------
// The gate-2 spellings, as plan_entity.md §5 and tools/entity-gates spell them.
// `ClientEntityList` is matched with its opening parenthesis so that the
// resolver names in shell_cliententitylist.h -- SourceSharp_ClientEntityList is
// not one of them, but CSourceSharpClientEntityList IS a class in
// shell_cliententitylist.cpp -- cannot be confused for a call to the stock
// accessor. A leading identifier character before `ClientEntityList` means it
// is part of a longer name; the check below requires the boundary.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The files this step moved. Named here ONLY so that the "the stock arm is
// still there" fact has a subject; membership of the waiver set is recomputed,
// never read from this list.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The compiled client TUs whose LIVE text may still name the stock list, and
// the reason each is waived. Every one of them leaves with the base cluster --
// they are the cluster -- so this list is the Phase 7 exit's remaining work
// written as a test rather than as a sentence.
//-----------------------------------------------------------------------------
struct Waiver
{
	const char *pszFile;
	const char *pszWhy;
};

// THE WAIVER TABLE. It is EMPTY, and the prose below is its history -- every
// row that was ever in it, why it was waived, and which lane took it out.

// LANE armed-link TOOK FOUR OUT HERE, AND THE FLIP IS WHY.
//
// game/client/cliententitylist.cpp, c_baseentity.cpp,
// c_baseentity_statics.cpp and c_baseanimating.cpp were waived because they
// were the stock list and the class it is a cycle around. The armed flip
// removes all four from client_csharp.vpc, so this scan no longer reaches
// them and every one of their waivers matched nothing.
//
// Removed rather than left standing, for the reason the fact below prints
// and for the reason five earlier lanes removed theirs: "a waiver that
// matches nothing excuses whatever is written there next." A waiver on a
// file that is not in the build is not a smaller waiver -- it is an open
// door with no one watching it.
// LANE c8-listseam MOVED THE PORT'S OWN SEAM CODE INTO THIS FILE, and that
// is why it is here rather than being fixed.
//
// Six SourceSharp_LegacyList* bodies and four ask counters were DEFINED in
// game/client/cliententitylist.cpp -- the port's own code inside the
// translation unit the client flip deletes, so the lane that deleted the
// vpc row would have deleted eleven port definitions with it. They are now
// at the foot of shell_cliententitylist.cpp, and naming `cl_entitylist` is
// what they ARE: they are the seam, not a caller of it.
//
// This is a trade and not a fix, so it is written down. Before the move,
// the shim named no entity list and the port's coupling was six functions
// in a file that leaves; after it, the coupling is the same six functions
// in one delimited region of a file the port owns. tools/entity-gates
// row 2 counts the same mentions either way -- they moved file, they did
// not become fewer. What changed is that deleting them is one edit in one
// place with a named owner, instead of a side effect of somebody else's
// vpc commit.
// (shell_cliententitylist.cpp -- "IT IS THE SEAM ITSELF" -- and
// shell_clientdll.cpp -- "ITERATION AND RELEASE" -- stood here until
// `the-close`. See the paragraph at the foot of this block.

// NOT THIS LANE'S FILES. Everything under game/shared/ and
// sourcesharp/game/shared/ is owned WHOLE, on both sides, by lane p4-exit,
// which ran concurrently with p7-exit. A shared file's virtuals differ per
// side, so a one-sided edit here compiles green on the client and breaks
// the server. The ask is written into
// .claude/entity-port/requests/p7-exit.md; each row is an index or a handle
// lookup and each takes the same resolver the client TUs above now take.
// AND TWO MORE, THE SAME WAY AND IN THE SAME COMMIT.
//
// game/shared/baseentity_shared.cpp and game/shared/gamerules.cpp were
// p4-exit's, waived as shared files whose server arm a one-sided edit would
// break. The flip removes both from the CLIENT's list, so the hazard that
// argued for the waiver is gone on this side and the waiver matched nothing.
// The server keeps its own copy of each and this scan never read that.
// game/shared/util_shared.cpp WAS HERE, AND LANE c8-shared6 TOOK IT OUT
// BECAUSE THE TEST SAID TO. Its one row was the client's
// `UTIL_PlayerByIndex`, `ToBasePlayer( ClientEntityList().GetEnt( i ) )`.
// That is now a declared, printed refusal: the viewer is a
// C_StandalonePlayer, IClientUnknown::GetBaseEntity() is NULL for it, and
// client.so links no C_BasePlayer at all, so the lookup could only ever
// answer NULL — and `cliententitylist.cpp` leaves in the client flip while
// util_shared.cpp stays (§6's trace-filter row).
//
// Removed rather than left standing for the reason this fact prints:
// "a waiver that matches nothing excuses whatever is written there next."
// game/shared/voice_status.cpp, game/shared/collisionproperty.cpp and
// game/shared/effect_dispatch_data.cpp WERE HERE AND LANE `f-list` TOOK ALL
// THREE OUT, because the fact below says to and a waiver that matches
// nothing excuses whatever is written there next.
//
// The shared-file hazard the "p4-exit's: shared, both sides" reason names is
// real and none of the three had it: every row was inside an
// `#ifdef CLIENT_DLL` / `#else` pair that already existed in the file, so the
// server arm is untouched in all three by construction.
//
// EACH KEPT ITS FACE RATHER THAN BEING WIDENED. GetBaseEntityFromHandle
// became SourceSharp_ClientSdkEntityFromHandle -- the FILTERED ask, which
// answers NULL for a managed entity by design -- in both files that used it,
// because both then call CBaseEntity members on the result.
// LANE sub-entitylist REMOVED TWO ROWS HERE, and this note is what stops
// them being put back as waivers instead of fixed.
//
// sourcesharp/game/client/sound_emitter.cpp and
// sourcesharp/game/shared/util_shared_library.cpp were waived as
// "p4-exit's: the shim's shared half". Neither is a shared-file HAZARD:
// both rows were CLIENT arms inside `#ifdef CLIENT_DLL`, both were an
// index lookup, and neither file is in a stock vpc, so there is no server
// virtual and no !SOURCESHARP arm to disagree with. They take
// shell_cliententitylist.h's resolvers now, exactly as the nine migrated
// files above do (kMigratedStockArmGone) -- see TheShimsSharedClientArmsResolveThroughTheSlotTable.
//
// The two facts below are the positive half: this list going empty of them
// says only that they stopped NAMING the list, which deletion satisfies
// just as well as routing.
//
// THE LAST TWO ROWS WENT IN `the-close`, AND THE LIST IS NOW EMPTY.
//
// sourcesharp/game/client/shell_cliententitylist.cpp ("it is the seam
// itself") and sourcesharp/game/client/shell_clientdll.cpp ("iteration and
// release") were the two survivors. Every line in either of them that named
// the stock list was inside `#ifndef SS_CBASEENTITY_ALIAS` -- an arm no
// project compiles, since both targets define that macro -- and `the-close
// 1/N` deleted all of them. Both files still NAME cl_entitylist in prose,
// which is why the scan reads LiveText and not the raw file.
//
// Removed rather than left standing, for the reason this fact prints and
// for the reason six earlier lanes removed theirs: "a waiver that matches
// nothing excuses whatever is written there next."
//
// AN EMPTY WAIVER LIST IS THE CLAUSE LANDING, NOT THE FACT WEAKENING. The
// scan's first direction -- no compiled client TU may name the stock list
// -- now has no exceptions at all, and it is the direction that catches a
// new one. The second direction has nothing left to check, which is what
// "zero waivers" means. And because "nobody names it" is also what an empty
// file answers, the two survivors' LIVE SUCCESSORS are asserted by name in
// TheSeamBlockStillAnswersWithoutTheStockList below.
//
// A NULL table rather than a zero-length array: C++ has no empty one, and
// a placeholder row would be a waiver.
} // namespace

//-----------------------------------------------------------------------------
// 1. The three resolvers this step added are built out of the SAME table-first
//    primitive as the two 5a wrote, and not out of the stock list.
//
//    This is the fact that makes the migration a widening rather than a rename:
//    if these bodies asked cl_entitylist, every call site would have been
//    renamed and nothing would have changed.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 2. The primitive itself asks the table BEFORE the seam. Order, not presence:
//    a body that asked the seam first would answer the stock entity for an
//    entry both hold, and the counters would read zero while the port was
//    unused.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 3. THE RECOMPUTED WAIVER SET. Every source client_sourcesharp compiles, comments
//    stripped and dead preprocessor arms stripped: the set of files whose LIVE
//    text still names the stock client entity list must be exactly kWaived.
//
//    Both directions are asserted. A new file naming the list is a failure;
//    so is a waived file that no longer needs its waiver, because a stale
//    waiver is a hole that widens every time somebody edits the file.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 3a. THE LIVE SUCCESSOR OF THE LAST TWO WAIVERS.
//
//     The fact above now reads "no compiled client TU names the stock list",
//     with no exceptions -- and an empty file, a deleted one, and a gutted one
//     all satisfy that. The two files that held the last two waivers therefore
//     have their surviving jobs asserted BY NAME here. Neither job was the
//     naming; the naming was how each did it before the list went.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 3b. THE SHIM'S TWO SHARED FILES, ROUTED RATHER THAN EMPTIED (lane
//     sub-entitylist).
//
//     Fact 3 above now proves neither file NAMES the stock list. That is
//     satisfied just as well by deleting the lookup, which is the failure mode
//     the whole wave brief exists to prevent -- deleting sound_emitter's would
//     silently emit every managed entity's sound as GENDER_NONE, which is the
//     exact defect step 7e closed. So the route is asserted by name.
//
//     The "stock arm survived" rule -- once every migrated row's --
//     deliberately does NOT apply to these two: they
//     are shim files, compiled only by client_sourcesharp and
//     server_sourcesharp, so a !SOURCESHARP arm would be dead text. That
//     assumption is asserted rather than assumed, below.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 3c. THE FOUR STOCK SHARED FILES LANE `f-list` UN-WAIVED, ROUTED RATHER THAN
//     EMPTIED -- the positive half, one fact per file.
//
//     Fact 3 above only proves these stopped NAMING the stock list, and the
//     comment at the head of kWaived says why that is not enough: deleting the
//     lookup satisfies it exactly as well as routing the lookup does, and
//     COMMON.md's "a compile gate selects for deletion" is this port's own
//     recorded failure mode. So each substitution is asserted BY NAME, and the
//     face it must keep is asserted with it.
//-----------------------------------------------------------------------------
// TheSoundPatchResolvesItsIndexPerSideAndItsHandleThroughGet stood here. Its
// subject, game/shared/soundenvelope.cpp's SoundPatchEntityFromNetworkIndex,
// is in neither DLL now: the server's patches are shell_soundenvelope.cpp's
// and the client's shell_soundenvelope_client.cpp's (sound cluster 17, lane
// c2-sims), which keeps the index its one caller passes (0, the world).

TEST( ClientEntityResolve, TheShimsSharedFilesAreBuiltOnlyWithSOURCESHARPSoTheyKeepNoStockArm )
{
	// The reason the "stock arm survived" rule
	// does not reach it: its client arm names shell_cliententitylist.h,
	// which no SOURCESHARP-less build has, so a !SOURCESHARP arm would be dead
	// text in every build that compiles them.
	//
	// RE-POINTED IN PHASE 6b. This read Valve's stock client_base.vpc and
	// server_base.vpc and asserted neither named the two files. Those describe
	// Valve's stock build, which this repo never builds, and they have left the
	// repo. The property that argument was evidence FOR is about OUR build, and
	// is asked of it directly: every target that compiles the file defines
	// SOURCESHARP. A target added without it is the "stock project" the old
	// fact was the reminder for -- put the !SOURCESHARP arm back, the way
	// game/client/fx.cpp carries one.
	// sound_emitter.cpp was the other file here, until the client's emit path
	// went to C# (SourceSharp.Client.Sounds) and it left client.so.
	const char *kShimShared[] = {
		"sourcesharp/game/shared/util_shared_library.cpp",
	};
	const char *kTargets[] = { cmake_graph::kServer, cmake_graph::kClient, cmake_graph::kHost };

	int nCompiling = 0;
	for ( size_t t = 0; t < sizeof( kTargets ) / sizeof( kTargets[0] ); t++ )
	{
		for ( size_t f = 0; f < sizeof( kShimShared ) / sizeof( kShimShared[0] ); f++ )
		{
			if ( !cmake_graph::Compiles( kTargets[t], kShimShared[f] ) )
				continue;
			nCompiling++;
			const std::set<std::string> defs = DefinesOf( cmake_graph::CompileCommand( kTargets[t], kShimShared[f] ) );
			EXPECT_TRUE( defs.count( "SOURCESHARP" ) )
				<< kTargets[t] << " (" << cmake_graph::ListFile( kTargets[t] ) << ") compiles " << kShimShared[f]
				<< " without -DSOURCESHARP. "
				   "Its client arm names shell_cliententitylist.h, which no "
				   "stock build has -- put the !SOURCESHARP arm back, the way "
				   "game/client/fx.cpp carries one.";
		}
	}

	// The proof-of-read: the client compiles it. A reader that found the file
	// nowhere would make every EXPECT above pass by never running.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kShimShared[0] ) )
		<< "client_sourcesharp does not compile the shim-shared file, so "
		   "this fact is not measuring what it says ("
		<< nCompiling << " target/file pairs compiled).";
}

//-----------------------------------------------------------------------------
// 4. THE MIGRATION LEFT NOTHING NAMING THE STOCK LIST -- IN EITHER ARM, FOR
//    NINE OF THE TEN.
//
//    THIS SECTION USED TO READ: "THE STOCK ARM SURVIVED EVERY MIGRATION. The
//    gate this step moves is a text count, and a text count is satisfied just
//    as well by deleting stock's call as by routing around it. These files are
//    in client_base.vpc too. So every migrated file must still contain the
//    stock spelling somewhere -- which, given fact 3 above proved none of them
//    names it in LIVE text, can only be inside a `#ifndef SOURCESHARP` arm."
//
//    THE REASONING IS STILL RIGHT AND ITS PREMISE IS NOT. "These files are in
//    client_base.vpc too" was true of the .vpc file on disk and false of this
//    tree's builds: the only two projects here were client_csharp.vpc and
//    server_csharp.vpc (client_sourcesharp and server_sourcesharp now), both
//    define SOURCESHARP, and nothing ever built client_base.vpc. So the
//    `#ifndef SOURCESHARP` arms those nine files
//    carried were source that no compiler in this tree had ever seen, and the
//    `residue` wave (74b071771..a46e8d645) deleted them. It deleted 5,282 lines
//    across 44 stock TUs and the client's objects came out identical -- same
//    count, same DEFINED and UNDEFINED symbol sets.
//
//    WHAT THE FACT PROTECTS NOW. The original worry is unchanged and still
//    live: a text-count gate is satisfied just as well by DELETING stock's call
//    as by routing around it, and this fact is where that would show. What
//    changed is which observation distinguishes the two. It used to be "the
//    stock spelling is still in the file"; it is now "the port's own resolver
//    shim is still included, and the stock spelling is in NEITHER arm". A file
//    that had its call deleted rather than routed loses the include too -- it
//    has nothing to include it for -- so the anchor is the discriminator the
//    deleted arm used to be.
//
//    THE SCANNER'S POSITIVE CONTROL is TheGuardPassIsWhatSeparatesTheTwoArms
//    (a): it asserts NamesTheStockList finds the stock spelling in a two-armed
//    fragment this file owns, so the nine absences below cannot pass because
//    the scanner stopped working.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 4b. THE BEAM'S ENDPOINT. view_beams.cpp (Beam_t::entity as a CBaseHandle,
//     BeamEntHandle, the seven BeamEndpointFromIndex lookups) left the build
//     with the C2 beam port: a managed beam holds its endpoints as raw
//     EntityHandle values, and the one native lookup left is the executor's
//     BeamEntityAt -- through the slot-table resolver, never the stock list.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 5. The guard pass is doing something, checked against a case that proves it.
//
//    Without StripDeadArms a file with two arms reads as naming the list in
//    both, so facts 3 and 4 would agree with each other for the wrong reason.
//    This is the control.
//
//    THE CONTROL SUBJECT MOVED, AND THE ASSERTION DID NOT. It used to be
//    game/client/view_beams.cpp, which at 74b071771 carried a
//    `#ifndef SOURCESHARP` arm naming the stock list. The `residue` wave
//    (74b071771..a46e8d645) deleted that arm -- nothing in this tree compiles a
//    !SOURCESHARP arm, so it had never been seen by a compiler -- and with it
//    the only thing that made view_beams.cpp a two-armed file. `ASSERT_TRUE(
//    NamesTheStockList( raw ) )` on it is now false, and a control whose input
//    the change under test can delete was never a control.
//
//    SO THE CONTROL IS TAKEN IN TWO PLACES, and the first of them is the one
//    that cannot be evicted at all:
//
//      (a) AN INLINE SOURCE THIS FILE OWNS. StripDeadArms is handed a
//          two-armed fragment written out below. No wave, flip or vpc edit can
//          reach it. It is also where the OTHER direction is asserted: the pass
//          must KEEP the SOURCESHARP arm, not merely drop the stock one -- a
//          pass that returned the empty string would satisfy every
//          `EXPECT_FALSE( NamesTheStockList( live ) )` in this file, and that
//          is the failure this control exists to make impossible.
//
//      (c) A COMPILED FILE THE PASS MUST NOT EMPTY, so that (a) is not just a
//          test agreeing with itself about a string it wrote.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 6. The preprocessor world this file assumes is the one the build sets. If
//    client_sourcesharp stopped defining SOURCESHARP, StripDeadArms would keep
//    the stock arms and facts 3 and 4 would both be measuring nothing.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheClientProjectStillDefinesTheGuardThisFileAssumes )
{
	// THE DEFINE MUST BE THE WHOLE NAME. A bare substring needle misses ADDED
	// TEXT: over the vpc, `$BASE;SOURCESHARP` on its own matched a list that
	// defined SOURCESHARPX and nothing else, and the needle then needed a
	// terminator that broke every time a define was appended after it. The
	// CMake definitions are one name per token, so the question is a set
	// lookup: SOURCESHARPX is not SOURCESHARP, and a later define is not this
	// fact's business.
	//
	// Asked twice: the list src/client/native/CMakeLists.txt writes, and the
	// compile command the compiler ran (the proof the list reached a TU).
	const std::vector<std::string> listed = cmake_graph::ListedDefinitions( cmake_graph::kClient );
	const std::set<std::string> compiled = cmake_graph::CompileDefinitions( cmake_graph::kClient );

	// Proof-of-read: CLIENT_DLL, the other half of ClientWorld().
	ASSERT_TRUE( std::find( listed.begin(), listed.end(), std::string( "CLIENT_DLL" ) ) != listed.end() &&
				 compiled.count( "CLIENT_DLL" ) )
		<< "client_sourcesharp does not define CLIENT_DLL -- the reader is not "
		   "reading the client's definitions, or ClientWorld() is wrong.";

	EXPECT_TRUE( std::find( listed.begin(), listed.end(), std::string( "SOURCESHARP" ) ) != listed.end() )
		<< "src/client/native/CMakeLists.txt no longer defines SOURCESHARP for "
		   "client_sourcesharp, so the guard world in this file is wrong and "
		   "every arm it strips is one the build actually compiles.";
	EXPECT_TRUE( compiled.count( "SOURCESHARP" ) )
		<< "client_sourcesharp's compile command carries no -DSOURCESHARP, so "
		   "the guard world in this file is wrong.";
}

//-----------------------------------------------------------------------------
// THE IMPACT'S CONTROL-POINT ENTITY IS THE SHELL THE TRACE HIT (2026-10-18).
//
// The impact effects bind points 0-2 to the entity the bullet hit; the
// particle query reads only whether a point has an object (m_pObject != NULL),
// which is what managed's hit-box answers are keyed on. That entity came from
// TraceGameEntity, i.e. GetGameEntity(), which answers NULL for every client
// object once C_StandaloneEntity is no IGameEntity -- so the binding would have
// gone silently. It comes from the slot table now.
//
// AND THE HANDLE IS A REAL ONE. The owners were an EHANDLE assigned from an
// IGameEntity *, which networkvar.h's SourceSharp_EntityHandle refuses: a
// Warning, an AssertMsg and the INVALID handle, for every managed entity a
// bullet hit. A CBaseHandle from the shell's own GetRefEHandle() is the handle.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE CLIENT SHELL IS NO IGameEntity (2026-10-18). The build's own gate is
// shell_standalone_client.cpp's static_assert, shown failing against the
// shell while it still derived; this pins that the gate is there and that the
// class head says so.
//-----------------------------------------------------------------------------
