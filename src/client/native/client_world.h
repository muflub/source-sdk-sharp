//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — client_world.h =====//
// C_World, worldspawn's client entity over IClientEntity, and its collideable:
// moved out of the fork's edited game/client/c_world.h (client.so on
// unmodified Valve headers, user 2026-10-25).
//=============================================================================//
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

//-----------------------------------------------------------------------------
// THE WORLD'S ICollideable, HELD AS A MEMBER — the client's twin of
// game/server/world.h's CWorldCollideable, on exactly the same terms.
//
// The engine reaches it BY POINTER and never by type:
// CEngineTraceClient::GetWorldCollideable() is
// `entitylist->GetClientEntity( 0 )->GetCollideable()` (engine/enginetrace.cpp
// :1330, 2018 drop) and every world clip on this side goes through it. So it
// has to be there and it must NOT be this object's own vtable — a member is
// what makes "the world answers a collideable" and "the world IS a collideable"
// two different statements.
//
// GetEntityHandle() IS THE ONE METHOD THE ENGINE ACTUALLY CALLS, and it must
// answer the OWNER rather than `this`: CEngineTraceClient::SetTraceEntity takes
// `pCollideable->GetEntityHandle()`, casts it to IClientUnknown and asks it for
// GetIClientEntity() — which is the pointer that lands in trace_t::m_pEnt.
// Answering `this` would put an ICollideable's vtable where an IClientUnknown's
// is expected, which is the wrong-vtable bug handle_cross.h exists to prevent.
//-----------------------------------------------------------------------------
class CWorldClientCollideable : public ICollideable
{
public:
	explicit CWorldClientCollideable( C_World *pOuter );

	// The BSP's own box, taken from the model at C_World::Init. Not the same
	// numbers as the world_mins / world_maxs KEYS, which are the mapper's
	// declaration and arrive over DT_World for the renderer.
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

	// FALSE MEANS "use the ordinary test", not "nothing is there". Both of
	// these are the hook an entity uses to answer a ray itself, and the world
	// has neither: the engine clips against the BSP, which IS the ordinary
	// test.
	virtual bool TestCollision( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }
	virtual bool TestHitboxes( const Ray_t &, unsigned int, trace_t & ) OVERRIDE { return false; }

	// Brush model 1, which is the world.
	virtual int            GetCollisionModelIndex() OVERRIDE;
	virtual const model_t *GetCollisionModel() OVERRIDE;

	// THE TWO THE ENGINE ASSERTS ON, every trace, in a debug build:
	// "Make sure the world entity is unrotated" (engine/enginetrace.cpp:1437).
	virtual const Vector      &GetCollisionOrigin() const OVERRIDE { return m_vecOrigin; }
	virtual const QAngle      &GetCollisionAngles() const OVERRIDE { return m_angRotation; }
	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE { return m_Transform; }

	virtual SolidType_t GetSolid() const OVERRIDE { return m_nSolidType; }
	virtual int         GetSolidFlags() const OVERRIDE { return m_nSolidFlags; }

	// Client side, so this is the half the SERVER's twin answers NULL for.
	virtual IClientUnknown *GetIClientUnknown() OVERRIDE;

	virtual int GetCollisionGroup() const OVERRIDE { return COLLISION_GROUP_NONE; }

	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE
	{
		if ( pMins ) *pMins = m_vecMins;
		if ( pMaxs ) *pMaxs = m_vecMaxs;
	}

	virtual bool ShouldTouchTrigger( int ) const OVERRIDE { return false; }

	// Never parented. NULL is how every caller spells "no parent transform".
	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

private:
	C_World *m_pOuter;

	Vector       m_vecMins;
	Vector       m_vecMaxs;

	Vector       m_vecOrigin;
	QAngle       m_angRotation;
	matrix3x4_t  m_Transform;

	// SOLID_BSP, and it is the only honest answer: this object's shape IS the
	// level. Never changes after the constructor.
	SolidType_t  m_nSolidType;
	int          m_nSolidFlags;
};

