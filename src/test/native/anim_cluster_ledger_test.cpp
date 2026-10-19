//========= SourceSharp ========================================================//
//
// anim_cluster_ledger_test.cpp -- lane p4-anim, the animation/ragdoll/dissolve
// cluster of the Phase 4 exit's one cycle (SCC 11, cluster C).
//
// WHAT THIS FILE IS FOR, and it is not the behaviour.
//
// The cluster's four stock TUs -- baseanimating.cpp, ragdoll_shared.cpp,
// EntityDissolve.cpp and RagdollBoogie.cpp -- leave server_csharp.vpc in the
// exit lane's ONE COMMIT, together with the rest of the cycle. The recorded
// hazard for that commit is "a compile gate selects for deletion": six silent
// losses have already been found in this port, every one of them a thing the
// BASE did FOR an entity rather than TO it, every one compiling green and
// passing make check and every live gate. 5e §5 is the checklist.
//
// So this file is the LEDGER. For each thing in the cluster that the exit
// commit will delete, it asserts EITHER that a managed replacement exists, OR
// that the thing is still wired where it has to be while the TU is still
// compiled. A row that is a refusal says so by name and points at
// ContractBaseline.
//
// It reads TEXT rather than running the game, for the reason every row here
// shares: what is at risk is a REGISTRATION -- a LINK_ENTITY_TO_CLASS, a game
// system row, a level-step call, a DispatchEffect name that a client callback
// is waiting for. None of those is reachable from a running test, and the only
// signal any of them gives when it goes is ABSENCE.
//
// EVERY SCAN STRIPS COMMENTS FIRST, and the stripper is itself tested in both
// directions, because half the rows below are about a file whose PROSE names
// the very symbol the row is hunting for. 4f lost a check to that shape from
// one side (a needle satisfied by a forward declaration) and 6d from the other
// (a needle that existed only inside a comment the test strips).
//
//=============================================================================

#include <stdio.h>
#include <string>
#include <vector>

#include "gtest/gtest.h"
#include "cmake_graph.h"

#define SS_ARRAYSIZE( a ) ( sizeof( a ) / sizeof( ( a )[0] ) )

namespace
{

std::string ReadFile( const char *pPath )
{
	FILE *fp = fopen( pPath, "rb" );

	if ( !fp )
		return std::string();

	std::string out;
	char buf[ 4096 ];
	size_t n;

	while ( ( n = fread( buf, 1, sizeof( buf ), fp ) ) > 0 )
		out.append( buf, n );

	fclose( fp );
	return out;
}

// ragdoll_signature_test.cpp's stripper, unchanged: it survives an
// unterminated block comment rather than running off the end.
std::string StripComments( const std::string &src )
{
	std::string out;
	out.reserve( src.size() );

	size_t i = 0;

	while ( i < src.size() )
	{
		if ( src[i] == '/' && i + 1 < src.size() && src[i + 1] == '/' )
		{
			while ( i < src.size() && src[i] != '\n' )
				i++;
			continue;
		}

		if ( src[i] == '/' && i + 1 < src.size() && src[i + 1] == '*' )
		{
			i += 2;
			while ( i + 1 < src.size() && !( src[i] == '*' && src[i + 1] == '/' ) )
				i++;
			i = ( i + 1 < src.size() ) ? i + 2 : src.size();
			continue;
		}

		out.push_back( src[i] );
		i++;
	}

	return out;
}

int CountOf( const std::string &haystack, const std::string &needle )
{
	if ( needle.empty() )
		return 0;

	int n = 0;
	size_t at = 0;

	while ( ( at = haystack.find( needle, at ) ) != std::string::npos )
	{
		n++;
		at += needle.size();
	}

	return n;
}

std::string Code( const char *pPath )
{
	return StripComments( ReadFile( pPath ) );
}

// Deleted at plan_physics_bones_native_zero Phase 3 (RagdollEvictionTests);
// asserted absent below. The client's proof-of-read is
// shell_physics_shared_client.cpp since physics_shared.cpp left too (2.6).
const char *kRagdollShared = "sourcesharp/game/shared/ragdoll_shared.cpp";
// The client's proof-of-read since the 2.6 flip: the remnant client.so compiles in its place.
const char *kPhysicsShared = "sourcesharp/game/shared/physics_shared.cpp";
const char *kPhysicsSharedClient = "sourcesharp/game/client/shell_physics_client.cpp";   // shell_physics_shared_client.cpp until it left with fork physics.cpp (2026-10-18)

const char *kServerSystems = "sourcesharp/game/server/shell_serversystems.cpp";
const char *kLevelSteps    = "sourcesharp/game/server/shell_level.cpp";
const char *kWorldAdapter  = "sourcesharp/game/server/world_adapter.cpp";
const char *kWorldQuery    = "sourcesharp/game/server/shell_worldquery.cpp";
const char *kScripted      = "sourcesharp/game/server/shell_scripted.cpp";
const char *kBaseline      = "sourcesharp/managed/SourceSharp.Tests.Common/ContractBaseline.cs";
const char *kPolicy        = "sourcesharp/managed/SourceSharp.Shared/RagdollPolicy.cs";
const char *kCorpses       = "sourcesharp/managed/SourceSharp.Server/Corpses.cs";

// How many rows of a target's CMakeLists source list name pszPath: the path
// itself, or any path ending in "/<pszPath>" (so "npc_hunter.cpp" finds it in
// any directory and "game/shared/x.cpp" finds sourcesharp/game/shared/x.cpp --
// the reach the vpc-era substring count had). A count, so a duplicated row
// reads 2. CompiledSources() is called first for its checks: it throws on a
// missing, empty or stale compile_commands.json, or one that disagrees with the
// list -- a reader that saw nothing never answers 0 here.
int CountNamed( const char *pTarget, const std::string &strPath )
{
	cmake_graph::CompiledSources( pTarget );
	const std::vector< std::string > listed = cmake_graph::ListedSources( pTarget );
	const std::string strSuffix = "/" + strPath;
	int n = 0;
	for ( size_t i = 0; i < listed.size(); i++ )
	{
		const std::string &src = listed[ i ];
		if ( src == strPath
			|| ( src.size() > strSuffix.size()
				&& src.compare( src.size() - strSuffix.size(), strSuffix.size(), strSuffix ) == 0 ) )
			n++;
	}
	return n;
}

} // namespace

