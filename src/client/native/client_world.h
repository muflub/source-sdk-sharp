//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// C_World, worldspawn's client entity over IClientEntity, and its collideable.
#pragma once

#include "client_entity.h"
#include "icliententity.h"
#include "iclientrenderable.h"
#include "engine/ICollideable.h"
#include "basehandle.h"
#include "mathlib/vector.h"
#include "shell_world_nettable_client.h"
#include "world_net_sync.h"

#define CWorld C_World

class C_World;
struct model_t;

// The world's ICollideable, held as a member. The engine reaches it by pointer
// through GetClientEntity( 0 )->GetCollideable(); GetEntityHandle must answer
// the owner, since the engine casts it to IClientUnknown for trace_t::m_pEnt.
class CWorldClientCollideable : public ICollideable
{
public:
	explicit CWorldClientCollideable( C_World *pOuter );

	// The BSP's own box, not the world_mins/world_maxs keys that arrive over DT_World.
	void SetBounds( const Vector &vecMins, const Vector &vecMaxs )
	{
		m_vecMins = vecMins;
		m_vecMaxs = vecMaxs;
	}

	virtual IHandleEntity *GetEntityHandle() OVERRIDE;

	virtual const Vector &OBBMinsPreScaled() const OVERRIDE { return m_vecMins; }
	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE { return m_vecMaxs; }
	virtual const Vector &OBBMins() const OVERRIDE { return m_vecMins; }
	virtual const Vector &OBBMaxs() const OVERRIDE { return m_vecMaxs; }

	virtual void WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const OVERRIDE
	{
		if ( pMins ) *pMins = m_vecMins;
		if ( pMaxs ) *pMaxs = m_vecMaxs;
	}

	// False means "use the ordinary test"; the engine clips against the BSP.
	virtual bool TestCollision( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }
	virtual bool TestHitboxes( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }

	// Brush model 1, which is the world.
	virtual int            GetCollisionModelIndex() OVERRIDE;
	virtual const model_t *GetCollisionModel() OVERRIDE;

	// Debug builds assert every trace that the world is unrotated.
	virtual const Vector      &GetCollisionOrigin() const OVERRIDE { return m_vecOrigin; }
	virtual const QAngle      &GetCollisionAngles() const OVERRIDE { return m_angRotation; }
	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE { return m_Transform; }

	virtual SolidType_t GetSolid() const OVERRIDE { return m_nSolidType; }
	virtual int         GetSolidFlags() const OVERRIDE { return m_nSolidFlags; }

	virtual IClientUnknown *GetIClientUnknown() OVERRIDE;

	virtual int GetCollisionGroup() const OVERRIDE { return COLLISION_GROUP_NONE; }

	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE
	{
		if ( pMins ) *pMins = m_vecMins;
		if ( pMaxs ) *pMaxs = m_vecMaxs;
	}

	virtual bool ShouldTouchTrigger( int ) const OVERRIDE { return false; }

	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

private:
	C_World *m_pOuter;

	Vector       m_vecMins;
	Vector       m_vecMaxs;

	Vector       m_vecOrigin;
	QAngle       m_angRotation;
	matrix3x4_t  m_Transform;

	SolidType_t  m_nSolidType;
	int          m_nSolidFlags;
};

// Worldspawn's client entity: neither a C_BaseEntity nor on the parallel
// hierarchy, so GetBaseEntity and GetGameEntity both answer NULL. Its vtable
// must be the IClientEntity chain, because the engine fills trace_t::m_pEnt and
// reaches entity 0 through it. The renderable answers are the neutral ones (the
// engine draws the BSP), and the networked values live in a blob past this
// object, located through world_net_sync.h.
class C_World : public IClientEntity
{
public:
	C_World();
	virtual ~C_World();

	// The blob: allocation over-allocates by what C# declared and is zeroed.
	// Both new forms, because tier0/memdbgon.h rewrites `new` to the four-argument one.
	static void *operator new( size_t stAllocateBlock );
	static void *operator new( size_t stAllocateBlock, int nBlockUse,
	                           const char *pFileName, int nLine );
	static void  operator delete( void *pMem );
	static void  operator delete( void *pMem, int nBlockUse,
	                              const char *pFileName, int nLine );

	unsigned char *DataBlob();
	int            BlobBytes() const;

	// Init binds this object to entity 0 at the serial the engine forced.
	bool Init( int entnum, int iSerialNum );

	void Precache();
	void Spawn();

	float       GetWaveHeight();
	const char *GetDetailSpriteMaterial();

	Vector GetWorldMins();
	Vector GetWorldMaxs();

