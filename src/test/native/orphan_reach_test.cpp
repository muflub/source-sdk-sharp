//====== SourceSharp — the orphan files define nothing nobody imports ========//
//
// `plan_shim.md` §6, lane `4a-clientshell`.
//
// WHAT THIS FILE IS FOR. The shim has LINKER-SATISFACTION files —
// `shell_player_orphans.cpp` and `shell_client_orphans.cpp` — whose entire
// content is definitions that exist because some still-compiled translation
// unit references them. Each one's own header states the rule:
//
//     Found by removing the files and reading the linker, which is also how
//     entries come off this list.
//
// That rule has one failure mode, and it is a slow one: a definition's caller
// is evicted by a later lane, the definition stays, and nothing says so. Both
// files had drifted. `shell_player_orphans.cpp` defined TWENTY-SIX symbols and
// the client link line imported FIVE; `shell_client_orphans.cpp` defined seven
// and imported four. That is 24 definitions kept alive by a rule nobody was
// re-running.
//
// So the rule is re-run here, every build. `tools/shim-orphan-reach` reads the
// link edge of client.so in the CMake build's build.ninja (farm/<config>/build,
// through tools/cmakegraph.py), `nm -C -u` against
// `nm -C --defined-only` with a T/B/D symbol-type filter, and exits 1 if any
// definition has no importer.
//
// WHY THE TYPE FILTER IS LOAD-BEARING. A plain `nm --defined-only` counts weak
// inline emissions as definitions, and an orphan file full of one-line bodies
// picks up dozens from `cbase.h` — `Vector::Init`, `Vector2D::Vector2D`. Every
// one of them would read as an unimported definition of this file's and this
// test would be permanently, uselessly red.
//
// WHY A REFUSAL IS NOT A PASS. An unbuilt tree answers "nobody imports
// anything", which is indistinguishable from "every definition here is dead"
// and would delete a whole file. That is failure mode 8 of the Phase 4
// addendum — a tool REFUSAL recorded as a zero — and it is what called
// `handle_cross.h` dead when it has 102 server / 169 client live TUs. The tool
// exits 2 rather than 0 in that case, and the facts below assert on the exit
// code, so an unbuilt tree fails loudly instead of passing quietly.
//
// WHERE THIS STOPS. A count of undefined references is a FLOOR: a virtual call
// emits no symbol. `TheOrphanFilesHoldNoClassWithAVtable` is the fact that
// makes the floor exact for these two files — every survivor is either a free
// function or a member of a class no linked object emits a vtable for, so no
// virtual call can dispatch to one.
//
//=============================================================================//

#include <string>
#include <cstdio>
#include <cstdlib>
#include <sys/stat.h>
#include <sys/wait.h>
#include <unistd.h>

#include "gtest/gtest.h"

