//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the tier0 thread and clock calls only client.so needs //
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// tier0_thread_port.cpp (src/shared/native) holds the thread and platform
// calls both DLLs make; this is the one only the client makes (user ruling
// 2026-10-12: what the server does not need is client-only).
//
// ThreadInMainThread: shell_clientdll's load-time pump and shell_entitystatics'
// abs-query guards ask it (and clientleafsystem and sequence_Transitioner,
// which bind here too while they are on the link). The main thread is the one
// that loaded client.so: the engine dlopens it from its main thread, so the
// id taken at this file's static initialisation is that thread's.
//
// Plat_FloatTime (plan_client_novalve C9, so client.so links no libtier0):
// particlemgr.cpp's Begin/EndSimulateParticles, its only caller, subtract two
// of its own readings to time one simulation pass. A difference needs only a
// steady clock in seconds, so it is ss_clock.h's, whose base is this DLL's
// first reading rather than tier0's; no client caller compares it with an
// engine time (the linker's undefined list without libtier0 names no other).
//=============================================================================//

#include "tier0/threadtools.h"
#include "ss_clock.h"
#include "ss_platform.h"	// SS_BIND_IMPORT
#ifdef _MSC_VER
// This file defines functions the engine DLLs' headers declare dllimport
// (C4273); SS_BIND_IMPORT at the end binds this module's calls to them.
#pragma warning( disable : 4273 )
#endif

namespace
{
const ThreadId_t s_MainThread = ThreadGetCurrentId();
}

bool ThreadInMainThread()
{
	return ThreadGetCurrentId() == s_MainThread;
}

double Plat_FloatTime()
{
	return ss::SecondsSinceStart();
}

// Every call in this module of the functions above, made through the
// import pointer, reaches these definitions (ss_platform.h).
SS_BIND_IMPORT( ThreadInMainThread )
SS_BIND_IMPORT( Plat_FloatTime )
