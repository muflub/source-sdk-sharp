//========= SourceSharp — the source scans left from the material proxies' lane ==//
//
// Lane c7-proxies wrote this file for the material proxies and recvproxy.cpp.
// The proxies are C# now (lane c5-proxies, plan_client_novalve.md §2 cluster
// 1): PlayerLogo's rules, the Alpha, IsNPC, Health and animated-texture facts
// and the cluster's renderable-not-entity scans left with the 22 proxy TUs and
// shell_materialproxies.cpp they read, and MaterialProxyFactsTests /
// MaterialProxyOracleTests hold that behaviour now. What stays is the
// scanner (PART 1) and, since C8 took recvproxy.cpp out of client.so, the
// facts that it and dt_utlvector_recv.cpp stay out (PART 2).
//
//=============================================================================//
#include "gtest/gtest.h"

#include <sys/stat.h>

#include <fstream>
#include <sstream>
#include <string>

//=============================================================================
// PART 1 — THE SOURCE SCAN
//=============================================================================

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

std::string ReadFile( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::stringstream ss;
	ss << file.rdbuf();
	return ss.str();
}

// 7c's stripper. This step needs it for the same reason 7d did: the shell file
// SPELLS the names it is being asserted about, in prose, to explain the
// measurement — so a raw grep reads several for a body that has one.
// String literals are preserved, because the proxy's registered NAME is one.
std::string StripComments( const std::string &strIn )
{
	std::string out;
	out.reserve( strIn.size() );

	enum { CODE, LINE_COMMENT, BLOCK_COMMENT, STRING, CHARLIT } state = CODE;

	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		const char c = strIn[ i ];
		const char n = ( i + 1 < strIn.size() ) ? strIn[ i + 1 ] : '\0';

		switch ( state )
		{
		case CODE:
			if ( c == '/' && n == '/' ) { state = LINE_COMMENT; i++; }
			else if ( c == '/' && n == '*' ) { state = BLOCK_COMMENT; i++; }
			else
			{
				if ( c == '"' ) state = STRING;
				else if ( c == '\'' ) state = CHARLIT;
				out += c;
			}
			break;

		case LINE_COMMENT:
			if ( c == '\n' ) { state = CODE; out += c; }
			break;

		case BLOCK_COMMENT:
			if ( c == '*' && n == '/' ) { state = CODE; i++; }
			else if ( c == '\n' ) out += c;
			break;

		case STRING:
			out += c;
			if ( c == '\\' && n != '\0' ) { out += n; i++; }
			else if ( c == '"' ) state = CODE;
			break;

		case CHARLIT:
			out += c;
			if ( c == '\\' && n != '\0' ) { out += n; i++; }
			else if ( c == '\'' ) state = CODE;
			break;
		}
	}

	return out;
}

int CountOccurrences( const std::string &strHaystack, const std::string &strNeedle )
{
	int n = 0;
	for ( size_t pos = strHaystack.find( strNeedle );
	      pos != std::string::npos;
	      pos = strHaystack.find( strNeedle, pos + strNeedle.size() ) )
	{
		n++;
	}
	return n;
}

// The same path as kRecvShim below, declared here because the liveness fact
// runs before that namespace block. Kept as two names rather than one moved
// declaration so the later facts read exactly as they did.
const char *kRecvShimForLiveness = "sourcesharp/game/client/shell_recvtable.cpp";

