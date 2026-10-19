//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's half of the parallel hierarchy ========//
//
// ONE class, matching the server's, and for the same reason: without
// C_BaseEntity there is nothing to make a native tree OUT of. What an entity
// can do is its data, not its C++ type.
//
// The layering lives in C#, on both sides, and it has to be the same tree —
// an entity is a PAIR of objects, and the layer that networks a model on one
// side is the layer that draws it on the other.
//
// The client asks less than the server: no edict to own, no spatial partition
// to join, no keyvalues, no I/O. What it asks for instead is RENDERING, and
// that is what this shell mostly is.
//
// It implements IClientEntity, which is what C_BaseEntity implements and is
// smaller than its name suggests: Release, GetAbsOrigin, GetAbsAngles, a mouth
// and a sound spatialisation, on top of the four interfaces it inherits. It
// began as a CDefaultClientRenderable — the SDK's own base for "something that
// draws but is not an entity" — and outgrew it for exactly one reason:
// render->DrawBrushModelEx takes an IClientEntity *, so a brush model cannot be
// drawn by anything less. The renderable defaults CDefaultClientRenderable
// supplied are carried below, since there is no longer a base to inherit them
// from.
//
// The obvious reading of C_BaseEntity::DrawModel is that studio models are
// impossible outside C_BaseAnimating:
//
//     Warning( "ERROR: Can't draw studio model %s because %s is not derived
//               from C_BaseAnimating\n" );
//
// That is not the engine refusing. It is C_BaseEntity declining to do work it
// has no bones for, in its own DrawModel, and nothing below it agrees. The
// engine draws through IClientRenderable: modelrender->DrawModelEx takes a
// ModelRenderInfo_t whose only entity-ish field is an IClientRenderable *, and
// the client leaf system holds the same. Neither has ever named C_BaseEntity.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_STANDALONE_CLIENT_H
#define SOURCESHARP_SHELL_STANDALONE_CLIENT_H
#ifdef _WIN32
#pragma once
#endif

#include "convar.h"
#include "iclientunknown.h"
#include "iclientnetworkable.h"
#include "iclientrenderable.h"
#include "icliententity.h"
#include "igameentity.h"
#include "client_class.h"
#include "engine/ICollideable.h"
#include "engine/ivmodelrender.h"
#include "model_types.h"
// MAXSTUDIOPOSEPARAM and MDLHandle_t, for the pose-parameter storage below.
#include "studio.h"
#include "datacache/imdlcache.h"
// THE ENGINE BLOCK (plan_entity.md §3.2). The client's half of step 1a's
// declaration: the same struct at the same offset, read by the same getters,
// with the CLIENT tail live rather than the server one.
#include "engine_block.h"
// The three guards a renderable owes the client leaf system. See the header:
// add twice, remove unadded and move without telling all fail silently and each
// in a different way, so all three live in one place with a test that drives
// them.
#include "shell_leaf_link.h"

//-----------------------------------------------------------------------------
// WHAT A MANAGED CLASS ASKED FOR ABOUT ITS SHADOW.
//
// The ABI contract of StandaloneSetShadowCast, and the mirror of
// SourceSharp.Abi's ShadowCastChoice. The three values cross the boundary, so
// they are fixed.
//
// THIS IS ALL THAT IS LEFT OF shadow_cast_policy.h, which held these AND the
// rule that read them. The rule was policy -- "does this entity cast" is what
// stock spells EF_NOSHADOW and a `disableshadows` keyvalue -- so step 6b moved
// it to ClientRenderPolicy.Shadow in C#, along with its sixteen tests, and
// deleted the header. What could not move was reading a studio header out of
// mdlcache, and that is PublishModelFacts below, which writes the counts the
// rule is decided from into the block.
//-----------------------------------------------------------------------------
enum StandaloneShadowChoice_t
{
	// The stock rule, from the model. Nothing is overridden.
	STANDALONE_SHADOW_CHOICE_AUTO   = 0,

	// Casts nothing, whatever it is wearing. EF_NOSHADOW's meaning, and what
	// prop_dynamic's `disableshadows` keyvalue asks for.
	STANDALONE_SHADOW_CHOICE_NEVER  = 1,

	// The cheap blob, for something the auto rule would refuse: an entity that
	// draws ITSELF has no studio header to be judged by and would otherwise
	// never cast at all.
	STANDALONE_SHADOW_CHOICE_SIMPLE = 2,
};

class CStudioHdr;

// The springs behind a jiggle bone. Forward-declared rather than included,
// because most of this hierarchy will never own one.
class CJiggleBones;

// The IK solver's per-entity state. Same argument: an oil drum has no chains.
class CIKContext;
class CBoneBitList;

// THE JOINTED BODY IS NO LONGER A TYPE THIS HEADER KNOWS. A body is managed's
// (SourceSharp.Client's ClientRagdolls, plan_physics_bones_native_zero Phase
// 3); the shell keeps one flag, that the solver owns its pose.

//-----------------------------------------------------------------------------
// The C_BaseEntity layer.
//
// Everything an entity is on the client before it has a shape: a class, a
// handle, a place, the networked state that arrives from the server, and a way
// to be drawn.
//-----------------------------------------------------------------------------
// NOT AN IGameEntity (2026-10-18). The client's native code reaches its
// entities through the slot table (slot_table.h's shell resolvers), so the
// game-entity face and its six inert slots went; GetGameEntity() answers
// IClientEntity's NULL. shell_standalone_client.cpp's static_assert keeps it so.
class C_StandaloneEntity : public IClientEntity, public ICollideable
{
	// THE ENGINE BLOCK, AND IT IS A MEMBER HERE WHERE THE SERVER'S IS THE
	// BLOB'S PREFIX. That is the one deliberate divergence between the two
	// shells, and it is forced rather than chosen — see BlockBase() below for
	// the two bugs the server's placement would be on this side.
	//
	// DECLARED FIRST for the reason CStandaloneEntity declares its pointer
	// first: member initialisers run in DECLARATION order, and the
	// interpolated vars below are Setup() OVER block fields. Declared late,
	// they would be set up over storage that had not been initialised yet.
	EngineBlock_t m_EngineBlock;

public:
	// The class is a constructor argument rather than a setter call afterwards,
	// because the constructor puts the entity into the client's entity list —
	// and from that moment the engine may ask it anything, GetClientClass
	// included.
	// THE TIER TRAVELS WITH THE CLASS (Part 18), because on this side they are
	// the same fact: there is one create function per tier, so the trampoline
	// that made this entity is the only thing that knows its tier — and it
	// knows it before a single prop has been decoded. Nothing about the tier is
	// on the wire and nothing has to be looked up.
	C_StandaloneEntity( int nEntNum, int nSerialNum, ClientClass *pClass, int iTier );
	virtual ~C_StandaloneEntity();

	// Allocated longer than the object, by what THIS class declared (Part 18).
	// The plain form is deliberately not declared, exactly as in the server's
	// twin: an un-sized `new` would be a blob shorter than the table addressing
	// it, and here that is the engine decoding into memory past the allocation.
	void *operator new( size_t nBytes, int nBlobBytes );
	void  operator delete( void *pMem );
	void  operator delete( void *pMem, int nBlobBytes );

	// The class's own networked state, past the declared members.
	unsigned char *DataBlob();

	// How long that block is, for THIS entity. Per instance since Part 18 — see
	// CStandaloneEntity::BlobBytes in the server's twin.
	int BlobBytes() const;

	int Tier() const { return m_iTier; }

