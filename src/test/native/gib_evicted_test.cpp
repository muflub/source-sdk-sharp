//========= SourceSharp — game/server/gib.cpp is EVICTED ======================//
//
// THIS FILE USED TO RECORD A REFUSAL. It was gib_verdict_test.cpp, and it
// argued, at length and with evidence, that the gib should NOT be ported: a gib
// is an ENTITY rather than an effect — it persists, it is simulated, it is drawn
// from per-entity render state and it can be a jointed body — and the managed
// hierarchy could do none of those. Three blockers were named, and the file's
// own closing instruction was:
//
//     "Every one of those blockers is a fact about a file somebody else may fix
//      for their own reasons, and the day one of them is fixed the verdict above
//      is worth re-taking."
//
// ALL THREE WERE FIXED, each by a different lane, and each fix was recorded in
// this file beside the blocker it closed before step 4a spent it:
//
//   1. The dead OnScriptedPhysicsCollision slot turned out to cost the gib
//      NOTHING. CGib::BounceGibTouch opens with
//      `if ( VPhysicsGetObject() ) return;`, so the decals and impact sounds
//      belong exclusively to the non-solver path — which is the entity TOUCH
//      path, and Touch is one of IGameEntity's six pure virtuals, already
//      delivered to every managed entity. (It is still a real blocker for the
//      RAGDOLL half of the cluster; see below.)
//
//   2. The standalone hierarchy gained a per-instance SKIN and MODEL SCALE, and
//      THE WIRE DID NOT GROW TO PAY FOR IT — both values ride the payload as
//      ordinary managed state. That second half is what decided it: two more
//      SendProps would have closed the blocker and left the reason for the
//      refusal standing.
//
//   3. StandaloneBecomeRagdoll builds a jointed body from an entity's own model
//      and its own current skeleton, client-side, exactly where stock builds a
//      debris ragdoll — so it costs the wire a renderfx and a force.
//
// SO THE ARITHMETIC INVERTED, which is the whole of the argument. The verdict
// rested on "the port would REMOVE 892 lines of C++ and ADD a networked field to
// every managed entity in the game twice over, a native collision call site in
// the shared standalone shell, and a ragdoll-from-model slot". Every one of
// those additions had already been made, for other reasons, by the time step 4a
// ran: what was left to pay was zero, and what was left to remove was gib.cpp's
// 768 lines plus the 240 in SourceSharp_ShootGib that built a CGib. That is the
// same correction lane U2 made to the C_SpriteTrail refusal, which this file's
// header used to cite as its second precedent and then withdrew.
//
// WHAT REPLACED IT: ss_gib (SourceSharp.Server/Entities/Effects/SsGib.cs) and
// Gibs (Gibs.cs beside it), with SsRagdoll serving GIB_SIMULATE_RAGDOLL exactly
// as stock's CRagGib was its own entity rather than a configured CGib. The
// BEHAVIOUR is asserted in C# — GibTests, sixteen facts, one per behaviour —
// because that is where the behaviour now is. What is asserted HERE is what
// only a source scan can see: that nothing reaches for the C++,
// that the retired ABI slot refuses out loud rather than returning a quiet zero,
// and that the three things the port stands on are still standing.
//
// ONE THING THIS EVICTION FIXED THAT NOBODY WAS LOOKING FOR. GibShot.Material
// carries a SurfaceMaterial — the ASCII letters the surfaceprop tables use, 'W'
// for wood. SourceSharp_ShootGib assigned that number straight into
// CGib::m_material, which is a `Materials` (func_break.h: matGlass = 0,
// matWood = 1, ... matNone = 10), and CBreakable::MaterialSound was then asked
// for a sound for material 87. Its switch has no case for 87 and returns NULL,
// so the gib impact sound was DEAD for every gib in the game — and
// `m_material != matNone` was TRUE for 87, so the branch was entered every time
// and did nothing. The only symptom is a sound that does not play. Pinned at
// both ends in GibTests.
//
// WHAT IS STILL REFUSED, and it is why the "blocker" tests below are kept
// rather than deleted: OnScriptedPhysicsCollision was a dead slot (retired at
// ABI 252), and the missing solver callback still blocks the RAGDOLL half of
// this cluster — CRagdollProp::VPhysicsCollision is live, does physics damage
// and HandleFirstCollisionInteractions, and genuinely needs a solver callback. And a standalone ragdoll's physics objects
// carry no game data, so the client's impact sound is gated off and one lands
// silently. Both are ContractBaseline.Refusals entries, which is where
// plan_entity.md section 7 puts a difference somebody chose.
//
// WHAT THIS FILE IS FOR. Every fact below is a fact about a file somebody may
// change for their own reasons. A red test here is not automatically a
// regression — read the message, re-measure, and either update the fact or take
// the consequence.
//
// ANCHORS, NOT LINE COUNTS. Every file is asserted to still contain a string
// that identifies it, because a source-scanning test whose file has been
// renamed reads an empty string and reports every absence invariant as
// satisfied — the check that cannot fail.
//
// THE STRIPPER IS LOAD-BEARING rather than tidy: this file's own prose names
// OnScriptedPhysicsCollision in the paragraph explaining that nothing calls it,
// and the scan below would read the explanation as the violation if it did not
// strip comments. Same rule as effects_evicted_test.cpp.
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <algorithm>
#include <dirent.h>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as effects_evicted_test.cpp.
//-----------------------------------------------------------------------------
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

