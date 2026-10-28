//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client's entity shell; the layering lives in managed code.
#pragma once

#include "iclientunknown.h"
#include "iclientnetworkable.h"
#include "iclientrenderable.h"
#include "icliententity.h"
#include "client_class.h"
#include "engine/ICollideable.h"
#include "engine/IEngineSound.h"
#include "engine/ishadowmgr.h"
#include "engine/ivdebugoverlay.h"
#include "mathlib/vector.h"
#include "tier0/platform.h"
#include "tier1/bitbuf.h"
#include "basehandle.h"
#include "bsptreedata.h"
#include "client_render_handle.h"
#include "client_world.h"
#include "cmodel.h"
#include "collisionproperty.h"
#include "const.h"
#include "detailobjectsystem.h"
#include "eiface.h"
#include "fx.h"
#include "gametrace.h"
#include "icliententitylist.h"
#include "iclientthinkable.h"
#include "ihandleentity.h"
#include "iinput.h"
#include "ivieweffects.h"
#include "mouthinfo.h"
#include "ragdoll_shared.h"
#include "usercmd.h"
#include "view.h"
#include "vphysics_interface.h"
#include "igameentity.h"
#include "engine_block.h"
#include "shell_leaf_link.h"

// Must match managed ShadowCastChoice.
enum StandaloneShadowChoice_t
{
	STANDALONE_SHADOW_CHOICE_AUTO   = 0,

	// EF_NOSHADOW's meaning.
	STANDALONE_SHADOW_CHOICE_NEVER  = 1,

	// For self-drawing entities, which have no studio header for the auto rule.
	STANDALONE_SHADOW_CHOICE_SIMPLE = 2,
};

// How seeing the owner gates ShouldDraw. Must match managed ClientOwnerSeenRule.
enum StandaloneOwnerSeenRule_t
{
	STANDALONE_OWNER_SEEN_ALWAYS         = 0,  // not at all
	STANDALONE_OWNER_SEEN_HIDDEN_WHILE   = 1,  // hidden while seen: the viewmodel
	STANDALONE_OWNER_SEEN_DRAWN_ONLY_WHILE = 2,  // drawn only while seen: the viewer's own body
};

class CStudioHdr;

class CJiggleBones;

class CIKContext;
class CBoneBitList;

void SourceSharp_KillDeadBeams( const CBaseHandle &hDeadEntity );

class C_StandaloneEntity : public IClientEntity, public ICollideable
{
	// Declared first: the interpolated vars are set up over block fields, and
	// members initialise in declaration order.
	EngineBlock_t m_EngineBlock;

public:
	C_StandaloneEntity( int nEntNum, int nSerialNum, ClientClass *pClass, int iTier );
	virtual ~C_StandaloneEntity();

	// Allocated longer than the object, by the blob size. There is no plain operator
	// new: an unsized allocation would let the engine decode past its end.
	void *operator new( size_t nBytes, int nBlobBytes );
	void  operator delete( void *pMem );
	void  operator delete( void *pMem, int nBlobBytes );

	unsigned char *DataBlob();

	int BlobBytes() const;

	int Tier() const { return m_iTier; }

	EngineBlock_t       &Block()       { return m_EngineBlock; }
	const EngineBlock_t &Block() const { return m_EngineBlock; }

	// Recv props for block fields are offset by this. The block is outside the blob:
	// the payload words cover the blob, so the server's copy would overwrite it,
	// including Head.Handle, whose serial differs per side.
	static int BlockBase();

	CBaseHandle &BlockHandle()
	{ return *reinterpret_cast< CBaseHandle * >( &m_EngineBlock.Head.Handle ); }
	const CBaseHandle &BlockHandle() const
	{ return *reinterpret_cast< const CBaseHandle * >( &m_EngineBlock.Head.Handle ); }

	#define SS_CLIENT_BLOCK_HEAD_OFFSET( field ) \
		( (int)( offsetof( EngineBlock_t, Head ) + offsetof( EngineBlockHead_t, field ) ) )

	class CSourceSharpSurfaceRenderer *SurfaceRenderer( bool bCreate );

