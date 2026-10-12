//========= SourceSharp — shard T3's .cpp arms, read as source ===============//
//
// A SOURCE SCANNER, in cpp_alias_lane1_test / cpp_alias_shard2_test's idiom and
// for their reason: every claim here is about the TEXT of files that need a
// whole engine to run, so the text is what is read.
//
// WHAT SHARD T3 DID. Route step 5's `.cpp` half, shard T3 of lane T
// (docs/typedef-dryrun/route/s5_shards.json): 176 armed sites in twelve files.
// Three claims in it are load-bearing and NOTHING IN THE BUILD NOTICES ANY OF
// THEM ROTTING BACK, because they are all about the disarmed tree's text:
//
//   1. THE SUB-OBJECT ARGUMENT. collisionproperty.cpp and particle_property.cpp
//      lose 93 of those 176 sites to one arm each, and the arm is only
//      justified because CCollisionProperty and CParticleProperty are
//      CBaseEntity's OWN sub-objects — `CNetworkVarEmbedded` members reached
//      only through CollisionProp() / ParticleProp(). What must not drift is
//      WHAT IS OUTSIDE those arms: UpdateDirtySpatialPartitionEntities is
//      called by name from particlemgr.cpp and that file stays in the build, so
//      guarding it would turn a compile error at step 6 into a LINK error, and
//      the CDirtySpatialPartitionEntityList read-lock pair is an
//      IPartitionQueryCallback the ENGINE calls and has to keep pairing. Both
//      are one careless brace move away from being swallowed.
//
//   2. EVERY GUARD MUST BE DEFEATABLE. survey_arm_alias.py's negative control
//      rewrites `#ifndef SS_CBASEENTITY_ALIAS` as a whole line and NOTHING
//      ELSE (GUARD_RE, `^([ \t]*)#ifndef SS_CBASEENTITY_ALIAS\b(?=[ \t]*$)`).
//      A compound `#if defined( CLIENT_DLL ) && !defined( SS_CBASEENTITY_ALIAS )`
//      compiles identically, guards identically, and `defeat` cannot reach it —
//      so the site stays hidden in the control run and the control quietly
//      stops being one. That spelling was written in this shard once and caught
//      before it landed; this is what stops it coming back.
//
//   3. TWO ARMS ARE COPIES OF AN ANSWER THAT LIVES SOMEWHERE ELSE, and a copy
//      with no compiler between it and its original is exactly what
//      docs/typedef-dryrun says to assert rather than assume. The server
//      CStandaloneMoveHelper::IsWorldEntity's armed arm must stay the line its
//      CLIENT TWIN already is.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given, and this tree has shipped several. The fixtures at
// the bottom drive the guard-region walker over SYNTHETIC input where the
// answer is known, including the compound spelling it exists to reject, and
// assert that every real file was opened and is not trivial.
//
// Comments are stripped before any match, for the reason cpp_alias_shard2_test
// records: every arm in this shard carries prose NAMING the member it stopped
// calling, so a raw-text scan would read the name straight out of the sentence
// saying it is gone. The GUARD WALK, by contrast, runs on RAW text — it reads
// preprocessor directives, and stripping comments does not move those, while
// stripping them first would let a `//`-commented `#endif` disappear silently.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <cctype>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//-----------------------------------------------------------------------------
const char *SourceRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		const std::string probe = std::string( kCandidates[ i ] ) + "/game/server";
		DIR *pDir = opendir( probe.c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadFile( const std::string &strRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
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
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' )
				i++;
			if ( i < strIn.size() )
				out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string CodeOf( const std::string &strRelative )
{
	return StripComments( ReadFile( strRelative ) );
}

bool Has( const std::string &strHay, const std::string &strNeedle )
{
	return strHay.find( strNeedle ) != std::string::npos;
}

std::string Trim( const std::string &strIn )
{
	size_t a = 0;
	while ( a < strIn.size() && ( strIn[ a ] == ' ' || strIn[ a ] == '\t' || strIn[ a ] == '\r' ) )
		a++;
	size_t b = strIn.size();
	while ( b > a && ( strIn[ b - 1 ] == ' ' || strIn[ b - 1 ] == '\t' || strIn[ b - 1 ] == '\r' ) )
		b--;
	return strIn.substr( a, b - a );
}

//=============================================================================
// THE GUARD WALK.
//
// Which arm of the alias each line of a file is in. Not proximity to a
// `#ifndef` — a scan that looked for the directive "near" a line would pass on
// a file where the `#endif` moved above it, which is precisely the drift claim
// 1 is about. This tracks the preprocessor nesting.
//
// Three answers, and the middle one is the one with teeth:
//
//   ARM_ALWAYS  compiled in both configurations
//   ARM_SDK     inside a live `#ifndef SS_CBASEENTITY_ALIAS` branch — dropped
//               when the alias is armed
//   ARM_ALIAS   inside the `#else` of one — compiled ONLY when it is armed
//
// Nesting is a stack, so a `#ifdef CLIENT_DLL` wrapped around an SS guard, or
// the other way round, is read correctly rather than by luck.
//=============================================================================
enum Arm_t
{
	ARM_ALWAYS,
	ARM_SDK,
	ARM_ALIAS,
};

struct Frame_t
{
	bool bIsAliasGuard;   // this #if is `#ifndef SS_CBASEENTITY_ALIAS`
	bool bInElse;         // we have passed its #else
};

// The EXACT spelling survey_arm_alias.py's negative control rewrites: optional
// leading whitespace, the directive, and nothing else on the line.
bool IsDefeatableGuardLine( const std::string &strLine )
{
	return Trim( strLine ) == "#ifndef SS_CBASEENTITY_ALIAS";
}

// Any OTHER preprocessor condition that mentions the macro. Every one of these
// is a guard `defeat` cannot reach.
bool IsUndefeatableGuardLine( const std::string &strLine )
{
	const std::string t = Trim( strLine );
	if ( t.size() < 3 || t[ 0 ] != '#' )
		return false;
	if ( IsDefeatableGuardLine( t ) )
		return false;
	// #ifdef / #ifndef / #if / #elif that names the macro.
	const bool bIsCondition =
		t.compare( 0, 4, "#if " ) == 0 || t.compare( 0, 4, "#if(" ) == 0 ||
		t.compare( 0, 6, "#ifdef" ) == 0 || t.compare( 0, 7, "#ifndef" ) == 0 ||
		t.compare( 0, 5, "#elif" ) == 0;
	if ( !bIsCondition )
		return false;
	// SS_CBASEENTITY_ALIAS_H is the alias header's own include guard and is not
	// one of these; require the name to end there.
	const size_t p = t.find( "SS_CBASEENTITY_ALIAS" );
	if ( p == std::string::npos )
		return false;
	const size_t after = p + sizeof( "SS_CBASEENTITY_ALIAS" ) - 1;
	if ( after < t.size() && ( t[ after ] == '_' || isalnum( t[ after ] ) ) )
		return false;
	return true;
}

void SplitLines( const std::string &strIn, std::vector< std::string > *pOut )
{
	std::string cur;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( strIn[ i ] == '\n' )
		{
			pOut->push_back( cur );
			cur.clear();
			continue;
		}
		cur += strIn[ i ];
	}
	pOut->push_back( cur );
}

// Per-line arm, for the RAW text of a file.
void WalkArms( const std::string &strRaw, std::vector< std::string > *pLines,
               std::vector< Arm_t > *pArms )
{
	pLines->clear();
	pArms->clear();
	SplitLines( strRaw, pLines );

	std::vector< Frame_t > stack;
	for ( size_t i = 0; i < pLines->size(); i++ )
	{
		const std::string t = Trim( ( *pLines )[ i ] );
		const bool bIsIf =
			t.compare( 0, 3, "#if" ) == 0;
		const bool bIsElse = ( t == "#else" ) || t.compare( 0, 5, "#elif" ) == 0;
		const bool bIsEndif = t.compare( 0, 6, "#endif" ) == 0;

		if ( bIsElse && !stack.empty() )
			stack.back().bInElse = true;

		if ( bIsEndif && !stack.empty() )
			stack.pop_back();

		// The arm of the DIRECTIVE line itself is whatever encloses it, which is
		// what the frame stack says after the pops above and before the push
		// below.
		Arm_t arm = ARM_ALWAYS;
		for ( size_t f = 0; f < stack.size(); f++ )
		{
			if ( !stack[ f ].bIsAliasGuard )
				continue;
			arm = stack[ f ].bInElse ? ARM_ALIAS : ARM_SDK;
			break;
		}
		pArms->push_back( arm );

		if ( bIsIf && !bIsElse && !bIsEndif )
		{
			Frame_t frame;
			frame.bIsAliasGuard = IsDefeatableGuardLine( t );
			frame.bInElse = false;
			stack.push_back( frame );
		}
	}
}

// The arm the first line CONTAINING a needle is in. ARM_ALWAYS with pbFound
// false when the needle is not in the file at all, which every assertion below
// reads as a failure rather than a pass.
Arm_t ArmOfFirst( const std::string &strRelative, const std::string &strNeedle,
                  bool *pbFound )
{
	std::vector< std::string > lines;
	std::vector< Arm_t > arms;
	WalkArms( ReadFile( strRelative ), &lines, &arms );

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].find( strNeedle ) != std::string::npos )
		{
			*pbFound = true;
			return arms[ i ];
		}
	}
	*pbFound = false;
	return ARM_ALWAYS;
}

