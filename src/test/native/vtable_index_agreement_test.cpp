//========= SourceSharp — the vtable table lengths, asked of the compiler =====//
//
// Some C++ tables are still described by the structs in
// src/abi/VtableLayouts.g.cs: the shim interfaces native implements (which
// managed now calls through their forwarders, plan_forwarders_everywhere.md)
// and the vphysics callbacks managed writes in Valve's layout. That file is
// GENERATED from the
// same headers by tools/vtable-layout, and a generated file can go stale:
// somebody edits a header, or lands the generator's input without re-running
// it. Then field N of the struct is a different method from slot N of the
// table, nothing faults, and the wrong function returns whatever it left in
// the return register.
//
// So there is ONE check and this file is its build-time half.
//
//   * native takes each table's length from the COMPILER,
//     `VTableHook::MemberOf( &Interface::LastMethod ).slot + 1`, and hands it to
//     managed beside the interface pointer;
//   * managed compares it to sizeof(TVtbl) / sizeof(void *) and refuses to bind
//     when they differ, naming both numbers;
//   * and the tests below hold the compiler's number against the COMMITTED
//     generated file, so a stale layout is red in `make check` rather than only
//     at run time on somebody's machine.
//
// THE COMMITTED FILE IS READ RATHER THAN REGENERATED, and that distinction is
// the whole value of these tests. A check that regenerates the layout from the
// headers and compares it to a count taken from the same headers derives BOTH
// SIDES FROM ONE INPUT: a shifted header shifts the layout too, the two agree,
// and the arm passes while being wrong. Parsing what is on disk is what makes
// this a second opinion.
//
// NO ENGINE TABLE IS HERE. Managed calls the engine's and vphysics' interfaces
// through the generated forwarders (plan_engine_forwarders.md), which native
// compiles as ordinary virtual calls, so their slots are the compiler's and
// there is no layout to hold against it. tools/vtable-layout refuses to emit a
// table MSVC orders differently, so every struct below has one order, its
// field order, on both ABIs.
//=============================================================================//

#include "gtest/gtest.h"

#include <type_traits>

// The data structs managed reads by layout (the tests after the lengths), and
// the vphysics callback interfaces managed writes.
#include "vphysics_interface.h"
#include "vphysics/constraints.h"
#include "vcollide_parse.h"
#include "vcollide.h"
#include "steam/steam_api_common.h"
#include "steam/isteamuser.h"
#include "steam/isteamapps.h"
#include "steam/isteamfriends.h"

// THE SHIM LAYER'S HEADERS, which are in THIS tree rather than the SDK's -- and
// that is the whole reason their agreement is cheap to check. The engine
// headers above describe a prebuilt module compiled by somebody else; these
// describe a class compiled from this file's own include, in this build, by
// this compiler.
#include "../game/server/ishim_frame.h"
#include "../game/server/ishim_level.h"
#include "../game/shared/ishim_root.h"
#include "../game/shared/ishim_console.h"
#include "../game/shared/ishim_areas.h"
#include "../game/shared/ishim_areas_client.h"
#include "../game/client/particles_lib/include/ss_particles/iparticlelib.h"

#include "../game/shared/vtable_hook.h"

#include <ctype.h>
#include <stdio.h>
#include <string.h>
#include <cstdlib>
#include <string>

namespace
{

//-----------------------------------------------------------------------------
// THE GENERATED LAYOUT, found by walking up from wherever the binary was run --
// the same way map_entity_block_test.cpp finds its table, and for the same
// reason: a compiled-in copy of the answers would be a SECOND record of them.
//-----------------------------------------------------------------------------
const char *const kLayoutRelative =
	"sourcesharp/managed/SourceSharp.Abi/VtableLayouts.g.cs";

std::string ReadRelative( const char *relative )
{
	std::string prefix;

	for ( int i = 0; i < 8; i++ )
	{
		std::string candidate = prefix + relative;

		if ( FILE *f = fopen( candidate.c_str(), "rb" ) )
		{
			std::string text;
			char buffer[ 4096 ];
			size_t got;

			while ( ( got = fread( buffer, 1, sizeof( buffer ), f ) ) > 0 )
				text.append( buffer, got );

			fclose( f );
			return text;
		}

		prefix += "../";
	}

	return std::string();
}

std::string ReadLayout()
{
	return ReadRelative( kLayoutRelative );
}

//-----------------------------------------------------------------------------
// The NAME of the last field one generated struct declares.
//
// The struct's fields are in the header's own declaration order, so the last
// one names the interface's last member -- which is the name an
// `Acquire( &nEntries )` has to read its slot out of. Empty for a struct that
// is not there, so a struct the generator stopped emitting cannot read as
// agreement.
//-----------------------------------------------------------------------------
std::string LastFieldOf( const std::string &text, const char *structName )
{
	// " unsafe struct ": internal, or public for an implemented-by-managed layout.
	std::string needle = std::string( " unsafe struct " ) + structName + "\n{";
	size_t at = text.find( needle );

	if ( at == std::string::npos )
		return std::string();

	size_t end = text.find( "\n}", at );

	if ( end == std::string::npos )
		return std::string();

	std::string name;
	size_t line = at;

	while ( ( line = text.find( "\n    public ", line ) ) != std::string::npos && line < end )
	{
		// `    public delegate* unmanaged<...> Name;` -- the identifier is what
		// sits between the last space and the semicolon. A layout MSVC orders
		// differently writes `    public delegate* unmanaged<...> Name => ...;`,
		// and there it is the word before the arrow.
		size_t semi = text.find( ';', line );

		if ( semi == std::string::npos || semi > end )
			break;

		size_t arrow = text.find( " => ", line );

		if ( arrow != std::string::npos && arrow < semi )
		{
			size_t space = text.rfind( ' ', arrow - 1 );

			if ( space != std::string::npos && space > line )
				name = text.substr( space + 1, arrow - space - 1 );
		}
		else
		{
			size_t space = text.rfind( ' ', semi );

			if ( space != std::string::npos && space > line )
				name = text.substr( space + 1, semi - space - 1 );
		}

		line += 2;
	}

	return name;
}

//-----------------------------------------------------------------------------
// The member an `Acquire` reads its entry count out of.
//
// Finds `VTableHook::MemberOf( &IShimFoo::` in the file and answers the
// identifier after it. Empty when the shape is not there at all, which fails
// the comparison rather than passing it -- an Acquire that stopped reading a
// slot must not read as agreeing with whatever the layout says.
//-----------------------------------------------------------------------------
std::string AcquireMemberIn( const std::string &text, const char *prefix )
{
	size_t at = text.rfind( prefix );

	if ( at == std::string::npos )
		return std::string();

	at += strlen( prefix );
	size_t end = at;

	while ( end < text.size()
	     && ( isalnum( (unsigned char)text[ end ] ) || text[ end ] == '_' ) )
	{
		end++;
	}

	return text.substr( at, end - at );
}

//-----------------------------------------------------------------------------
// How many fields one generated struct declares.
//
// COUNTED FROM `public ` AT THE START OF A LINE INSIDE THE STRUCT BODY, which is
// what every field declaration is and what no comment line is -- the prose the
// generator carries onto a field is `    // ...`. Returns -1 for a struct that
// is not there at all, so "the generator stopped emitting it" cannot read as
// "it has no fields".
//-----------------------------------------------------------------------------
int FieldsIn( const std::string &text, const char *structName )
{
	// " unsafe struct ": internal, or public for an implemented-by-managed layout.
	std::string needle = std::string( " unsafe struct " ) + structName + "\n{";
	size_t at = text.find( needle );

	if ( at == std::string::npos )
		return -1;

	size_t end = text.find( "\n}", at );

	if ( end == std::string::npos )
		return -1;

	int fields = 0;
	size_t line = at;

	while ( ( line = text.find( "\n    public ", line ) ) != std::string::npos && line < end )
	{
		fields++;
		line += 2;
	}

	return fields;
}

} // namespace