//-----------------------------------------------------------------------------
// C_World IS ITS OWN HIERARCHY (plan_entity.md §3.11, as amended), the client's
// twin of the CWorld that game/server/world.h declares.
//
// IT IS NOT A C_BaseEntity AND IT IS NOT A C_StandaloneEntity. The owner's
// standing instruction has both halves in it — *"CWorld should not inherit
// CStandaloneEntity either — it should be its own standalone class. A trace
// should be able to return a pointer to CWorld, or CStandaloneEntity, or
// CBaseEntity"* — and §3.11 says the two sides come off the SDK hierarchy
// together. So both partition arms answer NULL for this object:
//
//     GetBaseEntity()   NULL   -> not on the SDK's tree
//     GetGameEntity()   NULL   -> not on the parallel hierarchy
//
// which is what "a third hierarchy" means when the partition is asked about it,
// and it is why world_subject.h exists: two arms both answering NULL is
// indistinguishable from "nothing there" unless somebody asks the third
// question first.
//
// WHY THE CHAIN IS INHERITED HERE WHERE THE SERVER'S IS NARROW. CWorld derives
// IServerEntity — eight methods — and HOLDS its IServerNetworkable and its
// ICollideable, because the engine only ever wants those two by pointer. The
// client cannot make the same split, and the reason is measured in the engine
// rather than chosen: `trace_t::m_pEnt` on this side is filled from
// `pUnk->GetIClientEntity()` (engine/enginetrace.cpp:1026), and
// CEngineTraceClient::GetWorldCollideable reaches the world as
// `entitylist->GetClientEntity( 0 )` — an IClientEntity *, dereferenced with no
// test at engine/enginetrace.cpp:417. So the world's own vtable has to BE the
// IClientEntity chain, exactly as the server's has to be the IServerEntity one.
// The ICollideable is still a member, for the same reason it is one there.
//
// EVERY IClientRenderable ANSWER BELOW IS THE NEUTRAL ONE, and that is not a
// stub: the world's pixels are the BSP's, drawn by the engine, and this object
// was never in the client leaf system. CDefaultClientRenderable's bodies are
// where these come from, verbatim, because that is Valve's own written-down
// answer to "a thing that is reached as a renderable and does not draw".
//
// NO DECLARE_CLIENTCLASS AND NO RECEIVE TABLE. The ClientClass and every one of
// its props are built in shell_world_nettable_client.cpp out of
// SourceSharp.Shared.WorldNetState — the same declaration the server's SendTable
// comes from — and GetClientClass() below is the one line that hands it over.
// The values live in a blob past this object and every reader gets at them by
// asking C# where the field is (world_net_sync.h) rather than by naming a
// member.
//-----------------------------------------------------------------------------
class C_World : public IClientEntity
{
public:
	C_World( void );
	virtual ~C_World( void );

	//-------------------------------------------------------------------------
	// THE BLOB. AllocClientWorld over-allocates by what C# declared, and the
	// allocation is calloc'd — so the blob starts zeroed and every value has
	// the default its member had.
	//
	// Both forms, because tier0/memdbgon.h rewrites `new` to the four-argument
	// one and a class that declares only the plain form hides the other.
	//-------------------------------------------------------------------------
	static void *operator new( size_t stAllocateBlock );
	static void *operator new( size_t stAllocateBlock, int nBlockUse,
	                           const char *pFileName, int nLine );
	static void  operator delete( void *pMem );
	static void  operator delete( void *pMem, int nBlockUse,
	                              const char *pFileName, int nLine );

	unsigned char *DataBlob();
	int            BlobBytes() const;

	// The factory's two halves. Init binds this object to entity 0 at the
	// serial the engine forced; Release gives the binding back.
	bool Init( int entnum, int iSerialNum );

	void Precache();
	void Spawn();

	//-------------------------------------------------------------------------
	// The ten, as reads of the blob.
	//-------------------------------------------------------------------------
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

