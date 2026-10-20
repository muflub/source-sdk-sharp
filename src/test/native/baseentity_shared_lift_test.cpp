//========= SourceSharp — baseentity_shared.cpp's five really left the class ==//
//
// A SOURCE SCANNER, like physicslink_lift_test, soundemitter_lift_test and
// precache_model_move_test, and for the same reason: the claim is about the
// TEXT of files that need a whole engine to run, so the text is what is read.
// buildcpptests links each test binary against gtest and nothing else, so
// calling the real EntitySim_IsSimulatingOnAlternateTicks here is not
// available; re-implementing it in the test to "check the logic" would be a
// check that cannot fail, since it would only compare a copy against itself.
//
// WHAT WAS DONE. game/shared/baseentity_shared.cpp defined EIGHTY-FIVE
// CBaseEntity members out of line on the server and EIGHTY-SIX on the client
// (the extra is IsToolRecording, which is inside #ifdef CLIENT_DLL). FIVE of
// them take no `this`:
//
//   FOUR are already declared `static`, declaration by declaration, in
//   game/server/baseentity.h (1666, 1667, 1671, 2071) and
//   game/client/c_baseentity.h (640, 641, 645, 1249) -- IsPrecacheAllowed,
//   SetAllowPrecache, IsSimulatingOnAlternateTicks, SetPredictionRandomSeed.
//   A static member takes no `this`; the class scope was only ever being used
//   as a namespace.
//
//   The FIFTH, ComputeTracerStartPosition, is NOT declared static and is not
//   virtual on either side, and it reads no member: both arms that ever did --
//   the player eye-vector offset and the weapon muzzle attachment -- are behind
//   `#ifndef SOURCESHARP`, and SOURCESHARP is defined for both link lines. Same
//   terms as Physics_ClipVelocity at 1c75302c.
//
// Their bodies are now the free functions EntityPrecache_* /
// EntitySim_IsSimulatingOnAlternateTicks / EntityPrediction_SetSeedFromCommand
// / EntityTracer_ComputeStartPosition declared in
// game/shared/entity_shared_statics.h. The precache pair is DEFINED in that
// header, because the flag they read is the static data member below and the
// storage had to leave the class with them; the other three are defined,
// UNMOVED, in baseentity_shared.cpp, because they reach that translation unit's
// own objects (the sv_alternateticks ConVar, g_pGameRules).
//
// THE FIVE CLASS-SCOPE NAMES ARE GONE FROM THIS BUILD. They spent one phase as
// one-line forwarders quarantined in game/shared/baseentity_shared_forwarders.cpp;
// that file is retired. rta_referrers.sh named its demand exactly -- EIGHT
// objects on the server link line and FIVE on the client -- and every one of
// those files now calls the free function directly, 21 call sites in 11 files.
//
// The DECLARATIONS stay in baseentity.h and c_baseentity.h. SDK files outside
// this build (tf/, hl2/, the vscript and weapon paths) still reach
// CBaseEntity::IsPrecacheAllowed and SetAllowPrecache through the class, and
// server_csharp.vpc simply does not compile them. What the removal set counts
// is out-of-line DEFINITIONS, and that is what left.
//
// THE STATIC DATA MEMBER IS THE HALF THAT MATTERS MOST, and it is the one no
// consumer count can see. `bool CBaseEntity::m_bAllowPrecache = false;` was at
// baseentity_shared.cpp:85 -- one per side, this file's whole share of the
// tree's SIXTEEN. A static data member of an alias does not fail to LINK. It
// stops PARSING, everywhere, at once, the moment step 4 writes
// `using CBaseEntity = IGameEntity;`. So it is a strictly worse failure than
// the undefined symbols step 3 tracks, and no amount of consumer work reaches
// it. The storage is now EntityPrecache_AllowedRef() in
// entity_shared_statics.h, on entity_prediction_seed.h's shape.
//
// AND THREE MEMBERS WERE DELETED, each in its own revertible commit, each
// proven dead by `nm --undefined-only` over every object on both link lines
// naming no referrer AND a tree-wide source grep finding nothing but the
// definition and its declarations: ApplyLocalVelocityImpulse,
// GetLastThinkTick( const char * ), GetNextThink( int ) const. None is virtual,
// so the linker really did see every caller -- a virtual call emits no symbol.
// Pinned below as absences from all three files, because a deletion that gets
// typed back is exactly as invisible as one that never happened.
//
// Measured with the corrected pipeline from plan_baseentity.md Phase 13 note B
// (`nm --defined-only OBJ | awk '$2=="T"{print $3}' | c++filt |
//   grep -c '^C_\?BaseEntity::'`):
//
//   baseentity_shared.o            before 85/86   after lift 80/81   after deletes 77/78
//   baseentity_shared_forwarders.o                after lift  5/5   RETIRED 0/0
//
// 80 + 5 = 85 and 81 + 5 = 86: the lift changed no symbol the link is given.
// The three deletions then took 3 off each side, and those are real removals.
// Retiring the quarantine took the remaining 5/5 off the removal set outright,
// which is a removal of DEFINITIONS and not of symbols the link is given: the
// callers moved to the free functions in the same commit, so no undefined
// reference was created for the linker to resolve.
//
// NEITHER client.so NOR server.so COMPILES baseentity_shared.cpp ANY MORE, and
// it has left the source tree, along with server/util.cpp, physics_main.cpp,
// baseanimating.cpp and client/c_baseentity.cpp. The checks that read those
// files -- the 78-member count, "no longer defines the five", the static data
// member detector over the file, the ImpactTrace NULL-subject inventory, the
// stock callers' rows -- and the extractors that existed only for them were
// culled with them. What is left pins the live files: the free functions in
// entity_shared_statics.h/.cpp, the shim callers, and the two entity headers.
//
// WHY THIS NEEDS A GUARD, AND IT IS NOT PEDANTRY. None of these produces a
// diagnostic:
//
//   1. Somebody drops a `static`-free declaration into entity_shared_statics.h
//      as `static`. At namespace scope in a header that is internal linkage:
//      every TU gets its own precache flag, and nothing diagnoses it.
//   2. Somebody makes one of the five virtual again by moving it back into a
//      class. A virtual cannot be a lift.
//   3. A caller quietly reintroduces a class-scope call, or loses its call to
//      the free function altogether. Both directions are counted per file.
//
// THE SCANNER IS ITSELF CHECKED. A source scanner that reads nothing passes
// every claim it is given. The fixtures at the bottom assert that the files
// were really opened and are not trivial, and prove the static, virtual and
// mention detectors on hand-written positives and on a live header.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <fstream>
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

//-----------------------------------------------------------------------------
// Does a path under game/ exist at all? ReadGameFile cannot answer it: it
// returns the empty string BOTH for a file that is not there and for one that
// is there and empty, so a "it was deleted" assertion built on it would also
// pass on a file someone truncated.
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

// The path baseentity_shared_forwarders.cpp used to occupy.
const char *const kForwardersPath = "shared/baseentity_shared_forwarders.cpp";

//-----------------------------------------------------------------------------
// THE CALLERS. Every object rta_referrers.sh named as needing a symbol the
// quarantine defined alone -- eight on the server, five on the client -- mapped
// back to its source file. These are the files the retirement had to rewrite,
// and pinning them by NAME is what stops a later edit quietly reintroducing a
// class-scope call in one of them.
//-----------------------------------------------------------------------------
struct Caller_t
{
	const char *pszWhat;     // path under game/
	bool        bServer;     // on the server link line
	bool        bClient;     // on the client link line
};

