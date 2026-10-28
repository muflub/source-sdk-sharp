//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — a particle effect that is not an entity ============//
//
// WHAT THIS IS FOR, AND IT IS A LINKER PROBLEM RATHER THAN A DESIGN ONE.
//
// A VTABLE section names every inherited virtual whether or not one line of the
// file ever calls it, so one class deriving from C_BaseEntity costs ~275
// undefined references on the client for free, before the file does anything.
// `src/ref_kinds.py` measures the split: across the client's shell files 93% of
// the references to the base class are vtable and typeinfo, not calls. Retyping
// a member access cannot remove one of them. Only not deriving can.
//
// This is the base the port's client-side particle effects get INSTEAD of
// C_BaseParticleEntity. It is that class with the entity taken out: a place,
// and an orientation, which is the whole of what the executor that sits on it
// asks an entity for (lane M1-C took out the velocity and the render colour,
// which nothing read).
//
// WHO SITS ON IT: shell_managed_particles.cpp's CManagedParticleEffect, the
// executor every managed particle sim draws through (the trails, the steam
// jets, the smoke stacks and volumes, the dust, the temp entities' emitters).
//
// THE RENDERABLE IS THE EFFECT (C7 particles phase 2.3). Stock's effects were
// drawn through a CParticleEffectBinding, CParticleMgr::AddEffect's
// renderable. The old-style manager is managed now
// (SourceSharp.Client.Effects.ClientParticleManager), so the effect is a
// CDefaultClientRenderable itself: shell_managed_particles.cpp's executor
// registers it with the leaf system and answers it from what managed commits.
//
// WHAT IS *KEPT* FROM THE ENTITY, because dropping it would be a bug:
//
//  * THE ZEROING ALLOCATOR. C_BaseEntity::operator new memsets the whole block
//    to zero, and the families here silently rely on it. See
//    CTrailEffectZeroingTest.
//
// LIFETIME. Release() destroys it, once, through the latch; level shutdown
// releases every executor (shell_managed_particles.cpp's system).
//
// THE NAME. This was `CTrailEffect` in `shell_trail_effect.h` while the trails
// were the only family on it.
//
//=============================================================================//

#pragma once

#include "iclientrenderable.h"
#include "tier0/memalloc.h"
#include "mathlib/vector.h"
#include "networkvar.h"
#include "view.h"
#include "trail_lifetime.h"

//-----------------------------------------------------------------------------
// A particle effect with a place and an orientation: the whole of what the
// managed particle executor asks C_BaseEntity for.
//-----------------------------------------------------------------------------
class CShellParticleEffect : public CDefaultClientRenderable
{
public:
	DECLARE_CLASS_NOBASE( CShellParticleEffect );

	//-------------------------------------------------------------------------
	// C_BaseEntity's allocator, and it is not decoration. See the header note
	// and trail_lifetime.h: the constructors of these classes leave fields
	// untouched on the promise that the block arrived zeroed.
	//-------------------------------------------------------------------------
	SOURCESHARP_ZEROING_ALLOCATOR( MemAlloc_Alloc, MemAlloc_Free )

	//-------------------------------------------------------------------------
	// "I am going", told to whoever is holding a pointer to me.
	//
	// This exists because de-entifying cost the shell a guarantee it used to get
	// for free: its registry held a CHandle, which reads as null the moment the
	// engine destroys the entity behind it, so a stale entry announced itself.
	// A raw pointer does not. Rather than leave the `_render` shell to guess,
	// the one place that always knows -- the destructor, whichever of the two
	// routes reached it -- says so.
	//-------------------------------------------------------------------------
	typedef CTrailDestroyLatch< CShellParticleEffect >::GoneFn_t TrailGoneFn_t;

	void SetGoneCallback( TrailGoneFn_t pfnGone ) { m_Destroy.SetGoneCallback( pfnGone ); }

	CShellParticleEffect()
	{
		// Everything else arrived zeroed and is meant to stay that way.
		m_vecOrigin.Init();
		m_angAngles.Init();
	}

	virtual ~CShellParticleEffect()
	{
		// Claimed before the members go. This is the ONE claim that is allowed
		// to lose: a destructor reached through Release() is running because
		// that caller already won the claim.
		m_Destroy.Claim();
		m_Destroy.Announce( this );
	}

	//-------------------------------------------------------------------------
	// The place. Written once a frame by the `_render` shell's PreRender sweep,
	// which is what makes an effect follow the entity it belongs to.
	//-------------------------------------------------------------------------
	const Vector &GetAbsOrigin() const   { return m_vecOrigin; }
	const QAngle &GetAbsAngles() const   { return m_angAngles; }

	void SetAbsOrigin( const Vector &vecOrigin )   { m_vecOrigin  = vecOrigin; }
	void SetAbsAngles( const QAngle &angAngles )   { m_angAngles  = angAngles; }

	//-------------------------------------------------------------------------
	// The shell's destroy path. C_BaseEntity::Release() destroyed a client-only
	// entity outright and this does the same.
	//-------------------------------------------------------------------------
	void Release()
	{
		if ( m_Destroy.Claim() )
			delete this;
	}

protected:
	Vector	m_vecOrigin;
	QAngle	m_angAngles;

	// Claimed by the first of the destroy routes to arrive; every one after is
	// a no-op. Carries the registry's "it has gone" callback too, since the two
	// are the same question asked from the two ends. See trail_lifetime.h.
	CTrailDestroyLatch< CShellParticleEffect >	m_Destroy;

private:
	CShellParticleEffect( const CShellParticleEffect & );	// not defined, not accessible
};
