//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — libss_particles.so's one export (plan_particles_so.md spike)
// SS_REASON b  an interface resolved by name at load
//
// client.so links the library like any other (DT_NEEDED on its SONAME, the
// import library on Windows) and takes ISSParticleLib from its one export,
// SSParticleLib_Interface. Not CreateInterface: client.so exports one of its
// own, so it could not name the library's.
//
// The library statically holds particles.a, dmxloader.a and the tier1 and
// mathlib members they import, all local (version_script.txt exports only
// SSParticleLib_Interface). Each slot of ISSParticleLib runs the particles.a
// member the client named before the split; the client's thunks pass `this`
// through.
//=============================================================================//

#include "ss_particles/iparticlelib.h"

#include <new>

#include "filesystem.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"
#include "mathlib/mathlib.h"
#include "particles/particles.h"
#include "tier1/convar.h"
#include "tier1/tier1.h"

// What particles.a imports from tier2 (it was compiled against tier2.a's
// globals). The library resolves them itself, from the factory Connect gets.
IFileSystem *g_pFullFileSystem = NULL;
IMaterialSystem *g_pMaterialSystem = NULL;
IMaterialSystem *materials = NULL;
IMaterialSystemHardwareConfig *g_pMaterialSystemHardwareConfig = NULL;

SSParticleLibCounters_t g_SSParticleLibCounters;

namespace
{

// CParticleCollection's constructor and Init are protected; a derived type
// with no members of its own reaches them (its layout is the base's).
struct CollectionAccess : public CParticleCollection
{
	CollectionAccess() : CParticleCollection() {}
	bool InitDef( CParticleSystemDefinition *pDef ) { return Init( pDef ); }
	bool InitName( const char *pszName ) { return Init( pszName ); }
	void BloatBounds() { BloatBoundsUsingControlPoint(); }
	const Vector &Min() const { return m_MinBounds; }
	const Vector &Max() const { return m_MaxBounds; }
	void SetBounds( const Vector &vMin, const Vector &vMax )
	{
		m_MinBounds = vMin;
		m_MaxBounds = vMax;
	}
};

const CollectionAccess *Access( const CParticleCollection *p )
{
	return static_cast< const CollectionAccess * >( p );
}

CollectionAccess *Access( CParticleCollection *p )
{
	return static_cast< CollectionAccess * >( p );
}

class CSSParticleLib : public ISSParticleLib
{
public:
	bool Connect( CreateInterfaceFn appSystemFactory ) override
	{
		ConnectTier1Libraries( &appSystemFactory, 1 );
		g_pFullFileSystem = (IFileSystem *)appSystemFactory( FILESYSTEM_INTERFACE_VERSION, NULL );
		g_pMaterialSystem = materials = (IMaterialSystem *)appSystemFactory( MATERIAL_SYSTEM_INTERFACE_VERSION, NULL );
		g_pMaterialSystemHardwareConfig = (IMaterialSystemHardwareConfig *)appSystemFactory(
			MATERIALSYSTEM_HARDWARECONFIG_INTERFACE_VERSION, NULL );
		if ( !g_pCVar || !g_pFullFileSystem || !g_pMaterialSystem || !g_pMaterialSystemHardwareConfig )
			return false;
		MathLib_Init( 2.2f, 2.2f, 0.0f, 2.0f );
		ConVar_Register( FCVAR_CLIENTDLL );
		return true;
	}

	void Disconnect() override
	{
		ConVar_Unregister();
		DisconnectTier1Libraries();
		g_pFullFileSystem = NULL;
		g_pMaterialSystem = materials = NULL;
		g_pMaterialSystemHardwareConfig = NULL;
	}

	CParticleSystemMgr *Mgr() override { return g_pParticleSystemMgr; }
	void Counters( SSParticleLibCounters_t *pOut ) override { *pOut = g_SSParticleLibCounters; }

	void *CollectionNew( size_t nSize ) override { return CParticleCollection::operator new( nSize ); }
	void CollectionDelete( void *p ) override { CParticleCollection::operator delete( p ); }
	void CollectionConstruct( CParticleCollection *p ) override { ::new ( (void *)p ) CollectionAccess(); }
	void CollectionDestruct( CParticleCollection *p ) override { p->~CParticleCollection(); }
	bool CollectionInitDef( CParticleCollection *p, CParticleSystemDefinition *pDef ) override
	{
		return Access( p )->InitDef( pDef );
	}
	bool CollectionInitName( CParticleCollection *p, const char *pszName ) override
	{
		return Access( p )->InitName( pszName );
	}
	void CollectionBloatBoundsUsingControlPoint( CParticleCollection *p ) override
	{
		Access( p )->BloatBounds();
	}
	int CollectionGetHighestControlPoint( const CParticleCollection *p ) override { return p->GetHighestControlPoint(); }
	const char *CollectionGetName( const CParticleCollection *p ) override { return p->GetName(); }
	bool CollectionHasMoved( const CParticleCollection *p ) override { return p->HasMoved(); }
	bool CollectionIsFinished( CParticleCollection *p ) override { return p->IsFinished(); }
	bool CollectionIsTranslucent( const CParticleCollection *p ) override { return p->IsTranslucent(); }
	bool CollectionIsTwoPass( const CParticleCollection *p ) override { return p->IsTwoPass(); }
	void CollectionRender( CParticleCollection *p, IMatRenderContext *pCtx, bool bTranslucentOnly, void *pCamera ) override
	{
		p->Render( pCtx, bTranslucentOnly, pCamera );
	}
	void CollectionSetDormant( CParticleCollection *p, bool bDormant ) override { p->SetDormant( bDormant ); }
	void CollectionSimulate( CParticleCollection *p, float dt, bool bBboxOnly ) override { p->Simulate( dt, bBboxOnly ); }
	void CollectionStopEmission( CParticleCollection *p, bool bInfiniteOnly, bool bRemoveAll, bool bWakeOnStop ) override
	{
		p->StopEmission( bInfiniteOnly, bRemoveAll, bWakeOnStop );
	}
	bool CollectionUsesFullFrameBufferTexture( const CParticleCollection *p, bool bThisFrame ) override
	{
		return p->UsesFullFrameBufferTexture( bThisFrame );
	}
	bool CollectionUsesPowerOfTwoFrameBufferTexture( const CParticleCollection *p, bool bThisFrame ) override
	{
		return p->UsesPowerOfTwoFrameBufferTexture( bThisFrame );
	}

