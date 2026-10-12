//========= SourceSharp — our own shim TUs do not derive from the entity =====//
//
// Route step 4. Four shim source files per side held classes that structurally
// derived from CBaseEntity / C_BaseEntity. A class declaration emits a VTABLE
// naming every inherited virtual whether or not one line of the file ever calls
// one, so each of those cost its object ~75-180 undefined references FOR
// EXISTING. `src/ref_kinds.py` puts the split at 93% vtable-and-typeinfo across
// the client's shell files. No retype inside a file removes one of them. Only
// not deriving does.
//
// WHAT THE LINK ALREADY AGREES ABOUT, and therefore what this does not
// re-check. `./evict_consumers release client c_baseentity.o` moved
// 48 consumers / 4727 undefined refs -> 46 / 3812, and the server
// 49 / 2181 -> 48 / 2020. shell_effects_sim.o, shell_gamerules.o (both sides)
// left the consumer list entirely and shell_rope_render.o fell 186 -> 5.
// What the link CANNOT tell you is that the behaviour those entities were
// carrying still happens, and that is what every case below is about: each one
// is a thing whose absence produces NO diagnostic anywhere.
//
// THE SILENT LOSSES, one per invariant:
//
//   * THE THINK. SetNextClientThink( CLIENT_THINK_ALWAYS ) put these classes on
//     a list the engine walked. A de-entified class is on no such list, so the
//     call simply stops coming and the rain, the snow and the embers go still
//     with nothing reporting it. Three sweeps replace it and each is asserted
//     to exist and to be called.
//
//   * THE ZEROING ALLOCATOR. C_BaseEntity::operator new memsets the block —
//     "All fields in the object are all initialized to 0" (c_baseentity.cpp) —
//     and all three de-entified classes rest on that promise without saying so:
//     C_Embers' constructor sets one of its eight members and leaves the four
//     SourceSharp_UpdateEmbers fills in, CSnowFallManager's leaves
//     m_bRayParticles, which CreateSnowFall reads every frame. Plain
//     ::operator new zeroes nothing. De-entifying WITHOUT the allocator
//     compiles, links, runs, and turns documented-inert fields into whatever
//     the allocator last left there. Same hazard, and the same fix, as
//     CTrailEffectZeroingTest and C_EnvWind's own note.
//
//   * THE VOLUME TESTS. Three reads genuinely needed an entity and now ask the
//     volume's box directly. If one of them were quietly restored to a trace
//     against a model that no longer exists it would answer "no" forever —
//     ash that never activates, a player never inside a snow volume — and
//     nothing would say so.
//
// ANCHORS, NOT LINE COUNTS. Every file is asserted to still contain a string
// identifying it, BEFORE any absence is asserted about it. A source-scanning
// test whose file has been renamed reads an empty string and reports every
// absence invariant as satisfied — the check that cannot fail. Thirty-five of
// those have been found in this tree; this file is written not to be the
// thirty-sixth.
//
// THE STRIPPER IS LOAD-BEARING rather than tidy: the prose above and in the
// shims themselves spells `: public C_BaseEntity` and
// `CSourceSharpGameRulesProxy` in the paragraphs explaining where they may no
// longer be, and an unstripped scan would read the explanation as the
// violation. Same rule as effects_evicted_test.cpp and
// clientside_prop_evicted_test.cpp.
//
//=============================================================================//

#include <string>
#include <fstream>
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