//=============================================================================//
// THE STRIPPER, because every row below is only as good as it is.
//=============================================================================//

TEST( AnimClusterStripper, RemovesBothCommentForms )
{
	EXPECT_EQ( "int a;\nint b;\n",
	           StripComments( "int a;// LINK_ENTITY_TO_CLASS\nint b;/* DispatchEffect */\n" ) );
}

TEST( AnimClusterStripper, SurvivesAnUnterminatedBlockComment )
{
	EXPECT_EQ( "int a;", StripComments( "int a;/* and then nothing" ) );
}

TEST( AnimClusterStripper, KeepsANeedleThatIsCODE )
{
	EXPECT_EQ( 1, CountOf( StripComments( "DispatchEffect( \"TeslaHitboxes\", d );" ),
	                       "TeslaHitboxes" ) );
}

TEST( AnimClusterStripper, DropsTheSameNeedleWhenItIsPROSE )
{
	EXPECT_EQ( 0, CountOf( StripComments( "// we used to DispatchEffect TeslaHitboxes\n" ),
	                       "TeslaHitboxes" ) );
}

TEST( AnimClusterLedger, EveryFileTheLedgerReadsIsThere )
{
	// kAnimation IS OFF THIS LIST -- plan_shim.md Phase 2d deleted
	// game/shared/animation.cpp, and a file that is not there cannot be read
	// to a hundred bytes. Its absence is asserted below instead, which is the
	// stronger direction: this list says "a scan that could not open its file
	// reads exactly like a scan that passed", and the deleted file now has no
	// scan to be vacuous.
	const char *kAll[] = { kPhysicsSharedClient,
	                       kServerSystems, kLevelSteps, kWorldAdapter, kWorldQuery,
	                       kScripted, kBaseline, kPolicy, kCorpses };
	// (The two target source lists are read through cmake_graph.h, which throws
	// on a missing, empty or stale input.)

	for ( size_t i = 0; i < SS_ARRAYSIZE( kAll ); i++ )
	{
		SCOPED_TRACE( kAll[i] );
		EXPECT_GT( ReadFile( kAll[i] ).size(), 100u )
			<< "a scan of a file it could not open reads exactly like a scan that passed";
	}
}