const Caller_t kCallers[] =
{
	// server/gameinterface.cpp was here until plan_gamedll_after.md Phase A9
	// step 4 deleted it. Its two surviving EntityPrecache_SetAllowed calls are
	// Begin/EndRestoreEntities', which went to shell_restoreentities.cpp; the
	// other two went with stock's CServerGameDLL at step 3. That file left the
	// server link with plan_server_native_min P1 and its row went with it.
	//
	// server/baseanimating.cpp, server/physics_main.cpp,
	// shared/baseentity_shared.cpp and client/c_baseentity.cpp were here too.
	// Neither client.so nor server.so compiles them any more, so they left the
	// source tree and their rows went with them.
	{ "../sourcesharp/game/server/precache_model.cpp", true,  false },
	// client/sound_emitter.cpp was here: its GameSound_Precache asked
	// EntityPrecache_IsAllowed. The client's emit path went to C#
	// (SourceSharp.Client.Sounds) and the file left; nothing on the client
	// precaches a sound by name, so the call went with it.
};
const size_t kNumCallers = sizeof( kCallers ) / sizeof( kCallers[ 0 ] );

// The four outside game/, which SourceRoot() cannot reach: they are under
// src/sourcesharp/. Read through a second root rather than by reaching upward
// out of game/, which would break the moment the test binary's working
// directory changed.
const char *ShellRoot()
{
	static const char *kCandidates[] = { "../sourcesharp", "src/sourcesharp",
	                                     "../src/sourcesharp", "sourcesharp" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::string strProbe( kCandidates[ i ] );
		strProbe += "/game";

		DIR *pDir = opendir( strProbe.c_str() );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadShellFile( const char *pszRelative )
{
	const char *pszRoot = ShellRoot();
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

const char *const kShellCallers[] =
{
	"game/server/world_adapter.cpp",
	"game/server/shell_standalone_player.cpp",
	// plan_entity.md step 6f: the client player class is gone and its two
	// seeding calls moved to the replay bracket, which is where the client now
	// enters and leaves one replayed command.
	"game/client/shell_prediction.cpp",
	"game/client/shell_standalone_client.cpp",
};
const size_t kNumShellCallers = sizeof( kShellCallers ) / sizeof( kShellCallers[ 0 ] );

const std::string &StaticsHeader()
{
	static const std::string s = ReadGameFile( "../sourcesharp/game/shared/entity_shared_statics.h" );
	return s;
}

//-----------------------------------------------------------------------------
// plan_entity.md step 4e moved three of the five BODIES here. The header's own
// prose used to say they could not move, because they reached
// baseentity_shared.cpp's own objects -- the sv_alternateticks ConVar and
// g_pGameRules. 4e's answer was that the ConVar moved too, and g_pGameRules was
// never that translation unit's to begin with.
//-----------------------------------------------------------------------------
const std::string &StaticsSource()
{
	static const std::string s = ReadShellFile( "game/shared/entity_shared_statics.cpp" );
	return s;
}

// The client's utility library: EntityTracer_ComputeStartPosition's home since
// plan_server_native_v2 T2 (2026-10-18). It was #ifdef CLIENT_DLL in
// entity_shared_statics.cpp, and a shared TU keeps only what both DLLs import.
const std::string &ClientUtilLibrarySource()
{
	static const std::string s = ReadShellFile( "game/client/client_util_library.cpp" );
	return s;
}

// ServerBaseEntityHeader() WAS HERE. game/server/baseentity.h is compiled by
// nothing since plan_server_novalve.md P5 and left the farm in P6; its absence
// is pinned, anchored, in AllFilesWereReallyOpened.

// UTIL_ImpactTrace left game/server/util.cpp for the shim with the other two
// temp-entity emitters (Phase 4 exit, lane p4-util). The CLAIM below is
// unchanged -- it is about the guard that keeps CBaseEntity::ImpactTrace's
// unguarded dereference unreachable, not about which file holds the guard --
// so this reads the new address. (util.cpp is no longer compiled at all.)
const std::string &ServerUtilLibrarySource()
{
	static const std::string s = ReadShellFile( "game/server/server_util_library.cpp" );
	return s;
}

const std::string &ClientBaseEntityHeader()
{
	static const std::string s = ReadGameFile( "client/c_baseentity.h" );
	return s;
}

//-----------------------------------------------------------------------------
// game/server/world.h, where the static and virtual detectors are proved
// against REAL header text.
//
// They used to be proved against server/baseentity.h and client/c_baseentity.h,
// whose entity classes are gone -- the whole `class CBaseEntity { ... }` span
// was inside `#ifndef SS_CBASEENTITY_ALIAS`, an arm this build never compiled,
// and the close deleted it. A detector proved against a class that is not there
// is proved against nothing.
//
// CWorld is the right replacement rather than a convenient one. It is the live
// entity this file's own NULL-subject inventory now turns on -- UTIL_ImpactTrace
// dispatches to CWorld::ImpactTrace and nothing else -- and world.h is in the
// compiled include graph (game/server/mapentities.cpp and physics.cpp both
// include it). It carries plain `virtual T Foo( ... );` declarations AND plain
// `static void Foo( void );` ones, which is what the two detectors match:
// neither recognises a pure virtual, because `= 0` is not the `;` they look for.
//-----------------------------------------------------------------------------
const std::string &WorldHeader()
{
	static const std::string s = ReadGameFile( "../sourcesharp/game/server/ss_world.h" );
	return s;
}

//-----------------------------------------------------------------------------
// Strip // and /* */ comments, so that a CBaseEntity:: name written in prose --
// which every file under test is full of, being about exactly that -- is not
// mistaken for code.
//
// Not a C++ lexer: it does not track string or character literals. It does not
// need to, because what is built on top of it is a DEFINITION detector, and a
// definition cannot occur inside a string literal.
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

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' ) ||
	       ( c >= '0' && c <= '9' ) || c == '_';
}

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

size_t SkipSpace( const std::string &s, size_t i )
{
	while ( i < s.size() && ( s[ i ] == ' ' || s[ i ] == '\t' ||
	                          s[ i ] == '\r' || s[ i ] == '\n' ) )
		i++;
	return i;
}

//-----------------------------------------------------------------------------
// The body of a FREE function definition `<Name>( ... ) {`, squeezed.
//-----------------------------------------------------------------------------
std::string FreeFunctionBody( const std::string &strSource, const std::string &strName )
{
	const std::string strCode = StripComments( strSource );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		if ( pos >= 2 && strCode.compare( pos - 2, 2, "::" ) == 0 )
			continue;

		size_t i = pos + strName.size();
		if ( i < strCode.size() && IsIdentChar( strCode[ i ] ) )
			continue;

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;

		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;

		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '{' )
			continue;

		int depth = 0;
		const size_t bodyStart = i + 1;
		for ( size_t j = i; j < strCode.size(); j++ )
		{
			if ( strCode[ j ] == '{' ) depth++;
			else if ( strCode[ j ] == '}' )
			{
				depth--;
				if ( depth == 0 )
					return Squeezed( strCode.substr( bodyStart, j - bodyStart ) );
			}
		}
	}

	return std::string();
}