namespace
{

//-----------------------------------------------------------------------------
// The worktree root: the tree the harness NAMES, not one found by walking.
//
// A committed harness that hardcoded a path would outlive its lane and then
// measure somebody else's tree while reporting RED or GREEN about this one.
// The usual fix in this directory is an upward walk from the CURRENT DIRECTORY
// for a file that only exists at a root -- and that walk has a failure mode
// this test cannot afford:
//
//     this worktree is <main>/.claude/worktrees/agent-XXXX/, so from
//     <main>/.claude/worktrees/ the candidates `.`, `..`, `../..` reach
//     <main> ITSELF -- which carries its own tools/shim-orphan-reach the
//     moment this branch is merged.
//
// The tool would then enumerate the MAIN CHECKOUT's build graph and this test
// would pass or fail about a tree it is not testing. An absence measured in the
// wrong checkout is the worst shape a green has.
//
// So the root is NAMED BY THE HARNESS rather than found. It used to be derived
// from `/proc/self/exe` (<root>/tools/.cpptest/<name>, strip three), but since
// the move to source-sdk-sharp the binary lives in <repo>/bin/<config>/cpptest/
// and the tree the tool must read is the COMPAT FARM, <repo>/farm/<config>/ --
// a sibling of bin/, not an ancestor of the binary, so no depth count reaches
// it. buildcpptests, buildonecpptest and cpptest_one run every binary with cwd
// farm/<config>/src and export SOURCESHARP_SDK_TREE=farm/<config> (the same
// variable the managed RepoLayout reads). The tool's default --build-dir is
// `build` under that cwd, i.e. farm/<config>/build. When it is set it is the only
// answer: a named tree without the tool is a refusal, never a walk elsewhere.
// The bounded upward walk remains for a binary run by hand without it; from
// the farm's src/ its first hit is `..`, the farm itself.
//-----------------------------------------------------------------------------
const char *WorktreeRoot()
{
	static std::string s_szRoot;
	static bool s_bResolved = false;

	if ( s_bResolved )
		return s_szRoot.empty() ? NULL : s_szRoot.c_str();

	s_bResolved = true;

	// Named by the harness: the farm, <repo>/farm/<config>.
	if ( const char *pszTree = getenv( "SOURCESHARP_SDK_TREE" ) )
	{
		struct stat st;
		const std::string szProbe = std::string( pszTree ) + "/tools/shim-orphan-reach";

		if ( *pszTree && stat( szProbe.c_str(), &st ) == 0 && S_ISREG( st.st_mode ) )
			s_szRoot = pszTree;

		return s_szRoot.empty() ? NULL : s_szRoot.c_str();
	}

	// Fallback only, for a binary run by hand without SOURCESHARP_SDK_TREE.
	// Bounded; from the farm's src/ it stops at `..`.
	static const char *const kCandidates[] = { ".", "..", "../..", "../../.." };

	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		const std::string szProbe = std::string( kCandidates[i] ) + "/tools/shim-orphan-reach";
		struct stat st;

		if ( stat( szProbe.c_str(), &st ) == 0 && S_ISREG( st.st_mode ) )
		{
			s_szRoot = kCandidates[i];
			return s_szRoot.c_str();
		}
	}

	return NULL;
}

struct Run_t
{
	int         m_nExit;
	std::string m_szOutput;
};

//-----------------------------------------------------------------------------
// popen reports the wait(2) status, so the exit code has to be pulled back out
// of it. A test comparing the raw status against 1 would be comparing against
// 256 and would never fail.
//-----------------------------------------------------------------------------
// THE DEBUG BUILD, in either configuration's run. The orphan rule is a fact
// about what the DEBUG link imports -- -O2 inlines importers away -- and the
// VPC-era tool read sdk_csharp_debug.ninja whatever the farm; RunAtRoot runs
// at farm/<config>, so ../debug is farm/debug.
const char *const kDebugBuild = "--build-dir ../debug/build ";

Run_t RunAtRoot( const std::string &szCommand )
{
	Run_t run;
	run.m_nExit = -1;

	const char *pszRoot = WorktreeRoot();

	if ( !pszRoot )
		return run;

	const std::string szFull = std::string( "cd '" ) + pszRoot + "' && " + szCommand + " 2>&1";
	FILE *pPipe = popen( szFull.c_str(), "r" );

	if ( !pPipe )
		return run;

	char rgchBuf[ 4096 ];

	while ( fgets( rgchBuf, sizeof( rgchBuf ), pPipe ) )
		run.m_szOutput += rgchBuf;

	const int nStatus = pclose( pPipe );
	run.m_nExit = WIFEXITED( nStatus ) ? WEXITSTATUS( nStatus ) : -1;
	return run;
}

// The two files, and what each defined before this lane re-ran the rule.
struct Orphan_t
{
	const char *m_pszObject;
	const char *m_pszSource;
	int         m_nWas;      // definitions before
	int         m_nIs;       // definitions after
};

const Orphan_t kOrphans[] =
{
	{ "shell_player_orphans.o", "shell_player_orphans.cpp", 26, 5 },
	{ "shell_client_orphans.o", "shell_client_orphans.cpp",  7, 3 },
};

