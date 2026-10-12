//====== SourceSharp — the pool's SPAWN seam (plan_tmp.md Part 5, C-Phase 5) ==//
//
// Lane C4 moved the pool's BOOKKEEPING to C# and tempent_pool_test.cpp pins
// that seam. This is the other direction of the same boundary: seventeen spawn
// helpers in c_te_legacytempents.cpp allocate a tempent and then write fifteen
// to twenty of its fields, and seven of them are now C#. What crosses is one
// struct, SourceSharpTempEntSpawn_t, written whole on arrival.
//
// WHAT IS TESTED HERE AND NOT IN C#. The helpers' BEHAVIOUR is managed and is
// tested where it lives (TempEntSpawnTests, 19 [Fact]s). Three ways this seam
// can be wrong are invisible from that side:
//
//   (1) A FIELD THAT CROSSES AND IS NEVER APPLIED. SourceSharp_TempEntPoolWriteSpawn
//       writes each field onto the tempent by hand. A managed test asserting
//       "the record carried m_flFrameRate = 32" passes whether or not this side
//       ever assigns it -- the record IS what it asserts. The symptom would be
//       a blood sprite that animates at zero frames a second, which looks like
//       a sprite that is simply not animated.
//
//   (2) A ONE-SIDED FIELD ORDER. Every field of the struct is one word and each
//       has a plausible neighbour of the same width, so an insertion on one
//       side shifts everything after it and compiles on both. AbiParity-style
//       pairing is done in C# (TempEntSpawnTests.TheSpawnStructMatchesTheHeader);
//       what is here is the other half -- that the header's own field set has
//       not grown a member nothing applies.
//
//   (3) A STOCK ARM DELETED RATHER THAN GUARDED. The lane's whole C++ diff to
//       c_te_legacytempents.cpp is ONE #ifdef SOURCESHARP function; if that
//       becomes a #ifndef around a stock body, or the stock file loses a
//       helper, the managed side keeps compiling and the native fallback is
//       gone.
//
// EVERY ASSERTION READS THE .cpp OFF DISK, comments stripped first -- and here
// that is load-bearing rather than hygiene, because this lane left long banners
// naming every field it moved. A scan that read the prose would find every
// field name in the explanation of why it is there and pass while measuring
// nothing. Same rule, and the same reason, as tempent_pool_test.cpp.
//
//=============================================================================//
#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <fstream>
#include <set>
#include <string>
#include <vector>
#include <sys/stat.h>
#include <iterator>
#include <cctype>

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

int CountOf( const std::string &hay, const std::string &needle )
{
	int n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + needle.size() ) )
	{
		n++;
	}
	return n;
}

const char *kPoolCpp    = "sourcesharp/game/client/shell_te_pool_client.cpp";
// The thunk that replaced game/client/c_te_legacytempents.cpp when C2 evicted it:
// the ITempEnts and C_LocalTempEntity vtables, carrying out managed answers.
const char *kLegacyCpp  = "sourcesharp/game/client/shell_legacy_tempents_client.cpp";
const char *kLegacyH    = "game/client/c_te_legacytempents.h";
const char *kAbiHeader  = "sourcesharp/host/sourcesharp.h";
// Where the client's slots are wired since plan_client_vtables CC-last deleted
// the struct fill in sourcesharp_gamesystem.cpp: its shim areas' generated forwards.
const char *kClientAreas = "sourcesharp/game/shared/shell_shim_areas_client.inc";
const char *kServerAreas = "sourcesharp/game/shared/shell_shim_areas.inc";
const char *kClientAreasH = "sourcesharp/game/shared/ishim_areas_client.h";

//-----------------------------------------------------------------------------
// HOW MANY GENERATED SHIM-AREA FORWARDS IN `inc` ARE `CShim...::<slot>( ... )`
// WITH THE ONE-LINE BODY `{ return ::<body>( ... ); }` -- or with ANY body when
// `body` is empty. Since plan_client_vtables CC-last a slot is wired by such a
// forward on each side: shell_shim_areas.inc on the server,
// shell_shim_areas_client.inc on the client (both GENERATED by tools/shim-areas
// from docs/engineapi-areas.tsv), and the struct fill that used to say
// `engineApi.<Slot> = <Body>;` is gone. Whitespace is ignored, so the
// generator's line wrapping is not part of the needle; the class name must
// carry "CShim", so the body's own `::<body>(` never counts as a slot.
//-----------------------------------------------------------------------------
int ForwardCount( const std::string &inc, const std::string &slot, const std::string &body )
{
	int n = 0;
	const std::string needle = "::" + slot + "(";
	for ( size_t at = inc.find( needle ); at != std::string::npos; at = inc.find( needle, at + 1 ) )
	{
		size_t b = at;
		while ( b > 0 && ( isalnum( (unsigned char)inc[ b - 1 ] ) || inc[ b - 1 ] == '_' ) )
			b--;
		if ( inc.substr( b, at - b ).find( "CShim" ) == std::string::npos )
			continue;

		const size_t semi = inc.find( ';', at );
		std::string window;
		for ( size_t k = at; k < inc.size() && k <= semi; k++ )
		{
			if ( !isspace( (unsigned char)inc[ k ] ) )
				window += inc[ k ];
		}
		if ( window.find( "){return::" + body ) != std::string::npos
		     && ( body.empty() || window.find( "){return::" + body + "(" ) != std::string::npos ) )
			n++;
	}
	return n;
}
const char *kWorldQryC  = "sourcesharp/game/client/shell_worldquery_client.cpp";
const char *kBackends   = "sourcesharp/game/client/shell_te_backends_client.cpp";

