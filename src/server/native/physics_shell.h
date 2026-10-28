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

#pragma once

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
// THE NON-FINITE TEST AND THE POINTER SET ARE GONE (2026-10-18). Their one user
// was physics_debug.cpp's ss_physworld_bounds, which is managed's
// PhysWorldProbes now, over PhysicsNanGuard.IsFinite and a set of the virtual
// terrain's collides.
//-----------------------------------------------------------------------------

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