//=============================================================================
// ONE TEST PER INTERFACE: the compiler's own table length, against the number of
// fields the committed generated file declares.
//
// The compiler's answer comes from a pointer to the interface's LAST
// declaration. Under the Itanium ABI a pointer-to-member-function carries its
// slot, so this is not a count of anything -- and on a class whose virtual
// destructor occupies slots 0 and 1, those two are already inside the number.
//=============================================================================

//=============================================================================
// THE COUNT THE FACTORY REPORTS, AGAINST THE MEMBER THE LAYOUT ENDS WITH.
//
// ShimFrame_Acquire and ShimLevel_Acquire each answer their table length as
// `MemberOf( &IShimFoo::<a member> ).slot + 1`. That reads the slot out of the
// compiler and is exactly right -- FOR THE MEMBER IT NAMES. Name any member but
// the LAST one and the count is short, managed refuses the bind, and the whole
// area is dead.
//
// THE SUB-IOQUEUE LANE DID EXACTLY THAT. It appended
// IShimFrame::DeliverToSdkEntities, updated ishim_frame.h, shell_frame.cpp's
// class body, VtableLayouts.g.cs and both slot-count facts -- and left
// ShimFrame_Acquire naming StandaloneFinish. `make check` was green, the two
// EXPECTs below were green, and every server frame of the shipped game threw
//
//     native says "SourceSharpFrame001" has 18 vtable entries and
//     ShimFrameVtbl declares 19
//
// A LIVE RUN IS WHAT FOUND IT. The two tests here are so that the next one is
// not found that way.
//
// NOT CIRCULAR, and that is the whole design of the pair: the tests below
// already hold the LAYOUT against the COMPILER, so holding the ACQUIRE against
// the layout's last field name closes the triangle. Each half can fail on its
// own.
//=============================================================================

TEST( VtableAcquireCount, ShimFrameAcquireNamesTheLastMember )
{
	const std::string source =
		ReadRelative( "sourcesharp/game/server/shell_frame.cpp" );

	ASSERT_FALSE( source.empty() )
		<< "cannot find shell_frame.cpp -- this test is vacuous without it";

	const std::string named =
		AcquireMemberIn( source, "VTableHook::MemberOf( &IShimFrame::" );
	const std::string last = LastFieldOf( ReadLayout(), "ShimFrameVtbl" );

	EXPECT_FALSE( last.empty() )
		<< "ShimFrameVtbl has no fields in the committed layout";

	EXPECT_EQ( last, named )
		<< "ShimFrame_Acquire reads its entry count out of IShimFrame::" << named
		<< ", and the last slot of the interface is " << last
		<< ". A count taken from anything but the last member is SHORT, and a "
		   "short count makes managed refuse the whole area at run time while "
		   "every hermetic test stays green.";
}

TEST( VtableAcquireCount, ShimLevelAcquireNamesTheLastMember )
{
	// The same shape on the other shim interface, because the mistake is a
	// property of the pattern and not of the lane that made it.
	const std::string source =
		ReadRelative( "sourcesharp/game/server/shell_level.cpp" );

	ASSERT_FALSE( source.empty() )
		<< "cannot find shell_level.cpp -- this test is vacuous without it";

	const std::string named =
		AcquireMemberIn( source, "VTableHook::MemberOf( &IShimLevel::" );
	const std::string last = LastFieldOf( ReadLayout(), "ShimLevelVtbl" );

	EXPECT_FALSE( last.empty() )
		<< "ShimLevelVtbl has no fields in the committed layout";

	EXPECT_EQ( last, named );
}

