//========= SourceSharp — step 8e-classes ====================================//
//
// TWO DEFECTS AND ONE PHASE 5 EXIT CLAUSE.
//
// 1. `C_BaseEntity::InLocalTeam()` returned TRUE FOR EVERY ENTITY IN THE GAME
//    for the life of this build, because it compared two NULLs
//    (`GetTeam() == GetLocalTeam()`, and GetTeam() has been a hard NULL since
//    c_team.cpp left). Lane c-team preserved that `true` deliberately rather
//    than change behaviour inside an eviction, and wrote the trap down. It now
//    answers from the two team NUMBERS. `InSameTeam` had the identical shape
//    one function up and is fixed the same way.
//
// 2. The Phase 5 exit's "the class-list diff is empty" clause. c-team measured
//    it for the first time and it was NOT empty: four client classes had no
//    server counterpart -- CBoneFollower, CGameRulesProxy, CRagdollPropAttached
//    and CRopeKeyframe. They are not one problem with one answer:
//
//      CBoneFollower           server side never in the build; a debug
//                              visualiser; class DELETED
//      CRagdollPropAttached    server side deleted by lane A5 and pinned out
//                              by stock_serverclass_census_test; class DELETED
//      CGameRulesProxy         server side legitimately gone, and the port's
//                              own replacement proxy was deleted with a written
//                              argument; REGISTRATION removed, class kept
//                              because a SHARED header's inlines name it
//      CRopeKeyframe           GENUINELY NEEDED CLIENT-ONLY CLASS. The rope is
//                              ported to C#; the class is built locally 14
//                              times out of 14 over a live gate and drawn every
//                              frame. REGISTRATION removed, class kept.
//
//    So the exit clause did NOT need amending: a client-only class does not
//    belong in g_pClientClassHead in the first place, and taking the two live
//    classes out of the LIST is not the same as deleting them.
//
// THE TRAP THIS FILE EXISTS FOR. A class-list diff going to zero is satisfied
// just as well by deleting a class that was needed. So the facts here are
// two-sided on purpose: every removal is asserted TOGETHER WITH the thing that
// makes it safe -- the absent server class for the two deletions, and the
// surviving class plus its live creation paths for the two de-registrations.
//
// THE DIFF IS RECOMPUTED, NOT LISTED. TheClassListDiffIsEmpty walks every
// source BOTH targets compile (server_sourcesharp and client_sourcesharp, read
// through cmake_graph.h) and reads the class-registration macros out of the
// sources, so a client class added tomorrow with no server counterpart reddens
// it without anybody remembering to add a name here. It respects preprocessor
// guards, which is not fussiness: `c_te_effect_dispatch.cpp` holds an
// IMPLEMENT_CLIENTCLASS_EVENT_DT( ..., CTEEffectDispatch ) inside a
// `#ifndef SOURCESHARP` block, and a guard-blind scan reports a FIFTH orphan
// that is not in the binary at all. That was caught by reconciling the scan
// against `nm __g_*ClientClass client.so`, which reads four.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <map>
#include <fstream>
#include <sstream>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

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

bool Exists( const std::string &rel )
{
	const char *pszRoot = SrcRoot();
	if ( pszRoot == NULL )
		return false;
	struct stat st;
	const std::string path = std::string( pszRoot ) + "/" + rel;
	return stat( path.c_str(), &st ) == 0 && S_ISREG( st.st_mode );
}

std::string ReadRaw( const std::string &rel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the working directory";
	if ( pszRoot == NULL )
		return std::string();

	const std::string path = std::string( pszRoot ) + "/" + rel;
	std::ifstream f( path.c_str() );
	EXPECT_TRUE( f.good() ) << "cannot open " << path;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

//-----------------------------------------------------------------------------
// Comments out. The prose in this tree names the very macros the scans look
// for -- this file does it in its own banner -- so an unstripped scan reads the
// explanation as the thing explained.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );
	bool bBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( bBlock )
		{
			if ( i + 1 < in.size() && in[ i ] == '*' && in[ i + 1 ] == '/' )
			{
				bBlock = false; i++;
			}
			else if ( in[ i ] == '\n' )
			{
				out += '\n';   // keep the line structure the guard pass needs
			}
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' )
		{
			bBlock = true; i++; continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			if ( i < in.size() ) out += '\n';
			continue;
		}
		out += in[ i ];
	}
	return out;
}

bool Has( const std::string &hay, const char *pszNeedle )
{
	return hay.find( pszNeedle ) != std::string::npos;
}

int CountOf( const std::string &hay, const std::string &needle )
{
	int n = 0;
	for ( size_t p = hay.find( needle ); p != std::string::npos;
	      p = hay.find( needle, p + needle.size() ) )
		n++;
	return n;
}

std::string NoWhitespace( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );
	for ( size_t i = 0; i < in.size(); i++ )
		if ( !isspace( (unsigned char)in[ i ] ) )
			out += in[ i ];
	return out;
}

//-----------------------------------------------------------------------------
// The body of one function, braces counted rather than the first `}` taken.
//-----------------------------------------------------------------------------
std::string BodyOf( const std::string &strStripped, const char *pszSignature )
{
	const size_t sig = strStripped.find( pszSignature );
	EXPECT_NE( sig, std::string::npos ) << "no such function: " << pszSignature;
	if ( sig == std::string::npos )
		return std::string();

	const size_t open = strStripped.find( '{', sig );
	EXPECT_NE( open, std::string::npos );
	if ( open == std::string::npos )
		return std::string();

	int nDepth = 0;
	for ( size_t i = open; i < strStripped.size(); i++ )
	{
		if ( strStripped[ i ] == '{' ) nDepth++;
		else if ( strStripped[ i ] == '}' )
		{
			nDepth--;
			if ( nDepth == 0 )
				return strStripped.substr( open, i - open + 1 );
		}
	}
	ADD_FAILURE() << "unbalanced braces after " << pszSignature;
	return std::string();
}