//-----------------------------------------------------------------------------
// EVERY FIELD OF THE SPAWN RECORD, paired with what applying it must look like
// on this side.
//
// WRITTEN OUT RATHER THAN PARSED FROM THE HEADER, on purpose and for
// tempent_pool_test.cpp's reason: a list derived from the thing under test
// would agree with any change to it, which is this project's "do not compare a
// computation with itself". A field added to the header and to the managed
// struct and to nothing else is exactly the drift this catches -- both sides
// would agree, and the tempent would never see it.
//
// pszApplied is the text that must appear in SourceSharp_TempEntPoolWriteSpawn:
// either the tempent member being assigned, or the setter tempent.h publishes
// for it. NULL means the field is not a plain member write and is checked
// separately below (BreakHelper and OwnerEntityIndex).
//-----------------------------------------------------------------------------
struct SpawnField
{
	const char *pszAbi;      // the struct's field, as sourcesharp.h spells it
	const char *pszApplied;  // what applying it looks like in the seam, or NULL
};

const SpawnField kSpawn[] =
{
	{ "Flags",              "pTemp->flags=" },
	{ "Die",                "pTemp->die=" },
	{ "SpriteScale",        "pTemp->m_flSpriteScale=" },
	{ "Frame",              "pTemp->m_flFrame=" },
	{ "FrameMax",           "pTemp->m_flFrameMax=" },
	{ "FrameRate",          "pTemp->m_flFrameRate=" },
	{ "Body",               "pTemp->m_nBody=" },
	{ "Skin",               "pTemp->m_nSkin=" },
	{ "RenderMode",         "pTemp->SetRenderMode(" },
	{ "RenderFx",           "pTemp->m_nRenderFX=" },
	{ "RenderAmt",          "pTemp->tempent_renderamt=" },
	{ "FadeSpeed",          "pTemp->fadeSpeed=" },
	{ "ColorR",             "pTemp->SetRenderColor(" },
	{ "ColorG",             "pTemp->SetRenderColor(" },
	{ "ColorB",             "pTemp->SetRenderColor(" },
	{ "ColorA",             "pTemp->SetRenderColor(" },
	{ "VelocityXYZ",        "pTemp->SetVelocity(" },
	{ "AccelerationXYZ",    "pTemp->SetAcceleration(" },
	{ "AngularVelocityPYR", "pTemp->m_vecTempEntAngVelocity=" },
	{ "AnglesPYR",          "pTemp->SetAbsAngles(" },
	{ "BounceFactor",       "pTemp->bounceFactor=" },
	{ "SineX",              "pTemp->x=" },
	{ "SineY",              "pTemp->y=" },
	{ "NormalXYZ",          "pTemp->m_vecNormal=" },
	{ "HitSound",           "pTemp->hitSound=" },
	{ "ClientIndex",        "pTemp->clientIndex=" },
	{ "BreakHelper",        NULL },
	{ "OwnerEntityIndex",   NULL },
	{ "TentOffsetXYZ",      "pTemp->tentOffset=" },
};

const size_t kSpawnCount = sizeof( kSpawn ) / sizeof( kSpawn[ 0 ] );

//-----------------------------------------------------------------------------
// THE GATE. Every assertion below searches a string, and several are ABSENCES
// which hold trivially in an empty one -- which is how tempent_verdict_test.cpp
// once passed from a directory where src/ did not resolve.
//-----------------------------------------------------------------------------
TEST( TempEntSpawnSeam, EverySubjectFileIsReadable )
{
	const char *kAll[] = { kPoolCpp, kLegacyCpp, kLegacyH, kAbiHeader, kClientAreas,
	                       kWorldQryC, kBackends };

	ASSERT_NE( SrcRoot(), (const char *)NULL )
		<< "src/ was not found from the working directory, so every scan below "
		   "would search an empty string.";

	for ( size_t i = 0; i < sizeof( kAll ) / sizeof( kAll[ 0 ] ); i++ )
	{
		EXPECT_FALSE( ReadStripped( kAll[ i ] ).empty() )
			<< kAll[ i ] << " could not be read or is empty. Nothing else in "
			                "this file is evidence until it can be.";
	}
}

//-----------------------------------------------------------------------------
// (1) EVERY FIELD THAT CROSSES IS APPLIED.
//
// This is the assertion a managed test cannot make. TempEntSpawnTests reads the
// record BACK OUT of the recording engine, so "the helper set FrameRate" and
// "the tempent got a frame rate" are the same statement over there and two
// different statements here.
//-----------------------------------------------------------------------------
TEST( TempEntSpawnSeam, EverySpawnFieldIsReadOutOfTheRecordAndAppliedToTheTempent )
{
	const std::string pool = ReadSquashed( kPoolCpp );

	ASSERT_FALSE( pool.empty() );

	for ( size_t i = 0; i < kSpawnCount; i++ )
	{
		const std::string read = std::string( "pSpawn->" ) + kSpawn[ i ].pszAbi;

		EXPECT_LE( 1, CountOf( pool, read ) )
			<< "SourceSharpTempEntSpawn_t::" << kSpawn[ i ].pszAbi << " is never read "
			   "on this side. A field that crosses and is dropped is a spawn helper "
			   "whose value silently does nothing -- and the managed test for that "
			   "helper still passes, because it asserts the RECORD.";

		if ( kSpawn[ i ].pszApplied == NULL )
			continue;

		EXPECT_LE( 1, CountOf( pool, kSpawn[ i ].pszApplied ) )
			<< "SourceSharpTempEntSpawn_t::" << kSpawn[ i ].pszAbi << " does not reach "
			<< kSpawn[ i ].pszApplied << " on the tempent.";
	}
}

