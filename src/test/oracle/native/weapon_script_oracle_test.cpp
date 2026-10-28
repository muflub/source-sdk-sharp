//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ==================================================//
//
// THE NATIVE ORACLE for the managed weapon script database.
//
// WHY THIS EXISTS. plan_entity.md §6's "17 that LEAVE" moves the weapon script
// database to managed code: FileWeaponInfo_t's clip sizes, slots, buckets and
// sounds are what the game does. The gate for that move is that the managed
// declaration answers the SAME values the C++ parse answered -- and the only
// instrument that can say what the C++ parse answered is the C++ parse.
//
// So this compiles and runs the REAL game/shared/weapon_parse.cpp and the REAL
// game/shared/weapon_parse_default.cpp -- upstream's, from the pristine Valve
// SDK clone at valve-sdk.lock (src/test/oracle/native/CMakeLists.txt), not the
// fork's edited copies -- against the REAL
// game/mod_sharp/scripts/weapon_*.txt, with the REAL tier1 KeyValues doing the
// reading. Nothing here is a transcription of the parse: the subject IS
// FileWeaponInfo_t::Parse, reached through CreateWeaponInfo() exactly as
// ReadWeaponDataFromFileForSlot reaches it.
//
// `fixture-must-be-checked-against-native`: a stand-in for native drifts
// silently, so assert the agreement. This is that assertion.
//
// HOW THE TWO SIDES ARE TIED TOGETHER. sourcesharp/test/weapon_script_cases.txt
// is a golden table. THIS test asserts that the native parse produces it, and
// SourceSharp.Tests/WeaponScriptOracleTests.cs asserts that the managed parse
// produces it. Neither side can drift without one of them going red, and the
// two suites never have to run in a particular order.
//
// **REGENERATING THE GOLDEN IS NOT A FIX.** Creating the marker file
// sourcesharp/test/weapon_script_cases.regen and re-running this test rewrites
// the table from whatever the native parse currently says, which will
// make THIS test pass by construction and move the disagreement onto the
// managed side. It exists so the table can be recreated when a weapon script is
// added or edited on purpose. If it is used to silence a red test, the check
// has been deleted rather than satisfied.
//
// WHAT IT COMPILES AGAINST. Three one-TU stubs in weapon_parse_stub/ beside
// this file -- cbase.h, util_shared.h and cdll_util.h -- for the reason
// map_entity_block_stub/cbase.h gives; see those files. None contributes
// anything to Parse's body, which is the code under test. CAmmoDef's three
// out-of-line members are defined below (upstream keeps them in ammodef.cpp).
//
// WHAT IT DOES NOT COVER, stated rather than left to be discovered:
//
//   * iAmmoType / iAmmo2Type. Parse turns an ammo NAME into an index through
//     GetAmmoDef()->Index(), and that index is assigned by the ammo definition
//     table -- a different system, already ported (SourceSharp.Shared/
//     AmmoTypes.cs). The managed WeaponScript deliberately keeps the NAME. The
//     stub ammo def here holds no types, so Index() answers -1 for everything
//     and the field is not in the golden.
//
//   * The CLIENT_DLL spelling of the console-bucket condition. Parse reads
//     `hud_fastswitch.GetInt() == 2` when CLIENT_DLL is defined and
//     `IsX360()` when it is not; this TU is built without it, so the oracle
//     takes the IsX360() arm. Both are FALSE in this build -- IsX360() is a
//     compile-time false on linux64, and SourceSharp.Client/ConVars.cs declares hud_fastswitch
//     with a default of "1" -- so the two arms select the same values, and the
//     managed side is driven with UseConsoleBuckets=false to match. The
//     bucket_360 keys are covered separately, by a managed fact that turns the
//     flag ON, because no shipped script carries one.
//
//   * The encrypted (.ctx) script path in ReadEncryptedKVFile. No shipped
//     script uses it and no managed code reads one; UTIL_DecodeICE is defined
//     here only to satisfy the link and asserts if it is ever reached.
//
//=============================================================================

#include <gtest/gtest.h>

#include <KeyValues.h>
#include <ammodef.h>
#include <weapon_parse.h>

#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

//-----------------------------------------------------------------------------
// The symbols the real weapon_parse.o leaves undefined, measured with
// `nm -u` rather than guessed at.
//-----------------------------------------------------------------------------

