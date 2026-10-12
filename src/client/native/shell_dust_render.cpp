//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the dust motes' brush test, and the dust puff =====//
// SS_REASON a  a vtable the engine calls through
//
// The dust MOTES are C#'s (lane c5-particles): DustVolumeSim, owned by the
// managed func_dustmotes / func_dustcloud (ClientDustBase) and drawn by the
// particle executor (shell_managed_particles.cpp, quad kind 6). It is stock's
// CDustEffect and C_Func_Dust's mote half, held bit for bit to the native
// volume this file carried until then (DustOracleTests, Fixtures/
// dust-stock.tsv.gz, recorded with tools/probes/client/
// shell_dust_render.cpp.probe.diff at 5aeeef63).
//
// What is left here:
//
//   CSourceSharpBrushCollideable and IShimParticlesClient::
//     ParticlesBrushPointContents: the motes' spawn test, carried out. Managed
//     picks the point; the engine's point test answers for the owner's brush
//     through the decorator, whose note says why the managed entity's own
//     collideable is not enough.
//
//   (FX_Dust, the dust PUFF, is C#'s too: DustPuff, see the note at the bottom.)
//=============================================================================

#include "cbase.h"
#include "shell_standalone_client.h"
#include "engine/IEngineTrace.h"
#include "engine/ivmodelinfo.h"
#include "bspflags.h"
#include "sourcesharp_entrypoints.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"



//-----------------------------------------------------------------------------
// THE BRUSH, AS A COLLIDEABLE THE POINT TEST WILL ACTUALLY ANSWER FOR.
//
// MEASURED, not reasoned about, and the reasoning was WRONG. The obvious
// arrangement -- hand GetPointContents_Collideable the managed entity's own
// collideable, since C_StandaloneEntity implements the whole ICollideable
// surface and answers GetCollisionModelIndex and GetCollisionOrigin correctly --
// returns CONTENTS_EMPTY for every point inside the brush. Probed live on
// dm_lockdown against two managed brush entities, 400 points each inside their
// own model bounds: 0 hits, and 0 at the dead centre, with a control in the same
// command finding CONTENTS_SOLID in the floor under the player's feet, so the
// instrument was working.
//
// THE REASON, corrected -- the first reading of it here was wrong, and it sent
// the next lane looking above the map for a fault that was in it. The answers
// the engine's point test depends on are GetEntityHandle (the static-prop
// case), GetSolidFlags, GetCollisionModel and GetCollisionModelIndex, and
// GetCollisionOrigin/Angles. It does not depend on GetSolid() or OBBMins/Maxs.
//
// So the answer that failed is GetSolidFlags. C_StandaloneEntity's is a hardcoded
// FSOLID_NOT_SOLID (shell_standalone_client.h:851) with FSOLID_VOLUME_CONTENTS
// clear, and the point test skips any collideable without
// FSOLID_VOLUME_CONTENTS -- an early out, before the model
// is ever looked at. That is the whole of the CONTENTS_EMPTY the probe measured.
// GetSolid() and the OBB below are answered because ICollideable declares them,
// not because this path reads them.
//
// AND A SOLID FLAG IS STILL NOT ENOUGH, which is the other half of the same
// lesson: past that check the point test returns whatever contents it
// reads out of the BRUSH ITSELF, so a dust brush compiled without CONTENTS_SOLID
// answers empty at every point with this decorator in place and everything here
// working. That was a real defect for a phase (ss_sandbox's dust brushes were
// TOOLS/TOOLSINVISIBLE, which vbsp compiles to CONTENTS_GRATE) and it is pinned
// now by DustBrushSolidityTests, in the map rather than here.
//
// Stock did not hit this because its C_Func_Dust set SetSolid( SOLID_BSP ) and
// SetCollisionBounds( mins, maxs ) on its own collision property. That is what
// this replaces, and it is the whole of what the entity was providing here: the
// three answers below, over the managed entity's own model and origin.
//
// A DECORATOR RATHER THAN A COPY, so that the model, the origin and the
// transform can only ever be the managed entity's -- a second copy of an origin
// is a second thing to keep in step, and the motes go wherever this says they
// go.
//-----------------------------------------------------------------------------
class CSourceSharpBrushCollideable : public ICollideable
{
public:
	CSourceSharpBrushCollideable() : m_pOwner( NULL )
	{
		m_vecMins.Init();
		m_vecMaxs.Init();
	}

	void Bind( C_StandaloneEntity *pOwner, const Vector &vecMins, const Vector &vecMaxs )
	{
		m_pOwner  = pOwner;
		m_vecMins = vecMins;
		m_vecMaxs = vecMaxs;
	}

