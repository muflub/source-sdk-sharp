//========= SourceSharp — lane H's header arms, read as source ===============//
//
// Phase 13 step 4 turns `CBaseEntity` into `using CBaseEntity = IGameEntity;`.
// Lane P did that for sourcesharp/game/server/entity_bases.h — the three sites in the
// server's precompiled prelude — and proved its arms by COMPILING the header
// twice, once each way (entity_bases_alias_test.cpp and its armed twin). That
// is the strongest shape of evidence available and it is available only for a
// header that can be included standalone. entity_bases.h can; the 58 headers
// lane H owns cannot — player.h, Sprite.h, ai_basenpc.h and the rest pull in
// the whole SDK prelude, and a stand-in for that is a fiction that would drift
// from the real thing silently.
//
// So this file does what sdk_player_cast_test.cpp and alias_mechanics_test.cpp
// do: it READS THE SOURCE. That is weaker than compiling and it is not nothing,
// because the failure modes it is aimed at are textual ones:
//
//   * a guard whose #else or #endif was lost to a merge splice — the tree has
//     been bitten by exactly this, a keep-both merge swallowing a brace;
//   * an armed arm that still names the member the flip deletes, which compiles
//     unarmed, is never compiled armed by anything in buildcpptests, and is
//     therefore a branch nobody knows is broken until flip day;
//   * a guard quietly disappearing in a later edit, which no count would catch
//     without a floor to compare against.
//
// The COMPILE-side evidence for these arms is src/survey_run with the alias
// armed: it builds every server and client TU with -DSS_CBASEENTITY_ALIAS and
// an armed arm that does not compile shows up there as a site. That is a
// measurement run by hand, not a standing test, which is precisely why the
// textual invariants below are worth pinning: they are the half that can rot
// between surveys.
//
// The arithmetic half IS checked properly, at compile time, in
// TheObjectCapsFoldsAreArithmeticallyRight — the one part of this lane that is
// a calculation rather than a judgement.
//
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAMES. FLIP DAY HAPPENED, AND HALF OF THIS
// FILE'S SITES ARE NOW IN FILES THE BUILD DOES NOT COMPILE.
//-----------------------------------------------------------------------------
//
// the-close 1/N..3/N landed the flip and retired the alias:
// public/ss_cbaseentity_alias.h is DELETED, `CBaseEntity` and `C_BaseEntity` are
// not names in the compiled tree, and the 453 dead guarded arms are gone.
// SS_CBASEENTITY_ALIAS is still DEFINED by both targets and selects nothing.
//
// MEASURED AT THIS TIP, over the 34 headers this file speaks for, from the
// build's own dependency log (`src/.ninja_deps`, the 2,035 headers the 542
// compiled objects were compiled from):
//
//     19 of the 34 are COMPILED      -- and carry ZERO alias guards, and spell
//                                       neither retired name. The flip landed.
//     15 of the 34 are NOT COMPILED  -- and carry all 23 remaining guards
//                                       between them.
//
// The correlation is total: EVERY alias guard left in this file's world is in a
// file nothing compiles. Rules R1 and R2 both bear on that, and they point the
// same way:
//
//   * A fact that reads a guard in an uncompiled file IS THE CHECK THAT CANNOT
//     FAIL. It is green, and it is green about text no compiler will ever see,
//     in either arm of a flip that already happened. Six of this file's facts
//     were passing that way -- ThePropDataArmsDropTheEflagSetters read three
//     uncompiled headers, TheKeyValueArmsDropTheBaseForward one, and
//     ShardsA.TheAbstractEntityFactoriesAreGuardedOut two uncompiled .cpp files.
//     This file's own prose already ruled on exactly this case when team.cpp
//     left the build: "a guard in a file nothing compiles protects nothing, in
//     either arm of the flip". The ruling is applied to the rest of it here.
//
//   * A fact whose site IS compiled has a LIVE SUCCESSOR, and it is stronger
//     than the guard was. "The armed arm does not name the member" becomes
//     "the member is not named AT ALL, and the answer that replaced it is there
//     with no guard around it" -- which is a property of the shipping build
//     rather than of a branch nobody compiles.
//
// SO EVERY SITE ROW IS ROUTED THROUGH THE SAME TWO-WAY GATE, and neither way is
// a skip. A row in a compiled file is checked against the live tree. A row in an
// uncompiled file is checked to STILL BE uncompiled -- it is named, its state is
// asserted, and it is recorded as measuring nothing about the build. If such a
// file is ever brought back in, the gate goes red and says so, which is the only
// honest way to keep the row.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <dirent.h>
#include <sys/stat.h>

#include <cstring>
#include <fstream>
#include <set>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as alias_mechanics_test.cpp.
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

std::string Trimmed( const std::string &s )
{
	const size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
}

std::vector< std::string > ReadLines( const std::string &strRelative )
{
	std::vector< std::string > lines;
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return lines;

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return lines;

	std::string line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		lines.push_back( line );
	}
	return lines;
}

bool Contains( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

// The whole file as one string, and the same string with every space and tab
// removed. Whitespace-insensitive matching is how the verdict tests spell a
// base clause -- "classC_World:publicC_BaseEntity" -- so that a reformat does
// not silently turn a live check into a vacuous one.
std::string FileText( const std::string &strRelative )
{
	const std::vector< std::string > lines = ReadLines( strRelative );
	std::string all;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		all += lines[ i ];
		all += "\n";
	}
	return all;
}

std::string NoSpaces( const std::string &strIn )
{
	std::string out;
	out.reserve( strIn.size() );
	for ( size_t i = 0; i < strIn.size(); i++ )
		if ( strIn[ i ] != ' ' && strIn[ i ] != '\t' )
			out += strIn[ i ];
	return out;
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

void WalkDirectory( const std::string &strDir, const std::string &strPrefix,
                    std::vector< std::string > &out )
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
		const std::string rel = strPrefix.empty() ? name : strPrefix + "/" + name;
		struct stat st;
		if ( stat( full.c_str(), &st ) != 0 )
			continue;

		if ( S_ISDIR( st.st_mode ) )
			WalkDirectory( full, rel, out );
		else if ( IsSourceFile( name ) )
			out.push_back( rel );
	}
	closedir( pDir );
}

// Every .h/.cpp/.hpp/.inl under one directory of the tree, as paths RELATIVE to
// the source root -- which is what ReadLines and FileText take.
std::vector< std::string > SourceFilesUnder( const char *pszRelativeDir )
{
	std::vector< std::string > out;
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return out;
	WalkDirectory( std::string( pszRoot ) + "/" + pszRelativeDir, pszRelativeDir, out );
	return out;
}

// A line that carries no code: blank, or a whole-line // comment. Used to
// decide whether an arm is EMPTY.
bool IsBlankOrComment( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t.empty() || t.compare( 0, 2, "//" ) == 0;
}

// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and this is not tidiness.
//
// Every armed arm lane H writes explains ITSELF — "IsBaseCombatCharacter() is
// not askable of IGameEntity" sits directly above `return NULL;`. Matching raw
// text, the rule "the armed arm must not name the deleted member" then reads
// the member's name out of the sentence saying it is gone, and reports the
// correct arm as wrong. Caught in the act here, and it is the same failure
// TheResolverListIsHonest was found to have: a file's own prose carrying the
// token being searched for. The comment-carrying half of an arm is documentation;
// only the code half is the claim.
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
			// Rest of THIS line only.
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

//-----------------------------------------------------------------------------
// One #ifndef SS_CBASEENTITY_ALIAS / #else / #endif region, split into arms.
//-----------------------------------------------------------------------------
struct Guard_t
{
	std::string strFile;
	int nLine;                            // 1-based, the #ifndef
	std::vector< std::string > unarmed;   // between #ifndef and #else
	std::vector< std::string > armed;     // between #else and #endif
	bool bHasElse;
	bool bClosed;
	// Which polarity opened it. Needed because an #else-less guard means the
	// OPPOSITE thing in the two polarities: `#ifndef` with no #else deletes SDK
	// text when armed, `#ifdef` with no #else ADDS text when armed. Without
	// this the two are indistinguishable to the checks below and one of them
	// has to be waived, which is how a swallowed #else gets through.
	bool bArmedFirst;