	virtual void SetRefEHandle( const CBaseHandle &h ) OVERRIDE { BlockHandle() = h; }
	virtual const CBaseHandle &GetRefEHandle() const OVERRIDE { return BlockHandle(); }

	virtual ICollideable *GetCollideable() OVERRIDE { return this; }
	virtual IClientNetworkable *GetClientNetworkable() OVERRIDE { return this; }
	virtual IClientRenderable *GetClientRenderable() OVERRIDE { return this; }

	// DrawBrushModelEx requires an IClientEntity.
	virtual IClientEntity *GetIClientEntity() OVERRIDE { return this; }

	virtual IClientThinkable *GetClientThinkable() OVERRIDE { return NULL; }

	virtual void ClientThink() OVERRIDE {}
	virtual ClientThinkHandle_t GetThinkHandle() OVERRIDE { return m_hThink; }
	virtual void SetThinkHandle( ClientThinkHandle_t hThink ) OVERRIDE { m_hThink = hThink; }

	virtual const Vector &GetAbsOrigin() const OVERRIDE { return Block().Head.Origin; }
	virtual const QAngle &GetAbsAngles() const OVERRIDE { return Block().Head.Angles; }

	virtual CMouthInfo *GetMouth() OVERRIDE { return NULL; }
	virtual bool GetSoundSpatialization( SpatializationInfo_t & ) OVERRIDE { return false; }

	virtual IGameEntity *GetBaseEntity() OVERRIDE { return NULL; }

	virtual IClientUnknown *GetIClientUnknown() OVERRIDE { return this; }
	virtual void Release() OVERRIDE { delete this; }
	virtual ClientClass *GetClientClass() OVERRIDE;
	virtual void NotifyShouldTransmit( ShouldTransmitState_t state ) OVERRIDE;
	virtual void OnPreDataChanged( DataUpdateType_t ) OVERRIDE {}
	virtual void OnDataChanged( DataUpdateType_t ) OVERRIDE {}
	virtual void PreDataUpdate( DataUpdateType_t ) OVERRIDE {}
	virtual void PostDataUpdate( DataUpdateType_t updateType ) OVERRIDE;

	bool EnsureManagedHalf();
	virtual bool IsDormant() OVERRIDE { return Block().Client.Dormant != 0; }

	void PushDormancy();

	bool IsPlayerShell() const { return m_bPlayerShell; }

	void SetPlayerShell( bool bIsPlayer ) { m_bPlayerShell = bIsPlayer; }

	bool IsLocalPlayerShell() const { return m_bLocalPlayer; }

	void PredictionDrive( const struct SourceSharpClientPlayerView &view );

	Vector EyePosition() const { return GetAbsOrigin() + m_vecViewOffset; }

	const Vector &PredictedViewOffset() const { return m_vecViewOffset; }

	// -1 before the first prediction frame.
	int PredictedHealth() const { return m_nPredictedHealth; }
	const QAngle &PredictedPunchAngle() const { return m_angPunch; }
	const QAngle &PredictedPunchAngleVel() const { return m_angPunchVel; }
	virtual int  entindex() const OVERRIDE { return m_nEntIndex; }
	virtual void ReceiveMessage( int, bf_read & ) OVERRIDE {}
	virtual void SetDestroyedOnRecreateEntities() OVERRIDE {}
	virtual void OnDataUnchangedInPVS() OVERRIDE {}

	virtual void *GetDataTableBasePtr() OVERRIDE { return this; }

	// The physics step writes a ragdoll's pose into RenderOrigin, so it is drawn and
	// culled where the solver has it, not where it was thrown from.
	virtual const Vector &GetRenderOrigin() OVERRIDE
	{
		return Block().Client.RenderOrigin;
	}
	virtual const QAngle &GetRenderAngles() OVERRIDE { return Block().Client.RenderAngles; }
	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE { return Block().Client.RenderTransform; }
	virtual bool ShouldDraw() OVERRIDE;

