//========= SourceSharp — the sound system's entity reaches, and why they go ==//
//
// A SOURCE SCANNER, like soundemitter_lift_test, physicslink_lift_test and
// removal_set_middle_test, and for the same reason: the claim is about the TEXT
// of a file that needs a whole engine to run, so the text is what is read.
//
// WHAT THIS COVERS. sourcesharp/game/shared/sound_emitter.cpp is a REMOVAL-SET file
// that STAYS. Its nine (server) / five (client) out-of-line CBaseEntity members
// die with the class at Phase 13 step 6, but the rest of the file -- the
// CSoundEmitterSystem game system and the whole GameSound_* family -- is the
// only path a sound takes in this port: fourteen server objects and nineteen
// client objects reach into it for GameSound_Emit / GameSound_Stop /
// GameSound_Precache and friends, and shell_sound.o is one of them. Whatever
// else the removal set does, this file is kept.
//
// Kept means its NON-member code has to survive `using CBaseEntity =
// IGameEntity;`, and five places in it reached an entity. Each one was measured
// rather than argued, each turned out to be a path that CANNOT RUN in this
// build, and each is now behind `#ifndef SOURCESHARP` with the stock text
// intact. This file pins the five measurements, because every one of them is a
// fact about OTHER files that a later change could quietly falsify:
//
//   1. CBaseEntity::IsServer() is a per-DLL compile-time constant
//      (baseentity.h `return true;`, c_baseentity.h `return false;`). Nothing
//      to measure; the preprocessor asks it now.
//
//   2. ModifyEmitSoundParams -- GameSound_Emit looked the entity up SOLELY to
//      give an override a say. No file on either link line defines an override.
//      The base body's client arm is one call to
//      GameRules()->TranslateEffectForVisionFilter, whose only built
//      implementation is gamerules.h's `return pchEffectName;`.
//      IF EITHER OF THOSE STOPS BEING TRUE THE GUARD IS WRONG, and the build
//      stays green either way -- which is what this test is for.
//
//   3. `fromplayer` -- two owner-chain walks asking "is this entity, or its
//      owner, a player". The subject comes from CBaseEntity::Instance, so it is
//      always a CBaseEntity, and the only SDK class whose IsPlayer() answers
//      true is CBasePlayer, whose three translation units are on neither link
//      line. The answer was already false on every path.
//
//   4. The close-caption attenuation loop begins with UTIL_PlayerByIndex, which
//      resolves through UTIL_SDKPlayerFromEdict -- whose body is `return NULL;`
//      by construction, and says so. `if ( !player ) continue;` took every slot,
//      so no recipient was ever removed.
//
//   5. `playgamesound` begins with C_BasePlayer::GetLocalPlayer(), defined once
//      on the client link line (shell_player_orphans.cpp) as `return NULL;`.
//      The command has only ever printed "Can't play until a game is started."
//
// AND ONE THING THAT IS NOT A GUARD. The actor-gender lookup asks an entity
// index for a model name. On the SERVER that is IServerEntity's own virtual, so
// it is answered at the engine boundary now -- edict -> GetIServerEntity(),
// movehelper_server.cpp's recipe -- which is not merely alias-proof: it fixes a
// live defect, because CBaseEntity::Instance answers NULL for an entity outside
// the SDK hierarchy and every entity this port owns is one. On the CLIENT there
// is no interface carrying the model name, so that arm still asks the entity
// and is left for whoever answers "the model name at an index" on the client.
//
// THE SCANNER IS ITSELF CHECKED. A scanner that reads nothing passes every
// claim it is given, and the claims here are mostly of the form "X appears only
// inside a guard" -- the purest shape of a check that cannot fail. So the guard
// detector is run over hand-written fixtures that put the same token inside and
// outside a guard, over nested guards and over `#else` arms, and is asserted to
// answer differently. The build reader is asserted to contain a file everyone
// agrees is compiled.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <dirent.h>
#include <sys/stat.h>
#include <unistd.h>