//=============================================================================//
// ROW 1 -- THE CLUSTER TUs, AFTER THE FLIP.
//
// THIS FACT SAID "the four TUs are STILL COMPILED", AND IT WAS A TRIPWIRE HELD
// FOR ONE COMMIT. Its own comment said which: "The behaviour migration is not
// the vpc removal. If a row below has gone ... the exit lane's atomic commit
// has been started by somebody who was not supposed to."
//
// THE FLIP IS THE EXIT LANE'S ATOMIC COMMIT, and it took three of the five:
//
//     game\server\baseanimating.cpp    1 -> 0
//     game\server\EntityDissolve.cpp   1 -> 0
//     game\server\RagdollBoogie.cpp    1 -> 0
//
// It is REWRITTEN, NOT DELETED, and it keeps both halves of its value. The
// three that left are asserted ABSENT, so a re-appearance -- somebody restoring
// a TU the flip removed -- still reddens. The two that did NOT leave are still
// asserted PRESENT, unchanged: they are section 6 files, this is the original
// tripwire, and no lane may take them out. Neither half can pass by the other
// being true.
//=============================================================================//

TEST( AnimClusterLedger, TheFlipTookThreeOfTheseRowsAndRagdollSharedLeftBothSides )
{
	const char *const S = cmake_graph::kServer;

	// Left with the flip. Present here means a removed TU came back.
	EXPECT_EQ( 0, CountNamed( S, "game/server/baseanimating.cpp" ) );
	EXPECT_EQ( 0, CountNamed( S, "game/server/EntityDissolve.cpp" ) );
	EXPECT_EQ( 0, CountNamed( S, "game/server/RagdollBoogie.cpp" ) );

	// It left the SERVER in P5 lane SP (2026-10-01): no server object
	// referenced any symbol it defines. It left the CLIENT and the repository
	// at plan_physics_bones_native_zero Phase 3, retiring §6's keep: its
	// primitives and key handlers are SourceSharp.Client's RagdollSolver. The
	// proof-of-read for the absences around it is a file each side compiles.
	EXPECT_EQ( 0, CountNamed( S, "game/shared/ragdoll_shared.cpp" ) );
	EXPECT_EQ( 0, CountNamed( cmake_graph::kClient, "game/shared/ragdoll_shared.cpp" ) );
	EXPECT_EQ( 0, CountNamed( cmake_graph::kClient, "game/shared/physics_shared.cpp" ) );
	EXPECT_EQ( 1, CountNamed( cmake_graph::kClient, "game/client/shell_physics_client.cpp" ) );
	EXPECT_TRUE( ReadFile( kRagdollShared ).empty() ) << kRagdollShared << " is back on disk.";
	EXPECT_TRUE( ReadFile( kPhysicsShared ).empty() ) << kPhysicsShared << " is back on disk.";
	EXPECT_EQ( 1, CountNamed( S, "game/server/shell_serversystems.cpp" ) );

	// animation.cpp DID leave, and it is the only row here that changed sides.
	// plan_shim.md Phase 2d deleted it with the whole activity-ordinal family
	// -- shell_animlists.cpp, anim_name_registry.h, studio.cpp's
	// activity-to-sequence mapping and ActivityCatalog.cs -- once
	// precache_model.cpp started comparing event NAMES and the sequence
	// lookups became one managed rule over a ModelDescriptor.
	//
	// ASSERTED AS AN ABSENCE rather than deleted, because this fact's whole
	// shape is "a removed TU coming back is a failure", and that is as true of
	// this one as of the three above it.
	EXPECT_EQ( 0, CountNamed( S, "game/shared/animation.cpp" ) )
		<< "game/shared/animation.cpp is compiled into server_sourcesharp again. It exists to write "
		   "registry ORDINALS into mdl memory for a native reader to compare "
		   "against an enum, and there is no registry and no such reader.";
}

//=============================================================================//
// ROW 2 -- EntityDissolve.cpp
//
// WHAT THE BUILD GETS FROM IT, enumerated, and what happens to each:
//
//   a. LINK_ENTITY_TO_CLASS( env_entity_dissolver ) -- ONE map classname. It is
//      already recorded as absent from the managed sandbox
//      (SourceSharp.MapGen/Sandbox.cs), so its disappearance is a recorded
//      fact rather than a discovery.
//   b. IMPLEMENT_SERVERCLASS_ST( CEntityDissolve, DT_EntityDissolve ) -- the
//      send table whose client half is c_entitydissolve.cpp. Step 7a reports
//      that file could not leave client_csharp.vpc while this one is here.
//   c. CEntityDissolve::Precache -- PrecacheModel( DISSOLVE_SPRITE_NAME ). A
//      sprite index, which is the exact shape of the eight g_sModelIndex*
//      losses 5e found: a precache that stops happening leaves an index at 0
//      and nothing says so.
//   d. The behaviour: a dissolve is a CLIENT shader over the target's studio
//      model. Step 4a made SourceSharp_EntityDissolve a DECLARED REFUSAL
//      because it had already been answering 0 for every managed handle.
//
// (d) is the one that has to be pinned, and the pin is the refusal, because a
// refusal deleted along with the code it refuses looks exactly like a port.
//=============================================================================//

