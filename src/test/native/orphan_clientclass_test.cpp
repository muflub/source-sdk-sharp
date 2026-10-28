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
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[i] ) + "/game";
		const std::string pub = std::string( kCandidates[i] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) && stat( pub.c_str(), &st ) == 0 &&
			 S_ISDIR( st.st_mode ) )
		{
			return kCandidates[i];
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

//-----------------------------------------------------------------------------
// Comments out. The prose in this tree names the very macros the scans look
// for -- this file does it in its own banner -- so an unstripped scan reads the
// explanation as the thing explained.
//-----------------------------------------------------------------------------
bool Has( const std::string &hay, const char *pszNeedle )
{
	return hay.find( pszNeedle ) != std::string::npos;
}

std::string NoWhitespace( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );
	for ( size_t i = 0; i < in.size(); i++ )
		if ( !isspace( (unsigned char)in[i] ) )
			out += in[i];
	return out;
}

//-----------------------------------------------------------------------------
// The body of one function, braces counted rather than the first `}` taken.
//-----------------------------------------------------------------------------
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
std::vector<std::string> TargetFiles( const char *pszTarget )
{
	const std::set<std::string> srcs = cmake_graph::CompiledSources( pszTarget );
	return std::vector<std::string>( srcs.begin(), srcs.end() );
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
	bool bAlias; // SS_CBASEENTITY_ALIAS, from the build. See AliasArmedIn.

	bool Known( const std::string &name, bool &bValue ) const
	{
		if ( name == "SOURCESHARP" )
		{
			bValue = true;
			return true;
		}
		if ( name == "CLIENT_DLL" )
		{
			bValue = bClient;
			return true;
		}
		if ( name == "GAME_DLL" )
		{
			bValue = !bClient;
			return true;
		}
		if ( name == "SS_CBASEENTITY_ALIAS" )
		{
			bValue = bAlias;
			return true;
		}
		return false;
	}
};

std::string FirstIdentifier( const std::string &in )
{
	size_t i = 0;
	while ( i < in.size() && !( isalpha( (unsigned char)in[i] ) || in[i] == '_' ) )
		i++;
	size_t start = i;
	while ( i < in.size() && ( isalnum( (unsigned char)in[i] ) || in[i] == '_' ) )
		i++;
	return in.substr( start, i - start );
}

std::string StripDeadArms( const std::string &in, const GuardWorld &world )
{
	std::vector<std::pair<bool, bool>> stack; // (known, emitting)
	std::string out;
	size_t pos = 0;
	while ( pos <= in.size() )
	{
		size_t nl = in.find( '\n', pos );
		const std::string line = in.substr( pos, ( nl == std::string::npos ? in.size() : nl ) - pos );

		std::string t = line;
		size_t a = t.find_first_not_of( " \t\r" );
		t = ( a == std::string::npos ) ? std::string() : t.substr( a );

		bool bDirective = false;
		if ( !t.empty() && t[0] == '#' )
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
				if ( word == "ifdef" )
				{
					name = FirstIdentifier( tail );
					bWant = true;
					bParsed = true;
				}
				else if ( word == "ifndef" )
				{
					name = FirstIdentifier( tail );
					bWant = false;
					bParsed = true;
				}
				else
				{
					const std::string cond = NoWhitespace( tail );
					if ( cond.compare( 0, 8, "defined(" ) == 0 || cond.compare( 0, 7, "defined" ) == 0 )
					{
						name = FirstIdentifier( cond.substr( 7 ) );
						bWant = true;
						bParsed = true;
					}
					else if ( cond.compare( 0, 8, "!defined" ) == 0 )
					{
						name = FirstIdentifier( cond.substr( 8 ) );
						bWant = false;
						bParsed = true;
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
			if ( !stack[i].second )
				bEmit = false;

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
bool DefinesAlias( const std::set<std::string> &defs )
{
	for ( std::set<std::string>::const_iterator it = defs.begin(); it != defs.end(); ++it )
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

const char *const kClient = cmake_graph::kClient;
const char *const kServer = cmake_graph::kServer;

const char *kRagdoll = "game/client/ragdoll.cpp";
} // namespace

//=============================================================================//
// 1. THE PHASE 5 EXIT CLAUSE ITSELF, recomputed from both targets.
//=============================================================================//
//=============================================================================//
// 2. The guard pass, checked against the one case that proves it is needed.
//=============================================================================//
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
		std::set<std::string> armed;
		armed.insert( "SOURCESHARP" );
		armed.insert( "SS_CBASEENTITY_ALIAS" );
		std::set<std::string> armedValue;
		armedValue.insert( "SOURCESHARP" );
		armedValue.insert( "SS_CBASEENTITY_ALIAS=1" );
		std::set<std::string> bare;
		bare.insert( "SOURCESHARP" );
		// A NAME match, not a prefix one.
		std::set<std::string> lookalike;
		lookalike.insert( "SOURCESHARP" );
		lookalike.insert( "SS_CBASEENTITY_ALIAS_OFF" );

		EXPECT_TRUE( DefinesAlias( armed ) ) << "the reader cannot see the macro in a definitions set, so it "
												"would answer 'unarmed' about an armed tree -- which is the "
												"defect this fact exists for";
		EXPECT_TRUE( DefinesAlias( armedValue ) ) << "the reader misses -DSS_CBASEENTITY_ALIAS=1";
		EXPECT_FALSE( DefinesAlias( bare ) ) << "the reader says 'armed' about a build that defines nothing";
		EXPECT_FALSE( DefinesAlias( lookalike ) ) << "the reader is matching a PREFIX of the macro's name";
	}

	// And the guard world really uses it: an armed world must strip a
	// `#ifndef SS_CBASEENTITY_ALIAS` arm and an unarmed one must keep it.
	{
		const std::string body =
			"#ifndef SS_CBASEENTITY_ALIAS\nIMPLEMENT_CLIENTCLASS_DT(C_Probe,DT_Probe,CProbe)\n#endif\n";

		GuardWorld unarmed;
		unarmed.bClient = true;
		unarmed.bAlias = false;
		GuardWorld armed;
		armed.bClient = true;
		armed.bAlias = true;

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
		<< kRagdoll
		<< " is back in the client project, and with it the place "
		   "the attached ragdoll's ClientClass registration lived.";
}

//=============================================================================//
// 6. CRopeKeyframe -- THE CLASS IS NEEDED. Only the registration goes.
//=============================================================================//
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
		const std::vector<std::string> files = TargetFiles( targets[v] );

		std::string missing;
		int nMissing = 0;
		for ( size_t i = 0; i < files.size(); i++ )
			if ( !Exists( files[i] ) )
			{
				nMissing++;
				missing += "\n    " + files[i];
			}

		EXPECT_EQ( 0, nMissing ) << targets[v] << " (" << cmake_graph::ListFile( targets[v] ) << ") names " << nMissing
								 << " file(s) that are not on disk:" << missing;
	}
}

// TheVpcWalkerReadsContinuedFileRows is RETIRED (Phase 6b): it pinned the vpc
// walker's trap -- a backslash-continued `$File` row read only to its first
// path. The walker is gone with the vpcs; the CMake source list is parsed by
// cmake_graph.h and cross-checked there against compile_commands.json, so a
// list reader that drops rows throws rather than answering "none forbidden".
