//========= SourceSharp — the seventeen sound statics really left the class ===//
//
// A SOURCE SCANNER, like precache_model_move_test and for the same reason: the
// claim is about the TEXT of files that need a whole engine to run, so the text
// is what is read.
//
// WHAT WAS DONE. sourcesharp/game/shared/sound_emitter.cpp defined twenty-six
// CBaseEntity members out of line. SEVENTEEN of them are declared `static` --
// verified declaration by declaration in game/server/baseentity.h and
// game/client/c_baseentity.h -- and a static member takes no `this`. Their
// bodies are now the free functions GameSound_* declared in
// game/shared/gamesound.h and defined, unmoved, in sound_emitter.cpp. The
// seventeen class-scope names are one-line forwarders, quarantined together in
// game/shared/baseentity_sound_forwarders.cpp.
//
// The remaining NINE are not static, take a `this`, and stayed put.
//
// WHY THIS NEEDS A GUARD, AND IT IS NOT PEDANTRY. Three ways this move rots,
// none of which produces a diagnostic:
//
//   1. Somebody adds logic to a forwarder. Then there are two implementations
//      of emitting a sound: the SDK's hundreds of unqualified call sites take
//      the forwarder's, the port's own files take the free function's, and they
//      drift. The build stays green in both directions.
//   2. Somebody writes the eighteenth static member definition back into
//      sound_emitter.cpp, on the reasonable-looking grounds that all its
//      neighbours are there. Phase 13 step 4 then fails to compile on a file
//      that had been cleared, and the failure names a line nobody connects to
//      this move.
//   3. Somebody moves one of the nine NON-static members into the quarantine
//      file to tidy up. It would compile. It would also mean step 3 could no
//      longer delete that file wholesale, which is the only reason it exists.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given. The fixtures at the bottom assert that the emitter
// and gamesound.h were really opened and are not trivial, and that the
// definition extractor tells a DEFINITION from a CALL (this file's subject is
// full of both: sound_emitter.cpp calls CBaseEntity::Instance three times and
// names "CBaseEntity::EmitSound" in six VPROF string literals).
//
// baseentity_sound_members.cpp (the nine non-statics), and five stock callers
// (baseentity.cpp, movehelper_server.cpp, saverestore.cpp, c_baseanimating.cpp,
// c_baseviewmodel.cpp) are no longer compiled by either DLL and have left the
// source tree; the checks and the body extractor that read only them were
// culled with them.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <ctype.h>
#include <dirent.h>
#include <string.h>		// strcmp, for the dead-arm caller list

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
const char *SourceRoot()
{
	static const char *kCandidates[] = { "game", "src/game", "../src/game" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		DIR *pDir = opendir( kCandidates[ i ] );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

// Relative to game/, so the callers under server/, client/ and
// ../sourcesharp/ can be read too.
std::string ReadGameFile( const char *pszRelative )
{
	const char *pszRoot = SourceRoot();
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
	// Under ../sourcesharp/ since plan_entity.md step 4b: the emitter is a shim
	// mechanism file now. ReadGameFile reaches it because its root is game/ and
	// src/sourcesharp is game/'s sibling -- the same route this file's caller
	// table already uses for the shim files on it.
	static const std::string s =
		ReadGameFile( "../sourcesharp/game/shared/sound_emitter.cpp" );
	return s;
}

//-----------------------------------------------------------------------------
// Does a path under game/ exist at all? ReadSharedFile cannot answer it: it
// returns the empty string BOTH for a file that is not there and for one that
// is there and empty.
//-----------------------------------------------------------------------------
bool GameFileExists( const char *pszRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return false;

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += pszRelative;

	std::ifstream file( strPath.c_str() );
	return file.is_open();
}

// The path baseentity_sound_forwarders.cpp used to occupy, under game/.
const char *const kForwardersPath = "shared/baseentity_sound_forwarders.cpp";

//-----------------------------------------------------------------------------
// THE CALLERS, one entry per file behind an object rta_referrers.sh named --
// fourteen on the server link line, nineteen on the client, and the header
// whose INLINES put the demand into physics.o on both sides. It is twenty-nine
// rows rather than thirty since §6's fx_impact.cpp row landed; the note on
// shell_impact_effects.cpp below is where that one went and why it is the only
// row so far to leave rather than move.
//
// Paths are relative to game/ where they start "server/", "shared/" or
// "client/", and reached with `../sourcesharp/` otherwise.
//-----------------------------------------------------------------------------
//
// FIVE ROWS LEFT WITH THEIR FILES: server/baseentity.cpp,
// server/movehelper_server.cpp, shared/saverestore.cpp,
// client/c_baseanimating.cpp and client/c_baseviewmodel.cpp. Neither
// client.so nor server.so compiles them any more, so they are not in the
// source tree; a row naming one could only fail to load.
const char *const kCallers[] =
{
	// physics.cpp LEFT this list in plan_physics_bones_native_zero 2.3, as a
	// DROP: its queued impact and break sounds are managed's PhysicsSoundBatch,
	// and the one GameSound_GetParameters / GameSound_Emit pair they needed is
	// shell_sound.cpp's EmitWorldSoundScript, which is already a row below.
	// physics.cpp names no GameSound_* function now.
	// precache_model.cpp LEFT this list in plan_server_novalve.md P3: its one
	// GameSound_* call went with the model sounds cache, to C#.
	// server/sound.cpp's SUCCESSOR. plan_entity.md step 4a took that file out of
	// server_csharp.vpc and off disk: its three link-demanded functions moved
	// here and its ten unreferenced ones were deleted. The one emit that
	// survived is SENTENCEG_PlaySequentialSz's, and it still names
	// GameSound_EmitSentenceByIndex directly rather than the class forwarder --
	// which is exactly what this list is for, so the entry MOVES rather than
	// going away. ReadGameFile fails loudly on a path it cannot open, so a
	// stale one here would have been red and never vacuous.
	// AND IT LEFT IN THE SERVER GC CULL (2026-10-01): SENTENCEG_PlaySequentialSz
	// had no caller, so it and its emit went; shell_sentences.cpp names no
	// GameSound_* function now.
	// physics_shared.cpp LEFT this list in plan_physics_bones_native_zero 2.6,
	// as a DROP: its one call, PrecachePhysicsSounds' GameSound_PrecacheScript,
	// is managed's SurfaceSoundPrecache (the server's waves through the
	// PrecacheSound slot, the index from the sound emitter's table), and the
	// file left the repository.
	// shared/soundenvelope.cpp left client.so too (sound cluster 17, lane
	// c2-sims); its patches' emits and stops are the client shim's now.
	"../sourcesharp/game/client/shell_soundenvelope_client.cpp",
	// shared/vphysics_sound.h left the farm with fork client/physics.cpp, its
	// one includer (2026-10-18): the impact list is ClientImpactSounds' (C#).
	// client/cdll_util.cpp left client.so in C8 (lane c6-input) with no
	// importer; it was the dead-arm row below.
	// Step 7a moved all 1,391 compiled lines of game/client/c_impact_effects.cpp
	// to the shim and took the stock TU out of client_csharp.vpc. The
	// GameSound_* calls went with them, so this entry MOVED rather than going
	// away -- the same shape as shell_sentences.cpp above, and kNumCallers was
	// unchanged at 30 because a move is not an addition.
	//
	// IT NOW CARRIES A SECOND FILE'S EMIT AS WELL, and THAT one is a drop.
	// plan_entity.md §6's LEAVE row for game/client/fx_impact.cpp landed here
	// too: PlayImpactSound moved into this same shim, and its
	// `GameSound_Emit( filter, NULL, pbulletImpactSoundName, ... )` was the ONE
	// GameSound_ call fx_impact.cpp had. So `client/fx_impact.cpp` leaves this
	// list -- 30 -> 29 -- and it leaves as a DROP rather than a move because the
	// destination was already on the list and must not be counted twice. What is
	// left in fx_impact.cpp is CRagdollEnumerator, FX_AffectRagdolls,
	// RagdollImpactCallback and Impact(), inside `#ifndef SOURCESHARP`, and not
	// one of them emits a sound: TheCallersNameTheFreeFunctions below would have
	// gone red on the row, which is the check that made this a deletion rather
	// than a judgement call. The `#include "gamesound.h"` went with the call.
	"../sourcesharp/game/client/shell_impact_effects.cpp",
	// clientmode_shared.cpp left the client (C3 step 4, lane ui); its one sound,
	// the team-255 broadcast's GameSound_Emit, is the client mode's in shell_client.cpp.
	"../sourcesharp/game/client/shell_client.cpp",
	// clientsideeffects_test.cpp's FX_TracerSound is C# now (C2, lane c2-fx);
	// its GameSound_GetParameters went to the near-miss executor.
	"../sourcesharp/game/client/shell_effects_list_client.cpp",
	// plan_entity.md step 7e moved the soundscape file to the shim; sound
	// cluster 17 (lane c2-sims) left only its executors native. Path relative
	// to game/, which is why it climbs out with "../".
	"../sourcesharp/game/client/shell_soundscape_audio_client.cpp",
	// c_te_legacytempents.cpp's PlaySound is C# now (C2, lane c2-fx, TempEntSounds);
	// its GameSound_GetParameters and GameSound_Emit went to the pool's executors.
	"../sourcesharp/game/client/shell_te_pool_client.cpp",
	// fx_explosion.cpp left client.so (C5, lane c5-particles): its sounds are
	// ExplosionEffects' (C#), emitted through the game-sound executor.
	// fx.cpp left client.so (C5, lane c5-particles): FX_RicochetSound is
	// FxEffects' (C#), emitted through the game-sound executor.
	// fx_water.cpp left client.so (C5, lane c5-particles): its splash sound is
	// WaterSplashes' (C#, SplashEnvironment.SplashSound).
	// movehelper_client.cpp left with C6 (lane c6-input): its StartSound is
	// ClientPredictionMoveWorld's, through ClientEmitSound; the client
	// physics' impact emit is shell_physics_client.cpp's.
	"../sourcesharp/game/client/shell_physics_client.cpp",
	// client/physics.cpp (fork) left client.so (2026-10-18): its friction
	// sound's GameSound_GetParameters was a dead arm (the slot is managed's
	// ClientFriction), and nothing else of it called a sound.
	"../sourcesharp/game/server/shell_absent.cpp",
	"../sourcesharp/game/server/shell_sound.cpp",
	"../sourcesharp/game/server/shell_standalone_player.cpp",
	"../sourcesharp/game/server/shell_worldquery.cpp",
	"../sourcesharp/game/server/world_adapter.cpp",
	"../sourcesharp/game/client/shell_standalone_client.cpp",
	"../sourcesharp/game/client/shell_explosion_overlay.cpp",	// RPGShotDown, from shell_trail_sim.cpp (lane c2-sims)
};
const size_t kNumCallers = sizeof( kCallers ) / sizeof( kCallers[ 0 ] );

//-----------------------------------------------------------------------------
// THE ROWS WHOSE CALL WAS NEVER COMPILED -- added by the `residue` wave
// (74b071771..a46e8d645).
//
// kCallers was built from rta_referrers.sh, which reads SOURCE. A file whose
// only GameSound_* call sat inside a guard the build never takes therefore got
// onto the list without its OBJECT ever needing one of the seventeen statics.
// The wave deleted those dead arms, and the rows went quiet.
//
// Such a row is NOT dropped from kCallers: NoCallerReachesALiftedNameThroughThe
// Class still has to scan the file, and dropping it would lose that. It is
// listed here instead, and TheCallersNameTheFreeFunctions asserts the ABSENCE
// for it rather than the presence -- which is the stronger statement and the
// one that is actually true (residue brief rule 4).
//
//   client/cdll_util.cpp
//       Its one call was `GameSound_Emit( localFilter, pLocalPlayer->entindex(),
//       params )` at cdll_util.cpp:1460 at 74b071771, inside
//       `#ifndef SS_CBASEENTITY_ALIAS` (cdll_util.cpp:1414-1465) in the arrow
//       near-miss helper. SS_CBASEENTITY_ALIAS is defined in every
//       configuration this tree builds, so it was never compiled. Confirmed
//       against the link rather than the source: `nm -u` on
//       src/_vpc_/ninja/debug/objs/client_sourcesharp/cdll_util.o names no
//       GameSound_ symbol at all. The file's surviving
//       `#include "gamesound.h"` at cdll_util.cpp:42 is now unused; that is a
//       stock-source edit and is reported rather than made here.
//-----------------------------------------------------------------------------
// The one row this list held, client/cdll_util.cpp, left client.so in C8
// (lane c6-input). The list is empty, so the mechanism went with it; a file
// whose only call is in a dead arm would be a new decision, argued here again.

const std::string &GameSoundHeader()
{
	static const std::string s = ReadGameFile( "../sourcesharp/game/shared/gamesound.h" );
	return s;
}

//-----------------------------------------------------------------------------
// Strip // and /* */ comments, so that a CBaseEntity:: name written in prose --
// which both files under test are full of, being about exactly that -- is not
// mistaken for code.
//
// Not a C++ lexer: it does not track string or character literals. It does not
// need to, because what is built on top of it is a DEFINITION detector, and a
// definition cannot occur inside a string literal. The literals that do exist
// here -- VPROF_BUDGET( "CBaseEntity::EmitSound", ... ) six times, and
// Assert( !"CBaseEntity::PrecacheSound:  too late" ) -- are all followed by a
// quote and a comma or paren rather than by an argument list and a brace, so
// the detector rejects them. That rejection is asserted below rather than
// assumed.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );

	for ( size_t i = 0; i < s.size(); )
	{
		if ( i + 1 < s.size() && s[ i ] == '/' && s[ i + 1 ] == '/' )
		{
			while ( i < s.size() && s[ i ] != '\n' )
				i++;
			continue;
		}
		if ( i + 1 < s.size() && s[ i ] == '/' && s[ i + 1 ] == '*' )
		{
			i += 2;
			while ( i + 1 < s.size() && !( s[ i ] == '*' && s[ i + 1 ] == '/' ) )
				i++;
			i = ( i + 1 < s.size() ) ? i + 2 : s.size();
			continue;
		}
		out += s[ i ];
		i++;
	}
	return out;
}

std::string Squeezed( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\r' && c != '\n' )
			out += c;
	}
	return out;
}