	bool  GetStartDark();
	bool  IsColdWorld();
	float GetMaxOccludeeArea();
	float GetMinOccluderArea();
	float GetMinPropScreenSpaceWidth();
	float GetMaxPropScreenSpaceWidth();

	// IHandleEntity. This object is reused for every map, so the handle's
	// serial is what tells one map's world from the next.
	virtual void SetRefEHandle( const CBaseHandle &handle ) OVERRIDE { m_RefEHandle = handle; }
	virtual const CBaseHandle &GetRefEHandle() const OVERRIDE { return m_RefEHandle; }

	// Whether a level is loaded: the handle is bound by the level's create call
	// and cleared by Release().
	bool IsBoundToTheLevel() const { return m_RefEHandle.IsValid(); }

	// IClientUnknown
	virtual IClientUnknown     *GetIClientUnknown() OVERRIDE     { return this; }
	virtual ICollideable       *GetCollideable() OVERRIDE        { return &m_Collideable; }
	virtual IClientNetworkable *GetClientNetworkable() OVERRIDE  { return this; }
	virtual IClientRenderable  *GetClientRenderable() OVERRIDE   { return this; }
	virtual IClientEntity      *GetIClientEntity() OVERRIDE      { return this; }
	virtual IClientThinkable   *GetClientThinkable() OVERRIDE    { return this; }

	// Not on the SDK's tree. IClientEntity's GetGameEntity default (NULL) is
	// inherited on purpose: both NULL marks the world as its own hierarchy.
	virtual IGameEntity *GetBaseEntity() OVERRIDE { return NULL; }

	// IClientNetworkable
	virtual ClientClass *GetClientClass() OVERRIDE;
	virtual void         Release() OVERRIDE;
	virtual void         NotifyShouldTransmit( ShouldTransmitState_t state ) OVERRIDE {}
	virtual void         OnPreDataChanged( DataUpdateType_t updateType ) OVERRIDE {}
	virtual void         OnDataChanged( DataUpdateType_t updateType ) OVERRIDE;

	// Records that OnDataChanged is owed; the client delivers it after the net
	// update, not during it.
	virtual void         PreDataUpdate( DataUpdateType_t updateType ) OVERRIDE;

	// Delivers the deferred OnDataChanged, if owed. Called once a frame from OnRenderStart.
	void                 DeliverPendingDataChanged();

	virtual void         PostDataUpdate( DataUpdateType_t updateType ) OVERRIDE {}
	virtual bool         IsDormant() OVERRIDE { return false; }
	// -1 when no level is loaded: an invalid handle's GetEntryIndex answers
	// NUM_ENT_ENTRIES-1.
	virtual int          entindex() const OVERRIDE
	{
		return m_RefEHandle.IsValid() ? m_RefEHandle.GetEntryIndex() : -1;
	}
	virtual void         ReceiveMessage( int classID, bf_read &msg ) OVERRIDE {}
	virtual void         SetDestroyedOnRecreateEntities() OVERRIDE {}
	virtual void         OnDataUnchangedInPVS() OVERRIDE {}

	// The engine's decoder writes the blob relative to this pointer
	// (SourceSharp_WorldBlobOffset is computed from sizeof).
	virtual void *GetDataTableBasePtr() OVERRIDE { return this; }

	// IClientThinkable
	virtual void                ClientThink() OVERRIDE {}
	virtual ClientThinkHandle_t GetThinkHandle() OVERRIDE { return m_hThink; }
	virtual void                SetThinkHandle( ClientThinkHandle_t hThink ) OVERRIDE { m_hThink = hThink; }

	// IClientEntity
	virtual const Vector &GetAbsOrigin() const OVERRIDE;
	virtual const QAngle &GetAbsAngles() const OVERRIDE;
	virtual CMouthInfo   *GetMouth() OVERRIDE { return NULL; }
	virtual bool          GetSoundSpatialization( SpatializationInfo_t &info ) OVERRIDE { return false; }

	// IClientRenderable. The world is never added to the leaf system, so none
	// of these draw; they are CDefaultClientRenderable's answers.
	virtual const Vector     &GetRenderOrigin() OVERRIDE;
	virtual const QAngle     &GetRenderAngles() OVERRIDE;
	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE;
	virtual bool              ShouldDraw() OVERRIDE { return false; }
	virtual bool              IsTransparent() OVERRIDE { return false; }
	virtual bool              IsTwoPass() OVERRIDE { return false; }
	virtual void              OnThreadedDrawSetup() OVERRIDE {}
	virtual bool              UsesPowerOfTwoFrameBufferTexture() OVERRIDE { return false; }
	virtual bool              UsesFullFrameBufferTexture() OVERRIDE { return false; }
	virtual ClientShadowHandle_t GetShadowHandle() const OVERRIDE { return CLIENTSHADOW_INVALID_HANDLE; }
	virtual ClientRenderHandle_t &RenderHandle() OVERRIDE { return m_hRender; }
	virtual int               GetBody() OVERRIDE { return 0; }
	virtual int               GetSkin() OVERRIDE { return 0; }
	virtual bool              UsesFlexDelayedWeights() OVERRIDE { return false; }

