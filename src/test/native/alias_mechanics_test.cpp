//========= SourceSharp — the mechanics the CBaseEntity alias needs ==========//
//
// Phase 13 step 4 makes `CBaseEntity` an ALIAS for `IGameEntity`. An alias is a
// declaration OF THE NAME, so C++ refuses to let the same name be declared as a
// class anywhere else in the include graph -- and the tree declares it 74 times
// across 69 files, plus three `friend class` declarations, which are a fourth
// spelling of the same refusal. (73 across 68 until lane P guarded
// game/server/baseentity.h:93, the one declaration this file used to waive.)
//
// All of that is landed and INERT: every one of those declarations sits inside
// `#ifndef SS_CBASEENTITY_ALIAS`, and nothing in the tree defines that macro.
// This file is what keeps it true between now and step 4, because the failure
// mode is invisible until the day somebody arms the switch and gets 5,778
// diagnostics from files nobody had edited.
//
// It reads source rather than running any, for the same reason
// sdk_player_cast_test.cpp does: there is no runtime behaviour here at all --
// the property is entirely about what the preprocessor sees, and it has to hold
// for files that are not even compiled into this mod yet.
//
//-----------------------------------------------------------------------------
// WHY THE `#include` ORDER ASSERTIONS ARE HERE AND NOT A COMMENT
//-----------------------------------------------------------------------------
//
// plan_baseentity.md had the alias introduced by baseentity.h. It cannot be.
// Measured with `g++ -H` on the two precompiled preludes: the server's cbase.h
// reaches edict.h at include-event 324 and through it iserverunknown.h (340),
// iservernetworkable.h (345) and datamap.h (353), and does not reach
// game/server/baseentity.h until 478. The client reaches datamap.h at 316 from
// predictioncopy.h, which USES the name on its very next line, and does not
// reach c_baseentity.h until 442.
//
// That ordering is the entire reason public/ss_cbaseentity_alias.h existed. If
// somebody reorders cbase.h so the entity header comes first, the four
// boundary includes below become dead weight and step 4 breaks in a way whose
// cause is three files away from its symptom. Two cheap assertions pin it.
//
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAMES. STEP 4 LANDED. THE ALIAS IS RETIRED.
//-----------------------------------------------------------------------------
//
// Everything above is the tense this file was written in and it is history now.
// the-close 3/N (0bc727ed9) DELETED public/ss_cbaseentity_alias.h. `CBaseEntity`
// is not a name in the compiled tree; 1,543 code tokens in 220 files read
// `IGameEntity`; the 453 dead guarded arms went with the-close 1/N and 2/N.
// SS_CBASEENTITY_ALIAS is still DEFINED by both targets (server_sourcesharp and
// client_sourcesharp, src/{server,client}/native/CMakeLists.txt) and now
// selects nothing, which TheSwitchIsArmedInBothTargetsAndNowhereElse below
// still measures.
//
// MEASURED AT THIS TIP, by this file's own scanner over game/, public/ and
// sourcesharp/:
//
//     74 forward declarations  ->  22, in 21 files
//     every one of them GUARDED ->  NOT ONE of them guarded
//     ...and NOT ONE of the 21 files is in the build. Checked against the
//        compiled closure -- the 2,035 headers the 542 compiled objects were
//        compiled from, read out of src/.ninja_deps -- and all 21 read zero.
//        They are TF, SDK, NextBot, episodic and vgui headers plus
//        public/eifacev21.h: stock files this mod has never compiled.
//
// SO THE CLAIM INVERTS AND GETS STRONGER. It was "every declaration of the name
// is behind the switch, so arming the switch does not collide with one". It is
// now "THE NAME IS NOT DECLARED AT ALL IN ANYTHING THE BUILD COMPILES" -- which
// is what the guards were a way of arranging, achieved outright.
//
// AND THE GUARD FACTS CANNOT SIMPLY STAY. Every one of the 22 survivors is in a
// file nothing compiles, so a fact that requires them to be guarded is green
// about text no compiler sees -- the check that cannot fail, which this project
// has now found 45 times. Each is re-pointed rather than deleted: the set of
// files holding a declaration is asserted to be EXACTLY those 21, so a
// declaration appearing anywhere else -- compiled or not -- is red and names
// the file, which is a strictly tighter net than "is it guarded?" ever was.
//
// WHAT THE SCANNER IS FOR NOW. It has to still be able to FIND a declaration,
// or "none outside the 21" is a scanner that stopped matching. It finds 22, in
// three spellings including the tab-separated one public/eifacev21.h uses, and
// IsForwardDeclaration is additionally driven over literals for the spellings
// that no longer occur in the tree.
//
//=============================================================================//

#include <dirent.h>
#include <sys/stat.h>

#include <algorithm>
#include <fstream>
#include <iterator>
#include <set>
#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree. buildcpptests runs each binary with src/ as the working
// directory; the other candidates are so that running the binary by hand from
// the repo root, or from inside src/, both work.
//-----------------------------------------------------------------------------
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

std::vector< std::string > ReadLines( const std::string &strPath )
{
	std::vector< std::string > lines;
	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return lines;

	std::string line;
	while ( std::getline( file, line ) )
	{
		// Sources in this tree are CRLF in places.
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		lines.push_back( line );
	}
	return lines;
}

std::vector< std::string > ReadLinesFromRoot( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::vector< std::string >();
	return ReadLines( std::string( pszRoot ) + "/" + pszRelative );
}

std::string Trimmed( const std::string &s )
{
	size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos )
		return std::string();
	size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
}

bool EndsWith( const std::string &s, const char *pszSuffix )
{
	const size_t n = strlen( pszSuffix );
	return s.size() >= n && s.compare( s.size() - n, n, pszSuffix ) == 0;
}

bool IsSourceFile( const std::string &strName )
{
	return EndsWith( strName, ".h" ) || EndsWith( strName, ".cpp" )
		|| EndsWith( strName, ".hpp" ) || EndsWith( strName, ".inl" );
}

void WalkDirectory( const std::string &strDir, std::vector< std::string > &out )
{
	DIR *pDir = opendir( strDir.c_str() );
	if ( !pDir )
		return;

	struct dirent *pEntry;
	while ( ( pEntry = readdir( pDir ) ) != NULL )
	{
		const std::string name = pEntry->d_name;
		if ( name == "." || name == ".." )
			continue;

		const std::string full = strDir + "/" + name;
		struct stat st;
		if ( stat( full.c_str(), &st ) != 0 )
			continue;

		if ( S_ISDIR( st.st_mode ) )
			WalkDirectory( full, out );
		else if ( IsSourceFile( name ) )
			out.push_back( full );
	}
	closedir( pDir );
}

//-----------------------------------------------------------------------------
// One forward declaration of the name, and whether it is guarded.
//-----------------------------------------------------------------------------
struct Decl_t
{
	std::string strFile;    // relative to src/
	int nLine;              // 1-based
	std::string strText;
	bool bGuarded;
};