	//-------------------------------------------------------------------------
	// THE ENGINE BLOCK (plan_entity.md §3.2), reached exactly as the server's
	// shell reaches it — same name, same const pair, same cached pointer.
	//
	// THE TWO SIDES ARE IMPLEMENTED THE SAME WAY, so this is a copy of
	// CStandaloneEntity::Block() and not a client-flavoured variant of it —
	// same name, same const pair, same SS_..._BLOCK_HEAD_OFFSET arithmetic
	// behind the props. What differs is which tail is WRITTEN: the server
	// writes the server tail and the engine reads it back through
	// GetPlayerState; here the engine's DECODER writes the networked part of
	// the head (the recv props address it — see BuildStandaloneRecvProps) and
	// this side writes the client tail.
	//
	// A CACHED POINTER RATHER THAN ARITHMETIC PER GETTER, for the reason the
	// server's twin gives: SourceSharp_StandaloneBlobOffset() is one non-inline
	// function, so `(char *)this + offset` cannot be folded and every getter
	// would carry a call. The renderer asks GetRenderOrigin, IsTransparent and
	// GetFxBlend per draw; §3.2 says those must be one load.
	//-------------------------------------------------------------------------
	EngineBlock_t       &Block()       { return m_EngineBlock; }
	const EngineBlock_t &Block() const { return m_EngineBlock; }

	//-------------------------------------------------------------------------
	// Where the block starts, measured from the object base. A RecvProp
	// addressing a block field is offset by this, and the server's SendProp for
	// the same field is offset by ITS side's BlockBase(): the two numbers are
	// not equal and do not have to be, because a prop's offset is only ever
	// used against the side that declared it.
	//
	// AND HERE IT IS A MEMBER OFFSET, NOT THE BLOB BASE. The server's block is
	// the blob's prefix; this one is a member of the shell, outside the blob
	// entirely. THE DIVERGENCE IS FORCED, by two things that are only true on
	// this side:
	//
	//   * THE PAYLOAD WORDS COVER THE WHOLE BLOB. Every standalone tier's table
	//     is the six shared props followed by one int32 prop per four bytes of
	//     blob FROM THE BLOB BASE (SourceSharpPayload_BuildRecvProps). The
	//     server's block is inside that window, so the server's 352 block bytes
	//     are on the wire as 88 payload words — step 1b recorded exactly that.
	//     A client block at the blob base would therefore be OVERWRITTEN by the
	//     server's copy of it on every full update.
	//   * AND THE FIRST FOUR OF THOSE BYTES ARE THE HANDLE. Head.Handle is the
	//     entity's OWN CBaseHandle, and the two sides' serials are not the same
	//     number: this side's comes from the create function's serialNum. A
	//     block in the payload window would have the server's serial decoded
	//     over this client's, and every handle the engine holds for the entity
	//     would stop resolving — entities that exist and cannot be found.
	//     Head.Handle is read by GetRefEHandle, which the engine calls on a
	//     trace path that does not null-test the entity it resolves.
	//
	// The client tail is the same argument once more: it is state this side
	// COMPUTES (the interpolated pose, the fx blend, the render group), and the
	// server's copy of those fields is zero. In the payload window they would
	// be zeroed from the wire on every full update.
	//
	// Taken with a member, not offsetof(): this class has three bases and is
	// not standard-layout, so offsetof is conditionally supported here. The
	// null-pointer form below is the same one BuildStandaloneRecvProps has
	// always used for the other five props' offsets.
	//-------------------------------------------------------------------------
	static int BlockBase();

	// The handle as a CBaseHandle reference over the block's uint32 — the
	// server's BlockHandle(), spelled the same. CBaseHandle is one uint32 and
	// nothing else, which shell_standalone_client.cpp static_asserts rather
	// than assumes.
	CBaseHandle &BlockHandle()
	{ return *reinterpret_cast< CBaseHandle * >( &m_EngineBlock.Head.Handle ); }
	const CBaseHandle &BlockHandle() const
	{ return *reinterpret_cast< const CBaseHandle * >( &m_EngineBlock.Head.Handle ); }

	// The offset of one EngineBlockHead_t field from the block's own base. The
	// server's macro, spelled identically, because the recv props and the send
	// props are built from the same arithmetic on their own sides.
	#define SS_CLIENT_BLOCK_HEAD_OFFSET( field ) \
		( (int)( offsetof( EngineBlock_t, Head ) + offsetof( EngineBlockHead_t, field ) ) )


	//-------------------------------------------------------------------------
	// THE PAYLOAD SHADOW IS GONE (plan_nettable_generic.md Part 14 Step 6).
	//
	// It was a second copy of the blob, written by the payload's RecvProps
	// while the typed per-class props wrote the blob itself, so the two could be
	// compared. There are no typed props now — the payload IS the state — so
	// there is nothing left to compare against, and a shadow kept anyway would
	// be a buffer agreeing with itself. What replaced the check lives beside
	// SourceSharpPayload_ProbeWord in generic_payload.h.
	//-------------------------------------------------------------------------

	// The cracked edges its managed half decided, passed through to whatever
	// draws this entity. A no-op for the entities that draw plainly.
	void SetSurfaceEdges( const unsigned char *pEdges, int nCount );

	// --- IHandleEntity -------------------------------------------------------
	//
	// Overridden because CDefaultClientRenderable's own answer is Assert(false)
	// and a null dereference: its intended users are renderables with no handle
	// at all. This one is a networked entity and its handle is how both the
	// engine and the managed side name it.
	virtual void SetRefEHandle( const CBaseHandle &h ) OVERRIDE { BlockHandle() = h; }
	virtual const CBaseHandle &GetRefEHandle() const OVERRIDE { return BlockHandle(); }

	// --- IClientUnknown ------------------------------------------------------
	virtual ICollideable *GetCollideable() OVERRIDE { return this; }
	virtual IClientNetworkable *GetClientNetworkable() OVERRIDE { return this; }
	virtual IClientRenderable *GetClientRenderable() OVERRIDE { return this; }

	// Itself, now. This is what render->DrawBrushModelEx asks for and refuses to
	// work without, and answering NULL here is what kept every brush entity off
	// this hierarchy.
	virtual IClientEntity *GetIClientEntity() OVERRIDE { return this; }

	// NULL, and it stays NULL. A client think is the client deciding something
	// on its own between updates, and nothing here does: the server decides and
	// this side shows. Returning "this" would put the entity in the client
	// think list to be asked a question it has no answer to, every frame.
	virtual IClientThinkable *GetClientThinkable() OVERRIDE { return NULL; }

	// --- IClientThinkable ----------------------------------------------------
	//
	// Implemented because IClientEntity inherits it, not because anything calls
	// it: GetClientThinkable above answers NULL, so the entity is never in the
	// think list and none of these is ever reached. The handle is stored anyway
	// rather than asserted, because a stub that lies about what it kept is
	// worse than one that keeps it.
	virtual void ClientThink() OVERRIDE {}
	virtual ClientThinkHandle_t GetThinkHandle() OVERRIDE { return m_hThink; }
	virtual void SetThinkHandle( ClientThinkHandle_t hThink ) OVERRIDE { m_hThink = hThink; }

	// --- IClientEntity -------------------------------------------------------
	virtual const Vector &GetAbsOrigin( void ) const OVERRIDE { return Block().Head.Origin; }
	virtual const QAngle &GetAbsAngles( void ) const OVERRIDE { return Block().Head.Angles; }

	// No face and no voice. Both are for entities that speak, and this
	// hierarchy has no such layer yet; false means "not audible", which is the
	// honest answer for something that emits nothing.
	virtual CMouthInfo *GetMouth( void ) OVERRIDE { return NULL; }
	virtual bool GetSoundSpatialization( SpatializationInfo_t & ) OVERRIDE { return false; }

	// The client's half of the same NULL the server returns. Nothing in the
	// client SDK can reach this entity, which is the point.
	//
	// THE STEP-5 CANARY THAT USED TO BE HERE IS GONE, with the server's, at
	// plan_entity.md step 4g. The twin argument is written out once, on
	// CStandaloneEntity::GetBaseEntity() in shell_standalone.h; the short of it
	// is that the canary simulated `using C_BaseEntity = IGameEntity`, and
	// plan_entity.md does not alias the name -- it deletes the class.
	//
	// The client's half was the LATER of the two and the weaker: for as long as
	// IClientUnknown had no GetGameEntity() there was nothing for a client
	// resolver to ask, so arming it was a survey rather than a check. It became
	// a real check when the crossing landed, and it goes now for the same
	// reason the server's does.
	virtual IGameEntity *GetBaseEntity() OVERRIDE { return NULL; }