//-----------------------------------------------------------------------------
// Skip a balanced parenthesised run starting at strCode[at] == '('. Returns the
// index just past the closing paren, or npos.
//-----------------------------------------------------------------------------
size_t SkipParens( const std::string &strCode, size_t at )
{
	if ( at >= strCode.size() || strCode[ at ] != '(' )
		return std::string::npos;

	int depth = 0;
	for ( size_t i = at; i < strCode.size(); i++ )
	{
		if ( strCode[ i ] == '(' ) depth++;
		else if ( strCode[ i ] == ')' )
		{
			depth--;
			if ( depth == 0 )
				return i + 1;
		}
	}
	return std::string::npos;
}

//-----------------------------------------------------------------------------
// Every member name DEFINED out of line as `<Class>::<Name>( ... ) {`.
//
// The discriminator between a definition and a call is what follows the
// argument list: a definition's next non-space character is '{' (allowing for
// a trailing `const`), a call's is ';' or ',' or ')' or an operator. That is
// what keeps CBaseEntity::Instance( entindex ) -- a CALL, three of them in
// sound_emitter.cpp -- out of the answer, and it is what keeps a
// CBaseEntity:: name inside a string literal out too.
//
// Overloads collapse: the answer is a set of NAMES, and the counts asserted
// against it are counts of distinct names, stated as such at each use.
//-----------------------------------------------------------------------------
std::set< std::string > MembersDefinedIn( const std::string &strCode, const char *pszScope )
{
	std::set< std::string > names;
	std::string strNeedle( pszScope );
	strNeedle += "::";

	size_t at = 0;
	for ( ;; )
	{
		at = strCode.find( strNeedle, at );
		if ( at == std::string::npos )
			break;

		size_t p = at + strNeedle.size();
		at = p;

		std::string strName;
		while ( p < strCode.size() &&
		        ( isalnum( (unsigned char)strCode[ p ] ) || strCode[ p ] == '_' ) )
		{
			strName += strCode[ p ];
			p++;
		}
		if ( strName.empty() )
			continue;

		while ( p < strCode.size() && ( strCode[ p ] == ' ' || strCode[ p ] == '\t' ) )
			p++;
		if ( p >= strCode.size() || strCode[ p ] != '(' )
			continue;

		p = SkipParens( strCode, p );
		if ( p == std::string::npos )
			continue;

		while ( p < strCode.size() && isspace( (unsigned char)strCode[ p ] ) )
			p++;
		// A const member function's brace comes after the const.
		if ( strCode.compare( p, 5, "const" ) == 0 )
		{
			p += 5;
			while ( p < strCode.size() && isspace( (unsigned char)strCode[ p ] ) )
				p++;
		}
		if ( p < strCode.size() && strCode[ p ] == '{' )
			names.insert( strName );
	}
	return names;
}