// `class CBaseEntity;`, `class C_BaseEntity;`, and the SINGLE_INHERITANCE form
// that public/datamap.h and public/isaverestore.h use -- which is the spelling
// the dry run's regex could not see, and which is why the census in
// docs/typedef-dryrun/ says 71 where this scanner says 73.
// Tokenised rather than prefix-matched, and that is not fussiness: public/
// eiface.h and public/eifacev21.h separate the class-key from the name with a
// TAB, and public/datamap.h and public/isaverestore.h put SINGLE_INHERITANCE
// between them. A prefix scanner written the obvious way misses six of the
// seventy-four -- which is exactly the shape of the miss that left
// isaverestore.h out of the dry run's census in the first place.
bool IsForwardDeclaration( const std::string &strLine, std::string *pName )
{
	std::vector< std::string > tok;
	{
		std::string cur;
		const std::string t = Trimmed( strLine );
		for ( size_t i = 0; i < t.size(); i++ )
		{
			const char c = t[ i ];
			if ( c == ' ' || c == '\t' )
			{
				if ( !cur.empty() ) { tok.push_back( cur ); cur.clear(); }
			}
			else if ( c == ';' )
			{
				// STOP HERE. Anything after the semicolon is a trailing comment,
				// and requiring ';' to be the LAST token made
				// `class CBaseEntity; // why` invisible to this scanner -- the
				// same shape of miss as the tab-separated and SINGLE_INHERITANCE
				// spellings above, which is how isaverestore.h stayed out of the
				// dry run's census. Nothing in the tree spells it that way today;
				// this is so that the day something does, the scanner says so.
				if ( !cur.empty() ) { tok.push_back( cur ); cur.clear(); }
				tok.push_back( ";" );
				break;
			}
			else
			{
				cur += c;
			}
		}
		if ( !cur.empty() )
			tok.push_back( cur );
	}

	if ( tok.size() < 3 )
		return false;
	if ( tok[ 0 ] != "class" && tok[ 0 ] != "struct" )
		return false;
	if ( tok[ tok.size() - 1 ] != ";" )
		return false;

	// Exactly one optional attribute-ish token between key and name.
	size_t iName = 1;
	if ( tok.size() == 4 && tok[ 1 ] == "SINGLE_INHERITANCE" )
		iName = 2;
	else if ( tok.size() != 3 )
		return false;

	if ( tok[ iName ] == "CBaseEntity" || tok[ iName ] == "C_BaseEntity" )
	{
		*pName = tok[ iName ];
		return true;
	}
	return false;
}

bool IsAliasGuardOpen( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t == "#ifndef SS_CBASEENTITY_ALIAS"
		|| t == "#if !defined( SS_CBASEENTITY_ALIAS )"
		|| t == "#if !defined(SS_CBASEENTITY_ALIAS)";
}

// THE TWO ENTITY HEADERS USED TO BE EXEMPT WHOLE, AND ARE NOT ANY MORE.
//
// The argument was that they "own the name at step 4", so guarding
// game/server/baseentity.h's `class CBaseEntity;` here would leave the typedef
// half-landed. What that whole-file waiver actually did was hide ONE line that,
// armed, is a hard error in 180 of the server's 196 translation units -- with
// this suite green throughout, because no test could see the line
// (docs/typedef-dryrun/summary_server_armed.txt). Every other forward
// declaration in the tree carries the guard while its class still exists; that
// one now does too, so there is nothing left to waive and the waiver is gone.
//
// What remains exempt is the alias header, which decides what the two names
// mean rather than being a bystander that happens to say one -- and
// TheEntityHeadersWaiveNothing below pins the waived set to that and only that,
// so this cannot quietly grow back.
bool IsExempt( const std::string &strFile, int nLine, const std::string &strText )
{
	// THE LAST WAIVER IS GONE, and it went with its subject rather than being
	// argued away. It exempted public/ss_cbaseentity_alias.h, which was where
	// both names were DECIDED -- the alias on the client, and an ordinary
	// forward declaration of C_BaseEntity on the server, both inside
	// `#ifdef SS_CBASEENTITY_ALIAS` so that they existed only when armed. That
	// header is DELETED (0bc727ed9), so there is nothing left to waive.
	//
	// The function stays, returning false, for the reason the whole-file waiver
	// taught this tree: a waiver is invisible in a green suite, and the only way
	// to keep one from growing back quietly is to have a place it must be
	// written down. TheEntityHeadersWaiveNothing asserts that this place is
	// empty, and it can now assert it over the WHOLE scan rather than over the
	// two entity headers.
	(void)strFile; (void)nLine; (void)strText;
	return false;
}

struct Scan_t
{
	std::vector< Decl_t > decls;
	std::vector< std::string > files;
	int nFilesRead;
	int nLines;
	Scan_t() : nFilesRead( 0 ), nLines( 0 ) {}
};

const Scan_t &Scan()
{
	static Scan_t s;
	static bool bDone = false;
	if ( bDone )
		return s;
	bDone = true;

	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return s;

	const char *kRoots[] = { "game", "public", "sourcesharp" };
	std::vector< std::string > files;
	for ( size_t i = 0; i < sizeof( kRoots ) / sizeof( kRoots[ 0 ] ); i++ )
		WalkDirectory( std::string( pszRoot ) + "/" + kRoots[ i ], files );

	std::sort( files.begin(), files.end() );
	s.files = files;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		const std::vector< std::string > lines = ReadLines( files[ i ] );
		if ( lines.empty() )
			continue;
		s.nFilesRead++;
		s.nLines += (int)lines.size();

		for ( size_t j = 0; j < lines.size(); j++ )
		{
			std::string name;
			if ( !IsForwardDeclaration( lines[ j ], &name ) )
				continue;

			Decl_t d;
			d.strFile = files[ i ];
			d.nLine = (int)j + 1;
			d.strText = Trimmed( lines[ j ] );

			// Guarded means: the nearest preceding non-blank, non-comment line
			// opens the guard, and the nearest following one closes it. Nothing
			// cleverer, because nothing cleverer is what the preprocessor does.
			bool bOpen = false;
			for ( size_t k = j; k-- > 0; )
			{
				const std::string t = Trimmed( lines[ k ] );
				if ( t.empty() || t.compare( 0, 2, "//" ) == 0 )
					continue;
				bOpen = IsAliasGuardOpen( lines[ k ] );
				break;
			}
			bool bClose = false;
			for ( size_t k = j + 1; k < lines.size(); k++ )
			{
				const std::string t = Trimmed( lines[ k ] );
				if ( t.empty() || t.compare( 0, 2, "//" ) == 0 )
					continue;
				bClose = ( t.compare( 0, 6, "#endif" ) == 0 );
				break;
			}
			d.bGuarded = bOpen && bClose;
			s.decls.push_back( d );
		}
	}
	return s;
}