TEST( AnimClusterLedger, TheDissolveIsADECLAREDREFUSALAndNotASilentNoOp )
{
	const std::string shim = Code( kWorldQuery );

	// The warning body, not merely the function -- a refusal that stopped
	// warning is a silent no-op again, which is what §7 forbids.
	EXPECT_EQ( 1, CountOf( shim, "int32_t SourceSharp_EntityDissolve(" ) );
	EXPECT_EQ( 1, CountOf( shim, "EntityDissolve is refused" ) );
	EXPECT_EQ( 1, CountOf( shim, "ContractBaseline.Refusals" ) );

	// And the managed half of the pair still carries the entry, so the reason
	// survives the code.
	EXPECT_EQ( 1, CountOf( Code( kBaseline ), "EntityDissolve" ) );
}

//=============================================================================//
// ROW 4 -- RagdollBoogie.cpp
//
// WHAT THE BUILD GETS FROM IT:
//
//   a. LINK_ENTITY_TO_CLASS( env_ragdoll_boogie ) -- one map classname.
//   b. CRagdollBoogie::Create / IncrementSuppressionCount /
//      DecrementSuppressionCount -- called from physics_prop_ragdoll.cpp, which
//      is the PHYSICS cluster's file and is inside the same cycle. So the
//      boogie has ZERO shim consumers and dies with CRagdollProp.
//   c. DispatchEffect( "TeslaHitboxes" ) -- and this is the row worth having.
//      RagdollBoogie.cpp:141 is the ONLY compiled server dispatcher of that
//      effect name in this build; npc_hunter.cpp, npc_antlion.cpp and
//      npc_attackchopper.cpp all carry one and none of the three is in
//      server_csharp.vpc. The client has been waiting for it since forever:
//      game/client/fx.cpp carries DECLARE_CLIENT_EFFECT( "TeslaHitboxes", ... ).
//      Delete this file and that callback has no caller anywhere, with nothing
//      failing to build and nothing logging.
//   d. EmitSound( "RagdollBoogie.Zap" ) -- HL2_EPISODIC only.
//
// This lane does NOT port the boogie, and says so with the measurement rather
// than porting machinery with no customer (6e's own rejected shape): every
// caller is CRagdollProp's physgun interaction, and this mod has no physgun.
// What it does is make the loss impossible to take silently.
//=============================================================================//

TEST( AnimClusterLedger, TheClientIsStillListeningForTeslaHitboxes )
{
	// The other half of the pair. If this ever goes, row 4c is closed by
	// somebody else and the boogie's deletion costs nothing.
	//
	// fx.cpp left the build (lane c5-particles) and the listener with it moved
	// to C#: ClientModule registers TeslaHitboxes as a managed handler that,
	// like stock's armed body, draws nothing.
	EXPECT_EQ( 1, CountOf( Code( "sourcesharp/managed/SourceSharp.Client/ClientModule.cs" ),
	                       "new Effects.EmptyEffect(\"TeslaHitboxes\")" ) );
}

