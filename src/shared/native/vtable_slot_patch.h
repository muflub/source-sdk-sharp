// Overwrites one vtable slot in place for the render call recorder, and puts it back.
// In place because an interface pointer does not say how long the concrete class's
// table is. The page is made writable for the one store, then its protection restored.
// The engine's own calls through a patched slot reach the hook too.
#pragma once

#include "vtable_hook.h"
#include "ss_platform.h"

namespace VTableSlotPatch
{

// The protection (SS_PAGE_* bits, mprotect's values) of the mapping that
// holds pAddress, or -1 when no mapping does.
inline int PageProtection(const void* pAddress)
{
	return SS_PageProtection(pAddress);
}

// Stores pValue in *pSlot, lifting the page's protection for the store and
// putting nProtection back after. False when the page could not be made
// writable (nothing is stored then).
inline bool WriteSlot(void** pSlot, void* pValue, int nProtection)
{
	if (!SS_PageProtect(pSlot, nProtection | SS_PAGE_READ | SS_PAGE_WRITE))
		return false;
	*pSlot = pValue;
	SS_PageProtect(pSlot, nProtection);
	return true;
}

// One overwritten slot, and what to put back.
struct Patch
{
	void** pSlot;
	void* pOriginal;
	int nProtection;
};

// Overwrites pObject's slot for pmf with pHook; *pPatch's pOriginal is what the hook
// forwards to. Refuses non-virtual members and ones carrying a `this` adjustment.
template <typename PMF>
bool Install(void* pObject, PMF pmf, void* pHook, Patch* pPatch)
{
	if (!pObject || !pPatch)
		return false;
	const VTableHook::Member member = VTableHook::MemberOf(pmf);
	if (!member.isVirtual || member.adjust != 0 || member.slot < 0)
		return false;
	void** pSlot = &(*(void***)pObject)[member.slot];
	const int nProtection = PageProtection(pSlot);
	if (nProtection < 0)
		return false;
	void* pOriginal = *pSlot;
	if (!WriteSlot(pSlot, pHook, nProtection))
		return false;
	pPatch->pSlot = pSlot;
	pPatch->pOriginal = pOriginal;
	pPatch->nProtection = nProtection;
	return true;
}

// Puts a patch's original back.
inline bool Remove(const Patch& patch)
{
	return WriteSlot(patch.pSlot, patch.pOriginal, patch.nProtection);
}

} // namespace VTableSlotPatch