//-----------------------------------------------------------------------------
// (2) THE HEADER HAS NOT GROWN A FIELD NOTHING APPLIES.
//
// The other direction of the same check, and the one that catches an addition
// rather than a deletion. Counted against the list above, which is this file's
// own statement of what the record IS.
//-----------------------------------------------------------------------------
TEST( TempEntSpawnSeam, TheHeaderDeclaresExactlyTheFieldsThisFileKnowsAbout )
{
	const std::string abi = ReadStripped( kAbiHeader );

	ASSERT_FALSE( abi.empty() );

	const size_t end = abi.find( "} SourceSharpTempEntSpawn_t;" );

	ASSERT_NE( std::string::npos, end )
		<< "sourcesharp.h no longer declares SourceSharpTempEntSpawn_t.";

	const size_t start = abi.rfind( "typedef struct", end );

	ASSERT_NE( std::string::npos, start );

	const std::string body = abi.substr( start, end - start );

	for ( size_t i = 0; i < kSpawnCount; i++ )
	{
		// `float   SpriteScale;` or `float   VelocityXYZ[3];` -- a space before
		// the name and either a semicolon or an array bound after it, so
		// "Frame" cannot be satisfied by "FrameMax".
		const std::string scalar = std::string( " " ) + kSpawn[ i ].pszAbi + ";";
		const std::string vector = std::string( " " ) + kSpawn[ i ].pszAbi + "[";

		EXPECT_TRUE( body.find( scalar ) != std::string::npos
		          || body.find( vector ) != std::string::npos )
			<< kSpawn[ i ].pszAbi << " is no longer declared in the header, but this "
			   "file and the seam both still expect it.";
	}

	// Every `float Name;` / `int32_t Name;` / `float Name[3];` in the body,
	// counted -- so a field ADDED to the header without being added here fails.
	int nDeclared = 0;

	for ( size_t at = 0; at < body.size(); )
	{
		const size_t semi = body.find( ';', at );
		if ( semi == std::string::npos )
			break;

		const std::string line = body.substr( at, semi - at );

		if ( line.find( "float" ) != std::string::npos
		  || line.find( "int32_t" ) != std::string::npos )
		{
			nDeclared++;
		}

		at = semi + 1;
	}

	EXPECT_EQ( (int)kSpawnCount, nDeclared )
		<< "the header declares " << nDeclared << " spawn fields and this file knows "
		<< kSpawnCount << ". A field added on one side only shifts every field after "
		   "it -- every one of them is a single word with a plausible same-width "
		   "neighbour, so it compiles on both sides and produces a tempent at "
		   "plausible wrong values.";
}

//-----------------------------------------------------------------------------
// (3) THE TWO FIELDS THAT ARE NOT A MEMBER WRITE reach what they are FOR.
//
// Named for what they do rather than folded into a kind enum with a params
// blob, which is the shape lane CP's slots established. Each is one native
// action a managed helper cannot perform itself, and each has exactly one
// consumer.
//
// ONE BEHAVIOUR PER TEST. The owner half used to live in this body; lane
// `c8-tempents-beams` split it out into the four facts below, because it
// changed and a four-line diff inside a seven-assertion block is exactly the
// shape nobody re-reads.
//-----------------------------------------------------------------------------
TEST( TempEntSpawnSeam, TheBreakGroupReachesItsOneNativeConsumer )
{
	const std::string pool = ReadSquashed( kPoolCpp );
	const std::string legacy = ReadSquashed( kLegacyCpp );

	ASSERT_FALSE( pool.empty() );
	ASSERT_FALSE( legacy.empty() );

	EXPECT_LE( 1, CountOf( pool, "SourceSharpTePool_JoinBreakGroup(pTemp," ) )
		<< "BreakHelper does not reach the break-model lighting group. Without it "
		   "fifteen gibs from one crate build fifteen light caches and none of them "
		   "shares the master's lighting origin -- which is slow and looks fine.";

	// And the far end is in the thunk, where BreakModelDrawHelper is file-static.
	EXPECT_LE( 1, CountOf( legacy, "voidSourceSharpTePool_JoinBreakGroup(" ) )
		<< "shell_legacy_tempents_client.cpp no longer defines the break-group join, "
		   "so the call above it does not link.";

	EXPECT_LE( 1, CountOf( legacy, "pTemp->SetDrawHelper(BreakModelDrawHelper)" ) )
		<< "the join no longer installs BreakModelDrawHelper. The lighting group is "
		   "only ever READ by that draw helper, so a gib in the group without it is "
		   "in a list nothing consults.";

	// The group itself is managed (TempEntBreakGroups): the draw helper asks it
	// for the head whose origin lights the gib.
	EXPECT_LE( 1, CountOf( legacy, "pFx->TempEntLightingOrigin(SourceSharpTePool_SlotOf(entity))" ) )
		<< "BreakModelDrawHelper no longer asks the managed break groups for the "
		   "lighting origin, so every gib lights itself.";
}

//-----------------------------------------------------------------------------
// (3b) THE OWNER, WHICH LANE `c8-tempents-beams` MOVED OFF THE SDK-ONLY ARM.
//
// The owner handle is what Frame()'s collision arm hands its trace filter as
// the entity to IGNORE (c_te_legacytempents.cpp:958). Four separate ways it
// can be wrong, four facts.
//
// THE DEFECT THESE REPLACE, so the change reads as a fix and not as a rename.
// The spawn used to resolve OwnerEntityIndex through
// SourceSharp_ClientSdkEntityFromIndex, whose own header says it "answers NULL
// for every managed entity - and that is the answer, not a gap", and hand the
// result to SetTempEntOwner( C_BaseEntity * ). Every entity in this build's
// maps is managed, so the resolve answered NULL every time and NO tempent has
// ever carried an owner: an FTENT_COLLIDEALL tempent could collide with
// whoever spawned it on its first frame. The seam's both-hierarchies lookup
// answers for a managed owner, and the entity-free setter stores it.
//-----------------------------------------------------------------------------
TEST( TempEntSpawnSeam, TheOwnerReachesTheEntityFreeSetter )
{
	const std::string pool = ReadSquashed( kPoolCpp );
	ASSERT_FALSE( pool.empty() );

	EXPECT_LE( 1, CountOf( pool, "pTemp->SetTempEntOwnerHandle(" ) )
		<< "OwnerEntityIndex does not reach SetTempEntOwnerHandle. The owner handle "
		   "is what Frame's collision arm hands its trace filter as the entity to "
		   "IGNORE, so without it a projectile hits whoever fired it on its first "
		   "frame.";
}

