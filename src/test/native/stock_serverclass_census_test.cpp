//========= SourceSharp — the surviving SDK ServerClass registrations ========//
//
// THE ENDGAME METER, given a floor it cannot silently rise through.
// `CShellGameDll::SeedNetworkingPointers` prints
//
//     [SourceSharp] server classes: N from managed, M from g_pServerClassHead
//
// and M is the count of SDK IMPLEMENT_SERVERCLASS registrations still in the
// built server. Part 5's endgame is M == 0: g_pServerClassHead is deleted and
// the member is seeded purely from C#. M is a number that only ever goes down,
// and a phase that re-adds one has undone a phase that removed one with nothing
// said. This file is what says it.
//
// WHY A SOURCE SCAN AND NOT A LINK. The registration is a file-static
// `ServerClass g_<Name>_ClassReg` (public/server_class.h:139) that links itself
// into g_pServerClassHead from its own constructor. There is no symbol a test
// binary can ask about and no function to call: the fact is about which .cpp
// the vpc compiles and what is written in them. So the vpc is parsed and the
// files it names are read — derived, not listed, because a hand-written list is
// a list that drifts. Cross-checked on the way in against
// `nm -C server.so | grep _ClassReg`, which answered the same five names.
//
// THE FIVE THAT SURVIVE, AND WHY THEY ARE A TREE RATHER THAN A LIST. This is
// the finding the phase turned on, and it is mechanical rather than a matter of
// judgement: `BEGIN_SEND_TABLE(className, tableName)` expands to
// `SendTableBaseAccess( (className::BaseClass *)NULL )`, which is
// `BaseClass::m_pClassSendTable` (public/dt_send.h:576-588), and that member is
// defined by IMPLEMENT_SERVERCLASS_INTERNAL and by nothing else. So a class
// whose registration is removed takes its DESCENDANTS' send tables with it, and
//
//     CBaseEntity  <- CWorld, CEntityDissolve, CBaseAnimating
//     CBaseAnimating <- CRagdollProp
//
// leaves CRagdollProp and CWorld as the only leaves of what remains. The meter
// can only ever come down from the leaves inward. Phase A5 took the two leaves
// that existed then, CRagdollPropAttached and CTeam.
//
// AND THEN THE OWNER REFUSED THE WAY A5 TOOK THEM, which is what this file is
// now mostly about. A5 deleted each class's DECLARE_SERVERCLASS and left the
// class standing in the build, networking as its base. The ruling: network vars
// belong in MANAGED code, with C++ calling into managed for get/set, so a class
// that quietly networks as its base has LOST a capability rather than MOVED
// one. Two endings are honest — move the props to managed and keep the
// declaration, or take the class out of the build — and both classes took the
// second, because both were unreachable rather than merely unnetworked:
//
//     team.h / team.cpp        out of server_csharp.vpc entirely, by the
//                              owner's ruling. The team concept comes back on
//                              the managed side later.
//     CRagdollPropAttached     deleted from physics_prop_ragdoll.cpp, with its
//                              factory, its detach helpers and its three
//                              header declarations.
//
// The meter did not move: A5 had already taken both registrations, so what this
// lane removed was two classes that were no longer registered. It stays at five.
//
// THE PREMISE IS PINNED, NOT ONLY THE EDIT. A removal is only safe while
// nothing needs what went, and that is a property of OTHER files which can
// change without anybody touching the ones that were edited. So
// NothingInTheBuildNamesAnAttachedRagdoll and NothingInTheBuildNamesATeamRegistry
// scan the whole compiled set for a caller, and NoCompiledFileIncludesTeamH
// walks the include closure. They are the facts that go red when somebody
// starts needing a team again — which is the point at which the managed port
// has to happen, and is not otherwise announced anywhere.
//
// COMMENTS ARE STRIPPED BEFORE EVERY SCAN, AND THAT IS LOAD-BEARING RATHER THAN
// TIDY. Every file this lane touched carries a written argument that SPELLS the
// exact strings this file asserts are absent — "CRagdollPropAttached",
// "GetGlobalTeam", "team_manager", "team.h". An unstripped scan would read the
// explanation as the violation and this file would be permanently red for the
// wrong reason. Same rule as effects_evicted_test.cpp. The one deliberate
// exception is NoDeclareServerclassIsLeftCommentedOut, whose subject IS a
// comment — and it matches only a comment whose FIRST TOKEN is the macro, so
// prose about the ruling is not read as a breach of it.
//
// ANCHORS, NOT COUNTS. Every file read is asserted to still contain a string
// that identifies it, because a source scanner whose file was renamed reads an
// empty string and reports every absence invariant as satisfied — the check
// that cannot fail.
//
//=============================================================================//

#include <string>
#include <vector>
#include <set>
#include <fstream>
#include <sys/stat.h>
#include <algorithm>
#include <sstream>
#include <dirent.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as effects_evicted_test.cpp.
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
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

// Raw, comments kept. Empty means "could not open", which every caller must
// tell apart from "opened and found nothing".
std::string ReadRaw( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return all;
}

std::string ReadStripped( const std::string &strRelative )
{
	const std::string raw = ReadRaw( strRelative );
	if ( raw.empty() )
		return raw;
	return StripComments( raw );
}

int CountOf( const std::string &hay, const std::string &needle )
{
	if ( needle.empty() )
		return 0;
	int n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos; at = hay.find( needle, at + 1 ) )
		n++;
	return n;
}

//-----------------------------------------------------------------------------
// THE COMPILED SET, from the CMake build.
//
// server_sourcesharp's source list (src/server/native/CMakeLists.txt), read
// through cmake_graph.h: compile_commands.json cross-checked against the
// CMakeLists. Farm-relative paths, which is the spelling ReadStripped takes.
// (It was derived by parsing server_csharp.vpc's `$File` rows until Phase 6b
// removed VPC.)
//-----------------------------------------------------------------------------
// THE PROOF-OF-READ is the PORT'S OWN shim, which no flip can take. It used to
// be `$SRCDIR\game\shared\baseentity_shared.cpp`, and THE FLIP REMOVED THAT
// ROW along with the rest of the entity hierarchy: a stock TU is not an anchor
// for this port -- removing stock TUs is precisely what the port does, so an
// anchor on one is a proof-of-read the flip can evict.
const char *kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

