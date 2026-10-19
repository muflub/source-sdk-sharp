//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client physics' native half ===================//
// SS_REASON a  a vtable the engine calls through
//
// Fork game/client/physics.cpp is off client.so (docs/client_evictions.md).
// Its decisions are SourceSharp.Client's ClientPhysicsSystem (the level's
// environment, the step, the teardown) and the managed collision tables
// (ClientPhysicsCollisionEventBinding, ClientCollisionSolverBinding). What is
// here is what a link edge or the engine forces:
//
//   the client system row "CPhysicsSystem" (kClientSystems[] walks it at its
//     stock position) and the DLL's PhysicsDLLInit / PhysicsReset /
//     PhysicsSimulate (shell_clientdll.cpp calls them by those names), each a
//     forward to IManagedPhysicsClient;
//   PhysicsDLLInit's acquisition of the vphysics pointers the client's shells
//     read by name (physprops, physcollision; physics_shared.h), held here;
//   IShimPhysicsClient's bodies: a surfaceprop's impact audio, the emit of one
//     impact sound (PlayImpactSounds' carry-out), and the splash's reads and
//     its three fx_water.h effects.
//
// The decisions -- which impacts sound, how loud, merged how, when the world
// steps -- are managed's.
//=============================================================================//

#include "cbase.h"
#include "vphysics_interface.h"
#include "physics_shared.h"
#include "c_recipientfilter.h"
#include "fx_water.h"
#include "engine/IEngineTrace.h"
#include "physics.h"
#include "shell_clientsystem.h"
#include "imanaged_areas_client.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// Held pointers the client's shells read by name (physics_shared.h's
// declarations): physprops by shell_impact_effects, shell_prediction,
// shell_soundenvelope_client, shell_worldquery_client and the impact audio
// below; physcollision by the collide extent below.
IPhysicsSurfaceProps	*physprops = NULL;
IPhysicsCollision		*physcollision = NULL;

//-----------------------------------------------------------------------------
// The DLL's physics entry points, as shell_clientdll.cpp names them.
//-----------------------------------------------------------------------------

// Stock's contract: the DLL's Init fails unless vphysics answers all three
// interfaces. physprops and physcollision are kept for the shells that read
// them by name; managed acquires its own.
bool PhysicsDLLInit( CreateInterfaceFn physicsFactory )
{
	if ( !physicsFactory( VPHYSICS_INTERFACE_VERSION, NULL ) ||
		(physprops = (IPhysicsSurfaceProps *)physicsFactory( VPHYSICS_SURFACEPROPS_INTERFACE_VERSION, NULL )) == NULL ||
		(physcollision = (IPhysicsCollision *)physicsFactory( VPHYSICS_COLLISION_INTERFACE_VERSION, NULL )) == NULL )
	{
		return false;
	}

	IManagedPhysicsClient *pPhysics = ManagedPhysicsClient();
	if ( !pPhysics || pPhysics->ParseSurfaceData() < 0 )
		Warning( "[SourceSharp] the surface property manifest was not parsed: %s\n",
		         pPhysics ? "managed has no surface table" : "no managed load to parse it" );
	return true;
}

void PhysicsReset()
{
	if ( IManagedPhysicsClient *pPhysics = ManagedPhysicsClient() )
		pPhysics->Reset();
}

void PhysicsSimulate()
{
	if ( IManagedPhysicsClient *pPhysics = ManagedPhysicsClient() )
		pPhysics->Simulate();
}

// kClientSystems[]'s "CPhysicsSystem" row: the four level events, forwarded.
class CPhysicsSystem : public CClientSystem
{
public:
	CPhysicsSystem() : CClientSystem( "CPhysicsSystem" ) {}

	virtual void LevelInitPreEntity()
	{
		if ( IManagedPhysicsClient *pPhysics = ManagedPhysicsClient() )
			pPhysics->LevelInitPreEntity();
	}

	virtual void LevelInitPostEntity()
	{
		if ( IManagedPhysicsClient *pPhysics = ManagedPhysicsClient() )
			pPhysics->LevelInitPostEntity();
	}

	virtual void LevelShutdownPreEntity()
	{
		if ( IManagedPhysicsClient *pPhysics = ManagedPhysicsClient() )
			pPhysics->LevelShutdownPreEntity();
	}

	virtual void LevelShutdownPostEntity()
	{
		if ( IManagedPhysicsClient *pPhysics = ManagedPhysicsClient() )
			pPhysics->LevelShutdownPostEntity();
	}
};