// Never reached: no shipped script is encrypted and the tests below all take
// the plain .txt path. It asserts rather than returning quietly so that a
// future case which DOES reach it cannot be read as having decoded anything.
void UTIL_DecodeICE( unsigned char *, int, const unsigned char * )
{
	ADD_FAILURE() << "UTIL_DecodeICE was called. No shipped weapon script is "
					 "encrypted, so reaching this means the test drove the "
					 ".ctx path by accident and its readings are of a buffer "
					 "nothing decoded.";
}

// Upstream's ammodef.h only DECLARES CAmmoDef's constructor, destructor and
// Index(); their bodies are in game/shared/ammodef.cpp, the ammo table's own
// system (already ported: SourceSharp.Shared/AmmoTypes.cs), which is not the
// subject here. This is the table with no types in it: it starts at index 1,
// as upstream's does (0 is the invalid ammo type), owns no names, and so
// answers -1 for every name Parse asks about.
CAmmoDef::CAmmoDef()
{
	m_nAmmoIndex = 1;
}

CAmmoDef::~CAmmoDef() {}

int CAmmoDef::Index( const char * )
{
	return -1;
}

static CAmmoDef g_TestAmmoDef;

CAmmoDef *GetAmmoDef()
{
	return &g_TestAmmoDef;
}

//-----------------------------------------------------------------------------
// The thirteen scripts this mod ships. Listed rather than globbed, so that a
// script disappearing is a RED TEST and not a silently shorter run --
// `a check that cannot fail` is the recorded failure this guards against.
//-----------------------------------------------------------------------------
static const char *const kWeapons[] = {
	"weapon_357",  "weapon_ar2",        "weapon_crossbow",  "weapon_crowbar", "weapon_cubemap",
	"weapon_frag", "weapon_physcannon", "weapon_pistol",    "weapon_rpg",     "weapon_shotgun",
	"weapon_slam", "weapon_smg1",       "weapon_stunstick",
};

static const int kWeaponCount = (int)( sizeof( kWeapons ) / sizeof( kWeapons[0] ) );

static const char *const kGoldenPath = "sourcesharp/test/weapon_script_cases.txt";
static const char *const kScriptDir = "../game/mod_sharp/scripts/";
static const char *const kRegenMarkerPath = "sourcesharp/test/weapon_script_cases.regen";

//-----------------------------------------------------------------------------
// Reading.
//-----------------------------------------------------------------------------

