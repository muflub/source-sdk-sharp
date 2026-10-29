//========= SourceSharp — a libtier0.so holding what client.so binds to ======//
//
// For tier_bindings_client_test: built as fake_engine_tier/libtier0.so, so
// the loaded-object list names it as the game's libtier0.so is named. It
// exports, unmangled or mangled as the game's are (nm -D), each name
// tier_bindings_client.cpp binds, and records every call with its object, so a
// fact can tell that a call reached this library and not a copy of its own.
//
//=============================================================================//

#include "fake_engine_tier.h"

#define FAKE_EXPORT extern "C" __attribute__( ( visibility( "default" ) ) )

namespace
{
FakeTierCall s_Calls[64];
int s_nCalls = 0;
char s_CommandLine;
char s_Heap;

void Record( const char *pszName, const void *pThis, unsigned int nArg )
{
	if ( s_nCalls < 64 )
		s_Calls[s_nCalls++] = FakeTierCall{ pszName, pThis, nArg };
}
} // namespace

FAKE_EXPORT int FakeTier_CallCount()
{
	return s_nCalls;
}

FAKE_EXPORT const FakeTierCall *FakeTier_Call( int i )
{
	return i >= 0 && i < s_nCalls ? &s_Calls[i] : nullptr;
}

FAKE_EXPORT void FakeTier_Reset()
{
	s_nCalls = 0;
}

FAKE_EXPORT void *FakeTier_CommandLineObject()
{
	return &s_CommandLine;
}

FAKE_EXPORT void *FakeTier_HeapObject()
{
	return &s_Heap;
}

FAKE_EXPORT void *g_pMemAlloc;
void *g_pMemAlloc = &s_Heap;

FAKE_EXPORT void *CommandLine_Tier0()
{
	Record( "CommandLine_Tier0", nullptr, 0 );
	return &s_CommandLine;
}

// The members, under the names tier0 exports them by.
FAKE_EXPORT void Fake_EventCtor( void *p, bool b ) __asm__( "_ZN12CThreadEventC1Eb" );
FAKE_EXPORT bool Fake_EventSet( void *p ) __asm__( "_ZN12CThreadEvent3SetEv" );
FAKE_EXPORT void Fake_SyncDtor( void *p ) __asm__( "_ZN17CThreadSyncObjectD2Ev" );
FAKE_EXPORT void Fake_RWWaitForRead( void *p ) __asm__( "_ZN13CThreadRWLock11WaitForReadEv" );
FAKE_EXPORT void Fake_RWLockForWrite( void *p ) __asm__( "_ZN13CThreadRWLock12LockForWriteEv" );
FAKE_EXPORT void Fake_RWUnlockWrite( void *p ) __asm__( "_ZN13CThreadRWLock11UnlockWriteEv" );
FAKE_EXPORT void Fake_SpinLockForRead( void *p ) __asm__( "_ZN17CThreadSpinRWLock11LockForReadEv" );
FAKE_EXPORT void Fake_SpinUnlockRead( void *p ) __asm__( "_ZN17CThreadSpinRWLock10UnlockReadEv" );
FAKE_EXPORT void Fake_SpinUnlockWrite( void *p ) __asm__( "_ZN17CThreadSpinRWLock11UnlockWriteEv" );
FAKE_EXPORT void Fake_SpinLockForWrite( void *p,
										unsigned int n ) __asm__( "_ZN17CThreadSpinRWLock16SpinLockForWriteEj" );

void Fake_EventCtor( void *p, bool b )
{
	Record( "CThreadEvent::CThreadEvent", p, b ? 1u : 0u );
}

bool Fake_EventSet( void *p )
{
	Record( "CThreadEvent::Set", p, 0 );
	return true;
}

void Fake_SyncDtor( void *p )
{
	Record( "CThreadSyncObject::~CThreadSyncObject", p, 0 );
}

void Fake_RWWaitForRead( void *p )
{
	Record( "CThreadRWLock::WaitForRead", p, 0 );
}

void Fake_RWLockForWrite( void *p )
{
	Record( "CThreadRWLock::LockForWrite", p, 0 );
}

void Fake_RWUnlockWrite( void *p )
{
	Record( "CThreadRWLock::UnlockWrite", p, 0 );
}

void Fake_SpinLockForRead( void *p )
{
	Record( "CThreadSpinRWLock::LockForRead", p, 0 );
}

void Fake_SpinUnlockRead( void *p )
{
	Record( "CThreadSpinRWLock::UnlockRead", p, 0 );
}

void Fake_SpinUnlockWrite( void *p )
{
	Record( "CThreadSpinRWLock::UnlockWrite", p, 0 );
}

void Fake_SpinLockForWrite( void *p, unsigned int n )
{
	Record( "CThreadSpinRWLock::SpinLockForWrite", p, n );
}
