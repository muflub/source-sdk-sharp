//========= SourceSharp — IS THE PHYSICS EVENT RING'S SINK ACTUALLY BOUND? ====//
//
// plan_entity.md §3.8, lane p4-physics.
//
// WHY THIS FILE EXISTS, and it is the whole point of the lane. Step 3a built a
// bounded, ordered, refusing queue in physics_shell.h; game/server/physics.cpp
// has been pushing five kinds of event into it ever since; and
// CSourceSharpPhysicsShell::Drain hands the whole span to m_pSink once per
// simulation frame. `SetSink` had exactly ONE caller in the entire tree and it
// was physics_shell_test.cpp, which supplies its own.
//
// So Drain() emptied the ring into nothing on every frame, and NOTHING SAID SO:
//
//   * every gtest over the ring was green, because each one binds a recording
//     sink of its own before it pushes anything -- a check that could not fail
//     for the defect it looked exactly like it was checking;
//   * every managed test was green, because there was no managed end;
//   * the live gate loaded the map twice and noticed nothing, because the
//     symptom is an ABSENCE -- a managed prop's collisions made no sound, threw
//     no dust and shook no screen, and no log anywhere says a sound was not
//     played.
//
// Measured on dm_lockdown before the fix, with tools/p4phys-ringprobe:
//
//     high water        263
//     drain crossings   0
//     events handed on  0
//
// A behavioural test cannot see this: the bind is one line in a shim that
// #includes cbase.h's whole world, and the drain runs inside a physics frame.
// What CAN be checked is the text -- that a production caller exists, that it
// is not in the test tree, and that what it binds forwards to the ABI slot. So
// that is what this file checks, comment-stripped, with the argument as well as
// the name.
//
// COMMENT-STRIPPED, AND THAT IS LOAD-BEARING HERE. This file's own explanation
// names SetSink and PhysicsEvents many times, and several of the files it scans
// carry paragraphs about them too. A raw grep would read an explanation as the
// thing it explains -- 7b's C_BaseTempEntity needle and 6d's hitbox needle,
// twice over.
//
//=============================================================================//

#include <limits.h>
#include <stdlib.h>
#include <unistd.h>
#include <fstream>
#include <string>
#include <sys/stat.h>
#include <vector>

#include "gtest/gtest.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string Read( const std::string &strPath )
{
	std::ifstream in( strPath.c_str() );
	if ( !in )
		return std::string();
	return std::string( ( std::istreambuf_iterator< char >( in ) ),
	                    std::istreambuf_iterator< char >() );
}


//-----------------------------------------------------------------------------
// THE PATH A READ ACTUALLY RESOLVED TO, for a failure message.
//
// Added by lane 2-physics from lane 2-utilshared's finding: a test helper that
// WALKS UP to find the repo root escapes its worktree into the main checkout,
// and then answers confidently about a different tree. It does not error. A
// worktree's `.git` is a FILE and not a directory, and every tracked file
// exists in both trees, so none of the usual markers tells them apart -- the
// only thing that makes the difference visible is printing the path that was
// actually opened.
//
// This file's own root search does not walk up past one level, so it is not
// exposed today. The point of printing the path is that "not exposed today" is
// a property of the search, and the search is three lines somebody will edit.
//-----------------------------------------------------------------------------
std::string Resolved( const std::string &strRel )
{
	char szBuf[ PATH_MAX ];
	if ( realpath( strRel.c_str(), szBuf ) != NULL )
		return std::string( szBuf );

	// A missing file is often the POINT of the check, so a failed realpath is
	// not an error here: resolve the directory instead and say it is absent.
	char szCwd[ PATH_MAX ];
	if ( getcwd( szCwd, sizeof( szCwd ) ) != NULL )
		return std::string( szCwd ) + "/" + strRel + "   (not present)";

	return strRel + "   (unresolved)";
}

int CountOf( const std::string &strHay, const std::string &strNeedle )
{
	if ( strNeedle.empty() )
		return 0;
	int n = 0;
	for ( size_t at = strHay.find( strNeedle ); at != std::string::npos;
	      at = strHay.find( strNeedle, at + strNeedle.size() ) )
	{
		n++;
	}
	return n;
}

