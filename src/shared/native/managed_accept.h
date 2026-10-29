// Accepting a managed-built object: its Validate (slot 1 of IManagedObject) must
// agree with the slot hashes this binary was generated with, or it is refused
// naming the first slot that differs.

#pragma once

#include <stdint.h>
#include <stdio.h>

#include <type_traits>

#include "imanaged_core.h"

// The object as an IManagedObject *, or NULL with the reason in pWhy.
inline IManagedObject* ManagedAcceptObject(void* pObject, const uint32_t* pSlotHashes, int32_t nSlots, char* pWhy,
										   size_t nWhy)
{
	// Validate answers -1 when managed's slot hashes match, else the first slot
	// that differs.
	if (!pSlotHashes || nSlots <= 0)
	{
		snprintf(pWhy, nWhy, "no compiled slot hashes to check against");
		return NULL;
	}

	if (!pObject)
	{
		snprintf(pWhy, nWhy, "managed returned no object");
		return NULL;
	}

	IManagedObject* pManaged = static_cast<IManagedObject*>(pObject);
	const int32_t nDiffers = pManaged->Validate(pSlotHashes, nSlots);
	if (nDiffers != -1)
	{
		snprintf(pWhy, nWhy,
				 "managed's slot %d differs from the compiler's declaration "
				 "(%d slot(s) compiled); native and managed were built from different headers",
				 (int)nDiffers, (int)nSlots);
		return NULL;
	}

	if (nWhy)
		pWhy[0] = 0;
	return pManaged;
}

// The object as an I *, or NULL with the reason in pWhy.
template <class I>
I* ManagedAccept(void* pObject, const uint32_t* pSlotHashes, int32_t nSlots, char* pWhy, size_t nWhy)
{
	static_assert(std::is_base_of<IManagedObject, I>::value,
				  "a managed-built interface derives from IManagedObject (tools/managed-areas)");
	return static_cast<I*>(ManagedAcceptObject(pObject, pSlotHashes, nSlots, pWhy, nWhy));
}

// Nulls the holder, then releases what it held, through the base.
template <class I>
void ManagedRelease(I*& pHolder)
{
	static_assert(std::is_base_of<IManagedObject, I>::value,
				  "a managed-built interface derives from IManagedObject (tools/managed-areas)");
	IManagedObject* pObject = pHolder;
	pHolder = NULL;
	if (pObject)
		pObject->Release();
}