const size_t kNumOrphans = sizeof( kOrphans ) / sizeof( kOrphans[0] );

} // namespace

//-----------------------------------------------------------------------------
// THE FACT. Neither file defines anything the client link line does not import.
//-----------------------------------------------------------------------------
TEST( OrphanReach, NeitherOrphanFileDefinesAnythingUnimported )
{
	ASSERT_NE( WorktreeRoot(), (const char *)NULL )
		<< "tools/shim-orphan-reach was not found from the test's working "
		   "directory, so nothing below measured anything.";

	for ( size_t i = 0; i < kNumOrphans; i++ )
	{
		const Run_t run = RunAtRoot( std::string( "tools/shim-orphan-reach " ) +
		                             kDebugBuild + kOrphans[i].m_pszObject );

		ASSERT_NE( 2, run.m_nExit )
			<< kOrphans[i].m_pszSource << ": the tool REFUSED, which is not a "
			   "pass and is not a failure of the code under test. Build the tree "
			   "first (make debug): an "
			   "unbuilt tree answers \"nobody imports anything\", which reads "
			   "exactly like \"delete this whole file\".\n" << run.m_szOutput;

		EXPECT_EQ( 0, run.m_nExit )
			<< kOrphans[i].m_pszSource << " defines something no object on the "
			   "client link line imports. These files exist ONLY to satisfy the "
			   "linker, so a definition with no importer is dead source kept "
			   "alive by a rule nobody re-ran. Delete the DEAD rows -- the "
			   "transitive set, because a sibling in the same object does not "
			   "import its neighbour -- and update the file's roster comment.\n"
			<< run.m_szOutput;
	}
}

//-----------------------------------------------------------------------------
// THE POSITIVE CONTROL, and it is not optional: the fact above is satisfied by
// a tool that prints nothing and exits 0 for any argument. This one requires
// the tool to have actually found the object and to have enumerated symbols.
//-----------------------------------------------------------------------------
TEST( OrphanReach, TheToolActuallyFoundTheObjectsAndTheirSymbols )
{
	ASSERT_NE( WorktreeRoot(), (const char *)NULL );

	for ( size_t i = 0; i < kNumOrphans; i++ )
	{
		const Run_t run = RunAtRoot( std::string( "tools/shim-orphan-reach " ) +
		                             kDebugBuild + kOrphans[i].m_pszObject );

		ASSERT_NE( 2, run.m_nExit ) << run.m_szOutput;

		EXPECT_NE( run.m_szOutput.find( "objects on the graph, all on disk" ),
		           std::string::npos )
			<< kOrphans[i].m_pszObject << ": the tool did not report a graph it "
			   "had checked, so its exit code is not about the link line.\n"
			<< run.m_szOutput;

		EXPECT_NE( run.m_szOutput.find( "LIVE " ), std::string::npos )
			<< kOrphans[i].m_pszObject << ": not one LIVE row. A file that "
			   "defines nothing at all satisfies the fact above vacuously, and "
			   "these two files exist precisely because something imports them.\n"
			<< run.m_szOutput;
	}
}

//-----------------------------------------------------------------------------
// A REFUSAL IS REACHABLE. Without this, "the tool refuses on an unbuilt tree"
// is prose: an object name the graph does not carry takes the same exit-2 path
// as a missing object, and this proves that path is wired to the exit code.
//-----------------------------------------------------------------------------
TEST( OrphanReach, AnObjectTheGraphDoesNotNameIsRefusedRatherThanCalledDead )
{
	ASSERT_NE( WorktreeRoot(), (const char *)NULL );

	const Run_t run = RunAtRoot( "tools/shim-orphan-reach no_such_object_at_all.o" );

	EXPECT_EQ( 2, run.m_nExit )
		<< "an object the graph does not name came back as exit " << run.m_nExit
		<< " rather than a refusal. Exit 0 there would mean 'nothing unimported', "
		   "which is the answer this tool must never give for a question it "
		   "could not ask.\n" << run.m_szOutput;
}