	// --- IClientNetworkable --------------------------------------------------
	virtual IClientUnknown *GetIClientUnknown() OVERRIDE { return this; }
	virtual void Release() OVERRIDE { delete this; }
	virtual ClientClass *GetClientClass() OVERRIDE;
	virtual void NotifyShouldTransmit( ShouldTransmitState_t state ) OVERRIDE;
	virtual void OnPreDataChanged( DataUpdateType_t ) OVERRIDE {}
	virtual void OnDataChanged( DataUpdateType_t ) OVERRIDE {}
	virtual void PreDataUpdate( DataUpdateType_t ) OVERRIDE {}
	virtual void PostDataUpdate( DataUpdateType_t updateType ) OVERRIDE;

	// The managed half, made as soon as this entity knows what it is. Public
	// because the type id's RECV PROXY calls it — see
	// RecvProxy_StandaloneTypeId, which is where §3.6's "creates the managed
	// entity immediately rather than on first data update" actually happens, and
	// which is why this is not simply the top of PostDataUpdate.
	//
	// Idempotent, and answers whether this entity HAS a managed half rather than
	// whether this call made one. Most entities on this hierarchy never get one
	// and that is by design, not a failure.
	bool EnsureManagedHalf();
	virtual bool IsDormant( void ) OVERRIDE { return Block().Client.Dormant != 0; }

	// plan_crossings.md Phase X3: the dormancy bit, pushed to managed at every write.
	void PushDormancy( void );

	//-------------------------------------------------------------------------
	// "IS THIS A PLAYER" — plan_entity.md §3.16, cast family 5, whose stated
	// replacement is exactly this: "is a player is a flag in the engine block;
	// the shell is final, so there is no subtype to cast to".
	//
	// A FLAG NOW, which is what §3.16 asked for and what step 6f made possible:
	// C_StandalonePlayer is gone, this class is the only client shell, and
	// there is no subtype left for a virtual to distinguish. The flag is set by
	// the tier create function from the same fact that used to choose the
	// subclass -- an index in 1..maxClients -- so the rule that MAKES an entity
	// a player is still stated exactly once.
	//
	// IT REPLACED A dynamic_cast, in shell_beam_endpoint_client.cpp (now the
	// beam executor's endpoint facts), which asked the RTTI the same question.
	//-------------------------------------------------------------------------
	bool IsPlayerShell( void ) const { return m_bPlayerShell; }

	// Set once, by the tier create function, before any prop has decoded.
	void SetPlayerShell( bool bIsPlayer ) { m_bPlayerShell = bIsPlayer; }

	//-------------------------------------------------------------------------
	// THE VIEWER'S OWN AVATAR, and the predicted state of it (step 6f).
	//
	// The local-player latch is the engine naming its own client's index and
	// the entity wearing it -- the exact mirror of c_baseplayer.cpp:823, moved
	// here when the player subclass that used to hold it was deleted.
	//-------------------------------------------------------------------------
	bool IsLocalPlayerShell( void ) const { return m_bLocalPlayer; }

	// The managed replay's answer, once a frame: where it says this entity is,
	// what its eye offset and view punch are, and whether it is DRIVING the
	// entity at all. Driving turns interpolation off for it
	// (WantsInterpolation) -- a point read from the networked past must not
	// overwrite an origin the replay has already computed for now.
	void PredictionDrive( const struct SourceSharpClientPlayerView &view );

	// The viewer's eyes: the drawn origin plus the eye offset the managed
	// player published. What the view, the ears and the third-person camera
	// all share -- and what C_StandalonePlayer::EyePosition used to answer
	// from a native member the native replay wrote.
	Vector EyePosition( void ) const { return GetAbsOrigin() + m_vecViewOffset; }

	const Vector &PredictedViewOffset( void ) const { return m_vecViewOffset; }

	// What the HUD shows, published by the managed player. -1 until the first
	// prediction frame, which is honest: before a player has decoded there is
	// no health, and a zero would read as a corpse.
	int PredictedHealth( void ) const { return m_nPredictedHealth; }
	const QAngle &PredictedPunchAngle( void ) const { return m_angPunch; }
	const QAngle &PredictedPunchAngleVel( void ) const { return m_angPunchVel; }
	virtual int  entindex( void ) const OVERRIDE { return m_nEntIndex; }
	virtual void ReceiveMessage( int, bf_read & ) OVERRIDE {}
	virtual void SetDestroyedOnRecreateEntities( void ) OVERRIDE {}
	virtual void OnDataUnchangedInPVS() OVERRIDE {}

	// Where the networked data lands: this object, because the RecvTable's
	// offsets were taken against it. The same arrangement the server's
	// SendTable has, and the reason both must be built from one layout.
	virtual void *GetDataTableBasePtr() OVERRIDE { return this; }

	// --- IClientRenderable ---------------------------------------------------
	//
	// The pure ones CDefaultClientRenderable leaves to the subclass, plus the
	// handful whose default answer would make the entity invisible.
	// WHERE IT IS DRAWN FROM. A LOAD, and it stopped being a branch at 6e.
	//
	// A ragdoll on this hierarchy is simulated by the LOCAL solver and nothing
	// updates Block().Head.Origin afterwards -- for a gib whose server half has
	// already gone, nothing ever will. So this used to ask the solver: `return
	// m_pRagdoll ? RagdollOrigin() : Block().Client.RenderOrigin;`, on the
	// render path, per entity, per pass, per frame.
	//
	// The pose changes exactly ONCE PER PHYSICS STEP and is read many times a
	// frame, so the physics step now writes it here instead
	// (managed's ClientRagdolls, from RagdollLibrary_StepPoses) and this is a load. That is 6b's argument for
	// the render policy, applied to the one render input a ragdoll owns.
	//
	// Left on Block().Head.Origin, the bones would draw in the right place
	// (they come from the solver) while the entity was CULLED against the spot
	// it was thrown from -- a ragdoll that vanishes when the place it used to
	// be leaves the screen.
	virtual const Vector &GetRenderOrigin( void ) OVERRIDE
	{
		return Block().Client.RenderOrigin;
	}
	virtual const QAngle &GetRenderAngles( void ) OVERRIDE { return Block().Client.RenderAngles; }
	// THE RENDER TRANSFORM, WHICH IS NOT THE COLLISION ONE (step 6b). Both
	// returned Head.WorldTransform until this step, so the renderer and the
	// partition shared one matrix and a renderable could not be drawn anywhere
	// but where it collided -- see PublishRenderPose and MoveToEye.
	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE { return Block().Client.RenderTransform; }
	virtual bool ShouldDraw( void ) OVERRIDE;

	//-------------------------------------------------------------------------
	// Moves this renderable in or out of the leaf system to match ShouldDraw.
	//
	// STOCK HAS THIS AND THIS HIERARCHY DID NOT, and the gap is easy to miss
	// because ShouldDraw exists and looks like it is doing something. It is
	// not: CViewRender asks ShouldDraw only for the VIEWMODEL list
	// (DrawRenderablesInList). Every other renderable is drawn because it is in
	// a leaf, and the leaf system never re-asks — C_BaseEntity::UpdateVisibility
	// is what puts it there and takes it away, on the answer.
	//
	// So an entity whose ShouldDraw starts answering false keeps drawing
	// forever unless something calls this. Until the local player there was
	// nothing on this hierarchy whose answer changed for a reason other than a
	// model arriving or the PVS, both of which already call AddToLeafSystem.
	//
	// Edge-triggered off the render handle, which IS the "am I in it" bit, so
	// calling it every frame costs a comparison.
	//-------------------------------------------------------------------------
	void UpdateVisibility();
	// Transparent while it is FADING, and only then. The leaf system reads this
	// to choose which pass an entity is drawn in, and a brush drawn in the
	// opaque pass at anything but full alpha writes depth for pixels it did not
	// paint — which shows as the world disappearing behind it rather than as a
	// fade.
	// A managed class that DRAWS ITSELF says so through SetBlending, and the
	// sprite family is why: an additive sprite must be in the translucent list
	// or DrawTranslucentRenderable never sees it at all.
	// A LOAD, as of step 6b: ClientRenderPolicy.Transparent decides it out of
	// the fade style and the blending bit, both of which are in the block too.
	virtual bool IsTransparent( void ) OVERRIDE { return Block().Client.Transparent != 0; }
	virtual const model_t *GetModel( void ) const OVERRIDE;
	virtual int  DrawModel( int flags ) OVERRIDE;
	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE;

