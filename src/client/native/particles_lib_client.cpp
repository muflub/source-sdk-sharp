//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON b  an interface resolved by name at load
// particles.a's names in client.so, each defined as a call through the
// library's ISSParticleLib with `this` passed on. CNewParticleEffect still
// derives from CParticleCollection: client.so lays the object out from the
// same header and the library runs its members. Connect, from CHLClient::Init,
// sets g_pParticleSystemMgr before CParticleMgr::Init first reads it.

#include "particles/particles.h"
#include "tier1/interface.h"
#include "ss_particles/iparticlelib.h"
#include "particles_lib_client.h"

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

// The library's object, for the internal factory.
void *ParticleLib_Acquire()
{
	return Lib();
}

void ParticleLib_Counters( SSParticleLibCounters_t *pOut )
{
	Lib()->Counters( pOut );
}

void *CParticleCollection::operator new( size_t nSize ) { return Lib()->CollectionNew( nSize ); }
void CParticleCollection::operator delete( void *pData ) { Lib()->CollectionDelete( pData ); }
// The base-object constructor and destructor (C2/D2) are plain functions under
// the mangled names: written as C++ members, the compiler would also construct
// and destroy every member, which the library's real ones already do.
#if defined( _MSC_VER )
// MSVC has no asm labels: /alternatename maps ??0 / ??1 to the plain
// functions. An MSVC constructor returns `this`, which callers may use.
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

IMaterial *CParticleSystemDefinition::GetMaterial() const { return Lib()->DefinitionGetMaterial( this ); }
bool CParticleSystemDefinition::ShouldAlwaysPrecache() const { return Lib()->DefinitionShouldAlwaysPrecache( this ); }

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