	Guard_t() : nLine( 0 ), bHasElse( false ), bClosed( false ), bArmedFirst( false ) {}

	// CODE ONLY — see StripComments above for why the prose must not be matched.
	std::string UnarmedText() const
	{
		std::string s;
		for ( size_t i = 0; i < unarmed.size(); i++ ) s += unarmed[ i ] + "\n";
		return StripComments( s );
	}
	std::string ArmedText() const
	{
		std::string s;
		for ( size_t i = 0; i < armed.size(); i++ ) s += armed[ i ] + "\n";
		return StripComments( s );
	}
	bool ArmedHasCode() const
	{
		for ( size_t i = 0; i < armed.size(); i++ )
			if ( !IsBlankOrComment( armed[ i ] ) ) return true;
		return false;
	}
	bool UnarmedHasCode() const
	{
		for ( size_t i = 0; i < unarmed.size(); i++ )
			if ( !IsBlankOrComment( unarmed[ i ] ) ) return true;
		return false;
	}
};

// BOTH POLARITIES, and the difference is not cosmetic.
//
// `#ifndef SS_CBASEENTITY_ALIAS` puts the SDK text first and the armed answer
// after the #else; `#ifdef SS_CBASEENTITY_ALIAS` puts the armed answer first.
// Lane H uses the second wherever the armed arm is the SHORT one — the
// To<Class> resolvers, whose armed body is a single `return NULL;` against a
// dozen lines of casts. A scanner that knew only the first spelling would read
// those four guards as absent, report a smaller set, and still be green: the
// floor below is exactly the check that would have hidden it.
bool IsGuardOpen( const std::string &strLine, bool *pbArmedFirst )
{
	const std::string t = Trimmed( strLine );
	if ( t == "#ifndef SS_CBASEENTITY_ALIAS"
		|| t == "#if !defined( SS_CBASEENTITY_ALIAS )"
		|| t == "#if !defined(SS_CBASEENTITY_ALIAS)" )
	{
		*pbArmedFirst = false;
		return true;
	}
	if ( t == "#ifdef SS_CBASEENTITY_ALIAS"
		|| t == "#if defined( SS_CBASEENTITY_ALIAS )"
		|| t == "#if defined(SS_CBASEENTITY_ALIAS)" )
	{
		*pbArmedFirst = true;
		return true;
	}
	return false;
}

// Any other conditional, so nesting inside an arm does not steal its #else.
bool IsAnyConditionalOpen( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t.compare( 0, 3, "#if" ) == 0;
}

bool IsEndif( const std::string &strLine ) { return Trimmed( strLine ).compare( 0, 6, "#endif" ) == 0; }
bool IsElse( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t == "#else" || t.compare( 0, 5, "#elif" ) == 0;
}

std::vector< Guard_t > GuardsInLines( const std::vector< std::string > &lines,
                                     const std::string &strFile )
{
	std::vector< Guard_t > out;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		bool bArmedFirst = false;
		if ( !IsGuardOpen( lines[ i ], &bArmedFirst ) )
			continue;

		Guard_t g;
		g.strFile = strFile;
		g.nLine = (int)i + 1;
		g.bArmedFirst = bArmedFirst;

		int nDepth = 0;
		bool bInElse = false;
		size_t j = i + 1;
		for ( ; j < lines.size(); j++ )
		{
			const std::string &L = lines[ j ];
			if ( IsAnyConditionalOpen( L ) ) { nDepth++; }
			else if ( IsEndif( L ) )
			{
				if ( nDepth == 0 ) { g.bClosed = true; break; }
				nDepth--;
			}
			else if ( IsElse( L ) && nDepth == 0 )
			{
				g.bHasElse = true; bInElse = true; continue;
			}

			// Which side of the #else is the ARMED arm depends on the polarity
			// of the opening directive, not on its position.
			const bool bThisIsArmed = ( bInElse != bArmedFirst );
			if ( bThisIsArmed ) g.armed.push_back( L );
			else                g.unarmed.push_back( L );
		}
		out.push_back( g );
		i = j;
	}
	return out;
}

std::vector< Guard_t > GuardsIn( const std::string &strFile )
{
	return GuardsInLines( ReadLines( strFile ), strFile );
}

//-----------------------------------------------------------------------------
// WHAT THE BUILD COMPILES, asked two ways because the two file kinds have two
// different authorities.
//
// A .cpp is answered by what the build compiles -- the union of the sources of
// server_sourcesharp, client_sourcesharp and sourcesharp (libmanaged_host),
// read through cmake_graph.h from compile_commands.json, which IS the compile
// command line, cross-checked against each target's CMakeLists source list.
// There is no arguing with it. (Until Phase 6b this was VPC's ninja graph,
// _vpc_/ninja/sdk_csharp_debug.ninja.) A header is reached transitively and is
// not listed there, so the authority for a header is the NAME: `CBaseEntity` and
// `C_BaseEntity` were retired in 0bc727ed9 and a file that still spells one in
// CODE cannot be in any compiled TU's include graph, because that TU would not
// compile. That test is SOUND ONE WAY ONLY -- an uncompiled header that was
// tidied up spells neither name -- so the header partition is also written down
// explicitly below and checked, rather than derived.
//-----------------------------------------------------------------------------
const std::set< std::string > &BuiltSources()
{
	// cmake_graph throws on a missing, empty or stale input, which gtest
	// reports as a failure of the calling test; the cache is filled only on a
	// complete read.
	static std::set< std::string > s_built;
	if ( s_built.empty() )
	{
		const char *const kTargets[] = { cmake_graph::kServer, cmake_graph::kClient, cmake_graph::kHost };
		std::set< std::string > built;
		for ( size_t i = 0; i < sizeof( kTargets ) / sizeof( kTargets[ 0 ] ); i++ )
		{
			const std::set< std::string > t = cmake_graph::CompiledSources( kTargets[ i ] );
			built.insert( t.begin(), t.end() );
		}
		s_built.swap( built );
	}
	return s_built;
}

// By the farm-relative path, exactly -- the spelling the CMakeLists lists use.
bool BuildCompiles( const std::string &strRelative )
{
	return BuiltSources().count( strRelative ) != 0;
}

// The file's code, comments stripped -- see StripComments for why every match
// in this file is over code and never over prose.
std::string CodeOf( const std::string &strRelative )
{
	return StripComments( FileText( strRelative ) );
}

// A retired entity name, spelled in CODE. Prose still says CBaseEntity all over
// this tree and that is deliberate (0bc727ed9: a blind rename destroys facts).
bool SpellsRetiredEntityName( const std::string &strRelative )
{
	const std::string code = CodeOf( strRelative );
	return Contains( code, "CBaseEntity" ) || Contains( code, "C_BaseEntity" );
}

size_t AliasGuardCount( const std::string &strRelative )
{
	return GuardsIn( strRelative ).size();
}

//-----------------------------------------------------------------------------
// THE LANE-H HEADERS THIS FILE SPEAKS FOR.
//
// Listed rather than discovered, so that a header LOSING its guard is a
// failure here rather than a silently shorter list — the tree has already
// learned that a green suite does not prove a mechanical edit kept everything
// it was supposed to keep.
//-----------------------------------------------------------------------------
struct Site_t
{
	const char *pszFile;
	const char *pszMustBeInUnarmed;  // the member the flip deletes
	const char *pszMustNotBeInArmed; // ...which the armed arm must not name
};