	// The leaf system never re-asks ShouldDraw (only the viewmodel list does), so
	// without this an entity that stops drawing keeps drawing.
	void UpdateVisibility();
	// A brush drawn in the opaque pass below full alpha writes depth for pixels it
	// did not paint.
	virtual bool IsTransparent() OVERRIDE { return Block().Client.Transparent != 0; }
	virtual const model_t *GetModel() const OVERRIDE;
	virtual int  DrawModel( int flags ) OVERRIDE;
	int DrawExtras( int flags, bool bAfterModel );
	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE;

	virtual void CreateModelInstance() OVERRIDE;
	virtual ModelInstanceHandle_t GetModelInstance() OVERRIDE { return m_ModelInstance; }

	virtual ShadowType_t ShadowCastType() OVERRIDE;

	virtual bool IsTwoPass() OVERRIDE { return false; }
	virtual void OnThreadedDrawSetup() OVERRIDE {}
	virtual bool UsesPowerOfTwoFrameBufferTexture() OVERRIDE { return false; }
	virtual bool UsesFullFrameBufferTexture() OVERRIDE { return false; }
	virtual ClientShadowHandle_t GetShadowHandle() const OVERRIDE { return CLIENTSHADOW_INVALID_HANDLE; }
	virtual ClientRenderHandle_t &RenderHandle() OVERRIDE { return m_hRenderHandle; }
	virtual int  GetBody() OVERRIDE { return Block().Client.Body; }
	virtual int  GetSkin() OVERRIDE { return Block().Client.Skin; }
	virtual bool UsesFlexDelayedWeights() OVERRIDE { return false; }
	// Managed code writes FxBlend before the leaf system's blend batch.
	virtual void ComputeFxBlend() OVERRIDE {}
	virtual int  GetFxBlend() OVERRIDE { return Block().Client.FxBlend; }
	virtual bool LODTest() OVERRIDE { return true; }
	virtual void SetupWeights( const matrix3x4_t *, int, float *, float * ) OVERRIDE {}
	virtual void DoAnimationEvents() OVERRIDE {}
	virtual IPVSNotify *GetPVSNotifyInterface() OVERRIDE { return NULL; }
	virtual void GetRenderBoundsWorldspace( Vector &absMins, Vector &absMaxs ) OVERRIDE
	{
		DefaultRenderBoundsWorldspace( this, absMins, absMaxs );
	}
	virtual void GetColorModulation( float *color ) OVERRIDE
	{
		const EngineBlockClientTail_t &tail = Block().Client;

		color[0] = tail.RenderColorR * ( 1.0f / 255.0f );
		color[1] = tail.RenderColorG * ( 1.0f / 255.0f );
		color[2] = tail.RenderColorB * ( 1.0f / 255.0f );
	}
	virtual bool ShouldReceiveProjectedTextures( int flags ) OVERRIDE;
	virtual bool GetShadowCastDistance( float *, ShadowType_t ) const OVERRIDE { return false; }
	virtual bool GetShadowCastDirection( Vector *, ShadowType_t ) const OVERRIDE { return false; }
	virtual void GetShadowRenderBounds( Vector &mins, Vector &maxs, ShadowType_t ) OVERRIDE
	{
		GetRenderBounds( mins, maxs );
	}
	virtual bool IsShadowDirty() OVERRIDE { return false; }
	virtual void MarkShadowDirty( bool ) OVERRIDE {}
	virtual IClientRenderable *GetShadowParent() OVERRIDE { return NULL; }
	virtual IClientRenderable *FirstShadowChild() OVERRIDE { return NULL; }
	virtual IClientRenderable *NextShadowPeer() OVERRIDE { return NULL; }
	// One-based: attachment 0 means none.
	virtual int  LookupAttachment( const char *pAttachmentName ) OVERRIDE;
	virtual bool GetAttachment( int number, Vector &origin, QAngle &angles ) OVERRIDE;
	virtual bool GetAttachment( int number, matrix3x4_t &matrix ) OVERRIDE;
	virtual float *GetRenderClipPlane() OVERRIDE { return NULL; }
	virtual void RecordToolMessage() OVERRIDE {}
	virtual bool IgnoresZBuffer() const OVERRIDE { return m_bIgnoreZ; }

