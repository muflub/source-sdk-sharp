//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON e  a synchronous mid-step answer
// World queries for the emitter entities: what is within a radius, what an
// entity is, and the engine-side halves of damage and small effects. Managed
// makes the decisions; this is what needs the engine's spatial data, collision
// code and randomness.

#include "ss_util.h"
#include "ss_shareddefs.h"

#include "ss_recipient_filter.h"
#include "ss_world.h"

#include "shell_standalone_player.h"
#include "standalone_movetype.h"

#include "shell_diagnostics.h"

#include "mathlib_port.h"

#include "model_types.h"
#include "ss_physics_shared.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"

#include "precache_model.h"
#include "ss_takedamageinfo.h"

// util.h's helpers, defined in our own TUs.
extern IVModelInfo *modelinfo;
void UTIL_BloodStream( const Vector &origin, const Vector &direction, int color, int amount );
void UTIL_BloodSpray( const Vector &pos, const Vector &dir, int color, int amount, int flags );
void UTIL_BloodDrips( const Vector &origin, const Vector &direction, int color, int amount );
void PrecacheMaterial( const char *pMaterialName );
void PrecacheParticleSystem( const char *pParticleSystemName );

#include "trace_hit.h"

// The right and up vectors of a surface facing a direction. Native so the server
// and client compute them bit-identically from the same normal.
void SourceSharp_AngleVectorsFromForward( const float *pForwardXYZ, float *pOutRightXYZ, float *pOutUpXYZ )
{
	if ( !pForwardXYZ )
		return;

	QAngle vAngles;
	VectorAngles( Vector( pForwardXYZ[0], pForwardXYZ[1], pForwardXYZ[2] ), vAngles );

	Vector vRight, vUp;
	AngleVectors( vAngles, NULL, &vRight, &vUp );

	if ( pOutRightXYZ )
	{
		pOutRightXYZ[0] = vRight.x;
		pOutRightXYZ[1] = vRight.y;
		pOutRightXYZ[2] = vRight.z;
	}

	if ( pOutUpXYZ )
	{
		pOutUpXYZ[0] = vUp.x;
		pOutUpXYZ[1] = vUp.y;
		pOutUpXYZ[2] = vUp.z;
	}
}

// Every live standalone entity; gEntList's traversals skip them.
std::vector<CStandaloneEntity *> &StandaloneEntities();

// Everything within a radius, unfiltered: managed decides what to act on. The
// count is how many were written, so a full array means there may be more.
int32_t SourceSharp_EntitiesInSphere( const float *pCenterXYZ, float flRadius, uint32_t *pOutHandles, int32_t nMax )
{
	// Hot: driven by managed thinks, many times per tick.
	SOURCESHARP_TRACE_HOT();

	if ( !pCenterXYZ || !pOutHandles || nMax <= 0 )
		return 0;

	const Vector vecCenter( pCenterXYZ[0], pCenterXYZ[1], pCenterXYZ[2] );

	int32_t nCount = 0;

	// A registry walk, not a partition query: a standalone entity that is neither
	// solid nor a trigger is not in the partition. Tests the world-space AABB, and
	// includes server-only entities (an env_fire must find the next one).
	const std::vector<CStandaloneEntity *> &standalones = StandaloneEntities();

	for ( CStandaloneEntity *pStandalone : standalones )
	{
		if ( nCount >= nMax )
			break;

		Vector vecMins, vecMaxs;
		pStandalone->WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );

		// collisionutils.cpp's IsBoxIntersectingSphere, bit for bit.
		if ( !( ss_mathlib::CalcSqrDistanceToAABB( vecMins, vecMaxs, vecCenter ) < flRadius * flRadius ) )
			continue;

		pOutHandles[nCount++] = SourceSharp_HandleFromAnyEntity( pStandalone );
	}

	return nCount;
}

