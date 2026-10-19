//========= SourceSharp ========================================================//
//
// physics_move_scope_test.cpp -- plan_shim.md 4a item 1, lane 5a-physresidue.
//
// WHAT THIS PINS, AND WHY A DOCUMENT NEEDS PINNING AT ALL.
//
// docs/physics-move-scope.md is a SCOPE: it rules, function by function, on what
// can leave game/server/physics.cpp and what cannot. Nothing was executed from
// it. A scope is only worth the measurement it was taken on, and the whole
// reason it exists is that the previous figure -- "~1,011 lines" -- was quoted
// from phase to phase long after the file it described had changed underneath
// it. A document nothing checks decays into exactly that.
//
// So every number in it that CAN be re-derived from the tree is re-derived here,
// and the document is required to agree. What cannot be re-derived from source
// -- the link-line importer counts, which need nm over 108 objects -- is not
// asserted here and says so in the document instead.
//
// A FAILURE HERE IS NOT NECESSARILY A BUG. If a later lane executes part of the
// scope, several of these go red, and that is the point: the scope is then stale
// and must be re-measured rather than read. Each message says which.
//
//=============================================================================//
#include <gtest/gtest.h>
#include "ss_ci_skip.h"

#include <fstream>
#include <cstdio>
#include <sstream>
#include <string>
#include <sys/stat.h>
#include <sys/wait.h>

namespace
{

// The tests run with the working directory at the tree's src/ -- buildcpptests
// pushd's to its own directory and runs the binary from there.
std::string ReadRel( const char *pszRelative )
{
	std::ifstream in( pszRelative );
	std::ostringstream ss;
	ss << in.rdbuf();
	return ss.str();
}

// Comment-stripped, line count preserved is NOT needed here -- only the text --
// so this collapses // and /* */ without keeping newlines aligned.
std::string Strip( const std::string &in )
{
	std::string out;
	bool block = false;
	for ( size_t i = 0; i < in.size(); )
	{
		if ( block )
		{
			if ( in.compare( i, 2, "*/" ) == 0 ) { block = false; i += 2; }
			else { if ( in[ i ] == '\n' ) out += '\n'; i++; }
			continue;
		}
		if ( in.compare( i, 2, "//" ) == 0 )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			continue;
		}
		if ( in.compare( i, 2, "/*" ) == 0 ) { block = true; i += 2; continue; }
		out += in[ i ];
		i++;
	}
	return out;
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t p = hay.find( needle ); p != std::string::npos;
	      p = hay.find( needle, p + needle.size() ) )
	{
		n++;
	}
	return n;
}

int CountLines( const std::string &s )
{
	if ( s.empty() )
		return 0;
	int n = 0;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		if ( s[ i ] == '\n' )
			n++;
	}
	// A trailing line with no newline still counts.
	return s[ s.size() - 1 ] == '\n' ? n : n + 1;
}

const char *const kDoc      = "../docs/physics-move-scope.md";
const char *const kPhysics  = "sourcesharp/game/server/physics.cpp";
// The diagnostic console commands' home since 2026-10-01 (user ruling: debug
// commands live in debug files that only a debug build compiles).
const char *const kPhysicsDebug = "sourcesharp/game/server/physics_debug.cpp";
// CCollisionEvent's header, deleted with the handler at
// plan_physics_bones_native_zero 2.1: vphysics' solver and collision events
// are managed's own Valve-layout tables since.
const char *const kEventHdr = "sourcesharp/game/server/ss_physics_collisionevent.h";
const char *const kSolverBinding =
	"sourcesharp/managed/SourceSharp.Server.Interop/PhysicsCollisionSolverBinding.cs";
const char *const kEventBinding =
	"sourcesharp/managed/SourceSharp.Server.Interop/PhysicsCollisionEventBinding.cs";
const char *const kShellHdr = "sourcesharp/game/server/physics_shell.h";
// shell_constraints.cpp, deleted at plan_physics_bones_native_zero 2.4: the
// constraint break is managed's own IPhysicsConstraintEvent table since 2.2.
const char *const kConstr   = "sourcesharp/game/server/shell_constraints.cpp";
const char *const kConstraintsCs =
	"sourcesharp/managed/SourceSharp.Server/PhysicsConstraints.cs";
const char *const kResponse =
	"sourcesharp/managed/SourceSharp.Server/PhysicsCollisionResponse.cs";
const char *const kRules =
	"sourcesharp/managed/SourceSharp.Server/PhysicsCollisionRules.cs";
// The physics diagnostics, managed since plan_physics_bones_native_zero 2.3:
// ss_physworld_report, physics_budget and air_density, and the report itself.
const char *const kDiagnosticsCs =
	"sourcesharp/managed/SourceSharp.Server/PhysicsDiagnostics.cs";
const char *const kReportCs =
	"sourcesharp/managed/SourceSharp.Server/PhysWorldReport.cs";

std::string Doc()
{
	static const std::string s = ReadRel( kDoc );
	return s;
}

std::string PhysicsCode()
{
	static const std::string s = Strip( ReadRel( kPhysics ) );
	return s;
}

std::string PhysicsDebugCode()
{
	static const std::string s = Strip( ReadRel( kPhysicsDebug ) );
	return s;
}

} // namespace

//=============================================================================
// 1. Is the document about the file it thinks it is?
//=============================================================================

TEST( PhysicsMoveScope, TheDocumentIsReadable )
{
	// Without this every absence check below would pass against an empty
	// string, which is the vacuous-pass shape this directory exists to catch.
	ASSERT_FALSE( Doc().empty() )
		<< "cannot read " << kDoc << " -- every fact in this file would pass "
		   "vacuously. The tests run from the tree's src/.";
	ASSERT_FALSE( PhysicsCode().empty() )
		<< "cannot read " << kPhysics;
}

