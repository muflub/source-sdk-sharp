//========= SourceSharp — CWorld is a shim, and these are its terms ==========//
//
// plan_gamedll_after.md Part B2, the owner's ruling:
//
//     "CWorld needs a native shim class, but all the logic can be managed. It
//      should NOT DECLARE_SERVERCLASS -- that should all come from the managed
//      side. Network vars live in managed code, with C++ calling into managed
//      for get/set."
//
// and, on the same shim:
//
//     "Remove BEGIN_DATADESC( CWorld ) completely. If any of those fields still
//      need parsing, special-case them in the method."
//
// So worldspawn is the pattern-setting case for every remaining stock server
// class, and what this file pins is THE SHAPE rather than the diff. Every fact
// below is a property no compiler and no other test can see: a wire order, a
// name spelt on two sides of a language boundary, an ordering between a
// constructor and a map parse, or a list that must not exist twice.
//
//=============================================================================//
//
// THE HARD QUESTION THIS PORT TURNED ON, AND THE ANSWER, because the answer is
// the reason the shape below is possible at all.
//
// The lane was briefed that "worldspawn is created by the engine during map
// load, not through the managed spawn path that the fifty existing classes
// use". MEASURED, THAT IS NOT WHAT HAPPENS. worldspawn is created by the GAME
// DLL, on the same route every managed class takes:
//
//     mapentities.cpp:612    MapEntity_ParseEntity( pEntity, ... )
//     mapentities.cpp:1199   pEntity = CreateEntityByName( className )
//                            -> EntityFactoryDictionary()->Create("worldspawn")
//                            -> CEntityFactory<CWorld>::Create   (util.h:134)
//                            -> _CreateEntityTemplate            (util.h:66)
//                               `newEnt = new T;`
//
// mapentities.cpp is in server_csharp.vpc, so that whole chain is this DLL's.
// What the ENGINE assumes about worldspawn is its INDEX: RequiredEdictIndex()
// answers 0 and the constructor does NetworkProp()->AttachEdict( INDEXENT(0) ).
//
// So the difference between worldspawn and the fifty is not WHO CREATES IT, it
// is WHICH HIERARCHY it is on. CStandaloneEntity::operator new over-allocates
// past the object so the blob its props address exists; CBaseEntity::operator
// new does not. Attaching managed net state to a shim is therefore an
// ALLOCATION and a BASE OFFSET, and both are things a shim can have --
// TheWorldsBlobIsAllocatedPastTheObjectOnBothSides is that fact.
//
//=============================================================================//

#include <fstream>
#include <string>
#include <vector>
#include <set>
#include <sys/stat.h>
#include "gtest/gtest.h"

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

std::string ReadRaw( const std::string &strRelative )
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
		all += line; all += '\n';
	}
	return all;
}

//-----------------------------------------------------------------------------
// COMMENTS ARE STRIPPED BEFORE EVERY SCAN, and this file needs it more than
// most: the sources it reads carry paragraphs that spell the exact strings
// asserted absent, because that is where the reasons for the absences are
// written. A check that could not tell prose from code would force the
// explanations to be deleted, which is what effects_evicted_test.cpp recorded
// and what Phase A5 copied the rule from.
//-----------------------------------------------------------------------------
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
	return StripComments( ReadRaw( strRelative ) );
}

std::string Tight( const std::string &s )
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

int CountOf( const std::string &hay, const std::string &needle )
{
	if ( needle.empty() )
		return 0;

	int n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + needle.size() ) )
	{
		n++;
	}
	return n;
}

//-----------------------------------------------------------------------------
// The eight files this port is, and an anchor in each.
//
// ANCHORED so that a file rewritten around this test fails to anchor rather
// than reading as "the thing is gone". Phase A10 found six tests sharing one
// anchor that named a file none of them was about, and one whose anchor WAS
// the string it asserted present -- so it could never disagree with its own
// assertion. Each anchor here is something the file is FOR and none of them is
// a string any fact below asserts about.
//-----------------------------------------------------------------------------
const char *kWorldHeader   = "sourcesharp/game/server/ss_world.h";
const char *kWorldAdapter  = "sourcesharp/game/server/world_adapter.cpp";
const char *kServerTable   = "sourcesharp/game/server/shell_world_nettable.cpp";
const char *kClientHeader  = "game/client/c_world.h";
// THE CLIENT'S IMPLEMENTATION MOVED IN STEP 5e and this pointer moved with it.
// game/client/c_world.cpp came out of client_csharp.vpc — the same subtraction
// game/server/world.cpp took when CWorld became world_adapter.cpp — and it is
// still on disk for Phase 7 to delete. Every fact below would have gone on
// passing against that dead text while the compiled file drifted, which is this
// project's own recorded shape for a check that has stopped checking.
const char *kClientWorld   = "sourcesharp/game/client/world_adapter_client.cpp";
const char *kClientTable   = "sourcesharp/game/client/shell_world_nettable_client.cpp";
const char *kSync          = "sourcesharp/game/shared/world_net_sync.cpp";
const char *kSyncHeader    = "sourcesharp/game/shared/world_net_sync.h";
const char *kDeclaration   = "sourcesharp/managed/SourceSharp.Shared/WorldNetState.cs";
const char *kWorldNet      = "sourcesharp/managed/SourceSharp.Shared/WorldNet.cs";
const char *kNetTable      = "sourcesharp/game/server/shell_nettable.cpp";
const char *kEntityApi     = "sourcesharp/game/server/shell_entityapi.cpp";

struct Anchor_t { const char *pszFile; const char *pszAnchor; };