//-----------------------------------------------------------------------------
// Definitions of a FREE function `<Name>( ... ) {`, counted. Same
// definition-versus-call discriminator as MembersDefinedIn.
//-----------------------------------------------------------------------------
int FreeDefinitionCount( const std::string &strCode, const char *pszName )
{
	int nFound = 0;
	const std::string strName( pszName );

	size_t at = 0;
	for ( ;; )
	{
		at = strCode.find( strName, at );
		if ( at == std::string::npos )
			break;

		size_t p = at + strName.size();
		at = p;

		if ( p < strCode.size() &&
		     ( isalnum( (unsigned char)strCode[ p ] ) || strCode[ p ] == '_' ) )
			continue;

		while ( p < strCode.size() && ( strCode[ p ] == ' ' || strCode[ p ] == '\t' ) )
			p++;
		if ( p >= strCode.size() || strCode[ p ] != '(' )
			continue;

		p = SkipParens( strCode, p );
		if ( p == std::string::npos )
			continue;

		while ( p < strCode.size() && isspace( (unsigned char)strCode[ p ] ) )
			p++;
		if ( p < strCode.size() && strCode[ p ] == '{' )
			nFound++;
	}
	return nFound;
}

//-----------------------------------------------------------------------------
// The seventeen statics, and the free function each one must forward to.
//-----------------------------------------------------------------------------
struct Lift_t
{
	const char *pszMember;
	const char *pszFree;
	int nOverloads;
};