TEST( VtableAcquireCount, ShimRootGetHandsTheRootsForwarderTableRatherThanACount )
{
	// plan_forwarders_everywhere.md phase 3: the root LoadSideV2 hands managed
	// travels with its forwarder table, which managed holds against
	// ShimRootFwd's count, so ShimRoot_Get reads no member's slot any more.
	// The served name is the generated table's (tools/engine-forwarders'
	// ShimRoot row); a wrong one hands managed a null table, which it refuses.
	const std::string source =
		ReadRelative( "sourcesharp/game/shared/shell_internalfactory.cpp" );

	ASSERT_FALSE( source.empty() )
		<< "cannot find shell_internalfactory.cpp -- this test is vacuous without it";

	const size_t body = source.find( "IShimRoot *ShimRoot_Get( const void **ppTable )\n{" );
	ASSERT_NE( std::string::npos, body ) << "ShimRoot_Get no longer takes the table's out-parameter";
	const std::string fn = source.substr( body, source.find( "\n}\n", body ) - body );

	EXPECT_NE( std::string::npos, fn.find( "*ppTable = SSFwd_Serve( \"SSFwd_ShimRoot\", nullptr );" ) )
		<< "ShimRoot_Get no longer hands over SSFwd_ShimRoot";
	EXPECT_EQ( std::string::npos, fn.find( "VTableHook::MemberOf" ) )
		<< "ShimRoot_Get counts the root's vtable again";
}

TEST( VtableAcquireCount, ShimConsoleAcquireNamesTheLastMember )
{
	const std::string source =
		ReadRelative( "sourcesharp/game/shared/shell_internalfactory.cpp" );

	ASSERT_FALSE( source.empty() )
		<< "cannot find shell_internalfactory.cpp -- this test is vacuous without it";

	const std::string named =
		AcquireMemberIn( source, "VTableHook::MemberOf( &IShimConsole::" );
	const std::string last = LastFieldOf( ReadLayout(), "ShimConsoleVtbl" );

	EXPECT_FALSE( last.empty() )
		<< "ShimConsoleVtbl has no fields in the committed layout";

	EXPECT_EQ( last, named );
}

TEST( VtableAcquireCount, TheMemberReaderAnswersNothingForAShapeThatIsNotThere )
{
	// The reader has to be able to FAIL, or both tests above would agree with
	// an Acquire that had stopped naming a member at all.
	EXPECT_EQ( std::string(),
	           AcquireMemberIn( "nothing of the kind", "VTableHook::MemberOf( &IShimFrame::" ) );

	EXPECT_EQ( std::string( "Zeta" ),
	           AcquireMemberIn( "x VTableHook::MemberOf( &IShimFrame::Zeta ).slot + 1;",
	                            "VTableHook::MemberOf( &IShimFrame::" ) );
}

TEST( VtableAcquireCount, TheLastFieldReaderAnswersNothingForAStructThatIsNotThere )
{
	EXPECT_EQ( std::string(), LastFieldOf( ReadLayout(), "NoSuchVtblStructExists" ) );
}

TEST( VtableLayoutLength, TheGeneratedFileIsFound )
{
	EXPECT_FALSE( ReadLayout().empty() )
		<< "cannot find " << kLayoutRelative << " -- every test below is vacuous without it";
}

TEST( VtableLayoutLength, ShimRootIsThreeSlots )
{
	// plan_engineapi_vtables step A3, and C5 (AcquireInterface, before Release);
	// AbiVersion left with the ABI version (2026-10-01). No virtual destructor,
	// for ishim_frame.h's reason. Release is the anchor managed's
	// ShimRoot.TryBind holds the count against, with equality.
	const int compiler = VTableHook::MemberOf( &IShimRoot::Release ).slot + 1;

	EXPECT_EQ( 3, compiler );
	EXPECT_EQ( compiler, FieldsIn( ReadLayout(), "ShimRootVtbl" ) );
}

TEST( VtableLayoutLength, ShimConsoleIsElevenSlots )
{
	// plan_engineapi_vtables step A4: the ten console slots of EngineApi, in
	// the struct's order, then RevertNamedConVar (sound cluster 17, lane
	// c2-sims). No virtual destructor.
	const int compiler = VTableHook::MemberOf( &IShimConsole::ConsoleSnapshotEntry ).slot + 1;

	EXPECT_EQ( 11, compiler );
	EXPECT_EQ( compiler, FieldsIn( ReadLayout(), "ShimConsoleVtbl" ) );
}

TEST( VtableLayoutLength, ParticleLibIsFortyFiveSlots )
{
	// plan_particles_so.md: libss_particles.so's ISSParticleLib -- Connect,
	// Disconnect, Mgr, Counters, then the 41 particles.a members client.so
	// forwards. No virtual destructor. MgrUncacheAllParticleSystems is the
	// anchor ParticleLib_Acquire reports (particles_lib_client.cpp).
	const int compiler = VTableHook::MemberOf( &ISSParticleLib::MgrUncacheAllParticleSystems ).slot + 1;

	EXPECT_EQ( 45, compiler );
	EXPECT_EQ( compiler, FieldsIn( ReadLayout(), "ParticleLibVtbl" ) );
}

