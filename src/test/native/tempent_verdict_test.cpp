//====== SourceSharp — C_LocalTempEntity is DE-ENTIFIED, and why ============//
//
// Lane U6, docs/typedef-dryrun/lane_u/ledger.md §5. The ledger evaluated a
// de-entify for this class and REFUSED it; the refusal has been re-taken and
// the de-entify carried out. This file asserts the facts the new verdict rests
// on, so that a red here means RE-TAKE THE VERDICT rather than "a regression".
// Same standard as gib_verdict_test.cpp, rope_verdict_test.cpp and
// shim_underive_verdict_test.cpp.
//
// THE LEDGER'S REFUSAL, verbatim:
//
//     A de-entify I evaluated and REFUSE. The obvious answer is "a temp entity
//     is not a real entity, make it a plain drawable". It is not available:
//     c_te_legacytempents.cpp calls ClientEntityList().AddNonNetworkableEntity(
//     this ) (lines 671, 1207, 2158) and AddToLeafSystem( m_RenderGroup )
//     (2029, 2154, 2345). It IS in the client entity list and IS in the leaf
//     system, so it owes IClientEntity + IClientRenderable + IClientUnknown —
//     and the only thing in this tree that implements that floor is
//     C_StandaloneEntity (shell_standalone_client.h:86), 4,631 lines. A
//     de-entify here is a second copy of it.
//
// TWO PREMISES, BOTH FALSE, and each is asserted below so it cannot become true
// again without this file going red.
//
//   (1) "It IS in the client entity list."  It is not. All three
//       AddNonNetworkableEntity calls were dead in this build:
//
//         AddParticleEffect      no caller. Its callers are TF's, and TF is not
//                                in this build.
//         ClientProjectile       inside `if ( pszParticleEffect )`, and both
//                                live callers pass SIX arguments, so that
//                                parameter is always its NULL default.
//         TempEntAllocHigh       inside `if ( CommandLine()->CheckParm(
//                                "-tools" ) )`, whose twin in TempEntAlloc
//                                Valve had already commented out because
//                                recording tempents as entities crashes.
//
//       The SDK says it itself, two lines before it makes one visible:
//       "Temporary entities have no corresponding element in cl_entitylist".
//       So the floor owed is IClientUnknown + IClientRenderable, not
//       IClientEntity.
//
//   (2) "the only thing in this tree that implements that floor is
//       C_StandaloneEntity."  sourcesharp/game/client/shell_renderable.h —
//       CShellRenderable, 214 lines, header-only, already in this build and
//       already carrying CSourceSharpSpriteTrail — implements exactly it,
//       through CDefaultClientRenderable, and supplies AddToLeafSystem(
//       RenderGroup_t ) with the very signature this class calls.
//
// AND THE BLOB DID NOT REFUSE THE OTHER ROUTE EITHER, which was the ledger's
// single highest-value open question. Measured with src/tempent_layout_probe.cpp
// in the client project's own compiler, debug and release agreeing:
// SourceSharp_StandaloneBlobOffset() is 2,112 and a re-parented tempent is
// 1,320, so operator new's assert does not fire and every derived member is
// clear of the blob window. Re-parenting onto C_StandaloneEntity is refused on
// COST — 130 diagnostics naming 30 distinct missing members, plus a second,
// non-networkable constructor in a shell this lane does not own — not on the
// blob. That refusal has its own assertion below.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <fstream>
#include <string>
#include <sys/stat.h>
#include <iterator>

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

std::string ReadRaw( const std::string &strAbsolute )
{
	std::ifstream file( strAbsolute.c_str(), std::ios::binary );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
}

// Comments are stripped BEFORE anything is searched for, and that is not
// hygiene: this change left long banners in both files naming every construct
// it removed. A scan that read them would find AddNonNetworkableEntity,
// ParticleProp and C_BaseAnimating in a file that has none of them, and would
// pass for the rest of the port while measuring nothing.
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