//-----------------------------------------------------------------------------
// IS THIS NEEDLE INSIDE A `#ifndef SS_CBASEENTITY_ALIAS` REGION?
//
// Added by the `residue` wave, and it exists because of that wave's own rule:
// A TEST MUST NOT READ A DEAD ARM AS ITS REFERENCE. The armed configuration
// defines SS_CBASEENTITY_ALIAS -- game/client/client_csharp.vpc:74 -- and it is
// the only configuration this tree builds, so text inside such a region is on
// disk and is never compiled.
//
// Six facts below point at stock's relocated line in the shim. What they assert
// about it is that it is THERE AND DEAD -- moved rather than dropped, and still
// not reachable -- and this helper is the whole difference between that and a
// claim about what the build does. Without it those facts would be reading a
// dead arm as evidence, which is the mistake that made them red in the first
// place.
//
// Nearest-marker scan. Sufficient because the regions it is asked about are
// flat one-level guards: the needle is inside when the last guard opener before
// it is nearer than the last `#endif` before it. It answers false for a needle
// that is not in the file at all, so every caller asserts presence separately.
//-----------------------------------------------------------------------------
bool IsInsideAliasGuard( const std::string &str, const std::string &strNeedle )
{
	const size_t at = str.find( strNeedle );
	if ( at == std::string::npos )
		return false;

	const size_t iOpen = str.rfind( "#ifndef SS_CBASEENTITY_ALIAS", at );
	if ( iOpen == std::string::npos )
		return false;

	const size_t iClose = str.rfind( "#endif", at );
	return iClose == std::string::npos || iClose < iOpen;
}

//-----------------------------------------------------------------------------
// THE WHOLE-FILE FORM OF THE SAME QUESTION, added by the `the-close` wave.
//
// IsInsideAliasGuard above answers "is THIS needle dead". It was written when
// the two shim files still had dead arms in them. THE CLOSE DELETED EVERY ONE
// (f288a5e11), so both files now carry no conditional compilation at all -- and
// that is a much stronger and much cheaper liveness statement than a per-needle
// scan: if a file has no `#if` family directive in it, EVERY line in it is
// compiled, and every needle this suite reads out of it is a live reference.
//
// So the facts below prove liveness twice over: TheShimsCompileEveryLineTheseFactsRead
// asserts the file-level property once, and each re-pointed fact asserts
// EXPECT_FALSE( IsInsideAliasGuard( ... ) ) beside its own needle, which is the
// per-needle proof finding 3 of .claude/entity-port/status/residue-tests.md
// asked for. The pair matters: the per-needle scan alone answers false for a
// needle that is not in the file, and the file-level count alone says nothing
// about a needle that has been deleted.
//
// Line-initial only, on the RAW text, because a `#` inside a string literal or
// mid-line is not a directive. `#include`, `#define` and `#pragma` do not
// select anything and are not counted.
//-----------------------------------------------------------------------------
int CountConditionalDirectives( const std::string &strRaw )
{
	static const char *kWords[] = { "#if", "#ifdef", "#ifndef", "#else", "#elif" };

	int n = 0;
	size_t i = 0;
	while ( i < strRaw.size() )
	{
		size_t e = strRaw.find( '\n', i );
		if ( e == std::string::npos )
			e = strRaw.size();

		const std::string line = strRaw.substr( i, e - i );
		i = e + 1;

		const size_t w = line.find_first_not_of( " \t" );
		if ( w == std::string::npos || line[ w ] != '#' )
			continue;

		// The first token, so `#ifdef` is not counted as `#if`.
		const std::string rest = line.substr( w );
		const size_t sp = rest.find_first_of( " \t" );
		const std::string word = rest.substr( 0, sp );

		for ( size_t k = 0; k < sizeof( kWords ) / sizeof( kWords[ 0 ] ); k++ )
		{
			if ( word == kWords[ k ] )
			{
				n++;
				break;
			}
		}
	}

	return n;
}

//-----------------------------------------------------------------------------
// One function's body, so a count can be scoped to the function rather than to
// the file. Brace-matched from the signature's opening `{`.
//
// Answers an empty string for a signature that is not there, so every caller
// asserts non-empty first -- a count over nothing is zero, which is the answer
// a correct file would also give.
//-----------------------------------------------------------------------------
std::string BodyOf( const std::string &str, const std::string &strSignature )
{
	const size_t at = str.find( strSignature );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = str.find( '{', at );
	if ( open == std::string::npos )
		return std::string();

	int depth = 0;
	for ( size_t i = open; i < str.size(); i++ )
	{
		if ( str[ i ] == '{' )
		{
			depth++;
		}
		else if ( str[ i ] == '}' )
		{
			depth--;
			if ( depth == 0 )
				return str.substr( open, i - open + 1 );
		}
	}

	return std::string();
}

} // namespace