TEST( VtableLayoutLength, ShimFrameIsNineteenSlots )
{
	// NINETEEN AND NOT TWENTY-ONE. IShimFrame declares no virtual destructor --
	// deliberately, see ishim_frame.h -- so there is no Itanium complete/
	// deleting pair in front of the first method and slot 0 really is
	// IsSimulatingOnAlternateTicks. Adding one would move every slot by two,
	// and this arm is where that arrives.
	//
	// Seventeen until plan_entity.md step 1g, which split StandaloneRunFrame
	// into StandaloneUpdate and StandaloneFinish so the managed think pass
	// could run between them; eighteen until the sub-ioqueue lane appended
	// DeliverToSdkEntities, the leaf the managed I/O queue asks when nothing
	// managed answered an input.
	//
	// READ OFF THE LAST MEMBER, whichever it is: this is `+ 1` on the tail of
	// the header, so an append that forgets the layout file is caught by the
	// second EXPECT rather than by the first agreeing with a stale constant.
	const int compiler = VTableHook::MemberOf( &IShimFrame::DeliverToSdkEntities ).slot + 1;

	EXPECT_EQ( 19, compiler );
	EXPECT_EQ( compiler, FieldsIn( ReadLayout(), "ShimFrameVtbl" ) );
}

TEST( VtableLayoutLength, ShimLevelIsTwentySixSlots )
{
	// Twenty-six and not twenty-eight: IShimLevel declares no virtual
	// destructor either, for the reason ishim_level.h gives.
	//
	// TWENTY-TWO UNTIL STAGE 2 OF THE SAVE LANE, which appended
	// LoadSaveGameState -- the one call the game DLL has to make for a saved
	// world to come back, and the reason stage 1's ReadRestoreHeaders and
	// Restore never fired. TWENTY-THREE UNTIL plan_server_novalve P2.1, which
	// appended FileSizeOnPath and ReadFileOnPath for the managed particle
	// manifest. TWENTY-FIVE UNTIL P5, which appended SpawnWorld: the world
	// arrives as parsed pairs instead of map text. The number moving is what the
	// append-only rule looks like from here; the ORDER check below is what says
	// it appended.
	const int compiler = VTableHook::MemberOf( &IShimLevel::SpawnWorld ).slot + 1;

	EXPECT_EQ( 26, compiler );
	EXPECT_EQ( compiler, FieldsIn( ReadLayout(), "ShimLevelVtbl" ) );
}

TEST( VtableLayoutLength, TheShimLevelSlotsAreInTheHeadersOwnOrder )
{
	// The first, one in the middle and the last -- because the span check
	// cannot see a reorder and this is where a reorder arrives.
	EXPECT_EQ( 0, VTableHook::MemberOf( &IShimLevel::ResetServerTime ).slot );
	EXPECT_EQ( 7, VTableHook::MemberOf( &IShimLevel::ResetDeleteList ).slot );

	// PropDataClearFile WAS the last slot and is now the second to last. It
	// stays at 21, which is what says stage 2's LoadSaveGameState was APPENDED
	// rather than inserted -- the one diff shape the append-only rule permits
	// and the one the span check cannot check.
	EXPECT_EQ( 21, VTableHook::MemberOf( &IShimLevel::PropDataClearFile ).slot );
	EXPECT_EQ( 22, VTableHook::MemberOf( &IShimLevel::LoadSaveGameState ).slot );
}

TEST( VtableLayoutLength, TheShimFrameSlotsAreInTheHeadersOwnOrder )
{
	// THE SPAN CHECK CANNOT SEE A REORDER, which is the whole reason
	// ishim_frame.h is append-only by rule: `managed <= native` protects against
	// APPENDS and nothing else, and two methods of the same shape swapped in the
	// middle passes it and silently misbinds.
	//
	// So the compiler is asked where three of them are -- the first, one in the
	// middle, and the last -- rather than only how many there are.
	EXPECT_EQ( 0, VTableHook::MemberOf( &IShimFrame::IsSimulatingOnAlternateTicks ).slot );
	EXPECT_EQ( 6, VTableHook::MemberOf( &IShimFrame::PhysicsRunThinkFunctions ).slot );
	EXPECT_EQ( 16, VTableHook::MemberOf( &IShimFrame::StandaloneUpdate ).slot );
	EXPECT_EQ( 17, VTableHook::MemberOf( &IShimFrame::StandaloneFinish ).slot );
	EXPECT_EQ( 18, VTableHook::MemberOf( &IShimFrame::DeliverToSdkEntities ).slot );
}

TEST( VtableLayoutLength, EveryBoundMethodIsVirtualAndNeedsNoThisAdjustment )
{
	// A NON-VIRTUAL MEMBER IS NOT IN THE TABLE AT ALL, so indexing one is a
	// category error rather than an off-by-one. And a non-zero adjustment would
	// mean the pointer was converted from a base's member to a derived's, in
	// which case the slot is numbered against a table this is not indexing.
	const VTableHook::Member members[] = {
		// CShimFrame derives from IShimFrame singly, so a non-zero adjustment
		// here would mean the pointer had been converted across a base and the
		// slot numbered against a table nothing is indexing.
		VTableHook::MemberOf( &IShimFrame::TickCount ),
		VTableHook::MemberOf( &IShimLevel::ResetServerTime ),
		VTableHook::MemberOf( &IPhysicsCollisionEvent::PreCollision ),
	};

	for ( size_t i = 0; i < sizeof( members ) / sizeof( members[ 0 ] ); i++ )
	{
		EXPECT_TRUE( members[ i ].isVirtual ) << "member " << i;
		EXPECT_EQ( 0, members[ i ].adjust ) << "member " << i;
	}
}

