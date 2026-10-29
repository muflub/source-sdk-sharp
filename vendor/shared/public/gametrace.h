//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "cmodel.h"
#include "utlvector.h"

#include "ispatialpartition.h"

#if defined(CLIENT_DLL)
class C_BaseEntity;
#else
class CBaseEntity;
#endif

// Purpose: A trace is returned when a box is swept through the world
// NOTE: eventually more of this class should be moved up into the base class!!
class CGameTrace : public CBaseTrace
{
public:
	// Returns true if there was any kind of impact at all
	bool DidHit() const;

	// The engine doesn't know what a CBaseEntity is, so it has a backdoor to
	// let it get at the edict.

public:
	float fractionleftsolid; // time we left a solid, only valid if we started in solid
	csurface_t surface; // surface hit (impact surface)

	int hitgroup; // 0 == generic, non-zero is specific body part
	short physicsbone; // physics bone hit by trace in studio

#if defined(CLIENT_DLL)
	C_BaseEntity* m_pEnt;
#else
	CBaseEntity* m_pEnt;
#endif

	// NOTE: this member is overloaded.
	// If hEnt points at the world entity, then this is the static prop index.
	// Otherwise, this is the hitbox index.
	int hitbox; // box hit by trace in studio

	CGameTrace() = default;

private:
	// No copy constructors allowed
	CGameTrace(const CGameTrace& vOther);
};

// Returns true if there was any kind of impact at all
inline bool CGameTrace::DidHit() const
{
	return fraction < 1 || allsolid || startsolid;
}

typedef CGameTrace trace_t;

#define TLD_DEF_LEAF_MAX 256
#define TLD_DEF_ENTITY_MAX 1024

// misyl: Made final to workaround the warning:
//   "deleting object of polymorphic class type 'CTraceListData' which has non-virtual destructor might cause undefined behavior [-Wdelete-non-virtual-dtor]"
// We can't change the ABI of IPartitionEnumerator, so...
// Feel free to make a version of this class that's non final, and a final version for existing code and do your own thing in your own mod, if this breaks your usage.
class CTraceListData final : public IPartitionEnumerator
{
public:
	CTraceListData(int nLeafMax = TLD_DEF_LEAF_MAX, int nEntityMax = TLD_DEF_ENTITY_MAX)
	{
		MEM_ALLOC_CREDIT();
		m_nLeafCount = 0;
		m_aLeafList.SetSize(nLeafMax);

		m_nEntityCount = 0;
		m_aEntityList.SetSize(nEntityMax);
	}

	~CTraceListData()
	{
		m_nLeafCount = 0;
		m_aLeafList.RemoveAll();

		m_nEntityCount = 0;
		m_aEntityList.RemoveAll();
	}

	void Reset()
	{
		m_nLeafCount = 0;
		m_nEntityCount = 0;
	}

	bool IsEmpty() const { return (m_nLeafCount == 0 && m_nEntityCount == 0); }

	// For entities...
	IterationRetval_t EnumElement(IHandleEntity* pHandleEntity)
	{
		if (m_nEntityCount >= m_aEntityList.Count())
		{
			DevMsg("CTraceListData: Max entity count along ray exceeded!\n");
			m_aEntityList.AddMultipleToTail(m_aEntityList.Count());
		}

		m_aEntityList[m_nEntityCount] = pHandleEntity;
		m_nEntityCount++;

		return ITERATION_CONTINUE;
	}

public:
	int m_nLeafCount;
	CUtlVector<int> m_aLeafList;

	int m_nEntityCount;
	CUtlVector<IHandleEntity*> m_aEntityList;
};