const Lift_t kLifts[] =
{
	{ "EmitSound",             "GameSound_Emit",             4 },
	{ "StopSound",             "GameSound_Stop",             2 },
	{ "GetParametersForSound", "GameSound_GetParameters",    2 },
	{ "EmitCloseCaption",      "GameSound_EmitCloseCaption", 1 },
	{ "PrecacheScriptSound",   "GameSound_PrecacheScript",   1 },
	{ "PrecacheSound",         "GameSound_Precache",         1 },
	{ "PrefetchSound",         "GameSound_Prefetch",         1 },
};
const size_t kNumLifts = sizeof( kLifts ) / sizeof( kLifts[ 0 ] );

// Seven distinct names, twelve definitions. WAS eleven and seventeen.
const int kLiftedDefinitions = 12;

//-----------------------------------------------------------------------------
// THE FIVE DEFINITIONS PHASE 3f DELETED -- plan_shim.md §5 3f, lane 3f.
//
// GameSound_LookupSoundLevel (both overloads), GameSound_Duration,
// GameSound_EmitAmbient and GameSound_PrefetchScript. Each had 0 importers on
// either link line AND no caller inside sound_emitter.cpp -- BOTH halves,
// because a symbol census cannot see the second one and four of this file's
// other orphans (GameSound_Prefetch, WaveTrace, Hack_FixEscapeChars,
// SENTENCEG_Lookup) do have an in-TU caller and are therefore alive. That is
// why GameSound_Prefetch is still in the table above and its sibling
// GameSound_PrefetchScript is not.
//
// THE LIFT THIS FILE IS ABOUT IS NOT WEAKENED BY THEIR GOING, and the
// distinction is worth stating because the two look alike from the count. The
// lift's claim is "the body is HERE, not in the class" -- a claim about
// LOCATION. These five have no body anywhere, which is a different and stronger
// state: there is nothing left to be in the wrong place. The rows are kept as
// absences rather than dropped so that a lane restoring one has to come here
// and decide which of the two claims it is making.
//-----------------------------------------------------------------------------
const char *const kDeletedFrees[] = {
	"GameSound_LookupSoundLevel",
	"GameSound_Duration",
	"GameSound_EmitAmbient",
	"GameSound_PrefetchScript",
};
const size_t kNumDeletedFrees = sizeof( kDeletedFrees ) / sizeof( kDeletedFrees[ 0 ] );

} // namespace