TEST( AliasGuardScanner, ItTellsAGuardedNeedleFromAnUnguardedOne )
{
	// THE INSTRUMENT, CHECKED. Six facts below rest on this answer and a helper
	// that returned the same value for everything would make all six pass
	// without measuring anything.
	const std::string kSample =
		"live_one();\n"
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"dead_one();\n"
		"#endif // !SS_CBASEENTITY_ALIAS\n"
		"after_one();\n";

	EXPECT_TRUE( IsInsideAliasGuard( kSample, "dead_one();" ) );
	EXPECT_FALSE( IsInsideAliasGuard( kSample, "live_one();" ) );
	EXPECT_FALSE( IsInsideAliasGuard( kSample, "after_one();" ) )
		<< "the scan ran past the #endif, which would report every later line in "
		   "a file as dead";
	EXPECT_FALSE( IsInsideAliasGuard( kSample, "not_in_the_file();" ) )
		<< "a needle that is absent must not read as guarded -- that answer would "
		   "let a deleted line pass as a relocated one";
}

TEST( AliasGuardScanner, TheDirectiveCounterAndTheBodyScopeAgreeWithASample )
{
	// THE TWO INSTRUMENTS THE `the-close` WAVE ADDED, CHECKED BEFORE THEY ARE
	// BELIEVED. A directive counter that always answered zero would certify
	// every file in the tree as fully compiled -- which is the exact claim the
	// facts below rest on, so it is the exact claim a broken counter would
	// fabricate.
	const std::string kGuarded =
		"#include \"x.h\"\n"
		"#define K 1\n"
		"#pragma once\n"
		"const char *s = \"#ifdef NOT_A_DIRECTIVE\";\n"
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"dead();\n"
		"#else\n"
		"armed();\n"
		"#endif\n";

	// Three selecting directives: the #ifndef, the #else, the #endif is not
	// counted because it selects nothing on its own.
	EXPECT_EQ( 2, CountConditionalDirectives( kGuarded ) )
		<< "#include / #define / #pragma, or a directive spelled inside a string "
		   "literal, is being counted as conditional compilation";

	EXPECT_EQ( 0, CountConditionalDirectives(
		"#include \"x.h\"\nint f() { return 1; }\n" ) )
		<< "a file with no conditional compilation must count zero, or the "
		   "liveness fact below can never pass";

	// #ifdef is not #if, which matters because a substring test would count
	// every #ifdef twice and make a clean file read as guarded.
	EXPECT_EQ( 1, CountConditionalDirectives( "#ifdef FOO\nx();\n#endif\n" ) );

	// The body scope, which several facts below use to say "this whole FUNCTION
	// has no entity read left" rather than "this file does not".
	const std::string kTwo =
		"int a() { return 1; }\n"
		"int b() { if ( x ) { return 2; } return 3; }\n";

	EXPECT_EQ( "{ return 1; }", BodyOf( kTwo, "int a()" ) );
	EXPECT_EQ( "{ if ( x ) { return 2; } return 3; }", BodyOf( kTwo, "int b()" ) )
		<< "the brace match stopped at the first inner `}`, so every body would "
		   "be truncated and every absence inside one would pass";
	EXPECT_EQ( "", BodyOf( kTwo, "int missing()" ) )
		<< "a signature that is not there must answer empty, so a caller can "
		   "tell it apart from a body with nothing in it";
}