// Cluster 5 -- CBaseEntity::IsServer() -- is retired: its last site,
// ss_physics_collisionevent.h, was deleted with native's collision handler
// (plan_physics_bones_native_zero 2.1); player.h and saverestore_stringtable.h
// left the farm in plan_server_novalve.md P6.

// Cluster 7 -- the two animating roots. CBaseAnimating and C_BaseAnimating
// derive straight from CBaseEntity, so everything they ask of it goes with the
// alias: the flag word FL_ONFIRE / FL_DISSOLVING live in, the model pointer and
// the dynamic-model state GetModelPtr() consults, the GetBaseAnimating()
// downcast a script handle used to reach, and C_BaseEntity's UpdateVisibility()
// virtual -- whose disappearance makes OVERRIDE itself a hard error rather than
// a missing member.
//
// The needles avoid the substring trap the retired IsServer sites recorded. `GetModel()`
// and not `GetModel`, because `GetModelPtr(` -- the function these arms are
// inside -- contains the shorter one; `GetFlags() & FL_ONFIRE` in the unarmed
// column so the fire guard and the dissolve guard are told apart.
//
// The six game/server/baseanimating.h rows are retired: that header is compiled
// by nothing since plan_server_novalve.md P5 and left the farm in P6.
const Site_t kAnimatingRootSites[] = {
	{ "game/client/c_baseanimating.h", "UpdateVisibility() OVERRIDE", "OVERRIDE" },
	{ "game/client/c_baseanimating.h", "GetFlags() & FL_ONFIRE",      "GetFlags()" },
};

//-----------------------------------------------------------------------------
// THE LANE-H HEADERS, PARTITIONED. This was one list, kLaneHHeaders, and every
// fact below looped over the whole of it demanding a well-formed guard.
//
// It became two lists when the tree became two things: this compiled half, and
// the uncompiled lane-H headers. The uncompiled half, and the facts that read
// it, went when the farm stopped carrying stock files the build does not
// compile; what is left is the compiled half, which the site gate consults.
//
// MEASURED, not assumed -- from the build's own dependency log at this tip:
//
//     python3 - <<'EOF'   # src/.ninja_deps, ninja's deps log v4
//     ...the 2,035 headers the 542 compiled objects were compiled from...
//     EOF
//
// and cross-checked against the property the build itself enforces: a compiled
// file cannot spell `CBaseEntity` or `C_BaseEntity`, because neither is a name.
//-----------------------------------------------------------------------------

// COMPILED, AND FLIPPED. Zero alias guards, neither retired name, the armed
// answer standing unconditionally. These are the files the facts below measure.
const char *kLiveLaneHHeaders[] = {
	// game/server/player.h, fogcontroller.h, ai_basenpc.h, ai_navigator.h and
	// baseanimating.h, and game/shared/saverestore_stringtable.h, stood here;
	// they are compiled by nothing since plan_server_novalve.md P5 and left the
	// farm in P6, and their site rows went with them.
	"game/client/c_baseanimating.h",
	"game/shared/Sprite.h",
	"game/client/c_basecombatcharacter.h",
	"sourcesharp/game/shared/physicsowner.h",
	"sourcesharp/game/server/anyentity.h",
	"game/shared/beam_shared.h",
	"sourcesharp/game/shared/movement_ground.h",
	// game/server/entityfilter.h stood here; it was in no compiled object (ninja
	// deps) and was deleted after the server gc cull, with its facts below.
	// game/server/EntityDissolve.h and EntityFlame.h stood here; neither DLL
	// compiles them any more, and no site row below names either.
	// game/shared/env_detail_controller.h stood here; its one includer,
	// detailobjectsystem.cpp, left client.so in C7 cluster 10, and the header
	// left the farm with it (make sdk-census: unused).
	"game/shared/gamerules.h",
};

// Is this path one of the files the flip actually landed in? Every site row
// below is routed through here, and the answer is never "skip": a row in a live
// file is measured against the tree, a row in a dead-stock file is measured
// against the claim that it is still dead stock.
bool IsLiveLaneHFile( const std::string &strFile )
{
	for ( size_t i = 0; i < sizeof( kLiveLaneHHeaders ) / sizeof( kLiveLaneHHeaders[ 0 ] ); i++ )
		if ( strFile == kLiveLaneHHeaders[ i ] )
			return true;
	// .cpp rows (lane shards-a) answer from what the build compiles, which is
	// the compile command line rather than a list this test keeps.
	if ( EndsWith( strFile, ".cpp" ) )
		return BuildCompiles( strFile );
	return false;
}

// The gate. Returns true when the row was measured against a live file.
//
//   live file  -- `pszGone` must be absent from the CODE. Not "absent from an
//                 armed arm": absent, because there are no arms. The caller
//                 then adds the live-successor half.
//   dead stock -- the row cannot be measured. It is asserted to still be dead
//                 stock, so it cannot quietly become a vacuous pass.
bool CheckSiteAgainstTheTree( const std::string &strFile, const char *pszGone )
{
	if ( !IsLiveLaneHFile( strFile ) )
	{
		const bool bStillDead =
			SpellsRetiredEntityName( strFile ) || AliasGuardCount( strFile ) > 0;
		EXPECT_TRUE( bStillDead )
			<< strFile << " is on the dead-stock list -- nothing compiles it, so "
			   "this row measures nothing -- but it now spells no retired entity "
			   "name and carries no alias guard. Either it came into the build "
			   "(move it to kLiveLaneHHeaders and this row starts measuring the "
			   "tree again) or it was tidied up (say so here).";
		return false;
	}

	const std::string code = CodeOf( strFile );
	EXPECT_FALSE( code.empty() ) << strFile << " read as empty";
	EXPECT_FALSE( Contains( code, pszGone ) )
		<< strFile << " still says '" << pszGone << "' in code. The flip landed "
		   "unconditionally -- there is no arm for this to be safe in, and "
		   "`CBaseEntity` is not a name.";
	return true;
}

// AnyGuardApplies STOOD HERE AND IS DELETED WITH ITS REASON. It answered "does
// some guard in this file have `pszIn` in its unarmed arm and not `pszOut` in
// its armed one", and every site fact in this file was one loop over it.
//
// There are no guards left in any file this test measures -- the flip landed and
// the dead arms went with it -- so the helper could only ever answer false for a
// compiled file and true for an uncompiled one. That is the exact inversion of
// what it was for: it would have graded the finished tree as broken and a file
// nobody builds as correct. CheckSiteAgainstTheTree replaces it, and the arm
// comparison it used to make is now a plain absence over the code plus, per
// cluster, the text that replaced the deleted member.
//
// Guard_t and GuardsIn SURVIVE it and are still load-bearing: "zero guards in
// the compiled half" is a measurement only because the scanner can still find
// one, which the two HeaderAliasScanner fixtures prove on literals.

}  // namespace

TEST( HeaderAlias, TheAnimatingRootEntityAsksAreGone )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	// The client root is compiled, so every row here measures the tree. (The
	// server root's rows are retired: game/server/baseanimating.h is compiled
	// by nothing since plan_server_novalve.md P5 and left the farm in P6.) The asks --
	// the flag word, the model pointer, the dynamic-model state, the
	// GetBaseAnimating() downcast and C_BaseEntity's UpdateVisibility() virtual
	// -- went with CBaseEntity rather than being guarded away from it.
	int nLive = 0;
	for ( size_t i = 0; i < sizeof( kAnimatingRootSites ) / sizeof( kAnimatingRootSites[ 0 ] ); i++ )
	{
		const Site_t &S = kAnimatingRootSites[ i ];
		if ( CheckSiteAgainstTheTree( S.pszFile, S.pszMustBeInUnarmed ) )
			nLive++;
	}
	EXPECT_EQ( 2, nLive ) << "the client animating root left the build. It is in "
		"the compiled set at this tip and every row here depends on it.";

	// The live successor these absences leave behind: the root still derives
	// from the entity interface and still owns the CNetworkVars that made it a
	// root. Without this, "the asks are gone" is equally true of an empty file.
	static const char *kRoots[] = { "game/client/c_baseanimating.h" };
	for ( size_t i = 0; i < sizeof( kRoots ) / sizeof( kRoots[ 0 ] ); i++ )
	{
		const std::string code = CodeOf( kRoots[ i ] );
		EXPECT_TRUE( Contains( code, "IGameEntity" ) )
			<< kRoots[ i ] << " no longer names IGameEntity.";
		EXPECT_TRUE( Contains( code, "CNetworkVar(" ) )
			<< kRoots[ i ] << " no longer declares a CNetworkVar. It is the root "
			   "of the animating hierarchy's networking; if it owns none, the "
			   "NetworkStateChanged pair below serves nothing.";
	}
}