//=============================================================================//
// ROW 5 -- ragdoll_shared.cpp, and this is the one this lane actually MOVED.
//
// WHAT THE BUILD GETS FROM IT, split by §6:
//
//   MECHANISM, stays native (plan_entity.md §6 keeps "the ragdoll builder"):
//     RagdollCreate, RagdollActivate, RagdollSetupCollisions, RagdollDestroy,
//     RagdollGetBoneMatrix, RagdollComputeExactBbox, RagdollIsAsleep,
//     RagdollApplyAnimationAsVelocity, RagdollExtractBoneIndices,
//     RagdollSetupAnimatedFriction, RagdollSolveSeparation, and the two
//     IVPhysicsKeyHandlers.
//
//   POLICY, moved to managed by this lane (step 4d named both by name):
//     CRagdollLowViolenceManager -- which maps show bodies
//     CRagdollLRURetirement     -- how many bodies, and which one goes
//
// The three SHIM edges into this file were all on the policy half:
//   shell_serversystems.cpp   RagdollLRUGameSystem()
//   shell_level.cpp      g_RagdollLVManager, SetLowViolence
//
// ALL OF THEM ARE UNWIRED NOW -- plan_shim.md 4a item 3, lane 3a -- and the
// reason they were not is worth writing down because it was WRONG rather than
// merely out of date, and the two facts below have been flipped to say so.
//
// What stood here was: "the native LRU's customer is CRagdollProp from
// physics_prop_ragdoll.cpp ... still able to be spawned by a map: its
// LINK_ENTITY_TO_CLASS( prop_ragdoll ) is NOT guarded. Unregistering the system
// while that is true would stop every map ragdoll from ever fading, build
// green, and pass every gate."
//
// The guard was never the question. physics_prop_ragdoll.cpp HAS NO ROW IN
// EITHER VPC and produces no object on either link line, so CRagdollProp is not
// in this build and prop_ragdoll is not a native classname the game answers to
// -- an unguarded LINK_ENTITY_TO_CLASS in a translation unit nobody compiles
// registers nothing. The customer had already left. Measured the same way the
// deletion was:
//
//     grep -c 'objs/[a-z]*_sourcesharp/physics_prop_ragdoll\.o[: ]' \
//         <VPC's ninja graph, since retired>          ->  0
//
// and independently, MoveToTopOfLRU -- the LRU's only writer -- had 0 importers
// on both link lines, so the list was empty whatever spawned.
//=============================================================================//

TEST( AnimClusterLedger, TheNativeLowViolenceStepIsEmptyAndTheManagedOneIsNot )
{
	// The step KEEPS ITS PLACE -- ishim_level.h's SetLowViolence is a vtable
	// slot managed calls through, and its position in the sequence is stock's
	// -- but its body no longer computes anything.
	// NEEDLE RE-POINTED AT THE MERGE, INTENT UNCHANGED. Lane 3a emptied this
	// step's body when the LV manager went; lane 3b then folded
	// shell_levelsteps.cpp into shell_level.cpp, where the step is a virtual
	// member rather than a free function. The path above was updated with the
	// fold and the spelling was not, so this read 0 for a step that is present
	// and correct. What the assertion protects is unchanged: the step still
	// EXISTS, because its vtable slot and its position in stock's nine-step
	// order both depend on that.
	const std::string steps = Code( kLevelSteps );
	EXPECT_EQ( 1, CountOf( steps, "virtual void SetLowViolence( const char *pMapName ) OVERRIDE" ) )
		<< "the step is gone, not emptied; its vtable slot and its position in "
		   "stock's nine-step order both depend on it being here";
	EXPECT_EQ( 0, CountOf( steps, "g_RagdollLVManager.SetLowViolence( pMapName );" ) );

	// And the answer is still made, once, on the same map load.
	EXPECT_EQ( 1, CountOf( Code( kCorpses ), "RagdollViolencePolicy.LowViolenceForMap(" ) );
}

TEST( AnimClusterLedger, ThePolicyHalfHasAMANAGEDHomeToGoTo )
{
	const std::string policy = Code( kPolicy );

	// The two classes, by the names they answer to on this side.
	EXPECT_LE( 1, CountOf( policy, "class RagdollViolencePolicy" ) );
	EXPECT_LE( 1, CountOf( policy, "class RagdollRetirementPolicy" ) );

	// The three rules that are the whole of SetLowViolence.
	EXPECT_EQ( 1, CountOf( policy, "isServer && isMultiplayer" ) );
	EXPECT_LE( 1, CountOf( policy, "EpisodicFullViolenceMaps" ) );
	EXPECT_LE( 1, CountOf( policy, "FullViolenceMaps" ) );

	// And the four the retirement is.
	EXPECT_LE( 1, CountOf( policy, "EffectiveCap" ) );
	EXPECT_LE( 1, CountOf( policy, "RetireOneUnseen" ) );
	EXPECT_LE( 1, CountOf( policy, "ImportantOverflow" ) );
	EXPECT_LE( 1, CountOf( policy, "MoveToTop" ) );
}

TEST( AnimClusterLedger, TheManagedCORPSESIDEACTUALLYASKSThePolicy )
{
	// A policy nothing calls is a policy that has not moved. The needles carry
	// the call parentheses so a `using` or a comment cannot satisfy them.
	const std::string corpses = Code( kCorpses );

	EXPECT_LE( 1, CountOf( corpses, "RagdollRetirementPolicy.EffectiveCap(" ) );
	EXPECT_EQ( 1, CountOf( corpses, "RagdollRetirementPolicy.Retire(" ) );
	EXPECT_EQ( 1, CountOf( corpses, "RagdollViolencePolicy.LowViolenceForMap(" ) );
	EXPECT_EQ( 1, CountOf( corpses, "RagdollViolencePolicy.IsLowViolence(" ) );
}