//-----------------------------------------------------------------------------
// EVERY SOURCE A TARGET COMPILES, as farm-relative paths.
//
// Until Phase 6b this was a walker over a vpc's `$File` rows (with a trap for
// backslash-continued rows) plus AssertWalkIsAFileList, a relative guard that
// the walk produced a real file list. Both are RETIRED with the vpcs: the list
// is now the target's CMakeLists source list, and cmake_graph::CompiledSources
// throws when it is missing, empty, or disagrees with compile_commands.json --
// which is the walker's "did it really read the whole list" check, done by
// the helper against a second reading.
//-----------------------------------------------------------------------------
std::vector< std::string > TargetFiles( const char *pszTarget )
{
	const std::set< std::string > srcs = cmake_graph::CompiledSources( pszTarget );
	return std::vector< std::string >( srcs.begin(), srcs.end() );
}

//-----------------------------------------------------------------------------
// A MINIMAL PREPROCESSOR, and it is load-bearing rather than tidy.
//
// It knows exactly the four guard names this build decides:
//   SOURCESHARP          DEFINED on both projects (both targets' compile definitions)
//   CLIENT_DLL           defined on the client project only
//   GAME_DLL             defined on the server project only
//   SS_CBASEENTITY_ALIAS READ OUT OF THE BUILD -- see below
// Any OTHER condition keeps BOTH arms, which can only ever over-report a class
// and so cannot hide one.
//
// Without this pass the scan reads c_te_effect_dispatch.cpp's
// `#ifndef SOURCESHARP` arm and invents a fifth client class, CTEEffectDispatch,
// that is in no binary.
//
//-----------------------------------------------------------------------------
// WHY SS_CBASEENTITY_ALIAS IS READ AND NOT ASSUMED. IT USED TO BE A HARDCODED
// `false`, AND THAT MADE THIS WHOLE FILE BLIND TO THE ONE CONFIGURATION IT IS
// FOR.
//
// This test walks the source lists of the two projects and reads the
// registration macros out of whatever they name -- so when the flip arms those
// projects, every row it walks is the flip's. But the guard world went on saying
// SS_CBASEENTITY_ALIAS was undefined, so every `#ifndef SS_CBASEENTITY_ALIAS`
// arm was kept and every registration inside one was counted AS IF IT WERE
// COMPILED. The test then reported the armed tree's class-list diff as empty
// while the armed binaries disagreed.
//
// What that hid, measured on the armed tree rather than reasoned about -- AND
// THIS PARAGRAPH IS HISTORY, NOT THE STATE OF THE TREE. It is kept because it
// is the argument for reading the macro rather than assuming it, and every
// clause of it was true of the tree it was written against (74b071771):
//
//     game/client/ragdoll.cpp's IMPLEMENT_CLIENTCLASS_DT for C_ServerRagdoll
//     was at :540, inside the `#ifndef SS_CBASEENTITY_ALIAS` region at
//     :460-777. Both projects define SS_CBASEENTITY_ALIAS, so that registration
//     was never compiled while the guard world said it was. A server class on
//     the wire with no client half kills the connection at run time and says
//     nothing at build time on either side, which is the exact failure a
//     previous flip attempt died on.
//
// WHAT IS TRUE TODAY, and it is asserted rather than described -- see
// TheServerRagdollBaseIsDeliberatelyUntouched, which measures both halves:
// the `residue` wave deleted that dead arm, so ragdoll.cpp registers no
// ClientClass for C_ServerRagdoll at all; and
// game/server/physics_prop_ragdoll.cpp is NOT compiled by server_sourcesharp,
// so its IMPLEMENT_SERVERCLASS_ST( CRagdollProp, DT_Ragdoll ) is not compiled
// here either. The pair leaves together and there is no orphan in either
// direction.
//
// The guard world below is still read from the build and not assumed, for the
// reason above: both projects still define SS_CBASEENTITY_ALIAS -- in the
// compile definitions this file reads -- and files outside
// the compiled closure still carry `#ifndef` arms on it, every one of which a
// hardcoded `false` would count as compiled.
//
// The macro is a property of the CONFIGURATION, and the configuration is
// what the compiler is handed. So it is read from there: each target's -D
// definitions (cmake_graph::CompileDefinitions, from compile_commands.json). Unarmed the answer is
// false and every claim below means exactly what it meant; armed the answer is
// true and this file becomes a gate the flip has to pass.
//-----------------------------------------------------------------------------
struct GuardWorld
{
	bool bClient;
	bool bAlias;        // SS_CBASEENTITY_ALIAS, from the build. See AliasArmedIn.
	bool Known( const std::string &name, bool &bValue ) const
	{
		if ( name == "SOURCESHARP" )          { bValue = true;      return true; }
		if ( name == "CLIENT_DLL" )           { bValue = bClient;   return true; }
		if ( name == "GAME_DLL" )             { bValue = !bClient;  return true; }
		if ( name == "SS_CBASEENTITY_ALIAS" ) { bValue = bAlias;    return true; }
		return false;
	}
};

std::string FirstIdentifier( const std::string &in )
{
	size_t i = 0;
	while ( i < in.size() && !( isalpha( (unsigned char)in[ i ] ) || in[ i ] == '_' ) )
		i++;
	size_t start = i;
	while ( i < in.size() && ( isalnum( (unsigned char)in[ i ] ) || in[ i ] == '_' ) )
		i++;
	return in.substr( start, i - start );
}