TEST( VtableLayoutLength, AStructTheGeneratorNoLongerEmitsReadsAsMinusOneRatherThanZero )
{
	// THE ARM THAT MAKES EVERY TEST ABOVE MEAN SOMETHING. If FieldsIn answered 0
	// for a struct it could not find, every EXPECT_EQ above would compare a real
	// number against 0 and fail loudly -- but a future refactor that made it
	// answer the count of an EMPTY body would pass silently. -1 can never equal
	// a table length.
	EXPECT_EQ( -1, FieldsIn( ReadLayout(), "NoSuchVtblStructExists" ) );
}

//=============================================================================
// THE VALVE-LAYOUT TABLES MANAGED WRITES.
//
// One test per interface, and each is the same two questions: what does the
// COMPILER say this table's length is, and does the committed generated file
// say the same? The compiler's answer comes from a pointer to the chain's last
// declaration, which carries its own slot.
//
// THE TWO ANSWERS ARE INDEPENDENT even though the method's NAME came from the
// header walk. If the walk missed a method, the last one is still last and its
// true slot is higher than the walk's count; if the walk counted one the
// compiler does not (a `#if` compiled out, say), the true slot is lower. Either
// way the numbers part company, which is the whole failure mode a generated
// layout has.
//=============================================================================

#define VTABLE_LENGTH_TEST( name, structName, expr, expected )   \
	TEST( VtableLayoutLength, name )                             \
	{                                                            \
		const int compiler = VTableHook::MemberOf( expr ).slot + 1; \
		EXPECT_EQ( expected, compiler );                         \
		EXPECT_EQ( compiler, FieldsIn( ReadLayout(), structName ) ); \
	}

// plan_physics_managed B0b: the callbacks vphysics calls, in Valve's layout and
// written by managed. No virtual destructor in any of them, so slot 0 is the
// first method; a count that drifts from the header is a call through garbage.
VTABLE_LENGTH_TEST( PhysicsObjectEventIsTwoSlots, "PhysicsObjectEventVtbl",
                    &IPhysicsObjectEvent::ObjectSleep, 2 )
VTABLE_LENGTH_TEST( PhysicsCollisionEventIsTenSlots, "PhysicsCollisionEventVtbl",
                    &IPhysicsCollisionEvent::ObjectLeaveTrigger, 10 )
VTABLE_LENGTH_TEST( PhysicsCollisionSolverIsFiveSlots, "PhysicsCollisionSolverVtbl",
                    &IPhysicsCollisionSolver::ShouldFreezeContacts, 5 )
VTABLE_LENGTH_TEST( PhysicsConstraintEventIsOneSlot, "PhysicsConstraintEventVtbl",
                    &IPhysicsConstraintEvent::ConstraintBroken, 1 )
VTABLE_LENGTH_TEST( MotionEventIsOneSlot, "MotionEventVtbl",
                    &IMotionEvent::Simulate, 1 )

// plan_physics_bones_native_zero Phase 3: the managed ragdoll builder answers
// the key parser's custom keys from its own Valve-layout table.
VTABLE_LENGTH_TEST( VPhysicsKeyHandlerIsTwoSlots, "VPhysicsKeyHandlerVtbl",
                    &IVPhysicsKeyHandler::SetDefaults, 2 )
// The five callbacks managed registers (ClientSteamCallbacks.cs): each id and
// payload size the SDK headers give, which ClientSteamCallbacksTests pins on
// the managed side. A wrong size makes Steam refuse or misread the payload; a
// wrong id files the block under another callback.
TEST( SteamCallbackLayout, CallbackIdsAndSizes )
{
	EXPECT_EQ( 101, int( SteamServersConnected_t::k_iCallback ) );
	EXPECT_EQ( 1u, sizeof( SteamServersConnected_t ) );
	EXPECT_EQ( 102, int( SteamServerConnectFailure_t::k_iCallback ) );
	EXPECT_EQ( 8u, sizeof( SteamServerConnectFailure_t ) );
	EXPECT_EQ( 103, int( SteamServersDisconnected_t::k_iCallback ) );
	EXPECT_EQ( 4u, sizeof( SteamServersDisconnected_t ) );
	EXPECT_EQ( 1014, int( NewUrlLaunchParameters_t::k_iCallback ) );
	EXPECT_EQ( 1u, sizeof( NewUrlLaunchParameters_t ) );
	EXPECT_EQ( 337, int( GameRichPresenceJoinRequested_t::k_iCallback ) );
	EXPECT_EQ( 264u, sizeof( GameRichPresenceJoinRequested_t ) );
	EXPECT_EQ( 0u, offsetof( GameRichPresenceJoinRequested_t, m_steamIDFriend ) );
	EXPECT_EQ( 8u, offsetof( GameRichPresenceJoinRequested_t, m_rgchConnect ) );
	EXPECT_EQ( 256, k_cchMaxRichPresenceValueLength );
}

// plan_physics_managed B2.2: Pre/PostCollision hand managed a
// vcollisionevent_t*, read through VCollisionEvent's explicit offsets. These
// are the numbers VCollisionEventLayoutTests pins on the managed side.
TEST( VCollisionEventLayout, OffsetsAreTheManagedViews )
{
	EXPECT_EQ( 0u,  offsetof( vcollisionevent_t, pObjects ) );
	EXPECT_EQ( 16u, offsetof( vcollisionevent_t, surfaceProps ) );
	EXPECT_EQ( 24u, offsetof( vcollisionevent_t, isCollision ) );
	EXPECT_EQ( 25u, offsetof( vcollisionevent_t, isShadowCollision ) );
	EXPECT_EQ( 28u, offsetof( vcollisionevent_t, deltaCollisionTime ) );
	EXPECT_EQ( 32u, offsetof( vcollisionevent_t, collisionSpeed ) );
	EXPECT_EQ( 40u, offsetof( vcollisionevent_t, pInternalData ) );
	EXPECT_EQ( 48u, sizeof( vcollisionevent_t ) );
}

