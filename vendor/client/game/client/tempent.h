//========= Copyright Valve Corporation, All rights reserved. ============//

//========= SourceSharp's edit of tempent.h (vendor/README.md) =====//
// C_LocalTempEntity, the client's temp entity over CShellRenderable: moved out
// of the fork's edited game/client/tempent.h (client.so on unmodified Valve
// headers, user 2026-10-25). The FTENT_* values are the temp entity format's.
#pragma once

//========= SourceSharp ======================================================//
// A TEMP ENTITY IS NOT AN ENTITY, and after this it is not one in the type
// system either. See the banner above the class below for the measurement that
// decided it.
// c_baseanimating.h is gone: nothing here ever used C_BaseAnimating. The one
// studio draw this class does (DrawStudioModel) calls modelrender->DrawModel
// directly, with MODEL_INSTANCE_INVALID and no bone setup at all.
// c_baseentity.h STAYS, for two things and only two: EHANDLE-shaped ownership
// (SetTempEntOwner's parameter, which is the caller's type and not ours) and
// the cl_entitylist lookups the collision code does on OTHER entities.
#include "client_entity.h"
#include "Sprite.h"
#include "shell_renderable.h"
#include "shell_cliententitylist.h"

// Temporary entity array
#define TENTPRIORITY_LOW 0

// TEMPENTITY flags

#define FTENT_SINEWAVE 0x00000001

#define FTENT_ROTATE 0x00000004

#define FTENT_COLLIDEWORLD 0x00000020
#define FTENT_FLICKER 0x00000040
#define FTENT_FADEOUT 0x00000080
#define FTENT_SPRANIMATE 0x00000100

#define FTENT_SPRCYCLE 0x00000800
// will collide with world and slideboxes
#define FTENT_PERSIST 0x00002000 // tent is not removed when unable to draw
#define FTENT_COLLIDEKILL 0x00004000 // tent is removed upon collision with anything
#define FTENT_PLYRATTACHMENT 0x00008000 // tent is attached to a player (owner)
#define FTENT_SPRANIMATELOOP 0x00010000 // animating sprite doesn't die when last frame is displayed
// rapid grow and fade. Very specific for gunsmoke
// attach to whatever we hit, and stay there until die time is up
// Doesn't have a model, never try to draw ( it just triggers other things )
// Must specify callback.  Callback function is responsible for killing tempent and updating fields ( unless other flags specify how to do things )
#define FTENT_WINDBLOWN 0x00200000 // This is set when the temp entity is blown by the wind
// Don't die as long as die != 0
#define FTENT_BEOCCLUDED 0x00800000 // Don't draw if my specified normal's facing away from the view
//when we collide with something, change our rendergroup to RENDER_GROUP_OTHER
// if set, use the C_BaseEntity::GetCollisionGroup when doing collide trace
// Change angles to always point in the direction of motion
// The object has a clientside particle system.
// Use fast collisions (cl_fasttempentcollision).
// Collide with the world and props

class C_LocalTempEntity;

typedef int ( *pfnDrawHelper )( C_LocalTempEntity *entity, int flags );