std::string ReadSquashed( const std::string &strRelative )
{
	return NoWhitespace( ReadStripped( strRelative ) );
}

const char *kTempentH   = "game/client/tempent.h";
// The thunk that replaced game/client/c_te_legacytempents.cpp when C2 evicted it.
const char *kTempentCpp = "sourcesharp/game/client/shell_legacy_tempents_client.cpp";
const char *kShellRend  = "sourcesharp/game/client/shell_renderable.h";
const char *kRenderable = "public/iclientrenderable.h";
const char *kStdClientH  = "sourcesharp/game/client/shell_standalone_client.h";
const char *kStdClientCpp= "sourcesharp/game/client/shell_standalone_client.cpp";

// Every subject file has to be READABLE before any assertion about its
// contents means anything: a find() that misses on an empty string is
// indistinguishable from a find() that misses on a file that changed. This is
// the first test rather than a helper for that reason.
TEST( TempEntVerdict, EverySubjectFileIsReadable )
{
	const char *kAll[] = { kTempentH, kTempentCpp, kShellRend, kRenderable,
	                       kStdClientH, kStdClientCpp };

	ASSERT_NE( SrcRoot(), (const char *)NULL )
		<< "src/ was not found from the working directory, so every scan below "
		   "would search an empty string and pass.";

	for ( size_t i = 0; i < sizeof( kAll ) / sizeof( kAll[ 0 ] ); i++ )
	{
		EXPECT_FALSE( ReadStripped( kAll[ i ] ).empty() )
			<< kAll[ i ] << " could not be read or is empty. Nothing else in "
			                "this file is evidence until it can be.";
	}
}

//-----------------------------------------------------------------------------
// THE VERDICT ITSELF.
//-----------------------------------------------------------------------------
TEST( TempEntVerdict, TheTempEntIsARenderableAndNotAnEntity )
{
	const std::string hdr = ReadSquashed( kTempentH );

	ASSERT_FALSE( hdr.empty() )
		<< kTempentH << " could not be read; the two ABSENCE assertions below "
		   "would hold trivially.";

	EXPECT_NE( hdr.find( "classC_LocalTempEntity:publicCShellRenderable,"
	                     "publicC_SpriteRenderer" ), std::string::npos )
		<< "C_LocalTempEntity's base list has changed. If it is an entity "
		   "again, this whole file is stale; if it moved to a THIRD base, the "
		   "carried-over answers below were priced against CShellRenderable's "
		   "and have to be re-priced.";

	EXPECT_EQ( hdr.find( "publicC_BaseAnimating" ), std::string::npos )
		<< "C_LocalTempEntity derives from C_BaseAnimating again. It used "
		   "nothing from it -- DrawStudioModel calls modelrender->DrawModel "
		   "directly with MODEL_INSTANCE_INVALID and no bone setup -- and the "
		   "layer was 3,032 of the object's 3,240 bytes.";

	EXPECT_EQ( hdr.find( "#include\"c_baseanimating.h\"" ), std::string::npos )
		<< "tempent.h includes c_baseanimating.h again. It is the include that "
		   "put c_te_legacytempents.o inside the U-CBaseAnimating cluster's "
		   "blast radius.";
}