TEST( PhysicsMoveScope, TheFileIsStillTheSizeTheScopeWasTakenOn )
{
	// The scope is a per-function ruling over a file of a particular shape. If
	// the file has changed, the rulings have not been re-derived and the
	// document is a historical record rather than a plan.
	const int nLines = CountLines( ReadRel( kPhysics ) );
	EXPECT_EQ( 547, nLines )
		<< "game/server/physics.cpp is now " << nLines << " lines, not the "
		   "547 docs/physics-move-scope.md was measured on. RE-MEASURE the "
		   "scope (tools/physics-scope/fnmap, then tools/physics-scope/classify)"
		   " and update the document's section 1 and its totals table; do not "
		   "just edit the number. It was 4,291 until plan_shim.md \u00a78's "
		   "reason-letter marker added one COMMENT line, and 4,292 until lane "
		   "g-vdisp's virtual-mesh skip added 18 lines to one INSTRUMENT "
		   "function, and 4,310 until the attribution header replaced the "
		   "banner when the file came into this repository, and 4,309 until "
		   "the octree audit over private IVP offsets was removed, and 4,172 "
		   "until the commands and ConVars were declared in C# "
		   "(plan_server_novalve.md P2.3c), and 4,188 until the surface "
		   "property manifest moved to C# (P2.4), and 4,191 until the string "
		   "calls moved onto ss_string.h, and 4,192 until VProf was "
		   "deleted (P4), and 4,189 until the queued physics sounds moved onto "
		   "ss:: filters (P5), and 4,264 until the server gc cull deleted the "
		   "34 functions server.so's link dropped, and 3,663 until the "
		   "diagnostic console commands moved to physics_debug.cpp, and "
		   "2,875 until contact touch was ported (physics stock parity E1), "
		   "and 2,773 until PhysFrame recorded the per-frame update (B4b), "
		   "and 2,783 until ShouldCollide read the owner and hierarchy facts "
		   "(step 4), and 2,807 until the penetrate ledger came back (H), and "
		   "2,917 until ShouldFreezeObject crushed frozen debris (G), and 2,936 "
		   "until the impact and break sound slots (C3/L), and 2,970 until "
		   "PostCollision recorded the motion tail (C6), and 2,982 until it "
		   "recorded the solver's collision verdicts (C6, 8e), and 2,984 until "
		   "the fluid records carried the object's material (I2), and 2,985 until "
		   "PhysFrame recorded the players' shadow updates (B7), and 3,029 "
		   "until the penetrate ledger handed stuck debris to the entity "
		   "solver (plan_physics_managed A9), and 3,059 until IPhysicsObjectEvent "
		   "moved to managed (B2.1), and 2,994 until nine IPhysicsCollisionEvent "
		   "slots moved to managed (B2.2), and 2,192 until CCollisionEvent was "
		   "deleted, its solver half and PostSimulationFrame managed "
		   "(plan_physics_bones_native_zero 2.1), and 1,353 until the ask "
		   "ledger left the report (2.1), and 1,279 until the native constraint "
		   "event handler's declaration left (2.4), and 1,278 until the unused "
		   "CSkipKeys left (2.2), and 1,269 until the SS-PHYSWORLD report, "
		   "physics_budget's sleep and air_density moved to managed (2.3), and "
		   "832 until the queued impact and break sounds did (2.3), and 698 "
		   "until PhysFrame's decisions did (2.3), and 551 until the stress "
		   "walk's ring write left with it (2.3), and 544 until the event ring "
		   "went (2.3), and 534 until physics_shared.cpp's server half did "
		   "(2.6), and 538 until the owner batch held the velocity managed "
		   "hands over (plan_physics_bones_perf C2); all thirty-five re-runs are "
		   "recorded "
		   "in the document's section 1.";
	EXPECT_NE( Doc().find( "547 raw / 188 code" ), std::string::npos )
		<< "the document no longer states the line split it was measured on";
}

//=============================================================================
// 2. The sixteen slots. This is the number the plan got wrong, so it is the
//    one most worth deriving rather than quoting.
//=============================================================================

TEST( PhysicsMoveScope, NoVphysicsSlotIsAnsweredInPhysicsCpp )
{
	// THE FOURTEEN SLOTS LEFT NATIVE. IPhysicsObjectEvent at B2.1, nine
	// IPhysicsCollisionEvent slots at B2.2, and the four
	// IPhysicsCollisionSolver slots, AdditionalCollisionChecksThisTick and
	// PostSimulationFrame at plan_physics_bones_native_zero 2.1, when
	// CCollisionEvent and its header were deleted. Each slot is asserted on
	// the managed table that answers it now, and its native body absent.
	const std::string code = PhysicsCode();
	std::ifstream gone( kEventHdr );
	EXPECT_FALSE( gone.good() )
		<< kEventHdr << " is back: native's collision handler was deleted.";

	const std::string solver = ReadRel( kSolverBinding );
	const std::string events = ReadRel( kEventBinding );
	ASSERT_FALSE( solver.empty() ) << "cannot read " << kSolverBinding;
	ASSERT_FALSE( events.empty() ) << "cannot read " << kEventBinding;

	static const char *kSolverSlots[] = {
		"public int ShouldCollide(", "public int ShouldSolvePenetration(",
		"public byte ShouldFreezeObject(", "public int AdditionalCollisionChecksThisTick(",
		"public byte ShouldFreezeContacts(",
	};
	for ( size_t i = 0; i < sizeof( kSolverSlots ) / sizeof( kSolverSlots[ 0 ] ); i++ )
		EXPECT_EQ( 1u, CountOf( solver, kSolverSlots[ i ] ) ) << kSolverSlots[ i ] << " left the solver table";

	static const char *kEventSlots[] = {
		"public void PreCollision(", "public void PostCollision(", "public void Friction(",
		"public void StartTouch(", "public void EndTouch(", "public void FluidStartTouch(",
		"public void FluidEndTouch(", "public void PostSimulationFrame()",
		"public void ObjectEnterTrigger(", "public void ObjectLeaveTrigger(",
	};
	for ( size_t i = 0; i < sizeof( kEventSlots ) / sizeof( kEventSlots[ 0 ] ); i++ )
		EXPECT_EQ( 1u, CountOf( events, kEventSlots[ i ] ) ) << kEventSlots[ i ] << " left the event table";

	EXPECT_EQ( 0u, CountOf( code, "CCollisionEvent" ) )
		<< "physics.cpp names CCollisionEvent again";
	EXPECT_EQ( 0u, CountOf( code, "IPhysicsCollisionSolver" ) )
		<< "physics.cpp implements or installs a native collision solver again";

	EXPECT_NE( Doc().find( "0 are vtable slots" ), std::string::npos )
		<< "the document no longer states that no vtable slot is answered here";
}

TEST( PhysicsMoveScope, TheTwoTwinHeadersLeftWithTheirGtests )
{
	// penetration_ledger.h (H) and find_max_contact.h (G) were the native
	// deciders whose managed twins became the live ones at
	// plan_physics_bones_native_zero 2.1 (PhysicsPenetrationLedger,
	// PhysicsMaxContact); the headers and their gtests went once nothing
	// included them, every gtest case carried into the twins' facts first.
	// The sibling is opened so the absences are about a readable directory.
	ASSERT_FALSE( ReadRel( kShellHdr ).empty() ) << "cannot read " << kShellHdr;
	static const char *kGone[] = {
		"sourcesharp/game/server/penetration_ledger.h",
		"sourcesharp/game/server/find_max_contact.h",
		"sourcesharp/test/penetration_ledger_test.cpp",
		"sourcesharp/test/find_max_contact_test.cpp",
	};
	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		std::ifstream gone( kGone[ i ] );
		EXPECT_FALSE( gone.good() ) << kGone[ i ] << " is back in the farm; its decider is managed";
	}
	EXPECT_EQ( 0u, CountOf( PhysicsCode(), "penetration_ledger.h" ) );
	EXPECT_EQ( 0u, CountOf( PhysicsCode(), "find_max_contact.h" ) );
}

TEST( PhysicsMoveScope, NoKeyHandlerIsDefinedInPhysicsCpp )
{
	// CSkipKeys, an IVPhysicsKeyHandler that skipped every key, had no user
	// (nothing handed it to a solid parse) and left at
	// plan_physics_bones_native_zero 2.2. A key handler vphysics calls during
	// a parse belongs to the managed side as a Valve-layout table; the shared
	// CSolidSetDefaults is 2.6's.
	EXPECT_EQ( 0u, CountOf( PhysicsCode(), "IVPhysicsKeyHandler" ) )
		<< "physics.cpp defines a native key handler again";
}