// plan_physics_bones_native_zero 2.4: the managed constraint and spring tables
// hand vphysics' creators these blocks, built in VPhysicsJointParams.cs. These
// are the numbers VPhysicsJointParamsLayoutTests pins on the managed side.
// plan_physics_bones_native_zero Phase 3: the managed ragdoll builder reads a
// vcollide_t, parses each solid into a solid_t it allocates, writes its
// objectparams_t in place, and makes the joints' group from a
// constraint_groupparams_t. These are the numbers VPhysicsJointParamsLayoutTests
// (ObjectParams, ConstraintGroupParams) and SolidLayout / VCollideFields spell.
TEST( RagdollParamsLayout, ObjectParamsSolidGroupAndCollide )
{
	EXPECT_EQ( 8u,  offsetof( objectparams_t, mass ) );
	EXPECT_EQ( 24u, offsetof( objectparams_t, rotInertiaLimit ) );
	EXPECT_EQ( 32u, offsetof( objectparams_t, pName ) );
	EXPECT_EQ( 40u, offsetof( objectparams_t, pGameData ) );
	EXPECT_EQ( 48u, offsetof( objectparams_t, volume ) );
	EXPECT_EQ( 52u, offsetof( objectparams_t, dragCoefficient ) );
	EXPECT_EQ( 56u, offsetof( objectparams_t, enableCollisions ) );
	EXPECT_EQ( 64u, sizeof( objectparams_t ) );

	EXPECT_EQ( 4u,    offsetof( solid_t, name ) );
	EXPECT_EQ( 516u,  offsetof( solid_t, parent ) );
	EXPECT_EQ( 1028u, offsetof( solid_t, surfaceprop ) );
	EXPECT_EQ( 1540u, offsetof( solid_t, massCenterOverride ) );
	EXPECT_EQ( 1552u, offsetof( solid_t, params ) );
	EXPECT_EQ( 1616u, sizeof( solid_t ) );

	EXPECT_EQ( 4u,  offsetof( constraint_groupparams_t, minErrorTicks ) );
	EXPECT_EQ( 8u,  offsetof( constraint_groupparams_t, errorTolerance ) );
	EXPECT_EQ( 12u, sizeof( constraint_groupparams_t ) );

	EXPECT_EQ( 8u,  offsetof( vcollide_t, solids ) );
	EXPECT_EQ( 16u, offsetof( vcollide_t, pKeyValues ) );
}

// plan_physics_bones_native_zero 2.6: the managed world build parses each fluid
// block into a fluid_t and hands vphysics its fluidparams_t with the game data
// set, and the surface sound precache reads surfacedata_t's ten sound names and
// writes its ten handles. These are the numbers FluidLayout and
// SurfaceDataLayout (PhysicsWorldVPhysics.cs) spell.
//-----------------------------------------------------------------------------
// A Vector RETURNED BY VALUE IS SPELLED IN MANAGED (tools/vtable-layout's
// BY_VALUE_RETURNS, 2026-10-12), and this is the C++ half of why that is
// sound. The Itanium C++ ABI passes a class like a C struct unless it is
// "non-trivial for the purposes of calls" -- a non-trivial copy or move
// constructor or destructor -- and the SysV x86-64 ABI then returns a struct
// of at most 16 bytes in registers; three floats classify as SSE:SSE, so x and
// y come back in xmm0 and z in xmm1, with no hidden sret pointer. vector.h's
// copy constructor is commented out (187) and its definition is under `#if 0`
// (534). Vector is NOT trivially copyable -- it declares its own operator= --
// but assignment does not enter the call rule, so that is not asserted. If a
// copy constructor or destructor came back, the return would become an sret
// call and every managed `delegate* unmanaged<..., Vector>` would read
// garbage -- so those are pinned.
// (server.so's SourceSharp_PhysicsInertia, at the time: `call *0x100(%rax)`
// with only `this` in rdi, then `movlpd %xmm0` / `movss %xmm1`.)
//-----------------------------------------------------------------------------
TEST( VectorByValue, IsReturnedInRegisters )
{
	static_assert( std::is_trivially_copy_constructible< Vector >::value, "Vector grew a copy constructor" );
	static_assert( std::is_trivially_destructible< Vector >::value, "Vector grew a destructor" );
	static_assert( !std::is_polymorphic< Vector >::value, "Vector grew a vtable" );
	static_assert( std::is_standard_layout< Vector >::value, "Vector is not standard layout" );

	EXPECT_EQ( 12u, sizeof( Vector ) );
	EXPECT_EQ( 0u, offsetof( Vector, x ) );
	EXPECT_EQ( 4u, offsetof( Vector, y ) );
	EXPECT_EQ( 8u, offsetof( Vector, z ) );
}

TEST( WorldParamsLayout, FluidAndSurfaceSounds )
{
	EXPECT_EQ( 0u,   offsetof( fluid_t, index ) );
	EXPECT_EQ( 4u,   offsetof( fluid_t, surfaceprop ) );
	EXPECT_EQ( 512u, sizeof( fluid_t::surfaceprop ) );
	EXPECT_EQ( 520u, offsetof( fluid_t, params ) );
	EXPECT_EQ( 40u,  offsetof( fluidparams_t, pGameData ) );
	EXPECT_EQ( 56u,  sizeof( fluidparams_t ) );
	EXPECT_EQ( 576u, sizeof( fluid_t ) );

	EXPECT_EQ( 44u, offsetof( surfacedata_t, sounds ) );
	EXPECT_EQ( 0u,  offsetof( surfacesoundnames_t, stepleft ) );
	EXPECT_EQ( 18u, offsetof( surfacesoundnames_t, strainSound ) );
	EXPECT_EQ( 20u, sizeof( surfacesoundnames_t ) );
	EXPECT_EQ( 76u, offsetof( surfacedata_t, soundhandles ) );
	EXPECT_EQ( 0u,  offsetof( surfacesoundhandles_t, stepleft ) );
	EXPECT_EQ( 18u, offsetof( surfacesoundhandles_t, strainSound ) );
	EXPECT_EQ( 20u, sizeof( surfacesoundhandles_t ) );
	EXPECT_EQ( 96u, sizeof( surfacedata_t ) );
	EXPECT_EQ( 0x0200, CALLBACK_NEVER_DELETED );
}

