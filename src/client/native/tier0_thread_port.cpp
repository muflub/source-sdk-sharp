//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// tier0's thread calls that inline code in fork headers makes, since client.so
// links no tier0:
//   ThreadGetCurrentId                  CThreadFastMutex's inline Lock/Unlock/TryLock
//   CThreadFastMutex::Lock(id, sleep)   its contended path
// Hidden visibility, so client.so's calls bind here. On Windows STATIC_TIER0
// makes tier0's declarations plain so these definitions bind.

#include "tier0/threadtools.h"
#include "ss_platform.h"
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

// pthread_self(), as tier0 answers on Linux: never 0 (the fast mutex's
// "unowned"), and the same value tier0 would give this thread. Windows' tier0
// answers GetCurrentThreadId, likewise never 0.
ThreadId_t ThreadGetCurrentId()
{
#if defined( _WIN32 )
	return (ThreadId_t)::GetCurrentThreadId();
#else
	return (ThreadId_t)pthread_self();
#endif
}

// The contended path, entered after one failed TryLockInline and a pause: spin
// a while, then sleep nSpinSleepTime ms when asked or yield, and spin again.
// C++ linkage, so SS_BIND_IMPORT cannot bind it; the linker binds each import
// to this definition (LNK4217).
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
// threadtools.h's 16-byte compare-and-swap for 64-bit Windows (CTSList's head):
// CMPXCHG16B through MSVC's intrinsic. True when *pDest held comperand and now
// holds value.
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