TEST( PhysicsMoveScope, TheAskLedgerAndTheNativePairWritesLeft )
{
	// The ask ledger recorded native ShouldCollide's answers and the pair hash
	// was what it refused by; both are managed's since
	// plan_physics_bones_native_zero 2.1 (PhysicsSolver's counts by clause,
	// EntityPairCollisions). The rule table went with the report it stayed
	// for (2.3: PhysicsSolver.AgainstTheWorld), so the environment class is
	// the presence half that proves the header read.
	const std::string shell = Strip( ReadRel( kShellHdr ) );
	ASSERT_NE( shell.find( "class CSourceSharpPhysicsEnvironment" ), std::string::npos )
		<< kShellHdr << " did not read";
	EXPECT_EQ( 0u, CountOf( shell, "SourceSharpCollisionRuleTable" ) )
		<< "the native rule table is back in " << kShellHdr;
	EXPECT_EQ( 0u, CountOf( shell, "SourceSharpCollisionAskLedger" ) )
		<< "the native ask ledger is back in " << kShellHdr;
	EXPECT_EQ( 0u, CountOf( PhysicsCode(), "PhysicsAskLedger" ) )
		<< "physics.cpp reads a native ask ledger again";

	const std::string scripted = Strip( ReadRel( "sourcesharp/game/server/shell_scripted.cpp" ) );
	ASSERT_NE( scripted.find( "SourceSharp_PhysicsWorldObject" ), std::string::npos )
		<< "shell_scripted.cpp did not read";
	EXPECT_EQ( 0u, CountOf( scripted, "SourceSharp_EntitySetPairCollisions" ) );
	EXPECT_EQ( 0u, CountOf( scripted, "SourceSharp_EntityPairCollisionsDisabled" ) );

	// The hinge's native write went with shell_constraints.cpp itself
	// (plan_physics_bones_native_zero 2.4): the managed hinge records the pair.
	EXPECT_TRUE( ReadRel( kConstr ).empty() ) << kConstr << " is back";
	const std::string managed = ReadRel( kConstraintsCs );
	ASSERT_FALSE( managed.empty() ) << "cannot read " << kConstraintsCs;
	EXPECT_EQ( 0u, CountOf( managed, "PhysDisableEntityCollisions" ) )
		<< "the managed hinge writes native's pair hash; PhysHinge records the pair";
}

//=============================================================================
// 3. The event ring: fifteen of sixteen kinds recorded, and which one is not.
//=============================================================================

TEST( PhysicsMoveScope, TheRingStillHasSixteenKinds )
{
	// Twelve until physics stock parity B4b added SS_PHYSEVENT_UPDATE, thirteen
	// until B5 added SS_PHYSEVENT_STRESS, fourteen until B7 added
	// SS_PHYSEVENT_SHADOW_UPDATE, fifteen until plan_physics_managed A9 added
	// SS_PHYSEVENT_ENTITY_SOLVER. The native enum left with the ring
	// (plan_physics_bones_native_zero 2.3); managed's PhysicsEventKind is the one
	// definition, with the same numbers.
	const std::string cs = Strip( ReadRel( "sourcesharp/managed/SourceSharp.Shared.Interop/PhysicsEvents.cs" ) );
	ASSERT_FALSE( cs.empty() ) << "cannot read SourceSharp.Shared.Interop/PhysicsEvents.cs";
	EXPECT_NE( cs.find( "EntitySolver = 16," ), std::string::npos );
	EXPECT_NE( cs.find( "Unknown = 17," ), std::string::npos )
		<< "the kind count is no longer 17 (sixteen kinds plus NONE). The "
		   "scope's \"fifteen of sixteen recorded\" is arithmetic over this.";
	EXPECT_EQ( std::string::npos, Strip( ReadRel( kShellHdr ) ).find( "SS_PHYSEVENT_KIND_COUNT" ) );
}

TEST( PhysicsMoveScope, TheSevenCallbackKindsMovedOutOfPhysicsCpp )
{
	// Recorded here exactly once each until plan_physics_managed B2.2, when
	// managed's own IPhysicsCollisionEvent table took the touch, trigger,
	// friction and fluid callbacks. Each must now be absent: a recorder back
	// here would report what managed already answers.
	const std::string code = PhysicsCode();
	static const char *kKinds[] = {
		"START_TOUCH", "END_TOUCH", "FRICTION", "FLUID_START", "FLUID_END",
		"ENTER_TRIGGER", "LEAVE_TRIGGER",
		// WAKE and SLEEP left at plan_physics_managed B2.1: managed posts them.
	};
	for ( size_t i = 0; i < sizeof( kKinds ) / sizeof( kKinds[ 0 ] ); i++ )
	{
		const std::string call =
			"SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_" + std::string( kKinds[ i ] );
		EXPECT_EQ( 0u, CountOf( code, call ) )
			<< "SS_PHYSEVENT_" << kKinds[ i ] << " is recorded in physics.cpp "
			   "again, but managed's collision event table answers it.";
	}
}

TEST( PhysicsMoveScope, ConstraintBrokenAndPostCollisionAreManaged )
{
	// These two never went through the general recorder. Since
	// plan_physics_managed B2.2 POST_COLLISION is managed's
	// (PhysicsCollisionImpacts records it in the callback), and since
	// plan_physics_bones_native_zero 2.2 CONSTRAINT_BROKEN is too
	// (PhysicsConstraints posts it from managed's IPhysicsConstraintEvent
	// table). Asked of the KIND: any spelling of a native record names the
	// enumerator, and a renamed recorder would pass a name check.
	EXPECT_EQ( 0u, CountOf( PhysicsCode(), "SS_PHYSEVENT_POST_COLLISION" ) )
		<< "physics.cpp records POST_COLLISION natively again";
	EXPECT_EQ( 0u, CountOf( Strip( ReadRel( kShellHdr ) ), "SS_PHYSEVENT_CONSTRAINT_BROKEN;" ) )
		<< "physics_shell.h records CONSTRAINT_BROKEN natively again";
	EXPECT_TRUE( ReadRel( kConstr ).empty() ) << kConstr << " is back";

	const std::string managed = ReadRel( kConstraintsCs );
	ASSERT_FALSE( managed.empty() ) << "cannot read " << kConstraintsCs;
	EXPECT_EQ( 1u, CountOf( managed, "KindRaw = (uint)PhysicsEventKind.ConstraintBroken," ) )
		<< "PhysicsConstraints no longer posts the break";
}

TEST( PhysicsMoveScope, PreCollisionIsTheOneKindNothingRecords )
{
	// The claim that makes "eleven of twelve" a measurement rather than a
	// slogan. Written as an ABSENCE over the whole stripped file, so a record
	// site added anywhere in it turns this red.
	EXPECT_EQ( 0u, CountOf( PhysicsCode(), "SS_PHYSEVENT_PRE_COLLISION" ) )
		<< "PRE_COLLISION is recorded now. That is very likely an improvement, "
		   "but the scope calls it \"the one that is not, and that is a "
		   "documented decision\" -- so the decision has changed and section 4 "
		   "needs rewriting.";
	EXPECT_NE( Doc().find( "`PRE_COLLISION` is the one that is not" ),
	           std::string::npos )
		<< "the document no longer names which kind is unrecorded";
}

