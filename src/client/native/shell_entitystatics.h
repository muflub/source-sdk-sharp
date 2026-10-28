// The client entity allocator: MemAlloc, zeroed. Declared here for the world
// shell, which allocates through it.

#pragma once

#include <stddef.h>
#include <string.h>

void *EntityMem_Alloc( size_t stAllocateBlock );
void  EntityMem_Free( void *pMem );