// Whole file, comments gone. Empty means "could not open", which every caller
// must distinguish from "opened and found nothing".
std::string ReadStripped( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	const std::string all = ReadRaw( std::string( pszRoot ) + "/" + strRelative );
	return all.empty() ? all : StripComments( all );
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

struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// game/server/gib.cpp AND game/server/gib.h ARE NOT IN THIS TABLE ANY MORE.
// gib.cpp is off disk and out of server_csharp.vpc (plan_entity.md step 4a);
// gib.h stays, unreferenced by anything the build compiles, exactly as
// game/server/util.h keeps declaring UTIL_RestartAmbientSounds -- a link error
// naming CGib is the right answer for anyone who puts one of its four
// out-of-build callers back, and deleting the header would give them a compile
// error about an unknown type instead.
const File_t kSsGib = { "sourcesharp/managed/SourceSharp.Server/Entities/Effects/SsGib.cs",
	"class SsGib" };

const File_t kGibs = { "sourcesharp/managed/SourceSharp.Server/Entities/Effects/Gibs.cs",
	"class Gibs" };

const File_t kWorldQuery = { "sourcesharp/game/server/shell_worldquery.cpp",
	"void SourceSharp_SetSkyboxParams(" };   // SparkEffect went 2026-10-19; TraceLine at plan_server_native_v2 T4f
const File_t kNetTable = { "sourcesharp/game/shared/standalone_nettable.h",
	"STANDALONE_NET_FIELD_COUNT" };
const File_t kStandalonePlayer = { "sourcesharp/game/server/shell_standalone_player.h",
	"virtual float           GetModelScale() const OVERRIDE" };
// Where the jointed body's pose comes from: the CLIENT's managed skeleton
// (plan physics_bones_native_zero Phase 3; the client shell built it until
// then -- see BlockerThree below for why the old needle could not fail).
const File_t kStandaloneClientCpp = { "sourcesharp/managed/SourceSharp.Client/Ragdolls/ClientRagdollEngine.cs",
	"public sealed class ClientRagdollPoses(" };
// REPOINTED BY STEP 6e, and this time the code MOVED rather than being renamed.
//
// plan_entity.md 6e: "Creation is a flat function over a bone snapshot
// returning ids." The five solver calls BlockerThree names are now in
// ragdoll_library.cpp, and shell_standalone_client.cpp's BecomeRagdoll is what
// gathers the model, the pose and the owner word and hands them over -- which
// is why kStandaloneClientCpp above still anchors on it and still exists.
//
// The blocker's own argument is UNCHANGED by the move and is in fact the reason
// the move was cheap: the primitives take no entity, so the port needed a
// second CALLER and not a second implementation. What changed is that the
// caller no longer takes one either.
//
// MOVED AGAIN BY plan physics_bones_native_zero Phase 3: the body is managed's.
// SourceSharp.Client's ClientRagdolls.Become gathers the model, the pose and the
// owner and calls RagdollSolver (ragdoll_shared.cpp's primitives, ported), and
// the library keeps only the physics step's two forwarders.
const File_t kRagdollLibraryCpp = { "sourcesharp/managed/SourceSharp.Client/Ragdolls/ClientRagdolls.cs",
	"public bool Become(EntityHandle owner, Vector force, int forceBone, out RagdollRefusal refusal)" };
// The primitives it calls, and the header that proves they take no entity.
// The same file again, anchored on the bone build rather than on the ragdoll,
// so a rename of either is reported against the thing that moved.
//
// REPOINTED BY STEP 6c, and the needle did not move an inch. The anchor was
// `void C_StandaloneEntity::BuildTransformations(...)`, which is now
// BoneLibrary_PoseToWorldProcedural in the leaf library -- the extraction this
// blocker's own test has nothing to say about. What this reads is the
// ProceduralBone_WantsIk call, which is in EnsureBones and always was; the
// anchor names EnsureBones now because that is the function the assertion
// below is about.
//
// MOVED AGAIN BY plan physics_bones_native_zero 1.2's client half: the client
// skeleton is managed, and the ProceduralBone_WantsIk call is
// ClientBoneDriver's ProceduralBoneRule.WantsIk, in its EnsureBones' build.
const File_t kStandaloneClientCppScale = { "sourcesharp/managed/SourceSharp.Shared/Bones/ClientBoneDriver.cs",
	"private bool BuildPosedBones(" };
const File_t kRagdollShared = { "game/shared/ragdoll_shared.h",
	"struct ragdollparams_t" };
// Where the per-instance size actually lives now, and where the skin does.
const File_t kStandaloneServer = { "sourcesharp/game/server/shell_standalone.h",
	"float GetModelScale() const { return m_flModelScale; }" };
const File_t kStandaloneClient = { "sourcesharp/game/client/shell_standalone_client.h",
	"virtual int  GetSkin() OVERRIDE { return Block().Client.Skin; }" };
// Where the call had to be made. Native's PostCollision body is gone since
// plan_physics_managed B2.2: vphysics calls managed's own
// IPhysicsCollisionEvent table, whose PostCollision goes to the server's
// PhysicsCollisionImpacts. Native's whole handler went with
// plan_physics_bones_native_zero 2.1, and the step with 2.3 (managed's
// PhysicsFrame), so the anchor is the owners' update the step still asks.
const File_t kServerPhysics = { "sourcesharp/game/server/physics.cpp",
	"int32_t SourceSharp_PhysicsUpdateOwners( void **ppObjects, float *pVelocities, int32_t nCount," };
const File_t kCollisionBinding = { "sourcesharp/managed/SourceSharp.Server.Interop/PhysicsCollisionEventBinding.cs",
	"public void PostCollision(void* pEvent)" };
const File_t kCollisionImpacts = { "sourcesharp/managed/SourceSharp.Server/PhysicsCollisionImpacts.cs",
	"public void PostCollision(in VCollisionEvent ev)" };

std::string Load( const File_t &f )
{
	const std::string s = ReadStripped( f.pszRel );
	EXPECT_FALSE( s.empty() ) << "could not open " << f.pszRel
		<< " — every absence invariant below would pass vacuously";
	EXPECT_NE( s.find( f.pszAnchor ), std::string::npos )
		<< f.pszRel << " does not contain its anchor \"" << f.pszAnchor
		<< "\"; the file this test reasons about has moved or been rewritten";
	return s;
}

// Every .cpp and .h under one directory of the shim, comments stripped, so a
// scan for "nothing calls this" cannot be defeated by the call being added to a
// file this test did not think to name.
void CollectStripped( const std::string &strRelDir, std::vector<std::pair<std::string, std::string> > &out )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return;

	const std::string dir = std::string( pszRoot ) + "/" + strRelDir;
	DIR *pDir = opendir( dir.c_str() );
	if ( !pDir )
		return;

	for ( struct dirent *pEnt = readdir( pDir ); pEnt; pEnt = readdir( pDir ) )
	{
		const std::string name( pEnt->d_name );
		const bool bSource = name.size() > 4 && name.compare( name.size() - 4, 4, ".cpp" ) == 0;
		const bool bHeader = name.size() > 2 && name.compare( name.size() - 2, 2, ".h" ) == 0;
		if ( !bSource && !bHeader )
			continue;

		const std::string raw = ReadRaw( dir + "/" + name );
		if ( raw.empty() )
			continue;

		out.push_back( std::make_pair( strRelDir + "/" + name, StripComments( raw ) ) );
	}
	closedir( pDir );
}

} // namespace