//=============================================================================
// 3b. THE MANAGED SIDE'S ACCOUNT OF THE RING AGREES WITH THE RECORDERS.
//
// SourceSharp.Shared.Interop/PhysicsEvents.cs is where a managed reader learns which kinds
// actually arrive, and it says so in prose: a kind that nothing produces is
// marked "Not recorded today." Prose is not checked by a compiler, and this one
// was WRONG when this lane measured it -- Friction carried the marker while
// physics.cpp:3087 records SS_PHYSEVENT_FRICTION unconditionally, at the end of
// CCollisionEvent::Friction and outside the `if ( pEntity )` arm, with 65 lines
// of comment above it explaining that it is the push that unlocks the friction
// policy move.
//
// THAT IS THE SHAPE THAT HAS COST THIS PROJECT REAL BUGS: an armed producer
// nobody was told about reads exactly like a kind with no producer, and the
// next lane to look either re-adds the recorder or writes off the consumer. So
// the marker is checked against the recorders rather than trusted.
//
// The check is an IFF, both ways, over every kind -- not a spot check on the
// two that carry the marker today. A marker DROPPED from PreCollision would be
// as wrong as the one left on Friction, and only the reverse direction catches
// it.
//=============================================================================

TEST( PhysicsMoveScope, TheManagedEnumMarksExactlyTheKindsNothingRecords )
{
	const std::string cs = ReadRel(
		"sourcesharp/managed/SourceSharp.Shared.Interop/PhysicsEvents.cs" );
	ASSERT_FALSE( cs.empty() ) << "cannot read SourceSharp.Shared.Interop/PhysicsEvents.cs";

	const std::string code = PhysicsCode();
	const std::string constraints = Strip( ReadRel( kShellHdr ) );
	ASSERT_FALSE( constraints.empty() );

	// native kind, its C# member name, and how its producer is spelled. The
	// three that do not go through the general recorder are spelled out rather
	// than assumed, because that is exactly what a reader misses.
	struct Kind_t { const char *pszCs; const char *pszProducer; bool bInConstraints; };
	static const Kind_t kKinds[] = {
		{ "StartTouch",   "SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_START_TOUCH",    false },
		{ "EndTouch",     "SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_END_TOUCH",      false },
		{ "PreCollision", "SS_PHYSEVENT_PRE_COLLISION",                                  false },
		{ "PostCollision","SS_PHYSEVENT_POST_COLLISION",                                 false },
		{ "Friction",     "SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_FRICTION",       false },
		{ "FluidStart",   "SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_FLUID_START",    false },
		{ "FluidEnd",     "SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_FLUID_END",      false },
		{ "EnterTrigger", "SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_ENTER_TRIGGER",  false },
		{ "LeaveTrigger", "SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_LEAVE_TRIGGER",  false },
		{ "Wake",         "SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_WAKE",           false },
		{ "Sleep",        "SourceSharp_RecordPhysicsEvent( SS_PHYSEVENT_SLEEP",          false },
		{ "ConstraintBroken", "SS_PHYSEVENT_CONSTRAINT_BROKEN;",                          true  },
	};

	for ( size_t i = 0; i < sizeof( kKinds ) / sizeof( kKinds[ 0 ] ); i++ )
	{
		const std::string &hay = kKinds[ i ].bInConstraints ? constraints : code;
		const bool bRecorded = CountOf( hay, kKinds[ i ].pszProducer ) > 0;

		// The member's own doc line, found by the declaration rather than by
		// the name alone: `Friction` appears in prose elsewhere in the file and
		// would match a paragraph that says nothing about production.
		const std::string decl = std::string( "    " ) + kKinds[ i ].pszCs + " = ";
		const size_t at = cs.find( decl );
		ASSERT_NE( at, std::string::npos )
			<< "PhysicsEventKind." << kKinds[ i ].pszCs << " is gone from the "
			   "managed enum, which mirrors physics_shell.h member for member";

		// Its <summary> is the text between the previous member's declaration
		// and this one. Searching backwards from the declaration keeps the
		// marker attached to the member it belongs to.
		const size_t from = cs.rfind( ",\n", at );
		const std::string doc = cs.substr( from == std::string::npos ? 0 : from,
		                                   at - ( from == std::string::npos ? 0 : from ) );
		const bool bMarked = doc.find( "Not recorded today" ) != std::string::npos;

		EXPECT_EQ( bRecorded, !bMarked )
			<< "PhysicsEventKind." << kKinds[ i ].pszCs
			<< ( bRecorded
			     ? " IS recorded natively but its summary still says \"Not "
			       "recorded today\". A managed reader is being told an armed "
			       "producer does not exist; delete the marker."
			     : " is NOT recorded natively and its summary does not say so. "
			       "Add \"Not recorded today.\" or point at the producer -- a "
			       "kind with no producer that reads like a live one is how a "
			       "consumer gets written for events that never arrive." )
			<< " Producer looked for: \"" << kKinds[ i ].pszProducer << "\"";
	}
}

//=============================================================================
// 4. The console commands, split the way the scope splits them.
//=============================================================================

// MOVED, NOT RETIRED (2026-10-01). The six have lived in physics_debug.cpp
// since the user ruled that diagnostic commands live in debug-only files. This
// was a presence check on physics.cpp; it is now a presence check on the debug
// file AND an absence check on physics.cpp, so that neither a command lost in
// the move nor one left behind in both files passes.
TEST( PhysicsMoveScope, TheFiveInstrumentsAreStillNativeConsoleCommands )
{
	const std::string code  = PhysicsCode();
	const std::string debug = PhysicsDebugCode();
	ASSERT_FALSE( debug.empty() )
		<< "cannot read " << kPhysicsDebug << " -- the six presence checks below "
		   "would all fail for that reason alone, and the six absences in "
		   "physics.cpp would mean nothing. make setup links it into the farm.";
	static const char *kInstruments[] = {
		"ss_physworld_recheck", "ss_physworld_bounds",
		"ss_physworld_object",
		// cs_sv_physics_shell went with the event ring it reported on
		// (plan_physics_bones_native_zero 2.3); physics_event_sink_test
		// asserts it absent.
		// ss_physworld_report is managed's (PhysWorldReport,
		// plan_physics_bones_native_zero 2.3), asserted below.
		// ss_physworld_penetration went with the native penetration ledger
		// (plan_physics_bones_native_zero 2.1): its counters are managed's
		// cs_sv_physics_solver, and asserted absent below.
	};
	for ( size_t i = 0; i < sizeof( kInstruments ) / sizeof( kInstruments[ 0 ] ); i++ )
	{
		const std::string body = std::string( "SS_NATIVE_COMMAND( " ) + kInstruments[ i ] + " )";
		EXPECT_EQ( 1u, CountOf( debug, body ) )
			<< kInstruments[ i ] << " no longer has its native body in "
			   "physics_debug.cpp (SS_NATIVE_COMMAND; declared in C# since "
			   "plan_server_novalve.md P2.3c, debug-only since 2026-10-01). "
			   "39 sites across 9 files drive these by name (section 3.2); if "
			   "one moved, every driver moved with it or is now broken.";
		EXPECT_EQ( 0u, CountOf( code, body ) )
			<< kInstruments[ i ] << " is back in physics.cpp, which a release "
			   "build compiles; diagnostic commands live in physics_debug.cpp.";
	}
	EXPECT_EQ( 0u, CountOf( debug, "SS_NATIVE_COMMAND( ss_physworld_penetration )" ) )
		<< "ss_physworld_penetration is back, over a ledger that is managed's";

	// MOVED, NOT RETIRED (2.3): the report is a managed debug command over
	// the same per-pid file, and every driver keeps its name.
	EXPECT_EQ( 0u, CountOf( debug, "SS_NATIVE_COMMAND( ss_physworld_report )" ) )
		<< "ss_physworld_report has a native body again; the report is managed's";
	EXPECT_EQ( 0u, CountOf( code, "SourceSharp_PhysWorldReport" ) )
		<< "the native report is back in physics.cpp";
	const std::string diag = ReadRel( kDiagnosticsCs );
	ASSERT_FALSE( diag.empty() ) << "cannot read " << kDiagnosticsCs;
	EXPECT_EQ( 1u, CountOf( diag, "[ConCommand(\"ss_physworld_report\"" ) )
		<< "ss_physworld_report is not declared on the managed side; 13 sites drive it by name";
	EXPECT_EQ( 1u, CountOf( ReadRel( kReportCs ), "\"/tmp/ss-physworld-" ) )
		<< "the managed report no longer writes the per-pid file the tools read";
}

