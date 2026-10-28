//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Memory allocation!

#pragma once

#include <stddef.h>

#include "tier0/platform.h"

#if !defined(STATIC_TIER0) && !defined(_STATIC_LINKED)

#ifdef TIER0_DLL_EXPORT
#  define MEM_INTERFACE DLL_EXPORT
#else
#  define MEM_INTERFACE DLL_IMPORT
#endif

#else // BUILD_AS_DLL

#define MEM_INTERFACE extern

#endif // BUILD_AS_DLL

MEM_INTERFACE void MemAllocOOMError( size_t nSize );

#if defined(_LINUX) && !defined( USE_DXVK )
MEM_INTERFACE ;
#endif