const Anchor_t kAnchors[] = {
	// THE ANCHOR MOVED WITH THE CLASS (plan_cworld_trace.md T3). It read
	// "classCWorld:publicCBaseEntity"; the world is its own hierarchy now --
	// IServerEntity, IServerNetworkable and ICollideable, no CBaseEntity -- so
	// that string identifies nothing and an anchor that identifies nothing is
	// how every assertion below turns vacuous at once. Re-pointed at the class
	// head as it now reads, which is what an anchor is for.
	{ "sourcesharp/game/server/ss_world.h",                                        "classCWorld:publicIServerEntity" },
	// LINK_ENTITY_TO_CLASS went with the base: its CEntityFactory<CWorld>
	// calls PostConstructor() and NetworkProp(), both CBaseEntity's.
	// CWorldFactory is the four-line replacement and installs under the same
	// classname.
	{ "sourcesharp/game/server/world_adapter.cpp",                  "CWorldFactory" },
	{ "sourcesharp/game/server/shell_world_nettable.cpp",           "SourceSharp_WorldNetDeclare" },
	// AND THE CLIENT'S ANCHOR MOVED FOR THE SAME REASON THE SERVER'S DID, one
	// step later: 5e took C_World off C_BaseEntity too (plan_entity.md §3.11 as
	// amended), so "classC_World:publicC_BaseEntity" identifies nothing and an
	// anchor that identifies nothing is how every assertion below turns vacuous
	// at once.
	{ "game/client/c_world.h",                                      "classC_World:publicIClientEntity" },
	{ "sourcesharp/game/client/world_adapter_client.cpp",           "SourceSharp_ClientWorldFactory" },
	{ "sourcesharp/game/client/shell_world_nettable_client.cpp",    "SourceSharp_WorldNetDeclare" },
	{ "sourcesharp/game/shared/world_net_sync.cpp",                 "SourceSharpWorldFields_Resolve" },
	{ "sourcesharp/managed/SourceSharp.Shared/WorldNetState.cs",    "classWorldNetState" },
	{ "sourcesharp/managed/SourceSharp.Shared/WorldNet.cs",         "ApplyKey" },
};

//-----------------------------------------------------------------------------
// The ten fields, once, in this file.
//
// The whole point of the port is that this list exists in ONE place -- the C#
// declaration -- and both halves are built from it. That makes a list here a
// contradiction in principle and a necessity in practice: something has to
// check the two sides name the same ten, and a checker that derived its list
// from one of the two sides could only ever agree with that side.
//
// So this is a THIRD copy, and it is a third copy on purpose: it agrees with
// both or it fails, and it cannot be made to agree with a drifted pair by
// editing either of them.
//-----------------------------------------------------------------------------
const char *kFields[] = {
	"WaveHeight",
	"WorldMins",
	"WorldMaxs",
	"StartDark",
	"MaxOccludeeArea",
	"MinOccluderArea",
	"MaxPropScreenSpaceWidth",
	"MinPropScreenSpaceWidth",
	"DetailSpriteMaterial",
	"ColdWorld",
};
const int kFieldCount = (int)( sizeof( kFields ) / sizeof( kFields[ 0 ] ) );

//-----------------------------------------------------------------------------
// The nine map keys, and the two dispositions the port gave them.
//
// Measured off dm_lockdown's own entity lump on the way in: its worldspawn sets
// world_mins, world_maxs, maxpropscreenwidth, startdark and gametitle, so five
// of these ten rows are exercised by the gate map and the port's live proof
// reads exactly those five back on both sides.
//-----------------------------------------------------------------------------
const char *kManagedKeys[] = {
	"world_mins", "world_maxs", "startdark", "maxoccludeearea", "minoccluderarea",
	"maxpropscreenwidth", "minpropscreenwidth", "detailmaterial", "coldworld",
};
const int kManagedKeyCount = (int)( sizeof( kManagedKeys ) / sizeof( kManagedKeys[ 0 ] ) );

} // namespace


//=============================================================================//
// 0. THE FILES ARE READ, before anything is asserted about them.
//=============================================================================//

TEST( WorldShim, EveryFileIsReadAndCarriesItsAnchor )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot find the source tree from the cwd";

	for ( size_t i = 0; i < sizeof( kAnchors ) / sizeof( kAnchors[ 0 ] ); i++ )
	{
		const std::string raw = ReadRaw( kAnchors[ i ].pszFile );

		ASSERT_FALSE( raw.empty() ) << kAnchors[ i ].pszFile << " could not be read; "
			"every absence fact below would pass on an empty string";

		EXPECT_NE( Tight( StripComments( raw ) ).find( kAnchors[ i ].pszAnchor ),
		           std::string::npos )
			<< kAnchors[ i ].pszFile << " no longer contains its anchor \""
			<< kAnchors[ i ].pszAnchor << "\" -- this file is reading something it "
			   "does not recognise";
	}
}

//=============================================================================//
// 1. THE RULING, AS TWO ABSENCES AND TWO PRESENCES.
//=============================================================================//

TEST( WorldShim, TheServerShimDeclaresNoServerClassAndNoDatadesc )
{
	const std::string header  = Tight( ReadStripped( kWorldHeader ) );
	const std::string adapter = Tight( ReadStripped( kWorldAdapter ) );
	ASSERT_FALSE( header.empty() );
	ASSERT_FALSE( adapter.empty() );

	EXPECT_EQ( 0, CountOf( header, "DECLARE_SERVERCLASS" ) )
		<< "world.h declares a server class again. The ServerClass is supposed to "
		   "come from SourceSharp.Shared.WorldNetState -- see Part B2";
	EXPECT_EQ( 0, CountOf( adapter, "IMPLEMENT_SERVERCLASS" ) )
		<< "world_adapter.cpp registers CWorld with the SDK macro again";

	// THE SECOND RULING. A datamap is a class's own declarative table and the
	// shim is meant to have none.
	//
	// IT USED TO INHERIT CBaseEntity's, "which is what still answers every key
	// that was never the world's". There is no base to inherit one from now
	// (plan_cworld_trace.md T3), so CWorld::KeyValue answers false for a key it
	// does not itself handle -- which is what the datamap walk did for a key
	// that matched nothing, and what the engine does with the answer either way.
	// The ruling is satisfied more completely than before, not less.
	EXPECT_EQ( 0, CountOf( header, "DECLARE_DATADESC" ) )
		<< "world.h declares a datadesc again";
	EXPECT_EQ( 0, CountOf( adapter, "BEGIN_DATADESC" ) )
		<< "world_adapter.cpp defines a datadesc again. \"Remove "
		   "BEGIN_DATADESC( CWorld ) completely. If any of those fields still need "
		   "parsing, special-case them in the method\"";

	// AND THE PRESENCE, because two absences alone are also what a deleted class
	// looks like. The shim keeps its identity and the engine-facing vtable.
	//
	// LINK_ENTITY_TO_CLASS WENT WITH THE BASE (T3): the macro's
	// CEntityFactory<CWorld>::Create calls PostConstructor() and NetworkProp(),
	// both CBaseEntity's, to reach an IServerNetworkable that THIS CLASS
	// ALREADY IS. CWorldFactory is the four-line replacement and installs under
	// the same classname, which is the fact this line is about.
	EXPECT_EQ( 1, CountOf( adapter, "EntityFactoryDictionary()->InstallFactory(this,\"worldspawn\");" ) )
		<< "worldspawn is no longer a spawnable classname answered by CWorld";
	EXPECT_EQ( 1, CountOf( header, "virtualServerClass*GetServerClass();" ) )
		<< "CWorld no longer overrides GetServerClass. There is no base to "
		   "inherit one from, so the engine would get NULL and dereference it on "
		   "the first entity it packs";
	EXPECT_EQ( 1, CountOf( header, "virtualintRequiredEdictIndex(void){return0;}" ) )
		<< "the world no longer claims edict 0";
}

