//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// What a volume query found, on either hierarchy. The spatial partition finds
// every entity; CFlaggedEntitiesEnum then drops anything that is not a
// CBaseEntity. This keeps them, with neutral answers chosen so callers do the
// ordinary thing for an entity outside the SDK. Server only: client standalone
// entities are not in the partition.
#pragma once
#define ANYENTITY_H









#include "entity_name_match.h"
#include "collideable_center.h"
#include "shell_shot_path.h"


extern ISpatialPartition *partition;

// An entity from a volume query. pHandle is the identity both kinds share, and
// what a sweep compares against (the ignored entity, a trace's hit).
struct AnyEntity_t
{
	IHandleEntity *pHandle;

	// The SDK entity. NULL for the other kind.
	IGameEntity *pBase;

	// The entity outside the SDK. NULL for a CBaseEntity.
	IGameEntity *pGame;

	// worldspawn, on neither hierarchy. Forward-declared: world.h is heavy and
	// this header is in every volume query and trace filter.
	CWorld *pWorld;

	// The world counts as something.
	bool IsValid() const { return pBase != NULL || pGame != NULL || pWorld != NULL; }

	bool IsWorld() const { return pWorld != NULL; }

	// Compared as handles, since trace_t::m_pEnt and ignore arguments are
	// IHandleEntity.
	bool Is( const IHandleEntity *pOther ) const { return pHandle == pOther; }

	const char *GetClassname() const
	{
		// The cast is legal only for these kinds. A static prop has pHandle set
		// but no IServerEntity, so bare pHandle would crash.
		if ( ( pGame || pWorld ) && pHandle )
			return ( (IServerEntity *)pHandle )->GetNetworkable()->GetClassName();

		return "";
	}

	// What a blast asks before it decides this is a victim.

	// Standalone entities are offered damage and answer for themselves. The
	// world never takes damage, as stock's CWorld leaves m_takedamage at DAMAGE_NO.
	bool CanTakeDamage() const
	{
		if ( pWorld )
			return false;

		return true;
	}

	// An entity outside the SDK tracks no water level, so it reads as dry.
	int GetWaterLevel() const
	{
		return 0;
	}

	bool IsNPC() const { return false; }

	// What the shared trace filters ask.

	// Most entities block sight, so that is the neutral answer.
	bool BlocksLOS() const
	{
		return true;
	}

	bool IsPlayer() const
	{
		return pGame ? StandaloneEntity_IsPlayer( pGame ) : false;
	}

	// Stock's FClassnameIs rule over the same classname string.
	bool ClassMatches( const char *pszClassname ) const
	{
		return EntityNames_Match( pszClassname, GetClassname() );
	}

	// What a blast asks about the entity standing in the way of a victim.

	// Weighed by the blocking rule: stock scales what a blocker absorbs by its mass.
	IPhysicsObject *VPhysicsGetObject() const
	{
		return pGame ? StandaloneEntity_PhysicsObject( pGame ) : NULL;
	}

	// Who owns it, as a handle comparable with Is().
	IHandleEntity *GetOwnerHandle() const
	{
		return pGame ? StandaloneEntity_OwnerHandle( pGame ) : NULL;
	}

	// Where to aim at it: the collideable's world-space centre, as stock's
	// CBaseEntity::BodyTarget for a non-character. Not the middle of the surrounding
	// bounds, which differs for a rotated off-centre OBB.
	Vector BodyTarget( const Vector &posSrc, bool bNoisy ) const
	{
		if ( !pHandle )
			return posSrc;

		ICollideable *pCollide = ( (IServerEntity *)pHandle )->GetCollideable();
		if ( !pCollide )
			return posSrc;

		return UTIL_CollideableWorldSpaceCenter( pCollide );
	}

	int TakeDamage( const ss::CTakeDamageInfo &info ) const
	{
		return pGame ? StandaloneEntity_TakeDamage( pGame, info ) : 0;
	}

	// No hitgroups on the other side, so the blast lands as plain damage.
	void DispatchTraceAttack( const ss::CTakeDamageInfo &info, const Vector &vecDir, trace_t *ptr ) const
	{
		if ( pGame )
			StandaloneEntity_TakeDamage( pGame, info );
	}

	// Triggers along the path of the hit. Only an SDK entity walks them; the
	// standalone hierarchy keeps no touch links.
	void TraceAttackToTriggers( const ss::CTakeDamageInfo &info, const Vector &start,
	                            const Vector &end, const Vector &dir ) const
	{
		if ( pBase )
			EntityShot_TraceAttackToTriggers( info, start, end, dir );
	}
};

// Resolves a handle to its arms. The world arm is set alongside the others, and
// a static prop answers all three NULL.
inline AnyEntity_t AnyEntityOf( IHandleEntity *pHandleEntity )
{
	AnyEntity_t ent = { pHandleEntity, NULL, NULL, NULL };

	if ( !pHandleEntity )
		return ent;

	ent.pWorld = SourceSharp_WorldFromEntityHandle( pHandleEntity );

	// GetBaseEntity() answers NULL for every server.so implementation; the field
	// stays for the three-arm shape its readers expect.
	ent.pBase = NULL;

	ent.pGame = ss::GameEntityFromEntityHandle( pHandleEntity );

	return ent;
}

// The partition enumerator, keeping entities of both kinds. Stores handles, so a
// list is the size CFlaggedEntitiesEnum's is.
class CAnyEntitiesEnum : public IPartitionEnumerator
{
public:
	CAnyEntitiesEnum( IHandleEntity **pList, int listMax )
		: m_pList( pList ), m_listMax( listMax ), m_count( 0 )
	{
	}

	virtual IterationRetval_t EnumElement( IHandleEntity *pHandleEntity ) OVERRIDE
	{
		// A static prop is in the partition and is not an entity.
		if ( !AnyEntityOf( pHandleEntity ).IsValid() )
			return ITERATION_CONTINUE;

		if ( m_count >= m_listMax )
		{
			AssertMsgOnce( 0, "reached enumerated list limit.  Increase limit or decrease radius" );
			return ITERATION_STOP;
		}

		m_pList[m_count++] = pHandleEntity;
		return ITERATION_CONTINUE;
	}

	int GetCount() const { return m_count; }

private:
	IHandleEntity **m_pList;
	int             m_listMax;
	int             m_count;
};

// CEntitySphereQuery for both kinds, with the same MAX_SPHERE_QUERY limit.
class CAnyEntitySphereQuery
{
public:
	CAnyEntitySphereQuery( const Vector &center, float radius )
	{
		CAnyEntitiesEnum sphereEnum( m_pList, ARRAYSIZE( m_pList ) );
		::partition->EnumerateElementsInSphere( PARTITION_ENGINE_NON_STATIC_EDICTS,
		                                        center, radius, false, &sphereEnum );

		m_listIndex = 0;
		m_listCount = sphereEnum.GetCount();
	}

	// Valid until NextEntity: the damage the loop deals may remove the entity.
	AnyEntity_t GetCurrentEntity() const
	{
		if ( m_listIndex < m_listCount )
			return AnyEntityOf( m_pList[m_listIndex] );

		AnyEntity_t none = { NULL, NULL, NULL, NULL };
		return none;
	}

	inline void NextEntity() { m_listIndex++; }

private:
	int            m_listIndex;
	int            m_listCount;
	IHandleEntity *m_pList[ss::kMaxSphereQuery];
};
