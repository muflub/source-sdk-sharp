//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The one native object each side's managed half is handed at load. Every other
// native interface and forwarder table is acquired from it by name. Managed calls
// it through its forwarder table, so this is append-only with Release() last.

#pragma once

#include <stdint.h>
#include "tier0/platform.h"
#include "sourcesharp.h"

#define SOURCESHARP_SHIM_ROOT_VERSION "SourceSharpRoot003"

abstract_class IShimRoot
{
public:
	// A shim interface or forwarder table this side serves, by name, or NULL.
	virtual void *Acquire( const char *pszName ) = 0;

	// The engine's interfaces, by factory kind (a SourceSharpFactoryType) and version string.
	virtual void *AcquireInterface( int32_t nType, const char *pszVersion,
	                                char *pError, int32_t nErrorBytes ) = 0;

	// The root lives as long as the DLL; this does nothing.
	virtual void Release() = 0;
};

// This side's root, and its forwarder table in *ppTable. Defined in shell_internalfactory.cpp.
IShimRoot *ShimRoot_Get( const void **ppTable );