std::string StripDeadArms( const std::string &in, const GuardWorld &world )
{
	std::vector< std::pair< bool, bool > > stack;   // (known, emitting)
	std::string out;
	size_t pos = 0;
	while ( pos <= in.size() )
	{
		size_t nl = in.find( '\n', pos );
		const std::string line =
			in.substr( pos, ( nl == std::string::npos ? in.size() : nl ) - pos );

		std::string t = line;
		size_t a = t.find_first_not_of( " \t\r" );
		t = ( a == std::string::npos ) ? std::string() : t.substr( a );

		bool bDirective = false;
		if ( !t.empty() && t[ 0 ] == '#' )
		{
			std::string rest = t.substr( 1 );
			size_t b = rest.find_first_not_of( " \t" );
			rest = ( b == std::string::npos ) ? std::string() : rest.substr( b );
			const std::string word = FirstIdentifier( rest );
			const std::string tail = rest.substr( word.size() );

			if ( word == "ifdef" || word == "ifndef" || word == "if" )
			{
				bDirective = true;
				std::string name;
				bool bWant = true;
				bool bParsed = false;
				if ( word == "ifdef" )       { name = FirstIdentifier( tail ); bWant = true;  bParsed = true; }
				else if ( word == "ifndef" ) { name = FirstIdentifier( tail ); bWant = false; bParsed = true; }
				else
				{
					const std::string cond = NoWhitespace( tail );
					if ( cond.compare( 0, 8, "defined(" ) == 0
					    || cond.compare( 0, 7, "defined" ) == 0 )
					{
						name = FirstIdentifier( cond.substr( 7 ) );
						bWant = true; bParsed = true;
					}
					else if ( cond.compare( 0, 8, "!defined" ) == 0 )
					{
						name = FirstIdentifier( cond.substr( 8 ) );
						bWant = false; bParsed = true;
					}
					// A `defined(A) && defined(B)` reads as its FIRST name only,
					// which keeps both arms unless that one name settles it --
					// see the comment above: over-reporting is safe here.
					if ( bParsed && NoWhitespace( tail ).find( "&&" ) != std::string::npos )
						bParsed = false;
					if ( bParsed && NoWhitespace( tail ).find( "||" ) != std::string::npos )
						bParsed = false;
				}
				bool bValue = false;
				if ( bParsed && world.Known( name, bValue ) )
					stack.push_back( std::make_pair( true, bValue == bWant ) );
				else
					stack.push_back( std::make_pair( false, true ) );
			}
			else if ( word == "else" )
			{
				bDirective = true;
				if ( !stack.empty() )
				{
					const bool bKnown = stack.back().first;
					stack.back().second = bKnown ? !stack.back().second : true;
				}
			}
			else if ( word == "elif" )
			{
				bDirective = true;
				if ( !stack.empty() )
					stack.back() = std::make_pair( false, true );
			}
			else if ( word == "endif" )
			{
				bDirective = true;
				if ( !stack.empty() )
					stack.pop_back();
			}
		}

		bool bEmit = true;
		for ( size_t i = 0; i < stack.size(); i++ )
			if ( !stack[ i ].second ) bEmit = false;

		out += ( bDirective || !bEmit ) ? std::string() : line;
		out += '\n';

		if ( nl == std::string::npos )
			break;
		pos = nl + 1;
	}
	return out;
}

//-----------------------------------------------------------------------------
// The SERVER class name published by one registration macro. `IMPLEMENT_
// CLIENTCLASS*(client, table, server)` names it third; `IMPLEMENT_SERVERCLASS_ST*
// (server, table)` names it first; `IMPLEMENT_NETWORKCLASS_ALIASED(X, table)`
// is "C" + X on both sides, which is what makes it the shape that has to be
// read on both.
//-----------------------------------------------------------------------------
void ArgsAfter( const std::string &body, const std::string &macro,
                std::vector< std::vector< std::string > > &out )
{
	for ( size_t p = body.find( macro ); p != std::string::npos;
	      p = body.find( macro, p + macro.size() ) )
	{
		size_t q = p + macro.size();
		while ( q < body.size() && ( body[ q ] == ' ' || body[ q ] == '\t' ) )
			q++;
		// The macro name must be followed by `(` -- so `IMPLEMENT_CLIENTCLASS_DT`
		// does not also match inside `IMPLEMENT_CLIENTCLASS_DT_NOBASE`.
		if ( q >= body.size() || body[ q ] != '(' )
			continue;
		const size_t close = body.find( ')', q );
		if ( close == std::string::npos )
			continue;

		std::vector< std::string > args;
		std::string cur;
		for ( size_t i = q + 1; i < close; i++ )
		{
			if ( body[ i ] == ',' ) { args.push_back( NoWhitespace( cur ) ); cur.clear(); }
			else cur += body[ i ];
		}
		args.push_back( NoWhitespace( cur ) );
		out.push_back( args );
	}
}

const char *kMacrosClient[] = {
	"IMPLEMENT_CLIENTCLASS_DT_NOBASE", "IMPLEMENT_CLIENTCLASS_DT",
	"IMPLEMENT_CLIENTCLASS_EVENT_POINTER", "IMPLEMENT_CLIENTCLASS_EVENT_DT",
	"IMPLEMENT_CLIENTCLASS_EVENT", "IMPLEMENT_CLIENTCLASS",
};
const char *kMacrosServer[] = {
	"IMPLEMENT_SERVERCLASS_ST_NOBASE", "IMPLEMENT_SERVERCLASS_ST",
	"IMPLEMENT_SERVERCLASS",
};

//-----------------------------------------------------------------------------
// IS SS_CBASEENTITY_ALIAS ARMED IN THIS PROJECT? Read out of the -D
// definitions the target's sources are compiled with, which is where the
// answer actually lives.
//
// (Until Phase 6b this was read off the vpc's `$PreprocessorDefinitions`
// line, anchored on that line because `.vpc` prose named the macro beside
// rows. A definitions set carries no prose; what the rule must still get
// right is a NAME match, not a prefix: SS_CBASEENTITY_ALIAS_X is not it.)
//-----------------------------------------------------------------------------
// The rule on its own, so it can be checked on a set rather than only on
// whatever the tree is in today.
bool DefinesAlias( const std::set< std::string > &defs )
{
	for ( std::set< std::string >::const_iterator it = defs.begin(); it != defs.end(); ++it )
	{
		if ( *it == "SS_CBASEENTITY_ALIAS" || it->compare( 0, 21, "SS_CBASEENTITY_ALIAS=" ) == 0 )
			return true;
	}
	return false;
}

bool AliasArmedIn( const char *pszTarget )
{
	return DefinesAlias( cmake_graph::CompileDefinitions( pszTarget ) );
}