//=============================================================================//
// Claim 1 -- the quarantine is RETIRED, and no caller reaches a lifted name
// through the class any more.
//
// It used to be "the seventeen names are in the quarantine file and nowhere
// else". The file is gone: rta_referrers.sh named fourteen referring objects on
// the server link line and nineteen on the client, and every file behind one of
// them now calls GameSound_* directly. The DECLARATIONS stay in baseentity.h
// and c_baseentity.h -- the SDK reaches these from hundreds of sites in files
// server_csharp.vpc does not compile -- so what left is the DEFINITIONS, which
// is what the removal set counts.
//=============================================================================//

TEST( SoundEmitterLift, TheQuarantineIsRetired )
{
	EXPECT_FALSE( GameFileExists( kForwardersPath ) )
		<< "game/" << kForwardersPath << " is back. Its seventeen names had 78 "
		<< "call sites across 31 files between them, and all 78 now name the "
		<< "GameSound_* free function.";
}

TEST( SoundEmitterLift, NoCallerReachesALiftedNameThroughTheClass )
{
	for ( size_t i = 0; i < kNumCallers; i++ )
	{
		const std::string strSrc = StripComments( ReadGameFile( kCallers[ i ] ) );
		ASSERT_FALSE( strSrc.empty() ) << kCallers[ i ] << " did not load";

		for ( size_t k = 0; k < kNumLifts; k++ )
		{
			for ( int scope = 0; scope < 2; scope++ )
			{
				const std::string strNeedle =
					std::string( scope ? "C_BaseEntity::" : "CBaseEntity::" ) +
					kLifts[ k ].pszMember;

				EXPECT_EQ( std::string::npos, strSrc.find( strNeedle ) )
					<< kCallers[ i ] << " names " << strNeedle << " again. "
					<< "Nothing defines the class-scope form in this build; it "
					<< "must call " << kLifts[ k ].pszFree << ".";
			}
		}
	}
}

