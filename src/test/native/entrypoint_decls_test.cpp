//========= SourceSharp — the generated entry-point prototypes ===============//
//
// plan_shim.md §5 3e. `sourcesharp_gamesystem.cpp` opened with 483 lines of
// hand-written prototypes, one per engine-API slot whose body is in some other
// shim file, and every one of them was a SECOND SPELLING of a signature
// `SourceSharpEngineApi` already declares. Nothing checked the two against each
// other: `int32_t` where the field says `int`, or a dropped `const`, is a
// difference neither the compiler nor a reviewer sees. They are derived now, by
// `tools/entrypoint-decls`, into `sourcesharp_entrypoints.h`.
//
// THE FILL BLOCK THIS FILE FIRST READ IS GONE (plan_engineapi_vtables C6c on
// the server, plan_client_vtables CC-last on the client). A slot is now WIRED
// by its shim-area method's generated forward to its body, one forward file per
// side; "the fill" below means those forwards.
//
// WHAT A GENERATED FILE CAN DO THAT HAND-WRITTEN ONES CANNOT: go stale. A slot
// wired without a regeneration is a missing declaration,
// which IS a compile error and is therefore not what this file is about. The
// failure that is silent is the OTHER direction and the SIDE:
//
//   * A declaration for a slot the fill no longer wires. It compiles. It links.
//     It is a promise that a function exists, made to every translation unit
//     that includes the header, about a body nobody calls -- and the day
//     somebody DOES call it, the link fails somewhere else entirely.
//
//   * A prototype on the wrong SIDE. Only the server's forwards wire a slot
//     (their file is included inside `#ifndef CLIENT_DLL`); the header declares
//     it unguarded. The client then carries a
//     declaration of a function it has no body for. Nothing fails until a
//     client-side caller appears, and the error it gets names the header rather
//     than the guard that is wrong.
//
//   * THE FILE BEING EDITED BY HAND. It says DO NOT EDIT at the top, which has
//     never stopped anybody. An edit is invisible until the next regeneration
//     silently throws it away.
//
// SO THIS FILE RE-DERIVES THE ANSWER, in C++, and compares. The generator reads
// the slot table (docs/engineapi-areas.tsv); this reads the forward files
// generated FROM it and the guards they are included under, which is what the
// compiler actually sees. It is a second implementation on purpose: two scans
// that agree is evidence, one scan checked against its own output is not.
//
//=============================================================================//
#include "gtest/gtest.h"

#include <sys/stat.h>

#include <cctype>
#include <fstream>
#include <map>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string s = std::string( kCandidates[ i ] ) + "/sourcesharp/host/sourcesharp.h";
		if ( stat( s.c_str(), &st ) == 0 )
			return kCandidates[ i ];
	}
	return NULL;
}

std::string Read( const char *pRelative )
{
	const char *pRoot = SrcRoot();
	if ( !pRoot )
		return std::string();

	std::ifstream in( ( std::string( pRoot ) + "/" + pRelative ).c_str() );
	if ( !in )
		return std::string();

	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

std::vector< std::string > Lines( const std::string &text )
{
	std::vector< std::string > out;
	std::istringstream in( text );
	std::string line;
	while ( std::getline( in, line ) )
		out.push_back( line );
	return out;
}

std::string Trim( const std::string &s )
{
	size_t a = 0, b = s.size();
	while ( a < b && isspace( (unsigned char)s[ a ] ) ) a++;
	while ( b > a && isspace( (unsigned char)s[ b - 1 ] ) ) b--;
	return s.substr( a, b - a );
}

bool StartsWith( const std::string &s, const char *p )
{
	return s.compare( 0, strlen( p ), p ) == 0;
}

//-----------------------------------------------------------------------------
// THE SIDE EACH LINE OF A FILE IS COMPILED ON, from its CLIENT_DLL guards:
// "" both sides, "server" inside #ifndef CLIENT_DLL, "client" inside #ifdef
// CLIENT_DLL (and each one's #else is the other).
//-----------------------------------------------------------------------------
std::vector< std::string > GuardSides( const std::vector< std::string > &lines )
{
	std::vector< std::string > out( lines.size() );
	std::vector< std::string > stack;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string s = Trim( lines[ i ] );

		if ( StartsWith( s, "#ifndef CLIENT_DLL" ) )      stack.push_back( "server" );
		else if ( StartsWith( s, "#ifdef CLIENT_DLL" ) )  stack.push_back( "client" );
		else if ( StartsWith( s, "#elif" ) )
		{
			// Not the `if` arm and not its negation. Unknown, which is what ""
			// means everywhere else here.
			if ( !stack.empty() )
				stack.back() = "";
		}
		else if ( StartsWith( s, "#if" ) )                stack.push_back( "" );
		else if ( StartsWith( s, "#else" ) )
		{
			if ( !stack.empty() )
			{
				if ( stack.back() == "server" )      stack.back() = "client";
				else if ( stack.back() == "client" ) stack.back() = "server";
				else                                 stack.back() = "";
			}
		}
		else if ( StartsWith( s, "#endif" ) )
		{
			if ( !stack.empty() )
				stack.pop_back();
		}

		std::string side;
		for ( size_t k = 0; k < stack.size(); k++ )
		{
			if ( !stack[ k ].empty() )
				side = stack[ k ];
		}
		out[ i ] = side;
	}
	return out;
}