//-----------------------------------------------------------------------------
// THE FILES. Shard T3's twelve, named once.
//-----------------------------------------------------------------------------
const char *kCollision   = "sourcesharp/game/shared/collisionproperty.cpp";
const char *kParticleMgr = "game/client/particlemgr.cpp";
// The Phase 4 exit's cluster-E lane (p4-util) moved every definition
// game/shared/takedamageinfo.cpp held into the shim, WHOLE -- the value type,
// its datadesc, the CMultiDamage accumulator and the damage-force calculators.
// plan_shim.md Phase 1d then evicted the emptied stock TU from both vpcs, so
// it is not compiled on either side any more; the file is still on disk and
// still listed by server_base.vpc and client_base.vpc. The CLAIMS below are
// unchanged: they are about the answer GetAmmoName gives on each side, not
// about which file holds it. That the old address kept no copy is
// TakeDamageInfoLibrary's NothingWasLeftBehindInTakeDamageInfoCpp,
// mutation-verified there.
const char *kTakeDamage  = "sourcesharp/game/shared/takedamageinfo_library.cpp";
const char *kShellBase   = "sourcesharp/game/server/shell_standalone_base.cpp";
const char *kShellPlayerS= "sourcesharp/game/server/shell_standalone_player.cpp";
// ss_ground is a diagnostic and is compiled only into a debug build.
const char *kShellPlayerDebugS = "sourcesharp/game/server/shell_standalone_player_debug.cpp";
// plan_entity.md step 6f deleted the client player class; CStandaloneMoveHelperClient
// moved to shell_prediction.cpp with the replay it serves. The CLAIM below is
// unchanged -- it is about the answer that helper gives, not about which file
// holds it.
const char *kShellPlayerC= "sourcesharp/game/client/shell_prediction.cpp";

// kProxy IS DELIBERATELY NOT IN THIS LIST ANY MORE — lane c7-proxies, and the
// removal is the instruction the failing message below asks for ("if an arm was
// removed on purpose, remove it from kAllEdited too").
//
// animatedoffsettextureproxy.cpp's SS_CBASEENTITY_ALIAS arm guarded
//
//     C_BaseEntity* pEntity = (C_BaseEntity*)pBaseEntity;
//     m_flFrameOffset = pEntity->GetTextureAnimationStartTime();
//
// which is a WILD POINTER, not merely an entity ask: CBaseAnimatedTextureProxy
// derives from IMaterialProxy, so OnBind's argument is an IClientRenderable *,
// and C_BaseEntity's IClientRenderable subobject is its second base and not at
// offset zero (icliententity.h:31). c7-proxies replaced the SourceSharp side
// with a proper resolve through materialproxy_facts.h and left the stock arm
// carrying stock's own bug, so the guard is now `#ifdef SOURCESHARP`.
//
// THAT IS STRICTLY STRONGER THAN THE GUARD IT REPLACED: `#ifndef
// SS_CBASEENTITY_ALIAS` excludes the C_BaseEntity spelling only once the alias
// is armed, `#ifdef SOURCESHARP` excludes it in every SourceSharp build, and
// the alias is only ever defined in one of those. So there is nothing left in
// this file for the flip to change, and the row would now be asserting that a
// solved file still carries an unsolved file's marker.
//
// It stayed in the FIXTURE list below, so the file was still opened and still
// proved non-trivial, until lane c5-proxies evicted it from client.so: every
// material proxy is C# now (SourceSharp.Client.MaterialProxies, replayed
// against stock by MaterialProxyOracleTests), and the farm no longer carries
// the file, so its fixture row went with it.
//
// particle_property.cpp, gamerules.cpp and fx_impact.cpp -- the three rows that
// still carried a defeatable guard -- are compiled by neither client.so nor
// server.so and left this list when the farm stopped carrying unused stock
// files. Every row left is one the `residue` wave or `the-close` cleared (see
// below), so this list is now also the cleared set.
const char *kAllEdited[] = {
	kCollision, kParticleMgr, kTakeDamage, kShellBase, kShellPlayerS,
};

