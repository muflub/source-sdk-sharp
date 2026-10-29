//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The new-style particle effect: a renderable shell over the library's
// collection, one per started system. The library allocates and owns the
// collection, managed's NewParticleManager decides, and this carries it out.
// The interface is CNewParticleEffect's, for the members this build calls.
//
// Lifetime: Create hands back a CSmartPtr. A system the manager removes leaves
// the leaf system then, and is deleted once nothing holds it.

#pragma once

#include "iclientrenderable.h"
#include "particles/particles.h"
#include "tier1/smartptr.h"
#include "tier1/utlobjectreference.h"
#include "haptics/ihaptics.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "tier0/platform.h"
#include "collisionproperty.h"
#include "fx.h"
#include "icliententitylist.h"
#include "isaverestore.h"
#include "view.h"
#include "vphysics_interface.h"

class C_StandaloneEntity;

class CShellNewParticleEffect : public CDefaultClientRenderable
{
	DECLARE_REFERENCED_CLASS( CShellNewParticleEffect );

public:
	// The named system, allocated; the manager removes it when it finishes. A
	// name no .pcf defines gives an effect that is not IsValid, as stock's did.
	static CSmartPtr<CShellNewParticleEffect> Create( const char *pszName, const char *pszDebugName = NULL );

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
	virtual const Vector &GetRenderOrigin() OVERRIDE { return m_vSortOrigin; }

	virtual const QAngle &GetRenderAngles() OVERRIDE { return vec3_angle; }

	virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE;

	const char *GetDebugName() const { return m_pDebugName; }

	virtual void GetRenderBounds( Vector &mins, Vector &maxs ) OVERRIDE;

	virtual bool ShouldDraw() OVERRIDE { return true; }

	virtual bool IsTransparent() OVERRIDE;
	virtual bool IsTwoPass() OVERRIDE;
	virtual bool UsesPowerOfTwoFrameBufferTexture() OVERRIDE;
	virtual bool UsesFullFrameBufferTexture() OVERRIDE;
	virtual int DrawModel( int flags ) OVERRIDE;

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
	// The control points' entities, by handle. A point's object is its handle's
	// address: the library only tests it for non-NULL. A plain handle from the
	// shell's own GetRefEHandle(), because an EHANDLE set from an entity pointer
	// stores the invalid handle.
	CBaseHandle m_hControlPointOwners[MAX_PARTICLE_CONTROL_POINTS];
};

// The live effects, newest first (stock's AddToHead order).
int ShellNewParticles_Count();
CShellNewParticleEffect *ShellNewParticles_At( int nIndex );

// The new-effect half of the frame: managed decides, this carries it out.
// Called where CParticleMgr::Simulate runs.
void ShellNewParticles_Frame( float flFrameTime );

// CParticleMgr::RemoveAllNewEffects: level shutdown and the particle reload.
void ShellNewParticles_RemoveAll();