void CollectClasses( const char *pszTarget, bool bClient,
                     std::map< std::string, std::string > &out,
                     size_t &nRows )
{
	const std::vector< std::string > files = TargetFiles( pszTarget );
	nRows = files.size();

	GuardWorld world;
	world.bClient = bClient;
	// FROM THE TARGET THIS CALL IS WALKING, not from a constant. Unarmed it is
	// false and every claim below means what it always meant; armed it is
	// true and this file becomes a gate the flip has to pass.
	world.bAlias = AliasArmedIn( pszTarget );

	for ( size_t i = 0; i < files.size(); i++ )
	{
		const std::string &f = files[ i ];
		if ( f.size() < 4 || f.compare( f.size() - 4, 4, ".cpp" ) != 0 )
			continue;
		if ( !Exists( f ) )
			continue;

		const std::string body =
			StripDeadArms( StripComments( ReadRaw( f ) ), world );

		std::vector< std::vector< std::string > > hits;
		if ( bClient )
			for ( size_t m = 0; m < sizeof( kMacrosClient ) / sizeof( kMacrosClient[ 0 ] ); m++ )
			{
				std::vector< std::vector< std::string > > got;
				ArgsAfter( body, kMacrosClient[ m ], got );
				for ( size_t g = 0; g < got.size(); g++ )
					if ( got[ g ].size() >= 3 ) out[ got[ g ][ 2 ] ] = f;
			}
		else
			for ( size_t m = 0; m < sizeof( kMacrosServer ) / sizeof( kMacrosServer[ 0 ] ); m++ )
			{
				std::vector< std::vector< std::string > > got;
				ArgsAfter( body, kMacrosServer[ m ], got );
				for ( size_t g = 0; g < got.size(); g++ )
					if ( got[ g ].size() >= 2 ) out[ got[ g ][ 0 ] ] = f;
			}

		std::vector< std::vector< std::string > > aliased;
		ArgsAfter( body, "IMPLEMENT_NETWORKCLASS_ALIASED", aliased );
		for ( size_t g = 0; g < aliased.size(); g++ )
			if ( aliased[ g ].size() >= 2 ) out[ "C" + aliased[ g ][ 0 ] ] = f;
	}
}

std::string Join( const std::map< std::string, std::string > &m )
{
	std::string s;
	for ( std::map< std::string, std::string >::const_iterator it = m.begin();
	      it != m.end(); ++it )
		s += "\n    " + it->first + "   (" + it->second + ")";
	return s;
}

const char *const kClient = cmake_graph::kClient;
const char *const kServer = cmake_graph::kServer;

// The configuration this tree is in, from the two targets. Both must agree --
// TheAliasIsReadOutOfTheBuildAndNotAssumed is the fact that says so -- so either
// one answers, and the client's is read here for no reason but to pick one.
bool ArmedHere()
{
	return AliasArmedIn( kClient );
}

const char *kRagdoll     = "game/client/ragdoll.cpp";
const char *kRope        = "game/client/c_rope.cpp";
const char *kRopeHeader  = "game/client/c_rope.h";
const char *kGameRulesH  = "game/shared/gamerules.h";
const char *kRopeRender  = "sourcesharp/game/client/shell_rope_render.cpp";

} // namespace

//=============================================================================//
// 1. THE PHASE 5 EXIT CLAUSE ITSELF, recomputed from both targets.
//=============================================================================//
TEST( OrphanClientClass, TheClassListDiffIsEmpty )
{
	std::map< std::string, std::string > client, server;
	size_t nClientRows = 0, nServerRows = 0;
	CollectClasses( kClient, true,  client, nClientRows );
	CollectClasses( kServer, false, server, nServerRows );

	// A guard a broken collector fails instead of passing vacuously: an empty
	// scan of both sides would otherwise satisfy "the diff is empty" perfectly.
	//
	// THIS USED TO BE A PAIR OF HARD FLOORS (230/250 client, 130/150 server)
	// AND THEY WENT STALE, which is the failure mode a floor has in a project
	// that is deliberately shrinking: Phase 1 took the server project to 126
	// rows and the floor reported "not this project's file list" about this
	// project's file list. The list is now cross-checked by cmake_graph against
	// compile_commands.json (it throws on a missing, empty or stale input),
	// which cannot go stale in either direction.
	ASSERT_GT( nClientRows, 0u ) << "the client collector scanned no rows";
	ASSERT_GT( nServerRows, 0u ) << "the server collector scanned no rows";

	// AND THE CLASS FLOOR INVERTS WHEN ARMED, which is the flip's whole point.
	//
	// Unarmed, "at least four" is an anti-vacuity floor: an empty scan of both
	// sides satisfies "the diff is empty" and means nothing. ARMED, ZERO IS THE
	// CORRECT ANSWER ON BOTH SIDES -- the flip removes every stock TU that
	// registers an entity class, and a diff between two empty sets would indeed
	// be vacuous. So armed this stops asking for a diff and asserts the STRONGER
	// thing directly: neither project registers a stock entity class at all.
	// That is plan_entity.md's definition of done, checked rather than implied.
	if ( ArmedHere() )
	{
		EXPECT_EQ( 0u, server.size() )
			<< "the ARMED server project still registers " << server.size()
			<< " stock entity class(es):" << Join( server )
			<< "\n\nArmed there is no CBaseEntity for one to derive from, so a "
			   "surviving IMPLEMENT_SERVERCLASS_* is a row the flip failed to "
			   "remove -- and a server class whose client half went with the "
			   "client removal set kills the connection at run time and says "
			   "nothing at build time on either side.";
		EXPECT_EQ( 0u, client.size() )
			<< "the ARMED client project still registers " << client.size()
			<< " stock entity class(es):" << Join( client );
		return;
	}

	ASSERT_GE( client.size(), 4u ) << "the client scan found " << client.size()
		<< " registered classes; the macro reader is broken, not the tree";
	ASSERT_GE( server.size(), 4u ) << "the server scan found " << server.size()
		<< " registered classes; the macro reader is broken, not the tree";

	std::map< std::string, std::string > onlyClient;
	for ( std::map< std::string, std::string >::const_iterator it = client.begin();
	      it != client.end(); ++it )
		if ( server.find( it->first ) == server.end() )
			onlyClient[ it->first ] = it->second;

	EXPECT_TRUE( onlyClient.empty() )
		<< onlyClient.size() << " client class(es) have NO server counterpart, so "
		   "the client would build an entity for a class no server can ever send:"
		<< Join( onlyClient )
		<< "\n\nThat is the Phase 5 exit's 'the class-list diff is empty' clause. "
		   "Either the server class comes back in the SAME commit, or the client "
		   "half goes -- and if the class is needed client-side, keep the CLASS "
		   "and drop only its ClientClass registration, the way C_RopeKeyframe "
		   "does.";
}