//-----------------------------------------------------------------------------
// THE TWO FILES THE `residue` WAVE CLEARED, AND WHY THEIR ROWS STAY.
//
// The `residue` wave (74b071771..a46e8d645) deleted 5,282 lines of DEAD guarded
// arms out of 44 compiled stock TUs. Two of them are shard T3's, and every arm
// it took from them was a `#ifndef SS_CBASEENTITY_ALIAS` WITH NO `#else`:
//
//   game/shared/collisionproperty.cpp
//       74b071771:src/game/shared/collisionproperty.cpp:202  AddEntity's push
//       74b071771:src/game/shared/collisionproperty.cpp:240  the drain's
//                                                            client resolve
//       74b071771:src/game/shared/collisionproperty.cpp:288  the drain's body
//       74b071771:src/game/shared/collisionproperty.cpp:421  the WHOLE
//           out-of-line half of CCollisionProperty, closing at :1647 --
//           1,227 lines carrying DT_CollisionProperty, SetSolid,
//           UpdatePartition, GetCollisionOrigin (:816) and
//           GetCollisionAngles (:821)
//
//   game/client/particlemgr.cpp
//       74b071771:src/game/client/particlemgr.cpp:1434  the C_BaseEntityIterator
//           sweep in CParticleMgr::RemoveAllNewEffects
//
// THE ARMED CONFIGURATION IS SS_CBASEENTITY_ALIAS AND SOURCESHARP BOTH DEFINED
// and it is the only configuration this project builds, so not one line of any
// of those five regions was EVER COMPILED -- which is why the wave moved no
// symbol: 588 objects, identical DEFINED and UNDEFINED sets, 0 differing.
//
// THE ROWS STAY, AT ZERO. A file leaving kAllEdited is a claim nobody makes
// again; a row reading zero is a live assertion that fails the day a guard
// comes back into a file whose whole alias question is settled. The successor
// property -- "there are none, and their contents went with them" -- is
// asserted in TheResidueWaveTookTheDeadArmsWhole below, which is a STRONGER
// statement than the one the old rows made and is mutation-checked in both
// directions.
//-----------------------------------------------------------------------------
// THREE MORE CLEARED BY `the-close` 1/N (f288a5e11), AND THE ARRAY'S NAME IS
// NOW HISTORY RATHER THAN DESCRIPTION -- READ THE ROWS, NOT THE NAME. That
// commit is the same partial evaluation of the preprocessor applied to
// src/sourcesharp/**, which `residue` said in as many words it did not own.
// Both vpcs compile the shim with SS_CBASEENTITY_ALIAS and SOURCESHARP defined
// and this project builds no other configuration, so every arm it took was one
// the preprocessor was already discarding: 588 objects before and after,
// identical DEFINED and UNDEFINED symbol sets, 0 differing.
//
//   sourcesharp/game/shared/takedamageinfo_library.cpp
//       GetAmmoName's `m_hInflictor->GetClassname()` arm, and the
//       `STRING( m_hInflictor->GetModelName() )` arm inside the
//       "prop_physics" branch below it. Both are now `#ifdef CLIENT_DLL`
//       splits -- which is a DIFFERENT and narrower question than the alias
//       one, and the reason this file can be alias-clear and still have two
//       arms in that function.
//
//   sourcesharp/game/server/shell_standalone_base.cpp
//       TWELVE arms, every one an `if ( pBase ) return pBase->Something();`
//       fall-through in SourceSharpEntity_t's accessors, plus the
//       `ent.pBase = pUnknown->GetBaseEntity();` that filled pBase in the
//       first place. GetBaseEntity() is `mov $0x0,%eax; ret` in server.so, so
//       pBase was NULL for the life of every armed process and none of the
//       twelve was ever reached.
//
//   sourcesharp/game/server/shell_standalone_player.cpp
//       IsWorldEntity's `handle == CBaseEntity::Instance( 0 )` arm -- and
//       CBaseEntity::Instance is itself deleted now (`the-close` 4/N), so
//       there is no function left for that arm to have called -- and
//       ss_ground's `pBase->GetClassname()` classname pick.
//
// THEIR ROWS STAY, AT ZERO, for the reason the first two rows give: a file
// leaving kAllEdited is a claim nobody makes again, and a row reading zero is a
// live assertion that fails the day a guard comes back. The successor
// properties are in TheCloseWaveTookTheShimsDeadArmsWhole below, and the three
// per-site facts further down were re-pointed at the live answers rather than
// retired.
// How many `#ifndef SS_CBASEENTITY_ALIAS` regions a file opens, counted off the
// RAW text through the same walker every other claim here uses.
int GuardCount( const char *pszFile )
{
	std::vector< std::string > lines;
	std::vector< Arm_t > arms;
	WalkArms( ReadFile( pszFile ), &lines, &arms );

	int n = 0;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( IsDefeatableGuardLine( lines[ i ] ) )
			n++;
	}
	return n;
}

} // namespace


//=============================================================================
// CLAIM 2 FIRST, because it is the one that protects the measurement.
//
// PROVEN RED by rewriting collisionproperty.cpp's OnPreQuery guard as
// `#if defined( CLIENT_DLL ) && !defined( SS_CBASEENTITY_ALIAS )` — which is
// the spelling this shard actually wrote before catching it. The test named the
// file and the line.
//=============================================================================
TEST( CppAliasShardT3, EveryAliasGuardIsTheSpellingTheNegativeControlRewrites )
{
	for ( size_t f = 0; f < sizeof( kAllEdited ) / sizeof( kAllEdited[ 0 ] ); f++ )
	{
		std::vector< std::string > lines;
		std::vector< Arm_t > arms;
		const std::string raw = ReadFile( kAllEdited[ f ] );
		ASSERT_FALSE( raw.empty() ) << kAllEdited[ f ] << " did not open.";
		WalkArms( raw, &lines, &arms );

		for ( size_t i = 0; i < lines.size(); i++ )
		{
			EXPECT_FALSE( IsUndefeatableGuardLine( lines[ i ] ) )
				<< kAllEdited[ f ] << ":" << ( i + 1 )
				<< " names SS_CBASEENTITY_ALIAS in a condition survey_arm_alias.py's"
				   " `defeat` cannot rewrite:\n    " << lines[ i ]
				<< "\nGUARD_RE matches `#ifndef SS_CBASEENTITY_ALIAS` as a WHOLE"
				   " LINE. Nest the guard instead of folding it into a compound"
				   " condition, or the negative control stops being one.";
		}
	}
}