//-----------------------------------------------------------------------------
// Line-index helpers for the ordering assertions.
//-----------------------------------------------------------------------------
int IndexOfLineContaining( const std::vector< std::string > &lines, const char *pszNeedle )
{
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].find( pszNeedle ) != std::string::npos )
			return (int)i;
	}
	return -1;
}

// The first line that names the type outside a whole-line comment. Used to
// prove the alias include comes FIRST in each boundary header.
int IndexOfFirstMention( const std::vector< std::string > &lines, const char *pszName )
{
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string t = Trimmed( lines[ i ] );
		if ( t.compare( 0, 2, "//" ) == 0 || t.compare( 0, 1, "*" ) == 0 )
			continue;
		if ( lines[ i ].find( "ss_cbaseentity_alias.h" ) != std::string::npos )
			continue;
		if ( lines[ i ].find( pszName ) != std::string::npos )
			return (int)i;
	}
	return -1;
}

// A DECLARATION IS A SHAPE, NOT A STRING, and this is the second fact to learn
// it. public/datamap.h's is `class SINGLE_INHERITANCE IGameEntity;` -- the macro
// keeps a pointer-to-member of this class to 4 bytes, which `inputfunc_t` and
// DEFINE_FUNCTION_RAW sit on -- so a literal `"class IGameEntity;"` needle walks
// straight past it and finds a REDECLARATION 449 lines below.
//
// TheFourBoundaryHeadersDeclareTheInterfaceBeforeTheyNameIt was repaired for
// that in the-close 19/N, when the close collapsed datamap.h's last guard and
// the fact went red. NoForwardDeclarationSurvivesOutsideTheUncompiledStock kept
// the literal, and a mutation proved it mattered: turning datamap.h's real
// declaration into `struct SINGLE_INHERITANCE IGameEntity;` -- so the header
// supplies the name NOWHERE before it uses it -- left that fact GREEN, because
// the redeclaration below satisfied it. One helper now, used by both.
int IndexOfInterfaceDeclaration( const std::vector< std::string > &lines )
{
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		const std::string &szLine = lines[ i ];
		const size_t iFirst = szLine.find_first_not_of( " \t" );
		if ( iFirst == std::string::npos )
			continue;
		if ( szLine.compare( iFirst, 6, "class " ) != 0 )
			continue;
		if ( szLine.find( "IGameEntity;" ) == std::string::npos )
			continue;
		return (int)i;
	}
	return -1;
}

// Where the file supplies the name at all: the first of the declaration and the
// include, or -1 if it does neither.
int IndexOfInterfaceSupply( const std::vector< std::string > &lines )
{
	const int nDecl = IndexOfInterfaceDeclaration( lines );
	const int nInc = IndexOfLineContaining( lines, "#include \"igameentity.h\"" );
	if ( nDecl >= 0 && nInc >= 0 )
		return ( nDecl < nInc ) ? nDecl : nInc;
	return ( nDecl >= 0 ) ? nDecl : nInc;
}

} // namespace

//-----------------------------------------------------------------------------
// THE OTHER HALF, WHICH WAS MISSING FOR THE WHOLE PORT AND IS NOW SPENT.
// READ THE ASSERTIONS, NOT THE NAME.
//
// This was EveryGuardedDeclarationHasItsReplacement. The rule: a guard deletes
// the declaration when armed, so something has to put the name back, and that
// something was one `#include "ss_cbaseentity_alias.h"` ABOVE the guard. It was
// missing in 71 of the 75 files that carried a guard, an arm no build had ever
// compiled, and lane armed-rehearsal measured it at 35 of the client's failing
// TUs -- client failures fell 50 -> 15 the moment the include was supplied.
//
// THE RULE IS SPENT BECAUSE BOTH OF ITS TERMS ARE GONE. There is no guard to
// delete anything, and the header the replacement named is deleted, so an
// `#include "ss_cbaseentity_alias.h"` anywhere in the tree is now a hard error
// -- a missing file, not a missing declaration. So the fact inverts on both
// sides at once, and the second half is what makes it worth keeping: it is the
// only thing standing between a merge that restores one of those includes and a
// tree that will not preprocess.
//-----------------------------------------------------------------------------
TEST( AliasMechanics, NoDeclarationIsGuardedAndNothingIncludesTheDeletedHeader )
{
	const Scan_t &s = Scan();
	ASSERT_GT( s.nFilesRead, 1000 ) << "the scan found almost no files";

	// No guard, anywhere. The 22 survivors are in stock files that never had one.
	std::string strGuarded;
	int nGuarded = 0;
	for ( size_t i = 0; i < s.decls.size(); i++ )
	{
		if ( !s.decls[ i ].bGuarded )
			continue;
		nGuarded++;
		char buf[ 64 ];
		snprintf( buf, sizeof( buf ), ":%d: ", s.decls[ i ].nLine );
		strGuarded += "\n    " + s.decls[ i ].strFile + buf + s.decls[ i ].strText;
	}
	EXPECT_EQ( 0, nGuarded )
		<< nGuarded << " declaration(s) are inside `#ifndef SS_CBASEENTITY_ALIAS`. "
		   "Both targets define that macro and it selects nothing, so the arm is not "
		   "a choice -- it is text the compiler never sees, and the replacement "
		   "rule that used to go with it names a header that no longer exists."
		<< strGuarded;

	// AND NOTHING INCLUDES THE DELETED HEADER. This is the half that bites: the
	// include was added to 75 files and removed from all of them, so a revert or
	// a keep-both merge putting one back is a real possibility, and the failure
	// is "No such file or directory" in whatever TU reaches it.
	std::string strIncluders;
	int nIncluders = 0;
	for ( size_t i = 0; i < s.files.size(); i++ )
	{
		const std::vector< std::string > lines = ReadLines( s.files[ i ] );
		for ( size_t j = 0; j < lines.size(); j++ )
		{
			const std::string t = Trimmed( lines[ j ] );
			if ( t.compare( 0, 2, "//" ) == 0 )
				continue;
			if ( t.find( "#include \"ss_cbaseentity_alias.h\"" ) == std::string::npos )
				continue;
			nIncluders++;
			char buf[ 64 ];
			snprintf( buf, sizeof( buf ), ":%d", (int)j + 1 );
			strIncluders += "\n    " + s.files[ i ] + buf;
		}
	}
	EXPECT_EQ( 0, nIncluders )
		<< nIncluders << " file(s) still include \"ss_cbaseentity_alias.h\", which "
		   "was deleted in 0bc727ed9. That is not a missing declaration any more; "
		   "it is a missing FILE, and every TU that reaches it stops "
		   "preprocessing." << strIncluders;

	// THE LIVE SUCCESSOR, and the reason the 56 includes could be removed at
	// all: the name they were supplying is now supplied by the real interface
	// header, which does exist and is reached the same way.
	const std::vector< std::string > iface = ReadLinesFromRoot( "public/igameentity.h" );
	ASSERT_FALSE( iface.empty() ) << "public/igameentity.h is missing -- it is "
		"what replaced the alias header, and without it the absence above is a "
		"deletion with nothing behind it";
	EXPECT_GE( IndexOfLineContaining( iface, "abstract_class IGameEntity" ), 0 )
		<< "public/igameentity.h no longer defines IGameEntity.";

	int nInterfaceIncluders = 0;
	for ( size_t i = 0; i < s.files.size(); i++ )
	{
		const std::vector< std::string > lines = ReadLines( s.files[ i ] );
		if ( IndexOfLineContaining( lines, "#include \"igameentity.h\"" ) >= 0 )
			nInterfaceIncluders++;
	}
	EXPECT_GT( nInterfaceIncluders, 20 )
		<< "only " << nInterfaceIncluders << " files include igameentity.h. The "
		   "alias header was included by 56; if almost nothing reaches the "
		   "interface, the name the tree compiles against is coming from "
		   "somewhere this file does not know about.";
}

