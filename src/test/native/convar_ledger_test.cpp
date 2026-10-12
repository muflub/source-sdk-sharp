//========= SourceSharp — the ConVar ledger is the whole native keep list ====//
//
// plan_entity.md §6's rule, and plan_shim.md Phase 5's deliverable:
//
//     No ConVar's default or semantics is declared natively unless the engine
//     itself transmits or resolves the name.
//
// docs/convar-ledger.tsv is the ruling on every such declaration in the tree —
// one row per name, with the verdict and the evidence behind it. This file is
// what makes the ledger load-bearing rather than a document: it scans
// src/sourcesharp for ConVar and ConCommand CONSTRUCTORS and requires the set
// it finds to be exactly the set the ledger says should be there.
//
// THE COLUMN THAT IS PINNED IS `native_now`, NOT `verdict`. The verdict is the
// ruling — "this ought to be managed" — and several rows carry a ruling whose
// move has not landed (`managed-queued`, `reader-owed`, `deferred-abi`,
// `deferred-lane`). A test that pinned the ruling would be red for work that is
// correctly not done yet. `native_now` is the FACT: whether a native
// declaration of this name is expected in the tree at this commit. Ruling and
// fact are separate columns so that neither can quietly become the other.
//
// IT HAS TO FAIL BOTH WAYS, because a keep list rots in two directions and only
// one of them is obvious:
//
//   * A NATIVE DECLARATION APPEARS THAT THE LEDGER DOES NOT LIST, or lists with
//     native_now=no. That is the obvious direction — somebody declared a tuning
//     value in C++ again.
//
//   * A LEDGER ROW SURVIVES ITS CONVAR. This is the quiet one. A row saying
//     "keep this native, the engine resolves it" for a ConVar that was deleted
//     or renamed months ago reads as a live exemption and licenses the next
//     declaration that looks like it. An allow-list that is never checked
//     against reality is how these rot, so every native_now=yes row is required
//     to still have a declaration at the file and line it names.
//
// AND IT HAS TO BE ABLE TO SEE ANYTHING AT ALL. Facts 2, 3 and 5 are all
// quantified over "every declaration the scanner found", so a scanner that
// found nothing would satisfy all three and report green — one of this tree's
// recorded checks-that-cannot-fail. ScannerSeesTheDeclarationsWeKnowAreThere
// pins a floor and a handful of names by hand, and is the reason the others
// mean something.
//
// THREE DECLARATIONS THE SCANNER CANNOT SEE, and are in the ledger anyway:
// `cl_team` and `cl_class` are ConVar MEMBERS of the client shell's class, and
// the verbosity convar takes its name from a macro that differs per side
// (SOURCESHARP_VERBOSE_CVAR). A constructor scan keyed on a string literal
// finds none of the three. They are checked the other way round instead — the
// ledger names the file and line, and that line is required to still declare a
// ConVar — which is the same check every other row gets for its survival half.
//
// Run from src/, as buildcpptests does: paths here are relative to that.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>
#include <sys/stat.h>

#include <algorithm>
#include <fstream>
#include <map>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{

const char *const kLedger  = "../docs/convar-ledger.tsv";
const char *const kShimDir = "sourcesharp";

//-----------------------------------------------------------------------------
// The ledger
//-----------------------------------------------------------------------------

struct Row
{
	std::string name;
	std::string kind;
	std::string file;     // relative to src/
	int         line;
	std::string verdict;
	bool        nativeNow;
	std::string evidence;
};

std::vector< std::string > Split( const std::string &line, char sep )
{
	std::vector< std::string > out;
	std::string cur;
	for ( size_t i = 0; i < line.size(); i++ )
	{
		if ( line[ i ] == sep ) { out.push_back( cur ); cur.clear(); }
		else                    { cur += line[ i ]; }
	}
	out.push_back( cur );
	return out;
}

const std::vector< Row > &Ledger()
{
	static std::vector< Row > rows;
	static bool loaded = false;
	if ( loaded )
		return rows;
	loaded = true;

	std::ifstream f( kLedger );
	if ( !f.good() )
		return rows;

	std::string line;
	bool header = true;
	while ( std::getline( f, line ) )
	{
		if ( header ) { header = false; continue; }
		if ( line.empty() )
			continue;

		const std::vector< std::string > c = Split( line, '\t' );
		if ( c.size() < 12 )
			continue;

		Row r;
		r.name      = c[ 0 ];
		r.kind      = c[ 1 ];
		r.file      = c[ 3 ];
		r.line      = atoi( c[ 4 ].c_str() );
		r.verdict   = c[ 6 ];
		r.nativeNow = ( c[ 7 ] == "yes" );
		r.evidence  = c[ 11 ];
		rows.push_back( r );
	}
	return rows;
}