//=============================================================================
// THE SUCCESSOR PROPERTY, and it is stronger than the rows it replaces.
//
// "Every entity ask in this file is inside the alias arm" is true of a file
// with no asks and no arms, so it passes VACUOUSLY now. What is actually true
// of collisionproperty.cpp and particlemgr.cpp after the `residue` wave is
// "THERE ARE NONE, AND THE BODIES WENT WITH THE GUARDS", and that is what this
// asserts -- each absence paired with a POSITIVE anchor out of the same file,
// so none of it can be green because the file failed to open.
//=============================================================================
TEST( CppAliasShardT3, TheResidueWaveTookTheDeadArmsWhole )
{
	//-------------------------------------------------------------------------
	// collisionproperty.cpp. What LEFT is the whole out-of-line half of
	// CCollisionProperty (74b071771:...:421-1647) and the three entity asks in
	// the dirty-partition list (:202, :240, :288). What STAYED is
	// CDirtySpatialPartitionEntityList itself, which is an
	// IPartitionQueryCallback the ENGINE calls.
	//-------------------------------------------------------------------------
	const std::string strCollision = CodeOf( kCollision );
	ASSERT_FALSE( strCollision.empty() ) << kCollision << " did not open.";

	// POSITIVE FIRST: the file is still the real file and still carries the
	// parts that were never behind an arm.
	ASSERT_TRUE( Has( strCollision, "CDirtySpatialPartitionEntityList" ) )
		<< kCollision << " no longer names the partition list at all; the"
		   " absences below would prove nothing.";
	EXPECT_TRUE( Has( strCollision, "void UpdateDirtySpatialPartitionEntities()" ) )
		<< kCollision << " no longer defines UpdateDirtySpatialPartitionEntities,"
		   " which particlemgr.cpp calls by name.";
	EXPECT_TRUE( Has( strCollision, "m_DirtyEntities.PushItem(" ) )
		<< kCollision << " no longer re-pushes the still-dirty handles. That push"
		   " was ARM_ALWAYS at 74b071771 too (:338) and is not the one the wave"
		   " took (:203, which was inside the arm at :202).";

	EXPECT_EQ( 0, GuardCount( kCollision ) )
		<< kCollision << " carries a `#ifndef SS_CBASEENTITY_ALIAS` again.";

	// ...AND THE ABSENCES. Every one of these was inside a never-compiled arm.
	EXPECT_FALSE( Has( strCollision, "BEGIN_NETWORK_TABLE_NOBASE( CCollisionProperty" ) )
		<< kCollision << " defines DT_CollisionProperty again (74b071771:...:537,"
		   " inside the arm at :421).";
	EXPECT_FALSE( Has( strCollision, "void CCollisionProperty::SetSolid(" ) )
		<< kCollision << " defines CCollisionProperty::SetSolid again"
		   " (74b071771:...:710, inside the arm at :421).";
	EXPECT_FALSE( Has( strCollision, "void CCollisionProperty::UpdatePartition(" ) )
		<< kCollision << " defines CCollisionProperty::UpdatePartition again"
		   " (74b071771:...:1602, inside the arm at :421).";
	EXPECT_FALSE( Has( strCollision, "->CollisionProp()->UpdatePartition();" ) )
		<< kCollision << " asks an entity for its collision property again"
		   " (74b071771:...:324, inside the arm at :288).";
	EXPECT_FALSE( Has( strCollision, "pEntity->GetRefEHandle()" ) )
		<< kCollision << " pushes an entity handle again (74b071771:...:203,"
		   " inside the arm at :202). AddEntity's whole body was that push.";

	//-------------------------------------------------------------------------
	// particlemgr.cpp. One arm, one sweep: a C_BaseEntityIterator walk asking
	// every entity for its ParticleProp, at 74b071771:...:1434.
	//-------------------------------------------------------------------------
	const std::string strMgr = CodeOf( kParticleMgr );
	ASSERT_FALSE( strMgr.empty() ) << kParticleMgr << " did not open.";

	ASSERT_TRUE( Has( strMgr, "UpdateDirtySpatialPartitionEntities();" ) )
		<< kParticleMgr << " no longer calls UpdateDirtySpatialPartitionEntities;"
		   " the absences below would prove nothing.";
	EXPECT_EQ( 0, GuardCount( kParticleMgr ) )
		<< kParticleMgr << " carries a `#ifndef SS_CBASEENTITY_ALIAS` again.";
	EXPECT_FALSE( Has( strMgr, "C_BaseEntityIterator" ) )
		<< kParticleMgr << " walks the entity list again (74b071771:...:1434,"
		   " inside an arm with no #else, so it was never compiled).";
	EXPECT_FALSE( Has( strMgr, "ParticleProp()" ) )
		<< kParticleMgr << " asks an entity for its particle property again.";
}


//=============================================================================
// THE SAME CLAIM FOR THE THREE FILES `the-close` 1/N (f288a5e11) CLEARED.
//
// Written as its own fact rather than folded into the one above, because the
// two waves are two events: `residue` owned the stock tree and this one owned
// src/sourcesharp/**, and a single fact covering both could not say which wave
// a regression came from. Same shape either way -- every absence paired with a
// POSITIVE anchor out of the same file, so none of it can be green because the
// file failed to open.
//=============================================================================
TEST( CppAliasShardT3, TheCloseWaveTookTheShimsDeadArmsWhole )
{
	//-------------------------------------------------------------------------
	// shell_standalone_base.cpp. TWELVE arms, one shape: `if ( pBase ) return
	// pBase->Something();` in SourceSharpEntity_t's accessors, reached only
	// when the resolver had not answered on pStandalone -- plus the assignment
	// that filled pBase at all. `pUnknown->GetBaseEntity()` is `mov $0x0,%eax;
	// ret` in server.so, so pBase was NULL for every armed call and not one of
	// the twelve was ever taken.
	//-------------------------------------------------------------------------
	const std::string strBase = CodeOf( kShellBase );
	ASSERT_FALSE( strBase.empty() ) << kShellBase << " did not open.";

	// POSITIVE FIRST, and it is the successor as well as the control: every one
	// of the twelve was the `else` of a branch that asked pStandalone, and that
	// branch is the whole answer now.
	ASSERT_TRUE( Has( strBase, "SourceSharpEntity_t SourceSharp_ResolveEntity( uint32_t hEntity )" ) )
		<< kShellBase << " no longer defines the resolver; the absences below"
		   " would prove nothing.";
	EXPECT_TRUE( Has( strBase, "ent.pStandalone" ) )
		<< kShellBase << " no longer resolves onto the standalone hierarchy,"
		   " which is the arm all twelve deleted ones fell through from.";

	EXPECT_EQ( 0, GuardCount( kShellBase ) )
		<< kShellBase << " carries a `#ifndef SS_CBASEENTITY_ALIAS` again.";

	// THE ABSENCE, AS ONE CLAUSE COVERING ALL TWELVE. `pBase->` is the spelling
	// every one of them shared, and it is gone from the code -- prose still
	// discusses pBase at length, which is why this is asked of the stripped
	// text.
	EXPECT_FALSE( Has( strBase, "pBase->" ) )
		<< kShellBase << " dereferences pBase again. SourceSharpEntity_t::pBase"
		   " is filled from IServerUnknown::GetBaseEntity(), which returns a"
		   " constant NULL in server.so, so any such line is unreachable.";
	EXPECT_FALSE( Has( strBase, "ent.pBase = pUnknown->GetBaseEntity();" ) )
		<< kShellBase << " fills pBase again (74b071771 lineage: the resolver's"
		   " `else` arm). The assignment went with the twelve reads.";

	//-------------------------------------------------------------------------
	// takedamageinfo_library.cpp. The two arms were in GetAmmoName, and
	// plan_shim.md §5 3f DELETED THE WHOLE FUNCTION -- 0 importers on either
	// link line and no caller inside its own translation unit.
	//
	// The anchor changes shape rather than going away, and this is the case
	// the anchor rule was written for: an ASSERT_TRUE on
	// `const char *CTakeDamageInfo::GetAmmoName() const` would now fail for the
	// right reason and mean the wrong thing -- "the wave did not take the dead
	// arms" rather than "a later phase deleted the function". So the positive
	// control moves onto a definition this file still carries, and the two
	// absences stay: they were about SPELLINGS, and a spelling can come back in
	// a different function.
	//-------------------------------------------------------------------------
	const std::string strTake = CodeOf( kTakeDamage );
	ASSERT_FALSE( strTake.empty() ) << kTakeDamage << " did not open.";

	// (GuessDamageForce was the anchor until the server gc cull deleted it.)
	ASSERT_TRUE( Has( strTake, "void CalculateExplosiveDamageForce( CTakeDamageInfo *info" ) )
		<< kTakeDamage << " no longer defines CalculateExplosiveDamageForce; the"
		   " absences below would prove nothing.";
	EXPECT_FALSE( Has( strTake, "const char *CTakeDamageInfo::GetAmmoName() const" ) )
		<< kTakeDamage << " defines GetAmmoName again. Phase 3f deleted it for"
		   " having 0 importers on either link line and no in-TU caller; if it"
		   " has a caller now, the plan is where to say so.";
	EXPECT_EQ( 0, GuardCount( kTakeDamage ) )
		<< kTakeDamage << " carries a `#ifndef SS_CBASEENTITY_ALIAS` again.";
	EXPECT_FALSE( Has( strTake, "m_hInflictor->GetClassname()" ) )
		<< kTakeDamage << " asks the inflictor handle for a classname again."
		   " The handle no longer resolves to something with that member.";
	EXPECT_FALSE( Has( strTake, "STRING( m_hInflictor->GetModelName() )" ) )
		<< kTakeDamage << " asks the inflictor handle for a model name again.";

	//-------------------------------------------------------------------------
	// shell_standalone_player.cpp. IsWorldEntity's Instance( 0 ) arm and
	// ss_ground's classname pick.
	//-------------------------------------------------------------------------
	const std::string strPlayer = CodeOf( kShellPlayerS );
	ASSERT_FALSE( strPlayer.empty() ) << kShellPlayerS << " did not open.";

	ASSERT_TRUE( Has( strPlayer, "IsWorldEntity( const CBaseHandle &handle )" ) )
		<< kShellPlayerS << " no longer defines IsWorldEntity; the absences"
		   " below would prove nothing.";
	EXPECT_EQ( 0, GuardCount( kShellPlayerS ) )
		<< kShellPlayerS << " carries a `#ifndef SS_CBASEENTITY_ALIAS` again.";
	EXPECT_FALSE( Has( strPlayer, "CBaseEntity::Instance" ) )
		<< kShellPlayerS << " names CBaseEntity::Instance again. It is deleted:"
		   " it bottomed out in IServerUnknown::GetBaseEntity(), which is"
		   " `mov $0x0,%eax; ret` in server.so.";

	// ss_ground moved to the debug-only file; its classname pick is pinned there.
	const std::string strPlayerDebug = CodeOf( kShellPlayerDebugS );
	ASSERT_FALSE( strPlayerDebug.empty() ) << kShellPlayerDebugS << " did not open.";

	EXPECT_TRUE( Has( strPlayerDebug, "SS_NATIVE_COMMAND( ss_ground )" ) )
		<< kShellPlayerDebugS << " no longer registers ss_ground, whose classname"
		   " pick is the second of the two arms this wave took here.";
	EXPECT_EQ( 0, GuardCount( kShellPlayerDebugS ) )
		<< kShellPlayerDebugS << " carries a `#ifndef SS_CBASEENTITY_ALIAS` again.";
	EXPECT_FALSE( Has( strPlayerDebug, "CBaseEntity::Instance" ) )
		<< kShellPlayerDebugS << " names CBaseEntity::Instance again.";
	EXPECT_FALSE( Has( strPlayerDebug, "pBase->GetClassname()" ) )
		<< kShellPlayerDebugS << " picks ss_ground's classname off an SDK entity"
		   " again; StandaloneEntity_Name is the whole answer now.";
	EXPECT_TRUE( Has( strPlayerDebug, "pGameEntity ? StandaloneEntity_Name( pGameEntity ) : \"?\"" ) )
		<< kShellPlayerDebugS << " no longer picks ss_ground's classname through"
		   " StandaloneEntity_Name, which is the arm the deleted one fell"
		   " through from.";
}