TEST( TempEntSpawnSeam, TheOwnerIsResolvedForBOTHHierarchiesAndNotTheSdkOnlyArm )
{
	const std::string pool = ReadSquashed( kPoolCpp );
	ASSERT_FALSE( pool.empty() );

	// An INDEX, never a handle, in the OTHER direction: reading a client handle
	// back through CBaseHandle::Get() is behaviour ledger row C8 and it killed a
	// live client. Index-to-handle is the safe direction and has not moved. What
	// HAS moved is which lookup does it.
	EXPECT_LE( 1, CountOf( pool, "SourceSharp_ClientHandleFromIndex(pSpawn->OwnerEntityIndex)" ) )
		<< "the owner is no longer resolved through the seam's both-hierarchies "
		   "lookup. Going the other way -- reconstructing a CBaseHandle from the "
		   "wire -- is ledger row C8 and segfaults on this build's managed entities.";
}

TEST( TempEntSpawnSeam, TheSpawnNoLongerAsksTheSdkOnlyArmForTheOwner )
{
	const std::string pool = ReadSquashed( kPoolCpp );
	ASSERT_FALSE( pool.empty() );

	// A CHANGE-DETECTING fact and not a presence one: the assertion above would
	// still pass if BOTH lookups were written, and the SDK-only one answering
	// NULL for every managed entity is the whole defect.
	EXPECT_EQ( 0, CountOf( pool, "SourceSharp_ClientSdkEntityFromIndex(pSpawn->OwnerEntityIndex)" ) )
		<< "the spawn resolves the owner through the SDK-ONLY lookup again. That one "
		   "answers NULL for every managed entity by construction, and every entity "
		   "in this build's maps is managed -- so the tempent gets no owner and the "
		   "collision trace ignores nothing.";
}

TEST( TempEntSpawnSeam, ANegativeOwnerIndexStoresTheINVALIDHandleAndNotEntryZero )
{
	const std::string pool = ReadSquashed( kPoolCpp );
	ASSERT_FALSE( pool.empty() );

	// CBaseHandle::CBaseHandle() sets m_Index = INVALID_EHANDLE_INDEX
	// (public/basehandle.h:87-90), which is the same store the typed overload
	// made for a NULL pOwner. Spelling a zero here instead -- CBaseHandle( 0, 0 )
	// or a literal -- would be a VALID handle to entry 0, which is the world,
	// and the tempent would ignore the world in its collision trace and fall
	// through the floor. That confusion has already cost this port two lanes.
	EXPECT_LE( 1, CountOf( pool, "pTemp->SetTempEntOwnerHandle(CBaseHandle())" ) )
		<< "a negative OwnerEntityIndex no longer stores a default-constructed "
		   "CBaseHandle. That default is the INVALID handle; anything built from a "
		   "zero is a handle to entry 0, which is the world.";
}

//-----------------------------------------------------------------------------
// (4) THE CREATE IS STOCK'S TempEntAlloc, IN STOCK'S ORDER.
//
// Prepare BEFORE the origin, because Prepare itself does
// SetAbsOrigin( vec3_origin ); and the origin BEFORE AddToLeafSystem, because
// the leaf system is told where the renderable is. Getting them the other way
// round is a gib that draws at the world origin for one frame -- a flicker on a
// lit map and nothing at all on a dark one.
//-----------------------------------------------------------------------------
TEST( TempEntSpawnSeam, TheCreateRunsPrepareThenTheOriginThenTheLeafSystem )
{
	const std::string pool = ReadSquashed( kPoolCpp );

	ASSERT_FALSE( pool.empty() );

	const size_t create = pool.find( "int32_tSourceSharp_TempEntPoolCreate(" );

	ASSERT_NE( std::string::npos, create )
		<< "SourceSharp_TempEntPoolCreate is gone, and with it every managed spawn.";

	const size_t prepare = pool.find( "pTemp->Prepare(pModel,gpGlobals->curtime)", create );
	const size_t origin  = pool.find( "pTemp->SetAbsOrigin(", create );
	const size_t leaf    = pool.find( "pTemp->AddToLeafSystem(", create );

	ASSERT_NE( std::string::npos, prepare )
		<< "the create no longer runs stock's Prepare, so the tempent starts from a "
		   "zeroed block rather than from the state TempEntSpawn.Prepared mirrors.";
	ASSERT_NE( std::string::npos, origin );
	ASSERT_NE( std::string::npos, leaf );

	EXPECT_LT( prepare, origin )
		<< "Prepare runs BEFORE the origin is set. Prepare does "
		   "SetAbsOrigin( vec3_origin ) itself, so the other order puts every tempent "
		   "at the world origin.";

	EXPECT_LT( origin, leaf )
		<< "and the origin is set BEFORE AddToLeafSystem, which is what the leaf "
		   "system records the renderable's place from.";

	// The priority is a PARAMETER of the create and not a field of the record,
	// deliberately: the eviction rule reads priority off the LIVE OBJECT, so a
	// high-priority tempent that was briefly low -- between the create and the
	// record write -- could be sacrificed to make room for itself.
	EXPECT_LE( 1, CountOf( pool, "pTemp->priority=nPriority" ) )
		<< "the priority no longer arrives with the create. It must not move into "
		   "the spawn record: the eviction rule reads it off the live object, and a "
		   "high-priority tempent that is briefly low can be evicted for itself.";
}