// THE LEGACY INCLUDE ANCHOR: the TU whose compile command supplies the -I list
// the closure walk resolves with. It must be a TU still on the LEGACY include
// set (game/server, game/shared on its -I list), because the closure being
// measured is what the stock-header-including TUs can reach.
// plan_server_novalve.md P5 moves TUs onto the R5 set (public/ only):
// shell_shared.cpp moved, then physics.cpp, and a moved anchor resolves nothing
// under game/. shell_mapparse.cpp waits on its tokenizer's move to C#, so it is
// the legacy TU that stays longest.
// The last legacy TU moved (P5 closed): the anchor is now just a server TU whose
// compile command gives the R5 roots, and the walk's absence facts hold by
// construction -- TheIncludeClosureIsWalkedAndRecurses says so.
const char *kServerLegacyIncludeAnchor = "sourcesharp/game/server/shell_mapparse.cpp";

std::vector< std::string > CompiledServerSources()
{
	const std::set< std::string > src = cmake_graph::CompiledSources( cmake_graph::kServer );
	return std::vector< std::string >( src.begin(), src.end() );
}

//-----------------------------------------------------------------------------
// THE LIST REALLY IS THE SERVER'S, asserted without a magic number.
//
// Five facts in this file used to guard themselves with `files.size() > 100`,
// and then with a SECOND, INDEPENDENT READING of server_csharp.vpc that the
// `$File` parser had to agree with exactly. RETIRED IN PHASE 6b: the second
// reading now lives in cmake_graph.h itself -- CompiledSources() throws unless
// compile_commands.json and the CMakeLists source list agree exactly, and
// throws on an empty or missing input -- so a parser that stops early or reads
// the wrong file is already a failure before this is reached. What is left here
// is the proof-of-read: the list contains the port's own shim.
//-----------------------------------------------------------------------------
void AssertTheServerFileListParsed( const std::vector< std::string > &files )
{
	ASSERT_TRUE( std::find( files.begin(), files.end(), std::string( kServerProofOfRead ) )
		!= files.end() )
		<< "server_sourcesharp's compiled sources do not include "
		<< kServerProofOfRead << ", so what was read is not this project's file "
		   "list. Every absence check below is measured over this list.";
}

// Every compiled .cpp, comments gone, concatenated. One read of the tree for
// the several questions below to be asked of.
const std::string &CompiledServerCode()
{
	static std::string s_all;
	static bool s_bDone = false;
	if ( !s_bDone )
	{
		s_bDone = true;
		const std::vector< std::string > files = CompiledServerSources();
		for ( size_t i = 0; i < files.size(); i++ )
			s_all += ReadStripped( files[ i ] );
	}
	return s_all;
}

// The class name out of every IMPLEMENT_SERVERCLASS* in a body of code.
//
// SPLIT FROM RegisteredServerClasses() SO IT CAN BE EXERCISED. Since the flip
// the compiled set contains NO registration at all, so the set this returns is
// empty, and a matcher that had stopped recognising the macro would return an
// empty set too. Given fixture text it must still find one -- see
// TheRegistrationMatcherStillRecognisesTheMacro.
std::set< std::string > ServerClassesIn( const std::string &all )
{
	std::set< std::string > out;
	const std::string needle = "IMPLEMENT_SERVERCLASS";

	for ( size_t at = all.find( needle ); at != std::string::npos; at = all.find( needle, at + 1 ) )
	{
		size_t i = at + needle.size();
		// _ST, _ST_NOBASE, _INTERNAL, or nothing -- skip whatever suffix is
		// there and land on the open paren. The macro DEFINITIONS live in
		// public/server_class.h, which is not in the compiled set, so every
		// occurrence here is a use.
		while ( i < all.size() && ( all[ i ] == '_' || ( all[ i ] >= 'A' && all[ i ] <= 'Z' ) ) )
			i++;
		while ( i < all.size() && ( all[ i ] == ' ' || all[ i ] == '\t' ) )
			i++;
		if ( i >= all.size() || all[ i ] != '(' )
			continue;
		i++;
		while ( i < all.size() && ( all[ i ] == ' ' || all[ i ] == '\t' || all[ i ] == '\n' ) )
			i++;

		std::string name;
		while ( i < all.size()
			&& ( all[ i ] == '_'
				|| ( all[ i ] >= 'a' && all[ i ] <= 'z' )
				|| ( all[ i ] >= 'A' && all[ i ] <= 'Z' )
				|| ( all[ i ] >= '0' && all[ i ] <= '9' ) ) )
		{
			name += all[ i ];
			i++;
		}
		if ( !name.empty() )
			out.insert( name );
	}
	return out;
}

std::set< std::string > RegisteredServerClasses()
{
	return ServerClassesIn( CompiledServerCode() );
}

std::string Join( const std::set< std::string > &s )
{
	std::string out;
	for ( std::set< std::string >::const_iterator it = s.begin(); it != s.end(); ++it )
	{
		if ( !out.empty() )
			out += " ";
		out += *it;
	}
	return out;
}

// game/server/physics_prop_ragdoll.cpp is no longer compiled by server.so and
// has left the source tree; the two facts that read it (the attached class is
// gone from it, the two remaining classnames still register there) were culled
// with it. The header followed: it is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6, so it is named here only
// as the thing that must stay unreadable, next to a sibling in the same
// directory that is readable.
const char *kRagdollH = "game/server/physics_prop_ragdoll.h";
const char *kServerSiblingH = "game/server/activator.h";
const char *kServerSiblingHAnchor = "#define ACTIVATOR_H";

// team.cpp and team.h are OUT OF THE BUILD. They stay on disk, so they are
// still named here -- but only as the thing nothing may reach, never as a file
// read for its content. kTeamH is the target of the include-closure walk below.
const char *kTeamCpp = "game/server/team.cpp";
const char *kTeamH = "game/server/team.h";

const char *kSdkArmFixtureFile = "sourcesharp/game/server/shell_abi_sdk_fixture.cpp";
const char *kSdkArmFixture = "sourcesharp_abi_sdk_fixture";
const char *kEntityNames = "sourcesharp/managed/SourceSharp.Shared/EntityNames.cs";
const char *kManagedServer = "sourcesharp/managed/SourceSharp.Server";

// Every file with the given extension under one directory, recursively. Used to
// ask the managed tree a question no single file can answer -- "does ANY
// [ScriptedEntity] shadow this classname" -- rather than picking a file and
// hoping the answer is in it.
void CollectFiles( const std::string &strDir, const char *pszExt,
                   std::vector< std::string > &out )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return;

	DIR *pDir = opendir( ( std::string( pszRoot ) + "/" + strDir ).c_str() );
	if ( !pDir )
		return;

	for ( struct dirent *pEnt = readdir( pDir ); pEnt; pEnt = readdir( pDir ) )
	{
		const std::string name = pEnt->d_name;
		if ( name == "." || name == ".." )
			continue;

		const std::string rel = strDir + "/" + name;
		struct stat st;
		if ( stat( ( std::string( pszRoot ) + "/" + rel ).c_str(), &st ) != 0 )
			continue;

		if ( S_ISDIR( st.st_mode ) )
		{
			CollectFiles( rel, pszExt, out );
			continue;
		}

		const std::string ext = pszExt;
		if ( name.size() > ext.size()
			&& name.compare( name.size() - ext.size(), ext.size(), ext ) == 0 )
		{
			out.push_back( rel );
		}
	}
	closedir( pDir );
}

