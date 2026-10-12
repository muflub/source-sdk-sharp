//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — what fork game/client/physics.cpp links of physics_shared.cpp
// SS_REASON a  a vtable the engine calls through
//
// plan_physics_bones_native_zero 2.6. physics_shared.cpp's decisions are
// managed's (SourceSharp.Client's ClientPhysicsWorld over the shared
// PhysicsWorldBuilder, SurfacePropertyManifest, SurfaceSoundPrecache and
// SolidKeyDefaults). What stays here is what a link edge, a vtable or a held
// pointer forces, each with its reason:
//
//   * the vphysics pointers: handed to this DLL by the physics module (fork
//     physics.cpp's PhysicsDLLInit and PhysicsLevelInit fill them) and read by
//     the client's shells by name through physics_shared.h;
//   * g_SolidSetup: an IVPhysicsKeyHandler fork physics.cpp hands the static
//     prop manager by address; its two slots forward to managed;
//   * g_PhysDefaultObjectParams: a constant fork physics.cpp names;
//   * PhysParseSurfaceData, PrecachePhysicsSounds, PhysCreateWorld_Shared:
//     fork physics.cpp's calls, forwarded to IManagedPhysicsClient;
//   * PhysFrictionSound / PhysFrictionEffect: named by fork physics.cpp's
//     CCollisionEvent::Friction, whose slot managed's own collision-event
//     table answers (ClientFriction); a refusal that says so if reached;
//   * EntityDataObject_Has and ReportVPhysicsStateChanged: fork physics.cpp
//     asks them on wake and sleep. Nothing in client.so creates a data object
//     (EntityDataObject_Create had no referrer, nm), so there is none to have
//     and no watcher list to walk;
//   * ClientSystem_DataObjectAccess: kClientSystems[]'s STAYS row, now a
//     registration with nothing behind it.
//
// Each of these leaves with fork game/client/physics.cpp (cluster 9).
//=============================================================================//

#include "cbase.h"
#include "vphysics_interface.h"
#include "vcollide_parse.h"
#include "physics_shared.h"
#include "solidsetdefaults.h"
#include "positionwatcher.h"
#include "data_object_access.h"
#include "shell_clientsystem.h"
#include "imanaged_areas_client.h"
#include "physics_bridge_refusal.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

IPhysics				*physics = NULL;
IPhysicsObject			*g_PhysWorldObject = NULL;
IPhysicsCollision		*physcollision = NULL;
IPhysicsEnvironment		*physenv = NULL;
IPhysicsSurfaceProps	*physprops = NULL;
// Made, read and destroyed by fork physics.cpp alone; nothing in client.so
// adds a pair to it (the pair switches had no referrer), so it stays empty.
IPhysicsObjectPairHash	*g_EntityCollisionHash = NULL;

// stock's values, which managed's ObjectParams.Defaults spells too.
const objectparams_t g_PhysDefaultObjectParams =
{
	NULL,
	1.0, //mass
	1.0, // inertia
	0.1f, // damping
	0.1f, // rotdamping
	0.05f, // rotIntertiaLimit
	"DEFAULT",
	NULL,// game data
	0.f, // volume
	1.0f, // drag coefficient
	true,// enable collisions?
};

void CSolidSetDefaults::ParseKeyValue( void *pData, const char *pKey, const char *pValue )
{
	if ( IManagedPhysicsClient *pPhysics = ManagedPhysicsClient() )
		pPhysics->SolidKeyValue( pData, pKey, pValue );
}

void CSolidSetDefaults::SetDefaults( void *pData )
{
	if ( IManagedPhysicsClient *pPhysics = ManagedPhysicsClient() )
		pPhysics->SolidDefaults( pData );
}

CSolidSetDefaults g_SolidSetup;

void PhysParseSurfaceData( IPhysicsSurfaceProps *pProps, IFileSystem *pFileSystem )
{
	(void)pProps;
	(void)pFileSystem;
	IManagedPhysicsClient *pPhysics = ManagedPhysicsClient();
	if ( !pPhysics || pPhysics->ParseSurfaceData() < 0 )
		Warning( "[SourceSharp] the surface property manifest was not parsed: %s\n",
		         pPhysics ? "managed has no surface table" : "no managed load to parse it" );
}

void PrecachePhysicsSounds()
{
	if ( IManagedPhysicsClient *pPhysics = ManagedPhysicsClient() )
		pPhysics->PrecacheSurfaceSounds();
}

// Fork physics.cpp passes NULL for the world and the world model's collide
// with g_PhysDefaultObjectParams, which is what managed builds from on its own.
IPhysicsObject *PhysCreateWorld_Shared( IGameEntity *pWorld, vcollide_t *pWorldCollide, const objectparams_t &defaultParams )
{
	(void)pWorld;
	(void)pWorldCollide;
	(void)defaultParams;
	IManagedPhysicsClient *pPhysics = ManagedPhysicsClient();
	return pPhysics ? static_cast< IPhysicsObject * >( pPhysics->CreateWorld() ) : NULL;
}

static CPhysicsBridgeRefusal s_RefuseNativeFriction(
	"a native scrape (PhysFrictionSound / PhysFrictionEffect)",
	"fork game/client/physics.cpp's CCollisionEvent::Friction names them, and that slot is "
	"managed's: ClientPhysicsCollisionEventBinding answers Friction through ClientFriction "
	"and the shared FrictionSounds (plan_physics_bones_native_zero 2.6)." );

static void RefuseNativeFriction()
{
	if ( s_RefuseNativeFriction.Reached() )
		Warning( SOURCESHARP_PHYSICS_REFUSAL_FMT, s_RefuseNativeFriction.What(), s_RefuseNativeFriction.Refusal() );
}

void PhysFrictionSound( IGameEntity *pEntity, IPhysicsObject *pObject, float energy, int surfaceProps, int surfacePropsHit )
{
	RefuseNativeFriction();
}

void PhysFrictionEffect( Vector &vecPos, Vector vecVel, float energy, int surfaceProps, int surfacePropsHit )
{
	RefuseNativeFriction();
}

bool EntityDataObject_Has( int type, const void *pKey )
{
	return false;
}

void ReportVPhysicsStateChanged( IPhysicsObject *pPhysics, IGameEntity *pEntity, bool bAwake )
{
}

class CDataObjectAccessSystem : public CClientSystem
{
};

static CDataObjectAccessSystem g_DataObjectAccessSystem;

CClientSystem *ClientSystem_DataObjectAccess() { return &g_DataObjectAccessSystem; }
