//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON d  a layout the engine reads at fixed offsets

// Handle-based entity access for C#. A handle carries a serial, so a handle to
// a destroyed entity fails to resolve rather than naming whatever reuses its slot.

#include "ss_util.h"
#include "shell_standalone_player.h"
#include "basehandle.h"
#include "sourcesharp.h"
#include "shell_diagnostics.h"
#include "ss_mapentities.h"
#include "ss_shareddefs.h"
#include "vphysics_interface.h"
#include "standalone_relays.h"
#include "game_entity_relays.h"

#include "collision_group_names.h"
#include "physics_bridge_refusal.h"
#include "ss_world.h"
#include "entity_name_match.h"
#include "ss_takedamageinfo.h"

#include "shell_standalone_class.h"
#include "slot_table.h"
#include "edict.h"
#include "ss_string.h"

#include <algorithm>
#include <vector>
#include "shell_entity_lifecycle.h"
#include "ss_entityfactory.h"
#include "server_transform.h"
#include "server_entity_resolver.h"
#include "server_player_queries.h"
#include "server_entity_resolver.h"
#include "server_collision.h"
#include "server_entity_resolver.h"
#include "server_movement.h"
#include "server_entity_resolver.h"
#include "server_entities.h"
#include "server_entity_resolver.h"
#include "server_network.h"
#include "server_entity_resolver.h"
#include "server_models.h"
#include "server_entity_resolver.h"
#include "server_console_commands.h"
#include "server_entity_resolver.h"
#include "server_entity_context.h"

// A using-declaration rather than ss:: at the use: entity_shell_inert_slots_test
// reads `::` in a slot body as the body reaching something.
using ss::kDamageYes;

IGameEntity *SourceSharp_GameEntityFromHandleEntity( CServerEntityResolver &entities, IHandleEntity *pHandleEntity )
{
	return entities.ShellFromHandleEntity( pHandleEntity );
}

// An entity's own handle, as the packed uint32 C# is given or as a CBaseHandle.
CBaseHandle SourceSharp_RefHandleFromEntity( IGameEntity *pEntity )
{
	if ( !pEntity )
		return CBaseHandle();

	return GameEntity_RefHandle( pEntity );
}

uint32_t SourceSharp_HandleFromEntity( IGameEntity *pEntity )
{
	if ( !pEntity )
		return INVALID_EHANDLE_INDEX;

	return (uint32_t)SourceSharp_RefHandleFromEntity( pEntity ).ToInt();
}

int32_t CServerEntities::EntityIsValid( uint32_t hEntity )
{
	return m_Entities.Resolve( hEntity ).IsValid() ? 1 : 0;
}

int32_t CServerEntities::EntityIsPlayer( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	return ( ent.pStandalone && ent.pStandalone->IsPlayer() ) ? 1 : 0;
}

int32_t CServerEntities::EntityGetIndex( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	// The world is entity 0 but is on neither arm below.
	if ( ent.pWorld )
		return ent.EntityIndex();

	// -1 for a server-only entity, which has no edict.
	return ent.pStandalone ? ent.pStandalone->EntIndex() : -1;
}

void CServerEntities::EntityGetClassName( uint32_t hEntity, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = '\0';

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	ss::StrCopy( pOut, ent.Classname(), nOutSize );
}

int32_t CServerEntities::EntityGetHealth( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	return ent.pStandalone ? ent.pStandalone->GetHealth() : 0;
}

void CServerEntities::EntitySetHealth( uint32_t hEntity, int32_t nHealth )
{
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
		ent.pStandalone->SetHealth( nHealth );
}

void CServerEntities::EntityGetOrigin( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );
	if ( !ent.IsValid() )
		return;

	const Vector *pOrigin = &ent.Origin();

	pOutXYZ[0] = pOrigin->x;
	pOutXYZ[1] = pOrigin->y;
	pOutXYZ[2] = pOrigin->z;
}

void CServerTransform::EntitySetOrigin( uint32_t hEntity, const float *pXYZ )
{
	if ( !pXYZ )
		return;

	const Vector vecOrigin( pXYZ[0], pXYZ[1], pXYZ[2] );

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
		ent.pStandalone->SetAbsOrigin( vecOrigin );
}

