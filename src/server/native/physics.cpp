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

#include "ss_physics.h"
#include "shell_serversystem.h"
#include "overlay_library.h"
#include "ss_physics_shared.h"
#include "ss_recipient_filter.h"
#include "igamerules.h"
#include "ss_shareddefs.h"
#include <vstdlib/random.h>
#include "coordsize.h"
#include "physicsowner.h"
#include "data_object_access.h"
#include "ss_util.h"
#include "vcollide_parse.h"
extern IVEngineServer *engine;
#include "utlvector.h"
#include "ss_init_factory.h"
extern CGlobalVars *gpGlobals;
extern IVModelInfo *modelinfo;
extern class IStaticPropMgrServer *staticpropmgr;
#include "engine/IEngineSound.h"
#include "ss_world.h"
float GetCurrentGravity();
#include "vphysics/vehicles.h"
using ss::gamevcollisionevent_t;
#include "entity_shared_statics.h"
#include "ss_physsaverestore.h"
#include "engine/IStaticPropMgr.h"
using ss::masscenteroverride_t;
#include "vphysics/object_hash.h"
#include "vphysics/collision_set.h"
#include "vphysics/friction.h"
#include "fmtstr.h"
#include "vphysics/performance.h"
#include "tier1/callqueue.h"
#include "vphysics/constraints.h"

// Opens the engine half of physics_shell.h; the rest is engine-free for the gtest.
#define SOURCESHARP_PHYSICS_SHELL_ENGINE 1
#include "physics_shell.h"

#include "physicslink.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "imanaged_areas.h"
#include "ss_string.h"
#include "tier0/memdbgon.h"

// Stock's physics_shared.cpp globals, filled by CPhysicsHook.
IPhysics				*physics = NULL;
IPhysicsObject			*g_PhysWorldObject = NULL;
IPhysicsCollision		*physcollision = NULL;
IPhysicsEnvironment		*physenv = NULL;
IPhysicsSurfaceProps	*physprops = NULL;

class CPhysicsHook : public CServerSystem
{
public:
	virtual const char *Name() { return "CPhysicsHook"; }

	virtual bool Init();
	virtual void LevelInitPreEntity();
	virtual void LevelInitPostEntity();
	virtual void LevelShutdownPreEntity();
	virtual void LevelShutdownPostEntity();

	void FlushVehicleScripts() {}

	bool ShouldSimulate()
	{
		return (physenv && !m_bPaused) ? true : false;
	}

	CUtlVector<masscenteroverride_t>	m_massCenterOverrides;

	bool		m_bPaused;

	// The level's environment, owned by the game system rather than a file-scope global.
	CSourceSharpPhysicsEnvironment m_ShellEnv;
};

CPhysicsHook	g_PhysicsHook;

CServerSystem* PhysicsGameSystem()
{
	return &g_PhysicsHook;
}

bool CPhysicsHook::Init()
{
	ss::factorylist_t factories;
	
	// Get the list of interface factories to extract the physics DLL's factory
	ss::FactoryList_Retrieve( factories );

	if ( !factories.physicsFactory )
		return false;

	if ((physics = (IPhysics *)factories.physicsFactory( VPHYSICS_INTERFACE_VERSION, NULL )) == NULL ||
		(physcollision = (IPhysicsCollision *)factories.physicsFactory( VPHYSICS_COLLISION_INTERFACE_VERSION, NULL )) == NULL ||
		(physprops = (IPhysicsSurfaceProps *)factories.physicsFactory( VPHYSICS_SURFACEPROPS_INTERFACE_VERSION, NULL )) == NULL
		)
		return false;

	return true;
}

// A player shadow still live when a new environment is created points into the old one.
extern int g_nSourceSharpPlayerShadowsLive;
extern int g_nSourceSharpPlayerShadowsOutlived;