//=============================================================================
// CLAIM 1 — the sub-object arms, and what must stay OUTSIDE them.
//
// PROVEN RED by moving collisionproperty.cpp's `#ifndef` above the
// UpdateDirtySpatialPartitionEntities definition: the first EXPECT below went
// red naming it, while the whole rest of the suite stayed green and the build
// stayed clean — which is the point, because that move is a link failure at
// step 6 and nothing else in this tree says so.
//=============================================================================
TEST( CppAliasShardT3, TheCollisionPropertyHalfIsBehindTheAliasArm )
{
	// THE ARM WENT, AND THE HALF WENT WITH IT -- `residue` wave,
	// 74b071771..a46e8d645.
	//
	// This fact used to read "SetSolid, DT_CollisionProperty and UpdatePartition
	// are all in ARM_SDK". All three were, inside ONE region:
	// 74b071771:src/game/shared/collisionproperty.cpp:421, closing at :1647,
	// with NO `#else`. The armed configuration is SS_CBASEENTITY_ALIAS and
	// SOURCESHARP both defined and it is the only one this project builds, so
	// those 1,227 lines were never compiled, and the wave deleted them without
	// moving a symbol.
	//
	// THE SUCCESSOR IS STRUCTURAL AND IT IS STRONGER. "The half is behind the
	// arm" was a claim about WHERE those bodies sat; what is true now is that
	// there is nowhere for them to sit: not one line of this file is in an alias
	// arm of either polarity. That is checked first, so the absences after it
	// cannot be satisfied by a body moving into an `#else` nobody read.
	std::vector< std::string > lines;
	std::vector< Arm_t > arms;
	const std::string raw = ReadFile( kCollision );
	ASSERT_FALSE( raw.empty() ) << kCollision << " did not open.";
	WalkArms( raw, &lines, &arms );

	int nSdk = 0, nAlias = 0, nAlways = 0;
	for ( size_t i = 0; i < arms.size(); i++ )
	{
		if ( arms[ i ] == ARM_SDK )        nSdk++;
		else if ( arms[ i ] == ARM_ALIAS ) nAlias++;
		else                               nAlways++;
	}

	// The positive that stops the two counts below being zero because nothing
	// was read: this file is a thousand-odd lines and every one of them is
	// unconditional with respect to the alias.
	EXPECT_GT( nAlways, 200 )
		<< kCollision << " walked to only " << nAlways << " unconditional lines."
		   " The scan read nothing, or the file is a stub, and the zeroes below"
		   " would be free.";
	EXPECT_EQ( 0, nSdk )
		<< kCollision << " has " << nSdk << " lines inside a"
		   " `#ifndef SS_CBASEENTITY_ALIAS` arm. Every arm this file had was"
		   " DEAD -- see the `residue` note under kAllEdited -- so a new one is either dead text"
		   " coming back or a guard round code that does not need one.";
	EXPECT_EQ( 0, nAlias )
		<< kCollision << " has " << nAlias << " lines inside an alias `#else`.";

	// ...and the two bodies this file's whole retype argument rested on are
	// gone rather than relocated. GetCollisionOrigin (:816) and
	// GetCollisionAngles (:821) were both inside the arm at :421; the identity
	// they carried is re-pointed at the port's own shells in
	// sourcesharp/test/collision_origin_identity_test.cpp, which reads
	// shell_standalone.h and shell_standalone_client.h instead.
	const std::string strCode = CodeOf( kCollision );
	ASSERT_FALSE( strCode.empty() ) << kCollision << " did not open.";
	EXPECT_FALSE( Has( strCode, "CCollisionProperty::GetCollisionOrigin" ) )
		<< kCollision << " defines CCollisionProperty::GetCollisionOrigin again"
		   " (74b071771:...:816, inside the arm at :421).";
	EXPECT_FALSE( Has( strCode, "CCollisionProperty::GetCollisionAngles" ) )
		<< kCollision << " defines CCollisionProperty::GetCollisionAngles again"
		   " (74b071771:...:821, inside the arm at :421).";
}