//-----------------------------------------------------------------------------
// (5) THE STOCK FILE'S DIFF IS STILL INSERTIONS ONLY.
//
// Lane C4's whole C++ diff to c_te_legacytempents.cpp was nine #ifndef
// SOURCESHARP wrappers with the stock arm untouched; C-Phase 5 added one
// #ifdef SOURCESHARP function and nothing else. A compile gate selects for
// deletion (docs/te-lane-contract), so what is asserted is that the seventeen
// spawn helpers are all STILL THERE -- seven of them now have a managed twin,
// and none of the seven was removed to make room for it.
//-----------------------------------------------------------------------------
TEST( TempEntSpawnSeam, EverySpawnHelperIsStillDefinedInTheThunk )
{
	const std::string legacy = ReadSquashed( kLegacyCpp );

	ASSERT_FALSE( legacy.empty() );

	// The seven with a managed twin, plus the ten without. Named individually
	// so a red says WHICH one went.
	const char *kHelpers[] =
	{
		"voidCTempEnts::FizzEffect(",
		"voidCTempEnts::Bubbles(",
		"voidCTempEnts::BubbleTrail(",
		"voidCTempEnts::BreakModel(",
		"C_LocalTempEntity*CTempEnts::ClientProjectile(",
		"C_LocalTempEntity*CTempEnts::TempSprite(",
		"voidCTempEnts::Sprite_Spray(",
		"voidCTempEnts::Sprite_Trail(",
		"voidCTempEnts::AttachTentToPlayer(",
		"voidCTempEnts::KillAttachedTents(",
		"voidCTempEnts::RicochetSprite(",
		"voidCTempEnts::BloodSprite(",
		"C_LocalTempEntity*CTempEnts::DefaultSprite(",
		"voidCTempEnts::Sprite_Smoke(",
		"voidCTempEnts::EjectBrass(",
		"C_LocalTempEntity*CTempEnts::SpawnTempModel(",
		"voidCTempEnts::Sprite_Explode(",
	};

	for ( size_t i = 0; i < sizeof( kHelpers ) / sizeof( kHelpers[ 0 ] ); i++ )
	{
		EXPECT_LE( 1, CountOf( legacy, kHelpers[ i ] ) )
			<< kHelpers[ i ] << " is gone from shell_legacy_tempents_client.cpp. The managed "
			   "spawn helpers are ADDITIONS, not replacements: EjectBrass, "
			   "AttachTentToPlayer and SpawnTempModel are still the only entry points "
			   "for the four non-temp-entity callers of this pool, and every one of "
			   "these is still what its native callers reach.";
	}

	// THE ONE THING THIS LANE ADDED TO THE STOCK FILE IS NO LONGER GUARDED, AND
	// THAT IS THE POINT OF THE ASSERTIONS BELOW.
	//
	// This paragraph used to read "is under #ifdef SOURCESHARP -- so a stock
	// client, which compiles this file with SOURCESHARP undefined, does not see
	// it at all", and the assertion under it walked back from the function to the
	// nearest `#ifdef SOURCESHARP` and forward to its `#endif`.
	//
	// WHAT REMOVED THE SUBJECT, named: the `residue` wave,
	// 74b071771..a46e8d645. At 74b071771 the `#ifdef SOURCESHARP` stood at
	// game/client/c_te_legacytempents.cpp:1613 and its `#endif` at :1639, with
	// SourceSharpTePool_JoinBreakGroup`s definition between them at :1631. The
	// wave deleted the two directives and left the body. That is not a loosening:
	// there is no build of this tree with SOURCESHARP undefined. Both projects
	// define it (both targets' target_compile_definitions), so the `#ifdef` was
	// always taken and the `#else`-less guard decided nothing. The wave deleted
	// 634 lines from this file and the client`s objects came out identical.
	//
	// THE STOCK-CLIENT ARGUMENT IS THE ONE THAT EXPIRED, and it is worth saying
	// plainly because the rest of this file still leans on it: client_base.vpc
	// does still list c_te_legacytempents.cpp, but nothing in this tree builds
	// client_base.vpc, so "a stock client would compile this" describes a build
	// that does not happen here. The residue wave`s ruling is that a guard whose
	// other arm is never compiled is not protection, it is dead text.
	//
	// SO WHAT IS ASSERTED NOW: the definition is present, exactly once, and it is
	// UNCONDITIONAL -- the file carries no SOURCESHARP conditional at all, so
	// there is no arm the definition could be hiding in and no `#endif` that
	// could be moved to strand it. Presence and absence, and the presence half is
	// what stops the absence half passing over an empty read.
	EXPECT_EQ( 1, CountOf( legacy, "voidSourceSharpTePool_JoinBreakGroup(" ) )
		<< "shell_legacy_tempents_client.cpp does not define SourceSharpTePool_JoinBreakGroup "
		   "exactly once. BreakModelDrawHelper is file-static "
		   "to this translation unit, so this is the only place the BreakHelper field "
		   "can be answered from -- zero is an undefined reference from "
		   "shell_te_pool_client.cpp, and two is two lighting groups.";

	// UNCONDITIONAL, asserted over the whole file rather than around the
	// function: a needle-around-the-definition check answers "no guard here" just
	// as happily when the definition has moved, and this one does not.
	EXPECT_EQ( 0, CountOf( legacy, "#ifdefSOURCESHARP" ) )
		<< "a #ifdef SOURCESHARP is in shell_legacy_tempents_client.cpp. Every project "
		   "that builds this tree defines SOURCESHARP, so its `#else` -- if it has "
		   "one -- is dead source the residue wave will remove again, and if it has "
		   "none the guard decides nothing at all.";

	EXPECT_EQ( 0, CountOf( legacy, "#ifndefSOURCESHARP" ) )
		<< "a #ifndef SOURCESHARP is in shell_legacy_tempents_client.cpp. That arm is "
		   "never compiled by anything this tree builds; the residue wave deleted "
		   "nine of them from this file for exactly that reason.";

	// AND THE OTHER END OF THE SEAM, so that "defined here" is not the whole
	// claim: the managed pool is still the caller, and it still declares the
	// function it calls.
	const std::string pool = ReadSquashed( kPoolCpp );
	ASSERT_FALSE( pool.empty() );

	EXPECT_NE( pool.find( "voidSourceSharpTePool_JoinBreakGroup(C_LocalTempEntity*pTemp,boolbSlave);" ),
	           std::string::npos )
		<< "shell_te_pool_client.cpp no longer declares the break-group join it "
		   "calls. The definition above would then be a function nothing reaches, "
		   "and every break model would build its own light cache.";

	EXPECT_NE( pool.find( "SourceSharpTePool_JoinBreakGroup(pTemp," ), std::string::npos )
		<< "shell_te_pool_client.cpp no longer CALLS the break-group join. The "
		   "SourceSharpTempEntSpawn_t::BreakHelper field is answered nowhere else.";
}