//-----------------------------------------------------------------------------
// The scan
//
// Comments are stripped first. Every one of these names is written down in
// prose somewhere in this tree -- the ledger's own reasons are in .cpp banners
// -- and a needle a COMMENT can satisfy is a needle that stops being about the
// code. Four tests in this directory have gone red on exactly that.
//-----------------------------------------------------------------------------

std::string ReadFile( const std::string &path )
{
	std::ifstream f( path.c_str() );
	if ( !f.good() )
		return std::string();
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

std::string Code( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );
	for ( size_t i = 0; i < in.size(); )
	{
		if ( in.compare( i, 2, "//" ) == 0 )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
		}
		else if ( in.compare( i, 2, "/*" ) == 0 )
		{
			i += 2;
			while ( i + 1 < in.size() && in.compare( i, 2, "*/" ) != 0 ) i++;
			i = ( i + 1 < in.size() ) ? i + 2 : in.size();
		}
		else
		{
			out += in[ i++ ];
		}
	}
	return out;
}

void WalkSources( const std::string &dir, std::vector< std::string > &out )
{
	DIR *d = opendir( dir.c_str() );
	if ( !d )
		return;

	for ( struct dirent *e = readdir( d ); e; e = readdir( d ) )
	{
		const std::string leaf = e->d_name;
		if ( leaf == "." || leaf == ".." )
			continue;

		const std::string path = dir + "/" + leaf;

		struct stat st;
		if ( stat( path.c_str(), &st ) != 0 )
			continue;

		if ( S_ISDIR( st.st_mode ) )
		{
			// sourcesharp/test is not compiled into either DLL, and its files
			// QUOTE these declarations -- a gtest needle carrying
			// `CON_COMMAND( report_entities` inside a string literal is a
			// sentence about a declaration, not one. Scanning it made this test
			// report ten declarations that do not exist.
			if ( leaf != "test" )
				WalkSources( path, out );
			continue;
		}

		const size_t dot = leaf.rfind( '.' );
		if ( dot == std::string::npos )
			continue;
		const std::string ext = leaf.substr( dot );
		if ( ext == ".cpp" || ext == ".h" || ext == ".cc" )
			out.push_back( path );
	}
	closedir( d );
}

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' ) ||
	       ( c >= '0' && c <= '9' ) || c == '_';
}

size_t SkipSpace( const std::string &s, size_t i )
{
	while ( i < s.size() && ( s[ i ] == ' ' || s[ i ] == '\t' ||
	                          s[ i ] == '\n' || s[ i ] == '\r' ) )
	{
		i++;
	}
	return i;
}

// A declaration this test is about is a CONSTRUCTOR: `ConVar x( "name", ...` or
// `ConCommand x( "name", ...`, or the CON_COMMAND / CON_COMMAND_F macro, whose
// first argument is the console name as a bare token rather than a string.
struct Decl
{
	std::string name;
	std::string file;
};

// True when everything between the start of this line and `pos` is whitespace,
// `static` or `const`.
bool AtDeclarationStart( const std::string &s, size_t pos )
{
	size_t b = pos;
	while ( b > 0 && s[ b - 1 ] != '\n' )
		b--;

	std::string lead = s.substr( b, pos - b );
	std::string word;
	for ( size_t i = 0; i <= lead.size(); i++ )
	{
		const char c = ( i < lead.size() ) ? lead[ i ] : ' ';
		if ( IsIdentChar( c ) )
		{
			word += c;
			continue;
		}
		if ( c != ' ' && c != '\t' && c != '\r' )
			return false;
		if ( !word.empty() && word != "static" && word != "const" )
			return false;
		word.clear();
	}
	return true;
}

