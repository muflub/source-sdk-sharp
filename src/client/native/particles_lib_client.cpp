//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — particles.a's names in client.so, forwarded to libss_particles.so
// SS_REASON b  an interface resolved by name at load
//
// plan_particles_so.md. particles.a and dmxloader.a left
// client.so's link for their own library (src/client/native/particles_lib). The client's
// objects still name 41 of particles.a's functions and g_pParticleSystemMgr
// (particlemgr.cpp, particles_new.cpp and the shells, unchanged): each name is
// defined here, once, as a call through ISSParticleLib with `this` passed on.
// CNewParticleEffect still derives from CParticleCollection: client.so lays the
// object out from the same header and the library runs its members.
//
// client.so links the library like any other (DT_NEEDED on its SONAME, found
// beside client.so by its RUNPATH $ORIGIN; the import library on Windows), so
// SSParticleLib_Interface resolves when client.so loads. Connect, from
// tier_connect.cpp's tier2 lookups in CHLClient::Init, hands the library the
// engine's factory and sets g_pParticleSystemMgr, before CParticleMgr::Init
// (particlemgr.cpp) first reads it.
//=============================================================================//

#include "tier0/dbg.h"
#include "particles/particles.h"
#include "ss_particles/iparticlelib.h"
#include "particles_lib_client.h"
#include "vtable_hook.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

CParticleSystemMgr *g_pParticleSystemMgr = NULL;

namespace
{

// The library's one object, from its one export.
ISSParticleLib *Lib()
{
	return SSParticleLib_Interface();
}

} // namespace

bool ParticleLib_Connect( CreateInterfaceFn appSystemFactory )
{
	g_pParticleSystemMgr = Lib()->Mgr();
	return Lib()->Connect( appSystemFactory );
}

void ParticleLib_Disconnect()
{
	Lib()->Disconnect();
}

// MgrUncacheAllParticleSystems is the LAST declaration in ISSParticleLib (the
// interface is append-only): its slot plus one is the count managed checks
// its generated layout against.
void *ParticleLib_Acquire( int32_t *pnEntries )
{
	if ( pnEntries )
		*pnEntries = VTableHook::MemberOf( &ISSParticleLib::MgrUncacheAllParticleSystems ).slot + 1;
	return Lib();
}

void ParticleLib_Counters( SSParticleLibCounters_t *pOut )
{
	Lib()->Counters( pOut );
}

// CParticleCollection ---------------------------------------------------------
void *CParticleCollection::operator new( size_t nSize ) { return Lib()->CollectionNew( nSize ); }
void CParticleCollection::operator delete( void *pData ) { Lib()->CollectionDelete( pData ); }
// The base-object constructor and destructor (C2/D2, the only variants the
// client names: CNewParticleEffect's own constructor and destructor call them)
// are plain functions under the mangled names. Written as C++ members, the
// compiler would construct and destroy every member around the forward too,
// and the library's real constructor and destructor run them already.
#if defined( _MSC_VER )
// MSVC has no asm labels, and its names for the two are ??0 / ??1: the
// linker takes each from the plain function when nothing else defines it
// (/alternatename). An MSVC constructor hands back `this`, which callers may
// use, so this one returns it.
extern "C" CParticleCollection *SSParticleLib_CollectionCtor( CParticleCollection *p ) { Lib()->CollectionConstruct( p ); return p; }
extern "C" void SSParticleLib_CollectionDtor( CParticleCollection *p ) { Lib()->CollectionDestruct( p ); }
#pragma comment( linker, "/alternatename:??0CParticleCollection@@IEAA@XZ=SSParticleLib_CollectionCtor" )
#pragma comment( linker, "/alternatename:??1CParticleCollection@@QEAA@XZ=SSParticleLib_CollectionDtor" )
#else
extern "C" void SSParticleLib_CollectionC2( CParticleCollection *p ) __asm__( "_ZN19CParticleCollectionC2Ev" );
extern "C" void SSParticleLib_CollectionD2( CParticleCollection *p ) __asm__( "_ZN19CParticleCollectionD2Ev" );
extern "C" void SSParticleLib_CollectionC2( CParticleCollection *p ) { Lib()->CollectionConstruct( p ); }
extern "C" void SSParticleLib_CollectionD2( CParticleCollection *p ) { Lib()->CollectionDestruct( p ); }
#endif
bool CParticleCollection::Init( CParticleSystemDefinition *pDef ) { return Lib()->CollectionInitDef( this, pDef ); }
bool CParticleCollection::Init( const char *pszName ) { return Lib()->CollectionInitName( this, pszName ); }
void CParticleCollection::BloatBoundsUsingControlPoint() { Lib()->CollectionBloatBoundsUsingControlPoint( this ); }
int CParticleCollection::GetHighestControlPoint() const { return Lib()->CollectionGetHighestControlPoint( this ); }
const char *CParticleCollection::GetName() const { return Lib()->CollectionGetName( this ); }
bool CParticleCollection::HasMoved() const { return Lib()->CollectionHasMoved( this ); }
bool CParticleCollection::IsFinished() { return Lib()->CollectionIsFinished( this ); }
bool CParticleCollection::IsTranslucent() const { return Lib()->CollectionIsTranslucent( this ); }
bool CParticleCollection::IsTwoPass() const { return Lib()->CollectionIsTwoPass( this ); }
void CParticleCollection::Render( IMatRenderContext *pCtx, bool bTranslucentOnly, void *pCamera )
{
	Lib()->CollectionRender( this, pCtx, bTranslucentOnly, pCamera );
}
void CParticleCollection::SetDormant( bool bDormant ) { Lib()->CollectionSetDormant( this, bDormant ); }
void CParticleCollection::Simulate( float dt, bool bBboxOnly ) { Lib()->CollectionSimulate( this, dt, bBboxOnly ); }
void CParticleCollection::StopEmission( bool bInfiniteOnly, bool bRemoveAll, bool bWakeOnStop )
{
	Lib()->CollectionStopEmission( this, bInfiniteOnly, bRemoveAll, bWakeOnStop );
}
bool CParticleCollection::UsesFullFrameBufferTexture( bool bThisFrame ) const
{
	return Lib()->CollectionUsesFullFrameBufferTexture( this, bThisFrame );
}
bool CParticleCollection::UsesPowerOfTwoFrameBufferTexture( bool bThisFrame ) const
{
	return Lib()->CollectionUsesPowerOfTwoFrameBufferTexture( this, bThisFrame );
}

