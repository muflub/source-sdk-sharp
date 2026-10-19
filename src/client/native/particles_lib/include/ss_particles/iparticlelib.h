//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The one interface libss_particles.so exports, through SSParticleLib_Interface
// below (plan_particles_so.md). client.so's forwards call it, and managed calls the same object, acquired through the
// client's internal factory (shell_internalfactory.cpp), through its forwarders
// (SSFwd_ParticleLib, every method, tools/engine-forwarders). APPEND-ONLY BY RULE:
// new methods at the end, nothing inserted or reordered; the last declaration
// (CollectionSetBounds) is what fwd_steam_test holds the table's count to.
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
#include <stdint.h>

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

// One collection's step in a CollectionSimulateBatch (C7 particles phase 3):
// managed's new-effect manager decides which collections step, for how long
// and whether for their bounds only; the batch carries that out and answers
// what the manager's next decisions read. 16 bytes: managed builds these
// (NewParticleAbiParityTests).
struct SSParticleSimEntry_t
{
	CParticleCollection *pCollection;
	float flDt;              // the seconds to simulate; negative: no step, the answers only
	int32_t bBoundsOnly;     // Simulate's updateBboxOnly
};

struct SSParticleSimResult_t
{
	int32_t bFinished;       // IsFinished after the step
	int32_t nActiveParticles;
	float flNextSleepTime;
	float vMin[3];           // the collection's bounds after the step
	float vMax[3];
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

	// C7 particles phase 3: the new effects' frame, called by managed (the
	// bound layout ParticleLibVtbl). One batch steps every listed collection,
	// single-threaded (the phase 3 spike's measurement, docs/todo.md); the
	// bounds pair serves the first box (CNewParticleEffect::RecalculateBoundingBox,
	// decided by managed) and the shell's GetRenderBounds.
	virtual void CollectionSimulateBatch( const SSParticleSimEntry_t *pEntries, int nCount, SSParticleSimResult_t *pResults ) = 0;
	virtual void CollectionGetBounds( const CParticleCollection *p, float *pMin3, float *pMax3 ) = 0;
	virtual void CollectionSetBounds( CParticleCollection *p, const float *pMin3, const float *pMax3 ) = 0;
};

// The library's one export. client.so links the library (DT_NEEDED on its
// SONAME; the import library on Windows), so this resolves when client.so loads.
// Exported where the library is built (SS_PARTICLELIB_BUILD, its CMakeLists),
// a plain C declaration everywhere else.
#if defined( SS_PARTICLELIB_BUILD )
	#define SS_PARTICLELIB_API DLL_EXPORT
#else
	#define SS_PARTICLELIB_API extern "C"
#endif
SS_PARTICLELIB_API ISSParticleLib *SSParticleLib_Interface();

#endif // SS_PARTICLES_IPARTICLELIB_H