//-----------------------------------------------------------------------------
// THE INCLUDE CLOSURE, which is how "no compiled file includes team.h" is asked
// as a question about the BUILD rather than about the tree.
//
// A whole-tree grep is the wrong instrument and says so loudly: FIFTY-ONE files
// in this SDK include team.h, and after this lane none of them is compiled. The
// number that matters is over the compiled set AND its headers, so the walk
// starts at every source server_sourcesharp compiles and follows quoted
// includes.
//
// It resolves them against the server's own -I list, in its order, with the
// includer's own directory tried first -- which is what a quoted include means.
// The list is read out of the server's compile command (cmake_graph.h); it
// used to be a copy of VPC's ninja graph's server_sourcesharp_includes.
// Angle-bracket includes are skipped: nothing in this tree reaches a game
// header that way.
//
// IT DOES NOT MODEL #if, AND THAT IS THE FAIL-SAFE DIRECTION. An include the
// preprocessor would skip is walked anyway, so the closure is a SUPERSET of
// what ninja records. Measured against ninja's own dependency graph when this
// was written: the graph named five objects reaching team.h, the walk found the
// same four .cpp behind them, and with those four includes gone both say zero.
// A superset that is empty is a proof, and the over-approximation can only ever
// make this fire early, never late.
//-----------------------------------------------------------------------------
bool Exists( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return false;
	struct stat st;
	return stat( ( std::string( pszRoot ) + "/" + strRelative ).c_str(), &st ) == 0
		&& S_ISREG( st.st_mode );
}

// Collapse "a/b/../c" to "a/c" and drop "./", so two spellings of one file are
// one member of the closure rather than two.
std::string Normalise( const std::string &strPath )
{
	std::vector< std::string > parts;
	size_t at = 0;
	for ( ;; )
	{
		const size_t slash = strPath.find( '/', at );
		const std::string part = ( slash == std::string::npos )
			? strPath.substr( at )
			: strPath.substr( at, slash - at );
		if ( part == ".." && !parts.empty() && parts.back() != ".." )
			parts.pop_back();
		else if ( !part.empty() && part != "." )
			parts.push_back( part );
		if ( slash == std::string::npos )
			break;
		at = slash + 1;
	}
	std::string out;
	for ( size_t i = 0; i < parts.size(); i++ )
	{
		if ( i )
			out += "/";
		out += parts[ i ];
	}
	return out;
}

std::string ResolveInclude( const std::string &strIncluder, const std::string &strInclude )
{
	// server_sourcesharp's -I list, in its order, read out of the compile
	// command of the proof-of-read source (cmake_graph.h). Only roots under the
	// farm's src/ are kept: a system root (/usr/include/...) holds no game
	// header, and nothing in this tree reaches one through a quoted include.
	static std::vector< std::string > s_roots;
	if ( s_roots.empty() )
	{
		const std::string strPrefix = cmake_graph::FarmSrc() + "/";
		std::istringstream toks( cmake_graph::CompileCommand( cmake_graph::kServer, kServerLegacyIncludeAnchor ) );
		std::string strTok;
		while ( toks >> strTok )
		{
			if ( strTok.compare( 0, 2, "-I" ) != 0 )
				continue;
			const std::string strDir = strTok.substr( 2 );
			if ( strDir.compare( 0, strPrefix.size(), strPrefix ) == 0 )
				s_roots.push_back( strDir.substr( strPrefix.size() ) );
		}
		// A command whose -I list resolved to nothing under the farm is not
		// the server's, and an empty root list would make every include
		// unresolvable -- the closure checks below catch that too, but say so
		// here where the cause is.
		// Since P5 every server TU is on the R5 set, so the farm roots are
		// public/ and its tier0/tier1 children, never game/.
		EXPECT_TRUE( std::find( s_roots.begin(), s_roots.end(), std::string( "public" ) )
			!= s_roots.end() )
			<< "server_sourcesharp's compile command has no -I<farm>/src/public";
	}

	std::string inc = strInclude;
	for ( size_t i = 0; i < inc.size(); i++ )
	{
		if ( inc[ i ] == '\\' )
			inc[ i ] = '/';
	}

	// A quoted include looks beside the includer first.
	const size_t slash = strIncluder.rfind( '/' );
	if ( slash != std::string::npos )
	{
		const std::string beside = Normalise( strIncluder.substr( 0, slash + 1 ) + inc );
		if ( Exists( beside ) )
			return beside;
	}

	for ( size_t i = 0; i < s_roots.size(); i++ )
	{
		const std::string cand = Normalise( s_roots[ i ] + "/" + inc );
		if ( Exists( cand ) )
			return cand;
	}
	return std::string();
}

// Every quoted #include in one file, in source order. Comments are stripped
// first: a commented-out include is not an include, and stripping is what makes
// that true rather than assumed -- three files in the compiled set now carry a
// comment that names team.h and says why it is gone.
std::vector< std::string > QuotedIncludes( const std::string &strRelative )
{
	std::vector< std::string > out;
	const std::string body = ReadStripped( strRelative );

	size_t at = 0;
	while ( at < body.size() )
	{
		size_t eol = body.find( '\n', at );
		if ( eol == std::string::npos )
			eol = body.size();
		const std::string line = body.substr( at, eol - at );
		at = eol + 1;

		const size_t hash = line.find_first_not_of( " \t" );
		if ( hash == std::string::npos || line[ hash ] != '#' )
			continue;
		const size_t kw = line.find( "include", hash );
		if ( kw == std::string::npos )
			continue;
		const size_t q1 = line.find( '"', kw );
		if ( q1 == std::string::npos )
			continue;
		const size_t q2 = line.find( '"', q1 + 1 );
		if ( q2 == std::string::npos )
			continue;
		out.push_back( line.substr( q1 + 1, q2 - q1 - 1 ) );
	}
	return out;
}

