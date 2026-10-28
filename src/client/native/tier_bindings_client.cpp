//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets
// client.so links no tier0 and no vstdlib. What its objects still take from them
// is state only the engine's copies hold, so each name below is defined here
// and reaches the definition in the library the engine already loaded
// (loaded_symbol.h):
//
//   CommandLine_Tier0     the engine's command line, as the launcher built it
//   g_pThreadPool,        vstdlib's thread pool the client's parallel jobs run
//   DestroyThreadPool     on, and its pools' destruction
//   CThreadEvent, CThreadSyncObject, CThreadRWLock, CThreadSpinRWLock members
//                         tier0's own: a CJob's completion event is set here and
//                         waited on in the engine's pool
//   g_pMemAlloc           tier0's allocator, used by tier1's utlsymbol
//   g_pVCR                (Windows) tier0's VCR hooks, used by CThreadMutex::Lock
//
// Each function is found once, on first call; the engine keeps those libraries
// loaded for the client's lifetime. The two pointers are copied when the client
// loads, ahead of every C++ static initialiser. A missing library or name stops
// the process, naming both.
//
// Linux: no tier0 header is read. Functions are defined under the names client
// objects import (asm labels) with the arguments the Itanium ABI passes;
// constructor priority 101 copies the pointers.
//
// Windows: tier0's headers are read with STATIC_TIER0, so members are defined
// plainly and the linker binds each dllimport to its definition (LNK4217).
// Each finds tier0.dll's own by its decorated name (__FUNCDNAME__); the C names
// and the two pointers are bound with SS_BIND_IMPORT; init_seg(lib) copies the
// pointers.

#include "loaded_symbol.h"

#if defined( _WIN32 )
#include "tier0/threadtools.h"
#include "ss_platform.h"
#include <new>
#pragma warning( disable : 4073 ) // initializers put in library initialization area
#pragma init_seg( lib )
#endif

#include <stdio.h>
#include <stdlib.h>

// loaded_symbol.h's RequireLoadedSymbol, defined beside its one caller.
void *ss::RequireLoadedSymbol( const char *pszFile, const char *pszSymbol )
{
	void *pSymbol = FindLoadedSymbol( pszFile, pszSymbol );
	if ( !pSymbol )
	{
		fprintf( stderr,
				 "[SourceSharp] %s: no loaded %s defines it. This module binds it "
				 "to the copy the engine loaded and links no other; stopping.\n",
				 pszSymbol, pszFile );
		fflush( stderr );
		abort();
	}
	return pSymbol;
}

namespace
{
#if defined( _WIN32 )
const char kTier0[] = "tier0.dll";
const char kVstdlib[] = "vstdlib.dll";
#else
const char kTier0[] = "libtier0.so";
const char kVstdlib[] = "libvstdlib.so";
#endif

template <typename Fn>
Fn Tier0( const char *pszSymbol )
{
	return reinterpret_cast<Fn>( ss::RequireLoadedSymbol( kTier0, pszSymbol ) );
}

template <typename Fn>
Fn Vstdlib( const char *pszSymbol )
{
	return reinterpret_cast<Fn>( ss::RequireLoadedSymbol( kVstdlib, pszSymbol ) );
}

void *PointerIn( const char *pszFile, const char *pszSymbol )
{
	return *static_cast<void **>( ss::RequireLoadedSymbol( pszFile, pszSymbol ) );
}
} // namespace

extern "C"
{
void *g_pMemAlloc = nullptr;
void *g_pThreadPool = nullptr;
}

#if defined( _WIN32 )

// The two pointers, before the client's own initialisers (init_seg above).
namespace
{
struct BindEnginePointers
{
	BindEnginePointers()
	{
		g_pMemAlloc = PointerIn( kTier0, "g_pMemAlloc" );
		g_pThreadPool = PointerIn( kVstdlib, "g_pThreadPool" );
		g_pVCR = static_cast<VCR_t *>( PointerIn( kTier0, "g_pVCR" ) );
	}
};

BindEnginePointers s_BindEnginePointers;
} // namespace

// vcrmode.h read plain gives this name C++ linkage; tier0.dll exports its own
// by the C name, which the fork's objects import.
VCR_t *g_pVCR = nullptr;

extern "C" void *CommandLine_Tier0()
{
	static const auto pfn = Tier0<void *(*)()>( "CommandLine_Tier0" );
	return pfn();
}