//-----------------------------------------------------------------------------
// The three friend declarations. READ THE ASSERTIONS, NOT THE NAME.
//
// An alias cannot be a friend CLASS: C++ has no `friend class X;` for a
// typedef-name, and C++11's class-key-less `friend X;` does take one. So each of
// the three carried `#ifndef / friend class CBaseEntity; / #else / friend
// CBaseEntity; / #endif`, and this fact pinned that pair.
//
// TWO OF THE THREE ARE COMPILED AND THE FLIP LANDED IN THEM: collisionproperty.h
// and particle_property.h now say `friend IGameEntity;` outright -- the armed
// arm, with no guard, which is the whole point of the alias retiring. The rule
// survives in the form that still matters: the class-key-less spelling is the
// one that is there, because if the name is ever made an alias again a
// `friend class` would not accept it.
//
// THE THIRD IS NOT COMPILED. game/client/entity_predictable_list.h reads zero in
// the compiled closure and still carries the whole guarded pair. It is gated
// rather than dropped -- CPredictableList is the third friend and it is a real
// one; if the file comes back into the build, the pair has to be resolved the
// way the other two were, and this says so.
//-----------------------------------------------------------------------------
TEST( AliasMechanics, TheFriendDeclarationsNameTheInterface )
{
	// The two that are compiled: the armed arm landed and nothing is guarded.
	static const char *kLanded[] = {
		"game/shared/collisionproperty.h",
		"game/shared/particle_property.h",
	};
	for ( size_t i = 0; i < sizeof( kLanded ) / sizeof( kLanded[ 0 ] ); i++ )
	{
		const std::vector< std::string > lines = ReadLinesFromRoot( kLanded[ i ] );
		ASSERT_FALSE( lines.empty() ) << "could not read " << kLanded[ i ];

		int nFriend = -1, nGuard = -1, nOldForm = -1;
		for ( size_t j = 0; j < lines.size(); j++ )
		{
			const std::string t = Trimmed( lines[ j ] );
			if ( t == "friend IGameEntity;" )
				nFriend = (int)j;
			if ( t == "friend class CBaseEntity;" || t == "friend class IGameEntity;" )
				nOldForm = (int)j;
			if ( IsAliasGuardOpen( lines[ j ] ) )
				nGuard = (int)j;
		}

		EXPECT_GE( nFriend, 0 ) << kLanded[ i ] << " no longer declares "
			"`friend IGameEntity;`. That is the class-key-less C++11 spelling, "
			"and it is the one that survives the entity root being a typedef-name "
			"rather than a class -- which is what it was chosen for.";
		EXPECT_LT( nOldForm, 0 ) << kLanded[ i ] << ":" << ( nOldForm + 1 )
			<< " uses `friend class`. The class-key form does not accept a "
			   "typedef-name, which is the whole reason the other spelling is "
			   "here.";
		EXPECT_LT( nGuard, 0 ) << kLanded[ i ] << ":" << ( nGuard + 1 )
			<< " conditions on SS_CBASEENTITY_ALIAS again. It is compiled and the "
			   "macro selects nothing, so one of the two friends is deleted "
			   "outright -- and a missing friend is a private member access "
			   "error, not a missing name.";
	}

	// The third, which is out of the build. Its guarded pair is intact and
	// measures nothing; asserted so that it cannot quietly stop being true.
	const std::vector< std::string > pred =
		ReadLinesFromRoot( "game/client/entity_predictable_list.h" );
	ASSERT_FALSE( pred.empty() )
		<< "could not read game/client/entity_predictable_list.h";
	int nPairClass = -1, nPairAlias = -1;
	for ( size_t j = 0; j < pred.size(); j++ )
	{
		if ( Trimmed( pred[ j ] ) == "friend class C_BaseEntity;" ) nPairClass = (int)j;
		if ( Trimmed( pred[ j ] ) == "friend C_BaseEntity;" )       nPairAlias = (int)j;
	}
	EXPECT_GE( nPairClass, 0 )
		<< "game/client/entity_predictable_list.h no longer declares `friend class "
		   "C_BaseEntity;`. It is not in the compiled closure, so this fact is a "
		   "record rather than a measurement -- but if the file was ported, say "
		   "so here and give it the other two files' shape.";
	EXPECT_GE( nPairAlias, 0 )
		<< "game/client/entity_predictable_list.h's alias arm is gone while its "
		   "class-key arm remains. In a file nothing compiles that is harmless "
		   "today and wrong the moment it is compiled.";
}

