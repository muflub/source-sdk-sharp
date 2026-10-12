//========= SourceSharp — overwriting one vtable slot in place ================//
//
// The render call recorder's machinery (shell_render_trace_client.cpp, plan
// _client_novalve C7): for one frame, a hooked method's slot in an engine
// object's vtable is overwritten with a hook, and put back after.
//
// IN PLACE, NOT A CLONED TABLE. vtable_hook.h's history says why the clone was
// retired; it also had a real flaw here. A clone has to copy the concrete
// class's WHOLE table, and an interface pointer says nothing about how many
// slots the class behind it added (IMatRenderContextInternal adds 34 to
// IMatRenderContext). Overwriting the one slot needs no length: every other
// slot, and every object of the class, keeps working through the same table.
// The page holding the slot is made writable for the one store and given back
// the protection it had (read from /proc/self/maps on Linux; ss_platform.h
// says how the other platforms answer), so a .data.rel.ro table is read-only
// again before the next instruction.
//
// The engine's own calls through a patched slot reach the hook too. The
// recorder filters by the caller; this file only moves pointers.
//
// Deliberately free of Source headers, so it is tested on its own against a
// class its test declares (test/vtable_slot_patch_test.cpp).
//
//=============================================================================//
#ifndef SOURCESHARP_VTABLE_SLOT_PATCH_H
#define SOURCESHARP_VTABLE_SLOT_PATCH_H
#ifdef _WIN32
#pragma once
#endif

#include "vtable_hook.h"
#include "ss_platform.h"

#include <stdint.h>
#include <stdio.h>

namespace VTableSlotPatch
{

// The protection (SS_PAGE_* bits, mprotect's values) of the mapping that
// holds pAddress, or -1 when no mapping does.
inline int PageProtection( const void *pAddress )
{
	return SS_PageProtection( pAddress );
}

// Stores pValue in *pSlot, lifting the page's protection for the store and
// putting nProtection back after. False when the page could not be made
// writable (nothing is stored then).
inline bool WriteSlot( void **pSlot, void *pValue, int nProtection )
{
	if ( !SS_PageProtect( pSlot, nProtection | SS_PAGE_READ | SS_PAGE_WRITE ) )
		return false;
	*pSlot = pValue;
	SS_PageProtect( pSlot, nProtection );
	return true;
}

// One overwritten slot, and what to put back.
struct Patch
{
	void **pSlot;
	void  *pOriginal;
	int    nProtection;
};

// Overwrites pObject's slot for pmf with pHook. On success fills *pPatch (its
// pOriginal is the function the hook forwards to) and returns true. Refuses a
// non-virtual member and one whose pointer carries a `this` adjustment (its
// slot is numbered against another table; vtable_hook.h says why).
template <typename PMF>
bool Install( void *pObject, PMF pmf, void *pHook, Patch *pPatch )
{
	if ( !pObject || !pPatch )
		return false;
	const VTableHook::Member member = VTableHook::MemberOf( pmf );
	if ( !member.isVirtual || member.adjust != 0 || member.slot < 0 )
		return false;
	void **pSlot = &( *(void ***)pObject )[ member.slot ];
	const int nProtection = PageProtection( pSlot );
	if ( nProtection < 0 )
		return false;
	void *pOriginal = *pSlot;
	if ( !WriteSlot( pSlot, pHook, nProtection ) )
		return false;
	pPatch->pSlot = pSlot;
	pPatch->pOriginal = pOriginal;
	pPatch->nProtection = nProtection;
	return true;
}

// Puts a patch's original back.
inline bool Remove( const Patch &patch )
{
	return WriteSlot( patch.pSlot, patch.pOriginal, patch.nProtection );
}

} // namespace VTableSlotPatch

#endif // SOURCESHARP_VTABLE_SLOT_PATCH_H