// The entity's data blob, shared with C# rather than copied; C# writes it in place.
// Does not mark the edict changed: bound states report their own writes.
// The pointer lives as long as the entity; a caller that keeps it must use
// EntityBindBlob.
void *CServerNetwork::EntityGetDataBlob( uint32_t hEntity, int32_t *pOutSize )
{
	if ( pOutSize )
		*pOutSize = 0;

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( CStandaloneEntity *pStandalone = ent.pStandalone )
	{
		// C# bounds its writes by this length, so it must be this entity's own.
		if ( pOutSize )
			*pOutSize = pStandalone->BlobBytes();

		return pStandalone->DataBlob();
	}

	if ( ent.IsWorld() )
	{
		CWorld *pWorld = m_Entities.World();

		// Zero when managed never declared one, which the caller refuses as "no blob".
		if ( pWorld->BlobBytes() > 0 )
		{
			if ( pOutSize )
				*pOutSize = pWorld->BlobBytes();

			return pWorld->DataBlob();
		}
	}

	// NULL makes the managed read/write fail closed.
	return NULL;
}

// Whether the payload props on this entity's SendTable read the same bytes as its
// typed props. -1 for an entity with no payload. A diagnostic, so it does not
// mark the edict changed.
bool SourceSharp_StandalonePayloadAgrees( CStandaloneEntity *pEntity, int *pOutWords, int *pOutFirstBadWord );

int32_t CServerNetwork::EntityPayloadAgreement( uint32_t hEntity, int32_t *pOutWords, int32_t *pOutFirstBadWord )
{
	if ( pOutWords )
		*pOutWords = 0;
	if ( pOutFirstBadWord )
		*pOutFirstBadWord = -1;

	int nWords = 0;
	int nFirstBad = -1;
	bool bAgrees = false;

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( !ent.pStandalone )
		return -1;

	bAgrees = SourceSharp_StandalonePayloadAgrees( ent.pStandalone, &nWords, &nFirstBad );

	if ( pOutWords )
		*pOutWords = nWords;
	if ( pOutFirstBadWord )
		*pOutFirstBadWord = nFirstBad;

	// No payload props is not a disagreement.
	if ( nWords <= 0 )
		return -1;

	return bAgrees ? 1 : 0;
}

uint32_t CServerEntityLifecycle::CreateEntity( const char *pClassName )
{
	if ( !pClassName || !pClassName[0] )
		return INVALID_EHANDLE_INDEX;

	// CreateEntityByNameEither, since CreateEntityByName answers only CBaseEntity and
	// would destroy an entity of the parallel hierarchy.
	IGameEntity *pEnt = NULL;
	IGameEntity *pGameEntity = CreateEntityByNameEither( Factories(), pClassName, &pEnt, NULL );

	if ( pGameEntity )
		return SourceSharp_ShellOf( pGameEntity )->ManagedHandle();

	if ( pEnt )
		return SourceSharp_HandleFromEntity( pEnt );

	Warning( "[SourceSharp] could not create '%s' — no such entity class, or it is "
			 "neither a CBaseEntity nor an IGameEntity\n",
			 pClassName );
	return INVALID_EHANDLE_INDEX;
}

uint32_t CServerEntityLifecycle::MapSpawnCreate( const char *pClassName, const char *pPackedKeys, int32_t nBytes )
{
	const uint32_t hEntity = CreateEntity( pClassName );

	if ( hEntity == INVALID_EHANDLE_INDEX )
		return INVALID_EHANDLE_INDEX;

	const SourceSharpEntity_t ent = m_Entities.Resolver().Resolve( hEntity );

	if ( ent.pStandalone == NULL )
	{
		// The managed registry and the native factories disagree; say so.
		Warning( "[SourceSharp] map spawn: '%s' is not on the parallel hierarchy; "
				 "its map keys were not applied\n",
				 pClassName );
		return hEntity;
	}

	if ( pPackedKeys && nBytes > 0 )
	{
		const char *pAt = pPackedKeys;
		const char *pEnd = pPackedKeys + nBytes;

		while ( pAt < pEnd )
		{
			const char *pKey = pAt;
			pAt += strlen( pAt ) + 1;

			// A truncated payload: stop rather than read past pEnd.
			if ( pAt >= pEnd )
				break;

			const char *pValue = pAt;
			pAt += strlen( pAt ) + 1;

			ent.pStandalone->KeyValueShellOnly( pKey, pValue );
		}
	}

	return hEntity;
}

void CServerEntityLifecycle::EntitySpawn( uint32_t hEntity )
{
	// The shell's own Spawn is the whole precache, spawn and activate sequence.
	const SourceSharpEntity_t ent = m_Entities.Resolver().Resolve( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->Spawn();
		return;
	}
}