	// A model instance is where the engine keeps this renderable's baked
	// lighting. Without one every studio draw relights from scratch, and the
	// decal system has nowhere to put anything.
	virtual void CreateModelInstance() OVERRIDE;
	virtual ModelInstanceHandle_t GetModelInstance() OVERRIDE { return m_ModelInstance; }

	virtual ShadowType_t ShadowCastType() OVERRIDE;

	//-------------------------------------------------------------------------
	// The rest of IClientRenderable, at the values CDefaultClientRenderable
	// gave them.
	//
	// Carried here verbatim rather than inherited, because this class now
	// derives from IClientEntity and can no longer have that base. They are
	// listed in the header rather than hidden in the .cpp so that what this
	// entity does NOT do is as readable as what it does.
	//-------------------------------------------------------------------------
	virtual bool IsTwoPass( void ) OVERRIDE { return false; }
	virtual void OnThreadedDrawSetup() OVERRIDE {}
	virtual bool UsesPowerOfTwoFrameBufferTexture( void ) OVERRIDE { return false; }
	virtual bool UsesFullFrameBufferTexture( void ) OVERRIDE { return false; }
	// The client shadow manager's name for this entity's shadow, and the whole
	// reason managed props cast one at all.
	//
	// This answered CLIENTSHADOW_INVALID_HANDLE unconditionally until Phase 5's
	// gate, with no member behind it, and that ONE constant was the whole bug:
	// CClientShadowMgr reaches a renderable's shadow only through this handle —
	// GetActualShadowCastType( pRenderable ) is literally
	// GetActualShadowCastType( pRenderable->GetShadowHandle() ) — so an invalid
	// handle is SHADOWS_NONE no matter what ShadowCastType() below says. The
	// virtual was answering a question nobody was asking.
	virtual ClientShadowHandle_t GetShadowHandle() const OVERRIDE { return m_ShadowHandle; }
	virtual ClientRenderHandle_t &RenderHandle() OVERRIDE { return m_hRenderHandle; }
	// WHICH BODY GROUPS, out of the block rather than the constant zero that
	// stood here. Zero is still what it answers for every entity today, and
	// that is the point of the change rather than an argument against it: the
	// value is now STORAGE with a name in §3.2's tail, so the day a managed
	// class sets one there is a field to set. A literal is a value nothing can
	// ever write.
	virtual int  GetBody() OVERRIDE { return Block().Client.Body; }
	// WHICH SKIN, per instance, and it answered 0 for every entity on this
	// hierarchy until the animating blockers were taken.
	//
	// It is one int and it is the whole of what a mapper's `skin` keyvalue and
	// a break piece's inherited skin come to: modelrender reads
	// ModelRenderInfo_t::skin and picks a row out of the model's own texture
	// table. Zero is a legitimate row — the first one — which is why the
	// constant was invisible: every model has a skin 0 and it usually looks
	// right, so a crate that should have been the rusted variant drew as the
	// clean one and nothing anywhere reported a missing value.
	virtual int  GetSkin() OVERRIDE { return Block().Client.Skin; }
	virtual bool UsesFlexDelayedWeights() OVERRIDE { return false; }
	// How solid this entity is drawn, and when it is worked out.
	//
	// The engine's contract is a pair: ComputeFxBlend once per frame before
	// anything draws, GetFxBlend as many times as the renderer likes after. The
	// split is not decoration — the distance fade below reads the view origin,
	// which is only settled once per frame, and answering it per draw call
	// would give the same entity different alphas in the same frame.
	virtual void ComputeFxBlend( void ) OVERRIDE;
	virtual int  GetFxBlend( void ) OVERRIDE { return Block().Client.FxBlend; }
	virtual bool LODTest() OVERRIDE { return true; }
	virtual void SetupWeights( const matrix3x4_t *, int, float *, float * ) OVERRIDE {}
	virtual void DoAnimationEvents( void ) OVERRIDE {}
	virtual IPVSNotify *GetPVSNotifyInterface() OVERRIDE { return NULL; }
	virtual void GetRenderBoundsWorldspace( Vector &absMins, Vector &absMaxs ) OVERRIDE
	{
		DefaultRenderBoundsWorldspace( this, absMins, absMaxs );
	}
	// HOW THE MODEL IS TINTED, from the block's three colour bytes rather than
	// the three 1.0fs that stood here. Unmodulated is 255,255,255, which the
	// constructor writes, so every entity answers exactly what it did before —
	// and a managed class that wants a tint now has somewhere to put one.
	//
	// The engine wants 0..1 floats and the block holds the engine's own color32
	// bytes (§3.2), so the conversion is here, once, at the boundary.
	virtual void GetColorModulation( float *color ) OVERRIDE
	{
		const EngineBlockClientTail_t &tail = Block().Client;

		color[0] = tail.RenderColorR * ( 1.0f / 255.0f );
		color[1] = tail.RenderColorG * ( 1.0f / 255.0f );
		color[2] = tail.RenderColorB * ( 1.0f / 255.0f );
	}
	virtual bool ShouldReceiveProjectedTextures( int ) OVERRIDE { return false; }
	virtual bool GetShadowCastDistance( float *, ShadowType_t ) const OVERRIDE { return false; }
	virtual bool GetShadowCastDirection( Vector *, ShadowType_t ) const OVERRIDE { return false; }
	virtual void GetShadowRenderBounds( Vector &mins, Vector &maxs, ShadowType_t ) OVERRIDE
	{
		GetRenderBounds( mins, maxs );
	}
	// Whether this entity is already on the manager's dirty list.
	//
	// A real flag, not the `false` and the empty body that stood here while
	// there was no shadow to be dirty. CClientShadowMgr::AddToDirtyShadowList
	// opens with `if ( pRenderable->IsShadowDirty() ) return;`, so a renderable
	// that always answers no is added to that list again on every position
	// change and re-projected as many times per frame as it moved — and
	// CreateShadow's own `Assert( !pRenderable->IsShadowDirty() )` would then
	// be checking a constant. Stock keeps the same bit in EFL_DIRTY_SHADOWUPDATE.
	virtual bool IsShadowDirty( void ) OVERRIDE { return m_bShadowDirty; }
	virtual void MarkShadowDirty( bool bDirty ) OVERRIDE { m_bShadowDirty = bDirty; }
	virtual IClientRenderable *GetShadowParent() OVERRIDE { return NULL; }
	virtual IClientRenderable *FirstShadowChild() OVERRIDE { return NULL; }
	virtual IClientRenderable *NextShadowPeer() OVERRIDE { return NULL; }
	// Attachments and the skeleton, answered by the managed skeleton
	// (ClientSkeletons, through IManagedBonesClient; plan
	// physics_bones_native_zero 1.2, client half). ONE-BASED, the engine's
	// convention: attachment 0 means "no attachment".
	virtual int  LookupAttachment( const char *pAttachmentName ) OVERRIDE;
	virtual bool GetAttachment( int number, Vector &origin, QAngle &angles ) OVERRIDE;
	virtual bool GetAttachment( int number, matrix3x4_t &matrix ) OVERRIDE;
	virtual float *GetRenderClipPlane( void ) OVERRIDE { return NULL; }
	virtual void RecordToolMessage() OVERRIDE {}
	// Stock's is m_nRenderMode == kRenderGlow || kRenderWorldGlow, which this
	// hierarchy has no member to read. Pushed by SetBlending instead.
	virtual bool IgnoresZBuffer( void ) const OVERRIDE { return m_bIgnoreZ; }

