//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The native side of client physics: the CPhysicsSystem client system row, the DLL's
// physics entry points and IShimPhysicsClient's bodies. The decisions are managed's.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "client_entity.h"
#include "vphysics_interface.h"
#include "physics_shared.h"
#include "fx_water.h"
#include "engine/IEngineTrace.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "tier0/dbg.h"
#include "tier1/interface.h"
#include "tier1/strtools.h"
#include "cdll_client_int.h"
#include "collisionproperty.h"
#include "fx.h"
#include "shell_clientsystem.h"
#include "imanaged_areas_client.h"

// Read by name by the client's shells (physics_shared.h).
IPhysicsSurfaceProps	*physprops = NULL;
IPhysicsCollision		*physcollision = NULL;

// The DLL's Init fails unless vphysics answers all three interfaces, as stock's does.
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

// The four level events, forwarded to managed.
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