void CServerModels::EntitySetModel( uint32_t hEntity, const char *pModel )
{
	if ( !pModel || !pModel[0] )
		return;

	// Applied immediately: the native Spawn has already run.
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetModelName( pModel );
		return;
	}
}

// The name the entity reports from here on; the class it was made as, which pairs
// its native and managed halves and its network layout, stays.
void CServerEntities::EntitySetClassName( uint32_t hEntity, const char *pName )
{
	if ( !pName || !pName[0] )
		return;

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
		ent.pStandalone->SetReportedClassname( pName );
}

// Handle-based iteration, so a walk spanning frames cannot hold a dangling pointer.
uint32_t CServerEntities::FindEntityByClassname( uint32_t hStartAfter, const char *pClassName )
{
	if ( !pClassName || !pClassName[0] )
		return INVALID_EHANDLE_INDEX;

	// Resumes on whichever hierarchy hStartAfter belongs to.
	const SourceSharpEntity_t start = m_Entities.Resolve( hStartAfter );

	// The world is on neither hierarchy, so it comes first; resuming from its handle
	// leaves `start` empty and starts the walk below at the beginning.
	if ( !start.IsValid() && EntityNames_Match( pClassName, "worldspawn" ) )
	{
		if ( CWorld *pWorld = m_Entities.World() )
			return (uint32_t)pWorld->GetRefEHandle().ToInt();
	}

	CStandaloneEntity *pFound =
		StandaloneEntities_FindByClassname( m_Entities.Context(), start.pStandalone, pClassName );

	return pFound ? (uint32_t)pFound->GetRefEHandle().ToInt() : INVALID_EHANDLE_INDEX;
}

void CServerEntityLifecycle::EntityRemove( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = m_Entities.Resolver().Resolve( hEntity );

	if ( ent.pStandalone )
		ent.pStandalone->Remove();
}

int32_t CServerPlayerQueries::PlayerIsAlive( uint32_t hPlayer )
{
	if ( CStandalonePlayer *pStandalone = m_Entities.Player( hPlayer ) )
		return pStandalone->MvIsAlive() ? 1 : 0;

	return 0;
}

void CServerPlayerQueries::PlayerSetObserverMode( uint32_t hPlayer, int32_t nMode )
{
	if ( CStandalonePlayer *pStandalone = m_Entities.Player( hPlayer ) )
		pStandalone->SetObserver( nMode != SOURCESHARP_OBS_NONE, nMode );
}

int32_t CServerPlayerQueries::PlayerGetObserverMode( uint32_t hPlayer )
{
	if ( CStandalonePlayer *pStandalone = m_Entities.Player( hPlayer ) )
		return pStandalone->MvGetObserverMode();

	return SOURCESHARP_OBS_NONE;
}

uint32_t CServerPlayerQueries::GetPlayerHandle( int32_t nPlayerIndex )
{
	if ( CStandalonePlayer *pStandalone = m_Entities.Context().PlayerFromIndex( nPlayerIndex ) )
		return pStandalone->ManagedHandle();

	return INVALID_EHANDLE_INDEX;
}

void CServerEntities::EntitySetVisible( uint32_t hEntity, int32_t bVisible )
{
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetVisible( bVisible != 0 );
		return;
	}
}

void CServerEntities::EntityGetAngles( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	const QAngle *pAngles = NULL;

	if ( ent.pStandalone )
		pAngles = &ent.pStandalone->GetAbsAngles();

	if ( !pAngles )
		return;

	pOutPYR[0] = pAngles->x;
	pOutPYR[1] = pAngles->y;
	pOutPYR[2] = pAngles->z;
}

void CServerTransform::EntityGetVelocity( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	if ( CStandaloneEntity *pStandalone = m_Entities.Resolve( hEntity ).pStandalone )
	{
		const Vector &vecVelocity = pStandalone->GetVelocity();
		pOutXYZ[0] = vecVelocity.x;
		pOutXYZ[1] = vecVelocity.y;
		pOutXYZ[2] = vecVelocity.z;
	}
}

void CServerTransform::EntitySetVelocity( uint32_t hEntity, const float *pXYZ )
{
	if ( !pXYZ )
		return;

	if ( CStandaloneEntity *pStandalone = m_Entities.Resolve( hEntity ).pStandalone )
		pStandalone->SetVelocity( Vector( pXYZ[0], pXYZ[1], pXYZ[2] ) );
}