//-----------------------------------------------------------------------------
// The NetworkStateChanged pair, and this one is checked in the OPPOSITE
// polarity to every other site above.
//
// Those ask "the member the flip deletes is in the unarmed arm and gone from
// the armed one". This pair is text that must exist ONLY when armed, and the
// unarmed half is the load-bearing half: an empty NetworkStateChanged visible
// to the shipping build would shadow the real one CBaseEntity supplies and
// every CNetworkVar in the whole animating hierarchy -- players, NPCs, weapons,
// props, ragdolls -- would stop marking itself dirty. That is not a compile
// error anywhere. It is a server that changes state and never sends it, which
// is the exact silent failure chain_state_notify_test.cpp was written for.
//
// So: present in the armed arm of some guard, ABSENT from that guard's unarmed
// arm, and absent from the file outside every guard. The third clause is what
// stops the pair being moved out of its guard by a later edit and this test
// staying green on the other two.
//-----------------------------------------------------------------------------
// The three clauses above, applied to one file. Shared rather than copied
// because a second copy of a check drifts from the first, and the two callers
// below want exactly the same rule for different reasons -- one for a hierarchy
// ROOT whose pair reaches nine headers, one for a LEAF whose pair reaches only
// its own ten members. The rule does not care which.
//
// READ THE ASSERTIONS, NOT THE NAME. This helper was
// CheckNetworkStateChangedPairIsArmedOnly and it required the pair to be inside
// a guard's ARMED arm and nowhere else, because UNARMED the pair would shadow
// CBaseEntity's real NetworkStateChanged and the whole hierarchy would stop
// marking itself dirty.
//
// THE POLARITY IS THE OTHER WAY ROUND NOW, and the reason is the flip itself:
// there is no CBaseEntity to shadow. The base is IGameEntity, which declares no
// NetworkStateChanged at all -- asserted below rather than assumed -- so the
// pair is not a husk that must be hidden from the shipping build, it is the
// ONLY declaration the hierarchy has, and it must be there UNCONDITIONALLY.
//
// The hazard is inverted with it. It used to be "the pair leaks out of its
// guard"; it is now "the pair is put back behind a guard, or dropped", either
// of which stops every CNetworkVar below it compiling.
//
// Two halves, because one alone is half a fact: the pair is present and
// unguarded, AND the class still owns CNetworkVars for it to serve.
static void CheckNetworkStateChangedPairIsUnconditional( const char *pszFile )
{
	const std::string f = pszFile;
	const std::vector< std::string > lines = ReadLines( f );
	ASSERT_GT( lines.size(), 10u ) << f << " is missing or unreadable";

	const char *kPairA = "void NetworkStateChanged() {}";
	const char *kPairB = "void NetworkStateChanged( void *pVar ) {}";

	const std::string code = StripComments( FileText( f ) );
	EXPECT_TRUE( Contains( code, kPairA ) && Contains( code, kPairB ) )
		<< f << ": the NetworkStateChanged pair is not there. Its base is "
		   "IGameEntity, which has none, so every CNetworkVar this class owns "
		   "stops compiling.";

	// Not inside an alias guard -- in either arm. An `#ifdef` arm is never not
	// taken (both targets define the macro) and an `#ifndef` arm is never taken;
	// the second one silently deletes the pair from the build.
	const std::vector< Guard_t > guards = GuardsIn( f );
	for ( size_t g = 0; g < guards.size(); g++ )
	{
		const Guard_t &G = guards[ g ];
		EXPECT_FALSE( Contains( G.ArmedText(), "NetworkStateChanged" )
			|| Contains( G.UnarmedText(), "NetworkStateChanged" ) )
			<< f << ":" << G.nLine << " puts NetworkStateChanged back behind an "
			   "SS_CBASEENTITY_ALIAS guard. That macro selects nothing; the arm "
			   "is either always taken or never, and never is a class whose "
			   "CNetworkVars do not compile.";
	}

	// And nowhere conditional at all: every declaration in the file is at the
	// same nesting the class is. Counted rather than span-checked, for the
	// reason the old helper gave -- span arithmetic that gets an #else wrong
	// passes on a file it never read.
	size_t nInFile = 0, nInsideAGuard = 0;
	for ( size_t k = 0; k < lines.size(); k++ )
		if ( Contains( StripComments( lines[ k ] ), "void NetworkStateChanged" ) )
			nInFile++;
	for ( size_t g = 0; g < guards.size(); g++ )
	{
		const Guard_t &G = guards[ g ];
		for ( size_t k = 0; k < G.armed.size(); k++ )
			if ( Contains( StripComments( G.armed[ k ] ), "void NetworkStateChanged" ) )
				nInsideAGuard++;
		for ( size_t k = 0; k < G.unarmed.size(); k++ )
			if ( Contains( StripComments( G.unarmed[ k ] ), "void NetworkStateChanged" ) )
				nInsideAGuard++;
	}
	EXPECT_EQ( (size_t)2, nInFile ) << f << ": expected exactly the two-overload "
		"pair, found " << nInFile << ".";
	EXPECT_EQ( (size_t)0, nInsideAGuard ) << f << ": " << nInsideAGuard
		<< " of the declarations are inside an alias guard.";

	// The half that stops this passing over a class with nothing to network.
	EXPECT_TRUE( Contains( code, "CNetworkVar(" ) || Contains( code, "CNetworkVector(" )
		|| Contains( code, "CNetworkHandle(" ) || Contains( code, "CNetworkQAngle(" ) )
		<< f << " declares no CNetworkVar of any kind, so the pair above serves "
		   "nothing. Either the class's networked state moved (say so, the way "
		   "TheWorldCarriesNoNetworkStateChangedPairBecauseItDeclaresNoNetworkVars "
		   "does for CWorld) or this file is not what this fact is about.";
}

TEST( HeaderAlias, TheAnimatingRootsOwnTheNetworkStateChangedPairUnconditionally )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	// IGameEntity really has no NetworkStateChanged -- the premise both roots
	// rest on, read from the interface rather than argued.
	const std::string iface = StripComments( FileText( "public/igameentity.h" ) );
	ASSERT_FALSE( iface.empty() ) << "could not read public/igameentity.h";
	EXPECT_FALSE( Contains( iface, "NetworkStateChanged" ) )
		<< "IGameEntity has acquired a NetworkStateChanged. If it belongs there, "
		   "these two roots should stop declaring their own pair and this fact "
		   "should say so.";

	// The server root's row is retired: game/server/baseanimating.h is compiled
	// by nothing since plan_server_novalve.md P5 and left the farm in P6.
	CheckNetworkStateChangedPairIsUnconditional( "game/client/c_baseanimating.h" );
}

