//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "networkvar.h"
#include "engine/ICollideable.h"
#include "mathlib/vector.h"
#include "ispatialpartition.h"

// Forward declarations
class CBaseEntity;
class IHandleEntity;
class QAngle;
class Vector;
struct Ray_t;
class IPhysicsObject;

// Force spatial partition updates (to avoid threading problems caused by lazy update)
void UpdateDirtySpatialPartitionEntities();

// Encapsulates collision representation for an entity
class CCollisionProperty : public ICollideable
{
	DECLARE_CLASS_NOBASE(CCollisionProperty);
	DECLARE_EMBEDDED_NETWORKVAR();
	DECLARE_PREDICTABLE();

public:
	CCollisionProperty();
	~CCollisionProperty();

	void Init(CBaseEntity* pEntity);

	// Methods of ICollideable
	virtual IHandleEntity* GetEntityHandle();

	virtual const Vector& OBBMinsPreScaled() const { return m_vecMinsPreScaled.Get(); }

	virtual const Vector& OBBMaxsPreScaled() const { return m_vecMaxsPreScaled.Get(); }

	virtual const Vector& OBBMins() const { return m_vecMins.Get(); }

	virtual const Vector& OBBMaxs() const { return m_vecMaxs.Get(); }

	virtual void WorldSpaceTriggerBounds(Vector* pVecWorldMins, Vector* pVecWorldMaxs) const;
	virtual bool TestCollision(const Ray_t& ray, unsigned int fContentsMask, trace_t& tr);
	virtual bool TestHitboxes(const Ray_t& ray, unsigned int fContentsMask, trace_t& tr);
	virtual int GetCollisionModelIndex();
	virtual const model_t* GetCollisionModel();
	virtual const Vector& GetCollisionOrigin() const;
	virtual const QAngle& GetCollisionAngles() const;
	virtual const matrix3x4_t& CollisionToWorldTransform() const;
	virtual SolidType_t GetSolid() const;
	virtual int GetSolidFlags() const;
	virtual IClientUnknown* GetIClientUnknown();
	virtual int GetCollisionGroup() const;
	virtual void WorldSpaceSurroundingBounds(Vector* pVecMins, Vector* pVecMaxs);
	virtual bool ShouldTouchTrigger(int triggerSolidFlags) const;
	virtual const matrix3x4_t* GetRootParentToWorldTransform() const;

public:
private:
private:
	alignas(8) unsigned char unused_m_pOuter[8];

	CNetworkVector(m_vecMinsPreScaled);
	CNetworkVector(m_vecMaxsPreScaled);
	CNetworkVector(m_vecMins);
	CNetworkVector(m_vecMaxs);
	alignas(8) unsigned char unused_m_flRadius[4];

	CNetworkVar(unsigned short, m_usSolidFlags);

	// Spatial partition
	alignas(2) unsigned char unused_m_Partition[2];
	CNetworkVar(unsigned char, m_nSurroundType);

	// One of the SOLID_ defines. Use GetSolid/SetSolid.
	CNetworkVar(unsigned char, m_nSolidType);
	CNetworkVar(unsigned char, m_triggerBloat);
	CNetworkVar(bool, m_bUniformTriggerBloat);

	// SUCKY: We didn't use to have to store this previously
	// but storing it here means that we can network it + avoid a ton of
	// client-side mismatch problems
	CNetworkVector(m_vecSpecifiedSurroundingMinsPreScaled);
	CNetworkVector(m_vecSpecifiedSurroundingMaxsPreScaled);
	CNetworkVector(m_vecSpecifiedSurroundingMins);
	CNetworkVector(m_vecSpecifiedSurroundingMaxs);

	// Cached off world-aligned surrounding bounds
#if 0
	short	m_surroundingMins[3];
	short	m_surroundingMaxs[3];
#else
	Vector m_vecSurroundingMins;
	Vector m_vecSurroundingMaxs;
#endif

	// pointer to the entity's physics object (vphysics.dll)
	//IPhysicsObject	*m_pPhysicsObject;

	friend class CBaseEntity;
};

// For networking this bad boy
EXTERN_RECV_TABLE(DT_CollisionProperty);

inline int CCollisionProperty::GetSolidFlags() const
{
	return m_usSolidFlags;
}