	// A NULL pBoneToWorldOut asks only whether there are bones.
	virtual bool SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones, int boneMask,
	                         float currentTime ) OVERRIDE;

public:

	virtual IHandleEntity *GetEntityHandle() OVERRIDE { return this; }
	virtual const Vector &OBBMinsPreScaled() const OVERRIDE { return Block().Head.Mins; }
	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE { return Block().Head.Maxs; }
	virtual const Vector &OBBMins() const OVERRIDE { return Block().Head.Mins; }
	virtual const Vector &OBBMaxs() const OVERRIDE { return Block().Head.Maxs; }
	virtual void WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const OVERRIDE;
	// Stock's C_BaseAnimating::TestCollision without its terminal Assert(0): a brush
	// prop with neither custom flag reaches here on a box sweep.
	virtual bool TestCollision( const Ray_t &ray, unsigned int fContentsMask, trace_t &tr ) OVERRIDE
	{
		if ( ray.m_IsRay && IsSolidFlagSet( FSOLID_CUSTOMRAYTEST ) )
		{
			if ( !TestHitboxes( ray, fContentsMask, tr ) )
				return true;

			return tr.DidHit();
		}

		if ( !ray.m_IsRay && IsSolidFlagSet( FSOLID_CUSTOMBOXTEST ) )
		{
			if ( !TestHitboxes( ray, fContentsMask, tr ) )
				return true;

			return true;
		}

		return false;
	}
	// True means "I have hitboxes and tested them", not "the ray hit"; callers read
	// tr.DidHit().
	virtual bool TestHitboxes( const Ray_t &ray, unsigned int fContentsMask, trace_t &tr ) OVERRIDE;
	virtual int GetCollisionModelIndex() OVERRIDE { return Block().Head.ModelIndex; }
	virtual const model_t *GetCollisionModel() OVERRIDE { return (model_t *)GetModel(); }
	virtual const Vector &GetCollisionOrigin() const OVERRIDE { return Block().Head.Origin; }
	virtual const QAngle &GetCollisionAngles() const OVERRIDE { return Block().Head.Angles; }
	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE { return Block().Head.WorldTransform; }
	virtual SolidType_t GetSolid() const OVERRIDE { return (SolidType_t)Block().Head.SolidType; }
	virtual int GetSolidFlags() const OVERRIDE { return Block().Head.SolidFlags; }
	bool IsSolidFlagSet( int nFlagMask ) const { return ( GetSolidFlags() & nFlagMask ) != 0; }
	virtual IClientUnknown *GetIClientUnknown() const { return NULL; }
	virtual int GetCollisionGroup() const OVERRIDE { return COLLISION_GROUP_NONE; }
	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE;
	virtual bool ShouldTouchTrigger( int ) const OVERRIDE { return false; }
	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

	// Networked from the server. Public: the RecvProp offsets are taken here and
	// must match the server's SendProp offsets.

	char   m_szClassname[ 64 ];

	uint32 m_nTypeId;
	uint32 m_nSchemaHash;
	uint32 m_nPayloadWords;

	uint32 m_nResolvedTypeId;

	// Most entities have no managed class, so a refusal is remembered per type id
	// rather than asked again every update.
	uint32 m_nManagedTriedTypeId;

	void PostInterpolate( float flCurrentTime );

	void SetInterpAdmitted( bool bAdmitted ) { m_bInterpAdmitted = bAdmitted; }
	bool IsInterpAdmitted() const { return m_bInterpAdmitted; }

	void NoteNetworkedPosition();

protected:
	void OnPositionChanged();

	void PublishRenderPose();

	void AddToLeafSystem();