TEST( WorldShim, TheClientShimDeclaresNoClientClassAndNoRecvTable )
{
	const std::string header = Tight( ReadStripped( kClientHeader ) );
	const std::string world  = Tight( ReadStripped( kClientWorld ) );
	ASSERT_FALSE( header.empty() );
	ASSERT_FALSE( world.empty() );

	// THE CLIENT HALF IS NOT OPTIONAL, and this is the fact that says so. The
	// two sides of a table move together: a server that stops sending typed
	// props to a client that still expects them is a client that begins joining
	// and stops, with nothing in any log.
	EXPECT_EQ( 0, CountOf( header, "DECLARE_CLIENTCLASS" ) )
		<< "c_world.h declares a client class again";
	EXPECT_EQ( 0, CountOf( world, "BEGIN_RECV_TABLE" ) )
		<< "world_adapter_client.cpp defines a receive table again";
	EXPECT_EQ( 0, CountOf( world, "IMPLEMENT_CLIENTCLASS" ) )
		<< "world_adapter_client.cpp registers a client class with the SDK macro again";

	EXPECT_EQ( 1, CountOf( header, "virtualClientClass*GetClientClass()OVERRIDE;" ) )
		<< "C_World no longer overrides GetClientClass, so nothing hands the "
		   "engine the managed table for the world";
}

//-----------------------------------------------------------------------------
// THE FOUR VALUES C_BaseEntity USED TO FORCE FOR ENTITY ZERO, ANSWERED BY THE
// CLASS ITSELF.
//
// C_BaseEntity::PostDataUpdate had an `if ( index == 0 )` block -- stock's own
// comment is "If it's the world, force solid flags" -- that overwrote model
// index, solidity, origin and angles for entity 0 on every update, whatever the
// wire had just said. Step 5e took the world off that base, so nothing runs
// that block for it any more, and each of the four had to be answered
// directly.
//
// THE DISAPPEARANCE WOULD HAVE BEEN SILENT, WHICH IS WHY THIS IS A TEST. Two of
// the four are asserted by the ENGINE on every world trace in a debug build --
// the world entity must be unrotated, and it reads GetCollisionOrigin() and
// GetCollisionAngles() to check -- and the other two decide
// which brush model the world's collideable names. None of them fails as a
// crash; they fail as a wrong answer.
//-----------------------------------------------------------------------------
TEST( WorldShim, TheClientWorldAnswersTheFourValuesTheBaseUsedToForce )
{
	const std::string header = Tight( ReadStripped( kClientHeader ) );
	const std::string world  = Tight( ReadStripped( kClientWorld ) );
	ASSERT_FALSE( header.empty() );
	ASSERT_FALSE( world.empty() );

	// 1. BRUSH MODEL 1. Out of line, so it is read from the .cpp.
	EXPECT_NE( world.find( "intCWorldClientCollideable::GetCollisionModelIndex(){return1;}" ),
	           std::string::npos )
		<< "the client world's collideable no longer names brush model 1. Its "
		   "own GetCollisionModel() asks modelinfo for whatever this answers.";

	// 2. SOLID_BSP, set once in the constructor and never again -- this object's
	//    shape IS the level.
	EXPECT_NE( world.find( "m_nSolidType=SOLID_BSP;" ), std::string::npos )
		<< "the client world's collideable is not SOLID_BSP any more.";
	EXPECT_NE( header.find( "virtualSolidType_tGetSolid()constOVERRIDE{returnm_nSolidType;}" ),
	           std::string::npos )
		<< "GetSolid stopped answering the member the constructor set.";

	// 3 and 4. THE ORIGIN AND THE ANGLES, and the engine asserts on both.
	EXPECT_NE( world.find( "m_vecOrigin=vec3_origin;" ), std::string::npos )
		<< "the client world's collideable origin is not the level origin. The "
		   "engine asserts it is on every world trace in a debug build.";
	EXPECT_NE( world.find( "m_angRotation=vec3_angle;" ), std::string::npos )
		<< "the client world's collideable is rotated. Same assert.";

	// And the same two as the ENTITY answers them, which is what the render
	// path and IClientEntity ask for.
	EXPECT_NE( world.find( "constVector&C_World::GetAbsOrigin(void)const{returns_vecWorldOrigin;}" ),
	           std::string::npos )
		<< "C_World::GetAbsOrigin no longer answers the level origin.";
	EXPECT_NE( world.find( "constQAngle&C_World::GetAbsAngles(void)const{returns_angWorldAngles;}" ),
	           std::string::npos )
		<< "C_World::GetAbsAngles no longer answers the level frame.";
}

//=============================================================================//
// 2. THE TABLES, AND THE ORDER OF THE PROPS IN THEM.
//=============================================================================//

TEST( WorldShim, BothTablesAreBuiltFromTheManagedDeclaration )
{
	const std::string server = Tight( ReadStripped( kServerTable ) );
	const std::string client = Tight( ReadStripped( kClientTable ) );
	ASSERT_FALSE( server.empty() );
	ASSERT_FALSE( client.empty() );

	EXPECT_EQ( 1, CountOf( server, "newServerClass(\"CWorld\"" ) );
	EXPECT_EQ( 1, CountOf( client, "newClientClass(\"CWorld\"" ) );

	// THE TABLE NAMES ARE STOCK'S, INCLUDING THE PAIR THAT DISAGREES WITH
	// ITSELF. The engine pairs the two sides by SERVER CLASS NAME, and stock
	// spelt the server's table DT_WORLD and the client's DT_World. Both
	// spellings are carried across unchanged, because this port is not the place
	// to find out which of the engine's comparisons is case-sensitive.
	EXPECT_EQ( 1, CountOf( server, "\"DT_WORLD\"" ) );
	EXPECT_EQ( 1, CountOf( client, "\"DT_World\"" ) );

	// The size comes from C# and from nowhere else. A literal here would be the
	// second copy of a layout the whole design exists to keep single.
	EXPECT_EQ( 1, CountOf( server, "int32_tSourceSharp_WorldNetDeclare(int32_tnBlobBytes)" ) );
	EXPECT_EQ( 1, CountOf( client, "int32_tSourceSharp_WorldNetDeclare(int32_tnBlobBytes)" ) );
}