#include <algorithm>
#include <cstdlib>
#include <fstream>
#include <set>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::string strProbe( kCandidates[ i ] );
		strProbe += "/game/shared";
		DIR *pDir = opendir( strProbe.c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadSrcFile( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += pszRelative;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
}

const std::string &EmitterSource()
{
	static const std::string s = ReadSrcFile( "sourcesharp/game/shared/sound_emitter.cpp" );
	return s;
}

//-----------------------------------------------------------------------------
// THE GUARD DETECTOR.
//
// Walks the preprocessor conditionals of a translation unit and reports, per
// line, whether SOURCESHARP is defined on the arm that line sits on. It tracks
// a stack of "this conditional is a SOURCESHARP question, and we are on the
// arm that runs when SOURCESHARP is NOT defined" and flips that flag at the
// matching `#else` / `#elif`, so the `#else` of an `#ifdef SOURCESHARP` counts
// the same as the body of an `#ifndef SOURCESHARP`.
//
// It deliberately understands only the two spellings this tree uses --
// `#ifndef SOURCESHARP` and `#if !defined( SOURCESHARP )`, plus `#ifdef` /
// `#if defined(...)` for the inverse -- and treats any other conditional as
// transparent. A third spelling would read as unguarded, which fails toward
// RED. That is the direction a guard test must fail in.
//-----------------------------------------------------------------------------
std::vector< bool > GuardedOutOfThisBuild( const std::string &strSource )
{
	std::vector< bool > out;

	struct Frame
	{
		bool bIsSourceSharpQuestion;
		bool bOnNotDefinedArm;
	};
	std::vector< Frame > stack;
	int nDepth = 0;                      // how many frames currently say "off"

	size_t i = 0;
	while ( i <= strSource.size() )
	{
		size_t nEnd = strSource.find( '\n', i );
		if ( nEnd == std::string::npos )
			nEnd = strSource.size();
		std::string strLine = strSource.substr( i, nEnd - i );

		// Trim leading whitespace; the directive may be indented.
		size_t nFirst = strLine.find_first_not_of( " \t" );
		std::string strTrim = ( nFirst == std::string::npos )
		                      ? std::string() : strLine.substr( nFirst );

		bool bDirective = ( strTrim.size() > 0 && strTrim[ 0 ] == '#' );
		bool bLineIsOff = ( nDepth > 0 );

		if ( bDirective )
		{
			// Normalise `#  if` to `#if`.
			std::string strD;
			for ( size_t k = 1; k < strTrim.size(); k++ )
			{
				if ( strTrim[ k ] != ' ' && strTrim[ k ] != '\t' )
					strD += strTrim[ k ];
				else if ( !strD.empty() )
					break;
			}
			std::string strRest = strTrim.substr( 1 );

			bool bNames = ( strRest.find( "SOURCESHARP" ) != std::string::npos );

			if ( strD == "if" || strD == "ifdef" || strD == "ifndef" )
			{
				Frame f;
				f.bIsSourceSharpQuestion = bNames;
				// "SOURCESHARP is NOT defined on this arm" is true for
				// `#ifndef SOURCESHARP` and for `#if !defined( SOURCESHARP )`.
				bool bNegated = ( strD == "ifndef" )
				              || ( strRest.find( '!' ) != std::string::npos );
				f.bOnNotDefinedArm = bNames && bNegated;
				if ( f.bOnNotDefinedArm )
					nDepth++;
				stack.push_back( f );
			}
			else if ( strD == "else" || strD == "elif" )
			{
				if ( !stack.empty() )
				{
					Frame &f = stack.back();
					if ( f.bIsSourceSharpQuestion )
					{
						if ( f.bOnNotDefinedArm )
						{
							f.bOnNotDefinedArm = false;
							nDepth--;
						}
						else if ( strD == "else" )
						{
							f.bOnNotDefinedArm = true;
							nDepth++;
						}
					}
				}
			}
			else if ( strD == "endif" )
			{
				if ( !stack.empty() )
				{
					if ( stack.back().bOnNotDefinedArm )
						nDepth--;
					stack.pop_back();
				}
			}
			// A directive line itself is never a subject line.
			bLineIsOff = ( nDepth > 0 );
		}

		out.push_back( bLineIsOff );

		if ( nEnd == strSource.size() )
			break;
		i = nEnd + 1;
	}
	return out;
}

// Every line of `strSource` naming `pszNeedle`, split by whether the guard
// detector says that line is compiled out of THIS build.
void SplitByGuard( const std::string &strSource, const char *pszNeedle,
                   int *pnCompiled, int *pnGuardedOut )
{
	std::vector< bool > off = GuardedOutOfThisBuild( strSource );
	*pnCompiled = 0;
	*pnGuardedOut = 0;

	size_t i = 0;
	size_t nLine = 0;
	while ( i <= strSource.size() && nLine < off.size() )
	{
		size_t nEnd = strSource.find( '\n', i );
		if ( nEnd == std::string::npos )
			nEnd = strSource.size();
		std::string strLine = strSource.substr( i, nEnd - i );

		// Comments discuss every token this file searches for, at length. A
		// scanner that read prose would pass a file with the guard deleted.
		size_t nComment = strLine.find( "//" );
		if ( nComment != std::string::npos )
			strLine = strLine.substr( 0, nComment );

		if ( strLine.find( pszNeedle ) != std::string::npos )
		{
			if ( off[ nLine ] )
				( *pnGuardedOut )++;
			else
				( *pnCompiled )++;
		}

		if ( nEnd == strSource.size() )
			break;
		i = nEnd + 1;
		nLine++;
	}
}

//-----------------------------------------------------------------------------
// WHAT THE COMPILER WAS ACTUALLY GIVEN.
//
// The CMake build's compiled sources: the union of server_sourcesharp,
// client_sourcesharp and sourcesharp (libmanaged_host.so), read through
// cmake_graph.h -- compile_commands.json cross-checked against each target's
// CMakeLists source list. Farm-relative paths, the spelling ReadSrcFile takes.
//
// THIS USED TO READ VPC'S NINJA GRAPH (_vpc_/ninja/sdk_csharp_debug.ninja),
// with a freshness guard of its own: the graph's `regenerate_build` provenance
// row, each input's mtime against the graph's, a second loose `: cxx ` reading
// that the walk had to agree with, and a set of scratch-directory fixtures
// driving every arm of that guard. RETIRED IN PHASE 6b, all of it: that was VPC
// graph provenance. Missing, empty and stale inputs of the CMake graph are
// cmake_graph.h's own hard failures (CompiledSources() throws, and throws when
// compile_commands.json disagrees with the CMakeLists -- the second reading the
// loose scan used to be), and cmake_graph_test is where those arms are driven.
//
// What stays here is the proof-of-read: every fact that asks IsBuilt() opens
// by requiring the emitter shim this whole file is about to be in the set, so
// an absence is never the answer of a reader that saw nothing.
//-----------------------------------------------------------------------------
const char *const kBuildProofOfRead = "sourcesharp/game/shared/sound_emitter.cpp";

const std::set< std::string > &BuiltSources()
{
	static std::set< std::string > s;
	static bool bDone = false;
	if ( bDone )
		return s;

	const char *kTargets[] = { cmake_graph::kServer, cmake_graph::kClient, cmake_graph::kHost };
	std::set< std::string > all;
	for ( size_t i = 0; i < sizeof( kTargets ) / sizeof( kTargets[ 0 ] ); i++ )
	{
		const std::set< std::string > one = cmake_graph::CompiledSources( kTargets[ i ] );
		all.insert( one.begin(), one.end() );
	}
	s.swap( all );
	bDone = true;
	return s;
}

bool IsBuilt( const char *pszRelative )
{
	return BuiltSources().count( std::string( pszRelative ) ) > 0;
}

// Every fact that asks IsBuilt() opens with this. Not a helper call that could
// be forgotten quietly: without it the fact's absences are satisfied by a
// reader that read nothing.
#define REQUIRE_THE_BUILD_READ()                                        \
	do {                                                                \
		ASSERT_TRUE( IsBuilt( kBuildProofOfRead ) )                     \
			<< "the CMake build's compiled sources do not include "     \
			<< kBuildProofOfRead << " -- the reader is not reading the "  \
			   "build, and every IsBuilt() absence would pass for free."; \
	} while ( 0 )

// Strip // and /* */ so a claim about CODE is not answered by prose.
std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bLine = false, bBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bLine )
		{
			if ( strIn[ i ] == '\n' ) { bLine = false; out += '\n'; }
			continue;
		}
		if ( bBlock )
		{
			if ( strIn[ i ] == '*' && i + 1 < strIn.size() && strIn[ i + 1 ] == '/' )
			{
				bBlock = false;
				i++;
			}
			else if ( strIn[ i ] == '\n' )
			{
				out += '\n';
			}
			continue;
		}
		if ( strIn[ i ] == '/' && i + 1 < strIn.size() && strIn[ i + 1 ] == '/' )
		{
			bLine = true;
			continue;
		}
		if ( strIn[ i ] == '/' && i + 1 < strIn.size() && strIn[ i + 1 ] == '*' )
		{
			bBlock = true;
			i++;
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

size_t CountInCode( const std::string &strSource, const char *pszNeedle )
{
	std::string strCode = StripComments( strSource );
	size_t n = 0, i = 0;
	while ( ( i = strCode.find( pszNeedle, i ) ) != std::string::npos )
	{
		n++;
		i += 1;
	}
	return n;
}

} // namespace