TEST( GibEvicted, NothingInTheShimReachesIntoGibHAnyMore )
{
	const std::string wq = Load( kWorldQuery );

	// The five symbols shell_worldquery.o used to owe gib.o, measured per symbol
	// over the debug server objects with the owed instrument of the day --
	// `nm --undefined-only` across the link line. That instrument is deleted
	// with the rest of the removal-set family; the five names are the finding
	// and they are what is asserted, one site each, below:
	//
	//     CGib::DieThink()          CGib::SetBloodColor(int)
	//     CGib::Spawn(char const*)  typeinfo for CGib
	//     CreateRagGib(char const*, Vector const&, QAngle const&, ...)
	//
	// Each is asserted ABSENT at the site that used to have it, because a
	// link that got smaller and a source that stopped naming the class are
	// different facts and only the second survives someone re-adding the vpc row.
	EXPECT_EQ( wq.find( "CGib" ), std::string::npos )
		<< "shell_worldquery.cpp names CGib again.";
	EXPECT_EQ( wq.find( "CreateRagGib" ), std::string::npos )
		<< "shell_worldquery.cpp calls CreateRagGib again.";

	// AND THE SLOT IS GONE, refusal and all (plan_server_native_v2 T2): no
	// managed code called it -- a gib is ss_gib, made by SourceSharp.Server's
	// Gibs, which TheManagedGibAndItsMakerAreBothThere pins. A slot coming back
	// would be a second, native gib maker.
	EXPECT_EQ( wq.find( "SourceSharp_ShootGib(" ), std::string::npos )
		<< "SourceSharp_ShootGib is back in shell_worldquery.cpp; gibs are "
		<< "managed (ss_gib).";

	// And nothing anywhere in the shim reaches into gib.h. Zero consumers means
	// zero, and the scan is what says so rather than the one file above.
	std::vector<std::pair<std::string, std::string> > files;
	CollectStripped( "sourcesharp/game/server", files );
	CollectStripped( "sourcesharp/game/shared", files );
	ASSERT_FALSE( files.empty() ) << "scanned no files — this test would pass vacuously";

	for ( size_t i = 0; i < files.size(); i++ )
	{
		EXPECT_EQ( files[ i ].second.find( "#include \"gib.h\"" ), std::string::npos )
			<< files[ i ].first << " includes gib.h again; gib.o has a consumer "
			<< "once more and the eviction this file records is being undone.";
	}
}