//-----------------------------------------------------------------------------
// THE ALIAS HEADER. READ THE ASSERTIONS, NOT THE NAME.
//
// This was TheAliasHeaderAliasesBothSpellings, and it pinned the header's
// content: `using CBaseEntity = IGameEntity;`, a CLIENT_DLL-gated
// `using C_BaseEntity = IGameEntity;`, the `#ifdef SS_CBASEENTITY_ALIAS` that
// made the whole thing inert, and the include of igameentity.h underneath.
//
// THE HEADER IS DELETED (0bc727ed9) and its argument was that deleting it cannot
// change a program: armed, its last two lines made both names IGameEntity, so
// spelling that out across 1,543 code tokens in 220 files is a RENAME. The proof
// offered was not the argument -- 588 objects compared cold, 0 differing,
// 151,092 symbol lines.
//
// So the fact asserts the absence AND the thing the header used to say, now
// said by the tree itself: neither spelling is a name anywhere the build
// compiles, and IGameEntity is. Absence alone would pass over a tree that had
// lost the interface too.
//-----------------------------------------------------------------------------
TEST( AliasMechanics, TheAliasHeaderIsGoneAndNeitherSpellingIsAliasedAnywhere )
{
	const Scan_t &s = Scan();
	ASSERT_GT( s.nFilesRead, 1000 ) << "the scan found almost no files";

	EXPECT_TRUE( ReadLinesFromRoot( "public/ss_cbaseentity_alias.h" ).empty() )
		<< "public/ss_cbaseentity_alias.h is back.";

	// The two `using` lines, and the `#define CBaseEntity C_BaseEntity` in
	// shared_classnames.h that was the third way the name was made. All three
	// went in the same commit; any of them coming back re-creates the name the
	// whole port spent itself removing.
	static const char *kResurrections[] = {
		"using CBaseEntity = IGameEntity;",
		"using C_BaseEntity = IGameEntity;",
		"typedef IGameEntity CBaseEntity;",
		"#define CBaseEntity C_BaseEntity",
	};
	std::string strFound;
	int nFound = 0;
	for ( size_t i = 0; i < s.files.size(); i++ )
	{
		// THE TEST TREE IS EXCLUDED AND THE MATCH IS ANCHORED, both for the same
		// reason and both learned the hard way in this file's own first draft:
		// a substring sweep for `using CBaseEntity = IGameEntity;` matched SIX
		// gtest FAILURE MESSAGES that quote the line in prose -- including four
		// in the array above. A declaration is a line that STARTS with one of
		// these; a sentence about one never does.
		if ( s.files[ i ].find( "/sourcesharp/test/" ) != std::string::npos )
			continue;

		const std::vector< std::string > lines = ReadLines( s.files[ i ] );
		for ( size_t j = 0; j < lines.size(); j++ )
		{
			const std::string t = Trimmed( lines[ j ] );
			if ( t.compare( 0, 2, "//" ) == 0 || t.compare( 0, 1, "*" ) == 0 )
				continue;
			for ( size_t k = 0; k < sizeof( kResurrections ) / sizeof( kResurrections[ 0 ] ); k++ )
			{
				if ( t.compare( 0, strlen( kResurrections[ k ] ), kResurrections[ k ] ) != 0 )
					continue;
				nFound++;
				char buf[ 64 ];
				snprintf( buf, sizeof( buf ), ":%d: ", (int)j + 1 );
				strFound += "\n    " + s.files[ i ] + buf + t;
			}
		}
	}
	EXPECT_EQ( 0, nFound )
		<< nFound << " place(s) make `CBaseEntity` or `C_BaseEntity` a name again. "
		   "The rename is what let the alias header go; re-introducing the name "
		   "puts back every collision the 74 guards existed for." << strFound;

	// The live successor: the one name, defined, in the header the alias header
	// used to include for exactly this purpose.
	const std::vector< std::string > iface = ReadLinesFromRoot( "public/igameentity.h" );
	ASSERT_FALSE( iface.empty() ) << "public/igameentity.h is missing";
	EXPECT_GE( IndexOfLineContaining( iface, "abstract_class IGameEntity" ), 0 );
}

//-----------------------------------------------------------------------------
// THE DEFECT THIS TEST WAS WRITTEN FOR, AND HOW THE RENAME ENDED IT.
// READ THE ASSERTIONS, NOT THE NAME.
//
// `public/iclientunknown.h` and `public/iclientrenderable.h` are CLIENT
// interfaces the SERVER compiles too -- toolframework/itoolentity.h includes the
// renderable one, and game/server/entity_tools_server.cpp includes that. Both
// wrote `virtual C_BaseEntity *GetBaseEntity()` with no CLIENT_DLL gate. Armed,
// iclientunknown.h's own `class C_BaseEntity;` was inside the guard and went;
// the alias itself was gated on CLIENT_DLL, so nothing put the name back on the
// server and entity_tools_server.o failed on exactly those two lines.
//
// Every test here asked "is each declaration guarded?" -- and this one WAS
// guarded, on both sides, which is precisely what broke it. The complementary
// question, the one nobody asked, was: with the guards taken at their armed arm,
// is the name still DECLARED everywhere it is USED?
//
// THE RENAME ANSWERED IT BY DELETING THE QUESTION. Both files now say
// `virtual IGameEntity* GetBaseEntity()`, there is one name, and it is declared
// by public/igameentity.h which iclientunknown.h includes outright. So the fact
// is re-pointed at the two consumers -- the half that was always the real
// premise -- and it asserts what they say now, plus the half that made the
// premise load-bearing: neither has a CLIENT_DLL gate, so the server really does
// compile these lines.
//-----------------------------------------------------------------------------
TEST( AliasMechanics, TheServerCompiledClientBoundariesNameTheInterface )
{
	struct Consumer_t { const char *pszFile; const char *pszLine; };
	static const Consumer_t kConsumers[] = {
		{ "public/iclientunknown.h",    "virtual IGameEntity*\t\tGetBaseEntity() = 0;" },
		{ "public/iclientrenderable.h", "virtual IGameEntity*\t\tGetBaseEntity()\t\t\t{ return 0; }" },
	};
	for ( size_t i = 0; i < sizeof( kConsumers ) / sizeof( kConsumers[ 0 ] ); i++ )
	{
		const std::vector< std::string > c = ReadLinesFromRoot( kConsumers[ i ].pszFile );
		ASSERT_FALSE( c.empty() ) << "could not read " << kConsumers[ i ].pszFile;

		EXPECT_GE( IndexOfLineContaining( c, kConsumers[ i ].pszLine ), 0 )
			<< kConsumers[ i ].pszFile << " no longer declares GetBaseEntity() "
			<< "returning IGameEntity*. The server compiles this file through "
			   "toolframework/itoolentity.h, and `C_BaseEntity` is not a name -- "
			   "if this has gone back to the client spelling, entity_tools_server.o "
			   "does not compile.";

		// IndexOfFirstMention and not IndexOfLineContaining: iclientunknown.h:16
		// still carries a step-4 COMMENT that says `C_BaseEntity *`, and comments
		// were left alone on purpose (0bc727ed9 -- a blind rename turned a true
		// sentence about the port's history into nonsense). Prose naming the old
		// spelling is not the old spelling.
		EXPECT_LT( IndexOfFirstMention( c, "C_BaseEntity" ), 0 )
			<< kConsumers[ i ].pszFile << " names C_BaseEntity in code again.";

		// The half that made the original defect possible, and the reason this
		// row is about these two files rather than any other client header: the
		// SERVER compiles them, with no gate.
		EXPECT_LT( IndexOfFirstMention( c, "CLIENT_DLL" ), 0 )
			<< kConsumers[ i ].pszFile << " has grown a CLIENT_DLL gate. This row "
			   "exists because the server compiles these lines too; with a gate "
			   "it is a fact about a file the server does not read.";
	}
}