//-----------------------------------------------------------------------------
// CWorld OWNS NO PAIR ANY MORE, AND THE FACT IS INVERTED RATHER THAN DELETED.
//
// This read `CheckNetworkStateChangedPairIsArmedOnly( "game/server/world.h" )`,
// and what it protected was real: world.h is included by five compiled server
// files, CWorld declared ten CNetworkVar/CNetworkVector members, and an empty
// NetworkStateChanged pair leaking into the SHIPPING build would have shadowed
// CBaseEntity's real one so DT_WORLD stopped marking itself dirty -- a client
// receiving a world whose bounds never update, with no compile error on either
// side.
//
// THE SUBJECT IS GONE, NOT THE HAZARD'S SHAPE. plan_gamedll_after.md Part B2
// moved worldspawn's ten networked values out of members entirely: they are
// declared in SourceSharp.Shared.WorldNetState and live in a blob past the
// object, and CWorld marks itself dirty through CBaseEntity's own
// NetworkStateChanged, which nothing in world.h shadows.
//
// So the fact asserts the OTHER direction, and it is the stronger one: the
// husk pair must not come BACK. A pair re-added to this header today would
// shadow the real one for the whole class and nothing below it would notice,
// because there is no CNetworkVar left whose compile would fail to complain --
// which is exactly what makes re-adding it plausible and silent.
//
// It also pins WHY there is nothing to protect: zero CNetworkVar declarations.
// Asserting the absent pair alone would go on passing the day somebody
// re-declares a member and forgets the rest of the port.
//-----------------------------------------------------------------------------
TEST( HeaderAlias, TheWorldCarriesNoNetworkStateChangedPairBecauseItDeclaresNoNetworkVars )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	const std::vector< std::string > lines = ReadLines( "sourcesharp/game/server/ss_world.h" );
	ASSERT_FALSE( lines.empty() ) << "sourcesharp/game/server/ss_world.h is missing or unreadable -- "
		"both counts below would read zero having read nothing";

	// The ANCHOR, so "found none" is a measurement and not an empty read: the
	// class is still declared in this file.
	//
	// IT NAMED THE BASE AND THE BASE IS GONE (plan_cworld_trace.md T3).
	// worldspawn is its own hierarchy now, and since the follow-up to T3 its
	// ONE base is IServerEntity: the engine reaches its IServerNetworkable and
	// its ICollideable by pointer, so the world HOLDS one of each
	// (m_Networkable, m_Collideable) rather than inheriting them. Either way
	// "classCWorld:publicCBaseEntity" matches nothing, and an anchor that
	// matches nothing is exactly how the count below becomes a vacuous zero.
	// Re-pointed at the head as it reads.
	const std::string tight = NoSpaces( FileText( "sourcesharp/game/server/ss_world.h" ) );
	ASSERT_TRUE( Contains( tight, "classCWorld:publicIServerEntity" ) )
		<< "sourcesharp/game/server/ss_world.h no longer declares CWorld at all; "
		   "this test is reading a file it does not recognise";

	size_t nNetVars = 0;
	size_t nPairs   = 0;

	for ( size_t k = 0; k < lines.size(); k++ )
	{
		const std::string bare = StripComments( lines[ k ] );

		// CNetworkVar( T, m_x ) and CNetworkVector( m_x ) -- the two spellings
		// CWorld used. Comment-stripped, because the block above this test's
		// subject describes both of them by name.
		if ( Contains( bare, "CNetworkVar(" ) || Contains( bare, "CNetworkVector(" )
			|| Contains( bare, "CNetworkVar (" ) || Contains( bare, "CNetworkVector (" ) )
		{
			nNetVars++;
		}

		if ( Contains( bare, "void NetworkStateChanged" ) )
			nPairs++;
	}

	EXPECT_EQ( nNetVars, (size_t)0 ) << "sourcesharp/game/server/ss_world.h declares " << nNetVars
		<< " CNetworkVar(s). worldspawn's networked state is C#'s -- "
		   "SourceSharp.Shared.WorldNetState -- and a member here is a value the "
		   "managed declaration does not know about and no prop addresses.";

	EXPECT_EQ( nPairs, (size_t)0 ) << "sourcesharp/game/server/ss_world.h declares " << nPairs
		<< " NetworkStateChanged overload(s). There is no CNetworkVar for one to "
		   "serve, and an empty pair on this class shadows CBaseEntity's real one "
		   "for every write CWorld makes -- so SetStartDark would stop marking the "
		   "edict changed and the client would never receive the value.";
}

//-----------------------------------------------------------------------------
// CWorld is a LEAF, and the fact SURVIVES its original reason with a new one.
//
// It was written to bound the armed NetworkStateChanged pair above: a pair on a
// hierarchy root reaches every class below it, and this one reached nothing
// because nothing derives from CWorld. That pair is gone
// (plan_gamedll_after.md Part B2) and the leafness now bounds two other things,
// both of them the shim's:
//
//   * CWorld::operator new over-allocates by SourceSharp_WorldBlobOffset(),
//     which is ALIGN_VALUE( sizeof( CWorld ), 16 ). A derived class is longer,
//     so its members would start inside the blob its own props address --
//     silently, because the allocation would still be big enough and the
//     overlap would read as data. The standalone hierarchy pays a headroom cost
//     for exactly this reason (its offset clears the LARGEST shell on it);
//     CWorld pays none, and may not while this stays true.
//   * CWorld::GetServerClass answers the ONE managed table
//     shell_world_nettable.cpp built. A derived class would inherit it and
//     network as a world.
//
// Scanned over the server tree rather than asserted about world.h alone,
// because a derivation would be written in the DERIVED class's file, which
// world.h cannot see.
//-----------------------------------------------------------------------------
TEST( HeaderAlias, NothingDerivesFromCWorld )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	const std::vector< std::string > files = SourceFilesUnder( "game" );
	ASSERT_GT( files.size(), 100u ) << "only " << files.size() << " files walked "
		"under game/ -- that is a broken walk, not a clean tree";

	// THE ANCHOR IS TAKEN THROUGH THE SCAN'S OWN LOOP, and it used to be a
	// separate read of one file. It read game/client/c_world.h for
	// "classC_World:publicC_BaseEntity" -- and step 5e took C_World off
	// C_BaseEntity (plan_entity.md §3.11 as amended: both worlds come off the
	// SDK hierarchy), so the anchor named a derivation the tree no longer has.
	//
	// Replacing it with another single file's derivation would only move the
	// same problem. What the anchor is FOR is proving that this loop, over this
	// walk, with this spelling of a base clause, can come back non-empty -- so
	// it counts ":publicICollideable" in the same pass. That is a clause the
	// worlds themselves carry on both sides (CWorldCollideable and
	// CWorldClientCollideable), so it cannot go away while there is a world to
	// test, and it is found the same way the subject would be.
	std::vector< std::string > derived;
	std::vector< std::string > derivedClient;
	int nAnchorHits = 0;

	for ( size_t i = 0; i < files.size(); i++ )
	{
		const std::string text = NoSpaces( FileText( files[ i ] ) );
		if ( text.empty() )
			continue;

		if ( Contains( text, ":publicICollideable" ) )
			nAnchorHits++;

		// Both spellings of a base clause. Not "CWorld" alone: 28 files name
		// the type without deriving from it.
		if ( Contains( text, ":publicCWorld" ) || Contains( text, ":privateCWorld" )
			|| Contains( text, ":protectedCWorld" ) )
		{
			derived.push_back( files[ i ] );
		}

		// AND THE CLIENT'S, WHICH IS NEW WITH 5e AND HAS THE SAME TWO REASONS.
		// C_World::operator new over-allocates by SourceSharp_WorldBlobOffset(),
		// which on that side is ALIGN_VALUE( sizeof( C_World ), 16 ); and
		// C_World::GetClientClass answers the one managed table
		// shell_world_nettable_client.cpp built.
		if ( Contains( text, ":publicC_World" ) || Contains( text, ":privateC_World" )
			|| Contains( text, ":protectedC_World" ) )
		{
			derivedClient.push_back( files[ i ] );
		}
	}

	ASSERT_GT( nAnchorHits, 0 ) << "the scan found no ':publicICollideable' "
		"anywhere under game/, so it would report 'no derivations' having read "
		"nothing. This is a broken walk, not a clean tree.";

	std::string named;
	for ( size_t i = 0; i < derived.size(); i++ )
		named += ( i ? ", " : "" ) + derived[ i ];

	EXPECT_TRUE( derived.empty() ) << derived.size() << " class(es) derive from "
		"CWorld (" << named << "). CWorld's blob offset is sized to sizeof( CWorld ) "
		"exactly, on the assumption that it is a leaf: a longer derived class puts "
		"its own members inside the bytes worldspawn's props address, with no "
		"compile error and no crash -- the overlap reads as data. It would also "
		"inherit GetServerClass and network as a world. GIVE THE HIERARCHY "
		"HEADROOM THE WAY SourceSharp_StandaloneBlobOffset DOES, or do not derive.";

	std::string namedClient;
	for ( size_t i = 0; i < derivedClient.size(); i++ )
		namedClient += ( i ? ", " : "" ) + derivedClient[ i ];

	EXPECT_TRUE( derivedClient.empty() ) << derivedClient.size() << " class(es) "
		"derive from C_World (" << namedClient << "). The client's world is a leaf "
		"for the same two reasons the server's is: its blob offset is sized to "
		"sizeof( C_World ) exactly, so a longer derived class puts its own members "
		"inside the bytes worldspawn's props address; and it would inherit "
		"GetClientClass and decode as a world.";
}