// A single call, so the engine unlinks and relinks the entity once.
void CServerTransform::EntityTeleport( uint32_t hEntity, const float *pOrigin, const float *pAngles,
									   int32_t bZeroVelocity )
{
	if ( !pOrigin )
		return;

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );
	if ( !ent.IsValid() )
		return;

	Vector vecOrigin( pOrigin[0], pOrigin[1], pOrigin[2] );

	QAngle angles;
	if ( pAngles )
		angles.Init( pAngles[0], pAngles[1], pAngles[2] );

	Vector vecZero( 0, 0, 0 );

	// CBaseEntity::Teleport's relink, in one write with no trigger sweep.
	ent.pStandalone->TeleportTo( vecOrigin, pAngles ? &angles : NULL );

	// Through SetVelocity, which also ends an arc in flight.
	if ( bZeroVelocity )
		ent.pStandalone->SetVelocity( vecZero );

	// Managed moves a simulated entity's physics object (and a player's shadow)
	// after this returns.
}

void CServerTransform::EntitySetGravity( uint32_t hEntity, float flGravity )
{
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetGravityScale( flGravity );
		return;
	}
}

// Native because the surface normal exists only inside the step trace.
void CServerTransform::EntitySetBounce( uint32_t hEntity, int32_t nBounces )
{
	if ( CStandaloneEntity *pStandalone = m_Entities.Standalone( hEntity ) )
		pStandalone->SetBounces( nBounces );
}

// The owner is skipped by Source's own trace filters ("don't clip against own missiles").
void CServerEntities::EntitySetOwner( uint32_t hEntity, uint32_t hOwner )
{
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetOwnerHandle( hOwner );
		return;
	}
}

// The eye, not the origin: a player's origin is at their feet.
void CServerTransform::EntityGetEyePosition( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );
	if ( !ent.IsValid() )
		return;

	// A non-player's eye is its origin, as in stock; the standalone player adds its view offset.
	Vector vecEye;

	bool bHaveEye = false;

	if ( !bHaveEye )
	{
		if ( CStandalonePlayer *pStandalonePlayer = m_Entities.Player( hEntity ) )
		{
			vecEye = pStandalonePlayer->EyePosition();
		}
		else
		{
			vecEye = ent.Origin();
		}
	}

	pOutXYZ[0] = vecEye.x;
	pOutXYZ[1] = vecEye.y;
	pOutXYZ[2] = vecEye.z;
}

void CServerTransform::EntityGetEyeAngles( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );
	if ( !ent.IsValid() )
		return;

	// A player's view angles live in CPlayerState; its body angles carry no pitch.
	QAngle angles;

	bool bHaveAngles = false;

	if ( !bHaveAngles )
	{
		if ( CStandalonePlayer *pStandalonePlayer = m_Entities.Player( hEntity ) )
		{
			angles = pStandalonePlayer->PlayerStatePtr()->v_angle;
		}
		else
		{
			angles = ent.pStandalone->GetAbsAngles();
		}
	}

	pOutPYR[0] = angles.x;
	pOutPYR[1] = angles.y;
	pOutPYR[2] = angles.z;
}

// Runs a command on one client's console. The newline terminates it, and "%s"
// keeps a percent sign in map data from being read as a format.
void CServerConsoleCommands::SendClientCommand( uint32_t hPlayer, const char *pCommand )
{
	if ( !pCommand || !pCommand[0] )
		return;

	CStandalonePlayer *pStandalone = m_Entities.Player( hPlayer );
	if ( !pStandalone )
		return;

	// A player mid-disconnect still resolves while its edict is unbound, and the
	// engine dereferences that edict without checking.
	edict_t *pEdict = m_pEngine->PEntityOfEntIndex( pStandalone->EntIndex() );
	if ( !pEdict || !pEdict->GetUnknown() )
		return;

	m_pEngine->ClientCommand( pEdict, "%s\n", pCommand );
}