public:
	void SetCustomDraw( const Vector &mins, const Vector &maxs );

	// Style 0 never fades.
	void SetFade( int nStyle, float flStartDist, float flEndDist, float flMinAlpha );

	// Negative clears it.
	void SetOverlayModel( int nModelIndex ) { m_nOverlayModelIndex = nModelIndex; }

	bool IsRagdoll() const;

	void SetRagdollPose( bool bRagdoll );

	// Not range-checked: modelrender treats an out-of-range skin as skin 0.
	void SetSkin( int nSkin ) { Block().Client.Skin = nSkin; }

	// Refused at zero and below: it scales the bones' 3x3 block.
	void SetModelScale( float flScale );
	float GetModelScale() const { return m_flModelScale; }

	void SetBlending( bool bTranslucent, bool bIgnoreZ, int nAlpha );

	int  ShadowCastChoice() const { return Block().Client.ShadowChoice; }

	void SetViewModelPass( bool bEnable );

	void StoreSequence( int nSequence, float flPlaybackRate, float flAnimTime,
	                    float flDuration, bool bLooping );

	float SequenceCycle( float flNow ) const;

	int NetworkedModelIndex() const { return Block().Head.ModelIndex; }

	// No leaf-system notification: a viewmodel-pass renderable is collated from its
	// own list.
	void MoveToEye( const Vector &vecEye, const QAngle &angEye );

private:
	void RemoveFromLeafSystem();

	void RefreshShadow();
	void DestroyShadow();

	void PublishModelFacts();

public:
	void ApplyRenderPolicy();

	// Managed code re-decides an entity only when this changes; wrapping is fine.
	void BumpPolicyInputs() { Block().Client.PolicyInputs++; }

	bool IsInLeafSystem() const { return m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE; }

private:
	void SetDormant( bool bDormant );

	int         m_nEntIndex;

	bool        m_bHasManaged;

	bool        m_bDrawReported;

	ModelInstanceHandle_t m_ModelInstance;

	ClientRenderHandle_t  m_hRenderHandle;

	ClientThinkHandle_t   m_hThink;

	ClientClass *m_pClientClass;

	int          m_iTier;

	class CSourceSharpSurfaceRenderer *m_pSurface;

	float  m_flModelScale;

	bool       m_bRagdollPose = false;

	int    m_nOverlayModelIndex;

	bool   m_bIgnoreZ;

	// -1 before any; the decoder writes the index without this side running.
	int    m_nPublishedModelIndex;

	// The EF_* bits the policy last saw, -1 before any, for the same reason.
	int    m_nPublishedEffects;

	uint32_t m_nAppliedPolicy;

	bool   m_bPlayerShell;

	bool   m_bLocalPlayer;

	bool   m_bInterpAdmitted = false;

	Vector m_vecViewOffset;
	QAngle m_angPunch;
	QAngle m_angPunchVel;
	int    m_nPredictedHealth;

	float  m_flSeqDuration;
	bool   m_bSeqLooping;

};

// The client-side shell for a managed entity handle, or NULL.
class C_StandaloneEntity *SourceSharp_StandaloneFromHandle( uint32_t hEntity );

// Capture takes each view's renderables list as the leaf system builds it. The
// blend answers GatherSprite while managed code prepares the batch.
class CClientRenderablesList;
void SourceSharp_StandaloneDrawBatchCapture( const CClientRenderablesList &list );
float SourceSharp_StandaloneDrawBatchBlend( IClientEntity *pEntity, float flFallback );

// NULL between maps, before the player arrives, and with no player.
class C_StandaloneEntity *SourceSharp_LocalPlayerShell();

int SourceSharp_StandaloneClientCount();
class C_StandaloneEntity *SourceSharp_StandaloneClientAt( int nIndex );

// Called after the temp entities and before the post-entity system pass, which
// resets subsystems a surviving shell would hold handles into.
int SourceSharp_StandaloneClientsLevelShutdown();

void SourceSharp_StandaloneViewModelToEye( const Vector &vecEye, const QAngle &angEye );

// Not CAM_IsThirdPerson(): a third-person camera pulled onto the eye by a wall
// is first-person.
bool SourceSharp_ThirdPersonSeesOwner();

int  SourceSharp_StandaloneBlobOffset();

bool SourceSharp_StandaloneTierIsInUse( int iTier );

inline C_StandaloneEntity *SourceSharp_ClientShellFromRecvStruct( void *pStruct )
{
	return static_cast< C_StandaloneEntity * >( pStruct );
}

// Last, so any TU that can name C_StandaloneEntity has read the CHandle ban.
#include "standalone_handle_ban_client.h"
