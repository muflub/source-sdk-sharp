//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the one native pointer managed is handed ==========//
//
// plan_engineapi_vtables.md §1.1 (step A3). Each side's managed half is
// handed exactly one native object at load, this one, beside its forwarder
// table (SSFwd_ShimRoot, ShimRoot_Get below). Every other native interface
// managed calls, and every forwarder table it calls them through, is acquired
// FROM it by name through Acquire -- the internal factory that
// SourceSharp_InternalFactory implements.
//
// Managed never calls this class's vtable: it calls the root's forwarders
// (plan_forwarders_everywhere.md), so no slot count crosses. SAME RULES AS
// ishim_frame.h all the same: no virtual destructor, blittable arguments
// only, and APPEND-ONLY, Release() last, because the forwarder table keeps
// the header's order and only appends.
//
//=============================================================================//

#pragma once

#include <stdint.h>
#include "tier0/platform.h"
#include "sourcesharp.h"

#define SOURCESHARP_SHIM_ROOT_VERSION "SourceSharpRoot003"

abstract_class IShimRoot
{
public:
	// A shim interface or forwarder table this side serves, by name. NULL for
	// a name this side does not serve -- the same answer an engine factory
	// gives. No slot count comes back: managed calls nothing through a vtable
	// (plan_forwarders_everywhere.md), and a forwarder table carries its own
	// count in its header (the root became ...003 when the counts went).
	virtual void *Acquire( const char *pszName ) = 0;

	// The ENGINE's interfaces, by factory kind and version string
	// (plan_engineapi_vtables C5; the root became ...002 when this was inserted
	// before Release, which stays last).
	// nType is a SourceSharpFactoryType, passed as int32_t: blittable arguments only.
	virtual void *AcquireInterface( int32_t nType, const char *pszVersion,
	                                char *pError, int32_t nErrorBytes ) = 0;

	// The root lives as long as the DLL; this does nothing. Last, as its
	// forwarder table (SSFwd_ShimRoot) only appends.
	virtual void Release() = 0;
};

// This side's root, and its forwarder table in *ppTable. Defined in shell_internalfactory.cpp.
IShimRoot *ShimRoot_Get( const void **ppTable );