//-----------------------------------------------------------------------------
// AND THE MANAGED HALF EXISTS, which is the other direction of the same claim.
//
// "gib.cpp is gone" and "gib.cpp is gone and a gib still gets thrown" are
// different facts, and only the second is the port. The BEHAVIOUR is asserted
// in C# (GibTests, sixteen facts); what is asserted here is that the two files
// those facts are about are the ones the eviction handed the work to.
//-----------------------------------------------------------------------------
TEST( GibEvicted, TheManagedGibAndItsMakerAreBothThere )
{
	const std::string gib = NoWhitespace( Load( kSsGib ) );
	const std::string gibs = NoWhitespace( Load( kGibs ) );

	ASSERT_FALSE( gib.empty() );
	ASSERT_FALSE( gibs.empty() );

	// The three simulation modes stock had, all three answered. Point and
	// physics are the entity's; ragdoll is a DIFFERENT entity, exactly as stock
	// built a CRagGib rather than a configured CGib for it.
	EXPECT_NE( gibs.find( "GibSimulation.Ragdoll" ), std::string::npos )
		<< "the ragdoll simulation mode has gone; CreateRagGib's replacement is "
		<< "what answers GIB_SIMULATE_RAGDOLL";
	EXPECT_NE( gibs.find( "EntityNames.SsRagdoll" ), std::string::npos )
		<< "the ragdoll gib no longer makes an ss_ragdoll";
	EXPECT_NE( gib.find( "GibSimulation.Physics" ), std::string::npos )
		<< "the solver simulation mode has gone";
	EXPECT_NE( gib.find( "_physics.InitPhysics(" ), std::string::npos )
		<< "a physics gib no longer asks the solver for an object";
	EXPECT_NE( gib.find( "_motion.Launch(" ), std::string::npos )
		<< "a point gib is no longer thrown by the managed integrator";
}

//-----------------------------------------------------------------------------
// THE THREE FORMER BLOCKERS, WHICH ARE NOW THE THREE FOUNDATIONS.
//
// Each of these was the reason the gib was NOT ported, and each was answered by
// a different lane before step 4a spent them. They are kept, unchanged in
// substance, because their subjects are now load-bearing rather than absent: a
// managed gib needs a per-instance skin and model scale to be drawn right, needs
// the touch path to bleed, and needs a ragdoll-from-model slot to serve
// GIB_SIMULATE_RAGDOLL. Red here no longer means "re-take the verdict"; it means
// the port has lost something it stands on.
//-----------------------------------------------------------------------------

// AND WHERE THE FIX WOULD HAVE TO GO, which is why lane U5 did not make it.
// ANSWERED by plan_physics_managed B2.2 (2026-10-05): native's PostCollision
// resolved the owner through the SDK arm and gave up on a standalone entity,
// so a managed entity's collision was lost there. That body is deleted and
// the callback is managed's, which records the impact for whichever side has
// a managed owner. The gib ragdoll verdict is worth re-taking on this basis.
TEST( GibEvicted, BlockerOne_AStandaloneEntitysCollisionReachesManaged )
{
	const std::string physics  = Load( kServerPhysics );
	const std::string binding  = Load( kCollisionBinding );
	const std::string impacts  = Load( kCollisionImpacts );

	// The give-up is gone with its function.
	EXPECT_EQ( std::string::npos, physics.find( "CCollisionEvent::PostCollision(" ) )
		<< "native's PostCollision body is back";
	EXPECT_EQ( std::string::npos, NoWhitespace( physics ).find(
	               "IGameEntity*pEntity=PhysicsOwnerOf(pObject).pBase;if(!pEntity)return;" ) )
		<< "the SDK-arm give-up is back in physics.cpp";

	// And the managed half takes the event and records it by managed owner.
	EXPECT_NE( std::string::npos, NoWhitespace( binding ).find(
	               "impacts.PostCollision(in*(VCollisionEvent*)pEvent);" ) )
		<< "the binding no longer hands PostCollision to the impact sink";
	EXPECT_NE( std::string::npos, NoWhitespace( impacts ).find(
	               "if(owner0==EntityHandle.None&&owner1==EntityHandle.None)return;" ) )
		<< "the impact sink no longer keeps a collision with one managed owner";
	EXPECT_NE( std::string::npos, NoWhitespace( impacts ).find( "post(record);" ) )
		<< "the impact sink no longer posts the record";
}