//=============================================================================
// (0) The subject exists, and the instruments read something.
//=============================================================================

TEST( LaneRSoundDeadPaths, TheFileWasReadAndIsTheOneWeThink )
{
	ASSERT_NE( (const char *)NULL, SrcRoot() ) << "no src/ tree found";
	ASSERT_GT( EmitterSource().size(), 40000u )
		<< "sourcesharp/game/shared/sound_emitter.cpp did not read, or shrank by half.";
	EXPECT_NE( std::string::npos, EmitterSource().find( "GameSound_Emit" ) );
	EXPECT_NE( std::string::npos, EmitterSource().find( "CSoundEmitterSystem" ) );
}

TEST( LaneRSoundDeadPaths, TheBuildReaderReadsTheBuild )
{
	// WAS TheNinjaGraphReaderReadsAGraph, over VPC's ninja graph with a second
	// loose reading the walk had to agree with. That second reading is
	// cmake_graph.h's own now; what is left is the pair of controls.
	REQUIRE_THE_BUILD_READ();
	// A file nobody disputes is compiled, and one nobody disputes is not.
	EXPECT_TRUE( IsBuilt( "sourcesharp/game/shared/sound_emitter.cpp" ) );
	EXPECT_FALSE( IsBuilt( "game/shared/tf/tf_player_shared.cpp" ) );
}

//=============================================================================
// (1) The guard detector answers differently on the two sides of a guard.
//
// Every claim below this point is "X appears ONLY inside a guard", which a
// detector that called everything guarded would pass for free.
//=============================================================================

TEST( LaneRSoundDeadPaths, GuardDetectorSeparatesTheTwoArms )
{
	const std::string strFixture =
		"int a = NEEDLE;\n"
		"#ifndef SOURCESHARP\n"
		"int b = NEEDLE;\n"
		"#endif\n"
		"int c = NEEDLE;\n";

	int nCompiled = 0, nOff = 0;
	SplitByGuard( strFixture, "NEEDLE", &nCompiled, &nOff );
	EXPECT_EQ( 2, nCompiled );
	EXPECT_EQ( 1, nOff );
}

TEST( LaneRSoundDeadPaths, GuardDetectorHandlesTheDefinedSpellingAndElse )
{
	const std::string strFixture =
		"#if !defined( SOURCESHARP )\n"
		"int a = NEEDLE;\n"
		"#else\n"
		"int b = NEEDLE;\n"
		"#endif\n"
		"#ifdef SOURCESHARP\n"
		"int c = NEEDLE;\n"
		"#else\n"
		"int d = NEEDLE;\n"
		"#endif\n";

	int nCompiled = 0, nOff = 0;
	SplitByGuard( strFixture, "NEEDLE", &nCompiled, &nOff );
	EXPECT_EQ( 2, nCompiled ) << "b and c are the arms this build takes";
	EXPECT_EQ( 2, nOff ) << "a and d are the arms it does not";
}

TEST( LaneRSoundDeadPaths, GuardDetectorNestsAndUnnests )
{
	const std::string strFixture =
		"#if !defined( CLIENT_DLL ) && !defined( SOURCESHARP )\n"
		"int a = NEEDLE;\n"
		"#ifdef _DEBUG\n"
		"int b = NEEDLE;\n"
		"#endif\n"
		"#endif\n"
		"int c = NEEDLE;\n";

	int nCompiled = 0, nOff = 0;
	SplitByGuard( strFixture, "NEEDLE", &nCompiled, &nOff );
	EXPECT_EQ( 1, nCompiled ) << "only c survives; an unrelated nested "
	                             "conditional must not close the outer guard";
	EXPECT_EQ( 2, nOff );
}