void CPhysicsHook::LevelInitPreEntity() 
{
	ss::factorylist_t factories;
	ss::FactoryList_Retrieve( factories );

	// Before the new environment exists: any shadow still live belongs to the old one.
	if ( g_nSourceSharpPlayerShadowsLive > 0 )
		g_nSourceSharpPlayerShadowsOutlived += g_nSourceSharpPlayerShadowsLive;

	// The four callback tables are managed's, installed at pre-entity init.
	physenv = g_PhysicsHook.m_ShellEnv.Create(
		physics,
		NULL,
		NULL,
		NULL,
		NULL,
		factories.engineFactory,
		gpGlobals->interval_per_tick,   // 15 ms per tick
		GetCurrentGravity(),            // HL game gravity, not real-world gravity
		10 );                           // maxCollisionsPerObjectPerTimestep

	// Managed builds the world's physics object; native holds it.
	IManagedPhysicsFrame *pPhysicsFrame = ManagedPhysicsFrame();
	g_PhysWorldObject = pPhysicsFrame ? static_cast< IPhysicsObject * >( pPhysicsFrame->CreateWorld() ) : NULL;
	if ( !g_PhysWorldObject )
		Warning( "[SourceSharp] the level has no world physics object: %s\n",
		         pPhysicsFrame ? "managed built none" : "no managed load to build it" );

	if ( pPhysicsFrame )
		pPhysicsFrame->PrecacheSurfaceSounds();

	m_bPaused = true;
}

void CPhysicsHook::LevelInitPostEntity() 
{
	m_bPaused = false;
}

void CPhysicsHook::LevelShutdownPreEntity() 
{
	if ( !physenv )
		return;
	physenv->SetQuickDelete( true );
}

void CPhysicsHook::LevelShutdownPostEntity() 
{
	if ( !physenv )
		return;

	ss::PhysSaveRestore_ForgetAllModels();

	g_PhysicsHook.m_ShellEnv.Destroy( physics );
	physenv = NULL;

	physics->DestroyAllCollisionSets();

	g_PhysWorldObject = NULL;

	m_massCenterOverrides.Purge();
	FlushVehicleScripts();
}

// Whether this object is the world's: one compare against the singleton. Both
// NULL answers no, since g_PhysWorldObject is NULL during level load.
CWorld *PhysicsWorldOwner( const IPhysicsObject *pObject )
{
	if ( !pObject || !g_PhysWorldObject )
		return NULL;

	return ( pObject == g_PhysWorldObject ) ? GetWorldEntity() : NULL;
}

// Tells each active object's owner the solver moved it, holds the velocity
// managed computed for it (three floats per object), and writes out the entities
// whose own object it is. A player's movement shadow is active but not its
// entity's object. The world's object carries NULL game data, so it is
// identified by PhysicsWorldOwner, never by a cast.
int32_t SourceSharp_PhysicsUpdateOwners( void **ppObjects, float *pVelocities, int32_t nCount,
                                         uint32_t *pOutOwners )
{
	if ( !ppObjects || !pVelocities || !pOutOwners || nCount <= 0 )
		return 0;

	int32_t nOwned = 0;
	for ( int32_t i = 0; i < nCount; i++ )
	{
		IPhysicsObject *pObject = static_cast< IPhysicsObject * >( ppObjects[ i ] );
		if ( !pObject )
			continue;

		PhysicsOwner_t owner = PhysicsOwnerOf( pObject );
		if ( !owner.IsValid() )
			continue;

		owner.VPhysicsUpdate( pObject );

		if ( owner.pGame && owner.pServer && StandaloneEntity_PhysicsObject( owner.pGame ) == pObject )
		{
			StandaloneEntity_HoldMoveVelocity( owner.pGame,
				Vector( pVelocities[ i * 3 + 0 ], pVelocities[ i * 3 + 1 ], pVelocities[ i * 3 + 2 ] ) );
			pOutOwners[ nOwned++ ] = owner.pServer->GetRefEHandle().ToInt();
		}
	}

	return nOwned;
}