// BLOCKER 2, AS CORRECTED. RE-TAKEN 2026-08-24.
//
// The claim was that "the standalone hierarchy has no per-instance skin or
// model scale -- GetSkin() and GetModelScale() are hard-wired constants". That
// was true and is not any more, so what this test pins is the REPLACEMENT
// rather than the absence -- the same move BlockerOne made when its reasoning
// was corrected, and for the same reason: the value of these tests is that the
// REASONING stays true, not that the conclusion does.
//
// WHAT WAS ACTUALLY BUILT, because "we added two SendProps" would have been the
// expensive answer and is not what happened. The wire is UNCHANGED and still
// six fields. A skin and a scale ride the payload as ordinary managed state, so
// a class that wants one declares a field and pays nothing on any other entity;
// what the hierarchy was missing was never the wire, it was somewhere for the
// value to LAND once it arrived. Both halves of that are asserted -- here and
// in the test below -- because "the blocker is gone" and "the blocker is gone
// WITHOUT the cost that made it a blocker" are different facts, and only the
// second of them is the verdict.
TEST( GibEvicted, BlockerTwo_TheHierarchyNowHasAPerInstanceSkinAndModelScale )
{
	// (a) The size, on the server, where the SKELETON is built from it and
	// where scaled_collision_rule.h reads it.
	const std::string server = Load( kStandaloneServer );

	EXPECT_NE( NoWhitespace( server ).find( "floatGetModelScale()const{returnm_flModelScale;}" ),
	           std::string::npos )
		<< "the standalone server shell no longer answers its model scale from a "
		<< "member.\n"
		<< "If it has gone back to a constant, BLOCKER 2 IS BACK and the gib "
		<< "verdict is worth re-taking the other way -- see the header.";

	// (b) The skin, on the client, which is the side that draws.
	const std::string client = Load( kStandaloneClient );

	EXPECT_NE( NoWhitespace( client ).find( "virtualintGetSkin()OVERRIDE{returnBlock().Client.Skin;}" ),
	           std::string::npos )
		<< "C_StandaloneEntity::GetSkin no longer answers from per-instance "
		   "storage. Step 5c moved the storage from m_nSkin into the ENGINE "
		   "BLOCK's client tail (plan_entity.md §3.2); what this blocker "
		   "is about is unchanged -- that the skin is per instance and not a "
		   "constant -- so the needle moved with the spelling and nothing was "
		   "relaxed.";

	// (c) And the PLAYER's, which is where this blocker's needle used to point.
	// Asserted as the DELEGATION rather than as the absence of the constant:
	// "the 1.0f has gone" is also satisfied by the method having been deleted
	// outright, which would be the blocker back with nothing to say so. Both
	// are checked, in that order.
	//
	// In stock these are the same member -- CBasePlayer::GetModelScale forwards
	// to BaseClass::GetModelScale, which is CBaseAnimating::m_flModelScale --
	// so a player drawn at one size and collided at another is the divergence,
	// not the fix.
	const std::string player = Load( kStandalonePlayer );

	EXPECT_NE( NoWhitespace( player ).find( "returnCStandaloneEntity::GetModelScale();" ),
	           std::string::npos )
		<< "the standalone player no longer takes its model scale from the shell.\n"
		<< "shareddefs.h multiplies every view height and every duck-hull corner "
		<< "by this, so a player whose hull and whose skeleton disagree about its "
		<< "size is a player that ducks under the wrong doorways.";

	EXPECT_EQ( NoWhitespace( player ).find( "GetModelScale()constOVERRIDE{return1.0f;}" ),
	           std::string::npos )
		<< "the hard-wired 1.0f is back on the standalone player";

	// (d) AND THE ONE PLACE THAT WAS ALREADY ASKING, which is the part of this
	// blocker that had a test and could not fail.
	//
	// procedural_bone_rule.h's ProceduralBone_WantsIk( nIkChains, bModelScaled )
	// refuses a solver to a scaled model, and it is not an optimisation: the
	// solver marks the bones it placed as already computed, and the scale is
	// applied afterwards -- so a solved bone would be the one bone in the
	// skeleton that did not grow. procedural_bone_rule_test.cpp proves that
	// rule, thoroughly, in AScaledModelOptsOutOfIkHoweverManyChainsItHas.
	//
	// Its only caller passed a LITERAL false, because this hierarchy had no
	// per-instance size to give it. So the rule was correct and DECORATIVE --
	// tested exhaustively and never asked the question. Now that the size is
	// real the caller has to hand it over, and this is the assertion that says
	// so, because nothing in the rule's own test can.
	const std::string client2 = Load( kStandaloneClientCppScale );

	EXPECT_EQ( NoWhitespace( client2 ).find( "ProceduralBoneRule.WantsIk(s.ModelIkChains,false)" ),
	           std::string::npos )
		<< "the client bone build passes a LITERAL false for bModelScaled again.\n"
		<< "procedural_bone_rule.h's opt-out is then a rule with no caller: it "
		<< "is proven by its own test and asked nothing, and a scaled model with "
		<< "IK chains gets a solver that marks bones as computed which the scale "
		<< "then does not reach.";

	EXPECT_NE( NoWhitespace( client2 ).find( "ProceduralBoneRule.WantsIk(s.ModelIkChains,s.ModelScale!=1.0f)" ),
	           std::string::npos )
		<< "the client bone build no longer asks the IK opt-out about this "
		<< "entity's actual size";
}