// RETIRED WITH THE CODE THEY PINNED -- plan_shim.md 4a item 3, lane 3a.
//
// `TheMapLISTSAGREEWordForWordWithStocksOwnSource` read the seven Citadel map
// names out of ragdoll_shared.cpp's SetLowViolence and out of RagdollPolicy.cs
// and required both to carry each. `TheCAPDEFAULTSAGREEWithStocksConVars` did
// the same for 8 and 2 against g_ragdoll_maxcount and
// g_ragdoll_important_maxcount.
//
// BOTH WERE ABOUT A SECOND SPELLING, and there is no second spelling now: the
// native manager, the native LRU and the three ConVars are deleted, so each of
// those numbers exists in exactly one place. A cross-check with one side left
// is not a weaker check, it is a check that cannot fail -- CountOf over a file
// that no longer contains the needle would have to be asserted as ZERO, which
// says nothing about whether the surviving list is right.
//
// WHAT STILL PINS THE SURVIVING SPELLING: RagdollPolicyTests.cs on the managed
// side exercises LowViolenceForMap over the map names and EffectiveCap over the
// defaults as BEHAVIOUR rather than as text, which is the stronger form and is
// what a single-sided rule should be checked by.
//
// The structural facts below are kept and do the part these two did that is
// still meaningful: that the managed home exists and that the managed corpse
// registry actually asks it.

//=============================================================================//
// ROW 6 -- baseanimating.cpp and animation.cpp
//
// The shim edges into baseanimating.o are ALL the SDK arm of a two-branch
// resolve -- `if ( CBaseAnimating *pAnimating = ent.Animating() )` -- and every
// one of them has a STANDALONE arm beside it that answers through 4d's
// bone_library. So nothing managed loses an answer when the SDK hierarchy goes;
// what goes is the branch.
//
// This is asserted rather than stated because the failure mode is the
// attractive one: deleting the SDK arm AND its standalone twin passes every
// compile gate, and the second arm is the only thing that answers afterwards.
//
// THE COUNT MOVED FROM FIVE TO TWO, and the move is exactly what this fact was
// written to watch. Three of the five sat inside `#ifndef SS_CBASEENTITY_ALIAS`
// -- an arm the compiler never saw, because CBaseEntity WAS IGameEntity -- and
// the-close 1/N deleted all three with the shim's other 237 dead arms. What
// this fact has to prove, and now does, is that the branch went ALONE: each of
// those three entry points still answers through its standalone arm. The two
// survivors are unguarded and are counted as before.
//=============================================================================//

//=============================================================================//
// THE SDK-ARM POPULATION IN shell_scripted.cpp IS ZERO -- plan_shim.md 2d.
//
// EverySDKArmInTheAnimationShimHasAStandaloneArmBesideIt counted the two
// `.Animating()` resolves that were left (LookupSequenceByActivity and the
// shared ServerAnimStudioHdr) and required each to have a standalone twin
// beside it. TheThreeEntryPointsThatLostTheirSdkArmStillAnswerStandalone
// checked the three whose SDK arm the-close 1/N had deleted -- the half that
// deletion could have silently taken with it, since an entry point that loses
// BOTH arms compiles, links, and answers its failure value for every call.
//
// BOTH SUBJECTS ARE GONE, and gone in the direction those facts wanted.
// LookupSequence, LookupSequenceByActivity and SequenceDuration are RETIRED
// ABI slots: one ModelDescriptor crossing answers all three, in C#, reading
// the MODEL rather than an entity -- so there is no hierarchy to have an arm
// for. ServerAnimStudioHdr was their shared resolve and went with them.
//
// WHAT SURVIVES OF EACH CLAIM:
//
//   * "no entry point has only the SDK arm" is animating_parallel_arm_test's
//     sweep, and its own floor is an EXPECT_EQ( 0, nAsk ) now for this same
//     reason -- with the parser's positive control moved out of the tree so
//     that the tree getting better cannot make it vacuous.
//
//   * "each still answers the standalone hierarchy" is below, restated as
//     what it has become: the entry points are gone, the slots are NULL, and
//     the marker says a pass will remove them. An entry point that had
//     silently lost both arms would still be in the file.
//
//   * SetSequence's parallel arm, the one defect this family was written
//     around, is untouched and still asserted.
//=============================================================================//