//-----------------------------------------------------------------------------
// (6) THE FOUR NEW SLOTS ARE WIRED, and the two model ones are wired on BOTH
//     arms.
//
// A slot the header declares and nobody assigns is a null pointer every guarded
// call in Engine.cs skips in silence. For the spawn pair that silence is a
// helper that reserves a slot, constructs nothing into it, and leaves the pool
// holding a reservation that can never expire.
//
// ModelFrameCount is the interesting one: it is NOT a new slot. It has existed
// since a managed env_sprite needed m_flMaxFrame, with a SERVER arm only -- so
// a managed client asking got -1 and no error, which is the fifth instance of
// the shape SideAuditTests records for EntityGetModelName, EntityGetAngles,
// EntitySetSkin and PointContents.
//-----------------------------------------------------------------------------
TEST( TempEntSpawnSeam, TheSpawnSlotsAreImplementedHereAndWiredThere )
{
	const std::string pool = ReadSquashed( kPoolCpp );
	const std::string sys  = ReadSquashed( kClientAreas );
	const std::string srv  = ReadSquashed( kServerAreas );
	const std::string abi  = ReadSquashed( kAbiHeader );
	const std::string wq   = ReadSquashed( kWorldQryC );
	const std::string bk   = ReadSquashed( kBackends );
	const std::string areas = ReadSquashed( kClientAreasH );

	ASSERT_FALSE( pool.empty() );
	ASSERT_FALSE( sys.empty() );
	ASSERT_FALSE( srv.empty() );
	ASSERT_FALSE( abi.empty() );
	ASSERT_FALSE( wq.empty() );
	ASSERT_FALSE( bk.empty() );
	ASSERT_FALSE( areas.empty() );

	// The two that are the pool's, implemented in the pool's own seam.
	const char *kPoolSlots[] = { "TempEntPoolCreate", "TempEntPoolWriteSpawn" };

	for ( size_t i = 0; i < 2; i++ )
	{
		const std::string impl = std::string( "SourceSharp_" ) + kPoolSlots[ i ] + "(";

		EXPECT_LE( 1, CountOf( pool, impl ) )
			<< kPoolSlots[ i ] << " has no implementation in the pool seam.";

		// A client shim-area method since plan_client_vtables CC-last deleted
		// the engine API struct; both answer an int32_t (squashed text).
		EXPECT_EQ( 1, CountOf( areas, std::string( "virtualint32_t" ) + kPoolSlots[ i ] + "(" ) )
			<< kPoolSlots[ i ] << " is no longer declared in ishim_areas_client.h.";

		EXPECT_EQ( 1, ForwardCount( sys, kPoolSlots[ i ], std::string( "SourceSharp_" ) + kPoolSlots[ i ] ) )
			<< kPoolSlots[ i ] << " is declared and never wired (no client shim-area "
			   "forward to its body), which is a null "
			   "pointer every guarded call in Engine.cs skips in silence. For THIS "
			   "one that silence is a reservation with nothing in it -- a pool slot "
			   "that can never expire and never be reused.";
	}

	//-------------------------------------------------------------------------
	// THE TWO MODEL QUESTIONS ARE NOT ON THIS SEAM ANY MORE, and the hazard
	// they carried is still defended -- somewhere else.
	//
	// plan_native_api.md Phase 3 converted ModelFrameCount and ModelType onto
	// IVModelInfo, and Phase 4 removed their slots, their fills and both sides'
	// shell arms. So the four arms that used to stand here -- "the CLIENT arm
	// is gone", "wired on two arms and must be on two" -- have no subject left
	// to read, and this file is a SOURCE SCAN: it would go on passing forever
	// if they were rewritten to look for something else, and go on failing
	// forever if they were left as they were.
	//
	// WHAT THEY WERE DEFENDING IS EXACTLY AS REAL AS IT WAS. Ten of the
	// seventeen spawn helpers open by asking a model's frame count, and a
	// SERVER answer on the CLIENT is -1, which sets m_flFrameMax to -2 and
	// kills an animated sprite on its first frame; and ModelType's one reader
	// is BreakModel, where mod_bad is ZERO so a wrong side answers "that model
	// is broken" about a model that is fine. Both are the same bug: the client
	// asking the server's object.
	//
	// It cannot happen through the flat table any more because there is no flat
	// table row to leave unwired. It can still happen ONE way -- managed asking
	// for the wrong version string -- and that is what defends it now:
	//
	//     EngineBound.ModelInfo asks for VModelInfoClient006 on the client and
	//     VModelInfoServer004 on the server, and
	//     EngineBoundTests.ModelInfoIsAskedForByTheSidesOwnName says so.
	//
	// The arms are not deleted quietly; this block is what says where they
	// went, because a source scan that stops scanning something is exactly the
	// shape of check that disappears without anybody noticing.
	//-------------------------------------------------------------------------
	EXPECT_EQ( 0, CountOf( wq, "int32_tSourceSharp_ModelFrameCount(" ) )
		<< "the shell arms of ModelFrameCount are back. They were removed in the "
		   "Phase 4 batch because IVtableModelInfo.FrameCountAt does the same two "
		   "calls with no shim in between; a re-added arm is a second answer to one "
		   "question.";

	EXPECT_EQ( 0, CountOf( wq, "int32_tSourceSharp_ModelType(" ) )
		<< "the shell arms of ModelType are back -- see above.";

	EXPECT_EQ( 0, CountOf( abi, "(*ModelType)(" ) )
		<< "ModelType is declared in sourcesharp.h again. Its slot was retired in "
		   "the Phase 4 removal batch.";

	// "Wired again" is a shim-area forward on either side now that the struct
	// fill is gone; ModelFrameCount's slot once had a server arm too.
	ASSERT_EQ( 1, ForwardCount( srv, "GetCurrentTime", "SourceSharp_GetCurrentTime" ) )
		<< "the server's forwards did not read as forwards; the absences below "
		   "are not evidence";
	EXPECT_EQ( 0, ForwardCount( sys, "ModelFrameCount", "" ) + ForwardCount( srv, "ModelFrameCount", "" ) )
		<< "ModelFrameCount is wired again; its slot is gone.";

	EXPECT_EQ( 0, ForwardCount( sys, "ModelType", "" ) + ForwardCount( srv, "ModelType", "" ) )
		<< "ModelType is wired again; its slot is gone.";

	// The slot this lane added that was not the pool's, ClientBloodPuff, is
	// gone with lane c5-particles: CTEBloodSprite's second effect (FX_Blood) is
	// C#'s ImpactSprays.Blood, called by ClientTeBloodSprite beside the pool's
	// sprite (TempEntClientPool2Tests.ABloodSpriteFiresBOTHHalvesOffOneOffset
	// holds both halves).
	EXPECT_EQ( 0, CountOf( bk, "voidSourceSharp_ClientBloodPuff(" ) )
		<< "ClientBloodPuff has a native body again; the puff is managed.";

	EXPECT_EQ( 0, ForwardCount( sys, "ClientBloodPuff", "" ) )
		<< "ClientBloodPuff is wired again; its slot is gone.";
}