TEST( CppAliasShardT3, UpdateDirtySpatialPartitionEntitiesStaysOutsideTheArm )
{
	// particlemgr.cpp calls this BY NAME and stays in the build, so guarding it
	// turns step 6's compile error into a link error. Both halves are asserted:
	// the definition is unguarded, and the caller is real.
	bool bFound = false;
	EXPECT_EQ( ARM_ALWAYS,
	           ArmOfFirst( kCollision, "void UpdateDirtySpatialPartitionEntities()", &bFound ) );
	EXPECT_TRUE( bFound ) << "UpdateDirtySpatialPartitionEntities' definition was not found in "
	                      << kCollision;

	const std::string strMgr = CodeOf( kParticleMgr );
	ASSERT_FALSE( strMgr.empty() ) << kParticleMgr << " did not open.";
	EXPECT_TRUE( Has( strMgr, "UpdateDirtySpatialPartitionEntities();" ) )
		<< "particlemgr.cpp no longer calls UpdateDirtySpatialPartitionEntities."
		   " If that is deliberate the arm above may move, but say so here.";
}

TEST( CppAliasShardT3, ThePartitionReadLockPairStaysOutsideTheArm )
{
	// OnPreQuery / OnPostQuery are an IPartitionQueryCallback the ENGINE calls,
	// and the lock they take has to keep pairing armed. Only the two parts that
	// touch an SDK entity are guarded: AddEntity's push and the drain's body.
	bool bFound = false;
	EXPECT_EQ( ARM_ALWAYS,
	           ArmOfFirst( kCollision, "void CDirtySpatialPartitionEntityList::OnPreQuery(", &bFound ) );
	EXPECT_TRUE( bFound ) << "OnPreQuery was not found in " << kCollision;

	bFound = false;
	EXPECT_EQ( ARM_ALWAYS,
	           ArmOfFirst( kCollision, "void CDirtySpatialPartitionEntityList::OnPostQuery(", &bFound ) );
	EXPECT_TRUE( bFound ) << "OnPostQuery was not found in " << kCollision;

	bFound = false;
	EXPECT_EQ( ARM_ALWAYS,
	           ArmOfFirst( kCollision, "bool CDirtySpatialPartitionEntityList::Init()", &bFound ) );
	EXPECT_TRUE( bFound ) << "Init (which installs the query callback) was not found in "
	                      << kCollision;

	// ...AND THE SDK-ENTITY HALVES INSIDE THEM ARE GONE, not guarded.
	//
	// This is where the `residue` wave (74b071771..a46e8d645) landed on this
	// fact. Two arms, both `#ifndef SS_CBASEENTITY_ALIAS` with NO `#else`, so
	// neither was ever compiled in the only configuration this project builds:
	//
	//   74b071771:src/game/shared/collisionproperty.cpp:202  AddEntity's whole
	//       body -- `m_DirtyEntities.PushItem( pEntity->GetRefEHandle() )` at
	//       :203, and nothing else
	//   74b071771:src/game/shared/collisionproperty.cpp:288  the drain's body,
	//       whose ask was `pEntity->CollisionProp()->UpdatePartition()` at :324
	//
	// THE PUSH STILL IN THE FILE IS A DIFFERENT PUSH and the distinction is the
	// whole of this half. `m_DirtyEntities.PushItem( vecStillDirty[i] )` puts
	// back the handles the drain could not resolve; it was ARM_ALWAYS at
	// 74b071771 (:338) and it is ARM_ALWAYS now. Asserting it is ARM_ALWAYS is
	// therefore a claim about the LOCK-PAIR's body being unguarded -- the same
	// claim the three checks above make -- and NOT a restatement of the
	// deletion.
	bFound = false;
	EXPECT_EQ( ARM_ALWAYS, ArmOfFirst( kCollision, "m_DirtyEntities.PushItem(", &bFound ) );
	EXPECT_TRUE( bFound )
		<< "the still-dirty re-push was not found in " << kCollision
		<< ". It is inside OnPreQuery's write-locked block and must stay there:"
		   " a handle the drain could not resolve has to go back on the list or"
		   " it is silently dropped.";

	const std::string strCode = CodeOf( kCollision );
	ASSERT_FALSE( strCode.empty() ) << kCollision << " did not open.";

	EXPECT_FALSE( Has( strCode, "->CollisionProp()->UpdatePartition();" ) )
		<< kCollision << " asks a resolved entity for its collision property"
		   " again (74b071771:...:324, inside the never-compiled arm at :288)."
		   " If a real ask has come back it needs a real arm, and this file has"
		   " none -- see the `residue` note under kAllEdited.";
	EXPECT_FALSE( Has( strCode, "m_DirtyEntities.PushItem( pEntity" ) )
		<< kCollision << " pushes an entity's handle again (74b071771:...:203,"
		   " inside the never-compiled arm at :202). AddEntity's body was that"
		   " one line; armed there is no route from an IGameEntity * to a"
		   " handle, because igameentity.h has no IHandleEntity base.";
}

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. There are no SDK arms in this file any
// more, so "every SDK arm is guarded" is no longer a sentence about anything;
// the name is kept for identity, and what it checks is the successor.
//
// WHAT IT USED TO SAY: SourceSharpEntity_t::pBase is NULL for the life of an
// armed process, so every `pBase->` in this file must sit inside a `#ifndef
// SS_CBASEENTITY_ALIAS` arm -- counted line by line, twelve of them, rather
// than spot-checked, so a single unguarded one was a failure.
//
// WHAT HAPPENED: `the-close` 1/N (f288a5e11) deleted all twelve arms, and
// `the-close` 4/N established WHY they were safe to delete beyond the usual
// never-compiled argument: `IServerUnknown::GetBaseEntity()` is
// `mov $0x0,%eax; ret` in server.so, all eight implementations across the two
// DLLs, so pBase was a constant NULL and every one of the twelve was
// unreachable rather than merely unarmed.
//
// WHY THE SUCCESSOR IS STRONGER. The old rule could only catch a `pBase->`
// somebody wrote OUTSIDE an arm; it licensed any number of them inside one.
// The rule now is that the file contains none at all, in any arm, under any
// spelling -- which is a smaller set of permitted programs, and the clause that
// used to need a floor of ten to prove the scan was still matching needs no
// floor at all, because the presence half is asked of the successor instead.
//-----------------------------------------------------------------------------
TEST( CppAliasShardT3, EverySdkArmOfTheStandaloneResolveIsGuarded )
{
	const std::string code = CodeOf( kShellBase );
	ASSERT_FALSE( code.empty() ) << kShellBase << " did not open.";

	// THE PRESENCE HALF, FIRST AND DELIBERATELY. Every clause below is an
	// absence and would pass over a file that failed to open, a shape this tree
	// has shipped more than forty times. These say the accessors the twelve
	// arms lived in are still here and still answer.
	ASSERT_TRUE( Has( code, "SourceSharpEntity_t SourceSharp_ResolveEntity( uint32_t hEntity )" ) )
		<< kShellBase << " no longer defines the resolver.";
	EXPECT_TRUE( Has( code, "const Vector &SourceSharpEntity_t::Origin() const" ) );
	EXPECT_TRUE( Has( code, "void SourceSharpEntity_t::Bounds(" ) );
	EXPECT_TRUE( Has( code, "int SourceSharpEntity_t::EntityIndex() const" ) );
	EXPECT_TRUE( Has( code, "const char *SourceSharpEntity_t::Classname() const" ) );

	// ...AND THAT THEY ANSWER THROUGH THE ARM THE DELETED TWELVE FELL THROUGH
	// FROM. `pStandalone` is the hierarchy that exists in this build, and it is
	// what every one of those accessors asks first and now only.
	EXPECT_TRUE( Has( code, "pStandalone" ) )
		<< kShellBase << " no longer mentions the standalone hierarchy at all,"
		   " so the absence below would be the file having been emptied.";

	// THE ABSENCE, AS ONE CLAUSE COVERING ALL TWELVE AND ANY THIRTEENTH.
	EXPECT_FALSE( Has( code, "pBase->" ) )
		<< kShellBase << " dereferences pBase again. It is filled from"
		   " IServerUnknown::GetBaseEntity(), which returns a constant NULL in"
		   " server.so, so any such line is dead on arrival rather than merely"
		   " unarmed.";
	EXPECT_EQ( 0, GuardCount( kShellBase ) )
		<< kShellBase << " carries a `#ifndef SS_CBASEENTITY_ALIAS` again.";
}


