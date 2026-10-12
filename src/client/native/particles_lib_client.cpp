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
// The library is dlopened RTLD_LOCAL from beside client.so (the client's
// RUNPATH is $ORIGIN) when client.so loads, so g_pParticleSystemMgr is set
// before any client code runs; Connect, from tier_connect.cpp's tier2
// lookups in CHLClient::Init, hands it the engine's factory.
//=============================================================================//

#include "tier0/dbg.h"
#include "particles/particles.h"
#include "ss_particles/iparticlelib.h"
#include "particles_lib_client.h"
#include "vtable_hook.h"

#if defined( _WIN32 )
	#ifndef WIN32_LEAN_AND_MEAN
		#define WIN32_LEAN_AND_MEAN
	#endif
	#include <windows.h>
#else
	#include <dlfcn.h>
#endif

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

CParticleSystemMgr *g_pParticleSystemMgr = NULL;

namespace
{

// The library's one object. Set once, when client.so loads, and never again:
// the library is never unloaded (nothing dlcloses it), so neither the handle
// nor anything else about the load needs keeping.
ISSParticleLib *g_pLib = NULL;

ISSParticleLib *Lib()
{
	return g_pLib;
}

// The library's file name beside client.so, as the platform spells it.
#if defined( _WIN32 )
	#define SS_PARTICLE_LIB "ss_particles.dll"
#elif defined( __APPLE__ )
	#define SS_PARTICLE_LIB "libss_particles.dylib"
#else
	#define SS_PARTICLE_LIB "libss_particles.so"
#endif

// Runs when client.so is loaded, before any client code can name a particle
// function, so g_pParticleSystemMgr is set by the time anything reads it.
// GCC and clang run a constructor function at load; MSVC has no such
// attribute, so there it is a dynamic initialiser placed in the library
// segment (#pragma init_seg(lib)), which runs before every user-segment
// static initialiser in the DLL -- the same ordering guarantee.
#if defined( _MSC_VER )
void LoadParticleLib();
#pragma init_seg( lib )
static const int s_nParticleLibLoaded = ( LoadParticleLib(), 0 );
void LoadParticleLib()
#else
__attribute__( ( constructor ) ) void LoadParticleLib()
#endif
{
#if defined( _WIN32 )
	HMODULE hLib = ::LoadLibraryA( SS_PARTICLE_LIB );
	if ( !hLib )
	{
		Error( "[SourceSharp] " SS_PARTICLE_LIB " did not load: error %lu\n", (unsigned long)::GetLastError() );
		return;
	}
	CreateInterfaceFn factory = (CreateInterfaceFn)::GetProcAddress( hLib, "CreateInterface" );
#else
	void *hLib = dlopen( SS_PARTICLE_LIB, RTLD_NOW | RTLD_LOCAL );
	if ( !hLib )
	{
		Error( "[SourceSharp] " SS_PARTICLE_LIB " did not load: %s\n", dlerror() );
		return;
	}
	CreateInterfaceFn factory = (CreateInterfaceFn)dlsym( hLib, "CreateInterface" );
#endif
	g_pLib = factory ? (ISSParticleLib *)factory( SS_PARTICLE_LIB_INTERFACE_VERSION, NULL ) : NULL;
	if ( !g_pLib )
	{
		Error( "[SourceSharp] " SS_PARTICLE_LIB " has no %s\n", SS_PARTICLE_LIB_INTERFACE_VERSION );
		return;
	}
	g_pParticleSystemMgr = g_pLib->Mgr();
}

} // namespace

bool ParticleLib_Connect( CreateInterfaceFn appSystemFactory )
{
	return g_pLib && g_pLib->Connect( appSystemFactory );
}

void ParticleLib_Disconnect()
{
	if ( g_pLib )
		g_pLib->Disconnect();
}

// MgrUncacheAllParticleSystems is the LAST declaration in ISSParticleLib (the
// interface is append-only): its slot plus one is the count managed checks
// its generated layout against.
void *ParticleLib_Acquire( int32_t *pnEntries )
{
	if ( pnEntries )
		*pnEntries = g_pLib ? VTableHook::MemberOf( &ISSParticleLib::MgrUncacheAllParticleSystems ).slot + 1 : 0;
	return g_pLib;
}

void ParticleLib_Counters( SSParticleLibCounters_t *pOut )
{
	if ( g_pLib )
		g_pLib->Counters( pOut );
	else
		memset( pOut, 0, sizeof( *pOut ) );
}

// CParticleCollection ---------------------------------------------------------
void *CParticleCollection::operator new( size_t nSize ) { return Lib()->CollectionNew( nSize ); }
void CParticleCollection::operator delete( void *pData ) { Lib()->CollectionDelete( pData ); }
// The base-object constructor and destructor (C2/D2, the only variants the
// client names: CNewParticleEffect's own constructor and destructor call them)
// are plain functions under the mangled names. Written as C++ members, the
// compiler would construct and destroy every member around the forward too,
// and the library's real constructor and destructor run them already.
extern "C" void SSParticleLib_CollectionC2( CParticleCollection *p ) __asm__( "_ZN19CParticleCollectionC2Ev" );
extern "C" void SSParticleLib_CollectionD2( CParticleCollection *p ) __asm__( "_ZN19CParticleCollectionD2Ev" );
extern "C" void SSParticleLib_CollectionC2( CParticleCollection *p ) { Lib()->CollectionConstruct( p ); }
extern "C" void SSParticleLib_CollectionD2( CParticleCollection *p ) { Lib()->CollectionDestruct( p ); }
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
