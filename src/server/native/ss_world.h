//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The worldspawn entity. This spawns first when each level begins.
#pragma once

#include "server_string_pool.h"

#include "edict.h"

#include "standalone_netserial.h"
#include "shell_world_nettable.h"
#include "world_net_sync.h"
#include "igameentity_fwd.h"

// CWorld is a native shim of its own, neither CBaseEntity nor CStandaloneEntity: it is
// edict 0, owns brush model 1, and is what a trace that hits nothing returns. Its
// networked state is declared in C# (world_net_sync.h, shell_world_nettable.cpp).
//
// It inherits only IServerEntity, the type trace_t::m_pEnt holds. The IServerNetworkable
// and ICollideable the engine asks for are members. The engine's only frequent call on
// the collideable is GetEntityHandle; the world's collision is the BSP clip, but the
// other methods still answer sensibly.
class CWorld;

namespace ss
{
struct gamevcollisionevent_t;
}
class IPhysicsObject;
class CGameTrace;
typedef CGameTrace trace_t;
struct Ray_t;
struct model_t;

// The world's ICollideable, a member of CWorld so its lifetime is the world's. Bounds
// and transform are members because the interface returns references.
class CWorldCollideable : public ICollideable
{
public:
	explicit CWorldCollideable( CWorld *pOuter );

	// The BSP's bounds, not the world_mins / world_maxs keys sent to the client.
	void SetBounds( const Vector &vecMins, const Vector &vecMaxs )
	{
		m_vecMins = vecMins;
		m_vecMaxs = vecMaxs;
	}

	// Must answer the owner, never this: the result goes into trace_t::m_pEnt.
	virtual IHandleEntity *GetEntityHandle() OVERRIDE;

	virtual const Vector &OBBMinsPreScaled() const OVERRIDE { return m_vecMins; }

	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE { return m_vecMaxs; }

	virtual const Vector &OBBMins() const OVERRIDE { return m_vecMins; }

	virtual const Vector &OBBMaxs() const OVERRIDE { return m_vecMaxs; }

	virtual void WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const OVERRIDE
	{
		if ( pMins )
			*pMins = m_vecMins;
		if ( pMaxs )
			*pMaxs = m_vecMaxs;
	}

	// False means use the ordinary test (the engine's BSP clip), not "nothing is there".
	virtual bool TestCollision( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }

	virtual bool TestHitboxes( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }

	virtual int GetCollisionModelIndex() OVERRIDE;
	virtual const model_t *GetCollisionModel() OVERRIDE;

	virtual const Vector &GetCollisionOrigin() const OVERRIDE { return m_vecOrigin; }

	virtual const QAngle &GetCollisionAngles() const OVERRIDE { return m_angRotation; }

	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE { return m_Transform; }

	virtual SolidType_t GetSolid() const OVERRIDE { return m_nSolidType; }

	virtual int GetSolidFlags() const OVERRIDE { return m_nSolidFlags; }

	// Server only; the client's world is C_World.
	virtual IClientUnknown *GetIClientUnknown() OVERRIDE { return NULL; }

	virtual int GetCollisionGroup() const OVERRIDE { return COLLISION_GROUP_NONE; }

	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE
	{
		if ( pMins )
			*pMins = m_vecMins;
		if ( pMaxs )
			*pMaxs = m_vecMaxs;
	}

	virtual bool ShouldTouchTrigger( int ) const OVERRIDE { return false; }

	// Never parented; NULL means the identity.
	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

private:
	CWorld *m_pOuter;

	Vector m_vecMins;
	Vector m_vecMaxs;

	Vector m_vecOrigin;
	QAngle m_angRotation;
	matrix3x4_t m_Transform;

	// SOLID_BSP, set in the constructor.
	SolidType_t m_nSolidType;
	int m_nSolidFlags;
};

// The world's IServerNetworkable, the object CGlobalEntityList::Clear() reaches.
// GetBaseEntity answers NULL so Clear() takes its Release arm, and Release deletes the
// owner, as CServerNetworkProperty::Release does.
class CWorldNetworkable : public IServerNetworkable
{
public:
	explicit CWorldNetworkable( CWorld *pOuter );

	// Filled by the engine through GetPVSInfo; ~CWorld hands it to CleanUpEntityClusterList.
	PVSInfo_t *PVSInfo() { return &m_PVSInfo; }

	virtual IHandleEntity *GetEntityHandle() OVERRIDE;
	virtual ServerClass *GetServerClass() OVERRIDE;
	virtual edict_t *GetEdict() const OVERRIDE;

	virtual const char *GetClassName() const OVERRIDE { return "worldspawn"; }

	// Deletes the owning CWorld, which destroys this member: nothing may touch this object
	// afterwards. A no-op leaves the old world standing and the next map faults.
	virtual void Release() OVERRIDE;

	// The engine calls this through edict_t::m_pNetworkable with no null check.
	virtual int AreaNum() const OVERRIDE { return m_PVSInfo.m_nAreaNum; }

	virtual CBaseNetworkable *GetBaseNetworkable() OVERRIDE { return NULL; }

	// NULL keeps the world on Clear()'s Release arm. Defined out of class so it is not
	// textually identical to CWorld::GetBaseEntity.
	virtual IGameEntity *GetBaseEntity() OVERRIDE;