TEST( WorldShim, NeitherSideHasABaseTableAndBothStartThePayloadAtPropZero )
{
	// THE FACT INVERTED, AND IT IS A RE-TAKE RATHER THAN A DELETION.
	//
	// This test used to be TheBaseTableIsPropZeroOnBothSides, and it was right:
	// stock's BEGIN_SEND_TABLE emits SendPropDataTable("baseclass", ...) ahead
	// of the props the author wrote, DT_WORLD inherited DT_BaseEntity, and
	// dropping that would have cost the client the world's model index with
	// nothing saying so.
	//
	// WHAT CHANGED IS THE OBJECT, NOT THE ARGUMENT (plan_cworld_trace.md T3).
	// The base table was a POINTER to CBaseEntity's own send table, whose props
	// carry CBaseEntity's member offsets; the engine encodes against
	// edict->GetUnknown(), which is now a CWorld that is not a CBaseEntity and
	// is much smaller. Most of those props read PAST THE END of the object and
	// put the result on the wire. Keeping the base table stopped being "the
	// client gets the world's model index" and became "the client gets whatever
	// is at those addresses".
	//
	// AND THE CLIENT LOSES NOTHING MEASURABLE, which is the half that made the
	// deletion possible: C_BaseEntity::PostDataUpdate forces model index 1,
	// SOLID_BSP, the origin and the angles for entity 0 -- stock's own code,
	// under stock's own comment "If it's the world, force solid flags" -- so
	// the four fields the old argument named are overwritten a moment after
	// they arrive. Verified live: the world's ten declared fields cross intact
	// (server and client both print mins (-4608 1328 -144) maxs (-2412 6976
	// 652) on dm_lockdown).
	//
	// SO THE FACT IS NOW THE ABSENCE, ON BOTH SIDES, plus the payload starting
	// at prop 0. Two ways to get it wrong and neither is a compile error: put
	// a base table back on one side (the two tables stop pairing), or put one
	// back on BOTH (they pair, and the server sends garbage).
	const std::string server = Tight( ReadStripped( kServerTable ) );
	const std::string client = Tight( ReadStripped( kClientTable ) );
	ASSERT_FALSE( server.empty() );
	ASSERT_FALSE( client.empty() );

	EXPECT_EQ( server.find( "SendPropDataTable(\"baseclass\"" ), std::string::npos )
		<< "the server's world table inherits DT_BaseEntity again. Its props "
		   "carry CBaseEntity's member offsets and the engine encodes them "
		   "against a CWorld, which is not one.";
	EXPECT_EQ( client.find( "RecvPropDataTable(\"baseclass\"" ), std::string::npos )
		<< "the client's world table inherits DT_BaseEntity again. The two "
		   "halves move together or the update does not pair at all.";

	// The payload now starts at prop 0 on both sides. Asserted so the two
	// absences above are a measurement rather than a pair of files that have
	// stopped building a table at all.
	EXPECT_NE( server.find( "SourceSharpPayload_BuildSendProps(m_pProps,nWords,nBase)" ),
	           std::string::npos )
		<< "the server's world table no longer builds its payload at prop 0";
	EXPECT_NE( client.find( "SourceSharpPayload_BuildRecvProps(m_pProps,nWords,nBase)" ),
	           std::string::npos )
		<< "the client's world table no longer builds its payload at prop 0";
}

TEST( WorldShim, EachSideResolvesItsPropsAgainstItsOwnBlobBase )
{
	// A CWorld AND A C_World ARE UNRELATED OBJECTS, so their blobs start at
	// different offsets -- measured live at 1280 and 1968. What the two sides
	// agree on is the payload WORD INDEX and never the offset, which is the same
	// split the parallel hierarchy has always had.
	//
	// The failure this refuses is one number serving both: the props would then
	// address the other class's members, and the client would decode a vtable
	// pointer as a world bound.
	const std::string adapter = Tight( ReadStripped( kWorldAdapter ) );
	const std::string client  = Tight( ReadStripped( kClientWorld ) );
	ASSERT_FALSE( adapter.empty() );
	ASSERT_FALSE( client.empty() );

	EXPECT_EQ( 1, CountOf( adapter, "ALIGN_VALUE(sizeof(CWorld),16)" ) )
		<< "the server's blob base is no longer computed from sizeof( CWorld )";
	EXPECT_EQ( 1, CountOf( client, "ALIGN_VALUE(sizeof(C_World),16)" ) )
		<< "the client's blob base is no longer computed from sizeof( C_World )";

	// AND NEITHER SIDE NAMES THE OTHER'S CLASS. One number serving both is the
	// failure this refuses, and it is the one that reads as data: the props
	// would address the other class's members and the client would decode a
	// vtable pointer as a world bound.
	EXPECT_EQ( 0, CountOf( adapter, "sizeof(C_World)" ) );
	EXPECT_EQ( 0, CountOf( client, "sizeof(CWorld)" ) )
		<< "the client sizes its blob base with the SERVER's class";
}

TEST( WorldShim, TheWorldsBlobIsAllocatedPastTheObjectOnBothSides )
{
	// THE ANSWER TO THE HARD QUESTION, AS A FACT. See the block at the top of
	// this file: attaching managed net state to a shim on the SDK's hierarchy is
	// an allocation and a base offset, because CStandaloneEntity over-allocates
	// and CBaseEntity does not.
	const std::string adapter = Tight( ReadStripped( kWorldAdapter ) );
	const std::string client  = Tight( ReadStripped( kClientWorld ) );
	ASSERT_FALSE( adapter.empty() );
	ASSERT_FALSE( client.empty() );

	EXPECT_EQ( 1, CountOf( adapter,
		"engine->PvAllocEntPrivateData(SourceSharp_WorldBlobOffset()+nBlobBytes)" ) )
		<< "CWorld no longer over-allocates for its blob. Without the extra "
		   "bytes every payload prop addresses memory past the end of the "
		   "allocation -- which does not crash, it reads the heap";
	EXPECT_EQ( 1, CountOf( client,
		"returnEntityMem_Alloc(SourceSharp_WorldBlobOffset()+nBlobBytes)" ) )
		<< "C_World no longer over-allocates for its blob";

	// BOTH FORMS OF operator new, on both sides. tier0/memdbgon.h rewrites `new`
	// to the four-argument one and _CreateEntityTemplate is compiled with
	// memdbgon on, so the debug form is the one the factory actually calls.
	EXPECT_EQ( 1, CountOf( adapter, "void*CWorld::operatornew(size_tstAllocateBlock)" ) );
	// The server's debug form went in the server gc cull (2026-10-01): with
	// NO_MALLOC_OVERRIDE, memdbgon.h no longer rewrites `new`, so nothing called it.
	EXPECT_EQ( 0, CountOf( adapter, "void*CWorld::operatornew(size_tstAllocateBlock,intnBlockUse," ) );
	EXPECT_EQ( 1, CountOf( client, "void*C_World::operatornew(size_tstAllocateBlock)" ) );
	EXPECT_EQ( 1, CountOf( client, "void*C_World::operatornew(size_tstAllocateBlock,intnBlockUse," ) );
}