CClientSystem *ClientSystem_Physics()
{
	static CPhysicsSystem s_PhysicsSystem;
	return &s_PhysicsSystem;
}

int32_t SourceSharp_PhysicsClientSurfaceAudio( int32_t nSurfaceProps, char *pHard, int32_t nHard,
                                               char *pSoft, int32_t nSoft, float *pOut3 )
{
	if ( pHard && nHard > 0 )
		pHard[ 0 ] = 0;
	if ( pSoft && nSoft > 0 )
		pSoft[ 0 ] = 0;

	if ( !physprops || nSurfaceProps < 0 )
		return 0;

	const surfacedata_t *pSurf = physprops->GetSurfaceData( nSurfaceProps );
	if ( !pSurf )
		return 0;

	if ( pSurf->sounds.impactHard && pHard && nHard > 0 )
		V_strncpy( pHard, physprops->GetString( pSurf->sounds.impactHard ), nHard );
	if ( pSurf->sounds.impactSoft && pSoft && nSoft > 0 )
		V_strncpy( pSoft, physprops->GetString( pSurf->sounds.impactSoft ), nSoft );

	if ( pOut3 )
	{
		pOut3[ 0 ] = pSurf->audio.hardnessFactor;
		pOut3[ 1 ] = pSurf->audio.hardThreshold;
		pOut3[ 2 ] = pSurf->audio.hardVelocityThreshold;
	}
	return 1;
}

void SourceSharp_PhysicsClientCollideExtent( void *pObject, const float *pOrigin, const float *pAngles,
                                             const float *pDirection, float *pOut )
{
	if ( !pOut )
		return;

	pOut[ 0 ] = pOut[ 1 ] = pOut[ 2 ] = 0.0f;

	IPhysicsObject *pPhysics = static_cast< IPhysicsObject * >( pObject );
	if ( !pPhysics || !physcollision || !pOrigin || !pAngles || !pDirection )
		return;

	const Vector extent = physcollision->CollideGetExtent( pPhysics->GetCollide(),
		Vector( pOrigin[ 0 ], pOrigin[ 1 ], pOrigin[ 2 ] ),
		QAngle( pAngles[ 0 ], pAngles[ 1 ], pAngles[ 2 ] ),
		Vector( pDirection[ 0 ], pDirection[ 1 ], pDirection[ 2 ] ) );

	pOut[ 0 ] = extent.x;
	pOut[ 1 ] = extent.y;
	pOut[ 2 ] = extent.z;
}

int32_t SourceSharp_PhysicsClientPointContents( const float *pPoint )
{
	if ( !pPoint || !enginetrace )
		return 0;

	return enginetrace->GetPointContents( Vector( pPoint[ 0 ], pPoint[ 1 ], pPoint[ 2 ] ) );
}

void SourceSharp_PhysicsClientSplashLighting( const float *pPoint, float *pOut4 )
{
	if ( !pOut4 )
		return;

	Vector color = vec3_origin;
	float flLuminosity = 1.0f;
	if ( pPoint )
		FX_GetSplashLighting( Vector( pPoint[ 0 ], pPoint[ 1 ], pPoint[ 2 ] ), &color, &flLuminosity );

	pOut4[ 0 ] = color.x;
	pOut4[ 1 ] = color.y;
	pOut4[ 2 ] = color.z;
	pOut4[ 3 ] = flLuminosity;
}

void SourceSharp_PhysicsClientSplashEffect( int32_t nKind, const float *pOrigin, const float *pNormal, float flScale,
                                            const float *pColor, float flLifetime, float flAlpha )
{
	if ( !pOrigin || !pNormal )
		return;

	const Vector origin( pOrigin[ 0 ], pOrigin[ 1 ], pOrigin[ 2 ] );
	const Vector normal( pNormal[ 0 ], pNormal[ 1 ], pNormal[ 2 ] );

	// ClientSplashKind's order.
	switch ( nKind )
	{
	case 0:
		FX_GunshotSplash( origin, normal, flScale );
		break;
	case 1:
		FX_GunshotSlimeSplash( origin, normal, flScale );
		break;
	case 2:
	{
		Vector color = pColor ? Vector( pColor[ 0 ], pColor[ 1 ], pColor[ 2 ] ) : vec3_origin;
		FX_WaterRipple( origin, flScale, &color, flLifetime, flAlpha );
		break;
	}
	default:
		break;
	}
}
