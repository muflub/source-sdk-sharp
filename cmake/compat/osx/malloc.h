//========= SourceSharp — <malloc.h> for macOS ================================//
//
// Valve's platform.h includes <malloc.h> on every platform. Linux and Windows
// have one; macOS keeps its allocator API in <malloc/malloc.h> (malloc_size,
// which platform.h's mallocsize() uses under OSX) and the standard functions
// in <stdlib.h>. This header sits first on the include path of a macOS build
// (cmake/SourceSharp.cmake, ss_sdk_compile) and forwards to both. Nothing
// else is defined here. It lives under cmake/, not in the shim tree, because
// it is build plumbing and the shim's line ratchet walks that tree.
//=============================================================================//
#pragma once

#include <stdlib.h>
#include <malloc/malloc.h>