const std::set< std::string > &CompiledServerClosure()
{
	static std::set< std::string > s_closure;
	static bool s_bDone = false;
	if ( s_bDone )
		return s_closure;
	s_bDone = true;

	std::vector< std::string > queue = CompiledServerSources();
	for ( size_t i = 0; i < queue.size(); i++ )
		s_closure.insert( queue[ i ] );

	for ( size_t i = 0; i < queue.size(); i++ )
	{
		const std::vector< std::string > incs = QuotedIncludes( queue[ i ] );
		for ( size_t j = 0; j < incs.size(); j++ )
		{
			const std::string hit = ResolveInclude( queue[ i ], incs[ j ] );
			if ( hit.empty() || s_closure.count( hit ) )
				continue;
			s_closure.insert( hit );
			queue.push_back( hit );
		}
	}
	return s_closure;
}

} // namespace


//=============================================================================//
// THE METER'S FLOOR
//=============================================================================//

TEST( StockServerClassCensus, TheSourceListIsReadAndNamesTheFilesTheServerCompiles )
{
	// The vacuity guard for everything below. Every other fact in this file is
	// an assertion about a body of text assembled from this list; an empty or
	// wrong list makes all of them pass by finding nothing. (cmake_graph.h
	// throws on an unreadable or stale list; the anchor is asserted below.)
	const std::vector< std::string > files = CompiledServerSources();
	ASSERT_NO_FATAL_FAILURE( AssertTheServerFileListParsed( files ) );

	// THE CONCATENATION, ASKED PER FILE RATHER THAN AS A TOTAL. This was
	// `CompiledServerCode().size() > 1,000,000u` -- a byte floor over a body of
	// text the port exists to delete, which would say "the paths this file
	// builds no longer resolve" about paths that resolve perfectly, the moment
	// enough stock has left.
	//
	// What the floor was FOR is a path that does not open: ReadStripped answers
	// "" for one, and the concatenation swallows that silently. Asked per file,
	// that is caught whatever the population is -- and caught for ONE bad path,
	// where a million-byte total needed most of them to fail at once. Strictly
	// more than the number it replaces, and nothing left to go stale.
	std::string strUnread;
	for ( size_t i = 0; i < files.size(); i++ )
	{
		if ( ReadStripped( files[ i ] ).empty() )
			strUnread += "\n    " + files[ i ];
	}
	EXPECT_TRUE( strUnread.empty() )
		<< "server_sourcesharp compiles source(s) that read as nothing, so every "
		   "absence check below passes over a hole where their code should be:"
		<< strUnread;
}

TEST( StockServerClassCensus, ExactlyFourSdkServerClassRegistrationsSurvive )
{
	// THE METER'S SECOND NUMBER, as a set rather than a count, because a count
	// that stays the same while one name is swapped for another is a count that
	// said nothing. Cross-checked against the binary on the way in:
	//   nm -C server.so | grep ' [bd] .*_ClassReg'  ->  the same four.
	//
	// This is a CEILING that must come down and never go up. Every name below
	// has an entry in docs/gamedll-ledger.md's Phase A5 section saying what
	// blocks it and whose work moving it is.
	//
	// FIVE -> FOUR: CWorld LEFT, and it left by MOVING rather than by being
	// relabelled. plan_gamedll_after.md Part B2: worldspawn's ServerClass and
	// every one of its props are built in shell_world_nettable.cpp out of
	// SourceSharp.Shared.WorldNetState, so there is no IMPLEMENT_SERVERCLASS_ST
	// for this scan to find and no g_CWorld_ClassReg in the binary. The half
	// that says it moved rather than vanished is
	// OnlyCWorldIsRegisteredFromSourceSharpsOwnTree below, which asserts the
	// managed answerer exists in the same breath as asserting the macro does
	// not.
	// FOUR -> ZERO, AND THE CEILING HAS REACHED THE FLOOR. All four registrations
	// lived in TUs the flip removed from server_csharp.vpc:
	//
	//     CBaseAnimating    game\server\baseanimating.cpp
	//     CBaseEntity       game\server\baseentity.cpp
	//     CEntityDissolve   game\server\EntityDissolve.cpp
	//     CRagdollProp      game\server\physics_prop_ragdoll.cpp
	//
	// This fact's own sentence names what that means: "the count printed at
	// DLLInit is what Part 5 deletes g_pServerClassHead on reaching zero." It
	// has reached zero. The direction of the ratchet is unchanged -- a CEILING
	// that must come down and never go up -- and at zero every arrival is a
	// regression, which is a stronger statement than the four-name set was.
	std::set< std::string > expected;   // deliberately empty; see above.

	const std::set< std::string > got = RegisteredServerClasses();

	EXPECT_EQ( expected, got )
		<< "the surviving SDK ServerClass registrations are now [" << Join( got )
		<< "] and there should be none. The flip removed every TU that carried "
		   "one, so an arrival is a stock entity TU coming back into the server "
		   "project -- the endgame moving backwards, and g_pServerClassHead "
		   "needed again after Part 5 was told it could go";
}

//-----------------------------------------------------------------------------
// THE CONTROL FOR THAT EMPTY SET.
//
// An EXPECT_EQ against an empty set is satisfied by a scanner that stopped
// recognising the macro, by a compiled-file list that came back empty, and by
// the tree really being clean -- and only the third is good news. The list half
// is covered by TheScannerReallyReadTheServerProject above (>100 files, >1MB of
// text). This is the matcher half: given text that HAS registrations it must
// still find them, and it must still find them through every suffix the family
// spells.
//-----------------------------------------------------------------------------
TEST( StockServerClassCensus, TheRegistrationMatcherStillRecognisesTheMacro )
{
	const std::string fixture =
		"IMPLEMENT_SERVERCLASS_ST( CBaseEntity, DT_BaseEntity )\n"
		"IMPLEMENT_SERVERCLASS_ST_NOBASE( CRagdollProp, DT_Ragdoll )\n"
		"IMPLEMENT_SERVERCLASS(CWorld, DT_WORLD)\n";

	const std::set< std::string > got = ServerClassesIn( fixture );

	EXPECT_EQ( 3u, got.size() ) << "matched [" << Join( got ) << "]";
	EXPECT_EQ( 1u, got.count( "CBaseEntity" ) );
	EXPECT_EQ( 1u, got.count( "CRagdollProp" ) );
	EXPECT_EQ( 1u, got.count( "CWorld" ) );

	// And it does NOT match text that only mentions the macro name, which is
	// what would make the real scan answer non-empty off a comment.
	EXPECT_TRUE( ServerClassesIn( "// IMPLEMENT_SERVERCLASS_ST is the macro\n" ).empty() );
}