	bool IsBound() const { return m_pOwner != NULL; }

	// The three answers that are OURS. Everything else is the entity's.
	virtual SolidType_t GetSolid() const OVERRIDE { return SOLID_BSP; }
	virtual const Vector &OBBMins() const OVERRIDE { return m_vecMins; }
	virtual const Vector &OBBMaxs() const OVERRIDE { return m_vecMaxs; }
	virtual const Vector &OBBMinsPreScaled() const OVERRIDE { return m_vecMins; }
	virtual const Vector &OBBMaxsPreScaled() const OVERRIDE { return m_vecMaxs; }

	// NOT SOLID, and that is not a contradiction with SOLID_BSP above: stock's
	// dust set exactly this pair. SOLID_BSP says "my shape is a brush" and
	// FSOLID_NOT_SOLID says "do not stop anything moving through me", which is
	// what a volume of dust is. FSOLID_VOLUME_CONTENTS is stock's too and is the
	// flag that means "my contents are worth asking about".
	virtual int GetSolidFlags() const OVERRIDE { return FSOLID_NOT_SOLID | FSOLID_VOLUME_CONTENTS; }

	// The rest, straight through to the managed entity.
	virtual IHandleEntity *GetEntityHandle() OVERRIDE { return m_pOwner->GetEntityHandle(); }
	virtual void WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const OVERRIDE
		{ m_pOwner->WorldSpaceTriggerBounds( pMins, pMaxs ); }
	virtual bool TestCollision( const Ray_t &ray, unsigned int mask, trace_t &tr ) OVERRIDE
		{ return m_pOwner->TestCollision( ray, mask, tr ); }
	virtual bool TestHitboxes( const Ray_t &ray, unsigned int mask, trace_t &tr ) OVERRIDE
		{ return m_pOwner->TestHitboxes( ray, mask, tr ); }
	virtual int GetCollisionModelIndex() OVERRIDE { return m_pOwner->GetCollisionModelIndex(); }
	virtual const model_t *GetCollisionModel() OVERRIDE { return m_pOwner->GetCollisionModel(); }
	virtual const Vector &GetCollisionOrigin() const OVERRIDE { return m_pOwner->GetCollisionOrigin(); }
	virtual const QAngle &GetCollisionAngles() const OVERRIDE { return m_pOwner->GetCollisionAngles(); }
	virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE
		{ return m_pOwner->CollisionToWorldTransform(); }
	virtual IClientUnknown *GetIClientUnknown() OVERRIDE { return NULL; }
	virtual int GetCollisionGroup() const OVERRIDE { return COLLISION_GROUP_NONE; }
	virtual void WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs ) OVERRIDE
		{ m_pOwner->WorldSpaceSurroundingBounds( pMins, pMaxs ); }
	virtual bool ShouldTouchTrigger( int ) const OVERRIDE { return false; }
	virtual const matrix3x4_t *GetRootParentToWorldTransform() const OVERRIDE { return NULL; }

private:
	C_StandaloneEntity *m_pOwner;
	Vector m_vecMins;
	Vector m_vecMaxs;
};

//-----------------------------------------------------------------------------
// IShimParticlesClient::ParticlesBrushPointContents: the managed dust motes'
// spawn test (DustVolumeSim.AttemptSpawn decides the point; this carries the
// question out). The owner's brush, as the collideable above answers for it
// over the model's own bounds, asked of the engine's point test: stock's
// enginetrace->GetPointContents_Collideable on its SOLID_BSP dust volume. 0
// for an owner or model that is not there, which no brush is solid at.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ParticlesBrushPointContents( uint32_t hOwner, const float *pPointXYZ )
{
	C_StandaloneEntity *pOwner = SourceSharp_StandaloneFromHandle( hOwner );
	if ( !pOwner || !pPointXYZ )
		return 0;

	const model_t *pModel = pOwner->GetModel();
	if ( !pModel )
		return 0;

	Vector vecMins, vecMaxs;
	modelinfo->GetModelBounds( pModel, vecMins, vecMaxs );

	CSourceSharpBrushCollideable brush;
	brush.Bind( pOwner, vecMins, vecMaxs );
	return enginetrace->GetPointContents_Collideable( &brush, Vector( pPointXYZ[0], pPointXYZ[1], pPointXYZ[2] ) );
}

//=============================================================================//
// FX_DUST, the dust PUFF, left with lane c5-particles: DustPuff (C#), held bit
// for bit to this file's copy (DustPuffOracleTests, Fixtures/dustpuff-stock.tsv.gz),
// started undrawn as stock's particles_ez singleton left it (ClientDustPuffs).
//=============================================================================//