// NAMED FOR WHAT IT CHECKS AND NOT FOR THE RULING IT SUPPORTS. This was
// `...AndStillUndriven`, and it does not check that: the undriven half is a
// recursive grep over four trees, which is the document's evidence and not
// this binary's. A fact whose NAME claims more than its body asserts is the
// same defect as a check that cannot fail, read from the outside.
TEST( PhysicsMoveScope, TheFiveStockDebugCommandsAreStillNative )
{
	// The scope rules these "re-register or delete, by a named decision" rather
	// than "moves", on the strength of nothing in tools/, docs/, the managed
	// tree or cfg/ naming them. This checks only that they are still native.
	//
	// FOUR OF THE FIVE ARE IN physics_debug.cpp since 2026-10-01 (debug-only
	// file, user ruling), which is a location and not section 5.3's named
	// decision; so they are looked for there, and asserted absent from
	// physics.cpp. air_density stays in physics.cpp by the same ruling.
	const std::string code  = PhysicsCode();
	const std::string debug = PhysicsDebugCode();
	ASSERT_FALSE( debug.empty() ) << "cannot read " << kPhysicsDebug;
	static const char *kStock[] = {
		"physics_highlight_active", "physics_report_active", "physics_select",
		// physics_budget is managed's (PhysicsBudget, 2.3): asserted below.
	};
	for ( size_t i = 0; i < sizeof( kStock ) / sizeof( kStock[ 0 ] ); i++ )
	{
		const std::string body = std::string( "SS_NATIVE_COMMAND( " ) + kStock[ i ] + " )";
		EXPECT_EQ( 1u, CountOf( debug, body ) )
			<< kStock[ i ] << " is gone from physics_debug.cpp. If it was deleted "
			   "or re-registered on the managed side, say so in section 5.3.";
		EXPECT_EQ( 0u, CountOf( code, body ) )
			<< kStock[ i ] << " is back in physics.cpp, which a release build "
			   "compiles; diagnostic commands live in physics_debug.cpp.";
	}
	// SECTION 5.3'S NAMED DECISION, TAKEN (plan_physics_bones_native_zero
	// 2.3): physics_budget and air_density are re-registered on the managed
	// side -- physics_budget as a debug command over managed's
	// PhysicsSolver.ForceEntityToSleep, air_density in every build -- and
	// their native bodies are gone from both files.
	EXPECT_EQ( 0u, CountOf( debug, "SS_NATIVE_COMMAND( physics_budget )" ) )
		<< "physics_budget has a native body again";
	EXPECT_EQ( 0u, CountOf( code, "SS_NATIVE_COMMAND( air_density )" ) )
		<< "air_density has a native body again";
	EXPECT_EQ( 0u, CountOf( code, "PhysForceEntityToSleep" ) )
		<< "PhysForceEntityToSleep is back in physics.cpp; managed's solver puts objects to sleep";
	const std::string diag = ReadRel( kDiagnosticsCs );
	ASSERT_FALSE( diag.empty() ) << "cannot read " << kDiagnosticsCs;
	EXPECT_EQ( 1u, CountOf( diag, "[ConCommand(\"physics_budget\"" ) );
	EXPECT_EQ( 1u, CountOf( diag, "[ConCommand(\"air_density\"" ) );
}

//=============================================================================
// 5. The four symbols that are still imported, and the dead list.
//=============================================================================

TEST( PhysicsMoveScope, TheFourImportedSymbolsAreStillDefinedHere )
{
	// The document's strongest measurement is that physics.o defines 937
	// symbols and exactly four are imported. This cannot re-run nm, so it
	// checks the weaker half that source CAN answer: those four are still
	// defined in this file. If one moved out, the LIVE-API total is wrong.
	const std::string code = PhysicsCode();
	// THREE NOW: the named PhysFrictionSound overload went in the server gc
	// cull (2026-10-01); its importer, physics_shared.cpp's numeric overload,
	// is itself unreached. Asserted absent after the loop.
	// TWO NOW: PhysIsInCallback went with CCollisionEvent's in-callback count
	// (plan_physics_bones_native_zero 2.1); asserted absent after the loop.
	static const char *kImported[] = {
		"CServerSystem* PhysicsGameSystem()",
		"CWorld *PhysicsWorldOwner(",
	};
	for ( size_t i = 0; i < sizeof( kImported ) / sizeof( kImported[ 0 ] ); i++ )
	{
		EXPECT_LE( 1u, CountOf( code, kImported[ i ] ) )
			<< kImported[ i ] << " is no longer defined in physics.cpp, so the "
			   "scope's four-symbol import reading is stale. Re-run "
			   "5a-physics-exports.sh.";
	}

	// And the document still names all four, so a reader is not left to guess
	// which four "exactly four" meant.
	EXPECT_EQ( 0u, CountOf( code, "void PhysFrictionSound(" ) )
		<< "the named PhysFrictionSound is back in physics.cpp";
	EXPECT_EQ( 0u, CountOf( code, "PhysIsInCallback" ) )
		<< "PhysIsInCallback is back in physics.cpp";
	EXPECT_NE( Doc().find( "PhysicsWorldOwner(IPhysicsObject const*)" ), std::string::npos );
}

TEST( PhysicsMoveScope, EveryFunctionTheScopeCallsDeadIsStillThereToDelete )
{
	// The scope's DEAD list is 25 functions / 284 lines and is explicitly NOT
	// this item's work. It goes red the moment a deletion lane acts on it,
	// which is correct: the totals table is then wrong and the document has to
	// be re-measured rather than patched.
	const std::string code = PhysicsCode();
	static const char *kDead[] = {
		"bool PhysIsFinalTick()",
		"bool FindMaxContact(",
		"bool PhysShouldCollide(",
		"void PhysSolidOverride(",
		"void PhysSetMassCenterOverride(",
		"int PhysGetMassCenterOverrideIndex(",
		"void PhysGetMassCenterOverride(",
		"float PhysGetEntityMass(",
		"void PhysEnableFloating(",
		"void PhysOnCleanupDeleteList()",
		"bool PhysGetDamageInflictorVelocityStartOfFrame(",
		"bool PhysGetTriggerEvent(",
		"void PhysCollisionSound(",
		"void PhysBreakSound(",
		"void PhysCleanupFrictionSounds(",
		"void PhysCallbackImpulse(",
		"void PhysCallbackSetVelocity(",
		"void PhysCallbackDamage(",
		"void PhysCallbackRemove(",
		"void PhysSetEntityGameFlags(",
		"bool PhysFindOrAddVehicleScript(",
		"void PhysFlushVehicleScripts()",
		"IPhysicsObject *FindPhysicsObjectByName(",
		"void DebugDrawContactPoints(",
	};
	for ( size_t i = 0; i < sizeof( kDead ) / sizeof( kDead[ 0 ] ); i++ )
	{
		// THE DELETION HAPPENED (server gc cull, 2026-10-01): server.so's
		// --print-gc-sections link dropped every one, and the document was
		// re-measured. So each is now asserted ABSENT.
		EXPECT_EQ( 0u, CountOf( code, kDead[ i ] ) )
			<< kDead[ i ] << " is back in physics.cpp, which the scope deleted "
			   "as dead; re-measure docs/physics-move-scope.md if it is live again.";
	}
	EXPECT_NE( Doc().find( "| `DEAD` | 0 | 0 |" ), std::string::npos )
		<< "the document's DEAD row no longer reads 0 functions / 0 lines";
}