//-----------------------------------------------------------------------------
// THE FLOOR IS EXACT FOR THESE TWO FILES. A virtual call emits no symbol, so
// an undefined-reference count cannot see one -- unless no vtable for the class
// exists anywhere on the link line, in which case no instance can exist and no
// virtual call can dispatch.
//
// C_BasePlayer and C_BaseAnimating are the only classes whose members either
// file defines. Every C_BasePlayer constructor left the build in 13c;
// C_BaseAnimating's has never been on this repo's client link line (its fact
// is NoLinkedObjectEmitsAVtableOrConstructorForCBaseAnimating, below).
//-----------------------------------------------------------------------------
TEST( OrphanReach, NoLinkedObjectEmitsAVtableForCBasePlayer )
{
	ASSERT_NE( WorktreeRoot(), (const char *)NULL );

	// The objects on client.so's link edge, never a glob over the object
	// directory: that keeps the objects of TUs the build has evicted.
	// readlink -f: cmakegraph.py finds the repo (the CMakeLists) from its own
	// path, and the farm's tools/ is a symlink into the repo.
	const std::string szLinked =
		"python3 \"$(readlink -f tools/cmakegraph.py)\" objects client --build-dir ../debug/build | "
		"cut -d' ' -f1 | xargs -r nm -C | ";

	const Run_t probe = RunAtRoot( szLinked +
		"grep -c 'vtable for C_BasePlayer$' || true" );

	ASSERT_NE( -1, probe.m_nExit )
		<< "the vtable probe did not run at all.";

	// A positive control on the same command shape: the shell's OWN vtable is
	// there, so a zero above is a statement about C_BasePlayer and not about
	// nm, the object list, or an unbuilt tree.
	const Run_t control = RunAtRoot( szLinked +
		"grep -c 'vtable for C_StandaloneEntity$' || true" );

	const int nControl = atoi( control.m_szOutput.c_str() );

	ASSERT_GT( nControl, 0 )
		<< "no object emits a vtable for C_StandaloneEntity either, so the "
		   "reading below is about an unbuilt tree or an object list that "
		   "named nothing -- not about C_BasePlayer.\n" << control.m_szOutput;

	EXPECT_EQ( 0, atoi( probe.m_szOutput.c_str() ) )
		<< "an object on the client link line emits a vtable for C_BasePlayer, "
		   "so instances of it can exist and its members can be reached "
		   "virtually. The undefined-reference count the orphan facts above rest "
		   "on is then a FLOOR rather than a total, and a DEAD row is no longer "
		   "evidence that a definition can go.\n" << probe.m_szOutput;
}