TEST( StockServerClassCensus, OnlyCWorldIsRegisteredFromSourceSharpsOwnTree )
{
	// PHASE A5'S GUARD, AND IT IS NOT WEAKENED -- IT IS THE OTHER HALF OF IT.
	//
	// A5 wrote this to refuse ONE specific edit. CWorld was registered in
	// sourcesharp/game/server/world_adapter.cpp -- our own file -- with the SDK
	// macro, and it counted as STOCK because SourceSharp_IsManagedServerClass
	// answers by IDENTITY against the places SourceSharp CONSTRUCTS a
	// ServerClass and the macro was not one of them. A5's words:
	//
	//   "What must not happen is somebody moving the meter by adding
	//    world_adapter.cpp to that identity list: it would read as an eviction
	//    and evict nothing."
	//
	// plan_gamedll_after.md Part B2 MOVED IT, which is the legitimate way past
	// that guard and the only one. So the fact now asserts BOTH HALVES AT ONCE,
	// and neither of them alone gets a lower meter:
	//
	//   1. the SDK macro is GONE from world_adapter.cpp -- there is no
	//      g_CWorld_ClassReg to count;
	//   2. the meter's third answerer EXISTS and is answered by identity
	//      against an object shell_world_nettable.cpp built from the C#
	//      declaration.
	//
	// The relabelling A5 refused fails clause 1: adding the answerer while
	// leaving the macro standing is exactly "reads as an eviction and evicts
	// nothing", and it is red here. Deleting the macro and NOT moving the props
	// is Part B2's other forbidden ending -- a class networking as its base --
	// and it fails clause 2.
	const std::string world = ReadStripped( "sourcesharp/game/server/world_adapter.cpp" );
	ASSERT_FALSE( world.empty() );
	EXPECT_EQ( 0, CountOf( world, "IMPLEMENT_SERVERCLASS_ST(CWorld, DT_WORLD)" ) )
		<< "the SDK macro is back in world_adapter.cpp. Two registrations for "
		   "one class name do not collide at link time -- static-initialisation "
		   "order picks one, silently -- and the meter would count the stock one";

	// Clause 2, in two places, because a claim in the meter and no object
	// behind it is the relabelling with an extra step.
	const std::string nettable = ReadStripped( "sourcesharp/game/server/shell_nettable.cpp" );
	ASSERT_FALSE( nettable.empty() );
	EXPECT_EQ( 1, CountOf( nettable, "SourceSharp_IsWorldServerClass( pClass )" ) )
		<< "SourceSharp_IsManagedServerClass no longer asks about the world. "
		   "With the SDK macro gone as well, CWorld is counted as STOCK while "
		   "having no stock registration -- the meter would read one too high "
		   "and the reading would be about nothing";

	const std::string table = ReadStripped( "sourcesharp/game/server/shell_world_nettable.cpp" );
	ASSERT_FALSE( table.empty() );
	EXPECT_EQ( 1, CountOf( table, "new ServerClass( \"CWorld\"" ) )
		<< "nothing in SourceSharp's own tree constructs the world's ServerClass "
		   "any more. Counting it as managed with no managed object behind it is "
		   "the exact edit Phase A5 refused";
}


//=============================================================================//
// THE INCLUDE CLOSURE ITSELF, before anything is asked of it
//=============================================================================//

TEST( StockServerClassCensus, TheIncludeClosureIsWalkedAndRecurses )
{
	// The vacuity guard for the two absence facts below. A resolver that
	// resolved nothing would report a closure of just the sources, and every
	// "team.h is not in it" question would pass on the strength of that alone
	// -- the exact shape of failure this file's header is about.
	//
	// So: the closure is much bigger than the file list it starts from, and it
	// holds headers reachable ONLY through another header. cbase.h is included
	// by essentially every .cpp directly; ehandle.h is included by NO compiled
	// .cpp at all -- it arrives under baseentity.h and its neighbours -- so
	// finding it proves the walk RECURSED rather than reading one level of each
	// source. Measured, not assumed: of the 789 files in the closure, 383 are
	// direct includes and ehandle.h is in the other 406.
	const std::set< std::string > &closure = CompiledServerClosure();
	const std::vector< std::string > sources = CompiledServerSources();

	ASSERT_NO_FATAL_FAILURE( AssertTheServerFileListParsed( sources ) );
	EXPECT_GT( closure.size(), sources.size() * 3 )
		<< "the closure is " << closure.size() << " files over " << sources.size()
		<< " sources. The include resolver has stopped resolving, and the "
		   "absence facts below would then pass on an empty walk";

	// SINCE P5 EVERY SERVER TU IS ON THE R5 SET, so cbase.h, baseentity.h and
	// ehandle.h -- the witnesses this used -- are absent by construction: no
	// compile command has a game/ directory to find them in. They are asserted
	// ABSENT now, and recursion is shown by a header the closure reached that
	// no compiled source includes directly, computed rather than named.
	EXPECT_EQ( 0u, closure.count( "game/server/cbase.h" ) )
		<< "cbase.h is reachable from the compiled server again";
	EXPECT_EQ( 0u, closure.count( "game/server/baseentity.h" ) );
	EXPECT_EQ( 0u, closure.count( "game/shared/ehandle.h" ) );

	std::set< std::string > direct;
	for ( size_t i = 0; i < sources.size(); i++ )
	{
		const std::vector< std::string > incs = QuotedIncludes( sources[ i ] );
		for ( size_t j = 0; j < incs.size(); j++ )
			direct.insert( ResolveInclude( sources[ i ], incs[ j ] ) );
	}
	int nOnlyThroughAHeader = 0;
	for ( std::set< std::string >::const_iterator it = closure.begin(); it != closure.end(); ++it )
	{
		if ( !direct.count( *it ) && std::find( sources.begin(), sources.end(), *it ) == sources.end() )
			nOnlyThroughAHeader++;
	}
	EXPECT_GT( nOnlyThroughAHeader, 0 )
		<< "every file in the closure is a source or a direct include of one: "
		   "the walk is not recursing, and a one-level walk would say team.h "
		   "was absent while being unable to see it";
}


//=============================================================================//
// REMOVAL 1 -- team.h and team.cpp are out of the build
//
// The owner's ruling, superseding Phase A5. A5 deleted CTeam's
// DECLARE_SERVERCLASS and left the class in the build networking as
// DT_BaseEntity; that pattern is refused, because network vars belong in
// managed code with C++ calling in for get/set, and a class that quietly
// networks as its base has LOST a capability rather than MOVED one. The team
// concept comes back on the managed side later.
//=============================================================================//

