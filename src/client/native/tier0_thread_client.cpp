//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// The tier0 thread and clock calls only client.so makes.
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md

#include "tier0/threadtools.h"
#include "ss_clock.h"
#include "ss_platform.h"
#ifdef _MSC_VER
// Defines functions the engine headers declare dllimport (C4273).
#pragma warning(disable : 4273)
#endif

namespace
{
// Taken at static initialisation; the engine loads client.so from its main thread.
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

// Binds this module's calls through the import pointers to the definitions above.
SS_BIND_IMPORT(ThreadInMainThread)
SS_BIND_IMPORT(Plat_FloatTime)