//-----------------------------------------------------------------------------
// THE STUB WENT WITH ITS CALLER. shell_client_orphans.cpp defined
// C_BaseAnimating::LookupAttachment as `return -1` for c_rope.cpp's
// CreateFromKeyValues; c_rope.cpp left with the C2 rope port and the stub with
// it. What stays true, and is asserted: no object on the link line names the
// member or emits C_BaseAnimating's vtable or a constructor, so nothing on the
// client can reach a C_BaseAnimating at all. Same command shape and positive
// control as the C_BasePlayer fact above.
//-----------------------------------------------------------------------------
TEST( OrphanReach, NoLinkedObjectNamesOrBuildsACBaseAnimating )
{
	ASSERT_NE( WorktreeRoot(), (const char *)NULL );

	const std::string szLinked =
		"python3 \"$(readlink -f tools/cmakegraph.py)\" objects client --build-dir ../debug/build | "
		"cut -d' ' -f1 | xargs -r nm -C | ";

	const Run_t control = RunAtRoot( szLinked +
		"grep -c 'vtable for C_StandaloneEntity$' || true" );

	ASSERT_GT( atoi( control.m_szOutput.c_str() ), 0 )
		<< "no object emits a vtable for C_StandaloneEntity either, so the "
		   "readings below are about an unbuilt tree, not C_BaseAnimating.\n"
		<< control.m_szOutput;

	const Run_t named = RunAtRoot( szLinked +
		"grep -c 'C_BaseAnimating::LookupAttachment' || true" );
	const Run_t vtable = RunAtRoot( szLinked +
		"grep -c 'vtable for C_BaseAnimating$' || true" );
	const Run_t ctor = RunAtRoot( szLinked +
		"grep -cE ' [TW] C_BaseAnimating::C_BaseAnimating\\(' || true" );

	ASSERT_NE( -1, named.m_nExit ) << "the import probe did not run at all.";
	ASSERT_NE( -1, vtable.m_nExit ) << "the vtable probe did not run at all.";
	ASSERT_NE( -1, ctor.m_nExit ) << "the constructor probe did not run at all.";

	EXPECT_EQ( 0, atoi( named.m_szOutput.c_str() ) )
		<< "an object on the client link line names "
		   "C_BaseAnimating::LookupAttachment again; with no definition the "
		   "GC_SECTIONS=0 link (BothDllsLinkWithoutGcSections) is what breaks.\n"
		<< named.m_szOutput;
	EXPECT_EQ( 0, atoi( vtable.m_szOutput.c_str() ) )
		<< "an object on the client link line emits a vtable for "
		   "C_BaseAnimating, so instances can exist.\n" << vtable.m_szOutput;
	EXPECT_EQ( 0, atoi( ctor.m_szOutput.c_str() ) )
		<< "an object on the client link line defines a C_BaseAnimating "
		   "constructor; see the vtable message.\n" << ctor.m_szOutput;
}

//-----------------------------------------------------------------------------
// BOTH DLLS LINK WITH NOTHING COLLECTED -- the guard for `make debug
// GC_SECTIONS=0`, which every other check is blind to (see tools/nogc-relink's
// header and the Makefile's KEEP THIS WORKING note).
//
// tools/nogc-relink relinks server.so and client.so from the debug build's own
// objects with ninja's own link command minus -Wl,--gc-sections, into a
// temporary directory: about a quarter of a second for both. It found
// C_BaseAnimating::LookupAttachment, undefined on the client since this repo's
// first commit, and goes red when the stub for it is removed while the normal
// build stays green.
//-----------------------------------------------------------------------------
TEST( OrphanReach, BothDllsLinkWithoutGcSections )
{
	ASSERT_NE( WorktreeRoot(), (const char *)NULL );

	const Run_t run = RunAtRoot( std::string( "tools/nogc-relink " ) + kDebugBuild );

	ASSERT_NE( 2, run.m_nExit )
		<< "tools/nogc-relink REFUSED, which is neither a pass nor a failure of "
		   "the link. Build the tree first (make debug).\n" << run.m_szOutput;

	EXPECT_EQ( 0, run.m_nExit )
		<< "a DLL fails to link once --gc-sections is off, so `make debug "
		   "GC_SECTIONS=0` is broken. A retained file names a symbol nothing "
		   "defines, and only the collector was hiding it. Supply the symbol "
		   "(an orphan stub, or the definition moved to a kept file) or take "
		   "the caller out of the build.\n" << run.m_szOutput;

	// The stimulus reached the subject: both links ran, and each one really
	// had the collector to drop.
	EXPECT_NE( run.m_szOutput.find( "server LINKED  (server.so, --gc-sections dropped)" ), std::string::npos )
		<< run.m_szOutput;
	EXPECT_NE( run.m_szOutput.find( "client LINKED  (client.so, --gc-sections dropped)" ), std::string::npos )
		<< run.m_szOutput;
}

TEST( OrphanReach, NoGcRelinkRefusesAMissingBuild )
{
	ASSERT_NE( WorktreeRoot(), (const char *)NULL );

	const Run_t run = RunAtRoot( "tools/nogc-relink --build-dir no_such_build_dir" );

	EXPECT_EQ( 2, run.m_nExit )
		<< "a build directory that does not exist came back as exit "
		<< run.m_nExit << " rather than a refusal.\n" << run.m_szOutput;
}