	virtual PVSInfo_t *GetPVSInfo() OVERRIDE { return &m_PVSInfo; }

private:
	CWorld *m_pOuter;
	PVSInfo_t m_PVSInfo;
};

class IGameRules;
class CServerEntityContext;

class CWorld : public IServerEntity
{
public:
	explicit CWorld( CServerEntityContext &context );
	~CWorld();

	// What the world calls through, for its collideable and networkable members.
	CServerEntityContext &Context() const { return *m_pContext; }

	IGameRules *GameRules() const { return m_pGameRules; }

	// The world's pooled strings, for dumpgamestringtable.
	const CServerStringPool &Strings() const { return m_Strings; }

	// The ServerClass built from the managed declaration (shell_world_nettable.cpp).
	virtual ServerClass *GetServerClass();

	// Over-allocates so the C#-declared state sits past the object, like CStandaloneEntity.
	// The block is the engine's, so delete returns it through FreeEntPrivateData.
	static void *operator new( size_t stAllocateBlock );
	static void operator delete( void *pMem );

	// The bytes the props address, and how many of them there are.
	unsigned char *DataBlob();
	int BlobBytes() const;

	virtual int RequiredEdictIndex() { return 0; }

	virtual void Spawn();
	virtual void Precache();
	virtual bool KeyValue( const char *szKeyName, const char *szValue );

	virtual void VPhysicsCollision( int index, ss::gamevcollisionevent_t *pEvent ) {}

	virtual void VPhysicsFriction( IPhysicsObject *pObject, float energy, int surfaceProps, int surfacePropsHit ) {}

	// The networked fields, printed; from Spawn and cs_sv_world.
	void ReportNetState();

	// IHandleEntity
	//
	// Mirrors the handle's serial into the edict, as stock's CBaseEntity::SetRefEHandle
	// does. Without it the world's serial is 0, and a client handle (index 0, serial 0) is
	// the value LookupEntity refuses, so no client could resolve the world.
	virtual void SetRefEHandle( const CBaseHandle &handle ) OVERRIDE
	{
		COMPILE_TIME_ASSERT( STANDALONE_NETSERIAL_BITS == NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS );

		m_RefEHandle = handle;

		StandaloneNetSerial_Mirror( m_pEdict, (unsigned long)handle.GetSerialNumber() );
	}

	virtual const CBaseHandle &GetRefEHandle() const OVERRIDE { return m_RefEHandle; }

	// IServerUnknown
	virtual ICollideable *GetCollideable() OVERRIDE { return &m_Collideable; }

	virtual IServerNetworkable *GetNetworkable() OVERRIDE { return &m_Networkable; }

	// NULL: the world is not an entity of either hierarchy (see world_subject.h).
	virtual IGameEntity *GetBaseEntity() OVERRIDE { return NULL; }

	// IServerEntity
	virtual int GetModelIndex() const OVERRIDE { return m_nModelIndex; }

	virtual string_t GetModelName() const OVERRIDE { return m_iszModelName; }

	virtual void SetModelIndex( int index ) OVERRIDE { m_nModelIndex = index; }

	edict_t *GetEdict() const { return m_pEdict; }

	// Takes edict 0 and joins the entity list. Called once, from the constructor.
	void AttachToEdictZero();

private:
	// Resolves the field placements from C#, again after a managed reload.
	bool ResolveFields();

	// The level's rules object, made in Precache and deleted with the world.
	IGameRules *m_pGameRules = nullptr;

	SourceSharpWorldFields_t m_Fields;

	// The engine interfaces and DLL-tier state the world calls, and its level.
	CServerEntityContext *m_pContext;

	edict_t *m_pEdict;
	CBaseHandle m_RefEHandle;

	int m_nModelIndex;
	string_t m_iszModelName;

	// What m_iszModelName points into.
	CServerStringPool m_Strings;

	// Members, so they live and die with this object. Their m_pOuter is set in the member
	// initialiser list, before AttachToEdictZero hands &m_Networkable to edict 0.
	CWorldCollideable m_Collideable;
	CWorldNetworkable m_Networkable;
};

// Defined here, where CWorld is complete.
inline IHandleEntity *CWorldCollideable::GetEntityHandle()
{
	// Implicit on purpose: an explicit static_cast would silently become a reinterpret_cast
	// if the single-inheritance chain ever broke.
	return m_pOuter;
}

inline int CWorldCollideable::GetCollisionModelIndex()
{
	return m_pOuter->GetModelIndex();
}

inline IHandleEntity *CWorldNetworkable::GetEntityHandle()
{
	return m_pOuter;
}

inline ServerClass *CWorldNetworkable::GetServerClass()
{
	return m_pOuter->GetServerClass();
}

inline edict_t *CWorldNetworkable::GetEdict() const
{
	return m_pOuter->GetEdict();
}

// NULL: Clear() takes the Release arm for this object.
inline IGameEntity *CWorldNetworkable::GetBaseEntity()
{
	return NULL;
}

// Destroys the object this member lives inside; nothing may follow.
inline void CWorldNetworkable::Release()
{
	delete m_pOuter;
}

CWorld *GetWorldEntity();