	//=========================================================================
	// IHandleEntity
	//=========================================================================
	// THE SERIAL IS THE WHOLE OF WHY THIS IS A STORE AND NOT A CONSTANT.
	// This object is a SINGLETON reused for every map — ClientWorldFactoryInit
	// makes one at DLL init and the factory hands the same one back on every
	// load — so an index alone would let the first map's world answer for the
	// second's. The engine forces a fresh serial with each create call and this
	// is where it lands.
	virtual void SetRefEHandle( const CBaseHandle &handle ) OVERRIDE { m_RefEHandle = handle; }
	virtual const CBaseHandle &GetRefEHandle() const OVERRIDE { return m_RefEHandle; }

	// WHETHER A LEVEL IS LOADED, asked of the world rather than of a global.
	// The OBJECT exists for the whole process, so its existence says nothing;
	// the HANDLE is bound by the level's create call and given back by
	// Release(). gametrace_client.cpp's SourceSharp_IsTheWorld reads this to
	// keep "you hit the world" and "there is no world yet" apart, which is the
	// distinction world_subject.h exists for.
	bool IsBoundToTheLevel() const { return m_RefEHandle.IsValid(); }

	//=========================================================================
	// IClientUnknown — the four faces, and the two NULLs that are the design
	//=========================================================================
	virtual IClientUnknown     *GetIClientUnknown() OVERRIDE     { return this; }
	virtual ICollideable       *GetCollideable() OVERRIDE        { return &m_Collideable; }
	virtual IClientNetworkable *GetClientNetworkable() OVERRIDE  { return this; }
	virtual IClientRenderable  *GetClientRenderable() OVERRIDE   { return this; }
	virtual IClientEntity      *GetIClientEntity() OVERRIDE      { return this; }
	virtual IClientThinkable   *GetClientThinkable() OVERRIDE    { return this; }

	// NOT ON THE SDK'S TREE. Everything that used to reach the world through
	// here asks world_subject.h's third arm instead.
	virtual IGameEntity *GetBaseEntity() OVERRIDE { return NULL; }

	// AND NOT ON THE PARALLEL HIERARCHY EITHER. IClientEntity's own default
	// answers NULL and it is inherited deliberately rather than restated: this
	// is the arm handle_cross.h's partition asks, and both arms NULL is what
	// makes this a third hierarchy rather than a second spelling of the first.

	//=========================================================================
	// IClientNetworkable
	//=========================================================================
	virtual ClientClass *GetClientClass() OVERRIDE;
	virtual void         Release() OVERRIDE;
	virtual void         NotifyShouldTransmit( ShouldTransmitState_t state ) OVERRIDE {}
	virtual void         OnPreDataChanged( DataUpdateType_t updateType ) OVERRIDE {}
	virtual void         OnDataChanged( DataUpdateType_t updateType ) OVERRIDE;

	// THE ENGINE DOES NOT CALL OnDataChanged. THE CLIENT DLL DOES, DEFERRED,
	// and this is what defers it.
	//
	// A networkable whose data changed during a net update does not have
	// OnDataChanged called there and then: it records that it is owed one, and
	// it is delivered once from OnRenderStart after the update is complete.
	// C_BaseEntity::PreDataUpdate did exactly this and the world inherited it;
	// taking the base away took the registration with it, and the SYMPTOM was
	// nothing at all -- no crash, no log, the world decoding correctly and
	// OnDataChanged simply never running, so the occlusion parameters, the
	// prop fade range, the view-mode reset and the startdark fade all silently
	// stopped happening. Found in console.log, by the client's own world report
	// line being absent, against a control run of the same map on entity-port
	// that printed 128 of them.
	//
	// IT WAS A QUEUE AND IT IS ONE MEMBER NOW. Phase 1c: the list stock kept at
	// file scope (g_DataChangedEvents, ported to CDataChangedEventQueue in
	// shell_clientdll_datachanged.cpp) had exactly ONE producer on the client
	// link line -- this class -- because nothing else in the build is an
	// IClientNetworkable that registers. A linked list with one possible
	// element is a member, so that is what it is; the DEFERRAL, which is the
	// only thing the queue existed for, is unchanged.
	virtual void         PreDataUpdate( DataUpdateType_t updateType ) OVERRIDE;