TEST( ConstraintParamsLayout, BreakableAndAxisLimit )
{
	EXPECT_EQ( 0u,  offsetof( constraint_breakableparams_t, strength ) );
	EXPECT_EQ( 4u,  offsetof( constraint_breakableparams_t, forceLimit ) );
	EXPECT_EQ( 8u,  offsetof( constraint_breakableparams_t, torqueLimit ) );
	EXPECT_EQ( 12u, offsetof( constraint_breakableparams_t, bodyMassScale ) );
	EXPECT_EQ( 20u, offsetof( constraint_breakableparams_t, isActive ) );
	EXPECT_EQ( 24u, sizeof( constraint_breakableparams_t ) );
	EXPECT_EQ( 12u, offsetof( constraint_axislimit_t, torque ) );
	EXPECT_EQ( 16u, sizeof( constraint_axislimit_t ) );
	EXPECT_EQ( 48u, sizeof( matrix3x4_t ) );
}

TEST( ConstraintParamsLayout, FixedHingeBallsocketLength )
{
	EXPECT_EQ( 48u, offsetof( constraint_fixedparams_t, constraint ) );
	EXPECT_EQ( 72u, sizeof( constraint_fixedparams_t ) );

	EXPECT_EQ( 12u, offsetof( constraint_hingeparams_t, worldAxisDirection ) );
	EXPECT_EQ( 24u, offsetof( constraint_hingeparams_t, hingeAxis ) );
	EXPECT_EQ( 40u, offsetof( constraint_hingeparams_t, constraint ) );
	EXPECT_EQ( 64u, sizeof( constraint_hingeparams_t ) );

	EXPECT_EQ( 24u, offsetof( constraint_ballsocketparams_t, constraint ) );
	EXPECT_EQ( 48u, sizeof( constraint_ballsocketparams_t ) );

	EXPECT_EQ( 24u, offsetof( constraint_lengthparams_t, objectPosition ) );
	EXPECT_EQ( 48u, offsetof( constraint_lengthparams_t, totalLength ) );
	EXPECT_EQ( 52u, offsetof( constraint_lengthparams_t, minLength ) );
	EXPECT_EQ( 56u, sizeof( constraint_lengthparams_t ) );
}

TEST( ConstraintParamsLayout, SlidingPulleyRagdollSpring )
{
	EXPECT_EQ( 48u,  offsetof( constraint_slidingparams_t, slideAxisRef ) );
	EXPECT_EQ( 60u,  offsetof( constraint_slidingparams_t, constraint ) );
	EXPECT_EQ( 84u,  offsetof( constraint_slidingparams_t, limitMin ) );
	EXPECT_EQ( 88u,  offsetof( constraint_slidingparams_t, limitMax ) );
	EXPECT_EQ( 92u,  offsetof( constraint_slidingparams_t, friction ) );
	EXPECT_EQ( 96u,  offsetof( constraint_slidingparams_t, velocity ) );
	EXPECT_EQ( 100u, sizeof( constraint_slidingparams_t ) );

	EXPECT_EQ( 24u, offsetof( constraint_pulleyparams_t, pulleyPosition ) );
	EXPECT_EQ( 48u, offsetof( constraint_pulleyparams_t, objectPosition ) );
	EXPECT_EQ( 72u, offsetof( constraint_pulleyparams_t, totalLength ) );
	EXPECT_EQ( 76u, offsetof( constraint_pulleyparams_t, gearRatio ) );
	EXPECT_EQ( 80u, offsetof( constraint_pulleyparams_t, isRigid ) );
	EXPECT_EQ( 84u, sizeof( constraint_pulleyparams_t ) );

	EXPECT_EQ( 24u,  offsetof( constraint_ragdollparams_t, constraintToReference ) );
	EXPECT_EQ( 72u,  offsetof( constraint_ragdollparams_t, constraintToAttached ) );
	EXPECT_EQ( 120u, offsetof( constraint_ragdollparams_t, parentIndex ) );
	EXPECT_EQ( 124u, offsetof( constraint_ragdollparams_t, childIndex ) );
	EXPECT_EQ( 128u, offsetof( constraint_ragdollparams_t, axes ) );
	EXPECT_EQ( 176u, offsetof( constraint_ragdollparams_t, onlyAngularLimits ) );
	EXPECT_EQ( 177u, offsetof( constraint_ragdollparams_t, isActive ) );
	EXPECT_EQ( 178u, offsetof( constraint_ragdollparams_t, useClockwiseRotations ) );
	EXPECT_EQ( 180u, sizeof( constraint_ragdollparams_t ) );

	EXPECT_EQ( 4u,  offsetof( springparams_t, naturalLength ) );
	EXPECT_EQ( 8u,  offsetof( springparams_t, damping ) );
	EXPECT_EQ( 12u, offsetof( springparams_t, relativeDamping ) );
	EXPECT_EQ( 16u, offsetof( springparams_t, startPosition ) );
	EXPECT_EQ( 28u, offsetof( springparams_t, endPosition ) );
	EXPECT_EQ( 40u, offsetof( springparams_t, useLocalPositions ) );
	EXPECT_EQ( 41u, offsetof( springparams_t, onlyStretch ) );
	EXPECT_EQ( 44u, sizeof( springparams_t ) );
}