TEST( StockServerClassCensus, TeamCppIsNotOneOfTheFilesTheServerCompiles )
{
	// Asked of the COMPILED list rather than of any file's text: when this was
	// the vpc, that carried a long note that spelled "team.cpp" several times.
	const std::vector< std::string > files = CompiledServerSources();
	ASSERT_NO_FATAL_FAILURE( AssertTheServerFileListParsed( files ) );

	int nTeam = 0;
	for ( size_t i = 0; i < files.size(); i++ )
	{
		if ( files[ i ] == kTeamCpp )
			nTeam++;
	}
	EXPECT_EQ( 0, nTeam )
		<< "team.cpp is back in the server's file list. The team concept is "
		   "managed work now; a native CTeam is not the way back to it";
}

TEST( StockServerClassCensus, NoCompiledFileIncludesTeamH )
{
	// THE GATE, over the closure rather than over the tree. FIFTY-ONE files in
	// this SDK include team.h and every one of them is a file this server does
	// not compile -- so a grep answers 51 and means nothing. What this asks is
	// whether team.h is reachable from any compiled source by any chain of quoted
	// includes, which is the question the compiler answers.
	const std::set< std::string > &closure = CompiledServerClosure();
	// RELATIVE, NOT 300. The closure is walked out of the compiled source list,
	// so it shrinks exactly as that list does -- a fixed floor over it is the same
	// stale-on-a-correct-tree shape as the row floors above. The claim that does
	// not go stale is the one TheIncludeClosureIsWalkedAndRecurses already
	// makes: a real closure is several times the source list, because each
	// source drags in cbase.h and its neighbours, and a resolver that stopped
	// resolving returns roughly the sources themselves.
	const std::vector< std::string > sources = CompiledServerSources();
	ASSERT_NO_FATAL_FAILURE( AssertTheServerFileListParsed( sources ) );
	ASSERT_GT( closure.size(), sources.size() * 3 )
		<< "the closure is " << closure.size() << " files over " << sources.size()
		<< " compiled sources -- the include resolver has stopped resolving, and "
		   "the two absences below would pass on an empty walk";

	EXPECT_EQ( 0u, closure.count( kTeamH ) )
		<< "team.h is reachable from the compiled set again. Whatever includes "
		   "it now has a CTeam it can name and cannot use: nothing compiles the "
		   "class, so the first member access is a link error";
	EXPECT_EQ( 0u, closure.count( kTeamCpp ) )
		<< "team.cpp is being #included by something, which is a stranger way "
		   "back into the build than the vpc but works just as well";
}

TEST( StockServerClassCensus, NothingInTheBuildNamesATeamRegistry )
{
	// THE PREMISE, and the thing that goes red when the removal stops being
	// safe. team.cpp defined the registry; nothing may name it now, because
	// nothing DEFINES it -- a caller is a link error, and this is the cheaper
	// place to find that out.
	//
	// This also CORRECTS PHASE A5, which belongs here rather than only in a
	// commit message. A5's argument, and the test that stood on this spot, both
	// said g_Teams "is appended to only by CTeam::Init". CTeam::Init does not
	// touch g_Teams and never did; stock appends in the gamerules
	// (game/shared/hl2mp/hl2mp_gamerules.cpp:189), which has never been in this
	// build. So g_Teams was written by NOTHING the server compiled -- a
	// stronger reason for the same conclusion, and one that does not rest on a
	// function's caller count.
	const std::string &all = CompiledServerCode();
	ASSERT_FALSE( all.empty() );

	EXPECT_EQ( 0, CountOf( all, "g_Teams" ) )
		<< "g_Teams is named in the compiled server and defined nowhere in it";
	EXPECT_EQ( 0, CountOf( all, "GetGlobalTeam" ) )
		<< "GetGlobalTeam is named in the compiled server and defined nowhere "
		   "in it. It answered NULL for the life of the server before team.cpp "
		   "left; CBaseEntity::GetTeam now returns that NULL directly";
	EXPECT_EQ( 0, CountOf( all, "GetNumberOfTeams" ) )
		<< "GetNumberOfTeams is named in the compiled server and defined "
		   "nowhere in it";
	EXPECT_EQ( 0, CountOf( all, "CTeam::" ) )
		<< "a CTeam member function is named in the compiled server. None of "
		   "them is compiled";
	EXPECT_EQ( 0, CountOf( all, "team_manager" ) )
		<< "team_manager is spelled in the compiled server. Nothing links that "
		   "classname to a class any more, so CreateEntityByName answers NULL "
		   "for it";
}

//=============================================================================//
// THE SDK-ARM FIXTURE IS RESIDUE NOW -- step 8a
//
// It used to be REQUIRED: AbiBothHierarchies ran every entity ABI question
// against one entity of each hierarchy, and without a spawnable stock
// CBaseEntity subclass it had one arm and compared an arm with itself. Two
// facts stood here and both asserted the fixture EXISTED -- that
// `sourcesharp_abi_sdk_fixture` was LINK_ENTITY_TO_CLASS'd, that its file was
// in the server's list, and that no managed [ScriptedEntity] shadowed it.
//
// Step 8a deleted the probe (plan_entity.md Phase 8, step 8a: "delete
// EntityHierarchy, AbiBothHierarchies, the OnHierarchy flag, the dead
// OnNetEntity* slots"). Three of those four are now done: the OnNetEntity*
// slots were reaped at ABI 240, and the EntityHierarchy ABI slot -- the probe's
// one way of PROVING it held an entity of each hierarchy -- at ABI 241. The
// OnHierarchy flag deliberately STAYS: 8a checked it and found it is not
// scaffolding at all but the arithmetic of the standing owner instruction in
// plan_entity.md 3.11 that CWorld is its own standalone class, and that both
// sentences defending it in the tree had gone stale. So the fixture has no
// consumer.
//
// AND THE FILE IS NOW GONE TOO -- lane `5e-testonly`, which measured that the
// three sentences this block used to carry were all false by the time anyone
// read them:
//
//   "It is still COMPILED"          -- it was not. `shell_abi_sdk_fixture.cpp`
//                                      was named by NO .vpc and appeared on
//                                      NEITHER ninja graph.
//   "`: public CPointEntity`"       -- the class had already been stripped by
//                                      f288a5e11. The file's last 65 lines were
//                                      comment, `#include "cbase.h"` and
//                                      `#include "tier0/memdbgon.h"`. No class,
//                                      no LINK_ENTITY_TO_CLASS, no code.
//   "removing it ... is p4-exit's"  -- the vpc row had already gone in
//                                      fd5f3bc8d, so there was nothing left for
//                                      p4-exit to remove but the file itself.
//
// THE POLARITY THIS BLOCK CHOSE WAS THE RIGHT ONE, which is why the sweeps
// below survive the removal unchanged: they pin that NOTHING REACHES FOR IT,
// and that is still the regression that would matter -- a second entity model
// coming back needs a stock-hierarchy fixture, and re-introducing one here is
// how it would start. What did NOT survive is the vacuity control, which keyed
// on the file being in the server's file list: it had been unable to fire since
// fd5f3bc8d and is replaced below by the absence check for the file itself.
//=============================================================================//