//-----------------------------------------------------------------------------
// IMPLEMENT_CLIENTCLASS's factory, and the half of it that is easy to get wrong.
//
// Armed, `clientClassName` derives from `C_BaseEntity`, which is `IGameEntity`:
// no `Init`, and not an `IClientNetworkable`. Both faults are in the factory
// BODY, so gcc keys them to public/client_class.h rather than to any of the six
// .cpp files that wrote IMPLEMENT_CLIENTCLASS_DT -- six files in six different
// manifest shards, which is why the arm is decided here once instead of there
// six times.
//
// The mistake this test exists to prevent is the tempting one: guarding out the
// whole invocation at each call site. That compiles, and it is wrong in a way
// neither build can see. The engine matches a ClientClass to a server class BY
// NETWORK NAME and the entry is what carries the recv table, so a class that
// leaves the list is a class the server can still SEND and this client can no
// longer decode -- the tripwire sourcesharp/test/client_flex_evicted_test.cpp
// states for the flex pair, whose SERVER halves are struck from
// server_csharp.vpc in the same breath for exactly that reason.
//
// So: keep registering, refuse to build. Asserted in both directions, because
// "the armed arm does not say `new`" is also true of an armed arm that is empty.
//-----------------------------------------------------------------------------
TEST( AliasMechanics, TheClientClassFactoryIsTheHuskUnconditionally )
{
	// READ THE ASSERTIONS, NOT THE NAME. This required the husk to be the ARMED
	// arm of a guard around IMPLEMENT_CLIENTCLASS's definition. The guard is
	// gone with the alias and THE HUSK IS THE MACRO -- which is the outcome the
	// arm was written for, so the fact asserts it directly, and asserts the
	// thing the old version asserted in the OTHER arm: that registration itself
	// did not stop.
	const std::vector< std::string > lines = ReadLinesFromRoot( "public/client_class.h" );
	ASSERT_FALSE( lines.empty() ) << "public/client_class.h is missing";

	const char *pszDefine =
		"#define IMPLEMENT_CLIENTCLASS(clientClassName, dataTable, serverClassName) \\";

	int nDefine = -1;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( Trimmed( lines[ i ] ) == pszDefine )
		{
			nDefine = (int)i;
			break;
		}
	}
	ASSERT_GE( nDefine, 0 )
		<< "IMPLEMENT_CLIENTCLASS is no longer defined the way this test reads "
		<< "it -- re-read the macro before trusting anything below";

	// The macro body: every continued line until one that does not end in a
	// backslash.
	std::string strBody;
	for ( size_t i = nDefine; i < lines.size(); i++ )
	{
		strBody += lines[ i ] + "\n";
		const std::string t = Trimmed( lines[ i ] );
		if ( t.empty() || t[ t.size() - 1 ] != '\\' )
			break;
	}

	// THE HUSK. No construction, no Init, and a factory that answers NULL.
	EXPECT_EQ( strBody.find( "clientClassName *pRet = new clientClassName;" ),
		std::string::npos )
		<< "IMPLEMENT_CLIENTCLASS constructs the client class again. The entity "
		   "root is an interface: the class it would construct is not an "
		   "IClientNetworkable and has no Init -- 16 diagnostics keyed to this "
		   "header rather than to any of the six .cpp files that wrote the macro.";
	EXPECT_EQ( strBody.find( "->Init(" ), std::string::npos )
		<< "IMPLEMENT_CLIENTCLASS calls Init on what it built.";
	EXPECT_NE( strBody.find( "return NULL;" ), std::string::npos )
		<< "IMPLEMENT_CLIENTCLASS's factory does not answer NULL. The engine "
		   "CALLS that pointer, so the body has to be an answer its caller "
		   "already handles -- the old body returned 0 when `new` failed.";

	// THE HALF THAT IS NOT ABOUT THE HUSK, and it matters more: the class STAYS
	// REGISTERED. The tempting fix was to guard out the whole invocation at each
	// call site, which compiles and is wrong in a way neither build can see --
	// the engine matches a ClientClass to a server class BY NETWORK NAME, so a
	// class that leaves the list is one the server can still SEND and this
	// client can no longer decode.
	EXPECT_NE( strBody.find( "&dataTable::g_RecvTable" ), std::string::npos )
		<< "IMPLEMENT_CLIENTCLASS no longer registers the recv table. Keep "
		   "registering and refuse to build; do not stop registering.";
	EXPECT_NE( strBody.find( "ClientClass __g_##clientClassName##ClientClass" ),
		std::string::npos )
		<< "IMPLEMENT_CLIENTCLASS no longer declares the ClientClass entry.";
	EXPECT_NE( strBody.find( "INTERNAL_IMPLEMENT_CLIENTCLASS_PROLOGUE" ),
		std::string::npos )
		<< "the macro dropped the prologue, so m_pClassRecvTable and "
		   "GetClientClass() are left undefined for every class that used it.";

	// The signature is intact: the two parameters are cast to void rather than
	// dropped, because the factory has to stay exactly a CreateClientClassFn.
	EXPECT_NE( strBody.find( "(void)entnum;" ), std::string::npos )
		<< "the husk drops `entnum` rather than casting it to void. A name a body "
		   "cannot use is still part of the contract.";
	EXPECT_NE( strBody.find( "(void)serialNum;" ), std::string::npos );

	// And no guard around it, in either polarity.
	for ( int i = nDefine - 1; i >= 0; i-- )
	{
		const std::string t = Trimmed( lines[ i ] );
		if ( t.empty() || t.compare( 0, 2, "//" ) == 0 )
			continue;
		EXPECT_FALSE( IsAliasGuardOpen( lines[ i ] ) || t == "#ifdef SS_CBASEENTITY_ALIAS" )
			<< "public/client_class.h:" << ( i + 1 ) << " puts "
			   "IMPLEMENT_CLIENTCLASS back behind an SS_CBASEENTITY_ALIAS guard. "
			   "That macro is defined by both targets and selects nothing, so one of "
			   "the two arms is deleted outright.";
		break;
	}
}