TEST( WorldShim, TheServerWorldGoesBackToTheEngineTheWayItCame )
{
	// operator new takes the block from engine->PvAllocEntPrivateData, and
	// CWorld derives from IServerEntity alone: no base supplies a delete. Without
	// its own, `delete m_pOuter` (CWorldNetworkable::Release) handed the engine's
	// block to the global delete. memoverride routed both to tier0 and hid it;
	// the allocation checker counted it foreign. The client pairs C_World's the
	// same way (world_adapter_client.cpp).
	const std::string header  = Tight( ReadStripped( kWorldHeader ) );
	const std::string adapter = Tight( ReadStripped( kWorldAdapter ) );
	ASSERT_FALSE( header.empty() );
	ASSERT_FALSE( adapter.empty() );

	EXPECT_EQ( 1, CountOf( header, "staticvoidoperatordelete(void*pMem);" ) )
		<< "world.h no longer declares CWorld's operator delete";
	// The debug pair went in the server gc cull (2026-10-01; see above).
	EXPECT_EQ( 0, CountOf( header, "staticvoidoperatordelete(void*pMem,intnBlockUse," ) );
	EXPECT_EQ( 1, CountOf( adapter,
		"voidCWorld::operatordelete(void*pMem){engine->FreeEntPrivateData(pMem);}" ) )
		<< "CWorld's block no longer goes back through engine->FreeEntPrivateData";
	EXPECT_EQ( 0, CountOf( adapter,
		"constchar*pFileName,intnLine){engine->FreeEntPrivateData(pMem);}" ) );
}

//=============================================================================//
// 3. THE LAYOUT IS ASKED FOR, NEVER COPIED.
//=============================================================================//

TEST( WorldShim, TheTenFieldNamesAreTheSameTenOnBothSidesOfTheLanguageBoundary )
{
	// THE LOAD-BEARING PARITY FACT. The C++ side resolves each field BY NAME
	// through NetStateFieldOffset, and a name that does not match the C#
	// property resolves to -1 -- which reads as a zero, not as an error. Every
	// one of the ten is read by the client's renderer, so a single misspelling
	// is a rendering difference with nothing in any log.
	//
	// Checked against a THIRD list (kFields above) rather than against either
	// side, so it cannot be satisfied by a drifted pair.
	const std::string sync = ReadStripped( kSync );
	const std::string decl = ReadStripped( kDeclaration );
	ASSERT_FALSE( sync.empty() );
	ASSERT_FALSE( decl.empty() );

	for ( int i = 0; i < kFieldCount; i++ )
	{
		const std::string quoted = std::string( "\"" ) + kFields[ i ] + "\"";
		EXPECT_EQ( 1, CountOf( sync, quoted ) )
			<< "world_net_sync.cpp does not ask for exactly one field called "
			<< kFields[ i ];

		// `partial <type> Name { get; set; }` -- the declaration's own spelling.
		const std::string declared = std::string( "partial" );
		EXPECT_NE( decl.find( std::string( kFields[ i ] ) + " { get; set; }" ),
		           std::string::npos )
			<< "WorldNetState declares no property called " << kFields[ i ]
			<< ". The C++ side asks for it by that name and would get -1, which "
			   "reads as a zero rather than as an error";
	}

	// AND NO ELEVENTH, on either side. A field added to the declaration and not
	// to the resolve is a value the server stores and native never reads; one
	// added to the resolve and not to the declaration resolves to -1 forever.
	EXPECT_EQ( kFieldCount, CountOf( sync, "ResolveOffset( ask, " )
	                      + CountOf( sync, "ResolvePacked( ask, " ) )
		<< "world_net_sync.cpp resolves a different number of fields than the "
		<< kFieldCount << " this file knows about";
	EXPECT_EQ( kFieldCount, CountOf( decl, " { get; set; }" ) )
		<< "WorldNetState declares a different number of properties than the "
		<< kFieldCount << " this file knows about";
}

TEST( WorldShim, NoOffsetIsWrittenDownOnTheNativeSide )
{
	// STEP 7'S RULE, APPLIED TO THE WORLD: the layout exists once, in the
	// language that computes it. Native asks NetStateFieldOffset /
	// NetStateFieldBits and keeps the ANSWER; a literal offset here would be a
	// second copy of an arithmetic that drifts silently -- a wrong offset reads
	// a plausible number out of a neighbouring field.
	const std::string sync = Tight( ReadStripped( kSync ) );
	ASSERT_FALSE( sync.empty() );

	// TWO HELPERS AND NO THIRD ROUTE. Every one of the ten goes through
	// ResolveOffset or ResolvePacked -- counted by name in
	// TheTenFieldNamesAreTheSameTenOnBothSidesOfTheLanguageBoundary -- and each
	// helper makes exactly one ask.
	//
	// Counted as SITES rather than as ten calls on purpose: the first draft of
	// this fact asserted ten and read two, because the ten go through two
	// helpers. A count that is right about the wrong population is a count that
	// says nothing, and this tree has recorded that shape often enough.
	EXPECT_EQ( 2, CountOf( sync, "ask.pfnFieldOffset(SOURCESHARP_WORLD_NETCLASS," ) )
		<< "the offsets are not asked for through the two helpers";
	EXPECT_EQ( 1, CountOf( sync, "ask.pfnFieldBits(SOURCESHARP_WORLD_NETCLASS," ) )
		<< "the PACKED fields' placements are not asked for. StartDark and "
		   "ColdWorld share one word at shifts 0 and 1, so a caller that read a "
		   "byte at StartDark's offset would get ColdWorld's bit with it";
	EXPECT_EQ( 1, CountOf( sync, "SourceSharpPayload_Place(nOffset,nBits)" ) )
		<< "the packed placement is no longer built from the two answers C# gave";

	// AND NOTHING IS WRITTEN DOWN. A literal would be the second copy of a
	// layout that drifts silently -- a wrong offset reads a plausible number out
	// of a neighbouring field and nothing anywhere says so.
	//
	// The needle is the two spellings a hard-coded placement takes: a field set
	// assigned a number, and the struct's members touched anywhere but here.
	EXPECT_EQ( 0, CountOf( sync, "pFields->nWorldMins=0" ) );
	EXPECT_EQ( 0, CountOf( sync, "pFields->nWorldMins=24" ) );
	EXPECT_EQ( 0, CountOf( sync, "pFields->nDetailSpriteMaterial=48" ) )
		<< "world_net_sync.cpp writes an offset the generator computed. That is "
		   "the copy Step 7 deleted, re-created one field at a time";
}

