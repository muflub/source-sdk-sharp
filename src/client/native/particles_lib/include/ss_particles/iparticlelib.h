//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The one interface libss_particles.so exports (plan_particles_so.md). client.so's
// forwards call it, and managed binds the same object through the client's
// internal factory (shell_internalfactory.cpp) with the layout tools/vtable-layout
// generates from this header (ParticleLibVtbl). APPEND-ONLY BY RULE: new methods
// at the end, nothing inserted or reordered; MgrUncacheAllParticleSystems is the
// count anchor until something is appended after it (particles_lib_client.cpp).
// NO VIRTUAL DESTRUCTOR: the object is the library's singleton, never deleted.
//
// Every slot is one function client.so's objects named in particles.a before
// the split (nm -u over the client link edge intersected with particles.a's
// definitions: 41 functions and g_pParticleSystemMgr at 53676f29a). client.so
// defines each of those names itself as a thunk through this table
// (src/client/native/particles_lib_client.cpp), so the Valve TUs that call
// them (particlemgr.cpp, particles_new.cpp) compile unchanged.
//=============================================================================//

#ifndef SS_PARTICLES_IPARTICLELIB_H
#define SS_PARTICLES_IPARTICLELIB_H

#include <stddef.h>

#include "tier1/interface.h"

class CParticleCollection;
class CParticleSystemDefinition;
class CParticleSystemMgr;
class IMatRenderContext;
class IMaterial;
class IParticleSystemQuery;

#define SS_PARTICLE_LIB_INTERFACE_VERSION "SSParticleLib001"

// The stand-ins' counters (what particles.a asked tier2 for), read back so the
// client's effect ledger can report them as it did when they lived in client.so.
struct SSParticleLibCounters_t
{
	int nBeamStrips;
	int nWireframeSpheres;
	int nWireframeBoxes;
};

class ISSParticleLib
{
public:
	// Connects the library's own tier1/tier2 copies to the engine's
	// interfaces, registers the library's ConVars with the engine's ICvar and
	// initialises its mathlib. Disconnect undoes the ConVar registration.
	virtual bool Connect( CreateInterfaceFn appSystemFactory ) = 0;
	virtual void Disconnect() = 0;
	virtual CParticleSystemMgr *Mgr() = 0;
	virtual void Counters( SSParticleLibCounters_t *pOut ) = 0;

	// CParticleCollection (no virtuals; client.so derives CNewParticleEffect
	// from it, so its storage is laid out by client.so and its members run here)
	virtual void *CollectionNew( size_t nSize ) = 0;
	virtual void CollectionDelete( void *p ) = 0;
	virtual void CollectionConstruct( CParticleCollection *p ) = 0;
	virtual void CollectionDestruct( CParticleCollection *p ) = 0;
	virtual bool CollectionInitDef( CParticleCollection *p, CParticleSystemDefinition *pDef ) = 0;
	virtual bool CollectionInitName( CParticleCollection *p, const char *pszName ) = 0;
	virtual void CollectionBloatBoundsUsingControlPoint( CParticleCollection *p ) = 0;
	virtual int CollectionGetHighestControlPoint( const CParticleCollection *p ) = 0;
	virtual const char *CollectionGetName( const CParticleCollection *p ) = 0;
	virtual bool CollectionHasMoved( const CParticleCollection *p ) = 0;
	virtual bool CollectionIsFinished( CParticleCollection *p ) = 0;
	virtual bool CollectionIsTranslucent( const CParticleCollection *p ) = 0;
	virtual bool CollectionIsTwoPass( const CParticleCollection *p ) = 0;
	virtual void CollectionRender( CParticleCollection *p, IMatRenderContext *pCtx, bool bTranslucentOnly, void *pCamera ) = 0;
	virtual void CollectionSetDormant( CParticleCollection *p, bool bDormant ) = 0;
	virtual void CollectionSimulate( CParticleCollection *p, float dt, bool bBboxOnly ) = 0;
	virtual void CollectionStopEmission( CParticleCollection *p, bool bInfiniteOnly, bool bRemoveAll, bool bWakeOnStop ) = 0;
	virtual bool CollectionUsesFullFrameBufferTexture( const CParticleCollection *p, bool bThisFrame ) = 0;
	virtual bool CollectionUsesPowerOfTwoFrameBufferTexture( const CParticleCollection *p, bool bThisFrame ) = 0;

	// CParticleSystemDefinition
	virtual IMaterial *DefinitionGetMaterial( const CParticleSystemDefinition *p ) = 0;
	virtual bool DefinitionShouldAlwaysPrecache( const CParticleSystemDefinition *p ) = 0;

	// CParticleSystemMgr
	virtual void MgrAddBuiltinRenderingOperators( CParticleSystemMgr *p ) = 0;
	virtual void MgrAddBuiltinSimulationOperators( CParticleSystemMgr *p ) = 0;
	virtual void MgrAddToRenderCache( CParticleSystemMgr *p, CParticleCollection *pParticles ) = 0;
	virtual void MgrCommitProfileInformation( CParticleSystemMgr *p, bool bCommit ) = 0;
	virtual bool MgrDebugFrameWarningNeededTestAndReset( CParticleSystemMgr *p ) = 0;
	virtual void MgrDecommitTempMemory( CParticleSystemMgr *p ) = 0;
	virtual void MgrDrawRenderCache( CParticleSystemMgr *p, bool bShadowDepth ) = 0;
	virtual void MgrDumpProfileInformation( CParticleSystemMgr *p ) = 0;
	virtual CParticleSystemDefinition *MgrFindParticleSystem( CParticleSystemMgr *p, const char *pszName ) = 0;
	virtual float MgrGetLastSimulationTime( const CParticleSystemMgr *p ) = 0;
	virtual int MgrGetParticleSystemCount( CParticleSystemMgr *p ) = 0;
	virtual const char *MgrGetParticleSystemNameFromIndex( CParticleSystemMgr *p, int nIndex ) = 0;
	virtual bool MgrInit( CParticleSystemMgr *p, IParticleSystemQuery *pQuery ) = 0;
	virtual float MgrParticleThrottleScaling( const CParticleSystemMgr *p ) = 0;
	virtual void MgrPrecacheParticleSystem( CParticleSystemMgr *p, const char *pszName ) = 0;
	virtual bool MgrReadParticleConfigFile( CParticleSystemMgr *p, const char *pszFile, bool bPrecache, bool bDecommit ) = 0;
	virtual void MgrResetRenderCache( CParticleSystemMgr *p ) = 0;
	virtual void MgrSetLastSimulationTime( CParticleSystemMgr *p, float flTime ) = 0;
	virtual void MgrShouldLoadSheets( CParticleSystemMgr *p, bool bLoadSheets ) = 0;
	virtual void MgrUncacheAllParticleSystems( CParticleSystemMgr *p ) = 0;
};

#endif // SS_PARTICLES_IPARTICLELIB_H