std::string ReadStripped( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
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
	return StripComments( all );
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

// The anchor is a string the file must still contain for this test to be
// reasoning about the file it thinks it is.
// RE-TAKEN (lane c2-sims): the weather left shell_effects_sim.cpp for C#
// (PrecipitationSim.cs), so the subjects of sections 1-4 are its native half,
// shell_precipitation_client.cpp, and the managed system itself.
const File_t kEffectsSim = { "sourcesharp/game/client/shell_effects_sim.cpp",
	"void GetWindspeedAtTime( float flTime, Vector &vecVelocity )" };
const File_t kPrecipClient = { "sourcesharp/game/client/shell_precipitation_client.cpp",
	"void DrawPrecipitation()" };
const File_t kPrecipManaged = { "sourcesharp/managed/SourceSharp.Client/Effects/PrecipitationSim.cs",
	"public sealed class PrecipitationSystem" };
const File_t kClientModule = { "sourcesharp/managed/SourceSharp.Client/ClientModule.cs",
	"_precipitations.Frame(frameTime);" };
const File_t kRopeRender = { "sourcesharp/game/client/shell_rope_render.cpp",
	"SourceSharp_RopeDraw" };
// The base moved AGAIN, and the verdict below still did not. The chain has been
// CMultiplayRules -> CGameRules -> IGameRules, and each move was made for the
// same reason: the slots the base supplied were either answered here or reached
// by nothing. The last step took the class off CGameRules entirely, so it is
// not a CAutoGameSystemPerFrame either and answers all 50 IGameRules members
// itself.
//
// What THIS test is about -- the PROXY entity being gone while the RULES class
// and its network table stay -- is untouched by any of that. The anchor is
// re-taken for the second time rather than dropped, exactly as ReadAnchored's
// message asks. It now names the interface, which is the end of that chain:
// there is no base left to move to.
const File_t kGameRulesHeader = { "sourcesharp/game/shared/shell_gamerules.h",
	"class CSourceSharpGameRules : public IGameRules" };
// Re-taken with the registry. The anchor names the construction that replaced
// REGISTER_GAMERULES_CLASS; see the verdict below for why that is the same
// claim rather than a weaker one.
const File_t kGameRulesSource = { "sourcesharp/game/shared/shell_gamerules.cpp",
	"new CSourceSharpGameRules();" };

// Reads the file and fails — rather than returning empty — if the anchor has
// gone. Every absence assertion in this file goes through this, so a renamed or
// rewritten file is a red test and never a silently satisfied one.
std::string ReadAnchored( const File_t &file )
{
	const std::string body = ReadStripped( file.pszRel );

	EXPECT_FALSE( body.empty() )
		<< file.pszRel << " could not be read. Every absence invariant this "
		   "file asserts about it would otherwise pass vacuously.";

	if ( !body.empty() )
	{
		EXPECT_NE( body.find( file.pszAnchor ), std::string::npos )
			<< file.pszRel << " no longer contains its anchor \""
			<< file.pszAnchor << "\". The file has been renamed or rewritten, "
			   "so this test is not reasoning about the file it thinks it is — "
			   "RE-TAKE THE VERDICT, do not delete the anchor.";
	}

	return body;
}

} // namespace

//=============================================================================//
// 1-4. THE WEATHER IS NOT AN ENTITY, AND STILL THINKS AND TESTS ITS VOLUMES.
//
// RE-TAKEN, lane c2-sims. These four used to check the de-entified
// CClient_Precipitation and CSnowFallManager in shell_effects_sim.cpp: no
// C_BaseEntity base, a zeroing allocator, a sweep replacing the think list,
// and box tests replacing model traces. Both classes are C# now
// (PrecipitationSim.cs, held to stock by PrecipitationOracleTests), so the
// silent losses those guarded are re-asked of what replaced them: no native
// weather class is left; the think is the client module's frame; the volume
// tests are the managed system's, over the engine's own traces.
//=============================================================================//
TEST( ShimUnderive, EffectClassesDoNotDeriveFromTheEntity )
{
	const std::string sim = NoWhitespace( ReadAnchored( kEffectsSim ) );
	const std::string shell = NoWhitespace( ReadAnchored( kPrecipClient ) );

	for ( const std::string *body : { &sim, &shell } )
	{
		EXPECT_EQ( body->find( "classCClient_Precipitation" ), std::string::npos )
			<< "a native CClient_Precipitation is back; the weather is PrecipitationSystem.";
		EXPECT_EQ( body->find( "classCSnowFallManager" ), std::string::npos )
			<< "a native CSnowFallManager is back.";
	}

	// And the managed classes exist, so the absence is not a deletion.
	const std::string managed = NoWhitespace( ReadAnchored( kPrecipManaged ) );
	EXPECT_NE( managed.find( "publicsealedclassPrecipitationVolume" ), std::string::npos );
	EXPECT_NE( managed.find( "publicsealedclassSnowFallManager" ), std::string::npos );
}

TEST( ShimUnderive, DeEntifiedEffectClassesKeepTheZeroingAllocator )
{
	// No native weather object is allocated any more, so no zeroing allocator
	// is needed: the shell holds only the render plan's buffers.
	const std::string shell = NoWhitespace( ReadAnchored( kPrecipClient ) );
	EXPECT_EQ( shell.find( "newCClient_Precipitation" ), std::string::npos );
	EXPECT_EQ( shell.find( "SOURCESHARP_ZEROING_ALLOCATOR(" ), std::string::npos );
	EXPECT_NE( shell.find( "pApi->PrecipitationRender(" ), std::string::npos )
		<< "DrawPrecipitation no longer asks the managed weather for its plan.";
}

