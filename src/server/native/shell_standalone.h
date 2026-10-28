//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// One native shell serves every scripted entity class; the class hierarchy lives in C#.
#pragma once

#include "toucher.h"
#include "igameentity.h"
#include "iservernetworkable.h"
#include "iserverentity.h"
#include "engine/ICollideable.h"
#include "server_class.h"
#include "engine_block.h"
#include "ss_activator.h"
#include "ispatialpartition.h"

#include <vector>

namespace ss { class variant_t; }
namespace ss { class CTakeDamageInfo; }

// One registered entity class: what is the same for every instance of a classname.
struct StandaloneClassDesc_t
{
	const char  *pClassName;

	const char **ppOutputNames;
	int          nOutputCount;

	// No edict, not networked, never sent (what CServerOnlyEntity means).
	bool         bServerOnly;

	SolidType_t  nSolidType;
	int          nSolidFlags;

	// Touch reported every tick (items) or only on start and end (triggers).
	bool         bTouchEveryTick;

	bool         bPlayer;

	// Bit per EntityOp_t the managed class overrides; a clear bit skips the crossing.
	// Defaults to all ones so a class whose mask never arrived still gets every op.
	uint32_t     nOverrideMask;

	// Sizes both the allocation and the send table; -1 until looked up.
	int          iTier;
};

#define SOURCESHARP_MAX_STANDALONE_OUTPUTS 32