TEST( AliasMechanics, TheFourBoundaryHeadersDeclareTheInterfaceBeforeTheyNameIt )
{
	// READ THE ASSERTIONS, NOT THE NAME. The requirement was
	// `#include "ss_cbaseentity_alias.h"` above the first mention of the name.
	// That header is deleted, but THE ORDERING PROBLEM IT SOLVED IS UNCHANGED:
	// these four, and only these four, are what cbase.h reaches before it
	// reaches the entity header on either side (measured with g++ -H; see the
	// file comment), and each one NAMES the entity type. Whatever supplies the
	// name has to come first -- it is `class IGameEntity;` or
	// `#include "igameentity.h"` now instead of the alias.
	struct Boundary_t { const char *pszFile; const char *pszName; };
	static const Boundary_t kBoundaries[] = {
		{ "public/datamap.h", "IGameEntity" },
		{ "public/iserverunknown.h", "IGameEntity" },
		{ "public/iservernetworkable.h", "IGameEntity" },
		{ "public/iclientunknown.h", "IGameEntity" },
	};

	for ( size_t i = 0; i < sizeof( kBoundaries ) / sizeof( kBoundaries[ 0 ] ); i++ )
	{
		const std::vector< std::string > lines =
			ReadLinesFromRoot( kBoundaries[ i ].pszFile );
		ASSERT_FALSE( lines.empty() ) << "could not read " << kBoundaries[ i ].pszFile;

		EXPECT_LT( IndexOfLineContaining( lines, "ss_cbaseentity_alias.h" ), 0 )
			<< kBoundaries[ i ].pszFile << " still names the deleted alias header.";

		// A DECLARATION IS A SHAPE, NOT A STRING -- see
		// IndexOfInterfaceDeclaration, which this fact's own red in the-close
		// 19/N is the reason for, and which a mutation later showed
		// NoForwardDeclarationSurvivesOutsideTheUncompiledStock still needed
		// too. ONE helper, so the two facts cannot drift apart again.
		const int nSupply = IndexOfInterfaceSupply( lines );

		ASSERT_GE( nSupply, 0 )
			<< kBoundaries[ i ].pszFile << " neither declares nor includes "
			<< "IGameEntity. It is one of the four the prelude reaches before the "
			<< "entity header, so the name would be introduced too late to be "
			<< "introduced at all";

		const int nFirstMention = IndexOfFirstMention( lines, kBoundaries[ i ].pszName );
		ASSERT_GE( nFirstMention, 0 )
			<< kBoundaries[ i ].pszFile << " no longer names "
			<< kBoundaries[ i ].pszName << " -- if that is real, it has stopped "
			<< "being a boundary header and belongs off this list";

		EXPECT_LE( nSupply, nFirstMention )
			<< kBoundaries[ i ].pszFile << ": the name is supplied at line "
			<< ( nSupply + 1 ) << " but first used at line "
			<< ( nFirstMention + 1 ) << " -- it must be declared before it is "
			<< "used, not after";
	}
}

//-----------------------------------------------------------------------------
// The dependency-graph fact the whole shape rests on.
//-----------------------------------------------------------------------------
TEST( AliasMechanics, BothPreludesReachTheBoundaryBeforeTheEntityHeader )
{
	// The server half is retired: game/server/cbase.h is compiled by nothing
	// since plan_server_novalve.md P5 and left the farm in P6, so there is no
	// server prelude left whose include order could matter.
	{
		const std::vector< std::string > lines = ReadLinesFromRoot( "game/client/cbase.h" );
		ASSERT_FALSE( lines.empty() );
		// predictable_entity.h pulls predictioncopy.h, which pulls datamap.h and
		// then USES the name on its next line.
		const int nPredictable =
			IndexOfLineContaining( lines, "#include <predictable_entity.h>" );
		const int nEntity =
			IndexOfLineContaining( lines, "#include <baseentity_shared.h>" );
		ASSERT_GE( nPredictable, 0 );
		ASSERT_GE( nEntity, 0 );
		EXPECT_LT( nPredictable, nEntity )
			<< "client cbase.h now reaches c_baseentity.h before predictioncopy.h";
	}
}

//-----------------------------------------------------------------------------
// ARMED. THIS IS THE RED TEST STEP 4 WAS SUPPOSED TO WALK THROUGH, AND IT HAS.
//
// It used to be TheSwitchIsNotArmedYet, and its own banner said it was "the
// assertion step 4 DELETES" -- here so that arming the switch would be a
// deliberate act with a red test in front of it rather than a quiet one-line
// build-script edit. It did that job: this lane hit it, and this is the deliberate act.
//
// INVERTED RATHER THAN DELETED, which the old banner did not ask for and which
// is the better trade. Deleted, nothing would watch the switch again -- and the
// switch is the single line that decides whether this entire build compiles the
// stock entity class. Inverted, the same scan now pins the opposite fact and the
// same accident it was built to catch is still caught: a target that stops
// arming it, or a stray `#define` of it in a header, where the definition belongs to
// the build's configuration and to nothing else.
//
// BOTH HALVES ARE LOAD-BEARING, and they fail differently:
//
//   both targets arm it -- one-sided arming is the worst state this port can be
//                         in. It compiles: the server sees the alias and the
//                         client does not, so one side's entity IS the engine
//                         interface and the other side's is the stock class,
//                         and they disagree about a type they network to each
//                         other. Neither a link nor a suite would say so.
//   no header defines it -- the switch is the build's to set. A header that
//                         defines it arms translation units the build never meant
//                         to, and a header that defines it CONDITIONALLY arms
//                         some of them, which is the same disagreement one file
//                         at a time.
//-----------------------------------------------------------------------------
TEST( AliasMechanics, TheSwitchIsArmedInBothTargetsAndNowhereElse )
{
	const Scan_t &s = Scan();
	ASSERT_FALSE( s.files.empty() );

	std::vector< std::string > definitions;
	for ( size_t i = 0; i < s.files.size(); i++ )
	{
		const std::vector< std::string > lines = ReadLines( s.files[ i ] );
		for ( size_t j = 0; j < lines.size(); j++ )
		{
			const std::string t = Trimmed( lines[ j ] );
			if ( t.compare( 0, 8, "#define " ) != 0 )
				continue;
			if ( Trimmed( t.substr( 8 ) ).compare( 0, 21, "SS_CBASEENTITY_ALIAS " ) == 0
				|| Trimmed( t.substr( 8 ) ) == "SS_CBASEENTITY_ALIAS" )
			{
				definitions.push_back( s.files[ i ] );
			}
		}
	}

	// The two DLL targets are where step 4 armed it: server_sourcesharp and
	// client_sourcesharp, each in its own target_compile_definitions block.
	//
	// RE-POINTED IN PHASE 6b. This read the two .vpc files with a substring
	// match over their non-comment lines, because a vpc could arm the switch
	// as /define:, $PreprocessorDefinitions or $Define and a comment could
	// MENTION it without arming anything (the temp entity port's ledger row S4
	// once did, and turned this red). A CMake definitions block is one name per
	// token with comments dropped by the reader, so both hazards are gone: the
	// question is an exact-name lookup, asked twice per target --
	//   * in the list the target's CMakeLists writes (the source of truth), and
	//   * in the compile command the compiler actually ran (the proof the list
	//     reached a TU; a definition on the wrong target reads the same in text).
	// The two must agree. A target armed in one and not the other is reported.
	static const char *const kTargets[] = { cmake_graph::kServer, cmake_graph::kClient };
	std::vector< std::string > targetsArmed;
	for ( size_t i = 0; i < sizeof( kTargets ) / sizeof( kTargets[ 0 ] ); i++ )
	{
		const std::vector< std::string > listed = cmake_graph::ListedDefinitions( kTargets[ i ] );
		const std::set< std::string > compiled = cmake_graph::CompileDefinitions( kTargets[ i ] );

		// Proof-of-read: the port's own define, which no target of this build is
		// without. An absent switch over a list the reader did not see is free.
		ASSERT_TRUE( std::find( listed.begin(), listed.end(), "SOURCESHARP" ) != listed.end() )
			<< cmake_graph::ListFile( kTargets[ i ] ) << ": " << kTargets[ i ]
			<< "'s definitions block does not define SOURCESHARP -- the reader is "
			   "not reading it";
		ASSERT_TRUE( compiled.count( "SOURCESHARP" ) )
			<< kTargets[ i ] << "'s compile command carries no -DSOURCESHARP -- the "
			   "reader is not reading it";

		const bool bListed = std::find( listed.begin(), listed.end(), "SS_CBASEENTITY_ALIAS" ) != listed.end();
		const bool bCompiled = compiled.count( "SS_CBASEENTITY_ALIAS" ) > 0;
		EXPECT_EQ( bListed, bCompiled )
			<< kTargets[ i ] << ": SS_CBASEENTITY_ALIAS is "
			<< ( bListed ? "listed in " : "NOT listed in " ) << cmake_graph::ListFile( kTargets[ i ] )
			<< " but " << ( bCompiled ? "IS" : "is NOT" ) << " on its compile command";

		if ( bListed && bCompiled )
			targetsArmed.push_back( std::string( kTargets[ i ] ) + " (" + cmake_graph::ListFile( kTargets[ i ] ) + ")" );
	}

	std::string strTargets;
	for ( size_t i = 0; i < targetsArmed.size(); i++ )
		strTargets += "\n    " + targetsArmed[ i ];

	std::string strHeaders;
	for ( size_t i = 0; i < definitions.size(); i++ )
		strHeaders += "\n    " + definitions[ i ];

	EXPECT_EQ( 2u, targetsArmed.size() )
		<< "SS_CBASEENTITY_ALIAS must be armed in BOTH targets or neither. One-sided "
		<< "arming COMPILES and LINKS: one side's entity is the engine interface "
		<< "and the other side's is the stock class, and they network to each "
		<< "other. Armed here:" << strTargets;

	// The header scan above is now the whole of the second population: every
	// `#define` it found is in a source file, and a source file defining the
	// switch is the accident this test was originally built to catch.
	EXPECT_TRUE( definitions.empty() )
		<< "SS_CBASEENTITY_ALIAS is #define'd in a header. The switch belongs to "
		<< "the build's configuration and to nothing else; a header that defines "
		<< "it arms translation units the build never meant to:" << strHeaders;
}