TEST( WorldShim, TheClassnameIsSpeltOnceOnEachSideAndTheTwoAgree )
{
	// It is the map's classname, the factory's name and the [StandaloneNetState]
	// key at the same time, and a misspelling is not a compile error -- it is
	// ten fields that silently do not resolve.
	const std::string syncH = Tight( ReadStripped( kSyncHeader ) );
	const std::string decl  = ReadStripped( kDeclaration );
	ASSERT_FALSE( syncH.empty() );
	ASSERT_FALSE( decl.empty() );

	EXPECT_EQ( 1, CountOf( syncH, "#defineSOURCESHARP_WORLD_NETCLASS\"worldspawn\"" ) )
		<< "the native spelling of the net class name has moved or changed";
	EXPECT_NE( decl.find( "[StandaloneNetState(EntityNames.Worldspawn, OnHierarchy = false)]" ),
	           std::string::npos )
		<< "WorldNetState is no longer declared for EntityNames.Worldspawn with "
		   "OnHierarchy = false. On the hierarchy its 304 bytes would be added to "
		   "EVERY entity on the parallel hierarchy, none of which is the world";

	const std::string names = ReadStripped( "sourcesharp/managed/SourceSharp.Shared/EntityNames.cs" );
	ASSERT_FALSE( names.empty() );
	EXPECT_NE( names.find( "Worldspawn = \"worldspawn\"" ), std::string::npos )
		<< "EntityNames.Worldspawn is not the string world_net_sync.h asks with";
}

//=============================================================================//
// 4. THE KEYS: WHAT CROSSES, WHAT IS SPECIAL-CASED, AND WHAT IS LOST.
//=============================================================================//

TEST( WorldShim, TheShimOffersEveryKeyRatherThanListingTheNetworkedOnes )
{
	// THE SHIM ASKS AND DOES NOT TELL. A list of the nine networked key names on
	// the C++ side would be a second copy of a declaration that lives in C#, and
	// it would drift the way every second copy in this tree has drifted.
	//
	// So CWorld::KeyValue offers every key it does not itself answer, and C#
	// says whether it took it. That is what these absences are.
	const std::string adapter = Tight( ReadStripped( kWorldAdapter ) );
	ASSERT_FALSE( adapter.empty() );

	for ( int i = 0; i < kManagedKeyCount; i++ )
	{
		const std::string quoted = std::string( "\"" ) + kManagedKeys[ i ] + "\"";
		EXPECT_EQ( 0, CountOf( adapter, quoted ) )
			<< "world_adapter.cpp names the key '" << kManagedKeys[ i ] << "'. "
			   "That list belongs to WorldNet.cs, beside the declaration that "
			   "holds the value; a copy here is the drift the design deletes";
	}

	// THE HANDLE IS TAKEN FROM THE OTHER SIDE OF THE RESOLVER NOW (T3).
	// SourceSharp_HandleFromEntity takes a CBaseEntity * and the world is not
	// one; SourceSharp_HandleFromAnyEntity takes the IHandleEntity every
	// hierarchy has, which is the same number for the same object. What the
	// fact is about -- that the shim OFFERS rather than lists -- is unchanged.
	// Offered through the managed-built runtime since plan_engineapi_vtables E1.
	EXPECT_EQ( 1, CountOf( adapter,
		"pRuntime->OnWorldKeyValue(SourceSharp_HandleFromAnyEntity(static_cast<IServerEntity*>(this)),szKeyName,szValue)" ) )
		<< "CWorld::KeyValue no longer offers its keys to the managed side";

	// AND MANAGED HOLDS ALL NINE. The absence above is only correct while
	// something else has them.
	const std::string worldNet = ReadStripped( kWorldNet );
	ASSERT_FALSE( worldNet.empty() );
	for ( int i = 0; i < kManagedKeyCount; i++ )
	{
		const std::string quoted = std::string( "\"" ) + kManagedKeys[ i ] + "\"";
		EXPECT_EQ( 1, CountOf( worldNet, quoted ) )
			<< "WorldNet.cs no longer answers to the key '" << kManagedKeys[ i ]
			<< "', and nothing else does either -- a map that sets it now leaves "
			   "the value at its zero and the client renders against that";
	}
}

//=============================================================================//
// 5. THE ORDERING THAT COST A LIVE RUN TO FIND.
//=============================================================================//