// AND THE COST THAT DID NOT ARRIVE, which is the half that decides the verdict.
//
// The gib port was refused because it would "ADD a networked field to every
// managed entity in the game twice over". If the skin and the scale had been
// bought with two more SendProps, blocker 2 would be gone and the REASON for
// the refusal would still stand -- so this is asserted apart from the
// capability above, and a red here means the price was paid after all.
//
// STEP 8e-render DID ADD ONE FIELD, m_fEffects, and the body below says why
// that is not the price this verdict weighed. It is recorded in the PRESENT
// list rather than left out, so the wire's width stays a number this test
// states rather than one it stopped counting.
TEST( GibEvicted, BlockerTwo_AndTheWireDidNotGrowToPayForIt )
{
	// The wire itself, read where the names actually are: the shared table both
	// sides build from, not the server function that reads offsets out of it.
	//
	// Asserted as SEVEN NAMES PRESENT and four names ABSENT, rather than as a
	// count. A count of seven is satisfied by seven copies of one, and an
	// absence list alone is satisfied by a file that has been emptied.
	//
	// IT WAS SIX UNTIL STEP 8e-render, AND THE SEVENTH IS A REAL COST -- said
	// here rather than absorbed, because this test's whole subject is what the
	// wire was allowed to grow by. m_fEffects is EF_MAX_BITS on every entity on
	// the hierarchy, which is the same shape of price the gib verdict refused to
	// pay for m_nSkin and m_flModelScale. What makes it a different trade rather
	// than the same one reversed: skin and scale are wanted by the FEW classes
	// that draw a varied model, and belong in those classes' payloads;
	// EF_NODRAW is the answer to "is this entity drawn", which every entity on
	// the hierarchy has and which the shell itself reads per draw through
	// ShouldDraw. Without it "invisible" and "shapeless" were one state, and
	// three whole effect families -- dust, smoke, precipitation -- rendered
	// nothing (step 7a's cs_cl_dust_report). The verdict below is UNCHANGED:
	// the four names in kAbsent are still absent, so blocker 2 was not paid for.
	//
	// AND THE VERDICT WAS RE-TAKEN FOR ONE OF THEM, 2026-10-19: m_nSkin is now on
	// the wire, the eighth name present. The user asked that a skin the server
	// picks reach the client (env_beverage's can, a gib), and the per-class
	// payload route needs a managed CLIENT class for every class that picks a
	// skin, which item_sodacan and ss_gib do not have. The price is the one
	// m_fEffects was allowed: ten bits on every entity's table, which delta
	// compression leaves out while the skin equals the baseline's zero. The
	// other three stay absent.
	const std::string wire = Load( kNetTable );

	static const char *kPresent[] = { "\"m_vecOrigin\"", "\"m_angRotation\"",
	                                  "\"m_nModelIndex\"", "\"m_fEffects\"",
	                                  "\"m_nTypeId\"",
	                                  "\"m_nSchemaHash\"", "\"m_nPayloadWords\"",
	                                  "\"m_nSkin\"" };
	for ( size_t i = 0; i < sizeof( kPresent ) / sizeof( kPresent[ 0 ] ); i++ )
	{
		EXPECT_NE( wire.find( kPresent[ i ] ), std::string::npos )
			<< "standalone_nettable.h no longer networks " << kPresent[ i ]
			<< "; the seven fields this blocker counts have changed";
	}

	static const char *kAbsent[] = { "\"m_clrRender\"",
	                                 "\"m_nRenderMode\"", "\"m_flModelScale\"" };
	for ( size_t i = 0; i < sizeof( kAbsent ) / sizeof( kAbsent[ 0 ] ); i++ )
	{
		EXPECT_EQ( wire.find( kAbsent[ i ] ), std::string::npos )
			<< "standalone_nettable.h now networks " << kAbsent[ i ] << ".\n"
			<< "THE PRICE WAS PAID AFTER ALL: the per-instance render state that "
			<< "closed BLOCKER 2 was bought with a prop on every entity on the "
			<< "hierarchy rather than with a payload field on the few that want "
			<< "one. That is the cost the gib verdict weighed the port against, "
			<< "so the verdict is worth re-taking -- see the header.";
	}
}