	IMaterial *DefinitionGetMaterial( const CParticleSystemDefinition *p ) override { return p->GetMaterial(); }
	bool DefinitionShouldAlwaysPrecache( const CParticleSystemDefinition *p ) override { return p->ShouldAlwaysPrecache(); }

	void MgrAddBuiltinRenderingOperators( CParticleSystemMgr *p ) override { p->AddBuiltinRenderingOperators(); }
	void MgrAddBuiltinSimulationOperators( CParticleSystemMgr *p ) override { p->AddBuiltinSimulationOperators(); }
	void MgrAddToRenderCache( CParticleSystemMgr *p, CParticleCollection *pParticles ) override
	{
		p->AddToRenderCache( pParticles );
	}
	void MgrCommitProfileInformation( CParticleSystemMgr *p, bool bCommit ) override { p->CommitProfileInformation( bCommit ); }
	bool MgrDebugFrameWarningNeededTestAndReset( CParticleSystemMgr *p ) override
	{
		return p->Debug_FrameWarningNeededTestAndReset();
	}
	void MgrDecommitTempMemory( CParticleSystemMgr *p ) override { p->DecommitTempMemory(); }
	void MgrDrawRenderCache( CParticleSystemMgr *p, bool bShadowDepth ) override { p->DrawRenderCache( bShadowDepth ); }
	void MgrDumpProfileInformation( CParticleSystemMgr *p ) override { p->DumpProfileInformation(); }
	CParticleSystemDefinition *MgrFindParticleSystem( CParticleSystemMgr *p, const char *pszName ) override
	{
		return p->FindParticleSystem( pszName );
	}
	float MgrGetLastSimulationTime( const CParticleSystemMgr *p ) override { return p->GetLastSimulationTime(); }
	int MgrGetParticleSystemCount( CParticleSystemMgr *p ) override { return p->GetParticleSystemCount(); }
	const char *MgrGetParticleSystemNameFromIndex( CParticleSystemMgr *p, int nIndex ) override
	{
		return p->GetParticleSystemNameFromIndex( nIndex );
	}
	bool MgrInit( CParticleSystemMgr *p, IParticleSystemQuery *pQuery ) override { return p->Init( pQuery ); }
	float MgrParticleThrottleScaling( const CParticleSystemMgr *p ) override { return p->ParticleThrottleScaling(); }
	void MgrPrecacheParticleSystem( CParticleSystemMgr *p, const char *pszName ) override
	{
		p->PrecacheParticleSystem( pszName );
	}
	bool MgrReadParticleConfigFile( CParticleSystemMgr *p, const char *pszFile, bool bPrecache, bool bDecommit ) override
	{
		return p->ReadParticleConfigFile( pszFile, bPrecache, bDecommit );
	}
	void MgrResetRenderCache( CParticleSystemMgr *p ) override { p->ResetRenderCache(); }
	void MgrSetLastSimulationTime( CParticleSystemMgr *p, float flTime ) override { p->SetLastSimulationTime( flTime ); }
	void MgrShouldLoadSheets( CParticleSystemMgr *p, bool bLoadSheets ) override { p->ShouldLoadSheets( bLoadSheets ); }
	void MgrUncacheAllParticleSystems( CParticleSystemMgr *p ) override { p->UncacheAllParticleSystems(); }

	void CollectionSimulateBatch( const SSParticleSimEntry_t *pEntries, int nCount, SSParticleSimResult_t *pResults ) override
	{
		for ( int i = 0; i < nCount; i++ )
		{
			CParticleCollection *p = pEntries[i].pCollection;
			if ( pEntries[i].flDt >= 0.0f )
				p->Simulate( pEntries[i].flDt, pEntries[i].bBoundsOnly != 0 );
			SSParticleSimResult_t &r = pResults[i];
			r.bFinished = p->IsFinished() ? 1 : 0;
			r.nActiveParticles = p->m_nActiveParticles;
			r.flNextSleepTime = p->m_flNextSleepTime;
			CollectionGetBounds( p, r.vMin, r.vMax );
		}
	}
	void CollectionGetBounds( const CParticleCollection *p, float *pMin3, float *pMax3 ) override
	{
		VectorCopy( Access( p )->Min(), *(Vector *)pMin3 );
		VectorCopy( Access( p )->Max(), *(Vector *)pMax3 );
	}
	void CollectionSetBounds( CParticleCollection *p, const float *pMin3, const float *pMax3 ) override
	{
		Access( p )->SetBounds( *(const Vector *)pMin3, *(const Vector *)pMax3 );
	}
};

CSSParticleLib s_SSParticleLib;

} // namespace

SS_PARTICLELIB_API ISSParticleLib *SSParticleLib_Interface()
{
	return &s_SSParticleLib;
}