TEST( LaneRSoundDeadPaths, GuardDetectorIsNotFooledByAnUnrelatedConditional )
{
	const std::string strFixture =
		"#ifdef GAME_DLL\n"
		"int a = NEEDLE;\n"
		"#else\n"
		"int b = NEEDLE;\n"
		"#endif\n";

	int nCompiled = 0, nOff = 0;
	SplitByGuard( strFixture, "NEEDLE", &nCompiled, &nOff );
	EXPECT_EQ( 2, nCompiled ) << "a conditional that does not name SOURCESHARP "
	                             "is transparent -- both arms are 'compiled' as "
	                             "far as this question goes";
	EXPECT_EQ( 0, nOff );
}

//=============================================================================
// (2) IsServer -- the per-DLL constant is asked with the preprocessor now.
//=============================================================================

TEST( LaneRSoundDeadPaths, IsServerIsNoLongerBorrowedFromTheEntityClass )
{
	EXPECT_EQ( 0u, CountInCode( EmitterSource(), "CBaseEntity::IsServer" ) )
		<< "sound_emitter.cpp reaches CBaseEntity::IsServer() again. It is "
		   "a per-DLL compile-time constant (baseentity.h `return true;`, "
		   "c_baseentity.h `return false;`) borrowing the entity class for a "
		   "namespace, and an alias has no statics.";

	// And the two arms still say what the one expression said.
	EXPECT_NE( std::string::npos, EmitterSource().find( "\"(sv)\"" ) );
	EXPECT_NE( std::string::npos, EmitterSource().find( "\"(cl)\"" ) );
}

//=============================================================================
// (3) ModifyEmitSoundParams -- the lookup buys a no-op IN THIS BUILD.
//
// Two facts about OTHER files hold the guard up. Both are green either way if
// they stop being true, which is exactly why they are asserted here.
//=============================================================================

//-----------------------------------------------------------------------------
// GUARDED OUT BECAME DELETED at plan_entity.md step 4b, and the reason is about
// WHICH FILE this is, not about what the calls did.
//
// While the emitter was stock, keeping the two calls under `#ifndef SOURCESHARP`
// was the right shape: a build that compiles game/shared/tf/ needs them back.
// The emitter is a SHIM file now, and tools/entity-census counts §3.16 cast
// spellings over a shim file's TEXT -- guarded or not. Carrying a lookup that
// this build cannot reach, in a file the gate reads, means the gate counts it.
//
// The claim this test makes is therefore inverted, and it is a STRONGER one:
// zero either side of the guard. Everything that made the deletion safe is
// still asserted, by the two facts below it, which is what turns "deleted"
// into "deleted for a reason that is still true".
//-----------------------------------------------------------------------------
TEST( LaneRSoundDeadPaths, ModifyEmitSoundParamsIsNotReachedAtAll )
{
	int nCompiled = 0, nOff = 0;
	SplitByGuard( EmitterSource(), "ModifyEmitSoundParams", &nCompiled, &nOff );
	EXPECT_EQ( 0, nCompiled )
		<< "GameSound_Emit reaches an entity for its ModifyEmitSoundParams "
		   "override again, outside any guard.";
	EXPECT_EQ( 0, nOff )
		<< "a guarded ModifyEmitSoundParams lookup is back in the emitter. It "
		   "was deleted at step 4b because a shim file's text is what the cast "
		   "census reads; if an override is wanted on this side it is a managed "
		   "one, asked by handle.";
}

TEST( LaneRSoundDeadPaths, NothingOnEitherLinkLineOverridesModifyEmitSoundParams )
{
	REQUIRE_THE_BUILD_READ();
	// The only DEFINITION this build compiles is CBaseEntity's own. If a
	// second one appears the guard above is wrong and nothing else says so.
	static const char *kOverriders[] = {
		"game/shared/tf/tf_weapon_rocketlauncher.cpp",
		"game/shared/tf/tf_dropped_weapon.cpp",
		"game/shared/tf/tf_player_shared.cpp",
	};
	for ( size_t i = 0; i < sizeof( kOverriders ) / sizeof( kOverriders[ 0 ] ); i++ )
	{
		EXPECT_FALSE( IsBuilt( kOverriders[ i ] ) )
			<< kOverriders[ i ] << " is on a link line now, and it overrides "
			   "ModifyEmitSoundParams. GameSound_Emit's guarded lookup has to "
			   "come back or its override will never be consulted.";
	}
	// THE SUBJECT IS GONE, AND THAT IS NOW THE FACT.
	//
	// This line used to be EXPECT_TRUE, and its message was the right warning to
	// have written: "the base implementation's own file left the build -- this
	// test's subject moved." THE FLIP IS WHAT MOVED IT. game/shared/
	// baseentity_shared.cpp holds CBaseEntity::ModifyEmitSoundParams, the only
	// definition this build compiled, and it left both vpcs with the rest of the
	// entity hierarchy.
	//
	// So the claim is not weakened, it is INVERTED and strengthened. Before:
	// "the three overriders are off the link line, so the one definition that is
	// on it is the base's." Now: "the three overriders are off the link line AND
	// so is the base -- there is no ModifyEmitSoundParams on either link line at
	// all." The guard the whole section justifies is more clearly right than it
	// was, not less, and a re-appearance of baseentity_shared.cpp reddens this
	// just as a re-appearance of a TF overrider does.
	EXPECT_FALSE( IsBuilt( "game/shared/baseentity_shared.cpp" ) )
		<< "game/shared/baseentity_shared.cpp is back on a link line, so "
		   "CBaseEntity::ModifyEmitSoundParams is compiled again and the "
		   "reasoning above is about a build that no longer exists.";

	// AND THE POSITIVE CONTROL THE EXPECT_TRUE USED TO BE. Four EXPECT_FALSEs in
	// a row is exactly the shape that passes for free if IsBuilt() answers false
	// for everything -- an unbuilt graph, a moved `build <obj>: cxx <source>`
	// line, a path spelling change. The control is the port's own shim, which
	// holds the guarded emitter this whole section is about, so it cannot go
	// away without this test having no subject either way.
	EXPECT_TRUE( IsBuilt( "sourcesharp/game/shared/sound_emitter.cpp" ) )
		<< "IsBuilt() cannot find the emitter shim itself, so the four absences "
		   "above are vacuous rather than measured.";
}