// CParticleSystemDefinition ---------------------------------------------------
IMaterial *CParticleSystemDefinition::GetMaterial() const { return Lib()->DefinitionGetMaterial( this ); }
bool CParticleSystemDefinition::ShouldAlwaysPrecache() const { return Lib()->DefinitionShouldAlwaysPrecache( this ); }

// CParticleSystemMgr ----------------------------------------------------------
void CParticleSystemMgr::AddBuiltinRenderingOperators() { Lib()->MgrAddBuiltinRenderingOperators( this ); }
void CParticleSystemMgr::AddBuiltinSimulationOperators() { Lib()->MgrAddBuiltinSimulationOperators( this ); }
void CParticleSystemMgr::AddToRenderCache( CParticleCollection *pParticles ) { Lib()->MgrAddToRenderCache( this, pParticles ); }
void CParticleSystemMgr::CommitProfileInformation( bool bCommit ) { Lib()->MgrCommitProfileInformation( this, bCommit ); }
bool CParticleSystemMgr::Debug_FrameWarningNeededTestAndReset() { return Lib()->MgrDebugFrameWarningNeededTestAndReset( this ); }
void CParticleSystemMgr::DecommitTempMemory() { Lib()->MgrDecommitTempMemory( this ); }
void CParticleSystemMgr::DrawRenderCache( bool bShadowDepth ) { Lib()->MgrDrawRenderCache( this, bShadowDepth ); }
void CParticleSystemMgr::DumpProfileInformation() { Lib()->MgrDumpProfileInformation( this ); }
CParticleSystemDefinition *CParticleSystemMgr::FindParticleSystem( const char *pszName )
{
	return Lib()->MgrFindParticleSystem( this, pszName );
}
float CParticleSystemMgr::GetLastSimulationTime() const { return Lib()->MgrGetLastSimulationTime( this ); }
int CParticleSystemMgr::GetParticleSystemCount() { return Lib()->MgrGetParticleSystemCount( this ); }
const char *CParticleSystemMgr::GetParticleSystemNameFromIndex( ParticleSystemHandle_t nIndex )
{
	return Lib()->MgrGetParticleSystemNameFromIndex( this, nIndex );
}
bool CParticleSystemMgr::Init( IParticleSystemQuery *pQuery ) { return Lib()->MgrInit( this, pQuery ); }
float CParticleSystemMgr::ParticleThrottleScaling() const { return Lib()->MgrParticleThrottleScaling( this ); }
void CParticleSystemMgr::PrecacheParticleSystem( const char *pszName ) { Lib()->MgrPrecacheParticleSystem( this, pszName ); }
bool CParticleSystemMgr::ReadParticleConfigFile( const char *pszFile, bool bPrecache, bool bDecommit )
{
	return Lib()->MgrReadParticleConfigFile( this, pszFile, bPrecache, bDecommit );
}
void CParticleSystemMgr::ResetRenderCache() { Lib()->MgrResetRenderCache( this ); }
void CParticleSystemMgr::SetLastSimulationTime( float flTime ) { Lib()->MgrSetLastSimulationTime( this, flTime ); }
void CParticleSystemMgr::ShouldLoadSheets( bool bLoadSheets ) { Lib()->MgrShouldLoadSheets( this, bLoadSheets ); }
void CParticleSystemMgr::UncacheAllParticleSystems() { Lib()->MgrUncacheAllParticleSystems( this ); }