static std::string ReadFile( const std::string &path, bool *pOk )
{
	std::ifstream in( path.c_str() );
	*pOk = in.good();

	if ( !in.good() )
		return std::string();

	std::stringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

// weapon -> ( field -> value ), which is the golden's shape too.
typedef std::map<std::string, std::string> FieldMap;
typedef std::map<std::string, FieldMap> ScriptMap;

//-----------------------------------------------------------------------------
// Run the REAL parse over one script.
//-----------------------------------------------------------------------------
static FileWeaponInfo_t *ParseRealScript( const char *pName )
{
	bool ok = false;
	std::string text = ReadFile( std::string( kScriptDir ) + pName + ".txt", &ok );

	if ( !ok )
		return NULL;

	KeyValues *pKV = new KeyValues( "WeaponDatafile" );

	if ( !pKV->LoadFromBuffer( pName, text.c_str() ) )
	{
		pKV->deleteThis();
		return NULL;
	}

	// CreateWeaponInfo() is weapon_parse_default.cpp's -- the real one, so the
	// third file of the trio is on the link line and covered too.
	FileWeaponInfo_t *pInfo = CreateWeaponInfo();
	pInfo->Parse( pKV, pName );

	pKV->deleteThis();
	return pInfo;
}

static std::string Int( int v )
{
	char buf[32];
	V_snprintf( buf, sizeof( buf ), "%d", v );
	return buf;
}

static std::string Bool( bool v )
{
	return v ? "true" : "false";
}

//-----------------------------------------------------------------------------
// The field set the golden carries, from one parsed script.
//
// Sounds are emitted ONLY when non-empty, and the category is named rather
// than numbered: a golden keyed by index would still match after somebody
// renumbered WeaponSound_t, which is exactly the drift the numbering comment in
// WeaponScript.cs warns about.
//-----------------------------------------------------------------------------
static FieldMap FieldsOf( const FileWeaponInfo_t *pInfo )
{
	FieldMap f;

	f["classname"] = pInfo->szClassName;
	f["printname"] = pInfo->szPrintName;
	f["viewmodel"] = pInfo->szViewModel;
	f["worldmodel"] = pInfo->szWorldModel;
	f["animprefix"] = pInfo->szAnimationPrefix;

	f["slot"] = Int( pInfo->iSlot );
	f["position"] = Int( pInfo->iPosition );

	f["maxclip1"] = Int( pInfo->iMaxClip1 );
	f["maxclip2"] = Int( pInfo->iMaxClip2 );
	f["defaultclip1"] = Int( pInfo->iDefaultClip1 );
	f["defaultclip2"] = Int( pInfo->iDefaultClip2 );

	f["weight"] = Int( pInfo->iWeight );
	f["rumble"] = Int( pInfo->iRumbleEffect );
	f["flags"] = Int( pInfo->iFlags );

	f["ammo1"] = pInfo->szAmmo1;
	f["ammo2"] = pInfo->szAmmo2;

	f["autoswitchto"] = Bool( pInfo->bAutoSwitchTo );
	f["autoswitchfrom"] = Bool( pInfo->bAutoSwitchFrom );
	f["melee"] = Bool( pInfo->m_bMeleeWeapon );
	f["builtrighthanded"] = Bool( pInfo->m_bBuiltRightHanded );
	f["allowflipping"] = Bool( pInfo->m_bAllowFlipping );
	f["showusagehint"] = Bool( pInfo->bShowUsageHint );

	extern const char *pWeaponSoundCategories[NUM_SHOOT_SOUND_TYPES];

	for ( int i = 0; i < NUM_SHOOT_SOUND_TYPES; i++ )
	{
		if ( pInfo->aShootSounds[i][0] )
			f[std::string( "sound." ) + pWeaponSoundCategories[i]] = pInfo->aShootSounds[i];
	}

	return f;
}

//-----------------------------------------------------------------------------
// The golden table: "<weapon>\t<field>\t<value>" a line, '#' comments, sorted.
//-----------------------------------------------------------------------------
static ScriptMap ParseGolden( const std::string &text )
{
	ScriptMap out;
	std::istringstream in( text );
	std::string line;

	while ( std::getline( in, line ) )
	{
		if ( line.empty() || line[0] == '#' )
			continue;

		size_t a = line.find( '\t' );
		if ( a == std::string::npos )
			continue;
		size_t b = line.find( '\t', a + 1 );
		if ( b == std::string::npos )
			continue;

		out[line.substr( 0, a )][line.substr( a + 1, b - a - 1 )] = line.substr( b + 1 );
	}

	return out;
}

static ScriptMap NativeAnswers()
{
	ScriptMap out;

	for ( int i = 0; i < kWeaponCount; i++ )
	{
		FileWeaponInfo_t *pInfo = ParseRealScript( kWeapons[i] );

		if ( !pInfo )
			continue;

		out[kWeapons[i]] = FieldsOf( pInfo );

		// NOT deleted, and that is deliberate. FileWeaponInfo_t is polymorphic
		// with a NON-VIRTUAL destructor -- stock's own, which is why its
		// ResetFileWeaponInfoDatabase (the only place that deletes one) is
		// #if 0'd out in weapon_parse.cpp. `delete pInfo` here is undefined
		// behaviour and this file is compiled with -Werror=delete-non-virtual-dtor,
		// so thirteen small objects are leaked for the life of the test process
		// instead. Recorded rather than hidden.
	}

	return out;
}

//-----------------------------------------------------------------------------
// Regeneration -- deliberate, opt-in, and loud. See the header.
//-----------------------------------------------------------------------------
static void MaybeWriteGolden( const ScriptMap &answers )
{
	// A MARKER FILE rather than an environment variable: src/sdk_container
	// passes exactly two env vars into the sniper container these tests run in,
	// so SS_WEAPON_GOLDEN_WRITE=1 on the host silently did nothing and the
	// table came out empty. The marker is in the mounted tree, so it crosses.
	{
		std::ifstream marker( kRegenMarkerPath );

		if ( !marker.good() )
			return;
	}

	FILE *fp = fopen( kGoldenPath, "w" );
	ASSERT_TRUE( fp != NULL ) << "cannot write " << kGoldenPath;

	fprintf( fp, "# The answers game/shared/weapon_parse.cpp's FileWeaponInfo_t::Parse\n" );
	fprintf( fp, "# gives for the weapon scripts this mod ships, recorded by\n" );
	fprintf( fp, "# src/test/oracle/native/weapon_script_oracle_test.cpp running the REAL parse.\n" );
	fprintf( fp, "#\n" );
	fprintf( fp, "# Both sides of the port are pinned to this file: the native oracle\n" );
	fprintf( fp, "# asserts it produces these, and SourceSharp.Tests/WeaponScriptOracleTests.cs\n" );
	fprintf( fp, "# asserts the managed database produces the same. REGENERATING THIS TABLE\n" );
	fprintf( fp, "# TO MAKE A RED TEST GREEN DELETES THE CHECK -- see the oracle's header.\n" );
	fprintf( fp, "#\n" );
	fprintf( fp, "# weapon<TAB>field<TAB>value\n" );

	for ( ScriptMap::const_iterator w = answers.begin(); w != answers.end(); ++w )
	{
		for ( FieldMap::const_iterator f = w->second.begin(); f != w->second.end(); ++f )
			fprintf( fp, "%s\t%s\t%s\n", w->first.c_str(), f->first.c_str(), f->second.c_str() );
	}

	fclose( fp );
}

//-----------------------------------------------------------------------------
// A fixture, so the parse runs once and every fact reads the same answers.
//-----------------------------------------------------------------------------
class WeaponScriptOracle : public ::testing::Test
{
protected:
	static ScriptMap s_native;
	static ScriptMap s_golden;

	static void SetUpTestCase()
	{
		s_native = NativeAnswers();
		MaybeWriteGolden( s_native );

		bool ok = false;
		std::string text = ReadFile( kGoldenPath, &ok );

		if ( ok )
			s_golden = ParseGolden( text );
	}

	// The value both sides must agree on, or "" with a failure recorded.
	static std::string Golden( const std::string &weapon, const std::string &field )
	{
		ScriptMap::const_iterator w = s_golden.find( weapon );

		if ( w == s_golden.end() )
			return "";

		FieldMap::const_iterator f = w->second.find( field );
		return f == w->second.end() ? "" : f->second;
	}

	static std::string Native( const std::string &weapon, const std::string &field )
	{
		ScriptMap::const_iterator w = s_native.find( weapon );

		if ( w == s_native.end() )
			return "";

		FieldMap::const_iterator f = w->second.find( field );
		return f == w->second.end() ? "" : f->second;
	}

	// One field, across every weapon, compared both ways.
	void ExpectFieldAgrees( const char *pField )
	{
		for ( int i = 0; i < kWeaponCount; i++ )
		{
			const char *w = kWeapons[i];

			EXPECT_EQ( Golden( w, pField ), Native( w, pField ) )
				<< w << '.' << pField << ": the REAL FileWeaponInfo_t::Parse and "
				<< "the golden table disagree. Either weapon_parse.cpp changed or " << kGoldenPath
				<< " is stale -- do NOT regenerate to make this pass "
				<< "without establishing which.";
		}
	}
};

ScriptMap WeaponScriptOracle::s_native;
ScriptMap WeaponScriptOracle::s_golden;

//-----------------------------------------------------------------------------
// The anchors. Every fact below reads these two maps, so a fact that passed
// because both sides were EMPTY would be the check that cannot fail. These
// two stop that.
//-----------------------------------------------------------------------------

TEST_F( WeaponScriptOracle, TheRealParseAnsweredForEveryShippedScript )
{
	ASSERT_EQ( (size_t)kWeaponCount, s_native.size() )
		<< "the real parse did not answer for every script in kWeapons. A script "
		<< "that cannot be opened is skipped, so this is either a missing file "
		<< "under " << kScriptDir << " or a run from the wrong directory -- "
		<< "buildcpptests runs these from src/.";

	for ( int i = 0; i < kWeaponCount; i++ )
	{
		EXPECT_FALSE( s_native[kWeapons[i]].empty() ) << kWeapons[i] << " parsed to no fields at all";
	}
}

TEST_F( WeaponScriptOracle, TheGoldenTableIsPresentAndCoversEveryScript )
{
	ASSERT_FALSE( s_golden.empty() ) << kGoldenPath << " could not be read or holds no rows. Every comparison "
									 << "below would then be against an empty map and would pass whatever the "
									 << "parse said.";

	ASSERT_EQ( (size_t)kWeaponCount, s_golden.size() ) << "the golden table does not cover every shipped script.";

	// Not just present -- SUBSTANTIAL. A golden holding one field per weapon
	// would satisfy the count above and check almost nothing.
	for ( int i = 0; i < kWeaponCount; i++ )
	{
		EXPECT_GE( s_golden[kWeapons[i]].size(), (size_t)21 )
			<< kWeapons[i] << " has fewer golden fields than the parse always "
			<< "produces (21 non-sound fields), so some were dropped.";
	}
}

//-----------------------------------------------------------------------------
// One behaviour per fact, which is what the brief asks for instead of a bulk
// assertion dump.
//-----------------------------------------------------------------------------

TEST_F( WeaponScriptOracle, TheSoundCategoryNamesAreLiteralAndInOrder )
{
	// A MUTATION FOUND THIS GAP. SoundsAgree below builds its golden keys FROM
	// pWeaponSoundCategories, so renaming a category renames the key on both
	// sides at once and the comparison stays green -- a check that cannot fail,
	// in the test written to stop exactly that. Measured: mutating
	// "single_shot_npc" to "single_shot_npcX" in weapon_parse.cpp left all 15
	// facts passing.
	//
	// So the names are pinned here as LITERALS, in order, independently of the
	// array they are checking. This is the fact that makes SoundsAgree mean
	// something, and it is also what pins WeaponScriptSound's numbering in
	// SourceSharp.Shared/WeaponScript.cs: the managed enum is the index into
	// this list.
	extern const char *pWeaponSoundCategories[NUM_SHOOT_SOUND_TYPES];

	static const char *const kExpected[] = {
		"empty", // EMPTY            = 0
		"single_shot", // SINGLE           = 1
		"single_shot_npc", // SINGLE_NPC       = 2
		"double_shot", // WPN_DOUBLE       = 3
		"double_shot_npc", // DOUBLE_NPC       = 4
		"burst", // BURST            = 5
		"reload", // RELOAD           = 6
		"reload_npc", // RELOAD_NPC       = 7
		"melee_miss", // MELEE_MISS       = 8
		"melee_hit", // MELEE_HIT        = 9
		"melee_hit_world", // MELEE_HIT_WORLD  = 10
		"special1", // SPECIAL1         = 11
		"special2", // SPECIAL2         = 12
		"special3", // SPECIAL3         = 13
		"taunt", // TAUNT            = 14
		"deploy", // DEPLOY           = 15
	};

	ASSERT_EQ( (int)( sizeof( kExpected ) / sizeof( kExpected[0] ) ), (int)NUM_SHOOT_SOUND_TYPES )
		<< "a sound category was added or removed in weapon_parse.h. The managed "
		<< "WeaponScriptSound enum and WeaponScriptSounds.Categories must follow, "
		<< "and every weapon's sounds shift if the ORDER changed.";

	for ( int i = 0; i < NUM_SHOOT_SOUND_TYPES; i++ )
	{
		EXPECT_STREQ( kExpected[i], pWeaponSoundCategories[i] )
			<< "category " << i << " is not the name the weapon scripts use.";
	}
}

TEST_F( WeaponScriptOracle, ClipSizesAgree )
{
	ExpectFieldAgrees( "maxclip1" );
	ExpectFieldAgrees( "maxclip2" );
}

TEST_F( WeaponScriptOracle, DefaultClipsAgree )
{
	ExpectFieldAgrees( "defaultclip1" );
	ExpectFieldAgrees( "defaultclip2" );
}

TEST_F( WeaponScriptOracle, BucketsAgree )
{
	ExpectFieldAgrees( "slot" );
	ExpectFieldAgrees( "position" );
}

TEST_F( WeaponScriptOracle, SoundsAgree )
{
	extern const char *pWeaponSoundCategories[NUM_SHOOT_SOUND_TYPES];

	for ( int i = 0; i < NUM_SHOOT_SOUND_TYPES; i++ )
		ExpectFieldAgrees( ( std::string( "sound." ) + pWeaponSoundCategories[i] ).c_str() );
}

TEST_F( WeaponScriptOracle, AmmoNamesAgree )
{
	ExpectFieldAgrees( "ammo1" );
	ExpectFieldAgrees( "ammo2" );
}

TEST_F( WeaponScriptOracle, FlagsAndWeightAgree )
{
	ExpectFieldAgrees( "flags" );
	ExpectFieldAgrees( "weight" );
	ExpectFieldAgrees( "rumble" );
}

TEST_F( WeaponScriptOracle, ModelsAndNamesAgree )
{
	ExpectFieldAgrees( "classname" );
	ExpectFieldAgrees( "printname" );
	ExpectFieldAgrees( "viewmodel" );
	ExpectFieldAgrees( "worldmodel" );
	ExpectFieldAgrees( "animprefix" );
}

TEST_F( WeaponScriptOracle, SwitchingAndHandednessAgree )
{
	ExpectFieldAgrees( "autoswitchto" );
	ExpectFieldAgrees( "autoswitchfrom" );
	ExpectFieldAgrees( "melee" );
	ExpectFieldAgrees( "builtrighthanded" );
	ExpectFieldAgrees( "allowflipping" );
	ExpectFieldAgrees( "showusagehint" );
}

//-----------------------------------------------------------------------------
// The behaviours the corpus happens to exercise, asserted on the NATIVE side
// directly so that the rule -- not just the recorded value -- is pinned. The
// managed suite asserts the same four.
//-----------------------------------------------------------------------------

TEST_F( WeaponScriptOracle, ACrowbarHasNoClipAndTheDefaultFollowsIt )
{
	// clip_size "-1" in the script; default_clip is absent and defaults TO it.
	EXPECT_EQ( "-1", Native( "weapon_crowbar", "maxclip1" ) );
	EXPECT_EQ( "-1", Native( "weapon_crowbar", "defaultclip1" ) );
}

TEST_F( WeaponScriptOracle, APistolsDefaultClipFollowsItsClipSize )
{
	// clip_size "18", no default_clip key: stock passes iMaxClip1 as the
	// default, so the gun spawns full.
	EXPECT_EQ( "18", Native( "weapon_pistol", "maxclip1" ) );
	EXPECT_EQ( "18", Native( "weapon_pistol", "defaultclip1" ) );
}

TEST_F( WeaponScriptOracle, ADefaultClipMayExceedTheClipSizeAndIsNotClamped )
{
	// weapon_ar2: clip_size 30, default_clip 60. Stock reads the two keys
	// independently and clamps neither, so the AR2 spawns holding twice its
	// clip. A port that "fixed" this would change what the game does.
	EXPECT_EQ( "30", Native( "weapon_ar2", "maxclip1" ) );
	EXPECT_EQ( "60", Native( "weapon_ar2", "defaultclip1" ) );
}

TEST_F( WeaponScriptOracle, NoneAmmoBecomesTheEmptyString )
{
	// "secondary_ammo" "None" -- the sentinel, not an ammo type called None.
	EXPECT_EQ( "", Native( "weapon_pistol", "ammo2" ) );
	EXPECT_EQ( "Pistol", Native( "weapon_pistol", "ammo1" ) );
}

TEST_F( WeaponScriptOracle, AnAbsentSecondaryClipIsNoClipAndAZeroOneIsZero )
{
	// THIS FACT WAS WRONG TWICE, and the oracle is why it is right now. It
	// first asserted that no shipped script carries clip2_size at all, so every
	// weapon takes WEAPON_NOCLIP; six of the thirteen carry one. Corrected to
	// "and they are all 0", which was wrong again -- only physcannon is 0 and
	// the other five write -1 explicitly. Both errors came from generalising
	// off one file, and a corpus-wide sweep would have hidden the second the
	// same way it hid the first.
	//
	// What is actually worth pinning is that an ABSENT key and an explicit
	// "-1" reach the same value by different routes, and that "0" is a real,
	// distinct answer rather than a parse failure.
	EXPECT_EQ( "-1", Native( "weapon_pistol", "maxclip2" ) ); // absent
	EXPECT_EQ( "-1", Native( "weapon_crowbar", "maxclip2" ) ); // absent
	EXPECT_EQ( "-1", Native( "weapon_ar2", "maxclip2" ) ); // explicit -1
	EXPECT_EQ( "0", Native( "weapon_physcannon", "maxclip2" ) ); // explicit 0
}