TEST( OrphanClientClass, EveryServerClassHasItsClientHalf )
{
	std::map< std::string, std::string > client, server;
	size_t nClientRows = 0, nServerRows = 0;
	CollectClasses( kClient, true,  client, nClientRows );
	CollectClasses( kServer, false, server, nServerRows );

	// See TheClassListDiffIsEmpty for why this floor inverts when armed: the
	// flip removes every stock TU that registers a server class, so zero is the
	// correct armed answer and there is no orphan direction left to check.
	if ( ArmedHere() )
	{
		EXPECT_EQ( 0u, server.size() )
			<< "the ARMED server project still registers " << server.size()
			<< " stock entity class(es):" << Join( server );
		return;
	}

	ASSERT_GE( server.size(), 4u );

	std::map< std::string, std::string > onlyServer;
	for ( std::map< std::string, std::string >::const_iterator it = server.begin();
	      it != server.end(); ++it )
		if ( client.find( it->first ) == client.end() )
			onlyServer[ it->first ] = it->second;

	EXPECT_TRUE( onlyServer.empty() )
		<< onlyServer.size() << " server class(es) have no client half:"
		<< Join( onlyServer )
		<< "\n\nThis is the FATAL direction. A server that sends a class the "
		   "client cannot decode kills the connection at run time and says "
		   "nothing at build time on either side.";
}

//=============================================================================//
// 2. The guard pass, checked against the one case that proves it is needed.
//=============================================================================//
TEST( OrphanClientClass, TheScanReadsPreprocessorGuardsRatherThanRawText )
{
	// c_te_effect_dispatch.cpp USED TO register CTEEffectDispatch inside a
	// `#ifndef SOURCESHARP` block, and SOURCESHARP is defined by both targets.
	//
	// RE-ANCHORED BY THE `residue` WAVE (74b071771..a46e8d645), WHICH IS
	// EXACTLY WHAT THE OLD ASSERT ASKED FOR IF THE CLASS EVER WENT.
	//
	// The registration was `IMPLEMENT_CLIENTCLASS_EVENT_DT( C_TEEffectDispatch,
	// DT_TEEffectDispatch, CTEEffectDispatch )` at
	// game/client/c_te_effect_dispatch.cpp:104, inside the `#ifndef SOURCESHARP`
	// region at :96-158 as of 74b071771. SOURCESHARP is DEFINED in the one
	// configuration this project builds, so it was never compiled -- which is
	// the very thing this fact existed to prove the scan could see -- and the
	// wave deleted the arm with 5,282 other dead-arm lines across 44 stock TUs.
	// The file no longer names the class at all.
	//
	// THE FACT IS ABOUT THE SCAN, NOT ABOUT THAT FILE, so it is re-pointed at
	// two anchors the deletion cannot reach, and BOTH directions are written:
	//
	//   1. AN INLINE SOURCE, right here. No wave can delete it. It carries one
	//      registration inside `#ifndef SOURCESHARP` and one outside, and the
	//      guard pass must remove the first AND KEEP THE SECOND. Without the
	//      second half a StripDeadArms that returned an empty string would
	//      satisfy this fact -- and would silently empty CollectClasses, which
	//      is what every orphan fact in this file is built on.
	//
	//   2. THE WHOLE COMPILED CLIENT PROJECT. The successor to "the scan can
	//      see the one guarded registration" is the stronger measured statement
	//      that THERE ARE NONE LEFT: over every .cpp the client compiles,
	//      the guard-aware scan and a guard-blind one now find the same
	//      registrations. That was 1 before the wave (this file) and is 0 now.
	//      Pinned, so a new guarded registration is a decision somebody makes --
	//      and if one appears, re-point half 2 at it rather than relaxing it.

	// ---- 1. the scan reads guards, and reads them BOTH ways ------------------
	GuardWorld world;
	world.bClient = true;
	// This fact is about the SOURCESHARP guard and nothing else; the alias
	// axis is read from the client's build so that the world is this tree's
	// world here too, whichever configuration it is in.
	world.bAlias = AliasArmedIn( kClient );

	const std::string kInline =
		"IMPLEMENT_CLIENTCLASS_EVENT_DT( C_TEKept, DT_TEKept, CTEKept )\n"
		"#ifndef SOURCESHARP\n"
		"IMPLEMENT_CLIENTCLASS_EVENT_DT( C_TEDead, DT_TEDead, CTEDead )\n"
		"#endif\n";
	const std::string inlineLive = StripDeadArms( kInline, world );

	EXPECT_FALSE( Has( inlineLive, "CTEDead" ) )
		<< "the guard pass is letting a `#ifndef SOURCESHARP` arm through. A "
		   "guard-blind scan reported CTEEffectDispatch as a fifth orphan client "
		   "class on the strength of exactly this, and it was in no binary: "
		   "`nm -C client.so | grep __g_.*ClientClass` read four.";
	EXPECT_TRUE( Has( inlineLive, "CTEKept" ) )
		<< "the guard pass is dropping an UNGUARDED registration. Every fact in "
		   "this file reads CollectClasses, which reads StripDeadArms; a pass "
		   "that strips live code makes the class maps empty and every orphan "
		   "check vacuous.";

	// ---- 2. no registration hides in a dead arm anywhere any more ------------
	const std::vector< std::string > files = TargetFiles( kClient );

	size_t nScanned = 0, nHidden = 0;
	std::string strHidden;
	for ( size_t i = 0; i < files.size(); i++ )
	{
		const std::string &f = files[ i ];
		if ( f.size() < 4 || f.compare( f.size() - 4, 4, ".cpp" ) != 0 )
			continue;
		if ( !Exists( f ) )
			continue;

		nScanned++;
		const std::string body = StripComments( ReadRaw( f ) );
		const std::string live = StripDeadArms( body, world );

		const char *aszReg[] = { "IMPLEMENT_CLIENTCLASS", "IMPLEMENT_NETWORKCLASS_ALIASED" };
		for ( size_t m = 0; m < sizeof( aszReg ) / sizeof( aszReg[ 0 ] ); m++ )
		{
			const int nText = CountOf( body, aszReg[ m ] );
			const int nLive = CountOf( live, aszReg[ m ] );
			if ( nText != nLive )
			{
				nHidden += (size_t)( nText - nLive );
				strHidden += "\n  " + f + ": " + aszReg[ m ];
			}
		}
	}

	// Relative, not a floor: every .cpp the client compiles that exists on
	// disk must have opened. A hard floor here would
	// go stale the moment Phase 4 starts shrinking the client project.
	size_t nCppRows = 0;
	for ( size_t i = 0; i < files.size(); i++ )
	{
		const std::string &f = files[ i ];
		if ( f.size() >= 4 && f.compare( f.size() - 4, 4, ".cpp" ) == 0 && Exists( f ) )
			nCppRows++;
	}
	ASSERT_GT( nCppRows, 0u ) << "the client walk found no .cpp at all";
	ASSERT_EQ( nScanned, nCppRows )
		<< "only " << nScanned << " of the client's " << nCppRows
		<< " existing .cpp rows opened, so the count below is measured over a "
		   "subset and a registration hiding in an unopened file reads as absent";
	EXPECT_EQ( 0u, nHidden )
		<< nHidden << " client-class registration(s) are inside a DEAD "
		   "preprocessor arm:" << strHidden << "\n\nThat was 1 until the `residue` "
		   "wave -- CTEEffectDispatch, in game/client/c_te_effect_dispatch.cpp's "
		   "`#ifndef SOURCESHARP` block -- and it is the case that makes the guard "
		   "pass load-bearing rather than decorative. A new one is fine, but it is "
		   "a decision: re-point this half at it and say why.";

	// The proof-of-read for the zero above. The file this fact was named for,
	// game/client/c_te_effect_dispatch.cpp, left client_sourcesharp in C2 (lane
	// c2-fx: the dispatch table is C#'s), so the read is proven on a fork file
	// that is still a compiled row and still carries seam calls:
	// clientshadowmgr.cpp (cdll_util.cpp, then client_thinklist.cpp, until C8
	// took them out of the build).
	bool bFoundSubject = false;
	for ( size_t i = 0; i < files.size(); i++ )
		if ( files[ i ] == "game/client/clientshadowmgr.cpp" )
			bFoundSubject = true;
	EXPECT_TRUE( bFoundSubject )
		<< "game/client/clientshadowmgr.cpp has left client_sourcesharp. It is this "
		   "fact's proof-of-read subject; re-point it at another compiled fork file "
		   "with seam calls, or the scan above is not shown to walk the set.";
	EXPECT_TRUE( Has( StripComments( ReadRaw( "game/client/clientshadowmgr.cpp" ) ), "SourceSharp_" ) )
		<< "game/client/clientshadowmgr.cpp read as nothing, or has lost its seam calls; "
		   "the counts above cannot be trusted.";
	for ( size_t i = 0; i < files.size(); i++ )
		EXPECT_NE( files[ i ], "game/client/c_te_effect_dispatch.cpp" )
			<< "c_te_effect_dispatch.cpp is a compiled row again; its RecvTable "
			   "and dead CTEEffectDispatch arm are what this fact once guarded.";
}