void ScanText( const std::string &text, const std::string &file,
               std::vector< Decl > &out )
{
	static const char *const kTypes[] = { "ConVar", "ConCommand" };
	static const char *const kMacros[] = { "CON_COMMAND_F", "CON_COMMAND" };

	for ( size_t i = 0; i < text.size(); i++ )
	{
		if ( i && IsIdentChar( text[ i - 1 ] ) )
			continue;

		// ANCHORED TO THE START OF A LINE, past only whitespace and the two
		// words that may precede the type. A declaration is written that way in
		// every file in this tree; a mention inside a string literal or an
		// argument list is not, and counting one is how this test first came
		// back with ten declarations that were really gtest needles.
		if ( !AtDeclarationStart( text, i ) )
			continue;

		// CON_COMMAND( name, ... ) / CON_COMMAND_F( name, ... )
		bool macro = false;
		for ( size_t m = 0; m < 2 && !macro; m++ )
		{
			const std::string tok = kMacros[ m ];
			if ( text.compare( i, tok.size(), tok ) != 0 )
				continue;
			size_t j = i + tok.size();
			if ( j < text.size() && IsIdentChar( text[ j ] ) )
				continue;              // CON_COMMAND_EXTERN and friends
			j = SkipSpace( text, j );
			if ( j >= text.size() || text[ j ] != '(' )
				continue;
			j = SkipSpace( text, j + 1 );
			size_t s = j;
			while ( j < text.size() && IsIdentChar( text[ j ] ) ) j++;
			if ( j == s )
				continue;
			Decl d;
			d.name = text.substr( s, j - s );
			d.file = file;
			out.push_back( d );
			macro = true;
		}
		if ( macro )
			continue;

		// ConVar x( "name", ... ) / ConCommand x( "name", ... )
		for ( size_t t = 0; t < 2; t++ )
		{
			const std::string tok = kTypes[ t ];
			if ( text.compare( i, tok.size(), tok ) != 0 )
				continue;
			size_t j = i + tok.size();
			if ( j < text.size() && IsIdentChar( text[ j ] ) )
				continue;              // ConVarRef, ConCommandBase
			j = SkipSpace( text, j );
			size_t s = j;
			while ( j < text.size() && IsIdentChar( text[ j ] ) ) j++;
			if ( j == s )
				continue;              // `ConVar *` and the like
			j = SkipSpace( text, j );
			if ( j >= text.size() || text[ j ] != '(' )
				continue;
			j = SkipSpace( text, j + 1 );
			if ( j >= text.size() || text[ j ] != '"' )
				continue;              // a macro name, not a literal
			j++;
			size_t n = j;
			while ( j < text.size() && text[ j ] != '"' ) j++;
			Decl d;
			d.name = text.substr( n, j - n );
			d.file = file;
			out.push_back( d );
			break;
		}
	}
}

const std::vector< Decl > &Declarations()
{
	static std::vector< Decl > decls;
	static bool scanned = false;
	if ( scanned )
		return decls;
	scanned = true;

	std::vector< std::string > files;
	WalkSources( kShimDir, files );
	std::sort( files.begin(), files.end() );

	for ( size_t i = 0; i < files.size(); i++ )
		ScanText( Code( ReadFile( files[ i ] ) ), files[ i ], decls );

	return decls;
}

std::set< std::string > DeclaredNames()
{
	std::set< std::string > out;
	const std::vector< Decl > &d = Declarations();
	for ( size_t i = 0; i < d.size(); i++ )
		out.insert( d[ i ].name );
	return out;
}

// The recorded declaration line, and the two after it, with comments stripped.
// A declaration that was deleted takes its `ConVar` / `CON_COMMAND` token with
// it, and a row still claiming it is what the stale-allow-list fact catches.
std::string LineWindow( const std::string &file, int line )
{
	std::ifstream f( file.c_str() );
	if ( !f.good() )
		return std::string();

	std::string s, out;
	int n = 0;
	while ( std::getline( f, s ) )
	{
		n++;
		if ( n >= line && n <= line + 2 )
			out += s + "\n";
		if ( n > line + 2 )
			break;
	}
	return Code( out );
}

bool Mentions( const std::string &hay, const std::string &needle )
{
	return hay.find( needle ) != std::string::npos;
}

// The ledger's `file` column is repo-relative; this test runs in src/.
std::string InSrc( const std::string &repoRelative )
{
	const std::string prefix = "src/";
	if ( repoRelative.compare( 0, prefix.size(), prefix ) == 0 )
		return repoRelative.substr( prefix.size() );
	return "../" + repoRelative;
}

} // anonymous namespace

