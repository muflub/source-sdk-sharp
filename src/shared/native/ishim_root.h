//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the one native pointer managed is handed ==========//
//
// plan_engineapi_vtables.md §1.1 (step A3). The SERVER's managed side is handed
// exactly one native object at load, this one, with the count the compiler
// gives for it. Every other native interface managed calls is acquired FROM it
// by name, with its own count, through Acquire -- the internal factory that
// SourceSharp_InternalFactory already implements, now reachable through a
// vtable instead of through a slot of SourceSharpEngineApi.
//
// Phase C moves the server's EngineApi slots into IShim* classes acquired here,
// one class per commit, and at the end of it the server stops passing the
// struct. The client keeps LoadSide V1 until its own wave.
//
// SAME RULES AS ishim_frame.h: no virtual destructor (it would take the first
// two Itanium slots), blittable arguments only, and APPEND-ONLY. Release() is
// the LAST slot and the count anchor: native reports
// VTableHook::MemberOf( &IShimRoot::Release ).slot + 1 and managed refuses a
// root whose count differs from its generated layout.
//
//=============================================================================//

#ifndef ISHIM_ROOT_H
#define ISHIM_ROOT_H
#pragma once

#include <stdint.h>
#include "tier0/platform.h"
#include "sourcesharp.h"

#define SOURCESHARP_SHIM_ROOT_VERSION "SourceSharpRoot002"

abstract_class IShimRoot
{
public:
	// A shim interface this side implements, by version string, with the
	// compiler's slot count for it in *pnEntries. NULL and 0 for a name this
	// side does not serve -- the same answer an engine factory gives.
	virtual void *Acquire( const char *pszName, int32_t *pnEntries ) = 0;

	// The ENGINE's interfaces, by factory kind and version string, with their
	// slot counts -- what the struct's AcquireInterface slot did, on the root
	// (plan_engineapi_vtables C5; the root became ...002 when this was inserted
	// before Release, which stays last).
	// nType is a SourceSharpFactoryType, passed as int32_t: blittable arguments only.
	virtual void *AcquireInterface( int32_t nType, const char *pszVersion,
	                                int32_t *pnEntries, char *pError, int32_t nErrorBytes ) = 0;

	// The root lives as long as the DLL; this does nothing. Last, as its
	// forwarder table (SSFwd_ShimRoot) only appends.
	virtual void Release() = 0;
};

// This side's root, and its forwarder table in *ppTable. Defined in shell_internalfactory.cpp.
IShimRoot *ShimRoot_Get( const void **ppTable );

#endif // ISHIM_ROOT_H