TEST( AnimClusterLedger, TheRetiredSequenceEntryPointsAreGoneRatherThanSilentlyEmpty )
{
	const std::string scripted = Code( kScripted );

	// AN EMPTY BODY IS THE FAILURE THIS WATCHES FOR. A function that kept its
	// signature and lost its arms compiles, links, answers -1 for every call
	// and logs nothing -- which is exactly what these facts' predecessors
	// existed to catch. Retiring a slot means the DEFINITION goes.
	const char *const kRetired[] =
	{
		"int32_t SourceSharp_EntityLookupSequence(",
		"int32_t SourceSharp_EntityLookupSequenceByActivity(",
		"float SourceSharp_EntitySequenceDuration(",
		"int32_t SourceSharp_EntitySequenceEventCount(",
		"int32_t SourceSharp_EntitySequenceEvent(",
	};

	for ( size_t i = 0; i < SS_ARRAYSIZE( kRetired ); i++ )
	{
		EXPECT_EQ( 0, CountOf( scripted, kRetired[ i ] ) )
			<< kScripted << " still defines " << kRetired[ i ]
			<< " -- a retired slot's BODY goes; only its field stays, marked "
			   "SS_RETIRED for the serialised ABI pass.";
	}

	// And the shared two-branch resolve they used, which had no other caller.
	EXPECT_EQ( 0, CountOf( scripted, "SourceSharp_ServerAnimStudioHdr" ) )
		<< kScripted << " still carries the studio resolve the retired entry "
		   "points shared. With them gone it is a static nobody calls.";

	// NOT VACUOUS: the file is still the animation shim and still has the
	// entry points that were NOT retired.
	EXPECT_GT( CountOf( scripted, "SourceSharp_" ), 20 )
		<< "shell_scripted.cpp has almost no SourceSharp_ entry points left; "
		   "the read or the path is wrong, not the retirement.";
}

// THE MARKED-AND-NULLED FACT IS RETIRED WITH WHAT IT PINNED -- lane 2e-abi.
//
// TheSlotsTheyFilledAreMarkedAndNulledRatherThanRemoved required five slots to
// be assigned `engineApi.X = NULL;` in sourcesharp_gamesystem.cpp and to carry
// an SS_RETIRED marker in sourcesharp.h. Both were the protocol for a slot
// WAITING for the serialised ABI pass, and that pass has now run: the fields,
// the markers and the NULL assignments are all gone, so every assertion it
// makes can only fail. A fact that can only fail is no more a claim about the
// build than one that can only pass.
//
// What it really pinned -- that a lane may not remove its own slot, because a
// removal from the middle renumbers every slot after it and AbiParityTests pins
// names IN ORDER -- is not lost. It is AbiParityTests itself, and it is written
// down in docs/abi-retirements.md for the next lane that retires one.

//=============================================================================//
// THE TWO REGISTRY FACTS ARE RETIRED WITH THE REGISTRY -- plan_shim.md 2d.
//
// TheSharedActivityAndEventListsAreStillRegisteredByTheWorldAdapter required
// world_adapter.cpp to call ActivityList_Init / EventList_Init and then
// ActivityList_RegisterSharedActivities / EventList_RegisterSharedEvents,
// "without them SetActivityForSequence registers every activity name as a
// PRIVATE activity and every shared activity lookup answers nothing -- with no
// crash and no log".
//
// TheACTIVITYINDEXFamilyIsWhatHoldsAnimationCppOutsideTheCycle recorded the
// two consumers keeping animation.cpp compiled: precache_model.cpp's two
// VerifySequenceIndex calls and studio.cpp's SetActivityForSequence. Its own
// prose said what would end it -- "these two say it cannot [leave] without the
// family moving first" -- and that is what Phase 2d did.
//
// THE WHOLE ARRANGEMENT IS GONE, and the census is the proof rather than the
// intent:
//
//     tools/shim-census who-imports src/game/shared/animation.cpp
//         server 2: precache_model.o, studio.o   client 1: studio.o
//     tools/shim-census who-imports .../shell_animlists.cpp
//         server 2: animation.o, world_adapter.o
//         client 2: animation.o, world_adapter_client.o
//
// -- a closed cycle whose only outside edges were those three call sites. All
// three are removed, and the docs/entity-census.txt diff for that commit shows
// the two cross-edges disappearing by name.
//
// WHAT REPLACED THE THING THEY PROTECTED: nothing needs protecting. A "shared
// activity lookup answering nothing" was a failure mode of a per-level
// registry that had to be filled before a model was indexed; there is no
// registry, no indexing pass, and no ordinal. Activity names travel in the
// ModelDescriptor and are compared as names.
//
// The fact below replaces both, and asserts the absence from the adapters so
// that the calls cannot quietly come back to a registry that is not there.
//=============================================================================//