//=============================================================================//
// 0. THE INSTRUMENTS THEMSELVES
//
// Every fact below is quantified over one of these two sets. Either of them
// coming back empty would make the rest of the file pass while measuring
// nothing, so both are pinned first.
//=============================================================================//

TEST( ConVarLedger, LedgerLoadsAndHasRows )
{
	const std::vector< Row > &rows = Ledger();
	ASSERT_FALSE( rows.empty() )
		<< kLedger << " did not load, or has no rows. Every other fact in this "
		   "file is quantified over it and would pass vacuously.";
	// 125, not 135 (lane c5-proxies, 2026-10-05): rows leave with the native
	// commands plan_client_novalve ports or deletes, 133 after
	// cs_cl_alphaproxy and cs_cl_playerlogo went with shell_materialproxies.cpp,
	// 132 after cs_cl_dust_report went managed (lane c5-particles).
	EXPECT_GE( rows.size(), 125u )
		<< "the ledger was measured at 132 rows; a sudden collapse means the "
		   "file was truncated rather than that the tree got smaller";
}

TEST( ConVarLedger, ScannerSeesTheDeclarationsWeKnowAreThere )
{
	const std::set< std::string > found = DeclaredNames();

	ASSERT_FALSE( found.empty() )
		<< "the scanner found NO ConVar or ConCommand under " << kShimDir
		<< ". Every 'nothing outside the list' fact here would then pass "
		   "without looking at anything.";

	// A floor, not the exact count: lanes add and remove declarations, and a
	// test that pinned the total would be red for work that is going fine.
	//
	// 50, from 100, in plan_server_novalve.md P2.3c: the server's commands
	// and ConVars are declared in C# now, and what the scan finds is the
	// client's (65 names when that landed).
	EXPECT_GE( found.size(), 50u )
		<< "the scan was measured at 65 distinct names after the server's moved to C#";

	// And by name, so that a scanner which found only one SHAPE of declaration
	// still cannot pass. One of each kind it has to recognise:
	EXPECT_EQ( 1u, found.count( "sv_gravity" ) )        // ConVar, string literal
		<< "the scanner missed a plain `ConVar x( \"name\", ... )`";
	EXPECT_EQ( 1u, found.count( "cs_cl_te_pool_shell" ) ) // ConCommand
		<< "the scanner missed a plain `ConCommand x( \"name\", fn, ... )`";
	EXPECT_EQ( 1u, found.count( "cs_cl_crossings" ) )    // CON_COMMAND
		<< "the scanner missed a CON_COMMAND macro";
	EXPECT_EQ( 1u, found.count( "cs_cl_te_backend" ) )   // CON_COMMAND_F
		<< "the scanner missed a CON_COMMAND_F macro";
}

TEST( ConVarLedger, ScannerDoesNotCountLookupsAsDeclarations )
{
	// ConVarRef and CManagedConVarRef are how native code READS a convar
	// somebody else declared. Counting one as a declaration would put the whole
	// ledger out by the number of readers, and would make a correct move look
	// like a rule violation.
	std::vector< Decl > out;
	ScanText(
		"ConVarRef r( \"sv_cheats\" );\n"
		"static CManagedConVarRef m( \"sv_gravity\" );\n"
		"ConCommandBase *p = NULL;\n"
		"ConVar *pVar = NULL;\n",
		"synthetic", out );
	EXPECT_TRUE( out.empty() )
		<< "the scanner counted a lookup or a pointer as a declaration";

	// ...and it still finds the real thing in the same shape of text.
	out.clear();
	ScanText( "static ConVar sv_thing( \"sv_thing\", \"1\" );\n", "synthetic", out );
	ASSERT_EQ( 1u, out.size() );
	EXPECT_EQ( "sv_thing", out[ 0 ].name );
}