//=============================================================================
// CLAIM 3 — the two arms that COPY an answer living somewhere else.
//
// PROVEN RED: the first by rewriting the server IsWorldEntity's armed arm as
// `handle.GetEntryIndex() == 1`, the second by putting `pModel` back into
// ClipRayToEntity. Each named its own file.
//=============================================================================
TEST( CppAliasShardT3, TheServerIsWorldEntityArmedArmMatchesItsClientTwin )
{
	const std::string strServer = CodeOf( kShellPlayerS );
	const std::string strClient = CodeOf( kShellPlayerC );
	ASSERT_FALSE( strServer.empty() ) << kShellPlayerS << " did not open.";
	ASSERT_FALSE( strClient.empty() ) << kShellPlayerC << " did not open.";

	// The client twin has never needed a guard: it is this line unconditionally.
	const std::string strAnswer = "return handle.GetEntryIndex() == 0;";
	ASSERT_TRUE( Has( strClient, strAnswer ) )
		<< "the CLIENT CStandaloneMoveHelper::IsWorldEntity is no longer `"
		<< strAnswer << "`. It is the ORIGINAL of the server's armed arm; if it"
		   " changed, the server's copy is now wrong and this test is what says so.";

	EXPECT_TRUE( Has( strServer, strAnswer ) )
		<< "the SERVER CStandaloneMoveHelper::IsWorldEntity's armed arm no longer"
		   " matches its client twin's answer. CBaseEntity::Instance( 0 ) is a"
		   " static of the doomed class and cannot be the armed answer.";

	// THE SDK ARM IS GONE, AND WHAT THE CLAUSE HERE USED TO BUY IS BOUGHT
	// BETTER BY ITS ABSENCE.
	//
	// It used to require `return handle == CBaseEntity::Instance( 0 );` to be
	// PRESENT and inside an SDK arm, on the reasoning written beside it in the
	// source: Instance( 0 ) revalidates the serial where the entry index alone
	// does not, and this was not the shard to change what the SDK build
	// answers. `the-close` 1/N deleted the arm and `the-close` 4/N deleted
	// CBaseEntity::Instance itself -- it bottoms out in
	// IServerUnknown::GetBaseEntity(), which is `mov $0x0,%eax; ret` in
	// server.so, so Instance( 0 ) could not have revalidated anything here; it
	// answered NULL, and `handle == NULL` is a different question from "is this
	// the world".
	//
	// So the successor is the absence, and it is a rule with no ambiguity in
	// it: the two twins are now the SAME LINE unconditionally, which is what
	// the first half of this test has always wanted and could only ask for one
	// side at a time while an arm stood between them.
	EXPECT_FALSE( Has( strServer, "CBaseEntity::Instance" ) )
		<< "the server's IsWorldEntity names CBaseEntity::Instance again. There"
		   " is no such function: it is deleted, with EntityFromEntityHandle,"
		   " TraceEntity and GetContainingEntity, because all four bottom out in"
		   " GetBaseEntity() and every implementation of that in the two DLLs is"
		   " `mov $0x0,%eax; ret`.";

	// ...AND THE TWO ARE NOW ONE ANSWER RATHER THAN TWO THAT AGREE. The clause
	// above is an absence; this is what stops it passing over a file whose
	// IsWorldEntity was deleted outright.
	bool bFound = false;
	EXPECT_EQ( ARM_ALWAYS, ArmOfFirst( kShellPlayerS, strAnswer.c_str(), &bFound ) );
	EXPECT_TRUE( bFound )
		<< "the server's armed answer is gone from " << kShellPlayerS;
	EXPECT_EQ( ARM_ALWAYS, ArmOfFirst( kShellPlayerC, strAnswer.c_str(), &bFound ) );
	EXPECT_TRUE( bFound )
		<< "the client twin's answer is gone from " << kShellPlayerC;
}

//=============================================================================
// The remaining per-site answers, each of which is a CHOICE that reads as an
// accident once the comment beside it is gone.
//=============================================================================

// TheClientAmmoNameAnswersUnknownAndNotTheDtClassName IS RETIRED WITH ITS
// SUBJECT -- plan_shim.md §5 3f, lane 3f. CTakeDamageInfo::GetAmmoName is
// deleted: 0 importers on either link line, no caller inside its own
// translation unit.
//
// IT MADE THREE CLAIMS AND ONLY ONE OF THEM WAS ABOUT THE FUNCTION.
//
//   1. The client half answers "Unknown", inside `#ifdef CLIENT_DLL`. A claim
//      about a function that is gone. Retired.
//   2. The server half resolves the handle through SourceSharp_EntityFromHandle
//      and reads the classname off the networkable, rather than dereferencing
//      m_hInflictor. Also about that function. Retired.
//   3. GetClientClass()->GetName() is NOT a substitute for a classname -- it is
//      the DT class name ("C_PhysicsProp"), so a "prop_physics" comparison fed
//      by it can never match, and the branch would be silently dead rather than
//      honestly unanswered.
//
// THE THIRD IS NOT ABOUT GetAmmoName AT ALL. It is about a mistake anybody
// reaching for a classname on the client will make, in any file, and it is the
// one worth keeping -- so it is kept, widened from one function to the whole
// file, below. The reasoning behind all three is in
// docs/shim-3f-retired-arguments.md, under the GetAmmoName entry.
TEST( CppAliasShardT3, TheDtClassNameIsNotUsedAsAClassnameAnywhereInTheDamageFile )
{
	const std::string strCode = CodeOf( kTakeDamage );
	ASSERT_FALSE( strCode.empty() ) << kTakeDamage << " did not open.";

	// POSITIVE CONTROL, and it is the reason this is not a test about nothing:
	// the file is still read, and still holds the damage-force family.
	// (GuessDamageForce was the anchor until the server gc cull deleted it.)
	ASSERT_TRUE( Has( strCode, "void CalculateExplosiveDamageForce( CTakeDamageInfo *info" ) )
		<< kTakeDamage << " no longer defines CalculateExplosiveDamageForce.";

	EXPECT_FALSE( Has( strCode, "GetClientClass()->GetName()" ) )
		<< kTakeDamage << " resolves a name through the DT class name. That is"
		   " not a classname: it is \"C_PhysicsProp\" where a map classname"
		   " would be \"prop_physics\", so any comparison fed by it is"
		   " silently dead rather than honestly unanswered.";

	// And the function the old spelling lived in is gone, asserted here so that
	// the absence above cannot be explained away as "it moved".
	EXPECT_FALSE( Has( strCode, "const char *CTakeDamageInfo::GetAmmoName() const" ) )
		<< kTakeDamage << " defines GetAmmoName again.";
}