//=============================================================================//
// 2b. THE GUARD WORLD'S ALIAS AXIS IS READ, NOT ASSUMED.
//
// It used to be a hardcoded `false`, which made this whole file blind to the
// one configuration it exists to gate. See the GuardWorld banner for what that
// hid. This fact pins BOTH directions, so neither "it went back to a constant"
// nor "it now matches a name that merely starts with it" can happen quietly.
// (Until Phase 6b the reader was anchored on the vpc's $PreprocessorDefinitions
// line, because `.vpc` prose named the macro beside the rows the flip removed;
// a definitions set carries no prose, so that control became a name-vs-prefix
// one.)
//=============================================================================//
TEST( OrphanClientClass, TheAliasIsReadOutOfTheBuildAndNotAssumed )
{
	// Whatever the tree is in right now, both projects must agree: the flip
	// arms both projects in ONE commit, and a tree with one side armed is a
	// tree whose two halves disagree about what a CBaseEntity is.
	EXPECT_EQ( AliasArmedIn( kClient ), AliasArmedIn( kServer ) )
		<< "the two projects disagree about SS_CBASEENTITY_ALIAS. The flip arms "
		   "both in one commit; a half-armed tree is not a configuration "
		   "this file, or the build, can mean anything about.";

	// POSITIVE CONTROL, both ways, on constructed sets rather than on the tree
	// -- so this fact is not satisfied by whatever the tree happens to be today.
	{
		std::set< std::string > armed;
		armed.insert( "SOURCESHARP" ); armed.insert( "SS_CBASEENTITY_ALIAS" );
		std::set< std::string > armedValue;
		armedValue.insert( "SOURCESHARP" ); armedValue.insert( "SS_CBASEENTITY_ALIAS=1" );
		std::set< std::string > bare;
		bare.insert( "SOURCESHARP" );
		// A NAME match, not a prefix one.
		std::set< std::string > lookalike;
		lookalike.insert( "SOURCESHARP" ); lookalike.insert( "SS_CBASEENTITY_ALIAS_OFF" );

		EXPECT_TRUE( DefinesAlias( armed ) )
			<< "the reader cannot see the macro in a definitions set, so it "
			   "would answer 'unarmed' about an armed tree -- which is the "
			   "defect this fact exists for";
		EXPECT_TRUE( DefinesAlias( armedValue ) )
			<< "the reader misses -DSS_CBASEENTITY_ALIAS=1";
		EXPECT_FALSE( DefinesAlias( bare ) )
			<< "the reader says 'armed' about a build that defines nothing";
		EXPECT_FALSE( DefinesAlias( lookalike ) )
			<< "the reader is matching a PREFIX of the macro's name";
	}

	// And the guard world really uses it: an armed world must strip a
	// `#ifndef SS_CBASEENTITY_ALIAS` arm and an unarmed one must keep it.
	{
		const std::string body =
			"#ifndef SS_CBASEENTITY_ALIAS\nIMPLEMENT_CLIENTCLASS_DT(C_Probe,DT_Probe,CProbe)\n#endif\n";

		GuardWorld unarmed; unarmed.bClient = true; unarmed.bAlias = false;
		GuardWorld armed;   armed.bClient   = true; armed.bAlias   = true;

		EXPECT_TRUE( Has( StripDeadArms( body, unarmed ), "IMPLEMENT_CLIENTCLASS_DT" ) )
			<< "an UNARMED world dropped an arm it compiles";
		EXPECT_FALSE( Has( StripDeadArms( body, armed ), "IMPLEMENT_CLIENTCLASS_DT" ) )
			<< "an ARMED world kept a registration it does not compile -- which "
			   "is exactly how a server class with no client half reads as "
			   "'the diff is empty'";
	}
}