// BLOCKER 3, AS CORRECTED. RE-TAKEN 2026-08-24.
//
// The claim was that "GibShot.Simulation == 2 is a jointed body, built by
// CreateRagGib through CBaseAnimating::BecomeRagdollOnClient, and there is no
// managed way to make one". There is one now:
// C_StandaloneEntity::BecomeRagdoll, reached from managed code through the
// StandaloneBecomeRagdoll slot.
//
// THE OLD TEST COULD NOT HAVE CAUGHT IT, and that is worth recording rather
// than quietly replacing. It scanned `sourcesharp/game/server` for the string
// "BecomeRagdollOnClient" and asserted its absence. But BecomeRagdollOnClient
// is CBaseAnimating's SERVER-side method, and what it does is set a renderfx
// and a force so that each CLIENT builds a ragdoll locally -- so the route that
// actually closes this blocker is a client one, in a directory that test never
// read, calling primitives whose names it never mentioned. It would have stayed
// green through the entire change. That is the check-that-cannot-fail shape
// this port has now found forty-odd times: a needle chosen from the mechanism
// that USED to be the answer rather than from the question.
//
// So the question is asserted instead: can a managed entity become a jointed
// body, and did it cost what the refusal said it would cost.
TEST( GibEvicted, BlockerThree_TheHierarchyCanNowBecomeAJointedBody )
{
	// READ OUT OF THE LIBRARY SINCE 6e. The route is unchanged and the file it
	// lives in is not: see kRagdollLibraryCpp. The shell is still read below,
	// for the two facts that are about the SHELL rather than about the solver.
	const std::string client = Load( kRagdollLibraryCpp );
	const std::string clientNoWs = NoWhitespace( client );

	// (a) The primitives, named individually. A ragdoll that is CREATED and
	// never ACTIVATED is the specific failure worth pinning: RagdollActivate is
	// what applies the model's own collision rules, and without it the limbs
	// collide as ordinary solids and the body explodes on its first frame --
	// which reads as a physics bug rather than as a missing call.
	EXPECT_NE( clientNoWs.find( "solver.Create(body,p,environment);" ), std::string::npos )
		<< "the flat creation no longer builds a ragdoll.\n"
		<< "BLOCKER 3 IS BACK -- re-take the verdict; see the header.";

	EXPECT_NE( clientNoWs.find( "solver.Activate(body,collide,pose.ModelIndex);" ),
	           std::string::npos )
		<< "the ragdoll is created and never activated: its limbs will collide "
		<< "with each other as ordinary solids and the body will explode";

	EXPECT_NE( clientNoWs.find( "solver.Destroy(body,environment);" ), std::string::npos )
		<< "nothing releases the solver objects; one leak per limb per gib";

	// (b) The POSE it collapses from, which is the input that makes this a
	// ragdoll from a MODEL rather than from nothing. RagdollCreate places each
	// element at the bone its .phy names, read out of pCurrentBones.
	EXPECT_NE( clientNoWs.find( "CurrentBones=bones," ), std::string::npos )
		<< "the ragdoll is no longer built from the caller's bone snapshot, so "
		<< "its elements start wherever the matrices happened to be";

	// AND THE SHELL STILL HANDS OVER ITS OWN SKELETON. The needle above says the
	// library reads the snapshot it was given; this says the snapshot is THIS
	// ENTITY's pose. Two files, two assertions -- a library that read its input
	// faithfully from a caller that handed it nothing would pass the first.
	//
	// The skeleton is managed since plan physics_bones_native_zero 1.2's client
	// half, and so is the body since Phase 3: the snapshot is the entity's own
	// pose from the managed skeleton (BONE_USED_BY_ANYTHING), and Become is
	// handed that.
	const std::string ragdollShell = NoWhitespace( Load( kStandaloneClientCpp ) );

	EXPECT_NE( ragdollShell.find( "StandaloneBones?bones=skeletons.BoneMatrices(owner);" ),
	           std::string::npos )
		<< "the ragdolls no longer pose the skeleton they build the body from";
	EXPECT_NE( clientNoWs.find( "RagdollPosepose=poses.Pose(owner);" ), std::string::npos )
		<< "the body is no longer built from its entity's own pose";

	// (c) The REFUSAL for a model that has no jointed body, which is most of
	// them. Without it a one-solid collide produces a single-element "ragdoll"
	// that is a rigid lump pretending to be a body, and the caller has no way
	// to fall back -- which is exactly what CRagGib::Spawn's own
	// `if ( !BecomeRagdollOnClient( vecForce ) )` exists to do.
	EXPECT_NE( clientNoWs.find( "collide.SolidCount<=1" ), std::string::npos )
		<< "BecomeRagdoll no longer refuses a model with a single-solid collide; "
		<< "a rigid prop will now become a one-element ragdoll and the caller "
		<< "will not be told";

	// (d) WAS "AND IT IS NOT A SECOND CRagdoll", which held while the shell was
	// a second CALLER of the native primitives. Phase 3 moved CRagdoll and the
	// primitives themselves to managed code (RagdollSolver), so there is one
	// implementation again and it is the managed one.
}