//-----------------------------------------------------------------------------
// PREMISE (1): the entity list.
//-----------------------------------------------------------------------------
TEST( TempEntVerdict, NoTempEntGoesIntoTheClientEntityList )
{
	const std::string cpp = ReadSquashed( kTempentCpp );

	// THE GATE, and it is not boilerplate. Every assertion in this test is an
	// ABSENCE, and an absence holds trivially in an empty string -- so with the
	// tree unreadable this test passed while measuring nothing. The mutation
	// run that proved these seven tests can fail is what found it: run from a
	// directory where src/ does not resolve, six went red and this one stayed
	// green.
	ASSERT_FALSE( cpp.empty() )
		<< kTempentCpp << " could not be read. Every assertion below is an "
		   "ABSENCE and would hold trivially -- this test would report a pass "
		   "it had not earned.";

	EXPECT_EQ( cpp.find( "AddNonNetworkableEntity" ), std::string::npos )
		<< "A tempent is being put into the client entity list again. That is "
		   "the ledger's whole floor argument -- it is what made this class owe "
		   "IClientEntity -- and CShellRenderable cannot supply it: "
		   "CDefaultClientRenderable::SetRefEHandle is an Assert(false) and "
		   "GetRefEHandle a null dereference. RE-TAKE THE VERDICT.";

	EXPECT_EQ( cpp.find( "ParticleProp()" ), std::string::npos )
		<< "ParticleProp() is back. It is C_BaseEntity's, and every caller of "
		   "it here also needed the entity list -- a particle that FOLLOWS a "
		   "tempent finds it by handle. RE-TAKE THE VERDICT.";
}

//-----------------------------------------------------------------------------
// PREMISE (2): the floor is implemented, once, by something small.
//-----------------------------------------------------------------------------
TEST( TempEntVerdict, CShellRenderableStillSuppliesTheFloor )
{
	const std::string shell = ReadSquashed( kShellRend );

	EXPECT_NE( shell.find( "classCShellRenderable:publicCDefaultClientRenderable" ),
	           std::string::npos )
		<< "CShellRenderable no longer derives from CDefaultClientRenderable, "
		   "which is where the IClientUnknown + IClientRenderable floor comes "
		   "from. RE-TAKE THE VERDICT.";

	EXPECT_NE( shell.find( "voidAddToLeafSystem(RenderGroup_tgroup)" ),
	           std::string::npos )
		<< "CShellRenderable::AddToLeafSystem( RenderGroup_t ) is gone or has "
		   "changed shape. It is the one call this class needed an entity for.";

	EXPECT_NE( shell.find( "voidRemoveFromLeafSystem()" ), std::string::npos )
		<< "CShellRenderable::RemoveFromLeafSystem is gone. CTempEnts::"
		   "TempEntFree calls it on every tempent it frees.";

	// The place setters MUST notify the leaf system. A plain assignment here is
	// the silent failure shell_leaf_link.h names: the object keeps drawing from
	// the leaves it was FIRST added to, so a shell casing that bounces out of
	// the room it spawned in stops being drawn.
	EXPECT_NE( shell.find( "voidSetAbsOrigin(constVector&vecOrigin){"
	                       "if(m_vecOrigin==vecOrigin)return;m_vecOrigin="
	                       "vecOrigin;RenderableMoved();}" ), std::string::npos )
		<< "CShellRenderable::SetAbsOrigin no longer tells the leaf system the "
		   "object moved. Every tempent moves every frame; without that call "
		   "they draw only from where they spawned.";
}

