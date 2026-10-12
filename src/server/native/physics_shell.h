//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the physics shell (plan_entity.md §3.8, step 3a) ====//
//
// What is left of the shell: the environment's creation and destruction, the
// non-finite guard the game hands a position through, and the sorted pointer
// set the diagnostics walk. Mechanism only: nothing in this file decides what
// the game does, and nothing in it names an entity type.
//
// THE EVENT RING, ITS SINK AND THE PHYSICS ID TABLE ARE GONE
// (plan_physics_bones_native_zero 2.3). Every vphysics callback is answered by
// a managed table (2.1, 2.2, 2.4), and the ring's last producer, PhysFrame,
// is managed's PhysicsFrame, which posts the step's events to the router with
// no crossing. A queue nothing fills, drained into a sink nothing reads, and
// an id table nothing asks were what remained; managed's PhysicsId is the id
// scheme its own constraint table hands out. The rule table went first (2.3:
// PhysicsSolverRules).
//
// HEADER-ONLY AND ENGINE-FREE above the ENGINE section at the bottom, so the
// guard and the pointer set go under gtest with no engine.
//
//=============================================================================//

#ifndef SOURCESHARP_PHYSICS_SHELL_H
#define SOURCESHARP_PHYSICS_SHELL_H
#ifdef _WIN32
#pragma once
#endif

#include <stdint.h>
#include <string.h>

//-----------------------------------------------------------------------------
// THE RULE TABLE AND ITS TWO INPUT RECORDS ARE GONE (plan_physics_bones_native_zero
// 2.3). They carried CCollisionEvent::ShouldCollide's rules for native's
// solver, and after 2.1 for the SS-PHYSWORLD report's evaluation against the
// world alone. Both are managed's: PhysicsSolver answers vphysics, and
// PhysicsSolver.AgainstTheWorld answers the report, over PhysicsSolverRules
// (the same clauses, in stock's order, and the group matrix CollisionRules
// builds). RegisterCollisionRules still states the matrix to the game rules
// object, whose ShouldCollide the trace filters ask.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// THE NON-FINITE TEST, which the out-of-bounds report (physics_debug.cpp) asks.
//
// The guard that refused a non-finite vector before vphysics saw it, and its
// refusal ledger, are managed's PhysicsNanGuard: they moved with the player
// shadow, their only caller (plan_physics_bones_native_zero 2.5), and the
// [SS-PHYSWORLD-NAN] line is managed's ss_physworld_nan.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND IT READS THE BITS, NOT THE FLOAT, BECAUSE THE RELEASE BUILD IS
// -ffast-math.
//
// `src/vpc_scripts/source_posix_ninja.vpc:312` puts `-ffast-math` in
// NINJA_OPTIMIZATION_RELEASE. That implies `-ffinite-math-only`, under which the
// compiler is entitled to assume no operand is ever NaN or infinite -- so
// `f == f` may be folded to true and a range test reasoned about as if NaN could
// not reach it. A float-comparison guard would therefore be a check that CANNOT
// FAIL in the build that ships, which is the worst place to put one.
//
// IEEE-754 single precision, read as an integer, needs neither: an exponent of
// 0xFF is NaN or infinity, and for everything below that the magnitude orders
// monotonically as an unsigned integer. So one unsigned compare against the
// bound's own bit pattern answers both questions at once, and no floating-point
// comparison happens at all.
//
// The bound is two thousand times a Source map's legal half-extent (16384): a
// ceiling nothing real reaches and nothing broken stays under.
//-----------------------------------------------------------------------------
inline uint32_t SourceSharpFloatBits( float f )
{
	uint32_t nBits;
	memcpy( &nBits, &f, sizeof( nBits ) );
	return nBits;
}

// Derived from the literal rather than written as hex, so the constant cannot
// drift away from the number the comment names.
inline uint32_t SourceSharpPhysicsCoordLimitBits()
{
	return SourceSharpFloatBits( 3.2768e7f ) & 0x7FFFFFFFu;
}

inline bool SourceSharpIsFinitePhysicsCoord( float f )
{
	return ( SourceSharpFloatBits( f ) & 0x7FFFFFFFu ) < SourceSharpPhysicsCoordLimitBits();
}

inline bool SourceSharpIsFinitePhysicsVector( const float *pVec3 )
{
	return pVec3 != NULL
		&& SourceSharpIsFinitePhysicsCoord( pVec3[ 0 ] )
		&& SourceSharpIsFinitePhysicsCoord( pVec3[ 1 ] )
		&& SourceSharpIsFinitePhysicsCoord( pVec3[ 2 ] );
}

