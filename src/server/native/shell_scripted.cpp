//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

// The ABI behind [ScriptedEntity]: class registration, and the per-entity calls
// C# makes afterwards (spawn flags, outputs, thinking, movement, mass, prop data,
// inputs, explosions).

#include "ss_util.h"

#include "ss_physics_shared.h"
#include "physicsowner.h"

#include "scripted_kinds.h"

#include "shell_standalone_player.h"

#include "bone_setup.h"
#include "datacache/imdlcache.h"

extern CGlobalVars *gpGlobals;
extern IVModelInfo *modelinfo;
extern IMDLCache *mdlcache;

#include "ss_string.h"

// Declares an entity class; called once per [ScriptedEntity] at managed startup.

// The values scripted_kinds.h restates, pinned to the engine's own.
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_SOLID_NONE == (int)SOLID_NONE );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_SOLID_BSP == (int)SOLID_BSP );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_SOLID_BBOX == (int)SOLID_BBOX );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_SOLID_VPHYSICS == (int)SOLID_VPHYSICS );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_FSOLID_NOT_SOLID == (int)FSOLID_NOT_SOLID );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_FSOLID_TRIGGER == (int)FSOLID_TRIGGER );
COMPILE_TIME_ASSERT( (int)SOURCESHARP_KIND_FSOLID_NOT_STANDABLE == (int)FSOLID_NOT_STANDABLE );

void SourceSharp_RegisterScriptedEntity( const char *pClassName, const char **ppOutputNames, int32_t nOutputCount,
										 int32_t nKind )
{
	if ( !pClassName || !pClassName[0] )
		return;

	const SourceSharpScriptedKindParams_t params = SourceSharp_ScriptedKindParams( nKind );

	// A shell-less class installs no factory, so no creation path can construct it.
	// Managed creates these in the engine-invisible handle range instead.
	if ( params.bShellLess )
		return;

	SourceSharp_InstallStandaloneClass( pClassName, ppOutputNames, nOutputCount, params.bServerOnly,
										(SolidType_t)params.nSolidType, params.nSolidFlags, params.bTouchEveryTick,
										params.bPlayer );
}

// Counts handles from the engine-invisible range that reach native and do not
// resolve in the engine's list: managed shell-less entities that some route failed
// to keep in managed code. Counted here because every crossing handle arrives here.
static int32_t g_nInvisibleRangeReach = 0;

void SourceSharp_NoteInvisibleRangeReach()
{
	g_nInvisibleRangeReach++;
}

int32_t SourceSharp_InvisibleRangeReach()
{
	return g_nInvisibleRangeReach;
}

// Places a player at its spawn point. Called by managed from OnSelectSpawnPoint,
// inside Spawn, before the player is solid or has a hull or physics shadow.
void SourceSharp_PlayerPlaceAtSpawn( uint32_t hPlayer, const float *pOriginXYZ, const float *pAnglesPYR )
{
	if ( !pOriginXYZ || !pAnglesPYR )
		return;

	CStandalonePlayer *pPlayer = SourceSharp_ResolveStandalonePlayer( hPlayer );

	if ( !pPlayer )
		return;

	const Vector vecOrigin( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	const QAngle angles( pAnglesPYR[0], pAnglesPYR[1], pAnglesPYR[2] );

	// Yaw only on the entity; the player state takes all three for the view.
	pPlayer->SetAbsTransform( vecOrigin, QAngle( 0.0f, angles[YAW], 0.0f ) );
	pPlayer->SetVelocity( vec3_origin );

	// fixangle makes the client adopt v_angle rather than predict over it.
	pPlayer->PlayerStatePtr()->v_angle = angles;
	pPlayer->PlayerStatePtr()->fixangle = FIXANGLE_ABSOLUTE;
}

// Spawnflags are claimed by the engine during map parsing, so they never reach
// the managed keyvalue hook.
int32_t SourceSharp_EntityGetSpawnFlags( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	return ent.pStandalone ? ent.pStandalone->GetSpawnFlags() : 0;
}

void SourceSharp_EntityGetSpawnOrigin( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity ) )
	{
		const Vector &vec = pStandalone->GetSpawnOrigin();
		pOutXYZ[0] = vec.x;
		pOutXYZ[1] = vec.y;
		pOutXYZ[2] = vec.z;
	}
}