extern "C" void DestroyThreadPool( void *pPool )
{
	static const auto pfn = Vstdlib<void ( * )( void * )>( "DestroyThreadPool" );
	pfn( pPool );
}

SS_BIND_IMPORT( g_pMemAlloc )
SS_BIND_IMPORT( g_pThreadPool )
SS_BIND_IMPORT( CommandLine_Tier0 )
SS_BIND_IMPORT( DestroyThreadPool )

// tier0.dll's member, by this member's own decorated name. On x64 a member's
// `this` is passed as the first argument, so it is called as a plain function.
#define SS_TIER0_MEMBER( Ret, ... ) static const auto pfn = Tier0<Ret ( * )( __VA_ARGS__ )>( __FUNCDNAME__ )

// Runs before CThreadEvent's constructor below, which then runs tier0's, whose
// own call of this constructor again only clears the handle fields it set.
CThreadSyncObject::CThreadSyncObject()
{
	SS_TIER0_MEMBER( void, CThreadSyncObject * );
	pfn( this );
}

CThreadSyncObject::~CThreadSyncObject()
{
	SS_TIER0_MEMBER( void, CThreadSyncObject * );
	pfn( this );
}

bool CThreadSyncObject::operator!() const
{
	SS_TIER0_MEMBER( bool, const CThreadSyncObject * );
	return pfn( this );
}

bool CThreadSyncObject::Wait( uint32 dwTimeout )
{
	SS_TIER0_MEMBER( bool, CThreadSyncObject *, uint32 );
	return pfn( this, dwTimeout );
}

CThreadEvent::CThreadEvent( bool fManualReset )
{
	SS_TIER0_MEMBER( void, CThreadEvent *, bool );
	pfn( this, fManualReset );
}

bool CThreadEvent::Set()
{
	SS_TIER0_MEMBER( bool, CThreadEvent * );
	return pfn( this );
}

bool CThreadEvent::Reset()
{
	SS_TIER0_MEMBER( bool, CThreadEvent * );
	return pfn( this );
}

bool CThreadEvent::Check()
{
	SS_TIER0_MEMBER( bool, CThreadEvent * );
	return pfn( this );
}

bool CThreadEvent::Wait( uint32 dwTimeout )
{
	SS_TIER0_MEMBER( bool, CThreadEvent *, uint32 );
	return pfn( this, dwTimeout );
}

void CThreadRWLock::WaitForRead()
{
	SS_TIER0_MEMBER( void, CThreadRWLock * );
	pfn( this );
}

void CThreadRWLock::LockForWrite()
{
	SS_TIER0_MEMBER( void, CThreadRWLock * );
	pfn( this );
}

void CThreadRWLock::UnlockWrite()
{
	SS_TIER0_MEMBER( void, CThreadRWLock * );
	pfn( this );
}

void CThreadSpinRWLock::LockForRead()
{
	SS_TIER0_MEMBER( void, CThreadSpinRWLock * );
	pfn( this );
}

void CThreadSpinRWLock::UnlockRead()
{
	SS_TIER0_MEMBER( void, CThreadSpinRWLock * );
	pfn( this );
}

void CThreadSpinRWLock::UnlockWrite()
{
	SS_TIER0_MEMBER( void, CThreadSpinRWLock * );
	pfn( this );
}

void CThreadSpinRWLock::SpinLockForWrite( const uint32 threadId )
{
	SS_TIER0_MEMBER( void, CThreadSpinRWLock *, uint32 );
	pfn( this, threadId );
}

// tier0's headers define these members inline; a fork object that did not
// inline one imports it from tier0.dll. Read plain here, they are emitted as
// this module's own and the linker binds each import to them (LNK4217).
// Nothing calls this function.
#pragma optimize( "", off )

void SS_EmitTier0ThreadInlines( CThreadMutex *pMutex, void *pSpinLock )
{
	pMutex->Lock();
	CThreadSpinRWLock *pLock = new ( pSpinLock ) CThreadSpinRWLock;
	pLock->LockForWrite();
}

#pragma optimize( "", on )

#else // the ELF names