//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. This was NoOwnerWrapperArmKeepsTheSdkArm:
// no guard in physicsowner.h or anyentity.h may leave a `pBase->` ask in its
// ARMED arm, because armed there is no SDK entity to ask.
//
// THE ARM WENT AND pBase STAYED, RETYPED. physicsowner.h:117 now declares
// `IGameEntity *pBase` -- the field was not deleted, it was pointed at the
// interface -- so the successor rule is sharper than the old one and is checked
// over the whole file rather than over arms that no longer exist:
//
//     EVERY `pBase->` ASK IN EITHER FILE MUST BE ONE OF IGameEntity's SIX.
//
// Six, read out of public/igameentity.h rather than listed here, so that the
// day the interface grows or loses a member this rule follows it instead of
// going quietly wrong. An ask outside that set does not compile -- which is
// what makes this a real rule rather than a style, and what the guards used to
// be standing in for.
//-----------------------------------------------------------------------------
TEST( HeaderAlias, EveryOwnerWrapperAskIsOneOfTheInterfaceMembers )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	// The interface's own member list, taken from its pure virtuals.
	const std::vector< std::string > iface = ReadLines( "public/igameentity.h" );
	ASSERT_FALSE( iface.empty() ) << "could not read public/igameentity.h";

	std::vector< std::string > members;
	for ( size_t i = 0; i < iface.size(); i++ )
	{
		const std::string t = Trimmed( StripComments( iface[ i ] ) );
		if ( t.compare( 0, 8, "virtual " ) != 0 )
			continue;
		if ( t.find( "= 0;" ) == std::string::npos )
			continue;
		const size_t paren = t.find( '(' );
		if ( paren == std::string::npos )
			continue;
		size_t start = t.find_last_of( " \t*&", paren );
		if ( start == std::string::npos )
			continue;
		members.push_back( t.substr( start + 1, paren - start - 1 ) );
	}
	ASSERT_EQ( 6u, members.size() )
		<< "public/igameentity.h no longer declares exactly six pure virtuals. "
		   "The membership rule is what bounds this check; if the interface grew, "
		   "read igameentity.h's rule before raising this number.";

	const char *kFiles[] = { "sourcesharp/game/shared/physicsowner.h", "sourcesharp/game/server/anyentity.h" };
	int nAsks = 0;
	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); i++ )
	{
		const std::string code = CodeOf( kFiles[ i ] );
		ASSERT_FALSE( code.empty() ) << kFiles[ i ] << " read as empty";

		// The field is still there and still points at the interface -- without
		// this, "no bad ask" is equally true of a file that lost pBase.
		EXPECT_TRUE( Contains( CodeOf( kFiles[ i ] ), "pBase" ) )
			<< kFiles[ i ] << " no longer names pBase at all.";

		for ( size_t at = code.find( "pBase->" ); at != std::string::npos;
		      at = code.find( "pBase->", at + 1 ) )
		{
			const size_t nameAt = at + strlen( "pBase->" );
			size_t end = nameAt;
			while ( end < code.size()
				&& ( isalnum( (unsigned char)code[ end ] ) || code[ end ] == '_' ) )
				end++;
			const std::string ask = code.substr( nameAt, end - nameAt );
			nAsks++;

			bool bMember = false;
			for ( size_t m = 0; m < members.size(); m++ )
				bMember = bMember || ( members[ m ] == ask );

			EXPECT_TRUE( bMember )
				<< kFiles[ i ] << " asks pBase for `" << ask
				<< "`, which is not one of IGameEntity's six. pBase IS an "
				   "IGameEntity* now (physicsowner.h:117) -- there is no SDK "
				   "entity behind it and no guard left to put the ask in.";
		}
	}
	EXPECT_GT( nAsks, 1 ) << "no `pBase->` ask found in either wrapper, so the "
		"rule above swept nothing. These two files are where the SDK arms lived; "
		"if every ask has gone, say so here.";

	// physicsowner.h's declaration, pinned: this is the retype that replaced
	// fifteen guarded SDK arms, and it is what makes the sweep above a rule
	// about a real pointer rather than about a name.
	EXPECT_TRUE( Contains( NoSpaces( CodeOf( "sourcesharp/game/shared/physicsowner.h" ) ),
		"IGameEntity*pBase;" ) )
		<< "game/shared/physicsowner.h no longer declares `IGameEntity *pBase`.";
}

//-----------------------------------------------------------------------------
// 3. THE ARITHMETIC, CHECKED RATHER THAN EYEBALLED.
//
// Cluster 1's rule is a calculation: armed, the base term is 0, so
// `(0 & ~A) | B` folds to `B` and `0 | B` folds to `B`. Ten sites were folded
// BY HAND, and a hand-folded bitmask is exactly the kind of thing that is
// wrong in one place and looks right in ten.
//
// So each row below writes the SDK's expression as a function of the base
// term, and the armed arm as a constant, and requires them to agree at base=0.
// The left-hand sides are transcribed from the unarmed arms and the right-hand
// sides from the armed arms; a transcription error on either side shows up as
// a mismatch, and a fold error shows up as a mismatch.
//-----------------------------------------------------------------------------
namespace
{
// game/shared/baseentity_shared.h:21-37, spelled here rather than included
// because that header needs the whole prelude. entity_bases_alias_test.cpp
// does the same for the two it needs.
const int kAcrossTransition  = 0x00000002;
const int kImpulseUse        = 0x00000010;
const int kDirectionalUse    = 0x00000080;
const int kUseOnGround       = 0x00000100;
const int kUseInRadius       = 0x00000200;
const int kSaveNonNetworkable= 0x00000400;
const int kWCEditPosition    = 0x40000000;
const int kDontSave          = (int)0x80000000;
}