TEST( LaneRSoundDeadPaths, TheVisionFilterHookIsAPassThroughInThisBuild )
{
	// CBaseEntity::ModifyEmitSoundParams' client body is one call to
	// GameRules()->TranslateEffectForVisionFilter( "sounds", ... ). The only
	// implementation this build compiles is gamerules.h's, and it returns its
	// argument. Pinned by TEXT because the override list is the thing that
	// could grow.
	const std::string strGameRules = ReadSrcFile( "game/shared/gamerules.h" );
	ASSERT_GT( strGameRules.size(), 1000u ) << "gamerules.h did not read";
	const std::string strCode = StripComments( strGameRules );
	size_t nHook = strCode.find( "TranslateEffectForVisionFilter" );
	ASSERT_NE( std::string::npos, nHook )
		<< "gamerules.h no longer declares TranslateEffectForVisionFilter.";
	EXPECT_NE( std::string::npos, strCode.find( "return pchEffectName;", nHook ) )
		<< "the base TranslateEffectForVisionFilter stopped being a "
		   "pass-through. GameSound_Emit's guarded ModifyEmitSoundParams call "
		   "now has an observable effect and the guard is wrong.";
}

//=============================================================================
// (4) fromplayer -- the owner walks cannot answer yes in this build.
//=============================================================================

TEST( LaneRSoundDeadPaths, NoCBasePlayerTranslationUnitIsOnEitherLinkLine )
{
	REQUIRE_THE_BUILD_READ();
	// This is the whole argument for guarding the two owner walks: the subject
	// is always a CBaseEntity, and CBasePlayer is the only SDK class whose
	// IsPlayer() answers true.
	static const char *kPlayerTUs[] = {
		"game/server/player.cpp",
		"game/client/c_baseplayer.cpp",
		"game/shared/baseplayer_shared.cpp",
	};
	for ( size_t i = 0; i < sizeof( kPlayerTUs ) / sizeof( kPlayerTUs[ 0 ] ); i++ )
	{
		EXPECT_FALSE( IsBuilt( kPlayerTUs[ i ] ) )
			<< kPlayerTUs[ i ] << " is compiled now, so a CBasePlayer can "
			   "exist and `fromplayer` can be true again. The two guarded "
			   "owner walks in sound_emitter.cpp have to come back.";
	}
}

TEST( LaneRSoundDeadPaths, TheOwnerWalksAreGoneAltogether )
{
	// Deleted rather than guarded at step 4b -- same argument as
	// ModifyEmitSoundParamsIsNotReachedAtAll above. The three facts that made
	// the walks unable to answer yes are still asserted, on either side of
	// this test.
	int nCompiled = 0, nOff = 0;
	SplitByGuard( EmitterSource(), "GetOwnerEntity", &nCompiled, &nOff );
	EXPECT_EQ( 0, nCompiled )
		<< "an owner walk is compiled again in sound_emitter.cpp.";
	EXPECT_EQ( 0, nOff )
		<< "a guarded owner walk is back in the emitter. `fromplayer` has one "
		   "answer in this build and it is false.";

	// `fromplayer` itself must still be declared and initialised OUTSIDE the
	// guard -- guarding the declaration too would not compile, and guarding
	// only the walk is the whole point.
	EXPECT_EQ( 2u, CountInCode( EmitterSource(), "bool fromplayer = false;" ) );
}

//=============================================================================
// (5) The caption attenuation loop has never run its body.
//=============================================================================

TEST( LaneRSoundDeadPaths, UtilPlayerByIndexAndItsLookupAreGoneBecauseTheyOnlyAnsweredNull )
{
	// WAS UtilPlayerByIndexStillAnswersNullByConstruction. Both functions
	// answered NULL on every path -- server.so builds no CBasePlayer -- and the
	// guarded attenuation loop below rested on that answer. With their last
	// caller gone (engineapi ad131c0), they are DELETED (P5 lane E), so the
	// loop's premise is now an absence: there is no SDK player lookup to ask.
	const std::string strUtil = ReadSrcFile( "sourcesharp/game/server/shell_playerslots.cpp" );
	ASSERT_GT( strUtil.size(), 5000u ) << "shell_playerslots.cpp did not read";
	const std::string strCode = StripComments( strUtil );

	EXPECT_EQ( std::string::npos, strCode.find( "UTIL_PlayerByIndex(" ) )
		<< "UTIL_PlayerByIndex is defined again. It can only answer NULL here.";
	EXPECT_EQ( std::string::npos, strCode.find( "UTIL_SDKPlayerFromEdict(" ) )
		<< "UTIL_SDKPlayerFromEdict is defined again. It can only answer NULL here.";
	// Proof the file is the slot-walk family it was: its slot-keyed answers.
	EXPECT_NE( std::string::npos, strCode.find( "CStandalonePlayer" ) );
}