//-----------------------------------------------------------------------------
// Is `<Name>` declared `static` in this header?
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Is `<Name>` written `static` at namespace scope -- as a DECLARATION or as a
// DEFINITION?
//
// WHY THIS EXISTS AND DeclaredStatic DOES NOT ANSWER IT. DeclaredStatic
// requires a `;` straight after the parameter list, because it was written to
// read class-body declarations. Two of the five lifted forms are INLINE
// DEFINITIONS in entity_shared_statics.h -- EntityPrecache_IsAllowed and
// EntityPrecache_SetAllowed -- so their parameter list is followed by `{`, and
// DeclaredStatic cannot see them at all. It answers false for `static bool
// EntityPrecache_IsAllowed() { ... }` exactly as it answers false for a correct
// one.
//
// FOUND BY MUTATION, not by reading. Rewriting `inline bool
// EntityPrecache_IsAllowed()` to `static bool EntityPrecache_IsAllowed( )` and
// re-running TheFiveLeftBothEntityHeadersAndTheirFreeFormsAreNotStatic read
// GREEN -- so two of that test's five rows were measuring nothing, and they
// were the two the `static` prohibition is really about: both read one flag
// through EntityPrecache_AllowedRef's function-local static, and `static` on
// the accessor gives every including TU its own.
//
// `{` as well as `;`, and nothing else: a bare `static` on a line is not enough
// (a function-local `static` is on its own line inside a body, as the storage
// accessors here had until they became inline variables).
//-----------------------------------------------------------------------------
bool StaticAtNamespaceScope( const std::string &strHeader, const std::string &strName )
{
	const std::string strCode = StripComments( strHeader );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		const size_t after = pos + strName.size();
		if ( after < strCode.size() && IsIdentChar( strCode[ after ] ) )
			continue;

		size_t i = SkipSpace( strCode, after );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;
		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || ( strCode[ i ] != ';' && strCode[ i ] != '{' ) )
			continue;             // a call, not a declaration or a definition

		size_t lineStart = strCode.rfind( '\n', pos );
		lineStart = ( lineStart == std::string::npos ) ? 0 : lineStart + 1;
		const std::string strLine = strCode.substr( lineStart, pos - lineStart );
		if ( strLine.find( "static" ) != std::string::npos )
			return true;
	}
	return false;
}

bool DeclaredStatic( const std::string &strHeader, const std::string &strName )
{
	const std::string strCode = StripComments( strHeader );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		const size_t after = pos + strName.size();
		if ( after < strCode.size() && IsIdentChar( strCode[ after ] ) )
			continue;

		// must be a declaration: an argument list then a ';'
		size_t i = SkipSpace( strCode, after );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;
		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != ';' )
			continue;

		size_t lineStart = strCode.rfind( '\n', pos );
		lineStart = ( lineStart == std::string::npos ) ? 0 : lineStart + 1;
		const std::string strLine = strCode.substr( lineStart, pos - lineStart );
		if ( strLine.find( "static" ) != std::string::npos )
			return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// Is `<Name>` declared `virtual` in this header? Same walk, different keyword:
// a virtual cannot be lifted even when its body reads no member, because the
// name IS a vtable slot and a free function is not one.
//-----------------------------------------------------------------------------
bool DeclaredVirtual( const std::string &strHeader, const std::string &strName )
{
	const std::string strCode = StripComments( strHeader );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		const size_t after = pos + strName.size();
		if ( after < strCode.size() && IsIdentChar( strCode[ after ] ) )
			continue;

		size_t i = SkipSpace( strCode, after );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;
		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != ';' )
			continue;

		size_t lineStart = strCode.rfind( '\n', pos );
		lineStart = ( lineStart == std::string::npos ) ? 0 : lineStart + 1;
		const std::string strLine = strCode.substr( lineStart, pos - lineStart );
		if ( strLine.find( "virtual" ) != std::string::npos )
			return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// Is `<Name>` DEFINED at namespace scope (`<Name>( ... ) {` with no `::`
// immediately before it)?
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// How many CALLS to <name> are in this text, outside comments -- the name
// followed by `(` and NOT preceded by an identifier character.
//
// The boundary is not pedantry. Several of the lifted free functions END WITH
// the member name they replaced: "EntityPrecache_IsAllowed(" does not contain
// "IsPrecacheAllowed(", but "EntityTracer_ComputeStartPosition(" is not the
// trap either -- the one that bites is any future pair where it does, and a
// plain substring search would then count every NEW call as an old one and make
// the EXPECT_EQ( 0u, ... ) retirement assertions impossible to pass. Written
// with the boundary from the start so that cannot happen quietly later.
//-----------------------------------------------------------------------------
size_t CountCallsTo( const std::string &strSource, const std::string &strName )
{
	const std::string strCode = StripComments( strSource );

	size_t n = 0;
	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + strName.size() ) )
	{
		// Not part of a longer identifier on either side.
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;

		size_t i = pos + strName.size();
		if ( i < strCode.size() && IsIdentChar( strCode[ i ] ) )
			continue;

		// WHITESPACE IS ALLOWED BEFORE THE PAREN, and this is not pedantry:
		// physics_main.cpp:1171 is written `Physics_ClipVelocity (a, b, c, 1);`
		// in Valve's old style. Making "name(" the needle instead misses it --
		// which reads LOW in a positive count, and passes VACUOUSLY in the
		// EXPECT_EQ( 0u, ... ) assertions the quarantine retirements rest on.
		while ( i < strCode.size() &&
		        ( strCode[ i ] == ' ' || strCode[ i ] == '\t' ||
		          strCode[ i ] == '\r' || strCode[ i ] == '\n' ) )
			i++;

		if ( i < strCode.size() && strCode[ i ] == '(' )
			n++;
	}
	return n;
}

