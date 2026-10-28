//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — something that draws and is not an entity ==========//
//
// The second de-entified base in this lane, and the one for the families that
// draw THEMSELVES rather than through a particle binding.
//
// shell_particle_effect.h covers the effects whose pixels come from
// CParticleEffectBinding: the binding is a renderable in its own right,
// CParticleMgr::AddEffect registers it with the client leaf system, and the
// entity those classes derived from returned false from ShouldDraw() and drew
// nothing. Nothing there needed to be a renderable at all.
//
// C_PointWorldText is the other kind. It has no particles; it builds a mesh of
// per-glyph quads in its own DrawModel, and the ONLY thing that ever called
// that DrawModel was the client leaf system, which it reached by being an
// entity. So de-entifying it means keeping the renderable and dropping
// everything else -- which is precisely what CDefaultClientRenderable is for.
// Valve's comment on it: "This class can be used to implement default versions
// of some of the functions of IClientRenderable."
//
// WHAT THE ENTITY WAS ACTUALLY PROVIDING, item by item, because "it was an
// entity" is not a list:
//
//   InitializeAsClientEntity()  an entity index, a row in the client entity
//                               list, and a call to
//                               ClientLeafSystem()->AddRenderable(). Only the
//                               last of those was ever read on this path, and
//                               AddToLeafSystem() below is that call.
//   SetAbsOrigin/SetAbsAngles   a place. Three members here.
//                               AND THE SIDE EFFECT MATTERS: on an entity these
//                               marked the render handle dirty, so the leaf
//                               system re-linked the object into the leaves it
//                               had moved into. A plain member assignment does
//                               not, so SetAbsOrigin below tells the leaf system
//                               itself. Forgetting that is an object that draws
//                               correctly until it moves and then stops being
//                               drawn from anywhere it did not start.
//   Release()                   destroyed a client-created entity outright.
//   operator new                MEMSET THE BLOCK TO ZERO, which the classes that
//                               move onto this base rely on without saying so.
//                               See trail_lifetime.h.
//   ShouldDraw/IsTransparent    pure on CDefaultClientRenderable, so the
//                               subclass answers them, exactly as it did before.
//
// WHAT IS DELIBERATELY NOT HERE. No handle: CDefaultClientRenderable's
// SetRefEHandle/GetRefEHandle are an Assert(false) and a null dereference,
// because a thing with no entity list has nothing to hand out handles. That is
// the right answer and it is left alone -- a shell that wants to hold one of
// these holds the pointer and is told when it goes, the same arrangement
// shell_effect_registry.h describes.
//
//=============================================================================//

#pragma once

#include "iclientrenderable.h"
#include "clientleafsystem.h"
#include "mathlib/vector.h"
#include "tier0/memalloc.h"
#include "engine/IClientLeafSystem.h"
#include "mathlib/mathlib.h"
#include "tier0/platform.h"
#include "client_render_handle.h"
#include "clientsideeffects.h"
#include "collisionproperty.h"
#include "ivieweffects.h"
#include "ragdoll_shared.h"
#include "view.h"
#include "vphysics_interface.h"
#include "trail_lifetime.h"
#include "shell_leaf_link.h"

//-----------------------------------------------------------------------------
// A renderable with a place, drawn by the client leaf system, owned by whoever
// made it.
//-----------------------------------------------------------------------------
class CShellRenderable : public CDefaultClientRenderable
{
public:
	typedef CDefaultClientRenderable BaseClass;

	//-------------------------------------------------------------------------
	// C_BaseEntity's allocator. Not decoration: C_PointWorldText's constructor
	// sets eleven of its thirteen members and leaves m_localBBMin and
	// m_localBBMax alone, on the promise that the block arrived zeroed. See
	// trail_lifetime.h, and the note there on why an inline memset is not
	// enough.
	//-------------------------------------------------------------------------
	SOURCESHARP_ZEROING_ALLOCATOR( MemAlloc_Alloc, MemAlloc_Free )

	typedef CTrailDestroyLatch< CShellRenderable >::GoneFn_t GoneFn_t;

	void SetGoneCallback( GoneFn_t pfnGone ) { m_Destroy.SetGoneCallback( pfnGone ); }

	CShellRenderable()
	{
		// Everything else arrived zeroed and is meant to stay that way; these
		// two are the ones a zero is not the right answer for -- and
		// m_hRenderHandle in particular, because INVALID_CLIENT_RENDER_HANDLE is
		// what "not in the leaf system" is spelled as and it is not zero.
		m_vecOrigin.Init();
		m_angAngles.Init();
		m_hRenderHandle = INVALID_CLIENT_RENDER_HANDLE;
	}