// THE EXEMPTION WAIVES NOTHING NOW, and the one line it used to waive is
// guarded like every other.
//
// This test used to be called TheEntityHeaderExemptionHidesExactlyOneKnownDeclaration
// and it pinned the waived set to `game/server/baseentity.h: class CBaseEntity;`
// -- a whole-file waiver over both entity headers, deferring that one line to
// step 4. Armed, that line is a hard error in 180 of the server's 196
// translation units on its own, 86 of which have nothing else wrong, and this
// suite was green the entire time because the waiver is what stopped anything
// from looking. It is the second whole-file exemption in this file's history to
// hide something that mattered.
//
// So baseentity.h:93 now carries the guard, IsExempt() no longer names either
// entity header, and the two halves below are BOTH load-bearing:
//
//   the waived set is EMPTY          -- nothing is being deferred any more, and
//                                       a new whole-file waiver would show up
//                                       here rather than in a green run;
//   the declaration is FOUND and GUARDED -- an empty waived set is also what a
//                                       scanner that had stopped reading
//                                       baseentity.h would report, so the
//                                       declaration is asserted present by
//                                       content, not merely absent from a list.
TEST( AliasMechanics, TheEntityHeadersDeclareNothingAndNothingIsWaived )
{
	// READ THE ASSERTIONS, NOT THE NAME. This was TheEntityHeadersWaiveNothing,
	// and its second half said in as many words what to do when this day came:
	// "or baseentity.h:93 was deleted, which is step 4 landing". It was deleted.
	// The fact is re-pointed rather than removed, because the property it was
	// protecting -- no unguarded declaration of the name in the prelude -- has a
	// successor that is strictly stronger: NO DECLARATION AT ALL.
	//
	// The whole-file waiver this fact existed to abolish is the reason it is
	// still here. One line hidden behind one waiver failed 180 of the server's
	// 196 TUs with this suite green throughout; the lesson was that a waiver has
	// to have a place it is written down and a test that reads that place. That
	// place is IsExempt, it is empty, and this is the test that reads it.
	const Scan_t &s = Scan();
	ASSERT_GT( s.nFilesRead, 1000 ) << "the scan found almost no files";

	std::vector< std::string > waived;
	int nEntityHeaderDecls = 0;
	for ( size_t i = 0; i < s.decls.size(); i++ )
	{
		const Decl_t &d = s.decls[ i ];

		if ( EndsWith( d.strFile, "game/server/baseentity.h" )
			|| EndsWith( d.strFile, "game/client/c_baseentity.h" ) )
		{
			nEntityHeaderDecls++;
		}

		if ( IsExempt( d.strFile, d.nLine, d.strText ) )
			waived.push_back( d.strFile + ": " + d.strText );
	}

	std::string strReport;
	for ( size_t i = 0; i < waived.size(); i++ )
		strReport += "\n    " + waived[ i ];

	EXPECT_TRUE( waived.empty() )
		<< waived.size() << " declaration(s) are being waived by IsExempt(). "
		<< "Nothing is waived any more -- the last waiver went with the alias "
		<< "header it exempted -- and a waiver here is a hole of exactly the kind "
		<< "that let one line fail 180 server TUs with this suite green:"
		<< strReport;

	EXPECT_EQ( 0, nEntityHeaderDecls )
		<< "the two entity headers declare " << nEntityHeaderDecls
		<< " CBaseEntity/C_BaseEntity forward declaration(s). There was one, "
		   "guarded, and it was the prelude's last flip site; the flip landed and "
		   "it went. One coming back declares a class with no definition in a "
		   "header all 197 server TUs read.";

	// THE LIVE SUCCESSOR, so this cannot pass over a deleted header. The server
	// half is retired: game/server/baseentity.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6. The client entity
	// header is still compiled, and what stands where its declaration stood is
	// the include of the real interface.
	const std::vector< std::string > cbe = ReadLinesFromRoot( "game/client/c_baseentity.h" );
	ASSERT_FALSE( cbe.empty() ) << "game/client/c_baseentity.h is missing";
	EXPECT_GE( IndexOfLineContaining( cbe, "igameentity.h" ), 0 )
		<< "game/client/c_baseentity.h no longer reaches igameentity.h.";
}