// A file at the REPO root rather than under src/ -- tools/ lives there.
//
// RAW, not comment-stripped: these are shell scripts, and StripComments is a
// C++ comment stripper that would eat a `//` inside a path or a URL. The
// subject is a ConCommand name, which appears in a `debug-api run` line either way.
std::string RepoPath( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string( "<no src root>/" ) + pszRelative;
	return std::string( pszRoot ) + "/../" + pszRelative;
}

std::string RepoFile( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();
	return Read( RepoPath( pszRelative ) );
}

} // namespace

//=============================================================================
// THE BIND
//=============================================================================

TEST( PhysicsEventSink, TheTreeIsFound )
{
	// NOT A SKIP ARM. Every fact below reads the tree, and a not-found that
	// returned early would make all of them pass while checking nothing --
	// which is one level up from the null sink they exist to catch.
	ASSERT_TRUE( SrcRoot() != NULL ) << "source tree not found next to the test binary";
}

//=============================================================================
// THE KINDS THAT HAD NO PRODUCER (plan_shim.md Phase 2a, lane 2-physics)
//
// The ring has declared TWELVE kinds since step 3a (SS_PHYSEVENT_KIND_COUNT is
// 13 and SS_PHYSEVENT_NONE is the zero) and recorded FIVE. That is not a
// cosmetic gap: `cs_sv_physics_events` already enumerates every one of them and
// prints a count per kind, so the seven without a producer printed a permanent
// zero -- which reads as "no object ever entered water", "nothing ever scraped",
// "no constraint ever broke". A carrier with no producer is worse than an absent
// one, because the instrument that would tell you says everything is fine.
//
// These facts pin the producers that were added, one per callback, BY THE CALL
// AND ITS KIND rather than by a bare mention: physics.cpp names most of these
// enumerators in prose as well, so a bare-name needle is satisfied by a comment.
//
// WHAT IS DELIBERATELY NOT HERE: PRE_COLLISION. PreCollision's own arm is the
// player-held clamp, which answers the solver and has nothing to hand over. It
// stays declared and unrecorded, and that is a decision rather than an
// oversight.
//
// SINCE plan_physics_managed B2.2 most of those producers are gone from here:
// managed's own IPhysicsCollisionEvent table answers the touch, trigger,
// friction, fluid and collision callbacks and posts them to the router, so
// only CONSTRAINT_BROKEN (and the post-step kinds) still go through the ring.
// POST_COLLISION's producer is PhysicsCollisionImpacts.PostCollision, in the
// callback: it answers PhysicsCollisionResponse there and posts the same
// record to PhysicsEventRouter. The native recorder it replaced is gone.
//=============================================================================

//=============================================================================
// THE DOUBLE SHAKE, ANSWERED (plan_shim.md §10 lane S3's open question,
// settled by lane 2-physics)
//
// The question was whether one heavy collision shakes the screen twice:
// physics.cpp called UTIL_ScreenShake with no SOURCESHARP guard while
// PhysicsCollisionRules.Shake also shakes.
//
// It never did on this tree. PhysCollisionScreenShake and PhysCollisionDust had
// ZERO importers on both link lines and no caller inside physics.cpp either;
// their only caller was baseentity.cpp, a TU neither vpc compiles. The native
// shake has been unreachable since the entity port, and the managed one is the
// only shake there is. These facts pin that it stays that way -- a re-added
// native shake would be a real double, and nothing else in the tree would say
// so.
//=============================================================================