	virtual ~CShellRenderable()
	{
		RemoveFromLeafSystem();

		// Claimed before the members go, so a second destroy route reaching us
		// finds a corpse rather than a target. This claim is the ONE that is
		// allowed to lose: a destructor reached through Release() is running
		// because that caller already won it.
		m_Destroy.Claim();
		m_Destroy.Announce( this );
	}

	//-------------------------------------------------------------------------
	// The place.
	//
	// The leaf-system notification is the whole reason these are not two plain
	// assignments -- see the header note. RenderableChanged() is a no-op on an
	// INVALID handle, so an object that has not been added yet is fine.
	//-------------------------------------------------------------------------
	const Vector &GetAbsOrigin() const { return m_vecOrigin; }
	const QAngle &GetAbsAngles() const { return m_angAngles; }

	void SetAbsOrigin( const Vector &vecOrigin )
	{
		if ( m_vecOrigin == vecOrigin )
			return;

		m_vecOrigin = vecOrigin;
		RenderableMoved();
	}

	void SetAbsAngles( const QAngle &angAngles )
	{
		if ( m_angAngles == angAngles )
			return;

		m_angAngles = angAngles;
		RenderableMoved();
	}

	//-------------------------------------------------------------------------
	// Into and out of the leaf system, which is what makes DrawModel get called.
	//-------------------------------------------------------------------------
	// The three guards are in shell_leaf_link.h, engine-free, with a test that
	// drives them -- see the note there on what each one is standing in for and
	// how each fails silently without it.
	void AddToLeafSystem( RenderGroup_t group )
	{
		ShellLeaf_Add( ClientLeafSystem(), this, group );
	}

	void RemoveFromLeafSystem()
	{
		ShellLeaf_Remove( ClientLeafSystem(), this );

		// RemoveRenderable resets the handle itself, through the renderable --
		// but it returns early without doing so when the index is not valid,
		// which its own comment says "can happen upon level shutdown". This is
		// what makes the second remove a no-op in that case too.
		m_hRenderHandle = INVALID_CLIENT_RENDER_HANDLE;
	}

	bool IsInLeafSystem() const { return m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE; }

	//-------------------------------------------------------------------------
	// C_BaseEntity::Release() destroyed a client-only entity outright, and this
	// does the same. Claimed, so that two callers cannot both delete.
	//-------------------------------------------------------------------------
	void Release()
	{
		if ( m_Destroy.Claim() )
			delete this;
	}

// IClientRenderable. The three CDefaultClientRenderable leaves pure and this can
// answer; ShouldDraw and IsTransparent stay pure, because they are decisions
// only the subclass has.
public:
	virtual const Vector &GetRenderOrigin() OVERRIDE { return m_vecOrigin; }
	virtual const QAngle &GetRenderAngles() OVERRIDE { return m_angAngles; }

	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE
	{
		// A member rather than the function-local static C_EnvelopeFX uses: two
		// of these being drawn in one frame would otherwise share one matrix,
		// and the second would overwrite the first's between the call and the
		// read.
		AngleMatrix( m_angAngles, m_vecOrigin, m_matRenderable );
		return m_matRenderable;
	}

	//-------------------------------------------------------------------------
	// The bones the engine's studio draw asks for before it transforms a vertex.
	//
	// NOT CDefaultClientRenderable's, which answers TRUE AND WRITES NOTHING:
	// the engine then skins the model with whatever its bone buffer held from
	// the previous draw -- a breakmodel gib became screen-sized translucent
	// triangles converging on the view (cs_cl_te_live bones counted 11 of 11 model
	// bone matrices unwritten). This answers what stock's C_BaseAnimating did
	// for a tempent that never animates: the model's sequence 0 at cycle 0,
	// placed at the render origin and angles, and the render transform in every
	// slot the pose did not write, so no slot the engine reads is stale.
	//
	// Defined in shell_legacy_tempents_client.cpp: the tempent is the only
	// shell renderable that draws a studio model. One without a studio model
	// gets the render transform in every slot.
	//-------------------------------------------------------------------------
	virtual bool SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones, int boneMask,
	                         float currentTime ) OVERRIDE;

protected:
	//-------------------------------------------------------------------------
	// The leaf system's copy of where we are is now wrong.
	//
	// This is C_BaseEntity::MarkRenderHandleDirty's job, and it is the one piece
	// of entity behaviour that de-entifying a RENDERABLE quietly loses: the
	// object keeps drawing, from the leaves it was first added to, for as long
	// as it lives.
	//-------------------------------------------------------------------------
	void RenderableMoved()
	{
		ShellLeaf_Moved( ClientLeafSystem(), this );
	}

	Vector		m_vecOrigin;
	QAngle		m_angAngles;
	matrix3x4_t	m_matRenderable;

	CTrailDestroyLatch< CShellRenderable >	m_Destroy;

private:
	CShellRenderable( const CShellRenderable & );	// not defined, not accessible
};