TEST( AliasGuardScanner, TheShimsCompileEveryLineTheseFactsRead )
{
	// RULE 2, DISCHARGED ONCE FOR THE WHOLE FILE.
	//
	// Finding 3 of .claude/entity-port/status/residue-tests.md: six facts in
	// this file read shell_recvtable.cpp:204-229 as their reference, and all of
	// it was inside `#ifndef SS_CBASEENTITY_ALIAS`. game/client/client_csharp.vpc:74
	// defines that macro and it is the only client configuration this tree
	// builds, so those six were green while describing text no compiler had
	// ever seen.
	//
	// The close deleted every such arm from both shims (f288a5e11). What makes
	// the re-pointed facts below sound is not that they name different lines --
	// it is THIS: neither shim has a conditional directive left in it, so there
	// is no arm for a reference to hide in. A guard reappearing in either file
	// reddens this fact FIRST, before any of the individual facts have to
	// notice it one needle at a time.
	EXPECT_EQ( 0, CountConditionalDirectives( ReadFile( kRecvShimForLiveness ) ) )
		<< kRecvShimForLiveness << " has conditional compilation in it again. "
		   "This is the file finding 3 was filed about.";

	// PAIRED WITH A READ-BACK, because an unreadable file also counts zero and
	// that is the vacuous pass this whole rule exists to stop.
	ASSERT_FALSE( ReadFile( kRecvShimForLiveness ).empty() )
		<< "could not read " << kRecvShimForLiveness;
}

//=============================================================================
// PART 2 — recvproxy.cpp AND dt_utlvector_recv.cpp LEFT client.so
//
// plan_client_novalve C8 (lane c6-input). recvproxy.cpp was de-entitied in
// place (lane c7-proxies) and then had no importer at all: nm over the
// client's link-line objects found no object but its own using any of its
// thirteen strong definitions, and dt_utlvector_recv.cpp's five likewise. Every
// recv table the engine walks is built by shell_recvtable.cpp from C#
// declarations, with the standard proxies of dt_recv.cpp. The seam
// recvproxy.cpp's every-tick proxies called, SourceSharp_RecvTableInterpolation
// AmountsChanged, was empty on purpose and left with its one caller.
//=============================================================================

namespace
{
const char *kRecvShim = "sourcesharp/game/client/shell_recvtable.cpp";
} // namespace

TEST( RecvProxyLeft, NoReplacedRecvSourceIsInTheFarm )
{
	// The farm holds only what client.so compiles: absent there is absent from
	// the build. dt_recv.h, the allowlisted header the replacements are written
	// against, stays, and is the proof the root resolved.
	ASSERT_FALSE( ReadFile( "public/dt_recv.h" ).empty() )
		<< "public/dt_recv.h did not read: the absences below would pass vacuously";

	// C8: no importer, so they left.
	EXPECT_TRUE( ReadFile( "game/client/recvproxy.cpp" ).empty() ) << "recvproxy.cpp is back in the farm";
	EXPECT_TRUE( ReadFile( "public/dt_utlvector_recv.cpp" ).empty() ) << "dt_utlvector_recv.cpp is back in the farm";

	// C8: replaced by engineabi/recv.h's builders (engineabi_recv.cpp, held to
	// upstream by recv_builders_oracle_test) and their Valve names
	// (engineabi_recv_names.cpp, client_recv_names_test).
	const char *const kReplaced[] = {
		"public/dt_recv.cpp", "public/client_class.cpp", "public/networkvar.cpp",
		"public/renamed_recvtable_compat.cpp",
	};
	for ( const char *pszFile : kReplaced )
		EXPECT_TRUE( ReadFile( pszFile ).empty() ) << pszFile << " is back in the farm";
}

TEST( RecvProxyLeft, TheEmptySeamLeftWithItsCaller )
{
	const std::string str = StripComments( ReadFile( kRecvShim ) );
	ASSERT_FALSE( str.empty() ) << "could not read " << kRecvShim;

	EXPECT_EQ( 0, CountOccurrences( str, "SourceSharp_RecvTableInterpolationAmountsChanged" ) )
		<< "the seam recvproxy.cpp called is back with no caller";

	// RETENTION: the recv tables are still walked from the class list the
	// engine reads, by the dev API's route over shell_recvtable.cpp.
	EXPECT_NE( std::string::npos,
		BodyOf( str, "int32_t SourceSharp_ClientRecvProp(" ).find( "g_pClientClassHead" ) )
		<< "shell_recvtable.cpp no longer walks the ClientClass list";
}