	// Deliver the deferred OnDataChanged, if one is owed, and forget it.
	// Called once a frame from OnRenderStart, after the net update. A no-op
	// on a frame in which no world data arrived, which is nearly all of them.
	void                 DeliverPendingDataChanged();

	// Nothing. C_BaseEntity's does interpolation, prediction and the shadow
	// refresh; the world interpolates nothing, predicts nothing and casts no
	// shadow.
	virtual void         PostDataUpdate( DataUpdateType_t updateType ) OVERRIDE {}
	virtual bool         IsDormant( void ) OVERRIDE { return false; }
	// THE INDEX, OR -1 WHEN THERE IS NO LEVEL — and the -1 is the point.
	//
	// CBaseHandle::GetEntryIndex() on an INVALID handle answers
	// NUM_ENT_ENTRIES-1 (basehandle.h:150, and its comment calls it a
	// bug-submarining hack), so reading it unguarded would have this object say
	// it is entity 2047 between maps. -1 is the client's own spelling of "not
	// in the list" — C_BaseEntity carries it in `index` for a client-only
	// entity, and CGameTrace::GetEntityIndex answers it for a trace that hit
	// nothing.
	virtual int          entindex( void ) const OVERRIDE
	{
		return m_RefEHandle.IsValid() ? m_RefEHandle.GetEntryIndex() : -1;
	}
	virtual void         ReceiveMessage( int classID, bf_read &msg ) OVERRIDE {}
	virtual void         SetDestroyedOnRecreateEntities( void ) OVERRIDE {}
	virtual void         OnDataUnchangedInPVS() OVERRIDE {}

	// WHERE THE ENGINE'S DECODER WRITES. The props were resolved against
	// SourceSharp_WorldBlobOffset() past this pointer, so this pointer is what
	// they were resolved against — the same arrangement the server's blob has,
	// and the reason SourceSharp_WorldBlobOffset is computed from sizeof rather
	// than written down.
	virtual void *GetDataTableBasePtr() OVERRIDE { return this; }

	//=========================================================================
	// IClientThinkable — the world does not think, and never was in the list
	//=========================================================================
	virtual void                ClientThink() OVERRIDE {}
	virtual ClientThinkHandle_t GetThinkHandle() OVERRIDE { return m_hThink; }
	virtual void                SetThinkHandle( ClientThinkHandle_t hThink ) OVERRIDE { m_hThink = hThink; }

	//=========================================================================
	// IClientEntity — Release is shared with IClientNetworkable's above
	//=========================================================================
	virtual const Vector &GetAbsOrigin( void ) const OVERRIDE;
	virtual const QAngle &GetAbsAngles( void ) const OVERRIDE;
	virtual CMouthInfo   *GetMouth( void ) OVERRIDE { return NULL; }
	virtual bool          GetSoundSpatialization( SpatializationInfo_t &info ) OVERRIDE { return false; }

	//=========================================================================
	// IClientRenderable — CDefaultClientRenderable's answers, verbatim
	//
	// The world draws nothing through this face. Its pixels are the BSP's and
	// the engine draws them; this object is never handed to
	// ClientLeafSystem()->AddRenderable, so ShouldDraw is never asked and
	// DrawModel is never called. These are here because the interface is on the
	// chain the engine reaches entity 0 through, not because anything reads
	// them.
	//=========================================================================
	virtual const Vector     &GetRenderOrigin( void ) OVERRIDE;
	virtual const QAngle     &GetRenderAngles( void ) OVERRIDE;
	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE;
	virtual bool              ShouldDraw( void ) OVERRIDE { return false; }
	virtual bool              IsTransparent( void ) OVERRIDE { return false; }
	virtual bool              IsTwoPass( void ) OVERRIDE { return false; }
	virtual void              OnThreadedDrawSetup() OVERRIDE {}
	virtual bool              UsesPowerOfTwoFrameBufferTexture() OVERRIDE { return false; }
	virtual bool              UsesFullFrameBufferTexture() OVERRIDE { return false; }
	virtual ClientShadowHandle_t GetShadowHandle() const OVERRIDE { return CLIENTSHADOW_INVALID_HANDLE; }
	virtual ClientRenderHandle_t &RenderHandle() OVERRIDE { return m_hRender; }
	virtual int               GetBody() OVERRIDE { return 0; }
	virtual int               GetSkin() OVERRIDE { return 0; }
	virtual bool              UsesFlexDelayedWeights() OVERRIDE { return false; }