//-----------------------------------------------------------------------------
// The side sourcesharp_gamesystem.cpp compiles `#include "<inc>"` on, or "?"
// when it does not include it at all.
//-----------------------------------------------------------------------------
std::string IncludedOn( const char *pszInc )
{
	const std::vector< std::string > lines =
		Lines( Read( "sourcesharp/game/shared/sourcesharp_gamesystem.cpp" ) );
	const std::vector< std::string > sides = GuardSides( lines );
	const std::string want = std::string( "#include \"" ) + pszInc + "\"";
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( Trim( lines[ i ] ) == want )
			return sides[ i ];
	}
	return "?";
}

//-----------------------------------------------------------------------------
// WHAT EACH DLL WIRES: every FUNCTION a slot is wired to, the slot, and the
// side(s) it is wired on. "" both sides, "server", "client" -- as above.
//
// SINCE plan_client_vtables CC-last NO DLL FILLS A STRUCT. Every slot is a
// shim-area method whose GENERATED forward names its body --
// `int32_t CShimX::Slot( ... )` and, on the next line,
// `{ return ::SourceSharp_Body( ... ); }` -- in shell_shim_areas.inc for the
// server and shell_shim_areas_client.inc for the client. Which side a file's
// forwards are on is NOT assumed: it is the guard around that file's
// `#include` in sourcesharp_gamesystem.cpp, read the same way the fill's
// guards were. tools/entrypoint-decls derives the same answer from
// docs/engineapi-areas.tsv, the table both files are generated from; reading
// the forwards instead keeps this a second implementation rather than the
// generator's own input read twice.
//
// KEYED BY THE FUNCTION AND NOT BY THE SLOT, and that is the whole shape of
// this map rather than a detail. A SLOT WIRED ON BOTH SIDES CAN HAVE TWO
// FUNCTIONS BEHIND IT: `EntityGetIndex` is `SourceSharp_EntityGetIndex` on
// the server and `SourceSharp_ClientEntityGetIndex` on the client -- one slot,
// two bodies, one per DLL. Keyed by slot, the second overwrites the first and
// one of the two functions is never looked at.
//
// BOTH THE GENERATOR AND THIS FILE MADE THAT MISTAKE, which is worth writing
// down: a second implementation that repeats the first's error agrees with it
// perfectly and proves nothing. What caught it was neither -- it was reading
// the hand-written declarations the generator had NOT managed to replace and
// asking why six were left.
//-----------------------------------------------------------------------------
std::map< std::string, std::pair< std::string, std::set< std::string > > > Fill()
{
	// function -> (slot, sides)
	std::map< std::string, std::pair< std::string, std::set< std::string > > > out;

	static const char *const kForwards[] = { "shell_shim_areas.inc", "shell_shim_areas_client.inc" };
	for ( size_t f = 0; f < sizeof( kForwards ) / sizeof( kForwards[ 0 ] ); f++ )
	{
		const std::string side = IncludedOn( kForwards[ f ] );
		if ( side == "?" )
			continue;   // not included: wires nothing, and the >= 300 floors say so

		const std::vector< std::string > impl =
			Lines( Read( ( std::string( "sourcesharp/game/shared/" ) + kForwards[ f ] ).c_str() ) );
		for ( size_t i = 1; i < impl.size(); i++ )
		{
			const std::string s = Trim( impl[ i ] );
			if ( !StartsWith( s, "{ return ::SourceSharp_" ) )
				continue;

			const size_t open = s.find( '(' );
			const std::string &def = impl[ i - 1 ];
			const size_t scope = def.find( "::" );
			const size_t paren = def.find( '(', scope == std::string::npos ? 0 : scope );
			if ( scope == std::string::npos || paren == std::string::npos || open == std::string::npos )
				continue;
			if ( def.substr( 0, scope ).find( "CShim" ) == std::string::npos )
				continue;

			const std::string fn = Trim( s.substr( strlen( "{ return ::" ), open - strlen( "{ return ::" ) ) );
			out[ fn ].first = Trim( def.substr( scope + 2, paren - scope - 2 ) );
			out[ fn ].second.insert( side );
		}
	}

	// And the one slot the ROOT serves, hand-written in shell_internalfactory.cpp
	// (C5): AcquireInterface is IShimRoot's method, not an area's, on the side(s)
	// that file compiles it on.
	const std::vector< std::string > root =
		Lines( Read( "sourcesharp/game/shared/shell_internalfactory.cpp" ) );
	const std::vector< std::string > rootSides = GuardSides( root );
	for ( size_t i = 0; i < root.size(); i++ )
	{
		if ( root[ i ].find( "return SourceSharp_AcquireInterface(" ) == std::string::npos )
			continue;
		out[ "SourceSharp_AcquireInterface" ].first = "AcquireInterface";
		out[ "SourceSharp_AcquireInterface" ].second.insert( rootSides[ i ] );
	}
	return out;
}

