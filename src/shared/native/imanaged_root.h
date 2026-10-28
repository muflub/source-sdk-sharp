//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the one managed object native is handed ==========//
//
// plan_engineapi_vtables.md §1.3 (step D4). The mirror of ishim_root.h: the
// server's managed side hands native exactly one object at load, this one,
// with the count managed built it with. Every other managed-built interface
// native calls is acquired FROM it by name, through Acquire -- the managed
// internal factory, which looks the name up in the side's container.
//
// IMPLEMENTED BY MANAGED (tools/vtable-layout's chain kind of that name):
// native owns only this declaration; ManagedVtableGenerator writes the table.
// So, by that kind's refusals: NO VIRTUAL DESTRUCTOR (lifetime is Release),
// every slot blittable, and Release() is the LAST slot and the count anchor --
// native accepts the object only if managed's count equals
// VTableHook::MemberOf( &IManagedRoot::Release ).slot + 1 (managed_accept.h).
//
//=============================================================================//

#pragma once

#include <stdint.h>

// THE CLASS IS GENERATED (plan_managed_vtable_reload.md L0c): tools/managed-areas
// writes IManagedRoot into imanaged_core.h from docs/managed-interfaces.tsv.
//   Acquire( pszName ) -- a managed-built interface by name; NULL for a name
//     this side does not serve. Native checks what it gets through its
//     Validate slot, against the compiled slot hashes, not by a count.
//   Release() -- frees the managed handle behind this object. Native nulls its
//     pointer first: a freed handle's slot is reused, so a late call would land
//     on a different live object.
#include "imanaged_core.h"

#include <stddef.h>

// THIS SIDE'S ROOT, held by the shim root (shell_internalfactory.cpp) so the
// DLL gains no static for it. Server only until the client's own wave.
//
// Accept takes LoadSideV2's answer and keeps it only at the compiler's count
// (managed_accept.h), answering the refusal otherwise. Release nulls the held
// pointer and THEN releases through it: a freed handle's slot is reused, so a
// late call through a kept pointer would land on a different live object.
// It releases every acquired area before the root.
IManagedRoot *ManagedRoot_Get();
bool ManagedRoot_Accept( void *pObject, char *pWhy, size_t nWhy );
// The managed-built interfaces this side's native code calls, acquired from
// the accepted root by name and each accepted at its compiler's count; false
// with the reason on the first refusal. Called on every load, after Accept.
bool ManagedRoot_AcquireAreas( char *pWhy, size_t nWhy );

// After a reload: the root LoadSideV2 handed back is the one native holds, and
// everything native holds validates through its patched block (reload L4).
bool ManagedRoot_Reattached( void *pObject, char *pWhy, size_t nWhy );

void ManagedRoot_Release();