//=============================================================================
// 6. The managed destinations the plan's figure was never reduced by.
//=============================================================================

TEST( PhysicsMoveScope, TheManagedDestinationsStillHoldFiveHundredAndEightyFiveLines )
{
	const int nResponse = CountLines( ReadRel( kResponse ) );
	const int nRules    = CountLines( ReadRel( kRules ) );
	ASSERT_GT( nResponse, 0 ) << "cannot read " << kResponse;
	ASSERT_GT( nRules, 0 )    << "cannot read " << kRules;

	// 267 + 318 = 585 when measured; each file then gained the 7-line Valve
	// attribution header in the repo move, so 274 + 325 = 599; and the
	// response grew 19 when it started queuing the impact sound (physics stock
	// parity C3), so 293 + 325 = 618; and the rules grew 18 with the
	// FVPHYSICS_* names the impact damage reads (C6), so 293 + 343 = 636; and
	// the response grew 4 when its collision world was handed the side's own
	// surface table (plan_abi_hotreload Phase 1), so 297 + 343 = 640; and the
	// response grew 15 when it answered managed's own PostCollision in the
	// callback (plan_physics_managed B2.2), so 312 + 343 = 655; and the
	// response grew 16 with the solver-verdict gates (review F4) and 1 with
	// the dust prose (review F12), so 329 + 343 = 672; and 4 when the ring
	// drain stopped answering POST_COLLISION (it only counts; the callback is
	// the one producer of a response), so 333 + 343 = 676; and the response
	// lost 26 when Drain and its counter went with no producer left
	// (plan_physics_bones_perf C5, 2026-10-12), so 307 + 343 = 650 now.
	EXPECT_EQ( 307, nResponse ) << "PhysicsCollisionResponse.cs is now "
		<< nResponse << " lines";
	EXPECT_EQ( 343, nRules ) << "PhysicsCollisionRules.cs is now "
		<< nRules << " lines";
	EXPECT_EQ( 650, nResponse + nRules )
		<< "the managed destinations no longer hold 650 lines. 655 was the "
		   "figure plan_shim.md 4a item 1's \"~1,011\" was never reduced by, "
		   "quoted in section 4 and in the corrections table.";

	EXPECT_NE( Doc().find( "already hold 599 lines" ), std::string::npos )
		<< "the document no longer states the managed destinations' size";
}

//=============================================================================
// 7. The totals table adds up.
//=============================================================================

TEST( PhysicsMoveScope, TheTotalsTableSumsToItsOwnTotal )
{
	// THE NUMBERS ARE READ OUT OF THE DOCUMENT, not spelled here and compared
	// with themselves. Written the other way -- `EXPECT_EQ( 2136, 501 + 491 +
	// ... )` over literals in this file -- the fact could not fail for anything
	// a person does to the document, which is the "check that cannot fail" this
	// tree has 49 recorded instances of. Editing one row of the table without
	// re-deriving the total must be a failure, and that only works if both
	// sides of the arithmetic come from the document.
	const std::string doc = Doc();

	// All TEN, and the list is spelled out rather than scraped so that a verdict
	// dropped from the document is a missing row here rather than a smaller sum
	// that still adds up.
	static const char *kVerdicts[] = {
		"INSTRUMENT", "SLOT-HELP", "SLOT", "DEAD", "STOCK-DEBUG",
		"HOOK", "LIVE-API", "DEAD-VIA", "STALE-KEEP", "BOUNDARY",
	};
	int sumFns = 0, sumLines = 0;
	for ( size_t i = 0; i < sizeof( kVerdicts ) / sizeof( kVerdicts[ 0 ] ); i++ )
	{
		// The row opens "| `<verdict>` | ". Backticked, so `SLOT` does not
		// match the `SLOT-HELP` row -- the backtick after the name is what
		// separates them and is why the needle carries it.
		const std::string lead = std::string( "| `" ) + kVerdicts[ i ] + "` | ";
		const size_t at = doc.find( lead );
		ASSERT_NE( at, std::string::npos )
			<< "the totals table has no row for " << kVerdicts[ i ]
			<< ". Section 2's eight verdicts are what every other section "
			   "refers back to.";
		EXPECT_EQ( doc.find( lead, at + 1 ), std::string::npos )
			<< "more than one row for " << kVerdicts[ i ];

		int fns = 0, lines = 0;
		ASSERT_EQ( 2, sscanf( doc.c_str() + at + lead.size(), "%d | %d",
		                      &fns, &lines ) )
			<< "the " << kVerdicts[ i ] << " row does not read "
			   "\"| <functions> | <lines> |\"";
		// DEAD, DEAD-VIA and STALE-KEEP are EMPTY since the server gc cull
		// (2026-10-01) deleted every function in them; each other row is not.
		const std::string v( kVerdicts[ i ] );
		// SLOT and SLOT-HELP too since plan_physics_bones_native_zero 2.1
		// deleted CCollisionEvent.
		// STOCK-DEBUG too since 2.3 moved physics_budget's sleep and air_density
		// to managed. INSTRUMENT too since 2.3 moved PhysTimescale with
		// PhysFrame to managed's PhysicsFrame. BOUNDARY too since 2.6 moved
		// the world build (PhysCreateWorld) to managed's ServerPhysicsWorld.
		if ( v == "DEAD" || v == "DEAD-VIA" || v == "STALE-KEEP" || v == "SLOT" || v == "SLOT-HELP"
		     || v == "STOCK-DEBUG" || v == "INSTRUMENT" || v == "BOUNDARY" )
		{
			EXPECT_EQ( 0, fns ) << v << " has functions again";
			EXPECT_EQ( 0, lines ) << v << " has lines again";
		}
		else
		{
			EXPECT_GT( fns, 0 ) << v;
			EXPECT_GT( lines, 0 ) << v;
		}
		sumFns += fns;
		sumLines += lines;
	}

	// The stated total, also read out of the document.
	const std::string totalLead = "| **total** | **";
	const size_t tot = doc.find( totalLead );
	ASSERT_NE( tot, std::string::npos ) << "the totals table has no total row";
	int statedFns = 0, statedLines = 0, statedFrac = 0;
	// "**118** | **2,136**" -- a line total over a thousand carries a comma, so
	// it is read in two pieces rather than with one %d that would stop at the
	// comma and silently answer 2; "**27** | **591**" has none.
	const int nRead = sscanf( doc.c_str() + tot + totalLead.size(),
	                          "%d** | **%d,%d**", &statedFns, &statedLines, &statedFrac );
	ASSERT_GE( nRead, 2 )
		<< "the total row does not read \"| **total** | **<n>** | **<n[,nnn]>** |\"";
	const int statedLineTotal = nRead == 3 ? statedLines * 1000 + statedFrac : statedLines;

	EXPECT_EQ( sumFns, statedFns )
		<< "the eight verdict rows name " << sumFns << " functions and the "
		   "total says " << statedFns << ". A row was edited without the total "
		   "being re-derived; re-run 5a-physics-classify.py rather than "
		   "patching either number.";
	EXPECT_EQ( sumLines, statedLineTotal )
		<< "the eight verdict rows name " << sumLines << " lines and the total "
		   "says " << statedLineTotal << ". Re-run 5a-physics-classify.py.";

	// And the total is the one the classifier actually produced at 5034fb3e6,
	// which is the only part of this fact that is a claim about the tree rather
	// than about the document's internal consistency.
	// 119 / 2,129 until the server gc cull (2026-10-01) deleted 34 functions;
	// 83 / 1,736 until the diagnostic commands moved to physics_debug.cpp
	// (2026-10-01: 19 functions out, two accessors in); 66 / 1,315 until
	// contact touch was ported (physics stock parity E1: the two refusals'
	// bodies went, same 66 functions); 66 / 1,277 until B4b's update record
	// in PhysFrame; 66 / 1,282 until step 4's pair facts in ShouldCollide;
	// 66 / 1,300 until H's penetrate ledger added five helpers; 71 / 1,359
	// until G's ShouldFreezeObject crush; 72 / 1,383 until C3/L's sound slots;
	// 74 / 1,405 until C6's motion tail in PostCollision; 74 / 1,418 until
	// B5's stress write; 75 / 1,423 until B7's shadow walk; 77 / 1,447 until
	// plan_physics_managed A9's entity-solver record; 77 / 1,464 until B2.1
	// moved IPhysicsObjectEvent to managed; 75 / 1,446 until B2.2 moved nine
	// IPhysicsCollisionEvent slots to managed and deleted their bodies; 54 /
	// 1,023 until plan_physics_bones_native_zero 2.1 deleted CCollisionEvent;
	// 27 / 591 until the ask ledger left the report; 27 / 532 until 2.2
	// deleted the unused CSkipKeys; 26 / 526 until 2.3 moved the SS-PHYSWORLD
	// report, PhysForceEntityToSleep and air_density to managed; 19 / 312
	// until 2.3 moved the sound batches to managed's PhysicsSoundBatch; 13 /
	// 210 until 2.3 moved PhysFrame's decisions to managed's PhysicsFrame; 12
	// / 142 until the stress walk's ring write left with it; 11 / 138 until
	// the event ring went; 11 / 135 until 2.6 moved the world build; 10 / 130
	// until the owner batch held managed's velocity (plan_physics_bones_perf C2).
	EXPECT_EQ( 10, statedFns );
	EXPECT_EQ( 135, statedLineTotal );
}