//-----------------------------------------------------------------------------
// The generated header: every declared name, and the guard it is under.
//-----------------------------------------------------------------------------
std::map< std::string, std::string > Declared()
{
	std::map< std::string, std::string > out;

	const std::vector< std::string > lines =
		Lines( Read( "sourcesharp/game/shared/sourcesharp_entrypoints.h" ) );

	std::vector< std::string > stack;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string s = Trim( lines[ i ] );

		if ( StartsWith( s, "#ifndef CLIENT_DLL" ) ) { stack.push_back( "server" ); continue; }
		if ( StartsWith( s, "#ifdef CLIENT_DLL" ) )  { stack.push_back( "client" ); continue; }
		if ( StartsWith( s, "#ifndef SOURCESHARP_ENTRYPOINTS_H" ) ) { continue; }
		if ( StartsWith( s, "#ifdef _WIN32" ) )      { stack.push_back( "" );       continue; }
		if ( StartsWith( s, "#if" ) )                { stack.push_back( "" );       continue; }
		if ( StartsWith( s, "#endif" ) )
		{
			if ( !stack.empty() )
				stack.pop_back();
			continue;
		}
		if ( StartsWith( s, "//" ) || StartsWith( s, "#" ) || s.empty() )
			continue;

		const size_t at = s.find( "SourceSharp_" );
		if ( at == std::string::npos )
			continue;
		size_t e = at;
		while ( e < s.size() && ( isalnum( (unsigned char)s[ e ] ) || s[ e ] == '_' ) )
			e++;
		if ( e >= s.size() || s[ e ] != '(' )
			continue;

		std::string side;
		for ( size_t k = 0; k < stack.size(); k++ )
		{
			if ( !stack[ k ].empty() )
				side = stack[ k ];
		}
		out[ s.substr( at, e - at ) ] = side;
	}
	return out;
}