	// THE ONE RENDERABLE ANSWER WITH A LIVE READER, and it is a console
	// command's: `envmap` and cl_takesnapshot both name the screenshot's file
	// after `entitylist->GetClientEntity( 0 )->GetModel()`
	// (engine/buildcubemaps.cpp:368, engine/cl_main.cpp:1405) and both test it
	// for NULL first. NULL is also what stock's C_World answered — nothing ever
	// set a model index on the client's world entity — so the fallback name is
	// the one this build has always used.
	virtual const model_t    *GetModel() const OVERRIDE { return NULL; }
	virtual int               DrawModel( int flags ) OVERRIDE { return 0; }
	virtual void              ComputeFxBlend() OVERRIDE {}
	virtual int               GetFxBlend() OVERRIDE { return 255; }
	virtual bool              LODTest() OVERRIDE { return true; }
	virtual bool              SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones,
	                                      int boneMask, float currentTime ) OVERRIDE { return true; }
	virtual void              SetupWeights( const matrix3x4_t *pBoneToWorld, int nFlexWeightCount,
	                                        float *pFlexWeights, float *pFlexDelayedWeights ) OVERRIDE {}
	virtual void              DoAnimationEvents( void ) OVERRIDE {}
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
	virtual float            *GetRenderClipPlane( void ) OVERRIDE { return NULL; }
	virtual void              RecordToolMessage() OVERRIDE {}
	virtual bool              IgnoresZBuffer( void ) const OVERRIDE { return false; }

private:
	void RegisterSharedActivities( void );

	// Asks C# where its ten fields are, once, and again after a managed reload
	// has moved them.
	bool ResolveFields();

	// Prints the ten when they are not what was printed last. See the body.
	void ReportIfChanged();

	SourceSharpWorldFields_t m_Fields;

	// THE BYTES LAST PRINTED, so the line above cannot repeat itself.
	enum { REPORT_BUFFER = 512 };
	unsigned char m_LastReported[ REPORT_BUFFER ];
	bool          m_bReportedOnce;

	CBaseHandle             m_RefEHandle;

	// THE DEFERRED OnDataChanged. -1 is "none owed"; otherwise it holds the
	// DataUpdateType_t the delivery is owed with, and DATA_UPDATE_CREATED
	// overrides a DATA_UPDATE_DATATABLE_CHANGED already recorded -- which is
	// the one rule the queue's Add() had beyond storing the entry.
	//
	// It was a queue INDEX before Phase 1c and the name is kept so the member
	// that C_BaseEntity carries under the same name stays recognisable. The
	// values it holds are not interchangeable with that one's and nothing
	// outside this class reads it.
	int                     m_DataChangeEventRef;

	ClientThinkHandle_t     m_hThink;
	ClientRenderHandle_t    m_hRender;

	CWorldClientCollideable m_Collideable;
};

void ClientWorldFactoryInit();
void ClientWorldFactoryShutdown();
C_World* GetClientWorldEntity();

// The world as a material-proxy binding. Its only caller is viewpostprocess.cpp
// and its parameter type is `void *` everywhere it lands
// (imaterialsystem.h:1445), which is why this is not typed.
void *GetClientWorldRenderable();