//========= SourceSharp -- the tempent's disposition, and the measurement =====//
// Valve's own comment on this class was "Purpose: Should this derive from some
// other class". It should, and this is the other class.
// WHAT IT WAS. `C_LocalTempEntity : public C_BaseAnimating, public
// C_SpriteRenderer` -- a full client entity, 3,240 bytes, pooled 500 at a time,
// for a shell casing that lives two seconds and draws one static model.
// WHAT IT USED FROM C_BaseAnimating: NOTHING. DrawStudioModel calls
// modelrender->DrawModel directly, passing MODEL_INSTANCE_INVALID, index and a
// skin/body/hitboxset triple. No SetupBones, no sequence, no attachment, no
// pose. The animating layer was 3,032 of the 3,240 bytes and answered no
// question this class asks.
// WHY NOT C_StandaloneEntity, WHICH IS THE OTHER DE-ENTIFIED BASE. Measured,
// not argued -- see src/tempent_layout_probe.cpp, which compiles in the client
// project's own compiler with the client project's own flags:
//     SourceSharp_StandaloneBlobOffset()             2112
//     sizeof(C_LocalTempEntity) re-parented onto it  1264   -> FITS, 848 spare
// So the netentity's route-(b) refusal does NOT reach this class: it has no
// ClientClass, no RecvTable, no payload and no tier, so the "its own tables
// address somewhere else" horn is vacuous, and operator new's
// Assert( nBytes <= SourceSharp_StandaloneBlobOffset() ) does not fire. The
// blob was never what refused it.
// What refuses it is the COST, and that was measured too: re-parenting onto
// C_StandaloneEntity leaves 130 diagnostics naming 30 distinct missing members
// in c_te_legacytempents.cpp alone, and needs a second, NON-NETWORKABLE
// constructor added to the shared standalone shell -- C_StandaloneEntity's
// constructor ends in cl_entitylist->AddNetworkableEntity( ..., nEntNum,
// nSerialNum ), so 500 pooled tempents would all claim entity index 0, which is
// the world. That is more C++ than it removes, in a file this lane does not own.
// WHY CShellRenderable IS AVAILABLE, and the ledger's reason for thinking it
// was not. The refusal said a de-entified client drawable owes the
// IClientEntity + IClientRenderable floor and that "the only thing in this tree
// that implements that floor is C_StandaloneEntity, 4,631 lines". Two things
// are wrong with that here:
//   1. The floor this class owes is IClientUnknown + IClientRenderable, NOT
//      IClientEntity. It reaches DrawModel by being in the LEAF SYSTEM, and
//      CTempEnts::AddVisibleTempEntity says so in its own words two lines
//      before it adds one: "Temporary entities have no corresponding element in
//      cl_entitylist", followed by `pEntity->index = -1`.
//   2. All three ClientEntityList().AddNonNetworkableEntity() calls are dead in
//      this build. One is in AddParticleEffect, which has no caller here (its
//      callers are TF's, and TF is not in this build). One is in
//      ClientProjectile's `if ( pszParticleEffect )` branch, and both live
//      callers -- c_te_clientprojectile.cpp:58 and :69 -- pass six arguments,
//      so that parameter is always NULL. The third is a `-tools` recording path
//      whose twin in TempEntAlloc Valve already commented out, with the note
//      that recording tempents as entities crashes.
// CShellRenderable is 214 lines, header-only, already in this build and already
// carrying CSourceSharpSpriteTrail. It supplies exactly what an entity was
// supplying this class: the IClientUnknown + IClientRenderable floor through
// CDefaultClientRenderable, AddToLeafSystem( RenderGroup_t ) with the very
// signature called here, RemoveFromLeafSystem(), a place whose setters tell the
// leaf system the object moved, and a zeroing allocator.
// WHAT IS CARRIED ACROSS BELOW, and why each one rather than "it was an entity":
//   the place, local form   A tempent never has a move parent -- Frame() used
//                           to assert it -- so local IS abs and these are eight
//                           forwarders rather than a second transform.
//   render state            m_nRenderMode / m_nRenderFX / m_clrRender and their
//                           setters. Plain members now; they were plain members
//                           on C_BaseEntity too, one of them networked.
//   ComputeFxBlend          NOT optional and NOT defaultable.
//                           CDefaultClientRenderable::GetFxBlend answers 255
//                           unconditionally, so a fading tempent (FTENT_FADEOUT
//                           ramps m_clrRender.a) would stay fully opaque and
//                           then vanish. The switch is stock C_BaseEntity's,
//                           minus the two clauses that read entity-only state.
//   ShouldDraw/IsTransparent/GetRenderBounds/IgnoresZBuffer
//                           pure or wrong on CDefaultClientRenderable; each is
//                           stock's answer with the entity terms resolved.
//   index                   -1, always, and the renderer is handed it.
//   m_pModel                what GetModel() answered.
class C_LocalTempEntity : public CShellRenderable, public C_SpriteRenderer
{
public:
	typedef CShellRenderable BaseClass;

	C_LocalTempEntity();

	virtual void Prepare( const model_t *pmodel, float time );

	virtual bool IsActive();
	virtual bool Frame( float frametime, int framenumber );

	virtual int DrawModel( int flags ) OVERRIDE;

	// Sets the velocity
	void SetVelocity( const Vector &vecVelocity );

	const Vector &GetVelocity() const { return m_vecTempEntVelocity; }

	// Set the acceleration
	void SetAcceleration( const Vector &vecAccel );

	const Vector &GetAcceleration() const { return m_vecTempEntAcceleration; }

	void SetDrawHelper( pfnDrawHelper helper ) { m_pfnDrawHelper = helper; }

	void OnRemoveTempEntity();