	// The engine's skeleton ask: the engine's array, written in place by the
	// managed skeleton. A NULL pBoneToWorldOut asks only whether there are
	// bones (C_BaseAnimating's contract through CalcAttachments).
	virtual bool SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones, int boneMask,
	                         float currentTime ) OVERRIDE;

public:

	// --- ICollideable --------------------------------------------------------
	virtual IHandleEntity *GetEntityHandle() OVERRIDE { return this; }
	virtual const Vector &OBBMinsPreScaled() const OVERRIDE { return Block().Head.Mins; }
	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE { return Block().Head.Maxs; }
	virtual const Vector &OBBMins() const OVERRIDE { return Block().Head.Mins; }
	virtual const Vector &OBBMaxs() const OVERRIDE { return Block().Head.Maxs; }
	virtual void WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const OVERRIDE;
	// THE CUSTOM-TEST GATE, stock's C_BaseAnimating::TestCollision
	// (c_baseanimating.cpp:5280) word for word on its two arms: a RAY carrying
	// the FSOLID_CUSTOMRAYTEST flag and a swept BOX carrying
	// FSOLID_CUSTOMBOXTEST are answered by TestHitboxes, and the flag decides
	// which shape the answer was tested as. Stock's terminal `Assert(0)` is
	// deliberately NOT copied: it is a no-op in shipping builds, which return
	// false, and this shell has paths stock's C_BaseAnimating never walked —
	// a brush prop with neither custom flag set reaches here on a box sweep
	// and false ("use the box the engine already has") is the honest answer.
	// Red at the stub this replaces: two unnamed-parameter `return false;`
	// lines, which made the impact clip's hit on a managed prop impossible
	// whatever the flag said.
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
	// Declared here; defined beside SetupBones, a thunk into the managed
	// skeleton. Same shape as the server twin's (shell_standalone_hierarchy.cpp):
	// true means "I have hitboxes and I tested them", NOT "the ray hit" — the
	// caller reads the pair with tr.DidHit() the way stock's callers do.
	virtual bool TestHitboxes( const Ray_t &ray, unsigned int fContentsMask, trace_t &tr ) OVERRIDE;
	virtual int GetCollisionModelIndex() OVERRIDE { return Block().Head.ModelIndex; }
	virtual const model_t *GetCollisionModel() OVERRIDE { return (model_t *)GetModel(); }
	virtual const Vector &GetCollisionOrigin() const OVERRIDE { return Block().Head.Origin; }
	virtual const QAngle &GetCollisionAngles() const OVERRIDE { return Block().Head.Angles; }
	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE { return Block().Head.WorldTransform; }
	// THE SOLIDITY PAIR, THE SERVER TWIN'S WORDS (shell_standalone.h:468-469):
	// the received block head, not a constant. These are the first two answers
	// an engine clip asks — ClipRayToEntity at MASK_SHOT reads GetSolid and
	// dispatches on it — and while both answered a hardcoded inertness the
	// bullet-impact effect's clip passed through every managed prop, so
	// tr.DidHit() never fired and the custom metal effect never played. The
	// values arrive on the props tier with the m_nSolidType/m_nSolidFlags
	// rows; the block head is storage the decoder writes, and, as for Origin
	// below it, nothing here runs when they change — the answers are loads.
	virtual SolidType_t GetSolid() const OVERRIDE { return (SolidType_t)Block().Head.SolidType; }
	virtual int GetSolidFlags() const OVERRIDE { return Block().Head.SolidFlags; }
	// C_BaseEntity's helper, which the gate above needs and ICollideable does
	// not carry (ICollideable.h declares the answer, not the test).
	bool IsSolidFlagSet( int nFlagMask ) const { return ( GetSolidFlags() & nFlagMask ) != 0; }
	virtual IClientUnknown *GetIClientUnknown() const { return NULL; }
	virtual int GetCollisionGroup() const OVERRIDE { return COLLISION_GROUP_NONE; }
	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE;
	virtual bool ShouldTouchTrigger( int ) const OVERRIDE { return false; }
	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

	// Networked in from the server. Public: the RecvProp offsets are taken here,
	// and they must match the server's SendProp offsets field for field.

	// Which entity class this is. NOT networked any more: it is filled in from
	// m_nTypeId at the first update, out of the published type table, and every
	// reader below goes on reading a name exactly as it did.
	//
	// Sixty-four bytes of the object rather than sixty-four bytes on the wire
	// per entity, which is what the swap bought.
	char   m_szClassname[ 64 ];

	// The discriminators (Part 3 and Part 6). Public for the reason the others
	// are: the RecvProp offsets are taken against them, and they must pair with
	// the server's SendProps name for name.
	uint32 m_nTypeId;
	uint32 m_nSchemaHash;
	uint32 m_nPayloadWords;

	// The id the classname above was last resolved from. Not networked: it is
	// how PostDataUpdate knows whether the name it is holding still belongs to
	// the id that arrived, without a string compare per update per entity.
	uint32 m_nResolvedTypeId;

	// The type id the managed half was last OFFERED under, or 0xFFFFFFFF. Not
	// the same question as m_bHasManaged: most entities here have no managed
	// class by design, so "asked and refused" is the ordinary answer and has to
	// be remembered, or the refusal is a crossing per entity per update for the
	// life of the map. See EnsureManagedHalf.
	uint32 m_nManagedTriedTypeId;

	// Networked, because a physics prop tumbles. Public for the same reason the
	// others are: the RecvProp offset is taken against it.

	//-------------------------------------------------------------------------
	// Interpolation, AFTER THE ARITHMETIC (plan_entity.md step 6a).
	//
	// The server sends snapshots, not frames. Writing the arriving origin
	// straight into Block().Head.Origin draws the entity at whatever the last
	// snapshot said, so it advances in one jump per update and stands still in
	// between — at a 20Hz update rate a projectile crossing a room lands in
	// 75-unit steps. The average speed is right and the motion is not. So the
	// networked value is history and what is drawn is a point in it, read
	// slightly in the past.
	//
	// ALL OF THAT IS NOW MANAGED CODE, and this is what is left: what has to
	// happen to an entity once its pose has changed. Rebuilding the world
	// transform and telling the leaf system are engine calls, and the cycle is
	// the animation input that is a function of the clock. None of the three is
	// a decision.
	//
	// Called once a frame for every entity the pass writes, from
	// StandaloneClients_PostInterpolate.
	//-------------------------------------------------------------------------
	void PostInterpolate( float flCurrentTime );

	// Whether the interpolation pass may write this entity's position. TRUE
	// for everything except the entities whose position is computed rather
	// than received: the predicted local player (Phase 9c4), whose replay
	// already answered "where am I now" and must not be overwritten by a
	// point read from the networked past — and the local VIEWMODEL (Phase
	// 10's render half), whose position is the eye, re-pinned by the same
	// view setup that places the camera.
	// SEMANTIC MERGE, not keep-both: 6b moved the viewmodel flag into the
	// block (m_bViewModel -> Client.ViewModel, see the migration note
	// below) and 6f added the prediction-driving condition. 6f's arm
	// still named m_bViewModel, which no longer exists -- keeping either
	// side alone would have dropped a condition, and keeping 6f's would
	// not have compiled.
	virtual bool WantsInterpolation() const
	{ return Block().Client.ViewModel == 0 && !m_bPredictionDriving; }

	// The packet's own server time, into the block, for the managed rings to
	// key their history off. See the definition.
	void NoteNetworkedPosition();

protected:
	// Recomputed whenever the networked state moves it, because both the
	// renderer and the leaf system read it rather than the origin.
	void OnPositionChanged();

	// The drawn pose, into the client tail. Called by OnPositionChanged and,
	// separately, by MoveToEye — which does not go through OnPositionChanged
	// and would otherwise leave the viewmodel's render pose at the last
	// networked one. See the definition.
	void PublishRenderPose();

	// Into and out of the list the renderer walks. An entity that is not in it
	// is never asked to draw, and nothing says so.
	void AddToLeafSystem();