//-----------------------------------------------------------------------------
// Functions DEFINED in sourcesharp_gamesystem.cpp itself need no prototype.
//-----------------------------------------------------------------------------
std::set< std::string > DefinedInGameSystem()
{
	std::set< std::string > out;
	const std::string cpp = Read( "sourcesharp/game/shared/sourcesharp_gamesystem.cpp" );

	for ( size_t pos = cpp.find( "SourceSharp_" ); pos != std::string::npos;
	      pos = cpp.find( "SourceSharp_", pos + 1 ) )
	{
		size_t e = pos;
		while ( e < cpp.size() && ( isalnum( (unsigned char)cpp[ e ] ) || cpp[ e ] == '_' ) )
			e++;
		if ( e >= cpp.size() || cpp[ e ] != '(' )
			continue;

		// The definition form: the matching `)` is followed by `\n{`.
		size_t depth = 1, k = e + 1;
		while ( k < cpp.size() && depth )
		{
			if ( cpp[ k ] == '(' ) depth++;
			else if ( cpp[ k ] == ')' ) depth--;
			k++;
		}
		while ( k < cpp.size() && isspace( (unsigned char)cpp[ k ] ) )
			k++;
		if ( k < cpp.size() && cpp[ k ] == '{' )
			out.insert( cpp.substr( pos, e - pos ) );
	}
	return out;
}

} // namespace

TEST( EntrypointDecls, TheInputsAndTheGeneratedFileAllOpened )
{
	ASSERT_FALSE( Read( "sourcesharp/host/sourcesharp.h" ).empty() );
	ASSERT_FALSE( Read( "sourcesharp/game/shared/sourcesharp_gamesystem.cpp" ).empty() );
	ASSERT_FALSE( Read( "sourcesharp/game/shared/sourcesharp_entrypoints.h" ).empty() )
		<< "sourcesharp_entrypoints.h did not open. It is generated by "
		   "tools/entrypoint-decls --write and is committed; a build without it "
		   "does not compile, so a missing file here means the scan is looking "
		   "in the wrong place.";
}

TEST( EntrypointDecls, TheHeaderSaysItIsGeneratedAndNamesItsGenerator )
{
	const std::string h = Read( "sourcesharp/game/shared/sourcesharp_entrypoints.h" );
	ASSERT_FALSE( h.empty() );

	EXPECT_NE( h.find( "DO NOT EDIT" ), std::string::npos );
	EXPECT_NE( h.find( "tools/entrypoint-decls" ), std::string::npos )
		<< "the header does not name the tool that writes it, so a reader who "
		   "wants to change a declaration has nowhere to go but the file itself "
		   "-- and an edit there is thrown away by the next regeneration.";
}

TEST( EntrypointDecls, EverySlotTheFillWiresIsDeclaredOrDefinedHere )
{
	const std::map< std::string, std::pair< std::string, std::set< std::string > > > fill = Fill();
	const std::map< std::string, std::string > declared = Declared();
	const std::set< std::string > local = DefinedInGameSystem();

	ASSERT_GE( fill.size(), 300u )
		<< "only " << fill.size() << " wired functions parsed out of the shim-area forwards; "
		   "the scan lost its subject and every check below is vacuous";
	ASSERT_GE( declared.size(), 300u )
		<< "only " << declared.size() << " declarations parsed out of the "
		   "generated header; the scan lost its subject";

	// THE ROOT'S SLOT IS DECLARED BY HAND, beside its definition in
	// shell_vtable_interfaces.h: IShimRoot spells its first parameter int32_t
	// and the body a SourceSharpFactoryType, so tools/entrypoint-decls skips it
	// (it took its signature from the engine API struct, which plan_client_vtables
	// CC-last deleted). Only that one slot may be declared there; a prototype
	// at the start of a line, ended by ';' before any '{', is a declaration.
	const std::string rootHeader = Read( "sourcesharp/game/shared/shell_vtable_interfaces.h" );
	ASSERT_FALSE( rootHeader.empty() ) << "shell_vtable_interfaces.h did not open";
	const size_t rootAt = rootHeader.find( "\nvoid *SourceSharp_AcquireInterface(" );
	const size_t rootEnd = rootAt == std::string::npos ? rootAt : rootHeader.find( ';', rootAt );
	const bool rootDeclared = rootEnd != std::string::npos
		&& rootHeader.substr( rootAt, rootEnd - rootAt ).find( '{' ) == std::string::npos;

	std::vector< std::string > missing;
	for ( std::map< std::string, std::pair< std::string, std::set< std::string > > >::const_iterator
	          it = fill.begin(); it != fill.end(); ++it )
	{
		const std::string &fn = it->first;
		if ( local.count( fn ) )
			continue;
		if ( it->second.first == "AcquireInterface" && fn == "SourceSharp_AcquireInterface" && rootDeclared )
			continue;
		if ( !declared.count( fn ) )
			missing.push_back( fn + " (slot " + it->second.first + ")" );
	}

	EXPECT_TRUE( missing.empty() )
		<< missing.size() << " slot(s) are wired by a shim-area forward and have no "
		   "declaration in sourcesharp_entrypoints.h and no definition in "
		   "sourcesharp_gamesystem.cpp. Run tools/entrypoint-decls --write. "
		<< [ & ]{ std::string s; for ( size_t i = 0; i < missing.size() && i < 8; i++ )
		          { s += missing[ i ]; s += " "; } return s; }();
}