	// THE PLACE, in the local form the simulation is written in.
	// Frame() opened with Assert( !GetMoveParent() ) for as long as this was an
	// entity, because everything below it treats local and abs as the same
	// vector. A CShellRenderable has no parent at all, so these are that
	// identity written down rather than an assertion about it -- and the assert
	// is GONE rather than kept, because kept it would have been a tautology
	// about a base that cannot have a parent.
	// SetAbsOrigin/SetAbsAngles are CShellRenderable's and they tell the leaf
	// system the object moved. That side effect is not decoration: without it a
	// tempent draws only from the leaves it was first added to, so a shell
	// casing bouncing out of the room it spawned in stops being drawn.
	const Vector &GetLocalOrigin() const { return GetAbsOrigin(); }

	void SetLocalOrigin( const Vector &vecOrg ) { SetAbsOrigin( vecOrg ); }

	const QAngle &GetLocalAngles() const { return GetAbsAngles(); }

	void SetLocalAngles( const QAngle &ang ) { SetAbsAngles( ang ); }

	// THE MODEL. One pointer, which is all GetModel() ever was on this path:
	// nothing here has a model INDEX and nothing here precaches.
	virtual const model_t *GetModel() const OVERRIDE { return m_pModel; }

	void SetModelPointer( const model_t *pModel ) { m_pModel = pModel; }

	// RENDER STATE. Stock's setters, over plain members.
	RenderMode_t GetRenderMode() const { return (RenderMode_t)m_nRenderMode; }

	void SetRenderMode( RenderMode_t nMode, bool bForceUpdate = false ) { m_nRenderMode = nMode; }

	void SetRenderColor( byte r, byte g, byte b )
	{
		m_clrRender.r = r;
		m_clrRender.g = g;
		m_clrRender.b = b;
	}

	void SetRenderColor( byte r, byte g, byte b, byte a )
	{
		SetRenderColor( r, g, b );
		m_clrRender.a = a;
	}

	void SetRenderColorA( byte a ) { m_clrRender.a = a; }

	void SetTempEntOwnerHandle( const CBaseHandle &hOwner ) { m_hOwnerEntity = hOwner; }

	IHandleEntity *TempEntOwnerHandle() const
	{
		// THROUGH THE SLOT TABLE. Lane `f-list`. This was
		// `ClientEntityList().GetClientUnknownFromHandle( m_hOwnerEntity )` and
		// SourceSharp_ClientUnknownFromHandle is that same contract with the
		// table asked first and the world second.
		// IT IS A HEADER INLINE AND THAT IS WHY IT MATTERED. `ClientEntityList()`
		// is itself an inline over `*cl_entitylist`, so every TU that reached
		// this function emitted a weak copy carrying the stock list's symbol --
		// which is how shell_te_pool_client.o came to owe `cl_entitylist` while
		// naming it nowhere in its own text.
		return SourceSharp_ClientUnknownFromHandle( m_hOwnerEntity );
	}

	// The same owner as an ENTITY INDEX, for the managed collision arm --
	// ClientTraceLine's CTraceFilterSimple takes an index, never a handle
	// (behaviour ledger row C8). -1 when there is no owner.
	// BIT EXTRACTION AND NOT A LOOKUP. CBaseHandle::GetEntryIndex reads the
	// number out of the handle word; it does not go through
	// CBaseHandle::Get(), which is the unfiltered reinterpret of a handle
	// table that holds managed entities that row C8 exists for.
	int TempEntOwnerIndex() const { return m_hOwnerEntity.IsValid() ? m_hOwnerEntity.GetEntryIndex() : -1; }

	// IClientRenderable. CDefaultClientRenderable leaves ShouldDraw,
	// IsTransparent and GetRenderBounds pure; IgnoresZBuffer and GetFxBlend
	// below have defaults that are WRONG for this class rather than absent.
	virtual bool ShouldDraw() OVERRIDE
	{
		// Stock C_BaseEntity::ShouldDraw, with the entity terms resolved: a
		// tempent's index is -1 by construction (never 0), and it has no
		// m_fEffects, so EF_NODRAW can never be set on one.
		if ( m_nRenderMode == kRenderNone )
			return false;

		return m_pModel != NULL;
	}

	virtual bool IsTransparent() OVERRIDE;

	virtual void GetRenderBounds( Vector &theMins, Vector &theMaxs ) OVERRIDE;

	// CDefaultClientRenderable answers 1,1,1 here for every renderable, which
	// is a wrong answer rather than a missing one -- see the .cpp.
	virtual void GetColorModulation( float *color ) OVERRIDE;