//-----------------------------------------------------------------------------
// (7) THE TEN CLASSES ARE EVICTED, AND THE TWO THAT STILL HAVE A CALLER KEEP A
//     FORWARD THROUGH THE MANAGED PLAY PATH.
//
// Evicting a c_te_*.cpp is not optional -- the client resolves a data table BY
// NAME and cannot hold two of one -- and it takes that file's TE_X() with it.
// The eviction half below is unchanged and still holds for all ten.
//
// THE FORWARD HALF WAS TEN AND IS NOW TWO, and the premise that made it ten is
// what changed rather than the standard. It read: "c_te.cpp both declares and
// CALLS every one of them from CTempEntsSystem, so a missing forward is a link
// error and what this catches is the other half -- a forward that exists and
// does not go through the managed play path." plan_shim.md Phase 1f took stock
// c_te.cpp out of client_csharp.vpc. Nothing declares those ten TE_X() any
// more, so a missing forward is not a link error, and for eight of the ten the
// CORRECT state is no forward at all: the ITempEntsSystem override that would
// have reached them is one of the 38 empty ones in
// shell_te_forwards_client.cpp, because no `te->` call site on the client link
// line reaches it.
//
// SO THE ABSENCE IS ASSERTED TOO, rather than simply dropped. Eight rows below
// say "no forward", which is a claim that fails the moment somebody adds one --
// and adding one without a caller is exactly the dead code this section exists
// to keep out. The complementary half, that no CALLER appears either, is
// TempEntForwardersCollapsed.TheOnlyTeArrowCallsLeftAreTheThreeThatForward in
// tempent_evicted_test.cpp; between them a forward and its caller can only be
// added together.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// "CTEGlowSprite" -> "SOURCESHARP_TE_FWD_GLOW_SPRITE", which is how a converted
// forward names the class it forwards. Derived rather than tabulated: a second
// table of forty-two names is a second thing to keep in step, and the managed
// TempEntForwardIdTests already holds the C++ header against the managed enum.
//-----------------------------------------------------------------------------
static std::string ScreamingIdOf( const char *pszClass )
{
	std::string name = pszClass;

	if ( name.compare( 0, 3, "CTE" ) == 0 )
		name = name.substr( 3 );

	std::string out = "SOURCESHARP_TE_FWD_";

	for ( size_t i = 0; i < name.size(); i++ )
	{
		const bool bBoundary = i > 0 && isupper( (unsigned char)name[ i ] )
		                     && !isupper( (unsigned char)name[ i - 1 ] );

		if ( bBoundary )
			out += '_';

		out += (char)toupper( (unsigned char)name[ i ] );
	}

	return out;
}