TEST( EntrypointDecls, NoDeclarationIsLeftForASlotTheFillNoLongerWires )
{
	const std::map< std::string, std::pair< std::string, std::set< std::string > > > fill = Fill();
	const std::map< std::string, std::string > declared = Declared();

	ASSERT_GE( declared.size(), 300u );

	std::set< std::string > wired;
	for ( std::map< std::string, std::pair< std::string, std::set< std::string > > >::const_iterator
	          it = fill.begin(); it != fill.end(); ++it )
	{
		wired.insert( it->first );
	}

	std::vector< std::string > orphans;
	for ( std::map< std::string, std::string >::const_iterator it = declared.begin();
	      it != declared.end(); ++it )
	{
		if ( !wired.count( it->first ) )
			orphans.push_back( it->first );
	}

	EXPECT_TRUE( orphans.empty() )
		<< orphans.size() << " declaration(s) in sourcesharp_entrypoints.h are "
		   "for slots no shim-area forward wires. This is the half that does "
		   "NOT fail to compile: the header promises a function exists to every "
		   "translation unit that includes it, and the link fails only when "
		   "somebody takes it up on that. Run tools/entrypoint-decls --write. "
		<< [ & ]{ std::string s; for ( size_t i = 0; i < orphans.size() && i < 8; i++ )
		          { s += orphans[ i ]; s += " "; } return s; }();
}

TEST( EntrypointDecls, EveryDeclarationIsOnTheSideTheFillWiresIt )
{
	const std::map< std::string, std::pair< std::string, std::set< std::string > > > fill = Fill();
	const std::map< std::string, std::string > declared = Declared();

	ASSERT_GE( fill.size(), 300u );
	ASSERT_GE( declared.size(), 300u );

	int nChecked = 0;
	std::vector< std::string > wrong;
	for ( std::map< std::string, std::pair< std::string, std::set< std::string > > >::const_iterator
	          it = fill.begin(); it != fill.end(); ++it )
	{
		const std::string &fn = it->first;
		const std::set< std::string > &sides = it->second.second;

		std::map< std::string, std::string >::const_iterator d = declared.find( fn );
		if ( d == declared.end() )
			continue;

		std::string want;
		if ( sides.count( "" ) || ( sides.count( "server" ) && sides.count( "client" ) ) )
			want = "";
		else if ( sides.count( "server" ) )
			want = "server";
		else if ( sides.count( "client" ) )
			want = "client";

		nChecked++;
		if ( d->second != want )
		{
			wrong.push_back( fn + " declared on \"" + d->second +
			                 "\", wired on \"" + want + "\"" );
		}
	}

	EXPECT_GE( nChecked, 300 )
		<< "only " << nChecked << " slots were side-checked; the join between "
		   "the two scans has stopped matching and this fact is vacuous";

	EXPECT_TRUE( wrong.empty() )
		<< wrong.size() << " declaration(s) are guarded differently from the "
		   "forward that wires them. A prototype on the wrong side is a "
		   "promise of a body the DLL does not have, and nothing fails until a "
		   "caller on that side appears. Run tools/entrypoint-decls --write. "
		<< [ & ]{ std::string s; for ( size_t i = 0; i < wrong.size() && i < 8; i++ )
		          { s += wrong[ i ]; s += "; "; } return s; }();
}