//-----------------------------------------------------------------------------
// THE ANSWERS THAT ARE WRONG BY DEFAULT RATHER THAN MISSING.
//
// This is where a de-entified renderable stops being drawn, and none of these
// produces a diagnostic at any -W level. Each assertion below pairs the
// override with the DEFAULT it exists to replace, so that a default which
// changed would show up as an over-carry rather than being assumed forever.
//-----------------------------------------------------------------------------
TEST( TempEntVerdict, TheTempEntAnswersWhatTheDefaultsGetWrong )
{
	const std::string hdr = ReadSquashed( kTempentH );
	const std::string def = ReadSquashed( kRenderable );

	// --- the alpha ----------------------------------------------------------
	EXPECT_NE( def.find( "virtualintGetFxBlend(){return255;}" ), std::string::npos )
		<< "CDefaultClientRenderable::GetFxBlend no longer answers a constant "
		   "255. That constant is the REASON C_LocalTempEntity carries its own "
		   "ComputeFxBlend; if the default became correct, the carry is dead "
		   "weight and should go.";

	EXPECT_NE( hdr.find( "virtualvoidComputeFxBlend(void)OVERRIDE;" ), std::string::npos )
		<< "C_LocalTempEntity::ComputeFxBlend is gone. FTENT_FADEOUT ramps "
		   "m_clrRender.a down and expects the model to fade with it; against "
		   "the default constant 255 a fading tempent stays fully solid and "
		   "then vanishes in one frame.";

	EXPECT_NE( hdr.find( "virtualintGetFxBlend(void)OVERRIDE{returnm_nRenderFXBlend;}" ),
	           std::string::npos )
		<< "C_LocalTempEntity::GetFxBlend is gone -- same consequence.";

	// --- the tint -----------------------------------------------------------
	EXPECT_NE( def.find( "virtualvoidGetColorModulation(float*color)" ), std::string::npos )
		<< "CDefaultClientRenderable::GetColorModulation has moved or changed "
		   "shape; the override below was priced against its 1,1,1 default.";

	EXPECT_NE( hdr.find( "virtualvoidGetColorModulation(float*color)OVERRIDE;" ),
	           std::string::npos )
		<< "C_LocalTempEntity::GetColorModulation is gone. The default answers "
		   "1,1,1 for every renderable, so every tempent that sets a render "
		   "colour -- blood sprites and the impact tempents -- draws white.";

	// --- the pass -----------------------------------------------------------
	EXPECT_NE( def.find( "virtualboolIgnoresZBuffer(void)const{returnfalse;}" ),
	           std::string::npos )
		<< "CDefaultClientRenderable::IgnoresZBuffer no longer answers a "
		   "constant false; the override below was priced against it.";

	EXPECT_NE( hdr.find( "virtualboolIgnoresZBuffer(void)constOVERRIDE" ),
	           std::string::npos )
		<< "C_LocalTempEntity::IgnoresZBuffer is gone. "
		   "DrawTranslucentRenderable SKIPS a renderable whose answer does not "
		   "match the pass it is in, so a kRenderGlow tempent -- every muzzle "
		   "flash -- would never be drawn.";

	EXPECT_NE( hdr.find( "RenderGroup_tGetRenderGroup();" ), std::string::npos )
		<< "C_LocalTempEntity::GetRenderGroup is gone. A tempent CHANGES pass "
		   "while it lives: IsActive() flips kRenderNormal to "
		   "kRenderTransTexture the moment an FTENT_FADEOUT tempent starts "
		   "fading, and a renderable left in the opaque group after that draws "
		   "at full alpha until it is freed.";

	// --- the pure ones ------------------------------------------------------
	EXPECT_NE( hdr.find( "virtualboolShouldDraw(void)OVERRIDE" ), std::string::npos )
		<< "C_LocalTempEntity::ShouldDraw is gone; it is pure on "
		   "CDefaultClientRenderable and this would not compile -- which means "
		   "it moved somewhere this file cannot see it. RE-TAKE THE VERDICT.";

	EXPECT_NE( hdr.find( "virtualboolIsTransparent(void)OVERRIDE;" ), std::string::npos )
		<< "C_LocalTempEntity::IsTransparent is gone -- see ShouldDraw.";

	EXPECT_NE( hdr.find( "virtualvoidGetRenderBounds(Vector&theMins,Vector&theMaxs)OVERRIDE;" ),
	           std::string::npos )
		<< "C_LocalTempEntity::GetRenderBounds is gone -- see ShouldDraw.";
}