TEST( TempEntSpawnSeam, EveryEvictedClassHasAForwardThroughTheManagedPlayPath )
{
	const std::string fwd = ReadSquashed(
		"sourcesharp/game/client/shell_te_forwards_client.cpp" );
	// What client_sourcesharp compiles (src/client/native/CMakeLists.txt,
	// cross-checked against compile_commands.json). Throws on a missing, empty
	// or stale input.
	const std::set< std::string > client = cmake_graph::CompiledSources( cmake_graph::kClient );

	ASSERT_FALSE( fwd.empty() );

	// Any compiled source whose basename is pszFile -- the vpc scan matched the
	// name anywhere in the list, so a copy under another directory counts too.
	struct NamesFile
	{
		static bool In( const std::set< std::string > &srcs, const char *pszFile )
		{
			const std::string strSuffix = std::string( "/" ) + pszFile;
			for ( const std::string &src : srcs )
			{
				if ( src.size() >= strSuffix.size()
					&& src.compare( src.size() - strSuffix.size(), strSuffix.size(), strSuffix ) == 0 )
					return true;
			}
			return false;
		}
	};

	struct Ported
	{
		const char *pszFile;   // the evicted c_te_*.cpp
		const char *pszClass;  // the class name the two sides pair on
		bool bForwarded;       // does anything on this link line still make one?
	};

	// bForwarded is `is there a te-> call site for it`, measured over the 261
	// objects on the client link line at c81e5b48b: te->Bubbles in
	// cdll_util.cpp:301, and nothing for the other nine. CTESmoke, the third
	// forwarded class, is not in this list because c_te_smoke.cpp was not one of
	// the ten this section evicts.
	const Ported kPorted[] =
	{
		{ "c_te_sprite.cpp",            "CTESprite",           false },
		{ "c_te_glowsprite.cpp",        "CTEGlowSprite",       false },
		{ "c_te_spritespray.cpp",       "CTESpriteSpray",      false },
		{ "c_te_bloodsprite.cpp",       "CTEBloodSprite",      false },
		{ "c_te_bubbles.cpp",           "CTEBubbles",          true  },
		{ "c_te_bubbletrail.cpp",       "CTEBubbleTrail",      false },
		{ "c_te_breakmodel.cpp",        "CTEBreakModel",       false },
		{ "c_te_clientprojectile.cpp",  "CTEClientProjectile", false },
		{ "c_te_fizz.cpp",              "CTEFizz",             false },
		{ "c_te_physicsprop.cpp",       "CTEPhysicsProp",      false },
	};

	// POSITIVE CONTROL, once, before any absence is read out of the list.
	// "Excluded" is ABSENCE from what client_sourcesharp compiles -- and an
	// absence assertion over a list that was not really read passes for the
	// wrong reason.
	ASSERT_EQ( 1u, client.count( kLegacyCpp ) )
		<< "client_sourcesharp does not compile shell_legacy_tempents_client.cpp, so it is "
		   "not the file list this test thinks it is and every exclusion below "
		   "would pass vacuously.";

	for ( size_t i = 0; i < sizeof( kPorted ) / sizeof( kPorted[ 0 ] ); i++ )
	{
		EXPECT_FALSE( NamesFile::In( client, kPorted[ i ].pszFile ) )
			<< kPorted[ i ].pszFile << " is back in client_sourcesharp's source "
			   "list (src/client/native/CMakeLists.txt), so its native class still registers "
			<< kPorted[ i ].pszClass
			<< " -- and the client cannot hold two data tables of one name.";

		// EITHER FORM. A forward emptied into C# names an id rather than a
		// class -- SourceSharpTe_Forward( SOURCESHARP_TE_FWD_SPRITE, ... ) -- so
		// looking only for the class string reports "no forward" about one that
		// is right there, which is what this test did the moment the sweep
		// reached the files it watches.
		const std::string begin = std::string( "SourceSharpTe_Begin(TeHandle(s_hClass,\"" )
		                        + kPorted[ i ].pszClass + "\")";

		const std::string id = std::string( "SourceSharpTe_Forward(" )
		                     + ScreamingIdOf( kPorted[ i ].pszClass );

		const int nForwards = CountOf( fwd, begin ) + CountOf( fwd, id );

		if ( kPorted[ i ].bForwarded )
		{
			EXPECT_LE( 1, nForwards )
				<< kPorted[ i ].pszClass << " has no forward through the managed writer, "
				   "and something on this link line still calls te-> for it. A forward "
				   "that reached the spawn helper directly would be a SECOND description "
				   "of this effect on this client, and the two would agree exactly until "
				   "somebody changed one of them.";
		}
		else
		{
			EXPECT_EQ( 0, nForwards )
				<< kPorted[ i ].pszClass << " has a client-side forward and no caller. "
				   "Nothing on the client link line calls te-> for it -- the override in "
				   "shell_te_forwards_client.cpp is one of the 38 empty ones -- so a "
				   "forward here is reached by nothing, and dead code that looks like a "
				   "shipped effect is what Phase 1f removed 50 of. If a caller was just "
				   "added, flip this row's bForwarded and say where the caller is.";
		}
	}

	// c_te_legacytempents.cpp IS evicted (C2): its render choices, muzzle flashes,
	// sounds, attachments, brass and break groups are managed, and the thunk above
	// holds the vtables. Two definitions of CTempEnts would not link, but a
	// re-listed stock file next to a thunk is the shape to say out loud.
	EXPECT_EQ( 0u, client.count( "game/client/c_te_legacytempents.cpp" ) )
		<< "c_te_legacytempents.cpp is back in client_sourcesharp's source list. "
		   "shell_legacy_tempents_client.cpp replaces it.";
}

} // namespace