//-----------------------------------------------------------------------------
// ...and the rewrites are really there. Without this, every assertion above is
// satisfied by callers that lost their sound calls altogether -- which is a
// silent failure: a game with no sound still runs.
//-----------------------------------------------------------------------------
TEST( SoundEmitterLift, TheCallersNameTheFreeFunctions )
{
	size_t nFilesWithACall = 0;

	for ( size_t i = 0; i < kNumCallers; i++ )
	{
		const std::string strSrc = StripComments( ReadGameFile( kCallers[ i ] ) );
		ASSERT_FALSE( strSrc.empty() ) << kCallers[ i ] << " did not load";

		bool bAny = false;
		for ( size_t k = 0; k < kNumLifts; k++ )
			bAny = bAny || ( strSrc.find( kLifts[ k ].pszFree ) != std::string::npos );


		EXPECT_TRUE( bAny )
			<< kCallers[ i ] << " names no GameSound_* function at all. It is on "
			<< "this list because its OBJECT needed one of the seventeen statics "
			<< "at link time, so it must call the free function now -- if the "
			<< "call was deleted rather than rewritten, the sound simply stops "
			<< "happening and nothing else notices.";

		if ( bAny )
			nFilesWithACall++;
	}

	// Every row names a free function: none is exempt.
	EXPECT_EQ( kNumCallers, nFilesWithACall );
}

//=============================================================================//
// Claim 2 -- "what is in the quarantine file is a forwarder and nothing else"
// WAS HERE. There is no quarantine file. What that claim defended -- that a
// forwarder never grows a body, because a second implementation of the sound
// path would mean the SDK's call sites and the port's own doing different
// things -- is now true by construction: there is one implementation, in
// sound_emitter.cpp, and one way to reach it.
//=============================================================================//

//=============================================================================//
// Claim 3 -- the bodies really are free functions, and only there.
//=============================================================================//