TEST( ConVarLedger, ScannerDoesNotCountAQuotedDeclaration )
{
	// The failure this anchor was added for: a gtest needle, or any string
	// literal, carrying the text of a declaration. Ten of these were reported
	// as real declarations before the scan was anchored to the start of a line.
	std::vector< Decl > out;
	ScanText( "\tEXPECT_EQ( 1u, CountOf( src, \"CON_COMMAND( report_entities\" ) );\n",
	          "synthetic", out );
	EXPECT_TRUE( out.empty() )
		<< "the scanner counted a quoted declaration as a declaration";

	out.clear();
	ScanText( "\tCON_COMMAND( report_entities, \"help\" )\n", "synthetic", out );
	ASSERT_EQ( 1u, out.size() )
		<< "...and the anchor must not also reject a real, indented declaration";
	EXPECT_EQ( "report_entities", out[ 0 ].name );
}

//=============================================================================//
// 1. NOTHING NATIVE OUTSIDE THE LEDGER
//=============================================================================//

TEST( ConVarLedger, EveryNativeDeclarationHasALedgerRow )
{
	std::set< std::string > known;
	const std::vector< Row > &rows = Ledger();
	for ( size_t i = 0; i < rows.size(); i++ )
		known.insert( rows[ i ].name );

	const std::vector< Decl > &decls = Declarations();
	std::vector< std::string > missing;
	for ( size_t i = 0; i < decls.size(); i++ )
	{
		if ( !known.count( decls[ i ].name ) )
			missing.push_back( decls[ i ].name + "  (" + decls[ i ].file + ")" );
	}

	std::sort( missing.begin(), missing.end() );
	missing.erase( std::unique( missing.begin(), missing.end() ), missing.end() );

	std::string report;
	for ( size_t i = 0; i < missing.size(); i++ )
		report += "\n    " + missing[ i ];

	EXPECT_TRUE( missing.empty() )
		<< missing.size() << " native ConVar/ConCommand declaration(s) under "
		<< kShimDir << " have no row in " << kLedger << ':' << report
		<< "\n  plan_entity.md §6: no ConVar's default or semantics is declared "
		   "natively unless the engine itself transmits or resolves the name. "
		   "Give it a row with its verdict and the evidence, or move it.";
}

TEST( ConVarLedger, NoDeclarationExistsForARowThatSaysItMoved )
{
	std::set< std::string > moved;
	const std::vector< Row > &rows = Ledger();
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( !rows[ i ].nativeNow )
			moved.insert( rows[ i ].name );
	}

	const std::set< std::string > found = DeclaredNames();

	std::vector< std::string > back;
	for ( std::set< std::string >::const_iterator i = moved.begin();
	      i != moved.end(); ++i )
	{
		if ( found.count( *i ) )
			back.push_back( *i );
	}

	std::string report;
	for ( size_t i = 0; i < back.size(); i++ )
		report += "\n    " + back[ i ];

	EXPECT_TRUE( back.empty() )
		<< back.size() << " name(s) the ledger records as moved to managed code "
		   "are declared natively again:" << report
		<< "\n  Two declarations under one name is neither a compile error nor a "
		   "link error, and neither the console nor the game says which one it read.";
}

//=============================================================================//
// 2. AND NOTHING IN THE LEDGER WITHOUT ITS CONVAR
//
// The direction that rots quietly. A keep row outliving its declaration is a
// live exemption for a ConVar that is not there, and it licenses the next one
// that looks like it.
//=============================================================================//

TEST( ConVarLedger, EveryRowThatClaimsANativeDeclarationStillHasOne )
{
	const std::vector< Row > &rows = Ledger();
	const std::set< std::string > found = DeclaredNames();

	std::vector< std::string > stale;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		const Row &r = rows[ i ];
		if ( !r.nativeNow )
			continue;
		if ( found.count( r.name ) )
			continue;

		// The three the constructor scan cannot see -- two ConVar members and
		// one macro-named convar -- are checked at the file and line the row
		// names instead.
		const std::string window = LineWindow( InSrc( r.file ), r.line );
		if ( Mentions( window, "ConVar" ) || Mentions( window, "ConCommand" ) ||
		     Mentions( window, "CON_COMMAND" ) )
		{
			continue;
		}

		std::ostringstream where;
		where << r.name << "  (" << r.file << ':' << r.line
		      << ", verdict " << r.verdict << ')';
		stale.push_back( where.str() );
	}

	std::string report;
	for ( size_t i = 0; i < stale.size(); i++ )
		report += "\n    " + stale[ i ];

	EXPECT_TRUE( stale.empty() )
		<< stale.size() << " ledger row(s) say a native declaration is there and "
		   "it is not:" << report
		<< "\n  Either the ConVar moved and the row's native_now was not turned to "
		   "`no`, or it was deleted and the row should go with it. A keep list "
		   "nobody checks against the tree is how these rot.";
}