bool DefinedFree( const std::string &strSource, const std::string &strName )
{
	const std::string strCode = StripComments( strSource );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		if ( pos >= 2 && strCode.compare( pos - 2, 2, "::" ) == 0 )
			continue;   // qualified -- that is the forwarder, not the free one

		size_t i = pos + strName.size();
		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;
		i = SkipSpace( strCode, i );
		if ( i < strCode.size() && strCode[ i ] == '{' )
			return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// Is `<Name>` DECLARED (argument list then ';') anywhere in this text?
//-----------------------------------------------------------------------------
bool DeclaredInHeader( const std::string &strHeader, const std::string &strName )
{
	const std::string strCode = StripComments( strHeader );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		size_t i = pos + strName.size();
		i = SkipSpace( strCode, i );
		if ( i >= strCode.size() || strCode[ i ] != '(' )
			continue;
		i = SkipParens( strCode, i );
		if ( i == std::string::npos )
			continue;
		i = SkipSpace( strCode, i );
		if ( i < strCode.size() && strCode[ i ] == ';' )
			return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// Does this text mention `<Name>` at all, outside comments? Used for the three
// deletions: a deleted member must be absent from the definition file AND from
// both headers, and prose about the deletion must not satisfy the check --
// which is why comments are stripped first, and why the fixture below proves
// that stripping works.
//-----------------------------------------------------------------------------
bool MentionedInCode( const std::string &strSource, const std::string &strName )
{
	const std::string strCode = StripComments( strSource );

	for ( size_t pos = strCode.find( strName ); pos != std::string::npos;
	      pos = strCode.find( strName, pos + 1 ) )
	{
		if ( pos > 0 && IsIdentChar( strCode[ pos - 1 ] ) )
			continue;
		const size_t after = pos + strName.size();
		if ( after < strCode.size() && IsIdentChar( strCode[ after ] ) )
			continue;
		return true;
	}
	return false;
}

//-----------------------------------------------------------------------------
// The five, and the free function each one forwards to.
//-----------------------------------------------------------------------------
struct Lifted_t
{
	const char *pszMember;
	const char *pszFree;
	bool        bDeclaredStatic;   // four are; ComputeTracerStartPosition is not

	// WHERE THE BODY IS. Three homes, not two: the precache pair is defined
	// inline in entity_shared_statics.h because the flag they read is the
	// static data member that had to leave the class with them, and step 4e
	// moved the other three OUT of baseentity_shared.cpp entirely, to the shim
	// translation unit that now owns the sv_alternateticks ConVar as well.
	// A bool could not say that, and the version of this test that used one
	// would have gone on passing with the bodies in either place.
	//
	// A FOURTH since plan_server_native_v2 T2 (2026-10-18): the tracer helper
	// was compiled only into the client, so it left the shared source for the
	// client's utility library (client_util_library.cpp).
	enum Home_t { kInStaticsHeader, kInStaticsSource, kInClientUtilLibrary };
	Home_t      eHome;
};

const Lifted_t kLifted[] =
{
	{ "IsPrecacheAllowed",            "EntityPrecache_IsAllowed",                true,  Lifted_t::kInStaticsHeader },
	{ "SetAllowPrecache",             "EntityPrecache_SetAllowed",               true,  Lifted_t::kInStaticsHeader },
	{ "IsSimulatingOnAlternateTicks", "EntitySim_IsSimulatingOnAlternateTicks",  true,  Lifted_t::kInStaticsSource },
	{ "SetPredictionRandomSeed",      "EntityPrediction_SetSeedFromCommand",     true,  Lifted_t::kInStaticsSource },
	{ "ComputeTracerStartPosition",   "EntityTracer_ComputeStartPosition",       false, Lifted_t::kInClientUtilLibrary },
};

const size_t kNumLifted = sizeof( kLifted ) / sizeof( kLifted[ 0 ] );

//-----------------------------------------------------------------------------
// The three deleted members. Each was proven dead by nm over every object on
// both link lines plus a tree-wide source grep, and each landed in its own
// revertible commit.
//-----------------------------------------------------------------------------
const char *const kDeleted[] =
{
	"ApplyLocalVelocityImpulse",
	"GetLastThinkTick",
	// GetNextThink( int ) is NOT here: the char* overload of that name survives,
	// so its absence is a signature question the name alone cannot ask.
};

const size_t kNumDeleted = sizeof( kDeleted ) / sizeof( kDeleted[ 0 ] );

} // namespace

//-----------------------------------------------------------------------------
// THE QUARANTINE IS RETIRED, and three things have to be true at once or the
// retirement is half-done and passes anyway: the file is gone, no caller names
// a class-scope form, and the callers really do name the free functions.
//-----------------------------------------------------------------------------
TEST( BaseEntitySharedLift, QuarantineIsRetired )
{
	EXPECT_FALSE( GameFileExists( kForwardersPath ) )
		<< "game/" << kForwardersPath << " is back. Its five names had 21 call "
		<< "sites in 11 files between them, and all 21 now name the free "
		<< "function -- restoring the file would give the class-scope names a "
		<< "definition nothing asks for.";

	for ( size_t i = 0; i < kNumCallers; i++ )
	{
		const std::string strSrc = ReadGameFile( kCallers[ i ].pszWhat );
		ASSERT_FALSE( strSrc.empty() ) << kCallers[ i ].pszWhat << " did not load";

		for ( size_t k = 0; k < kNumLifted; k++ )
		{
			EXPECT_EQ( 0u, CountCallsTo( strSrc, kLifted[ k ].pszMember ) )
				<< kCallers[ i ].pszWhat << " calls " << kLifted[ k ].pszMember
				<< " through the class again. It must call "
				<< kLifted[ k ].pszFree << ": nothing defines the class-scope "
				<< "name in this build any more.";
		}
	}

	for ( size_t i = 0; i < kNumShellCallers; i++ )
	{
		const std::string strSrc = ReadShellFile( kShellCallers[ i ] );
		ASSERT_FALSE( strSrc.empty() ) << kShellCallers[ i ] << " did not load";

		for ( size_t k = 0; k < kNumLifted; k++ )
		{
			EXPECT_EQ( 0u, CountCallsTo( strSrc, kLifted[ k ].pszMember ) )
				<< "sourcesharp/" << kShellCallers[ i ] << " calls "
				<< kLifted[ k ].pszMember << " through the class again.";
		}
	}
}

//-----------------------------------------------------------------------------
// ...and the positive half. Without it every zero-count above is satisfied by a
// caller that lost the call altogether.
//-----------------------------------------------------------------------------
TEST( BaseEntitySharedLift, TheCallersNameTheFreeFunctions )
{
	// One free function per file, chosen as the one that file actually calls.
	struct Expect_t { const char *pszWhat; const char *pszFree; size_t n; bool bShell; };
	const Expect_t kExpect[] =
	{
		// 4 -> 2, AND THE TWO THAT WENT ARE A DELETION RATHER THAN A MOVE.
		// plan_gamedll_after.md Phase A9 step 3 deleted stock's CServerGameDLL,
		// which held two of the four: LevelInit's precache window and
		// LevelShutdown's. The two left are Begin/EndRestoreEntities', which
		// stayed -- and moved with them to shell_restoreentities.cpp when step 4
		// deleted the file. CShellGameDll opens and closes its own precache
		// window in shell_level.cpp and is pinned there separately.
		// 2 -> 1 in the server gc cull (2026-10-01): BeginRestoreEntities had no
		// caller and the linker dropped it, so its call went with it; End's
		// stayed until plan_server_native_min P1 deleted the whole restore path
		// (the file and its row went with it). 1 -> 0.
		// server/physics_main.cpp, server/baseanimating.cpp,
		// shared/baseentity_shared.cpp, client/c_baseentity.cpp and
		// client/c_baseentity_statics.cpp had rows here. Neither client.so nor
		// server.so compiles them any more; they left the source tree and their
		// rows went with them.
		{ "../sourcesharp/game/server/precache_model.cpp", "EntityPrecache_IsAllowed",               1, false },   // 2 -> 1: the second was in the never-defined WATCHACCESS block, deleted in plan_server_native_v2 T2
		// sound_emitter.cpp's EntityPrecache_IsAllowed row went with the file
		// (the client's emit path is C#'s, SourceSharp.Client.Sounds).
		// The DEFINITION step 4e moved from baseentity_shared.cpp into
		// entity_shared_statics.cpp, and plan_server_native_v2 T2 on into the
		// client's utility library (it was #ifdef CLIENT_DLL there).
		{ "game/client/client_util_library.cpp", "EntityTracer_ComputeStartPosition", 1, true },
		// The SAME QUESTION c_baseentity.cpp's interpolation walk used to ask
		// is still asked on the client's live path, by
		// StandaloneInterpolationAmount() in shell_standalone_client.cpp, which
		// is the row at the foot of this table.
		// world_adapter.cpp's window opening is managed's world precache since
		// plan_server_native_v2 T4e: it opens the window through IShimLevel's
		// SetPrecacheAllowed, the one native call left.
		{ "game/server/world_adapter.cpp",             "EntityPrecache_SetAllowed",              0, true },
		{ "game/server/shell_level.cpp",               "EntityPrecache_SetAllowed",              1, true },
		// 0 since plan_server_native_v2 T4g: the server's commands are managed.
		{ "game/server/shell_standalone_player.cpp",   "EntityPrediction_SetSeedFromCommand",    0, true },
		{ "game/client/shell_prediction.cpp",          "EntityPrediction_SetSeedFromCommand",    2, true },
		{ "game/client/shell_standalone_client.cpp",   "EntitySim_IsSimulatingOnAlternateTicks", 1, true },
	};
	const size_t kNumExpect = sizeof( kExpect ) / sizeof( kExpect[ 0 ] );

	for ( size_t i = 0; i < kNumExpect; i++ )
	{
		const std::string strSrc = kExpect[ i ].bShell
			? ReadShellFile( kExpect[ i ].pszWhat )
			: ReadGameFile( kExpect[ i ].pszWhat );

		ASSERT_FALSE( strSrc.empty() ) << kExpect[ i ].pszWhat << " did not load";

		EXPECT_EQ( kExpect[ i ].n, CountCallsTo( strSrc, kExpect[ i ].pszFree ) )
			<< kExpect[ i ].pszWhat << " makes "
			<< CountCallsTo( strSrc, kExpect[ i ].pszFree ) << " calls to "
			<< kExpect[ i ].pszFree << ", not " << kExpect[ i ].n << ". FEWER "
			<< "means a call was lost rather than rewritten; MORE means one was "
			<< "added without saying so.";
	}
}

//-----------------------------------------------------------------------------
// The existence probe answers both ways. Without this, QuarantineIsRetired's
// EXPECT_FALSE passes on a probe that says no to everything.
//-----------------------------------------------------------------------------
TEST( BaseEntitySharedLiftFixture, ExistenceProbeAnswersBothWays )
{
	EXPECT_TRUE( GameFileExists( "../sourcesharp/game/shared/entity_shared_statics.h" ) )
		<< "the probe cannot see a file that is definitely there.";
	EXPECT_FALSE( GameFileExists( "shared/no_such_file_4b71.cpp" ) )
		<< "the probe reports a file that was never in this tree.";
}

//=============================================================================
// THE STATIC DATA MEMBER. The failure nobody was tracking: it stops the file
// PARSING under the alias, not linking, so no consumer count can see it.
//=============================================================================

// TheQuarantineDefinesNoStaticDataMemberEither WAS HERE. It pinned that no
// static data member had been put into a file that was going to be deleted
// wholesale. The file has now been deleted, so the hazard it guarded against no
// longer has anywhere to happen and the assertion has nothing to read. The
// storage question itself is still pinned, twice: TheStorageExistsInTheHeader
// below, and SharedFileDefinesNoStaticDataMember above.

TEST( BaseEntitySharedLift, TheStorageExistsInTheHeader )
{
	EXPECT_TRUE( DefinedFree( StaticsHeader(), "EntityPrecache_AllowedRef" ) )
		<< "entity_shared_statics.h no longer defines EntityPrecache_AllowedRef, "
		<< "which is where m_bAllowPrecache's storage went.";

	EXPECT_NE( std::string::npos, StripComments( StaticsHeader() ).find( "inline bool s_bAllowPrecache = false;" ) )
		<< "the flag itself is gone from entity_shared_statics.h, or no longer "
		<< "starts false. It has to be a C++17 inline variable: one object per "
		<< "DLL, constant-initialised, exactly what the static data member was.";
}

//=============================================================================
// The three deletions stay deleted.
//=============================================================================

TEST( BaseEntitySharedLift, TheDeletedMembersAreGoneFromBothEntityHeaders )
{
	// baseentity_shared.cpp, the third file this used to read, is no longer
	// compiled and has left the source tree.
	for ( size_t i = 0; i < kNumDeleted; i++ )
	{
		// The server header's row is retired: game/server/baseentity.h is
		// compiled by nothing since plan_server_novalve.md P5 and left the farm
		// in P6 (AllFilesWereReallyOpened pins it absent).
		EXPECT_FALSE( MentionedInCode( ClientBaseEntityHeader(), kDeleted[ i ] ) )
			<< kDeleted[ i ] << " is declared again in game/client/c_baseentity.h.";
	}
}

//=============================================================================
// "Every forwarder is a forward and nothing more" WAS HERE, and it read the
// quarantine file. There are no forwarders left: the file is retired and the
// callers name the free functions, which QuarantineIsRetired and
// TheCallersNameTheFreeFunctions pin instead. What that test was really
// defending -- that the SDK's call sites and the port's own call sites cannot
// end up doing different things -- is now true by construction, because there
// is only one implementation and only one way to reach it.
//=============================================================================

//=============================================================================
// The free functions exist where they are supposed to.
//=============================================================================

TEST( BaseEntitySharedLift, HeaderCarriesEveryFreeFunction )
{
	for ( size_t i = 0; i < kNumLifted; i++ )
	{
		const bool bDeclared = DeclaredInHeader( StaticsHeader(), kLifted[ i ].pszFree );
		const bool bDefined  = DefinedFree( StaticsHeader(), kLifted[ i ].pszFree );

		EXPECT_TRUE( bDeclared || bDefined )
			<< "entity_shared_statics.h neither declares nor defines "
			<< kLifted[ i ].pszFree;
	}
}

TEST( BaseEntitySharedLift, EachFreeFunctionIsDefinedWhereItWasSaidToBe )
{
	for ( size_t i = 0; i < kNumLifted; i++ )
	{
		if ( kLifted[ i ].eHome == Lifted_t::kInStaticsHeader )
		{
			EXPECT_TRUE( DefinedFree( StaticsHeader(), kLifted[ i ].pszFree ) )
				<< kLifted[ i ].pszFree << " is not defined in "
				<< "entity_shared_statics.h. The precache pair lives there "
				<< "because the flag they read is the static data member, and "
				<< "the storage had to leave the class with them.";
			continue;
		}

		if ( kLifted[ i ].eHome == Lifted_t::kInClientUtilLibrary )
		{
			EXPECT_TRUE( DefinedFree( ClientUtilLibrarySource(), kLifted[ i ].pszFree ) )
				<< kLifted[ i ].pszFree << " is not defined at namespace scope in "
				<< "game/client/client_util_library.cpp, where plan_server_native_v2 "
				<< "T2 moved it out of the shared entity_shared_statics.cpp.";
			EXPECT_FALSE( DefinedFree( StaticsSource(), kLifted[ i ].pszFree ) )
				<< kLifted[ i ].pszFree << " is defined in the shared "
				<< "entity_shared_statics.cpp again, which server.so compiles too.";
			continue;
		}

		EXPECT_TRUE( DefinedFree( StaticsSource(), kLifted[ i ].pszFree ) )
			<< kLifted[ i ].pszFree << " is not defined at namespace scope in "
			<< "sourcesharp/game/shared/entity_shared_statics.cpp, which is "
			<< "where step 4e moved these three bodies.";
	}
}

//=============================================================================
// The PREMISE of the lift.
//
// Four of the five are declared `static`, and none of the five is `virtual`.
// If somebody drops a `static`, or makes one of them virtual, the forwarder
// still compiles and the free function still compiles, and the reason these
// were safe to lift has silently stopped being true.
//=============================================================================

TEST( BaseEntitySharedLift, TheFiveLeftBothEntityHeadersAndTheirFreeFormsAreNotStatic )
{
	// WAS TheFourAreStillDeclaredStaticOnBothSides, which read the `static` on
	// each member's declaration in the two entity headers. Those declarations
	// were in the class bodies, inside `#ifndef SS_CBASEENTITY_ALIAS`, which
	// this build never compiled -- so the premise was being read off text the
	// compiler never saw, and the close has now deleted the arm.
	//
	// THE PREMISE DID NOT EVAPORATE, IT CHANGED ADDRESS. `static` on a MEMBER
	// meant "one instance, no `this`". The five are free functions now, and
	// `static` on a namespace-scope declaration in a HEADER means the opposite
	// of what it meant on the member: internal linkage, one copy per including
	// translation unit. EntityPrecache_IsAllowed and EntityPrecache_SetAllowed
	// read one flag through EntityPrecache_AllowedRef's function-local static;
	// mark any of them `static` and every TU gets its own flag, every
	// precache bracket reads a different one, and nothing diagnoses it.
	for ( size_t i = 0; i < kNumLifted; i++ )
	{
		// game/server/baseentity.h's row is retired: that header is compiled by
		// nothing since plan_server_novalve.md P5 and left the farm in P6.
		EXPECT_FALSE( MentionedInCode( ClientBaseEntityHeader(), kLifted[ i ].pszMember ) )
			<< "game/client/c_baseentity.h names " << kLifted[ i ].pszMember
			<< " in code again.";

		// The live successor, in the header the build really compiles.
		//
		// StaticAtNamespaceScope and NOT DeclaredStatic. Two of the five are
		// INLINE DEFINITIONS here, so their parameter list is followed by `{`
		// and DeclaredStatic -- which wants a `;` -- cannot see them either
		// way. A mutation found that: `inline bool EntityPrecache_IsAllowed()`
		// rewritten to `static bool EntityPrecache_IsAllowed( )` left this test
		// GREEN, and those two are the rows the prohibition is really about.
		EXPECT_FALSE( StaticAtNamespaceScope( StaticsHeader(), kLifted[ i ].pszFree ) )
			<< kLifted[ i ].pszFree << " is declared `static` in "
			<< "entity_shared_statics.h. At namespace scope in a header that is "
			<< "internal linkage -- one copy per TU -- which is the opposite of "
			<< "what `static` bought on the member it replaced.";

		EXPECT_TRUE( MentionedInCode( StaticsHeader(), kLifted[ i ].pszFree ) )
			<< kLifted[ i ].pszFree << " is not named in entity_shared_statics.h "
			<< "at all, so the assertion above is passing over its absence.";
	}
}

TEST( BaseEntitySharedLift, ComputeTracerStartPositionIsTheOneThatWasNeverStatic )
{
	// READ THE ASSERTIONS, NOT THE NAME. The name is kept because it names the
	// asymmetry, which has not changed: this one lifts because its BODY READS
	// NO MEMBER, not because it was declared static -- and the four others lift
	// for the other reason. What changed is where that is readable. The two
	// `static`-free declarations this used to read were in class bodies inside
	// `#ifndef SS_CBASEENTITY_ALIAS`, which the compiler never saw, and the
	// close deleted the arm.
	//
	// The lifted form is where the asymmetry shows now, and it shows it more
	// plainly than the absent `static` ever did: EntityTracer_ComputeStartPosition
	// TAKES NO ENTITY AT ALL. It used to take one and not read it; step 4e
	// removed the parameter, because the only two arms that read it -- the
	// player eye-vector offset and the weapon muzzle attachment -- are compiled
	// out of this build.
	const std::string strStatics = Squeezed( StripComments( StaticsHeader() ) );

	EXPECT_TRUE( strStatics.find(
		"voidEntityTracer_ComputeStartPosition(constVector&vecShotSrc,Vector*pVecTracerStart);" )
		!= std::string::npos )
		<< "EntityTracer_ComputeStartPosition's declaration in "
		<< "entity_shared_statics.h is not the shooter-free one. If it has taken "
		<< "an entity back, the reason this member was a lift at all -- its body "
		<< "reads nothing of the entity -- has stopped being true, and no "
		<< "compiler would say so.";

	EXPECT_FALSE( StaticAtNamespaceScope( StaticsHeader(), "EntityTracer_ComputeStartPosition" ) )
		<< "the one that was never static has acquired `static` at namespace "
		<< "scope, which is internal linkage in a header.";

	// game/server/baseentity.h's row is retired: that header is compiled by
	// nothing since plan_server_novalve.md P5 and left the farm in P6.
	EXPECT_FALSE( MentionedInCode( ClientBaseEntityHeader(), "ComputeTracerStartPosition" ) )
		<< "the class-scope name is back in game/client/c_baseentity.h.";
}

TEST( BaseEntitySharedLift, NoneOfTheFiveIsVirtualOnEitherSide )
{
	// A virtual cannot be lifted even when its body reads no member: the name IS
	// a vtable slot, and a free function is not one. TEN members of this file
	// take no `this` and are NOT here for exactly that reason -- nine virtual on
	// both sides (DecalTrace, ImpactTrace, CreateVPhysics, GetBeamTraceFilter,
	// GetTracerType, BloodColor, DoImpactEffect, GetTracerAttachment,
	// ModifyEmitSoundParams) and ShouldDrawUnderwaterBulletBubbles, which is
	// virtual on the CLIENT only and is pinned separately below. If one of the
	// five ever acquires `virtual`, the lift is wrong and nothing else notices.
	// READ THE ASSERTIONS, NOT THE NAME -- and read this note, because the two
	// EXPECT_FALSEs below no longer carry the fact on their own. Both entity
	// headers lost their class bodies with `#ifndef SS_CBASEENTITY_ALIAS`, so a
	// "not virtual there" assertion is now also satisfied by the member not
	// being there. They are kept as the regression half: if a class body ever
	// comes back into either header, these say whether it brought a virtual.
	// The fact itself is carried by the third assertion, which is about the
	// FREE declarations the build actually compiles.
	for ( size_t i = 0; i < kNumLifted; i++ )
	{
		// game/server/baseentity.h's row is retired: that header is compiled
		// by nothing since plan_server_novalve.md P5 and left the farm in P6.
		EXPECT_FALSE( DeclaredVirtual( ClientBaseEntityHeader(), kLifted[ i ].pszMember ) )
			<< kLifted[ i ].pszMember << " is now virtual in "
			<< "game/client/c_baseentity.h; a virtual cannot be a lift.";

		// The live one. A `virtual` written on a namespace-scope declaration is
		// a compile error, so what this really catches is the other direction:
		// somebody moving the declaration back INTO a class in this header --
		// entity_shared_statics.h has classes in it -- and giving it a slot.
		EXPECT_FALSE( DeclaredVirtual( StaticsHeader(), kLifted[ i ].pszFree ) )
			<< kLifted[ i ].pszFree << " is declared virtual in "
			<< "entity_shared_statics.h. The whole reason these five were "
			<< "liftable and the other ten were not is that a name which is a "
			<< "vtable slot cannot become a free function.";
	}
}

//=============================================================================
// THE NULL-SUBJECT INVENTORY -- one instance in this file, and it is NOT live.
//
// CBaseEntity::ImpactTrace narrows pTrace->m_pEnt with TraceEntity() and then
// dereferences the result without testing it. TraceEntity answers NULL for
// everything that is not a CBaseEntity -- every entity on the parallel
// hierarchy and every static prop -- so this is the shape that made the
// dm_lockdown skylight a reliable segfault at FireBullets' glass check.
//
// It has not gone off because every path into the virtual establishes that the
// trace's entity IS a CBaseEntity first, and the load-bearing one of those is
// UTIL_ImpactTrace's `if ( !pEntity ... ) return;`. Delete that guard and this
// site becomes live, in a different file, with nothing connecting the two.
//
// An INVENTORY rather than a ban, on ground_trace_subject_test's terms: the fix
// is a behaviour decision (drop the effect for a managed entity, or hand the
// client's EntIndexToHandle a -1 it has never been given) that belongs to the
// lane porting effect dispatch. Pinned so it cannot be silently lost -- and so
// that FIXING it goes red too, which is correct: the inventory must then go.
//=============================================================================

TEST( BaseEntitySharedNullSubject, TheDispatchWentSoTheGuardWentWithIt )
{
	// WAS TheGuardThatKeepsItUnreachableIsStillThere, which asserted that
	// UTIL_ImpactTrace still narrowed with `TraceEntity( *pTrace )` and still
	// returned on `!pEntity` before dispatching.
	//
	// THE GUARD'S SUBJECT IS GONE, AND FOR THE REASON THE GUARD EXISTED.
	// TraceEntity was one of the four gated families the close deleted: all
	// four bottom out in IServerUnknown::GetBaseEntity(), and every
	// implementation of that in both DLLs is `mov $0x0,%eax; ret` -- a constant
	// NULL. The `!pEntity` return was that constant written down, so with
	// TraceEntity deleted the return has nothing left to test.
	//
	// What keeps CBaseEntity::ImpactTrace's unguarded dereference unreachable
	// is now stronger and is one file rather than two: UTIL_ImpactTrace has NO
	// dispatch to an SDK entity at all. It answers for the world and returns.
	// Both halves are asserted, because "the guard is gone" on its own is also
	// true of a function that dispatches unguarded -- which is precisely the
	// segfault this inventory is about.
	const std::string strBody = FreeFunctionBody( ServerUtilLibrarySource(), "UTIL_ImpactTrace" );
	ASSERT_FALSE( strBody.empty() )
		<< "UTIL_ImpactTrace is gone from "
		   "sourcesharp/game/server/server_util_library.cpp, where it moved";

	EXPECT_EQ( std::string::npos, strBody.find( "TraceEntity(" ) )
		<< "UTIL_ImpactTrace asks TraceEntity again. That family is deleted -- "
		<< "it was a constant NULL in both DLLs -- and GameEntityFromEntityHandle "
		<< "/ TraceGameEntity are what replaced it.";

	// EXACTLY ONE dispatch through a pointer, and it is the world's. Written as
	// a count rather than "no `->ImpactTrace(`", because the world dispatch IS
	// one and a bare absence check would have to be deleted to accommodate it --
	// at which point a second, unguarded dispatch would go unnoticed.
	size_t nDispatches = 0;
	for ( size_t pos = strBody.find( "->ImpactTrace(" ); pos != std::string::npos;
	      pos = strBody.find( "->ImpactTrace(", pos + 1 ) )
		nDispatches++;

	EXPECT_EQ( 1u, nDispatches )
		<< "UTIL_ImpactTrace does not dispatch exactly once. Every path that "
		<< "reaches CBaseEntity::ImpactTrace reaches its unguarded dereference "
		<< "of the narrowed trace entity -- the dm_lockdown skylight segfault -- "
		<< "and the guard that used to stand in front of it is gone.";

	EXPECT_EQ( std::string::npos, strBody.find( "pEntity->ImpactTrace(" ) )
		<< "UTIL_ImpactTrace dispatches to an SDK entity again.";

	// The live successor: the world answers for itself, and that dispatch is
	// inside the world test rather than in front of a general one.
	EXPECT_NE( std::string::npos, strBody.find( "SourceSharp_IsTheWorld(ss::TraceHit(*pTrace))" ) )
		<< "UTIL_ImpactTrace no longer asks whether the trace hit the world. "
		<< "Without that it emits no impact effect on level geometry at all, "
		<< "which is almost every impact in the game and reads as a renderer "
		<< "bug.";

	EXPECT_NE( std::string::npos, strBody.find( "pWorld->ImpactTrace(pTrace,iDamageType,pCustomImpactName)" ) )
		<< "UTIL_ImpactTrace no longer hands the world its own ImpactTrace. "
		<< "That call is the whole of what the function does now.";
}

//=============================================================================
// THE SCANNER IS ITSELF CHECKED.
//
// Everything above is an assertion about strings. If the files never opened, or
// an extractor never matched anything, every one of those assertions that is
// phrased as "does not contain" passes for free.
//=============================================================================

TEST( BaseEntitySharedLiftFixture, AllFilesWereReallyOpened )
{
	// FLOOR PLUS ANCHOR, the shape the two entity headers below already use and
	// the server vpc took next (05237b9c8). Both subjects here are files the
	// port is actively moving code OUT of -- that is what this suite measures --
	// so a byte threshold over either one reddens on a correct tree eventually,
	// saying "did not load" about a file that loaded perfectly. Measured, not
	// guessed: baseentity_shared.cpp is 81,361 bytes and physics_main_shared.cpp
	// 59,986, both against floors of 50,000, and the lift is still taking bodies
	// out of them. The floor drops to a value only an unopened or truncated read
	// can miss; the anchor identifies WHICH file was read, which a size cannot.
	// (baseentity_shared.cpp's row went when it stopped being compiled.)
	EXPECT_GT( StaticsHeader().size(),            200u ) << "entity_shared_statics.h did not load";
	EXPECT_NE( StaticsHeader().find( "ENTITY_SHARED_STATICS_H" ), std::string::npos )
		<< "entity_shared_statics.h is not entity_shared_statics.h.";

	// ANCHORS, NOT SIZES, for the two entity headers. Both lost their class
	// bodies -- 2,693 lines from the server's and 2,233 from the client's --
	// when the close deleted `#ifndef SS_CBASEENTITY_ALIAS`, so the 50,000-byte
	// thresholds that stood here said "did not load" about files that had
	// loaded perfectly. A size was the wrong instrument anyway: it cannot tell
	// a truncated file from a shortened one. Each anchor below is a LIVE line
	// from the tail that SURVIVED the class, at preprocessor depth 1.
	EXPECT_GT( ClientBaseEntityHeader().size(), 5000u ) << "client/c_baseentity.h did not load";
	EXPECT_GT( WorldHeader().size(),            5000u ) << "ss_world.h did not load";


	// The server entity header is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6. Its absence is the
	// strong form of every "not declared again there" row this suite used to
	// read it for, so it is pinned -- anchored on a sibling in the same
	// directory that IS readable, so a probe that cannot see game/server at
	// all does not pass it.
	EXPECT_TRUE( GameFileExists( "shared/shareddefs.h" ) )
		<< "game/shared/shareddefs.h is not readable, so the absence below proves "
		<< "nothing about game/server.";
	EXPECT_FALSE( GameFileExists( "server/baseentity.h" ) )
		<< "game/server/baseentity.h is readable from the farm again. Nothing "
		<< "compiles it; if it is back, the rows retired from this suite need "
		<< "their subject read again.";
	EXPECT_NE( std::string::npos,
	           ClientBaseEntityHeader().find( "EXTERN_RECV_TABLE(DT_BaseEntity);" ) )
		<< "client/c_baseentity.h is not the file this suite means to be reading.";
	EXPECT_NE( std::string::npos,
	           WorldHeader().find( "class CWorld" ) )
		<< "ss_world.h is not the file the detectors are proved against.";
}

TEST( BaseEntitySharedLiftFixture, StaticAndVirtualDetectorsWorkOnFixtures )
{
	const std::string strFixture =
		"class CBaseEntity\n"
		"{\n"
		"    static bool   IsStatic();\n"
		"    virtual void  IsVirtual( int a );\n"
		"    void          IsPlain( int a );\n"
		"};\n";

	EXPECT_TRUE ( DeclaredStatic( strFixture, "IsStatic" ) );
	EXPECT_FALSE( DeclaredStatic( strFixture, "IsPlain" ) )
		<< "the static detector answers true for a non-static declaration, so "
		<< "TheFourAreStillDeclaredStaticOnBothSides cannot fail";
	EXPECT_FALSE( DeclaredStatic( strFixture, "NotPresentAtAll" ) );

	EXPECT_TRUE ( DeclaredVirtual( strFixture, "IsVirtual" ) )
		<< "the virtual detector answers false for a virtual declaration, so "
		<< "NoneOfTheFiveIsVirtualOnEitherSide cannot fail";
	EXPECT_FALSE( DeclaredVirtual( strFixture, "IsPlain" ) );
	EXPECT_FALSE( DeclaredVirtual( strFixture, "IsStatic" ) );

	// AND THE ONE DeclaredStatic CANNOT SEE. This is the half a mutation found
	// missing: a DEFINITION's parameter list is followed by `{`, not `;`, so
	// DeclaredStatic answers false for a static inline exactly as it answers
	// false for a correct one. Both answers are asserted here so that the
	// difference between the two detectors is a fact and not a comment.
	const std::string strDefs =
		"static bool StaticInline() { return true; }\n"
		"inline bool InlineOnly()   { return true; }\n"
		"static bool StaticDecl();\n"
		"bool        PlainDecl();\n"
		"void Caller() { StaticInline(); }\n";

	EXPECT_FALSE( DeclaredStatic( strDefs, "StaticInline" ) )
		<< "DeclaredStatic has started seeing definitions. If it can, "
		<< "StaticAtNamespaceScope is redundant -- say so and delete one of "
		<< "them rather than leaving two detectors that agree.";

	EXPECT_TRUE ( StaticAtNamespaceScope( strDefs, "StaticInline" ) )
		<< "the namespace-scope detector cannot see a static INLINE, which is "
		<< "the shape it exists for and the shape two of the five lifted forms "
		<< "have.";
	EXPECT_TRUE ( StaticAtNamespaceScope( strDefs, "StaticDecl" ) );
	EXPECT_FALSE( StaticAtNamespaceScope( strDefs, "InlineOnly" ) )
		<< "the namespace-scope detector answers true for `inline`, so every "
		<< "EXPECT_FALSE written on it passes on correct code and this test "
		<< "would be red on a correct tree rather than green on a wrong one.";
	EXPECT_FALSE( StaticAtNamespaceScope( strDefs, "PlainDecl" ) );
	EXPECT_FALSE( StaticAtNamespaceScope( strDefs, "NotPresentAtAll" ) );
}

TEST( BaseEntitySharedLiftFixture, BothDetectorsSeeTheRealDeclarationsOfALiveHeader )
{
	// WAS VirtualDetectorSeesTheRealVirtualsOfThisFile, proved against the nine
	// virtuals of the two entity headers' class bodies. Those bodies were
	// inside `#ifndef SS_CBASEENTITY_ALIAS`, which this build never compiled
	// and the close has deleted, so there is nothing left in either header for
	// a detector to be proved against.
	//
	// RE-POINTED AT game/server/world.h, which is live -- mapentities.cpp and
	// physics.cpp include it -- and which is where DecalTrace actually lives
	// now: the world answers impacts for itself, and UTIL_ImpactTrace's only
	// dispatch is to CWorld. Both detectors are proved here, positively and
	// negatively, on the same file: a plain `virtual T Foo( ... );`, a plain
	// `static void operator delete( void * );`, and a member that is NEITHER.
	const char *const kKnownVirtuals[] =
	{
		"Spawn", "Precache", "KeyValue", "DecalTrace",
	};
	for ( size_t i = 0; i < sizeof( kKnownVirtuals ) / sizeof( kKnownVirtuals[ 0 ] ); i++ )
	{
		EXPECT_TRUE( DeclaredVirtual( WorldHeader(), kKnownVirtuals[ i ] ) )
			<< kKnownVirtuals[ i ] << " is no longer virtual in "
			<< "sourcesharp/game/server/ss_world.h, or the detector cannot see it there -- in "
			<< "which case NoneOfTheFiveIsVirtual... cannot fail either";
		EXPECT_FALSE( DeclaredStatic( WorldHeader(), kKnownVirtuals[ i ] ) )
			<< kKnownVirtuals[ i ] << " reads as `static` to the static "
			<< "detector, which means that detector matches on the wrong line";
	}

	const char *const kKnownStatics[] =
	{
		// CWorld's operator delete: its last plain static member since the
		// empty RegisterShared* hooks went (plan_server_native_v2 T2).
		"delete",
	};
	for ( size_t i = 0; i < sizeof( kKnownStatics ) / sizeof( kKnownStatics[ 0 ] ); i++ )
	{
		EXPECT_TRUE( DeclaredStatic( WorldHeader(), kKnownStatics[ i ] ) )
			<< kKnownStatics[ i ] << " is no longer static in "
			<< "sourcesharp/game/server/ss_world.h, or the static detector cannot see it there";
		EXPECT_FALSE( DeclaredVirtual( WorldHeader(), kKnownStatics[ i ] ) )
			<< kKnownStatics[ i ] << " reads as `virtual` to the virtual "
			<< "detector";
	}

	// And the member that is neither, so "true for everything" is excluded.
	EXPECT_FALSE( DeclaredVirtual( WorldHeader(), "ReportNetState" ) )
		<< "the virtual detector answers true for a plain declaration";
	EXPECT_FALSE( DeclaredStatic( WorldHeader(), "ReportNetState" ) )
		<< "the static detector answers true for a plain declaration";
}

TEST( BaseEntitySharedLift, TheTenthNoThisMemberIsVirtualOnTheClientOnly )
{
	// ShouldDrawUnderwaterBulletBubbles is the awkward one and the reason a
	// "not virtual" premise has to be checked on BOTH headers rather than one.
	// Its body reads no member, and on the SERVER it is not virtual -- so read
	// server-only it looks exactly like ComputeTracerStartPosition and lifts.
	// The file is shared, and on the client it IS virtual: lifting it would
	// leave every client override without a base implementation the day step 3
	// deletes the quarantine file wholesale. Recorded here so the asymmetry is
	// found by reading rather than by breaking the client.
	// READ THE ASSERTIONS, NOT THE NAME. The asymmetry the name records is real
	// and is the reason a "not virtual" premise has to be read on BOTH headers
	// -- but it was readable only in the two class bodies, and both of those
	// went with `#ifndef SS_CBASEENTITY_ALIAS`. Neither header declares it now,
	// so the two DeclaredVirtual reads have no subject.
	//
	// WHAT THE FACT BECOMES: it was never lifted, and it still is not. No
	// free form of it exists in entity_shared_statics.h -- which is exactly the outcome
	// the asymmetry was recorded to produce. If someone lifts it now, having
	// read only the server side, this goes red where nothing else would.
	// game/server/baseentity.h's row is retired: that header is compiled by
	// nothing since plan_server_novalve.md P5 and left the farm in P6.
	EXPECT_FALSE( MentionedInCode( ClientBaseEntityHeader(), "ShouldDrawUnderwaterBulletBubbles" ) )
		<< "game/client/c_baseentity.h declares ShouldDrawUnderwaterBulletBubbles "
		<< "again -- and on the client it was the VIRTUAL one.";

	EXPECT_FALSE( MentionedInCode( StaticsHeader(), "ShouldDrawUnderwaterBulletBubbles" ) )
		<< "a free form of ShouldDrawUnderwaterBulletBubbles has appeared in "
		<< "entity_shared_statics.h. Read server-only it looks exactly like "
		<< "ComputeTracerStartPosition and lifts; on the CLIENT it is virtual, "
		<< "and lifting it leaves every client override without a base "
		<< "implementation.";
}

TEST( BaseEntitySharedLiftFixture, MentionDetectorSeesAndIgnoresTheRightThings )
{
	// TheDeletedMembersAreGoneFromBothEntityHeaders is EXPECT_FALSEs, and both
	// files carry PROSE naming the deleted members -- the commit trail is
	// written into them. If comment stripping failed, that test would go red on
	// correct code; if the detector matched nothing, it would pass on wrong.
	const std::string strFixture =
		"// ApplyLocalVelocityImpulse was deleted\n"
		"void Other( int ApplyLocalVelocityImpulseNot );\n"
		"void Live( int GetLastThinkTick );\n";

	EXPECT_FALSE( MentionedInCode( strFixture, "ApplyLocalVelocityImpulse" ) )
		<< "prose satisfied the mention detector, so the deletion checks are "
		<< "checks about comments";
	EXPECT_TRUE( MentionedInCode( strFixture, "GetLastThinkTick" ) )
		<< "the mention detector cannot see a real occurrence at all";
}