TEST( StockServerClassCensus, TheSdkArmFixtureHasNoConsumerLeft )
{
	// NATIVE. Every file the server compiles, comment-stripped -- the fixture's
	// own file is allowed to name the classname (that IS the registration) and
	// nothing else may.
	const std::vector< std::string > files = CompiledServerSources();
	ASSERT_NO_FATAL_FAILURE( AssertTheServerFileListParsed( files ) );

	std::string offenders;
	int nRegistrations = 0;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		const std::string body = ReadStripped( files[ i ] );

		if ( files[ i ] == kSdkArmFixtureFile )
		{
			nRegistrations += CountOf( body, kSdkArmFixture );
			continue;
		}

		if ( body.find( kSdkArmFixture ) != std::string::npos )
			offenders += "\n    " + files[ i ];
	}

	EXPECT_TRUE( offenders.empty() )
		<< kSdkArmFixture << " is named by a compiled server TU other than its "
		<< "own:" << offenders
		<< "\n  The classname exists to give a two-hierarchy probe a stock arm, "
		   "and step 8a deleted that probe. A new native consumer means a "
		   "second entity model is being reached for again.";

	// THE VACUITY CONTROL IS RETIRED, and saying why is the point.
	//
	// It read: "while the file is still in the build its own registration must
	// be found", and fired only inside `if ( nRegistrations == 0 )`. The file
	// stopped being in the server's file list at fd5f3bc8d, so from that commit
	// on `bFileInBuild` was false on every run and EXPECT_FALSE( false ) could
	// not fail. It was a check that cannot fail for five days before anybody
	// looked, and it is exactly the shape this file's own header warns about:
	// a source scanner whose file went away reads nothing and reports every
	// absence invariant as satisfied.
	//
	// What replaces it is the absence check for the file itself, which CAN
	// fail -- restore shell_abi_sdk_fixture.cpp and this goes red.
	EXPECT_EQ( 0, nRegistrations )
		<< kSdkArmFixture << " is registered by a compiled server TU. The "
		   "fixture's own file was deleted by lane 5e-testonly because the "
		   "probe it existed for went in step 8a; a registration reappearing "
		   "means a stock-hierarchy fixture is being reached for again.";
}

//=============================================================================//
// THE ABSENCE CHECK FOR THE FILE, and it proves WHICH TREE it read.
//
// A helper that walks UPWARD to find a repo root escapes a worktree into the
// main checkout, where the deleted file still exists, and the absence then
// answers about the wrong tree. SrcRoot() does not walk up: it tries ".",
// "src" and "../src" and nothing else, so the deepest it can reach is the
// parent of the directory the test was run from. The resolved root is printed
// on every failure, and the ANCHOR below is a file that must be present in the
// same tree -- without it, a root that resolved to an empty directory would
// report the absence as satisfied.
//=============================================================================//

TEST( StockServerClassCensus, TheSdkArmFixtureFileIsGone )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL )
		<< "SrcRoot() found no candidate with both game/ and public/ under it; "
		   "the absence below would pass on a tree that was never read";

	// THE ANCHOR. A tree this test can read at all still has the census test's
	// own subject in it. If this is missing the root is wrong and the absence
	// claim underneath is worthless.
	const std::string anchor = std::string( pszRoot ) + "/" + kEntityNames;
	struct stat st;
	ASSERT_EQ( 0, stat( anchor.c_str(), &st ) )
		<< "resolved SrcRoot() to '" << pszRoot << "' but " << anchor
		<< " is not there, so this tree is not the one the deletion was made "
		   "in and the absence below would be about somebody else's checkout";

	const std::string gone = std::string( pszRoot ) + "/" + kSdkArmFixtureFile;
	EXPECT_NE( 0, stat( gone.c_str(), &st ) )
		<< gone << " still exists. Resolved SrcRoot() to '" << pszRoot
		<< "'. Lane 5e-testonly deleted it: no .vpc named it, neither ninja "
		   "graph carried an object for it, its class and LINK_ENTITY_TO_CLASS "
		   "had already been stripped by f288a5e11, and the AbiBothHierarchies "
		   "probe it was the stock arm for went in step 8a. If it is back, the "
		   "two sweeps above need their needles rechecked before it stays.";
}

TEST( StockServerClassCensus, NoManagedCodeReachesForTheSdkArmFixture )
{
	// MANAGED. AbiBothHierarchies.cs was the one file that named the classname
	// to spawn it; AbiBothHierarchiesTests.cs was the one that read it back.
	// Both are deleted. EntityNames.cs still declares the constant, and lane
	// 5e-testonly kept it deliberately even after deleting the fixture's file:
	// the reason first written here ("because the classname is still registered
	// by a file the server compiles") was false -- nothing registered it -- but
	// the constant has a second job that is real, and it is the one asserted at
	// the bottom of this test. It is the SPELLING ANCHOR for both sweeps. Delete
	// it and `kSdkArmFixture` is a needle no file in the tree contains, at which
	// point both sweeps match nothing and pass on everything.
	//
	// The sweep is over the managed SERVER tree, which is where a consumer would
	// live, and the two declarations are not in it.
	std::vector< std::string > cs;
	CollectFiles( kManagedServer, ".cs", cs );
	ASSERT_GT( cs.size(), 50u )
		<< "only " << cs.size() << " .cs found under " << kManagedServer
		<< "; the sweep is not reading the managed server tree, so the question "
		   "below would pass on nothing";

	std::string offenders;
	for ( size_t i = 0; i < cs.size(); i++ )
	{
		// STRIPPED. `///` is `//`, so a doc comment discussing the fixture --
		// and several did -- would otherwise read as a consumer. Same rule the
		// shadow sweep this replaced had to learn on its first run.
		const std::string body = ReadStripped( cs[ i ] );

		if ( body.find( kSdkArmFixture ) != std::string::npos
			|| body.find( "SourceSharpAbiSdkFixture" ) != std::string::npos )
		{
			offenders += "\n    " + cs[ i ];
		}
	}

	EXPECT_TRUE( offenders.empty() )
		<< kSdkArmFixture << " is reached for by managed server code:" << offenders
		<< "\n  Step 8a deleted the probe that spawned it. A managed consumer "
		   "of a stock-hierarchy fixture is a two-hierarchy gate coming back.";

	// The needle is not empty and it is spelled the way the tree spells it.
	// Without this, a renamed constant makes both sweeps above match nothing.
	const std::string names = ReadRaw( kEntityNames );
	ASSERT_FALSE( names.empty() ) << kEntityNames << " could not be read";
	EXPECT_NE( std::string::npos,
	           names.find( std::string( "SourceSharpAbiSdkFixture = \"" ) + kSdkArmFixture + "\"" ) )
		<< kEntityNames << " no longer spells the constant "
		   "SourceSharpAbiSdkFixture = \"" << kSdkArmFixture << "\", so the two "
		   "needles this file sweeps with are not the ones in use.";
}