TEST( WorldShim, TheWorldSingletonIsClaimedBeforeTheMapsKeysArrive )
{
	// THE REGRESSION TEST FOR THE ONE REAL BUG THIS PORT HAD, and it is an
	// ordering nothing else in the build can see.
	//
	// SourceSharp_EntityGetDataBlob finds the world's blob by IDENTITY against
	// GetWorldEntity(). Stock assigns that pointer in CWorld::Precache, which is
	// inside Spawn -- and Spawn runs AFTER the map's keys:
	//
	//   CreateEntityByName( "worldspawn" )      the object exists
	//   ParseMapData -> CWorld::KeyValue x N    the keys arrive HERE
	//   DispatchSpawn -> Spawn -> Precache      g_WorldEntity = this
	//
	// so for the whole of the key parse GetWorldEntity() answered NULL, every
	// key was refused, the world networked its zero-initialised blob, and the
	// client decoded ten zeroes. No crash, no assert, no missing DT class --
	// both sides agreeing about nothing.
	//
	// Asserted as POSITION rather than presence: the assignment being somewhere
	// in the file is what was true before, and it is the wrong somewhere.
	const std::string adapter = Tight( ReadStripped( kWorldAdapter ) );
	ASSERT_FALSE( adapter.empty() );

	EXPECT_EQ( 1, CountOf( adapter, "g_WorldEntity=this;" ) )
		<< "the world singleton is claimed " << CountOf( adapter, "g_WorldEntity=this;" )
		<< " times rather than once. Two writes of one fact is how the two come "
		   "to disagree";

	const size_t at   = adapter.find( "g_WorldEntity=this;" );
	const size_t ctor = adapter.find( "CWorld::CWorld()" );
	const size_t prec = adapter.find( "voidCWorld::Precache(void)" );

	ASSERT_NE( ctor, std::string::npos ) << "CWorld's constructor is not where this "
		"test can see it";
	ASSERT_NE( prec, std::string::npos ) << "CWorld::Precache is not where this test "
		"can see it";
	ASSERT_NE( at, std::string::npos );

	EXPECT_GT( at, ctor ) << "the world singleton is claimed before the constructor";
	EXPECT_LT( at, prec ) << "the world singleton is claimed in or after "
		"CWorld::Precache. Precache runs from Spawn, and Spawn runs AFTER the map "
		"parse has handed CWorld::KeyValue every one of worldspawn's keys -- so "
		"the blob those keys are stored in cannot be found while they are "
		"arriving, and the world networks ten zeroes with nothing in any log.";
}

TEST( WorldShim, TheWorldCarriesABlobAndIsFoundOnItsOwnArm )
{
	// AND IT IS FOUND WITHOUT RTTI. The runtime-networked entity's branch left
	// EntityGetDataBlob with a dynamic_cast on CBaseEntity -- the dependency the
	// parallel hierarchy exists to cut -- and this branch does not bring it
	// back: the world is a singleton, so the test is pointer identity.
	//
	// THE ARM IT IS FOUND ON MOVED, AND THE FAILURE THAT FORCED IT IS THE ONE
	// THIS FACT EXISTS TO CATCH (plan_cworld_trace.md T3). The test read
	// `ent.pBase == (CBaseEntity *)GetWorldEntity()` -- identity, which was
	// right, made through an arm that stopped carrying the world when CWorld
	// left the CBaseEntity hierarchy. It could never fire again, and it did not:
	// a live run reported every worldspawn key refused with "entity 0
	// ('worldspawn') offers 0" and the world networking 76 zero words, which is
	// word for word the failure world_adapter.cpp records the singleton
	// assignment being moved to fix. Same symptom, different cause, and no
	// offline gate saw either.
	//
	// So the fact is now stated over the arm that carries it:
	// SourceSharpEntity_t::IsWorld, the same pointer identity against the same
	// singleton, reached through the resolve that already happened.
	const std::string api = Tight( ReadStripped( kEntityApi ) );
	ASSERT_FALSE( api.empty() );

	EXPECT_EQ( 1, CountOf( api, "if(ent.IsWorld())" ) )
		<< "EntityGetDataBlob no longer answers for the world, so the managed "
		   "side cannot bind WorldNetState and every one of worldspawn's keys is "
		   "refused";
	EXPECT_EQ( 0, CountOf( api, "dynamic_cast<CWorld" ) )
		<< "the world's blob is found by RTTI. It is a singleton; pointer "
		   "identity is a load and a compare";

	// AND THE ARM IS REAL. IsWorld() reads pWorld, which SourceSharp_ResolveEntity
	// sets from the third arm -- asked BEFORE the two hierarchy asks, both of
	// which answer NULL for the world and would classify it as gone.
	const std::string base = Tight( ReadStripped( "sourcesharp/game/server/shell_standalone_base.cpp" ) );
	ASSERT_FALSE( base.empty() );
	EXPECT_EQ( 1, CountOf( base, "ent.pWorld=SourceSharp_WorldFromEntityHandle(pHandleEntity);" ) )
		<< "SourceSharp_ResolveEntity no longer resolves a world arm, so "
		   "IsWorld() above is permanently false";
}

TEST( WorldShim, TheWorldHasNoNativeWrite )
{
	// There was one: CWorld::SetStartDark wrote a flag into the blob and
	// announced it (m_pEdict->StateChanged(), because an unannounced blob write
	// never reaches the client). Nothing called it, and the server gc cull
	// (2026-10-01) deleted it. A native write coming back must announce itself
	// again; this keeps it from coming back unnoticed.
	const std::string adapter = Tight( ReadStripped( kWorldAdapter ) );
	ASSERT_FALSE( adapter.empty() );
	ASSERT_NE( adapter.find( "voidCWorld::Spawn(" ), std::string::npos ) << kWorldAdapter << " did not read";

	EXPECT_EQ( adapter.find( "CWorld::SetStartDark(" ), std::string::npos )
		<< "CWorld::SetStartDark is back; its blob write must announce itself";
	EXPECT_EQ( adapter.find( "SourceSharpWorld_WriteFlag(" ), std::string::npos )
		<< "the world writes its blob natively again; the write must call "
		   "m_pEdict->StateChanged() or the client never sees it";
}

//=============================================================================//
// 6. THE METER, AND THE GUARD PHASE A5 PUT ON IT.
//=============================================================================//

TEST( WorldShim, TheMeterCountsTheWorldAsManagedBecauseItMoved )
{
	// Phase A5's guard, from the other end. It refused ONE edit: adding
	// world_adapter.cpp to SourceSharp_IsManagedServerClass's identity list
	// while the SDK macro still built the class -- "it would read as an eviction
	// and evict nothing".
	//
	// The macro is gone and the object is constructed from a C# declaration, so
	// counting it is a report of the move. Both halves are asserted together, in
	// two files, so neither a relabelling nor a bare eviction gets a lower meter.
	const std::string nettable = Tight( ReadStripped( kNetTable ) );
	const std::string table    = Tight( ReadStripped( kServerTable ) );
	ASSERT_FALSE( nettable.empty() );
	ASSERT_FALSE( table.empty() );

	EXPECT_EQ( 1, CountOf( nettable, "SourceSharp_IsWorldServerClass(pClass)" ) );
	EXPECT_EQ( 1, CountOf( table, "boolSourceSharp_IsWorldServerClass(constServerClass*pClass)" ) );
	EXPECT_EQ( 1, CountOf( table, "pClass==WorldNetTable().Class()" ) )
		<< "the world's ServerClass is not identified by IDENTITY. A name test "
		   "would have to agree with the table's own spelling forever, and a "
		   "rename would silently move the class from one column of the meter to "
		   "the other";
}