//-----------------------------------------------------------------------------
// THE OTHER ROUTE, and the two facts its refusal is priced on. Neither is the
// blob: the blob was measured and does not bind.
//-----------------------------------------------------------------------------
TEST( TempEntVerdict, ReparentingOntoTheStandaloneShellIsStillTheExpensiveRoute )
{
	const std::string stdCpp = ReadSquashed( kStdClientCpp );
	const std::string stdH   = ReadSquashed( kStdClientH );

	// (a) THE BLOB DOES NOT BIND, and this pins the arithmetic that says so.
	//     The constant is ONE base for the whole hierarchy, sized to the
	//     LARGEST shell. It used to take a MAX over two shells because there
	//     were two -- the player subclass ran 2,104 bytes against the entity's
	//     1,056 -- and plan_entity.md step 6f deleted the subclass: the client
	//     player's state is the managed player's, so C_StandaloneEntity is the
	//     only shell and the MAX has one argument left. A re-parented tempent is
	//     1,320, which is what the verdict is about, and the base it must fit
	//     under is now the SMALLER of the two numbers this line used to compare
	//     -- so the refusal is stronger than when it was measured, not weaker.
	//     If this ever becomes per class the way the LENGTH already is, the
	//     measurement is stale.
	EXPECT_NE( stdCpp.find( "return(int)ALIGN_VALUE(sizeof(C_StandaloneEntity),16);" ),
	           std::string::npos )
		<< "SourceSharp_StandaloneBlobOffset is no longer one constant sized to "
		   "the client hierarchy's single shell. RE-MEASURE: "
		   "src/tempent_layout_probe.cpp reports the numbers this verdict used.";

	EXPECT_NE( stdCpp.find( "Assert((int)nBytes<=SourceSharp_StandaloneBlobOffset());" ),
	           std::string::npos )
		<< "C_StandaloneEntity::operator new no longer asserts that a subclass "
		   "fits under the shared blob base. That assert is the thing this "
		   "lane measured NOT to fire; without it the measurement has no "
		   "subject. RE-MEASURE.";

	// (b) THE COST THAT DOES REFUSE IT. C_StandaloneEntity's only constructor
	//     binds the object under a NETWORKED entity index, so 500 pooled
	//     tempents would all claim index 0 -- the world. Re-parenting means
	//     adding a second, non-networkable constructor to a shell this lane
	//     does not own.
	//
	//     REPOINTED BY plan_entity.md STEP 5a, PRICE UNCHANGED. The spelling was
	//     cl_entitylist->AddNetworkableEntity( GetIClientUnknown(), nEntNum,
	//     nSerialNum ); the client shim stopped naming the entity list and the
	//     call is now SourceSharp_ClientEntityBind, which binds §3.3's slot
	//     table AND (while the port seam lasts) performs that same
	//     AddNetworkableEntity. Still one unconditional call, still under a
	//     networked index, still no non-networkable arm -- shell_cliententitylist.h
	//     says why the two halves are behind one name.
	EXPECT_NE( stdCpp.find( "SourceSharp_ClientEntityBind(this,nEntNum,nSerialNum);" ),
	           std::string::npos )
		<< "C_StandaloneEntity's constructor no longer unconditionally binds "
		   "under a NETWORKED entity index. That was half the price of "
		   "re-parenting a pooled, client-local drawable onto it -- if there is "
		   "a non-networkable path now, RE-TAKE THE REFUSAL.";

	//     And the bind really is the networkable one: the seam it goes through
	//     has no free-slot allocator arm. Pinned separately, because the
	//     spelling above could survive a change of what it calls.
	EXPECT_EQ( std::string::npos, stdCpp.find( "AddNonNetworkableEntity" ) )
		<< "the client shell has gained a non-networkable bind path, which is "
		   "exactly the thing this refusal was priced on it not having.";

	EXPECT_NE( stdH.find( "C_StandaloneEntity(intnEntNum,intnSerialNum,"
	                      "ClientClass*pClass,intiTier);" ), std::string::npos )
		<< "C_StandaloneEntity has gained or lost a constructor. The refusal "
		   "was priced on there being exactly one, and on it demanding an "
		   "entity number, a serial and a ClientClass -- none of which a temp "
		   "entity has. RE-TAKE THE REFUSAL.";

	// (c) THE SILENT-CORRUPTION SITE the re-parent would have to clear. Both
	//     accessors are NON-virtual, so a subclass cannot answer for its own
	//     storage and would be handed the standalone blob instead.
	EXPECT_NE( stdH.find( "unsignedchar*DataBlob();" ), std::string::npos )
		<< "C_StandaloneEntity::DataBlob has been renamed or removed.";
	EXPECT_EQ( stdH.find( "virtualunsignedchar*DataBlob();" ), std::string::npos )
		<< "C_StandaloneEntity::DataBlob is now VIRTUAL, so a re-parented class "
		   "could answer for its own storage and the silent-substitution hazard "
		   "is gone. RE-TAKE THE REFUSAL.";
}