public:
	// Managed code declaring that it draws this entity itself, and what it
	// occupies. Re-inserts into the leaf system, because which leaves an entity
	// belongs to is decided by exactly these bounds.
	void SetCustomDraw( const Vector &mins, const Vector &maxs );

	// Which of stock's two distance-fade rules this entity follows, and over
	// what band. Pushed by managed code; style 0 never fades, which is what
	// every entity that has not asked already does.
	void SetFade( int nStyle, float flStartDist, float flEndDist, float flMinAlpha );

	// A second brush model drawn at this entity's transform, opaque, after its
	// own. Negative clears it.
	void SetOverlayModel( int nModelIndex ) { m_nOverlayModelIndex = nModelIndex; }

	//-------------------------------------------------------------------------
	// THE JOINTED BODY. A body is managed's (SourceSharp.Client's
	// ClientRagdolls: RagdollSolver over vphysics' vtables, plan
	// physics_bones_native_zero Phase 3), as is the render tail the physics
	// step writes for it. What the shell keeps is whether the solver owns this
	// entity's pose: PublishRenderPose then leaves the tail alone, and
	// GetRenderBounds reads the body's cube from it.
	//-------------------------------------------------------------------------
	bool IsRagdoll() const;

	// Set by managed through IShimStandaloneClient::StandaloneSetRagdoll;
	// setting it re-leafs the entity. A store: managed decides.
	void SetRagdollPose( bool bRagdoll );

	// WHICH SKIN this instance wears. See GetSkin above for what it reaches.
	//
	// Not range-checked against the model's skin count, deliberately: the count
	// belongs to the model, a model can change under an entity between the
	// state arriving and the next draw, and modelrender already treats an
	// out-of-range skin as skin 0. Refusing here would turn "this model has
	// fewer skins than the mapper thought" into a warning per entity per
	// update, for a thing the renderer handles quietly and correctly.
	void SetSkin( int nSkin ) { Block().Client.Skin = nSkin; }

	// HOW BIG this instance wears it. The client's copy of the server's, set
	// from the same managed state through the same slot; the two skeletons are
	// separate for the reason the pose is (see the side-ness note in the
	// server's shell), and this one is what the player actually sees.
	//
	// Refused at zero and below exactly as the server's is, and for the same
	// reason: it multiplies the bones' 3x3 block, so zero is a model drawn as
	// nothing and picked by nothing.
	void SetModelScale( float flScale );
	float GetModelScale() const { return m_flModelScale; }

	// How an entity that draws ITSELF is sorted, blended and depth-tested.
	// See StandaloneSetBlending in sourcesharp.h.
	void SetBlending( bool bTranslucent, bool bIgnoreZ, int nAlpha );

	//-------------------------------------------------------------------------
	// Whether this entity casts a shadow, when the model's own answer is not
	// the wanted one. A StandaloneShadowChoice_t; see ClientRenderPolicy for
	// the three values and StandaloneSetShadowCast in sourcesharp.h for the
	// contract. Takes effect at once — the shadow is created or destroyed on
	// the spot rather than at the next update.
	//-------------------------------------------------------------------------
	void SetShadowCast( int nChoice );

	// What was asked for, and what the entity is actually casting right now.
	// The second is a ShadowType_t and is NOT the first: it folds in the model,
	// dormancy and the viewmodel pass. Both are read by the report slot,
	// because "I asked for auto" and "it is casting nothing" is the pair that
	// names the problem.
	int  ShadowCastChoice() const { return Block().Client.ShadowChoice; }
	bool HasShadow() const { return m_ShadowHandle != CLIENTSHADOW_INVALID_HANDLE; }

	//-------------------------------------------------------------------------
	// The viewmodel pass (Phase 10's render half). See StandaloneSetViewModel
	// in sourcesharp.h for the contract; mechanically, enabling it moves the
	// renderable into the leaf system's VIEWMODEL list — the one
	// CViewRender::DrawViewModels collates, drawn with the viewmodel FOV and
	// the depth-range hack — ends the draw-nothing custom draw that stood in
	// for rendering, and registers this entity as the one MoveViewModelToEye
	// below re-pins to the eyes every frame.
	//-------------------------------------------------------------------------
	void SetViewModelPass( bool bEnable );

	// The clock managed code chose, stored (IShimStandaloneClient's
	// StandaloneStoreSequence): the sequence, its rate and start in the block,
	// its duration and loop flag here, so SequenceCycle needs no studio header.
	// A store and nothing else: managed decides when a sequence starts.
	void StoreSequence( int nSequence, float flPlaybackRate, float flAnimTime,
	                    float flDuration, bool bLooping );

	// Where in the playing sequence this moment falls, 0..1.
	float SequenceCycle( float flNow ) const;

	// The index as it arrived — cs_cl_viewmodel_report prints it beside the
	// resolved name, because "which index is this client holding" is the first
	// question when the name looks like last week's weapon.
	int NetworkedModelIndex() const { return Block().Head.ModelIndex; }

	// The eye pin: origin, angles and transform in one write, no leaf-system
	// notification — a viewmodel-pass renderable is collated from its own
	// list, never from the leaves.
	void MoveToEye( const Vector &vecEye, const QAngle &angEye );

private:
	void RemoveFromLeafSystem();

	//-------------------------------------------------------------------------
	// Bring the client shadow into line with what this entity now is.
	//
	// C_BaseEntity::CreateShadow, under a name that says what it does both
	// ways: it destroys as often as it creates, because ShadowCastType()
	// changes as the model arrives, as the entity goes dormant, and as managed
	// code changes its mind. Called from every place that can change any of
	// those, which is why it is one function and not a line at each.
	//-------------------------------------------------------------------------
	void RefreshShadow();
	void DestroyShadow();

	//-------------------------------------------------------------------------
	// The model facts the shadow rule is decided FROM, into the block.
	//
	// RenderGroup() USED TO BE HERE and is gone: which of the renderer's lists
	// an entity belongs in is a decision, and decisions are C#'s (§3.2). So is
	// what kind of shadow it casts -- shadow_cast_policy.h held that rule and
	// has been deleted with it. What could NOT move is this: opening a studio
	// header is mdlcache's business. See the definition.
	//-------------------------------------------------------------------------
	void PublishModelFacts();

	//-------------------------------------------------------------------------
	// One entity's share of the render-policy apply sweep. See the definition:
	// edge-triggered off Client.PolicyOutputs, so an entity whose choices have
	// not moved costs a load and a compare.
	//-------------------------------------------------------------------------
public:
	void ApplyRenderPolicy();

	//-------------------------------------------------------------------------
	// "Something the render policy is decided from has changed."
	//
	// One counter, bumped by every writer of every input -- the dormancy arm,
	// the model-facts publish, and each of the five managed setters. Managed
	// code decides for an entity when this differs from the value it last
	// decided for, so a frame in which nothing about an entity changed costs it
	// one load and one compare and no arithmetic at all.
	//
	// WRAPPING IS FINE AND IS NOT A LATENT BUG. It is compared for equality,
	// never ordered, and a wrap would have to land on exactly the value the
	// managed side recorded four billion bumps ago for the same entity in the
	// same slot with the same serial.
	//-------------------------------------------------------------------------
	void BumpPolicyInputs() { Block().Client.PolicyInputs++; }

	// Whether this renderable is in the client leaf system, which is what
	// shell_leaf_link.h's three guards are written against.
	bool IsInLeafSystem() const { return m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE; }