	// envmap and cl_takesnapshot name their file after entity 0's model and
	// test it for NULL first; stock's C_World also answered NULL.
	virtual const model_t    *GetModel() const OVERRIDE { return NULL; }
	virtual int               DrawModel( int flags ) OVERRIDE { return 0; }
	virtual void              ComputeFxBlend() OVERRIDE {}
	virtual int               GetFxBlend() OVERRIDE { return 255; }
	virtual bool              LODTest() OVERRIDE { return true; }
	virtual bool              SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones,
	                                      int boneMask, float currentTime ) OVERRIDE { return true; }
	virtual void              SetupWeights( const matrix3x4_t *pBoneToWorld, int nFlexWeightCount,
	                                        float *pFlexWeights, float *pFlexDelayedWeights ) OVERRIDE {}
	virtual void              DoAnimationEvents() OVERRIDE {}
	virtual IPVSNotify       *GetPVSNotifyInterface() OVERRIDE { return NULL; }
	virtual void              GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE;
	virtual void              GetRenderBoundsWorldspace( Vector &mins, Vector &maxs ) OVERRIDE;
	virtual void              GetShadowRenderBounds( Vector &mins, Vector &maxs,
	                                                 ShadowType_t shadowType ) OVERRIDE;
	virtual void              GetColorModulation( float *color ) OVERRIDE;
	virtual bool              ShouldReceiveProjectedTextures( int flags ) OVERRIDE { return false; }
	virtual bool              GetShadowCastDistance( float *pDist, ShadowType_t shadowType ) const OVERRIDE { return false; }
	virtual bool              GetShadowCastDirection( Vector *pDirection, ShadowType_t shadowType ) const OVERRIDE { return false; }
	virtual bool              IsShadowDirty() OVERRIDE { return false; }
	virtual void              MarkShadowDirty( bool bDirty ) OVERRIDE {}
	virtual IClientRenderable *GetShadowParent() OVERRIDE { return NULL; }
	virtual IClientRenderable *FirstShadowChild() OVERRIDE { return NULL; }
	virtual IClientRenderable *NextShadowPeer() OVERRIDE { return NULL; }
	virtual ShadowType_t      ShadowCastType() OVERRIDE { return SHADOWS_NONE; }
	virtual void              CreateModelInstance() OVERRIDE {}
	virtual ModelInstanceHandle_t GetModelInstance() OVERRIDE { return MODEL_INSTANCE_INVALID; }
	virtual int               LookupAttachment( const char *pAttachmentName ) OVERRIDE { return -1; }
	virtual bool              GetAttachment( int number, Vector &origin, QAngle &angles ) OVERRIDE { return false; }
	virtual bool              GetAttachment( int number, matrix3x4_t &matrix ) OVERRIDE { return false; }
	virtual float            *GetRenderClipPlane() OVERRIDE { return NULL; }
	virtual void              RecordToolMessage() OVERRIDE {}
	virtual bool              IgnoresZBuffer() const OVERRIDE { return false; }

private:
	void RegisterSharedActivities();

	// Asks C# where its ten fields are, again after a managed reload moves them.
	bool ResolveFields();

	// Prints the ten when they differ from what was printed last.
	void ReportIfChanged();

	SourceSharpWorldFields_t m_Fields;

	enum { REPORT_BUFFER = 512 };
	unsigned char m_LastReported[ REPORT_BUFFER ];
	bool          m_bReportedOnce;

	CBaseHandle             m_RefEHandle;

	// The deferred OnDataChanged: -1 when none is owed, else the update type;
	// DATA_UPDATE_CREATED overrides DATA_UPDATE_DATATABLE_CHANGED.
	int                     m_DataChangeEventRef;

	ClientThinkHandle_t     m_hThink;
	ClientRenderHandle_t    m_hRender;

	CWorldClientCollideable m_Collideable;
};

void ClientWorldFactoryInit();
void ClientWorldFactoryShutdown();
C_World* GetClientWorldEntity();

// The world as a material-proxy binding; the material system takes it as `void *`.
void *GetClientWorldRenderable();
