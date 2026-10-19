//========= SourceSharp — the layouts managed shares with native, asked of the compiler //
//
// What crosses between native and managed by LAYOUT rather than by call: the
// data structs managed reads and writes in place (vphysics' collision event,
// the ragdoll and constraint params, the world's fluid and surface data, the
// Steam callback payloads), each held field by field to the offsets its
// managed view uses.
//
// No vtable layout is here any more (plan_forwarders_everywhere.md). Managed
// calls every native interface through a generated forwarder table, compiled
// against the header as ordinary virtual calls, so the slots are the
// compiler's and no layout is held against it; each table's count is held to
// the compiler's in the forwarder libraries' tests (fwd_shim_test and
// shim_areas_forwarders.inc, fwd_steam_test). What is left of the old count
// check is the fact that no acquire hands one back.
//=============================================================================//

#include "gtest/gtest.h"

#include <type_traits>

// The data structs managed reads by layout.
#include "vphysics_interface.h"
#include "vphysics/constraints.h"
#include "vcollide_parse.h"
#include "vcollide.h"
#include "steam/steam_api_common.h"
#include "steam/isteamuser.h"
#include "steam/isteamapps.h"
#include "steam/isteamfriends.h"


#include <ctype.h>
#include <stdio.h>
#include <string.h>
#include <cstdlib>
#include <string>

namespace
{

//-----------------------------------------------------------------------------
// A tree file, found by walking up from wherever the binary was run -- the
// same way map_entity_block_test.cpp finds its table.
//-----------------------------------------------------------------------------
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

} // namespace

//=============================================================================
// NO ACQUIRE COUNTS A VTABLE (plan_forwarders_everywhere.md phase 6).
//
// The internal factory once answered each area's table length as
// `VTableHook::MemberOf( &IShimFoo::<last member> ).slot + 1`, which managed
// held against its generated layout -- and a count that named any member but
// the last (the sub-ioqueue lane's StandaloneFinish) killed every managed
// frame with the whole suite green. Managed now calls each area through a
// generated forwarder table that carries its own count, so no acquire takes
// one from the compiler at all; these facts hold that, and that ShimRoot_Get
// hands over the root's table.
//=============================================================================

TEST( VtableAcquireCount, NoAcquireTakesASlotCountFromTheCompiler )
{
	const char *const kAcquires[] = {
		"sourcesharp/game/server/shell_frame.cpp",
		"sourcesharp/game/server/shell_level.cpp",
		"sourcesharp/game/shared/shell_internalfactory.cpp",
		"sourcesharp/game/shared/shell_vtable_interfaces.cpp",
		"sourcesharp/game/shared/shell_shim_areas.inc",
		"sourcesharp/game/shared/shell_shim_areas_client.inc",
		"sourcesharp/game/client/particles_lib_client.cpp",
	};

	for ( const char *pszFile : kAcquires )
	{
		const std::string source = ReadRelative( pszFile );
		ASSERT_FALSE( source.empty() ) << "cannot find " << pszFile << " -- this test is vacuous without it";
		EXPECT_NE( std::string::npos, source.find( "_Acquire" ) )
			<< pszFile << " holds no acquire any more: the scan reads the wrong file";
		EXPECT_EQ( std::string::npos, source.find( "VTableHook::MemberOf(" ) )
			<< pszFile << " takes a vtable slot count again";
		EXPECT_EQ( std::string::npos, source.find( "pnEntries" ) )
			<< pszFile << " hands a count back beside a pointer again";
	}
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

	EXPECT_NE( std::string::npos, fn.find( "*ppTable = SSFwd_Serve( \"SSFwd_ShimRoot\" );" ) )
		<< "ShimRoot_Get no longer hands over SSFwd_ShimRoot";
	EXPECT_EQ( std::string::npos, fn.find( "VTableHook::MemberOf" ) )
		<< "ShimRoot_Get counts the root's vtable again";
}

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