// plan_physics_bones_native_zero 2.3: the queued impact and break sounds are
// managed's PhysicsSoundBatch, so physics.cpp holds no queue, no play and no
// PreClientUpdate of its own, ss_physics_sound.h is gone, and the batch is
// where they went (the presence half: a deletion with nowhere to go would
// pass the absences alone).
TEST( PhysicsMoveScope, TheSoundBatchesAreManaged )
{
	const std::string code = PhysicsCode();
	EXPECT_EQ( 0u, CountOf( code, "SourceSharp_PlayImpactSounds" ) );
	EXPECT_EQ( 0u, CountOf( code, "SourceSharp_PlayBreakSounds" ) );
	EXPECT_EQ( 0u, CountOf( code, "SourceSharp_EntityPhysicsImpactSound" ) );
	EXPECT_EQ( 0u, CountOf( code, "SourceSharp_EntityPhysicsBreakSound" ) );
	EXPECT_EQ( 0u, CountOf( code, "CPhysicsHook::PreClientUpdate" ) );
	EXPECT_EQ( 0u, CountOf( code, "physsound" ) );
	EXPECT_TRUE( ReadRel( "sourcesharp/game/server/ss_physics_sound.h" ).empty() )
		<< "ss_physics_sound.h is back";

	const std::string batch = ReadRel( "sourcesharp/managed/SourceSharp.Server/PhysicsSoundBatch.cs" );
	ASSERT_FALSE( batch.empty() ) << "PhysicsSoundBatch.cs did not read";
	EXPECT_EQ( 1u, CountOf( batch, "public void AddImpactSound(" ) );
	EXPECT_EQ( 1u, CountOf( batch, "public void AddBreakSound(" ) );
	EXPECT_EQ( 1u, CountOf( batch, "public void OnPreClientUpdate(float frameTime)" ) );
}

TEST( PhysicsMoveScope, TheScopeStillRefusesToGiveANumberForWhatMoves )
{
	// The finding this document exists to record is that NO WHOLE FUNCTION
	// MOVES and that a defensible line count for the movable residue cannot be
	// produced by any instrument this lane had. A later edit that quietly
	// supplies one -- without the per-line attribution pass section 4 names --
	// would put the tree back where item 1 started.
	const std::string doc = Doc();
	EXPECT_NE( doc.find( "**Nothing, as a function.**" ), std::string::npos )
		<< "the document no longer states that no whole function moves";
	EXPECT_NE( doc.find( "so: no number" ), std::string::npos )
		<< "the document now offers a line count for what moves. If it was "
		   "measured by the per-line attribution pass section 4 specifies, say "
		   "so there and retire this fact in the same commit; if it was "
		   "estimated, that is the failure mode item 1 was re-scoped to end.";
}

//=============================================================================
// 8. plan_shim.md 4a item 2, the neighbouring item this lane re-measured.
//
// THE PhysModelCreate* FAMILY WAS ALREADY DELETED (lane 3a) AND ITS ABSENCE WAS
// ASSERTED NOWHERE. physics_shared.h carries a tombstone naming all nine
// entry points, cpp_alias_t1_test re-pointed its anchor off the family when it
// went, and standalone_solidity_order_test quotes stock's comment about it --
// so a grep for the name finds five files and every hit is prose. The item was
// carried forward as "still present in 5 files" on the strength of that grep.
//
// What nothing checked was whether a DECLARATION could come back. The family's
// replacement is managed's EntityPhysicsCreation (CStandaloneEntity::InitPhysics
// until 2026-10-12) parsing the solid itself and calling CreatePolyObject
// through the vtables, so a restored declaration would compile
// against a definition that does not exist and fail at the link -- eventually,
// loudly, and with no statement anywhere of why it should not be there.
//=============================================================================

