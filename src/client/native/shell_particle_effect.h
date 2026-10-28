//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The base for client-side particle effects that are not entities: a place and
// an orientation, a CDefaultClientRenderable the managed particle executor
// registers with the leaf system. Not deriving from C_BaseEntity keeps its
// vtable's undefined references out of the link.
//
// It keeps C_BaseEntity's zeroing allocator: the families here rely on it.
// Release() destroys it once, through the latch.

#pragma once

#include "iclientrenderable.h"
#include "tier0/memalloc.h"
#include "mathlib/vector.h"
#include "networkvar.h"
#include "view.h"
#include "trail_lifetime.h"

// A particle effect with a place and an orientation.
class CShellParticleEffect : public CDefaultClientRenderable
{
public:
	DECLARE_CLASS_NOBASE( CShellParticleEffect );

	// Zeroing allocator: constructors leave fields untouched on the promise
	// that the block arrived zeroed.
	SOURCESHARP_ZEROING_ALLOCATOR( MemAlloc_Alloc, MemAlloc_Free )

	// Tells whoever holds a raw pointer to this effect that it is going; called
	// from the destructor, whichever route reached it.
	typedef CTrailDestroyLatch<CShellParticleEffect>::GoneFn_t TrailGoneFn_t;

	void SetGoneCallback( TrailGoneFn_t pfnGone ) { m_Destroy.SetGoneCallback( pfnGone ); }

	CShellParticleEffect()
	{
		// Everything else arrived zeroed and is meant to stay that way.
		m_vecOrigin.Init();
		m_angAngles.Init();
	}

	virtual ~CShellParticleEffect()
	{
		// Claimed before the members go. A destructor reached through Release()
		// is running because that caller already won the claim.
		m_Destroy.Claim();
		m_Destroy.Announce( this );
	}

	// The place, written once a frame by the shell's PreRender sweep.
	const Vector &GetAbsOrigin() const { return m_vecOrigin; }

	const QAngle &GetAbsAngles() const { return m_angAngles; }

	void SetAbsOrigin( const Vector &vecOrigin ) { m_vecOrigin = vecOrigin; }

	void SetAbsAngles( const QAngle &angAngles ) { m_angAngles = angAngles; }

	// The shell's destroy path, as C_BaseEntity::Release() was for a client-only entity.
	void Release()
	{
		if ( m_Destroy.Claim() )
			delete this;
	}

protected:
	Vector m_vecOrigin;
	QAngle m_angAngles;

	// Claimed by the first destroy route to arrive; every one after is a no-op.
	// Also carries the registry's "it has gone" callback. See trail_lifetime.h.
	CTrailDestroyLatch<CShellParticleEffect> m_Destroy;

private:
	CShellParticleEffect( const CShellParticleEffect & ); // not defined, not accessible
};