TEST( ConVarLedger, EveryRowPointsAtAFileThatExists )
{
	const std::vector< Row > &rows = Ledger();

	std::vector< std::string > gone;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( !rows[ i ].nativeNow )
			continue;
		std::ifstream f( InSrc( rows[ i ].file ).c_str() );
		if ( !f.good() )
			gone.push_back( rows[ i ].name + "  ->  " + rows[ i ].file );
	}

	std::string report;
	for ( size_t i = 0; i < gone.size(); i++ )
		report += "\n    " + gone[ i ];

	EXPECT_TRUE( gone.empty() )
		<< gone.size() << " ledger row(s) name a file that is not in the tree:"
		<< report;
}

//=============================================================================//
// 3. THE VERDICTS THEMSELVES
//=============================================================================//

TEST( ConVarLedger, EveryRowCarriesAKnownVerdict )
{
	static const char *const kVerdicts[] = {
		"protocol-keep",   // the engine declares, resolves or queries the name
		"managed",         // moved; the declaration is a [ConVar]/[ConCommand]
		"managed-queued",  // ruled managed, move not landed yet
		"native-required", // the body is native-only mechanism
		"reader-owed",     // ruled managed, blocked on converting named readers
		"deferred-abi",    // ruled managed, blocked on an ABI addition
		"deferred-lane",   // in a file another lane holds open
		"delete",
		"dead-already",
	};

	const std::vector< Row > &rows = Ledger();
	std::vector< std::string > bad;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		bool ok = false;
		for ( size_t v = 0; v < sizeof( kVerdicts ) / sizeof( kVerdicts[ 0 ] ); v++ )
		{
			if ( rows[ i ].verdict == kVerdicts[ v ] ) { ok = true; break; }
		}
		if ( !ok )
			bad.push_back( rows[ i ].name + " -> '" + rows[ i ].verdict + "'" );
	}

	std::string report;
	for ( size_t i = 0; i < bad.size(); i++ )
		report += "\n    " + bad[ i ];

	EXPECT_TRUE( bad.empty() )
		<< bad.size() << " row(s) carry a verdict this test does not know:"
		<< report << "\n  A free-text verdict column stops being a ruling.";
}

TEST( ConVarLedger, EveryRowCarriesItsEvidence )
{
	const std::vector< Row > &rows = Ledger();

	std::vector< std::string > bare;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[ i ].evidence.size() < 20 )
			bare.push_back( rows[ i ].name );
	}

	std::string report;
	for ( size_t i = 0; i < bare.size(); i++ )
		report += "\n    " + bare[ i ];

	EXPECT_TRUE( bare.empty() )
		<< bare.size() << " row(s) have no evidence behind the verdict:" << report
		<< "\n  The ledger is the lasting artefact; a verdict with no reason is a "
		   "preference.";
}

TEST( ConVarLedger, ProtocolKeepsNameWhatResolvesThem )
{
	// §6 allows exactly one reason to keep a ConVar's declaration in C++: the
	// engine itself transmits or resolves the name. A protocol-keep row that
	// does not say WHICH engine-side thing does that is the reason this rule
	// erodes, so the evidence has to name it.
	const std::vector< Row > &rows = Ledger();

	size_t keeps = 0;
	std::vector< std::string > unsupported;
	for ( size_t i = 0; i < rows.size(); i++ )
	{
		if ( rows[ i ].verdict != "protocol-keep" )
			continue;
		keeps++;

		const std::string &e = rows[ i ].evidence;
		if ( !Mentions( e, "engine.so" ) && !Mentions( e, "by name" ) &&
		     !Mentions( e, "BEFORE the managed side" ) )
		{
			unsupported.push_back( rows[ i ].name );
		}
	}

	ASSERT_GT( keeps, 0u )
		<< "no protocol-keep rows at all -- this fact would pass without "
		   "looking at anything";

	std::string report;
	for ( size_t i = 0; i < unsupported.size(); i++ )
		report += "\n    " + unsupported[ i ];

	EXPECT_TRUE( unsupported.empty() )
		<< unsupported.size() << " protocol-keep row(s) do not name what resolves "
		   "the name engine-side:" << report;
}