TEST( SoundEmitterLift, EveryFreeFunctionIsDeclaredInGamesoundHeader )
{
	const std::string strHeader = Squeezed( StripComments( GameSoundHeader() ) );

	for ( size_t i = 0; i < kNumLifts; i++ )
	{
		const std::string strNeedle = std::string( kLifts[ i ].pszFree ) + "(";
		EXPECT_NE( std::string::npos, strHeader.find( strNeedle ) )
			<< kLifts[ i ].pszFree << " is not declared in gamesound.h";
	}
}

TEST( SoundEmitterLift, TheFourDeletedFreeFunctionsHaveNoBodyAtAll )
{
	const std::string strCode = StripComments( EmitterSource() );
	ASSERT_FALSE( strCode.empty() ) << "the emitter source did not load";

	// Positive control. FreeDefinitionCount answering 0 for everything would
	// pass this whole test, and it is the shape that has gone unnoticed in this
	// repo more than once -- so one name that IS there is counted first.
	ASSERT_EQ( 4, FreeDefinitionCount( strCode, "GameSound_Emit" ) )
		<< "GameSound_Emit's four definitions are not in sound_emitter.cpp, so "
		   "the counter is not reading the file this test thinks it is and "
		   "every zero below means nothing";

	for ( size_t i = 0; i < kNumDeletedFrees; i++ )
	{
		EXPECT_EQ( 0, FreeDefinitionCount( strCode, kDeletedFrees[ i ] ) )
			<< kDeletedFrees[ i ] << " has a body again. It had 0 importers on "
			   "either link line and no in-TU caller when it went; if it has "
			   "grown either, add it back to kLifts and raise "
			   "kLiftedDefinitions, rather than leaving two tables disagreeing.";
	}
}

TEST( SoundEmitterLift, EveryFreeFunctionIsDefinedInSoundEmitterSystemCpp )
{
	const std::string strCode = StripComments( EmitterSource() );

	int nTotal = 0;
	for ( size_t i = 0; i < kNumLifts; i++ )
	{
		const int nFound = FreeDefinitionCount( strCode, kLifts[ i ].pszFree );
		nTotal += nFound;

		EXPECT_EQ( kLifts[ i ].nOverloads, nFound )
			<< "expected " << kLifts[ i ].nOverloads << " definitions of "
			<< kLifts[ i ].pszFree << " in sound_emitter.cpp, found " << nFound
			<< ". The lift is only real if the BODY is here.";
	}

	EXPECT_EQ( kLiftedDefinitions, nTotal );
}

//=============================================================================//
// Claims 4-6 -- that game/shared/baseentity_sound_members.cpp held exactly the
// nine non-static definitions -- were culled when that file left the source
// tree (neither DLL compiles it). The emitter-side half stands below.
//=============================================================================//

//=============================================================================//
// The scanner's own fixtures. Everything above is worthless if these fail.
//=============================================================================//

TEST( SoundEmitterLiftScanner, TheEmitterAndItsHeaderWereReallyOpenedAndAreNotTrivial )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no game/ directory found from the working directory -- the scanner read "
		<< "nothing and every claim above passed vacuously";

	EXPECT_GT( EmitterSource().size(), (size_t)30000 )
		<< "sound_emitter.cpp came back too small to be the real file";
	EXPECT_GT( GameSoundHeader().size(), (size_t)2000 )
		<< "gamesound.h came back too small to be the real file";
}

//-----------------------------------------------------------------------------
// THE MOVE'S OWN INVARIANT, and it is the one step 4b exists for: the emitter
// defines NO entity member.
//
// A tenth definition sitting in the emitter is exactly what a merge that
// resurrects a hunk produces. This is what makes sourcesharp/game/shared/sound_emitter.cpp a
// shim mechanism file rather than a stock one wearing a shim path.
//-----------------------------------------------------------------------------
TEST( SoundEmitterLift, TheEmitterDefinesNoEntityMemberAtAll )
{
	const std::string strCode = StripComments( EmitterSource() );

	const std::set< std::string > server = MembersDefinedIn( strCode, "CBaseEntity" );
	EXPECT_TRUE( server.empty() )
		<< "sound_emitter.cpp defines " << server.size() << " CBaseEntity member(s); "
		<< "the nine that took a `this` went to game/shared/"
		<< "baseentity_sound_members.cpp at step 4b and the rest are free functions";

	const std::set< std::string > client = MembersDefinedIn( strCode, "C_BaseEntity" );
	EXPECT_TRUE( client.empty() )
		<< "sound_emitter.cpp defines " << client.size() << " C_BaseEntity member(s). "
		<< "The client compiles this file too, and the same rule holds there.";
}