	// Which pass this tempent draws in. Not an IClientRenderable method: the
	// leaf system asks for it when the object is added and ComputeFxBlend keeps
	// it current, because a fading tempent changes pass while it lives.
	RenderGroup_t GetRenderGroup();

	// CShellRenderable's AddToLeafSystem takes the group. C_BaseEntity had a
	// no-argument form as well, whose whole body was
	// `AddToLeafSystem( GetRenderGroup() )`, and CTempEnts::AddVisibleTempEntity
	// calls it for a tempent whose m_RenderGroup is RENDER_GROUP_OTHER. Same
	// body, so the two spellings cannot drift.
	using CShellRenderable::AddToLeafSystem;

	void AddToLeafSystem() { AddToLeafSystem( GetRenderGroup() ); }

	virtual bool IgnoresZBuffer() const OVERRIDE
	{
		// Stock C_BaseEntity::IgnoresZBuffer, unchanged. Not defaultable:
		// DrawTranslucentRenderable SKIPS a renderable whose answer does not
		// match the pass it is in, so a kRenderGlow tempent answering false
		// here is a muzzle flash that is never drawn.
		return m_nRenderMode == kRenderGlow || m_nRenderMode == kRenderWorldGlow;
	}

	// The alpha the renderer draws at.
	// CDefaultClientRenderable answers 255 for every renderable, always, and
	// that is not a missing feature here -- it is a wrong answer. FTENT_FADEOUT
	// ramps m_clrRender.a down and expects the model to fade with it; against a
	// constant 255 it would stay solid and then disappear at once.
	virtual void ComputeFxBlend() OVERRIDE;

	virtual int GetFxBlend() OVERRIDE { return m_nRenderFXBlend; }

protected:
	pfnDrawHelper m_pfnDrawHelper;

public:
	int flags;
	float die;
	float m_flFrameMax;
	float x;
	float y;
	float fadeSpeed;
	float bounceFactor;
	int hitSound;
	int priority;
	// if attached, this is the index of the client to stick to
	// if COLLIDEALL, this is the index of the client to ignore
	// TENTS with FTENT_PLYRATTACHMENT MUST set the clientindex!
	short clientIndex;

	// if attached, client origin + tentOffset = tent origin.
	Vector tentOffset;

	// Used by temp entities.
	QAngle m_vecTempEntAngVelocity;
	int tempent_renderamt;
	Vector m_vecNormal;

	float m_flSpriteScale;
	int m_nFlickerFrame;

	float m_flFrameRate;
	float m_flFrame;

	RenderGroup_t m_RenderGroup;

	alignas( 4 ) unsigned char unused_m_bParticleCollision[20];

	// WHAT THE ENTITY HELD, now held here.
	// Every one of these was a plain member of C_BaseEntity except m_clrRender,
	// which was a CNetworkColor32 -- a plain member with a change-notifier on
	// it. Nothing networks a tempent, so it is a plain member here and the four
	// `m_clrRender->` reads in c_te_legacytempents.cpp became `m_clrRender.`.
public:
	// -1 for the whole life of a tempent, and the renderer is handed it:
	// DrawStudioModel passes it to modelrender->DrawModel as the entity index.
	// CTempEnts::AddVisibleTempEntity re-asserts it every time one is made
	// visible, with the comment "Temporary entities have no corresponding
	// element in cl_entitylist".
	int index;

	int m_nBody;
	int m_nSkin;
	int m_nHitboxSet;

	int m_nRenderMode;
	int m_nRenderFX;
	color32 m_clrRender;

private:
	C_LocalTempEntity( const C_LocalTempEntity & );

	const model_t *m_pModel;

	// The owner, for the one trace that ignores it. A bare handle, so the read
	// side names no entity type -- see SetTempEntOwner above.
	CBaseHandle m_hOwnerEntity;

	// ComputeFxBlend's output, and the frame it was computed on. Stock kept the
	// pair on C_BaseEntity for the same reason: the engine's contract is
	// ComputeFxBlend once per frame and GetFxBlend as many times as the
	// renderer likes afterwards, so the answer has to be stored.
	int m_nRenderFXBlend;
	int m_nFXComputeFrame;

	Vector m_vecTempEntVelocity;
	alignas( 8 ) unsigned char unused_m_vecPrevLocalOrigin[12];

	Vector m_vecTempEntAcceleration;

	// Draw tempent as a studio model
	int DrawStudioModel( int flags );
};