// Creates and spawns template instances from parsed key/value pairs. pClassNames
// holds nCount NUL-terminated names end to end; pPackedKeys holds one
// key\0value\0... pack per entity, pPackBytes[i] long. Each entity is placed and
// spawned at once, or it would wait until the next level load. Returns how many
// were created, which can exceed nMax.
int32_t CServerEntityLifecycle::TemplateSpawnKeyed( const char *pClassNames, const char *pPackedKeys,
													const int32_t *pPackBytes, const float *pOriginsXYZ,
													const float *pAnglesPYR, int32_t nCount, uint32_t *pOutHandles,
													int32_t nMax )
{
	SOURCESHARP_TRACE();

	if ( !pClassNames || !pPackBytes || !pOriginsXYZ || !pAnglesPYR || nCount <= 0 )
		return 0;

	std::vector<IGameEntity *> standalone;
	std::vector<const char *> keys, values;

	const char *pName = pClassNames;
	const char *pPack = pPackedKeys;

	for ( int32_t i = 0; i < nCount; i++ )
	{
		const char *pClassName = pName;
		pName += strlen( pName ) + 1;

		// Pointers into the pack: a split on NUL, not a parse.
		keys.clear();
		values.clear();
		const char *pAt = pPack;
		const char *pEnd = pPack ? pPack + pPackBytes[i] : pPack;
		while ( pAt && pAt < pEnd )
		{
			const char *pKey = pAt;
			pAt += strlen( pAt ) + 1;
			if ( pAt >= pEnd )
				break;
			keys.push_back( pKey );
			values.push_back( pAt );
			pAt += strlen( pAt ) + 1;
		}
		if ( pPack )
			pPack = pEnd;

		IGameEntity *pEntity =
			MapEntity_CreateFromKeys( Factories(), pClassName, keys.data(), values.data(), (int)keys.size() );
		if ( pEntity == NULL )
		{
			Msg( "Failed to initialize templated entity of class %s\n", pClassName );
			return 0;
		}

		const Vector vecOrigin( pOriginsXYZ[i * 3], pOriginsXYZ[i * 3 + 1], pOriginsXYZ[i * 3 + 2] );
		const QAngle angles( pAnglesPYR[i * 3], pAnglesPYR[i * 3 + 1], pAnglesPYR[i * 3 + 2] );

		StandaloneEntity_PlaceAndSpawn( pEntity, &vecOrigin, &angles );
		standalone.push_back( pEntity );
	}

	int32_t nWritten = 0;
	int32_t nCreated = 0;

	for ( IGameEntity *pEntity : standalone )
	{
		nCreated++;
		if ( pOutHandles && nWritten < nMax )
			pOutHandles[nWritten++] = SourceSharp_ShellOf( pEntity )->ManagedHandle();
	}

	return nCreated;
}

// Sets a player's view angles. fixangle makes the client adopt v_angle instead of
// predicting over it from its next usercmd.
void CServerPlayerQueries::PlayerSnapEyeAngles( uint32_t hPlayer, const float *pPitchYawRoll )
{
	if ( !pPitchYawRoll )
		return;

	CStandalonePlayer *pPlayer = m_Entities.Player( hPlayer );

	if ( !pPlayer )
		return;

	pPlayer->PlayerStatePtr()->v_angle = QAngle( pPitchYawRoll[0], pPitchYawRoll[1], pPitchYawRoll[2] );
	pPlayer->PlayerStatePtr()->fixangle = FIXANGLE_ABSOLUTE;
}

// Sets a player's move type outside a movement frame (noclip) and returns the
// previous one, so a toggle is one call. SOURCESHARP_MOVETYPE_* are MOVETYPE_*.
int32_t CServerMovement::PlayerSetMoveType( uint32_t hPlayer, int32_t nMoveType )
{
	CStandalonePlayer *pPlayer = m_Entities.Player( hPlayer );

	if ( !pPlayer )
		return SOURCESHARP_MOVETYPE_NONE;

	const int32_t nWas = (int32_t)pPlayer->MvGetMoveType();

	pPlayer->MvSetMoveType( (MoveType_t)nMoveType );

	return nWas;
}

// Whether the entity is sent to every client regardless of PVS, as for a camera
// viewed from another room (stock's CTriggerCamera uses FL_EDICT_ALWAYS).
void CServerNetwork::EntitySetTransmitAlways( uint32_t hEntity, int32_t bAlways )
{
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( CStandaloneEntity *pStandalone = ent.pStandalone )
	{
		edict_t *pEdict = pStandalone->GetEdict();
		if ( !pEdict )
			return;

		// CheckTransmit decides from one transmit flag, so the old one is cleared first.
		pEdict->ClearTransmitState();
		pEdict->m_fStateFlags |= ( bAlways ? FL_EDICT_ALWAYS : FL_EDICT_PVSCHECK );
		return;
	}
}

// Hands a map keyvalue to a spawned entity; the shell or its managed class claims it.
int32_t CServerEntities::EntitySetKeyValue( uint32_t hEntity, const char *pKey, const char *pValue )
{
	if ( !pKey || !pKey[0] )
		return 0;

	// An empty value is legal; NULL would reach strcmp.
	if ( !pValue )
		pValue = "";

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
		return ent.pStandalone->KeyValue( pKey, pValue ) ? 1 : 0;

	return 0;
}