TEST( AnimClusterLedger, TheWorldAdaptersNoLongerOpenARegistryThatIsGone )
{
	const char *const kAdapters[] =
	{
		"sourcesharp/game/server/world_adapter.cpp",
		"sourcesharp/game/client/world_adapter_client.cpp",
	};

	const char *const kCalls[] =
	{
		"ActivityList_Init();",
		"EventList_Init();",
		"ActivityList_Free();",
		"EventList_Free();",
		"ActivityList_RegisterSharedActivities();",
		"EventList_RegisterSharedEvents();",
	};

	for ( size_t a = 0; a < SS_ARRAYSIZE( kAdapters ); a++ )
	{
		const std::string adapter = Code( kAdapters[ a ] );

		// NOT VACUOUS: the adapter is still the adapter.
		ASSERT_GT( adapter.size(), 1000u )
			<< kAdapters[ a ] << " read as almost nothing, so every absence "
			   "below would pass for the wrong reason.";

		for ( size_t c = 0; c < SS_ARRAYSIZE( kCalls ); c++ )
		{
			EXPECT_EQ( 0, CountOf( adapter, kCalls[ c ] ) )
				<< kAdapters[ a ] << " calls " << kCalls[ c ]
				<< " again. shell_animlists.cpp is deleted, so this does not "
				   "link -- and if something re-adds the registry with it, the "
				   "whole activity-ordinal family comes back.";
		}
	}
}

TEST( AnimClusterLedger, TheActivityOrdinalFamilyIsGoneFromDiskAndFromBothTargets )
{
	const char *const kGone[] =
	{
		"game/shared/animation.cpp",
		"sourcesharp/game/shared/shell_animlists.cpp",
		"sourcesharp/game/shared/anim_name_registry.h",
		"sourcesharp/managed/SourceSharp.Abi/ActivityCatalog.cs",
	};

	for ( size_t i = 0; i < SS_ARRAYSIZE( kGone ); i++ )
	{
		EXPECT_TRUE( ReadFile( kGone[ i ] ).empty() )
			<< kGone[ i ] << " is back on disk.";
	}

	// And the two rows, on both sides.
	const char *const kTargets[] = { cmake_graph::kServer, cmake_graph::kClient };

	for ( size_t v = 0; v < SS_ARRAYSIZE( kTargets ); v++ )
	{
		// Proof-of-read: a file the target DOES compile (the client's is
		// shell_physics_shared_client.cpp since physics_shared.cpp left at
		// plan_physics_bones_native_zero 2.6; the server's is shell_serversystems).
		const char *pProof = ( v == 0 ) ? kServerSystems : "game/client/shell_physics_client.cpp";
		ASSERT_EQ( 1, CountNamed( kTargets[ v ], pProof ) )
			<< kTargets[ v ] << "'s source list does not name " << pProof;

		EXPECT_EQ( 0, CountNamed( kTargets[ v ], "shell_animlists.cpp" ) )
			<< kTargets[ v ] << " compiles shell_animlists.cpp, which is not there.";
		EXPECT_EQ( 0, CountNamed( kTargets[ v ], "shared/animation.cpp" ) )
			<< kTargets[ v ] << " compiles animation.cpp, which is not there.";
	}

	// THE LAST READER OF THE ORDINAL, which is what made the family a cycle:
	// studio.cpp's activity-to-sequence mapping read seqdesc.activity, the
	// number animation.cpp wrote there. The mapping went first; since
	// 2026-10-18 studio.cpp itself is gone (no native code reads the mdl
	// through CStudioHdr: the descriptor and the render counts are managed's),
	// so the reader cannot come back inside it.
	ASSERT_GT( Code( kServerSystems ).size(), 1000u )
		<< "the source reader reads " << kServerSystems << " as almost nothing, "
		   "so the absence below would be the reader's";

	EXPECT_TRUE( Code( "sourcesharp/public/studio.cpp" ).empty() )
		<< "studio.cpp is back. Its activity-to-sequence mapping was reached "
		   "only by the client's ::LookupSequence fall-through, which is the "
		   "prediction defect Phase 2d closed, and its accessors' consumers "
		   "read the managed StudioHdr now.";
}