// What an entity is, as one answer: one crossing per candidate a radius query
// returns, rather than four.
int32_t SourceSharp_EntityGetState( uint32_t hEntity, int32_t *pOutMoveType )
{
	if ( pOutMoveType )
		*pOutMoveType = SOURCESHARP_MOVETYPE_NONE;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return 0;

	if ( ent.pStandalone )
	{
		int32_t nStandaloneFlags = 0;

		if ( ent.pStandalone->GetSolid() != SOLID_NONE && ( ent.pStandalone->GetSolidFlags() & FSOLID_NOT_SOLID ) == 0 )
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_SOLID;

		if ( ent.pStandalone->GetPhysics() != NULL )
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_PHYSICS;

		// A player answers its own movetype; anything else with a physics object
		// the one managed wrote into its block (standalone_movetype.h).
		if ( pOutMoveType )
		{
			CStandalonePlayer *pStandalonePlayer =
				ent.pStandalone->IsPlayer() ? SourceSharp_PlayerOf( ent.pStandalone ) : NULL;

			*pOutMoveType = StandaloneMoveType_Report(
				pStandalonePlayer != NULL, pStandalonePlayer ? (int)pStandalonePlayer->MvGetMoveType() : 0,
				ent.pStandalone->GetPhysics() != NULL, ent.pStandalone->Block().Head.MoveType );
		}

		// Every standalone entity is offered damage; managed decides what lands.
		nStandaloneFlags |= SOURCESHARP_ENTSTATE_TAKESDAMAGE;

		if ( ent.pStandalone->IsPlayer() )
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_PLAYER;

		// Only a player has ground on this hierarchy.
		if ( ent.pStandalone->IsPlayer() )
		{
			CStandalonePlayer *pStandalonePlayer = SourceSharp_PlayerOf( ent.pStandalone );

			if ( pStandalonePlayer->MvGetFlags() & FL_ONGROUND )
				nStandaloneFlags |= SOURCESHARP_ENTSTATE_ONGROUND;
		}

		if ( StandaloneHierarchy_IsParented( ent.pStandalone ) )
			nStandaloneFlags |= SOURCESHARP_ENTSTATE_PARENTED;

		return nStandaloneFlags;
	}

	// No SDK hierarchy remains, so a handle that is not standalone answers nothing.
	return 0;
}

// Damage from an emitter, and its force. CalculateExplosiveDamageForce scales,
// clamps and jitters the force per victim; the jitter is server randomness and
// stays here so managed is never a source of values demo playback must reproduce.
void SourceSharp_EntityTakeDamage( uint32_t hEntity, uint32_t hAttacker, float flDamage, int32_t nDamageType,
								   const float *pDirXYZ, const float *pForceOriginXYZ, int32_t bPhysicsOnly )
{
	SOURCESHARP_TRACE();

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	IGameEntity *pEntity = ent.pBase;
	CStandaloneEntity *pStandalone = ent.pStandalone;

	if ( !pEntity && !pStandalone )
		return;

	IGameEntity *pAttacker = SourceSharp_ResolveEntity( hAttacker ).GameEntity();

	if ( !pAttacker )
		pAttacker = pEntity;

	ss::CTakeDamageInfo info( pAttacker, pAttacker, flDamage, nDamageType );

	if ( pDirXYZ && pForceOriginXYZ )
	{
		ss::CalculateExplosiveDamageForce( &info, Vector( pDirXYZ[0], pDirXYZ[1], pDirXYZ[2] ),
										   Vector( pForceOriginXYZ[0], pForceOriginXYZ[1], pForceOriginXYZ[2] ) );
	}

	if ( pStandalone )
	{
		// Physics-only means no damage; the shove is managed's.
		if ( !bPhysicsOnly )
			pStandalone->TakeDamage( info );

		return;
	}
}

// The collision box as offsets from the entity's origin (world-space AABB minus
// origin). Offsets, not a size: most props have their origin at the base.
void SourceSharp_EntityGetBounds( uint32_t hEntity, float *pOutMinsXYZ, float *pOutMaxsXYZ )
{
	// Untraced: [SuppressGCTransition].
	if ( pOutMinsXYZ )
		pOutMinsXYZ[0] = pOutMinsXYZ[1] = pOutMinsXYZ[2] = 0.0f;
	if ( pOutMaxsXYZ )
		pOutMaxsXYZ[0] = pOutMaxsXYZ[1] = pOutMaxsXYZ[2] = 0.0f;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return;

	Vector vecMins, vecMaxs;
	ent.Bounds( &vecMins, &vecMaxs );

	if ( pOutMinsXYZ )
	{
		pOutMinsXYZ[0] = vecMins.x;
		pOutMinsXYZ[1] = vecMins.y;
		pOutMinsXYZ[2] = vecMins.z;
	}

	if ( pOutMaxsXYZ )
	{
		pOutMaxsXYZ[0] = vecMaxs.x;
		pOutMaxsXYZ[1] = vecMaxs.y;
		pOutMaxsXYZ[2] = vecMaxs.z;
	}
}

// Precaches a model and answers its index, or -1 if it would not load (0 is a
// real index).
int32_t SourceSharp_PrecacheModel( const char *pszModel )
{
	// Hot: the recursive precache walk calls this per model every map change.
	SOURCESHARP_TRACE_HOT();

	if ( !pszModel || !pszModel[0] )
		return -1;

	const int nIndex = Precache_Model( pszModel );

	return nIndex;
}

