//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON e  a synchronous mid-step answer
// The server's VPhysics hook. vphysics calls the collision callbacks from inside
// its solve and needs an answer before it continues, so they stay native
// (physics_shell.h).

#include "server_state.h"
#include "ss_physics.h"
#include "shell_serversystem.h"

#include "ss_recipient_filter.h"

#include "physicsowner.h"

#include "ss_util.h"
#include "vcollide_parse.h"

#include "ss_init_factory.h"
#include "server_physics.h"
#include "server_entity_context.h"
#include "server_entity_resolver.h"
#include "shell_managed_areas.h"

#include "ss_world.h"
float GetCurrentGravity();
#include "vphysics/vehicles.h"
using ss::gamevcollisionevent_t;

#include "ss_physsaverestore.h"
#include "engine/IStaticPropMgr.h"
using ss::masscenteroverride_t;

#include "vphysics/collision_set.h"
#include "vphysics/friction.h"

#include "vphysics/performance.h"
#include "tier1/callqueue.h"

// Opens the engine half of physics_shell.h; the rest is engine-free for the gtest.
#define SOURCESHARP_PHYSICS_SHELL_ENGINE 1
#include "physics_shell.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "imanaged_areas.h"
#include "ss_string.h"

struct CServerPhysics::Impl
{
	CSourceSharpPhysicsEnvironment m_ShellEnv;
	CUtlVector<masscenteroverride_t> m_massCenterOverrides;
};

void CServerPhysics::DeleteImpl( Impl *pImpl )
{
	delete pImpl;
}

CServerPhysics::CServerPhysics( CGlobalVars *pGlobals, const ss::factorylist_t &factories, const CManagedAreas &managed,
								CServerPlayersDll &players, const CServerEntityContext &entities,
								CServerEntityResolver &resolver )
	: CServerSystem( "CPhysicsHook" )
	, m_pGlobals( pGlobals )
	, m_Factories( factories )
	, m_Managed( managed )
	, m_Players( players )
	, m_Entities( entities )
	, m_Resolver( resolver )
	, m_pImpl( new Impl, &CServerPhysics::DeleteImpl )
{
}

bool CServerPhysics::Init()
{
	const ss::factorylist_t &factories = m_Factories;

	if ( !factories.physicsFactory )
		return false;

	if ( ( m_pPhysics = (IPhysics *)factories.physicsFactory( VPHYSICS_INTERFACE_VERSION, NULL ) ) == NULL ||
		 ( m_pCollision =
			   (IPhysicsCollision *)factories.physicsFactory( VPHYSICS_COLLISION_INTERFACE_VERSION, NULL ) ) == NULL ||
		 ( m_pSurfaceProps = (IPhysicsSurfaceProps *)factories.physicsFactory( VPHYSICS_SURFACEPROPS_INTERFACE_VERSION,
																			   NULL ) ) == NULL )
		return false;

	return true;
}

void CServerPhysics::LevelInitPreEntity()
{
	const ss::factorylist_t &factories = m_Factories;

	// Before the new environment exists: any shadow still live belongs to the old one.
	if ( m_Players.m_nShadowsLive > 0 )
		m_Players.m_nShadowsOutlived += m_Players.m_nShadowsLive;

	// The four callback tables are managed's, installed at pre-entity init.
	m_pEnv = m_pImpl->m_ShellEnv.Create( m_pPhysics, NULL, NULL, NULL, NULL, factories.engineFactory,
										 m_pGlobals->interval_per_tick, // 15 ms per tick
										 GetCurrentGravity(), // HL game gravity, not real-world gravity
										 10 ); // maxCollisionsPerObjectPerTimestep

	// Managed builds the world's physics object; native holds it.
	IManagedPhysicsFrame *pPhysicsFrame = m_Managed.m_pPhysicsFrame;
	m_pWorldObject = pPhysicsFrame ? static_cast<IPhysicsObject *>( pPhysicsFrame->CreateWorld() ) : NULL;
	if ( !m_pWorldObject )
		Warning( "[SourceSharp] the level has no world physics object: %s\n",
				 pPhysicsFrame ? "managed built none" : "no managed load to build it" );

	if ( pPhysicsFrame )
		pPhysicsFrame->PrecacheSurfaceSounds();

	m_bPaused = true;
}

void CServerPhysics::LevelInitPostEntity()
{
	m_bPaused = false;
}

void CServerPhysics::LevelShutdownPreEntity()
{
	if ( !m_pEnv )
		return;
	m_pEnv->SetQuickDelete( true );
}

void CServerPhysics::LevelShutdownPostEntity()
{
	if ( !m_pEnv )
		return;

	ss::PhysSaveRestore_ForgetAllModels();

	m_pImpl->m_ShellEnv.Destroy( m_pPhysics );
	m_pEnv = NULL;

	m_pPhysics->DestroyAllCollisionSets();

	m_pWorldObject = NULL;

	m_pImpl->m_massCenterOverrides.Purge();
}

CWorld *CServerPhysics::WorldOwner( const IPhysicsObject *pObject ) const
{
	if ( !pObject || !m_pWorldObject || pObject != m_pWorldObject )
		return NULL;

	return m_Entities.m_pLevel ? m_Entities.m_pLevel->m_pWorld : NULL;
}

// Tells each active object's owner the solver moved it, holds the velocity
// managed computed for it (three floats per object), and writes out the entities
// whose own object it is. A player's movement shadow is active but not its
// entity's object. The world's object carries NULL game data, so it is
// identified by WorldOwner, never by a cast.
int32_t CServerPhysics::PhysicsUpdateOwners( void **ppObjects, float *pVelocities, int32_t nCount,
											 uint32_t *pOutOwners )
{
	if ( !ppObjects || !pVelocities || !pOutOwners || nCount <= 0 )
		return 0;

	int32_t nOwned = 0;
	for ( int32_t i = 0; i < nCount; i++ )
	{
		IPhysicsObject *pObject = static_cast<IPhysicsObject *>( ppObjects[i] );
		if ( !pObject )
			continue;

		// The world first: one exact pointer compare, and its game data is NULL.
		PhysicsOwner_t owner = { NULL, NULL, WorldOwner( pObject ), NULL, pObject };
		if ( !owner.pWorld )
			owner = PhysicsOwnerOfGameData( pObject->GetGameData(), pObject );
		if ( !owner.IsValid() )
			continue;

		owner.VPhysicsUpdate( pObject );

		if ( owner.pGame && owner.pServer && StandaloneEntity_PhysicsObject( owner.pGame ) == pObject )
		{
			StandaloneEntity_HoldMoveVelocity(
				owner.pGame, Vector( pVelocities[i * 3 + 0], pVelocities[i * 3 + 1], pVelocities[i * 3 + 2] ) );
			pOutOwners[nOwned++] = owner.pServer->GetRefEHandle().ToInt();
		}
	}

	return nOwned;
}