TEST( ShimUnderive, TheClientThinkListIsReplacedBySweeps )
{
	// The think: once a frame from the client module, after the winds.
	const std::string module = NoWhitespace( ReadAnchored( kClientModule ) );
	const size_t winds = module.find( "_winds.Frame();" );
	const size_t weather = module.find( "_precipitations.Frame(frameTime);" );
	ASSERT_NE( winds, std::string::npos );
	ASSERT_NE( weather, std::string::npos );
	EXPECT_LT( winds, weather ) << "the weather thinks before the winds it blows by.";

	// And the system walks every volume and the snowfall manager.
	const std::string managed = NoWhitespace( ReadAnchored( kPrecipManaged ) );
	EXPECT_NE( managed.find( "v.Think(curTime,frameTime);" ), std::string::npos );
	EXPECT_NE( managed.find( "s.Think(frameTime);" ), std::string::npos );
}

TEST( ShimUnderive, VolumeTestsAskTheBoxRatherThanAnEntity )
{
	// The volume tests are stock's again, now that the volume has an entity to
	// trace: the ash's hull trace and the snowfall's point test go through
	// UTIL_TraceModel's body against the owner, and the PVS cull through
	// IsRenderableInPVS. A volume with no entity (r_RainHack's) is a box test.
	const std::string shell = NoWhitespace( ReadAnchored( kPrecipClient ) );
	EXPECT_NE( shell.find( "enginetrace->ClipRayToEntity(ray,MASK_ALL," ), std::string::npos );
	EXPECT_NE( shell.find( "g_pClientLeafSystem->IsRenderableInPVS(" ), std::string::npos );

	const std::string world = NoWhitespace( ReadStripped(
		"sourcesharp/managed/SourceSharp.Client/Effects/ClientPrecipitations.cs" ) );
	ASSERT_FALSE( world.empty() );
	EXPECT_NE( world.find( "if(volume.Owner==EntityHandle.None)returnBoxTrace(" ), std::string::npos )
		<< "an entity-less volume no longer traces its own box.";
}

//=============================================================================//
// 5. THE ROPE ANCHORS ARE GONE, AND THE ROPE STILL HAS TWO ENDS.
//
// This test used to assert the OPPOSITE of its first half's spirit: that the
// anchors were still built, spelled as a plain entity rather than as a
// subclass of one. That was the right assertion while the endpoints had to be
// entities -- C_RopeKeyframe::Create took C_BaseEntity * -- and it is the
// wrong one now that c_rope.h takes a position. The construction is the thing
// gate 1 cannot reach zero through, so the invariant inverts.
//
// AND THE INVERSION IS ONLY SAFE WITH THE SECOND HALF. "This file constructs
// no entity" is satisfied perfectly by a file that constructs nothing and
// draws no ropes, which is the deletion this wave's brief names as the trap.
// So the absence is paired with the two positives that say the cable is still
// strung: the rope is still created, and both of its ends are still given a
// place.
//=============================================================================//
TEST( ShimUnderive, RopeAnchorsAreNotASubclass )
{
	const std::string tight = NoWhitespace( ReadAnchored( kRopeRender ) );

	EXPECT_EQ( tight.find( ":publicC_BaseEntity" ), std::string::npos )
		<< "a class in shell_rope_render.cpp derives from the entity again. "
		   "C_SourceSharpRopeAnchor was an EMPTY subclass and cost this object "
		   "181 of its 186 undefined references for existing.";

	EXPECT_EQ( tight.find( "C_SourceSharpRopeAnchor" ), std::string::npos )
		<< "the empty anchor subclass is back.";

	// THE CONSTRUCTION ITSELF. Any `new` of the client base entity, however it
	// is spelled on the left-hand side, and whether or not it is assigned at
	// all -- the old site read `C_BaseEntity *pAnchor = new C_BaseEntity;` and
	// a needle carrying the variable name would have missed a rename.
	EXPECT_EQ( tight.find( "newC_BaseEntity" ), std::string::npos )
		<< "shell_rope_render.cpp constructs a stock client entity again. This "
		   "is the site plan_entity.md's gate 1 client column cannot reach zero "
		   "through: the port building SDK entities at run time, from its own "
		   "code, three per managed rope. The endpoints are positions now -- "
		   "see C_RopeKeyframe::SetStartPos / SetEndPos.";

	// AND THE ROPE IS STILL THERE. Deleting the ropes satisfies the absence
	// above and every gated count in the port, and takes the cables out of
	// dm_lockdown with nothing failing at build time. The C2 rope port moved
	// the rope to C#, so "still there" is: this file still draws a strip, and
	// the managed rope still gives both ends a place.
	EXPECT_NE( tight.find( "ss::beamstrip::WriteIndexed(meshBuilder," ), std::string::npos )
		<< "shell_rope_render.cpp no longer draws a strip at all. The absence "
		   "asserted above is then satisfied by a file that draws nothing.";

	const std::string managed = NoWhitespace( ReadStripped(
		"sourcesharp/managed/SourceSharp.Client/Ropes/ClientRopeSystem.cs" ) );
	ASSERT_FALSE( managed.empty() ) << "ClientRopeSystem.cs could not be read";

	EXPECT_NE( managed.find( "span.Sim.SetStartPos(origin);" ), std::string::npos )
		<< "the rope's NEAR end is no longer given a place each frame. Without it "
		   "the cable hangs from the origin of the world.";

	EXPECT_NE( managed.find( "span.Sim.SetEndPos(c.End);" ), std::string::npos )
		<< "the rope's FAR end is no longer given a place.";
}