TEST( PhysicsMoveScope, ThePhysModelCreateFamilyIsStillGoneFromTheHeader )
{
	SS_SKIP_ON_CI( "reads the fork's own edits to Valve files (its markers, comments or line counts), which the public patch series CI sets up from does not carry" );
	const std::string hdr = Strip( ReadRel( "game/shared/physics_shared.h" ) );
	ASSERT_FALSE( hdr.empty() ) << "cannot read game/shared/physics_shared.h";

	// THE ANTI-VACUOUS HALF FIRST, and it has to be first: every assertion
	// below is an absence, and all nine would pass against an empty string or
	// against the wrong file. PhysModelParseSolid is the PARSE half -- never
	// part of this family, kept deliberately, and named in the tombstone as the
	// thing that stays -- so its presence is what says this is the right header
	// read the right way.
	ASSERT_LE( 1u, CountOf( hdr, "PhysModelParseSolid" ) )
		<< "physics_shared.h no longer declares PhysModelParseSolid, so it is "
		   "not the header this fact reasons about and the nine absences below "
		   "would mean nothing";

	static const char *kFamily[] = {
		"PhysModelCreateBox", "PhysModelCreateOBB", "PhysModelCreateUnmoveable",
		"PhysModelCreateCustom", "PhysCreateBbox", "PhysSphereCreate",
		"PhysDestroyObject", "PhysGetDefaultAABBSolid",
	};
	for ( size_t i = 0; i < sizeof( kFamily ) / sizeof( kFamily[ 0 ] ); i++ )
	{
		EXPECT_EQ( 0u, CountOf( hdr, kFamily[ i ] ) )
			<< kFamily[ i ] << " is declared again in physics_shared.h. The "
			   "family had zero importers on either link line when 4a item 2 "
			   "deleted it, and what builds a physics object from an entity's "
			   "vcollide in this tree is managed's EntityPhysicsCreation (it was "
			   "CStandaloneEntity::InitPhysics until 2026-10-12), which parses the "
			   "solid itself. If a caller has returned, say which.";
	}

	// PhysModelCreate is checked apart because it is a PREFIX of five of the
	// names above: counted with them, deleting it alone would still leave this
	// fact red for the wrong reason, and restoring only it would leave the
	// others' counts unchanged. Checked after the family, so by here every
	// longer name is known to be at zero and any remaining hit is this one.
	EXPECT_EQ( 0u, CountOf( hdr, "PhysModelCreate" ) )
		<< "PhysModelCreate is declared again in physics_shared.h";

	// And the tombstone that says all of this is still there to be read. The
	// fork's copy of the header carries it as a comment; the comment-free
	// patched upstream (make sdk-upstream) does not, so the record of the
	// deletion is this document's appendix, item 2.
	const std::string doc = ReadRel( kDoc );
	const size_t posItem = doc.find( "### Item 2" );
	ASSERT_NE( posItem, std::string::npos ) << kDoc << " lost its appendix item 2";
	const std::string item = doc.substr( posItem, doc.find( "\n### ", posItem + 1 ) - posItem );
	EXPECT_NE( item.find( "THE `PhysModelCreate*` FAMILY IS GONE" ), std::string::npos )
		<< "the tombstone is gone from the appendix, so a reader cannot "
		   "tell the family was removed on purpose rather than lost";
}

//=============================================================================
// 18. THE DERIVATION, NOT JUST THE DOCUMENT.
//
// Every test above reads docs/physics-move-scope.md and checks it against the
// tree or against itself. None of them could tell you whether the document's
// totals still follow from the RULINGS they were derived from -- and the lane
// that produced them said plainly that its measurement scripts were
// uncommitted and would go with its worktree.
//
// That is the shape where a committed number quietly stops being falsifiable:
// the document is pinned, the derivation is gone, and nobody can re-take it.
// So the classifier and its two inputs are committed under
// tools/physics-scope/, and this section runs them.
//
// It is NOT a generator reading its own output. rulings.tsv is a hand ruling
// per function; the document is hand-written prose. Comparing them crosses two
// artefacts made in different ways, which is the only reason the comparison is
// worth anything.
//=============================================================================

namespace
{

struct ClassifyRun_t
{
	int         m_nExit;
	std::string m_szOut;
};

// Runs the committed classifier from the repo root, wherever the test is run
// from. Bounded walk, anchored on a path only this branch has, so it cannot
// wander up into the main checkout and classify a different tree.
ClassifyRun_t RunClassify()
{
	static const char *kCandidates[] = { ".", "..", "../..", "../../.." };
	ClassifyRun_t run;
	run.m_nExit = -1;

	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[0] ); i++ )
	{
		const std::string root = kCandidates[ i ];
		struct stat st;
		if ( stat( ( root + "/tools/physics-scope/classify" ).c_str(), &st ) != 0 )
			continue;

		const std::string cmd =
			"cd " + root + " && tools/physics-scope/classify "
			"tools/physics-scope/fnmap.tsv tools/physics-scope/rulings.tsv 2>&1";
		FILE *pPipe = popen( cmd.c_str(), "r" );
		if ( pPipe == NULL )
			return run;

		char szBuf[ 4096 ];
		while ( fgets( szBuf, sizeof( szBuf ), pPipe ) != NULL )
			run.m_szOut += szBuf;

		// popen hands back wait(2)'s status; a test comparing that raw value
		// against 1 would be comparing against 256 and could never fail.
		const int nStatus = pclose( pPipe );
		run.m_nExit = WIFEXITED( nStatus ) ? WEXITSTATUS( nStatus ) : -1;
		return run;
	}
	return run;
}

} // namespace

TEST( PhysicsMoveScope, TheClassifierAndItsInputsAreCommitted )
{
	const ClassifyRun_t run = RunClassify();
	ASSERT_NE( -1, run.m_nExit )
		<< "tools/physics-scope/classify did not resolve from the test's "
		   "working directory, so every assertion below would be vacuous. "
		   "The document's totals are only falsifiable while this tool and "
		   "its two inputs are committed beside it.";
}

TEST( PhysicsMoveScope, EveryFunctionIsRuledAndEveryRulingIsUsed )
{
	const ClassifyRun_t run = RunClassify();
	ASSERT_NE( -1, run.m_nExit );

	EXPECT_NE( std::string::npos,
	           run.m_szOut.find( "UNMATCHED fnmap rows (no ruling): 0" ) )
		<< "a function in physics.cpp has no ruling, so the scope is silently "
		   "incomplete:\n" << run.m_szOut;
	EXPECT_NE( std::string::npos,
	           run.m_szOut.find( "AMBIGUOUS (ruling key matched >1): 0" ) )
		<< "a ruling key matched more than one function, so its lines are "
		   "counted under the wrong verdict:\n" << run.m_szOut;
	EXPECT_NE( std::string::npos,
	           run.m_szOut.find( "UNUSED rulings (key matched no function): 0" ) )
		<< "a ruling names a function that no longer exists -- a stale "
		   "allow-list, which is how these rot:\n" << run.m_szOut;
	EXPECT_EQ( 0, run.m_nExit )
		<< "the classifier refuses its own inputs:\n" << run.m_szOut;
}

TEST( PhysicsMoveScope, TheDocumentsTotalsStillFollowFromTheRulings )
{
	const ClassifyRun_t run = RunClassify();
	ASSERT_NE( -1, run.m_nExit );

	// The headline the document states in prose (:33, :47, :95, :360) and the
	// number the rulings actually sum to. Two artefacts, made differently.
	EXPECT_NE( std::string::npos,
	           run.m_szOut.find( "TOTAL        10 functions    135 lines" ) )
		<< "the rulings no longer sum to the 10 / 135 the document states "
		   "in four places. One of the two moved and the other did not:\n"
		   << run.m_szOut;

	EXPECT_NE( std::string::npos, Doc().find( "all 10 functions" ) )
		<< "the document stopped saying 10 functions";
	EXPECT_NE( std::string::npos, Doc().find( "all 135 lines" ) )
		<< "the document stopped saying 135 lines";
}