TEST( LaneRSoundDeadPaths, TheAttenuationLoopIsGoneAltogether )
{
	int nCompiled = 0, nOff = 0;
	SplitByGuard( EmitterSource(), "AttenuateCaption", &nCompiled, &nOff );
	EXPECT_EQ( 0, nCompiled )
		<< "the close-caption attenuation loop is compiled again. Its first "
		   "statement is UTIL_PlayerByIndex, which answers NULL for every slot "
		   "in this build, and its next reaches an SDK player's inheritance.";
	EXPECT_EQ( 0, nOff )
		<< "a guarded attenuation loop is back in the emitter. Deleted at step "
		   "4b: AttenuateCaption is declared in sceneentity.cpp, which is in "
		   "neither build, so even unguarded it would not link.";
}

//=============================================================================
// (6) playgamesound has only ever printed its else-arm.
//=============================================================================

TEST( LaneRSoundDeadPaths, GetLocalPlayerStillAnswersNull )
{
	const std::string strOrphans =
		ReadSrcFile( "sourcesharp/game/client/shell_player_orphans.cpp" );
	ASSERT_GT( strOrphans.size(), 1000u )
		<< "shell_player_orphans.cpp did not read";
	const std::string strCode = StripComments( strOrphans );

	size_t nFn = strCode.find( "C_BasePlayer *C_BasePlayer::GetLocalPlayer" );
	ASSERT_NE( std::string::npos, nFn )
		<< "C_BasePlayer::GetLocalPlayer moved. playgamesound's guarded arm "
		   "rests on this body being `return NULL;`.";
	size_t nEnd = strCode.find( "\n}", nFn );
	ASSERT_NE( std::string::npos, nEnd );
	const std::string strBody = strCode.substr( nFn, nEnd - nFn );
	EXPECT_NE( std::string::npos, strBody.find( "return NULL;" ) );
	EXPECT_EQ( std::string::npos, strBody.find( "s_pLocalPlayer" ) )
		<< "GetLocalPlayer has grown a real answer.";
}

// READ THE ASSERTIONS, NOT THE NAME. There is no guarded arm left to be outside
// of: the `#ifndef SOURCESHARP` arm that held the GetLocalPlayer call was one of
// the shim's 240 dead arms and was deleted. The claim is the same claim --
// playgamesound reaches no player and prints its else-arm -- and it is now
// unconditional, so the fact reads the LIVE text (R2).
// THE COMMAND ITSELF IS GONE NOW -- plan_shim.md §5 3f, lane 3f -- so this test
// is rewritten rather than deleted, and what it claims is STRICTLY STRONGER
// than what it claimed before.
//
// It used to say: playgamesound does not call C_BasePlayer::GetLocalPlayer(),
// it writes its player subject out as a NULL so the refusal is legible, and the
// message it prints is unguarded so the command has not gone silent. All three
// were about a command that could not do anything -- GetLocalPlayer() is
// defined once on this link line, in shell_player_orphans.cpp:96, and its whole
// body is `return NULL;`.
//
// A command that only ever prints "Can't play until a game is started." is not
// a behaviour to preserve, and the live successor does more than stock's ever
// did: SourceSharp.Server DevCommandsLive.cs's `playentitysound` emits a
// soundscript from a CHOSEN entity on the server, including from the shell-less
// entities that have no edict for stock's route to name.
//
// THE PAIR. The absence of the command is asserted WITH the absence of the
// thing it was refused for -- GetLocalPlayer, compiled or guarded -- so this
// cannot be satisfied by the file being unreadable, and the positive control
// below proves the file is the one this test thinks it is.
TEST( LaneRSoundDeadPaths, PlaygamesoundIsGoneAndSoIsTheLocalPlayerReach )
{
	const std::string strAll = EmitterSource();
	ASSERT_FALSE( strAll.empty() ) << "the emitter source did not load";

	// Positive control: this really is sound_emitter.cpp and it still holds the
	// emitter. Without it every absence below is a statement about nothing.
	ASSERT_NE( StripComments( strAll ).find( "void GameSound_Emit(" ), std::string::npos )
		<< "sound_emitter.cpp no longer defines GameSound_Emit";

	const std::string strCode = StripComments( strAll );

	EXPECT_EQ( std::string::npos, strCode.find( "Playgamesound_f" ) )
		<< "playgamesound is back in CODE. It had no caller by name anywhere "
		   "-- tools/, docs/, cfg/, the managed tree -- and its body could not "
		   "reach a player on this link line.";
	EXPECT_EQ( std::string::npos, strCode.find( "GamesoundCompletion" ) )
		<< "the completion callback is back without the command it completed.";

	// The original claim, kept: no reach for the local player, on EITHER arm.
	int nCompiled = 0, nOff = 0;
	SplitByGuard( strAll, "GetLocalPlayer", &nCompiled, &nOff );
	EXPECT_EQ( 0, nCompiled )
		<< "the emitter calls C_BasePlayer::GetLocalPlayer() again. Its body is "
		   "`return NULL;` -- shell_player_orphans.cpp -- so the call buys "
		   "nothing and every line it leads to reaches C_BasePlayer through the "
		   "SDK inheritance this port removes.";
	EXPECT_EQ( 0, nOff )
		<< "a guarded GetLocalPlayer arm is back in the emitter. SOURCESHARP is "
		   "always defined on this link line, so an arm under `#ifndef "
		   "SOURCESHARP` is source no compiler sees and a fact anchored in it "
		   "cannot fail.";
}

