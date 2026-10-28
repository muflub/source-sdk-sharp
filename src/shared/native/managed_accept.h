//========= SourceSharp — accepting a managed-built object =================//
//
// plan_engineapi_vtables.md Phase D3, reshaped by plan_managed_vtable_reload.md
// L1. Native takes a managed-built object only if the object's own Validate
// (slot 1) agrees with the per-slot hashes the COMPILER's declaration was
// generated with (k<I>SlotHashes, tools/managed-areas). A table built from an
// older or newer header is refused here, naming the first slot that differs,
// rather than called through a slot whose shape changed. L1b: every such
// interface derives from IManagedObject, so the check is one function on the
// base; the templates only add the type and prove the derivation.
//
//=============================================================================//

#pragma once

#include <stdint.h>
#include <stdio.h>

#include <type_traits>

#include "imanaged_core.h"


// The object as an IManagedObject *, or NULL with the reason in pWhy.
inline IManagedObject *ManagedAcceptObject( void *pObject, const uint32_t *pSlotHashes, int32_t nSlots,
                                            char *pWhy, size_t nWhy )
{
	// plan_managed_vtable_reload.md L1: the object says whether it is the shape
	// this binary was compiled against. IManagedObject is every managed-built
	// interface's primary base, so its Validate is slot 1 of whatever table
	// managed built and calling it is safe on any object that is one at all;
	// it answers -1 when managed's per-slot hashes equal these, else the first
	// slot that differs (a count mismatch reports at the shorter count).
	if ( !pSlotHashes || nSlots <= 0 )
	{
		snprintf( pWhy, nWhy, "no compiled slot hashes to check against" );
		return NULL;
	}

	if ( !pObject )
	{
		snprintf( pWhy, nWhy, "managed returned no object" );
		return NULL;
	}

	IManagedObject *pManaged = static_cast< IManagedObject * >( pObject );
	const int32_t nDiffers = pManaged->Validate( pSlotHashes, nSlots );
	if ( nDiffers != -1 )
	{
		snprintf( pWhy, nWhy, "managed's slot %d differs from the compiler's declaration "
		          "(%d slot(s) compiled); native and managed were built from different headers",
		          (int)nDiffers, (int)nSlots );
		return NULL;
	}

	if ( nWhy )
		pWhy[ 0 ] = 0;
	return pManaged;
}

// The object as an I *, or NULL with the reason in pWhy.
template < class I >
I *ManagedAccept( void *pObject, const uint32_t *pSlotHashes, int32_t nSlots, char *pWhy, size_t nWhy )
{
	static_assert( std::is_base_of< IManagedObject, I >::value,
	               "a managed-built interface derives from IManagedObject (tools/managed-areas)" );
	return static_cast< I * >( ManagedAcceptObject( pObject, pSlotHashes, nSlots, pWhy, nWhy ) );
}

// Nulls the holder, then releases what it held, through the base.
template < class I >
void ManagedRelease( I *&pHolder )
{
	static_assert( std::is_base_of< IManagedObject, I >::value,
	               "a managed-built interface derives from IManagedObject (tools/managed-areas)" );
	IManagedObject *pObject = pHolder;
	pHolder = NULL;
	if ( pObject )
		pObject->Release();
}