private:
	// Out of the client's PVS, into the block, and out to managed code.
	void SetDormant( bool bDormant );

	//-------------------------------------------------------------------------
	// SIXTEEN MEMBERS ARE GONE FROM HERE, INTO THE ENGINE BLOCK (step 5c, the
	// client half of step 1b). They are named so the absence is a statement
	// rather than a gap, and standalone_client_layout_test.cpp pins every one
	// of them BY NAME:
	//
	//   head   m_vecOrigin        -> Head.Origin
	//          m_angRotation      -> Head.Angles
	//          m_nModelIndex      -> Head.ModelIndex
	//          m_vecMins          -> Head.Mins
	//          m_vecMaxs          -> Head.Maxs
	//          m_Transform        -> Head.WorldTransform
	//          m_RefEHandle       -> Head.Handle          (through BlockHandle)
	//
	//   tail   m_nFxBlend         -> Client.FxBlend
	//          m_nSkin            -> Client.Skin
	//          m_nShadowChoice    -> Client.ShadowChoice   (§3.2's "shadow choice")
	//          m_nSequence        -> Client.Sequence
	//          m_flPlaybackRate   -> Client.PlaybackRate
	//          m_flSequenceStart  -> Client.AnimTime
	//          m_vecDrawMins      -> Client.RenderMins
	//          m_vecDrawMaxs      -> Client.RenderMaxs
	//          m_nCustomAlpha     -> Client.RenderColorA
	//
	// TWO COPIES OF AN ENTITY'S ORIGIN IS NOT A COST, IT IS A BUG WAITING FOR
	// ITS FIRST DIVERGENT WRITER -- 1b's words, and they are the reason the
	// members are deleted rather than left in place beside the block. Here the
	// two writers would be the engine's DECODER (which now addresses the block
	// directly, see BuildStandaloneRecvProps) and this shell's own code, and the
	// renderer, the leaf system and the collision partition would then disagree
	// about where the entity is depending on which of the two they reached.
	//
	// Three more block fields have no member behind them and are written by the
	// paths named here: Client.RenderOrigin and Client.RenderAngles in
	// OnPositionChanged, Client.Cycle in Interpolate, Client.SimulationTime in
	// NoteNetworkedPosition. Client.Body is storage only, and answers the zero
	// GetBody() used to return as a literal.
	//
	// EIGHT MORE WENT THE SAME WAY IN STEP 6b, and they are the render policy's
	// inputs rather than its outputs -- what an entity IS, which managed code
	// decides what to draw from:
	//
	//   tail   m_bDormant         -> Client.Dormant
	//          m_bViewModel       -> Client.ViewModel
	//          m_bCustomDraw      -> Client.CustomDraw
	//          m_bTranslucent     -> Client.Translucent
	//          m_nFadeStyle       -> Client.FadeStyle
	//          m_flFadeStartDist  -> Client.FadeStartDistance
	//          m_flFadeEndDist    -> Client.FadeEndDistance
	//          m_flFadeMinAlpha   -> Client.FadeMinAlpha
	//
	// The OUTPUTS have no member and never had one: Client.Drawn,
	// Client.Transparent, Client.RenderGroup, Client.ShadowType,
	// Client.ShadowFlags and Client.HideWhenOwnerSeen are written by C# and read
	// by the four getters above. Client.RenderTransform is written by
	// PublishRenderPose and MoveToEye.
	//-------------------------------------------------------------------------

	int         m_nEntIndex;

	// Whether a managed class claimed this classname, asked once on creation.
	bool        m_bHasManaged;

	// Whether the first draw has been reported. See DrawModel.
	bool        m_bDrawReported;

	ModelInstanceHandle_t m_ModelInstance;

	// The leaf system's handle for this renderable. CDefaultClientRenderable
	// owned this; it lives here now for the same reason the defaults above do.
	ClientRenderHandle_t  m_hRenderHandle;

	// The client shadow manager's handle for this entity's shadow, invalid
	// while it casts none. C_BaseEntity keeps the same member; not having one
	// is what the Phase 5 gate photographed.
	ClientShadowHandle_t  m_ShadowHandle;

	// Whether that shadow is already queued for re-projection. Stock keeps it
	// in EFL_DIRTY_SHADOWUPDATE; this hierarchy has no eflags, so it is a bool.
	bool                  m_bShadowDirty;

	// A StandaloneShadowChoice_t. AUTO until a managed class says otherwise,
	// which is what every entity that has never been told anything uses.

	// Never used — see ClientThink above — and kept so the setter is honest.
	ClientThinkHandle_t   m_hThink;

	// THE HISTORY BEHIND Block().Head.Origin AND Block().Head.Angles IS NOT
	// HERE ANY MORE -- plan_entity.md step 6a. It was two CInterpolatedVars per
	// entity; it is two managed rings per entity, keyed by this entity's handle,
	// in SourceSharp.Client's Interpolation. The engine block is what the two
	// sides share, and it already carries everything the rings need: the
	// networked pose in the head, and the packet time in Client.SimulationTime.
	//
	// What is left on this side is the list -- SourceSharp_ClientInterpBlocks
	// below -- and the after-effects of a pose changing, which are engine calls
	// and stay where the engine is (PostInterpolate).

	// The class this entity was created through. Never NULL for a live entity:
	// the engine cannot make one except through a ClientClass.
	ClientClass *m_pClientClass;

	// And which tier that class is, from the create function that made it. Set
	// in the constructor and never changed: it decides how long the blob past
	// this object is, and the object has already been allocated by then.
	int          m_iTier;

	// What draws this entity when a brush model is not enough on its own.
	//
	// NULL for all but one class, and it stays a pointer rather than a virtual
	// on the shell because the shell has no business knowing what glass is —
	// it owns the object and asks it to draw, and that is the whole of the
	// coupling.
	class CSourceSharpSurfaceRenderer *m_pSurface;

	// How this instance wears its model: which texture row, and at what size.
	// Both are per-instance state the hierarchy did not have, and both are
	// pushed by managed code rather than networked as props of their own — the
	// value rides the payload as ordinary managed state and the client's
	// managed half hands it here, which is the same road SetFade and
	// SetOverlayModel already take.
	float  m_flModelScale;

	// See SetRagdollPose.
	bool       m_bRagdollPose = false;

	// A second brush model drawn at this entity's transform. -1 for everything
	// but a func_areaportalwindow with a background set.
	int    m_nOverlayModelIndex;

	// An entity that draws ITSELF from managed code, and the box it occupies
	// while doing it.
	//
	// The alternative was a renderer class per drawable kind, written in C++ and
	// re-reading the entity's replicated state at offsets it had to resolve by
	// name — which is what the surface renderer does and what a beam does NOT
	// need. A surface builds a mesh per crack edge per frame and could not
	// afford the boundary; a beam is one call and eleven numbers, and managed
	// code already holds every one of them decoded.
	// Set together by StandaloneSetCustomDraw; see there. The FLAG went into the
	// block with step 6b -- it is what ClientRenderPolicy.Drawn reads to know an
	// entity needs no model -- and the bounds were already there.

	// The one answer a stock C_BaseEntity derives from its render mode that is
	// NOT policy: whether the draw ignores the depth buffer is a parameter of
	// the draw call, read by DrawModel and by nothing that decides anything.
	// Pushed by SetBlending beside the translucency bit, which did go into the
	// block.
	bool   m_bIgnoreZ;

	// WHICH MODEL INDEX THE FACTS IN THE BLOCK DESCRIBE, and -1 before any have
	// been published. The index is networked and the engine's decoder writes it
	// into the block with nothing on this side running, so this is what notices
	// it moved -- see PublishModelFacts.
	int    m_nPublishedModelIndex;

	// WHICH EF_* BITS THE POLICY LAST SAW, and -1 before any update (step
	// 8e-render). Exactly the shape above and for exactly the same reason: the
	// effects word is networked and the engine's decoder writes it straight into
	// the block, so nothing on this side runs when EF_NODRAW arrives. Without
	// this the bit would sit in the block unread until some OTHER input bumped
	// the serial -- which for a func_dust that never moves is never.
	int    m_nPublishedEffects;

	// THE LAST Client.PolicyOutputs THIS SHELL ACTED ON. See ApplyRenderPolicy:
	// it is compared, never ordered, and it is what keeps the leaf system and
	// the shadow manager off the per-frame path.
	uint32_t m_nAppliedPolicy;

	// An index in 1..maxClients, decided by the tier create function before a
	// prop has decoded. See IsPlayerShell.
	bool   m_bPlayerShell;

	// Of those, the one the engine names as this client's own. Latched on the
	// creating update, cleared when the entity goes.
	bool   m_bLocalPlayer;

	// Whether the managed replay is driving this entity's position right now
	// (step 6f). Written by PredictionDrive, read by WantsInterpolation.
	bool   m_bPredictionDriving;

	// The rest of the view state the managed player publishes (step 6f). Zero
	// until the first PredictionDrive, which is honest: before a player has
	// decoded there is no eye and no punch, and a default 64-unit eye height
	// would be a plausible number invented for a viewer who does not exist.
	Vector m_vecViewOffset;
	QAngle m_angPunch;
	QAngle m_angPunchVel;
	int    m_nPredictedHealth;

	// The playing sequence's duration and loop flag, as the managed skeleton
	// stored them with its clock (StoreSequence), so the per-frame cycle
	// PostInterpolate writes needs no studio header in hand.
	float  m_flSeqDuration;
	bool   m_bSeqLooping;


};


