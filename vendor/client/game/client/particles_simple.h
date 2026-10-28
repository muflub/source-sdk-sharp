//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#include "particlesphererenderer.h"
#include "smartptr.h"

// CParticleEffect is the base class that you can derive from to make a particle effect.
// These can be used two ways:
// 1. Allocate a CParticleEffect-based object using the class's static Create() function. This gives
//    you back a smart pointer that handles the reference counting for you.
// 2. Contain a CParticleEffect object in your class.

class CParticleEffect : public IParticleEffect
{
public:
	DECLARE_CLASS_NOBASE( CParticleEffect );

	friend class CRefCountAccessor;

	const char *GetEffectName();

// IParticleEffect overrides
public:

	virtual void				SetParticleCullRadius( float radius );
	virtual void				NotifyRemove();
	virtual const Vector &		GetSortOrigin();
	virtual void				NotifyDestroyParticle( Particle* pParticle );
	virtual void				Update( float flTimeDelta );

	virtual bool				ShouldSimulate() const { return m_bSimulate; }
	virtual void				SetShouldSimulate( bool bSim ) { m_bSimulate = bSim; }

protected:
								CParticleEffect( const char *pDebugName );
	virtual						~CParticleEffect();

	enum
	{
		FLAG_ALLOCATED = (1<<1),	// Most of the CParticleEffects are dynamically allocated but
								// some are member variables of a class. If they're member variables.
		FLAG_DONT_REMOVE = (1<<2),
	};

	// Used to track down bugs.
	alignas( 8 ) unsigned char unused_m_pDebugName[ 312 ];

	bool						m_bSimulate;
	int							m_nToolParticleEffectId;

private:
	// Update the reference count.
	void						AddRef();
	void						Release();
	
	int							m_RefCount;		// When this goes to zero and the effect has no more active
												// particles, (and it's dynamically allocated), it will delete itself.

	CParticleEffect( const CParticleEffect & ); // not defined, not accessible
};

class SimpleParticle : public Particle
{
public:
	SimpleParticle() : m_iFlags(0) {}

	// AddSimpleParticle automatically initializes these fields.
	Vector		m_vecVelocity;
	float		m_flRoll;
	float		m_flDieTime;	// How long it lives for.
	float		m_flLifetime;	// How long it has been alive for so far.
	unsigned char	m_uchColor[3];
	unsigned char	m_uchStartAlpha;
	unsigned char	m_uchEndAlpha;
	unsigned char	m_uchStartSize;
	unsigned char	m_uchEndSize;
	unsigned char 	m_iFlags;	// See SimpleParticleFlag_t above
	float		m_flRollDelta;
};

// CSimpleEmitter implements a common way to simulate and render particles.
// Effects can add particles to the particle manager and point at CSimpleEmitter
// for the effect so they don't have to implement the simulation code. It simulates
// velocity, and fades their alpha from invisible to solid and back to invisible over their lifetime.
// Particles you add using this effect must use the class CParticleSimple::Particle.
class CSimpleEmitter : public CParticleEffect
{
// IParticleEffect overrides.
public:

	DECLARE_CLASS( CSimpleEmitter, CParticleEffect );

	virtual void	SimulateParticles( CParticleSimulateIterator *pIterator );
	virtual void	RenderParticles( CParticleRenderIterator *pIterator );

// Overridables for variants like CEmberEffect.
protected:
					CSimpleEmitter( const char *pDebugName = NULL );
	virtual			~CSimpleEmitter();

	virtual	float	UpdateAlpha( const SimpleParticle *pParticle );
	virtual float	UpdateScale( const SimpleParticle *pParticle );
	virtual	float	UpdateRoll( SimpleParticle *pParticle, float timeDelta );
	virtual	void	UpdateVelocity( SimpleParticle *pParticle, float timeDelta );
	virtual Vector	UpdateColor( const SimpleParticle *pParticle );

	float			m_flNearClipMin;
	float			m_flNearClipMax;

private:
	CSimpleEmitter( const CSimpleEmitter & ); // not defined, not accessible
};

// EmberEffect

// FireSmokeEffect

// CFireParticle