//=============================================================================//
// 3. CBoneFollower -- the class is gone, and the server side was never there.
//=============================================================================//
TEST( OrphanClientClass, NeitherProjectCompilesABoneFollowerEntity )
{
	// Proof-of-read: a file the server certainly compiles.
	ASSERT_TRUE( cmake_graph::Compiles( kServer, "sourcesharp/game/shared/shell_shared.cpp" ) )
		<< "server_sourcesharp does not compile shell_shared.cpp -- the reader "
		   "is not reading the server's source list";
	EXPECT_FALSE( cmake_graph::Compiles( kServer, "game/server/physics_bone_follower.cpp" ) )
		<< "physics_bone_follower.cpp is back in the server project. It holds the "
		   "only IMPLEMENT_SERVERCLASS_ST( CBoneFollower, DT_BoneFollower ) in the "
		   "tree. If bone followers are wanted again the CLIENT half has to come "
		   "back in the same commit -- a server class with no client half is fatal "
		   "at run time and silent at build time.";
}

TEST( OrphanClientClass, TheAttachedRagdollClientClassAndItsRecvTableAreGone )
{
	// THE FILE THEY LIVED IN IS GONE. C_ServerRagdollAttached's registration and
	// DT_Ragdoll_Attached were deleted from game/client/ragdoll.cpp by lane A5;
	// the file itself left client.so in plan physics_bones_native_zero Phase 3,
	// so neither can come back without the file coming back first.
	// (The proof-of-read was game/client/physics.cpp until that fork file left
	// client.so too; shell_clientdll.cpp is the client's own DLL object.)
	ASSERT_TRUE( cmake_graph::Compiles( kClient, "sourcesharp/game/client/shell_clientdll.cpp" ) )
		<< "client_sourcesharp does not compile shell_clientdll.cpp -- the "
		   "reader is not reading the client's source list";
	EXPECT_FALSE( cmake_graph::Compiles( kClient, kRagdoll ) )
		<< kRagdoll << " is back in the client project, and with it the place "
		   "the attached ragdoll's ClientClass registration lived.";
}

TEST( OrphanClientClass, TheGameRulesProxyClassItselfIsDeliberatelyKept )
{
	// Stops the fact above being satisfied by deleting the class -- which would
	// break CGameRules' two inline NetworkStateChanged overloads in a SHARED
	// header included by 23 client and 31 server translation units.
	const std::string hdr = NoWhitespace( StripComments( ReadRaw( kGameRulesH ) ) );

	// The `{` is part of the needle deliberately: without it, renaming the base
	// to CBaseEntityX leaves the old needle intact as a PREFIX and this fact
	// passes against a class that no longer derives from what it says. The
	// mutation harness found exactly that.
	EXPECT_EQ( 1, CountOf( hdr, "classCGameRulesProxy:publicIGameEntity{" ) )
		<< "CGameRulesProxy has been deleted from the shared header. Removing its "
		   "REGISTRATION was this step; removing the class is a gamerules decision "
		   "and it has to take CGameRules::NetworkStateChanged with it.";
	EXPECT_EQ( 1, CountOf( hdr, "staticvoidNotifyNetworkStateChanged();" ) )
		<< "NotifyNetworkStateChanged has gone; CGameRules' inlines call it.";
	EXPECT_EQ( 2, CountOf( hdr, "CGameRulesProxy::NotifyNetworkStateChanged();" ) )
		<< "CGameRules' two inline NetworkStateChanged forwards are not both there.";
}

//=============================================================================//
// 6. CRopeKeyframe -- THE CLASS IS NEEDED. Only the registration goes.
//=============================================================================//
TEST( OrphanClientClass, TheRopeKeyframeClientClassAndItsRecvTableAreGone )
{
	// c_rope.cpp itself left the client and the farm with the C2 rope port
	// (TheClientRopeIsManagedAndItsExecutorIsCompiled), so its registration
	// and recv proxy cannot come back without the file; the header stays, for
	// IRopeManager.
	const std::string hdr = NoWhitespace( StripComments( ReadRaw( kRopeHeader ) ) );

	EXPECT_EQ( 0, CountOf( hdr, "DECLARE_CLIENTCLASS()" ) )
		<< "c_rope.h declares a client class again.";
}