//=============================================================================//
// 6. The gamerules proxy entity is gone from both halves.
//
// It was `: public CGameRulesProxy`, registered as sourcesharp_gamerules, and
// nothing in the tree ever spawned one — CGameRules::CreateStandardEntities
// makes player_manager and nothing else, and the classname is absent from
// ss_sandbox.bsp. It cost 178 client / 76 server refs for existing.
//=============================================================================//
TEST( ShimUnderive, TheGameRulesProxyEntityIsGone )
{
	const std::string header = NoWhitespace( ReadAnchored( kGameRulesHeader ) );
	const std::string source = NoWhitespace( ReadAnchored( kGameRulesSource ) );

	EXPECT_EQ( header.find( "classCSourceSharpGameRulesProxy" ), std::string::npos )
		<< "the gamerules proxy entity is declared again.";
	EXPECT_EQ( header.find( "C_SourceSharpGameRulesProxy" ), std::string::npos )
		<< "the proxy's client alias is back.";

	EXPECT_EQ( source.find( "LINK_ENTITY_TO_CLASS(sourcesharp_gamerules" ), std::string::npos )
		<< "the proxy is registered as a classname again.";
	EXPECT_EQ( source.find( "IMPLEMENT_NETWORKCLASS_ALIASED(SourceSharpGameRulesProxy" ),
	           std::string::npos )
		<< "the proxy's network class is back.";

	// The RULES class and its table are deliberately untouched. Asserting this
	// is what stops the test above being satisfied by deleting the whole file.
	//
	// THE VERDICT IS RE-TAKEN, NOT DROPPED. This used to require
	// REGISTER_GAMERULES_CLASS, and the registry is gone: it was a name->factory
	// map for a build holding several rules classes, and this mod has one. What
	// the assertion was actually protecting -- that the server ends up WITH a
	// rules object -- is unchanged, so it now names the construction that
	// replaced the lookup.
	EXPECT_NE( source.find( "newCSourceSharpGameRules()" ), std::string::npos )
		<< "nothing constructs the rules object — the server would have no game "
		   "rules at all.";
	EXPECT_NE( source.find( "BEGIN_NETWORK_TABLE_NOBASE(CSourceSharpGameRules,DT_SourceSharpGameRules)" ),
	           std::string::npos )
		<< "the rules network table has gone with the proxy; only the proxy was "
		   "meant to go.";
}

//=============================================================================//
// 7. The render shell holds raw pointers to an incomplete type.
//
// They held CHandle< C_BaseEntity >, which read as null of its own accord once
// the engine destroyed the entity behind it. A de-entified effect is in no
// entity list and has no handle, so the guarantee is gone and single ownership
// is what replaces it. A CHandle here would not compile against an incomplete
// type — but a C_BaseEntity * would, and would be a lie.
//=============================================================================//
TEST( ShimUnderive, RenderShellsDoNotHoldTheseAsEntities )
{
	// shell_embers_render.cpp left with C_Embers and shell_precipitation_render.cpp
	// with the weather (lane c2-sims). The weather's native half holds no
	// handle and no object: it draws the plan managed hands it.
	const std::string precip = NoWhitespace( ReadAnchored( kPrecipClient ) );

	EXPECT_EQ( precip.find( "CHandle<" ), std::string::npos )
		<< "shell_precipitation_client.cpp holds the weather as an entity handle again.";
	EXPECT_EQ( precip.find( "CClient_Precipitation" ), std::string::npos )
		<< "shell_precipitation_client.cpp names the native weather class again.";
	// The tracers' corners are TracerGeometry's (C#, lane c5-particles), asked
	// once a render; the shell writes each quad it is handed.
	EXPECT_NE( precip.find( "pParticles->TracerQuads(nCount,s_TracerIn,s_TracerVerts[0].Base(),s_TracerValid)" ), std::string::npos )
		<< "the shell no longer asks for the tracers' corners in one batch.";
	EXPECT_NE( precip.find( "constVector&v=s_TracerVerts[i*4+s_nCorner[k]];mb.Position3f(v.x,v.y,v.z);" ), std::string::npos )
		<< "the shell no longer draws the tracers it is handed.";
}