// The four DEVELOPER commands went in the same commit, and a ConCommand is
// reached BY NAME, which no link-line census sees. The proof was a grep of
// every place a name can come from -- tools/, docs/, game/mod_sharp/cfg/ and
// the managed tree -- and none of the four appears in any of them.
//
// sv_soundemitter_trace DOES appear: tools/7e-particle-sound-gate drives it at
// two sites and parses its output. Its NAME stays, and it is asserted here
// beside the four absences precisely so that this test cannot be read as "the
// sound commands all went".
//
// WHAT CHANGED UNDER THIS ASSERTION, plan_shim.md Phase 5 (lane 5d): the
// DECLARATION moved to SourceSharp.{Server,Client}/MoveVars.cs and what is left
// in this file is a CManagedConVarRef of the same name. The needle below is
// satisfied either way, which is correct -- what it is for is that the gate's
// knob still resolves, and it does, because Engine.RegisterConVar builds a real
// native ConVar in the engine's registry. TheTraceKnobIsAByNameHandleNow, just
// after, pins WHICH of the two shapes is there, so the pair cannot both be
// satisfied by a native declaration coming back.
TEST( LaneRSoundDeadPaths, TheDeveloperCommandsAreGoneAndTheGatesOneIsNot )
{
	const std::string strCode = StripComments( EmitterSource() );
	ASSERT_FALSE( strCode.empty() );

	static const char *const kGone[] = {
		"sv_soundemitter_flush",
		"cl_soundemitter_flush",
		"sv_soundemitter_filecheck",
		"sv_findsoundname",
		"S_SoundEmitterSystemFlush",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_EQ( std::string::npos, strCode.find( kGone[ i ] ) )
			<< kGone[ i ] << " is back in CODE. If something now drives it by "
			   "name, say where -- a console command is invisible to every "
			   "census this tree has.";
	}

	EXPECT_NE( std::string::npos, strCode.find( "sv_soundemitter_trace" ) )
		<< "sv_soundemitter_trace is gone. tools/7e-particle-sound-gate turns "
		   "it on, parses its output into (script, wave, entindex, recipients) "
		   "and turns it off again; without it that gate measures nothing and "
		   "still passes.";
}

// THE OTHER HALF OF THE ONE ABOVE. That needle is the bare name, so it is happy
// with a native declaration and with a by-name handle alike; on its own it
// would not notice a ConVar coming back into C++ under §6's nose. This says
// which shape is there.
TEST( LaneRSoundDeadPaths, TheTraceKnobIsAByNameHandleNow )
{
	const std::string strCode = StripComments( EmitterSource() );
	ASSERT_FALSE( strCode.empty() );

	EXPECT_NE( std::string::npos,
	           strCode.find( "CManagedConVarRef sv_soundemitter_trace(" ) )
		<< "the by-name handle is gone. Either the declaration came back to C++ "
		   "-- see docs/convar-ledger.tsv -- or the read below it now resolves "
		   "nothing, and a trace knob that reads as off looks exactly like one "
		   "nobody turned on.";

	EXPECT_EQ( std::string::npos,
	           strCode.find( "ConVar sv_soundemitter_trace(" ) )
		<< "a native ConVar sv_soundemitter_trace is back in the emitter. The "
		   "declaration is SourceSharp.{Server,Client}/MoveVars.cs now, and two "
		   "ConVars under one name is neither a compile error nor a link error.";

	EXPECT_NE( std::string::npos, strCode.find( "sv_soundemitter_trace.GetBool()" ) )
		<< "the one read of the trace knob is gone; the handle above then has no "
		   "caller and the gate's output never appears.";
}

//=============================================================================
// (7) The actor-gender lookup was RETYPED, not guarded -- and on the server it
//     no longer goes through the SDK resolve at all.
//=============================================================================

TEST( LaneRSoundDeadPaths, TheServerActorLookupGoesThroughTheEdict )
{
	const std::string strCode = StripComments( EmitterSource() );

	// Two server sites: EmitSoundByHandle and EmitCloseCaption.
	size_t nEdict = 0, i = 0;
	while ( ( i = strCode.find( "GetIServerEntity()", i ) ) != std::string::npos )
	{
		nEdict++;
		i += 1;
	}
	EXPECT_EQ( 2u, nEdict )
		<< "the two server actor-gender lookups should each resolve the edict's "
		   "own IServerEntity -- movehelper_server.cpp's recipe. "
		   "CBaseEntity::Instance answers NULL for every entity this port owns, "
		   "so going back to it silently loses their gender.";

	// And the SDK resolve is gone from the file ALTOGETHER, not merely from the
	// arms this build compiles. The two occurrences that used to remain were
	// inside the guarded owner walks; step 4b deleted those walks, so the
	// server-spelled resolve appears zero times either side of a guard.
	//
	// `CBaseEntity::` is NOT a substring of `C_BaseEntity::` -- the underscore
	// -- so these two needles really do separate the sides, and the count below
	// is about the SERVER spelling only. That is asserted rather than assumed
	// by the fixture below.
	int nSdkCompiled = 0, nSdkOff = 0;
	SplitByGuard( EmitterSource(), "CBaseEntity::Instance( entindex )",
	              &nSdkCompiled, &nSdkOff );
	EXPECT_EQ( 0, nSdkCompiled )
		<< "a SERVER-spelled CBaseEntity::Instance is compiled again. On the "
		   "server the model name is IServerEntity's own virtual and the edict "
		   "answers it.";
	EXPECT_EQ( 0, nSdkOff )
		<< "a guarded SERVER-spelled CBaseEntity::Instance is back. The emitter "
		   "is a shim file since step 4b and the cast census reads its text, so "
		   "a guarded spelling is a counted spelling.";

	// THE CLIENT ARM IS ANSWERED, and this fact is the record of it. It read
	// `EXPECT_EQ( 1, nClientCompiled )` with a message inviting whoever closed
	// the question to come here and say how. plan_entity.md step 7e closed it:
	// the client asks ClientEntityList().GetClientEntity( entindex ) for the
	// renderable and modelinfo for that model's name, which is the same
	// question the server arm asks its edict, at the same engine boundary.
	//
	// It is ZERO NOW, in both directions, and the two directions are not the
	// same claim: not compiled, and not present behind a guard either -- the
	// emitter is a shim file since step 4b and the cast census reads a shim
	// file's TEXT, so a guarded spelling is a counted spelling.
	//
	// WHY IT MATTERED rather than being tidier: C_BaseEntity::Instance answers
	// NULL for every entity on the parallel hierarchy, so every managed
	// entity's sound was emitted GENDER_NONE and a soundscript with
	// gender_male/gender_female entries silently took its fallback.
	int nClientCompiled = 0, nClientOff = 0;
	SplitByGuard( EmitterSource(), "C_BaseEntity::Instance( entindex )",
	              &nClientCompiled, &nClientOff );
	EXPECT_EQ( 0, nClientCompiled )
		<< "the SDK resolve is back in the client actor-gender arm. It answers "
		   "NULL for every entity on the parallel hierarchy, which is the bug "
		   "step 7e removed.";
	EXPECT_EQ( 0, nClientOff )
		<< "a guarded C_BaseEntity::Instance is back in the emitter.";
}

