//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The physics environment's creation and destruction. Mechanism only.

#pragma once

#include <stdint.h>
#include <string.h>

// physics.cpp defines SOURCESHARP_PHYSICS_SHELL_ENGINE before including this; the guard
// keeps the header compilable by a gtest with no Source includes.
#ifdef SOURCESHARP_PHYSICS_SHELL_ENGINE

#include "vphysics_interface.h"

// Creates the environment, wires the four callback handlers, and destroys it.
class CSourceSharpPhysicsEnvironment
{
public:
	CSourceSharpPhysicsEnvironment() : m_pEnv( 0 ) {}

	// The handlers are parameters: this class does not know what implements them.
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
		// Managed's IPhysicsConstraintEvent table depends on this being on.
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