// The collision group, in SourceSharp's names (collision_group_names.h) rather
// than Collision_Group_t's numbers. The first row is the fallback both ways.
static const CollisionGroupName_t s_CollisionGroupNames[] = {
	{ SS_COLLISION_GROUP_NONE, COLLISION_GROUP_NONE },
	{ SS_COLLISION_GROUP_VEHICLE_CLIP, COLLISION_GROUP_VEHICLE_CLIP },
	{ SS_COLLISION_GROUP_WEAPON, COLLISION_GROUP_WEAPON },
	{ SS_COLLISION_GROUP_DEBRIS, COLLISION_GROUP_DEBRIS },
	{ SS_COLLISION_GROUP_INTERACTIVE, COLLISION_GROUP_INTERACTIVE },
	{ SS_COLLISION_GROUP_PROJECTILE, COLLISION_GROUP_PROJECTILE },
	{ SS_COLLISION_GROUP_BREAKABLE_GLASS, COLLISION_GROUP_BREAKABLE_GLASS },
};

static const int s_nCollisionGroupNames = sizeof( s_CollisionGroupNames ) / sizeof( s_CollisionGroupNames[0] );

void CServerCollision::EntitySetCollisionGroup( uint32_t hEntity, int32_t nGroup )
{
	const int nEngineGroup = CollisionGroup_ToEngine( nGroup, s_CollisionGroupNames, s_nCollisionGroupNames );

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
	{
		ent.pStandalone->SetCollisionGroup( nEngineGroup );
		return;
	}
}

// Many-to-one: a group with no SourceSharp name reads as None.
int32_t CServerCollision::EntityGetCollisionGroup( uint32_t hEntity )
{
	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	int nEngineGroup = COLLISION_GROUP_NONE;

	if ( ent.pStandalone )
		nEngineGroup = ent.pStandalone->GetCollisionGroup();
	else if ( ent.pBase )
		nEngineGroup = ent.pBase->GetCollisionGroup();
	else
		return 0;

	return CollisionGroup_FromEngine( nEngineGroup, s_CollisionGroupNames, s_nCollisionGroupNames );
}

void CServerModels::EntityGetModelName( uint32_t hEntity, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = '\0';

	const SourceSharpEntity_t ent = m_Entities.Resolve( hEntity );

	if ( ent.pStandalone )
	{
		ss::StrCopy( pOut, ent.pStandalone->GetModelNameString(), nOutSize );
		return;
	}
}

// Bounds as offsets from the origin, in the entity's own space.
void CServerCollision::EntitySetBounds( uint32_t hEntity, const float *pMinsXYZ, const float *pMaxsXYZ )
{
	// Both or neither.
	if ( !pMinsXYZ || !pMaxsXYZ )
		return;

	if ( CStandaloneEntity *pStandalone = m_Entities.Standalone( hEntity ) )
	{
		pStandalone->SetCollisionBounds( Vector( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] ),
										 Vector( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] ) );
	}
}

// One call, because UpdatePartition reads both halves to decide whether the
// entity is in the partition at all.
void CServerCollision::EntitySetSolidity( uint32_t hEntity, int32_t nSolidType, int32_t nSolidFlags )
{
	if ( CStandaloneEntity *pStandalone = m_Entities.Standalone( hEntity ) )
		pStandalone->SetSolidity( (SolidType_t)nSolidType, nSolidFlags );
}

// The bloat half of CCollisionProperty::UseTriggerBounds; the flag goes through EntitySetSolidity.
void CServerCollision::EntitySetTriggerBloat( uint32_t hEntity, int32_t nBloat, int32_t bUniform )
{
	if ( CStandaloneEntity *pStandalone = m_Entities.Standalone( hEntity ) )
		pStandalone->SetTriggerBloat( nBloat, bUniform != 0 );
}

// Wholesale, for a restore putting back a value it read.
void CServerEntities::EntitySetSpawnFlags( uint32_t hEntity, int32_t nSpawnFlags )
{
	if ( CStandaloneEntity *pStandalone = m_Entities.Standalone( hEntity ) )
		pStandalone->SetSpawnFlags( nSpawnFlags );
}

// The gravity scale: a multiple of the world's, so 1 is no change.
float CServerTransform::EntityGetGravity( uint32_t hEntity )
{
	CStandaloneEntity *pStandalone = m_Entities.Standalone( hEntity );

	return pStandalone ? pStandalone->GetGravityScale() : 0.0f;
}