// The CBaseEntity layer. Never put a member behind a preprocessor conditional: the blob
// sits at a fixed offset past the members.
class CStandaloneEntity : public IServerEntity, public IServerNetworkable,
                          public ICollideable, public IGameEntity
{
private:
	// Declared first so it is initialised before any member initialiser could use it.
	EngineBlock_t *m_pEngineBlock;

public:
	CStandaloneEntity();
	virtual ~CStandaloneEntity();

	// Every ICollideable, IServerEntity and IServerNetworkable getter reads the block.
	EngineBlock_t       &Block()       { return *m_pEngineBlock; }
	const EngineBlock_t &Block() const { return *m_pEngineBlock; }

	// SendProp and StateChanged offsets for block fields are both measured from this.
	static int BlockBase();

	// Marks only this field's offset; the argument-less StateChanged() resends every prop.
	void MarkBlockChanged( int nBlockOffset );

	void OnPositionChanged( const Vector &vecPrevOrigin, bool bTouchTriggers = true );

	#define SS_BLOCK_HEAD_OFFSET( field ) \
		( (int)( offsetof( EngineBlock_t, Head ) + offsetof( EngineBlockHead_t, field ) ) )

	// Memory comes back zeroed, which SDK types rely on. There is no unsized operator new.
	void *operator new( size_t nBytes, int nBlobBytes );
	void operator delete( void *pMem );

	// Pairs with the sized operator new; never called, since nothing throws.
	void operator delete( void *pMem, int nBlobBytes );

	// Before the first keyvalue: the managed instance is keyed by handle.
	void PostConstruct( const StandaloneClassDesc_t *pClass );

	virtual void Spawn();

	// --- IHandleEntity -------------------------------------------------------
	// The serial must reach the edict: it tells the engine the slot changed occupants.
	virtual void SetRefEHandle( const CBaseHandle &handle ) OVERRIDE;

	virtual const CBaseHandle &GetRefEHandle() const OVERRIDE { return BlockHandle(); }

	// --- IServerUnknown ------------------------------------------------------
	virtual ICollideable *GetCollideable() OVERRIDE { return this; }
	virtual IServerNetworkable *GetNetworkable() OVERRIDE { return this; }

	// Non-null marks a standalone entity; the world answers NULL.
	virtual IGameEntity *GetBaseEntity() OVERRIDE { return this; }

	// --- IServerEntity -------------------------------------------------------
	virtual int GetModelIndex( void ) const OVERRIDE { return Block().Head.ModelIndex; }
	virtual string_t GetModelName( void ) const OVERRIDE { return NULL_STRING; }

	virtual void SetModelIndex( int index ) OVERRIDE;

	// --- IServerNetworkable --------------------------------------------------
	virtual IHandleEntity *GetEntityHandle() OVERRIDE { return this; }

	virtual ServerClass *GetServerClass() OVERRIDE;

	unsigned char *DataBlob();

	// Per class; never use a global length.
	int BlobBytes() const;

	int Tier() const { return m_iTier; }

	// C# writes the blob through a pointer, so nothing native sees the write.
	void NetworkStateChanged();
	virtual edict_t *GetEdict() const OVERRIDE { return m_pEdict; }
	virtual const char *GetClassName() const OVERRIDE { return GetClassname(); }
	virtual void Release() OVERRIDE { delete this; }
	virtual int AreaNum() const OVERRIDE { return BlockPVSInfo()->m_nAreaNum; }
	virtual CBaseNetworkable *GetBaseNetworkable() OVERRIDE { return NULL; }
	// The engine fills this in BuildEntityClusterList; the block's server tail reserves it.
	virtual PVSInfo_t *GetPVSInfo() OVERRIDE { return BlockPVSInfo(); }

	// --- ICollideable --------------------------------------------------------
	virtual const Vector &OBBMinsPreScaled() const OVERRIDE { return Block().Head.Mins; }
	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE { return Block().Head.Maxs; }
	virtual const Vector &OBBMins() const OVERRIDE { return Block().Head.Mins; }
	virtual const Vector &OBBMaxs() const OVERRIDE { return Block().Head.Maxs; }
	virtual void WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const OVERRIDE;
	virtual bool TestCollision( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }
	// Ray against the model's hitboxes via IManagedBones. False means use the box.
	virtual bool TestHitboxes( const Ray_t &ray, unsigned int fContentsMask, trace_t &tr ) OVERRIDE;
	// Distinct from the drawn ModelIndex, which goes to zero when the entity is hidden.
	virtual int GetCollisionModelIndex() OVERRIDE { return Block().Head.CollisionModelIndex; }
	virtual const model_t *GetCollisionModel() OVERRIDE;
	virtual const Vector &GetCollisionOrigin() const OVERRIDE { return Block().Head.Origin; }
	virtual const QAngle &GetCollisionAngles() const OVERRIDE { return Block().Head.Angles; }
	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE { return Block().Head.WorldTransform; }
	virtual SolidType_t GetSolid() const OVERRIDE { return (SolidType_t)Block().Head.SolidType; }
	virtual int GetSolidFlags() const OVERRIDE { return Block().Head.SolidFlags; }
	virtual IClientUnknown *GetIClientUnknown() OVERRIDE { return NULL; }
	virtual int GetCollisionGroup() const OVERRIDE { return Block().Head.CollisionGroup; }
	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE;
	// CCollisionProperty's rule.
	virtual bool ShouldTouchTrigger( int triggerSolidFlags ) const OVERRIDE
	{
		if ( GetCollisionGroup() == COLLISION_GROUP_DEBRIS )
			return ( triggerSolidFlags & FSOLID_TRIGGER_TOUCH_DEBRIS ) != 0;

		return ( Block().Head.SolidFlags & FSOLID_TRIGGER ) == 0;
	}
	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

	// --- IGameEntity ---------------------------------------------------------
	virtual bool ShouldCollide( int, int ) const OVERRIDE { return true; }

	const Vector &GetAbsOrigin() const { return Block().Head.Origin; }
	const QAngle &GetAbsAngles() const { return Block().Head.Angles; }
	int EntIndex() const;
	const char *GetClassname() const { return m_szClassname; }
	void SetClassname( const char *pName );
	virtual const char *GetEntityName() const { return m_szName; }

	// Decided by managed code; stored for player movement and scaled collision.
	void  SetModelScale( float flScale ) { m_flModelScale = flScale; }
	float GetModelScale() const { return m_flModelScale; }

	virtual bool IsPlayer() const { return false; }
	virtual int TakeDamage( const ss::CTakeDamageInfo &info );
	int GetSpawnFlags() const { return m_nSpawnFlags; }

	void SetSpawnFlags( int nFlags ) { m_nSpawnFlags = nFlags; }

	virtual void Touch( const Toucher_t &toucher ) OVERRIDE;

	virtual void StartTouch( const Toucher_t &toucher ) OVERRIDE;
	virtual void EndTouch( const Toucher_t &toucher ) OVERRIDE;

	Toucher_t AsToucher();

	// CBaseEntity::PhysicsTouchTriggers: the engine only finds overlaps for an entity that asks.
	void TouchTriggers( const Vector &vecPrevOrigin );

	virtual void VPhysicsUpdate( IPhysicsObject *pObject ) OVERRIDE;

	virtual bool KeyValue( const char *szKeyName, const char *szValue );

	// KeyValue without the offer to managed code, for the managed map spawner.
	bool KeyValueShellOnly( const char *szKeyName, const char *szValue );

	virtual bool AcceptInput( const char *szInputName, const Activator_t &activator,
	                          IGameEntity *pCaller, const ss::variant_t &Value );

	int  GetHealth() const { return m_iHealth; }
	void SetHealth( int nHealth ) { m_iHealth = nHealth; }

	// Precaches and applies in one go: managed code sets it from OnSpawn, at the end of Spawn.
	void SetModelName( const char *pModel );

	void SetVisible( bool bVisible );
	bool IsVisible() const { return ( Block().Head.Effects & EF_NODRAW ) == 0; }

	virtual void SetAbsOrigin( const Vector &vecOrigin );

	// SetAbsOrigin (and the angles) touching no trigger: stock's TeleportEntity.
	void TeleportTo( const Vector &vecOrigin, const QAngle *pAngles );

	virtual void SetAbsAngles( const QAngle &angles );

	void SetAbsTransform( const Vector &vecOrigin, const QAngle &angles );

	void SetSolidity( SolidType_t nSolidType, int nSolidFlags );

	void SetCollisionBounds( const Vector &vecMins, const Vector &vecMaxs );

	void SetCollisionGroup( int nGroup );

	const Vector &GetSpawnOrigin() const { return m_vecSpawnOrigin; }
	const QAngle &GetSpawnAngles() const { return m_angSpawnRotation; }

	const StandaloneClassDesc_t *GetClassDesc() const { return m_pClass; }

	// Physics objects here carry NULL game data, so the SDK physics bridge skips them.
	void DestroyPhysics();
	IPhysicsObject *GetPhysics() const { return m_pPhysics; }

	// Stock's VPhysicsSwapObject: the previous object is returned, not destroyed.
	IPhysicsObject *SwapPhysics( IPhysicsObject *pObject )
	{
		IPhysicsObject *pPrevious = m_pPhysics;
		m_pPhysics = pObject;
		return pPrevious;
	}

	void UpdatePhysics();

	void SetVelocity( const Vector &vecVelocity );
	void SetGravityScale( float flScale ) { m_flGravityScale = flScale; }
	float GetGravityScale() const { return m_flGravityScale; }
	const Vector &GetVelocity() const { return m_vecVelocity; }

	// Decided by managed code; read per trace by StandaloneEntity_Velocity.
	void HoldMoveVelocity( const Vector &vecVelocity ) { m_vecMoveVelocity = vecVelocity; }
	const Vector &MoveVelocity() const { return m_vecMoveVelocity; }

	// A handle because the shooter can die mid-flight.
	void SetOwnerHandle( uint32_t hOwner );
	IHandleEntity *OwnerHandleEntity() const;

	// How many times it may bounce before the flight ends; zero stops on the first hit.
	void SetBounces( int nBounces ) { m_nBouncesLeft = nBounces; }

	void SetAngularVelocity( const QAngle &angPerSecond );

	const QAngle &GetAngularVelocity() const { return m_angVelocity; }

	// Deferred, so an entity can remove itself inside its own think or damage handler.
	virtual void Remove();
protected:
	// Managed relays (shell_dispatch.cpp), gated on the class's override bit.
	virtual void OnManagedSpawn();
	virtual bool OnManagedKeyValue( const char *szKeyName, const char *szValue );
	virtual bool OnManagedInput( const char *szInputName, const Activator_t &activator,
	                             const ss::variant_t &Value );

	// NULL: the trace hit no entity. The world is entity zero.
	virtual void OnManagedImpact( IHandleEntity *pOther );

	virtual void OnManagedMoveDone();
	virtual void OnManagedBlocked( IHandleEntity *pOther );

	// Returns the damage actually taken. The shell applies it to health and reports death.
	virtual int  OnManagedDamage( const ss::CTakeDamageInfo &info );
	virtual void OnManagedKilled( IGameEntity *pAttacker );

public:
	// A shell with no class yet answers yes, so a spawn is never skipped.
	bool ClassOverridesOp( int nOp ) const
	{
		if ( !m_pClass )
			return true;

		return ( m_pClass->nOverrideMask & ( 1u << (uint32_t)nOp ) ) != 0;
	}

public:
	float                     m_flModelScale;

	char   m_szClassname[ 64 ];

	// m_nTypeId is FNV-1a of m_szClassname; the other two are stamped by GetServerClass.
	uint32 m_nTypeId;
	uint32 m_nSchemaHash;
	uint32 m_nPayloadWords;

public:
	uint32_t ManagedHandle() const;

public:
	bool HasServerClass() const { return m_pServerClass != NULL; }

	// Re-stamps from the type table after a managed reload.
	void RestampServerClass()
	{
		m_pServerClass = NULL;
		GetServerClass();
	}

protected:
	// Resolved lazily by GetServerClass. NULL means not asked yet.
	ServerClass *m_pServerClass;

	// Decides the blob length, the ServerClass and the payload word count together.
	int          m_iTier;

	char         m_szName[ 64 ];

	char         m_szModel[ 128 ];

public:
	const char *GetModelNameString() const { return m_szModel; }

private:
	int          m_nSpawnFlags;
	int          m_iHealth;

	Vector       m_vecSpawnOrigin;
	QAngle       m_angSpawnRotation;

	IPhysicsObject *m_pPhysics;

	void UpdatePartition();
	void MovePartitionElement();
	void WorldCollisionBounds( Vector *pMins, Vector *pMaxs ) const;

	const StandaloneClassDesc_t *m_pClass;

	Vector       m_vecVelocity;
	Vector       m_vecMoveVelocity;
	float        m_flGravityScale;
	int          m_nBouncesLeft;
	QAngle       m_angVelocity;

private:
	// The handle is a uint32 in the block, overlaid by CBaseHandle; PVSInfo_t is the
	// engine's struct in reserved room, written by the engine through a const shell.
	CBaseHandle &BlockHandle()
	{ return *reinterpret_cast< CBaseHandle * >( &m_pEngineBlock->Head.Handle ); }
	const CBaseHandle &BlockHandle() const
	{ return *reinterpret_cast< const CBaseHandle * >( &m_pEngineBlock->Head.Handle ); }

	CBaseHandle &BlockOwner()
	{ return *reinterpret_cast< CBaseHandle * >( &m_pEngineBlock->Head.OwnerHandle ); }
	const CBaseHandle &BlockOwner() const
	{ return *reinterpret_cast< const CBaseHandle * >( &m_pEngineBlock->Head.OwnerHandle ); }

	PVSInfo_t *BlockPVSInfo() const
	{ return reinterpret_cast< PVSInfo_t * >( &m_pEngineBlock->Server.PvsInfo[ 0 ] ); }

	edict_t                 *m_pEdict;

	// False only for a player, whose edict the engine allocated and frees with the client.
	bool                     m_bOwnsEdict;

	SpatialPartitionHandle_t m_Partition;
};