//=============================================================================
// THE FIXTURES — the scanner, checked against input whose answer is known.
//
// Without these, every claim above is "a string was or was not found in a file
// this test may never have opened", and this tree has shipped that check more
// than forty times.
//=============================================================================
TEST( CppAliasShardT3Fixture, EveryFileNamedByThisTestOpensAndIsNotTrivial )
{
	const char *kAll[] = {
		kCollision, kParticleMgr, kTakeDamage,
		kShellBase, kShellPlayerS, kShellPlayerC,
	};
	for ( size_t i = 0; i < sizeof( kAll ) / sizeof( kAll[ 0 ] ); i++ )
	{
		const std::string strRaw = ReadFile( kAll[ i ] );
		EXPECT_GT( strRaw.size(), 1000u ) << kAll[ i ] << " did not open, or is a stub.";
		EXPECT_TRUE( Has( strRaw, "//" ) ) << kAll[ i ] << " has no comment at all, which"
		                                                   " no file in this tree does.";
	}
}

TEST( CppAliasShardT3Fixture, TheGuardWalkAnswersKnownInputCorrectly )
{
	const std::string strFixture =
		"always_a\n"
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"sdk_a\n"
		"#else\n"
		"alias_a\n"
		"#endif\n"
		"always_b\n"
		"#ifdef CLIENT_DLL\n"
		"always_c\n"
		"	#ifndef SS_CBASEENTITY_ALIAS\n"
		"sdk_b\n"
		"	#endif\n"
		"always_d\n"
		"#endif\n"
		"always_e\n";

	std::vector< std::string > lines;
	std::vector< Arm_t > arms;
	WalkArms( strFixture, &lines, &arms );

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trim( lines[ i ] );
		if ( t.empty() || t[ 0 ] == '#' )
			continue;
		Arm_t expect = ARM_ALWAYS;
		if ( t.compare( 0, 4, "sdk_" ) == 0 )
			expect = ARM_SDK;
		else if ( t.compare( 0, 6, "alias_" ) == 0 )
			expect = ARM_ALIAS;
		EXPECT_EQ( expect, arms[ i ] ) << "line " << ( i + 1 ) << " (" << t
		                               << ") landed in the wrong arm.";
	}

	// And the walk does not simply answer ARM_SDK for everything.
	int nAlways = 0, nSdk = 0, nAlias = 0;
	for ( size_t i = 0; i < arms.size(); i++ )
	{
		if ( arms[ i ] == ARM_ALWAYS ) nAlways++;
		else if ( arms[ i ] == ARM_SDK ) nSdk++;
		else nAlias++;
	}
	EXPECT_GT( nAlways, 0 );
	EXPECT_GT( nSdk, 0 );
	EXPECT_GT( nAlias, 0 );
}

TEST( CppAliasShardT3Fixture, TheDefeatabilityTestRejectsTheCompoundSpelling )
{
	// The exact spellings, because this is the rule the negative control stands
	// on and "roughly matches" is not good enough.
	EXPECT_TRUE( IsDefeatableGuardLine( "#ifndef SS_CBASEENTITY_ALIAS" ) );
	EXPECT_TRUE( IsDefeatableGuardLine( "\t#ifndef SS_CBASEENTITY_ALIAS" ) );
	EXPECT_TRUE( IsDefeatableGuardLine( "    #ifndef SS_CBASEENTITY_ALIAS  " ) );

	EXPECT_FALSE( IsDefeatableGuardLine( "#ifndef SS_CBASEENTITY_ALIAS // why" ) );
	EXPECT_FALSE( IsDefeatableGuardLine( "#if !defined( SS_CBASEENTITY_ALIAS )" ) );

	// ...and those same non-matches are reported as UNDEFEATABLE, which is what
	// the whole-tree scan above fires on.
	EXPECT_TRUE( IsUndefeatableGuardLine(
		"#if defined( CLIENT_DLL ) && !defined( SS_CBASEENTITY_ALIAS )" ) );
	EXPECT_TRUE( IsUndefeatableGuardLine( "#if !defined( SS_CBASEENTITY_ALIAS )" ) );
	EXPECT_TRUE( IsUndefeatableGuardLine( "#ifdef SS_CBASEENTITY_ALIAS" ) );
	EXPECT_TRUE( IsUndefeatableGuardLine( "	#elif !defined( SS_CBASEENTITY_ALIAS )" ) );

	// The bare spelling is not undefeatable, and the alias header's own include
	// guard is not one of these at all.
	EXPECT_FALSE( IsUndefeatableGuardLine( "#ifndef SS_CBASEENTITY_ALIAS" ) );
	EXPECT_FALSE( IsUndefeatableGuardLine( "#ifndef SS_CBASEENTITY_ALIAS_H" ) );
	EXPECT_FALSE( IsUndefeatableGuardLine( "#ifdef SS_CBASEENTITY_ALIAS_H" ) );
	EXPECT_FALSE( IsUndefeatableGuardLine( "int x = SS_CBASEENTITY_ALIAS;" ) );
}

TEST( CppAliasShardT3Fixture, CommentStrippingReallyStrips )
{
	const std::string strIn =
		"int a; // pEntity->GetClassname()\n"
		"/* pModel */ int b;\n"
		"int c;\n";
	const std::string strOut = StripComments( strIn );
	EXPECT_FALSE( Has( strOut, "GetClassname" ) );
	EXPECT_FALSE( Has( strOut, "pModel" ) );
	EXPECT_TRUE( Has( strOut, "int a;" ) );
	EXPECT_TRUE( Has( strOut, "int b;" ) );
	EXPECT_TRUE( Has( strOut, "int c;" ) );
}

TEST( CppAliasShardT3Fixture, ArmOfFirstReportsAMissingNeedleRatherThanPassing )
{
	bool bFound = true;
	ArmOfFirst( kCollision, "a_needle_no_file_in_this_tree_contains", &bFound );
	EXPECT_FALSE( bFound ) << "ArmOfFirst claimed to find a needle that is not there,"
	                          " which would make every assertion above vacuous.";
}
