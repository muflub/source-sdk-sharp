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

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	bool bInLine  = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( bInLine )
		{
			if ( strIn[ i ] == '\n' )
			{
				bInLine = false;
				out += '\n';
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			bInLine = true;
			i++;
			continue;
		}
		out += strIn[ i ];
	}
	return out;
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

std::string SrcPath( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string( "<no src root>/" ) + pszRelative;
	return std::string( pszRoot ) + "/" + pszRelative;
}

std::string Code( const char *pszRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();
	return StripComments( Read( SrcPath( pszRelative ) ) );
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

// The one production binder. Named here rather than searched for, because the
// point of the fact below is that it is exactly one file and exactly this one.
const char *kBinder = "sourcesharp/game/server/shell_serversystems.cpp";

// Every file that could plausibly hold the bind or a second one. The scan is a
// LIST rather than a walk of the tree on purpose: a walk that found nothing
// because it walked the wrong directory would pass, and "vpc $File has two
// spellings" is this project's own record of that shape.
const char *kProductionFiles[] =
{
	"sourcesharp/game/server/shell_serversystems.cpp",
	"sourcesharp/game/server/physics_shell.cpp",
	"sourcesharp/game/server/physics_shell.h",
	"sourcesharp/game/server/shell_frame.cpp",
	"sourcesharp/game/server/shell_level.cpp",
	"sourcesharp/game/server/shell_gamedll.cpp",
	"sourcesharp/game/server/physics.cpp",
};
const int kProductionFileCount =
	(int)( sizeof( kProductionFiles ) / sizeof( kProductionFiles[ 0 ] ) );

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

TEST( PhysicsEventSink, NothingBindsASinkAnyMore )
{
	// plan_physics_bones_native_zero 2.3: the ring's last producer was
	// PhysFrame, and the physics frame is managed's PhysicsFrame, which posts
	// the step's events to the router with no crossing. The bind went with it;
	// a bind restored here would drain a ring nothing fills.
	int nBinds = 0;
	for ( int i = 0; i < kProductionFileCount; i++ )
		nBinds += CountOf( Code( kProductionFiles[ i ] ), "PhysicsShell().SetSink(" );

	EXPECT_EQ( 0, nBinds );
	EXPECT_EQ( 0, CountOf( Code( kBinder ), "PhysicsEvents(" ) );
}

TEST( PhysicsEventSink, TheHooksFramePositionIsManagedsAndTheForwardIsGone )
{
	// CPhysicsHook's FrameUpdatePostEntityThink only forwarded to managed's
	// PhysicsFrame through IManagedPhysicsFrame::Frame. Since kServerSystems[]'s
	// order went managed (plan_server_native_v2 T4e) ServerSystems runs the
	// step at the row's position itself, so the forward, its slot and the
	// round trip are gone; the area's world build and sound precache stay.
	const std::string strPhysics = Code( "sourcesharp/game/server/physics.cpp" );
	const std::string strSystems = Code( "sourcesharp/managed/SourceSharp.Server/ServerSystems.cs" );
	ASSERT_FALSE( strPhysics.empty() );
	ASSERT_FALSE( strSystems.empty() );

	EXPECT_EQ( 0, CountOf( strPhysics, "FrameUpdatePostEntityThink" ) );
	EXPECT_EQ( 0, CountOf( strPhysics, "->Frame();" ) );
	EXPECT_EQ( 1, CountOf( strPhysics, "IManagedPhysicsFrame *pPhysicsFrame = ManagedPhysicsFrame();" ) )
		<< "the control: the area the level start asks is still read by the same needle shape";

	// And the step runs in the post-think pass, before the managed frame.
	const size_t physics = strSystems.find( "managed.PhysicsFrame();" );
	const size_t frame = strSystems.find( "managed.Frame(frameTime);" );
	ASSERT_NE( physics, std::string::npos );
	ASSERT_NE( frame, std::string::npos );
	EXPECT_LT( physics, frame );
}

TEST( PhysicsEventSink, ThePhysicsFramesDecisionsAreManaged )
{
	// Whether to step, how long, the timescale's clock reset and what the step
	// tells the game: PhysicsFrame's. Native carries out only the owners'
	// transform update (SourceSharp_PhysicsUpdateOwners).
	const std::string strPhysics = Code( "sourcesharp/game/server/physics.cpp" );
	ASSERT_FALSE( strPhysics.empty() );

	EXPECT_EQ( 1, CountOf( strPhysics, "int32_t SourceSharp_PhysicsUpdateOwners(" ) );
	static const char *const kGone[] =
	{
		"void PhysFrame(",
		"PhysTimescale(",
		"ClampStep(",
		"->Simulate(",
		"ResetSimulationClock(",
		"\"phys_speeds\"",
		"\"phys_timescale\"",
		"m_isFinalTick",
		"SS_PHYSEVENT_UPDATE",
		"SS_PHYSEVENT_SHADOW_UPDATE",
	};
	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
		EXPECT_EQ( 0, CountOf( strPhysics, kGone[ i ] ) ) << kGone[ i ] << " is back in physics.cpp";
}

TEST( PhysicsEventSink, TheRingItsSinkAndTheIdTableAreGone )
{
	// Nothing records into the ring any more (the frame is managed's), so the
	// queue, its sink, its report and step 3b's id table went with it. The
	// presence half: the environment the header still creates, and the
	// managed frame that posts the step's events now.
	const std::string strHeader = Code( "sourcesharp/game/server/physics_shell.h" );
	ASSERT_FALSE( strHeader.empty() );
	static const char *const kGone[] =
	{
		"class SourceSharpPhysicsEventRing",
		"class SourceSharpPhysicsIdTable",
		"class CSourceSharpPhysicsShell",
		"PhysicsShell()",
		"SourceSharp_RecordPhysicsEvent(",
		"SourceSharp_PhysicsEventObject(",
		"SourceSharpPhysicsEventSink_t",
		"SS_PHYSID_",
		"SS_PHYSEVENT_",
		"ClampStep(",
	};
	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
		EXPECT_EQ( 0, CountOf( strHeader, kGone[ i ] ) ) << kGone[ i ] << " is back in physics_shell.h";
	EXPECT_EQ( 1, CountOf( strHeader, "class CSourceSharpPhysicsEnvironment" ) );

	EXPECT_TRUE( Code( "sourcesharp/game/server/physics_shell.cpp" ).empty() )
		<< "physics_shell.cpp is back: its only content was the ring's and the ids' layout";
	// physics_debug.cpp, which carried cs_sv_physics_shell until the ring
	// went, is itself gone (2026-10-18: its commands are managed's).
	EXPECT_TRUE( Code( "sourcesharp/game/server/physics_debug.cpp" ).empty() );
	for ( int i = 0; i < kProductionFileCount; i++ )
		EXPECT_EQ( 0, CountOf( Code( kProductionFiles[ i ] ), "PhysicsShell()" ) ) << kProductionFiles[ i ];

	const std::string strFrame = Code( "sourcesharp/managed/SourceSharp.Server/PhysicsFrame.cs" );
	ASSERT_FALSE( strFrame.empty() );
	EXPECT_GE( CountOf( strFrame, "_post(new PhysicsEvent" ), 3 );
}

TEST( PhysicsEventSink, TheFrameAreaIsAcquiredOrTheServerWillNotRun )
{
	// The load refuses unless the managed root serves IManagedPhysicsFrame at
	// its compiler's count; the retired events area is not asked for.
	const std::string strAcquire = Code( "sourcesharp/game/shared/shell_managed_areas.inc" );
	EXPECT_EQ( 1, CountOf( strAcquire, "ManagedAccept< IManagedPhysicsFrame >" ) );
	EXPECT_EQ( 0, CountOf( strAcquire, "IManagedPhysicsEvents" ) );
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

TEST( PhysicsEventSink, ConstraintBrokenHasAProducer )
{
	// It had none, then native's CPhysConstraintEvents pushed it into the ring.
	// Since plan_physics_bones_native_zero 2.2 the callback is managed's own
	// table (libss_fwd's IPhysicsConstraintEvent forwarding to
	// IManagedPhysicsConstraintEvent since plan_forwarders_everywhere.md
	// phase 5): PhysicsConstraints.Broken finds the row
	// by its pointer and posts ConstraintBroken (the id, both ends) to the
	// router. The retention half: that post, by its construct.
	const std::string strManaged = Code( "sourcesharp/managed/SourceSharp.Server/PhysicsConstraints.cs" );
	const std::string strBinding = Code( "sourcesharp/managed/SourceSharp.Server.Interop/PhysicsConstraintEventBinding.cs" );

	EXPECT_EQ( 1, CountOf( strManaged, "KindRaw = (uint)PhysicsEventKind.ConstraintBroken," ) );
	EXPECT_EQ( 1, CountOf( strBinding, "[ManagedVtable(typeof(ManagedPhysicsConstraintEventTable))]" ) );
}

TEST( PhysicsEventSink, TheNativeConstraintRecorderAndHandlerAreGone )
{
	// The removal half: the ring push, its native caller and the handler
	// physics.cpp handed the environment.
	const std::string strHeader  = Code( "sourcesharp/game/server/physics_shell.h" );
	const std::string strPhysics = Code( "sourcesharp/game/server/physics.cpp" );

	ASSERT_FALSE( strHeader.empty() );
	ASSERT_FALSE( strPhysics.empty() );
	EXPECT_EQ( 0, CountOf( strHeader, "inline bool SourceSharp_RecordConstraintBroken(" ) );
	EXPECT_EQ( 0, CountOf( strPhysics, "g_pConstraintEvents" ) );
	EXPECT_TRUE( Code( "sourcesharp/game/server/shell_constraints.cpp" ).empty() )
		<< "shell_constraints.cpp is back";
}

TEST( PhysicsEventSink, TheMotionEventIsManagedAndTheNativeControllersAreGone )
{
	// vphysics asks IMotionEvent::Simulate mid-step for every controller's
	// force. Since plan_physics_bones_native_zero 2.4 that is managed's own
	// table (MotionEventBinding, one block for every controller, behind
	// libss_fwd's IMotionEvent since plan_forwarders_everywhere.md phase 5),
	// answered by
	// MotionSimulation; native's four CSourceSharpMotionEvent classes and their
	// table left with shell_motioncontrollers.cpp.
	const std::string strBinding = Code( "sourcesharp/managed/SourceSharp.Server.Interop/MotionEventBinding.cs" );
	ASSERT_FALSE( strBinding.empty() );
	EXPECT_EQ( 1, CountOf( strBinding, "[ManagedVtable(typeof(ManagedMotionEventTable))]" ) );
	EXPECT_EQ( 1, CountOf( Code( "sourcesharp/managed/SourceSharp.Server/MotionSimulation.cs" ),
	                       "public static MotionSimResult Motor(" ) );

	EXPECT_TRUE( Code( "sourcesharp/game/server/shell_motioncontrollers.cpp" ).empty() )
		<< "shell_motioncontrollers.cpp is back";
	EXPECT_EQ( 0, CountOf( Code( "sourcesharp/game/shared/sourcesharp_gamesystem.cpp" ),
	                       "SourceSharp_MotionControllersLevelShutdown" ) );
}

TEST( PhysicsEventSink, NoCallbackKindIsRecordedNatively )
{
	// The count, as a measurement rather than a list, so that a producer
	// restored in passing shows up here. FIVE before lane 2-physics; ELEVEN
	// after; NINE since plan_physics_managed B2.1 (WAKE and SLEEP moved to
	// managed's IPhysicsObjectEvent table); ONE since B2.2, when managed's
	// IPhysicsCollisionEvent table took the touch, trigger, friction, fluid
	// and collision callbacks and native's bodies were deleted.
	// NONE since plan_physics_bones_native_zero 2.2: CONSTRAINT_BROKEN is
	// posted by managed's IPhysicsConstraintEvent table (above).
	const std::string strPhysics = Code( "sourcesharp/game/server/physics.cpp" );
	const std::string strHeader  = Code( "sourcesharp/game/server/physics_shell.h" );

	static const char *const kMoved[] =
	{
		"SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_START_TOUCH,",
		"SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_END_TOUCH,",
		"SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_FRICTION,",
		"SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_FLUID_START,",
		"SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_FLUID_END,",
		"SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_ENTER_TRIGGER,",
		"SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_LEAVE_TRIGGER,",
		"SourceSharp_RecordPhysicsCollision(",
	};
	for ( size_t i = 0; i < sizeof( kMoved ) / sizeof( kMoved[ 0 ] ); i++ )
	{
		EXPECT_EQ( 0, CountOf( strPhysics, kMoved[ i ] ) ) << kMoved[ i ] << " is back in physics.cpp";
		EXPECT_EQ( 0, CountOf( strHeader, kMoved[ i ] ) ) << kMoved[ i ] << " is back in physics_shell.h";
	}

	EXPECT_EQ( 0, CountOf( strHeader, "ev.nKind    = SS_PHYSEVENT_CONSTRAINT_BROKEN;" ) );
}

TEST( PhysicsEventSink, TheCollisionTailNamesTheProducerThatFillsIt )
{
	// RAW, not comment-stripped: the subject is the header's own account of
	// who fills the collision tail. It named SourceSharp_RecordPhysicsCollision,
	// a recorder B2.2 deleted (OfTheCallbackKindsOnlyConstraintBrokenIsRecordedNatively
	// pins the absence of the call), so a reader was sent to a function that
	// does not exist for the one producer that does.
	const std::string strPath   = SrcPath( "sourcesharp/game/server/physics_shell.h" );
	const std::string strHeader = Read( strPath );
	ASSERT_FALSE( strHeader.empty() ) << "cannot read " << Resolved( strPath );

	EXPECT_EQ( 0, CountOf( strHeader, "SourceSharp_RecordPhysicsCollision" ) )
		<< "physics_shell.h names a collision recorder that does not exist";
	// The record itself left the header with the ring (plan_physics_bones_native_zero
	// 2.3); managed's PhysicsEvent is the one definition, and the producer is below.
	EXPECT_EQ( 0, CountOf( strHeader, "SourceSharpPhysicsEvent_s" ) )
		<< "physics_shell.h declares the event record again";

	// The retention half: the producer the header names exists, fills the
	// kind, and hands the record to both consumers.
	const std::string strImpacts = Code( "sourcesharp/managed/SourceSharp.Server/PhysicsCollisionImpacts.cs" );
	ASSERT_FALSE( strImpacts.empty() ) << "cannot read PhysicsCollisionImpacts.cs";
	EXPECT_EQ( 1, CountOf( strImpacts, "KindRaw = (uint)PhysicsEventKind.PostCollision," ) );
	EXPECT_EQ( 1, CountOf( strImpacts, "response.Respond(in record);" ) );
	EXPECT_EQ( 1, CountOf( strImpacts, "post(record);" ) );
}

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

TEST( PhysicsEventSink, TheNativeCollisionShakeIsGone )
{
	const std::string strPhysics = Code( "sourcesharp/game/server/physics.cpp" );
	ASSERT_FALSE( strPhysics.empty() )
		<< "physics.cpp did not open, so the absences below prove nothing. "
		<< "Resolved to: " << Resolved( SrcPath( "sourcesharp/game/server/physics.cpp" ) );

	// The CALL, not the function name: the comment block that records why the
	// body went names the function, and a bare-name needle would be satisfied
	// by it forever.
	EXPECT_EQ( 0, CountOf( strPhysics, "UTIL_ScreenShake(" ) )
		<< "physics.cpp shakes the screen natively again. PhysicsCollisionRules"
		   ".Shake already answers this, so a heavy collision would now shake "
		   "twice -- which is the defect this deletion closed.";
	EXPECT_EQ( 0, CountOf( strPhysics, "void PhysCollisionScreenShake(" ) );
	EXPECT_EQ( 0, CountOf( strPhysics, "void PhysCollisionDust(" ) );
}

TEST( PhysicsEventSink, TheThreeShakeConVarsLeftWithTheirOnlyReader )
{
	// A ConVar whose only reader is gone is worse than no ConVar: an operator
	// who sets it gets no effect and no error.
	const std::string strPhysics = Code( "sourcesharp/game/server/physics.cpp" );
	EXPECT_EQ( 0, CountOf( strPhysics, "ConVar collision_shake_amp(" ) );
	EXPECT_EQ( 0, CountOf( strPhysics, "ConVar collision_shake_freq(" ) );
	EXPECT_EQ( 0, CountOf( strPhysics, "ConVar collision_shake_time(" ) );
}

TEST( PhysicsEventSink, TheManagedRuleStillCarriesEveryNumberThatLeft )
{
	// THE OTHER HALF, and without it the three absences above are just a
	// deletion. Each of these is one of stock's numbers, cited in
	// PhysicsCollisionRules.cs against the physics.cpp line it came from.
	const std::string strRules =
		Code( "sourcesharp/managed/SourceSharp.Server/PhysicsCollisionRules.cs" );
	ASSERT_FALSE( strRules.empty() ) << "PhysicsCollisionRules.cs did not open";

	EXPECT_EQ( 1, CountOf( strRules, "public const float ShakeAmplitudeScale = 0.2f;" ) );
	EXPECT_EQ( 1, CountOf( strRules, "public const float ShakeFrequency = 0.5f;" ) );
	EXPECT_EQ( 1, CountOf( strRules, "public const float ShakeDuration = 0.5f;" ) );
	EXPECT_EQ( 1, CountOf( strRules, "public const float ShakeMinSpeed = 30.0f;" ) );
	EXPECT_EQ( 1, CountOf( strRules, "public const float ShakeMinDelta = 0.25f;" ) );
	EXPECT_EQ( 1, CountOf( strRules, "public const float DustSoftMinSpeed = 200.0f;" ) );
	EXPECT_EQ( 1, CountOf( strRules, "public const float DustConcreteMinSpeed = 340.0f;" ) );
	EXPECT_EQ( 1, CountOf( strRules, "public const float DustSize = 8.0f;" ) );
}

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

// Diagnostics, managed debug commands since 2026-10-18 (PhysicsDebugCommands.cs
// and PhysicsDiagnostics.cs, under #if DEBUG): physics_debug.cpp, their native
// home from 2026-10-01, is gone. The tools that drive them need a debug build.
TEST( PhysicsEventSink, TheFourPhysWorldCommandsStillExist )
{
	const std::string strHost = Code( "sourcesharp/game/server/physics.cpp" );
	ASSERT_FALSE( strHost.empty() )
		<< "physics.cpp did not open, so the absence below means nothing. Resolved to: "
		<< Resolved( SrcPath( "sourcesharp/game/server/physics.cpp" ) );
	EXPECT_TRUE( Code( "sourcesharp/game/server/physics_debug.cpp" ).empty() )
		<< "physics_debug.cpp is back; its commands are managed's";

	const std::string strManaged = Read( SrcPath( "sourcesharp/managed/SourceSharp.Server/PhysicsDiagnostics.cs" ) );
	ASSERT_FALSE( strManaged.empty() ) << "PhysicsDiagnostics.cs did not open";
	EXPECT_EQ( 1, CountOf( strManaged, "[ConCommand(\"ss_physworld_report\"" ) );
	const std::string strProbes = Read( SrcPath( "sourcesharp/managed/SourceSharp.Server/PhysicsDebugCommands.cs" ) );
	ASSERT_FALSE( strProbes.empty() ) << "PhysicsDebugCommands.cs did not open";
	EXPECT_EQ( 1, CountOf( strProbes, "[ConCommand(\"ss_physworld_recheck\"" ) );
	EXPECT_EQ( 1, CountOf( strProbes, "[ConCommand(\"ss_physworld_bounds\"" ) );
	EXPECT_EQ( 1, CountOf( strProbes, "[ConCommand(\"ss_physworld_object\"" ) );
	// The per-pid file every one of them writes, which the tools read.
	EXPECT_EQ( 1, CountOf( Read( SrcPath( "sourcesharp/managed/SourceSharp.Server/PhysWorldReport.cs" ) ),
	                       "\"/tmp/ss-physworld-" ) );
}

TEST( PhysicsEventSink, TheNanLedgerIsManagedAndNativesIsGone )
{
	// The guard moved with the player shadow (plan_physics_bones_native_zero
	// 2.5), so native's ledger was fed by nothing and ss_physworld_bounds
	// printed refusals=0 whatever happened. The line is managed's
	// ss_physworld_nan.
	const std::string strHeader = Code( "sourcesharp/game/server/physics_shell.h" );
	ASSERT_FALSE( strHeader.empty() );
	EXPECT_EQ( 0, CountOf( strHeader, "PhysicsNanLedger" ) );
	EXPECT_EQ( 0, CountOf( strHeader, "SourceSharpAcceptPhysicsVector" ) );
	EXPECT_EQ( 0, CountOf( strHeader, "SS_PHYSNAN_" ) );
	// And the finite test the bounds walk asked went with the walk (2026-10-18:
	// ss_physworld_bounds is managed's, over PhysicsNanGuard.IsFinite).
	EXPECT_EQ( 0, CountOf( strHeader, "SourceSharpIsFinitePhysicsVector" ) );

	const std::string strManaged = Read( SrcPath( "sourcesharp/managed/SourceSharp.Server/PhysicsDiagnostics.cs" ) );
	ASSERT_FALSE( strManaged.empty() );
	EXPECT_EQ( 1, CountOf( strManaged, "[ConCommand(\"ss_physworld_nan\"" ) );
	EXPECT_EQ( 1, CountOf( strManaged, "$\"[SS-PHYSWORLD-NAN] refusals={guard.Refusals} firstSite={guard.FirstSite}" ) );
}

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
