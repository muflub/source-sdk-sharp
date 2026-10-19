//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — tier0's thread and platform calls, in server.so =====//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// plan_server_novalve.md P4: server.so stops linking libtier0. Our own code
// uses std for its locks and clocks; what is left are the calls inline code in
// fork headers makes, which P5's header move takes away:
//
//   ThreadGetCurrentId          CThreadFastMutex's inline Lock/Unlock/TryLock
//   CThreadFastMutex::Lock(id, sleep)   its contended path (studio.h's
//                               CStudioHdr holds two of these)
//   Plat_IsInDebugSession       DebuggerBreak(), in _DEBUG asserts and
//                               tslist.h's CTSQueue::ValidateQueue
//   GetCPUInformation           client.so's libmathlib.a MathLib_Init (server.so has none)
//
// Defined here with hidden visibility (the version script exports
// CreateInterface only), so server.so's calls bind to these. Each has the
// platform's own spelling: pthreads, /proc and CPUID through <cpuid.h> on
// Linux; the same minus /proc (sysctl's P_TRACED flag) on macOS; the Win32
// thread, debugger and CPUID intrinsics on Windows, where STATIC_TIER0 makes
// tier0's declarations plain so these definitions bind.
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
	#include <cpuid.h>
	#include <fcntl.h>
	#include <pthread.h>
	#include <sched.h>
	#include <unistd.h>
	#if defined( __APPLE__ )
		#include <sys/sysctl.h>
		#include <sys/types.h>
	#endif
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

// A tracer is attached when /proc/self/status says "TracerPid:" is not 0
// (Linux), when the kernel's process entry carries P_TRACED (macOS), or when
// IsDebuggerPresent says so (Windows). Asked again each time: a debugger can
// attach or leave while the game runs.
bool Plat_IsInDebugSession()
{
#if defined( _WIN32 )
	return ::IsDebuggerPresent() != 0;
#elif defined( __APPLE__ )
	int mib[ 4 ] = { CTL_KERN, KERN_PROC, KERN_PROC_PID, (int)getpid() };
	struct kinfo_proc info;
	size_t nSize = sizeof( info );
	memset( &info, 0, sizeof( info ) );
	if ( sysctl( mib, 4, &info, &nSize, NULL, 0 ) != 0 )
		return false;
	return ( info.kp_proc.p_flag & P_TRACED ) != 0;
#else
	const int fd = open( "/proc/self/status", O_RDONLY | O_CLOEXEC );
	if ( fd < 0 )
		return false;

	char szStatus[ 4096 ];
	const ssize_t n = read( fd, szStatus, sizeof( szStatus ) - 1 );
	close( fd );
	if ( n <= 0 )
		return false;
	szStatus[ n ] = 0;

	const char *pTracer = strstr( szStatus, "TracerPid:" );
	return pTracer && strtol( pTracer + strlen( "TracerPid:" ), NULL, 10 ) != 0;
#endif
}

// CPUID leaf nLeaf's four registers, or false when the CPU has no such leaf.
// Read directly: __builtin_cpu_supports would link libgcc's __cpu_model state.
static bool CpuId( unsigned nLeaf, unsigned *pEax, unsigned *pEbx, unsigned *pEcx, unsigned *pEdx )
{
#if defined( _WIN32 )
	int regs[ 4 ] = { 0, 0, 0, 0 };
	__cpuid( regs, (int)( nLeaf & 0x80000000u ) );
	if ( (unsigned)regs[ 0 ] < nLeaf )
		return false;
	__cpuid( regs, (int)nLeaf );
	*pEax = (unsigned)regs[ 0 ];
	*pEbx = (unsigned)regs[ 1 ];
	*pEcx = (unsigned)regs[ 2 ];
	*pEdx = (unsigned)regs[ 3 ];
	return true;
#else
	return __get_cpuid( nLeaf, pEax, pEbx, pEcx, pEdx ) != 0;
#endif
}

// How many logical processors are online.
static long OnlineProcessors()
{
#if defined( _WIN32 )
	SYSTEM_INFO si;
	::GetSystemInfo( &si );
	return (long)si.dwNumberOfProcessors;
#else
	return sysconf( _SC_NPROCESSORS_ONLN );
#endif
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

// What the CPU supports, from CPUID. MathLib_Init (the one caller, in the
// libmathlib.a client.so links; server.so has none) reads the instruction-set
// flags to choose its SSE paths; the fields nothing here can measure cheaply
// stay at the constructor's zeros, and the two strings are empty, not NULL.
const CPUInformation *GetCPUInformation()
{
	static const CPUInformation s_Info = [] {
		CPUInformation info;
		info.m_Size = sizeof( CPUInformation );
		// CPUID leaf 1 (EDX, ECX) and 0x80000001 (ECX). The bit numbers are
		// the architecture's, spelled here rather than through <cpuid.h>'s
		// bit_* names, which MSVC does not have.
		unsigned eax = 0, ebx = 0, ecx = 0, edx = 0;
		if ( CpuId( 1, &eax, &ebx, &ecx, &edx ) )
		{
			info.m_bRDTSC = ( edx & ( 1u << 4 ) ) != 0;    // TSC
			info.m_bCMOV = ( edx & ( 1u << 15 ) ) != 0;    // CMOV
			info.m_bFCMOV = info.m_bCMOV;
			info.m_bMMX = ( edx & ( 1u << 23 ) ) != 0;     // MMX
			info.m_bSSE = ( edx & ( 1u << 25 ) ) != 0;     // SSE
			info.m_bSSE2 = ( edx & ( 1u << 26 ) ) != 0;    // SSE2
			info.m_bHT = ( edx & ( 1u << 28 ) ) != 0;      // HTT
			info.m_bSSE3 = ( ecx & ( 1u << 0 ) ) != 0;     // SSE3
			info.m_bSSSE3 = ( ecx & ( 1u << 9 ) ) != 0;    // SSSE3
			info.m_bSSE41 = ( ecx & ( 1u << 19 ) ) != 0;   // SSE4.1
			info.m_bSSE42 = ( ecx & ( 1u << 20 ) ) != 0;   // SSE4.2
			info.m_bAVX = ( ecx & ( 1u << 28 ) ) != 0;     // AVX
			info.m_nModel = eax;
			info.m_nFeatures[ 0 ] = edx;
			info.m_nFeatures[ 1 ] = ecx;
		}
		if ( CpuId( 0x80000001u, &eax, &ebx, &ecx, &edx ) )
			info.m_bSSE4a = ( ecx & ( 1u << 6 ) ) != 0;    // SSE4a
		info.m_b3DNow = false;   // gone from every CPU this runs on
		const long nCpus = OnlineProcessors();
		info.m_nLogicalProcessors = (uint8)( nCpus > 0 && nCpus < 255 ? nCpus : 255 );
		info.m_nPhysicalProcessors = info.m_nLogicalProcessors;
		info.m_szProcessorID = const_cast< char * >( "" );
		info.m_szProcessorBrand = const_cast< char * >( "" );
		return info;
	}();
	return &s_Info;
}

// Every call in this module of the functions above, made through the
// import pointer, reaches these definitions (ss_platform.h).
SS_BIND_IMPORT( ThreadGetCurrentId )
SS_BIND_IMPORT( Plat_IsInDebugSession )
SS_BIND_IMPORT( GetCPUInformation )
#if defined( _WIN32 )
SS_BIND_IMPORT( ThreadInterlockedAssignIf128 )
#endif