// The C#-declared state is a trailing block past the object, sized per class.

int  SourceSharp_StandaloneBlobOffset();

// The smallest tier whose blob holds the engine block.
int  SourceSharp_StandaloneMinTier();
int  SourceSharp_StandaloneMinBlobBytes();

// The player's edict, handed from the engine to PostConstruct beside the argument-less
// factory call. Cleared unconditionally by PostConstruct.
void     SourceSharp_SetPendingPlayerEdict( edict_t *pEdict );
edict_t *SourceSharp_ClaimPendingPlayerEdict();

// Parenting, relayed to the managed hierarchy (shell_standalone_hierarchy.cpp).

bool StandaloneHierarchy_IsParented( const CStandaloneEntity *pChild );

void StandaloneHierarchy_SetParentName( CStandaloneEntity *pChild, const char *pParentName );
void StandaloneHierarchy_SetParentAttachmentName( CStandaloneEntity *pChild,
                                                  const char *pAttachmentName );

void StandaloneHierarchy_ParentMoved( CStandaloneEntity *pParent );
void StandaloneHierarchy_ChildMoved( CStandaloneEntity *pChild );

bool StandaloneHierarchy_WorldToLocal( const CStandaloneEntity *pChild,
                                       Vector *pOrigin, QAngle *pAngles );
void StandaloneHierarchy_SetLocal( CStandaloneEntity *pChild, const Vector &vecOrigin,
                                   const QAngle &angles );

// Tells the managed side that a shell native decided to free is going, before it goes.
void SourceSharp_AnnounceStandaloneRemoved( CStandaloneEntity *pEntity );

// CStandaloneEntity is final and the only IGameEntity in server.so; debug verifies it.
inline CStandaloneEntity *SourceSharp_ShellOf( IGameEntity *pEnt )
{
	Assert( !pEnt || dynamic_cast< CStandaloneEntity * >( pEnt ) != NULL );
	return static_cast< CStandaloneEntity * >( pEnt );
}

inline const CStandaloneEntity *SourceSharp_ShellOf( const IGameEntity *pEnt )
{
	return SourceSharp_ShellOf( const_cast< IGameEntity * >( pEnt ) );
}

inline CStandaloneEntity *SourceSharp_ShellOf( IServerNetworkable *pNet )
{
	Assert( !pNet || dynamic_cast< CStandaloneEntity * >( pNet ) != NULL );
	return static_cast< CStandaloneEntity * >( pNet );
}

// Bans CHandle< CStandaloneEntity >. Must stay last in the file.
#include "standalone_handle_ban.h"