TEST( HeaderAlias, TheObjectCapsFoldsAreArithmeticallyRight )
{
	const int base = 0;  // what BaseClass::ObjectCaps() contributes, armed.

	// func_areaportalbase.h, player.h — `base & ~FCAP_ACROSS_TRANSITION` -> 0
	EXPECT_EQ( base & ~kAcrossTransition, 0 );

	// trains.h — `base | FCAP_DIRECTIONAL_USE | FCAP_USE_ONGROUND`
	EXPECT_EQ( base | kDirectionalUse | kUseOnGround,
	           kDirectionalUse | kUseOnGround );

	// player_resource.h — `base | FCAP_DONT_SAVE`
	EXPECT_EQ( base | kDontSave, kDontSave );

	// items.h — `base | FCAP_IMPULSE_USE | FCAP_WCEDIT_POSITION`
	EXPECT_EQ( base | kImpulseUse | kWCEditPosition, kImpulseUse | kWCEditPosition );

	// vehicle_base.h — `base | FCAP_IMPULSE_USE`
	EXPECT_EQ( base | kImpulseUse, kImpulseUse );

	// c_baseanimating.h — `base | FCAP_SAVE_NON_NETWORKABLE`
	EXPECT_EQ( base | kSaveNonNetworkable, kSaveNonNetworkable );

	// gib.h — `(base & ~FCAP_ACROSS_TRANSITION) | FCAP_DONT_SAVE | FCAP_IMPULSE_USE`
	EXPECT_EQ( ( base & ~kAcrossTransition ) | kDontSave | kImpulseUse,
	           kDontSave | kImpulseUse );

	// Sprite.h — `(base & ~FCAP_ACROSS_TRANSITION) | flags`, for both values
	// `flags` can take. The armed arm returns `flags` unchanged, so BOTH must
	// agree, not just the interesting one.
	EXPECT_EQ( ( base & ~kAcrossTransition ) | kDontSave, kDontSave );
	EXPECT_EQ( ( base & ~kAcrossTransition ) | 0, 0 );

	// doors.h — `flags = base`, then `flags | FCAP_IMPULSE_USE | FCAP_USE_IN_RADIUS`
	// on the SF_DOOR_PUSE arm and `flags` otherwise. Armed the spawnflag store
	// is gone too, so HasSpawnFlags() is false and only the second arm remains;
	// the armed text returns 0, and that is what these two lines check.
	EXPECT_EQ( base, 0 );
	EXPECT_EQ( base | kImpulseUse | kUseInRadius, kImpulseUse | kUseInRadius );
}

//-----------------------------------------------------------------------------
// 4. THE SCANNER ITSELF.
//
// Every assertion above is only as good as GuardsIn() and StripComments(), and
// both were wrong once in this file's short life: the polarity handling read
// four `#ifdef` guards as absent, and the raw-text match read a member's name
// out of the comment explaining that the member is gone. A scanner that
// silently finds nothing and a tree that is silently correct look identical
// from outside, so the scanner gets its own tests.
//-----------------------------------------------------------------------------
TEST( HeaderAliasScanner, StripCommentsRemovesBothFormsAndKeepsCode )
{
	EXPECT_EQ( StripComments( "int a; // IsPlayer()\n" ), "int a; \n" );
	EXPECT_EQ( StripComments( "// whole line IsPlayer()\nint a;\n" ), "\nint a;\n" );
	EXPECT_EQ( StripComments( "int /* IsPlayer() */ a;\n" ), "int  a;\n" );
	EXPECT_EQ( StripComments( "/* multi\n IsPlayer()\n line */int a;\n" ), "int a;\n" );

	// The load-bearing case: code and prose naming the same token.
	const std::string s = StripComments( "// IsPlayer() is gone\nreturn NULL;\n" );
	EXPECT_TRUE( s.find( "IsPlayer()" ) == std::string::npos );
	EXPECT_TRUE( s.find( "return NULL;" ) != std::string::npos );
}

TEST( HeaderAliasScanner, BothGuardPolaritiesAreRecognisedAndArmsAssignedRight )
{
	bool bArmedFirst = true;
	EXPECT_TRUE( IsGuardOpen( "#ifndef SS_CBASEENTITY_ALIAS", &bArmedFirst ) );
	EXPECT_FALSE( bArmedFirst );
	EXPECT_TRUE( IsGuardOpen( "\t#ifdef SS_CBASEENTITY_ALIAS", &bArmedFirst ) );
	EXPECT_TRUE( bArmedFirst );
	// SS_CBASEENTITY_ALIAS_H is the alias header's own include guard.
	EXPECT_FALSE( IsGuardOpen( "#ifndef SS_CBASEENTITY_ALIAS_H", &bArmedFirst ) );
	EXPECT_FALSE( IsGuardOpen( "#ifdef SOURCESHARP", &bArmedFirst ) );

	// THE ARM ASSIGNMENT, ON A LITERAL. This used to read player.h, which
	// carried one guard of each polarity; player.h carries none now and neither
	// does any other compiled file, so reading one would be a scanner self-test
	// that has quietly stopped testing the scanner.
	//
	// A literal is the right fixture here for the reason the file's own
	// introduction gives: the scanner has to be able to FIND a guard, or a
	// "zero guards" reading over a compiled header is a scanner returning
	// nothing rather than a tree that is clean.
	// The shapes are transcribed from the two guards player.h used to carry.
	std::vector< std::string > lines;
	lines.push_back( "#ifdef SS_CBASEENTITY_ALIAS" );
	lines.push_back( "\treturn NULL;" );
	lines.push_back( "#else" );
	lines.push_back( "\treturn static_cast<CBasePlayer *>( pEntity );" );
	lines.push_back( "#endif" );
	lines.push_back( "#ifndef SS_CBASEENTITY_ALIAS" );
	lines.push_back( "\treturn IsServer();" );
	lines.push_back( "#else" );
	lines.push_back( "\treturn true;" );
	lines.push_back( "#endif" );

	const std::vector< Guard_t > guards = GuardsInLines( lines, "<fixture>" );
	ASSERT_EQ( 2u, guards.size() );

	bool bSawIfdefStyle = false, bSawIfndefStyle = false;
	for ( size_t i = 0; i < guards.size(); i++ )
	{
		if ( Contains( guards[ i ].ArmedText(), "return NULL;" )
			&& Contains( guards[ i ].UnarmedText(), "static_cast<" ) )
			bSawIfdefStyle = true;
		if ( Contains( guards[ i ].UnarmedText(), "return IsServer();" )
			&& Contains( guards[ i ].ArmedText(), "return true;" ) )
			bSawIfndefStyle = true;
	}
	EXPECT_TRUE( bSawIfdefStyle ) << "the #ifdef-style guard was not parsed with "
		"its arms the right way round: armed-first means the FIRST arm is the "
		"armed one.";
	EXPECT_TRUE( bSawIfndefStyle ) << "the #ifndef-style guard was not parsed.";
}

TEST( HeaderAliasScanner, AnAdditionGuardIsDistinguishedFromADeletionGuard )
{
	// The classification the addition-guard rules turned on. Both shapes are
	// `#else`-less and BOTH have one empty arm, so the ONLY thing telling them
	// apart is bArmedFirst. Get it wrong and an #ifdef that lost its #else --
	// which silently deletes SDK text from the shipping build -- is waived as a
	// legitimate addition guard.
	//
	// READ FROM A LITERAL, NOT FROM c_baseanimating.h. This used to read the
	// real free-rider chain-end out of that header "so it tracks the header
	// instead of a copy of it". The header has no guards now -- the flip landed
	// and the free-rider overloads stand unconditionally -- so tracking it would
	// mean asserting nothing at all. The distinction the scanner has to make is
	// unchanged, and it is what keeps a guard reappearing in a compiled file
	// from being classified as harmless.
	std::vector< std::string > lines;
	lines.push_back( "#ifdef SS_CBASEENTITY_ALIAS" );                    // addition
	lines.push_back( "inline void DataMapAccess( C_BaseAnimating *, datamap_t **p ) { *p = NULL; }" );
	lines.push_back( "#endif" );
	lines.push_back( "#ifndef SS_CBASEENTITY_ALIAS" );                   // deletion
	lines.push_back( "\tif ( pBase ) return pBase->GetDebugName();" );
	lines.push_back( "#endif" );

	const std::vector< Guard_t > guards = GuardsInLines( lines, "<fixture>" );
	ASSERT_EQ( 2u, guards.size() );

	size_t nAddition = 0, nDeletion = 0;
	for ( size_t i = 0; i < guards.size(); i++ )
	{
		const Guard_t &G = guards[ i ];
		ASSERT_TRUE( G.bClosed );
		ASSERT_FALSE( G.bHasElse );
		if ( G.bArmedFirst )
		{
			nAddition++;
			EXPECT_TRUE( Contains( G.ArmedText(), "DataMapAccess( C_BaseAnimating *" ) )
				<< "the #else-less #ifdef's content landed in the wrong arm";
			EXPECT_FALSE( G.UnarmedHasCode() )
				<< "an addition guard has no unarmed arm by definition";
		}
		else
		{
			nDeletion++;
			EXPECT_TRUE( Contains( G.UnarmedText(), "pBase->GetDebugName()" ) )
				<< "the #else-less #ifndef's content landed in the wrong arm";
			EXPECT_FALSE( G.ArmedHasCode() )
				<< "a deletion guard has no armed arm by definition";
		}
	}
	EXPECT_EQ( 1u, nAddition );
	EXPECT_EQ( 1u, nDeletion );
}

