//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the new-style particle effect, a shell over the library's collection
//
// C7 particles phase 3. Stock's CNewParticleEffect (particles_new.h) derived
// from CParticleCollection, so client.so laid out the library's object and
// CParticleMgr (particlemgr.cpp) decided its frame. Here the library allocates
// and owns the collection (libss_particles.so, through particles.a's names in
// particles_lib_client.cpp), the decisions are managed's
// (SourceSharp.Client.Effects.NewParticleManager), and this is the renderable
// the leaf system draws, which carries them out: one per started system.
//
// The interface is CNewParticleEffect's, for the members this build calls: the
// impacts' one-shots (shell_impact_effects.cpp) and the managed entities'
// systems (shell_managed_particles.cpp) start and steer these exactly as they
// did the stock class.
//
// LIFETIME. Create hands back a CSmartPtr. A system the manager removes (it
// finished, or Release found it finished) leaves the leaf system then, and is
// deleted once nothing holds it; stock deleted it at once, whoever held it.
//=============================================================================//

#ifndef SHELL_NEW_PARTICLES_H
#define SHELL_NEW_PARTICLES_H
#ifdef _WIN32
#pragma once
#endif

#include "iclientrenderable.h"
#include "particles/particles.h"
#include "smartptr.h"
#include "tier1/utlobjectreference.h"

class C_StandaloneEntity;

class CShellNewParticleEffect : public CDefaultClientRenderable
{
	DECLARE_REFERENCED_CLASS( CShellNewParticleEffect );

public:
	// CNewParticleEffect::Create: the named system, its debug name, allocated
	// (removed by the manager when it finishes). A name no .pcf defines gives
	// an effect that is not IsValid, as stock's did.
	static CSmartPtr< CShellNewParticleEffect > Create( const char *pszName, const char *pszDebugName = NULL );

	bool IsValid() const { return m_pCollection && m_pCollection->IsValid(); }
	const char *GetEffectName() const { return m_pCollection ? m_pCollection->GetName() : ""; }
	CParticleCollection *Collection() const { return m_pCollection; }
	uint32_t Id() const { return m_nId; }

	// The definition reads this control point (CParticleSystemDefinition::ReadsControlPoint).
	bool DefinitionReadsControlPoint( int nPoint ) const;

	void SetSortOrigin( const Vector &vSortOrigin ) { m_vSortOrigin = vSortOrigin; }
	const Vector &GetSortOrigin() const { return m_vSortOrigin; }

	void SetControlPoint( int nWhichPoint, const Vector &v );
	void SetControlPointOrientation( int nWhichPoint, const Vector &forward, const Vector &right, const Vector &up );
	void SetControlPointEntity( int nWhichPoint, C_StandaloneEntity *pEntity );
	C_StandaloneEntity *GetControlPointEntity( int nWhichPoint ) const;
	void StopEmission( bool bInfiniteOnly = false, bool bRemoveAllParticles = false, bool bWakeOnStop = false );

	// The cull replacement EarlyRetireParticleSystems asked for: this one's
	// particles removed, and the named system started at its control points.
	CShellNewParticleEffect *ReplaceWith( const char *pszName );

	// CSmartPtr's (CRefCountAccessor).
	void AddRef();
	void Release();

	// IClientRenderable, answered as CNewParticleEffect did.
	virtual const Vector &GetRenderOrigin( void ) OVERRIDE { return m_vSortOrigin; }
	virtual const QAngle &GetRenderAngles( void ) OVERRIDE { return vec3_angle; }
	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE;
	const char *GetDebugName() const { return m_pDebugName; }
	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE;
	virtual bool ShouldDraw( void ) OVERRIDE { return true; }
	virtual bool IsTransparent( void ) OVERRIDE;
	virtual bool IsTwoPass( void ) OVERRIDE;
	virtual bool UsesPowerOfTwoFrameBufferTexture( void ) OVERRIDE;
	virtual bool UsesFullFrameBufferTexture( void ) OVERRIDE;
	virtual int DrawModel( int flags ) OVERRIDE;

	// The manager's bookkeeping (shell_new_particles_client.cpp).
	bool IsReleasedSinceFrame() const { return m_bReleasedSinceFrame; }
	bool IsAddedSinceFrame() const { return m_bAddedSinceFrame; }
	void ClearFrameFlags() { m_bReleasedSinceFrame = m_bAddedSinceFrame = false; }
	int RefCount() const { return m_nRefCount; }
	void Remove();

private:
	CShellNewParticleEffect( CParticleCollection *pCollection, const char *pszDebugName );
	virtual ~CShellNewParticleEffect();
	CShellNewParticleEffect( const CShellNewParticleEffect & ); // not defined

	CParticleCollection *m_pCollection;
	const char *m_pDebugName;
	uint32_t m_nId;
	int m_nRefCount;
	bool m_bRemoved;
	bool m_bReleasedSinceFrame;
	bool m_bAddedSinceFrame;
	Vector m_vSortOrigin;
	matrix3x4_t m_RenderTransform;
	// The control points' entities, by handle as stock's were. A point's
	// object is its handle's address, as stock's was: the library and the
	// particle query test it only for non-NULL. A plain handle, from the
	// shell's own GetRefEHandle(): an EHANDLE set from an entity pointer goes
	// through networkvar.h's refusal and stores the invalid handle.
	CBaseHandle m_hControlPointOwners[ MAX_PARTICLE_CONTROL_POINTS ];
};

// The live effects, newest first (stock's AddToHead order).
int ShellNewParticles_Count();
CShellNewParticleEffect *ShellNewParticles_At( int nIndex );

// UpdateAllEffects' new-effect half: the frame managed decides (NewEffectsFrame),
// and what it commits, carried out. Called where CParticleMgr::Simulate runs.
void ShellNewParticles_Frame( float flFrameTime );

// CParticleMgr::RemoveAllNewEffects: level shutdown and the particle reload.
void ShellNewParticles_RemoveAll();

#endif // SHELL_NEW_PARTICLES_H
