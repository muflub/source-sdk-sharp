//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Memory allocation!

#pragma once

#include "tier0/platform.h"

#if !defined(STATIC_TIER0)

#define MEM_INTERFACE DLL_IMPORT

#else // BUILD_AS_DLL

#define MEM_INTERFACE extern

#endif // BUILD_AS_DLL

MEM_INTERFACE void MemAllocOOMError(size_t nSize);
