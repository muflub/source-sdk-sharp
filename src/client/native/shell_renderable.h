//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// A base for things that draw themselves and are not entities: a
// CDefaultClientRenderable with a place, added to the client leaf system.
//
// SetAbsOrigin and SetAbsAngles tell the leaf system the object moved; without
// that it keeps drawing from the leaves it was first added to. It keeps
// C_BaseEntity's zeroing allocator, which subclasses rely on (see
// trail_lifetime.h). There is no handle: CDefaultClientRenderable's
// SetRefEHandle/GetRefEHandle are not usable, so holders keep the pointer and
// are told when it goes.

#pragma once

#include "clientsideeffects.h"

#include "view.h"
#include "vphysics_interface.h"
#include "trail_lifetime.h"
#include "shell_leaf_link.h"

// A renderable with a place, drawn by the client leaf system, owned by whoever
// made it.
class CShellRenderable : public CDefaultClientRenderable
{
public:
	typedef CDefaultClientRenderable BaseClass;

	// Zeroing allocator: subclass constructors leave members untouched on the
	// promise that the block arrived zeroed. See trail_lifetime.h.
	SOURCESHARP_ZEROING_ALLOCATOR( MemAlloc_Alloc, MemAlloc_Free )

	typedef CTrailDestroyLatch<CShellRenderable>::GoneFn_t GoneFn_t;

	void SetGoneCallback( GoneFn_t pfnGone ) { m_Destroy.SetGoneCallback( pfnGone ); }

	CShellRenderable()
	{
		// The members zero is wrong for: INVALID_CLIENT_RENDER_HANDLE is not zero.
		m_vecOrigin.Init();
		m_angAngles.Init();
		m_hRenderHandle = INVALID_CLIENT_RENDER_HANDLE;
	}

	virtual ~CShellRenderable()
	{
		RemoveFromLeafSystem();

		// Claimed before the members go. A destructor reached through Release()
		// is running because that caller already won the claim.
		m_Destroy.Claim();
		m_Destroy.Announce( this );
	}

	// The place. The leaf-system notification is why these are not plain
	// assignments; RenderableChanged() is a no-op on an invalid handle.
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

	// Into and out of the leaf system. The guards are in shell_leaf_link.h.
	void AddToLeafSystem( RenderGroup_t group ) { ShellLeaf_Add( ClientLeafSystem(), this, group ); }

	void RemoveFromLeafSystem()
	{
		ShellLeaf_Remove( ClientLeafSystem(), this );

		// RemoveRenderable returns early without resetting the handle when the
		// index is not valid (it can happen at level shutdown); this makes the
		// second remove a no-op then too.
		m_hRenderHandle = INVALID_CLIENT_RENDER_HANDLE;
	}

	bool IsInLeafSystem() const { return m_hRenderHandle != INVALID_CLIENT_RENDER_HANDLE; }

	// Destroys the object outright, as C_BaseEntity::Release() did for a
	// client-only entity. Claimed, so two callers cannot both delete.
	void Release()
	{
		if ( m_Destroy.Claim() )
			delete this;
	}

	// IClientRenderable. ShouldDraw and IsTransparent stay pure: only the subclass
	// can decide them.
public:
	virtual const Vector &GetRenderOrigin() OVERRIDE { return m_vecOrigin; }

	virtual const QAngle &GetRenderAngles() OVERRIDE { return m_angAngles; }

	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE
	{
		// A member, not a function-local static: two of these drawn in one frame
		// would otherwise share one matrix.
		AngleMatrix( m_angAngles, m_vecOrigin, m_matRenderable );
		return m_matRenderable;
	}

	// The bones the engine's studio draw reads. CDefaultClientRenderable's
	// answers true and writes nothing, leaving stale bones from the previous
	// draw. This answers the model's sequence 0 at cycle 0 at the render origin,
	// and the render transform in every slot the pose did not write. Defined in
	// shell_legacy_tempents_client.cpp.
	virtual bool SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones, int boneMask, float currentTime ) OVERRIDE;

protected:
	// Tells the leaf system its copy of where this is is stale.
	void RenderableMoved() { ShellLeaf_Moved( ClientLeafSystem(), this ); }

	Vector m_vecOrigin;
	QAngle m_angAngles;
	matrix3x4_t m_matRenderable;

	CTrailDestroyLatch<CShellRenderable> m_Destroy;

private:
	CShellRenderable( const CShellRenderable & ); // not defined, not accessible
};
