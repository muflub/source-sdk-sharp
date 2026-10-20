//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — tier0's thread and platform calls, in client.so =====//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// plan_server_novalve.md P4 stopped server.so linking libtier0, and
// plan_client_novalve C9 client.so. Our own code uses std for its locks and
// clocks; what is left are the calls inline code in fork headers makes. Since
// plan_server_native_v2 T5 no server.so object imports any of them (nm U over
// its link edge), so the file is client.so's alone:
//
//   ThreadGetCurrentId          CThreadFastMutex's inline Lock/Unlock/TryLock
//   CThreadFastMutex::Lock(id, sleep)   its contended path (studio.h's
//                               CStudioHdr holds two of these)
//
// Defined here with hidden visibility (client.so exports CreateInterface
// only), so client.so's calls bind to these. Each has the
// platform's own spelling: pthreads on Linux and macOS, the Win32 thread
// calls and intrinsics on Windows, where STATIC_TIER0 makes tier0's
// declarations plain so these definitions bind. Plat_IsInDebugSession and
// GetCPUInformation went (plan_server_native_v2 T5): no object of either DLL
// imports them.
//=============================================================================//

#include "tier0/platform.h"
#include "tier0/threadtools.h"
#include "ss_platform.h"	// SS_BIND_IMPORT
#ifdef _MSC_VER
// This file defines functions the engine DLLs' headers declare dllimport
// (C4273); SS_BIND_IMPORT at the end binds this module's calls to them.
#pragma warning( disable : 4273 )
#endif

#include <stdlib.h>
#include <string.h>

#if defined( _WIN32 )
	#ifndef WIN32_LEAN_AND_MEAN
		#define WIN32_LEAN_AND_MEAN
	#endif
	#include <windows.h>
	#include <intrin.h>
#else
	#include <pthread.h>
	#include <sched.h>
	#include <unistd.h>
#endif

// pthread_self(), as tier0 answers on Linux: never 0 (0 is the fast mutex's
// "unowned"), and the same value tier0 would give this thread, so an owner id
// means the same thing on either side. Windows' tier0 answers
// GetCurrentThreadId, likewise never 0.
ThreadId_t ThreadGetCurrentId()
{
#if defined( _WIN32 )
	return (ThreadId_t)::GetCurrentThreadId();
#else
	return (ThreadId_t)pthread_self();
#endif
}

// The contended path, entered after one failed TryLockInline and a pause:
// spin a while, then give the CPU away -- for nSpinSleepTime ms when asked,
// else a yield -- and spin again.
//
// A C++-linkage member: its import name is mangled, so SS_BIND_IMPORT cannot
// bind it. Neither DLL links tier0.lib (client.dll since plan_client_novalve
// C9), so the linker binds each import of it to this definition (LNK4217).
void CThreadFastMutex::Lock( const ThreadId_t threadId, unsigned nSpinSleepTime ) volatile
{
	enum { kSpinsBeforeYield = 1000 };
	for ( ;; )
	{
		for ( int i = 0; i < kSpinsBeforeYield; ++i )
		{
			if ( TryLockInline( threadId ) )
				return;
			ThreadPause();
		}

#if defined( _WIN32 )
		if ( nSpinSleepTime )
			::Sleep( nSpinSleepTime );
		else
			::SwitchToThread();
#else
		if ( nSpinSleepTime )
			usleep( nSpinSleepTime * 1000 );
		else
			sched_yield();
#endif
	}
}

#if defined( _WIN32 )
// threadtools.h's 16-byte compare-and-swap, declared only for 64-bit Windows
// (_WIN64; this tree builds x64 only, so _WIN32 is the same test here, and
// the spelling tools/dead-conditionals-ledger allows)
// (CTSList's head on Windows): CMPXCHG16B through MSVC's intrinsic. True when
// *pDest held comperand and now holds value.
bool ThreadInterlockedAssignIf128( volatile int128 *pDest, const int128 &value, const int128 &comperand )
{
	__int64 comparand[2];
	memcpy( comparand, &comperand, sizeof( comparand ) );
	__int64 exchange[2];
	memcpy( exchange, &value, sizeof( exchange ) );
	return _InterlockedCompareExchange128( (volatile __int64 *)pDest, exchange[1], exchange[0], comparand ) != 0;
}
#endif

// Every call in this module of the functions above, made through the
// import pointer, reaches these definitions (ss_platform.h).
SS_BIND_IMPORT( ThreadGetCurrentId )
#if defined( _WIN32 )
SS_BIND_IMPORT( ThreadInterlockedAssignIf128 )
#endif