//-----------------------------------------------------------------------------
// A SET OF COLLIDE POINTERS the out-of-bounds report must not ask for a hull.
//
// vphysics' CollideGetAABB SEGFAULTS on a virtual-mesh collide (the displacement
// terrain PhysCreateVirtualTerrain makes from GetCollideForVirtualTerrain),
// reproduced in isolation by maptools lane p3h
// and hit at level init by p3i's live gate on every map whose displacements are
// virtual meshes (power <= 3), stock-compiled or not. So the report collects
// those collide pointers from the same source, sorts them once, and asks this
// before each CollideGetAABB.
//
// Membership is by POINTER, never by the object's name "vdisp_%04d": the name
// is a string the creating code happens to print and anything may reuse, while
// the pointer is the exact thing CollideGetAABB would be handed.
//
// Pure: no engine, no allocation, no statics -- sorted in the caller's array.
// Insertion sort because the engine hands the pointers over in creation order,
// which is near-sorted, and n is at most MAX_MAP_DISPINFO.
//-----------------------------------------------------------------------------
inline void SourceSharpSortPointerSet( const void **ppSet, int32_t nCount )
{
	for ( int32_t i = 1; i < nCount; i++ )
	{
		const void *p = ppSet[ i ];
		int32_t j = i - 1;
		while ( j >= 0 && (uintptr_t)ppSet[ j ] > (uintptr_t)p )
		{
			ppSet[ j + 1 ] = ppSet[ j ];
			j--;
		}
		ppSet[ j + 1 ] = p;
	}
}

// ppSet must have been sorted by SourceSharpSortPointerSet. Duplicates are fine.
inline bool SourceSharpPointerSetContains( const void *const *ppSet, int32_t nCount, const void *p )
{
	int32_t lo = 0;
	int32_t hi = nCount - 1;
	while ( ppSet && lo <= hi )
	{
		const int32_t mid = lo + ( hi - lo ) / 2;
		const uintptr_t v = (uintptr_t)ppSet[ mid ];
		if ( v == (uintptr_t)p )
			return true;
		if ( v < (uintptr_t)p )
			lo = mid + 1;
		else
			hi = mid - 1;
	}
	return false;
}

//=============================================================================//
// THE ENGINE HALF.
//
// Guarded so that everything above can be compiled by a gtest with no Source in
// the include path. physics.cpp defines SOURCESHARP_PHYSICS_SHELL_ENGINE before
// including this file; nothing else does.
//=============================================================================//
#ifdef SOURCESHARP_PHYSICS_SHELL_ENGINE

#include "vphysics_interface.h"

//-----------------------------------------------------------------------------
// ENVIRONMENT MANAGEMENT — the arms this step takes off physics.cpp.
//
// Create, wire the four callback vtables, step, destroy. What is deliberately
// NOT here is everything CPhysicsHook::LevelInitPreEntity does BESIDE the
// environment: the object-pair hash, the world's physics object, the shadow
// entity list, the sound precache. Those are SDK bookkeeping that happens to
// share a function, not environment management, and they leave with the files
// that own them in Phase 4.
//-----------------------------------------------------------------------------
class CSourceSharpPhysicsEnvironment
{
public:
	CSourceSharpPhysicsEnvironment() : m_pEnv( 0 ) {}

	// Creates the environment and hands it the four vtables. The handlers are
	// PARAMETERS rather than globals reached from in here: this class does not
	// know what implements them, which is what makes it possible for Phase 4 to
	// replace CCollisionEvent without touching this code.
	IPhysicsEnvironment *Create( IPhysics *pPhysics,
	                             IPhysicsCollisionSolver *pSolver,
	                             IPhysicsCollisionEvent *pCollisionEvents,
	                             IPhysicsObjectEvent *pObjectEvents,
	                             IPhysicsConstraintEvent *pConstraintEvents,
	                             CreateInterfaceFn engineFactory,
	                             float flTimestep,
	                             float flGravity,
	                             int nMaxCollisionsPerObjectPerTimestep )
	{
		if ( !pPhysics )
			return 0;

		m_pEnv = pPhysics->CreateEnvironment();
		if ( !m_pEnv )
			return 0;

		physics_performanceparams_t params;
		params.Defaults();
		params.maxCollisionsPerObjectPerTimestep = nMaxCollisionsPerObjectPerTimestep;
		m_pEnv->SetPerformanceSettings( &params );

		if ( engineFactory )
			m_pEnv->SetDebugOverlay( engineFactory );
		m_pEnv->EnableDeleteQueue( true );

		m_pEnv->SetCollisionSolver( pSolver );
		m_pEnv->SetCollisionEventHandler( pCollisionEvents );
		m_pEnv->SetConstraintEventHandler( pConstraintEvents );
		// The callback when an object attached to a constraint is deleted:
		// managed's IPhysicsConstraintEvent table depends on this being on.
		m_pEnv->EnableConstraintNotify( true );
		m_pEnv->SetObjectEventHandler( pObjectEvents );

		m_pEnv->SetSimulationTimestep( flTimestep );
		m_pEnv->SetGravity( Vector( 0, 0, -flGravity ) );

		return m_pEnv;
	}

	void Destroy( IPhysics *pPhysics )
	{
		if ( m_pEnv && pPhysics )
			pPhysics->DestroyEnvironment( m_pEnv );
		m_pEnv = 0;
	}

	IPhysicsEnvironment *Get() const { return m_pEnv; }

private:
	IPhysicsEnvironment *m_pEnv;
};

#endif // SOURCESHARP_PHYSICS_SHELL_ENGINE

#endif // SOURCESHARP_PHYSICS_SHELL_H
