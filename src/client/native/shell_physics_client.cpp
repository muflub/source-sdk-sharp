//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client physics' native half ===================//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve C6, cluster 9 (lane c6-input). The client's physics
// callbacks move to managed the way the server's did (plan_physics_managed
// B2): managed installs its own IPhysicsCollisionEvent table on the client's
// environment (ClientPhysicsCollisionEventBinding), each slot it has taken
// over answered in C#, the rest forwarded to game/client/physics.cpp's
// g_Collisions. What is here is IShimPhysicsClient's bodies:
//
//   the environment and native's handler, for the install and the forward;
//   a surfaceprop's impact audio, the physprops read stock's PlayImpactSounds
//     makes; and
//   the emit of one impact sound, PlayImpactSounds' carry-out: the script's
//     parameters, a PAS filter at the origin, the emit from the world; and
//   the splash's reads (a collide's extent at a transform, the point contents,
//     the water's lighting) and its three fx_water.h effects.
//
// The decisions -- which impacts sound, how loud, hard or soft, merged how --
// are ClientImpactSounds' (C#).
//=============================================================================//

#include "cbase.h"
#include "vphysics_interface.h"
#include "physics_shared.h"
#include "gamesound.h"
#include "c_recipientfilter.h"
#include "fx_water.h"
#include "engine/IEngineTrace.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// game/client/physics.cpp's handler. Its class is that file's own, so it is
// named here incomplete: the address is all this file needs, and an
// IPhysicsCollisionEvent is its FIRST base (CCollisionEvent : public
// IPhysicsCollisionEvent, public IPhysicsCollisionSolver, public
// IPhysicsObjectEvent), which the Itanium C++ ABI places at offset zero -- so
// the object's address is that base's address.
class CCollisionEvent;
extern CCollisionEvent g_Collisions;

void *SourceSharp_PhysicsClientEnvironment()
{
	return physenv;
}

void *SourceSharp_PhysicsClientCollisionEvents()
{
	return (void *)&g_Collisions;
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

int32_t SourceSharp_PhysicsClientEmitImpactSound( const char *pszName, const float *pOrigin, int32_t nChannel,
                                                  float flVolume )
{
	if ( !pszName || !pszName[ 0 ] || !pOrigin )
		return 0;

	CSoundParameters params;
	if ( !GameSound_GetParameters( pszName, params, NULL ) )
		return 0;

	const Vector origin( pOrigin[ 0 ], pOrigin[ 1 ], pOrigin[ 2 ] );
	CPASAttenuationFilter filter( origin, params.soundlevel );

	EmitSound_t ep;
	ep.m_nChannel    = nChannel;
	ep.m_pSoundName  = params.soundname;
	ep.m_flVolume    = params.volume * flVolume;
	ep.m_SoundLevel  = params.soundlevel;
	ep.m_nPitch      = params.pitch;
	ep.m_pOrigin     = &origin;

	// From the world (entity 0), as stock does it: "If this entity gets
	// deleted, the sound comes out at the world origin".
	GameSound_Emit( filter, 0, ep );
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
