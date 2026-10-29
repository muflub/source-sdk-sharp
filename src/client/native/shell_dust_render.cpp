//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The dust motes' brush test: the engine's point test, answered for the owner's brush.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "client_entity.h"

#include "shell_standalone_client.h"
#include "sourcesharp_entrypoints.h"

// The owner's brush as a collideable the engine's point test answers for. The managed
// entity's own collideable reports FSOLID_NOT_SOLID without FSOLID_VOLUME_CONTENTS, which
// the point test skips. A decorator, so the model and origin stay the managed entity's.
// The brush itself must still be compiled solid, or every point answers empty.
class CSourceSharpBrushCollideable : public ICollideable
{
public:
	CSourceSharpBrushCollideable()
		: m_pOwner( NULL )
	{
		m_vecMins.Init();
		m_vecMaxs.Init();
	}

	void Bind( C_StandaloneEntity *pOwner, const Vector &vecMins, const Vector &vecMaxs )
	{
		m_pOwner = pOwner;
		m_vecMins = vecMins;
		m_vecMaxs = vecMaxs;
	}

	bool IsBound() const { return m_pOwner != NULL; }

	// The three answers that are ours; everything else is the entity's.
	virtual SolidType_t GetSolid() const OVERRIDE { return SOLID_BSP; }

	virtual const Vector &OBBMins() const OVERRIDE { return m_vecMins; }

	virtual const Vector &OBBMaxs() const OVERRIDE { return m_vecMaxs; }

	virtual const Vector &OBBMinsPreScaled() const OVERRIDE { return m_vecMins; }

	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE { return m_vecMaxs; }

	// Stock's dust used this pair: a brush shape that stops nothing, whose contents are
	// worth asking about.
	virtual int GetSolidFlags() const OVERRIDE { return FSOLID_NOT_SOLID | FSOLID_VOLUME_CONTENTS; }

	virtual IHandleEntity *GetEntityHandle() OVERRIDE { return m_pOwner->GetEntityHandle(); }

	virtual void WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const OVERRIDE
	{
		m_pOwner->WorldSpaceTriggerBounds( pMins, pMaxs );
	}

	virtual bool TestCollision( const Ray_t &ray, unsigned int mask, trace_t &tr ) OVERRIDE
	{
		return m_pOwner->TestCollision( ray, mask, tr );
	}

	virtual bool TestHitboxes( const Ray_t &ray, unsigned int mask, trace_t &tr ) OVERRIDE
	{
		return m_pOwner->TestHitboxes( ray, mask, tr );
	}

	virtual int GetCollisionModelIndex() OVERRIDE { return m_pOwner->GetCollisionModelIndex(); }

	virtual const model_t *GetCollisionModel() OVERRIDE { return m_pOwner->GetCollisionModel(); }

	virtual const Vector &GetCollisionOrigin() const OVERRIDE { return m_pOwner->GetCollisionOrigin(); }

	virtual const QAngle &GetCollisionAngles() const OVERRIDE { return m_pOwner->GetCollisionAngles(); }

	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE
	{
		return m_pOwner->CollisionToWorldTransform();
	}

	virtual IClientUnknown *GetIClientUnknown() OVERRIDE { return NULL; }

	virtual int GetCollisionGroup() const OVERRIDE { return COLLISION_GROUP_NONE; }

	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE
	{
		m_pOwner->WorldSpaceSurroundingBounds( pMins, pMaxs );
	}

	virtual bool ShouldTouchTrigger( int ) const OVERRIDE { return false; }

	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

private:
	C_StandaloneEntity *m_pOwner;
	Vector m_vecMins;
	Vector m_vecMaxs;
};

// The managed dust motes' spawn test: the owner's brush contents at a point. 0 for an
// owner or model that is not there.
int32_t SourceSharp_ParticlesBrushPointContents( uint32_t hOwner, const float *pPointXYZ )
{
	C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( hOwner );
	if ( !pOwner || !pPointXYZ )
		return 0;

	const model_t *pModel = pOwner->GetModel();
	if ( !pModel )
		return 0;

	Vector vecMins, vecMaxs;
	modelinfo->GetModelBounds( pModel, vecMins, vecMaxs );

	CSourceSharpBrushCollideable brush;
	brush.Bind( pOwner, vecMins, vecMaxs );
	return enginetrace->GetPointContents_Collideable( &brush, Vector( pPointXYZ[0], pPointXYZ[1], pPointXYZ[2] ) );
}