void SourceSharp_EntityGetSpawnAngles( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	// Current angles, which for an entity that never moves are the mapper's.
	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveStandalone( hEntity ) )
	{
		const QAngle &ang = pStandalone->GetSpawnAngles();
		pOutPYR[0] = ang.x;
		pOutPYR[1] = ang.y;
		pOutPYR[2] = ang.z;
	}
}

void SourceSharp_EntityGetBoundsSize( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
	{
		// The collision bounds, which for a brush entity are its compiled submodel's.
		const Vector vecSize = ent.pStandalone->OBBMaxs() - ent.pStandalone->OBBMins();
		pOutXYZ[0] = vecSize.x;
		pOutXYZ[1] = vecSize.y;
		pOutXYZ[2] = vecSize.z;
		return;
	}

	if ( !ent.pBase )
		return;
}

void SourceSharp_EntitySetAngles( uint32_t hEntity, const float *pPYR )
{
	if ( !pPYR )
		return;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( CStandaloneEntity *pStandalone = ent.pStandalone )
	{
		pStandalone->SetAbsAngles( QAngle( pPYR[0], pPYR[1], pPYR[2] ) );

		// SetAbsOrigin with the same position rebuilds the transform and relinks the entity.
		pStandalone->SetAbsOrigin( pStandalone->GetAbsOrigin() );
		return;
	}

	if ( !ent.pBase )
		return;
}

// Degrees per second about the world's axes. Managed reads an IPhysicsObject's
// own spin when there is one; this is the shell's member.
void SourceSharp_EntitySetAngularVelocity( uint32_t hEntity, const float *pPYR )
{
	if ( !pPYR )
		return;

	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveEntity( hEntity ).pStandalone )
		pStandalone->SetAngularVelocity( QAngle( pPYR[0], pPYR[1], pPYR[2] ) );
}

void SourceSharp_EntityGetName( uint32_t hEntity, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = 0;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );

	if ( ent.pStandalone )
		ss::StrCopy( pOut, ent.pStandalone->GetEntityName(), nOutSize );
}

static IPhysicsObject *SourceSharp_PhysicsOf( uint32_t hEntity )
{
	return SourceSharp_ResolveEntity( hEntity ).Physics();
}

void SourceSharp_EntityGetAngularVelocity( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	if ( CStandaloneEntity *pStandalone = SourceSharp_ResolveEntity( hEntity ).pStandalone )
	{
		const QAngle &angVelocity = pStandalone->GetAngularVelocity();
		pOutPYR[0] = angVelocity[0];
		pOutPYR[1] = angVelocity[1];
		pOutPYR[2] = angVelocity[2];
	}
}

// The entity's IPhysicsObject, or NULL.
void *SourceSharp_EntityPhysicsObject( uint32_t hEntity )
{
	return SourceSharp_PhysicsOf( hEntity );
}

// The level's IPhysicsEnvironment, or NULL between levels.
void *SourceSharp_PhysicsEnvironmentPointer()
{
	return physenv;
}

// The level's world physics object, or NULL between levels. Its game data is
// NULL (PhysCreateWorld), so managed recognises it by identity.
void *SourceSharp_PhysicsWorldObject()
{
	return g_PhysWorldObject;
}

// Scale of the server-side skeleton, which places attachments on a scaled model.
// Managed validates it; this only stores it.
void SourceSharp_StandaloneSetModelScale( uint32_t hEntity, float flScale )
{
	if ( CStandaloneEntity *pEntity = SourceSharp_ResolveEntity( hEntity ).pStandalone )
		pEntity->SetModelScale( flScale );
}
