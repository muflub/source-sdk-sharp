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
//   GetCPUInformation           libmathlib.a's MathLib_Init (the SSE/MMX paths)
//
// Defined here with hidden visibility (the version script exports
// CreateInterface only), so server.so's calls bind to these.
//=============================================================================//

#include "tier0/platform.h"
#include "tier0/threadtools.h"

#include <cpuid.h>
#include <fcntl.h>
#include <pthread.h>
#include <sched.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

// pthread_self(), as tier0 answers on Linux: never 0 (0 is the fast mutex's
// "unowned"), and the same value tier0 would give this thread, so an owner id
// means the same thing on either side.
ThreadId_t ThreadGetCurrentId()
{
	return (ThreadId_t)pthread_self();
}

// The contended path, entered after one failed TryLockInline and a pause:
// spin a while, then give the CPU away -- for nSpinSleepTime ms when asked,
// else a yield -- and spin again.
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

		if ( nSpinSleepTime )
			usleep( nSpinSleepTime * 1000 );
		else
			sched_yield();
	}
}

// A tracer is attached when /proc/self/status says "TracerPid:" is not 0.
// Asked again each time: a debugger can attach or leave while the game runs.
bool Plat_IsInDebugSession()
{
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
}

// What the CPU supports, from CPUID. MathLib_Init (the one caller, in the
// libmathlib.a server.so keeps) reads the instruction-set flags to choose its
// SSE paths; the fields nothing here can measure cheaply stay at the
// constructor's zeros, and the two strings are empty rather than NULL.
const CPUInformation *GetCPUInformation()
{
	static const CPUInformation s_Info = [] {
		CPUInformation info;
		info.m_Size = sizeof( CPUInformation );
		// CPUID leaf 1 (EDX, ECX) and 0x80000001 (ECX), read directly:
		// __builtin_cpu_supports would link libgcc's __cpu_model state.
		unsigned eax = 0, ebx = 0, ecx = 0, edx = 0;
		if ( __get_cpuid( 1, &eax, &ebx, &ecx, &edx ) )
		{
			info.m_bRDTSC = ( edx & bit_TSC ) != 0;
			info.m_bCMOV = ( edx & bit_CMOV ) != 0;
			info.m_bFCMOV = info.m_bCMOV;
			info.m_bMMX = ( edx & bit_MMX ) != 0;
			info.m_bSSE = ( edx & bit_SSE ) != 0;
			info.m_bSSE2 = ( edx & bit_SSE2 ) != 0;
			info.m_bHT = ( edx & bit_HTT ) != 0;
			info.m_bSSE3 = ( ecx & bit_SSE3 ) != 0;
			info.m_bSSSE3 = ( ecx & bit_SSSE3 ) != 0;
			info.m_bSSE41 = ( ecx & bit_SSE4_1 ) != 0;
			info.m_bSSE42 = ( ecx & bit_SSE4_2 ) != 0;
			info.m_bAVX = ( ecx & bit_AVX ) != 0;
			info.m_nModel = eax;
			info.m_nFeatures[ 0 ] = edx;
			info.m_nFeatures[ 1 ] = ecx;
		}
		if ( __get_cpuid( 0x80000001, &eax, &ebx, &ecx, &edx ) )
			info.m_bSSE4a = ( ecx & bit_SSE4a ) != 0;
		info.m_b3DNow = false;   // gone from every CPU this runs on
		const long nCpus = sysconf( _SC_NPROCESSORS_ONLN );
		info.m_nLogicalProcessors = (uint8)( nCpus > 0 && nCpus < 255 ? nCpus : 255 );
		info.m_nPhysicalProcessors = info.m_nLogicalProcessors;
		info.m_szProcessorID = const_cast< char * >( "" );
		info.m_szProcessorBrand = const_cast< char * >( "" );
		return info;
	}();
	return &s_Info;
}
