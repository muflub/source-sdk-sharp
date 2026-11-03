//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON d  a layout the engine reads at fixed offsets
// client.so links no tier0. What its objects still take from it
// is state only the engine's copies hold, so each name below is defined here
// and reaches the definition in the library the engine already loaded
// (loaded_symbol.h):
//
//   CommandLine_Tier0     the engine's command line, as the launcher built it
//   g_pMemAlloc           tier0's allocator, used by tier1's utlsymbol
//   g_pVCR                (Windows) tier0's VCR hooks, used by CThreadMutex::Lock
//
// Each function is found once, on first call; the engine keeps those libraries
// loaded for the client's lifetime. g_pMemAlloc is copied when the client loads,
// ahead of every C++ static initialiser. A missing library or name stops
// the process, naming both.
//
// Linux: no tier0 header is read. Functions are defined under the names client
// objects import (asm labels);
// constructor priority 101 copies the pointer.
//
// Windows: tier0's headers are read with STATIC_TIER0. The C names and g_pMemAlloc
// are bound with SS_BIND_IMPORT; init_seg(lib) copies the pointers.

#include "loaded_symbol.h"

#if defined(_WIN32)
#include "tier0/threadtools.h"
#include "ss_platform.h"
#include <new>
#pragma warning(disable : 4073) // initializers put in library initialization area
#pragma init_seg(lib)
#endif

#include <stdio.h>
#include <stdlib.h>

// loaded_symbol.h's RequireLoadedSymbol, defined beside its one caller.
void* ss::RequireLoadedSymbol(const char* pszFile, const char* pszSymbol)
{
	void* pSymbol = FindLoadedSymbol(pszFile, pszSymbol);
	if (!pSymbol)
	{
		fprintf(stderr,
				"[SourceSharp] %s: no loaded %s defines it. This module binds it "
				"to the copy the engine loaded and links no other; stopping.\n",
				pszSymbol, pszFile);
		fflush(stderr);
		abort();
	}
	return pSymbol;
}

namespace
{
#if defined(_WIN32)
const char kTier0[] = "tier0.dll";
#else
const char kTier0[] = "libtier0.so";
#endif

template <typename Fn>
Fn Tier0(const char* pszSymbol)
{
	return reinterpret_cast<Fn>(ss::RequireLoadedSymbol(kTier0, pszSymbol));
}

void* PointerIn(const char* pszFile, const char* pszSymbol)
{
	return *static_cast<void**>(ss::RequireLoadedSymbol(pszFile, pszSymbol));
}
} // namespace

extern "C"
{
void* g_pMemAlloc = nullptr;
}

#if defined(_WIN32)

// The pointers, before the client's own initialisers (init_seg above).
namespace
{
struct BindEnginePointers
{
	BindEnginePointers()
	{
		g_pMemAlloc = PointerIn(kTier0, "g_pMemAlloc");
		g_pVCR = static_cast<VCR_t*>(PointerIn(kTier0, "g_pVCR"));
	}
};

BindEnginePointers s_BindEnginePointers;
} // namespace

// vcrmode.h read plain gives this name C++ linkage; tier0.dll exports its own
// by the C name, which the fork's objects import.
VCR_t* g_pVCR = nullptr;

extern "C" void* CommandLine_Tier0()
{
	static const auto pfn = Tier0<void* (*)()>("CommandLine_Tier0");
	return pfn();
}

SS_BIND_IMPORT(g_pMemAlloc)
SS_BIND_IMPORT(CommandLine_Tier0)

#else // the ELF names

// The declarations give each definition its import's name; the C++ names are
// this file's own.
extern "C"
{
void* SS_CommandLine_Tier0() __asm__("CommandLine_Tier0");
}

__attribute__((constructor(101))) static void BindEnginePointers()
{
	g_pMemAlloc = PointerIn(kTier0, "g_pMemAlloc");
}

void* SS_CommandLine_Tier0()
{
	static const auto pfn = Tier0<void* (*)()>("CommandLine_Tier0");
	return pfn();
}

#endif