TEST( EntrypointDecls, ATwoArmedSlotDeclaresBothOfItsBodies )
{
	const std::map< std::string, std::pair< std::string, std::set< std::string > > > fill = Fill();
	const std::map< std::string, std::string > declared = Declared();

	ASSERT_GE( fill.size(), 300u );
	ASSERT_GE( declared.size(), 300u );

	// The six slots this tree wires once per side, each with a DIFFERENT
	// function. They are named rather than discovered because the failure being
	// pinned is that one of each pair goes missing -- and a scan that discovers
	// the pairs from the same map would lose the same half.
	static const char *const kPairs[ 6 ][ 2 ] =
	{
		{ "SourceSharp_EntityGetIndex",       "SourceSharp_ClientEntityGetIndex" },
		{ "SourceSharp_EntityGetOrigin",      "SourceSharp_ClientEntityGetOrigin" },
		{ "SourceSharp_EntityGetAngles",      "SourceSharp_ClientEntityGetAngles" },
		{ "SourceSharp_EntityGetModelName",   "SourceSharp_ClientEntityGetModelName" },
		{ "SourceSharp_EntitiesInvalidateMdlCacheAll",
		  "SourceSharp_ClientEntitiesInvalidateMdlCacheAll" },
		// SourceSharp_TempEntRegister left the server (plan_server_native_v2 T4b):
		// the slot is the client's alone, so it is no pair.
		// Lane side-audit-2 (2026-10-02): the client answers liveness from its
		// own list, so a cs_cl_reload's restored handles survive.
		{ "SourceSharp_EntityIsValid",        "SourceSharp_ClientEntityIsValid" },
	};

	for ( int i = 0; i < 6; i++ )
	{
		std::map< std::string, std::string >::const_iterator sv = declared.find( kPairs[ i ][ 0 ] );
		std::map< std::string, std::string >::const_iterator cl = declared.find( kPairs[ i ][ 1 ] );

		ASSERT_NE( sv, declared.end() )
			<< kPairs[ i ][ 0 ] << " has no declaration. It shares a slot with "
			<< kPairs[ i ][ 1 ] << ", and a generator keyed by SLOT keeps only "
			   "whichever of the two it reads last.";
		ASSERT_NE( cl, declared.end() )
			<< kPairs[ i ][ 1 ] << " has no declaration, for the same reason.";

		EXPECT_EQ( "server", sv->second ) << kPairs[ i ][ 0 ];
		EXPECT_EQ( "client", cl->second ) << kPairs[ i ][ 1 ];
	}
}

TEST( EntrypointDecls, TheProseThatIntroducedThemIsFindable )
{
	const std::string doc = Read( "../docs/engine-boundary.md" );
	ASSERT_FALSE( doc.empty() )
		<< "docs/engine-boundary.md did not open. The prototypes' prose moved "
		   "there when they were generated; without it the generated header is "
		   "435 signatures and no reason for any of them.";

	EXPECT_NE( doc.find( "# Appendix: where each entry point's body lives" ),
	           std::string::npos )
		<< "the appendix the prototypes' prose moved into is gone.";

	// A sample of slots whose paragraph said something a signature cannot: not
	// a list to maintain, three spot-checks that the move carried content.
	for ( const char *pszSlot : { "SourceSharp_MoveTrace",
	                              "SourceSharp_EntitySetParent",
	                              "SourceSharp_StringTableCreate" } )
	{
		EXPECT_NE( doc.find( std::string( "## `" ) + pszSlot + "`" ), std::string::npos )
			<< pszSlot << " has no section in the appendix. Its paragraph named "
			   "the file its body lives in, which is the one thing the generated "
			   "header cannot say.";
	}
}