// objectparams_t, written by managed's player shadow for CreatePolyObject
// (plan_physics_bones_native_zero 2.5). VPhysicsObjectParams in
// SourceSharp.Abi carries the same offsets.
TEST( ObjectParamsLayout, TheFieldsTheManagedShadowWrites )
{
	EXPECT_EQ( 0u,  offsetof( objectparams_t, massCenterOverride ) );
	EXPECT_EQ( 8u,  offsetof( objectparams_t, mass ) );
	EXPECT_EQ( 12u, offsetof( objectparams_t, inertia ) );
	EXPECT_EQ( 16u, offsetof( objectparams_t, damping ) );
	EXPECT_EQ( 20u, offsetof( objectparams_t, rotdamping ) );
	EXPECT_EQ( 24u, offsetof( objectparams_t, rotInertiaLimit ) );
	EXPECT_EQ( 32u, offsetof( objectparams_t, pName ) );
	EXPECT_EQ( 40u, offsetof( objectparams_t, pGameData ) );
	EXPECT_EQ( 48u, offsetof( objectparams_t, volume ) );
	EXPECT_EQ( 52u, offsetof( objectparams_t, dragCoefficient ) );
	EXPECT_EQ( 56u, offsetof( objectparams_t, enableCollisions ) );
	EXPECT_EQ( 64u, sizeof( objectparams_t ) );
}

// surfacedata_t, read in place by managed's queued physics sounds through
// IPhysicsSurfaceProps::GetSurfaceData (plan_physics_bones_native_zero 2.3).
// VPhysicsSurfaceData in SourceSharp.Abi carries the same offsets.
TEST( SurfaceDataLayout, TheSoundFieldsTheManagedBatchReads )
{
	EXPECT_EQ( 20u, offsetof( surfacedata_t, audio ) );
	EXPECT_EQ( 44u, offsetof( surfacedata_t, sounds ) );
	EXPECT_EQ( 24u, offsetof( surfacedata_t, audio ) + offsetof( surfaceaudioparams_t, hardnessFactor ) );
	EXPECT_EQ( 36u, offsetof( surfacedata_t, audio ) + offsetof( surfaceaudioparams_t, hardThreshold ) );
	EXPECT_EQ( 40u, offsetof( surfacedata_t, audio ) + offsetof( surfaceaudioparams_t, hardVelocityThreshold ) );
	EXPECT_EQ( 48u, offsetof( surfacedata_t, sounds ) + offsetof( surfacesoundnames_t, impactSoft ) );
	EXPECT_EQ( 50u, offsetof( surfacedata_t, sounds ) + offsetof( surfacesoundnames_t, impactHard ) );
	EXPECT_EQ( 60u, offsetof( surfacedata_t, sounds ) + offsetof( surfacesoundnames_t, breakSound ) );
	EXPECT_EQ( 96u, sizeof( surfacedata_t ) );
}

// plan_engineapi_vtables Phase C: one length test per interface tools/shim-areas
// generates, and kShimAreasCovered naming their structs -- generated from the
// same table as the interfaces and their layouts.
#include "shim_areas_layout.inc"

// The tables managed writes and native calls are not here: tools/managed-areas
// writes both sides of each from docs/managed-interfaces.tsv, its C struct with
// a static_assert of its size, and native checks managed's per-slot hashes at
// acquire (plan_forwarders_everywhere.md phase 4).

TEST( VtableLayoutLength, EveryGeneratedStructIsCoveredByATestAbove )
{
	// THE ARM THAT NOTICES A LAYOUT NOBODY CHECKED. tools/vtable-layout emits
	// one struct per interface it knows about; if a later lane adds one and no
	// test here names it, its length is a claim with nothing behind it.
	const char *const kCovered[] = {
		// The vphysics callbacks managed writes in Valve's layout.
		"PhysicsObjectEventVtbl", "PhysicsCollisionEventVtbl", "PhysicsCollisionSolverVtbl",
		"PhysicsConstraintEventVtbl", "MotionEventVtbl", "VPhysicsKeyHandlerVtbl",

		// The shim layer.
		"ShimFrameVtbl", "ShimLevelVtbl", "ShimRootVtbl", "ShimConsoleVtbl",
		// plan_particles_so.md: libss_particles.so's own export, served by the
		// client's internal factory.
		"ParticleLibVtbl",
	};

	const std::string layout = ReadLayout();
	const int covered = (int)( sizeof( kCovered ) / sizeof( kCovered[ 0 ] ) );

	const int areas = (int)( sizeof( kShimAreasCovered ) / sizeof( kShimAreasCovered[ 0 ] ) );

	// Every name above must really be in the file...
	for ( int i = 0; i < covered; i++ )
		EXPECT_NE( -1, FieldsIn( layout, kCovered[ i ] ) ) << kCovered[ i ];
	for ( int i = 0; i < areas; i++ )
		EXPECT_NE( -1, FieldsIn( layout, kShimAreasCovered[ i ] ) ) << kShimAreasCovered[ i ];

	// ...and the file must hold no OTHER struct.
	int emitted = 0;
	size_t at = 0;
	const std::string needle = " unsafe struct ";   // internal or public

	while ( ( at = layout.find( needle, at ) ) != std::string::npos )
	{
		emitted++;
		at += needle.size();
	}

	EXPECT_EQ( covered + areas, emitted )
		<< "a generated struct has no length test naming it";
}