//=============================================================================//
// REMOVAL 2 -- CRagdollPropAttached is out of the build
//
// The same ruling, decided the same way and for the same reason. A5 deleted
// this class's DECLARE_SERVERCLASS and left it networking as DT_Ragdoll. Under
// the ruling there were two honest endings: restore the declaration and move
// the four props to managed, or take the class out. It is out, because the
// feature is not merely unnetworked but unreachable -- CreateServerRagdollAttached
// had zero callers, its stock caller being the NPC death path, and the AI is
// not compiled. Four permanently-zero props are not worth a managed half.
//=============================================================================//

TEST( StockServerClassCensus, TheAttachedRagdollFactoriesAreGoneFromTheHeaderToo )
{
	// A declaration that survives its definition is a link error waiting for
	// the first caller to be written, which is a worse way to find this out
	// than not finding the name at all.
	//
	// THE HEADER ITSELF IS GONE NOW, which is the strong form of "declares
	// neither": game/server/physics_prop_ragdoll.h is compiled by nothing since
	// plan_server_novalve.md P5 and left the farm in P6. The scan of its text
	// for CreateServerRagdollAttached and DetachAttachedRagdoll became an
	// assertion that it cannot be read at all, anchored on a sibling that can,
	// so a tree with no game/server in it does not pass.
	const std::string sibling = ReadStripped( kServerSiblingH );
	ASSERT_NE( std::string::npos, sibling.find( kServerSiblingHAnchor ) )
		<< kServerSiblingH << " could not be read or lost its anchor; the "
		   "absence below would prove nothing about game/server";

	EXPECT_TRUE( ReadStripped( kRagdollH ).empty() )
		<< kRagdollH << " is readable from the farm again. Nothing compiles it; "
		   "if it is back, check that it did not bring the attached-ragdoll "
		   "declarations, which are defined nowhere";
}

TEST( StockServerClassCensus, NothingInTheBuildNamesAnAttachedRagdoll )
{
	// Over the whole compiled set, not only the file the class lived in --
	// because the fact that matters is that no CALLER appeared, and a caller is
	// somewhere else by definition.
	const std::string &all = CompiledServerCode();
	ASSERT_FALSE( all.empty() );

	EXPECT_EQ( 0, CountOf( all, "CRagdollPropAttached" ) );
	EXPECT_EQ( 0, CountOf( all, "CreateServerRagdollAttached" ) )
		<< "something calls CreateServerRagdollAttached, which is defined "
		   "nowhere in this build. If attached ragdolls are wanted again they "
		   "come back with the NPC death path that is their only producer";
	EXPECT_EQ( 0, CountOf( all, "DetachAttachedRagdoll" ) );
	EXPECT_EQ( 0, CountOf( all, "InitRagdollAttached" ) );
}


//=============================================================================//
// RULING 1, AS A CHECKABLE FACT
//=============================================================================//

TEST( StockServerClassCensus, NoDeclareServerclassIsLeftCommentedOut )
{
	// The owner's first ruling, in the form a reader can check. A class may not
	// have its DECLARE_SERVERCLASS commented out and be left in the build
	// networking as its base: either the props move to managed and the
	// declaration stays with them, or the class goes. Phase A5 did it twice and
	// both are now gone by the second route.
	//
	// The needle is A COMMENT WHOSE FIRST TOKEN IS THE MACRO, which is what a
	// commented-out invocation looks like, plus the "NO DECLARE_SERVERCLASS"
	// marker A5 used. Prose that merely mentions the macro is not a violation
	// -- and there is some, in exactly the files that explain why the class it
	// belonged to is no longer here. A check that could not tell those apart
	// would force the explanations to be deleted, which is the opposite of what
	// this is for.
	const std::vector< std::string > files = CompiledServerSources();
	ASSERT_NO_FATAL_FAILURE( AssertTheServerFileListParsed( files ) );

	// The headers too. A DECLARE_SERVERCLASS lives in a class body, and every
	// class body in this build that has one is in a header.
	std::vector< std::string > scan = files;
	const std::set< std::string > &closure = CompiledServerClosure();
	for ( std::set< std::string >::const_iterator it = closure.begin();
	      it != closure.end(); ++it )
	{
		if ( it->size() > 2 && it->compare( it->size() - 2, 2, ".h" ) == 0 )
			scan.push_back( *it );
	}
	ASSERT_GT( scan.size(), files.size() )
		<< "no headers came out of the closure, so this only scanned .cpp";

	std::string offenders;
	for ( size_t i = 0; i < scan.size(); i++ )
	{
		const std::string raw = ReadRaw( scan[ i ] );
		size_t at = 0;
		while ( at < raw.size() )
		{
			size_t eol = raw.find( '\n', at );
			if ( eol == std::string::npos )
				eol = raw.size();
			const std::string line = raw.substr( at, eol - at );
			at = eol + 1;

			const size_t first = line.find_first_not_of( " \t" );
			if ( first == std::string::npos )
				continue;
			if ( line.compare( first, 2, "//" ) != 0 )
				continue;
			const size_t body = line.find_first_not_of( " \t", first + 2 );
			if ( body == std::string::npos )
				continue;

			const std::string rest = line.substr( body );
			if ( rest.compare( 0, 19, "DECLARE_SERVERCLASS" ) == 0
				|| rest.compare( 0, 22, "NO DECLARE_SERVERCLASS" ) == 0 )
			{
				offenders += "\n    " + scan[ i ] + ": " + rest;
			}
		}
	}

	EXPECT_TRUE( offenders.empty() )
		<< "a DECLARE_SERVERCLASS is commented out in a file the server "
		   "compiles:" << offenders
		<< "\n  Network vars belong in managed code with C++ calling in for "
		   "get/set. A class that networks as its base has lost a capability "
		   "rather than moved one, so the two endings are: move the props and "
		   "keep the declaration, or take the class out of the build.";
}