//=============================================================================//
// 7. THE REPORT, WHICH IS THE ONLY LIVE EVIDENCE ANY OF THIS WORKS.
//=============================================================================//

TEST( WorldShim, TheTwoSidesPrintTheirValuesThroughOneBody )
{
	// Phase A2's finding, applied: "an empty body and a working one are
	// otherwise indistinguishable from outside". Every one of the ten values
	// fails as a plausible zero, so the pair of lines is what turns "the client
	// receives them" from an assertion into a reading.
	//
	// ONE FORMATTER, so the server's line and the client's are comparable. Two
	// would be two chances to print a different subset, and the whole value of
	// the pair is that they can be diffed.
	const std::string sync    = Tight( ReadStripped( kSync ) );
	const std::string adapter = Tight( ReadStripped( kWorldAdapter ) );
	const std::string client  = Tight( ReadStripped( kClientWorld ) );
	ASSERT_FALSE( sync.empty() );

	EXPECT_EQ( 1, CountOf( sync, "voidSourceSharpWorld_Report(constchar*pszSide," ) )
		<< "the shared reporter is gone, or there are two of it";
	EXPECT_EQ( 1, CountOf( adapter, "SourceSharpWorld_Report(\"Server\"" ) );
	EXPECT_EQ( 1, CountOf( client, "SourceSharpWorld_Report(\"Client\"" ) );

	// The client's is on CHANGE and not only on create, which is what makes a
	// runtime key write provable: `ent_fire worldspawn AddOutput "..."` reaches
	// CWorld::KeyValue exactly as the map parse does and arrives in a LATER
	// update.
	EXPECT_EQ( 1, CountOf( client, "voidC_World::ReportIfChanged()" ) );
	EXPECT_EQ( 1, CountOf( client, "V_memcmp(m_LastReported,DataBlob(),nBytes)==0" ) )
		<< "the client's report no longer compares against what it last printed, "
		   "so it prints on every world update rather than on every change";
}

//=============================================================================//
// THE ASKS ARE THIS SIDE'S (plan_engineapi_vtables E16). The resolve loops are
// shared; SourceSharp_NetStateAsk (standalone_player_sync.h) hands the server
// its managed-built IManagedNetState and the client its managed-built
// IManagedStandaloneClient (plan_client_vtables CE5).
//=============================================================================//

namespace
{

const char *kAskHeader = "sourcesharp/game/shared/standalone_player_sync.h";

// SourceSharp_NetStateAsk's #ifndef CLIENT_DLL and #else arms, or empty.
void AskArms( std::string &strServer, std::string &strClient )
{
	const std::string ask = Tight( ReadStripped( kAskHeader ) );
	const size_t body  = ask.find( "inlineboolSourceSharp_NetStateAsk(SourceSharpNetStateAsk_t*pAsk){" );
	const size_t open  = body == std::string::npos ? body : ask.find( "#ifndefCLIENT_DLL", body );
	const size_t other = open == std::string::npos ? open : ask.find( "#else", open );
	const size_t close = other == std::string::npos ? other : ask.find( "#endif", other );
	if ( close == std::string::npos )
		return;
	strServer = ask.substr( open, other - open );
	strClient = ask.substr( other, close - other );
}

} // namespace

TEST( WorldShim, TheServerAsksItsNetStateArea )
{
	std::string server, client;
	AskArms( server, client );
	ASSERT_FALSE( server.empty() ) << "SourceSharp_NetStateAsk has no #ifndef CLIENT_DLL / #else arms in " << kAskHeader;

	EXPECT_EQ( 1, CountOf( server, "pAsk->pfnFieldOffset=&SourceSharp_ServerNetStateFieldOffset;" ) );
	EXPECT_EQ( 1, CountOf( server, "pAsk->pfnFieldBits=&SourceSharp_ServerNetStateFieldBits;" ) );
	EXPECT_EQ( 0, CountOf( server, "SourceSharp_GetManagedApi" ) )
		<< "the server arm still reads the ManagedApi struct, whose NetState slots it no longer fills";

	const std::string ask = Tight( ReadStripped( kAskHeader ) );
	EXPECT_EQ( 1, CountOf( ask, "IManagedNetState*pNetState=ManagedNetState();returnpNetState?pNetState->NetStateFieldOffset(pClassName,pFieldName):-1;" ) )
		<< "the server's offset adapter does not read the area at the point of use, or does not answer -1 without it";
	EXPECT_EQ( 1, CountOf( ask, "IManagedNetState*pNetState=ManagedNetState();returnpNetState?pNetState->NetStateFieldBits(pClassName,pFieldName):-1;" ) )
		<< "the server's bits adapter does not read the area at the point of use, or does not answer -1 without it";
}

TEST( WorldShim, TheClientAsksItsStandaloneArea )
{
	std::string server, client;
	AskArms( server, client );
	ASSERT_FALSE( client.empty() ) << "SourceSharp_NetStateAsk has no #else arm in " << kAskHeader;

	EXPECT_EQ( 1, CountOf( client, "pAsk->pfnFieldOffset=&SourceSharp_ClientNetStateFieldOffset;" ) );
	EXPECT_EQ( 1, CountOf( client, "pAsk->pfnFieldBits=&SourceSharp_ClientNetStateFieldBits;" ) );
	EXPECT_EQ( 0, CountOf( client, "SourceSharp_GetManagedApi" ) )
		<< "the client arm still reads the ManagedApi struct, whose NetState slots it no longer fills";

	const std::string ask = Tight( ReadStripped( kAskHeader ) );
	EXPECT_EQ( 1, CountOf( ask, "IManagedStandaloneClient*pStandalone=ManagedStandaloneClient();returnpStandalone?pStandalone->NetStateFieldOffset(pClassName,pFieldName):-1;" ) )
		<< "the client's offset adapter does not read the area at the point of use, or does not answer -1 without it";
	EXPECT_EQ( 1, CountOf( ask, "IManagedStandaloneClient*pStandalone=ManagedStandaloneClient();returnpStandalone?pStandalone->NetStateFieldBits(pClassName,pFieldName):-1;" ) )
		<< "the client's bits adapter does not read the area at the point of use, or does not answer -1 without it";
}