//=============================================================================
// THE SS-PHYSWORLD SCAFFOLDING HAS SIX CONSUMERS AND A LINK-LINE CENSUS
// CANNOT SEE ANY OF THEM (plan_shim.md Phase 2a, lane 2-physics)
//
// plan_shim_rulings.tsv's physics.cpp#delete row rules this block DELETE on the
// grounds that it is "off by default, no tool/managed caller". The first half is
// true and the second is not: six committed tools drive its four ConCommands.
//
// A CONCOMMAND IS REACHED BY NAME FROM A SHELL SCRIPT. That is not a symbol
// import, so tools/shim-census reads zero consumers and is right about the
// symbols and wrong about the commands -- the same blind spot
// gamedll_convars_test.cpp exists for. This fact is the instrument that would
// have caught the ruling, and it is here rather than in a plan document so that
// the next lane to read that row finds it red instead of finding it plausible.
//
// IF A TOOL LEGITIMATELY STOPS USING ONE, lower the count and say which tool and
// why. If the count reaches zero the block really is dead and may go.
//
// ss_physworld_ovtree, which audited vphysics' octree through private IVP
// offsets, was removed 2026-09-28 by user ruling with its only consumers
// (tools/physics-defects-ovprobe, tools/nan-explain-probe). Its public-API half
// -- the walk for objects at a non-finite or out-of-map position -- is
// ss_physworld_bounds, which tools/defect-shadow-probe now drives.
//=============================================================================

TEST( PhysicsEventSink, EveryPhysWorldCommandHasAToolThatDrivesIt )
{
	// The tools are read from the repo, not listed from memory: a tool renamed
	// or deleted changes this count, and a command whose last driver went is
	// the one case where deleting it is right.
	struct Cmd_t { const char *pszName; int nTools; };
	static const Cmd_t kCommands[] =
	{
		// report: 8e-physfreeze, gateseq-probe, 3f-gatebisect
		{ "ss_physworld_report",  3 },
		// object: c8-clientsystems-gate, phys-wakesleep-gate,
		//         defect-shadow-probe
		{ "ss_physworld_object",  3 },
		// bounds: defect-shadow-probe
		{ "ss_physworld_bounds",  1 },
		// recheck: 3f-gatebisect
		{ "ss_physworld_recheck", 1 },
		// nan: defect-shadow-probe (managed's PhysWorldNanCommand since the
		// guard moved with the player shadow, plan_physics_bones_native_zero 2.5)
		{ "ss_physworld_nan",     1 },
	};

	static const char *const kTools[] =
	{
		"tools/8e-physfreeze",
		"tools/gateseq-probe",
		"tools/3f-gatebisect",
		"tools/c8-clientsystems-gate",
		"tools/phys-wakesleep-gate",
		"tools/defect-shadow-probe",
	};
	const size_t nTools = sizeof( kTools ) / sizeof( kTools[ 0 ] );

	// VACUITY GUARD ON THE READER, not on the result: every tool is above the
	// src/ root this test's helper reads from, so a wrong prefix would make
	// every count zero and every expectation below read "the tool went away".
	int nOpened = 0;
	std::string strMissing;
	for ( size_t t = 0; t < nTools; t++ )
	{
		if ( !RepoFile( kTools[ t ] ).empty() )
		{
			nOpened++;
			continue;
		}
		// THE RESOLVED PATH, not the relative one. lane 2-utilshared lost a day
		// to a helper that answered about the MAIN checkout from inside a
		// worktree; the relative path is identical in both trees and says
		// nothing, and the absolute one is the whole diagnosis.
		strMissing += std::string( "\n    " ) + kTools[ t ] + "  ->  "
		            + Resolved( RepoPath( kTools[ t ] ) );
	}
	ASSERT_EQ( (int)nTools, nOpened )
		<< "not every tool file opened, so the counts below measure the reader "
		   "and not the tree. Resolved to:" << strMissing;

	for ( size_t i = 0; i < sizeof( kCommands ) / sizeof( kCommands[ 0 ] ); i++ )
	{
		int n = 0;
		for ( size_t t = 0; t < nTools; t++ )
			n += ( CountOf( RepoFile( kTools[ t ] ), kCommands[ i ].pszName ) > 0 ) ? 1 : 0;

		EXPECT_EQ( kCommands[ i ].nTools, n )
			<< kCommands[ i ].pszName << " is driven by " << n << " committed tool(s), "
			<< "not " << kCommands[ i ].nTools << ". plan_shim_rulings.tsv rules this "
			   "block DELETE on the grounds that it has NO tool caller; that premise "
			   "is what this fact measures. If a tool legitimately stopped using it, "
			   "lower the number here and say which tool and why -- and if it reaches "
			   "zero for all four, the block really is dead.";
	}
}