TEST( LaneRSoundDeadPaths, TheTwoScopeSpellingsAreNotSubstringsOfEachOther )
{
	// The single most repeated instrument fault in this port, and it is simply
	// false: `CBaseEntity::` does not occur inside `C_BaseEntity::`. The test
	// above separates the server and client arms on exactly that, so it is
	// asserted here rather than believed.
	const std::string strClient = "C_BaseEntity::Instance( entindex )";
	EXPECT_EQ( std::string::npos, strClient.find( "CBaseEntity::Instance" ) );

	const std::string strServer = "CBaseEntity::Instance( entindex )";
	EXPECT_NE( std::string::npos, strServer.find( "CBaseEntity::Instance" ) );
	EXPECT_EQ( std::string::npos, strServer.find( "C_BaseEntity::Instance" ) );
}

TEST( LaneRSoundDeadPaths, TheClientActorArmAsksTheEntityListAndNotTheEntity )
{
	// THE ANSWER THIS FILE WAS WAITING FOR. It used to require
	// `ent->GetModelName()` to be present and said, in its own failure message,
	// "if the client now answers the model name at an entity index without the
	// entity, that is the answer this file was waiting for -- record it here."
	// plan_entity.md step 7e is that answer, so the fact is inverted rather
	// than deleted: the OLD spelling must be gone and the NEW one present.
	const std::string strCode = StripComments( EmitterSource() );

	EXPECT_EQ( std::string::npos, strCode.find( "ent->GetModelName()" ) )
		<< "the SDK-resolve spelling is back in the client actor-gender arm.";

	// Comments stripped on purpose: this file's own header explains the change
	// in prose and a raw grep would find the explanation instead of the code.
	//
	// LANE sub-entitylist ROUTED THIS LINE AND THE FACT IS THE SAME FACT.
	// 7e's reason -- "the entity list is the engine boundary and it holds both
	// hierarchies; the SDK class holds one" -- has not changed. What changed is
	// WHICH OBJECT holds both: SourceSharp_ClientEntityFromIndex is
	// shell_cliententitylist.h's union, the slot table first and the stock list
	// as the fallback that exists only while stock client entities do, and its
	// body is stock's own GetClientEntity written over that one primitive.
	// plan_entity.md clause 2 forced it -- no compiled TU may name the stock
	// client entity list -- and this was one of the last two in a game/shared
	// shim file. The same amendment p7-exit made to particlemgr.cpp's fact.
	EXPECT_EQ( std::string::npos,
	           strCode.find( "ClientEntityList().GetClientEntity(" ) )
		<< "the stock client entity list is named again in a compiled TU.";
	EXPECT_NE( std::string::npos,
	           strCode.find( "SourceSharp_ClientEntityFromIndex( entindex )" ) )
		<< "the client actor-gender arm no longer resolves the entity index at "
		   "all. It must ask something that holds BOTH hierarchies: the SDK "
		   "class holds one, and every managed entity's sound would go out as "
		   "GENDER_NONE.";
	EXPECT_NE( std::string::npos,
	           strCode.find( "modelinfo->GetModelName( pGenderModel )" ) )
		<< "the client arm stopped turning the renderable's model back into a "
		   "path, so GetActorGender has nothing to parse.";
}

//=============================================================================
// (Z) THE INSTRUMENT'S OWN PROVENANCE -- RETIRED IN PHASE 6b.
//
// Fourteen facts stood here: TheReleaseGraphIsNotReadByThisFile,
// TheDebugGraphIsTheOneConfigurationRead, TheRealDebugGraphIsPresentAndCurrent,
// AMissingGraphIsAHardFailure, AnEmptyGraphIsAHardFailure,
// AGraphThatWillNotSayWhatItCameFromIsAHardFailure,
// AGraphOlderThanOneOfItsOwnInputsIsStale,
// TheStaleMessageNamesTheGuiltyScriptAndBothMtimes,
// AGraphNewerThanEveryInputIsAccepted,
// AnInputTheTreeNoLongerHasDoesNotCountAsStale,
// TheProvenanceRowIsReadOutOfTheGraphAndNotGuessed,
// TheProvenanceRowStopsAtNinjasImplicitDeps,
// AGraphWithNoProvenanceRowYieldsNoInputs and
// TheRealGraphNamesTheTwoGameProjectVpcs. Every one was about VPC's ninja
// graph: which configuration's graph was read, and whether it was present,
// non-empty and newer than the .vpc/.vgc scripts its `regenerate_build` row
// named. That is VPC graph provenance, and VPC is gone. The CMake build is
// read per configuration through SOURCESHARP_BUILD_DIR, and its missing,
// empty and stale inputs are cmake_graph.h's own hard failures, driven by
// cmake_graph_test.
//=============================================================================