// The declarations give each definition its import's name; the C++ names are
// this file's own.
extern "C"
{
void *SS_CommandLine_Tier0() __asm__( "CommandLine_Tier0" );
void SS_DestroyThreadPool( void *pPool ) __asm__( "DestroyThreadPool" );

void SS_CThreadEvent_Ctor( void *pThis, bool bManualReset ) __asm__( "_ZN12CThreadEventC1Eb" );
bool SS_CThreadEvent_Set( void *pThis ) __asm__( "_ZN12CThreadEvent3SetEv" );
void SS_CThreadSyncObject_Dtor( void *pThis ) __asm__( "_ZN17CThreadSyncObjectD2Ev" );

void SS_CThreadRWLock_WaitForRead( void *pThis ) __asm__( "_ZN13CThreadRWLock11WaitForReadEv" );
void SS_CThreadRWLock_LockForWrite( void *pThis ) __asm__( "_ZN13CThreadRWLock12LockForWriteEv" );
void SS_CThreadRWLock_UnlockWrite( void *pThis ) __asm__( "_ZN13CThreadRWLock11UnlockWriteEv" );

void SS_CThreadSpinRWLock_LockForRead( void *pThis ) __asm__( "_ZN17CThreadSpinRWLock11LockForReadEv" );
void SS_CThreadSpinRWLock_UnlockRead( void *pThis ) __asm__( "_ZN17CThreadSpinRWLock10UnlockReadEv" );
void SS_CThreadSpinRWLock_UnlockWrite( void *pThis ) __asm__( "_ZN17CThreadSpinRWLock11UnlockWriteEv" );
void SS_CThreadSpinRWLock_SpinLockForWrite( void *pThis, unsigned int nThreadId ) __asm__(
	"_ZN17CThreadSpinRWLock16SpinLockForWriteEj" );
}

__attribute__( ( constructor( 101 ) ) ) static void BindEnginePointers()
{
	g_pMemAlloc = PointerIn( kTier0, "g_pMemAlloc" );
	g_pThreadPool = PointerIn( kVstdlib, "g_pThreadPool" );
}

void *SS_CommandLine_Tier0()
{
	static const auto pfn = Tier0<void *(*)()>( "CommandLine_Tier0" );
	return pfn();
}

void SS_DestroyThreadPool( void *pPool )
{
	static const auto pfn = Vstdlib<void ( * )( void * )>( "DestroyThreadPool" );
	pfn( pPool );
}

void SS_CThreadEvent_Ctor( void *pThis, bool bManualReset )
{
	static const auto pfn = Tier0<void ( * )( void *, bool )>( "_ZN12CThreadEventC1Eb" );
	pfn( pThis, bManualReset );
}

bool SS_CThreadEvent_Set( void *pThis )
{
	static const auto pfn = Tier0<bool ( * )( void * )>( "_ZN12CThreadEvent3SetEv" );
	return pfn( pThis );
}

void SS_CThreadSyncObject_Dtor( void *pThis )
{
	static const auto pfn = Tier0<void ( * )( void * )>( "_ZN17CThreadSyncObjectD2Ev" );
	pfn( pThis );
}

void SS_CThreadRWLock_WaitForRead( void *pThis )
{
	static const auto pfn = Tier0<void ( * )( void * )>( "_ZN13CThreadRWLock11WaitForReadEv" );
	pfn( pThis );
}

void SS_CThreadRWLock_LockForWrite( void *pThis )
{
	static const auto pfn = Tier0<void ( * )( void * )>( "_ZN13CThreadRWLock12LockForWriteEv" );
	pfn( pThis );
}

void SS_CThreadRWLock_UnlockWrite( void *pThis )
{
	static const auto pfn = Tier0<void ( * )( void * )>( "_ZN13CThreadRWLock11UnlockWriteEv" );
	pfn( pThis );
}

void SS_CThreadSpinRWLock_LockForRead( void *pThis )
{
	static const auto pfn = Tier0<void ( * )( void * )>( "_ZN17CThreadSpinRWLock11LockForReadEv" );
	pfn( pThis );
}

void SS_CThreadSpinRWLock_UnlockRead( void *pThis )
{
	static const auto pfn = Tier0<void ( * )( void * )>( "_ZN17CThreadSpinRWLock10UnlockReadEv" );
	pfn( pThis );
}

void SS_CThreadSpinRWLock_UnlockWrite( void *pThis )
{
	static const auto pfn = Tier0<void ( * )( void * )>( "_ZN17CThreadSpinRWLock11UnlockWriteEv" );
	pfn( pThis );
}

void SS_CThreadSpinRWLock_SpinLockForWrite( void *pThis, unsigned int nThreadId )
{
	static const auto pfn = Tier0<void ( * )( void *, unsigned int )>( "_ZN17CThreadSpinRWLock16SpinLockForWriteEj" );
	pfn( pThis, nThreadId );
}

#endif