TEST( SoundEmitterLiftScanner, StripCommentsRemovesBothCommentForms )
{
	EXPECT_EQ( std::string( "a\nb" ), StripComments( "a// CBaseEntity::Nope\nb" ) );
	EXPECT_EQ( std::string( "ab" ), StripComments( "a/* CBaseEntity::Nope */b" ) );
	EXPECT_EQ( std::string( "a/b" ), StripComments( "a/b" ) );
}

//-----------------------------------------------------------------------------
// A DEFINITION is found; a CALL, a DECLARATION and a STRING LITERAL are not.
//
// All four shapes are live in sound_emitter.cpp. Getting this wrong in the
// permissive direction makes claim 4 fail for no reason; getting it wrong in the
// strict direction makes claims 1 and 4 pass against an empty set.
//-----------------------------------------------------------------------------
TEST( SoundEmitterLiftScanner, DefinitionDetectorTellsDefinitionsFromEverythingElse )
{
	const std::string strFixture =
		"void Foo::Called() { Foo::Instance( 3 ); }\n"            // Called defined, Instance called
		"void Foo::Declared( int );\n"                             // declaration only
		"void trace() { VPROF( \"Foo::Literal\", 1 ); }\n"         // string literal
		"int Foo::WithConst( int n ) const { return n; }\n";       // const member

	const std::set< std::string > got = MembersDefinedIn( strFixture, "Foo" );

	EXPECT_EQ( (size_t)2, got.size() ) << "expected exactly Called and WithConst";
	EXPECT_EQ( (size_t)1, got.count( "Called" ) );
	EXPECT_EQ( (size_t)1, got.count( "WithConst" ) );
	EXPECT_EQ( (size_t)0, got.count( "Instance" ) )  << "a CALL was read as a definition";
	EXPECT_EQ( (size_t)0, got.count( "Declared" ) )  << "a DECLARATION was read as a definition";
	EXPECT_EQ( (size_t)0, got.count( "Literal" ) )   << "a STRING LITERAL was read as a definition";
}

//-----------------------------------------------------------------------------
// The real file exercises the string-literal case, so assert against the real
// file too rather than only the fixture. PrecacheSound is the discriminating
// one: after the lift it appears in sound_emitter.cpp ONLY inside
// Assert( !"CBaseEntity::PrecacheSound:  too late" ), so a scanner that read
// literals would report it as a definition and claim 4 would fail loudly --
// which is the right direction, but only because this fixture says the literal
// is still there to be misread.
//-----------------------------------------------------------------------------
TEST( SoundEmitterLiftScanner, TheAssertLiteralInTheRealFileIsNotReadAsADefinition )
{
	const std::string strCode = StripComments( EmitterSource() );

	ASSERT_NE( std::string::npos, strCode.find( "CBaseEntity::PrecacheSound" ) )
		<< "the Assert literal naming CBaseEntity::PrecacheSound is gone from "
		<< "sound_emitter.cpp, so this fixture no longer tests anything";

	EXPECT_EQ( (size_t)0, MembersDefinedIn( strCode, "CBaseEntity" ).count( "PrecacheSound" ) )
		<< "the scanner read a string literal as a definition";
}

TEST( SoundEmitterLiftScanner, FreeDefinitionCountRequiresTheNameToEndToo )
{
	const std::string strFixture =
		"void GameSound_EmitAmbient( int n ) { ambient(); }\n"
		"void GameSound_Emit( int n ) { plain(); }\n"
		"void caller() { GameSound_Emit( 1 ); }\n";

	EXPECT_EQ( 1, FreeDefinitionCount( strFixture, "GameSound_Emit" ) )
		<< "GameSound_EmitAmbient, or the call, was counted as GameSound_Emit";
	EXPECT_EQ( 1, FreeDefinitionCount( strFixture, "GameSound_EmitAmbient" ) );
}