TEST( OrphanClientClass, TheRopeKeyframeClassItselfSurvives )
{
	const std::string hdr = NoWhitespace( StripComments( ReadRaw( kRopeHeader ) ) );

	// The `{` is part of the needle for the same reason as CGameRulesProxy's:
	// a substring needle misses added text, and `...CShellRenderableX` contains
	// `...CShellRenderable`.
	//
	// THE BASE CHANGED IN STEP c8-ropeanchor and this needle changed with it.
	// C_RopeKeyframe is no longer an entity: `: public C_BaseEntity` was the
	// largest single entry in the client removal set (160 of 161 vtable-only
	// symbol pairs, this object emitting the rope's vtable), and the class now
	// derives from CShellRenderable -- the de-entified renderable base
	// tempent.h already uses. The CLASS surviving is what this fact is about
	// and that has not changed; the assertion is pinned to the new base so
	// that a re-derivation onto the entity is a red test here as well as a
	// gate-1 regression.
	EXPECT_EQ( 1, CountOf( hdr, "classC_RopeKeyframe:publicCShellRenderable{" ) )
		<< "C_RopeKeyframe has been DELETED, or put back on C_BaseEntity. It is "
		   "the one of the four Phase 5 orphans that is a genuinely needed "
		   "client-only class: it is what draws every rope in the game. A "
		   "class-list diff going to zero is satisfied just as well by deleting "
		   "a class that was needed, and this is that class.";

	// And it is not an entity by any route. A base that reached C_BaseEntity
	// again -- directly, or through some new intermediate -- would satisfy the
	// needle above only if it were spelled exactly, so the entity's own name is
	// refused outright.
	EXPECT_EQ( 0, CountOf( hdr, "classC_RopeKeyframe:publicC_BaseEntity{" ) )
		<< "C_RopeKeyframe derives from the client base entity again. That one "
		   "line was 160 of the 161 vtable-only obligations in the whole client "
		   "hierarchy relink.";
	EXPECT_EQ( 1, CountOf( hdr, "staticC_RopeKeyframe*Create(" ) )
		<< "C_RopeKeyframe::Create has gone; it is how both client-only paths "
		   "build a rope.";
	EXPECT_EQ( 1, CountOf( hdr, "staticC_RopeKeyframe*CreateFromKeyValues(" ) )
		<< "C_RopeKeyframe::CreateFromKeyValues has gone.";
}

TEST( OrphanClientClass, TheClientRopeIsManagedAndItsExecutorIsCompiled )
{
	// RE-POINTED BY THE C2 ROPE PORT. The rope used to be drawn by building a
	// stock C_RopeKeyframe (14 constructions over a two-pass live gate, 14
	// Create() calls). It is simulated and built in C# now
	// (src/client/managed/Ropes, RopeStockReplayTests), and c_rope.cpp has left
	// the client; what must stay compiled is the executor that draws the strips.
	EXPECT_FALSE( cmake_graph::Compiles( kClient, kRope ) )
		<< "c_rope.cpp is back in the client project beside the managed rope.";
	EXPECT_TRUE( cmake_graph::Compiles( kClient, kRopeRender ) )
		<< "shell_rope_render.cpp left the client project; the managed ropes lose "
		   "their executor.";

	const std::string render = NoWhitespace( StripComments( ReadRaw( kRopeRender ) ) );
	EXPECT_EQ( 0, CountOf( render, "C_RopeKeyframe::Create(" ) )
		<< "shell_rope_render.cpp creates a stock rope again.";
	EXPECT_EQ( 1, CountOf( render, "voidSourceSharp_RopeDraw(" ) )
		<< "shell_rope_render.cpp no longer takes a managed rope's strip.";
}

TEST( OrphanClientClass, TheRecvProxySpringRecomputeIsCoveredByTheManagedRope )
{
	// THE SILENT-LOSS PIN. RecvProxy_RecomputeSprings existed to call
	// RecomputeSprings() after m_Slack or m_RopeLength moved. Deleting a recv
	// proxy deletes a thing the network did FOR the entity, and that is exactly
	// the class of loss this tree has been bitten by five times: invisible to the
	// compiler, to make check and to every live gate. The configure that took
	// over moved to C# with the rope.
	const std::string system = ReadRaw( "sourcesharp/managed/SourceSharp.Client/Ropes/ClientRopeSystem.cs" );
	const std::string apply = BodyOf( system, "private static void Apply(" );
	EXPECT_TRUE( Has( apply, "span.Sim.RecomputeSprings();" ) )
		<< "ClientRopeSystem.Apply writes the slack and the length and no longer "
		   "recomputes the springs. This call is the ONLY thing keeping a managed "
		   "rope's physics in step with its length.";

	const std::string sim = ReadRaw( "sourcesharp/managed/SourceSharp.Client/Ropes/RopeSimulation.cs" );
	const std::string setSlack = BodyOf( sim, "public void SetSlack(" );
	EXPECT_TRUE( Has( setSlack, "RecomputeSprings();" ) )
		<< "RopeSimulation.SetSlack no longer recomputes the springs.";
}

// TheLocalPlayerTeamAccessorIsCompiledOnThisClient stood here: it held
// cdll_util.cpp's GetLocalPlayerTeam in the build for InLocalTeam, whose own
// fact had already left with c_baseentity.cpp. cdll_util.cpp left client.so in
// C8 (lane c6-input) with no importer, so the accessor has no subject either.

TEST( OrphanClientClass, EveryCompiledSourceResolvesOnDisk )
{
	const char *targets[] = { kClient, kServer };
	for ( size_t v = 0; v < 2; v++ )
	{
		// cmake_graph throws on a missing, empty or stale list; see TargetFiles.
		const std::vector< std::string > files = TargetFiles( targets[ v ] );

		std::string missing;
		int nMissing = 0;
		for ( size_t i = 0; i < files.size(); i++ )
			if ( !Exists( files[ i ] ) )
			{
				nMissing++;
				missing += "\n    " + files[ i ];
			}

		EXPECT_EQ( 0, nMissing ) << targets[ v ] << " ("
			<< cmake_graph::ListFile( targets[ v ] ) << ") names " << nMissing
			<< " file(s) that are not on disk:" << missing;
	}
}

// TheVpcWalkerReadsContinuedFileRows is RETIRED (Phase 6b): it pinned the vpc
// walker's trap -- a backslash-continued `$File` row read only to its first
// path. The walker is gone with the vpcs; the CMake source list is parsed by
// cmake_graph.h and cross-checked there against compile_commands.json, so a
// list reader that drops rows throws rather than answering "none forbidden".