// THE FACT THE WHOLE ROUTE RESTS ON, asserted where it lives rather than
// paraphrased here: the ragdoll builder takes no entity of either kind.
//
// This is the one that makes BlockerThree above cheap. If ragdollparams_t grows
// a CBaseEntity, or RagdollCreate starts taking one, the standalone caller
// stops being a caller and becomes a port -- and the cost of this route
// changes completely without anything else going red.
TEST( GibEvicted, BlockerThree_TheRagdollBuilderTakesNoEntityAtAll )
{
	const std::string shared = Load( kRagdollShared );

	// Scoped to the STRUCT rather than to the file: ragdoll_shared.h also
	// declares CRagdollLRURetirement, which holds CHandle<CBaseAnimating> and
	// would satisfy a file-wide search for the very names this test is about.
	// That is the check that cannot fail, avoided.
	const size_t iStart = shared.find( "struct ragdollparams_t" );
	ASSERT_NE( iStart, std::string::npos ) << "ragdollparams_t has gone";

	const size_t iEnd = shared.find( "};", iStart );
	ASSERT_NE( iEnd, std::string::npos ) << "ragdollparams_t has no end";

	const std::string params = shared.substr( iStart, iEnd - iStart );

	EXPECT_NE( params.find( "void\t\t*pGameData;" ), std::string::npos )
		<< "ragdollparams_t no longer carries its owner as an untyped pointer";

	EXPECT_EQ( params.find( "CBaseEntity" ), std::string::npos )
		<< "ragdollparams_t now names CBaseEntity.\n"
		<< "The standalone ragdoll route rests on this struct being entity-free: "
		<< "it is what lets the shell CALL the ragdoll primitives rather than "
		<< "reimplement them. Re-read BlockerThree.";
	EXPECT_EQ( params.find( "C_BaseEntity" ), std::string::npos )
		<< "ragdollparams_t now names C_BaseEntity; see above";

	// And the builder itself.
	EXPECT_NE( NoWhitespace( shared ).find(
	               "boolRagdollCreate(ragdoll_t&ragdoll,constragdollparams_t&params,"
	               "IPhysicsEnvironment*pPhysEnv)" ),
	           std::string::npos )
		<< "RagdollCreate's signature has changed; if it now takes an entity, "
		<< "the standalone route is a port rather than a call";
}

//-----------------------------------------------------------------------------
// The gap that is NOT a blocker, recorded so it is not re-derived: a sprite
// `shootmodel`.
//-----------------------------------------------------------------------------
TEST( GibEvicted, ASpriteShootmodelThrowsAnInvisibleGibAndSaysSo )
{
	const std::string gibs = NoWhitespace( Load( kGibs ) );

	ASSERT_FALSE( gibs.empty() );

	// Asked of the ENGINE rather than of the filename. Stock sniffs for ".vmt"
	// with Q_stristr; what matters is what the loaded model IS.
	//
	// Matched on the WHOLE test and not on `ModelTypeOf(` alone, which is the
	// difference between a check and a check that cannot fail -- the same
	// mistake this assertion's native ancestor was caught making, where a bare
	// `GetModelType(` was satisfied by the areaportal brush test in the same
	// file while the sprite test had been mutated away.
	EXPECT_NE( gibs.find( "_precache.ModelTypeOf(index)==ModelType.Sprite" ), std::string::npos )
		<< "Gibs.Shoot no longer asks the engine whether the model is a SPRITE; "
		<< "a sprite worn by a gib reaches the client's `case mod_sprite:` and "
		<< "prints an engine error once per gib in the volley";

	// It is SAID, once per model rather than once per gib. Six identical lines a
	// volley is how the engine error this replaces went unnoticed for as long as
	// it did.
	EXPECT_NE( gibs.find( "_spritesSaid.Add(model)" ), std::string::npos )
		<< "the once-per-model guard on the sprite warning has gone";
}