// Blood at a point and in a direction: stream or drips, then spray, in stock's order.
void SourceSharp_BloodEffect( const float *pOriginXYZ, const float *pDirXYZ, int32_t nColor, float flAmount,
							  int32_t bStream, int32_t nSprayFlags )
{
	SOURCESHARP_TRACE();

	if ( !pOriginXYZ || !pDirXYZ )
		return;

	// DONT_BLEED means no blood. UTIL_BloodSpray checks it too.
	if ( nColor == ss::DONT_BLEED )
		return;

	const Vector vecOrigin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	const Vector vecDir( pDirXYZ[0], pDirXYZ[1], pDirXYZ[2] );

	if ( bStream )
		UTIL_BloodStream( vecOrigin, vecDir, nColor, (int)flAmount );
	else
		UTIL_BloodDrips( vecOrigin, vecDir, nColor, (int)flAmount );

	if ( nSprayFlags )
		UTIL_BloodSpray( vecOrigin, vecDir, nColor, (int)flAmount, nSprayFlags );
}

// The 3D skybox, as the managed sky_camera states it: pushed once per level,
// read for every player every snapshot.
static SourceSharpSkyboxParams g_SkyboxParams;
static bool g_bHaveSkyboxParams = false;

void SourceSharp_SetSkyboxParams( const SourceSharpSkyboxParams *pParams )
{
	SOURCESHARP_TRACE();

	if ( !pParams )
	{
		g_bHaveSkyboxParams = false;
		return;
	}

	g_SkyboxParams = *pParams;
	g_bHaveSkyboxParams = true;
}

// One packed RGBA word into the engine's colour type, which wants its components
// set through its setters.
template <typename T>
static void SourceSharp_UnpackColor32( uint32_t packed, T &out )
{
	out.SetR( (unsigned char)( packed & 0xFF ) );
	out.SetG( (unsigned char)( ( packed >> 8 ) & 0xFF ) );
	out.SetB( (unsigned char)( ( packed >> 16 ) & 0xFF ) );
	out.SetA( (unsigned char)( ( packed >> 24 ) & 0xFF ) );
}

// Fills the engine's struct from the last push. False for a map with no sky_camera.
bool SourceSharp_FillSkyboxParams( ss::sky3dparams_t &out )
{
	if ( !g_bHaveSkyboxParams )
		return false;

	const SourceSharpSkyboxParams &in = g_SkyboxParams;

	out.origin = Vector( in.OriginXYZ[0], in.OriginXYZ[1], in.OriginXYZ[2] );
	out.scale = in.Scale;
	out.area = in.Area;

	out.fog.enable = in.FogEnable != 0;
	out.fog.blend = in.FogBlend != 0;
	out.fog.radial = in.FogRadial != 0;
	out.fog.dirPrimary = Vector( in.FogDirPrimaryXYZ[0], in.FogDirPrimaryXYZ[1], in.FogDirPrimaryXYZ[2] );

	// Bytes, as the send table carries them, to avoid rounding twice.
	SourceSharp_UnpackColor32( in.FogColorPrimary, out.fog.colorPrimary );
	SourceSharp_UnpackColor32( in.FogColorSecondary, out.fog.colorSecondary );

	out.fog.start = in.FogStart;
	out.fog.end = in.FogEnd;
	out.fog.maxdensity = in.FogMaxDensity;

	return true;
}

// The surfaceprop an entity's own brush model declares, or -1, read as stock's
// CBreakableSurface::Spawn reads $crackmaterial. A trace reports the struck face,
// and a func_breakable_surf's undrawn faces carry nodraw's surfaceprop. -1 is the
// common answer: a studio model, more than one material, or no materials loaded
// (a dedicated server).
int32_t SourceSharp_EntityModelSurfaceProp( uint32_t hEntity )
{
	SOURCESHARP_TRACE();

	SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( !ent.IsValid() || !physprops )
		return -1;

	// Both hierarchies are IServerEntity by single inheritance, so the views share an address.
	IServerEntity *pServerEntity = (IServerEntity *)ent.ToHandleEntity();

	if ( !pServerEntity )
		return -1;

	const model_t *pModel = modelinfo->GetModel( pServerEntity->GetModelIndex() );

	if ( !pModel || modelinfo->GetModelType( pModel ) != mod_brush )
		return -1;

	model_t *pMutable = const_cast<model_t *>( pModel );

	if ( modelinfo->GetModelMaterialCount( pMutable ) != 1 )
		return -1;

	IMaterial *pMaterial = NULL;
	modelinfo->GetModelMaterials( pMutable, 1, &pMaterial );

	if ( !pMaterial )
		return -1;

	bool bFound = false;
	IMaterialVar *pVar = pMaterial->FindVar( "$surfaceprop", &bFound, false );

	if ( !bFound || !pVar )
		return -1;

	const char *pszName = pVar->GetStringValue();

	if ( !pszName || !pszName[0] )
		return -1;

	return physprops->GetSurfaceIndex( pszName );
}