//-----------------------------------------------------------------------------
// The client-side shell for a managed entity handle, or NULL.
//
// The client's half of SourceSharp_ResolveStandalone, and the one thing a
// per-class renderer in another file needs: what model this entity is wearing
// and where it is, both of which are already networked and neither of which
// should be passed in a second time.
//-----------------------------------------------------------------------------
class C_StandaloneEntity *SourceSharp_StandaloneFromHandle( uint32_t hEntity );

//-----------------------------------------------------------------------------
// The render plan's custom-draw batch (C7 item 7; shell_standalone_client.cpp
// says how it works). Capture is called with each view's renderables list as
// the leaf system builds it; the blend answers GatherSprite while managed
// prepares the batch (the blend the entity's DrawModel will see), and the
// fallback otherwise.
//-----------------------------------------------------------------------------
class CClientRenderablesList;
void SourceSharp_StandaloneDrawBatchCapture( const CClientRenderablesList &list );
float SourceSharp_StandaloneDrawBatchBlend( IClientEntity *pEntity, float flFallback );

//-----------------------------------------------------------------------------
// THE VIEWER'S OWN AVATAR, or NULL (step 6f). C_StandalonePlayer::GetLocal's
// replacement, answering the same question about the same entity -- the one
// whose index the engine names as this client's.
//
// NULL between maps, before the player entity has arrived, and on a client with
// no player at all. Every caller has always had to survive that: stock's
// C_BasePlayer::GetLocalPlayer answered NULL in exactly the same moments.
//-----------------------------------------------------------------------------
class C_StandaloneEntity *SourceSharp_LocalPlayerShell();

//-----------------------------------------------------------------------------
// Every live entity on the hierarchy, by index. For the prediction wiring's
// per-update sweep (shell_prediction.cpp, Phase 9c1) — the one walker that is
// not per classname. At() answers NULL out of range.
//-----------------------------------------------------------------------------
int SourceSharp_StandaloneClientCount();
class C_StandaloneEntity *SourceSharp_StandaloneClientAt( int nIndex );

//-----------------------------------------------------------------------------
// DESTROYS EVERY LIVE SHELL AT LEVEL SHUTDOWN, and returns how many there were.
//
// NOTHING ELSE DOES. Today the shells are torn down by the STOCK client entity
// list's Release() walking rows that SourceSharp_ClientEntityBind put there,
// and the flip deletes the file that list lives in. Lane `f-list` removed that
// bind without this and the game segfaulted in the client shadow manager on the
// SECOND map load, 0 of 5 runs. The server's twin is
// StandaloneEntities_LevelShutdown; see the definition for why this is the same
// shape and why it returns a count.
//
// Called from CShellClientDLL::LevelShutdown, in the position the stock list's
// Release had -- after the temp entities and BEFORE the post-entity system
// pass, which is what resets the subsystems a surviving shell would still hold
// handles into.
//-----------------------------------------------------------------------------
int SourceSharp_StandaloneClientsLevelShutdown();

//-----------------------------------------------------------------------------
// Pins the entity in the viewmodel pass — the local player's viewmodel — to
// the eyes the view was just set up with. Called from OverrideView, which is
// the one seam that runs every frame with the frame's own eye origin and
// angles in hand; a no-op while nothing is in the pass.
//-----------------------------------------------------------------------------
void SourceSharp_StandaloneViewModelToEye( const Vector &vecEye, const QAngle &angEye );

//-----------------------------------------------------------------------------
// Whether the camera the last frame was set up with can actually SEE the local
// player — a third-person camera that got far enough from the eye to have the
// player's own body in front of it.
//
// NOT the same question as ::input->CAM_IsThirdPerson(), and the difference is
// the one that costs a frame: a third-person camera backed into a wall is
// pulled onto the eye, and there it is a first-person camera whatever the mode
// says. Drawing the owner's model for it fills the screen with the inside of a
// head; hiding the viewmodel for it takes the arms away for no visible reason.
// Both readers want the CAMERA's answer, so both ask this one.
//
// Written once a frame by the shell's OverrideView (shell_client.cpp), which
// is the only place that knows where the camera ended up.
//-----------------------------------------------------------------------------
bool SourceSharp_ThirdPersonSeesOwner();

//-----------------------------------------------------------------------------
// Every live entity of a given classname, as renderables, into a caller-supplied
// array. Returns how many were written.
//
// For the renderer's per-kind sweeps — the reflective glass list being the first
// — which a stock entity joins by being of the right C++ type and which nothing
// on this hierarchy can. See the definition.
//-----------------------------------------------------------------------------
int SourceSharp_StandaloneGetRenderables( const char *pClassName,
                                          IClientRenderable **pOut, int nMax );

//-----------------------------------------------------------------------------
// The instance blob, mirroring the server's exactly. One size for every class,
// fixed at DLL init before any entity exists.
//-----------------------------------------------------------------------------
int  SourceSharp_StandaloneBlobOffset();



//-----------------------------------------------------------------------------
// Accepts a class's declared field list. Builds NOTHING since Step 6 — its
// entities are created through the tier — and is kept only so the description
// can still be recorded and dumped. Step 7 takes the description ABI itself.

//-----------------------------------------------------------------------------
// Which tier every entity on the hierarchy is created through, and whether one
// was registered. Both sides reach the same tier by the same rule from the same
// declared number; see SourceSharp_NetHierarchyDeclare.
//-----------------------------------------------------------------------------
bool SourceSharp_StandaloneTierIsInUse( int iTier );


//-----------------------------------------------------------------------------
// A RECV PROXY'S SUBJECT. `pStruct` is the object the decoder is writing into,
// and on this hierarchy that object IS the shell -- the datatable is built from
// C_StandaloneEntity's own offsets. There is no interface to step through and
// no handle to resolve, so this conversion cannot be either of the other two;
// it is named here so the whole tree has one spelling of it and it is not a
// `(C_StandaloneEntity *)` sitting in a proxy body where nobody reads it.
//-----------------------------------------------------------------------------
inline C_StandaloneEntity *SourceSharp_ClientShellFromRecvStruct( void *pStruct )
{
	return static_cast< C_StandaloneEntity * >( pStruct );
}

//-----------------------------------------------------------------------------
// LAST IN THE FILE, and that placement is the ban (plan_entity.md 5d).
//
// A TU that can spell C_StandaloneEntity has read this header, so it has read
// the ban, so it cannot instantiate CHandle over the shell. Anywhere earlier
// would work equally well today and would stop working the moment something
// above it named the class; anywhere else in the tree would be a ban a TU can
// simply not include. See standalone_handle_ban_client.h for why the client
// needs its own, and 1e's shell_standalone.h for the server's twin.
//-----------------------------------------------------------------------------
#include "standalone_handle_ban_client.h"

#endif // SOURCESHARP_SHELL_STANDALONE_CLIENT_H
