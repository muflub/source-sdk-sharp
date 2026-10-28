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

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[i] == '*' && strIn[i + 1] == '/' )
			{
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[i] == '/' && strIn[i + 1] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[i] == '/' && strIn[i + 1] == '/' )
		{
			while ( i < strIn.size() && strIn[i] != '\n' )
				i++;
			if ( i < strIn.size() )
				out += '\n';
			continue;
		}
		out += strIn[i];
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
		if ( !line.empty() && line[line.size() - 1] == '\r' )
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

std::vector<std::string> CompiledServerSources()
{
	const std::set<std::string> src = cmake_graph::CompiledSources( cmake_graph::kServer );
	return std::vector<std::string>( src.begin(), src.end() );
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
void AssertTheServerFileListParsed( const std::vector<std::string> &files )
{
	ASSERT_TRUE( std::find( files.begin(), files.end(), std::string( kServerProofOfRead ) ) != files.end() )
		<< "server_sourcesharp's compiled sources do not include " << kServerProofOfRead
		<< ", so what was read is not this project's file "
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
		const std::vector<std::string> files = CompiledServerSources();
		for ( size_t i = 0; i < files.size(); i++ )
			s_all += ReadStripped( files[i] );
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
std::set<std::string> ServerClassesIn( const std::string &all )
{
	std::set<std::string> out;
	const std::string needle = "IMPLEMENT_SERVERCLASS";

	for ( size_t at = all.find( needle ); at != std::string::npos; at = all.find( needle, at + 1 ) )
	{
		size_t i = at + needle.size();
		// _ST, _ST_NOBASE, _INTERNAL, or nothing -- skip whatever suffix is
		// there and land on the open paren. The macro DEFINITIONS live in
		// public/server_class.h, which is not in the compiled set, so every
		// occurrence here is a use.
		while ( i < all.size() && ( all[i] == '_' || ( all[i] >= 'A' && all[i] <= 'Z' ) ) )
			i++;
		while ( i < all.size() && ( all[i] == ' ' || all[i] == '\t' ) )
			i++;
		if ( i >= all.size() || all[i] != '(' )
			continue;
		i++;
		while ( i < all.size() && ( all[i] == ' ' || all[i] == '\t' || all[i] == '\n' ) )
			i++;

		std::string name;
		while ( i < all.size() &&
				( all[i] == '_' || ( all[i] >= 'a' && all[i] <= 'z' ) || ( all[i] >= 'A' && all[i] <= 'Z' ) ||
				  ( all[i] >= '0' && all[i] <= '9' ) ) )
		{
			name += all[i];
			i++;
		}
		if ( !name.empty() )
			out.insert( name );
	}
	return out;
}

std::set<std::string> RegisteredServerClasses()
{
	return ServerClassesIn( CompiledServerCode() );
}

std::string Join( const std::set<std::string> &s )
{
	std::string out;
	for ( std::set<std::string>::const_iterator it = s.begin(); it != s.end(); ++it )
	{
		if ( !out.empty() )
			out += " ";
		out += *it;
	}
	return out;
}

// team.cpp and team.h are OUT OF THE BUILD. They stay on disk, so they are
// still named here -- but only as the thing nothing may reach, never as a file
// read for its content. kTeamH is the target of the include-closure walk below.
const char *kTeamCpp = "game/server/team.cpp";
const char *kSdkArmFixtureFile = "sourcesharp/game/server/shell_abi_sdk_fixture.cpp";
const char *kEntityNames = "sourcesharp/managed/SourceSharp.Shared/EntityNames.cs";
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
} // namespace

//=============================================================================//
// THE METER'S FLOOR
//=============================================================================//

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
	std::set<std::string> expected; // deliberately empty; see above.

	const std::set<std::string> got = RegisteredServerClasses();

	EXPECT_EQ( expected, got ) << "the surviving SDK ServerClass registrations are now [" << Join( got )
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
	const std::string fixture = "IMPLEMENT_SERVERCLASS_ST( CBaseEntity, DT_BaseEntity )\n"
								"IMPLEMENT_SERVERCLASS_ST_NOBASE( CRagdollProp, DT_Ragdoll )\n"
								"IMPLEMENT_SERVERCLASS(CWorld, DT_WORLD)\n";

	const std::set<std::string> got = ServerClassesIn( fixture );

	EXPECT_EQ( 3u, got.size() ) << "matched [" << Join( got ) << "]";
	EXPECT_EQ( 1u, got.count( "CBaseEntity" ) );
	EXPECT_EQ( 1u, got.count( "CRagdollProp" ) );
	EXPECT_EQ( 1u, got.count( "CWorld" ) );

	// And it does NOT match text that only mentions the macro name, which is
	// what would make the real scan answer non-empty off a comment.
	EXPECT_TRUE( ServerClassesIn( "// IMPLEMENT_SERVERCLASS_ST is the macro\n" ).empty() );
}

//=============================================================================//
// THE INCLUDE CLOSURE ITSELF, before anything is asked of it
//=============================================================================//

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
	const std::vector<std::string> files = CompiledServerSources();
	ASSERT_NO_FATAL_FAILURE( AssertTheServerFileListParsed( files ) );

	int nTeam = 0;
	for ( size_t i = 0; i < files.size(); i++ )
	{
		if ( files[i] == kTeamCpp )
			nTeam++;
	}
	EXPECT_EQ( 0, nTeam ) << "team.cpp is back in the server's file list. The team concept is "
							 "managed work now; a native CTeam is not the way back to it";
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

	EXPECT_EQ( 0, CountOf( all, "g_Teams" ) ) << "g_Teams is named in the compiled server and defined nowhere in it";
	EXPECT_EQ( 0, CountOf( all, "GetGlobalTeam" ) )
		<< "GetGlobalTeam is named in the compiled server and defined nowhere "
		   "in it. It answered NULL for the life of the server before team.cpp "
		   "left; CBaseEntity::GetTeam now returns that NULL directly";
	EXPECT_EQ( 0, CountOf( all, "GetNumberOfTeams" ) )
		<< "GetNumberOfTeams is named in the compiled server and defined "
		   "nowhere in it";
	EXPECT_EQ( 0, CountOf( all, "CTeam::" ) ) << "a CTeam member function is named in the compiled server. None of "
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
	ASSERT_TRUE( pszRoot != NULL ) << "SrcRoot() found no candidate with both game/ and public/ under it; "
									  "the absence below would pass on a tree that was never read";

	// THE ANCHOR. A tree this test can read at all still has the census test's
	// own subject in it. If this is missing the root is wrong and the absence
	// claim underneath is worthless.
	const std::string anchor = std::string( pszRoot ) + "/" + kEntityNames;
	struct stat st;
	ASSERT_EQ( 0, stat( anchor.c_str(), &st ) ) << "resolved SrcRoot() to '" << pszRoot << "' but " << anchor
												<< " is not there, so this tree is not the one the deletion was made "
												   "in and the absence below would be about somebody else's checkout";

	const std::string gone = std::string( pszRoot ) + "/" + kSdkArmFixtureFile;
	EXPECT_NE( 0, stat( gone.c_str(), &st ) ) << gone << " still exists. Resolved SrcRoot() to '" << pszRoot
											  << "'. Lane 5e-testonly deleted it: no .vpc named it, neither ninja "
												 "graph carried an object for it, its class and LINK_ENTITY_TO_CLASS "
												 "had already been stripped by f288a5e11, and the AbiBothHierarchies "
												 "probe it was the stock arm for went in step 8a. If it is back, the "
												 "two sweeps above need their needles rechecked before it stays.";
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