//=============================================================================//
//
// LANE shards-a — the SERVER-side shards in the first third of step 6's dispatch
// manifest (04, 05, 06, 07, 08, 09, 10, 11, 12, 13, 15, 16, 19).
//
// Appended here rather than given a file of its own because buildcpptests is
// another lane's fence, and a test nothing runs is not a test. The shapes below
// are this lane's, not lane H's, so they carry their own site tables. The rest
// of the lane's facts read files the build no longer compiles and went with
// them.
//
//=============================================================================//

//-----------------------------------------------------------------------------
// 4b. THE `UTIL_ClipTraceToPlayers` ROUTE IS GONE ENTIRELY, NOT MERELY GUARDED.
//
// Two rows used to sit in a ShardsA site table: `filter->ShouldHitEntity( player, mask )`
// and `enginetrace->ClipRayToEntity(`. Both wanted an IHandleEntity for a
// CBasePlayer that does not exist on either side, and the assertion there was
// that the ARMED arm no longer names them.
//
// c8-shared6 went further: the whole walk became a declared, printed refusal
// (§7), so the two calls were not in the file at all and there was no guard to
// examine. This fact was the replacement for the kRows pair.
//
// plan_shim.md PHASE 2c, lane 2-utilshared, WENT FURTHER AGAIN, AND THE §7
// ARGUMENT CHANGES SHAPE WITH IT. The refusal was a runtime warning, and a
// runtime warning has an audience only while something CALLS the function. The
// census over both link lines found nothing calling it:
//
//     tools/shim-census --mtime-policy off who-imports \
//         src/game/shared/util_shared.cpp
//       server 3 importers (hl2_episodic, UTIL_PlayerByCommandArg,
//       UTIL_BloodDrips), client 1 (hl2_episodic) -- and
//       UTIL_ClipTraceToPlayers is none of them
//
// so the warning could never print and the function was deleted with the rest
// of that file's dead bodies. What §7 asks for -- that a removed capability be
// DECLARED rather than silently answering a plausible nothing -- is now carried
// by the LINK instead of by a Warning: `util_shared.h` still declares the
// function and nothing either target compiles defines it, so a caller that reappears
// fails to link, naming the symbol. That is louder than a once-per-session
// message nobody was there to read.
//
// THE THREE HALVES BELOW, and each refuses a different wrong answer:
//   1. the two player-clipping calls are absent      (the original claim)
//   2. the definition is absent, here AND in the shim file that is the natural
//      place for a "move" to have quietly put it back  (a relocation is not a
//      removal, and this fact must not be satisfiable by either)
//   3. the DECLARATION is still there                (without this, a future
//      lane deleting the declaration too would turn the link error into a
//      compile error at the call site, which tempts a stub -- and a stub that
//      returns an unshortened trace is exactly the silent no-op §7 forbids)
//-----------------------------------------------------------------------------
TEST( ShardsA, TheClipTraceToPlayersRouteIsGoneEntirely )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	// THE FILE IS GONE, plan_shim.md Phase 2c, lane 2-utilshared. Its two
	// surviving definitions moved into the shim library and the route below is
	// asserted THERE, which is the only place it could come back to: an absence
	// over a file that does not exist is vacuous, and this fact's whole subject
	// is a route that must not be re-invented anywhere.
	EXPECT_TRUE( ReadLines( "game/shared/util_shared.cpp" ).empty() )
		<< "game/shared/util_shared.cpp exists again. It is where "
		<< "UTIL_ClipTraceToPlayers was; a body there reaches the route this "
		<< "fact forbids with nothing else in the build watching.";

	const std::vector< std::string > lines =
		ReadLines( "sourcesharp/game/shared/util_shared_library.cpp" );
	ASSERT_GT( lines.size(), 10u )
		<< "sourcesharp/game/shared/util_shared_library.cpp read as empty -- "
		   "every absence below would pass over a file nobody read";

	std::string all;
	for ( size_t j = 0; j < lines.size(); j++ ) all += lines[ j ] + "\n";
	all = StripComments( all );

	EXPECT_FALSE( Contains( all, "filter->ShouldHitEntity( player, mask )" ) )
		<< "the shim library asks a trace filter about a `player`. There is "
		<< "no CBasePlayer on either side for one to be, and the ask needs an "
		<< "IHandleEntity that IGameEntity deliberately is not.";

	// SCOPED TO THE PLAYER CALL, and the first draft of this assertion was NOT
	// -- it used the bare `enginetrace->ClipRayToEntity(` that kRows carried,
	// and went red on `UTIL_TraceModel`'s call. That one was FINE and was the
	// de-entitied shape this port wants: its subject was an
	// `IHandleEntity *pentModel`, which both hierarchies answer. (It is gone
	// too now, for having no callers, but the needle stays scoped: a needle
	// that matches a legitimate site is a check that fails for the wrong
	// reason, and UTIL_TraceModel could legitimately come back.)
	EXPECT_FALSE( Contains( all, "ClipRayToEntity( ray, mask|CONTENTS_HITBOX, player," ) )
		<< "the shim library clips a ray to a PLAYER. Same route, same "
		<< "missing IHandleEntity: there is no CBasePlayer on either side.";

	EXPECT_FALSE( Contains( all, "UTIL_PlayerByIndex( k )" ) )
		<< "the player-slot walk came back into UTIL_ClipTraceToPlayers.";

	// 2. NO DEFINITION.
	EXPECT_FALSE( Contains( all, "void UTIL_ClipTraceToPlayers(" ) )
		<< "UTIL_ClipTraceToPlayers was MOVED into the shim rather than removed. "
		<< "A relocation is not a removal: the walk still needs, per player, "
		<< "something ClipRayToEntity accepts plus 'is he alive' and 'where is "
		<< "his centre', and nothing on either side answers the third ask "
		<< "(filter->ShouldHitEntity( player, mask )). Port the walk to "
		<< "CStandalonePlayer::FromIndex / C_StandalonePlayer to change this.";

	// 3. AND THE DECLARATION IS STILL THERE, so a caller fails at the LINK.
	const std::vector< std::string > hdrLines = ReadLines( "game/shared/util_shared.h" );
	ASSERT_GT( hdrLines.size(), 10u ) << "game/shared/util_shared.h read as empty";
	std::string hdr;
	for ( size_t j = 0; j < hdrLines.size(); j++ ) hdr += hdrLines[ j ] + "\n";
	EXPECT_TRUE( Contains( StripComments( hdr ), "UTIL_ClipTraceToPlayers" ) )
		<< "util_shared.h stopped declaring UTIL_ClipTraceToPlayers. With the "
		<< "declaration gone a returning caller gets a COMPILE error at its own "
		<< "call site, which is the error somebody fixes by writing a stub -- "
		<< "and a stub here returns a trace that was not shortened, which is "
		<< "indistinguishable from one that could not be. Keep the declaration; "
		<< "the missing definition is the message.";
}