//-----------------------------------------------------------------------------
// THE BONES. A studio tempent is skinned by the engine with whatever
// IClientRenderable::SetupBones hands back, and CDefaultClientRenderable's
// answer is TRUE WITH NOTHING WRITTEN -- so every gib was drawn with the
// engine's stale bone buffer (screen-sized triangles converging on the view;
// cs_cl_te_live bones counted 11 of 11 model bone matrices unwritten). The shell
// renderable answers it, and writes every slot it was handed.
//-----------------------------------------------------------------------------
TEST( TempEntVerdict, TheShellRenderableWritesEveryBoneSlotTheEngineReads )
{
	const std::string shell = ReadSquashed( kShellRend );
	const std::string cpp   = ReadSquashed( kTempentCpp );

	ASSERT_FALSE( shell.empty() );
	ASSERT_FALSE( cpp.empty() );

	EXPECT_NE( shell.find( "virtualboolSetupBones(matrix3x4_t*pBoneToWorldOut,intnMaxBones,intboneMask,floatcurrentTime)OVERRIDE;" ),
	           std::string::npos )
		<< "CShellRenderable no longer overrides SetupBones, so a studio tempent gets "
		   "CDefaultClientRenderable's true-and-nothing-written and is skinned with "
		   "garbage.";

	const size_t at = cpp.find( "boolCShellRenderable::SetupBones(" );
	ASSERT_NE( at, std::string::npos )
		<< "nothing defines CShellRenderable::SetupBones in the thunk.";

	const size_t end = cpp.find( "staticintBreakModelDrawHelper(", at );
	ASSERT_NE( end, std::string::npos ) << "the slice past SetupBones lost its end marker";
	const std::string body = cpp.substr( at, end - at );

	// THE POSE IS MANAGED (plan physics_bones_native_zero 1.2, client half):
	// the thunk asks IManagedBonesClient::PoseModel, and with no managed side
	// (or a model that is not studio) writes the render transform into every
	// slot itself.
	EXPECT_NE( body.find( "pBones->PoseModel(" ), std::string::npos )
		<< "SetupBones no longer asks the managed pose.";
	EXPECT_NE( body.find( "for(inti=0;i<nMaxBones;i++)" ), std::string::npos )
		<< "SetupBones' fallback no longer walks EVERY slot it was handed, so a "
		   "bone keeps the engine's stale matrix.";
	EXPECT_NE( body.find( "MatrixCopy(matRender,pBoneToWorldOut[i]);" ), std::string::npos )
		<< "an unposed slot is no longer given the render transform.";

	// And the managed half writes every slot too: the posed bones the build
	// wrote, the render transform in the rest (ClientSkeletonsTests.
	// ATempEntsModelIsPosedAtItsPlaceAndUnwrittenSlotsAreTheRenderTransform).
	const std::string managed = ReadSquashed( "sourcesharp/managed/SourceSharp.Client/ClientSkeletons.cs" );
	ASSERT_FALSE( managed.empty() );
	EXPECT_NE( managed.find( "if(i<posed&&BoneLibrary.BoneIsWritten(hdr,i,boneMask))" ), std::string::npos )
		<< "the managed fill no longer asks which bones the pose actually wrote; "
		   "the build leaves unmasked bones untouched.";
	EXPECT_NE( managed.find( "output[i]=render;" ), std::string::npos )
		<< "an unposed slot is no longer given the render transform.";
}

}  // namespace
