//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The client's entity allocator and the visibility sweep.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"

#include "shell_standalone_client.h"
#include "shell_standalone_clients.h"

// The entity allocator. MEM_ALLOC_CREDIT() credits this file.
void* EntityMem_Alloc(size_t stAllocateBlock)
{
	Assert(stAllocateBlock != 0);
	MEM_ALLOC_CREDIT();
	void* pMem = MemAlloc_Alloc(stAllocateBlock);
	memset(pMem, 0, stAllocateBlock);
	return pMem;
}

void EntityMem_Free(void* pMem)
{
	// get the engine to free the memory
	MemAlloc_Free(pMem);
}

// Calls UpdateVisibility() on every standalone client shell; answers how many
// it swept, since the effect is only visible next frame.
int32_t CStandaloneClients::EntitiesUpdateVisibilityAll()
{
	const int nCount = Count();
	int32_t nSwept = 0;

	for (int i = 0; i < nCount; i++)
	{
		if (C_StandaloneEntity* pEntity = At(i))
		{
			pEntity->UpdateVisibility();
			nSwept++;
		}
	}

	return nSwept;
}
